// The loading screen waits for the 3D world, for real. node --test tools/site-loading.test.mjs
// 9 Nov. 2026: the owner saw the loading screen go before the 3D scenes were there. It went on a timer (1.1 s on a phone,
// 6 s on a PC) whatever the world was doing; measured with a slower CPU and network, the world came up to 12 s after it
// (lab/evidence/session-6/loading-before.json). These tests load the home, a guide and the 404 in Chrome with the CPU and
// the network slowed down, on a PC and on a phone, and check the screen goes only once the world has drawn its first frame;
// and that it never holds the page without WebGL, with reduced motion, when the 3D script fails or when the network stalls.
// SITE_DIR=<folder> runs them on another copy (the entry copy fails the phone cases). Needs Playwright with Chrome or its
// Chromium (PLAYWRIGHT=<folder>, the repo, the global npm); without it the browser tests are skipped, never passed.
import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = resolve(process.env.SITE_DIR || join(ROOT, 'site'));
const BASE = '/cs2-ip-changer';
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.woff2': 'font/woff2', '.json': 'application/json' };
const GUIDES = readdirSync(SITE).filter((n) => existsSync(join(SITE, n, 'index.html')));
const PAGES = ['index.html', '404.html', ...GUIDES.map((g) => `${g}/index.html`)];
const read = (f) => readFileSync(join(SITE, f), 'utf8').replace(/\r\n/g, '\n');
const headBlock = (s) => (/<script>\n\/\* the loading screen:[\s\S]*?<\/script>/.exec(s) || [''])[0];

test('Loading: the same loading screen on the home, the 404 and every guide, and in the guide builder', () => {
  const home = headBlock(read('index.html'));
  assert.ok(home, 'the home has a loading screen');
  for (const p of PAGES) assert.equal(headBlock(read(p)), home, `${p}: its loading screen differs from the home's`);
  if (!process.env.SITE_DIR) {
    const builder = readFileSync(join(ROOT, 'tools/site-pages.mjs'), 'utf8').replace(/\r\n/g, '\n');
    assert.ok(builder.includes(home), 'tools/site-pages.mjs writes another loading screen than the home has');
  }
});

test('Loading: no timer ends it while the world is loading, and the 3D script it counts is the size it says', () => {
  const home = headBlock(read('index.html'));
  // the old ending: a fixed timer, 1.1 s on a phone and 6 s on a PC
  assert.doesNotMatch(home, /setTimeout\(end,matchMedia\('\(pointer:coarse\)'\)/, 'the loading screen still ends on a fixed timer');
  assert.match(home, /addEventListener\('worldready',end,true\)/, 'the loading screen does not end on the world\'s first frame');
  for (const p of PAGES) {
    const s = read(p), m = /__ldGet\('([^']+)',(\d+),/.exec(s);
    assert.ok(m, `${p}: the 3D script is not read through __ldGet (its bytes would not move the line)`);
    const file = join(SITE, m[1].replace(/^\/cs2-ip-changer\//, '').replace(/^(\.\.\/)+/, ''));
    const real = statSync(existsSync(file) ? file : join(SITE, p, '..', m[1])).size;
    assert.ok(Math.abs(+m[2] - real) / real < 0.05, `${p}: __ldGet says ${m[2]} bytes, ${m[1]} is ${real}: the line would fill too early or too late`);
  }
});

async function findChromium() {
  const where = [process.env.PLAYWRIGHT, ROOT, 'C:/dev/puredeploy-site-v2'];
  const g = spawnSync('npm root -g', { encoding: 'utf8', shell: true });
  if (g.status === 0 && g.stdout.trim()) where.push(g.stdout.trim(), join(g.stdout.trim(), '@playwright', 'cli'));
  for (const w of where.filter(Boolean)) {
    try {
      const m = await import(pathToFileURL(createRequire(join(resolve(w), 'x.js')).resolve('playwright')).href);
      return m.chromium || m.default.chromium;
    } catch { /* not there */ }
  }
  return null;
}

let browser = null, server = null, origin = '', why = '';
const stall = new Set(); // paths the server never answers (a stalled network)
before(async () => {
  const chromium = await findChromium();
  if (!chromium) { why = 'no Playwright (PLAYWRIGHT=<folder with it>)'; return; }
  const gpu = ['--enable-gpu', '--ignore-gpu-blocklist'];
  for (const o of [{ channel: 'chrome', args: gpu }, { args: gpu }]) {
    try { browser = await chromium.launch(o); break; } catch (e) { why = `Chromium did not start: ${e.message.split('\n')[0]}`; }
  }
  if (!browser) return;
  server = createServer((q, s) => {
    const p = decodeURIComponent(q.url.split('?')[0]);
    if (stall.has(p)) return; // never answers
    if (!p.startsWith(BASE)) { s.writeHead(404); return s.end(); }
    let f = resolve(SITE, '.' + (p.slice(BASE.length) || '/'));
    if (f !== SITE && !f.startsWith(SITE + sep)) { s.writeHead(404); return s.end(); }
    if (existsSync(f) && statSync(f).isDirectory()) f = join(f, 'index.html');
    if (!existsSync(f) && /\/img\/[\w-]+\.(png|webp)$/.test(p)) for (const d of ['docs/screenshots', 'docs/assets']) { const c = join(ROOT, d, p.split('/').pop().replace(/\.webp$/, '.png')); if (existsSync(c)) { f = c; break; } }
    if (!existsSync(f)) { s.writeHead(404); return s.end(); }
    s.writeHead(200, { 'content-type': TYPES[extname(f)] || 'application/octet-stream', 'cache-control': 'no-store' });
    s.end(readFileSync(f));
  });
  await new Promise((ok) => server.listen(0, '127.0.0.1', ok));
  origin = `http://127.0.0.1:${server.address().port}${BASE}/`;
});
after(async () => { await browser?.close(); server?.close(); });

// in the page from its first line: when the screen starts to go, what the world had done by then, the line's steps
const PROBE = () => {
  const T = (window.__lt = { line: [] });
  const now = () => performance.now();
  new MutationObserver(() => {
    const h = document.documentElement; if (!h) return;
    if (h.classList.contains('ld')) T.on = T.on ?? now();
    if (h.classList.contains('ld-out') && T.out == null) { T.out = now(); T.outWorld = T.world != null; T.outReady = !!document.querySelector('.stage.ready,.gw.ready'); T.ldw = h.classList.contains('ldw'); }
    if (T.on != null && !h.classList.contains('ld') && T.gone == null) T.gone = now();
    const b = document.querySelector('.ld-bar b');
    if (b && !T.bar) { T.bar = 1; new MutationObserver(() => { const m = /scaleX\(([\d.]+)\)/.exec(b.style.transform); if (m) T.line.push([now(), +m[1]]); }).observe(b, { attributes: true, attributeFilter: ['style'] }); }
  }).observe(document, { attributes: true, childList: true, subtree: true, attributeFilter: ['class'] });
  document.addEventListener('worldready', () => { T.world = T.world ?? now(); }, true);
};
const PHONE = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true };
const PC = { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 };

async function load(t, path, ctxOpts, { cpu = 4, net = { latency: 60, down: 9e6 } } = {}, extra) {
  const ctx = await browser.newContext(ctxOpts);
  t.after(() => ctx.close());
  const page = await ctx.newPage();
  await page.addInitScript(PROBE);
  if (extra) await extra(page);
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.setCacheDisabled', { cacheDisabled: true });
  if (net) await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: net.latency, downloadThroughput: net.down / 8, uploadThroughput: 1.5e6 / 8 });
  if (cpu > 1) await cdp.send('Emulation.setCPUThrottlingRate', { rate: cpu });
  await page.goto(origin + path, { waitUntil: 'commit' });
  return page;
}
const gone = (page, ms) => page.waitForFunction(() => window.__lt && window.__lt.gone != null, null, { timeout: ms, polling: 100 });

for (const [label, ctxOpts] of [['a PC', PC], ['a phone', PHONE]]) {
  for (const [what, path] of [['the home', ''], ['a guide', 'cs2-ip-ban/'], ['the 404', '404.html']]) {
    test(`Loading: on ${label}, ${what} keeps the loading screen until the world has drawn (CPU 4x slower, 9 Mbit/s)`, async (t) => {
      if (!browser) return t.skip(why);
      const page = await load(t, path, ctxOpts);
      await gone(page, 60000);
      const T = await page.evaluate(() => window.__lt);
      assert.ok(T.on != null, 'no loading screen at all');
      assert.ok(T.outWorld && T.ldw, `the loading screen began to go ${Math.round(T.out - T.on)} ms in, before the world's first frame (worldready ${T.world == null ? 'not yet' : Math.round(T.world) + ' ms'})`);
      assert.ok(T.outReady, 'the scene was not marked ready when the screen went: the poster would still show');
      // the line only goes forward, and it moved while the 3D script came in (not one jump at the end)
      for (let i = 1; i < T.line.length; i++) assert.ok(T.line[i][1] >= T.line[i - 1][1], 'the line went back');
      const mid = T.line.filter(([, v]) => v > 0.16 && v < 0.47).length;
      assert.ok(mid >= 3, `the line did not follow the 3D script's bytes (${mid} steps between 16 and 47 %)`);
    });
  }
}

// the world off screen when the page opens: a phone on its side (the guide's world is below the fold) and the home opened
// at its FAQ (the stage is off). The world is hidden there, and until 9 Nov. it drew nothing until scrolled to, so it never
// said it was ready: the screen would have waited 12 s for it
for (const [what, path, ctxOpts] of [['a guide on a phone on its side', 'cs2-ip-ban/', { ...PHONE, viewport: { width: 844, height: 390 } }], ['the home opened at its Setup', '#setup', PC]]) {
  test(`Loading: ${what}: the world draws its first frame off screen and the screen goes with it`, async (t) => {
    if (!browser) return t.skip(why);
    const page = await load(t, path, ctxOpts, { cpu: 1, net: null });
    await gone(page, 20000);
    const T = await page.evaluate(() => window.__lt);
    assert.ok(T.outWorld && T.ldw, `the screen went after ${Math.round(T.out - T.on)} ms without the world's first frame`);
    assert.ok(T.out - T.on < 9000, `the screen stayed ${Math.round(T.out - T.on)} ms`);
  });
}

test('Loading: with reduced motion the screen never holds the page (gone within 2 s with the CPU 6x slower, the wheel is not stopped)', async (t) => {
  if (!browser) return t.skip(why);
  const page = await load(t, '', { ...PHONE, reducedMotion: 'reduce' }, { cpu: 6, net: { latency: 150, down: 1.6e6 } });
  const held = await page.waitForFunction(() => document.body && document.documentElement.classList.contains('ld') ? (() => { const e = new WheelEvent('wheel', { cancelable: true, bubbles: true, deltaY: 100 }); document.body.dispatchEvent(e); return { prevented: e.defaultPrevented }; })() : null, null, { timeout: 5000, polling: 50 }).then((h) => h.jsonValue()).catch(() => ({ prevented: false, never: true }));
  assert.equal(held.prevented, false, 'the wheel is held under the loading screen with reduced motion');
  await gone(page, 4000);
  const T = await page.evaluate(() => window.__lt);
  if (T.on != null) assert.ok(T.out - T.on < 2000, `the loading screen stayed ${Math.round(T.out - T.on)} ms with reduced motion`);
});

test('Loading: without WebGL there is no loading screen and the cover shows at once', async (t) => {
  if (!browser) return t.skip(why);
  const page = await load(t, '', PC, { cpu: 1, net: null }, (p) => p.addInitScript(() => {
    const g = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function (k, ...a) { return /webgl/i.test(k) ? null : g.call(this, k, ...a); };
  }));
  await page.waitForLoadState('load');
  await page.waitForTimeout(400);
  const T = await page.evaluate(() => ({ on: window.__lt.on ?? null, ld: document.documentElement.classList.contains('ld'), h1: getComputedStyle(document.querySelector('h1')).visibility }));
  assert.equal(T.on, null, 'a loading screen showed without WebGL');
  assert.equal(T.ld, false);
  assert.equal(T.h1, 'visible');
});

test('Loading: when the 3D script fails the screen goes at once; when the network stalls, after 12 s with nothing new', async (t) => {
  if (!browser) return t.skip(why);
  const failed = await load(t, '', PC, { cpu: 1, net: null }, (p) => p.route(/scenes\.min\.js$/, (r) => r.abort()));
  await gone(failed, 6000);
  const F = await failed.evaluate(() => ({ ...window.__lt, noGl: document.documentElement.classList.contains('no-gl') }));
  assert.ok(F.noGl, 'the page was not told the world failed (no-gl)');
  assert.ok(F.gone - F.on < 3000, `the loading screen stayed ${Math.round(F.gone - F.on)} ms after the 3D script failed`);
  stall.add(`${BASE}/assets/guides/world.min.js`);
  t.after(() => stall.delete(`${BASE}/assets/guides/world.min.js`));
  const stalled = await load(t, 'cs2-ip-ban/', PC, { cpu: 1, net: null });
  await gone(stalled, 25000);
  const S = await stalled.evaluate(() => window.__lt);
  assert.ok(S.gone - S.on > 11000 && S.gone - S.on < 16000, `with the 3D script stalled the screen went after ${Math.round(S.gone - S.on)} ms (12 s without news expected)`);
});
