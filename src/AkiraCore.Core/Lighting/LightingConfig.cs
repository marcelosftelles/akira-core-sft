using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Lighting;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6)
        {
            throw new FormatException("Use 6 dígitos hexadecimais, ex: ff0000");
        }
        return new Rgb(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..], 16));
    }
}

/// <summary>
/// Espelha uma linha de t_light_data. Validação no construtor: o firmware
/// nunca recebe valor fora da faixa.
/// </summary>
public sealed record LightingConfig
{
    public Effect Effect { get; }
    public Rgb Color { get; }
    public int Brightness { get; }
    public int Speed { get; }
    public int Direction { get; }
    public bool Rainbow { get; }

    /// <summary>Inverte R e B caso a suposição de ordem COLORREF esteja errada.</summary>
    public bool SwapRedBlue { get; init; }

    public LightingConfig(Effect effect, Rgb color, int brightness = ProtocolConstants.BrightnessMax,
        int speed = 3, int direction = 0, bool rainbow = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(brightness, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(brightness, ProtocolConstants.BrightnessMax);
        ArgumentOutOfRangeException.ThrowIfLessThan(speed, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(speed, ProtocolConstants.SpeedMax);
        ArgumentOutOfRangeException.ThrowIfLessThan(direction, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(direction, 255);

        Effect = effect;
        Color = color;
        Brightness = brightness;
        Speed = speed;
        Direction = direction;
        Rainbow = rainbow;
    }

    /// <summary>Bloco de 16 bytes, idêntico nos canais com fio e 2.4G.</summary>
    public byte[] ToLightBlock()
    {
        // Com modo 0 o app oficial manda só o byte de modo (fcn.0042afd0)
        if (Effect.Mode == 0)
        {
            return PacketBuilder.LightBlock(new Dictionary<int, byte> { [0] = 0 });
        }

        var (r, b) = SwapRedBlue ? (Color.B, Color.R) : (Color.R, Color.B);
        return PacketBuilder.LightBlock(new Dictionary<int, byte>
        {
            [0] = Effect.Mode,
            [1] = r,             // ordem COLORREF (0x00BBGGRR) assumida
            [2] = Color.G,
            [3] = b,
            [8] = (byte)(Rainbow ? 1 : 0),
            [ProtocolConstants.BrightnessIndex] = (byte)Brightness,
            [10] = (byte)Speed,
            [11] = (byte)Direction,
        });
    }
}
