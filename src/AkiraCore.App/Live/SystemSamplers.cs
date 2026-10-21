using AkiraCore.Core.Lighting;
using static AkiraCore.App.Platform.NativeSystem;

namespace AkiraCore.App.Live;

/// <summary>
/// Reduz a tela principal a uma grade pequena de cores (StretchBlt em meio-tom, que já tira a
/// média de cada bloco). Nada é salvo: a grade vai para o efeito "Cor da tela" e é descartada.
/// Conteúdo protegido (vídeo com DRM) aparece preto, como em qualquer captura.
/// </summary>
public sealed class ScreenSampler
{
    public const int Width = 24;
    public const int Height = 8;

    public Rgb[]? Sample()
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return null;
        }
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, Width, Height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            SetStretchBltMode(memory, HalfTone);
            var sourceWidth = GetSystemMetrics(SmCxScreen);
            var sourceHeight = GetSystemMetrics(SmCyScreen);
            if (!StretchBlt(memory, 0, 0, Width, Height, screen, 0, 0, sourceWidth, sourceHeight, SrcCopy))
            {
                return null;
            }
            SelectObject(memory, previous);
            var header = new BitmapInfoHeader
            {
                Size = 40,
                Width = Width,
                Height = -Height, // negativo: linhas de cima para baixo
                Planes = 1,
                BitCount = 32,
            };
            var bits = new byte[Width * Height * 4];
            if (GetDIBits(memory, bitmap, 0, Height, bits, ref header, 0) == 0)
            {
                return null;
            }
            var grid = new Rgb[Width * Height];
            for (var i = 0; i < grid.Length; i++)
            {
                grid[i] = new Rgb(bits[i * 4 + 2], bits[i * 4 + 1], bits[i * 4]); // BGRA
            }
            return grid;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

/// <summary>Uso total da CPU entre duas leituras (GetSystemTimes), de 0 a 1.</summary>
public sealed class CpuSampler
{
    private long _idle;
    private long _total;

    public double? Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }
        // O tempo de kernel já inclui o ocioso
        var total = kernel + user;
        var deltaTotal = total - _total;
        var deltaIdle = idle - _idle;
        var first = _total == 0;
        (_idle, _total) = (idle, total);
        return first || deltaTotal <= 0 ? null : Math.Clamp(1 - (double)deltaIdle / deltaTotal, 0, 1);
    }
}
