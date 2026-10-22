// Só para abrir a interface num navegador comum (sem WebView2), ex.: revisar o visual.
// Responde como o app responderia. Parâmetros na URL: view, device (usb|dongle|none),
// effect, off, rainbow, color, direction, log, dialog, mirror (1 = imita os quadros que o app
// lê do teclado pelo cabo; sem ele a prévia fica parada, como no dongle),
// macros (1 = já vem com uma macro e duas teclas mudadas), select (índice da tecla aberta
// na página Teclas). view aceita keys, macros, colors e settings. battery (nível, ex.: 15) e
// cable=1 (cabo ligado junto com o dongle) mostram a bateria na barra lateral. colors=1 traz
// um esquema de cores por tecla em uso (colors=0 nenhum esquema); text=1 abre "Inserir texto".
// frame=native esconde a barra de título própria; maximized=1 e inactive=1 mudam os botões dela.
// O catálogo é uma cópia do EffectCatalog do Core apenas para esta prévia.
import { KEYS } from "./layout.js";

const CATALOG = [
  ["static", "Estático", 1, "BCR"], ["single-on", "Acender ao tocar", 2, "BSCR"],
  ["single-off", "Apagar ao tocar", 3, "BSCR"], ["glittering", "Brilhante", 4, "BSCR"],
  ["fluttering", "Queda", 5, "BSCR"], ["colourful", "Colorido", 6, "BS"],
  ["breath", "Respiração", 7, "BSCR"], ["spectrum", "Espectro", 8, "BS"],
  ["outward", "Para fora", 9, "BSCR"], ["scrolling", "Rolagem", 10, "BSCRD"],
  ["rolling", "Rolando", 11, "BSCRD"], ["rotating", "Rotacionando", 12, "BSCRD"],
  ["explode", "Explodir", 13, "BSCR"], ["launch", "Lançamento", 14, "BSCR"],
  ["ripples", "Ondulações", 15, "BSCR"], ["flowing", "Fluindo", 16, "BSCRD"],
  ["pulsating", "Pulsante", 17, "BSCR"], ["tilt", "Inclinação", 18, "BSCRD"],
  ["shuttle", "Vaivém", 19, "BSCR"],
].map(([key, label, mode, caps]) => ({
  key, label, mode,
  brightness: caps.includes("B"), speed: caps.includes("S"), color: caps.includes("C"),
  rainbow: caps.includes("R"),
  // Setas do oficial: 0 esquerda e 1 direita; no Rolagem, 2 cima e 3 baixo
  directions: !caps.includes("D") ? [] : key === "scrolling" ? [2, 3] : [0, 1],
  directionFallback: !caps.includes("D") ? null : key === "scrolling" ? 3 : 0,
}));

const DEFAULTS = { color: "#FF0000", brightness: 5, speed: 3, direction: 0, rainbow: true };

// Quadro como o que o app lê com 04 F5: cor 0xRRGGBB por índice de LED. Aqui, um arco-íris
// andando da esquerda para a direita, só para ver a prévia mudando sozinha.
function mirrorFrame(t) {
  const colors = new Array(143).fill(0);
  for (const k of KEYS) {
    const h = ((k.x / 800) * 360 - t * 0.12) % 360;
    const f = (n) => {
      const x = (n + h / 60) % 6;
      return Math.round(255 * Math.max(0, Math.min(1, Math.min(x, 4 - x))));
    };
    colors[k.led] = (f(5) << 16) | (f(3) << 8) | f(1);
  }
  return colors;
}

// Resumo do KeysCatalogView do Core (o app real manda as 108 teclas de destino)
function keyboardMock(sample) {
  const targets = [
    ...[..."ABCDEFGHIJKLMNOPQRSTUVWXYZ"].map((l, i) => ({ code: `Key${l}`, usage: 4 + i, label: l, group: "Letras" })),
    ...[1, 2, 3, 4, 5, 6, 7, 8, 9].map((n) => ({ code: `Digit${n}`, usage: 0x1D + n, label: String(n), group: "Números" })),
    { code: "Digit0", usage: 0x27, label: "0", group: "Números" },
    { code: "Enter", usage: 0x28, label: "Enter", group: "Edição" },
    { code: "Escape", usage: 0x29, label: "Esc", group: "Edição" },
    { code: "Space", usage: 0x2C, label: "Espaço", group: "Edição" },
    ...Array.from({ length: 12 }, (_, i) => ({ code: `F${i + 1}`, usage: 0x3A + i, label: `F${i + 1}`, group: "Funções" })),
    { code: "ControlLeft", usage: 0xE0, label: "Ctrl esquerdo", group: "Modificadores" },
    { code: "ShiftLeft", usage: 0xE1, label: "Shift esquerdo", group: "Modificadores" },
    { code: "AltLeft", usage: 0xE2, label: "Alt esquerdo", group: "Modificadores" },
  ];
  const keys = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 119, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
    31, 103, 117, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 67, 120, 55, 56, 57, 58, 59, 60, 61, 62,
    63, 64, 65, 66, 85, 118, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 101, 121, 91, 92, 93, 94, 95, 96,
    98, 99, 100, 102].map((index) => ({ index, label: index === 55 ? "Caps Lock" : `Tecla ${index}`, remappable: index !== 96 }));
  const list = (pairs) => pairs.map(([key, label]) => ({ key, label }));
  return {
    catalog: {
      keys, targets,
      media: list([["play-pause", "Tocar/pausar"], ["next", "Próxima faixa"], ["mute", "Sem som"]]),
      mouse: list([["left", "Clique esquerdo"], ["right", "Clique direito"], ["scroll-up", "Rolar para cima"]]),
      playback: list([["once", "Tocar uma vez"], ["repeat", "Repetir algumas vezes"],
        ["until-pressed", "Repetir até apertar de novo"], ["while-held", "Repetir enquanto segurar"]]),
      sleep: list([["never", "Nunca"], ["1m", "1 minuto"], ["5m", "5 minutos"], ["30m", "30 minutos"]]),
      maxMacros: 100,
      macroCapacity: 2700,
    },
    assignments: sample ? [
      { key: 55, layer: "normal", action: "key", modifiers: 0, usage: 0xE0 },
      { key: 2, layer: "normal", action: "macro", macroId: "m1", playback: "once", repeat: 0 },
    ] : [],
    macros: sample ? [{ id: "m1", name: "Copiar e colar", events: [
      { kind: "down", code: 0xE0, delay: 40 }, { kind: "down", code: 0x06, delay: 60 },
      { kind: "up", code: 0x06, delay: 30 }, { kind: "up", code: 0xE0, delay: 120 },
      { kind: "mouse-down", code: 1, delay: 10 }, { kind: "mouse-up", code: 1, delay: 10 },
    ] }] : [],
    settings: { disableWin: true, disableAltF4: false, disableAltTab: false, fnToggle: false, sleep: "5m", response: 0 },
  };
}

// Esquema de exemplo: teclado azul com WASD e setas em laranja (índices do layout.js)
function colorsMock(param) {
  if (param === "0") return { schemes: [], activeId: null };
  const colors = {};
  const keys = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 119, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
    31, 103, 117, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 67, 120, 55, 56, 57, 58, 59, 60, 61, 62,
    63, 64, 65, 66, 85, 118, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 101, 121, 91, 92, 93, 94, 95, 96,
    98, 99, 100, 102];
  for (const key of keys) colors[key] = "#0040FF";
  for (const key of [39, 56, 57, 58, 101, 99, 100, 102]) colors[key] = "#FF6A00";
  delete colors[94];
  const schemes = [
    { id: "c1", name: "Jogo", brightness: 4, colors },
    { id: "c2", name: "Trabalho", brightness: 3, colors: { 1: "#FFFFFF" } },
  ];
  return { schemes, activeId: param === "1" ? "c1" : null };
}

const PROFILES = { list: [{ id: "p1", name: "Padrão" }, { id: "p2", name: "Jogos" }], activeId: "p1" };

export function createMockHost() {
  const params = new URLSearchParams(location.search);
  const listeners = [];
  const emit = (message) => setTimeout(() => listeners.forEach((fn) => fn(structuredClone(message))), 0);
  const deviceParam = params.get("device") ?? "usb";
  // Nível só vem do dongle; com o cabo ligado o app mostra "carregando"
  const battery = {
    level: params.has("battery") ? Number(params.get("battery")) : deviceParam === "dongle" ? 72 : null,
    cable: deviceParam === "usb" || params.get("cable") === "1",
  };
  const device = deviceParam === "none"
    ? { connected: false, channel: null, name: "", battery: { level: null, cable: false } }
    : { connected: true, channel: deviceParam === "dongle" ? "dongle" : "usb", name: "AKIRA CORE", battery };

  const windowState = {
    custom: params.get("frame") !== "native",
    maximized: params.get("maximized") === "1",
    active: params.get("inactive") !== "1",
  };

  let mirrorTimer = 0;
  if (params.get("mirror") === "1" && device.channel === "usb") {
    mirrorTimer = setInterval(() => emit({ type: "mirror", colors: mirrorFrame(performance.now()) }), 120);
  }

  const respond = (message) => {
    switch (message.type) {
      case "ready":
        emit({
          type: "state",
          effects: CATALOG,
          lighting: {
            effect: params.get("effect") ?? "rolling",
            color: params.get("color") ?? "#FF0000",
            brightness: 5,
            speed: 3,
            direction: Number(params.get("direction") ?? 0),
            rainbow: params.get("rainbow") !== "0",
          },
          effectSettings: {},
          effectDefaults: DEFAULTS,
          lightOn: params.get("off") !== "1",
          options: { swapRedBlue: false, simulate: false, startWithWindows: false, batteryAlert: true },
          device,
          keyboard: keyboardMock(params.get("macros") === "1"),
          customLights: colorsMock(params.get("colors")),
          profiles: PROFILES,
        });
        if (params.get("text")) {
          setTimeout(() => document.getElementById("macro-text-open").click(), 60);
        }
        if (params.get("view")) {
          setTimeout(() => document.querySelector(`[data-view="${params.get("view")}"]`)?.click(), 0);
        }
        if (params.get("log")) {
          emit({ type: "log", title: "Bytes enviados", lines: [
            "SET 0004180000000000000000000000000000...",
            "SET 0004130000000000000001000000000000...",
            "SET 000BFF0000000000000105030000000000...",
          ] });
        }
        emit({ type: "window", ...windowState });
        if (params.get("dialog")) setTimeout(() => document.getElementById("reset").click(), 50);
        if (params.get("select")) {
          setTimeout(() => document.querySelector(`#keymap [data-key="${params.get("select")}"]`)
            ?.dispatchEvent(new MouseEvent("click", { bubbles: true })), 50);
        }
        break;
      case "window":
        // Aqui não há janela: só alterna o botão maximizar para ver os dois desenhos
        if (message.action === "maximize") {
          windowState.maximized = !windowState.maximized;
          emit({ type: "window", ...windowState });
        }
        break;
      case "view":
        // O app só lê o teclado com a página de iluminação aberta e o cabo ligado
        clearInterval(mirrorTimer);
        if (params.get("mirror") === "1" && message.name === "lighting" && device.channel === "usb") {
          mirrorTimer = setInterval(() => emit({ type: "mirror", colors: mirrorFrame(performance.now()) }), 120);
        } else {
          emit({ type: "mirror", colors: null });
        }
        break;
      case "apply":
      case "setLight":
      case "factoryReset":
      case "setKeys":
      case "saveMacros":
      case "setDevice":
      case "useCustomLight":
        emit({ type: "status", kind: "busy", text: "Enviando…" });
        setTimeout(() => emit({ type: "status", kind: "ok", text: "Aplicado" }), 350);
        break;
      case "textMacro": {
        // Só letras minúsculas e espaço, como um layout simples faria
        const events = [];
        let skipped = "";
        for (const c of message.text) {
          const usage = c === " " ? 0x2C : c >= "a" && c <= "z" ? 4 + c.charCodeAt(0) - 97 : 0;
          if (!usage) { if (!skipped.includes(c)) skipped += c; continue; }
          events.push({ kind: "down", code: usage, delay: message.delay }, { kind: "up", code: usage, delay: message.delay });
        }
        emit({ type: "macroText", events, skipped, layout: "Português (Brasil ABNT2)" });
        break;
      }
      case "switchProfile":
        PROFILES.activeId = message.id;
        emit({ type: "profiles", profiles: PROFILES });
        emit({ type: "status", kind: "ok", text: "Perfil aplicado" });
        break;
      case "createProfile":
        PROFILES.list.push({ id: `p${PROFILES.list.length + 1}`, name: message.name });
        PROFILES.activeId = PROFILES.list[PROFILES.list.length - 1].id;
        emit({ type: "profiles", profiles: PROFILES });
        break;
      case "exportBackup":
      case "exportMacros":
      case "exportColors":
        emit({ type: "status", kind: "ok", text: "Exportado" });
        break;
      default:
        break;
    }
  };

  return {
    post: (message) => respond(message),
    onMessage: (fn) => listeners.push(fn),
  };
}
