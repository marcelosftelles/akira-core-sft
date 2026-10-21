using AkiraCore.Core.Lighting;

namespace AkiraCore.Core.Live;

/// <summary>Contas de cor dos efeitos calculados no PC. Funções puras.</summary>
public static class ColorMath
{
    // Brilho 0 a 5 como fator de intensidade. Curva perceptual: o olho nota mais a diferença
    // nas cores fracas, então os primeiros degraus sobem devagar.
    private static readonly double[] BrightnessFactors = [0.06, 0.2, 0.36, 0.55, 0.77, 1.0];

    public static double BrightnessFactor(int level) =>
        BrightnessFactors[Math.Clamp(level, 0, BrightnessFactors.Length - 1)];

    /// <summary>Cor pelo matiz (graus), saturação e valor de 0 a 1.</summary>
    public static Rgb Hsv(double hue, double saturation = 1, double value = 1)
    {
        hue = (hue % 360 + 360) % 360 / 60;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue % 2 - 1));
        var m = value - c;
        var (r, g, b) = (int)hue switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Rgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public static (double Hue, double Saturation, double Value) ToHsv(Rgb color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double hue = 0;
        if (delta > 0)
        {
            hue = max == r ? 60 * ((g - b) / delta % 6)
                : max == g ? 60 * ((b - r) / delta + 2)
                : 60 * ((r - g) / delta + 4);
        }
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }

    public static Rgb Scale(Rgb color, double factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return new Rgb(ToByte(color.R / 255d * factor), ToByte(color.G / 255d * factor), ToByte(color.B / 255d * factor));
    }

    public static Rgb Lerp(Rgb from, Rgb to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    /// <summary>Realça a saturação (cores de tela costumam sair lavadas nos LEDs).</summary>
    public static Rgb Vivid(Rgb color, double factor = 1.5)
    {
        var (hue, saturation, value) = ToHsv(color);
        return Hsv(hue, Math.Min(1, saturation * factor), value);
    }

    public static Rgb SwapRedBlue(Rgb color) => new(color.B, color.G, color.R);

    public static string ToHex(Rgb color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static byte ToByte(double unit) => (byte)Math.Round(Math.Clamp(unit, 0, 1) * 255);
}
