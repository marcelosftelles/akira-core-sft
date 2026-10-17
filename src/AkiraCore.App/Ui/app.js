// Interface do AkiraCore. Fala com o C# por mensagens JSON (WebView2); toda regra de
// protocolo fica no AkiraCore.Core. Aqui só há estado de tela e a prévia do teclado.
import { VIEWBOX, BODY, KNOB, ZONES, KEYS } from "./layout.js";

const host = window.chrome?.webview
  ? webViewHost(window.chrome.webview)
  : (await import("./dev-mock.js")).createMockHost();

const PRESETS = [
  ["#FF0000", "Vermelho"], ["#FF6A00", "Laranja"], ["#FFD000", "Amarelo"],
  ["#00FF40", "Verde"], ["#00E5FF", "Ciano"], ["#0040FF", "Azul"],
  ["#8A2BE2", "Roxo"], ["#FF2D95", "Rosa"], ["#FFFFFF", "Branco"],
];

const state = {
  effects: [],
  lighting: { effect: "rolling", color: "#FF0000", brightness: 5, speed: 3, reverse: false, rainbow: true },
  lightOn: true,
  options: { swapRedBlue: false, simulate: false },
  device: { connected: false, channel: null, name: "" },
  view: "lighting",
};

const $ = (id) => document.getElementById(id);
const reduceMotion = matchMedia("(prefers-reduced-motion: reduce)");

// --- Mensagens -----------------------------------------------------------------

function webViewHost(webview) {
  return {
    post: (message) => webview.postMessage(message),
    onMessage: (handler) => webview.addEventListener("message", (e) => handler(e.data)),
  };
}

host.onMessage((message) => {
  switch (message.type) {
    case "state":
      state.effects = message.effects;
      state.lighting = message.lighting;
      state.lightOn = message.lightOn;
      state.options = message.options;
      state.device = message.device;
      renderAll();
      break;
    case "device":
      state.device = message.device;
      renderDevice();
      break;
    case "status":
      showStatus(message.kind, message.text);
      break;
    case "log":
      appendLog(message.title, message.lines);
      break;
  }
});

let applyTimer;
function applySoon(delay = 180) {
  clearTimeout(applyTimer);
  applyTimer = setTimeout(() => host.post({ type: "apply", lighting: { ...state.lighting } }), delay);
}

// --- Barra lateral -------------------------------------------------------------

const effectsList = $("effects");

function renderEffects() {
  effectsList.replaceChildren(...state.effects.map((effect) => {
    const item = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.className = "nav-item";
    button.setAttribute("role", "option");
    button.dataset.key = effect.key;
    button.textContent = effect.label;
    button.addEventListener("click", () => chooseEffect(effect.key));
    item.append(button);
    return item;
  }));
  renderSelection();
  revealChosenEffect();
}

function revealChosenEffect() {
  effectsList.querySelector(`[data-key="${state.lighting.effect}"]`)
    ?.scrollIntoView({ block: "nearest", inline: "nearest" });
}

function renderSelection() {
  for (const button of effectsList.querySelectorAll(".nav-item")) {
    const chosen = button.dataset.key === state.lighting.effect;
    button.setAttribute("aria-selected", String(chosen && state.view === "lighting"));
    button.dataset.inactive = String(chosen && state.view !== "lighting");
    button.tabIndex = chosen ? 0 : -1;
  }
  for (const button of document.querySelectorAll(".nav-bottom .nav-item")) {
    if (button.dataset.view === state.view) button.setAttribute("aria-current", "page");
    else button.removeAttribute("aria-current");
  }
}

effectsList.addEventListener("keydown", (e) => {
  if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
  e.preventDefault();
  const keys = state.effects.map((x) => x.key);
  const next = keys.indexOf(state.lighting.effect) + (e.key === "ArrowDown" ? 1 : -1);
  if (next < 0 || next >= keys.length) return;
  chooseEffect(keys[next]);
  effectsList.querySelector(`[data-key="${keys[next]}"]`)?.focus();
});

function chooseEffect(key) {
  state.lighting.effect = key;
  state.lightOn = true;
  showView("lighting");
  renderLighting();
  revealChosenEffect();
  applySoon(0);
}

for (const button of document.querySelectorAll(".nav-bottom .nav-item")) {
  button.addEventListener("click", () => showView(button.dataset.view));
}

function showView(view) {
  state.view = view;
  for (const section of document.querySelectorAll(".view")) {
    section.hidden = section.id !== `view-${view}`;
  }
  renderSelection();
}

function renderDevice() {
  const device = $("device");
  const { connected, channel } = state.device;
  let text = "Teclado não encontrado";
  let mode = "missing";
  if (state.options.simulate) {
    text = "Simulação, nada é enviado";
    mode = "simulated";
  } else if (connected) {
    text = channel === "dongle" ? "Conectado pelo dongle 2.4G" : "Conectado pelo cabo";
    mode = "connected";
  }
  device.dataset.state = mode;
  $("device-text").textContent = text;
}

// --- Iluminação ------------------------------------------------------------------

const currentEffect = () =>
  state.effects.find((e) => e.key === state.lighting.effect) ?? state.effects[0];

function buildSwatches() {
  const group = $("swatches");
  for (const [hex, name] of PRESETS) {
    const swatch = document.createElement("button");
    swatch.type = "button";
    swatch.className = "swatch";
    swatch.setAttribute("role", "radio");
    swatch.setAttribute("aria-label", name);
    swatch.dataset.color = hex;
    swatch.style.setProperty("--c", hex);
    swatch.addEventListener("click", () => chooseColor(hex));
    group.append(swatch);
  }
  // Outra cor: o seletor nativo fica por cima do círculo, invisível
  const custom = document.createElement("span");
  custom.className = "swatch custom";
  custom.setAttribute("role", "radio");
  custom.setAttribute("aria-label", "Outra cor");
  const picker = document.createElement("input");
  picker.type = "color";
  picker.setAttribute("aria-label", "Escolher outra cor");
  picker.addEventListener("input", () => { state.lighting.color = picker.value.toUpperCase(); renderLighting(); });
  picker.addEventListener("change", () => chooseColor(picker.value.toUpperCase()));
  custom.append(picker);
  group.append(custom);
}

function chooseColor(hex) {
  state.lighting.color = hex;
  state.lighting.rainbow = false;
  renderLighting();
  applySoon();
}

function renderSwatches() {
  const color = state.lighting.color.toUpperCase();
  let matched = false;
  for (const swatch of $("swatches").querySelectorAll(".swatch:not(.custom)")) {
    const on = swatch.dataset.color === color;
    matched ||= on;
    swatch.setAttribute("aria-checked", String(on));
  }
  const custom = $("swatches").querySelector(".custom");
  custom.setAttribute("aria-checked", String(!matched));
  if (matched) {
    delete custom.dataset.color;
  } else {
    custom.dataset.color = color;
    custom.style.setProperty("--c", color);
    custom.querySelector("input").value = color.toLowerCase();
  }
}

function setDisabled(control, disabled) {
  document.querySelectorAll(`#view-lighting [data-control="${control}"]`)
    .forEach((row) => row.setAttribute("aria-disabled", String(disabled)));
}

function renderRange(input, value) {
  input.value = value;
  input.style.setProperty("--p", `${(value / Number(input.max)) * 100}%`);
}

function renderLighting() {
  const effect = currentEffect();
  if (!effect) return;
  const { lighting } = state;
  $("effect-title").textContent = state.lightOn ? effect.label : "Luz desligada";
  $("light-on").checked = state.lightOn;
  $("rainbow").checked = lighting.rainbow;
  renderRange($("brightness"), lighting.brightness);
  renderRange($("speed"), lighting.speed);
  for (const button of document.querySelectorAll(".segmented button")) {
    button.setAttribute("aria-checked", String(button.dataset.reverse === String(lighting.reverse)));
  }
  renderSwatches();

  const off = !state.lightOn;
  setDisabled("color", off || !effect.color || (effect.rainbow && lighting.rainbow));
  setDisabled("rainbow", off || !effect.rainbow);
  setDisabled("brightness", off || !effect.brightness);
  setDisabled("speed", off || !effect.speed);
  setDisabled("direction", off || !effect.direction);
  renderSelection();
  paintBoards();
}

$("light-on").addEventListener("change", (e) => {
  state.lightOn = e.target.checked;
  renderLighting();
  host.post({ type: "setLight", on: state.lightOn, lighting: { ...state.lighting } });
});

$("rainbow").addEventListener("change", (e) => {
  state.lighting.rainbow = e.target.checked;
  renderLighting();
  applySoon();
});

for (const id of ["brightness", "speed"]) {
  const input = $(id);
  // Arrastar só atualiza a prévia; o teclado recebe ao soltar
  input.addEventListener("input", () => { state.lighting[id] = Number(input.value); renderLighting(); });
  input.addEventListener("change", () => applySoon());
}

for (const button of document.querySelectorAll(".segmented button")) {
  button.addEventListener("click", () => {
    state.lighting.reverse = button.dataset.reverse === "true";
    renderLighting();
    applySoon();
  });
}

// --- Ajustes ---------------------------------------------------------------------

function renderSettings() {
  $("swap").checked = state.options.swapRedBlue;
  $("simulate").checked = state.options.simulate;
}

for (const key of ["swap", "simulate"]) {
  $(key).addEventListener("change", (e) => {
    const option = key === "swap" ? "swapRedBlue" : "simulate";
    state.options[option] = e.target.checked;
    renderDevice();
    host.post({ type: "setOption", key: option, value: e.target.checked });
  });
}

$("diagnose").addEventListener("click", () => host.post({ type: "diagnose" }));
$("open-lab").addEventListener("click", () => host.post({ type: "openLab" }));

const confirmDialog = $("confirm");
$("reset").addEventListener("click", () => {
  $("confirm-text").textContent = state.device.channel === "dongle"
    ? "Pelo dongle, o teclado recebe o mesmo reset do app oficial: teclas remapeadas, macros e " +
      "iluminação guardadas nele voltam ao padrão."
    : "Pelo cabo, o app oficial não tem comando de reset: só a iluminação volta ao padrão " +
      "(Rolando, arco-íris, brilho 5, velocidade 3). As fitas continuam nos atalhos.";
  confirmDialog.showModal();
});
confirmDialog.addEventListener("close", () => {
  if (confirmDialog.returnValue === "confirm") host.post({ type: "factoryReset" });
});

// --- Status e log ------------------------------------------------------------------

let statusTimer;
function showStatus(kind, text) {
  clearTimeout(statusTimer);
  for (const status of document.querySelectorAll(".status")) {
    status.dataset.kind = kind;
    status.dataset.fade = "false";
    status.textContent = text;
  }
  if (kind === "ok") {
    statusTimer = setTimeout(() => {
      document.querySelectorAll(".status").forEach((s) => { s.dataset.fade = "true"; });
    }, 2200);
  }
}

function appendLog(title, lines) {
  $("log").hidden = false;
  $("log-title").textContent = title;
  const body = $("log-body");
  body.textContent += lines.join("\n") + "\n";
  body.scrollTop = body.scrollHeight;
}

$("log-clear").addEventListener("click", () => {
  $("log-body").textContent = "";
  $("log").hidden = true;
});

// --- Prévia do teclado ---------------------------------------------------------------

const SVG = "http://www.w3.org/2000/svg";
const CAP = [240, 240, 243];
const keyBounds = KEYS.reduce((b, k) => ({
  minX: Math.min(b.minX, k.x), maxX: Math.max(b.maxX, k.x + k.w),
  minY: Math.min(b.minY, k.y), maxY: Math.max(b.maxY, k.y + k.h),
}), { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity });

// Posição normalizada (0 a 1) e uma fase aleatória fixa por tecla
const keyPoints = KEYS.map((k, i) => ({
  x: (k.x + k.w / 2 - keyBounds.minX) / (keyBounds.maxX - keyBounds.minX),
  y: (k.y + k.h / 2 - keyBounds.minY) / (keyBounds.maxY - keyBounds.minY),
  phase: fract(Math.sin((i + 1) * 12.9898) * 43758.5453),
  column: Math.round((k.x - keyBounds.minX) / 39),
}));

function el(name, attrs, parent) {
  const node = document.createElementNS(SVG, name);
  for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
  parent?.append(node);
  return node;
}

const boards = [...document.querySelectorAll("[data-board]")].map((figure, index) => {
  const svg = el("svg", {
    class: "board",
    viewBox: `${VIEWBOX.x} ${VIEWBOX.y} ${VIEWBOX.width} ${VIEWBOX.height}`,
    role: "img",
    "aria-label": figure.dataset.zones !== undefined
      ? "Teclado com as fitas laterais e a loading bar destacadas"
      : "Prévia do teclado com o efeito escolhido",
  });
  const defs = el("defs", {}, svg);
  const glowId = `glow-${index}`;
  const filter = el("filter", { id: glowId, x: "-20%", y: "-20%", width: "140%", height: "140%" }, defs);
  el("feGaussianBlur", { stdDeviation: "4" }, filter);

  el("rect", { class: "body", x: BODY.x, y: BODY.y, width: BODY.width, height: BODY.height, rx: BODY.radius }, svg);
  const glowLayer = el("g", { filter: `url(#${glowId})` }, svg);
  const capLayer = el("g", {}, svg);
  const glows = KEYS.map((k) =>
    el("rect", { x: k.x - 3.5, y: k.y - 3.5, width: k.w + 7, height: k.h + 7, rx: 7, opacity: 0 }, glowLayer));
  const caps = KEYS.map((k) =>
    el("rect", { x: k.x + 1, y: k.y + 1, width: k.w - 2, height: k.h - 2, rx: 4.5 }, capLayer));
  el("circle", { class: "knob", cx: KNOB.cx, cy: KNOB.cy, r: KNOB.r }, svg);
  el("circle", { class: "knob-cap", cx: KNOB.cx, cy: KNOB.cy, r: KNOB.r - 4 }, svg);
  const zoneGlow = el("g", { filter: `url(#${glowId})` }, svg);
  for (const [name, zone] of Object.entries(ZONES)) {
    const rect = { x: zone.x, y: zone.y, width: zone.width, height: zone.height, rx: 2, "data-zone": name };
    el("rect", { ...rect, class: "zone-glow", x: zone.x - 3, y: zone.y - 3, width: zone.width + 6, height: zone.height + 6 }, zoneGlow);
    el("rect", { ...rect, class: "zone" }, svg);
  }
  figure.append(svg);
  // A página das fitas mostra o teclado apagado: o destaque são as zonas
  return { glows, caps, lit: figure.dataset.zones === undefined };
});

// Passar o mouse num grupo de atalhos destaca a zona correspondente
for (const group of document.querySelectorAll("[data-zone-group]")) {
  const stage = document.querySelector("[data-zones]");
  const focus = () => { stage.dataset.focus = group.dataset.zoneGroup; };
  const blur = () => { delete stage.dataset.focus; };
  group.addEventListener("mouseenter", focus);
  group.addEventListener("mouseleave", blur);
}

function fract(v) { return v - Math.floor(v); }

// Faixa de largura w em torno de 0 numa onda periódica
function band(v, w) {
  const f = fract(v);
  const d = Math.min(f, 1 - f);
  return Math.max(0, 1 - d / w);
}

function hsl(h) {
  h = fract(h / 360) * 6;
  const x = 1 - Math.abs((h % 2) - 1);
  const [r, g, b] = h < 1 ? [1, x, 0] : h < 2 ? [x, 1, 0] : h < 3 ? [0, 1, x]
    : h < 4 ? [0, x, 1] : h < 5 ? [x, 0, 1] : [1, 0, x];
  return [r * 255, g * 255, b * 255];
}

function hexToRgb(hex) {
  const n = parseInt(hex.slice(1), 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

// Eventos aleatórios (teclas tocadas, explosões) para os efeitos reativos
const events = { flashes: new Float32Array(KEYS.length).fill(-99), bursts: [], last: 0 };

function spawnEvents(ts) {
  if (ts - events.last < 0.42) return;
  events.last = ts;
  events.flashes[Math.floor(Math.random() * KEYS.length)] = ts;
  events.bursts.push({ x: Math.random(), y: Math.random(), t: ts });
  if (events.bursts.length > 6) events.bursts.shift();
}

function rings(p, ts, width) {
  let total = 0;
  for (const burst of events.bursts) {
    const age = ts - burst.t;
    const d = Math.hypot((p.x - burst.x) * 2.4, p.y - burst.y);
    total += Math.max(0, 1 - Math.abs(d - age * 0.9) / width) * Math.max(0, 1 - age / 1.6);
  }
  return Math.min(1, total);
}

// Aproximação visual de cada efeito do firmware: [intensidade 0..1, cor RGB]
const EFFECTS = {
  1: (p, ts, c) => [1, c.pick(p.x * 300)],
  2: (p, ts, c, i) => [0.05 + Math.exp(-(ts - events.flashes[i]) * 2.4), c.pick(p.x * 300 + ts * 30)],
  3: (p, ts, c, i) => [1 - 0.95 * Math.exp(-(ts - events.flashes[i]) * 2.4), c.pick(p.x * 300)],
  4: (p, ts, c) => [0.15 + 0.85 * Math.max(0, Math.sin(ts * 2.4 + p.phase * 6.28)) ** 6, c.pick(p.phase * 360)],
  5: (p, ts, c) => [0.06 + 0.94 * band(p.y - ts * 0.55 + fract(p.column * 0.37), 0.22), c.pick(p.x * 300)],
  6: (p, ts) => [1, hsl(p.phase * 360 + ts * 30)],
  7: (p, ts, c) => [0.06 + 0.94 * (0.5 - 0.5 * Math.cos(ts * 2.2)), c.pick(ts * 25)],
  8: (p, ts) => [1, hsl(ts * 45)],
  9: (p, ts, c) => {
    const d = Math.hypot((p.x - 0.5) * 2.4, p.y - 0.5);
    return [0.1 + 0.9 * band(d * 0.9 - ts * 0.45 * c.dir, 0.2), c.pick(d * 300 - ts * 60)];
  },
  10: (p, ts, c) => [0.1 + 0.9 * band(p.x * c.dir - ts * 0.32, 0.16), c.pick(p.x * 360)],
  11: (p, ts, c) => c.rainbow
    ? [1, hsl((p.x * c.dir - ts * 0.22) * 360)]
    : [0.18 + 0.82 * band(p.x * c.dir - ts * 0.3, 0.3), c.base],
  12: (p, ts, c) => {
    const a = Math.atan2(p.y - 0.5, (p.x - 0.5) * 2.4) / (Math.PI * 2);
    return c.rainbow ? [1, hsl((a * c.dir - ts * 0.22) * 360)] : [0.18 + 0.82 * band(a * c.dir - ts * 0.25, 0.2), c.base];
  },
  13: (p, ts, c) => [0.05 + 0.95 * rings(p, ts, 0.16), c.pick(p.x * 300 + ts * 40)],
  14: (p, ts, c) => [0.06 + 0.94 * band(1 - p.y - ts * 0.5 + fract(p.column * 0.29), 0.2), c.pick(p.x * 300)],
  15: (p, ts, c) => [0.12 + 0.88 * rings(p, ts, 0.09), c.pick(p.y * 200 + ts * 40)],
  16: (p, ts, c) => c.rainbow
    ? [1, hsl(((p.x + p.y * 0.5) * c.dir - ts * 0.22) * 360)]
    : [0.18 + 0.82 * band((p.x + p.y * 0.5) * c.dir - ts * 0.3, 0.25), c.base],
  17: (p, ts, c) => [0.08 + 0.92 * (0.5 + 0.5 * Math.sin(ts * 3)) ** 4, c.pick(ts * 30)],
  18: (p, ts, c) => [0.12 + 0.88 * band((p.x - p.y * 0.6) * c.dir - ts * 0.3, 0.2), c.pick(p.x * 300)],
  19: (p, ts, c) => {
    const pos = 1 - Math.abs(fract(ts * 0.18) * 2 - 1);
    return [0.06 + 0.94 * Math.max(0, 1 - Math.abs(p.x - pos) / 0.14), c.pick(p.x * 360)];
  },
};

const startedAt = performance.now();
let lastPaint = 0;

function paintBoards(now = performance.now()) {
  const effect = currentEffect();
  if (!effect || boards.length === 0) return;
  const { lighting } = state;
  const seconds = reduceMotion.matches ? 1.2 : (now - startedAt) / 1000;
  const ts = seconds * (0.35 + (lighting.speed / 5) * 1.4);
  const level = state.lightOn ? (lighting.brightness === 0 ? 0.1 : 0.3 + 0.7 * (lighting.brightness / 5)) : 0;
  const rainbow = !effect.color || (effect.rainbow && lighting.rainbow);
  const base = hexToRgb(lighting.color);
  const context = { base, rainbow, dir: lighting.reverse ? -1 : 1, pick: (h) => (rainbow ? hsl(h) : base) };
  const run = EFFECTS[effect.mode] ?? EFFECTS[1];
  // Abertura: as teclas acendem da esquerda para a direita no primeiro segundo
  const sweep = reduceMotion.matches ? 1 : Math.min(1, seconds / 0.9);
  spawnEvents(ts);

  for (let i = 0; i < KEYS.length; i++) {
    const p = keyPoints[i];
    const [raw, rgb] = run(p, ts, context, i);
    const power = Math.max(0, Math.min(1, (sweep * 1.3 - p.x) * 4));
    const intensity = Math.max(0, Math.min(1, raw)) * level * power;
    const color = `rgb(${rgb.map(Math.round).join(",")})`;
    const tint = 0.22 * intensity;
    const cap = `rgb(${CAP.map((v, k) => Math.round(v + (rgb[k] - v) * tint)).join(",")})`;
    for (const board of boards) {
      board.glows[i].setAttribute("fill", color);
      board.glows[i].setAttribute("opacity", board.lit ? intensity.toFixed(3) : "0");
      board.caps[i].setAttribute("fill", board.lit ? cap : `rgb(${CAP.join(",")})`);
    }
  }
}

function frame(now) {
  if (now - lastPaint >= 33) {
    paintBoards(now);
    lastPaint = now;
  }
  if (!reduceMotion.matches) requestAnimationFrame(frame);
}

reduceMotion.addEventListener("change", () => { if (!reduceMotion.matches) requestAnimationFrame(frame); });

// --- Início --------------------------------------------------------------------------

function renderAll() {
  renderEffects();
  renderLighting();
  renderSettings();
  renderDevice();
}

buildSwatches();
renderAll();
requestAnimationFrame(frame);
host.post({ type: "ready" });
