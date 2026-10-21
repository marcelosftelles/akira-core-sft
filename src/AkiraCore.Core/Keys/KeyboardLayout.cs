namespace AkiraCore.Core.Keys;

/// <summary>Uma tecla física do AKIRA CORE: posição na tabela (key_index) e o código que ela manda.</summary>
public sealed record PhysicalKey(int Index, byte Usage, string Label)
{
    /// <summary>
    /// O Fn não entra no remapeamento: é ele que abre a camada Fn e os atalhos das fitas.
    /// Perder o Fn tiraria o caminho de volta pelo próprio teclado.
    /// </summary>
    public bool Remappable => Usage != KeyboardLayout.FnUsage;
}

/// <summary>
/// As 82 teclas do layouts/rgb-keyboard.xml oficial (atributos key_index e code).
/// Neste modelo key_index é igual a light_index, o índice das cores por LED.
/// </summary>
public static class KeyboardLayout
{
    public const byte FnUsage = 0xAF; // código que o XML oficial dá ao Fn; não é uma tecla HID padrão

    public static IReadOnlyList<PhysicalKey> All { get; } =
    [
        Key(1, 0x29), Key(2, 0x3A), Key(3, 0x3B), Key(4, 0x3C), Key(5, 0x3D), Key(6, 0x3E),
        Key(7, 0x3F), Key(8, 0x40), Key(9, 0x41), Key(10, 0x42), Key(11, 0x43), Key(12, 0x44),
        Key(13, 0x45), Key(119, 0x4C),
        Key(19, 0x35), Key(20, 0x1E), Key(21, 0x1F), Key(22, 0x20), Key(23, 0x21), Key(24, 0x22),
        Key(25, 0x23), Key(26, 0x24), Key(27, 0x25), Key(28, 0x26), Key(29, 0x27), Key(30, 0x2D),
        Key(31, 0x2E), Key(103, 0x2A), Key(117, 0x4A),
        Key(37, 0x2B), Key(38, 0x14), Key(39, 0x1A), Key(40, 0x08), Key(41, 0x15), Key(42, 0x17),
        Key(43, 0x1C), Key(44, 0x18), Key(45, 0x0C), Key(46, 0x12), Key(47, 0x13), Key(48, 0x2F),
        Key(49, 0x30), Key(67, 0x31), Key(120, 0x4D),
        Key(55, 0x39), Key(56, 0x04), Key(57, 0x16), Key(58, 0x07), Key(59, 0x09), Key(60, 0x0A),
        Key(61, 0x0B), Key(62, 0x0D), Key(63, 0x0E), Key(64, 0x0F), Key(65, 0x33), Key(66, 0x34),
        Key(85, 0x28), Key(118, 0x4B),
        Key(73, 0xE1), Key(74, 0x1D), Key(75, 0x1B), Key(76, 0x06), Key(77, 0x19), Key(78, 0x05),
        Key(79, 0x11), Key(80, 0x10), Key(81, 0x36), Key(82, 0x37), Key(83, 0x38), Key(84, 0xE5),
        Key(101, 0x52), Key(121, 0x4E),
        Key(91, 0xE0), Key(92, 0xE3), Key(93, 0xE2), Key(94, 0x2C), Key(95, 0xE6),
        new(96, FnUsage, "Fn"),
        Key(98, 0xE4), Key(99, 0x50), Key(100, 0x51), Key(102, 0x4F),
    ];

    private static readonly Dictionary<int, PhysicalKey> ByIndexMap = All.ToDictionary(k => k.Index);
    private static readonly Dictionary<byte, PhysicalKey> ByUsageMap = All.ToDictionary(k => k.Usage);

    public static PhysicalKey? ByIndex(int index) => ByIndexMap.GetValueOrDefault(index);

    /// <summary>
    /// LED da tecla do AKIRA que o Raw Input entregou (scan code do conjunto 1, 0xE0xx com prefixo).
    /// Usado pelas teclas "acender ao tocar" da luz em tempo real.
    /// </summary>
    public static int? LedOfScanCode(int scanCode) =>
        ScanCodes.ToUsage(scanCode) is { } usage && ByUsageMap.TryGetValue(usage, out var key) ? key.Index : null;

    private static PhysicalKey Key(int index, byte usage) =>
        new(index, usage, HidKeys.ByUsage(usage)?.Label ?? throw new InvalidOperationException($"Código 0x{usage:X2} fora do catálogo."));
}
