// The site in a real browser: what a visitor sees, checked on the pixels. node --test tools/site.test.mjs
// Needs Playwright with Chrome or its Chromium (found like tools/site-renders.mjs: PLAYWRIGHT=<folder>, the repo, the
// global npm); without it the tests are skipped, never passed. Beta.13 (7 Nov. 2026, the owner's points 10, 11 and 13).
import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = resolve(process.env.SITE_DIR || join(ROOT, 'site'));   // SITE_DIR=<folder>: an older copy, to see a test fail before a fix
const BASE = '/cs2-ip-changer';
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.woff2': 'font/woff2', '.json': 'application/json' };

async function findChromium() {
  const where = [process.env.PLAYWRIGHT, ROOT];
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
before(async () => {
  const chromium = await findChromium();
  if (!chromium) { why = 'no Playwright (PLAYWRIGHT=<folder with it>)'; return; }
  for (const o of [{ channel: 'chrome' }, {}]) {
    try { browser = await chromium.launch(o); break; } catch (e) { why = `Chromium did not start: ${e.message.split('\n')[0]}`; }
  }
  if (!browser) return;
  server = createServer((q, s) => {
    let p = decodeURIComponent(q.url.split('?')[0]);
    if (!p.startsWith(BASE)) { s.writeHead(404); return s.end(); }
    let f = resolve(SITE, '.' + (p.slice(BASE.length) || '/'));
    if (f !== SITE && !f.startsWith(SITE + sep)) { s.writeHead(404); return s.end(); }
    if (existsSync(f) && statSync(f).isDirectory()) f = join(f, 'index.html');
    if (!existsSync(f)) { s.writeHead(404); return s.end(); }
    s.writeHead(200, { 'content-type': TYPES[extname(f)] || 'application/octet-stream' });
    s.end(readFileSync(f));
  });
  await new Promise((ok) => server.listen(0, '127.0.0.1', ok));
  origin = `http://127.0.0.1:${server.address().port}${BASE}/`;
});
after(async () => { await browser?.close(); server?.close(); });

/** One row of pixels of a page's screenshot, as luminance 0..255. */
async function row(page, clip, y) {
  const png = await page.screenshot({ clip });
  const reader = await browser.newPage();
  const out = await reader.evaluate(async ([b64, yy]) => {
    const im = new Image(); im.src = 'data:image/png;base64,' + b64; await im.decode();
    const c = document.createElement('canvas'); c.width = im.width; c.height = im.height;
    const x = c.getContext('2d'); x.drawImage(im, 0, 0);
    const d = x.getImageData(0, yy, im.width, 1).data, r = [];
    for (let i = 0; i < d.length; i += 4) r.push(0.2126 * d[i] + 0.7152 * d[i + 1] + 0.0722 * d[i + 2]);
    return r;
  }, [png.toString('base64'), y]);
  await reader.close();
  return out;
}

// Point 13: the footer name lit with the scroll had a straight, hard cut between its lit and its unlit part, and the glow
// was cut along the same line (publish/feedback-2026-11-07/2.png). The light that lights it is measured on its own: a
// white bar in the lit group, everything else hidden, the scroll position set by hand.
test('Site: o nome do rodape acende com uma frente suave, sem corte a direito, e fica todo aceso no fim', async (t) => {
  if (!browser) return t.skip(why);
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  await page.goto(origin, { waitUntil: 'load' });
  await page.waitForFunction(() => !document.documentElement.classList.contains('ld'), null, { timeout: 15000 });
  await page.addStyleTag({ content: '.wordmark use,.wordmark line{visibility:hidden}.wm-pass{animation:none!important;opacity:0}.wm-spot{opacity:0!important}' });
  await page.evaluate(() => {
    const wm = document.querySelector('.wordmark'), g = wm.querySelector('g[mask="url(#wm-lit)"]');
    const r = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    r.setAttribute('x', '7'); r.setAttribute('y', '60'); r.setAttribute('width', '1433'); r.setAttribute('height', '40'); r.setAttribute('fill', 'white');
    g.appendChild(r);
    const y = wm.getBoundingClientRect().top + scrollY - innerHeight / 2;
    if (window.__lenis) window.__lenis.scrollTo(y, { immediate: true, force: true }); else scrollTo(0, y);
  });
  await page.waitForTimeout(1500);   // the scroll-linked light settles (scrub) before it is set by hand
  const clip = await page.evaluate(() => {
    const b = document.querySelector('.wordmark svg').getBoundingClientRect();
    return { x: Math.round(b.left), y: Math.round(b.top), width: Math.round(b.width), height: Math.round(b.height) };
  });
  const y = Math.round(clip.height * 80 / 252);   // the middle of the bar
  // the name is mapped to the scroll by --wmp (0 to 1); --wm was the old percentage, set too so the old page is measured the same
  const at = async (p) => {
    await page.evaluate((v) => { const wm = document.querySelector('.wordmark'); wm.style.setProperty('--wmp', String(v)); wm.style.setProperty('--wm', (v * 100) + '%'); }, p);
    await page.waitForTimeout(150);
    return row(page, clip, y);
  };
  const inner = (r) => r.slice(3, -3);   // the bar's own antialiased ends left out
  const none = inner(await at(0)), half = inner(await at(0.5)), all = inner(await at(1));
  const bg = Math.min(...none), lit = Math.max(...all);
  assert.ok(Math.max(...none) - bg < 6, 'before the page reaches it, nothing is lit');
  assert.ok(lit - bg > 60, `fully lit, the bar is clearly lit (${Math.round(lit - bg)})`);
  assert.ok(Math.min(...all) > lit - 8, 'at the end of the page the whole name is lit, to the last letter');
  let jump = 0, at1 = 0;
  for (let i = 1; i < half.length; i++) { const d = Math.abs(half[i] - half[i - 1]); if (d > jump) { jump = d; at1 = i; } }
  assert.ok(Math.max(...half) > lit - 8 && Math.min(...half) < bg + 6, 'half way, part of the name is lit and part is not');
  // a straight cut drops from lit to unlit in one pixel; the front fades over ~20% of the name's width
  assert.ok(jump < (lit - bg) * 0.06, `the lit part fades out at its front, no hard edge (largest step ${Math.round(jump)} of ${Math.round(lit - bg)} at x=${at1})`);
  const fadePx = half.filter((v) => v > bg + (lit - bg) * 0.05 && v < lit - (lit - bg) * 0.05).length;
  assert.ok(fadePx > clip.width * 0.1, `the front is wide enough to read as soft (${fadePx}px of ${clip.width})`);
  await page.close();
});
