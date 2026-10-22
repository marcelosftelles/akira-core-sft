// Interface do AkiraCore. Fala com o C# por mensagens JSON (WebView2); toda regra de
// protocolo fica no AkiraCore.Core. Aqui só há estado de tela e a prévia do teclado.
import { VIEWBOX, BODY, KNOB, ZONES, KEYS, SCAN_CODES } from "./layout.js";
import {
  simulatedKeyColor, isAnimated, isReactive, APPROXIMATE_EFFECTS, PRESS_MEMORY_MS, MIRROR_STALE_MS,
} from "./preview.js";
import { createKeyboardPages } from "./keys.js";
import { createColorsPage } from "./colors.js";

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
  lighting: { effect: "rolling", color: "#FF0000", brightness: 5, speed: 3, direction: 0, rainbow: true },
  // Ajustes de cada efeito (t_light_data do oficial) e os semeados para efeito nunca mexido
  effectSettings: {},
  effectDefaults: { color: "#FF0000", brightness: 5, speed: 3, direction: 0, rainbow: true },
  lightOn: true,
  options: { swapRedBlue: false, simulate: false, startWithWindows: false, batteryAlert: true },
  device: { connected: false, channel: null, name: "", battery: { level: null, cable: false } },
  customActive: false, // um esquema de cores por tecla está no teclado no lugar do efeito
  profiles: { list: [], activeId: null },
  view: "lighting",
  loaded: false,
};

const $ = (id) => document.getElementById(id);

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
      state.effectSettings = message.effectSettings ?? {};
      state.effectDefaults = message.effectDefaults ?? state.effectDefaults;
      state.lightOn = message.lightOn;
      state.options = message.options;
      state.device = message.device;
      state.profiles = message.profiles ?? state.profiles;
      loadCustomLights(message.customLights);
      // Ao abrir, a página certa é a do que está no teclado
      if (!state.loaded && state.customActive) showView("colors");
      state.loaded = true;
      renderAll();
      if (message.keyboard) keyboardPages.load(message.keyboard);
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
    case "mirror":
      receiveMirror(message.colors ?? null);
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
    case "window":
      renderWindow(message);
      break;
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

// O efeito em uso guarda os próprios ajustes, como cada linha de t_light_data no oficial
function rememberEffect() {
  const { effect, ...settings } = state.lighting;
  state.effectSettings[effect] = settings;
}

let applyTimer;
function applySoon(delay = 180) {
  rememberEffect();
  clearTimeout(applyTimer);
  applyTimer = setTimeout(() => host.post({ type: "apply", lighting: { ...state.lighting } }), delay);
}

// --- Barra de título -----------------------------------------------------------

// Os botões mandam o mesmo comando dos nativos; o app responde com o estado da janela
for (const button of document.querySelectorAll("[data-window]")) {
  button.addEventListener("click", () => host.post({ type: "window", action: button.dataset.window }));
}

function renderWindow({ custom, maximized, active }) {
  const root = document.documentElement;
  root.dataset.frame = custom ? "custom" : "native";
  root.toggleAttribute("data-maximized", maximized);
  root.toggleAttribute("data-inactive", !active);
  const label = maximized ? "Restaurar" : "Maximizar";
  const button = $("caption-maximize");
  button.title = label;
  button.setAttribute("aria-label", label);
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
  // Escolher um efeito volta aos ajustes dele, como a lista do oficial (fcn.0042aa10)
  rememberEffect();
  state.lighting = { effect: key, ...(state.effectSettings[key] ?? state.effectDefaults) };
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
    releaseAllTouches();
  }
  state.view = view;
  // O app só lê os LEDs para a prévia com a página de iluminação aberta, como o oficial
  host.post({ type: "view", name: view });
  for (const section of document.querySelectorAll(".view")) {
    section.hidden = section.id !== `view-${view}`;
  }
  renderSelection();
  paintBoards();
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

// Como o oficial (fcn.0042aa10): o que o efeito não usa some da tela
function showControl(control, visible) {
  document.querySelectorAll(`#view-lighting [data-control="${control}"]`)
    .forEach((row) => { row.hidden = !visible; });
}

// A primeira linha visível de cada grupo não leva o separador de cima
function markFirstRows() {
  for (const group of document.querySelectorAll("#view-lighting .group")) {
    const first = [...group.querySelectorAll(":scope > .row")].find((row) => !row.hidden);
    for (const row of group.querySelectorAll(":scope > .row")) row.toggleAttribute("data-first", row === first);
  }
}

// Setas do oficial: valor gravado 0 esquerda, 1 direita, 2 cima, 3 baixo (fcn.0042c500)
const ARROWS = { 0: ["←", "Esquerda"], 1: ["→", "Direita"], 2: ["↑", "Para cima"], 3: ["↓", "Para baixo"] };

function renderDirections(effect) {
  const group = $("directions");
  const values = effect.directions ?? [];
  const wanted = values.join(",");
  if (group.dataset.values !== wanted) {
    group.dataset.values = wanted;
    group.replaceChildren(...values.map((value) => {
      const b = document.createElement("button");
      b.type = "button";
      b.setAttribute("role", "radio");
      b.dataset.direction = String(value);
      b.textContent = `${ARROWS[value][0]} ${ARROWS[value][1]}`;
      b.addEventListener("click", () => {
        state.lighting.direction = value;
        renderLighting();
        applySoon();
      });
      return b;
    }));
  }
  // Valor que não é de nenhuma seta (o 0 semeado no Rolagem) marca a seta de reserva
  const shown = values.includes(state.lighting.direction) ? state.lighting.direction : effect.directionFallback;
  for (const b of group.children) b.setAttribute("aria-checked", String(Number(b.dataset.direction) === shown));
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
  renderDirections(effect);
  renderSwatches();

  showControl("color", effect.color);
  showControl("rainbow", effect.rainbow);
  showControl("brightness", effect.brightness);
  showControl("speed", effect.speed);
  showControl("direction", effect.directions.length > 0);
  markFirstRows();
  // Cor continua clicável com o arco-íris ligado: escolher uma cor desliga o arco-íris,
  // como o painel de cor do oficial (fcn.0042bb00 desmarca o "Colorido")
  const off = !state.lightOn;
  for (const control of ["color", "rainbow", "brightness", "speed", "direction"]) setDisabled(control, off);
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

// --- Ajustes ---------------------------------------------------------------------

function renderSettings() {
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
    const row = document.createElement("div");
    row.className = "row";
    row.append(name, actions);
    return row;
  }));
  $("profile-new").disabled = list.length >= 20;
}

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
// Pelo cabo, espelho do que os LEDs mostram; sem leitura, o efeito simulado com as tabelas
// medidas no teclado (preview.js e effect-tables.js).

const SVG = "http://www.w3.org/2000/svg";
const CAP = [240, 240, 243];

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

// Último quadro lido do teclado (cor 0xRRGGBB por índice de LED) e quando chegou
const mirror = { colors: null, at: 0, timer: 0 };

// Relógio da simulação: o teclado recomeça o efeito a cada envio, a prévia também
const sim = { start: performance.now(), signature: "", frame: 0 };
const reduceMotion = matchMedia("(prefers-reduced-motion: reduce)");

function simulationTime(now) {
  const { effect, color, rainbow, speed, direction } = state.lighting;
  const signature = [effect, color, rainbow, speed, direction, state.lightOn].join("|");
  if (signature !== sim.signature) {
    sim.signature = signature;
    sim.start = now;
    // O teclado recomeça o efeito do zero: toques antigos não contam mais
    touches.list = [];
    touches.demoAt = 0;
  }
  return reduceMotion.matches ? 0 : now - sim.start;
}

// Toques para os efeitos que reagem a tecla, no relógio da simulação. Os do AKIRA chegam do
// app (Raw Input); sem ninguém digitando, a prévia toca teclas sozinha para não ficar escura.
const DEMO_AFTER_MS = 5000; // sem tecla de verdade nesse tempo, a demonstração volta
const DEMO_EVERY_MS = 1100;
const DEMO_HOLD_MS = 140;
const keyByScan = new Map(KEYS.map((k) => [SCAN_CODES[k.name], k]));
const touches = { list: [], cycles: new Map(), realAt: -Infinity, demoAt: 0 };

function addTouch(key, ms) {
  const open = touches.list.findLast((t) => t.led === key.led);
  // Repetição automática do Windows não é um toque novo
  if (open && open.up === null) return null;
  // Cada toque conta uma volta da tecla: com arco-íris, a cor muda a cada uma
  const cycle = (touches.cycles.get(key.led) ?? 0) + 1;
  touches.cycles.set(key.led, cycle);
  const touch = { led: key.led, x: key.x + key.w / 2, y: key.y + key.h / 2, at: ms, up: null, cycle };
  touches.list.push(touch);
  return touch;
}

function releaseTouches(ms, led = null) {
  for (const touch of touches.list) {
    if (touch.up === null && (led === null || touch.led === led)) touch.up = Math.max(ms, touch.at);
  }
}

function pressKey(scan, down) {
  const key = keyByScan.get(scan);
  if (!key || reduceMotion.matches) return;
  const now = performance.now();
  touches.realAt = now;
  const ms = simulationTime(now);
  // Soltar sempre conta; apertar só guarda toque nos efeitos que reagem a tecla
  if (!down) releaseTouches(ms, key.led);
  else if (isReactive(currentEffect())) addTouch(key, ms);
  pruneTouches(ms);
  paintBoards(now);
}

// Com a janela escondida ou outra página aberta o app para de escutar: uma tecla solta
// nesse meio tempo ficaria acesa para sempre
function releaseAllTouches() {
  releaseTouches(simulationTime(performance.now()));
}

function playDemo(now, ms) {
  for (const touch of touches.list) {
    if (touch.demo && touch.up === null && ms - touch.at >= DEMO_HOLD_MS) touch.up = touch.at + DEMO_HOLD_MS;
  }
  if (now - touches.realAt < DEMO_AFTER_MS || ms - touches.demoAt < DEMO_EVERY_MS) return;
  touches.demoAt = ms;
  const touch = addTouch(KEYS[Math.floor(Math.random() * KEYS.length)], ms);
  if (touch) touch.demo = true;
}

// Guarda só o que ainda muda o desenho e o último toque de cada tecla (a cor dela)
function pruneTouches(ms) {
  const last = new Map(touches.list.map((t, i) => [t.led, i]));
  touches.list = touches.list.filter((t, i) =>
    t.up === null || ms - t.up < PRESS_MEMORY_MS || last.get(t.led) === i);
}

function renderPreviewNote(effect, live) {
  const note = $("preview-note");
  let text = "";
  if (isReactive(effect) && !live && state.lightOn && !reduceMotion.matches) {
    text = "Toque teclas no AKIRA para ver o efeito aqui. Sem ninguém digitando, a prévia toca teclas sozinha.";
    if (APPROXIMATE_EFFECTS.has(effect.key)) text += " O desenho deste efeito ainda é aproximado.";
  }
  if (note.textContent !== text) note.textContent = text;
  note.hidden = !text;
}

function receiveMirror(colors) {
  mirror.colors = colors;
  mirror.at = performance.now();
  // Se os quadros pararem (cabo solto, envio longo), a prévia volta a ficar parada sozinha
  clearTimeout(mirror.timer);
  if (colors) mirror.timer = setTimeout(paintBoards, MIRROR_STALE_MS + 50);
  paintBoards();
}

function liveColors() {
  return mirror.colors && performance.now() - mirror.at < MIRROR_STALE_MS ? mirror.colors : null;
}

// Cor da tecla agora, [r, g, b] de 0 a 255
function keyColor(key, effect, live, ms) {
  if (!state.lightOn) return [0, 0, 0];
  if (live) {
    const n = live[key.led] ?? 0;
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  }
  return simulatedKeyColor(key, effect, state.lighting, ms, touches.list);
}

// Quadros novos só quando algo mexe: efeito animado ou que reage a tecla, sem espelho, na
// página de iluminação
function wantsAnimation(effect, live) {
  return state.lightOn && !live && !reduceMotion.matches && !document.hidden
    && state.view === "lighting" && (isAnimated(effect, state.lighting) || isReactive(effect));
}

function paintBoards(now = performance.now()) {
  const effect = currentEffect();
  if (!effect || boards.length === 0) return;
  const live = liveColors();
  const ms = simulationTime(now);
  renderPreviewNote(effect, live);
  const animating = wantsAnimation(effect, live);
  if (animating && isReactive(effect)) {
    playDemo(now, ms);
    pruneTouches(ms);
  }
  if (animating && !sim.frame) {
    sim.frame = requestAnimationFrame((t) => { sim.frame = 0; paintBoards(t); });
  }
  for (let i = 0; i < KEYS.length; i++) {
    const rgb = keyColor(KEYS[i], effect, live, ms);
    // O brilho do desenho vem da própria cor: LED apagado some, LED fraco brilha pouco
    const peak = Math.max(...rgb);
    const intensity = peak / 255;
    const pure = peak ? rgb.map((v) => Math.round((v / peak) * 255)) : CAP;
    const color = `rgb(${pure.join(",")})`;
    const tint = 0.22 * intensity;
    const cap = `rgb(${CAP.map((v, k) => Math.round(v + (pure[k] - v) * tint)).join(",")})`;
    for (const board of boards) {
      board.glows[i].setAttribute("fill", color);
      board.glows[i].setAttribute("opacity", board.lit ? intensity.toFixed(3) : "0");
      board.caps[i].setAttribute("fill", board.lit ? cap : `rgb(${CAP.join(",")})`);
    }
  }
}

document.addEventListener("visibilitychange", () => {
  if (document.hidden) releaseAllTouches();
  paintBoards();
});
reduceMotion.addEventListener("change", () => paintBoards());

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
host.post({ type: "ready" });
