// Direction D (26 Nov. 2026): the home keeps its story in scroll with the 3D, and on a phone the lists of equals after it
// swipe sideways, the VPN table reads one row at a time with both answers, the footer's lists open in place and "Send the link to
// my PC" waits at the bottom from the app to the guides (after a designer's and a critic's review the same night). On a PC
// nothing of this shows. node --test tools/site-direcao-d.test.mjs
// Needs Playwright (PLAYWRIGHT=<folder>, the repo or the global npm); without it the browser tests are skipped, never passed.
import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
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
