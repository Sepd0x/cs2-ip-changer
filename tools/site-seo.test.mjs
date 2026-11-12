// What search engines and AI answers read on the site (12 Nov. 2026): node --test tools/site-seo.test.mjs
// No browser needed. Fails when a guide is missing from the places a crawler finds it (the home's list, the footer, the
// sitemap in every language, llms.txt), when a title or a description is too long to show whole in a result, when the
// structured data of a page is not valid JSON or its FAQ differs from the page, when the questions people really search
// (Search Console, Nov. 2026) lose their page, and when the IndexNow step of the Pages workflow and the key file disagree.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = join(ROOT, 'site');
const BASE = 'https://sepd0x.github.io/cs2-ip-changer/';
const LANGS = ['', 'pt-br/', 'es/', 'ru/'];
const read = (f) => readFileSync(join(SITE, f), 'utf8');
const guides = readdirSync(SITE, { withFileTypes: true })
  .filter((d) => d.isDirectory() && !LANGS.includes(d.name + '/') && existsSync(join(SITE, d.name, 'index.html'))).map((d) => d.name);
const plain = (s) => s.replace(/&amp;/g, '&').replace(/&#39;|&apos;/g, "'").replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
const jsonLd = (html) => [...html.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)].map((m) => JSON.parse(m[1]));
const nodes = (html) => jsonLd(html).flatMap((d) => d['@graph'] || [d]);

test('SEO: every guide is in the home list, the footer, llms.txt and the sitemap in the four languages', () => {
  assert.ok(guides.length >= 10, `guides found: ${guides.length}`);
  const home = read('index.html'), llms = read('llms.txt'), map = read('sitemap.xml');
  const list = home.match(/<ul class="guides">[\s\S]*?<\/ul>/)[0], footer = home.match(/<footer>[\s\S]*?<\/footer>/)[0];
  const miss = [];
  for (const g of guides) {
    if (!list.includes(`href="${g}/"`)) miss.push(`${g}: not in the home's guide list`);
    if (!footer.includes(`href="${g}/"`)) miss.push(`${g}: not in the footer`);
    if (!llms.includes(`${BASE}${g}/`)) miss.push(`${g}: not in llms.txt`);
    for (const l of LANGS) {
      if (!map.includes(`<loc>${BASE}${l}${g}/</loc>`)) miss.push(`${l}${g}: not in sitemap.xml`);
      if (!existsSync(join(SITE, l, g, 'index.html'))) miss.push(`${l}${g}: no page`);
    }
  }
  assert.deepEqual(miss, []);
  // the numbers of the list run 01, 02, ... with no gap and no repeat
  const nums = [...list.matchAll(/<span class="n">(\d+)<\/span>/g)].map((m) => +m[1]);
  assert.deepEqual(nums, nums.map((_, i) => i + 1), 'the home list is numbered in order');
  assert.equal(nums.length, guides.length, 'one row per guide');
});

test('SEO: titles fit in 60 characters and descriptions in 155, in every language, and no two pages share one', () => {
  const bad = [], seen = new Map();
  for (const l of LANGS) for (const p of ['', ...guides.map((g) => g + '/')]) {
    const f = `${l}${p}index.html`, html = read(f);
    const title = plain(html.match(/<title>([^<]*)<\/title>/)[1]), desc = plain(html.match(/<meta name="description" content="([^"]*)"/)[1]);
    if (title.length > 60) bad.push(`${f}: title ${title.length}`);
    if (desc.length > 155) bad.push(`${f}: description ${desc.length}`);
    if (desc.length < 70) bad.push(`${f}: description only ${desc.length}`);
    for (const [kind, v] of [['title', title], ['description', desc]]) {
      const k = `${l}|${kind}|${v}`;
      if (seen.has(k)) bad.push(`${f}: same ${kind} as ${seen.get(k)}`);
      seen.set(k, f);
    }
    if (!html.includes(`<link rel="canonical" href="${BASE}${l}${p}">`)) bad.push(`${f}: canonical`);
  }
  assert.deepEqual(bad, []);
});

test('SEO: the structured data of every page is valid, and each FAQ question in it is on the page', () => {
  const bad = [];
  for (const l of LANGS) for (const p of ['', ...guides.map((g) => g + '/')]) {
    const f = `${l}${p}index.html`, html = read(f);
    let all;
    try { all = nodes(html); } catch (e) { bad.push(`${f}: JSON-LD does not parse (${e.message.slice(0, 60)})`); continue; }
    const faq = all.find((n) => n['@type'] === 'FAQPage');
    if (!faq) { bad.push(`${f}: no FAQPage`); continue; }
    const text = plain(html.replace(/<script[\s\S]*?<\/script>/g, '').replace(/<[^>]+>/g, ''));
    for (const q of faq.mainEntity) {
      if (!q.acceptedAnswer?.text) bad.push(`${f}: no answer for "${q.name}"`);
      if (!text.includes(q.name)) bad.push(`${f}: the question "${q.name.slice(0, 50)}" is in the data but not on the page`);
    }
    if (p && !all.some((n) => n['@type'] === 'BreadcrumbList')) bad.push(`${f}: no breadcrumbs`);
  }
  assert.deepEqual(bad.slice(0, 12), [], `${bad.length} problems`);
});

// Search Console, 28 Oct. to 10 Nov. 2026: the searches that showed the site. Each keeps a page that answers it in its words.
test('SEO: the questions people search have their answer on the right page', () => {
  const has = (page, ...words) => { const h = plain(read(`${page}/index.html`)); for (const w of words) assert.ok(h.includes(w), `${page}: "${w}"`); };
  has('cs2-region-picker', 'CS2 server picker', 'What is a CS2 server picker?', 'Is a CS2 server picker safe? Can it get you VAC banned?', 'How do I change my region in CS2?',
    'CS2 server pickers compared', 'github.com/FN-FAL113/server-picker-x', 'github.com/FN-FAL113/cs2-server-picker', 'github.com/oqyh/cs2-Region-Picker', 'github.com/Jyben/cs2-mm-server-picker');
  has('cs2-ip-ban', 'Does CS2 IP ban?', 'Can you get IP banned in CS2?', 'Is a VAC ban an IP ban?', 'How do I know if I am IP banned on a CS2 server?');
  has('copy-cs2-config', 'Copy CS2 settings to another account', 'Where is the CS2 config file?', 'How do I save or export my CS2 settings?');
  has('hide-ip-cs2', 'Why is my VPN blocked on CS2 community servers?', 'What is a VPN alternative for CS2?');
  has('cs2-mobile-data', 'Can you play CS2 on a mobile hotspot?', 'How much data does CS2 use per hour?', 'Is USB tethering better than a hotspot for gaming?');
  // the comparison is dated, so nobody reads an old row as today's
  assert.match(plain(read('cs2-region-picker/index.html')), /Checked on \d+ \w+ 20\d\d/);
  // llms.txt: the short answers an AI can quote, and the comparison's address
  const llms = read('llms.txt');
  for (const w of ['## What people use it for', '## Short answers', 'Is a VAC ban an IP ban?', 'server picker', `${BASE}cs2-region-picker/#cs2-server-pickers-compared`]) assert.ok(llms.includes(w), `llms.txt: "${w}"`);
  assert.ok(read('cs2-region-picker/index.html').includes('id="cs2-server-pickers-compared"'), "the comparison keeps the anchor llms.txt points to");
});

test('SEO: the Pages workflow tells IndexNow with the key that is on the site, and robots.txt names the sitemap', () => {
  const yml = readFileSync(join(ROOT, '.github/workflows/pages.yml'), 'utf8');
  const key = yml.match(/key=([0-9a-f]{32})/)?.[1];
  assert.ok(key, 'the IndexNow step has its key');
  assert.equal(read(`${key}.txt`).trim(), key, 'site/<key>.txt holds the same key');
  assert.match(yml, /api\.indexnow\.org\/indexnow/);
  assert.match(yml, /continue-on-error: true/, 'a failed ping never fails the deploy');
  assert.match(read('robots.txt'), new RegExp(`Sitemap: ${BASE}sitemap\\.xml`));
  // the plain-text sitemap (Search Console's "Couldn't fetch" on the XML, Nov. 2026) lists the same pages
  assert.match(read('robots.txt'), new RegExp(`Sitemap: ${BASE}sitemap\\.txt`));
  const xml = [...read('sitemap.xml').matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]).filter((u) => !u.includes('/img/'));
  assert.deepEqual(read('sitemap.txt').trim().split(/\r?\n/), xml, 'sitemap.txt has the addresses of sitemap.xml');
});
