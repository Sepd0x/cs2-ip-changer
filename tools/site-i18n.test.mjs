// The site in other languages (tools/site-i18n.mjs): node --test tools/site-i18n.test.mjs
// Fails when a text of the English site has no translation, when a translated page is older than the English one (it was
// not made again after a change), when hreflang, the canonical address, <html lang> or the sitemap are wrong, when a
// translated page points to a file that isn't there, and, in a real browser (Playwright, as tools/site.test.mjs finds it;
// skipped without it, never passed), when a translated page has an error, a failed request, no 3D, a page wider than the
// screen or a word out of its button, at 390 and 1440 px.
import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build, loadDicts, LANGS, BASE, same } from './site-i18n.mjs';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = join(ROOT, 'site');
const read = (f) => readFileSync(join(SITE, f), 'utf8');
const { files, report, all } = build();
const pages = all.filter((p) => !p.abs);
const url = (L, p) => BASE + (L.code === 'en' ? '' : `${L.code}/`) + p.url;
const fileOf = (L, p) => (L.code === 'en' ? '' : `${L.code}/`) + p.file;

test('i18n: every text of the English site has a translation in every language, with its placeholders', () => {
  const miss = [...new Set(report.missing.map((m) => `${m.lang} ${m.group}: ${m.key.slice(0, 80)}`))];
  assert.deepEqual(miss.slice(0, 15), [], `${miss.length} texts without a translation (node tools/site-i18n.mjs --todo <folder>)`);
  const bad = [...new Set(report.bad.map((m) => `${m.lang} ${m.group}: ${m.key.slice(0, 80)}`))];
  assert.deepEqual(bad, [], 'translations that lost or changed a placeholder (<a1>, <code2/>) or a {token}');
});

test('i18n: no translation is left over from an English text that changed or went', () => {
  const dicts = loadDicts(), used = new Set(report.used.map((u) => u.key));
  for (const L of LANGS.slice(1)) {
    const extra = Object.entries(dicts[L.code]).flatMap(([g, d]) => Object.keys(d).filter((k) => !used.has(k)).map((k) => `${g}: ${k.slice(0, 70)}`));
    assert.deepEqual(extra.slice(0, 10), [], `${L.code}: ${extra.length} translations of texts the site no longer has (delete them from tools/i18n/${L.code}.json)`);
  }
});

test('i18n: no translated page is older than the English (node tools/site-i18n.mjs makes them again)', () => {
  const stale = Object.entries(files).filter(([f, s]) => !existsSync(join(SITE, f)) || !same(read(f), s)).map(([f]) => f);
  assert.deepEqual(stale, [], 'out of date: node tools/site-i18n.mjs (or node tools/site-pages.mjs)');
});

test('i18n: <html lang>, canonical, hreflang (with x-default) and og:locale on every page, both ways', () => {
  for (const L of LANGS) for (const p of pages) {
    const h = read(fileOf(L, p)), f = fileOf(L, p);
    assert.equal(/<html lang="([^"]+)"/.exec(h)?.[1], L.hreflang, `${f}: <html lang>`);
    assert.equal(/<link rel="canonical" href="([^"]+)">/.exec(h)?.[1], url(L, p), `${f}: canonical`);
    assert.equal(/<meta property="og:url" content="([^"]+)">/.exec(h)?.[1], url(L, p), `${f}: og:url`);
    assert.equal(/<meta property="og:locale" content="([^"]+)">/.exec(h)?.[1], L.og, `${f}: og:locale`);
    const alt = Object.fromEntries([...h.matchAll(/<link rel="alternate" hreflang="([^"]+)" href="([^"]+)">/g)].map((m) => [m[1], m[2]]));
    assert.deepEqual(alt, { ...Object.fromEntries(LANGS.map((M) => [M.hreflang, url(M, p)])), 'x-default': url(LANGS[0], p) }, `${f}: hreflang`);
    // the structured data speaks the page's language and points to its own address
    for (const m of h.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)) {
      const s = m[1];
      JSON.parse(s);
      for (const l of s.matchAll(/"inLanguage": "([^"]+)"/g)) assert.equal(l[1], L.hreflang, `${f}: inLanguage`);
      if (L.code !== 'en') assert.ok(!s.includes(`"url": "${url(LANGS[0], p)}"`), `${f}: structured data points to the English page`);
    }
  }
});

test('i18n: sitemap.xml lists every page in every language, each with all its alternates', () => {
  const x = read('sitemap.xml');
  assert.ok(x.startsWith('<?xml version="1.0" encoding="UTF-8"?>') && x.includes('xmlns:xhtml="http://www.w3.org/1999/xhtml"'));
  assert.equal((x.match(/<url>/g) || []).length, (x.match(/<\/url>/g) || []).length);
  const urls = [...x.matchAll(/<url>([\s\S]*?)<\/url>/g)].map((m) => m[1]);
  const locs = urls.map((u) => /<loc>([^<]+)<\/loc>/.exec(u)[1]);
  assert.deepEqual([...locs].sort(), LANGS.flatMap((L) => pages.map((p) => url(L, p))).sort(), 'one <url> per page and language');
  for (const u of urls) {
    const loc = /<loc>([^<]+)<\/loc>/.exec(u)[1], p = pages.find((q) => LANGS.some((L) => url(L, q) === loc));
    const alt = Object.fromEntries([...u.matchAll(/<xhtml:link rel="alternate" hreflang="([^"]+)" href="([^"]+)"\/>/g)].map((m) => [m[1], m[2]]));
    assert.deepEqual(alt, { ...Object.fromEntries(LANGS.map((M) => [M.hreflang, url(M, p)])), 'x-default': url(LANGS[0], p) }, `${loc}: alternates`);
    assert.match(u, /<lastmod>\d{4}-\d\d-\d\d<\/lastmod>/, `${loc}: lastmod`);
  }
});

// what the Pages workflow adds to img/ (.github/workflows/pages.yml): the screenshots and pictures from docs/, with a WebP each
const PAGES_IMG = (n) => [join(ROOT, 'docs/screenshots', n), join(ROOT, 'docs/assets', n), join(ROOT, 'docs/screenshots', n.replace(/\.webp$/, '.png'))];
const served = (rel) => {
  const f = join(SITE, rel);
  if (existsSync(f) && (!statSync(f).isDirectory() || existsSync(join(f, 'index.html')))) return true;
  return rel.startsWith('img/') && PAGES_IMG(rel.slice(4)).some(existsSync);
};

test('i18n: every address on a translated page leads to a file, and its links stay in its language', () => {
  for (const L of LANGS.slice(1)) for (const p of all) {
    const f = fileOf(L, p), h = read(f), from = `/${L.code}/${p.dir}`;
    // the language picker and the hreflang links lead to the other languages on purpose
    const own = h.replace(/<!--i18n(:[a-z]+)?-->[\s\S]*?<!--\/i18n\1-->/g, '');
    const refsOf = (s) => [...s.matchAll(/\s(?:href|src|poster|srcset)="([^"]+)"/g)].flatMap((m) => m[1].split(',').map((x) => x.trim().split(/\s+/)[0]))
      .concat([...s.matchAll(/url\(['"]?([^'")]+)['"]?\)/g)].map((m) => m[1]));
    const where = (r) => {
      if (/^(https?:|data:|mailto:|#|javascript:)/.test(r) || r.startsWith('/') && !r.startsWith('/cs2-ip-changer/')) return null;
      const path = r.startsWith('/cs2-ip-changer/') ? r.slice('/cs2-ip-changer'.length) : new URL(r, `http://x${p.abs ? `/${L.code}/` : from}`).pathname;
      return decodeURIComponent(path.split(/[?#]/)[0]).replace(/^\//, '');
    };
    for (const r of refsOf(h)) { const rel = where(r); if (rel != null) assert.ok(served(rel), `${f}: ${r} leads to /${rel}, which isn't there`); }
    // a page of the site (a folder with an index.html, or the home) must be the one in this language
    for (const r of refsOf(own)) { const rel = where(r); if (rel != null && (rel === '' || rel.endsWith('/'))) assert.ok(rel.startsWith(`${L.code}/`), `${f}: ${r} leaves ${L.code}`); }
  }
});

// ------------------------------------------------------------------ in a browser
async function findChromium() {
  const where = [process.env.PLAYWRIGHT, ROOT];
  const g = spawnSync('npm root -g', { encoding: 'utf8', shell: true });
  if (g.status === 0 && g.stdout.trim()) where.push(g.stdout.trim(), join(g.stdout.trim(), '@playwright', 'cli'));
  for (const w of where.filter(Boolean)) {
    try { const m = await import(pathToFileURL(createRequire(join(resolve(w), 'x.js')).resolve('playwright')).href); return m.chromium || m.default.chromium; } catch { /* not there */ }
  }
  return null;
}
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.woff2': 'font/woff2', '.json': 'application/json', '.ico': 'image/x-icon', '.txt': 'text/plain' };
let browser = null, server = null, origin = '', why = '';
before(async () => {
  const chromium = await findChromium();
  if (!chromium) { why = 'no Playwright (PLAYWRIGHT=<folder with it>)'; return; }
  for (const o of [{ channel: 'chrome' }, {}]) { try { browser = await chromium.launch(o); break; } catch (e) { why = `Chromium did not start: ${e.message.split('\n')[0]}`; } }
  if (!browser) return;
  // like GitHub Pages: site/ under /cs2-ip-changer/, the Pages workflow's img/ from docs/, and 404.html for a missing address
  server = createServer((q, s) => {
    const p = decodeURIComponent(q.url.split('?')[0]);
    if (!p.startsWith('/cs2-ip-changer/')) { s.writeHead(404); return s.end(); }
    const rel = p.slice('/cs2-ip-changer/'.length);
    let f = resolve(SITE, rel || '.');
    if (f !== SITE && !f.startsWith(SITE + sep)) { s.writeHead(404); return s.end(); }
    if (existsSync(f) && statSync(f).isDirectory()) f = join(f, 'index.html');
    if (!existsSync(f) && rel.startsWith('img/')) f = PAGES_IMG(rel.slice(4)).find(existsSync) || f;
    if (!existsSync(f)) { s.writeHead(404, { 'content-type': TYPES['.html'] }); return s.end(readFileSync(join(SITE, '404.html'))); }
    s.writeHead(200, { 'content-type': TYPES[extname(f)] || 'application/octet-stream' });
    s.end(readFileSync(f));
  });
  await new Promise((ok) => server.listen(0, '127.0.0.1', ok));
  origin = `http://127.0.0.1:${server.address().port}/cs2-ip-changer/`;
});
after(async () => { await browser?.close(); server?.close(); });

async function open(path, width, locale = 'en-US') {
  const phone = width < 760;
  const ctx = await browser.newContext({ viewport: { width, height: phone ? 844 : 900 }, isMobile: phone, hasTouch: phone, locale });
  const page = await ctx.newPage(), bad = [], errors = [];
  page.on('response', (r) => { if (r.status() >= 400 && !r.url().includes('workers.dev') && r.url() !== origin + path) bad.push(`${r.status()} ${r.url().replace(origin, '')}`); });
  page.on('requestfailed', (r) => { if (!r.url().includes('workers.dev')) bad.push(`failed ${r.url().replace(origin, '')}`); });
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(origin + path, { waitUntil: 'load' });
  // the loading screen goes when the 3D world has drawn (worldready) or gave up
  await page.waitForFunction(() => !document.documentElement.classList.contains('ld'), null, { timeout: 30000 }).catch(() => {});
  return { ctx, page, bad, errors };
}

for (const L of LANGS.slice(1)) {
  test(`i18n browser: ${L.code}, the home, a guide and the 404 at 390 and 1440 px: no error, no failed file, the 3D, nothing out of its box`, async (t) => {
    if (!browser) return t.skip(why);
    for (const path of [`${L.code}/`, `${L.code}/cs2-ip-ban/`, `${L.code}/cs2-region-picker/`]) for (const w of [390, 1440]) {
      const { ctx, page, bad, errors } = await open(path, w);
      const r = await page.evaluate(() => {
        const out = [], h = document.documentElement;
        // words that run out of a button, a link of the header or a label (a 1 px rounding is fine)
        for (const e of document.querySelectorAll('.btn, .top nav a, .top nav summary, .chip, .tabs button, .ph-tabs button, .tour-tabs button, summary, .lang-foot a')) {
          const s = getComputedStyle(e);
          if (!e.offsetParent || s.visibility === 'hidden' || s.overflowX === 'auto' || s.overflowX === 'scroll') continue;
          if (e.scrollWidth > e.clientWidth + 1) out.push(`${e.className || e.tagName}: "${e.textContent.trim().slice(0, 40)}" ${e.scrollWidth}>${e.clientWidth}`);
        }
        return { wide: h.scrollWidth - innerWidth, lang: h.lang, gl: !h.classList.contains('no-gl'), over: out.slice(0, 8) };
      });
      assert.deepEqual(errors, [], `${path} @${w}: script errors`);
      assert.deepEqual(bad, [], `${path} @${w}: failed requests`);
      assert.equal(r.lang, L.hreflang, `${path}: lang`);
      assert.ok(r.wide <= 0, `${path} @${w}: the page is ${r.wide} px wider than the screen`);
      assert.deepEqual(r.over, [], `${path} @${w}: text out of its box`);
      await ctx.close();
    }
  });
}

test('i18n browser: every translated page fits a 320 px phone, and a 390 px phone with the text at 200% (UI-004)', async (t) => {
  if (!browser) return t.skip(why);
  const wide = [];
  for (const L of LANGS.slice(1)) for (const p of all) {
    const path = `${L.code}/${p.abs ? '404.html' : p.url}`;
    for (const [w, zoom] of [[320, 0], [390, 200]]) {
      const { ctx, page } = await open(path, w);
      if (zoom) { await page.evaluate((z) => document.documentElement.style.setProperty('font-size', `${z}%`, 'important'), zoom); await page.waitForTimeout(400); }
      const sw = await page.evaluate(() => document.documentElement.scrollWidth);
      if (sw > w) wide.push(`${path} @${w}${zoom ? ` text ${zoom}%` : ''}: ${sw} px`);
      await ctx.close();
    }
  }
  assert.deepEqual(wide, [], 'pages wider than the phone');
});

test('i18n browser: a missing address under /es/ shows the Spanish 404 and keeps the address; the picker leads to the same page', async (t) => {
  if (!browser) return t.skip(why);
  const { ctx, page } = await open('es/no-such-page/', 1440);
  await page.waitForFunction(() => document.documentElement.lang === 'es', null, { timeout: 10000 });
  assert.equal(new URL(page.url()).pathname, '/cs2-ip-changer/es/no-such-page/');
  await ctx.close();
  const g = await open('ru/cs2-ip-ban/', 1440);
  const hrefs = await g.page.$$eval('.top .lang-pick a', (as) => as.map((a) => new URL(a.href).pathname));
  assert.deepEqual(hrefs, ['/cs2-ip-changer/cs2-ip-ban/', '/cs2-ip-changer/pt-br/cs2-ip-ban/', '/cs2-ip-changer/es/cs2-ip-ban/', '/cs2-ip-changer/ru/cs2-ip-ban/']);
  await g.ctx.close();
});

test('i18n browser: an English page offers the version in the browser\'s language once, and never moves by itself', async (t) => {
  if (!browser) return t.skip(why);
  const { ctx, page } = await open('cs2-ip-ban/', 1440, 'pt-BR');
  await page.waitForSelector('.lang-hint', { timeout: 6000 });
  assert.equal(new URL(page.url()).pathname, '/cs2-ip-changer/cs2-ip-ban/', 'no redirect');
  assert.equal(await page.$eval('.lang-hint a', (a) => new URL(a.href).pathname), '/cs2-ip-changer/pt-br/cs2-ip-ban/');
  await page.click('.lang-hint button');
  await page.reload({ waitUntil: 'load' });
  await page.waitForTimeout(2500);
  assert.equal(await page.$('.lang-hint'), null, 'closed for good');
  await ctx.close();
  const en = await open('cs2-ip-ban/', 1440, 'en-US');
  await en.page.waitForTimeout(2500);
  assert.equal(await en.page.$('.lang-hint'), null, 'nothing for an English browser');
  await en.ctx.close();
});
