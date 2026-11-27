// What AI answers and image search take from the site (27 Nov. 2026): node --test tools/site-ai-images.test.mjs
// That day Bing Images had no picture of ours, ChatGPT put another app's picture under our name and called it "a real screenshot
// from the official website", the AIs could not tell the app is in Turkish, and two of them did not find "Pickroute" on GitHub.
// Fails when a page shows a screenshot of the app under its bare name (regions.png) or with an alt that doesn't start with the
// app's name, when a page of the sitemap has no screenshot, when the Pages workflow doesn't publish the named copies, when a
// page doesn't say which languages the app is in, and when llms.txt loses the facts the AIs got wrong. No browser needed.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { SHOT_NAMES, SHOT_TOPIC, shotFile, shotSource, shotAlt } from './shot-names.mjs';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = join(ROOT, 'site');
const BASE = 'https://sepd0x.github.io/pickroute/';
const LANGS = ['', 'pt-br/', 'es/', 'ru/', 'tr/'];
const read = (f) => readFileSync(join(SITE, f), 'utf8');
const guides = readdirSync(SITE, { withFileTypes: true })
  .filter((d) => d.isDirectory() && !LANGS.includes(d.name + '/') && existsSync(join(SITE, d.name, 'index.html'))).map((d) => d.name);
const pages = LANGS.flatMap((l) => ['', ...guides.map((g) => g + '/')].map((g) => `${l}${g}index.html`));
const PAGES = Object.keys(SHOT_NAMES);

test('AI images: every screenshot has a name that says what it shows, and the name maps back to its file', () => {
  for (const p of PAGES) {
    assert.match(shotFile(p), /^pickroute-[a-z0-9-]+$/, `${p}: the published name starts with pickroute-`);
    assert.equal(shotSource(`${shotFile(p)}.png`), `${p}.png`);
    assert.equal(shotSource(`img/${shotFile(p)}.webp`), `${p}.png`);
    assert.ok(existsSync(join(ROOT, 'docs/screenshots', `${p}.png`)), `${p}: docs/screenshots/${p}.png is the file behind it`);
    assert.match(SHOT_TOPIC[p], /^Pickroute, /);
    assert.ok(shotAlt(p, 'x').startsWith(SHOT_TOPIC[p]));
  }
  assert.equal(new Set(Object.values(SHOT_NAMES)).size, PAGES.length, 'no two screenshots share a name');
  assert.equal(shotSource('icon.png'), 'icon.png', 'any other image is itself');
});

test('AI images: the Pages workflow publishes the named copies before the WebP step', () => {
  const wf = readFileSync(join(ROOT, '.github/workflows/pages.yml'), 'utf8');
  const copy = wf.indexOf('node tools/shot-names.mjs _site/img'), webp = wf.indexOf('cwebp ');
  assert.ok(copy > 0, 'the workflow runs tools/shot-names.mjs on _site/img');
  assert.ok(copy < webp, 'the named copies are made before the WebP loop, so each gets its WebP');
  assert.ok(wf.indexOf('cp docs/screenshots/*.png _site/img/') < copy, 'after the screenshots are copied in');
});

test('AI images: every page shows the app under the named file, with an alt that starts with Pickroute', () => {
  const bad = [];
  let seen = 0;
  for (const f of pages) {
    const html = read(f);
    for (const m of html.matchAll(/<img\b[^>]*>/g)) {
      const tag = m[0], src = (tag.match(/\ssrc="([^"]+)"/) || [])[1] || '', alt = (tag.match(/\salt="([^"]*)"/) || [])[1];
      const file = src.split('/').pop();
      if (PAGES.some((p) => file === `${p}.png`)) bad.push(`${f}: ${file} under its bare name`);
      if (/^pickroute-/.test(file)) {
        seen++;
        if (!alt || !alt.startsWith('Pickroute')) bad.push(`${f}: ${file} has the alt "${(alt || '').slice(0, 60)}"`);
      }
    }
    for (const m of html.matchAll(/srcset="([^"]+)"/g)) if (PAGES.some((p) => m[1].endsWith(`/${p}.webp`) || m[1] === `img/${p}.webp`)) bad.push(`${f}: srcset ${m[1]}`);
  }
  assert.deepEqual(bad, []);
  assert.ok(seen >= pages.length, `screenshots seen: ${seen} on ${pages.length} pages`);
});

test('AI images: the structured data names the screenshots with a caption, on the home pages and the guides', () => {
  for (const l of LANGS) {
    const ld = [...read(`${l}index.html`).matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)].map((m) => JSON.parse(m[1]));
    const app = ld.flatMap((d) => d['@graph'] || [d]).find((n) => n['@type'] === 'SoftwareApplication');
    assert.equal(app.screenshot.length, PAGES.length, `${l}: six screenshots`);
    for (const s of app.screenshot) {
      assert.equal(s['@type'], 'ImageObject');
      assert.match(s.url, new RegExp(`^${BASE}img/pickroute-`));
      assert.match(s.caption, /^Pickroute, /, `${l}: ${s.url} has a caption that starts with the name`);
    }
  }
  for (const g of guides) for (const l of LANGS) {
    const html = read(`${l}${g}/index.html`);
    const img = (html.match(/"primaryImageOfPage":\s*\{([\s\S]*?)\}/) || [])[1] || '';
    assert.match(img, /img\/pickroute-[a-z0-9-]+\.png/, `${l}${g}: primaryImageOfPage is a named screenshot`);
    assert.match(img, /"caption":\s*"Pickroute, /, `${l}${g}: with a caption`);
  }
});

test('AI images: every page in the sitemap, in every language, lists the screenshot it shows', () => {
  const map = read('sitemap.xml');
  const urls = [...map.matchAll(/<url>([\s\S]*?)<\/url>/g)].map((m) => m[1]);
  assert.equal(urls.length, pages.length, 'one entry per page');
  const bad = urls.filter((u) => !/<image:loc>https:\/\/sepd0x\.github\.io\/pickroute\/img\/pickroute-[a-z0-9-]+\.png<\/image:loc>/.test(u))
    .map((u) => /<loc>([^<]+)/.exec(u)[1]);
  assert.deepEqual(bad, []);
  assert.ok(!/img\/(connection|servers|accounts|regions|configs|profiles)\.png/.test(map), 'no bare names in the sitemap');
});

test('AI images: every page says which languages the app is in, in its own language', () => {
  const say = { '': 'app in English, Português, Español, Русский, Türkçe', 'pt-br/': 'app em português', 'es/': 'app en español', 'ru/': 'приложение на русском', 'tr/': 'uygulama Türkçe' };
  const miss = pages.filter((f) => !read(f).includes(say[LANGS.find((l) => l && f.startsWith(l)) || '']));
  assert.deepEqual(miss, []);
  assert.match(readFileSync(join(ROOT, 'README.md'), 'utf8'), /The app is in English, Português \(Brasil\), Español, Русский and Türkçe/);
});

test('AI images: llms.txt keeps the facts the AIs got wrong on 27 Nov. 2026', () => {
  const llms = read('llms.txt');
  for (const s of ['https://github.com/Sepd0x/pickroute (it was github.com/Sepd0x/cs2-ip-changer', 'pickroute.app', 'source-available, not open source',
    'the app itself is in English, Brazilian Portuguese, Spanish, Russian and Turkish', 'No phone is needed, and the block stays when the app is closed',
    'first stable release']) assert.ok(llms.includes(s), `llms.txt: ${s}`);
  for (const p of PAGES) assert.ok(llms.includes(`${BASE}img/${shotFile(p)}.png`), `llms.txt names the ${p} screenshot`);
});

// 27 Nov. 2026, later: a page with only the screenshots, so image search has a page whose title and captions say Pickroute and
// what people look for (ChatGPT and Perplexity show pictures from Bing Images with the title of the page they come from).
test('AI images: the screenshots page shows all six, the two main pages first, with captions and a gallery that start with Pickroute', () => {
  const order = ['connection', 'regions', 'servers', 'accounts', 'configs', 'profiles'];
  for (const l of LANGS) {
    const html = read(`${l}screenshots/index.html`);
    const title = html.match(/<title>([^<]*)<\/title>/)[1];
    assert.ok(title.includes('Pickroute') && title.length <= 60, `${l}: the title says Pickroute and fits (${title})`);
    const figs = [...html.matchAll(/<figure class="shotfig">([\s\S]*?)<\/figure>/g)].map((m) => m[1]);
    assert.deepEqual(figs.map((f) => (f.match(/<img src="[^"]*img\/(pickroute-[a-z0-9-]+)\.png"/) || [])[1]), order.map(shotFile), `${l}: the six screenshots, Connection and Regions first`);
    for (const f of figs) assert.match(f, /<figcaption>Pickroute, /, `${l}: every caption starts with Pickroute`);
    const g = [...html.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)].map((m) => JSON.parse(m[1]))
      .flatMap((d) => d['@graph'] || [d]).find((n) => n['@type'] === 'ImageGallery');
    assert.ok(g, `${l}: an ImageGallery`);
    assert.deepEqual(g.image.map((i) => i.url), order.map((p) => `${BASE}img/${shotFile(p)}.png`));
    for (const i of g.image) { assert.match(i.caption, /^Pickroute, /); assert.match(i.name, /^Pickroute, /); }
    assert.ok(read(`${l}index.html`).includes('href="screenshots/"'), `${l}: the home links to it`);
  }
  const map = read('sitemap.xml'), entry = map.match(new RegExp(`<url>\\s*<loc>${BASE}screenshots/</loc>[\\s\\S]*?</url>`))[0];
  for (const p of order) assert.ok(entry.includes(`${BASE}img/${shotFile(p)}.png`), `sitemap: the screenshots page lists ${p}`);
  assert.ok(read('llms.txt').includes(`${BASE}screenshots/`), 'llms.txt points to it');
});

// A9: a guide title ends with " · Pickroute" in the languages where it still fits in 60 characters, so the label under an image
// or a citation carries the name; where it doesn't fit, the title stays as it was.
test('AI images: guide titles end with the name where it fits in 60 characters', () => {
  const tagged = { 'ru/': ['change-ip-with-phone', 'cs2-config-file-location', 'cs2-ping-test', 'cs2-region-picker', 'hide-ip-cs2'], 'tr/': ['cs2-iphone-hotspot', 'cs2-mobile-data'], 'es/': ['cs2-ping-test'] };
  for (const [l, gs] of Object.entries(tagged)) for (const g of gs) {
    const t = read(`${l}${g}/index.html`).match(/<title>([^<]*)<\/title>/)[1].replace(/&#39;|&apos;/g, "'").replace(/&amp;/g, '&');
    assert.ok(t.endsWith(' · Pickroute') && t.length <= 60, `${l}${g}: ${t}`);
  }
});
