// The app's logo: every file from one drawing (tools/logo-shape.mjs) and three colours of src/Theme.xaml.
// node tools/logo.mjs [--out <folder>]
//   writes docs/assets/: logo.svg (master), logo-black.svg, logo-white.svg, logo-16.svg and logo-24.svg (the small sizes,
//   fitted to whole pixels), icon.png (256, the README and the site), app.ico, favicon.svg, favicon.ico,
//   apple-touch-icon.png; and copies app.ico to src/app.ico. --out <folder> is a trial that writes only there.
// Needs Playwright (PLAYWRIGHT=<folder with node_modules/playwright> when not the repo or the global npm) and paper.js
// 0.12.18 for the shape (PAPER=<path to paper-core.min.js>; npm i paper@0.12.18 in any folder).
// Design, the rounds and the critics: docs/DESIGN.md (Logo).
import { createRequire } from 'node:module';
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { execSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { build, svg as draw, G, FIT } from './logo-shape.mjs';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const trial = args.includes('--out') ? resolve(args[args.indexOf('--out') + 1]) : null;
const OUT = trial || join(ROOT, 'docs', 'assets');
mkdirSync(OUT, { recursive: true });

function playwright() {
  const tries = [process.env.PLAYWRIGHT, ROOT];
  try { tries.push(join(execSync('npm root -g').toString().trim(), '..')); } catch {}
  for (const t of tries.filter(Boolean)) {
    try { return createRequire(join(t, 'package.json'))('playwright'); } catch {}
  }
  throw new Error('Playwright not found: npm i playwright (and npx playwright install chromium), or set PLAYWRIGHT=<folder>');
}
const { chromium } = playwright();

// the colours: ground AccentColor, sky AccentDeepColor, the point Top3 (chosen by the owner on 6 Nov 2026)
const theme = readFileSync(join(ROOT, 'src', 'Theme.xaml'), 'utf8');
const key = (k) => {
  const m = theme.match(new RegExp(`<Color x:Key="${k}">#([0-9A-Fa-f]{6})</Color>`));
  if (!m) throw new Error(`Theme.xaml has no ${k}`);
  return '#' + m[1].toLowerCase();
};
const COLORS = [key('AccentColor'), key('AccentDeepColor'), key('Top3')];
const BG = key('BgColor');
let SHAPE; const SHAPES = {};
const svg = (fills, opts = {}) => draw(opts.px && SHAPES[opts.px] ? SHAPES[opts.px] : SHAPE, fills, opts);
const color = () => COLORS;
const mono = (c) => [c, c, c];


async function raster(page, src, size) {
  return page.evaluate(async ([src, size]) => {
    const img = new Image();
    img.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(src)));
    await img.decode();
    const c = Object.assign(document.createElement('canvas'), { width: size, height: size });
    const x = c.getContext('2d');
    x.drawImage(img, 0, 0, size, size);
    return { png: c.toDataURL('image/png').split(',')[1], rgba: Array.from(x.getImageData(0, 0, size, size).data) };
  }, [src, size]);
}

// .ico: 32-bit DIB up to 64 px (every Windows API reads them), PNG for 256.
function ico(frames) {
  const head = Buffer.alloc(6 + 16 * frames.length);
  head.writeUInt16LE(1, 2); head.writeUInt16LE(frames.length, 4);
  const blobs = frames.map(({ size, png, rgba }) => {
    if (size >= 256) return Buffer.from(png, 'base64');
    const bih = Buffer.alloc(40);
    bih.writeUInt32LE(40, 0); bih.writeInt32LE(size, 4); bih.writeInt32LE(size * 2, 8);
    bih.writeUInt16LE(1, 12); bih.writeUInt16LE(32, 14); bih.writeUInt32LE(size * size * 4, 20);
    const px = Buffer.alloc(size * size * 4);
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
      const s = (y * size + x) * 4, d = ((size - 1 - y) * size + x) * 4; // bottom-up rows, BGRA
      px[d] = rgba[s + 2]; px[d + 1] = rgba[s + 1]; px[d + 2] = rgba[s]; px[d + 3] = rgba[s + 3];
    }
    return Buffer.concat([bih, px, Buffer.alloc(Math.ceil(size / 32) * 4 * size)]); // AND mask 0: alpha decides
  });
  let off = head.length;
  frames.forEach(({ size }, i) => {
    const e = 6 + 16 * i;
    head.writeUInt8(size >= 256 ? 0 : size, e); head.writeUInt8(size >= 256 ? 0 : size, e + 1);
    head.writeUInt16LE(1, e + 4); head.writeUInt16LE(32, e + 6);
    head.writeUInt32LE(blobs[i].length, e + 8); head.writeUInt32LE(off, e + 12);
    off += blobs[i].length;
  });
  return Buffer.concat([head, ...blobs]);
}

const b = await chromium.launch();
const page = await b.newPage();
await page.setContent('<!doctype html><body></body>');
SHAPE = await build(page);
for (const [px, f] of Object.entries(FIT)) SHAPES[px] = await build(page, { ...G, ...f });
const png = (r) => Buffer.from(r.png, 'base64');
const T = { title: 'CS2 IP Changer' };

writeFileSync(join(OUT, 'logo.svg'), svg(color(), T));
writeFileSync(join(OUT, 'logo-black.svg'), svg(mono('#000000'), T));
writeFileSync(join(OUT, 'logo-white.svg'), svg(mono('#ffffff'), T));
for (const px of Object.keys(FIT)) writeFileSync(join(OUT, `logo-${px}.svg`), svg(color(), { ...T, px: +px }));
const frames = {};
for (const s of [16, 20, 24, 32, 40, 48, 64, 256]) frames[s] = { size: s, ...(await raster(page, svg(color(), { px: s }), s)) };
writeFileSync(join(OUT, 'icon.png'), png(frames[256]));
writeFileSync(join(OUT, 'app.ico'), ico([16, 20, 24, 32, 40, 48, 64, 256].map((s) => frames[s])));
writeFileSync(join(OUT, 'favicon.svg'), svg(color(), T));
writeFileSync(join(OUT, 'favicon.ico'), ico([16, 32, 48].map((s) => frames[s])));
// iOS fills transparency with black and rounds the corners itself: the window colour behind, the mark at 68%.
writeFileSync(join(OUT, 'apple-touch-icon.png'), png(await raster(page, svg(color(), { bg: BG, scale: 0.68 }), 180)));
await b.close();
if (!trial) copyFileSync(join(OUT, 'app.ico'), join(ROOT, 'src', 'app.ico'));
console.log(`Logo written to ${OUT}${trial ? '' : ' and src/app.ico'}. The window's corner (src/MainWindow.xaml, SideLogo) draws logo-24.svg: change both together.`);
