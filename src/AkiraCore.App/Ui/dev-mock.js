// Só para abrir a interface num navegador comum (sem WebView2), ex.: revisar o visual.
// Responde como o app responderia. Parâmetros na URL: view, device (usb|dongle|none),
// effect, off, rainbow, color, log, dialog, typing (1 = simula teclas tocadas no AKIRA),
// macros (1 = já vem com uma macro e duas teclas mudadas), select (índice da tecla aberta
// na página Teclas). view aceita keys, macros, colors e settings. battery (nível, ex.: 15) e
// cable=1 (cabo ligado junto com o dongle) mostram a bateria na barra lateral. colors=1 traz
// um esquema de cores por tecla em uso (colors=0 nenhum esquema); text=1 abre "Inserir texto".
// view=live com live=scene|music|screen|cpu|timer liga um modo ao vivo (quadros simulados);
// timer=1 deixa o cronômetro correndo; mirror=1 liga o espelho dos LEDs; appkey=1 abre uma tecla
// com ação do app em Teclas.
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
  rainbow: caps.includes("R"), direction: caps.includes("D"),
}));

// Resumo do KeysCatalogView do Core (o app real manda as 108 teclas de destino)
function keyboardMock(sample, appKey) {
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
      appActions: [
        { key: "open", label: "Abrir programa ou arquivo", target: true },
        { key: "site", label: "Abrir site", target: true },
        { key: "text", label: "Digitar texto", target: true },
        { key: "media", label: "Multimídia estendida", target: true },
        { key: "lock", label: "Bloquear o computador", target: false },
        { key: "sleep", label: "Suspender o computador", target: false },
        { key: "shutdown", label: "Desligar o computador", target: false },
      ],
      appMedia: list([["calculator", "Calculadora"], ["mail", "E-mail"], ["browser", "Navegador (página inicial)"]]),
      maxAppKeys: 48,
      maxMacros: 100,
      macroCapacity: 2700,
    },
    assignments: [
      ...(sample ? [
        { key: 55, layer: "normal", action: "key", modifiers: 0, usage: 0xE0 },
        { key: 2, layer: "normal", action: "macro", macroId: "m1", playback: "once", repeat: 0 },
      ] : []),
      ...(appKey ? [{ key: 7, layer: "normal", action: "app", app: "open", target: "C:\\Windows\\notepad.exe" }] : []),
    ],
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

const PROFILES = {
  list: [{ id: "p1", name: "Padrão", apps: [] }, { id: "p2", name: "Jogos", apps: ["valorant.exe", "cs2.exe"] }],
  activeId: "p1",
  auto: { enabled: true, fallbackId: "p1" },
};

// Cópia do LiveModes do Core, só para a prévia
const LIVE_CATALOG = {
  modes: [
    { key: "off", label: "Desligado", cable: true, dongle: true },
    { key: "scene", label: "Teclas animadas", cable: true, dongle: false },
    { key: "music", label: "Ritmo musical", cable: true, dongle: true },
    { key: "screen", label: "Cor da tela", cable: true, dongle: false },
    { key: "cpu", label: "Uso da CPU", cable: true, dongle: false },
    { key: "timer", label: "Cronômetro", cable: true, dongle: false },
  ],
  animations: [
    { key: "static", label: "Fixa" }, { key: "breath", label: "Respirar" }, { key: "blink", label: "Piscar" },
    { key: "spectrum", label: "Espectro" }, { key: "wave", label: "Onda de arco-íris" },
    { key: "reactive", label: "Acender ao tocar" },
  ],
  maxTimerMinutes: 180,
};

function liveMock(mode) {
  const keys = {};
  for (const k of KEYS) keys[k.led] = { effect: "wave", color: "#00E5FF" };
  for (const led of [39, 56, 57, 58]) keys[led] = { effect: "breath", color: "#FF6A00" };
  return {
    mode,
    brightness: 5,
    scene: { speed: 3, keys },
    music: { style: "rainbow", color: "#00E5FF", background: true, sensitivity: 5 },
    screen: { smoothing: 4, vivid: true },
    cpu: { style: "bar", low: "#00FF40", high: "#FF0000" },
    timer: { minutes: 25, color: "#00E5FF" },
  };
}

function hsl(h) {
  h = (((h % 360) + 360) % 360) / 60;
  const x = Math.round((1 - Math.abs((h % 2) - 1)) * 255);
  const [r, g, b] = h < 1 ? [255, x, 0] : h < 2 ? [x, 255, 0] : h < 3 ? [0, 255, x]
    : h < 4 ? [0, x, 255] : h < 5 ? [x, 0, 255] : [255, 0, x];
  return hex(r, g, b);
}

function hex(r, g, b) {
  return `#${[r, g, b].map((v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, "0")).join("").toUpperCase()}`;
}

function scale(color, f) {
  const n = parseInt(color.slice(1), 16);
  return hex(((n >> 16) & 255) * f, ((n >> 8) & 255) * f, (n & 255) * f);
}

const minX = Math.min(...KEYS.map((k) => k.x + k.w / 2));
const maxX = Math.max(...KEYS.map((k) => k.x + k.w / 2));
const rowOf = (k) => (k.y < 50 ? 0 : k.y < 100 ? 1 : k.y < 136 ? 2 : k.y < 176 ? 3 : k.y < 214 ? 4 : 5);

// Aproximação do que o LiveEngine do Core desenha, só para ver a página se mexer
function mockFrame(live, t, timer) {
  const colors = {};
  for (const k of KEYS) {
    const x = (k.x + k.w / 2 - minX) / (maxX - minX);
    const row = rowOf(k);
    let color = "#000000";
    switch (live.mode) {
      case "scene": {
        const key = live.scene.keys[k.led];
        if (!key) break;
        color = key.effect === "wave" ? hsl(x * 360 - t * 120)
          : key.effect === "breath" ? scale(key.color, 0.5 - 0.5 * Math.cos(t * 2 * Math.PI))
          : key.effect === "reactive" ? "#000000" : key.color;
        break;
      }
      case "music": {
        const level = 0.5 + 0.45 * Math.sin(t * 3 + x * 9) * Math.cos(t * 1.3 + x * 4);
        const lit = Math.max(0, Math.min(1, level * 6 - (5 - row)));
        const tint = live.music.style === "gradient" ? hsl(120 - ((5 - row) / 5) * 120)
          : live.music.style === "color" ? live.music.color : hsl(x * 300);
        color = scale(tint, Math.max(lit, live.music.background ? 0.07 : 0));
        break;
      }
      case "screen":
        color = hsl(200 + x * 80 + Math.sin(t / 2) * 30);
        break;
      case "cpu":
        color = row === 1 ? (x < 0.4 ? "#00FF40" : "#0A2A10") : "#04140A";
        break;
      case "timer": {
        const fraction = timer ? timer.remainingMs / timer.totalMs : 1;
        const f = /^F\d+$/.test(k.name) ? Number(k.name.slice(1)) : 0;
        color = f && f <= Math.ceil(fraction * 12) ? live.timer.color : scale(live.timer.color, 0.05);
        break;
      }
    }
    colors[k.led] = color;
  }
  return colors;
}

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

  let live = liveMock(params.get("live") ?? "off");
  let timer = params.get("timer") === "1" ? { remainingMs: 14 * 60000 + 32000, totalMs: 25 * 60000, running: true } : null;
  if (timer) live.mode = "timer";
  let alertUntil = 0;
  let alertColor = "#FF0000";
  const liveStatus = () => ({
    mode: live.mode,
    running: live.mode !== "off",
    state: live.mode === "off" ? "idle" : "sending",
    detail: null,
    paused: false,
    lightOverride: "none",
    timer,
  });
  let framesStarted = false;
  const startFrames = () => {
    if (framesStarted) return;
    framesStarted = true;
    const started = performance.now();
    setInterval(() => {
      if (timer?.running) timer.remainingMs = Math.max(0, timer.remainingMs - 100);
      const now = performance.now();
      const alerting = now < alertUntil;
      if (live.mode === "off" && !alerting) return;
      let colors = mockFrame(live, (now - started) / 1000, timer);
      if (alerting) {
        const on = ((alertUntil - now) / 250) % 2 > 1;
        colors = Object.fromEntries(KEYS.map((k) => [k.led, on ? alertColor : "#000000"]));
      }
      emit({ type: "liveFrame", colors });
    }, 100);
    setInterval(() => { if (timer) emit({ type: "liveStatus", status: liveStatus() }); }, 1000);
  };

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
            reverse: false,
            rainbow: params.get("rainbow") !== "0",
          },
          lightOn: params.get("off") !== "1",
          options: { swapRedBlue: false, simulate: false, startWithWindows: false, batteryAlert: true },
          device,
          keyboard: keyboardMock(params.get("macros") === "1", params.get("appkey") === "1"),
          customLights: colorsMock(params.get("colors")),
          profiles: PROFILES,
          live: { settings: live, catalog: LIVE_CATALOG, lastMode: "music", status: liveStatus() },
          automation: {
            rules: { offWhenLocked: true, offWhenDisplayOff: false, night: true, nightStart: "23:00", nightEnd: "07:00", nightAction: "dim" },
            hotkeys: [{ action: "toggleLight", modifiers: 3, key: "KeyL" }, { action: "nextEffect", modifiers: 3, key: "ArrowRight" }],
            hotkeyActions: [
              { key: "toggleLight", label: "Ligar ou desligar a luz" }, { key: "nextEffect", label: "Próximo efeito" },
              { key: "previousEffect", label: "Efeito anterior" }, { key: "brightnessUp", label: "Mais brilho" },
              { key: "brightnessDown", label: "Menos brilho" }, { key: "nextProfile", label: "Próximo perfil" },
              { key: "previousProfile", label: "Perfil anterior" }, { key: "liveToggle", label: "Ligar ou desligar o modo ao vivo" },
              { key: "showWindow", label: "Abrir o AkiraCore" },
            ],
          },
          mirror: params.get("mirror") === "1",
        });
        startFrames();
        if (params.get("appkey")) {
          setTimeout(() => document.querySelector('#keymap [data-key="7"]')
            ?.dispatchEvent(new MouseEvent("click", { bubbles: true })), 80);
        }
        if (params.get("mirror") === "1") {
          // O "teclado de verdade": tudo azul com WASD laranja
          const colors = {};
          for (const k of KEYS) colors[k.led] = [39, 56, 57, 58].includes(k.led) ? "#FF6A00" : "#0040FF";
          setTimeout(() => emit({ type: "mirror", colors, problem: null }), 50);
        }
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
        if (params.get("dialog")) setTimeout(() => document.getElementById("reset").click(), 50);
        if (params.get("select")) {
          setTimeout(() => document.querySelector(`#keymap [data-key="${params.get("select")}"]`)
            ?.dispatchEvent(new MouseEvent("click", { bubbles: true })), 50);
        }
        if (params.get("typing")) {
          // Letras da fileira do meio e de cima (scan codes 0x10..0x26), como o Raw Input mandaria
          setInterval(() => {
            const scan = 0x10 + Math.floor(Math.random() * 0x17);
            emit({ type: "key", scan, down: true });
            setTimeout(() => emit({ type: "key", scan, down: false }), 120);
          }, 260);
        }
        break;
      case "setLive":
        live = message.live;
        emit({ type: "liveStatus", status: liveStatus() });
        break;
      case "liveTimer":
        if (message.action === "start") timer = { remainingMs: timer?.remainingMs ?? live.timer.minutes * 60000, totalMs: live.timer.minutes * 60000, running: true };
        if (message.action === "pause" && timer) timer.running = false;
        if (message.action === "stop") timer = null;
        if (message.action === "start" && live.mode !== "timer") live.mode = "timer";
        emit({ type: "liveStatus", status: liveStatus() });
        break;
      case "testAlert":
        alertUntil = performance.now() + 1500;
        alertColor = message.color;
        break;
      case "setProfileApps":
        PROFILES.list.find((p) => p.id === message.id).apps = message.apps;
        emit({ type: "profiles", profiles: PROFILES });
        break;
      case "setAutoProfile":
        PROFILES.auto = message.auto;
        emit({ type: "profiles", profiles: PROFILES });
        break;
      case "pickFile":
        emit({ type: "filePicked", purpose: message.purpose, path: "C:\\Program Files\\Spotify\\Spotify.exe" });
        break;
      case "setRules":
      case "setHotkeys":
        emit({ type: "status", kind: "ok", text: "Salvo" });
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
