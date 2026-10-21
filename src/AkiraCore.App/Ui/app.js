// Interface do AkiraCore. Fala com o C# por mensagens JSON (WebView2); toda regra de
// protocolo fica no AkiraCore.Core. Aqui só há estado de tela e a prévia do teclado.
import { VIEWBOX, BODY, KNOB, ZONES, KEYS, SCAN_CODES } from "./layout.js";
import { createKeyboardPages } from "./keys.js";
import { createColorsPage } from "./colors.js";
import { createLivePage } from "./live.js";
import { createAutomationSettings } from "./automation.js";

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
  options: { swapRedBlue: false, simulate: false, startWithWindows: false, batteryAlert: true },
  device: { connected: false, channel: null, name: "", battery: { level: null, cable: false } },
  customActive: false, // um esquema de cores por tecla está no teclado no lugar do efeito
  profiles: { list: [], activeId: null, auto: { enabled: false, fallbackId: null } },
  mirror: false,          // prévia lendo as cores reais (04 F5)
  mirrorColors: null,     // índice do LED → #RRGGBB, da última leitura
  view: "lighting",
  loaded: false,
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
      state.profiles = message.profiles ?? state.profiles;
      state.mirror = Boolean(message.mirror);
      loadCustomLights(message.customLights);
      // Ao abrir, a página certa é a do que está no teclado
      if (!state.loaded && state.customActive) showView("colors");
      state.loaded = true;
      renderAll();
      if (message.keyboard) keyboardPages.load(message.keyboard);
      livePage.device(state.device);
      livePage.load(message.live);
      automation.load(message.automation);
      break;
    case "device":
      state.device = message.device;
      renderDevice();
      livePage.device(state.device);
      break;
    case "liveFrame":
      livePage.frame(message.colors);
      break;
    case "liveStatus":
      livePage.status(message.status);
      break;
    case "mirror":
      state.mirrorColors = message.colors;
      renderMirrorHint(message.problem);
      paintBoards();
      break;
    case "automation":
      automation.load(message.automation);
      break;
    case "filePicked":
      keyboardPages.filePicked(message.purpose, message.path);
      break;
    case "status":
      showStatus(message.kind, message.text);
      break;
    case "log":
      appendLog(message.title, message.lines);
      break;
    case "key":
      pressKey(message.scan, message.down);
      break;
    case "customLights":
      loadCustomLights(message.customLights);
      renderSelection();
      break;
    case "profiles":
      state.profiles = message.profiles;
      renderProfiles();
      break;
    case "macroText": {
      keyboardPages.appendEvents(message.events);
      const where = `no layout ${message.layout}`;
      if (message.skipped) showStatus("error", `Ficaram de fora (${where}): ${message.skipped}`);
      else showStatus("ok", `Texto inserido, ${where}`);
      break;
    }
    case "macrosImported": {
      const added = keyboardPages.importMacros(message.macros);
      showStatus(added === message.macros.length ? "ok" : "error",
        added === message.macros.length
          ? `${added} macro${added === 1 ? "" : "s"} importada${added === 1 ? "" : "s"}. Grave no teclado para usar`
          : `Só ${added} de ${message.macros.length} macros cabem na lista`);
      break;
    }
  }
});

function loadCustomLights(customLights) {
  if (!customLights) return;
  state.customActive = Boolean(customLights.activeId);
  colorsPage.load(customLights);
}

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
    // Com cores por tecla no teclado, nenhum efeito está em uso
    const chosen = button.dataset.key === state.lighting.effect && !state.customActive;
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
  state.customActive = false;
  colorsPage.deactivate();
  showView("lighting");
  renderLighting();
  revealChosenEffect();
  applySoon(0);
}

for (const button of document.querySelectorAll(".nav-bottom .nav-item")) {
  button.addEventListener("click", () => showView(button.dataset.view));
}

// Teclas, Macros e o grupo Teclado de Ajustes ficam em keys.js
const keyboardPages = createKeyboardPages(host);
keyboardPages.onNavigate(showView);

// Ao vivo fica em live.js; regras de luz e atalhos globais em automation.js
const livePage = createLivePage(host);
const automation = createAutomationSettings(host);

// Cores por tecla fica em colors.js; usar um esquema liga a luz e tira o efeito da seleção
const colorsPage = createColorsPage(host, {
  onActivate(id) {
    state.customActive = Boolean(id);
    if (id) state.lightOn = true;
    renderLighting();
  },
});

function showView(view) {
  if (view !== state.view) {
    keyboardPages.leave();
    automation.leave();
  }
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
  $("brand-board").dataset.connected = String(connected || state.options.simulate);
  renderBattery();
}

// O nível vem do dongle (o mesmo pedido do app oficial); carregando = cabo ligado no PC
function renderBattery() {
  const battery = $("battery");
  const { level = null, cable = false } = state.device.battery ?? {};
  const known = Number.isInteger(level);
  battery.hidden = state.options.simulate || (!known && !cable);
  if (battery.hidden) {
    return;
  }
  // Só o ícone e o número; o texto completo fica no tooltip e para leitores de tela
  battery.style.setProperty("--level", known ? `${level}%` : "0%");
  battery.toggleAttribute("data-charging", cable);
  battery.dataset.level = !known ? "unknown" : level > 70 ? "high" : level > 20 ? "mid" : "low";
  $("battery-text").textContent = known ? `${level}%` : "";
  const full = known ? `Bateria ${level}%${cable ? ", carregando" : ""}` : "Bateria carregando";
  battery.setAttribute("aria-label", full);
  battery.title = known ? full : `${full}. O nível só é informado pelo dongle 2.4G`;
}

// Teclado da barra lateral: só o contorno das teclas, do mesmo layout da prévia
function buildBrandBoard() {
  const root = el("svg", {
    viewBox: `${BODY.x - 2} ${BODY.y - 2} ${BODY.width + 4} ${BODY.height + 4}`,
    role: "presentation",
  });
  el("rect", { class: "outline", x: BODY.x, y: BODY.y, width: BODY.width, height: BODY.height, rx: BODY.radius }, root);
  const keys = el("g", { class: "keys" }, root);
  for (const k of KEYS) el("rect", { x: k.x + 2, y: k.y + 2, width: k.w - 4, height: k.h - 4, rx: 4 }, keys);
  el("circle", { class: "keys", cx: KNOB.cx, cy: KNOB.cy, r: KNOB.r - 2 }, root);
  $("brand-board").append(root);
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
  document.querySelectorAll(".light-switch").forEach((input) => { input.checked = state.lightOn; });
  $("rainbow").checked = lighting.rainbow;
  renderRange($("brightness"), lighting.brightness);
  renderRange($("speed"), lighting.speed);
  for (const button of document.querySelectorAll("#view-lighting .segmented button")) {
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

// Uma chave de luz em cada página de iluminação; o app reaplica o que estiver em uso
for (const input of document.querySelectorAll(".light-switch")) {
  input.addEventListener("change", (e) => {
    state.lightOn = e.target.checked;
    renderLighting();
    host.post({ type: "setLight", on: state.lightOn, lighting: { ...state.lighting } });
  });
}

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

for (const button of document.querySelectorAll("#view-lighting .segmented button")) {
  button.addEventListener("click", () => {
    state.lighting.reverse = button.dataset.reverse === "true";
    renderLighting();
    applySoon();
  });
}

// --- Ajustes ---------------------------------------------------------------------

function renderSettings() {
  $("mirror").checked = state.mirror;
  renderMirrorHint();
  $("swap").checked = state.options.swapRedBlue;
  $("simulate").checked = state.options.simulate;
  $("autostart").checked = state.options.startWithWindows;
  $("battery-alert").checked = state.options.batteryAlert;
  renderProfiles();
}

const OPTION_KEYS = {
  swap: "swapRedBlue", simulate: "simulate", autostart: "startWithWindows", "battery-alert": "batteryAlert",
};
for (const [id, option] of Object.entries(OPTION_KEYS)) {
  $(id).addEventListener("change", (e) => {
    state.options[option] = e.target.checked;
    renderDevice();
    host.post({ type: "setOption", key: option, value: e.target.checked });
  });
}

// Espelho: a prévia passa a pintar as cores lidas do teclado (só cabo; o app avisa o motivo se não der)
$("mirror").addEventListener("change", (e) => {
  state.mirror = e.target.checked;
  if (!state.mirror) state.mirrorColors = null;
  renderMirrorHint();
  host.post({ type: "setMirror", on: state.mirror });
});

function renderMirrorHint(problem) {
  const hint = $("mirror-hint");
  hint.textContent = state.mirror && problem
    ? problem
    : state.mirror && state.mirrorColors
      ? "Mostrando as cores lidas do teclado agora"
      : "A prévia lê as cores que os LEDs mostram agora. Só pelo cabo";
  hint.dataset.kind = state.mirror && problem ? "warn" : "";
}

$("diagnose").addEventListener("click", () => host.post({ type: "diagnose" }));
$("open-lab").addEventListener("click", () => host.post({ type: "openLab" }));

// Alerta de confirmação: um só diálogo, com título, texto e ação de cada pedido
const confirmDialog = $("confirm");
let confirmed = null;
function confirmAction({ title, text, action, onConfirm }) {
  $("confirm-title").textContent = title;
  $("confirm-text").textContent = text;
  $("confirm-action").textContent = action;
  confirmed = onConfirm;
  confirmDialog.returnValue = "";
  confirmDialog.showModal();
}
confirmDialog.addEventListener("close", () => {
  if (confirmDialog.returnValue === "confirm") confirmed?.();
  confirmed = null;
});

$("reset").addEventListener("click", () => confirmAction({
  title: "Restaurar o padrão de fábrica?",
  text: state.device.channel === "dongle"
    ? "Pelo dongle, o teclado recebe o mesmo reset do app oficial: teclas mudadas, macros, " +
      "configurações e iluminação guardadas nele voltam ao padrão."
    : "Pelo cabo, como no app oficial, o app regrava os padrões: teclas originais, nenhuma macro, " +
      "configurações do teclado zeradas e iluminação Rolando. As fitas continuam nos atalhos.",
  action: "Restaurar",
  onConfirm: () => host.post({ type: "factoryReset" }),
}));

$("backup-export").addEventListener("click", () => host.post({ type: "exportBackup" }));
$("backup-import").addEventListener("click", () => confirmAction({
  title: "Importar um backup?",
  text: "Iluminação, cores por tecla, teclas, macros, configurações e perfis do app são trocados " +
    "pelos do arquivo, e o teclado é regravado. Exporte um backup antes se quiser voltar depois.",
  action: "Escolher arquivo…",
  onConfirm: () => host.post({ type: "importBackup" }),
}));

// --- Perfis ------------------------------------------------------------------------

function renderProfiles() {
  const { list, activeId } = state.profiles;
  const select = $("profile-select");
  select.replaceChildren(...list.map((p) => {
    const option = document.createElement("option");
    option.value = p.id;
    option.textContent = p.name;
    option.selected = p.id === activeId;
    return option;
  }));
  select.closest(".profile-picker").hidden = list.length === 0;

  $("profile-list").replaceChildren(...list.map((p) => {
    const name = document.createElement("input");
    name.type = "text";
    name.className = "text";
    name.maxLength = 40;
    name.spellcheck = false;
    name.value = p.name;
    name.setAttribute("aria-label", `Nome do perfil ${p.name}`);
    name.addEventListener("change", () => host.post({ type: "renameProfile", id: p.id, name: name.value }));
    const actions = document.createElement("span");
    actions.className = "inline";
    if (p.id === activeId) {
      const badge = document.createElement("span");
      badge.className = "badge";
      badge.textContent = "Em uso";
      actions.append(badge);
    } else {
      const use = button("Usar", () => host.post({ type: "switchProfile", id: p.id }));
      const remove = button("Apagar", () => confirmAction({
        title: `Apagar o perfil ${p.name}?`,
        text: "As teclas, a iluminação e as configurações guardadas nele somem. Macros e esquemas de cores continuam.",
        action: "Apagar",
        onConfirm: () => host.post({ type: "deleteProfile", id: p.id }),
      }));
      remove.classList.add("destructive");
      actions.append(use, remove);
    }
    const apps = document.createElement("input");
    apps.type = "text";
    apps.className = "text apps";
    apps.spellcheck = false;
    apps.placeholder = "Programas, ex.: valorant.exe, cs2.exe";
    apps.value = (p.apps ?? []).join(", ");
    apps.setAttribute("aria-label", `Programas que trocam para o perfil ${p.name}`);
    apps.addEventListener("change", () => host.post({
      type: "setProfileApps",
      id: p.id,
      apps: apps.value.split(/[,;\n]/).map((a) => a.trim()).filter(Boolean),
    }));
    const fields = document.createElement("span");
    fields.className = "profile-fields";
    fields.append(name, apps);
    const row = document.createElement("div");
    row.className = "row";
    row.append(fields, actions);
    return row;
  }));
  $("profile-new").disabled = list.length >= 20;

  const auto = state.profiles.auto ?? { enabled: false, fallbackId: null };
  $("auto-profile").checked = auto.enabled;
  const fallback = $("auto-fallback");
  fallback.replaceChildren(new Option("Manter o perfil atual", ""),
    ...list.map((p) => new Option(p.name, p.id, false, p.id === auto.fallbackId)));
  fallback.value = auto.fallbackId ?? "";
  fallback.closest(".row").setAttribute("aria-disabled", String(!auto.enabled));
}

function sendAutoProfile() {
  const auto = { enabled: $("auto-profile").checked, fallbackId: $("auto-fallback").value || null };
  state.profiles.auto = auto;
  renderProfiles();
  host.post({ type: "setAutoProfile", auto });
}
$("auto-profile").addEventListener("change", sendAutoProfile);
$("auto-fallback").addEventListener("change", sendAutoProfile);

function button(text, onClick) {
  const b = document.createElement("button");
  b.type = "button";
  b.className = "button";
  b.textContent = text;
  b.addEventListener("click", onClick);
  return b;
}

$("profile-select").addEventListener("change", (e) => host.post({ type: "switchProfile", id: e.target.value }));
$("profile-new").addEventListener("click", () => {
  const name = $("profile-name").value.trim();
  if (!name) {
    $("profile-name").focus();
    return;
  }
  $("profile-name").value = "";
  host.post({ type: "createProfile", name });
});
$("profile-name").addEventListener("keydown", (e) => { if (e.key === "Enter") $("profile-new").click(); });

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

// Teclas tocadas para os efeitos reativos. As do AKIRA chegam do app (Raw Input) e
// acendem a tecla certa na prévia; sem ninguém digitando, a prévia toca teclas sozinha.
const DEMO_AFTER = 5000; // ms sem tecla real até voltar a demonstração
const keyByScan = new Map(KEYS.map((k, i) => [SCAN_CODES[k.name], i]));
const events = {
  flashes: new Float32Array(KEYS.length).fill(-99),
  bursts: [],
  pressed: new Set(),
  last: 0,
  realAt: -Infinity,
  ts: 0, // tempo do efeito no último quadro, para datar as teclas reais
};

function addPress(i, ts) {
  events.flashes[i] = ts;
  const p = keyPoints[i];
  events.bursts.push({ x: p.x, y: p.y, t: ts });
  if (events.bursts.length > 12) events.bursts.shift();
}

function pressKey(scan, down) {
  const i = keyByScan.get(scan);
  if (i === undefined) return;
  events.realAt = performance.now();
  if (!down) {
    events.pressed.delete(i);
    return;
  }
  // Repetição automática do Windows não é um novo toque
  if (events.pressed.has(i)) return;
  events.pressed.add(i);
  addPress(i, events.ts);
}

// Escondido, o app para de escutar: uma tecla solta nesse meio tempo ficaria presa
document.addEventListener("visibilitychange", () => events.pressed.clear());

function spawnEvents(ts, now) {
  if (now - events.realAt < DEMO_AFTER || ts - events.last < 0.42) return;
  events.last = ts;
  addPress(Math.floor(Math.random() * KEYS.length), ts);
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
  // Rolagem é o único com o par de direções alternativo (config_func 0x08): vertical
  10: (p, ts, c) => [0.1 + 0.9 * band(p.y * c.dir - ts * 0.32, 0.16), c.pick(p.y * 360)],
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
// Tempo do efeito: acumulado quadro a quadro, para mudar a velocidade sem saltar a animação
const clock = { ts: 0, at: startedAt };

function effectTime(now, speed) {
  if (reduceMotion.matches) return 1.2;
  const dt = Math.min(0.1, Math.max(0, (now - clock.at) / 1000));
  clock.at = now;
  clock.ts += dt * (0.35 + (speed / 5) * 1.4);
  return clock.ts;
}

function paintBoards(now = performance.now()) {
  const effect = currentEffect();
  if (!effect || boards.length === 0) return;
  const { lighting } = state;
  const seconds = (now - startedAt) / 1000;
  const ts = effectTime(now, lighting.speed);
  events.ts = ts;
  const level = state.lightOn ? (lighting.brightness === 0 ? 0.1 : 0.3 + 0.7 * (lighting.brightness / 5)) : 0;
  const rainbow = !effect.color || (effect.rainbow && lighting.rainbow);
  const base = hexToRgb(lighting.color);
  const context = { base, rainbow, dir: lighting.reverse ? -1 : 1, pick: (h) => (rainbow ? hsl(h) : base) };
  const run = EFFECTS[effect.mode] ?? EFFECTS[1];
  // Espelho ligado e com leitura: as cores vêm do teclado, não da simulação
  const mirror = state.mirror && state.mirrorColors;
  // Abertura: as teclas acendem da esquerda para a direita no primeiro segundo
  const sweep = reduceMotion.matches ? 1 : Math.min(1, seconds / 0.9);
  if (!reduceMotion.matches) spawnEvents(ts, now);

  for (let i = 0; i < KEYS.length; i++) {
    const p = keyPoints[i];
    let [raw, rgb] = run(p, ts, context, i);
    let power = Math.max(0, Math.min(1, (sweep * 1.3 - p.x) * 4));
    let gain = level;
    if (mirror) {
      const real = hexToRgb(state.mirrorColors[KEYS[i].led] ?? "#000000");
      const peak = Math.max(...real);
      raw = peak / 255;
      rgb = peak > 0 ? real.map((c) => (c * 255) / peak) : [0, 0, 0];
      power = 1;
      gain = 1;
    }
    const intensity = Math.max(0, Math.min(1, raw)) * gain * power;
    const color = `rgb(${rgb.map(Math.round).join(",")})`;
    const tint = 0.22 * intensity;
    // Tecla segurada no AKIRA fica um pouco mais escura, como se afundasse
    const shade = events.pressed.has(i) ? 0.84 : 1;
    const cap = `rgb(${CAP.map((v, k) => Math.round((v + (rgb[k] - v) * tint) * shade)).join(",")})`;
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
buildBrandBoard();
renderAll();
requestAnimationFrame(frame);
host.post({ type: "ready" });
