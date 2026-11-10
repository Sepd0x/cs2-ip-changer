// The site on a phone, used by a finger, not a mouse. node --test tools/site-touch.test.mjs
// 10 Nov. 2026 (session 8, the owner: "test everything like a human on the phone, not with the mouse"): with taps and flicks
// (touch points with their own timestamps, so Chrome flings on as from a finger) on 320 to 430 px phones, upright and on their
// side, these were found and fixed: a tapped FAQ question kept the hover colour after it was shut, and every hover style stuck
// the same way after a tap (a phone keeps :hover on what was tapped); the Android | iPhone switch broke "Androi / d" (UI-001);
// a guide's contents link left its heading 100 px under the header (the page's scroll padding and the heading's scroll margin
// both added the header); a step picked on the rail (PC) or the chip (phone on its side) rested 81 px before the step's shot.
// The flick test keeps the snap between the beats as it was: a flick from the cover rests on the next beat's card.
// SITE_DIR=<folder> runs them on another copy. Needs Playwright with Chrome or its Chromium (PLAYWRIGHT=<folder>, the repo,
// the global npm); without it the browser tests are skipped, never passed. The static tests always run.
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
const CSS_FILES = readdirSync(join(SITE, 'assets')).filter((n) => n.endsWith('.css')).map((n) => `assets/${n}`);
const read = (f) => readFileSync(join(SITE, f), 'utf8').replace(/\r\n/g, '\n');

// every style rule of a stylesheet with the at-rules around it (comments and strings left out)
function rules(css) {
  css = css.replace(/\/\*[\s\S]*?\*\//g, '').replace(/"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'/g, '""');
  const out = [], stack = [];
  let start = 0;
  for (let i = 0; i < css.length; i++) {
    const c = css[i];
    if (c === '{') { stack.push(css.slice(start, i).trim()); start = i + 1; }
    else if (c === '}') {
      const head = stack.pop();
      if (head !== undefined && !head.startsWith('@')) out.push({ sel: head, body: css.slice(start, i), at: stack.filter((h) => h.startsWith('@')) });
      start = i + 1;
    } else if (c === ';' && (stack.length === 0 || stack[stack.length - 1].startsWith('@'))) start = i + 1;
  }
  return out;
}
const sheets = () => [
  ...PAGES.flatMap((p) => [...read(p).matchAll(/<style>([\s\S]*?)<\/style>/g)].map((m) => ({ file: p, css: m[1] }))),
  ...CSS_FILES.map((f) => ({ file: f, css: read(f) })),
];

test('Touch: every hover style is only for a mouse or a trackpad, so nothing stays lit after a tap on a phone', () => {
  const loose = [];
  for (const { file, css } of sheets()) for (const r of rules(css)) {
    if (/:hover/.test(r.sel) && !r.at.some((a) => /\(\s*hover\s*:\s*hover\s*\)|\(\s*pointer\s*:\s*fine\s*\)/.test(a))) loose.push(`${file}: ${r.sel}`);
  }
  assert.deepEqual([...new Set(loose)], [], 'these :hover rules stick after a tap on a phone; put them in @media (hover:hover)');
});

test('Touch: no heading adds the header to its scroll margin, the page\'s scroll padding already leaves room for it', () => {
  const twice = [];
  for (const { file, css } of sheets()) for (const r of rules(css)) if (/scroll-margin-top\s*:[^;]*--top-h/.test(r.body)) twice.push(`${file}: ${r.sel}`);
  assert.deepEqual([...new Set(twice)], [], 'scroll-padding-top on html already has var(--top-h); a scroll margin with it lands the heading a header lower');
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

const phone = (width, height) => ({ viewport: { width, height }, deviceScaleFactor: 2, isMobile: true, hasTouch: true,
  userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36' });
const PC = { viewport: { width: 1440, height: 900 } };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function open(t, path, opts) {
  const ctx = await browser.newContext(opts);
  t.after(() => ctx.close());
  const page = await ctx.newPage();
  const errors = []; page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(origin + path, { waitUntil: 'networkidle' });
  await page.waitForFunction(() => !document.documentElement.classList.contains('ld'), null, { timeout: 45000 }).catch(() => {});
  await page.evaluate(() => document.fonts.ready);
  await sleep(700);
  return { page, ctx, errors, cdp: await ctx.newCDPSession(page) };
}
// until the scroll has stopped for 300 ms
async function rest(page, ms = 4000) {
  let last = null, still = 0; const t0 = Date.now();
  while (Date.now() - t0 < ms && still < 6) { const v = await page.evaluate(() => scrollY); still = v === last ? still + 1 : 0; last = v; await sleep(50); }
  return last;
}
// a finger: points 12 ms apart with their own timestamps, let go while moving, so Chrome flings on
async function flick(cdp, x, y, dy, frames = 8, gap = 12) {
  let ts = Date.now() / 1000;
  const send = (type, pts) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: pts, timestamp: ts });
  await send('touchStart', [{ x, y }]);
  for (let i = 1; i <= frames; i++) { ts += gap / 1000; await send('touchMove', [{ x, y: y - dy * i / frames }]); }
  ts += 0.004; await send('touchEnd', []);
}
async function tap(page, el) {
  await el.evaluate((e) => e.scrollIntoView({ block: 'center', behavior: 'instant' }));
  await rest(page, 1500);
  const b = await el.boundingBox();
  await page.touchscreen.tap(b.x + b.width / 2, b.y + b.height / 2);
  await sleep(400);
}

test('Touch: Android | iPhone, each name on one line on a 320 and a 390 px phone (UI-001)', async (t) => {
  if (!browser) return t.skip(why);
  for (const w of [320, 390]) {
    const { page } = await open(t, '', phone(w, w === 320 ? 568 : 844));
    const tabs = await page.evaluate(() => [...document.querySelectorAll('.ph-tabs button')].map((b) => {
      const r = document.createRange(); r.selectNodeContents(b);
      const tops = new Set([...r.getClientRects()].filter((x) => x.width).map((x) => Math.round(x.top)));
      const box = b.getBoundingClientRect();
      return { name: b.textContent, lines: tops.size, left: box.left, right: box.right, W: innerWidth };
    }));
    assert.equal(tabs.length, 2, `${w} px: no Android | iPhone switch`);
    for (const b of tabs) {
      assert.equal(b.lines, 1, `${w} px: "${b.name}" breaks over ${b.lines} lines`);
      assert.ok(b.left >= 0 && b.right <= b.W, `${w} px: "${b.name}" goes past the screen's edge`);
    }
  }
});

test('Touch: a FAQ question opened and shut by a tap looks like the others again, on the home and in a guide', async (t) => {
  if (!browser) return t.skip(why);
  for (const [path, sel] of [['', '#faq details > summary'], ['change-ip-with-phone/', '.gfaq details > summary']]) {
    const { page } = await open(t, path, phone(390, 844));
    const sums = await page.$$(sel);
    assert.ok(sums.length > 1, `${path || 'home'}: no FAQ`);
    await tap(page, sums[0]);
    assert.equal(await sums[0].evaluate((s) => s.parentNode.open), true, `${path || 'home'}: a tap did not open the question`);
    await tap(page, sums[0]);
    const st = await page.evaluate((sel) => { const s = [...document.querySelectorAll(sel)]; return { open: s[0].parentNode.open, tapped: getComputedStyle(s[0]).color, other: getComputedStyle(s[1]).color, hovered: s[0].matches(':hover') }; }, sel);
    assert.equal(st.open, false, `${path || 'home'}: a second tap did not shut it`);
    assert.equal(st.tapped, st.other, `${path || 'home'}: the shut question stays lit (${st.tapped}, the others ${st.other}) while the phone keeps it hovered (${st.hovered})`);
  }
});

test('Touch: a guide\'s contents link lands its heading right under the header', async (t) => {
  if (!browser) return t.skip(why);
  const { page } = await open(t, 'change-ip-with-phone/', phone(390, 844));
  const summary = await page.$('.guide-contents summary');
  assert.ok(summary && await summary.isVisible(), 'the guide has no contents on a phone');
  const n = await page.$$eval('.guide-contents a', (a) => a.length);
  for (let i = 0; i < n; i++) {
    await page.evaluate(() => scrollTo(0, 0)); await rest(page);
    if (!(await page.$eval('.guide-contents', (d) => d.open))) await tap(page, summary);
    const link = (await page.$$('.guide-contents a'))[i], href = await link.getAttribute('href');
    await tap(page, link); await rest(page);
    const at = await page.evaluate((h) => ({ top: Math.round(document.querySelector(h).getBoundingClientRect().top), head: document.querySelector('.top').offsetHeight }), href);
    assert.ok(at.top >= at.head && at.top <= at.head + 40, `${href}: the heading rests ${at.top - at.head} px under the header`);
  }
});

test('Touch: a step picked on the rail (PC) or the chip (a phone on its side) rests on that step\'s shot', async (t) => {
  if (!browser) return t.skip(why);
  for (const [opts, label] of [[PC, 'PC rail'], [phone(844, 390), 'chip, 844x390']]) {
    const { page } = await open(t, '', opts);
    for (const i of [1, 2, 3, 4]) {
      await page.evaluate(() => scrollTo(0, Math.round(window.__story().A[1].s))); await rest(page); await sleep(300);
      if (label === 'PC rail') await (await page.$$('#rail a'))[i].click();
      else { await page.touchscreen.tap(...(await page.$eval('#where button', (b) => { const r = b.getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; }))); await sleep(300); await tap(page, (await page.$$('#where-list a'))[i]); }
      await rest(page); await sleep(400);
      const at = await page.evaluate((i) => { const s = window.__story(); return { y: Math.round(scrollY), s: Math.round(s.A[i].s), hash: location.hash }; }, i);
      assert.ok(Math.abs(at.y - at.s) <= 2, `${label}, step ${i + 1} (${at.hash}): rests at ${at.y}, its shot is at ${at.s}`);
    }
  }
});

// The snap is proximity (the plain sections scroll freely), so a hard flick can pass a card even with snap-stop: always
// (Chrome keeps snap-stop only when it snaps); it still rests on a card, never between two, and never back against the finger
test('Touch: the snap between the beats still holds, each flick from the cover on rests on a card', async (t) => {
  if (!browser) return t.skip(why);
  const { page, cdp, errors } = await open(t, '', phone(390, 844));
  const cards = await page.evaluate(() => [...document.querySelectorAll('.beat .copy')].filter((c) => getComputedStyle(c).scrollSnapAlign !== 'none').map((c) => Math.round(c.getBoundingClientRect().bottom + scrollY + 12 - innerHeight)));
  assert.ok(cards.length >= 3, 'the beats\' cards have no snap on a phone held upright');
  let y = 0;
  while (y < cards[cards.length - 1] - 2) {
    const from = y;
    await flick(cdp, 195, 640, 250);
    y = await rest(page);
    assert.ok(y > from, `a flick from ${from} rested at ${y}, back against the finger`);
    if (y <= cards[cards.length - 1] + 2) assert.ok(cards.some((c) => Math.abs(c - y) <= 2), `a flick from ${from} rested at ${y}, between two cards (${cards.join(', ')})`);
  }
  assert.deepEqual(errors, []);
});

test('Touch: the app tour says "Scroll image sideways" only when the close-up has more than its margin to show', async (t) => {
  if (!browser) return t.skip(why);
  for (const [w, h] of [[568, 320], [390, 844]]) {
    const { page } = await open(t, '', phone(w, h));
    const bad = await page.evaluate(async () => {
      const out = [], wait = () => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
      const tabs = [...document.querySelectorAll('.tour-tabs button')];
      for (const tab of tabs) {
        tab.click(); await wait();
        for (let k = 0; k < 3; k++) {
          const crop = document.querySelector('.tour-crop'), hint = document.querySelector('.tour-pan');
          const extra = crop.scrollWidth - crop.clientWidth;
          if (hint && !hint.hidden && extra <= 24) out.push(`${tab.textContent} ${k + 1}: the hint for a ${extra} px scroll`);
          if (hint && hint.hidden && extra > 40) out.push(`${tab.textContent} ${k + 1}: ${extra} px to scroll and no hint`);
          document.querySelector('.tour-next').click(); await wait();
        }
      }
      return out;
    });
    assert.deepEqual(bad, [], `${w}x${h}`);
  }
});

test('Touch: with the text at 200% (UI-004) nothing is wider than a 390 px phone; at 100% the header keeps the name', async (t) => {
  if (!browser) return t.skip(why);
  for (const p of ['', ...GUIDES.map((g) => g + '/'), '404.html']) {
    const { page } = await open(t, p, phone(390, 844));
    const name = await page.evaluate(() => { const s = document.querySelector('.top .brand span'); return s ? s.getBoundingClientRect().width : -1; });
    if (name !== -1) assert.ok(name > 40, `${p || 'home'}: at 100% the header hides the name`);
    await page.evaluate(() => document.documentElement.style.setProperty('font-size', '200%', 'important'));
    await sleep(500);
    // against the phone's own width: a phone zooms out to a page wider than the screen, so innerWidth grows with it
    const w = [await page.evaluate(() => document.documentElement.scrollWidth), 390];
    assert.ok(w[0] <= w[1], `${p || 'home'}: with the text at 200% the page is ${w[0]} px wide on a ${w[1]} px phone`);
  }
});

test('Touch: nothing is wider than a 320 px phone, on the home, the guides and the 404', async (t) => {
  if (!browser) return t.skip(why);
  for (const p of ['', ...GUIDES.map((g) => g + '/'), '404.html']) {
    const { page } = await open(t, p, phone(320, 568));
    const w = [await page.evaluate(() => document.documentElement.scrollWidth), 320];
    assert.ok(w[0] <= w[1], `${p || 'home'}: the page is ${w[0]} px wide on a ${w[1]} px phone`);
  }
});
