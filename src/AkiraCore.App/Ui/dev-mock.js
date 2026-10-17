// Só para abrir a interface num navegador comum (sem WebView2), ex.: revisar o visual.
// Responde como o app responderia. Parâmetros na URL: view, device (usb|dongle|none),
// effect, off, rainbow, color, log, dialog.
// O catálogo é uma cópia do EffectCatalog do Core apenas para esta prévia.

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

export function createMockHost() {
  const params = new URLSearchParams(location.search);
  const listeners = [];
  const emit = (message) => setTimeout(() => listeners.forEach((fn) => fn(structuredClone(message))), 0);
  const deviceParam = params.get("device") ?? "usb";
  const device = deviceParam === "none"
    ? { connected: false, channel: null, name: "" }
    : { connected: true, channel: deviceParam === "dongle" ? "dongle" : "usb", name: "AKIRA CORE" };

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
          options: { swapRedBlue: false, simulate: false },
          device,
        });
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
        break;
      case "apply":
      case "setLight":
      case "factoryReset":
        emit({ type: "status", kind: "busy", text: "Enviando…" });
        setTimeout(() => emit({ type: "status", kind: "ok", text: "Aplicado" }), 350);
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
