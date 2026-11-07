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

// Point 10: with Windows set to fewer animations the home and the guides start still and offer Play motion (beta.12);
// the 404 had no Play motion, and ignored it when pressed on another page: its world stayed still and the button hidden.
test('Site: a 404 tambem oferece Play motion com menos animacoes no Windows, e lembra a escolha das outras paginas', async (t) => {
  if (!browser) return t.skip(why);
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 }, reducedMotion: 'reduce' });
  const page = await ctx.newPage();
  const btn = () => page.evaluate(() => { const b = [...document.querySelectorAll('button')].find((x) => /motion/i.test(x.title)); return b && { t: b.textContent.trim(), title: b.title, shown: !b.hidden && getComputedStyle(b).display !== 'none' && getComputedStyle(b).visibility !== 'hidden', pressed: b.getAttribute('aria-pressed') }; });
  await page.goto(origin + '404.html', { waitUntil: 'load' });
  await page.waitForFunction(() => document.querySelector('.gw.ready') || document.documentElement.classList.contains('no-gl'), null, { timeout: 15000 });
  if (await page.evaluate(() => document.documentElement.classList.contains('no-gl'))) { await ctx.close(); return t.skip('no WebGL: the 404 shows no world and no button'); }
  const still = await btn();
  assert.equal(still?.shown, true, 'the button is there with fewer animations');
  assert.equal(still.title, 'Play motion', 'and it is Play motion');
  await Promise.all([page.waitForEvent('load'), page.click('.demo')]);
  await page.waitForFunction(() => document.querySelector('.gw.ready'), null, { timeout: 15000 });
  assert.equal(await page.evaluate(() => localStorage.getItem('cs2-fx')), 'on', 'remembered in this browser, like the home and the guides');
  const moving = await btn();
  assert.deepEqual([moving.shown, moving.title, moving.pressed], [true, 'Pause motion', 'false'], 'after Play motion the world moves and the button is Pause motion');
  await ctx.close();
});

// Point 10/11: in "IP banned? Back in with one click" the log plays with the scroll. The line being read moved right by
// padding, so on a phone (320 px) or a small PC window (1024 px) its text wrapped to another line and the log and the
// caption under it jumped by a line at every step; and the note under the IP changed its number of lines with its text,
// or (upright, 760 to 999 px) went up beside the shorter IPs. A text that is replaced (the chip's 4/6 to 5/6) is not a move.
test('Site: no "IP banned?" o registo toca sem empurrar as linhas de baixo', async (t) => {
  if (!browser) return t.skip(why);
  const bad = [];
  for (const [w, h, phone] of [[320, 568, 1], [390, 844, 1], [768, 1024, 1], [1024, 768, 0]]) {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, ...(phone ? { isMobile: true, hasTouch: true, deviceScaleFactor: 2 } : {}) });
    await ctx.addInitScript(() => { window.__moves = []; new PerformanceObserver((l) => { for (const e of l.getEntries()) for (const s of e.sources || []) { if (!s.node || !s.node.isConnected) continue; const dy = Math.abs(s.currentRect.y - s.previousRect.y), dh = Math.abs(s.currentRect.height - s.previousRect.height); if ((dy >= 4 || dh >= 4) && s.currentRect.height && s.previousRect.height) window.__moves.push(`${s.node && s.node.nodeType === 1 ? (s.node.id || s.node.tagName + '.' + s.node.className) : '?'} y${Math.round(s.previousRect.y)}>${Math.round(s.currentRect.y)} h${Math.round(s.previousRect.height)}>${Math.round(s.currentRect.height)}`); } }).observe({ type: 'layout-shift', buffered: true }); });
    const page = await ctx.newPage();
    await page.goto(origin, { waitUntil: 'load' });
    await page.waitForTimeout(2500);
    const top = await page.evaluate(() => document.getElementById('ban').getBoundingClientRect().top + scrollY);
    await page.evaluate((y) => window.__lenis.scrollTo(y, { immediate: true, force: true }), top - h);
    await page.waitForTimeout(500);
    await page.evaluate(() => { window.__moves = []; });
    for (let y = top - h; y < top + 4 * h; y += Math.round(h * 0.12)) {
      await page.evaluate((yy) => window.__lenis.scrollTo(yy, { immediate: true, force: true }), y);
      await page.waitForTimeout(450);
    }
    const moves = await page.evaluate(() => window.__moves);
    if (moves.length) bad.push(`${w}x${h}: ${[...new Set(moves)].slice(0, 4).join(' ; ')}`);
    await ctx.close();
  }
  assert.deepEqual(bad, [], 'parts of the ban card that moved by themselves while the log played');
});

// Point 10/11: on a tablet held upright and on a phone in "Desktop site" (a 980 px page), the guides' footer jumped up
// as it came on screen: its height was guessed at 1250 px until laid out, and it is about 955 (760 to 899 px wide, two
// columns) or 465 (900 to 999, four columns), and the last line was guessed at 120 px everywhere. Scrolled to the end a
// screen at a time, nothing on screen may move by itself.
test('Site: nas guias nada salta ao chegar ao rodape, num tablet ao alto e num telemovel em versao de computador', async (t) => {
  if (!browser) return t.skip(why);
  const sizes = [{ width: 980, height: 2100, deviceScaleFactor: 2.75, hasTouch: true }, { width: 820, height: 1180, deviceScaleFactor: 2 }, { width: 768, height: 1024, deviceScaleFactor: 2, isMobile: true, hasTouch: true }, { width: 1280, height: 800 }, { width: 390, height: 844, deviceScaleFactor: 3 }];
  const bad = [];
  for (const { width, height, ...o } of sizes) {
    for (const g of ['change-ip-with-phone/', 'cs2-ip-ban/']) {
      const ctx = await browser.newContext({ viewport: { width, height }, ...o });
      await ctx.addInitScript(() => { window.__cls = []; new PerformanceObserver((l) => { for (const e of l.getEntries()) if (!e.hadRecentInput) window.__cls.push({ v: e.value, src: (e.sources || []).map((s) => s.node && s.node.nodeType === 1 ? s.node.tagName + '.' + s.node.className : '?').join(',') }); }).observe({ type: 'layout-shift', buffered: true }); });
      const page = await ctx.newPage();
      await page.goto(origin + g, { waitUntil: 'load' });
      await page.waitForTimeout(1500);
      await page.evaluate(() => { window.__cls = []; });   // the first screen settling is not what is measured here
      // a screen at a time, the way Page Down, a fling on a phone or a link to a part of the page moves it
      for (let i = 0; i < 40; i++) {
        await page.evaluate(() => { const y = scrollY + innerHeight * 0.8; if (window.__lenis) window.__lenis.scrollTo(y, { immediate: true, force: true }); else scrollTo(0, y); });
        await page.waitForTimeout(300);
        if (await page.evaluate(() => innerHeight + scrollY >= document.documentElement.scrollHeight - 2)) break;
      }
      await page.waitForTimeout(800);
      const cls = await page.evaluate(() => window.__cls);
      const sum = cls.reduce((s, e) => s + e.v, 0);
      if (sum > 0.01) bad.push(`${width}x${height} ${g}: ${sum.toFixed(3)} (${[...new Set(cls.map((e) => e.src))].join(' ; ')})`);
      await ctx.close();
    }
  }
  assert.deepEqual(bad, [], 'layout shifts while scrolling a guide to its end');
});
