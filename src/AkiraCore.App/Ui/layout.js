// Geometria do AKIRA CORE no espaço 800 × 300 do layout oficial.
// Teclas: code (uso HID), rect_left, rect_top, rect_width, rect_height e light_index copiados do
// layouts/rgb-keyboard.xml do instalador oficial (AKIRA_CORE_Driver 1.0.0.0).
// Corpo, knob e loading bar: medidos na imagem img_keyboard_layout.png do mesmo pacote.

export const VIEWBOX = { x: 52, y: 0, width: 696, height: 300 };

export const BODY = { x: 60, y: 4, width: 680, height: 290, radius: 16 };

export const KNOB = { cx: 697, cy: 38, r: 17 };

// Zonas que só o teclado controla (atalhos Fn). As fitas laterais ficam nas bordas
// esquerda e direita do corpo, da fileira dos números até a de baixo (posição
// indicada pelo usuário no teclado real, 20/10/2026).
export const ZONES = {
  loadingBar: { x: 671, y: 78, width: 4, height: 30 },
  leftStrip: { x: 58, y: 90, width: 4, height: 146 },
  rightStrip: { x: 738, y: 90, width: 4, height: 146 },
};

export const KEYS = [
  { name: "Esc", code: 0x29, led: 1, x: 81, y: 23, w: 30, h: 30 },
  { name: "F1", code: 0x3a, led: 2, x: 130, y: 23, w: 30, h: 30 },
  { name: "F2", code: 0x3b, led: 3, x: 169, y: 23, w: 30, h: 30 },
  { name: "F3", code: 0x3c, led: 4, x: 208, y: 23, w: 30, h: 30 },
  { name: "F4", code: 0x3d, led: 5, x: 247, y: 23, w: 30, h: 30 },
  { name: "F5", code: 0x3e, led: 6, x: 296, y: 23, w: 30, h: 30 },
  { name: "F6", code: 0x3f, led: 7, x: 336, y: 23, w: 30, h: 30 },
  { name: "F7", code: 0x40, led: 8, x: 376, y: 23, w: 30, h: 30 },
  { name: "F8", code: 0x41, led: 9, x: 415, y: 23, w: 30, h: 30 },
  { name: "F9", code: 0x42, led: 10, x: 464, y: 23, w: 30, h: 30 },
  { name: "F10", code: 0x43, led: 11, x: 504, y: 23, w: 30, h: 30 },
  { name: "F11", code: 0x44, led: 12, x: 544, y: 23, w: 30, h: 30 },
  { name: "F12", code: 0x45, led: 13, x: 584, y: 23, w: 30, h: 30 },
  { name: "Del", code: 0x4c, led: 119, x: 633, y: 23, w: 30, h: 30 },
  { name: "~`", code: 0x35, led: 19, x: 81, y: 76, w: 30, h: 30 },
  { name: "!1", code: 0x1e, led: 20, x: 118, y: 76, w: 30, h: 30 },
  { name: "@2", code: 0x1f, led: 21, x: 157, y: 76, w: 30, h: 30 },
  { name: "#3", code: 0x20, led: 22, x: 197, y: 76, w: 30, h: 30 },
  { name: "$4", code: 0x21, led: 23, x: 237, y: 76, w: 30, h: 30 },
  { name: "%5", code: 0x22, led: 24, x: 277, y: 76, w: 30, h: 30 },
  { name: "^6", code: 0x23, led: 25, x: 317, y: 76, w: 30, h: 30 },
  { name: "&7", code: 0x24, led: 26, x: 356, y: 76, w: 30, h: 30 },
  { name: "*8", code: 0x25, led: 27, x: 396, y: 76, w: 30, h: 30 },
  { name: "(9", code: 0x26, led: 28, x: 436, y: 76, w: 30, h: 30 },
  { name: ")0", code: 0x27, led: 29, x: 475, y: 76, w: 30, h: 30 },
  { name: "_-", code: 0x2d, led: 30, x: 515, y: 76, w: 30, h: 30 },
  { name: "+=", code: 0x2e, led: 31, x: 554, y: 76, w: 30, h: 30 },
  { name: "Backspace", code: 0x2a, led: 103, x: 594, y: 76, w: 69, h: 30 },
  { name: "Home", code: 0x4a, led: 117, x: 690, y: 76, w: 30, h: 30 },
  { name: "Tab", code: 0x2b, led: 37, x: 81, y: 116, w: 48, h: 30 },
  { name: "Q", code: 0x14, led: 38, x: 139, y: 116, w: 30, h: 30 },
  { name: "W", code: 0x1a, led: 39, x: 179, y: 116, w: 30, h: 30 },
  { name: "E", code: 0x8, led: 40, x: 218, y: 116, w: 30, h: 30 },
  { name: "R", code: 0x15, led: 41, x: 258, y: 116, w: 30, h: 30 },
  { name: "T", code: 0x17, led: 42, x: 297, y: 116, w: 30, h: 30 },
  { name: "Y", code: 0x1c, led: 43, x: 336, y: 116, w: 30, h: 30 },
  { name: "U", code: 0x18, led: 44, x: 376, y: 116, w: 30, h: 30 },
  { name: "I", code: 0xc, led: 45, x: 415, y: 116, w: 30, h: 30 },
  { name: "O", code: 0x12, led: 46, x: 455, y: 116, w: 30, h: 30 },
  { name: "P", code: 0x13, led: 47, x: 495, y: 116, w: 30, h: 30 },
  { name: "{[", code: 0x2f, led: 48, x: 534, y: 116, w: 30, h: 30 },
  { name: "}}", code: 0x30, led: 49, x: 574, y: 116, w: 30, h: 30 },
  { name: "|\\", code: 0x31, led: 67, x: 614, y: 116, w: 49, h: 30 },
  { name: "end", code: 0x4d, led: 120, x: 690, y: 116, w: 30, h: 30 },
  { name: "CapsLock", code: 0x39, led: 55, x: 81, y: 156, w: 58, h: 30 },
  { name: "A", code: 0x4, led: 56, x: 149, y: 156, w: 30, h: 30 },
  { name: "S", code: 0x16, led: 57, x: 188, y: 156, w: 30, h: 30 },
  { name: "D", code: 0x7, led: 58, x: 228, y: 156, w: 30, h: 30 },
  { name: "F", code: 0x9, led: 59, x: 267, y: 156, w: 30, h: 30 },
  { name: "G", code: 0xa, led: 60, x: 307, y: 156, w: 30, h: 30 },
  { name: "H", code: 0xb, led: 61, x: 346, y: 156, w: 30, h: 30 },
  { name: "J", code: 0xd, led: 62, x: 386, y: 156, w: 30, h: 30 },
  { name: "K", code: 0xe, led: 63, x: 425, y: 156, w: 30, h: 30 },
  { name: "L", code: 0xf, led: 64, x: 465, y: 156, w: 30, h: 30 },
  { name: ":;", code: 0x33, led: 65, x: 505, y: 156, w: 30, h: 30 },
  { name: "\"'", code: 0x34, led: 66, x: 544, y: 156, w: 30, h: 30 },
  { name: "Enter", code: 0x28, led: 85, x: 584, y: 156, w: 79, h: 30 },
  { name: "PgUp", code: 0x4b, led: 118, x: 690, y: 156, w: 30, h: 30 },
  { name: "Shift_L", code: 0xe1, led: 73, x: 81, y: 194, w: 79, h: 30 },
  { name: "Z", code: 0x1d, led: 74, x: 168, y: 194, w: 30, h: 30 },
  { name: "X", code: 0x1b, led: 75, x: 208, y: 194, w: 30, h: 30 },
  { name: "C", code: 0x6, led: 76, x: 247, y: 194, w: 30, h: 30 },
  { name: "V", code: 0x19, led: 77, x: 287, y: 194, w: 30, h: 30 },
  { name: "B", code: 0x5, led: 78, x: 326, y: 194, w: 30, h: 30 },
  { name: "N", code: 0x11, led: 79, x: 366, y: 194, w: 30, h: 30 },
  { name: "M", code: 0x10, led: 80, x: 405, y: 194, w: 30, h: 30 },
  { name: ",<", code: 0x36, led: 81, x: 445, y: 194, w: 30, h: 30 },
  { name: ".>", code: 0x37, led: 82, x: 485, y: 194, w: 30, h: 30 },
  { name: "/?", code: 0x38, led: 83, x: 524, y: 194, w: 30, h: 30 },
  { name: "Shift_R", code: 0xe5, led: 84, x: 564, y: 194, w: 58, h: 30 },
  { name: "↑", code: 0x52, led: 101, x: 641, y: 202, w: 30, h: 30 },
  { name: "PgDn", code: 0x4e, led: 121, x: 690, y: 194, w: 30, h: 30 },
  { name: "Ctrl_L", code: 0xe0, led: 91, x: 81, y: 233, w: 40, h: 30 },
  { name: "Win_L", code: 0xe3, led: 92, x: 129, y: 233, w: 40, h: 30 },
  { name: "Alt_L", code: 0xe2, led: 93, x: 179, y: 233, w: 40, h: 30 },
  { name: "Space", code: 0x2c, led: 94, x: 230, y: 233, w: 233, h: 30 },
  { name: "Alt_R", code: 0xe6, led: 95, x: 474, y: 233, w: 30, h: 30 },
  { name: "Fn", code: 0xAF, led: 96, x: 514, y: 233, w: 30, h: 30 },
  { name: "Ctrl_R", code: 0xE4, led: 98, x: 553, y: 233, w: 30, h: 30 },
  { name: "←", code: 0x50, led: 99, x: 602, y: 240, w: 30, h: 30 },
  { name: "↓", code: 0x51, led: 100, x: 641, y: 240, w: 30, h: 30 },
  { name: "→", code: 0x4f, led: 102, x: 680, y: 240, w: 30, h: 30 },
];



// Scan code (conjunto 1, como o Raw Input entrega; 0xE0xx = prefixo E0) de cada tecla
// acima, pelo nome. Liga as teclas tocadas no AKIRA à prévia. Fn não tem: o firmware a consome.
export const SCAN_CODES = {
  "Esc": 0x01, "F1": 0x3B, "F2": 0x3C, "F3": 0x3D, "F4": 0x3E, "F5": 0x3F, "F6": 0x40,
  "F7": 0x41, "F8": 0x42, "F9": 0x43, "F10": 0x44, "F11": 0x57, "F12": 0x58, "Del": 0xE053,
  "~`": 0x29, "!1": 0x02, "@2": 0x03, "#3": 0x04, "$4": 0x05, "%5": 0x06, "^6": 0x07,
  "&7": 0x08, "*8": 0x09, "(9": 0x0A, ")0": 0x0B, "_-": 0x0C, "+=": 0x0D, "Backspace": 0x0E,
  "Home": 0xE047, "Tab": 0x0F, "Q": 0x10, "W": 0x11, "E": 0x12, "R": 0x13, "T": 0x14,
  "Y": 0x15, "U": 0x16, "I": 0x17, "O": 0x18, "P": 0x19, "{[": 0x1A, "}}": 0x1B, "|\\": 0x2B,
  "end": 0xE04F, "CapsLock": 0x3A, "A": 0x1E, "S": 0x1F, "D": 0x20, "F": 0x21, "G": 0x22,
  "H": 0x23, "J": 0x24, "K": 0x25, "L": 0x26, ":;": 0x27, "\"'": 0x28, "Enter": 0x1C,
  "PgUp": 0xE049, "Shift_L": 0x2A, "Z": 0x2C, "X": 0x2D, "C": 0x2E, "V": 0x2F, "B": 0x30,
  "N": 0x31, "M": 0x32, ",<": 0x33, ".>": 0x34, "/?": 0x35, "Shift_R": 0x36, "↑": 0xE048,
  "PgDn": 0xE051, "Ctrl_L": 0x1D, "Win_L": 0xE05B, "Alt_L": 0x38, "Space": 0x39,
  "Alt_R": 0xE038, "Ctrl_R": 0xE01D, "←": 0xE04B, "↓": 0xE050, "→": 0xE04D,
};
