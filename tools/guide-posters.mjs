// The guides' 3D band on a phone, as a picture: site/img/gp-<slug>.webp, shown under the canvas until the guide's piece of the
// world is ready (27 Nov. 2026: on the owner's S21 the band stayed empty for 4 to 5 s). Each is the band itself on a 390 px
// phone at 2x, taken once the world says it is ready, without the veil and the labels (the page draws those over it).
//   node tools/guide-posters.mjs            every guide
//   node tools/guide-posters.mjs cs2-ip-ban only those
// Run it after a change to the 3D world, the guides' scenes or the Connection capture (the monitor shows it).
// Needs Playwright (PLAYWRIGHT=<folder>, the repo or the global npm) and ffmpeg on the PATH.
import { createServer } from 'node:http';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = join(ROOT, 'site'), SHOTS = join(ROOT, 'docs', 'screenshots'), BASE = '/pickroute';
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.woff2': 'font/woff2', '.json': 'application/json' };
const LANGS = new Set(['pt-br', 'es', 'ru', 'tr']);
const want = process.argv.slice(2);
const guides = readdirSync(SITE, { withFileTypes: true }).filter((d) => d.isDirectory() && !LANGS.has(d.name) && existsSync(join(SITE, d.name, 'index.html'))
  && readFileSync(join(SITE, d.name, 'index.html'), 'utf8').includes('class="gw"')).map((d) => d.name).filter((s) => !want.length || want.includes(s));

const server = createServer((q, s) => {
  const p = decodeURIComponent(q.url.split('?')[0]);
  if (!p.startsWith(BASE)) { s.writeHead(404); return s.end(); }
  let f = resolve(SITE, '.' + (p.slice(BASE.length) || '/'));
  if (existsSync(f) && statSync(f).isDirectory()) f = join(f, 'index.html');
  // like .github/workflows/pages.yml: the captures sit in img/, each also as .webp (the png stands in for it)
  if (!existsSync(f) && p.startsWith(BASE + '/img/')) { const n = p.slice((BASE + '/img/').length); for (const c of [join(SHOTS, n), join(SHOTS, n.replace(/\.webp$/, '.png'))]) if (existsSync(c)) { f = c; break; } }
  if (!existsSync(f)) { s.writeHead(404); return s.end(); }
  s.writeHead(200, { 'content-type': TYPES[extname(f)] || 'application/octet-stream', 'cache-control': 'no-store' }); s.end(readFileSync(f));
});
await new Promise((ok) => server.listen(0, '127.0.0.1', ok));
const origin = `http://127.0.0.1:${server.address().port}${BASE}/`;

async function playwright() {
  const where = [process.env.PLAYWRIGHT, ROOT];
  const g = spawnSync('npm root -g', { encoding: 'utf8', shell: true });
  if (g.status === 0 && g.stdout.trim()) where.push(g.stdout.trim());
  for (const w of where.filter(Boolean)) {
    try { const m = await import(pathToFileURL(createRequire(join(resolve(w), 'x.js')).resolve('playwright')).href); return m.chromium || m.default.chromium; } catch { /* not there */ }
  }
  console.error('No Playwright: PLAYWRIGHT=<folder with it> node tools/guide-posters.mjs'); process.exit(1);
}
const chromium = await playwright();
const browser = await chromium.launch({ args: ['--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist'] });
const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale: 'en-US',
  userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36' });
const tmp = join(tmpdir(), 'cs2-guide-posters'); mkdirSync(tmp, { recursive: true });
for (const slug of guides) {
  const page = await ctx.newPage();
  await page.goto(origin + slug + '/', { waitUntil: 'load' });
  await page.waitForFunction(() => document.querySelector('.gw.ready'), null, { timeout: 60000 });
  // the world without what the page puts over it (veil, labels), and the band in the middle of the screen
  await page.addStyleTag({ content: '.gw::after,.gw .wd-lab,.gw .wd-labels,.top{display:none!important}.gw-poster{display:none!important}' });
  await page.waitForTimeout(2500);
  const png = join(tmp, slug + '.png');
  await page.locator('.gw canvas').screenshot({ path: png });
  const out = join(SITE, 'img', `gp-${slug}.webp`);
  const r = spawnSync('ffmpeg', ['-y', '-loglevel', 'error', '-i', png, '-c:v', 'libwebp', '-quality', '72', '-compression_level', '6', out], { stdio: 'inherit' });
  if (r.status !== 0) { console.error('ffmpeg failed (winget install Gyan.FFmpeg)'); process.exit(1); }
  console.log(`site/img/gp-${slug}.webp ${(statSync(out).size / 1024).toFixed(1)} KB`);
  await page.close();
}
await browser.close(); server.close();
