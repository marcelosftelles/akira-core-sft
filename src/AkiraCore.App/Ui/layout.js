// Geometria do AKIRA CORE no espaço 800 × 300 do layout oficial.
// Teclas: rect_left, rect_top, rect_width, rect_height e light_index copiados do
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
  { name: "Esc", led: 1, x: 81, y: 23, w: 30, h: 30 },
  { name: "F1", led: 2, x: 130, y: 23, w: 30, h: 30 },
  { name: "F2", led: 3, x: 169, y: 23, w: 30, h: 30 },
  { name: "F3", led: 4, x: 208, y: 23, w: 30, h: 30 },
  { name: "F4", led: 5, x: 247, y: 23, w: 30, h: 30 },
  { name: "F5", led: 6, x: 296, y: 23, w: 30, h: 30 },
  { name: "F6", led: 7, x: 336, y: 23, w: 30, h: 30 },
  { name: "F7", led: 8, x: 376, y: 23, w: 30, h: 30 },
  { name: "F8", led: 9, x: 415, y: 23, w: 30, h: 30 },
  { name: "F9", led: 10, x: 464, y: 23, w: 30, h: 30 },
  { name: "F10", led: 11, x: 504, y: 23, w: 30, h: 30 },
  { name: "F11", led: 12, x: 544, y: 23, w: 30, h: 30 },
  { name: "F12", led: 13, x: 584, y: 23, w: 30, h: 30 },
  { name: "Del", led: 119, x: 633, y: 23, w: 30, h: 30 },
  { name: "~`", led: 19, x: 81, y: 76, w: 30, h: 30 },
  { name: "!1", led: 20, x: 118, y: 76, w: 30, h: 30 },
  { name: "@2", led: 21, x: 157, y: 76, w: 30, h: 30 },
  { name: "#3", led: 22, x: 197, y: 76, w: 30, h: 30 },
  { name: "$4", led: 23, x: 237, y: 76, w: 30, h: 30 },
  { name: "%5", led: 24, x: 277, y: 76, w: 30, h: 30 },
  { name: "^6", led: 25, x: 317, y: 76, w: 30, h: 30 },
  { name: "&7", led: 26, x: 356, y: 76, w: 30, h: 30 },
  { name: "*8", led: 27, x: 396, y: 76, w: 30, h: 30 },
  { name: "(9", led: 28, x: 436, y: 76, w: 30, h: 30 },
  { name: ")0", led: 29, x: 475, y: 76, w: 30, h: 30 },
  { name: "_-", led: 30, x: 515, y: 76, w: 30, h: 30 },
  { name: "+=", led: 31, x: 554, y: 76, w: 30, h: 30 },
  { name: "Backspace", led: 103, x: 594, y: 76, w: 69, h: 30 },
  { name: "Home", led: 117, x: 690, y: 76, w: 30, h: 30 },
  { name: "Tab", led: 37, x: 81, y: 116, w: 48, h: 30 },
  { name: "Q", led: 38, x: 139, y: 116, w: 30, h: 30 },
  { name: "W", led: 39, x: 179, y: 116, w: 30, h: 30 },
  { name: "E", led: 40, x: 218, y: 116, w: 30, h: 30 },
  { name: "R", led: 41, x: 258, y: 116, w: 30, h: 30 },
  { name: "T", led: 42, x: 297, y: 116, w: 30, h: 30 },
  { name: "Y", led: 43, x: 336, y: 116, w: 30, h: 30 },
  { name: "U", led: 44, x: 376, y: 116, w: 30, h: 30 },
  { name: "I", led: 45, x: 415, y: 116, w: 30, h: 30 },
  { name: "O", led: 46, x: 455, y: 116, w: 30, h: 30 },
  { name: "P", led: 47, x: 495, y: 116, w: 30, h: 30 },
  { name: "{[", led: 48, x: 534, y: 116, w: 30, h: 30 },
  { name: "}}", led: 49, x: 574, y: 116, w: 30, h: 30 },
  { name: "|\\", led: 67, x: 614, y: 116, w: 49, h: 30 },
  { name: "end", led: 120, x: 690, y: 116, w: 30, h: 30 },
  { name: "CapsLock", led: 55, x: 81, y: 156, w: 58, h: 30 },
  { name: "A", led: 56, x: 149, y: 156, w: 30, h: 30 },
  { name: "S", led: 57, x: 188, y: 156, w: 30, h: 30 },
  { name: "D", led: 58, x: 228, y: 156, w: 30, h: 30 },
  { name: "F", led: 59, x: 267, y: 156, w: 30, h: 30 },
  { name: "G", led: 60, x: 307, y: 156, w: 30, h: 30 },
  { name: "H", led: 61, x: 346, y: 156, w: 30, h: 30 },
  { name: "J", led: 62, x: 386, y: 156, w: 30, h: 30 },
  { name: "K", led: 63, x: 425, y: 156, w: 30, h: 30 },
  { name: "L", led: 64, x: 465, y: 156, w: 30, h: 30 },
  { name: ":;", led: 65, x: 505, y: 156, w: 30, h: 30 },
  { name: "\"'", led: 66, x: 544, y: 156, w: 30, h: 30 },
  { name: "Enter", led: 85, x: 584, y: 156, w: 79, h: 30 },
  { name: "PgUp", led: 118, x: 690, y: 156, w: 30, h: 30 },
  { name: "Shift_L", led: 73, x: 81, y: 194, w: 79, h: 30 },
  { name: "Z", led: 74, x: 168, y: 194, w: 30, h: 30 },
  { name: "X", led: 75, x: 208, y: 194, w: 30, h: 30 },
  { name: "C", led: 76, x: 247, y: 194, w: 30, h: 30 },
  { name: "V", led: 77, x: 287, y: 194, w: 30, h: 30 },
  { name: "B", led: 78, x: 326, y: 194, w: 30, h: 30 },
  { name: "N", led: 79, x: 366, y: 194, w: 30, h: 30 },
  { name: "M", led: 80, x: 405, y: 194, w: 30, h: 30 },
  { name: ",<", led: 81, x: 445, y: 194, w: 30, h: 30 },
  { name: ".>", led: 82, x: 485, y: 194, w: 30, h: 30 },
  { name: "/?", led: 83, x: 524, y: 194, w: 30, h: 30 },
  { name: "Shift_R", led: 84, x: 564, y: 194, w: 58, h: 30 },
  { name: "↑", led: 101, x: 641, y: 202, w: 30, h: 30 },
  { name: "PgDn", led: 121, x: 690, y: 194, w: 30, h: 30 },
  { name: "Ctrl_L", led: 91, x: 81, y: 233, w: 40, h: 30 },
  { name: "Win_L", led: 92, x: 129, y: 233, w: 40, h: 30 },
  { name: "Alt_L", led: 93, x: 179, y: 233, w: 40, h: 30 },
  { name: "Space", led: 94, x: 230, y: 233, w: 233, h: 30 },
  { name: "Alt_R", led: 95, x: 474, y: 233, w: 30, h: 30 },
  { name: "Fn", led: 96, x: 514, y: 233, w: 30, h: 30 },
  { name: "Ctrl_R", led: 98, x: 553, y: 233, w: 30, h: 30 },
  { name: "←", led: 99, x: 602, y: 240, w: 30, h: 30 },
  { name: "↓", led: 100, x: 641, y: 240, w: 30, h: 30 },
  { name: "→", led: 102, x: 680, y: 240, w: 30, h: 30 },
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
