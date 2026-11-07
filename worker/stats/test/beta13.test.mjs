// Beta.13 (7 nov. 2026): what the owner found on the dashboard after using beta.12 (publish/PASSAGEM-COORDENADOR-2026-11-07.md,
// points 4 to 9). Each fix has its test here, written to fail before the fix.
//   node --test worker/stats/test/beta13.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeD1 } from './d1.mjs';
import ingest, { contract } from '../src/index.js';
import { summary } from '../src/api.js';
import { ASSETS } from '../src/assets.js';
import { snapshotSearch } from '../src/dashboard.js';
import { seed } from './v4.test.mjs';
import { launch, briefOf, GH_OK } from './brief.test.mjs';
import { buildHome } from '../dashboard/v5/model.js';

const ID = (n) => n.toString(16).padStart(32, '0');
const F0 = { version: 'all', country: 'all', mode: 'all', link: 'all' };
const report = (over = {}) => ({ v: 1, id: ID(1), app: '0.1.0-beta.13', os: '10.0.26300.0', mode: 'game', link: 'usb', minutes: 12, dev: false, clicks: {}, counts: {}, errors: [], ...over });
const post = (body) => { const r = new Request('https://stats.example.workers.dev/v1', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }); Object.defineProperty(r, 'cf', { value: { country: 'PT' } }); return r; };
const sum = async (DB, over = {}) => JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: F0, dev: false, now: Date.now(), ...over })));
const read = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');

// ------------------------------------------------------------------ point 9: the heat map in the page, scroll included

test('point 9: the contract has the page grid (24 columns, rows of 40 px, 150 rows), apart from the old window grid', () => {
  assert.deepEqual({ cols: contract.page.cols, rowPx: contract.page.rowPx, rows: contract.page.rows }, { cols: 24, rowPx: 40, rows: 150 });
  assert.deepEqual(contract.grid, { cols: 24, rows: 16 });
});

test('point 9: a beta.13 cell in the page ("s6,30") is kept as it came; the old window cells of beta.12 are kept too, never mixed', async () => {
  const DB = fakeD1();
  const heat = [['accounts', 'c', 's6,30', 2], ['accounts', 'd', 's6,30', 1], ['accounts', 'c', 's23,149', 1], ['accounts', 'c', '6,12', 4]];
  assert.equal((await ingest.fetch(post(report({ heat })), { DB })).status, 204);
  const rows = DB.raw.prepare('SELECT kind, cell, n FROM heat ORDER BY cell').all().map((r) => `${r.kind} ${r.cell} ${r.n}`);
  assert.deepEqual(rows.sort(), ['c 6,12 4', 'c s23,149 1', 'c s6,30 2', 'd s6,30 1']);
  const s = await sum(DB);
  assert.deepEqual(s.heatPage.accounts.c.sort(), [[23, 149, 1], [6, 30, 2]]);
  assert.deepEqual(s.heatPage.accounts.d, [[6, 30, 1]]);
  assert.deepEqual(s.heat.accounts.c, [[6, 12, 4]], 'the old grid has only the old cells');
});

test('point 9: cells outside the page grid or in a made-up shape are dropped', async () => {
  const DB = fakeD1();
  const heat = [['accounts', 'c', 's24,0', 1], ['accounts', 'c', 's0,150', 1], ['accounts', 'c', 's1,5x', 1], ['accounts', 'c', 'S1,5', 1], ['accounts', 'c', 's-1,5', 1], ['accounts', 'c', 's1,2,3', 1]];
  assert.equal((await ingest.fetch(post(report({ heat })), { DB })).status, 204);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM heat').get().n, 0);
});

test('point 9: the version filter applies to the page cells too; rage and dead clicks of both grids reach "Sinais de frustração", each with its grid', async () => {
  const DB = fakeD1();
  await ingest.fetch(post(report({ heat: [['servers', 'r', 's3,40', 1], ['servers', 'd', 's3,40', 2]] })), { DB });
  await ingest.fetch(post(report({ id: ID(2), app: '0.1.0-beta.12', heat: [['servers', 'd', '3,9', 5]] })), { DB });
  const s = await sum(DB);
  const dead = s.stuck.signals.find((x) => x.id === 'dead_clicks');
  assert.equal(dead.n, 7);
  assert.deepEqual(dead.top.map((x) => [x.screen, x.c, x.r, x.n, !!x.page]).sort(), [['servers', 3, 40, 2, true], ['servers', 3, 9, 5, false]]);
  const only13 = await sum(DB, { f: { ...F0, version: '0.1.0-beta.13' } });
  assert.deepEqual(only13.heatPage.servers.d, [[3, 40, 2]]);
  assert.equal(only13.heat.servers, undefined);
});

test('point 9: the dashboard draws the two grids apart and on the whole page capture (no fixed 800 px high map)', () => {
  const app = read('../dashboard/v4/app.js');
  assert.match(app, /heatPage/, 'the page grid is read');
  assert.match(app, /UI\.ROWPX|ROWPX/, 'rows of the page grid in pixels of the capture');
  const UI = new Function(read('../dashboard/ui-map.js') + ';return UI;')();
  assert.equal(UI.ROWPX, contract.page.rowPx);
  assert.equal(UI.PAGEROWS, contract.page.rows);
});

// ------------------------------------------------------------------ points 4, 5 and 6: the logo, the way back to Resumo, the loose line

test('point 4: the dashboard shows the app logo (docs/assets/logo-24.svg, the same file), in the top bar of both pages and as the tab icon', () => {
  // the same drawing, whatever line endings the checkout gave each file (a Windows checkout can have CRLF in one and LF in the other)
  const lf = (s) => s.replaceAll('\r\n', '\n');
  assert.equal(lf(read('../dashboard/logo.svg')), lf(read('../../../docs/assets/logo-24.svg')), 'a copy of the logo, not a new drawing');
  for (const p of ['../dashboard/v5/index.html', '../dashboard/v4/index.html']) {
    const html = read(p);
    assert.match(html, /<a class="brand" href="\/dashboard"><img class="mark" src="\/d\/logo\.svg"/, `${p}: the logo in the brand`);
    assert.match(html, /<link rel="icon" href="\/d\/logo\.svg" type="image\/svg\+xml">/, `${p}: the tab icon`);
  }
  assert.equal(ASSETS['logo.svg']?.type, 'image/svg+xml', 'packed for the Worker');
  assert.doesNotMatch(read('../dashboard/v5/home.css'), /\.mark \{[^}]*linear-gradient/, 'no gradient square in place of the logo');
});

test('point 5: Dados has the same Resumo | Dados tabs as Resumo, at the top and a normal size, not a small link under the side bar', () => {
  const html = read('../dashboard/v4/index.html');
  assert.match(html, /<nav class="tabs" aria-label="Páginas do painel"><a href="\/dashboard">Resumo<\/a><a href="\/data" aria-current="page">Dados<\/a><\/nav>/);
  const app = read('../dashboard/v4/app.js');
  assert.doesNotMatch(app, /class="links"><a href="\/dashboard">/, 'the old small link is gone');
  const css = read('../dashboard/v4/v4.css');
  assert.match(css, /\.tabs a \{[^}]*padding: 5px 14px[^}]*font-size: 13px/, 'a target of a normal size');
});

test('point 6: the line "Unidade: a instalação…" is no longer loose in the side bar; what it said is in "Como se conta"', () => {
  const app = read('../dashboard/v4/app.js');
  assert.doesNotMatch(app, /Unidade: a instalação \(ID fixo/);
  assert.doesNotMatch(app, /Só tu vês este painel: números exatos/);
  const how = /<summary>Como se conta<\/summary><p>([^<]*)/.exec(app)[1];
  for (const s of ['Instalação: um ID de instalação (fixo desde a beta.9)', 'Instalação-dia', 'Dias em UTC, horas de Lisboa']) assert.ok(how.includes(s), s);
});

// ------------------------------------------------------------------ point 7: numbers that agree with each other (the owner's 3.png)

const V4F = () => new Function('window', read('../dashboard/v4/funnel.js') + ';return window.V4F;')({});

test('point 7 (3.png): in the funnel, the exe downloads (GitHub, every origin) are beside the site\'s steps, not a step after "Cliques em Download"', () => {
  const rows = V4F().funnelRows([{ k: 'Visitas ao site', n: 33 }, { k: 'Cliques em Download', n: 0 }, { k: 'Downloads do exe', n: 6, outside: true }]);
  assert.deepEqual(rows.map((r) => [r.k, r.bar, r.ofFirst, r.ofPrev, !!r.outside]), [
    ['Visitas ao site', 100, null, null, false],
    ['Cliques em Download', 0, [0, 33], [0, 33], false],   // "do anterior" said nothing before: now 0 of 33
    ['Downloads do exe', null, null, null, true],         // no bar against the visits, no % of a step it doesn't come from
  ]);
});

test('point 7: a step bigger than the one before is flagged (a funnel can only shrink), never drawn as if it came from it', () => {
  const rows = V4F().funnelRows([{ k: 'a', n: 10 }, { k: 'b', n: 4 }, { k: 'c', n: 6 }]);
  assert.deepEqual(rows.map((r) => !!r.bigger), [false, false, true]);
  const na = V4F().funnelRows([{ k: 'Impressões', n: null }, { k: 'Visitas', n: 20 }, { k: 'Cliques', n: 5 }]);
  assert.deepEqual(na.map((r) => [r.bar, r.ofFirst]), [[null, null], [100, null], [25, [5, 20]]], 'a step with no source is not the first');
});

test('point 7: the brief says how many of the exe downloads were the app updating itself (upd_done), in the same 7 complete days', async () => {
  const NOW = Date.parse('2026-11-07T03:00:00Z');
  const DB = seed([[1, 1, 'usb', { upd_done: 1 }], [3, 2, 'usb', { upd_done: 1 }], [3, 3, 'usb', { upd_done: 1 }], [0, 4, 'usb', { upd_done: 1 }], [9, 5, 'usb', { upd_done: 1 }], [5, 6, 'usb', { upd_done: 1 }]], { now: NOW });
  // daily GitHub pictures for every day but the one dated 4 days ago (so the downloads of 5 days ago are not known)
  const gh = DB.raw.prepare("INSERT INTO gh_daily (day, kind, name, n) VALUES (?, 'asset', 'v0.1.0-beta.12/CS2IPChanger.exe', ?)");
  for (let d = 12; d >= 0; d--) if (d !== 4) gh.run(new Date(NOW - d * 864e5).toISOString().slice(0, 10), 100 - d);
  const B = await briefOf(DB, { now: NOW });
  assert.equal(B.kpis.exe_downloads.app_updates_d7, 3, 'yesterday and 3 days ago; not today, not 9 days ago, not on a day with no picture');
});

test('point 7: Resumo names its window (the 7 complete days) and the journey says when it is a different one, never "últimos 7 dias" for both', async () => {
  const B = JSON.parse(JSON.stringify(await briefOf(launch())));
  const H = buildHome({ B, G: GH_OK });
  const [a, b] = B.window.d7;
  assert.ok(H.cap.includes(`de ${+a.slice(8)} `) && H.cap.includes(`a ${+b.slice(8)} `), H.cap);
  B.funnel.activation.steps[0].n = 3;   // running week: the last 7 days with today
  const R = buildHome({ B, G: GH_OK });
  assert.doesNotMatch(R.journey.text, /últimos 7 dias/);
  assert.match(R.journey.text, /até hoje/);
});

test('point 7: "Como chegam" keeps the site (visits, clicks) apart from the downloads and new installs, and says the downloads include the app\'s updates', async () => {
  const B = JSON.parse(JSON.stringify(await briefOf(launch())));
  B.kpis.exe_downloads.app_updates_d7 = 2;
  const H = buildHome({ B, G: GH_OK });
  assert.deepEqual(H.arrive.map((g) => g.rows.map((r) => r.label)), [['Visitas ao site', 'Cliques em Download'], ['Downloads da app', 'Instalações novas']]);
  assert.match(H.arrive[1].rows[0].note, /2 .*atualiza/);
});

test('point 7: the same heat cell sent by two versions is one dot with the sum (it was two dots on top of each other)', async () => {
  const DB = fakeD1();
  await ingest.fetch(post(report({ heat: [['servers', 'c', 's3,4', 2]] })), { DB });
  await ingest.fetch(post(report({ id: ID(2), app: '0.1.0-beta.14', heat: [['servers', 'c', 's3,4', 5]] })), { DB });
  const s = await sum(DB);
  assert.deepEqual(s.heatPage.servers.c, [[3, 4, 7]]);
});

// ------------------------------------------------------------------ point 8: what said "não medido" is measured

test('point 8: with GSC_KEY and GSC_SITE the daily cron keeps Google\'s search impressions and clicks per day, and the brief fills the two funnel steps', async () => {
  const NOW = Date.parse('2026-11-04T15:30:00Z');
  const day = (n) => new Date(NOW - n * 864e5).toISOString().slice(0, 10);
  const { privateKey, publicKey } = await crypto.subtle.generateKey({ name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['sign', 'verify']);
  const b64 = Buffer.from(await crypto.subtle.exportKey('pkcs8', privateKey)).toString('base64');
  // a key made just now for the test, in the PEM shape of a Google key file (the armour lines put together, so no key is in the file)
  const armour = (w) => `-----${w} ${['PRIVATE', 'KEY'].join(' ')}-----`;
  const pem = `${armour('BEGIN')}\n${b64.match(/.{1,64}/g).join('\n')}\n${armour('END')}\n`;
  const env = { GSC_KEY: JSON.stringify({ client_email: 'stats@example.iam.gserviceaccount.com', private_key: pem }), GSC_SITE: 'https://sepd0x.github.io/cs2-ip-changer/' };
  const asked = [];
  const fetchFn = async (url, init) => {
    asked.push(url);
    if (url === 'https://oauth2.googleapis.com/token') {
      const [h, p, s] = new URLSearchParams(init.body).get('assertion').split('.');
      assert.ok(await crypto.subtle.verify('RSASSA-PKCS1-v1_5', publicKey, Buffer.from(s, 'base64url'), new TextEncoder().encode(`${h}.${p}`)), 'a signed JWT');
      const claims = JSON.parse(Buffer.from(p, 'base64url').toString());
      assert.equal(claims.scope, 'https://www.googleapis.com/auth/webmasters.readonly', 'read only');
      assert.equal(claims.iss, 'stats@example.iam.gserviceaccount.com');
      return new Response(JSON.stringify({ access_token: 'tok', expires_in: 3600 }));
    }
    assert.equal(url, `https://www.googleapis.com/webmasters/v3/sites/${encodeURIComponent(env.GSC_SITE)}/searchAnalytics/query`);
    assert.equal(init.headers.authorization, 'Bearer tok');
    assert.deepEqual(JSON.parse(init.body).dimensions, ['date'], 'only daily totals of the site, no query, page or country');
    return new Response(JSON.stringify({ rows: [{ keys: [day(3)], impressions: 40, clicks: 3, ctr: 0.07, position: 9 }, { keys: [day(4)], impressions: 60, clicks: 2 }] }));
  };
  const DB = launch();
  assert.equal(await snapshotSearch(DB, env, fetchFn, NOW), 2);
  assert.deepEqual(DB.raw.prepare("SELECT day, name, n FROM gh_daily WHERE kind = 'search' ORDER BY day, name").all().map((r) => `${r.day} ${r.name} ${r.n}`),
    [`${day(4)} clicks 2`, `${day(4)} impressions 60`, `${day(3)} clicks 3`, `${day(3)} impressions 40`]);
  const B = await briefOf(DB, { now: NOW });
  const st = Object.fromEntries(B.funnel.acquisition.steps.map((s) => [s.id, s]));
  assert.equal(st.search_impressions.n, 100);
  assert.equal(st.search_clicks.n, 5);
  assert.equal(st.search_impressions.days_covered, 2, 'Google gives its numbers 2 or 3 days late: how many of the 7 days it has');
  // without the two secrets nothing is asked and the steps still say not measured
  const none = [];
  assert.equal(await snapshotSearch(fakeD1(), {}, async (u) => { none.push(u); }, NOW), 0);
  assert.deepEqual(none, []);
  const B0 = await briefOf(launch(), { now: NOW });
  assert.equal(B0.funnel.acquisition.steps[0].reason, 'not_measured');
});

test('point 7: the same old window cell ("3,4") from beta.11 and beta.12 is one dot with the sum (the cells in the D1 today are all old ones)', async () => {
  const DB = fakeD1();
  await ingest.fetch(post(report({ app: '0.1.0-beta.11', heat: [['servers', 'c', '3,4', 2]] })), { DB });
  await ingest.fetch(post(report({ id: ID(2), app: '0.1.0-beta.12', heat: [['servers', 'c', '3,4', 5]] })), { DB });
  const s = await sum(DB);
  assert.deepEqual(s.heat.servers.c, [[3, 4, 7]]);
});

test('point 7 (verification): each window is said with its dates: Hoje\'s funnel and the rules are the 7 complete days, Funil says how it differs', () => {
  const app = read('../dashboard/v4/app.js');
  assert.doesNotMatch(app, /do \/api\/brief, últimos 7 dias, sem filtros/);
  assert.doesNotMatch(app, /dispararam · últimos 7 dias, sem filtros/);
  assert.doesNotMatch(app, /'aquisição: 7 dias completos · /);
  assert.match(app, /const d7Txt = /);
  assert.ok((app.match(/d7Txt\(B\)/g) || []).length >= 3, 'the rules band, the rules list and Hoje\'s funnel');
  assert.match(app, /No Hoje e no Resumo o funil usa os \$\{esc\(d7Txt\(D\.B\)\)\} \(hoje fica de fora\); aqui é o período escolhido/);
});

test('point 8: "1.ª entrada num servidor" is measured: of the new installs Protected without a match, how many joined a server (join)', async () => {
  const NOW = Date.parse('2026-11-04T15:30:00Z');
  const DB = seed([[2, 1, 'usb', { first_start: 1, tp_10_30: 1, join: 2 }], [2, 2, 'usb', { first_start: 1, tp_10_30: 1 }], [2, 3, 'usb', { first_start: 1, tp_10_30: 1, join: 1, m_comm: 1 }]], { now: NOW });
  const s = await sum(DB, { now: NOW });
  assert.deepEqual({ n: s.stuck.protected_no_match.n, joined: s.stuck.protected_no_match.joined }, { n: 2, joined: 1 });
  const app = read('../dashboard/v4/app.js');
  assert.doesNotMatch(app, /"1\.ª entrada num servidor" não é recolhida/);
  assert.match(app, /PM\.joined/);
});

test('point 8: the data page reads Google\'s numbers when they exist instead of always drawing "não medido"', () => {
  const app = read('../dashboard/v4/app.js');
  assert.doesNotMatch(app, /k: 'Impressões no Google', n: null/);
  assert.match(app, /history\.search/);
});
