// Prévia da página de iluminação, copiada do app oficial (DeviceDriver.exe 1.0.0.0 + mui.dll).
//
// Pelo cabo o app manda a cor que cada LED mostra agora (mensagem mirror, lida com 04 F5 como em
// fcn.0042adb0) e a prévia é um espelho do teclado: qualquer efeito aparece como ele é.
// Sem essa leitura (dongle, simulação ou nenhum teclado) o oficial deixa a prévia parada:
// todas as teclas na cor escolhida ou, com "Colorido", a paleta fixa abaixo. Brilho,
// velocidade e direção não mudam a prévia parada (fcn.0042aa10, fcn.0042a8f0 e fcn.0042b730
// só chamam SetLightModeColor com a cor e o "Colorido").

// MUI::KeyboardCtrl::SetLightModeColor com o primeiro argumento 1 (mui.dll, 0x10032790):
// cor de cada tecla pelo uso HID, na ordem do código original. Inclui teclas que o AKIRA
// não tem; as dele estão todas aqui.
export const RAINBOW_PALETTE = {
  0x29: "#4500FF", 0x3A: "#FF004E", 0x3B: "#FF0000", 0x3C: "#FF4500", 0x3D: "#FFFF00", 0x3E: "#00FF00",
  0x3F: "#00FF45", 0x40: "#00FFFF", 0x41: "#00FFFF", 0x42: "#00FF45", 0x43: "#00FF00", 0x44: "#4EFF00",
  0x45: "#4EFF00", 0x46: "#FFFF00", 0x47: "#FF4500", 0x48: "#FF0000", 0xD1: "#FF004E", 0xD2: "#FF00FF",
  0xD3: "#4500FF", 0xD4: "#4500FF", 0x35: "#FF00FF", 0x1E: "#FF004E", 0x1F: "#FF0000", 0x20: "#FF4500",
  0x21: "#FFFF00", 0x22: "#4EFF00", 0x23: "#00FF00", 0x24: "#00FF45", 0x25: "#00FFFF", 0x26: "#004EFF",
  0x27: "#004EFF", 0x2D: "#00FFFF", 0x2E: "#00FF45", 0x2A: "#00FF00", 0x49: "#4EFF00", 0x4A: "#FFFF00",
  0x4B: "#FF4500", 0x53: "#FF0000", 0x54: "#FF004E", 0x55: "#FF00FF", 0x56: "#4500FF", 0x2B: "#FF004E",
  0x14: "#FF0000", 0x1A: "#FF4500", 0x08: "#FFFF00", 0x15: "#4EFF00", 0x17: "#00FF00", 0x1C: "#00FF45",
  0x18: "#00FFFF", 0x0C: "#004EFF", 0x12: "#0000FF", 0x13: "#004EFF", 0x2F: "#00FFFF", 0x30: "#00FF45",
  0x31: "#00FF00", 0x4C: "#4EFF00", 0x4D: "#FFFF00", 0x4E: "#FF4500", 0x5F: "#FF0000", 0x60: "#FF004E",
  0x61: "#FF00FF", 0x39: "#FF004E", 0x04: "#FF0000", 0x16: "#FF4500", 0x07: "#FFFF00", 0x09: "#4EFF00",
  0x0A: "#00FF00", 0x0B: "#00FF45", 0x0D: "#00FFFF", 0x0E: "#004EFF", 0x0F: "#004EFF", 0x33: "#00FFFF",
  0x34: "#00FF45", 0x28: "#00FF00", 0x5C: "#FF0000", 0x5D: "#FF004E", 0x5E: "#FF00FF", 0x57: "#4500FF",
  0xE1: "#FF00FF", 0x1D: "#FF0000", 0x1B: "#FF4500", 0x06: "#FFFF00", 0x19: "#4EFF00", 0x05: "#00FF00",
  0x11: "#00FF45", 0x10: "#00FFFF", 0x36: "#00FFFF", 0x37: "#00FFFF", 0x38: "#00FF45", 0xE5: "#00FF00",
  0x52: "#FFFF00", 0x59: "#FF0000", 0x5A: "#FF004E", 0x5B: "#FF00FF", 0xE0: "#FF00FF", 0xE3: "#FF00FF",
  0xE2: "#FF0000", 0x2C: "#4EFF00", 0xE6: "#00FF45", 0xAF: "#00FF00", 0x65: "#4EFF00", 0xE4: "#4EFF00",
  0x50: "#FFFF00", 0x51: "#FF4500", 0x4F: "#FF0000", 0x62: "#FF004E", 0x63: "#FF00FF", 0x58: "#4500FF",
};

/**
 * Cor da tecla na prévia parada. Colorido e Espectro não têm cor nem "Colorido": o oficial não
 * mexe na prévia ao escolher um deles (ela fica como estava); aqui eles mostram a paleta.
 * @param {{code: number}} key tecla do layout.js
 * @param {{color: boolean, rainbow: boolean}} effect efeito do catálogo
 * @param {{color: string, rainbow: boolean}} lighting ajustes do efeito
 * @returns {string} cor #RRGGBB
 */
export function staticKeyColor(key, effect, lighting) {
  const rainbow = effect.rainbow ? lighting.rainbow : !effect.color;
  return (rainbow && RAINBOW_PALETTE[key.code]) || lighting.color;
}

// Sem quadro novo nesse tempo, a prévia volta a ficar parada (cabo desligado, envio longo)
export const MIRROR_STALE_MS = 2000;
