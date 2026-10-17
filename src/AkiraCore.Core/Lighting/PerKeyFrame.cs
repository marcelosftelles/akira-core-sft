using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Lighting;

/// <summary>
/// Um "quadro" de cores por índice de LED. LEDs não definidos ficam apagados,
/// como no app oficial (buffer zerado).
/// </summary>
public sealed class PerKeyFrame
{
    private readonly Dictionary<int, Rgb> _colors = [];

    public IReadOnlyDictionary<int, Rgb> Colors => _colors;

    public PerKeyFrame Set(int ledIndex, Rgb color)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ledIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ledIndex, PerKeyProtocol.MaxLedIndex);
        _colors[ledIndex] = color;
        return this;
    }

    public byte[] ToBuffer()
    {
        var buffer = new byte[PerKeyProtocol.BufferLength];
        foreach (var (index, color) in _colors)
        {
            var offset = index * 4;
            buffer[offset] = (byte)index;
            buffer[offset + 1] = color.R;
            buffer[offset + 2] = color.G;
            buffer[offset + 3] = color.B;
        }
        buffer[PerKeyProtocol.SignatureOffset] = ProtocolConstants.SignatureLow;
        buffer[PerKeyProtocol.SignatureOffset + 1] = ProtocolConstants.SignatureHigh;
        return buffer;
    }
}

/// <summary>
/// Índices de LED que o layout oficial não associa a nenhuma tecla.
/// A matriz tem 6 linhas de 18 posições; as lacunas são candidatas às
/// fitas dos cantos e à mini fita.
/// </summary>
public static class LedMap
{
    public static IReadOnlyList<int> UnmappedIndices { get; } =
    [
        14, 15, 16, 17, 18,
        32, 33, 34, 35, 36,
        50, 51, 52, 53, 54,
        68, 69, 70, 71, 72,
        86, 87, 88, 89, 90,
        97,
        104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116,
        .. Enumerable.Range(122, PerKeyProtocol.MaxLedIndex - 122 + 1),
    ];
}
