// Gera os ícones do AkiraCore a partir de um desenho só: teclado de 3 fileiras
// (5 teclas, 5 teclas, tecla + espaço + tecla), igual em todos os tamanhos.
//
//   node tools/icones/gerar-icones.mjs
//
// Saída em src/AkiraCore.App/Assets:
//   app.svg          ícone geral (vetor, 256 × 256): teclas em arco-íris num quadrado escuro
//   tray.svg         ícone da bandeja (vetor, 16 × 16, cor do texto)
//   AkiraCore.ico    executável, janela, barra de tarefas e Alt + Tab
//   tray-white.ico   bandeja com a barra de tarefas escura
//   tray-black.ico   bandeja com a barra de tarefas clara
//
// Cada tamanho do .ico é desenhado de novo com medidas inteiras (tecla e espaço em pixels
// cheios), por isso 16, 24 e 32 saem nítidos em vez de reduzidos do 256.
// Quem rasteriza é o Edge headless (já vem no Windows), pelo protocolo de depuração.
import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const out = path.join(root, "src", "AkiraCore.App", "Assets");

// Tamanhos que o Windows pede: 16 a 48 (e 20/30/36/40 com escala de 125% a 300%), 256 no Explorer
const APP_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 64, 96, 256];
const TRAY_SIZES = [16, 20, 24, 32, 40, 48];

// Mesmo arco-íris da prévia, da esquerda para a direita
const RAINBOW = [
  [0, "#ff375f"], [0.2, "#ff9f0a"], [0.36, "#ffd60a"], [0.52, "#30d158"],
  [0.68, "#64d2ff"], [0.84, "#0a84ff"], [1, "#bf5af2"],
];

// --- Geometria -----------------------------------------------------------------

/**
 * Escolhe tecla (k) e espaço (g) inteiros para ocupar perto de `span` pixels com 5 teclas,
 * preferindo a proporção 3:1 do desenho grande e margens iguais dos dois lados.
 */
function fitKeys(area, span) {
  let best = null;
  for (let k = 2; k <= area; k++) {
    for (let g = 1; g <= k; g++) {
      const w = 5 * k + 4 * g;
      const h = 3 * k + 2 * g;
      if (w > area) continue;
      const score = Math.abs(w - span)
        + 2 * k * Math.abs(g / k - 1 / 3)
        + ((area - w) % 2 ? 0.75 : 0)
        + ((area - h) % 2 ? 0.5 : 0);
      if (!best || score < best.score) best = { k, g, w, h, score };
    }
  }
  return best;
}

/** Retângulos das 13 teclas, centralizados num quadrado de lado `size` a partir de `origin`. */
function keyRects(origin, size, k, g) {
  const w = 5 * k + 4 * g;
  const h = 3 * k + 2 * g;
  const x0 = origin + Math.floor((size - w) / 2);
  const y0 = origin + Math.floor((size - h) / 2);
  const col = (i) => x0 + i * (k + g);
  const row = (j) => y0 + j * (k + g);
  const rects = [];
  for (let j = 0; j < 2; j++) {
    for (let i = 0; i < 5; i++) rects.push([col(i), row(j), k, k]);
  }
  // Última fileira: tecla, barra de espaço (3 teclas de largura) e tecla
  rects.push([col(0), row(2), k, k]);
  rects.push([col(1), row(2), 3 * k + 2 * g, k]);
  rects.push([col(4), row(2), k, k]);
  return { rects, x0, x1: x0 + w };
}

const num = (v) => Number(v.toFixed(2)).toString();

function rectsPath(rects, r) {
  return rects.map(([x, y, w, h]) =>
    `<rect x="${num(x)}" y="${num(y)}" width="${num(w)}" height="${num(h)}" rx="${num(r)}"/>`).join("");
}

/** Ícone do app: quadrado escuro arredondado com as teclas em arco-íris. */
function appSvg(n) {
  const inset = n >= 48 ? Math.round(n / 32) : 0;
  const tile = n - 2 * inset;
  const radius = tile * 0.225;
  const { k, g } = fitKeys(tile - 2, tile * 0.8);
  const { rects, x0, x1 } = keyRects(inset, tile, k, g);
  const keyRadius = k >= 4 ? k * 0.22 : k >= 3 ? 0.6 : 0.4;
  const detailed = n >= 48;
  const stops = RAINBOW.map(([o, c]) => `<stop offset="${o}" stop-color="${c}"/>`).join("");
  const keys = rectsPath(rects, keyRadius);
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${n}" height="${n}" viewBox="0 0 ${n} ${n}">
<defs>
<linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#323236"/><stop offset="1" stop-color="#161618"/></linearGradient>
<linearGradient id="rgb" gradientUnits="userSpaceOnUse" x1="${num(x0)}" y1="0" x2="${num(x1)}" y2="0">${stops}</linearGradient>
<linearGradient id="shine" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity="0.35"/><stop offset="0.5" stop-color="#fff" stop-opacity="0"/></linearGradient>
${detailed ? `<filter id="glow" x="-20%" y="-30%" width="140%" height="160%"><feGaussianBlur stdDeviation="${num(k * 0.4)}"/></filter>` : ""}
</defs>
<rect x="${inset}" y="${inset}" width="${tile}" height="${tile}" rx="${num(radius)}" fill="url(#tile)"/>
${detailed ? `<rect x="${num(inset + n / 512)}" y="${num(inset + n / 512)}" width="${num(tile - n / 256)}" height="${num(tile - n / 256)}" rx="${num(radius - n / 512)}" fill="none" stroke="#fff" stroke-opacity="0.1" stroke-width="${num(n / 256)}"/>` : ""}
${detailed ? `<g fill="url(#rgb)" opacity="0.75" filter="url(#glow)">${keys}</g>` : ""}
<g fill="url(#rgb)">${keys}</g>
${detailed ? `<g fill="url(#shine)">${keys}</g>` : ""}
</svg>`;
}

/** Ícone da bandeja: as mesmas teclas, numa cor só, sem fundo. */
function traySvg(n, color) {
  const { k, g } = fitKeys(n, n * 0.875);
  const { rects } = keyRects(0, n, k, g);
  const keyRadius = k >= 4 ? k * 0.2 : k >= 3 ? 0.5 : 0.35;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${n}" height="${n}" viewBox="0 0 ${n} ${n}">
<g fill="${color}">${rectsPath(rects, keyRadius)}</g>
</svg>`;
}

// --- Rasterização pelo Edge ------------------------------------------------------

function findEdge() {
  const candidates = [
    process.env.EDGE,
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    "C:/Program Files/Microsoft/Edge/Application/msedge.exe",
  ].filter(Boolean);
  const found = candidates.find((p) => fs.existsSync(p));
  if (!found) throw new Error("Edge não encontrado; defina a variável EDGE com o caminho do msedge.exe");
  return found;
}

async function openBrowser() {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), "akira-icones-"));
  const proc = spawn(findEdge(), [
    "--headless=new", "--disable-gpu", "--no-first-run", "--remote-debugging-port=0",
    `--user-data-dir=${profile}`, "about:blank",
  ], { stdio: ["ignore", "ignore", "pipe"] });
  const wsUrl = await new Promise((resolve, reject) => {
    let text = "";
    proc.stderr.on("data", (chunk) => {
      text += chunk;
      const match = /DevTools listening on (ws:\/\/\S+)/.exec(text);
      if (match) resolve(match[1]);
    });
    proc.on("exit", () => reject(new Error("O Edge fechou antes de abrir a depuração")));
  });

  const ws = new WebSocket(wsUrl);
  await new Promise((resolve) => ws.addEventListener("open", resolve, { once: true }));
  let nextId = 1;
  const pending = new Map();
  ws.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    const wait = pending.get(message.id);
    if (!wait) return;
    pending.delete(message.id);
    if (message.error) wait.reject(new Error(message.error.message));
    else wait.resolve(message.result);
  });
  const send = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params, sessionId }));
  });

  const { targetInfos } = await send("Target.getTargets");
  const page = targetInfos.find((t) => t.type === "page");
  const { sessionId } = await send("Target.attachToTarget", { targetId: page.targetId, flatten: true });

  return {
    async render(svg, n) {
      const expression = `(async () => {
        const img = new Image();
        img.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(${JSON.stringify(svg)});
        await img.decode();
        const canvas = document.createElement("canvas");
        canvas.width = canvas.height = ${n};
        const ctx = canvas.getContext("2d");
        ctx.drawImage(img, 0, 0, ${n}, ${n});
        const data = ctx.getImageData(0, 0, ${n}, ${n}).data;
        let bin = "";
        for (let i = 0; i < data.length; i++) bin += String.fromCharCode(data[i]);
        return { rgba: btoa(bin), png: canvas.toDataURL("image/png").split(",")[1] };
      })()`;
      const { result, exceptionDetails } = await send("Runtime.evaluate",
        { expression, awaitPromise: true, returnByValue: true }, sessionId);
      if (exceptionDetails) throw new Error(exceptionDetails.text);
      return { rgba: Buffer.from(result.value.rgba, "base64"), png: Buffer.from(result.value.png, "base64") };
    },
    close() {
      ws.close();
      proc.kill();
      try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* o Edge ainda segura a pasta */ }
    },
  };
}

// --- Arquivo .ico ------------------------------------------------------------------

/** Imagem do .ico em BMP de 32 bits (o que todo leitor aceita); 256 vai em PNG. */
function bmpEntry(rgba, n) {
  const header = Buffer.alloc(40);
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(n, 4);
  header.writeInt32LE(n * 2, 8); // altura dobrada: cor + máscara
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  const pixels = Buffer.alloc(n * n * 4);
  for (let y = 0; y < n; y++) {
    for (let x = 0; x < n; x++) {
      const src = (y * n + x) * 4;
      const dst = ((n - 1 - y) * n + x) * 4; // BMP guarda de baixo para cima
      pixels[dst] = rgba[src + 2];
      pixels[dst + 1] = rgba[src + 1];
      pixels[dst + 2] = rgba[src];
      pixels[dst + 3] = rgba[src + 3];
    }
  }
  const mask = Buffer.alloc(Math.ceil(n / 32) * 4 * n); // tudo zero: vale o canal alfa
  return Buffer.concat([header, pixels, mask]);
}

function writeIco(file, images) {
  const head = Buffer.alloc(6 + 16 * images.length);
  head.writeUInt16LE(1, 2);
  head.writeUInt16LE(images.length, 4);
  let offset = head.length;
  images.forEach(({ n, data }, i) => {
    const at = 6 + 16 * i;
    head.writeUInt8(n >= 256 ? 0 : n, at);
    head.writeUInt8(n >= 256 ? 0 : n, at + 1);
    head.writeUInt16LE(1, at + 4);
    head.writeUInt16LE(32, at + 6);
    head.writeUInt32LE(data.length, at + 8);
    head.writeUInt32LE(offset, at + 12);
    offset += data.length;
  });
  fs.writeFileSync(file, Buffer.concat([head, ...images.map((image) => image.data)]));
}

async function buildIco(browser, file, sizes, draw) {
  const images = [];
  for (const n of sizes) {
    const { rgba, png } = await browser.render(draw(n), n);
    images.push({ n, data: n >= 256 ? png : bmpEntry(rgba, n) });
  }
  writeIco(file, images);
  console.log(`${path.relative(root, file)}: ${sizes.join(", ")} px`);
}

// --- Principal -----------------------------------------------------------------------

fs.mkdirSync(out, { recursive: true });
fs.writeFileSync(path.join(out, "app.svg"), appSvg(256) + "\n");
fs.writeFileSync(path.join(out, "tray.svg"), traySvg(16, "currentColor") + "\n");
console.log("src/AkiraCore.App/Assets/app.svg e tray.svg");

// --previa=arquivo.html: todos os tamanhos lado a lado, em fundo claro e escuro, para conferir
const previewArg = process.argv.find((a) => a.startsWith("--previa="));
if (previewArg) {
  // Como <img>: inline, os ids dos gradientes de um tamanho valeriam para os outros
  const cells = (draw, sizes) => sizes.map((n) =>
    `<img width="${n}" height="${n}" src="data:image/svg+xml,${encodeURIComponent(draw(n))}">`).join("");
  fs.writeFileSync(previewArg.slice("--previa=".length), `<!doctype html><meta charset="utf-8">
<style>body{margin:0;font:12px sans-serif}div{display:flex;gap:16px;align-items:center;padding:16px}
.dark{background:#202020}.light{background:#f3f3f3}span{display:inline-flex}</style>
<div class="light">${cells(appSvg, APP_SIZES)}</div><div class="dark">${cells(appSvg, APP_SIZES)}</div>
<div class="dark">${cells((n) => traySvg(n, "#fff"), TRAY_SIZES)}</div>
<div class="light">${cells((n) => traySvg(n, "#1d1d1f"), TRAY_SIZES)}</div>`);
}

const browser = await openBrowser();
try {
  await buildIco(browser, path.join(out, "AkiraCore.ico"), APP_SIZES, appSvg);
  await buildIco(browser, path.join(out, "tray-white.ico"), TRAY_SIZES, (n) => traySvg(n, "#ffffff"));
  await buildIco(browser, path.join(out, "tray-black.ico"), TRAY_SIZES, (n) => traySvg(n, "#1d1d1f"));
} finally {
  browser.close();
}
