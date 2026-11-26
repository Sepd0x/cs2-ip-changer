// Direction D (26 Nov. 2026): the home keeps its story in scroll with the 3D, and on a phone the lists of equals after it
// swipe sideways, the VPN table reads one row at a time with both answers, the footer's lists open in place and "Send the link to
// my PC" waits at the bottom from the app to the guides (after a designer's and a critic's review the same night). On a PC
// nothing of this shows. node --test tools/site-direcao-d.test.mjs
// Needs Playwright (PLAYWRIGHT=<folder>, the repo or the global npm); without it the browser tests are skipped, never passed.
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
const PHONE = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true };
const PC = { viewport: { width: 1440, height: 900 } };

let browser = null, server = null, origin = '', why = '';
before(async () => {
  const where = [process.env.PLAYWRIGHT, ROOT, 'C:/dev/puredeploy-site-v2'];
  const g = spawnSync('npm root -g', { encoding: 'utf8', shell: true });
  if (g.status === 0 && g.stdout.trim()) where.push(g.stdout.trim());
  let chromium = null;
  for (const w of where.filter(Boolean)) {
    try { const m = await import(pathToFileURL(createRequire(join(resolve(w), 'x.js')).resolve('playwright')).href); chromium = m.chromium || m.default.chromium; break; } catch { /* not there */ }
  }
  if (!chromium) { why = 'no Playwright'; return; }
  for (const o of [{ channel: 'chrome' }, {}]) { try { browser = await chromium.launch(o); break; } catch (e) { why = e.message.split('\n')[0]; } }
  if (!browser) return;
  server = createServer((q, s) => {
    const p = decodeURIComponent(q.url.split('?')[0]);
    if (!p.startsWith(BASE)) { s.writeHead(404); return s.end(); }
    let f = resolve(SITE, '.' + (p.slice(BASE.length) || '/'));
    if (f !== SITE && !f.startsWith(SITE + sep)) { s.writeHead(404); return s.end(); }
    if (existsSync(f) && statSync(f).isDirectory()) f = join(f, 'index.html');
    if (!existsSync(f) && /\/img\/[\w-]+\.(png|webp)$/.test(p)) for (const d of ['docs/screenshots', 'docs/assets']) { const c = join(ROOT, d, p.split('/').pop().replace(/\.webp$/, '.png')); if (existsSync(c)) { f = c; break; } }
    if (!existsSync(f)) { s.writeHead(404); return s.end(); }
    s.writeHead(200, { 'content-type': TYPES[extname(f)] || 'application/octet-stream' }); s.end(readFileSync(f));
  });
  await new Promise((ok) => server.listen(0, '127.0.0.1', ok));
  origin = `http://127.0.0.1:${server.address().port}${BASE}/`;
});
after(async () => { await browser?.close(); server?.close(); });

async function open(t, opts, path = '') {
  const ctx = await browser.newContext(opts); t.after(() => ctx.close());
  const page = await ctx.newPage();
  await page.goto(origin + path, { waitUntil: 'load' });
  await page.waitForFunction(() => !document.documentElement.classList.contains('ld') && document.querySelector('.thumb, footer nav.fold') !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(800);
  return page;
}

for (const lang of ['', 'tr/']) {
  test(`D: on a phone (${lang || 'en'}), the lists after the story swipe sideways with the next card peeking, and a dot per card`, async (t) => {
    if (!browser) return t.skip(why);
    const page = await open(t, PHONE, lang);
    const r = await page.evaluate(() => ['#safety .instr', '.more-kit .kit', '#vpn .vs', '#setup .steps4'].map((s) => {
      const el = document.querySelector(s), kids = [...el.children], dots = el.nextElementSibling;
      const first = kids[0].getBoundingClientRect(), second = kids[1].getBoundingClientRect(), d = dots.getBoundingClientRect();
      const bottom = Math.max(...kids.map((k) => k.getBoundingClientRect().bottom));
      return { s, shelf: el.classList.contains('shelf'), wide: el.scrollWidth > el.clientWidth + 40, peek: second.left < innerWidth && second.left > innerWidth * .6, top: Math.abs(first.top - second.top) < 1, gap: d.top - bottom, dots: dots && dots.classList.contains('shelf-dots') ? dots.children.length : 0, page: document.documentElement.scrollWidth };
    }));
    for (const x of r) {
      assert.ok(x.shelf && x.wide, `${x.s}: not a shelf that scrolls sideways`);
      assert.ok(x.peek, `${x.s}: the next card does not peek at the right edge`);
      assert.ok(x.dots >= 2, `${x.s}: no dots under it`);
      assert.ok(x.top, `${x.s}: the cards are not top-aligned (the peek would show the middle of a sentence)`);
      assert.ok(x.gap >= 4 && x.gap <= 24, `${x.s}: the dots sit ${Math.round(x.gap)} px under the cards (4 to 24 wanted, the same under every shelf)`);
      assert.equal(x.page, 390, `${x.s}: the page itself got wider than the phone`);
    }
  });
}

test('D: on a phone the VPN table reads one row at a time, both answers shown, and the two pictures sit side by side', async (t) => {
  if (!browser) return t.skip(why);
  const page = await open(t, PHONE);
  const r = await page.evaluate(() => {
    const tds = [...document.querySelectorAll('#vpn tbody tr:first-child td')].map((td) => [getComputedStyle(td).display, td.getBoundingClientRect().top]);
    const figs = [...document.querySelectorAll('#vpn .pair figure')].map((f) => f.getBoundingClientRect());
    return { shown: tds.every(([d]) => d !== 'none'), stacked: tds[1][1] > tds[0][1], toggle: document.querySelectorAll('.vs-tog').length, pics: figs.length === 2 && Math.abs(figs[0].top - figs[1].top) < 1 && figs[1].right <= innerWidth };
  });
  assert.deepEqual(r, { shown: true, stacked: true, toggle: 0, pics: true });
});

test('D: on a phone the footer\'s lists open in place, and "Send the link to my PC" waits at the bottom only from the app to the guides', async (t) => {
  if (!browser) return t.skip(why);
  const page = await open(t, PHONE);
  const fold = page.locator('footer nav.fold h3 button');
  // the footer's name is centred on a phone: the drawn word's middle within 4 px of the footer's middle
  const mid = await page.evaluate(() => { const r = document.querySelector('.wordmark .wm-base').getBoundingClientRect(), w = document.querySelector('.wordmark svg').getBoundingClientRect(); return Math.abs((r.left + r.right) / 2 - (w.left + w.right) / 2); });
  assert.ok(mid < 4, `the footer's name is ${Math.round(mid)} px off the middle`);
  assert.equal(await fold.count(), 3, 'three footer lists with a button');
  assert.equal(await page.locator('footer nav.fold ul:visible').count(), 0, 'a footer list is open before a tap');
  await fold.nth(1).click();
  assert.equal(await page.locator('footer nav.fold ul:visible').count(), 1);
  const bar = async (sel, at = 0.2) => { await page.evaluate(([sel, at]) => { const e = document.querySelector(sel); scrollTo(0, e.getBoundingClientRect().top + scrollY - innerHeight * at); }, [sel, at]); await page.waitForTimeout(700); return page.evaluate(() => document.querySelector('.thumb').classList.contains('on')); };
  assert.equal(await bar('#how'), false, 'the bar covers the story');
  assert.equal(await bar('#safety'), true, 'no bar after the app');
  assert.equal(await bar('#guides'), true, 'no bar at the guides');
  assert.equal(await bar('section.cta [data-share]', 0.7), false, 'the bar sits next to the final button');
  assert.equal(await page.locator('.thumb .btn').textContent(), await page.locator('#send-link').textContent());
  await bar('#safety');
  assert.equal(await page.evaluate(() => getComputedStyle(document.querySelector('.get-top')).visibility), 'hidden', 'two Send buttons on screen at once');
});

test('D: on a PC none of it shows: no shelves, both VPN columns, open footer lists, no bar', async (t) => {
  if (!browser) return t.skip(why);
  const page = await open(t, PC);
  const r = await page.evaluate(() => ({ shelves: document.querySelectorAll('.shelf').length, folds: document.querySelectorAll('footer nav.fold').length, bar: getComputedStyle(document.querySelector('.thumb')).display, thead: getComputedStyle(document.querySelector('#vpn thead')).display !== 'none' }));
  assert.deepEqual(r, { shelves: 0, folds: 0, bar: 'none', thead: true });
});

test('D: a guide\'s footer folds on a phone too, and not on a PC', async (t) => {
  if (!browser) return t.skip(why);
  const phone = await open(t, PHONE, 'cs2-ip-ban/');
  assert.equal(await phone.locator('footer nav.fold').count(), 3);
  const pc = await open(t, PC, 'cs2-ip-ban/');
  assert.equal(await pc.locator('footer nav.fold').count(), 0);
});

// 27 Nov. 2026, the phone made clean (the owner on his S21: cards of different heights, text squeezed into small boxes, things
// cut or floating in the app tour, the 01s, the borders, the language notice, the guides). Measured on a 390 and a 360 px phone.
for (const [w, h] of [[390, 844], [360, 800]]) {
  test(`Clean phone (${w} px): the cards of a shelf are one height, one corner, one border, and the next shows only its empty edge`, async (t) => {
    if (!browser) return t.skip(why);
    const page = await open(t, { ...PHONE, viewport: { width: w, height: h } });
    const r = await page.evaluate(() => ['#safety .instr', '.more-kit .kit', '#vpn .vs', '#setup .steps4'].map((s) => {
      const el = document.querySelector(s), kids = [...el.children], cs = kids.map((k) => getComputedStyle(k));
      const hs = kids.map((k) => Math.round(k.getBoundingClientRect().height));
      const second = kids[1].getBoundingClientRect(), pad = parseFloat(cs[1].paddingLeft);
      return { s, heights: [...new Set(hs)].length, radius: [...new Set(cs.map((c) => c.borderTopLeftRadius))], border: [...new Set(cs.map((c) => c.borderTopWidth + ' ' + c.borderTopStyle))], peek: innerWidth - second.left, pad };
    }));
    for (const x of r) {
      assert.equal(x.heights, 1, `${x.s}: the cards have ${x.heights} different heights`);
      assert.deepEqual(x.radius, ['16px'], `${x.s}: corners ${x.radius}`);
      assert.deepEqual(x.border, ['1px solid'], `${x.s}: borders ${x.border}`);
      assert.ok(x.peek > 4 && x.peek < x.pad, `${x.s}: ${Math.round(x.peek)} px of the next card show (its text starts ${x.pad} px in, so its words would show)`);
    }
    // Setup: each step's number on its own line over its words
    const st = await page.evaluate(() => { const li = document.querySelector('#setup .steps4 > li'), b = getComputedStyle(li, '::before'); return { pos: b.position, disp: b.display, pad: getComputedStyle(li).paddingLeft }; });
    assert.deepEqual(st, { pos: 'static', disp: 'block', pad: '20px' });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), w, 'the page got wider than the phone');
  });
}

test('Clean phone: the app tour shows only the marked part of the real window, whole, scaled to fit, and the marks sit on the app of today', async (t) => {
  if (!browser) return t.skip(why);
  const page = await open(t, PHONE);
  const r = await page.evaluate(async () => {
    const out = [], wait = () => new Promise((ok) => requestAnimationFrame(() => requestAnimationFrame(ok)));
    for (const tab of document.querySelectorAll('.tour-tabs button')) {
      tab.click(); await wait();
      for (let k = 0; k < 3; k++) {
        const crop = document.querySelector('.tour-crop'), b = crop.getBoundingClientRect(), inn = crop.querySelector('.cut-in'), ring = crop.querySelector('.cring').getBoundingClientRect();
        const s = parseFloat(getComputedStyle(crop.firstElementChild).getPropertyValue('--s')), clip = getComputedStyle(inn).clipPath;
        const name = tab.textContent + ' ' + (k + 1);
        if (!/^inset\(/.test(clip)) out.push(`${name}: nothing clips the rest of the window (${clip})`);
        if (!(s > 0 && s <= 2)) out.push(`${name}: scale ${s}`);
        if (ring.left < b.left + 8 || ring.right > b.right - 8 || ring.top < b.top + 8 || ring.bottom > b.bottom - 8) out.push(`${name}: the mark is not whole in its box`);
        if (crop.scrollWidth > crop.clientWidth) out.push(`${name}: the close-up scrolls sideways`);
        document.querySelector('.tour-next').click(); await wait();
      }
    }
    // the captures since 26 Nov. have the sidebar to 13.6 % of the width: a mark there points at the old app
    for (const mk of document.querySelectorAll('.marks .mk')) { const x = parseFloat(mk.style.getPropertyValue('--x')); if (x < 14) out.push(`${mk.dataset.pg} mark at x ${x}% is on the sidebar`); }
    return out;
  });
  assert.deepEqual(r, []);
  assert.equal(await page.evaluate(() => getComputedStyle(document.querySelector('.tour-detail'), '::before').content), 'none', 'the close-up carries a three-colour line');
});

test('Clean phone: the language notice has room (one line of text, the button across), and the header does not blur the 3D', async (t) => {
  if (!browser) return t.skip(why);
  const page = await open(t, { ...PHONE, viewport: { width: 360, height: 800 }, locale: 'pt-PT' });
  await page.waitForSelector('.lang-hint', { timeout: 10000 });
  const r = await page.evaluate(() => {
    const hint = document.querySelector('.lang-hint'), hb = hint.getBoundingClientRect(), span = hint.querySelector('span'), a = hint.querySelector('a').getBoundingClientRect();
    const lh = parseFloat(getComputedStyle(span).lineHeight);
    return { lines: Math.round(span.getBoundingClientRect().height / lh), wide: a.width / hb.width > .8, inside: hb.left >= 0 && hb.right <= innerWidth, blur: getComputedStyle(document.querySelector('.top')).backdropFilter };
  });
  assert.deepEqual(r, { lines: 1, wide: true, inside: true, blur: 'none' });
});

test('Clean phone: every guide has the picture of its 3D band, shown under the live scene on a phone', async (t) => {
  if (!browser) return t.skip(why);
  const guides = readdirSync(SITE, { withFileTypes: true }).filter((d) => d.isDirectory() && existsSync(join(SITE, d.name, 'index.html')) && readFileSync(join(SITE, d.name, 'index.html'), 'utf8').includes('class="gw"')).map((d) => d.name);
  assert.ok(guides.length >= 14, `${guides.length} guides`);
  for (const g of guides) {
    for (const l of ['', 'pt-br/']) {
      const html = readFileSync(join(SITE, l, g, 'index.html'), 'utf8');
      assert.ok(html.includes(`<img class="gw-poster" src="${l ? '../../' : '../'}img/gp-${g}.webp"`), `${l}${g}: no poster in its band`);
    }
    assert.ok(statSync(join(SITE, 'img', `gp-${g}.webp`)).size > 4000, `img/gp-${g}.webp`);
  }
  const page = await open(t, PHONE, 'cs2-ip-ban/');
  const r = await page.evaluate(() => { const p = document.querySelector('.gw-poster'), c = document.querySelector('.gw canvas'), pb = p.getBoundingClientRect(), cb = c.getBoundingClientRect(); return { shown: getComputedStyle(p).display !== 'none' && p.complete && p.naturalWidth > 0, same: Math.abs(pb.top - cb.top) < 1 && Math.abs(pb.height - cb.height) < 1 }; });
  assert.deepEqual(r, { shown: true, same: true });
});

