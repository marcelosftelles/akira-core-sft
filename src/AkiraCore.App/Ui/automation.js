// Ajustes de automação: regras de luz e atalhos globais. Só estado de tela: a validação fica no
// Core (LightRules.cs, Hotkeys.cs) e o registro no Windows no app.
// Mensagens: setRules {rules}, setHotkeys {hotkeys}. Recebe o bloco automation {rules, hotkeys,
// hotkeyActions} da mensagem state (ou a mensagem automation, quando o app recusa uma mudança).

const $ = (id) => document.getElementById(id);
// Bits do RegisterHotKey: Alt 1, Ctrl 2, Shift 4, Win 8
const MOD = { alt: 1, ctrl: 2, shift: 4, win: 8 };
const MODIFIER_CODES = ["ControlLeft", "ControlRight", "ShiftLeft", "ShiftRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight"];

function describe(hotkey) {
  const parts = [];
  if (hotkey.modifiers & MOD.ctrl) parts.push("Ctrl");
  if (hotkey.modifiers & MOD.alt) parts.push("Alt");
  if (hotkey.modifiers & MOD.shift) parts.push("Shift");
  if (hotkey.modifiers & MOD.win) parts.push("Win");
  const key = hotkey.key;
  const label = key.startsWith("Key") ? key.slice(3)
    : key.startsWith("Digit") ? key.slice(5)
    : key.startsWith("Numpad") ? `Num ${key.slice(6)}`
    : { ArrowUp: "↑", ArrowDown: "↓", ArrowLeft: "←", ArrowRight: "→", Space: "Espaço" }[key] ?? key;
  parts.push(label);
  return parts.join(" + ");
}

export function createAutomationSettings(host) {
  const as = { rules: null, hotkeys: [], actions: [], capturing: null };

  // --- Regras de luz --------------------------------------------------------------------

  function sendRules() {
    host.post({ type: "setRules", rules: { ...as.rules } });
  }

  for (const [id, key] of [["rule-lock", "offWhenLocked"], ["rule-display", "offWhenDisplayOff"], ["rule-night", "night"]]) {
    $(id).addEventListener("change", (e) => {
      as.rules[key] = e.target.checked;
      renderRules();
      sendRules();
    });
  }
  for (const [id, key] of [["rule-start", "nightStart"], ["rule-end", "nightEnd"]]) {
    $(id).addEventListener("change", (e) => {
      if (!/^\d\d:\d\d$/.test(e.target.value)) {
        e.target.value = as.rules[key];
        return;
      }
      as.rules[key] = e.target.value;
      sendRules();
    });
  }
  for (const button of document.querySelectorAll("#rule-action button")) {
    button.addEventListener("click", () => {
      as.rules.nightAction = button.dataset.action;
      renderRules();
      sendRules();
    });
  }

  function renderRules() {
    const r = as.rules;
    $("rule-lock").checked = r.offWhenLocked;
    $("rule-display").checked = r.offWhenDisplayOff;
    $("rule-night").checked = r.night;
    $("rule-start").value = r.nightStart;
    $("rule-end").value = r.nightEnd;
    for (const button of document.querySelectorAll("#rule-action button")) {
      button.setAttribute("aria-checked", String(button.dataset.action === r.nightAction));
    }
    $("rule-night-time").setAttribute("aria-disabled", String(!r.night));
    $("rule-night-action").setAttribute("aria-disabled", String(!r.night));
  }

  // --- Atalhos globais --------------------------------------------------------------------

  function sendHotkeys() {
    host.post({ type: "setHotkeys", hotkeys: as.hotkeys.map((h) => ({ ...h })) });
  }

  function onCapture(e) {
    if (!as.capturing) return;
    e.preventDefault();
    e.stopPropagation();
    if (e.type !== "keydown" || e.repeat) return;
    if (e.code === "Escape" && !e.ctrlKey && !e.altKey && !e.metaKey) {
      stopCapture();
      return;
    }
    if (MODIFIER_CODES.includes(e.code)) return; // espera a tecla de verdade
    const modifiers = (e.altKey ? MOD.alt : 0) | (e.ctrlKey ? MOD.ctrl : 0) | (e.shiftKey ? MOD.shift : 0) | (e.metaKey ? MOD.win : 0);
    const action = as.capturing;
    stopCapture();
    as.hotkeys = as.hotkeys.filter((h) => h.action !== action);
    as.hotkeys.push({ action, modifiers, key: e.code });
    renderHotkeys();
    sendHotkeys();
  }

  function startCapture(action) {
    stopCapture();
    as.capturing = action;
    window.addEventListener("keydown", onCapture, true);
    window.addEventListener("keyup", onCapture, true);
    renderHotkeys();
    document.querySelector(`[data-hotkey="${action}"]`)?.focus();
  }

  function stopCapture() {
    if (!as.capturing) return;
    as.capturing = null;
    window.removeEventListener("keydown", onCapture, true);
    window.removeEventListener("keyup", onCapture, true);
    renderHotkeys();
  }

  function renderHotkeys() {
    $("hotkey-list").replaceChildren(...as.actions.map((action) => {
      const current = as.hotkeys.find((h) => h.action === action.key);
      const capture = document.createElement("button");
      capture.type = "button";
      capture.className = `button hotkey${as.capturing === action.key ? " recording" : ""}`;
      capture.dataset.hotkey = action.key;
      capture.textContent = as.capturing === action.key ? "Aperte o atalho…" : current ? describe(current) : "Definir…";
      capture.addEventListener("click", () => (as.capturing === action.key ? stopCapture() : startCapture(action.key)));
      const controls = document.createElement("span");
      controls.className = "inline nowrap";
      controls.append(capture);
      if (current) {
        const clear = document.createElement("button");
        clear.type = "button";
        clear.className = "icon-button";
        clear.textContent = "×";
        clear.setAttribute("aria-label", `Tirar o atalho de "${action.label}"`);
        clear.addEventListener("click", () => {
          as.hotkeys = as.hotkeys.filter((h) => h.action !== action.key);
          renderHotkeys();
          sendHotkeys();
        });
        controls.append(clear);
      }
      const label = document.createElement("span");
      label.className = "label";
      label.textContent = action.label;
      const row = document.createElement("div");
      row.className = "row";
      row.append(label, controls);
      return row;
    }));
  }

  // --- Linha de comando ----------------------------------------------------------------------

  $("cli-copy").addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText("AkiraCore.exe --efeito rolando --cor azul --brilho 3");
      $("cli-copy").textContent = "Copiado";
      setTimeout(() => { $("cli-copy").textContent = "Copiar exemplo"; }, 1500);
    } catch {
      $("cli-copy").textContent = "Não deu para copiar";
    }
  });

  return {
    /** Bloco "automation" da mensagem state. */
    load(automation) {
      if (!automation) return;
      as.rules = { ...automation.rules };
      as.hotkeys = (automation.hotkeys ?? []).map((h) => ({ ...h }));
      as.actions = automation.hotkeyActions ?? [];
      renderRules();
      renderHotkeys();
    },
    /** Ao sair da página, para a captura de atalho. */
    leave() { stopCapture(); },
  };
}
