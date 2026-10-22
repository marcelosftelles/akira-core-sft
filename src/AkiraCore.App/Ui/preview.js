// Prévia da página de iluminação.
//
// Pelo cabo o app manda a cor que cada LED mostra agora (mensagem mirror, lida com 04 F5 como em
// fcn.0042adb0): a prévia é um espelho do teclado. Sem essa leitura (dongle, simulação) ela
// imita o teclado com as tabelas medidas em effect-tables.js: cada efeito foi gravado pelo cabo
// no AKIRA de verdade (Laboratório, seção 6) e reduzido a fase por LED, velocidade, forma de
// onda e modo de cor. Os efeitos que reagem a tecla recebem as teclas tocadas no AKIRA (o app
// as repassa pelo Raw Input) ou toques de demonstração: Acender e Apagar ao tocar seguem a
// gravação; Explodir, Lançamento e Ondulações ainda não foram gravados com teclas e usam um
// desenho aproximado (a página avisa).

import { EFFECT_TABLES, PALETTE, SPEED_FACTOR } from "./effect-tables.js";

// Sem quadro novo nesse tempo, a prévia volta a ser simulada (cabo desligado, envio longo)
export const MIRROR_STALE_MS = 2000;

// Brilho máximo que o firmware devolve na leitura (FD com brilho 5)
const FULL = 253;

const norm = (a) => ((a % 360) + 360) % 360;

/**
 * Cor dos efeitos que giram a matiz: HSV com saturação e valor cheios (um canal sempre em
 * 253, outro subindo ou descendo), como o teclado devolve na leitura.
 * @param {number} angle matiz em graus
 * @returns {number[]} [r, g, b] de 0 a 253
 */
export function wheelRgb(angle) {
  const a = norm(angle) / 60;
  const sector = Math.floor(a);
  const up = Math.round(FULL * (a - sector));
  const down = FULL - up;
  return [
    [FULL, up, 0], [down, FULL, 0], [0, FULL, up], [0, down, FULL], [up, 0, FULL], [FULL, 0, down],
  ][sector % 6];
}

const rgbOf = (n) => [(n >> 16) & 255, (n >> 8) & 255, n & 255];

function hexToRgb(hex) {
  const n = parseInt(hex.slice(1), 16);
  return rgbOf(n);
}

// Cor da paleta para um LED numa volta: sorteio fixo, para a mesma volta dar a mesma cor
function paletteColor(led, cycle) {
  let h = Math.imul(led, 0x9e3779b1) ^ Math.imul(cycle, 0x85ebca6b);
  h = Math.imul(h ^ (h >>> 15), 0x2c1b3c6d);
  h ^= h >>> 13;
  return rgbOf(PALETTE[(h >>> 0) % PALETTE.length]);
}

function waveAt(wave, angle) {
  const pos = (norm(angle) / 360) * wave.length;
  const i = Math.floor(pos);
  const a = wave[i % wave.length];
  const b = wave[(i + 1) % wave.length];
  return (a + (b - a) * (pos - i)) / FULL;
}

/**
 * Tabela do efeito com o arco-íris ligado ou desligado. Colorido e Espectro não têm cor
 * escolhida: o firmware decide as cores, então só existe a variante arco-íris.
 * @param {{key: string, color: boolean, rainbow: boolean}} effect efeito do catálogo
 * @param {{rainbow: boolean}} lighting ajustes do efeito
 */
export function effectTable(effect, lighting) {
  const tables = EFFECT_TABLES[effect.key];
  if (!tables) return null;
  const rainbow = effect.rainbow ? lighting.rainbow : !effect.color;
  return (rainbow ? tables.rainbow : tables.single) ?? tables.rainbow ?? tables.single;
}

/** O efeito muda com o tempo (precisa de quadros novos para a prévia). */
export function isAnimated(effect, lighting) {
  const table = effectTable(effect, lighting);
  return Boolean(table?.rate);
}

// Setas: 1 (direita) e 2 (cima) invertem o sentido gravado com 0 (Rolagem: 0 e 3 iguais)
function directionSign(direction) {
  return direction === 1 || direction === 2 ? -1 : 1;
}

// --- Efeitos que reagem a tecla -----------------------------------------------------------

// Acender ao tocar, gravado em 21/10/2026 (velocidade 3): a tecla fica cheia enquanto está
// apertada e, ao soltar, apaga em (1 - t/T)² com T = 2,03 s (pior quadro a 6/253 da gravação).
const SINGLE_ON_FADE_MS = 2030;
// Apagar ao tocar: a tecla apaga ao apertar e, ao soltar, volta devagar, em (t/T)² (os quadros
// medidos dão T perto de 2 s). Com arco-íris ela volta com outra cor da paleta.
const SINGLE_OFF_RETURN_MS = 2000;
// Explodir, Lançamento e Ondulações: desenho aproximado até haver gravação com teclas.
// Unidades do layout.js (uma tecla tem 39 de passo); tempos na velocidade 3.
const WAVE_SPEED = 300; // unidades por segundo
const WAVE_LIFE_MS = 1400;
const RING_WIDTH = 34;
const EXPLODE_RADIUS = 105; // Explodir para em umas duas teclas e meia de raio

/** Efeitos que só mudam quando alguém toca uma tecla. */
export const REACTIVE_EFFECTS = new Set(["single-on", "single-off", "explode", "launch", "ripples"]);
/** Efeitos cujo desenho ainda é aproximado (sem gravação com teclas). */
export const APPROXIMATE_EFFECTS = new Set(["explode", "launch", "ripples"]);
/** Depois de soltar, um toque não muda mais nada após esse tempo (na velocidade mais lenta). */
export const PRESS_MEMORY_MS = Math.ceil(Math.max(SINGLE_ON_FADE_MS, SINGLE_OFF_RETURN_MS,
  WAVE_LIFE_MS + 900) / SPEED_FACTOR[1]);

export const isReactive = (effect) => REACTIVE_EFFECTS.has(effect.key);

const centerOf = (key) => ({ x: key.x + key.w / 2, y: key.y + key.h / 2 });

// Último toque da tecla (a lista vem em ordem de tempo)
function lastPressOf(led, presses, ms) {
  for (let i = presses.length - 1; i >= 0; i--) {
    if (presses[i].led === led && presses[i].at <= ms) return presses[i];
  }
  return null;
}

// Desvanecimento das ondas desenhadas a partir de cada toque
const fade = (age, life) => (age < 0 || age > life ? 0 : (1 - age / life) ** 2);

/**
 * Cor ([0..253]) e nível (0..1) de uma tecla num efeito reativo.
 * @param {{led: number, x: number, y: number, w: number, h: number}} key
 * @param {{led: number, x: number, y: number, at: number, up: number|null, cycle: number}[]} presses
 */
function reactiveColor(key, effect, lighting, ms, presses) {
  const rainbow = effect.rainbow ? lighting.rainbow : !effect.color;
  const speed = effect.speed ? SPEED_FACTOR[lighting.speed] ?? 1 : 1;
  const user = hexToRgb(lighting.color);
  const led = key.led;

  if (effect.key === "single-on" || effect.key === "single-off") {
    const press = lastPressOf(led, presses, ms);
    const pick = (p) => (rainbow ? paletteColor(led, p.cycle) : user);
    if (effect.key === "single-on") {
      if (!press) return [user, 0];
      if (press.up === null || press.up > ms) return [pick(press), 1];
      const t = (ms - press.up) / (SINGLE_ON_FADE_MS / speed);
      return [pick(press), t >= 1 ? 0 : (1 - t) ** 2];
    }
    if (!press) {
      // Sem toque ainda: as cores que o teclado mostra ao ligar o efeito
      const table = effectTable(effect, lighting);
      return [table?.color === "fixed" ? rgbOf(table.colors[led] ?? 0) : user, 1];
    }
    if (press.up === null || press.up > ms) return [pick(press), 0];
    const t = (ms - press.up) / (SINGLE_OFF_RETURN_MS / speed);
    return [pick(press), t >= 1 ? 1 : t * t];
  }

  // Ondas a partir de cada toque: a mais forte vence
  const here = centerOf(key);
  let best = 0;
  let color = user;
  for (const press of presses) {
    const age = ms - press.at;
    const life = WAVE_LIFE_MS / speed;
    if (age < 0 || age > life + 900 / speed) continue;
    const reach = (WAVE_SPEED * speed * age) / 1000;
    let level = 0;
    if (effect.key === "launch") {
      // Uma luz sobe pela coluna da tecla tocada até sair pela fileira de cima
      const sameColumn = Math.abs(here.x - press.x) < key.w / 2 + 6;
      const y = press.y - reach;
      if (sameColumn && here.y <= press.y + 1) level = Math.max(0, 1 - Math.abs(here.y - y) / 30);
    } else {
      // Distância entre os centros, nas unidades do layout.js
      const d = Math.hypot(here.x - press.x, here.y - press.y);
      level = effect.key === "explode"
        // Explodir: um disco que cresce e some
        ? Math.max(0, Math.min(1, 1 - (d - Math.min(reach, EXPLODE_RADIUS)) / RING_WIDTH)) * Math.max(0, 1 - (age / life) ** 2)
        // Ondulações: um anel que se espalha
        : Math.max(0, 1 - Math.abs(d - reach) / RING_WIDTH) * fade(age, life + 400 / speed);
    }
    if (level > best) {
      best = level;
      color = rainbow ? paletteColor(press.led, press.cycle) : user;
    }
  }
  return [color, best];
}

// Cor ([0..253]) e nível (0..1) de uma tecla pelas tabelas gravadas
function tableColor(key, effect, lighting, ms) {
  const table = effectTable(effect, lighting);
  if (!table || table.color === "off") return [[0, 0, 0], 0];
  const led = key.led;

  let angle = 0;
  if (table.rate) {
    const rate = Array.isArray(table.rate) ? table.rate[led] ?? 0 : table.rate;
    const sign = (table.sign?.[led] ?? 1) * directionSign(lighting.direction);
    const speed = effect.speed ? SPEED_FACTOR[lighting.speed] ?? 1 : 1;
    angle = (table.phase?.[led] ?? 0) + (rate * sign * speed * ms) / 1000;
  }

  let rgb;
  switch (table.color) {
    case "hue": rgb = wheelRgb(angle); break;
    case "palette": rgb = paletteColor(led, Math.floor(angle / 360)); break;
    case "fixed": {
      rgb = rgbOf(table.colors[led] ?? 0);
      // Respiração: a cor gravada no pico é levada ao máximo e o brilho vem da onda
      const peak = Math.max(...rgb);
      if (table.normalize && peak) rgb = rgb.map((v) => (v * FULL) / peak);
      break;
    }
    default: rgb = hexToRgb(lighting.color); break;
  }
  // Brilho do efeito (onda medida)
  return [rgb, table.wave ? waveAt(table.wave, angle) : 1];
}

/**
 * Cor simulada de uma tecla, [r, g, b] de 0 a 255.
 * @param {{led: number, x: number, y: number, w: number, h: number}} key tecla do layout.js
 * @param {{key: string, color: boolean, rainbow: boolean}} effect efeito do catálogo
 * @param {{color: string, rainbow: boolean, brightness: number, speed: number, direction: number}} lighting
 * @param {number} ms tempo desde que o efeito começou
 * @param {{led: number, x: number, y: number, at: number, up: number|null, cycle: number}[]} [presses]
 *   toques no mesmo relógio de ms, em ordem de tempo (x e y: centro da tecla; up null = apertada)
 */
export function simulatedKeyColor(key, effect, lighting, ms, presses = []) {
  const [rgb, level] = isReactive(effect)
    ? reactiveColor(key, effect, lighting, ms, presses)
    : tableColor(key, effect, lighting, ms);
  // Ajuste de brilho de 1 a 5
  const brightness = effect.brightness ? (lighting.brightness ?? 5) / 5 : 1;
  const scale = (level * brightness * 255) / FULL;
  return rgb.map((v) => Math.min(255, Math.round(v * scale)));
}
