using AkiraCore.Core.Lighting;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Live;

/// <summary>
/// Um quadro do fluxo em tempo real: a cor de cada LED, pelo índice. LED não pintado fica apagado.
/// </summary>
public sealed class LiveFrame
{
    private readonly Rgb[] _colors = new Rgb[PerKeyProtocol.MaxLedIndex + 1];

    public Rgb this[int led]
    {
        get => _colors[led];
        set => _colors[led] = value;
    }

    public void Clear() => Array.Clear(_colors);

    public void Fill(Rgb color)
    {
        foreach (var key in KeyGeometry.All)
        {
            _colors[key.Led] = color;
        }
    }

    public void Scale(double factor)
    {
        for (var i = 0; i < _colors.Length; i++)
        {
            _colors[i] = ColorMath.Scale(_colors[i], factor);
        }
    }

    public LiveFrame Clone()
    {
        var copy = new LiveFrame();
        _colors.CopyTo(copy._colors, 0);
        return copy;
    }

    /// <summary>
    /// Quadro de 512 bytes do 04 20: [índice, R, G, B] em sequência, na ordem do desenho
    /// (como o app oficial da plataforma irmã manda), e zeros depois da última tecla.
    /// </summary>
    public byte[] ToStreamFrame(bool swapRedBlue = false)
    {
        var frame = new byte[StreamProtocol.FrameLength];
        var offset = 0;
        foreach (var key in KeyGeometry.All)
        {
            var color = swapRedBlue ? ColorMath.SwapRedBlue(_colors[key.Led]) : _colors[key.Led];
            frame[offset] = (byte)key.Led;
            frame[offset + 1] = color.R;
            frame[offset + 2] = color.G;
            frame[offset + 3] = color.B;
            offset += StreamProtocol.EntryLength;
        }
        return frame;
    }

    /// <summary>Cores para a prévia da interface: índice do LED para #RRGGBB.</summary>
    public IReadOnlyDictionary<int, string> ToHexMap() =>
        KeyGeometry.All.ToDictionary(k => k.Led, k => ColorMath.ToHex(_colors[k.Led]));
}
