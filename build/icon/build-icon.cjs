// Gera src/AkiraCore.App/Assets/akira.ico a partir do akira.svg, renderizando no Chromium.
// Tamanhos pequenos em BMP de 32 bits (o formato mais compatível com Windows e NSIS) e 256 em PNG.
// Uso: node build/icon/build-icon.cjs   (precisa do pacote "playwright" e de um Chromium)
const fs = require('fs');
const path = require('path');

function loadPlaywright() {
  for (const candidate of ['playwright', '/opt/node-tools/node_modules/playwright']) {
    try { return require(candidate); } catch { /* tenta o próximo */ }
  }
  throw new Error('Instale o playwright: npm i -D playwright');
}

const root = path.resolve(__dirname, '..', '..');
const svgPath = path.join(root, 'src/AkiraCore.App/Assets/akira.svg');
const icoPath = path.join(root, 'src/AkiraCore.App/Assets/akira.ico');
const SIZES = [16, 20, 24, 32, 40, 48, 64, 256];

(async () => {
  const { chromium } = loadPlaywright();
  const browser = await chromium.launch();
  const page = await browser.newPage();
  // base64 calculado aqui: o btoa do navegador estragaria os acentos (UTF-8)
  const svg = fs.readFileSync(svgPath).toString('base64');
  const images = await page.evaluate(async ({ svg, sizes }) => {
    const img = new Image();
    img.src = 'data:image/svg+xml;base64,' + svg;
    await img.decode();
    return sizes.map((size) => {
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = size;
      const ctx = canvas.getContext('2d');
      ctx.imageSmoothingQuality = 'high';
      ctx.drawImage(img, 0, 0, size, size);
      return {
        size,
        rgba: Array.from(ctx.getImageData(0, 0, size, size).data),
        png: canvas.toDataURL('image/png').split(',')[1],
      };
    });
  }, { svg, sizes: SIZES });
  await browser.close();

  const entries = images.map(({ size, rgba, png }) =>
    ({ size, data: size >= 256 ? Buffer.from(png, 'base64') : dib(size, rgba) }));

  // ICONDIR + ICONDIRENTRY[] + imagens
  const header = Buffer.alloc(6 + 16 * entries.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(entries.length, 4);
  let offset = header.length;
  entries.forEach(({ size, data }, i) => {
    const at = 6 + 16 * i;
    header.writeUInt8(size >= 256 ? 0 : size, at);
    header.writeUInt8(size >= 256 ? 0 : size, at + 1);
    header.writeUInt8(0, at + 2);
    header.writeUInt8(0, at + 3);
    header.writeUInt16LE(1, at + 4);
    header.writeUInt16LE(32, at + 6);
    header.writeUInt32LE(data.length, at + 8);
    header.writeUInt32LE(offset, at + 12);
    offset += data.length;
  });
  fs.writeFileSync(icoPath, Buffer.concat([header, ...entries.map((e) => e.data)]));
  console.log(`akira.ico: ${entries.length} tamanhos, ${offset} bytes`);
})();

// BMP de ícone: BITMAPINFOHEADER com altura dobrada, BGRA de baixo para cima e máscara AND zerada
function dib(size, rgba) {
  const maskRow = Math.ceil(size / 32) * 4;
  const info = Buffer.alloc(40);
  info.writeUInt32LE(40, 0);
  info.writeInt32LE(size, 4);
  info.writeInt32LE(size * 2, 8);
  info.writeUInt16LE(1, 12);
  info.writeUInt16LE(32, 14);
  info.writeUInt32LE(0, 16);
  info.writeUInt32LE(size * size * 4 + maskRow * size, 20);
  const pixels = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const src = ((size - 1 - y) * size + x) * 4;
      const dst = (y * size + x) * 4;
      pixels[dst] = rgba[src + 2];
      pixels[dst + 1] = rgba[src + 1];
      pixels[dst + 2] = rgba[src];
      pixels[dst + 3] = rgba[src + 3];
    }
  }
  return Buffer.concat([info, pixels, Buffer.alloc(maskRow * size)]);
}
