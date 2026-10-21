namespace AkiraCore.Core.Keys;

/// <summary>
/// Scan code do conjunto 1 (o que o Windows usa; 0xE0xx = prefixo E0) para o código de uso HID
/// (página 0x07). Serve para digitar texto em macro: o Windows diz em que tecla está cada
/// caractere no layout atual e o teclado precisa do código HID dela.
/// </summary>
public static class ScanCodes
{
    private static readonly Dictionary<int, byte> Map = Build();

    public static byte? ToUsage(int scanCode) => Map.TryGetValue(scanCode, out var usage) ? usage : null;

    public static IReadOnlyDictionary<int, byte> All => Map;

    private static Dictionary<int, byte> Build()
    {
        var map = new Dictionary<int, byte>
        {
            [0x01] = 0x29, // Esc
            [0x0C] = 0x2D, [0x0D] = 0x2E, [0x0E] = 0x2A, [0x0F] = 0x2B,
            [0x1A] = 0x2F, [0x1B] = 0x30, [0x1C] = 0x28, [0x1D] = 0xE0,
            [0x27] = 0x33, [0x28] = 0x34, [0x29] = 0x35, [0x2A] = 0xE1, [0x2B] = 0x31,
            [0x33] = 0x36, [0x34] = 0x37, [0x35] = 0x38, [0x36] = 0xE5,
            [0x37] = 0x55, [0x38] = 0xE2, [0x39] = 0x2C, [0x3A] = 0x39,
            [0x45] = 0x53, [0x46] = 0x47, [0x4A] = 0x56, [0x4E] = 0x57,
            [0x47] = 0x5F, [0x48] = 0x60, [0x49] = 0x61,
            [0x4B] = 0x5C, [0x4C] = 0x5D, [0x4D] = 0x5E,
            [0x4F] = 0x59, [0x50] = 0x5A, [0x51] = 0x5B,
            [0x52] = 0x62, [0x53] = 0x63,
            [0x56] = 0x64, // \| do ABNT2, ao lado do Shift esquerdo
            [0x57] = 0x44, [0x58] = 0x45, // F11, F12
            [0x73] = 0x87, // /? do ABNT2, ao lado do Shift direito
            [0xE01C] = 0x58, [0xE01D] = 0xE4, [0xE035] = 0x54, [0xE038] = 0xE6,
            [0xE047] = 0x4A, [0xE048] = 0x52, [0xE049] = 0x4B, [0xE04B] = 0x50,
            [0xE04D] = 0x4F, [0xE04F] = 0x4D, [0xE050] = 0x51, [0xE051] = 0x4E,
            [0xE052] = 0x49, [0xE053] = 0x4C, [0xE05B] = 0xE3, [0xE05C] = 0xE7, [0xE05D] = 0x65,
        };

        // Números 1..0 (0x02..0x0B) e F1..F10 (0x3B..0x44) são sequenciais nas duas tabelas
        for (var i = 0; i < 10; i++)
        {
            map[0x02 + i] = (byte)(0x1E + i);
            map[0x3B + i] = (byte)(0x3A + i);
        }

        // Letras seguem a posição física (QWERTY), não a ordem alfabética
        const string rows = "QWERTYUIOP" + "ASDFGHJKL" + "ZXCVBNM";
        int[] starts = [0x10, 0x1E, 0x2C];
        int[] lengths = [10, 9, 7];
        var offset = 0;
        for (var row = 0; row < 3; row++)
        {
            for (var i = 0; i < lengths[row]; i++)
            {
                map[starts[row] + i] = (byte)(0x04 + (rows[offset + i] - 'A'));
            }
            offset += lengths[row];
        }
        return map;
    }
}
