namespace AkiraCore.Core.Diagnostics;

/// <summary>Hexadecimal legível para o log: bytes separados e cauda de zeros resumida.</summary>
public static class HexText
{
    private const int MinZerosToCompact = 4;

    /// <summary>"05 A6 01 … (+29 × 00)". Report de 65 bytes quase vazio cabe numa linha.</summary>
    public static string Spaced(ReadOnlySpan<byte> data, bool compactTrailingZeros = true)
    {
        var end = data.Length;
        if (compactTrailingZeros)
        {
            var lastNonZero = data.LastIndexOfAnyExcept((byte)0);
            var trailingZeros = data.Length - (lastNonZero + 1);
            if (trailingZeros >= MinZerosToCompact)
            {
                end = lastNonZero + 1;
            }
        }

        var text = Convert.ToHexString(data[..end]);
        var spaced = string.Join(' ', Enumerable.Range(0, end).Select(i => text.Substring(i * 2, 2)));
        if (end == data.Length)
        {
            return spaced;
        }
        var suffix = $"(+{data.Length - end} × 00)";
        return end == 0 ? suffix : $"{spaced} … {suffix}";
    }
}
