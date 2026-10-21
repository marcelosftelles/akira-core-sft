// Telas de Teclas, Macros e do grupo Teclado em Ajustes. Só estado de tela: a validação e os
// bytes ficam no AkiraCore.Core (KeysState.cs, AppKeyActions.cs). Mensagens: setKeys {assignments},
// saveMacros {macros, assignments}, setDevice {settings}, pickFile {purpose}.
import { VIEWBOX, BODY, KEYS } from "./layout.js";

const SVG = "http://www.w3.org/2000/svg";
const MOD_BITS = { ctrl: 0x01, shift: 0x02, alt: 0x04, win: 0x08 };
const MOD_NAMES = [[0x01, "Ctrl"], [0x02, "Shift"], [0x04, "Alt"], [0x08, "Win"],
  [0x10, "Ctrl dir."], [0x20, "Shift dir."], [0x40, "Alt dir."], [0x80, "Win dir."]];
// "Atalho do Windows" do app oficial (tipo 7 em fcn.00418550): modificadores e código HID
const SHORTCUTS = [
  ["Mostrar a área de trabalho (Win + D)", 0x08, 0x07],
  ["Abrir o Explorador (Win + E)", 0x08, 0x08],
  ["Bloquear o computador (Win + L)", 0x08, 0x0F],
  ["Fechar a aba (Ctrl + W)", 0x01, 0x1A],
  ["Trocar de janela (Alt + Tab)", 0x04, 0x2B],
  ["Copiar (Ctrl + C)", 0x01, 0x06],
  ["Colar (Ctrl + V)", 0x01, 0x19],
  ["Recortar (Ctrl + X)", 0x01, 0x1B],
];
const MOUSE_BUTTONS = [[1, "esquerdo"], [2, "direito"], [4, "do meio"]];
const ACTIONS = [
  ["default", "Original"], ["disabled", "Desativada"], ["key", "Outra tecla ou atalho"],
  ["media", "Multimídia"], ["mouse", "Mouse"], ["macro", "Macro"], ["app", "Ação do app"],
];
// Dica de cada ação do app (os rótulos vêm do Core, em catalog.appActions)
const APP_HINTS = {
  open: "Programa, arquivo ou atalho do Windows",
  site: "Endereço completo, com https://",
  text: "Digitado como está, em qualquer layout de teclado",
  media: "Teclas que o firmware não tem",
  lock: "Como Win + L",
  sleep: "O computador entra em suspensão",
  shutdown: "Desliga em 30 s; apertar de novo cancela",
};

const $ = (id) => document.getElementById(id);

function node(tag, props = {}, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (key === "dataset") Object.assign(el.dataset, value);
    else if (key in el) el[key] = value;
    else el.setAttribute(key, value);
  }
  el.append(...children);
  return el;
}

function svg(tag, attrs, parent) {
  const el = document.createElementNS(SVG, tag);
  for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
  parent?.append(el);
  return el;
}

function option(value, label, selected = false) {
  return node("option", { value, textContent: label, selected });
}

// Rótulo curto para a tecla desenhada: "!1" vira "1", "Shift_L" vira "Shift"
function capLabel(name) {
  const special = {
    Backspace: "⌫", CapsLock: "Caps", Shift_L: "Shift", Shift_R: "Shift", Ctrl_L: "Ctrl",
    Ctrl_R: "Ctrl", Win_L: "Win", Alt_L: "Alt", Alt_R: "Alt", Space: "", end: "End", "}}": "]",
  };
  if (name in special) return special[name];
  if (name.length === 2 && !/^F\d$/.test(name)) return name[1];
  return name;
}

export function createKeyboardPages(host) {
  const ks = {
    catalog: null,
    assignments: [],
    macros: [],          // cópia de trabalho; vai ao teclado em "Salvar"
    savedMacros: "[]",   // JSON do que foi enviado, para saber se há mudança
    settings: null,
    layer: "normal",
    selected: null,      // índice da tecla escolhida
    draft: null,         // ação em edição da tecla escolhida
    capturing: false,
    macroId: null,
    recording: null,
  };

  const usageLabel = (usage) => ks.catalog.targets.find((t) => t.usage === usage)?.label ?? `0x${usage.toString(16)}`;
  const keyLabel = (index) => ks.catalog.keys.find((k) => k.index === index)?.label ?? String(index);
  const assignmentOf = (index, layer = ks.layer) =>
    ks.assignments.find((a) => a.key === index && a.layer === layer);

  function describe(a) {
    switch (a.action) {
      case "disabled": return "Desativada";
      case "key": {
        const mods = MOD_NAMES.filter(([bit]) => a.modifiers & bit).map(([, name]) => name);
        return [...mods, ...(a.usage ? [usageLabel(a.usage)] : [])].join(" + ");
      }
      case "media": return ks.catalog.media.find((m) => m.key === a.media)?.label ?? "Multimídia";
      case "mouse": return ks.catalog.mouse.find((m) => m.key === a.mouse)?.label ?? "Mouse";
      case "macro": {
        const macro = ks.macros.find((m) => m.id === a.macroId);
        return `Macro: ${macro?.name ?? "?"}`;
      }
      case "app": {
        const kind = ks.catalog.appActions?.find((k) => k.key === a.app)?.label ?? "Ação do app";
        if (!a.target) return kind;
        const target = a.app === "media"
          ? ks.catalog.appMedia?.find((m) => m.key === a.target)?.label ?? a.target
          : a.app === "open" ? a.target.split(/[\\/]/).pop() : a.target;
        return `${kind}: ${target.length > 40 ? `${target.slice(0, 40)}…` : target}`;
      }
      default: return "Original";
    }
  }

  // --- Teclas ---------------------------------------------------------------------

  const keymap = $("keymap");
  const caps = new Map();

  function buildKeymap() {
    const root = svg("svg", {
      class: "board keymap",
      viewBox: `${VIEWBOX.x} ${VIEWBOX.y} ${VIEWBOX.width} ${VIEWBOX.height}`,
      role: "group",
      "aria-label": "Teclado: escolha a tecla que quer mudar",
    });
    svg("rect", { class: "body", x: BODY.x, y: BODY.y, width: BODY.width, height: BODY.height, rx: BODY.radius }, root);
    for (const k of KEYS) {
      const info = ks.catalog.keys.find((c) => c.index === k.led);
      const g = svg("g", {
        class: "cap",
        tabindex: info?.remappable ? "0" : "-1",
        role: "button",
        "data-key": k.led,
        "aria-label": info?.label ?? k.name,
        "data-disabled": String(!info?.remappable),
      }, root);
      svg("rect", { x: k.x + 1, y: k.y + 1, width: k.w - 2, height: k.h - 2, rx: 4.5 }, g);
      const text = svg("text", { x: k.x + k.w / 2, y: k.y + k.h / 2 + 3.5 }, g);
      text.textContent = capLabel(k.name);
      const title = svg("title", {}, g);
      if (info?.remappable) {
        g.addEventListener("click", () => selectKey(k.led));
        g.addEventListener("keydown", (e) => {
          if (e.key === "Enter" || e.key === " ") { e.preventDefault(); selectKey(k.led); }
        });
      } else {
        title.textContent = "O Fn não pode ser mudado: é ele que abre a camada Fn e os atalhos do teclado";
      }
      caps.set(k.led, { g, title, remappable: info?.remappable });
    }
    keymap.replaceChildren(root);
  }

  function renderKeymap() {
    for (const [index, cap] of caps) {
      const a = assignmentOf(index);
      cap.g.dataset.changed = String(Boolean(a));
      cap.g.setAttribute("aria-pressed", String(index === ks.selected));
      if (cap.remappable) {
        cap.title.textContent = `${keyLabel(index)}: ${a ? describe(a) : "original"}`;
      }
    }
  }

  function selectKey(index) {
    ks.selected = index;
    const existing = assignmentOf(index);
    ks.draft = existing ? { ...existing } : { key: index, layer: ks.layer, action: "default" };
    stopCapture();
    renderKeys();
  }

  for (const button of document.querySelectorAll("#layer [data-layer]")) {
    button.addEventListener("click", () => {
      ks.layer = button.dataset.layer;
      if (ks.selected !== null) selectKey(ks.selected);
      else renderKeys();
    });
  }

  function renderKeys() {
    for (const button of document.querySelectorAll("#layer [data-layer]")) {
      button.setAttribute("aria-checked", String(button.dataset.layer === ks.layer));
    }
    renderKeymap();
    renderEditor();
    renderChangedList();
  }

  const editor = $("key-editor");

  function renderEditor() {
    if (ks.selected === null || !ks.draft) {
      editor.replaceChildren(node("div", { className: "row empty" },
        node("span", { className: "label", textContent: "Clique numa tecla do desenho para mudar o que ela faz." })));
      return;
    }
    const d = ks.draft;
    const rows = [];
    const actionSelect = node("select", { id: "key-action", "aria-label": "O que a tecla faz" },
      ...ACTIONS.map(([value, label]) => option(value, label, value === d.action)));
    actionSelect.addEventListener("change", () => changeAction(actionSelect.value));
    rows.push(row(`${keyLabel(ks.selected)}${ks.layer === "fn" ? " com Fn" : ""}`, actionSelect,
      "O que a tecla faz"));

    if (d.action === "key") rows.push(...keyRows(d));
    if (d.action === "media") rows.push(row("Função", listSelect(ks.catalog.media, d.media, (v) => update({ media: v }))));
    if (d.action === "mouse") rows.push(row("Função", listSelect(ks.catalog.mouse, d.mouse, (v) => update({ mouse: v }))));
    if (d.action === "macro") rows.push(...macroRows(d));
    if (d.action === "app") rows.push(...appRows(d));
    editor.replaceChildren(...rows);
  }

  function row(label, control, hint) {
    const text = node("span", { className: "label", textContent: label });
    if (hint) text.append(node("small", { textContent: hint }));
    return node("div", { className: "row" }, text, control);
  }

  function listSelect(items, value, onChange, placeholder) {
    const select = node("select", {},
      ...(placeholder ? [option("", placeholder, !value)] : []),
      ...items.map((i) => option(i.key, i.label, i.key === value)));
    select.addEventListener("change", () => onChange(select.value));
    return select;
  }

  function keyRows(d) {
    const target = node("select", { "aria-label": "Tecla de destino" }, option("0", "Só os modificadores", d.usage === 0));
    const groups = new Map();
    for (const t of ks.catalog.targets) {
      if (!groups.has(t.group)) groups.set(t.group, node("optgroup", { label: t.group }));
      groups.get(t.group).append(option(String(t.usage), t.label, t.usage === d.usage));
    }
    target.append(...groups.values());
    target.addEventListener("change", () => update({ usage: Number(target.value) }));

    const capture = node("button", {
      type: "button", className: "button", id: "capture",
      textContent: ks.capturing ? "Aperte o atalho…" : "Capturar",
    });
    capture.addEventListener("click", () => (ks.capturing ? stopCapture() : startCapture()));

    const chips = node("div", { className: "chips", role: "group", "aria-label": "Junto com" },
      ...Object.entries(MOD_BITS).map(([name, bit]) => {
        const chip = node("button", {
          type: "button", className: "chip",
          textContent: name === "win" ? "Win" : name[0].toUpperCase() + name.slice(1),
        });
        chip.setAttribute("aria-pressed", String(Boolean(d.modifiers & bit)));
        chip.addEventListener("click", () => update({ modifiers: d.modifiers ^ bit }));
        return chip;
      }));

    const presets = node("select", { "aria-label": "Atalhos prontos" }, option("", "Escolher…", true),
      ...SHORTCUTS.map(([label], i) => option(String(i), label)));
    presets.addEventListener("change", () => {
      const [, modifiers, usage] = SHORTCUTS[Number(presets.value)];
      update({ modifiers, usage });
    });

    return [
      row("Tecla", node("div", { className: "inline" }, target, capture),
        "Capturar: aperte a tecla ou o atalho no teclado"),
      row("Junto com", chips),
      row("Atalhos prontos", presets),
    ];
  }

  function macroRows(d) {
    if (ks.macros.length === 0) {
      return [node("div", { className: "row" },
        node("span", { className: "label", textContent: "Nenhuma macro ainda" }),
        node("button", { type: "button", className: "button", textContent: "Criar macro", onclick: () => openView("macros") }))];
    }
    const playback = listSelect(ks.catalog.playback, d.playback, (v) => update({ playback: v }));
    const rows = [
      row("Macro", listSelect(ks.macros.map((m) => ({ key: m.id, label: m.name })), d.macroId,
        (v) => update({ macroId: v }), "Escolher…")),
      row("Como toca", playback),
    ];
    if (d.playback === "repeat") {
      const repeat = node("input", { type: "number", min: 1, max: 255, value: d.repeat || 1, className: "number" });
      repeat.addEventListener("change", () => {
        const value = Math.min(255, Math.max(1, Math.round(Number(repeat.value) || 1)));
        repeat.value = value;
        update({ repeat: value });
      });
      rows.push(row("Vezes", repeat, "De 1 a 255"));
    }
    return rows;
  }

  // Ação do app: o teclado manda um atalho reservado (F13 a F24) e o app aberto executa
  function appRows(d) {
    const kinds = ks.catalog.appActions ?? [];
    const kind = listSelect(kinds, d.app, (v) => update({ app: v, target: v === "media" ? ks.catalog.appMedia[0].key : "" }));
    const rows = [row("Ação", kind, APP_HINTS[d.app])];
    const needs = kinds.find((k) => k.key === d.app)?.target;
    if (!needs) {
      rows.push(note("Funciona com o app aberto, inclusive na bandeja."));
      return rows;
    }
    if (d.app === "media") {
      rows.push(row("Tecla", listSelect(ks.catalog.appMedia, d.target, (v) => update({ target: v }))));
    } else if (d.app === "text") {
      const text = node("textarea", { className: "text", rows: 3, maxLength: 500, spellcheck: false, value: d.target ?? "" });
      text.addEventListener("change", () => update({ target: text.value }));
      rows.push(node("div", { className: "row text-entry" },
        node("span", { className: "label", textContent: "Texto" }, node("small", { textContent: "Até 500 caracteres" })), text));
    } else {
      const input = node("input", {
        type: "text", className: "text", spellcheck: false, value: d.target ?? "",
        placeholder: d.app === "site" ? "https://" : "C:\\Programas\\app.exe",
      });
      input.addEventListener("change", () => update({ target: input.value.trim() }));
      const controls = node("div", { className: "inline nowrap" }, input);
      if (d.app === "open") {
        const browse = node("button", { type: "button", className: "button", textContent: "Procurar…" });
        browse.addEventListener("click", () => host.post({ type: "pickFile", purpose: "key" }));
        controls.append(browse);
      }
      rows.push(row(d.app === "site" ? "Site" : "Programa", controls));
    }
    rows.push(note("Funciona com o app aberto, inclusive na bandeja."));
    return rows;
  }

  function note(text) {
    return node("div", { className: "row empty" }, node("span", { className: "label", textContent: text }));
  }

  function changeAction(action) {
    const base = { key: ks.selected, layer: ks.layer, action };
    const defaults = {
      key: { modifiers: 0, usage: 0 },
      media: { media: ks.catalog.media[0].key },
      mouse: { mouse: ks.catalog.mouse[0].key },
      macro: { macroId: ks.macros[0]?.id ?? null, playback: "once", repeat: 1 },
      app: { app: "open", target: "" },
    };
    ks.draft = { ...base, ...(defaults[action] ?? {}) };
    commit();
  }

  function update(changes) {
    Object.assign(ks.draft, changes);
    commit();
  }

  // A ação vai ao teclado assim que fica completa; incompleta, só fica na tela
  function complete(d) {
    switch (d.action) {
      case "key": return d.usage > 0 || d.modifiers > 0;
      case "macro": return ks.macros.some((m) => m.id === d.macroId);
      case "app": {
        const needs = ks.catalog.appActions?.find((k) => k.key === d.app)?.target;
        if (!needs) return true;
        const target = (d.target ?? "").trim();
        return d.app === "site" ? /^https?:\/\/\S+$/i.test(target) : target.length > 0;
      }
      default: return true;
    }
  }

  function commit() {
    const d = ks.draft;
    renderEditor();
    if (!complete(d)) return;
    const others = ks.assignments.filter((a) => !(a.key === d.key && a.layer === d.layer));
    ks.assignments = d.action === "default" ? others : [...others, { ...d }];
    renderKeymap();
    renderChangedList();
    host.post({ type: "setKeys", assignments: ks.assignments });
  }

  // Capturar: o próximo atalho apertado vira o destino (modificador sozinho vale ao soltar)
  let captureModifierOnly = null;
  function onCaptureKey(e) {
    if (!ks.capturing) return;
    e.preventDefault();
    e.stopPropagation();
    const target = ks.catalog.targets.find((t) => t.code === e.code);
    const isModifier = target && target.usage >= 0xE0;
    if (e.type === "keydown") {
      if (e.repeat) return;
      if (isModifier) { captureModifierOnly = target.usage; return; }
      captureModifierOnly = null;
      if (!target) return;
      const modifiers = (e.ctrlKey ? 1 : 0) | (e.shiftKey ? 2 : 0) | (e.altKey ? 4 : 0) | (e.metaKey ? 8 : 0);
      stopCapture();
      update({ usage: target.usage, modifiers });
    } else if (isModifier && captureModifierOnly === target.usage) {
      stopCapture();
      update({ usage: target.usage, modifiers: 0 });
    }
  }

  function startCapture() {
    ks.capturing = true;
    captureModifierOnly = null;
    window.addEventListener("keydown", onCaptureKey, true);
    window.addEventListener("keyup", onCaptureKey, true);
    renderEditor();
    $("capture")?.focus();
  }

  function stopCapture() {
    if (!ks.capturing) return;
    ks.capturing = false;
    window.removeEventListener("keydown", onCaptureKey, true);
    window.removeEventListener("keyup", onCaptureKey, true);
    renderEditor();
  }

  function renderChangedList() {
    const list = $("changed-keys");
    const mine = ks.assignments.filter((a) => a.layer === ks.layer)
      .sort((a, b) => a.key - b.key);
    $("changed-title").hidden = mine.length === 0;
    list.hidden = mine.length === 0;
    list.replaceChildren(...mine.map((a) => {
      const undo = node("button", { type: "button", className: "link", textContent: "Voltar ao original" });
      undo.addEventListener("click", () => {
        ks.assignments = ks.assignments.filter((x) => x !== a);
        if (ks.selected === a.key) ks.draft = { key: a.key, layer: a.layer, action: "default" };
        renderKeys();
        host.post({ type: "setKeys", assignments: ks.assignments });
      });
      const open = node("button", { type: "button", className: "row-button" },
        node("span", { className: "label", textContent: keyLabel(a.key) },
          node("small", { textContent: describe(a) })));
      open.addEventListener("click", () => selectKey(a.key));
      return node("div", { className: "row" }, open, undo);
    }));
  }

  $("keys-reset").addEventListener("click", () => {
    ks.assignments = [];
    if (ks.selected !== null) ks.draft = { key: ks.selected, layer: ks.layer, action: "default" };
    renderKeys();
    host.post({ type: "setKeys", assignments: [] });
  });

  // --- Macros ---------------------------------------------------------------------

  const currentMacro = () => ks.macros.find((m) => m.id === ks.macroId);
  const macroSize = () => (ks.macros.length + ks.macros.reduce((n, m) => n + m.events.length, 0)) * 8;
  const macrosDirty = () => JSON.stringify(ks.macros) !== ks.savedMacros;

  function newId() {
    return crypto.randomUUID?.() ?? `m${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`;
  }

  $("macro-new").addEventListener("click", () => {
    if (ks.macros.length >= ks.catalog.maxMacros) return;
    let n = ks.macros.length + 1;
    while (ks.macros.some((m) => m.name === `Macro ${n}`)) n++;
    const macro = { id: newId(), name: `Macro ${n}`, events: [] };
    ks.macros.push(macro);
    ks.macroId = macro.id;
    renderMacros();
    $("macro-name").focus();
  });

  function renderMacroList() {
    const list = $("macro-list");
    list.replaceChildren(...ks.macros.map((m) => {
      const button = node("button", { type: "button", className: "row-button" },
        node("span", { className: "label", textContent: m.name },
          node("small", { textContent: m.events.length === 0 ? "Vazia" : `${m.events.length} ações` })));
      button.setAttribute("aria-current", String(m.id === ks.macroId));
      button.addEventListener("click", () => {
        stopRecording();
        ks.macroId = m.id;
        renderMacros();
      });
      return node("div", { className: "row" }, button);
    }));
    list.hidden = ks.macros.length === 0;
    $("macro-empty").hidden = ks.macros.length > 0;
    $("macro-new").disabled = ks.macros.length >= ks.catalog.maxMacros;
    $("macro-import").disabled = ks.macros.length >= ks.catalog.maxMacros;
    $("macro-export").disabled = ks.macros.length === 0;
  }

  function eventText(e) {
    if (e.kind === "down" || e.kind === "up") {
      return `${e.kind === "down" ? "Apertar" : "Soltar"} ${usageLabel(e.code)}`;
    }
    const button = MOUSE_BUTTONS.find(([code]) => code === e.code)?.[1] ?? "";
    return `${e.kind === "mouse-down" ? "Apertar" : "Soltar"} botão ${button}`;
  }

  function renderEvents(macro) {
    const list = $("macro-events");
    list.replaceChildren(...macro.events.map((e, i) => {
      const delay = node("input", { type: "number", min: 0, max: 65535, value: e.delay, className: "number",
        "aria-label": `Pausa depois de "${eventText(e)}", em milissegundos` });
      delay.addEventListener("change", () => {
        e.delay = Math.min(65535, Math.max(0, Math.round(Number(delay.value) || 0)));
        delay.value = e.delay;
        renderMacroFooter();
      });
      const remove = node("button", { type: "button", className: "icon-button", textContent: "×",
        "aria-label": `Apagar "${eventText(e)}"` });
      remove.addEventListener("click", () => {
        macro.events.splice(i, 1);
        renderMacros();
      });
      return node("li", { className: `event ${e.kind}` },
        node("span", { className: "event-text", textContent: eventText(e) }),
        node("span", { className: "event-delay" }, delay, node("span", { textContent: "ms" })),
        remove);
    }));
    $("macro-events-empty").hidden = macro.events.length > 0 || Boolean(ks.recording);
  }

  function renderMacroFooter() {
    const used = macroSize();
    const meter = $("macro-meter");
    meter.value = used;
    meter.max = ks.catalog.macroCapacity;
    $("macro-capacity").textContent = `Memória do teclado: ${Math.round((used / ks.catalog.macroCapacity) * 100)}% usada`;
    const over = used > ks.catalog.macroCapacity;
    const unnamed = ks.macros.some((m) => !m.name.trim());
    $("macro-save").disabled = !macrosDirty() || over || unnamed || Boolean(ks.recording);
    $("macro-dirty").hidden = !macrosDirty();
    $("macro-dirty").textContent = over ? "As macros passaram do limite da memória"
      : unnamed ? "Toda macro precisa de um nome" : "Alterações ainda não gravadas no teclado";
  }

  function renderMacros() {
    if (!ks.catalog) return;
    renderMacroList();
    const macro = currentMacro();
    $("macro-editor").hidden = !macro;
    if (macro) {
      const name = $("macro-name");
      if (name.value !== macro.name) name.value = macro.name;
      $("macro-record").textContent = ks.recording ? "Parar" : "Gravar";
      $("macro-record").classList.toggle("recording", Boolean(ks.recording));
      $("macro-recording").hidden = !ks.recording;
      renderEvents(macro);
    }
    renderMacroFooter();
  }

  $("macro-name").addEventListener("input", (e) => {
    const macro = currentMacro();
    if (!macro) return;
    macro.name = e.target.value.slice(0, 40);
    renderMacroList();
    renderMacroFooter();
  });

  // Gravação: teclas apertadas na janela viram ações; a pausa é o tempo até a próxima
  function onRecordKey(e) {
    const rec = ks.recording;
    if (!rec || e.repeat) return;
    const target = ks.catalog.targets.find((t) => t.code === e.code);
    if (!target) return;
    e.preventDefault();
    e.stopPropagation();
    const now = performance.now();
    const macro = currentMacro();
    const last = macro.events[macro.events.length - 1];
    if (last && rec.started) {
      last.delay = rec.realDelays ? Math.min(65535, Math.round(now - rec.at)) : rec.fixedDelay;
    }
    rec.at = now;
    rec.started = true;
    macro.events.push({ kind: e.type === "keydown" ? "down" : "up", code: target.usage,
      delay: rec.realDelays ? 10 : rec.fixedDelay });
    renderEvents(macro);
    renderMacroFooter();
    $("macro-events").lastElementChild?.scrollIntoView({ block: "nearest" });
  }

  function startRecording() {
    const fixed = Math.min(65535, Math.max(0, Number($("macro-fixed-delay").value) || 0));
    ks.recording = { at: 0, started: false, realDelays: $("macro-real-delays").checked, fixedDelay: fixed };
    window.addEventListener("keydown", onRecordKey, true);
    window.addEventListener("keyup", onRecordKey, true);
    renderMacros();
  }

  function stopRecording() {
    if (!ks.recording) return;
    ks.recording = null;
    window.removeEventListener("keydown", onRecordKey, true);
    window.removeEventListener("keyup", onRecordKey, true);
    renderMacros();
  }

  $("macro-record").addEventListener("click", () => (ks.recording ? stopRecording() : startRecording()));
  $("macro-real-delays").addEventListener("change", (e) => { $("macro-fixed-delay").disabled = e.target.checked; });

  $("macro-mouse").addEventListener("change", (e) => {
    const macro = currentMacro();
    const code = Number(e.target.value);
    e.target.value = "";
    if (!macro || !code) return;
    const delay = Math.max(10, Number($("macro-fixed-delay").value) || 10);
    macro.events.push({ kind: "mouse-down", code, delay }, { kind: "mouse-up", code, delay });
    renderMacros();
  });

  // Texto: o app converte no layout do Windows e devolve as ações (macroText → appendEvents)
  $("macro-text-open").addEventListener("click", () => {
    const row = $("macro-text-row");
    row.hidden = !row.hidden;
    if (!row.hidden) $("macro-text").focus();
  });
  $("macro-text-insert").addEventListener("click", () => {
    const text = $("macro-text").value;
    if (!text || !currentMacro()) {
      $("macro-text").focus();
      return;
    }
    stopRecording();
    host.post({ type: "textMacro", text, delay: Math.max(10, Number($("macro-fixed-delay").value) || 10) });
  });

  $("macro-export").addEventListener("click", () => {
    stopRecording();
    host.post({ type: "exportMacros", macros: ks.macros });
  });
  $("macro-import").addEventListener("click", () => {
    stopRecording();
    host.post({ type: "importMacros" });
  });

  $("macro-delays-all").addEventListener("click", () => {
    const macro = currentMacro();
    const delay = Math.min(65535, Math.max(0, Number($("macro-fixed-delay").value) || 0));
    macro?.events.forEach((e) => { e.delay = delay; });
    renderMacros();
  });

  $("macro-clear").addEventListener("click", () => {
    const macro = currentMacro();
    if (!macro) return;
    macro.events = [];
    renderMacros();
  });

  $("macro-delete").addEventListener("click", () => {
    stopRecording();
    ks.macros = ks.macros.filter((m) => m.id !== ks.macroId);
    ks.macroId = ks.macros[0]?.id ?? null;
    renderMacros();
  });

  $("macro-save").addEventListener("click", () => {
    stopRecording();
    // Teclas que usavam uma macro apagada voltam ao original
    ks.assignments = ks.assignments.filter((a) => a.action !== "macro" || ks.macros.some((m) => m.id === a.macroId));
    ks.macros.forEach((m) => { m.name = m.name.trim(); });
    ks.savedMacros = JSON.stringify(ks.macros);
    host.post({ type: "saveMacros", macros: ks.macros, assignments: ks.assignments });
    renderMacros();
    renderKeys();
  });

  // --- Teclado (em Ajustes) --------------------------------------------------------

  function renderDevice() {
    const s = ks.settings;
    $("disable-win").checked = s.disableWin;
    $("disable-altf4").checked = s.disableAltF4;
    $("disable-alttab").checked = s.disableAltTab;
    for (const button of document.querySelectorAll("#fn-mode button")) {
      button.setAttribute("aria-checked", String(button.dataset.toggle === String(s.fnToggle)));
    }
    const sleep = $("sleep");
    sleep.replaceChildren(...ks.catalog.sleep.map((o) => option(o.key, o.label, o.key === s.sleep)));
    $("response").value = String(s.response);
  }

  function sendDevice() {
    host.post({ type: "setDevice", settings: { ...ks.settings } });
  }

  for (const [id, key] of [["disable-win", "disableWin"], ["disable-altf4", "disableAltF4"], ["disable-alttab", "disableAltTab"]]) {
    $(id).addEventListener("change", (e) => { ks.settings[key] = e.target.checked; sendDevice(); });
  }
  for (const button of document.querySelectorAll("#fn-mode button")) {
    button.addEventListener("click", () => {
      ks.settings.fnToggle = button.dataset.toggle === "true";
      renderDevice();
      sendDevice();
    });
  }
  $("sleep").addEventListener("change", (e) => { ks.settings.sleep = e.target.value; sendDevice(); });
  $("response").addEventListener("change", (e) => { ks.settings.response = Number(e.target.value); sendDevice(); });

  // --- Entrada ----------------------------------------------------------------------

  let openView = () => {};

  return {
    /** Recebe o bloco "keyboard" da mensagem state. */
    load(keyboard) {
      const first = !ks.catalog;
      ks.catalog = keyboard.catalog;
      ks.assignments = keyboard.assignments ?? [];
      ks.macros = structuredClone(keyboard.macros ?? []);
      ks.savedMacros = JSON.stringify(ks.macros);
      ks.settings = { ...keyboard.settings };
      if (!ks.macros.some((m) => m.id === ks.macroId)) ks.macroId = ks.macros[0]?.id ?? null;
      if (ks.selected !== null) {
        const existing = assignmentOf(ks.selected);
        ks.draft = existing ? { ...existing } : { key: ks.selected, layer: ks.layer, action: "default" };
      }
      if (first) buildKeymap();
      renderKeys();
      renderMacros();
      renderDevice();
    },
    /** Liga o botão "Criar macro" à navegação do app.js. */
    onNavigate(fn) { openView = fn; },
    /** Ações vindas do texto convertido pelo app, no fim da macro aberta. */
    appendEvents(events) {
      const macro = currentMacro();
      if (!macro || !events?.length) return;
      macro.events.push(...events);
      $("macro-text").value = "";
      $("macro-text-row").hidden = true;
      renderMacros();
      $("macro-events").lastElementChild?.scrollIntoView({ block: "nearest" });
    },
    /** Macros de um arquivo: entram na lista (com nome único) e vão ao teclado em Gravar. */
    importMacros(macros) {
      let added = 0;
      for (const macro of macros) {
        if (ks.macros.length >= ks.catalog.maxMacros) break;
        let name = macro.name;
        for (let n = 2; ks.macros.some((m) => m.name === name); n++) name = `${macro.name.slice(0, 34)} (${n})`;
        ks.macros.push({ ...macro, name });
        added++;
      }
      if (added) ks.macroId = ks.macros[ks.macros.length - added].id;
      renderMacros();
      return added;
    },
    /** Ao sair da página, para gravação e captura em andamento. */
    leave() { stopRecording(); stopCapture(); },
    /** Arquivo escolhido no "Procurar…" de uma ação do app. */
    filePicked(purpose, path) {
      if (purpose !== "key" || ks.draft?.action !== "app" || !path) return;
      update({ target: path });
    },
  };
}
