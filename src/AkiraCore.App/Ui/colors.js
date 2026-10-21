// Página Cores por tecla: pintar cada tecla e guardar esquemas ("Iluminação do usuário" do
// oficial). Só estado de tela: a validação e os bytes ficam no Core (CustomLightState).
// Mensagens: saveCustomLights {schemes}, useCustomLight {id}, exportColors {id}, importColors.
import { VIEWBOX, BODY, KNOB, KEYS } from "./layout.js";

const SVG = "http://www.w3.org/2000/svg";
const OFF = "#000000";
const BRUSHES = [
  ["#FF0000", "Vermelho"], ["#FF6A00", "Laranja"], ["#FFD000", "Amarelo"],
  ["#00FF40", "Verde"], ["#00E5FF", "Ciano"], ["#0040FF", "Azul"],
  ["#8A2BE2", "Roxo"], ["#FF2D95", "Rosa"], ["#FFFFFF", "Branco"], [OFF, "Apagada"],
];
// Grupos para pintar de uma vez, pelos nomes do layout.js
const GROUPS = [
  ["Todas", () => true],
  ["Letras", (k) => /^[A-Z]$/.test(k.name)],
  ["Números", (k) => k.name.length === 2 && /\d/.test(k.name[1])],
  ["F1 a F12", (k) => /^F\d+$/.test(k.name)],
  ["WASD", (k) => ["W", "A", "S", "D"].includes(k.name)],
  ["Setas", (k) => ["↑", "←", "↓", "→"].includes(k.name)],
];

const $ = (id) => document.getElementById(id);

function svg(tag, attrs, parent) {
  const el = document.createElementNS(SVG, tag);
  for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
  parent?.append(el);
  return el;
}

function newId() {
  return crypto.randomUUID?.().replaceAll("-", "") ?? `c${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`;
}

export function createColorsPage(host, { onActivate }) {
  const cs = {
    schemes: [],
    activeId: null,
    selectedId: null,
    brush: "#FF0000",
    painting: false,
    changed: false,
  };
  const current = () => cs.schemes.find((s) => s.id === cs.selectedId);

  // --- Teclado pintável ------------------------------------------------------------

  const caps = new Map();
  const board = svg("svg", {
    class: "board paint",
    viewBox: `${VIEWBOX.x} ${VIEWBOX.y} ${VIEWBOX.width} ${VIEWBOX.height}`,
    role: "group",
    "aria-label": "Teclado: clique ou arraste para pintar as teclas",
  });
  const defs = svg("defs", {}, board);
  const filter = svg("filter", { id: "paint-glow", x: "-20%", y: "-20%", width: "140%", height: "140%" }, defs);
  svg("feGaussianBlur", { stdDeviation: "4" }, filter);
  svg("rect", { class: "body", x: BODY.x, y: BODY.y, width: BODY.width, height: BODY.height, rx: BODY.radius }, board);
  const glowLayer = svg("g", { filter: "url(#paint-glow)" }, board);
  for (const k of KEYS) {
    const glow = svg("rect", { x: k.x - 3.5, y: k.y - 3.5, width: k.w + 7, height: k.h + 7, rx: 7, opacity: 0 }, glowLayer);
    const g = svg("g", { class: "cap", tabindex: "0", role: "button", "data-key": k.led, "aria-label": k.name }, board);
    const rect = svg("rect", { x: k.x + 1, y: k.y + 1, width: k.w - 2, height: k.h - 2, rx: 4.5 }, g);
    const title = svg("title", {}, g);
    g.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") { e.preventDefault(); paint(k.led); commit(); }
    });
    caps.set(k.led, { g, rect, glow, title, name: k.name });
  }
  svg("circle", { class: "knob", cx: KNOB.cx, cy: KNOB.cy, r: KNOB.r }, board);
  svg("circle", { class: "knob-cap", cx: KNOB.cx, cy: KNOB.cy, r: KNOB.r - 4 }, board);
  $("color-board").append(board);

  const keyOf = (target) => Number(target.closest?.("[data-key]")?.dataset.key) || null;

  // Botão direito pega a cor da tecla; esquerdo pinta, e arrastar pinta por onde passar
  board.addEventListener("pointerdown", (e) => {
    const key = keyOf(e.target);
    if (!key || !current()) return;
    if (e.button === 2) {
      setBrush(current().colors[key] ?? OFF);
      return;
    }
    if (e.button !== 0) return;
    cs.painting = true;
    board.setPointerCapture(e.pointerId);
    paint(key);
  });
  board.addEventListener("pointermove", (e) => {
    if (!cs.painting) return;
    // Com o ponteiro capturado, o alvo é o próprio svg: procura a tecla sob o ponteiro
    const key = keyOf(document.elementFromPoint(e.clientX, e.clientY) ?? e.target);
    if (key) paint(key);
  });
  const stop = () => {
    if (!cs.painting) return;
    cs.painting = false;
    commit();
  };
  board.addEventListener("pointerup", stop);
  board.addEventListener("pointercancel", stop);
  board.addEventListener("contextmenu", (e) => e.preventDefault());

  function paint(key) {
    const scheme = current();
    if (!scheme) return;
    const color = cs.brush.toUpperCase();
    const before = scheme.colors[key] ?? OFF;
    if (before === color) return;
    if (color === OFF) delete scheme.colors[key];
    else scheme.colors[key] = color;
    cs.changed = true;
    renderBoard();
  }

  function renderBoard() {
    const scheme = current();
    const level = scheme ? (scheme.brightness === 0 ? 0.12 : 0.35 + 0.65 * (scheme.brightness / 5)) : 0;
    for (const [key, cap] of caps) {
      const color = scheme?.colors[key] ?? OFF;
      const lit = color !== OFF;
      cap.rect.setAttribute("fill", lit ? mix(color, level) : "");
      cap.g.dataset.off = String(!lit);
      cap.glow.setAttribute("fill", color);
      cap.glow.setAttribute("opacity", lit ? (0.85 * level).toFixed(3) : "0");
      cap.title.textContent = `${cap.name}: ${lit ? color : "apagada"}`;
    }
  }

  // Cor da tecla escurecida pelo brilho, para a prévia parecer o teclado
  function mix(hex, level) {
    const n = parseInt(hex.slice(1), 16);
    const channel = (shift) => Math.round(((n >> shift) & 255) * (0.25 + 0.75 * level));
    return `rgb(${channel(16)},${channel(8)},${channel(0)})`;
  }

  // --- Pincel e grupos --------------------------------------------------------------

  const brushGroup = $("brush-swatches");
  for (const [hex, name] of BRUSHES) {
    const swatch = document.createElement("button");
    swatch.type = "button";
    swatch.className = `swatch${hex === OFF ? " off" : ""}`;
    swatch.setAttribute("role", "radio");
    swatch.setAttribute("aria-label", name);
    swatch.title = name;
    swatch.dataset.color = hex;
    swatch.style.setProperty("--c", hex);
    swatch.addEventListener("click", () => setBrush(hex));
    brushGroup.append(swatch);
  }
  const custom = document.createElement("span");
  custom.className = "swatch custom";
  custom.setAttribute("role", "radio");
  custom.setAttribute("aria-label", "Outra cor");
  const picker = document.createElement("input");
  picker.type = "color";
  picker.setAttribute("aria-label", "Escolher outra cor para o pincel");
  picker.addEventListener("input", () => setBrush(picker.value.toUpperCase()));
  custom.append(picker);
  brushGroup.append(custom);

  function setBrush(hex) {
    cs.brush = hex.toUpperCase();
    renderBrush();
  }

  function renderBrush() {
    let matched = false;
    for (const swatch of brushGroup.querySelectorAll(".swatch:not(.custom)")) {
      const on = swatch.dataset.color === cs.brush;
      matched ||= on;
      swatch.setAttribute("aria-checked", String(on));
    }
    custom.setAttribute("aria-checked", String(!matched));
    if (matched) {
      delete custom.dataset.color;
    } else {
      custom.dataset.color = cs.brush;
      custom.style.setProperty("--c", cs.brush);
      picker.value = cs.brush.toLowerCase();
    }
  }

  $("color-groups").replaceChildren(...GROUPS.map(([label, test]) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "chip";
    button.textContent = label;
    button.addEventListener("click", () => {
      for (const k of KEYS.filter(test)) paint(k.led);
      commit();
    });
    return button;
  }));

  // --- Brilho, nome e lista -----------------------------------------------------------

  const brightness = $("scheme-brightness");
  brightness.addEventListener("input", () => {
    const scheme = current();
    if (!scheme) return;
    scheme.brightness = Number(brightness.value);
    cs.changed = true;
    renderControls();
    renderBoard();
  });
  brightness.addEventListener("change", () => commit());

  $("scheme-name").addEventListener("input", (e) => {
    const scheme = current();
    if (!scheme) return;
    scheme.name = e.target.value.slice(0, 40);
    renderList();
  });
  $("scheme-name").addEventListener("change", () => {
    const scheme = current();
    if (!scheme) return;
    scheme.name = scheme.name.trim() || "Sem nome";
    $("scheme-name").value = scheme.name;
    renderList();
    save();
  });

  $("scheme-select").addEventListener("change", (e) => {
    cs.selectedId = e.target.value;
    render();
    activate();
  });

  function uniqueName(base) {
    let name = base;
    for (let n = 2; cs.schemes.some((s) => s.name === name); n++) name = `${base} ${n}`;
    return name;
  }

  function addScheme(scheme) {
    if (cs.schemes.length >= 50) return;
    cs.schemes.push(scheme);
    cs.selectedId = scheme.id;
    render();
    save();
    activate();
  }

  // Esquema novo: todas as teclas brancas, para pintar por cima
  const blank = () => Object.fromEntries(KEYS.map((k) => [k.led, "#FFFFFF"]));
  $("scheme-new").addEventListener("click", () =>
    addScheme({ id: newId(), name: uniqueName("Cores"), brightness: 5, colors: blank() }));
  $("scheme-first").addEventListener("click", () =>
    addScheme({ id: newId(), name: uniqueName("Cores"), brightness: 5, colors: blank() }));
  $("scheme-duplicate").addEventListener("click", () => {
    const scheme = current();
    if (scheme) addScheme({ ...structuredClone(scheme), id: newId(), name: uniqueName(`${scheme.name} cópia`.slice(0, 40)) });
  });
  $("scheme-delete").addEventListener("click", () => {
    const scheme = current();
    if (!scheme) return;
    cs.schemes = cs.schemes.filter((s) => s !== scheme);
    if (cs.activeId === scheme.id) {
      cs.activeId = null;
      onActivate(null);
    }
    cs.selectedId = cs.schemes[0]?.id ?? null;
    render();
    save();
  });
  $("scheme-use").addEventListener("click", () => activate());
  $("scheme-export").addEventListener("click", () => {
    if (current()) host.post({ type: "exportColors", id: cs.selectedId });
  });
  for (const button of document.querySelectorAll("[data-import-colors]")) {
    button.addEventListener("click", () => host.post({ type: "importColors" }));
  }

  // --- Envio --------------------------------------------------------------------------

  function save() {
    host.post({ type: "saveCustomLights", schemes: cs.schemes });
  }

  // O que se pinta vai para o teclado: pintar um esquema que não está em uso passa a usá-lo
  function commit() {
    if (!cs.changed) return;
    cs.changed = false;
    save();
    activate();
  }

  function activate() {
    if (!current() || cs.activeId === cs.selectedId) {
      renderControls();
      return;
    }
    cs.activeId = cs.selectedId;
    host.post({ type: "useCustomLight", id: cs.activeId });
    onActivate(cs.activeId);
    renderControls();
  }

  // --- Renderização --------------------------------------------------------------------

  function renderList() {
    const select = $("scheme-select");
    select.replaceChildren(...cs.schemes.map((s) => {
      const option = document.createElement("option");
      option.value = s.id;
      option.textContent = s.name || "Sem nome";
      option.selected = s.id === cs.selectedId;
      return option;
    }));
  }

  function renderControls() {
    const scheme = current();
    const inUse = Boolean(scheme) && scheme.id === cs.activeId;
    $("scheme-state").textContent = inUse ? "No teclado agora" : "Um efeito está no teclado";
    $("scheme-use").hidden = !scheme || inUse;
    if (scheme) {
      brightness.value = scheme.brightness;
      brightness.style.setProperty("--p", `${(scheme.brightness / 5) * 100}%`);
      if (document.activeElement !== $("scheme-name")) $("scheme-name").value = scheme.name;
    }
    $("scheme-duplicate").disabled = !scheme || cs.schemes.length >= 50;
    $("scheme-new").disabled = cs.schemes.length >= 50;
  }

  function render() {
    const has = Boolean(current());
    $("colors-empty").hidden = has;
    for (const id of ["colors-editor", "color-board", "colors-tools", "colors-manage"]) $(id).hidden = !has;
    renderList();
    renderControls();
    renderBoard();
  }

  setBrush(cs.brush);

  return {
    /** Recebe o bloco customLights do app (state ou customLights). */
    load(customLights) {
      if (cs.painting) return;
      cs.schemes = structuredClone(customLights?.schemes ?? []);
      cs.activeId = customLights?.activeId ?? null;
      if (!cs.schemes.some((s) => s.id === cs.selectedId)) {
        cs.selectedId = cs.activeId ?? cs.schemes[0]?.id ?? null;
      } else if (cs.activeId) {
        cs.selectedId = cs.activeId;
      }
      render();
    },
    /** Um efeito foi escolhido: o esquema deixa de estar no teclado. */
    deactivate() {
      cs.activeId = null;
      renderControls();
    },
    activeId: () => cs.activeId,
  };
}
