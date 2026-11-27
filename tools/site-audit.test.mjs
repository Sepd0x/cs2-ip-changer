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
const LANGS = ['', 'pt-br/', 'es/', 'ru/', 'tr/'];
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

// Review of the stats switch (25 Nov. 2026, findings 3, 6 and 12): the site said the basis was accepting the notice while the
// README and the privacy page said legitimate interest, and three sentences still read as when the stats were required.
test('Stats review: "Is it safe?" gives the legal basis the README and the privacy page give, in every language', () => {
  const basis = "legitimate interest in finding and fixing bugs (GDPR art. 6(1)(f))";
  const readme = readFileSync(join(ROOT, 'README.md'), 'utf8');
  assert.ok(readme.includes(`The legal basis is the author's ${basis}; you can object at any time by turning the stats off, at the first start or in Settings.`), 'README > Telemetry');
  assert.ok(readFileSync(join(ROOT, 'docs', 'PRIVACY-AND-SECURITY.md'), 'utf8').includes(basis), 'docs/PRIVACY-AND-SECURITY.md');
  assert.ok(plain(read('is-it-safe/index.html')).includes(`The legal basis is the author's ${basis}; you can object at any time by turning the stats off, at the first start or in Settings.`),
    'is-it-safe: the sentence of the README');
  for (const l of LANGS) {
    const h = plain(read(`${l}is-it-safe/index.html`));
    assert.ok(!/The basis is that you accept|La base es que aceptes|A base é você aceitar|Основание: вы принимаете|Dayanak, bildirimi kabul/.test(h), `${l}is-it-safe: the basis is still "you accept the notice"`);
    assert.ok(h.includes('6(1)(f)'), `${l}is-it-safe: no legal basis (GDPR art. 6(1)(f))`);
  }
  for (const l of LANGS.slice(1)) assert.ok(!plain(read(`${l}is-it-safe/index.html`)).includes('legitimate interest'), `${l}is-it-safe: the basis is still in English`);
});

test('Stats review: no sentence is left from when the stats were required (README, llms.txt)', () => {
  const readme = readFileSync(join(ROOT, 'README.md'), 'utf8');
  assert.ok(!readme.includes('usage stats of the beta'), 'README > Privacy: the stats are not "of the beta" any more');
  assert.ok(readme.includes('Besides the usage stats ([Telemetry](#telemetry)), the app only talks to the services below.'));
  // off: the ID and what was already counted stay on the PC (Telemetry.SetOff), so "nothing is kept" was not true
  assert.ok(!readme.includes('nothing is counted or kept'), 'README > Telemetry: off keeps the ID and what was waiting');
  assert.ok(readme.includes('nothing new is counted. The ID and what was already counted and waiting to go stay on your PC'), 'README > Telemetry: what off keeps');
  const llms = read('llms.txt');
  assert.ok(!llms.includes('before Accept'), 'llms.txt: Accept is not the only answer');
  assert.ok(llms.includes('Nothing is sent and no ID is made before you answer.'), 'llms.txt: as the README says it');
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
  assert.ok(/<a class="btn[^"]*"[^>]*href="https:\/\/github\.com\/Sepd0x\/pickroute\/releases\/latest\/download\/Pickroute\.exe"/.test(article(read('cs2-region-picker/index.html'))), 'no Download button in the text of the guide');
});

test('Final audit: the notices of the libraries are also served as a text file next to the scripts, the same as THIRD-PARTY-NOTICES', () => {
  assert.equal(read('assets/THIRD-PARTY.txt').replace(/\r\n/g, '\n'), readFileSync(join(ROOT, 'THIRD-PARTY-NOTICES'), 'utf8').replace(/\r\n/g, '\n'));
});

test('beta.14: the 3D posters are rendered in English, so the bar that offers the PC\'s language never sits over the scene', () => {
  const src = readFileSync(join(ROOT, 'tools', 'site-renders.mjs'), 'utf8');
  const opens = src.match(/browser\.new(Context|Page)\(\{[^\n]*\}\)/g) || [];
  assert.ok(opens.length >= 2, 'site-renders opens a context for the posters and a page for the stills');
  for (const o of opens) assert.match(o, /locale: 'en-US'/, o);
});

test('beta.14: site-legal --check reads a file with Windows line ends (core.autocrlf) as the same notice', () => {
  assert.ok(readFileSync(join(ROOT, 'tools', 'site-legal.mjs'), 'utf8').includes(".replace(/\\r\\n/g, '\\n')"), 'site-legal turns CRLF into LF before it compares');
});
