// 27 Nov. 2026 (the owner: "a dashboard também continua desatualizada"): the owner's page for "more users". The two doors side by
// side, the stuck note that no longer says "after Protected" when nobody plugged a phone in, the whole way from the site to coming
// back, the site's pages, languages and countries, and the visits with no referrer named for what they are. Nothing the page had
// is taken out. Run with the rest: node --test "worker/stats/test/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { seed } from './v4.test.mjs';
import { briefOf } from './brief.test.mjs';
import { buildHome, hostName, pageName, countryName, relName, NO_REF } from '../dashboard/v5/model.js';

const NOW = Date.parse('2026-11-20T15:30:00Z');
const clone = (x) => JSON.parse(JSON.stringify(x));
const brief = async (DB) => clone(await briefOf(DB, { G: null, L: null, now: NOW }));
const day = (d) => new Date(NOW - d * 864e5).toISOString().slice(0, 10);
// The 19 Nov. case: everyone picked regions or nothing, nobody plugged a phone in
const regionsOnly = () => seed([
  [9, 1, 'none', { first_start: 1, regions_blocked: 2, m_valve: 1 }],
  [9, 2, 'none', { first_start: 1, regions_blocked: 2 }],
  [9, 3, 'none', { first_start: 1, regions_blocked: 6 }],
  [9, 4, 'none', { first_start: 1, regions_blocked: 1 }],
  [9, 5, 'none', { first_start: 1 }],
], { now: NOW });
const withSite = (DB) => {
  const S = DB.raw.prepare('INSERT INTO site (day, page, ref, country, event, n) VALUES (?, ?, ?, ?, ?, ?)');
  S.run(day(2), '/cs2-region-picker/', 'google.com', 'TR', 'view', 30); S.run(day(2), '/cs2-region-picker/', 'google.com', 'TR', 'download', 4);
  S.run(day(3), '/tr/cs2-region-picker/', '', 'TR', 'view', 12); S.run(day(3), '/', 'yandex.ru', 'RU', 'view', 8); S.run(day(3), '/', '', 'PT', 'download', 1);
  S.run(day(0), '/', 'google.com', 'PT', 'view', 50);    // today: not in the 7 complete days
  S.run(day(12), '/is-it-safe/', 'bing.com', 'US', 'view', 40);   // older than 7 days
  return DB;
};

test('owner page: the two doors are side by side, each with its own steps, and nothing of the old list is lost', async () => {
  const H = buildHome({ B: await brief(regionsOnly()), G: null, L: null });
  const j = H.journey;
  assert.deepEqual(j.groups.map((g) => g.title), ['Pelas regiões', 'Pelo telemóvel']);
  assert.deepEqual(j.groups[0].rows.map((r) => [r.label, r.n]), [['Escolheram regiões', 4], ['e jogaram com a app aberta', 1]]);
  assert.deepEqual(j.groups[1].rows.map((r) => r.n), [0, 0, 0]);
  assert.equal(j.opened.n, 5);
  assert.deepEqual(j.end.map((r) => [r.label, r.n]), [['Usaram uma das portas', 4], ['Pessoas que jogaram uma partida', 1]]);
  // the old steps are still there (v4 and the tests read them)
  assert.deepEqual(j.steps.map((s) => s.id), ['first_start', 'phone_seen', 'protected', 'regions', 'played']);
});

test('owner page: with nobody on the phone the note no longer says they stopped "after Protected"', async () => {
  const H = buildHome({ B: await brief(regionsOnly()), G: null, L: null });
  assert.ok(!/depois de ligar a proteção e antes de jogar/.test(H.journey.note), H.journey.note);
  assert.match(H.journey.note, /As regiões ficam bloqueadas com a app fechada/);
  assert.match(H.journey.note, /\(3 de 5\)\.$/);
  assert.ok(!/\.\./.test(H.journey.text), 'no double full stop after a date like "12 nov."');
});

test('owner page: the way from the site to coming back, three blocks never divided by each other', async () => {
  const H = buildHome({ B: await brief(withSite(regionsOnly())), G: null, L: null });
  const p = H.path;
  assert.deepEqual(p.before.map((x) => x.label), ['Visitas ao site', 'Cliques em Download', 'Downloads da app', 'Instalações novas']);
  assert.equal(p.before[0].n, 50);   // 30 + 12 + 8, today and 12 days ago left out
  assert.equal(p.before[1].n, 5);
  assert.equal(p.before[1].rate, 0.1);
  assert.equal(p.after.base, 5);
  assert.deepEqual(p.after.steps.map((x) => x.n), [5, 4, 1]);
  assert.match(p.lines.join(' '), /1 em cada 10 acaba num clique em Download/);
  assert.match(p.lines.join(' '), /1 de 5 abriram a app e não escolheram regiões nem ligaram o telemóvel/);
});

test('owner page: the pages that bring people, their language, and the countries, over the 7 complete days', async () => {
  const B = await brief(withSite(regionsOnly()));
  assert.deepEqual(B.acquisition.top_site_pages_d7.map((r) => [r.page, r.views, r.download_clicks]), [['/cs2-region-picker/', 30, 4], ['/tr/cs2-region-picker/', 12, 0], ['/', 8, 1]]);
  assert.deepEqual(B.acquisition.top_site_countries_d7.map((r) => [r.country, r.n]), [['TR', 42], ['RU', 8]]);
  const H = buildHome({ B, G: null, L: null });
  assert.deepEqual(H.pages.map((x) => [x.name, x.lang]), [['Guia: escolher a região no CS2', 'Inglês'], ['Guia: escolher a região no CS2', 'Turco'], ['Página inicial', 'Inglês']]);
  assert.deepEqual(H.langs.map((x) => [x.name, x.n]), [['Inglês', 38], ['Turco', 12]]);
  assert.equal(H.countries[0].name, countryName('TR'));
  assert.equal(Math.round(100 * H.countries.reduce((a, x) => a + x.share, 0)), 100);
});

test('owner page: languages and country shares come from every visit of the 7 days, not only the 10 biggest pages and countries', async () => {
  const DB = regionsOnly();
  const S = DB.raw.prepare('INSERT INTO site (day, page, ref, country, event, n) VALUES (?, ?, ?, ?, ?, ?)');
  // 12 English pages with 20 visits each from 12 countries, then 5 Turkish and 3 Russian pages with 2 visits each from Turkey
  const en = ['/', '/cs2-region-picker/', '/cs2-ip-ban/', '/change-ip-with-phone/', '/copy-cs2-config/', '/cs2-iphone-hotspot/', '/cs2-mobile-data/', '/cs2-ping-test/', '/cs2-config-file-location/', '/hide-ip-cs2/', '/is-it-safe/', '/steam-account-switcher/'];
  const cc = ['PT', 'BR', 'US', 'DE', 'PL', 'ES', 'GB', 'FR', 'IT', 'NL', 'SE', 'CZ'];
  en.forEach((p, i) => S.run(day(2), p, 'google.com', cc[i], 'view', 20));
  for (const p of ['/tr/', '/tr/cs2-region-picker/', '/tr/cs2-ip-ban/', '/tr/is-it-safe/', '/tr/cs2-ping-test/']) S.run(day(3), p, '', 'TR', 'view', 2);
  for (const p of ['/ru/', '/ru/cs2-region-picker/', '/ru/cs2-ip-ban/']) S.run(day(3), p, '', 'TR', 'view', 2);
  const B = await brief(DB);
  assert.equal(B.kpis.site_visits.d7, 256);
  const H = buildHome({ B, G: null, L: null });
  const L = Object.fromEntries(H.langs.map((x) => [x.name, x.n]));
  assert.deepEqual(L, { 'Inglês': 240, 'Turco': 10, 'Russo': 6 });
  assert.equal(Math.round(100 * H.langs.reduce((a, x) => a + x.share, 0)), 100);
  // Turkey has 16 visits of 256, about 6%, not a share of the first 10 countries only
  const tr = H.countries.find((x) => x.name === countryName('TR'));
  assert.equal(Math.round(100 * (H.countries[0].share)), 8);   // 20 of 256
  assert.ok(!tr || Math.round(100 * tr.share) === 6);
  assert.ok(H.countries.reduce((a, x) => a + x.share, 0) < 1, 'six countries of thirteen are not 100%');
});

test('owner page: no site table means no pages block and no made-up zeros', async () => {
  const B = await brief(regionsOnly());
  B.acquisition.top_site_pages_d7 = null; B.acquisition.top_site_countries_d7 = null;
  const H = buildHome({ B, G: null, L: null });
  assert.equal(H.pages, null); assert.equal(H.langs, null); assert.equal(H.countries, null);
});

test('names: a visit with no referrer is not called "direct", search engines and pages get their names, 1.0.0 reads as 1.0.0', () => {
  assert.equal(hostName(''), NO_REF); assert.equal(hostName('direct'), NO_REF);
  assert.ok(!/Direto/.test(NO_REF));
  assert.equal(hostName('yandex.ru'), 'Yandex'); assert.equal(hostName('m.facebook.com'), 'Facebook'); assert.equal(hostName('google.com.tr'), 'Google');
  assert.equal(hostName('ref:tiktok'), 'Link marcado: tiktok');
  assert.deepEqual(pageName('/ru/is-it-safe/'), { name: 'É seguro?', lang: 'Russo' });
  assert.deepEqual(pageName('/pt-br/'), { name: 'Página inicial', lang: 'Português' });
  assert.equal(countryName('?'), 'Desconhecido');
  assert.equal(relName('v1.0.0'), '1.0.0'); assert.equal(relName('v0.1.0-beta.15'), 'beta.15');
});

test('owner page: every block it had is still drawn, the new ones too, and data texts go through esc()', () => {
  const js = readFileSync(new URL('../dashboard/v5/home.js', import.meta.url), 'utf8');
  const html = readFileSync(new URL('../dashboard/v5/index.html', import.meta.url), 'utf8');
  for (const id of ['hero', 'changes', 'tiles', 'panel', 'arrive', 'journey', 'path', 'pages']) assert.match(html, new RegExp(`id="${id}"`), id);
  for (const f of ['renderHero', 'renderChanges', 'renderBoard', 'renderArrive', 'renderJourney', 'renderPath', 'renderPages']) assert.match(js, new RegExp(`${f}\\(\\)`), f);
  assert.ok(!/\$\{(x|g|l|p)\.(name|lang|label|title|note|text)\}/.test(js), 'a text drawn without esc()');
});
