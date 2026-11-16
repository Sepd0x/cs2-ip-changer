// Final audit before 1.0.0 (13 Nov. 2026, report G) and the SEO measurement of 15 Nov. 2026, as tests on the pages: node --test tools/site-audit.test.mjs
// No browser needed. What the pages say must be true (the old name, the license, what the app changes, the home IP and protection,
// the notice about Valve on the 404), the MIT notices of the 3D libraries must travel with the scripts, and the small SEO requests
// of the measurement (the copy-config page, the links between guides, the region guide's words and its Download button).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = join(ROOT, 'site');
const LANGS = ['', 'pt-br/', 'es/', 'ru/'];
const read = (f) => readFileSync(join(SITE, f), 'utf8');
const guides = readdirSync(SITE, { withFileTypes: true })
  .filter((d) => d.isDirectory() && !LANGS.includes(d.name + '/') && existsSync(join(SITE, d.name, 'index.html'))).map((d) => d.name);
const plain = (s) => s.replace(/&amp;/g, '&').replace(/&#39;|&apos;/g, "'").replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
const walk = (d) => readdirSync(join(SITE, d), { withFileTypes: true }).flatMap((e) => e.isDirectory() ? walk(join(d, e.name)) : [join(d, e.name)]);
const noCode = (h) => h.replace(/<style[\s\S]*?<\/style>|<script[\s\S]*?<\/script>/g, '');
// the text of a guide: from the article to the FAQ (no header, no side card list, no footer)
const article = (h) => h.slice(h.indexOf('<article class="gtext">'), h.indexOf('<section class="gfaq"'));

test('Final audit: the old name of the app is nowhere on the site (the owner dropped "ServerRouteTool", 13 Nov.)', () => {
  const files = walk('').filter((f) => /\.(html|txt|xml|json|css|svg)$/.test(f) || (f.endsWith('.js') && !f.endsWith('.min.js')));
  assert.ok(files.length > 50);
  assert.deepEqual(files.filter((f) => /serverroutetool/i.test(readFileSync(join(SITE, f), 'utf8'))), []);
});

test('Final audit: the license answer says what LICENSE says', () => {
  assert.match(readFileSync(join(ROOT, 'LICENSE'), 'utf8'), /may not copy, modify, merge, publish, redistribute/);
  const en = plain(read('is-it-safe/index.html'));
  assert.ok(!/read and build/i.test(en), 'it cannot say anyone may build it: LICENSE does not allow that');
  assert.ok(en.includes('All rights reserved: you can download and run the official releases'), 'the answer follows LICENSE');
  assert.ok(!/during the beta: you can use/i.test(en), 'the license is not "during the beta"');
});

test('Final audit: "Is it safe?" lists the launch options, the ADB download and the rest of the stats, in every language', () => {
  for (const l of LANGS) assert.ok(plain(read(`${l}is-it-safe/index.html`)).includes('-condebug -conclearlog'), `${l}is-it-safe: the launch options CS2 gets are not listed`);
  const en = plain(read('is-it-safe/index.html'));
  for (const w of ['launch options', 'downloads Google', 'how far down each screen you scroll', 'whether the report came from a hosting network']) assert.ok(en.includes(w), `is-it-safe: "${w}"`);
  for (const l of LANGS.slice(1)) assert.ok(!plain(read(`${l}is-it-safe/index.html`)).includes('how far down each screen'), `${l}: the new list item is still in English`);
});

test('Final audit: the README says what the website keeps in the browser (language and motion choice, no ID)', () => {
  const r = readFileSync(join(ROOT, 'README.md'), 'utf8');
  assert.ok(!r.includes('Nothing is stored in your browser'), 'the site keeps cs2-lang, cs2-fx and cs2-motion in localStorage');
  assert.match(r, /cs2-lang/);
});

test('Final audit: "your home IP never reaches the server" only with protection on, in every language', () => {
  for (const l of LANGS) assert.ok(!/<li>[^<]{0,6}(Your home IP never reaches|Tu IP de casa nunca|Seu IP de casa nunca|Ваш домашний IP никогда)/i.test(read(`${l}cs2-ip-ban/index.html`)), `${l}cs2-ip-ban: the sentence has no condition`);
  assert.ok(plain(read('cs2-ip-ban/index.html')).includes('With protection on, your home IP never reaches the server'));
});

test('Final audit: the 404 page names Valve like every other page, in the four languages', () => {
  for (const l of LANGS) {
    const h = noCode(read(`${l}404.html`));
    assert.ok(h.includes('Valve'), `${l}404.html: no notice about Valve`);
    assert.ok(h.includes('Counter-Strike'), `${l}404.html: Counter-Strike and Steam are not named as Valve's marks`);
  }
});

test('Final audit: the MIT notices of three.js and Lenis travel with the scripts the site serves', () => {
  for (const f of ['assets/js/scenes.min.js', 'assets/guides/world.min.js', 'assets/js/lenis.min.js']) {
    const head = read(f).slice(0, 3000);
    assert.match(head, /^\/\*!/, `${f}: starts with a license comment`);
    assert.match(head, /Permission is hereby granted, free of charge/, `${f}: the MIT permission notice`);
    assert.match(head, /Copyright/, `${f}: the copyright line`);
  }
  assert.match(read('assets/js/scenes.min.js').slice(0, 3000), /three\.js/i);
  assert.match(read('assets/guides/world.min.js').slice(0, 3000), /three\.js/i);
  assert.match(read('assets/js/lenis.min.js').slice(0, 3000), /darkroom\.engineering/);
  assert.ok(existsSync(join(ROOT, 'tools', 'site-legal.mjs')), 'tools/site-legal.mjs puts the notices back after a build');
});

test('SEO measurement 15 Nov.: /copy-cs2-config/ answers "copy settings from one account to another"; it and the file location guide link each other', () => {
  const h = read('copy-cs2-config/index.html');
  assert.equal(plain(h.match(/<title>([^<]*)<\/title>/)[1]), 'How to copy CS2 settings to another account (all of them)');
  const lead = plain(h.match(/<p class="lead">([\s\S]*?)<\/p>/)[1]);
  for (const w of ['copy all CS2 settings', 'from one account to another', 'export CS2 settings']) assert.ok(lead.includes(w), `the first paragraph lacks "${w}"`);
  for (const l of LANGS) {
    assert.ok(article(read(`${l}copy-cs2-config/index.html`)).includes('href="../cs2-config-file-location/"'), `${l}copy-cs2-config does not link to the file location guide in its text`);
    assert.ok(article(read(`${l}cs2-config-file-location/index.html`)).includes('href="../copy-cs2-config/"'), `${l}cs2-config-file-location does not link to the copy guide in its text`);
  }
  assert.ok(article(read('copy-cs2-config/index.html')).includes('<a href="../cs2-config-file-location/">where CS2 saves your config</a>'));
  assert.ok(article(read('cs2-config-file-location/index.html')).includes('<a href="../copy-cs2-config/">copy your config to another account</a>'));
});

test('SEO measurement 15 Nov.: the three newest guides are linked from the text of other guides and from the home, not only from the lists', () => {
  for (const g of ['cs2-mobile-data', 'cs2-ping-test', 'cs2-config-file-location']) {
    const from = guides.filter((o) => o !== g && article(read(`${o}/index.html`)).includes(`href="../${g}/"`));
    assert.ok(from.length >= 3, `${g}: only ${from.length} guides link to it in their text (${from.join(', ')})`);
  }
  const home = read('index.html'), faq = home.slice(home.indexOf('<details><summary>Is it a VPN?'));
  for (const g of ['cs2-mobile-data', 'cs2-ping-test']) assert.ok(faq.includes(`href="${g}/"`), `home: no link to ${g} in its FAQ`);
});

test('SEO measurement 15 Nov.: the region guide names what people call a server picker and the tool most AIs list first, and has a Download button in its text', () => {
  const h = plain(read('cs2-region-picker/index.html'));
  for (const w of ['region changer', 'region selector', 'region blocker', 'region lock', 'Steam Route Tool']) assert.ok(h.includes(w), `cs2-region-picker: "${w}"`);
  assert.ok(/<a class="btn[^"]*"[^>]*href="https:\/\/github\.com\/Sepd0x\/cs2-ip-changer\/releases\/latest\/download\/CS2IPChanger\.exe"/.test(article(read('cs2-region-picker/index.html'))), 'no Download button in the text of the guide');
});

test('Final audit: the notices of the libraries are also served as a text file next to the scripts, the same as THIRD-PARTY-NOTICES', () => {
  assert.equal(read('assets/THIRD-PARTY.txt').replace(/\r\n/g, '\n'), readFileSync(join(ROOT, 'THIRD-PARTY-NOTICES'), 'utf8').replace(/\r\n/g, '\n'));
});
