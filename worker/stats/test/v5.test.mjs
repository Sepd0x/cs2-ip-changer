// Tests of the owner's page (dashboard v5, 5 Nov. 2026, publish/auditoria-2026-11-04/dashboard-v5/): the numbers and sentences
// of worker/stats/dashboard/v5/model.js, made from the same /api/brief the agent reads. Run with the rest:
// node --test test/worker.test.mjs (it imports this file)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeD1 } from './d1.mjs';
import { launch, briefOf, GH_OK } from './brief.test.mjs';
import { buildHome, delta, weeks, hostName, num, span } from '../dashboard/v5/model.js';
import { rules } from '../src/brief.js';

const clone = (x) => JSON.parse(JSON.stringify(x));

test('v5 model: the change against the week before says it simply, and says nothing when there is nothing to say', () => {
  assert.equal(delta(0, 0), null, '0 and 0: no "igual"');
  assert.equal(delta(5, null), null);
  assert.deepEqual(delta(5, 0), { dir: 'new', text: '+5' }, 'nothing before: the number, not "novo" (the 10-second test read it as "new what?")');
  assert.deepEqual(delta(4, 2), { dir: 'up', text: '+2' }, 'under 20 before: a number, never +100%');
  assert.deepEqual(delta(1, 3), { dir: 'down', text: '−2' });
  assert.deepEqual(delta(94, 17), { dir: 'up', text: '+77' }, 'the brief says low_n under 20: absolute numbers only');
  assert.deepEqual(delta(50, 20), { dir: 'up', text: '2,5× mais' });
  assert.deepEqual(delta(80, 100), { dir: 'down', text: '−20%' });
  assert.deepEqual(delta(115, 100), { dir: 'up', text: '+15%' });
  assert.deepEqual(delta(10, 10), { dir: 'flat', text: 'igual' });
});

test('v5 model: the 7 complete days and the 7 before are the brief\'s windows (today left out)', () => {
  const l = [...Array(30)].map((_, i) => i);
  assert.deepEqual(weeks(l), { d7: 22 + 23 + 24 + 25 + 26 + 27 + 28, prev7: 15 + 16 + 17 + 18 + 19 + 20 + 21 });
  assert.deepEqual(weeks(null), { d7: null, prev7: null });
  assert.deepEqual(weeks([...Array(30)].fill(null)), { d7: null, prev7: null });
});

test('v5 model: the six numbers are the brief\'s numbers, the chart has only complete days, at most 3 sentences', async () => {
  const B = await briefOf(launch());
  const H = buildHome({ B, G: GH_OK, L: null });
  assert.deepEqual(H.tiles.map((t) => t.id), ['active', 'new', 'downloads', 'matches', 'stars', 'visits']);
  const T = Object.fromEntries(H.tiles.map((t) => [t.id, t]));
  assert.equal(T.active.value, B.kpis.active_installs.d7);
  assert.equal(T.active.prev, B.kpis.active_installs.prev7);
  assert.equal(T.new.value, B.kpis.new_installs.d7);
  assert.equal(T.downloads.value, B.kpis.exe_downloads.d7);
  assert.equal(T.visits.value, B.kpis.site_visits.d7);
  assert.equal(T.stars.value, B.kpis.stars.total);
  assert.equal(T.matches.value, weeks(B.series.matches).d7);
  for (const t of H.tiles) {
    assert.equal(t.series.length, B.series.days.length - 1, `${t.id}: today (still going) is not on the chart`);
    assert.ok(!t.series.some((p) => p.date === B.window.today), t.id);
  }
  assert.ok(H.changes.length <= 3);
  assert.equal(H.empty, false);
  assert.match(H.headline.text, new RegExp(`\\b${num(T.active.value)}\\b`));
});

test('v5 model: what asks for action comes first (new errors, then a release with no downloads), and links to the data page', async () => {
  const B = clone(await briefOf(launch()));
  B.anomalies = [
    { id: 'metric_spike:site_visits@2026-11-01', metric: 'site_visits', observed: 300, expected: 20, text_pt: '' },
    { id: 'release_no_downloads:v0.1.0-beta.10', text_pt: '' },
    { id: 'new_error:X@Adb.Run', text_pt: 'Erro novo X em Adb.Run: 5 vezes em 3 instalações, 2 no 1.º dia.' },
  ];
  const H = buildHome({ B, G: GH_OK });
  assert.equal(H.changes[0].tone, 'bad');
  assert.match(H.changes[0].text, /erro novo.*pela primeira vez/i);
  assert.equal(H.changes[0].link, '/data#qualidade');
  assert.match(H.changes[1].text, /beta\.10 ainda não tem downloads/);
  assert.ok(!H.changes.some((c) => /Adb\.Run|Exception/.test(c.text)), 'no code names on the owner\'s page');
});

test('v5 model: when the data stops, nothing says "0 now": a banner, no live number, no "today so far"', async () => {
  const B = clone(await briefOf(launch()));
  const f = B.health.checks.find((c) => c.id === 'freshness');
  Object.assign(f, { status: 'bad', value: 16 * 60 });
  const H = buildHome({ B, G: GH_OK, L: { now: 0, inGame: 0, protectedN: 0 } });
  assert.equal(H.status.tone, 'bad');
  assert.match(H.status.text, /há 16 horas/);
  assert.equal(H.live.stale, true);
  assert.equal(H.live.staleText, 'sem dados há 16 horas');
  for (const t of H.tiles) assert.equal(t.today, null, t.id);
  assert.equal(span(40), '40 min');
  assert.equal(span(3 * 1440), '3 dias');
});

test('v5 model: an empty database is one welcome, with no sentences, no journey and no fake zeros', async () => {
  const B = await briefOf(fakeD1(), { G: null, L: null });
  const H = buildHome({ B, G: null, L: null });
  assert.equal(H.empty, true);
  assert.equal(H.status, null, 'no "data stopped" banner when there never was data');
  assert.deepEqual(H.changes, []);
  assert.equal(H.journey, null);
  assert.match(H.headline.text, /primeiros utilizadores/);
  const T = Object.fromEntries(H.tiles.map((t) => [t.id, t]));
  assert.equal(T.downloads.value, null, 'no GitHub: no number, not 0');
  assert.equal(T.stars.value, null);
});

test('v5 model: the journey uses the closed week when it has 5 or more, the running one otherwise; the live peak is never under now', async () => {
  const B = clone(await briefOf(launch()));
  const H = buildHome({ B, G: GH_OK, L: { now: 9, inGame: 2, protectedN: 7 } });
  assert.equal(H.journey.base, B.funnel.activation.steps[0].n);
  assert.match(H.journey.text, /instalaram a app entre .* e .*. Na primeira semana de uso:/);
  assert.deepEqual(H.journey.steps.map((s) => s.label), ['Abriram a app', 'Ligaram o telemóvel', 'Ligaram a proteção', 'Jogaram uma partida']);
  assert.ok(H.live.peakToday >= 9);
  B.funnel.activation.steps[0].n = 3;
  const R = buildHome({ B, G: GH_OK });
  assert.equal(R.journey.base, B.funnel.activation.running_steps[0].n);
  assert.match(R.journey.text, /últimos 7 dias/);
});

test('v5 model: where visits come from is a share of the visits with an origin, with the same site merged', async () => {
  const B = clone(await briefOf(launch()));
  B.acquisition.top_site_refs_d7 = [{ host: 'youtube.com', n: 30 }, { host: 'www.youtube.com', n: 10 }, { host: 'direct', n: 40 }, { host: 'example.org', n: 20 }];
  const H = buildHome({ B, G: GH_OK });
  assert.deepEqual(H.refs.map((r) => [r.name, r.n]), [['YouTube', 40], ['Direto (sem link)', 40], ['example.org', 20]]);
  assert.equal(Math.round(100 * H.refs.reduce((a, r) => a + r.share, 0)), 100);
  assert.equal(hostName('www.reddit.com'), 'Reddit');
});

test('v5 page: every text from the data goes through esc() before it is drawn, and nothing loads from outside', () => {
  const js = readFileSync(new URL('../dashboard/v5/home.js', import.meta.url), 'utf8');
  const html = readFileSync(new URL('../dashboard/v5/index.html', import.meta.url), 'utf8');
  assert.ok(!/\$\{(c|t|x|s|j|m|v|H\.headline|H\.status|s)\.(text|label|name|sub|what|staleText)\}/.test(js), 'a text drawn without esc()');
  assert.ok(!/https?:\/\//.test(js + html), 'nothing from outside (the CSP only allows self)');
  assert.ok(!/<script(?![^>]*type="module" src="\/d\/v5\/home\.js")/.test(html), 'only the one module script');
});

test('v5: "no downloads" for a release only when GitHub says 0 in both sources (the public answer said 0, the daily pictures 2)', async () => {
  const B = clone(await briefOf(launch()));
  B.anomalies = [{ id: 'release_no_downloads:v0.1.0-beta.10', text_pt: '' }];
  const said = (byRelease) => buildHome({ B, G: { ...GH_OK, history: { days: [], byRelease } } }).changes.some((c) => /ainda não tem downloads/.test(c.text));
  assert.equal(said({}), true);
  assert.equal(said({ 'v0.1.0-beta.10': [{ day: '2026-11-04', n: 0 }] }), true);
  assert.equal(said({ 'v0.1.0-beta.10': [{ day: '2026-11-04', n: 2 }] }), false);
  const R = rules(clone(await briefOf(launch())), { now: Date.parse('2026-11-04T15:30:00Z'), releases: [{ tag: 'v0.1.0-beta.11', date: '2026-11-01', exe: 0 }], byRelease: { 'v0.1.0-beta.11': [{ day: '2026-11-02', n: 3 }] } });
  assert.ok(!R.anomalies.some((a) => a.id.startsWith('release_no_downloads')), 'the brief rule too');
});
