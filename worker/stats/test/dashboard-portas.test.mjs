// Nov. 2026 (the owner's 24 Nov. review): the funnel and the activation only counted the phone, but most players only use the Regions
// page. Two doors now (phone, regions), the first start's question is shown, "played" no longer contradicts "matches" and a link
// of none is information, not a warning. Run with the rest: node --test "worker/stats/test/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { counterOk } from '../src/index.js';
import { summary } from '../src/api.js';
import { seed } from './v4.test.mjs';
import { briefOf } from './brief.test.mjs';
import { buildHome } from '../dashboard/v5/model.js';
import { rules } from '../src/brief.js';

const F0 = { version: 'all', country: 'all', mode: 'all', link: 'all' };
const NOW = Date.parse('2026-11-20T15:30:00Z');
const sum7 = async (DB, period = 7) => JSON.parse(JSON.stringify(await summary(DB, { period, f: F0, dev: false, now: NOW })));
const clone = (x) => JSON.parse(JSON.stringify(x));
const brief = async (DB) => clone(await briefOf(DB, { G: null, L: null, now: NOW }));

test('doors: the Worker takes the first start counters', () => {
  for (const n of ['tour_door_shown', 'tour_door_regions', 'tour_door_phone', 'tour_door_tour', 'tour_door_later']) assert.ok(counterOk(n), n);
});

// Four new installs today: regions + a match, phone with Protected and a match, regions only, nothing
const DB4 = () => seed([
  [0, 1, 'none', { first_start: 1, regions_blocked: 2, m_valve: 1, tour_door_shown: 1, tour_door_regions: 1 }],
  [0, 2, 'usb', { first_start: 1, tp_1: 1, min_prot: 5, m_comm: 1, ip_changed: 1, tour_door_shown: 1, tour_door_phone: 1 }],
  [0, 3, 'none', { first_start: 1, regions_blocked: 1, tour_door_shown: 1, tour_door_tour: 1 }],
  [0, 4, 'none', { first_start: 1, tour_door_shown: 1, tour_door_later: 1 }],
], { now: NOW });

test('doors: activation counts the regions door and the phone door, each new install once', async () => {
  const A = (await sum7(DB4())).activation;
  assert.equal(A.n, 4);
  assert.deepEqual(A.regions, { n: 2, match: 1 });
  assert.equal(A.activated, 3, 'regions (2) + phone with Protected (1)');
  assert.equal(A.played, 2, 'people who played a match, by any door');
  assert.equal(A.steps[2].n, 1, 'the phone path keeps its own steps');
});

test('doors: someone who picked regions is not stuck before the phone', async () => {
  const S = (await sum7(DB4())).stuck;
  assert.deepEqual(S.buckets.map((b) => b.n), [1, 0, 1, 2], 'nothing; -; regions without a match; played (regions + phone)');
  assert.equal(S.buckets.reduce((a, b) => a + b.n, 0), S.n);
});

test('doors: the cohorts carry the regions door, those that played and those activated', async () => {
  const c = (await sum7(DB4())).activation.cohorts.at(-1);
  assert.equal(c.regions, 2); assert.equal(c.regions_match, 1); assert.equal(c.played, 2); assert.equal(c.activated, 3);
});

test('doors: how many saw the first start question and the door each one picked', async () => {
  assert.deepEqual((await sum7(DB4())).stuck.doors, { shown: 4, regions: 1, phone: 1, tour: 1, later: 1 });
});

test('doors: before any install sends the question it says nothing (null), never 0', async () => {
  const DB = seed([[0, 1, 'none', { first_start: 1 }]], { now: NOW });
  assert.equal((await sum7(DB)).stuck.doors, null);
});

test('doors: regions picked after day 0 still count within the first 7 days', async () => {
  const DB = seed([[3, 1, 'none', { first_start: 1 }], [1, 1, 'none', { regions_blocked: 1 }]], { now: NOW });
  const A = (await sum7(DB)).activation;
  assert.equal(A.regions.n, 1); assert.equal(A.activated, 1);
});

// A closed cohort (first day 9 days ago, 5 installs: the owner's page needs 5 to use the closed week)
const closed = () => seed([
  [9, 1, 'none', { first_start: 1, regions_blocked: 2, m_valve: 1 }],
  [9, 2, 'usb', { first_start: 1, tp_1: 1, min_prot: 5, m_comm: 1 }],
  [9, 3, 'none', { first_start: 1 }],
  [9, 4, 'none', { first_start: 1, regions_blocked: 1 }],
  [9, 5, 'none', { first_start: 1 }],
], { now: NOW });

test('doors: the activation KPI counts either door, the funnel shows both', async () => {
  const b = await brief(closed());
  assert.equal(b.kpis.activation_7d.den, 5);
  assert.equal(b.kpis.activation_7d.num, 3, 'before: 1 (phone only)');
  assert.equal(b.funnel.activation.regions.n, 2);
  assert.equal(b.funnel.activation.regions.match, 1);
  assert.equal(b.funnel.activation.played.n, 2);
  assert.equal(b.funnel.activation.activated.n, 3);
});

test('doors: the brief says how many saw the question, and measured false when nobody sent it', async () => {
  const b0 = await brief(closed());
  assert.equal(b0.funnel.doors.measured, false); assert.equal(b0.funnel.doors.shown, 0);
  const b1 = await brief(DB4());
  assert.equal(b1.funnel.doors.measured, true);
  assert.deepEqual([b1.funnel.doors.shown, b1.funnel.doors.regions, b1.funnel.doors.phone, b1.funnel.doors.tour, b1.funnel.doors.later], [4, 1, 1, 1, 1]);
});

test('owner page: the journey shows both doors and "played" counts people, not matches', async () => {
  const H = buildHome({ B: await brief(closed()), G: null, L: null });
  const by = Object.fromEntries(H.journey.steps.map((s) => [s.id, s]));
  assert.equal(by.regions.n, 2); assert.equal(by.played.n, 2);
  assert.equal(by.played.label, 'Pessoas que jogaram uma partida');
  const m = H.tiles.find((t) => t.id === 'matches');
  assert.match(m.what, /no total/, 'the card counts matches and says so');
  assert.notEqual(m.label, by.played.label);
});

test('owner page: the first start question is a sentence when installs sent it, nothing otherwise', async () => {
  const H0 = buildHome({ B: await brief(closed()), G: null, L: null });
  assert.equal(H0.journey.doors, null);
  // Four installs sent the question 7 to 13 days ago
  const DB = seed([
    [9, 1, 'none', { first_start: 1, regions_blocked: 1, tour_door_shown: 1, tour_door_regions: 1 }],
    [9, 2, 'none', { first_start: 1, tour_door_shown: 1, tour_door_phone: 1 }],
    [9, 3, 'none', { first_start: 1, tour_door_shown: 1, tour_door_later: 1 }],
    [9, 4, 'none', { first_start: 1, tour_door_shown: 1, tour_door_tour: 1 }],
    [9, 5, 'none', { first_start: 1, tour_door_shown: 1, tour_door_regions: 1 }],
  ], { now: NOW });
  const H1 = buildHome({ B: await brief(DB), G: null, L: null });
  assert.match(H1.journey.doors, /5 pessoas viram/);
  assert.match(H1.journey.doors, /2 escolheram as regiões, 1 o telemóvel, 1 a visita e 1 ficaram para depois/);
});

test('link none: a high share is information (severity info, health ok), not a warning', async () => {
  const b = await brief(closed());
  const h = b.health.checks.find((c) => c.id === 'link_none_share_7d');
  assert.equal(h.status, 'ok');
  Object.assign(h, { value: 0.96, num: 24, den: 25 });
  const out = rules(b, { now: NOW }).anomalies.filter((a) => a.id === 'link_none');
  assert.equal(out.length, 1);
  assert.equal(out[0].severity, 'info');
  assert.match(out[0].text_pt, /normal para quem só usa as regiões/);
});

test('the dashboards name the doors and use the counts', () => {
  const app = readFileSync(new URL('../dashboard/v4/app.js', import.meta.url), 'utf8');
  assert.match(app, /function doorsNote\(/);
  assert.match(app, /id="e-doors"/);
  assert.match(app, /Nem telemóvel nem regiões/);
  const home = readFileSync(new URL('../dashboard/v5/home.js', import.meta.url), 'utf8');
  assert.match(home, /j\.doors/);
});
