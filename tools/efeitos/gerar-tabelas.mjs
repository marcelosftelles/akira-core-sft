// Gera src/AkiraCore.App/Ui/effect-tables.js a partir de uma gravação do Laboratório (seção 6):
// cada efeito lido pelo cabo com 04 F5 várias vezes por segundo.
//
//   node tools/efeitos/gerar-tabelas.mjs docs/gravacoes/efeitos-cabo-2026-10-21.json
//
// Modelo (vale para todos os efeitos que não dependem de tecla): cada LED tem uma fase em graus
// e o ângulo anda a uma velocidade constante. O brilho sai de uma forma de onda medida (brilho em
// função do ângulo) e a cor vem da matiz (HSV com valor cheio), da cor escolhida, de
// uma paleta sorteada a cada volta ou de uma cor fixa por tecla. Tudo aqui é medido; nada é
// inventado. Limites: um quadro a cada ~290 ms, então velocidades acima de ~620°/s apareceriam
// rebatidas; a busca fica abaixo disso.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const input = process.argv[2] ?? "docs/gravacoes/efeitos-cabo-2026-10-21.json";
const output = path.join(root, "src/AkiraCore.App/Ui/effect-tables.js");
const rec = JSON.parse(fs.readFileSync(path.resolve(root, input), "utf8"));
const layout = fs.readFileSync(path.join(root, "src/AkiraCore.App/Ui/layout.js"), "utf8");
const KEYS = [...layout.matchAll(/led: (\d+), x: (\d+), y: (\d+), w: (\d+), h: (\d+)/g)]
  .map((m) => ({ led: +m[1], cx: +m[2] + m[4] / 2, cy: +m[3] + m[5] / 2 }));
const LEDS = KEYS.map((k) => k.led);

// Ordem de leitura (fileira a fileira, da esquerda para a direita): o caminho do Fluindo
const row = (cy) => (cy < 60 ? 0 : cy < 110 ? 1 : cy < 150 ? 2 : cy < 190 ? 3 : cy < 230 ? 4 : 5);
const READING_ORDER = [...KEYS].sort((a, b) => row(a.cy) - row(b.cy) || a.cx - b.cx).map((k) => k.led);
const MAX_LED = Math.max(...LEDS);

const NYQUIST = 600;   // °/s; acima disso o quadro de ~290 ms não distingue a velocidade
const BINS = 72;       // amostras da forma de onda (5° cada)

// A leitura traz os LEDs em pedaços: os de índice alto chegam depois (medido no Espectro,
// que é uniforme no teclado). Atraso em ms por pedaço de 16 LEDs.
const readDelay = (led) => {
  const chunk = Math.floor((led * 4) / 64);
  return chunk === 0 ? 0 : chunk <= 3 ? 30 : chunk <= 5 ? 66 : 107;
};

function decode(hex) {
  const b = Buffer.from(hex, "hex");
  const out = [];
  for (let i = 0; i <= 142; i++) out[i] = [b[i * 4 + 1], b[i * 4 + 2], b[i * 4 + 3]];
  return out;
}

// Matiz como no HSV: os efeitos que giram a cor mantêm um canal no máximo (FD) e sobem ou
// descem outro, ou seja, saturação e valor cheios. (O Estático em arco-íris usa outra roda,
// com a soma dos canais fixa, mas ele vai para a tabela como cores fixas.)
function wheelAngle([r, g, b]) {
  const max = Math.max(r, g, b);
  const d = max - Math.min(r, g, b);
  if (max < 60 || d === 0) return null;
  const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return norm(h * 60);
}

const norm = (a) => ((a % 360) + 360) % 360;
const round1 = (x) => Math.round(x * 10) / 10;

function variant(effect, rainbow, extra = {}) {
  const v = rec.variants.find((x) => x.effect === effect && x.rainbow === rainbow
    && x.speed === (extra.speed ?? 3) && x.direction === (extra.direction ?? 0) && x.brightness === 5);
  if (!v) throw new Error(`Sem gravação de ${effect} ${rainbow ? "arco-íris" : "cor única"}`);
  return v.frames.map((f) => ({ t: f.t, d: decode(f.leds) }));
}

function perLed(fn) {
  const arr = new Array(MAX_LED + 1).fill(null);
  for (const led of LEDS) arr[led] = fn(led);
  return arr;
}

// Matiz girando: fase e velocidade pela regressão linear da matiz
function hueRotation(frames) {
  const fits = {};
  for (const led of LEDS) {
    const hs = frames.map((f) => wheelAngle(f.d[led]));
    if (hs.some((h) => h === null)) continue;
    const ts = frames.map((f) => (f.t + readDelay(led)) / 1000);
    const u = [hs[0]];
    for (let i = 1; i < hs.length; i++) u.push(u[i - 1] + (norm(hs[i] - hs[i - 1] + 180) - 180));
    const mt = ts.reduce((a, b) => a + b) / ts.length;
    const mu = u.reduce((a, b) => a + b) / u.length;
    let sxy = 0;
    let sxx = 0;
    ts.forEach((t, i) => { sxy += (t - mt) * (u[i] - mu); sxx += (t - mt) ** 2; });
    const rate = sxy / sxx;
    fits[led] = { rate, phase: norm(mu - rate * mt) };
  }
  const rates = Object.values(fits).map((f) => f.rate).sort((a, b) => a - b);
  const rate = rates[rates.length >> 1];
  const mixed = rates[0] < 0 && rates[rates.length - 1] > 0;
  return {
    color: "hue",
    rate: round1(mixed ? Math.abs(rate) : rate),
    // Vaivém: fileiras alternadas andam em sentidos opostos
    sign: mixed ? perLed((led) => (fits[led]?.rate < 0 ? -1 : 1)) : undefined,
    phase: perLed((led) => (fits[led] ? Math.round(fits[led].phase) : 0)),
  };
}

// Componente principal de frequência w (°/s) da série de um LED
function project(ts, ys, w) {
  const m = ys.reduce((a, b) => a + b) / ys.length;
  let re = 0;
  let im = 0;
  ts.forEach((t, i) => {
    const a = (w * t * Math.PI) / 180;
    re += (ys[i] - m) * Math.cos(a);
    im -= (ys[i] - m) * Math.sin(a);
  });
  return { power: re * re + im * im, phase: norm((Math.atan2(im, re) * 180) / Math.PI) };
}

// Brilho periódico: velocidade (global ou por tecla), fase por tecla e forma de onda
function brightnessWave(frames, { perKeyRate = false, fixedRate = null, fixedPhases = null } = {}) {
  const series = LEDS.map((led) => ({
    led,
    ts: frames.map((f) => (f.t + readDelay(led)) / 1000),
    ys: frames.map((f) => Math.max(...f.d[led])),
  }));
  const bestRate = (list) => {
    let best = null;
    for (let w = 5; w <= NYQUIST; w += 0.5) {
      const p = list.reduce((sum, s) => sum + project(s.ts, s.ys, w).power, 0);
      if (!best || p > best.p) best = { w, p };
    }
    return best.w;
  };
  const global = fixedRate ?? bestRate(series);
  const rates = {};
  for (const s of series) rates[s.led] = perKeyRate ? bestRate([s]) : global;
  const phases = {};
  for (const s of series) phases[s.led] = project(s.ts, s.ys, rates[s.led]).phase;
  if (fixedPhases) Object.assign(phases, fixedPhases);

  const bins = Array.from({ length: BINS }, () => []);
  for (const s of series) {
    s.ts.forEach((t, i) => {
      const a = norm(phases[s.led] + rates[s.led] * t);
      bins[Math.floor((a / 360) * BINS)].push(s.ys[i]);
    });
  }
  let sse = 0;
  let n = 0;
  const wave = bins.map((b) => {
    if (!b.length) return null;
    const m = b.reduce((x, y) => x + y) / b.length;
    b.forEach((y) => { sse += (y - m) ** 2; n++; });
    return Math.round(m);
  });
  // Amostras sem dado: interpola entre as vizinhas (a onda é circular)
  const at = (i) => ((i % BINS) + BINS) % BINS;
  const known = [...wave];
  for (let i = 0; i < BINS; i++) {
    if (known[i] !== null) continue;
    let a = i;
    let b = i;
    while (known[at(a - 1)] === null) a--;
    while (known[at(b + 1)] === null) b++;
    const left = known[at(a - 1)];
    const right = known[at(b + 1)];
    wave[i] = Math.round(left + ((right - left) * (i - a + 1)) / (b - a + 2));
  }
  return {
    rate: perKeyRate ? perLed((led) => round1(rates[led])) : round1(global),
    phase: perLed((led) => Math.round(phases[led])),
    wave,
    rmse: round1(Math.sqrt(sse / n)),
  };
}

// Fluindo: um rastro que percorre as teclas. Num quadro, o brilho cai um tanto fixo por tecla
// atrás da cabeça, então a diferença de brilho entre duas teclas acesas juntas dá a distância
// entre elas no caminho. Junta todas essas distâncias (mínimos quadrados, a volta fecha em P)
// e tira a posição de cada tecla; a fase é a posição em graus da volta.
function snake(frames) {
  const order = new Map(READING_ORDER.map((led, i) => [led, i]));
  const lit = frames.map((f) => ({
    t: f.t,
    keys: LEDS.map((led) => ({ led, v: Math.max(...f.d[led]) })).filter((k) => k.v > 0).sort((a, b) => b.v - a.v),
  }));
  // Queda de brilho por tecla: mediana das diferenças entre vizinhas no rastro
  const steps = lit.flatMap((f) => f.keys.slice(1).map((k, i) => f.keys[i].v - k.v)).filter((d) => d > 15 && d < 45);
  const step = steps.sort((a, b) => a - b)[steps.length >> 1];

  // Incógnitas: posição de cada LED (a primeira da ordem fica em 0) e o tamanho da volta P
  const index = new Map(LEDS.map((led, i) => [led, i]));
  const n = LEDS.length + 1;
  const A = Array.from({ length: n }, () => new Float64Array(n));
  const b = new Float64Array(n);
  const add = (row, rhs) => {
    for (let i = 0; i < n; i++) if (row[i]) { for (let j = 0; j < n; j++) A[i][j] += row[i] * row[j]; b[i] += row[i] * rhs; }
  };
  for (const f of lit) {
    for (let i = 0; i < f.keys.length; i++) {
      for (let j = i + 1; j < f.keys.length; j++) {
        const ahead = f.keys[i];
        const behind = f.keys[j];
        const row = new Float64Array(n);
        row[index.get(ahead.led)] += 1;
        row[index.get(behind.led)] -= 1;
        // A cabeça já deu a volta (ordem de leitura bem menor que a do rastro)
        if (order.get(behind.led) - order.get(ahead.led) > READING_ORDER.length / 2) row[n - 1] = 1;
        add(row, (ahead.v - behind.v) / step);
      }
    }
  }
  const anchor = new Float64Array(n);
  anchor[index.get(READING_ORDER[0])] = 1;
  add(anchor, 0);
  // Gauss com pivô parcial
  for (let c = 0; c < n; c++) {
    let p = c;
    for (let r = c + 1; r < n; r++) if (Math.abs(A[r][c]) > Math.abs(A[p][c])) p = r;
    [A[c], A[p]] = [A[p], A[c]];
    [b[c], b[p]] = [b[p], b[c]];
    for (let r = c + 1; r < n; r++) {
      const k = A[r][c] / A[c][c];
      if (!k) continue;
      for (let j = c; j < n; j++) A[r][j] -= k * A[c][j];
      b[r] -= k * b[c];
    }
  }
  const x = new Float64Array(n);
  for (let r = n - 1; r >= 0; r--) {
    let s = b[r];
    for (let j = r + 1; j < n; j++) s -= A[r][j] * x[j];
    x[r] = s / A[r][r];
  }
  const P = x[n - 1];
  // Cabeça em cada quadro: posição da tecla mais forte + o que falta para o brilho cheio
  const heads = lit.filter((f) => f.keys.length).map((f) => ({
    t: f.t / 1000,
    h: x[index.get(f.keys[0].led)] + (253 - f.keys[0].v) / step,
  }));
  for (let i = 1; i < heads.length; i++) {
    while (heads[i].h < heads[i - 1].h) heads[i].h += P;
  }
  const mt = heads.reduce((s, h) => s + h.t, 0) / heads.length;
  const mh = heads.reduce((s, h) => s + h.h, 0) / heads.length;
  const v = heads.reduce((s, h) => s + (h.t - mt) * (h.h - mh), 0) / heads.reduce((s, h) => s + (h.t - mt) ** 2, 0);
  const phases = {};
  for (const led of LEDS) phases[led] = norm((-360 * x[index.get(led)]) / P);
  return { rate: (360 * v) / P, phases, keysPerSecond: v, loop: P };
}

// Cor de cada tecla num quadro (normalizada para o máximo, como no brilho 5)
function fixedColors(frame) {
  return perLed((led) => {
    const [r, g, b] = frame.d[led];
    return (r << 16) | (g << 8) | b;
  });
}

const PALETTE = [...new Set(fixedColors(variant("single-off", true)[0]).filter((c) => c !== null))];

function brightestFrame(frames) {
  return frames.reduce((best, f) => {
    const sum = LEDS.reduce((s, led) => s + Math.max(...f.d[led]), 0);
    return !best || sum > best.sum ? { f, sum } : best;
  }, null).f;
}

const quality = [];
function wave(effect, rainbow, opts) {
  const fit = brightnessWave(variant(effect, rainbow), opts);
  quality.push(`${effect} ${rainbow ? "arco-íris" : "cor única"}: erro médio ${fit.rmse} de 253`);
  delete fit.rmse;
  return fit;
}

const off = { color: "off" };
const FLOW = snake(variant("flowing", false));
console.log(`Fluindo: volta de ${FLOW.loop.toFixed(1)} posições, ${FLOW.keysPerSecond.toFixed(1)} por segundo`);
const EFFECTS = {
  static: { rainbow: { color: "fixed", colors: fixedColors(variant("static", true)[0]) }, single: { color: "user" } },
  // Reagem a tecla: parados, o teclado fica assim
  "single-on": { rainbow: off, single: off },
  "single-off": { rainbow: { color: "fixed", colors: fixedColors(variant("single-off", true)[0]) }, single: { color: "user" } },
  glittering: {
    rainbow: { color: "palette", ...wave("glittering", true, { perKeyRate: true }) },
    single: { color: "user", ...wave("glittering", false, { perKeyRate: true }) },
  },
  fluttering: { rainbow: { color: "palette", ...wave("fluttering", true) }, single: { color: "user", ...wave("fluttering", false) } },
  colourful: { rainbow: hueRotation(variant("colourful", true)) },
  breath: {
    rainbow: { color: "fixed", colors: fixedColors(brightestFrame(variant("breath", true))), normalize: true, ...wave("breath", true) },
    single: { color: "user", ...wave("breath", false) },
  },
  spectrum: { rainbow: hueRotation(variant("spectrum", true)) },
  outward: { rainbow: hueRotation(variant("outward", true)), single: { color: "user", ...wave("outward", false) } },
  scrolling: { rainbow: hueRotation(variant("scrolling", true)), single: { color: "user", ...wave("scrolling", false) } },
  rolling: { rainbow: hueRotation(variant("rolling", true)), single: { color: "user", ...wave("rolling", false) } },
  rotating: { rainbow: hueRotation(variant("rotating", true)), single: { color: "user", ...wave("rotating", false) } },
  explode: { rainbow: off, single: off },
  launch: { rainbow: off, single: off },
  ripples: { rainbow: off, single: off },
  // Um rastro percorre as teclas fileira a fileira; o arco-íris usa o mesmo caminho
  flowing: {
    rainbow: { color: "palette", ...wave("flowing", false, { fixedRate: FLOW.rate, fixedPhases: FLOW.phases }) },
    single: { color: "user", ...wave("flowing", false, { fixedRate: FLOW.rate, fixedPhases: FLOW.phases }) },
  },
  pulsating: {
    rainbow: { color: "palette", ...wave("pulsating", true, { perKeyRate: true }) },
    single: { color: "user", ...wave("pulsating", false, { perKeyRate: true }) },
  },
  tilt: { rainbow: { color: "palette", ...wave("tilt", true) }, single: { color: "user", ...wave("tilt", false) } },
  shuttle: { rainbow: hueRotation(variant("shuttle", true)), single: { color: "user", ...wave("shuttle", false) } },
};

// Velocidade: o Rolando foi gravado em 1, 3 e 5. O intervalo entre passos do firmware
// (inverso da velocidade) cai quase em linha reta; 2 e 4 ficam no meio dele.
const rateAt = (s) => Math.abs(hueRotation(variant("rolling", true, { speed: s })).rate);
const r1 = rateAt(1);
const r3 = rateAt(3);
const r5 = rateAt(5);
const r2 = 2 / (1 / r1 + 1 / r3);
const r4 = 2 / (1 / r3 + 1 / r5);
const SPEED = [0, r1, r2, r3, r4, r5].map((r) => Math.round((r / r3) * 1000) / 1000);

const hex = (c) => `0x${c.toString(16).padStart(6, "0")}`;
const arr = (a, asColor = false) => `[${a.map((x) => (x === null ? "null" : asColor ? hex(x) : x)).join(",")}]`;
const body = Object.entries(EFFECTS).map(([key, variants]) => {
  const parts = Object.entries(variants).map(([name, v]) => {
    const fields = Object.entries(v).filter(([, x]) => x !== undefined).map(([k, x]) =>
      `${k}: ${Array.isArray(x) ? arr(x, k === "colors") : JSON.stringify(x)}`);
    return `    ${name}: { ${fields.join(", ")} },`;
  });
  return `  ${JSON.stringify(key)}: {\n${parts.join("\n")}\n  },`;
}).join("\n");

const source = `// Gerado por tools/efeitos/gerar-tabelas.mjs a partir de ${input.replaceAll("\\", "/")}.
// Não editar à mão: rode o gerador de novo com outra gravação do Laboratório (seção 6).
//
// Medido no AKIRA pelo cabo (04 F5, brilho 5, velocidade 3, direção 0). Por LED: phase em graus;
// rate em graus por segundo (número ou um por LED); sign inverte o sentido de um LED.
// wave: brilho (0 a 253) a cada ${360 / BINS}° do ângulo. color: hue (matiz HSV, valor 253),
// user (cor escolhida), palette (cor sorteada a cada volta), fixed (colors por LED) ou off.
${quality.map((q) => `// ${q}`).join("\n")}

export const SPEED_FACTOR = ${arr(SPEED)};

export const PALETTE = ${arr(PALETTE, true)};

export const EFFECT_TABLES = {
${body}
};
`;
fs.writeFileSync(output, source);
console.log(`Escrito ${path.relative(root, output)} (${(source.length / 1024).toFixed(0)} KB)`);
console.log(quality.join("\n"));
console.log("Velocidade:", SPEED.join(" "));
