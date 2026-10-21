// Página Ao vivo: efeitos calculados no PC (luz personalizada, ritmo musical, cor da tela, CPU,
// cronômetro) e avisos. Só estado de tela: a validação e os quadros ficam no Core (LiveState.cs) e
// o laço no app (LiveRunner). Mensagens: setLive {live}, liveTimer {action}, testAlert {color, times}.
// Recebe: liveFrame {colors} (o quadro que está indo ao teclado) e liveStatus {status}.
import { VIEWBOX, BODY, KNOB, KEYS } from "./layout.js";

const SVG = "http://www.w3.org/2000/svg";
const OFF = "#000000";
const COLORS = [
  ["#FF0000", "Vermelho"], ["#FF6A00", "Laranja"], ["#FFD000", "Amarelo"],
  ["#00FF40", "Verde"], ["#00E5FF", "Ciano"], ["#0040FF", "Azul"],
  ["#8A2BE2", "Roxo"], ["#FF2D95", "Rosa"], ["#FFFFFF", "Branco"],
];
const ALERT_COLORS = [["#FF0000", "Vermelho"], ["#FFD000", "Amarelo"], ["#00FF40", "Verde"], ["#0040FF", "Azul"]];
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

function hexToRgb(hex) {
  const n = parseInt(hex.slice(1), 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

function renderRange(input, value) {
  input.value = value;
  const min = Number(input.min);
  input.style.setProperty("--p", `${((value - min) / (Number(input.max) - min)) * 100}%`);
}

// Um grupo de círculos de cor, como os da iluminação; onPick recebe #RRGGBB
function swatchGroup(container, colors, onPick, withCustom = true) {
  const buttons = colors.map(([hex, name]) => {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "swatch";
    b.setAttribute("role", "radio");
    b.setAttribute("aria-label", name);
    b.title = name;
    b.dataset.color = hex;
    b.style.setProperty("--c", hex);
    b.addEventListener("click", () => onPick(hex));
    return b;
  });
  container.replaceChildren(...buttons);
  let custom = null;
  if (withCustom) {
    custom = document.createElement("span");
    custom.className = "swatch custom";
    custom.setAttribute("role", "radio");
    custom.setAttribute("aria-label", "Outra cor");
    const picker = document.createElement("input");
    picker.type = "color";
    picker.setAttribute("aria-label", "Escolher outra cor");
    picker.addEventListener("change", () => onPick(picker.value.toUpperCase()));
    custom.append(picker);
    container.append(custom);
  }
  return (value) => {
    const color = (value ?? "").toUpperCase();
    let matched = false;
    for (const b of buttons) {
      const on = b.dataset.color === color;
      matched ||= on;
      b.setAttribute("aria-checked", String(on));
    }
    if (!custom) return;
    custom.setAttribute("aria-checked", String(!matched));
    if (matched) {
      delete custom.dataset.color;
    } else {
      custom.dataset.color = color;
      custom.style.setProperty("--c", color);
      custom.querySelector("input").value = color.toLowerCase();
    }
  };
}

export function createLivePage(host) {
  const ls = {
    settings: null,
    catalog: null,
    status: null,
    channel: null,
    brush: { effect: "static", color: "#00E5FF" },
    painting: false,
    frame: null,      // último quadro recebido do app
    frameAt: 0,
  };

  // --- Teclado ---------------------------------------------------------------------

  const caps = new Map();
  const board = svg("svg", {
    class: "board paint live",
    viewBox: `${VIEWBOX.x} ${VIEWBOX.y} ${VIEWBOX.width} ${VIEWBOX.height}`,
    role: "group",
    "aria-label": "Prévia do modo ao vivo",
  });
  const defs = svg("defs", {}, board);
  const filter = svg("filter", { id: "live-glow", x: "-20%", y: "-20%", width: "140%", height: "140%" }, defs);
  svg("feGaussianBlur", { stdDeviation: "4" }, filter);
  svg("rect", { class: "body", x: BODY.x, y: BODY.y, width: BODY.width, height: BODY.height, rx: BODY.radius }, board);
  const glowLayer = svg("g", { filter: "url(#live-glow)" }, board);
  for (const k of KEYS) {
    const glow = svg("rect", { x: k.x - 3.5, y: k.y - 3.5, width: k.w + 7, height: k.h + 7, rx: 7, opacity: 0 }, glowLayer);
    const g = svg("g", { class: "cap", "data-key": k.led, "aria-label": k.name }, board);
    const rect = svg("rect", { x: k.x + 1, y: k.y + 1, width: k.w - 2, height: k.h - 2, rx: 4.5 }, g);
    const title = svg("title", {}, g);
    caps.set(k.led, { g, rect, glow, title, name: k.name });
  }
  svg("circle", { class: "knob", cx: KNOB.cx, cy: KNOB.cy, r: KNOB.r }, board);
  svg("circle", { class: "knob-cap", cx: KNOB.cx, cy: KNOB.cy, r: KNOB.r - 4 }, board);
  $("live-board").append(board);

  const scene = () => ls.settings.scene;
  const keyOf = (target) => Number(target?.closest?.("[data-key]")?.dataset.key) || null;
  const painting = () => ls.settings?.mode === "scene";

  board.addEventListener("pointerdown", (e) => {
    const key = keyOf(e.target);
    if (!key || !painting()) return;
    if (e.button === 2) {
      const current = scene().keys[key];
      if (current) {
        ls.brush = { effect: current.effect, color: current.color };
        renderScene();
      }
      return;
    }
    if (e.button !== 0) return;
    ls.painting = true;
    board.setPointerCapture(e.pointerId);
    paint(key);
  });
  board.addEventListener("pointermove", (e) => {
    if (!ls.painting) return;
    const key = keyOf(document.elementFromPoint(e.clientX, e.clientY) ?? e.target);
    if (key) paint(key);
  });
  const stop = () => {
    if (!ls.painting) return;
    ls.painting = false;
    send(0);
  };
  board.addEventListener("pointerup", stop);
  board.addEventListener("pointercancel", stop);
  board.addEventListener("contextmenu", (e) => e.preventDefault());

  function paint(key) {
    const keys = scene().keys;
    const next = { effect: ls.brush.effect, color: ls.brush.color };
    if (keys[key]?.effect === next.effect && keys[key]?.color === next.color) return;
    keys[key] = next;
    renderBoard();
  }

  // Sem quadro chegando (modo desligado, sem teclado), a prévia mostra a cena desenhada
  function boardColors() {
    const fresh = ls.frame && performance.now() - ls.frameAt < 1500;
    if (fresh) return ls.frame;
    if (painting()) {
      const colors = {};
      for (const [key, value] of Object.entries(scene().keys)) {
        colors[key] = value.effect === "reactive" ? OFF : value.color;
      }
      return colors;
    }
    return {};
  }

  function renderBoard() {
    const colors = boardColors();
    for (const [key, cap] of caps) {
      const color = (colors[key] ?? OFF).toUpperCase();
      const [r, g, b] = hexToRgb(color);
      const level = Math.max(r, g, b) / 255;
      const lit = level > 0.02;
      cap.rect.setAttribute("fill", lit ? `rgb(${Math.round(r * 0.75 + 40)},${Math.round(g * 0.75 + 40)},${Math.round(b * 0.75 + 40)})` : "");
      cap.g.dataset.off = String(!lit);
      cap.glow.setAttribute("fill", color);
      cap.glow.setAttribute("opacity", lit ? (0.85 * level).toFixed(3) : "0");
      const sceneKey = painting() ? scene().keys[key] : null;
      const animation = sceneKey ? ls.catalog?.animations.find((a) => a.key === sceneKey.effect)?.label : null;
      cap.title.textContent = sceneKey ? `${cap.name}: ${animation}, ${sceneKey.color}` : cap.name;
    }
  }

  // --- Modos -------------------------------------------------------------------------

  function renderModes() {
    const container = $("live-modes");
    container.replaceChildren(...ls.catalog.modes.map((mode) => {
      const chip = document.createElement("button");
      chip.type = "button";
      chip.className = "chip";
      chip.setAttribute("role", "radio");
      chip.dataset.mode = mode.key;
      const onlyCable = mode.key !== "off" && !mode.dongle;
      chip.textContent = mode.label;
      chip.title = onlyCable ? "Só pelo cabo USB" : mode.key === "music" ? "Cabo e dongle 2.4G" : "";
      chip.addEventListener("click", () => {
        if (ls.settings.mode === mode.key) return;
        ls.settings.mode = mode.key;
        ls.frame = null;
        render();
        send(0);
      });
      return chip;
    }));
  }

  function renderModeSelection() {
    for (const chip of $("live-modes").querySelectorAll(".chip")) {
      const on = chip.dataset.mode === ls.settings.mode;
      chip.setAttribute("aria-checked", String(on));
      chip.setAttribute("aria-pressed", String(on));
      const mode = ls.catalog.modes.find((m) => m.key === chip.dataset.mode);
      chip.dataset.unsupported = String(ls.channel === "dongle" && mode.key !== "off" && !mode.dongle);
    }
    for (const section of document.querySelectorAll("[data-live]")) {
      section.hidden = section.dataset.live !== ls.settings.mode;
    }
    board.dataset.painting = String(painting());
    $("live-common").hidden = ls.settings.mode === "off";
  }

  // --- Teclas animadas ------------------------------------------------------------------

  const sceneSwatches = swatchGroup($("scene-swatches"), COLORS, (hex) => {
    ls.brush.color = hex;
    renderScene();
  });

  $("scene-animation").addEventListener("change", (e) => {
    ls.brush.effect = e.target.value;
    renderScene();
  });

  $("scene-groups").replaceChildren(...[...GROUPS, ["Limpar", null]].map(([label, test]) => {
    const chip = document.createElement("button");
    chip.type = "button";
    chip.className = "chip";
    chip.textContent = label;
    chip.addEventListener("click", () => {
      if (!test) {
        scene().keys = {};
      } else {
        for (const k of KEYS.filter(test)) scene().keys[k.led] = { effect: ls.brush.effect, color: ls.brush.color };
      }
      renderBoard();
      send(0);
    });
    return chip;
  }));

  $("scene-speed").addEventListener("input", (e) => {
    scene().speed = Number(e.target.value);
    renderRange(e.target, scene().speed);
  });
  $("scene-speed").addEventListener("change", () => send());

  function renderScene() {
    const select = $("scene-animation");
    if (select.options.length !== ls.catalog.animations.length) {
      select.replaceChildren(...ls.catalog.animations.map((a) => new Option(a.label, a.key)));
    }
    select.value = ls.brush.effect;
    sceneSwatches(ls.brush.color);
    renderRange($("scene-speed"), scene().speed);
  }

  // --- Ritmo musical ----------------------------------------------------------------------

  const musicSwatches = swatchGroup($("music-swatches"), COLORS, (hex) => {
    ls.settings.music.color = hex;
    renderMusic();
    send();
  });
  for (const button of document.querySelectorAll("#music-style button")) {
    button.addEventListener("click", () => {
      ls.settings.music.style = button.dataset.style;
      renderMusic();
      send();
    });
  }
  $("music-background").addEventListener("change", (e) => {
    ls.settings.music.background = e.target.checked;
    send();
  });
  $("music-sensitivity").addEventListener("input", (e) => {
    ls.settings.music.sensitivity = Number(e.target.value);
    renderRange(e.target, ls.settings.music.sensitivity);
  });
  $("music-sensitivity").addEventListener("change", () => send());

  function renderMusic() {
    const music = ls.settings.music;
    for (const button of document.querySelectorAll("#music-style button")) {
      button.setAttribute("aria-checked", String(button.dataset.style === music.style));
    }
    $("music-color-row").hidden = music.style !== "color";
    musicSwatches(music.color);
    $("music-background").checked = music.background;
    renderRange($("music-sensitivity"), music.sensitivity);
  }

  // --- Cor da tela e CPU --------------------------------------------------------------------

  $("screen-vivid").addEventListener("change", (e) => {
    ls.settings.screen.vivid = e.target.checked;
    send();
  });
  $("screen-smoothing").addEventListener("input", (e) => {
    ls.settings.screen.smoothing = Number(e.target.value);
    renderRange(e.target, ls.settings.screen.smoothing);
  });
  $("screen-smoothing").addEventListener("change", () => send());

  for (const button of document.querySelectorAll("#cpu-style button")) {
    button.addEventListener("click", () => {
      ls.settings.cpu.style = button.dataset.style;
      renderCpu();
      send();
    });
  }
  for (const [id, key] of [["cpu-low", "low"], ["cpu-high", "high"]]) {
    $(id).addEventListener("change", (e) => {
      ls.settings.cpu[key] = e.target.value.toUpperCase();
      send();
    });
  }

  function renderOthers() {
    $("screen-vivid").checked = ls.settings.screen.vivid;
    renderRange($("screen-smoothing"), ls.settings.screen.smoothing);
    renderCpu();
  }

  function renderCpu() {
    for (const button of document.querySelectorAll("#cpu-style button")) {
      button.setAttribute("aria-checked", String(button.dataset.style === ls.settings.cpu.style));
    }
    $("cpu-low").value = ls.settings.cpu.low.toLowerCase();
    $("cpu-high").value = ls.settings.cpu.high.toLowerCase();
  }

  // --- Cronômetro ---------------------------------------------------------------------------

  const timerSwatches = swatchGroup($("timer-swatches"), COLORS, (hex) => {
    ls.settings.timer.color = hex;
    timerSwatches(hex);
    send();
  });
  $("timer-minutes").addEventListener("change", (e) => {
    const minutes = Math.min(180, Math.max(1, Math.round(Number(e.target.value) || 25)));
    e.target.value = minutes;
    ls.settings.timer.minutes = minutes;
    renderTimer();
    send(0);
  });
  $("timer-start").addEventListener("click", () => {
    const timer = ls.status?.timer;
    host.post({ type: "liveTimer", action: timer?.running ? "pause" : "start" });
  });
  $("timer-stop").addEventListener("click", () => host.post({ type: "liveTimer", action: "stop" }));

  function clock(ms) {
    const total = Math.max(0, Math.ceil(ms / 1000));
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = String(total % 60).padStart(2, "0");
    return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${s}` : `${m}:${s}`;
  }

  function renderTimer() {
    const timer = ls.status?.timer;
    const totalMs = ls.settings.timer.minutes * 60000;
    $("timer-minutes").value = ls.settings.timer.minutes;
    timerSwatches(ls.settings.timer.color);
    $("timer-clock").textContent = clock(timer ? timer.remainingMs : totalMs);
    const started = timer && timer.remainingMs < timer.totalMs;
    $("timer-start").textContent = timer?.running ? "Pausar" : started ? "Continuar" : "Iniciar";
    $("timer-stop").disabled = !started;
    $("timer-minutes").disabled = Boolean(started);
  }

  // --- Avisos ----------------------------------------------------------------------------------

  let alertColor = ALERT_COLORS[0][0];
  const alertSwatches = swatchGroup($("alert-swatches"), ALERT_COLORS, (hex) => {
    alertColor = hex;
    alertSwatches(hex);
  }, false);
  alertSwatches(alertColor);
  $("alert-test").addEventListener("click", () => host.post({ type: "testAlert", color: alertColor, times: 3 }));

  // --- Brilho, estado e envio ----------------------------------------------------------------------

  $("live-brightness").addEventListener("input", (e) => {
    ls.settings.brightness = Number(e.target.value);
    renderRange(e.target, ls.settings.brightness);
  });
  $("live-brightness").addEventListener("change", () => send());

  function renderState() {
    const text = $("live-state");
    const status = ls.status ?? {};
    const mode = ls.settings.mode;
    const modeInfo = ls.catalog.modes.find((m) => m.key === mode);
    let message = "";
    let kind = "info";
    if (mode === "off") {
      message = "Desligado: o teclado mostra o efeito ou as cores escolhidas. Escolha um modo acima.";
    } else if (ls.channel === "dongle" && !modeInfo?.dongle) {
      message = "Este modo só funciona pelo cabo USB. Pelo dongle, use o ritmo musical.";
      kind = "warn";
    } else if (status.lightOverride === "off") {
      message = "Pausado por uma regra de luz (bloqueio, tela ou horário noturno).";
    } else if (status.paused) {
      message = "Luz desligada ou simulação: a prévia continua, o teclado não recebe nada.";
    } else if (status.state === "sending") {
      message = ls.channel === "dongle" ? "No teclado, desenhado pelo próprio firmware." : "No teclado agora, pelo cabo.";
      kind = "ok";
    } else if (status.state === "no-keyboard") {
      message = "Teclado não encontrado. Conecte o cabo ou o dongle.";
      kind = "warn";
    } else if (status.state === "wrong-channel") {
      message = "Este modo só funciona pelo cabo USB.";
      kind = "warn";
    } else if (status.state === "failing") {
      message = `O teclado parou de responder${status.detail ? `: ${status.detail}` : "."} Tentando de novo…`;
      kind = "warn";
    } else {
      message = "Começando…";
    }
    text.textContent = message;
    text.dataset.kind = kind;
  }

  let sendTimer;
  function send(delay = 200) {
    clearTimeout(sendTimer);
    sendTimer = setTimeout(() => host.post({ type: "setLive", live: structuredClone(ls.settings) }), delay);
  }

  function render() {
    if (!ls.settings || !ls.catalog) return;
    renderModeSelection();
    renderRange($("live-brightness"), ls.settings.brightness);
    renderScene();
    renderMusic();
    renderOthers();
    renderTimer();
    renderState();
    renderBoard();
  }

  // A prévia sem quadros novos (modo desligado) volta à cena desenhada
  setInterval(() => {
    if (ls.frame && performance.now() - ls.frameAt > 1500) {
      ls.frame = null;
      renderBoard();
    }
  }, 1000);

  return {
    /** Bloco "live" da mensagem state. */
    load(live) {
      if (!live || ls.painting) return;
      const first = !ls.catalog;
      ls.catalog = live.catalog;
      ls.settings = structuredClone(live.settings);
      ls.status = live.status;
      if (first) renderModes();
      render();
    },
    /** Quadro que o app está mandando ao teclado (índice do LED → #RRGGBB). */
    frame(colors) {
      if (ls.painting) return;
      ls.frame = colors;
      ls.frameAt = performance.now();
      renderBoard();
    },
    status(status) {
      ls.status = status;
      if (!ls.settings) return;
      if (status.mode !== ls.settings.mode) {
        // O modo mudou fora da página (atalho, linha de comando, cronômetro)
        ls.settings.mode = status.mode;
        renderModeSelection();
      }
      renderTimer();
      renderState();
    },
    device(device) {
      ls.channel = device?.connected ? device.channel : null;
      if (ls.settings) {
        renderModeSelection();
        renderState();
      }
    },
  };
}
