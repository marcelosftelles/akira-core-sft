namespace AkiraCore.Core.Live;

/// <summary>Posição de uma tecla no desenho oficial (espaço 800 × 300 do rgb-keyboard.xml).</summary>
public sealed record KeyShape(int Led, string Name, double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>
/// Geometria das 82 teclas, a mesma do Ui/layout.js (rect_left, rect_top, rect_width, rect_height e
/// light_index do layouts/rgb-keyboard.xml oficial). Os efeitos calculados no PC (tempo real,
/// música, tela) precisam saber onde cada tecla fica; um teste confere que as duas cópias batem.
/// </summary>
public static class KeyGeometry
{
    public const int Rows = 6;

    public static IReadOnlyList<KeyShape> All { get; } =
    [
        new(1, "Esc", 81, 23, 30, 30),
        new(2, "F1", 130, 23, 30, 30),
        new(3, "F2", 169, 23, 30, 30),
        new(4, "F3", 208, 23, 30, 30),
        new(5, "F4", 247, 23, 30, 30),
        new(6, "F5", 296, 23, 30, 30),
        new(7, "F6", 336, 23, 30, 30),
        new(8, "F7", 376, 23, 30, 30),
        new(9, "F8", 415, 23, 30, 30),
        new(10, "F9", 464, 23, 30, 30),
        new(11, "F10", 504, 23, 30, 30),
        new(12, "F11", 544, 23, 30, 30),
        new(13, "F12", 584, 23, 30, 30),
        new(119, "Del", 633, 23, 30, 30),
        new(19, "~`", 81, 76, 30, 30),
        new(20, "!1", 118, 76, 30, 30),
        new(21, "@2", 157, 76, 30, 30),
        new(22, "#3", 197, 76, 30, 30),
        new(23, "$4", 237, 76, 30, 30),
        new(24, "%5", 277, 76, 30, 30),
        new(25, "^6", 317, 76, 30, 30),
        new(26, "&7", 356, 76, 30, 30),
        new(27, "*8", 396, 76, 30, 30),
        new(28, "(9", 436, 76, 30, 30),
        new(29, ")0", 475, 76, 30, 30),
        new(30, "_-", 515, 76, 30, 30),
        new(31, "+=", 554, 76, 30, 30),
        new(103, "Backspace", 594, 76, 69, 30),
        new(117, "Home", 690, 76, 30, 30),
        new(37, "Tab", 81, 116, 48, 30),
        new(38, "Q", 139, 116, 30, 30),
        new(39, "W", 179, 116, 30, 30),
        new(40, "E", 218, 116, 30, 30),
        new(41, "R", 258, 116, 30, 30),
        new(42, "T", 297, 116, 30, 30),
        new(43, "Y", 336, 116, 30, 30),
        new(44, "U", 376, 116, 30, 30),
        new(45, "I", 415, 116, 30, 30),
        new(46, "O", 455, 116, 30, 30),
        new(47, "P", 495, 116, 30, 30),
        new(48, "{[", 534, 116, 30, 30),
        new(49, "}}", 574, 116, 30, 30),
        new(67, "|\\", 614, 116, 49, 30),
        new(120, "end", 690, 116, 30, 30),
        new(55, "CapsLock", 81, 156, 58, 30),
        new(56, "A", 149, 156, 30, 30),
        new(57, "S", 188, 156, 30, 30),
        new(58, "D", 228, 156, 30, 30),
        new(59, "F", 267, 156, 30, 30),
        new(60, "G", 307, 156, 30, 30),
        new(61, "H", 346, 156, 30, 30),
        new(62, "J", 386, 156, 30, 30),
        new(63, "K", 425, 156, 30, 30),
        new(64, "L", 465, 156, 30, 30),
        new(65, ":;", 505, 156, 30, 30),
        new(66, "\"'", 544, 156, 30, 30),
        new(85, "Enter", 584, 156, 79, 30),
        new(118, "PgUp", 690, 156, 30, 30),
        new(73, "Shift_L", 81, 194, 79, 30),
        new(74, "Z", 168, 194, 30, 30),
        new(75, "X", 208, 194, 30, 30),
        new(76, "C", 247, 194, 30, 30),
        new(77, "V", 287, 194, 30, 30),
        new(78, "B", 326, 194, 30, 30),
        new(79, "N", 366, 194, 30, 30),
        new(80, "M", 405, 194, 30, 30),
        new(81, ",<", 445, 194, 30, 30),
        new(82, ".>", 485, 194, 30, 30),
        new(83, "/?", 524, 194, 30, 30),
        new(84, "Shift_R", 564, 194, 58, 30),
        new(101, "↑", 641, 202, 30, 30),
        new(121, "PgDn", 690, 194, 30, 30),
        new(91, "Ctrl_L", 81, 233, 40, 30),
        new(92, "Win_L", 129, 233, 40, 30),
        new(93, "Alt_L", 179, 233, 40, 30),
        new(94, "Space", 230, 233, 233, 30),
        new(95, "Alt_R", 474, 233, 30, 30),
        new(96, "Fn", 514, 233, 30, 30),
        new(98, "Ctrl_R", 553, 233, 30, 30),
        new(99, "←", 602, 240, 30, 30),
        new(100, "↓", 641, 240, 30, 30),
        new(102, "→", 680, 240, 30, 30),
    ];

    private static readonly double MinX = All.Min(k => k.CenterX);
    private static readonly double MaxX = All.Max(k => k.CenterX);
    private static readonly double MinY = All.Min(k => k.CenterY);
    private static readonly double MaxY = All.Max(k => k.CenterY);

    // Topo de cada fileira no desenho: F, números, Q, A, Z (a seta para cima desce 8) e espaço
    private static readonly double[] RowTops = [0, 50, 100, 136, 176, 214];

    /// <summary>Centro da tecla de 0 (esquerda) a 1 (direita).</summary>
    public static double NormalizedX(KeyShape key) => (key.CenterX - MinX) / (MaxX - MinX);

    /// <summary>Centro da tecla de 0 (topo) a 1 (base).</summary>
    public static double NormalizedY(KeyShape key) => (key.CenterY - MinY) / (MaxY - MinY);

    /// <summary>Fileira de 0 (F1 a F12) a 5 (espaço).</summary>
    public static int RowOf(KeyShape key)
    {
        for (var row = Rows - 1; row > 0; row--)
        {
            if (key.Y >= RowTops[row])
            {
                return row;
            }
        }
        return 0;
    }

    /// <summary>Teclas de uma fileira, da esquerda para a direita.</summary>
    public static IReadOnlyList<KeyShape> Row(int row) =>
        All.Where(k => RowOf(k) == row).OrderBy(k => k.X).ToList();
}
