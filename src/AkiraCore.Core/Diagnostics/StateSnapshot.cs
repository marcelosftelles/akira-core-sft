namespace AkiraCore.Core.Diagnostics;

/// <summary>
/// "Foto" do que o teclado devolve: cada fonte (uma resposta, um GET_REPORT)
/// com seus bytes, na ordem em que foi lida. Duas fotos, uma antes e outra
/// depois de um atalho, dizem se o firmware expõe o estado que o atalho mudou.
/// </summary>
public sealed class StateSnapshot
{
    private readonly List<KeyValuePair<string, byte[]>> _sources = [];

    public DateTime TakenAt { get; } = DateTime.Now;

    public IReadOnlyList<KeyValuePair<string, byte[]>> Sources => _sources;

    public StateSnapshot Add(string source, byte[] data)
    {
        if (Find(source) is not null)
        {
            throw new ArgumentException($"Fonte repetida na foto: {source}", nameof(source));
        }
        // Cópia: o chamador pode reaproveitar o buffer
        _sources.Add(new(source, (byte[])data.Clone()));
        return this;
    }

    public byte[]? Find(string source) =>
        _sources.FirstOrDefault(s => s.Key == source).Value;
}

/// <summary>Um byte que mudou entre duas fotos. Null = o byte não existia naquela foto.</summary>
public sealed record ByteChange(string Source, int Offset, byte? Before, byte? After);

public sealed record StateDiffResult(
    IReadOnlyList<ByteChange> Changes,
    IReadOnlyList<string> OnlyBefore,
    IReadOnlyList<string> OnlyAfter)
{
    public bool IsEmpty => Changes.Count == 0 && OnlyBefore.Count == 0 && OnlyAfter.Count == 0;
}

/// <summary>Compara duas fotos byte a byte, como um diff de regressão visual.</summary>
public static class StateDiff
{
    public static StateDiffResult Compare(StateSnapshot before, StateSnapshot after)
    {
        var changes = new List<ByteChange>();
        var onlyBefore = new List<string>();

        foreach (var (source, oldData) in before.Sources)
        {
            var newData = after.Find(source);
            if (newData is null)
            {
                onlyBefore.Add(source);
                continue;
            }
            for (var i = 0; i < Math.Max(oldData.Length, newData.Length); i++)
            {
                byte? oldByte = i < oldData.Length ? oldData[i] : null;
                byte? newByte = i < newData.Length ? newData[i] : null;
                if (oldByte != newByte)
                {
                    changes.Add(new ByteChange(source, i, oldByte, newByte));
                }
            }
        }

        var onlyAfter = after.Sources
            .Select(s => s.Key)
            .Where(source => before.Find(source) is null)
            .ToList();

        return new StateDiffResult(changes, onlyBefore, onlyAfter);
    }

    /// <summary>Texto para o log do Laboratório, uma linha por fonte.</summary>
    public static IReadOnlyList<string> Describe(StateDiffResult diff)
    {
        if (diff.IsEmpty)
        {
            return ["Nenhuma diferença: nada do que o teclado devolve mudou entre as fotos."];
        }

        var lines = diff.Changes
            .GroupBy(c => c.Source)
            .Select(g => $"{g.Key}: " + string.Join(", ",
                g.Select(c => $"[{c.Offset}] {Hex(c.Before)} → {Hex(c.After)}")))
            .ToList();
        lines.AddRange(diff.OnlyBefore.Select(s => $"{s}: só na foto anterior"));
        lines.AddRange(diff.OnlyAfter.Select(s => $"{s}: só na foto nova"));
        return lines;
    }

    private static string Hex(byte? value) => value is { } v ? v.ToString("X2") : "--";
}
