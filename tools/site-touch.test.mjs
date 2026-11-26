// The site on a phone, used by a finger, not a mouse. node --test tools/site-touch.test.mjs
// 10 Nov. 2026 (session 8, the owner: "test everything like a human on the phone, not with the mouse"): with taps and flicks
// (touch points with their own timestamps, so Chrome flings on as from a finger) on 320 to 430 px phones, upright and on their
// side, these were found and fixed: a tapped FAQ question kept the hover colour after it was shut, and every hover style stuck
// the same way after a tap (a phone keeps :hover on what was tapped); the Android | iPhone switch broke "Androi / d" (UI-001);
// a guide's contents link left its heading 100 px under the header (the page's scroll padding and the heading's scroll margin
// both added the header); a step picked on the rail (PC) or the chip (phone on its side) rested 81 px before the step's shot.
// 25 Nov. 2026 (the phone audit, publish/plano-1-0-0/10-site-telemovel.md): the snap between the beats, which this file kept
// until then ("a flick from the cover rests on the next beat's card"), is gone, and the test says the opposite: a drag on a
// step, held and let go, leaves the page where the finger left it (the snap undid swipes shorter than half a screen); and
// a tap on a link of the menu no longer holds the finger while the page travels (drag(): a finger in real time, by CDP).
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

// 25 Nov. 2026 (A1): the guides and the language folders carry a copy of the home's style, so the snap must be gone from all
test('Touch: the page itself has no scroll snap (html, body), on the home in every language, the guides and the 404', () => {
  const snaps = [];
  for (const { file, css } of sheets()) for (const r of rules(css)) {
    if (/(^|,)\s*(html|body|:root)\s*(,|$)/.test(r.sel) && /scroll-snap-type\s*:\s*(?!none)/.test(r.body)) snaps.push(`${file}: ${r.sel}{${r.body.trim().slice(0, 60)}}`);
  }
  assert.deepEqual([...new Set(snaps)], [], 'a snap on the page pulls it back after a swipe on a phone');
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

// 25 Nov. 2026 (phone audit, M2): on 6 Nov. the owner had the steps of "How it works" made shorter on a phone held upright
// (#how .beat: 78svh, in the home's style), and a stylesheet loaded after it (assets/story-reading.css) put 100svh back:
// three steps of a whole screen each for cards of 330 to 500 px. The home's 78svh is the only height a step is given.
test('Touch: a step of "How it works" is shorter than the screen on a phone held upright (78svh), and no stylesheet puts 100svh back', async (t) => {
  const tall = [];
  for (const { file, css } of sheets()) for (const r of rules(css)) {
    if (r.sel.split(',').some((s) => /#how\s+\.beat$/.test(s.trim())) && /min-height\s*:\s*100svh/.test(r.body)) tall.push(`${file}: ${r.at.join(' ')} ${r.sel}{${r.body.trim()}}`);
  }
  assert.deepEqual([...new Set(tall)], [], 'the steps are 78svh on a phone held upright (the home\'s own style); this rule undoes it');
  if (!browser) return t.skip(why);
  const { page } = await open(t, '', phone(390, 844));
  const beats = await page.evaluate(() => [...document.querySelectorAll('#how .beat')].map((b) => ({ h: b.offsetHeight, card: b.querySelector('.copy').offsetHeight })));
  assert.equal(beats.length, 3, 'the three steps of How it works');
  for (const [i, b] of beats.entries()) {
    assert.ok(b.h < 844 * .85, `step ${i + 1} is ${b.h} px tall on a screen of 844 (${(b.h / 844).toFixed(2)} of it; 78svh is 658)`);
    assert.ok(b.h >= b.card, `step ${i + 1}: its card (${b.card} px) does not fit in it (${b.h} px)`);
  }
});

// 25 Nov. 2026 (phone audit, M3): held upright, a card's opacity followed its place on the screen (1 to 0 over a third of the
// screen), so for most of the way between two steps its text sat at 30 to 50 % over the 3D scene, like a drawing error. A card
// is now readable or gone (a 0.18 s fade between the two): wherever the page rests, no card is in between.
test('Touch: wherever the page rests between the steps, a card is readable or gone, never half faded over the scene', async (t) => {
  if (!browser) return t.skip(why);
  const { page, errors } = await open(t, '', phone(390, 844));
  const A = await page.evaluate(() => window.__story().A.map((a) => Math.round(a.s)));
  const half = [], both = [];
  let seen = 0;
  // at rest: 400 ms, and then until no card's opacity moves any more (on a busy machine the 0.18 s fade can take over a second
  // to be drawn to its end; a card caught at 0.93 on its way to 1 is not a card left half faded)
  const rest = async () => {
    await sleep(400);
    const read = () => page.evaluate(() => [...document.querySelectorAll('.beat .copy')].map((c) => getComputedStyle(c).opacity).join());
    for (let was = await read(), n = 0; n < 30; n++) { await sleep(120); const now = await read(); if (now === was) return; was = now; }
  };
  for (let y = A[1] - 200; y <= A[4] + 60; y += 20) {
    await page.evaluate((y) => scrollTo(0, y), y);
    await rest();
    const cards = await page.evaluate(() => [...document.querySelectorAll('.beat .copy')].slice(1).map((c, i) => { const r = c.getBoundingClientRect(); return { i: i + 1, o: +getComputedStyle(c).opacity, top: Math.round(r.top), bottom: Math.round(r.bottom) }; }).filter((c) => c.bottom > 0 && c.top < innerHeight));
    seen += cards.length;
    for (const c of cards) if (c.o > .05 && c.o < .95) half.push(`y ${y}: card ${c.i} (${c.top} to ${c.bottom}) at ${c.o.toFixed(2)}`);
    const lit = cards.filter((c) => c.o > .5);
    for (const a of lit) for (const b of lit) if (a.i < b.i && a.top < b.bottom && b.top < a.bottom) both.push(`y ${y}: cards ${a.i} and ${b.i}`);
  }
  assert.ok(seen > 100, 'the cards of the steps were on screen');
  assert.deepEqual(half.slice(0, 8), [], `${half.length} rests with a half faded card`);
  assert.deepEqual(both, [], 'two readable cards over each other');
  // at each step's own rest its card is whole, and the one before it is gone
  for (const i of [1, 2, 3]) {
    await page.evaluate((y) => scrollTo(0, y), A[i]); await rest();
    const o = await page.evaluate(() => [...document.querySelectorAll('.beat .copy')].map((c) => +getComputedStyle(c).opacity));
    assert.equal(o[i], 1, `step ${i}: its card at its rest`);
    if (i > 1) assert.equal(o[i - 1], 0, `step ${i}: the card of step ${i - 1} is gone at its rest`);
  }
  assert.deepEqual(errors, []);
});

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

// a finger that drags in real time: a point every 16 ms, and it stays down (hold) until lift()
async function drag(cdp, x, y, dy, { step = 10, hold = 0 } = {}) {
  const send = (type, pts) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: pts });
  await send('touchStart', [{ x, y }]);
  const n = Math.ceil(Math.abs(dy) / step);
  for (let i = 1; i <= n; i++) { await sleep(16); await send('touchMove', [{ x, y: y - dy * i / n }]); }
  if (hold) await sleep(hold);
  return () => send('touchEnd', []);
}

// 25 Nov. 2026 (phone audit, A2): a tap on a link of the menu (or the chip, or "Follow the route") sent the page there with
// Lenis and lock: true, and with lock Lenis cancels every touchmove: for 1.1 s the finger could not scroll (12 of 24 moves
// blocked) and the page went on to the FAQ, 5000 px away, against it. On a touch screen the trip has no lock: the finger wins.
test('Touch: after a tap on "FAQ" in the menu the finger still scrolls the page: no touchmove is blocked and the trip stops', async (t) => {
  if (!browser) return t.skip(why);
  const { page, cdp, errors } = await open(t, '', phone(390, 844));
  // every touchmove is kept (whether it was cancelled is read at the end), and where the page was when the first one came
  await page.evaluate(() => { window.__moves = []; window.__y1 = null; addEventListener('touchmove', (e) => { if (window.__y1 === null) window.__y1 = scrollY; window.__moves.push(e); }, { capture: true, passive: true }); });
  const mid = (sel) => page.$eval(sel, (e) => { const r = e.getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; });
  await page.touchscreen.tap(...(await mid('.site-menu > summary'))); await sleep(300);
  const faq = await page.evaluate(() => Math.round(document.getElementById('faq').getBoundingClientRect().top + scrollY));
  assert.ok(faq > 3000, 'the FAQ is far down the page');
  await page.touchscreen.tap(...(await mid('.site-menu .menu-links a[href="#faq"]')));
  await sleep(150);
  const lift = await drag(cdp, 195, 620, 250, { hold: 500 });
  const held = await page.evaluate(() => ({ y: scrollY, y1: window.__y1, moves: window.__moves.length, blocked: window.__moves.filter((e) => e.defaultPrevented).length }));
  await lift();
  const end = await rest(page);
  assert.ok(held.moves >= 12, `the finger's moves reached the page (${held.moves})`);
  assert.equal(held.blocked, 0, `${held.blocked} of ${held.moves} touchmove were blocked (preventDefault) while the finger dragged`);
  // from the finger's first move on, the page goes where the finger takes it and no further (Chrome keeps the first 15 px of a drag)
  const went = held.y - held.y1;
  assert.ok(went > 150 && went < 270, `a drag of 250 px moved the page ${went} px (from ${held.y1} to ${held.y}; the trip was going to ${faq})`);
  assert.ok(Math.abs(end - held.y) <= 20, `after the finger let go the page went on by itself, from ${held.y} to ${end}`);
  assert.deepEqual(errors, []);
});

// with a mouse the trip keeps its lock (a turn of the wheel does not stop it half way), as before
test('Touch: with a mouse, a link of the header still takes the page to its section', async (t) => {
  if (!browser) return t.skip(why);
  const { page } = await open(t, '', PC);
  const before = await page.evaluate(() => scrollY);
  await page.click('.top nav > a[href="#guides"]');
  await sleep(300); await page.mouse.move(700, 500); await page.mouse.wheel(0, -300);
  const end = await rest(page, 5000);
  const at = await page.evaluate(() => Math.round(document.getElementById('guides').getBoundingClientRect().top - document.querySelector('.top').offsetHeight));
  assert.ok(end > before + 1000 && Math.abs(at - 16) <= 4, `Guides rests ${at} px under the header (16), the page at ${end}`);
});

// 25 Nov. 2026 (phone audit, A1; the owner: "the scroll is strange"): held upright and by touch, the page snapped to each
// beat's card (html: scroll-snap-type y proximity; the cards: snap-align end and snap-stop always), one screen apart. A drag
// of 250 px, held and let go, went back 235 px to where it started; a short flick went 185 px with the finger and came back;
// let go 74 px from a card, the page jumped to it. The snap is gone: by touch the page stays where the finger leaves it.
// (From 10 Nov. until then this test kept the snap: "each flick from the cover on rests on a card".)
test('Touch: no snap between the beats: a 250 px drag on a step, held and let go, leaves the page where the finger left it, down and up', async (t) => {
  if (!browser) return t.skip(why);
  for (const [w, h] of [[390, 844], [820, 1180]]) {
    const { page, cdp, errors } = await open(t, '', phone(w, h));
    const steps = await page.evaluate(() => window.__story().A.slice(1, 4).map((a) => Math.round(a.s)));
    assert.equal(steps.length, 3, 'the three steps of How it works');
    for (const [i, s] of steps.entries()) for (const dy of [250, -250]) {
      await page.evaluate((y) => scrollTo(0, y), s);
      const from = await rest(page);
      const lift = await drag(cdp, w / 2, dy > 0 ? 620 : 300, dy, { hold: 500 });
      const held = await page.evaluate(() => scrollY);
      await lift();
      const end = await rest(page);
      const where = `${w}x${h}, step ${i + 1}, the page ${dy > 0 ? 'down' : 'up'}`;
      assert.ok(Math.abs(held - from) > 200, `${where}: a drag of 250 px moved the page ${held - from} px`);
      assert.ok(Math.abs(end - held) < 20, `${where}: the finger left the page at ${held} and it went ${end - held} px by itself, to ${end} (it started at ${from})`);
    }
    // a short flick is not taken back either
    await page.evaluate((y) => scrollTo(0, y), steps[0]);
    const from = await rest(page);
    await flick(cdp, w / 2, 640, 200, 6, 13);
    const end = await rest(page);
    assert.ok(end - from > 150, `${w}x${h}: a 200 px flick from ${from} rested at ${end}`);
    assert.equal(await page.evaluate(() => getComputedStyle(document.documentElement).scrollSnapType), 'none', `${w}x${h}: the page has a scroll snap`);
    assert.deepEqual(errors, []);
  }
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

// 25 Nov. 2026 (phone audit, A5): the close-up of the app tour cut the text next to its mark in half. 27 Nov. (the owner on his
// S21: "ervers", "egions", a box floating over another thing): it shows only the marked part of the real window with a few px of
// its own ground, clipped (clip-path) and scaled to fit the box (up to 2x, the screenshot's own pixels), so nothing round it shows.
// Every mark stays whole and clear of the box's edges, at 390x844 and 320x568, and a tall mark is scaled down to fit.
test('Touch: in the app tour the close-up shows only its mark, whole, clipped and scaled to fit, even a tall one', async (t) => {
  if (!browser) return t.skip(why);
  for (const [w, h] of [[390, 844], [320, 568]]) {
    const { page, errors } = await open(t, '', phone(w, h));
    const r = await page.evaluate(async () => {
      const out = [], wait = () => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
      const crop = () => document.querySelector('.tour-crop');
      const look = (name) => {
        const c = crop(), b = c.getBoundingClientRect(), ring = c.querySelector('.cring').getBoundingClientRect();
        const clip = getComputedStyle(c.querySelector('.cut-in')).clipPath;
        if (!/^inset\(/.test(clip)) out.push(`${name}: the rest of the window is not clipped away (${clip})`);
        if (ring.top < b.top + 8 || ring.bottom > b.bottom - 8 || ring.left < b.left + 8 || ring.right > b.right - 8) out.push(`${name}: the mark (${Math.round(ring.left - b.left)},${Math.round(ring.top - b.top)} to ${Math.round(ring.right - b.left)},${Math.round(ring.bottom - b.top)}) is not whole in a ${Math.round(b.width)}x${Math.round(b.height)} box`);
        return { box: b.height, ring: ring.height };
      };
      const tabs = [...document.querySelectorAll('.tour-tabs button')];
      for (const tab of tabs) {
        tab.click(); await wait();
        for (let k = 0; k < 3; k++) { look(`${tab.textContent} ${document.querySelector('.tour-caption').textContent.slice(0, 5)}`); document.querySelector('.tour-next').click(); await wait(); }
      }
      // a mark 220 px tall, put by hand on the first detail of the last tab: it is scaled down into the box
      const before = crop().getBoundingClientRect().height;
      const cut = document.querySelector('#pages > li:last-child .mk-key > li .cut'), was = cut.dataset.crop;
      cut.dataset.crop = '300,200,200,220';
      for (let k = 0; k < 3; k++) { document.querySelector('.tour-next').click(); await wait(); if (/^1 \//.test(document.querySelector('.tour-caption').textContent)) break; }
      const tall = look('a 220 px mark');
      if (was === undefined) delete cut.dataset.crop; else cut.dataset.crop = was;
      return { out, before, tall };
    });
    assert.deepEqual(r.out, [], `${w}x${h}`);
    assert.ok(r.tall.ring < r.tall.box, `${w}x${h}: a mark 220 px tall shows ${Math.round(r.tall.ring)} px in a ${Math.round(r.tall.box)} px box`);
    assert.ok(r.before >= 120, `${w}x${h}: the close-up is ${Math.round(r.before)} px tall, a strip`);
    assert.deepEqual(errors, []);
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
