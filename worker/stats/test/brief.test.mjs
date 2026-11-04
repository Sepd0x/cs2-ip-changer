// Tests of /api/brief (spec dashboard-v4 7.6): shape, privacy, definitions, nulls and every rule on both sides of its minimum n.
// Run with the rest: node --test test/worker.test.mjs (it imports this file)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeD1 } from './d1.mjs';
import { seed } from './v4.test.mjs';
import { summary, live } from '../src/api.js';
import { buildBrief, renderMd, rules, DEFINITIONS, wilson } from '../src/brief.js';
import { githubHistory, tableNames, readBrief } from '../src/dashboard.js';

const F0 = { version: 'all', country: 'all', mode: 'all', link: 'all' };
const ID = (n) => n.toString(16).padStart(32, '0');
const NOW = Date.parse('2026-11-04T15:30:00Z');
const ago = (n, now = NOW) => new Date(now - n * 864e5).toISOString().slice(0, 10);

export const GH_OK = { repo: 'x/y', problems: [], source: 'token', traffic: null, stars: 14, forks: 1, watchers: 2, issues: 0, views: null, clones: null,
  referrers: [{ name: 'youtube.com', n: 30, u: 20 }], paths: [], releases: [{ tag: 'v0.1.0-beta.10', date: '2026-11-03', draft: false, downloads: 9, exe: 8 }] };
export const GH_401 = { ...GH_OK, problems: ['The GITHUB_TOKEN was refused (401): stars and downloads come from the public API.'], source: 'public (the token failed)', traffic: 'the token has no access to the traffic', referrers: null };

/** A launch-like fortnight: perDay new installs a day, most with the phone, some Protected, some matches; a site and daily pictures. */
export function launch({ perDay = 4, now = NOW } = {}) {
  const rows = [];
  let id = 1;
  for (let d = 13; d >= 0; d--)
    for (let k = 0; k < perDay; k++, id++) {
      const phone = k % 4 !== 0, prot = phone && k % 4 !== 1, match = prot && k % 2 === 0;
      rows.push([d, id, phone ? 'usb' : 'none', { first_start: 1, accounts: 1, app_start: 1, ...(prot ? { tp_10_30: 1, ip_changed: 1 } : {}), ...(match ? { m_valve: 2 } : {}), ...(phone ? {} : { phone_setup_nousbdebugging: 1 }) }]);
      if (d > 1 && k % 3 === 0) rows.push([d - 1, id, 'usb', { accounts: 1, app_start: 1 }]);
    }
  const DB = seed(rows, { now });
  const site = DB.raw.prepare("INSERT INTO site (day, page, ref, country, event, n) VALUES (?, '/', ?, 'PT', ?, ?)");
  for (let d = 0; d < 30; d++) { site.run(ago(d, now), 'youtube.com', 'view', 20 + d); site.run(ago(d, now), '', 'download', 5); }
  const gh = DB.raw.prepare('INSERT INTO gh_daily (day, kind, name, n) VALUES (?, ?, ?, ?)');
  for (let d = 20; d >= 0; d--) { gh.run(ago(d, now), 'repo', 'stars', 20 - d); gh.run(ago(d, now), 'asset', 'v0.1.0-beta.10/CS2IPChanger.exe', 400 - d * 10); }
  return DB;
}
export async function briefOf(DB, { G = GH_OK, now = NOW, L: Lin } = {}) {
  const S = await summary(DB, { period: 30, f: F0, dev: false, now });
  const L = Lin === undefined ? await live(DB, { dev: false, now }) : Lin;
  return buildBrief({ S, L, G, GH: await githubHistory(DB, now), tables: await tableNames(DB), now, excluded: 2 });
}
/** Every key path of a JSON value; arrays as [] with their items' keys merged; the definitions only by their names. */
export function keyPaths(v, p = '', out = new Set()) {
  if (Array.isArray(v)) { out.add(p + '[]'); for (const x of v) keyPaths(x, p + '[]', out); }
  else if (v && typeof v === 'object') for (const [k, x] of Object.entries(v)) { out.add(`${p}.${k}`); if (p !== '.definitions') keyPaths(x, `${p}.${k}`, out); }
  return out;
}
const OPTIONAL = /\.(reason|[a-z0-9_]+_reason|github_refs_source|ci95)$/;

test('v4 brief: the same frozen keys (brief-keys.json) on a launch-like, a small and an empty database; nothing outside them', async () => {
  const small = seed([[1, 1, 'none', { first_start: 1, accounts: 1 }, '0.1.0-beta.9'], [0, 2, 'usb', { accounts: 1, crash: 1, app_start: 3 }]]);
  const sets = [await briefOf(launch()), await briefOf(small, { G: GH_401 }), await briefOf(fakeD1(), { G: null, L: null })].map((b) => keyPaths(JSON.parse(JSON.stringify(b))));
  const frozen = new Set(JSON.parse(readFileSync(new URL('./brief-keys.json', import.meta.url), 'utf8')));
  for (const s of sets) for (const p of s) assert.ok(frozen.has(p), `key outside brief.v1: ${p}`);
  // Every key that is not inside an array, not optional and not under an object that may be null is on every database
  const fixed = [...frozen].filter((p) => !p.includes('[]') && !OPTIONAL.test(p) && !/^\.(kpis\.live_now|kpis\.[a-z_]+\.prev|acquisition\.github_views_d7|funnel\.activation\.biggest_drop|kpis\.[a-z_]+\.rate_d7)\./.test(p));
  for (const s of sets) for (const p of fixed) assert.ok(s.has(p), `missing ${p}`);
});

test('v4 brief: no install ID in the JSON or the Markdown, and the Markdown is made from the JSON alone', async () => {
  const b = await briefOf(launch());
  const md = renderMd(b);
  assert.ok(!/[0-9a-f]{32}/.test(JSON.stringify(b)) && !/[0-9a-f]{32}/.test(md));
  assert.equal(md, renderMd(JSON.parse(JSON.stringify(b))));
  for (const h of ['# CS2 IP Changer · brief ', '## Anomalies', '## Stuck', '## Suggested next', '## Data caveats']) assert.ok(md.includes(h), h);
  assert.ok(md.split('\n').length <= 50, `about 30 lines: ${md.split('\n').length}`);
});

test('v4 brief: every KPI, funnel step, stuck bucket and health check has a definition', async () => {
  const b = await briefOf(launch());
  for (const k of Object.keys(b.kpis)) assert.ok(DEFINITIONS[k], `definition for ${k}`);
  for (const s of [...b.funnel.acquisition.steps, ...b.funnel.activation.steps, ...b.stuck.buckets]) assert.ok(DEFINITIONS[s.id], `definition for ${s.id}`);
  const H = { freshness: 'real_report', any_pc_freshness: 'real_report', live_freshness: 'live_now', github_snapshot: 'exe_downloads', tables: 'day', truncated: 'day', excluded_ids: 'install' };
  for (const c of b.health.checks) assert.ok(DEFINITIONS[c.id] || DEFINITIONS[H[c.id]] || (c.id.startsWith('budget_') && DEFINITIONS.budget), `definition for ${c.id}`);
});

test('v4 brief: its numbers are summary()\'s (7 complete days, the closed cohort), and the rates carry num and den', async () => {
  const DB = launch();
  const b = await briefOf(DB);
  const S = await summary(DB, { period: 30, f: F0, dev: false, now: NOW });
  assert.deepEqual(b.window.d7, [ago(7), ago(1)], '7 complete days, today out');
  assert.equal(b.kpis.new_installs.d7, S.days.slice(-8, -1).reduce((a, d) => a + d.fresh, 0));
  assert.equal(b.kpis.new_installs.d7, 28);
  // the closed cohort (first day 7 to 13 days ago) from the cohorts per day
  const closed = S.activation.cohorts.filter((c) => c.day >= ago(13) && c.day <= ago(7));
  assert.deepEqual(b.funnel.activation.steps.map((x) => x.n), [0, 1, 2, 3].map((k) => closed.reduce((a, c) => a + c.steps[k], 0)));
  assert.deepEqual(b.window.closed_cohort, [ago(13), ago(7)]);
  assert.equal(b.kpis.activation_7d.den, 28);
  assert.equal(b.kpis.activation_7d.running.den, 28, 'the last 7 days, still going, apart');
  assert.equal(b.stuck.buckets.reduce((a, x) => a + x.n, 0), b.stuck.n);
  assert.equal(b.kpis.active_installs.d7, S.windows.c7.active);
  const r = b.kpis.site_download_clicks.rate_d7;
  assert.ok(r.value > 0 && r.value <= 1 && r.num === 35 && r.den > r.num);
  assert.equal(b.kpis.exe_downloads.yesterday, 10);
  assert.equal(b.kpis.exe_downloads.d7, 70, 'the pictures of the 6 days after the first: 7 complete days of downloads');
  assert.equal(b.series.exe_downloads.at(-1), null, 'today\'s downloads are known tomorrow');
  assert.equal(b.series.exe_downloads.at(-2), 10, 'the picture of today is yesterday\'s downloads');
  assert.equal(b.kpis.stars.d7_added, 7);
  assert.equal(b.scope.excluded_ids, 2);
  assert.deepEqual([b.kpis.yesterday_complete.date, b.kpis.yesterday_complete.new_installs, b.kpis.yesterday_complete.exe_downloads], [ago(1), 4, 10]);
});

test('v4 brief: an empty database gives null with a reason where there is no source, and 0 where the source said 0', async () => {
  const DB = fakeD1();
  DB.raw.exec('DROP TABLE site');
  const b = await briefOf(DB, { G: null, L: null });
  assert.equal(b.health.status, 'bad');
  assert.deepEqual([b.health.checks[0].id, b.health.checks[0].value, b.health.checks[0].reason], ['freshness', null, 'no_source_rows']);
  assert.deepEqual(b.health.checks.find((c) => c.id === 'tables').missing, ['site']);
  assert.deepEqual([b.kpis.site_visits.d7, b.kpis.site_visits.reason], [null, 'table_missing']);
  assert.deepEqual([b.kpis.exe_downloads.d7, b.kpis.exe_downloads.yesterday_reason], [null, 'no_source_rows']);
  assert.deepEqual([b.kpis.activation_7d.value, b.kpis.activation_7d.reason], [null, 'no_source_rows']);
  assert.deepEqual([b.kpis.retention_d1.value, b.kpis.retention_d1.reason], [null, 'not_mature']);
  assert.deepEqual([b.kpis.live_now.value, b.kpis.live_now.reason], [null, 'no_answer']);
  assert.equal(b.funnel.acquisition.steps[0].reason, 'not_measured');
  assert.equal(b.kpis.new_installs.today, 0, 'the reports table is there and empty: 0, not null');
  assert.equal(b.anomalies[0].id, 'stale_data');
});

test('v4 brief: a Wilson interval under 30 new installs only', async () => {
  assert.equal(wilson(0, 0), null);
  const [lo, hi] = wilson(3, 10);
  assert.ok(lo > 0.1 && lo < 0.11 && hi > 0.6 && hi < 0.61, `${lo} ${hi}`);
  const b = await briefOf(seed([[8, 1, 'usb', { first_start: 1, accounts: 1, tp_lt10: 1 }], [9, 2, 'none', { first_start: 1, accounts: 1 }]]));
  assert.ok(Array.isArray(b.kpis.activation_7d.ci95) && b.kpis.activation_7d.low_n);
  assert.equal((await briefOf(launch({ perDay: 6 }))).kpis.activation_7d.ci95, null, '42 new installs: no interval');
});

test('v4 brief: the GitHub token state says missing, empty, refused with the code, or ok', async () => {
  const DB = fakeD1();
  const state = async (G) => (await briefOf(DB, { G, L: null })).health.checks.find((c) => c.id === 'github_token').value;
  assert.equal(await state(GH_OK), 'ok');
  assert.equal(await state(GH_401), '401');
  assert.equal(await state({ ...GH_401, problems: ['No GITHUB_TOKEN secret: stars and downloads…'] }), 'missing');
  assert.equal(await state({ ...GH_401, problems: ['The GITHUB_TOKEN secret is empty (paste…'] }), 'empty');
  assert.equal(await state(null), 'no_answer');
});

// ------------------------------------------------------------------ the rules, each on both sides of its minimum
const ids = (b) => b.anomalies.map((a) => a.id.split(':')[0]);
const clone = (b) => JSON.parse(JSON.stringify(b));
const rerun = (b, change, releases = []) => { const x = clone(b); if (change) change(x); return rules(x, { now: NOW, releases }).anomalies.map((a) => a.id.split(':')[0]); };
const H = (b, id) => b.health.checks.find((c) => c.id === id);

test('v4 rules: the data rules (stale, cron, budget, token, tables, cut reads, old cadence, link none) fire only past their limit', async () => {
  const base = await briefOf(launch());
  assert.ok(!ids(base).includes('stale_data'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'freshness'), { status: 'warn', value: 200 })).includes('stale_data'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'cron_live_hourly'), { status: 'warn', value: 130 })).includes('cron_stale'));
  assert.ok(!rerun(base, (b) => Object.assign(H(b, 'budget_report'), { value: 0.69 })).includes('budget_high'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'budget_report'), { value: 0.7, status: 'warn' })).includes('budget_high'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'budget_site'), { value: 0.5, status: 'warn' })).includes('budget_high'));
  assert.ok(!ids(base).includes('github_token'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'github_token'), { status: 'warn', value: '401', impact: 'x' })).includes('github_token'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'tables'), { missing: ['site'] })).includes('table_missing'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'truncated'), { value: ['counts'] })).includes('truncated'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'old_cadence_share_today'), { status: 'warn', value: 0.4, num: 4, den: 10 })).includes('old_cadence'));
  assert.ok(rerun(base, (b) => Object.assign(H(b, 'link_none_share_7d'), { status: 'warn', value: 0.9, num: 9, den: 10 })).includes('link_none'));
  // The checks keep their minimum n: 2 reports today, both from an old build, stay quiet
  const small = await briefOf(seed([[0, 1, 'none', { accounts: 1 }, '0.1.0-beta.9'], [0, 2, 'none', { accounts: 1 }, '0.1.0-beta.9']]));
  assert.equal(H(small, 'old_cadence_share_today').reason, 'low_n');
  assert.equal(H(small, 'link_none_share_7d').reason, 'low_n');
  assert.ok(!ids(small).includes('old_cadence') && !ids(small).includes('link_none'));
});

test('v4 rules: a new error fires with 3 installs, or one new install on its first day; not with 2 older installs', async () => {
  const old = [[3, 1, 'usb', { accounts: 1 }], [3, 2, 'usb', { accounts: 1 }], [3, 3, 'usb', { accounts: 1 }]];
  assert.ok(!ids(await briefOf(seed(old, { errors: [[3, 1, 'E', 'A.B', 2], [3, 2, 'E', 'A.B', 1]] }))).includes('new_error'));
  assert.ok(ids(await briefOf(seed(old, { errors: [[3, 1, 'E', 'A.B', 2], [3, 2, 'E', 'A.B', 1], [3, 3, 'E', 'A.B', 1]] }))).includes('new_error'));
  const b = await briefOf(seed([[3, 1, 'usb', { accounts: 1, first_start: 1 }]], { errors: [[3, 1, 'E', 'A.B', 1]] }));
  assert.ok(ids(b).includes('new_error'));
  assert.equal(b.anomalies.find((a) => a.id.startsWith('new_error')).severity, 'medium', 'one install: medium, not high');
  const three = await briefOf(seed(old, { errors: [[3, 1, 'E', 'A.B', 2], [3, 2, 'E', 'A.B', 1], [3, 3, 'E', 'A.B', 1]] }));
  assert.equal(three.anomalies.find((a) => a.id.startsWith('new_error')).severity, 'high');
  assert.deepEqual(three.quality.new_errors[0].versions, [{ version: '0.1.0-beta.10', times: 4, installs: 3 }]);
  assert.equal(three.quality.new_errors[0].last_day, ago(3));
  assert.ok(b.suggested_actions.find((x) => x.anomaly.startsWith('new_error')).text.includes('first day'));
});

test('v4 rules: crash rate (50 sessions), leak (any), funnel drop and stuck shift (20 in both periods)', async () => {
  const base = await briefOf(launch());
  assert.ok(!rerun(base, (b) => Object.assign(b.quality, { sessions_d7: 49, crashes_d7: 10, sessions_prev7: 100, crashes_prev7: 0 })).includes('crash_rate'));
  assert.ok(rerun(base, (b) => Object.assign(b.quality, { sessions_d7: 50, crashes_d7: 3, sessions_prev7: 100, crashes_prev7: 1 })).includes('crash_rate'));
  assert.ok(!rerun(base, (b) => Object.assign(b.quality, { sessions_d7: 50, crashes_d7: 1, sessions_prev7: 100, crashes_prev7: 1 })).includes('crash_rate'), '2 per 100 is not over 2');
  assert.ok(!ids(base).includes('leak'));
  assert.ok(rerun(base, (b) => { b.quality.home_ip_back_d7 = 1; }).includes('leak'));
  const drop = (n, pn) => (b) => {
    b.funnel.activation.steps = [{ id: 'first_start', n }, { id: 'phone_seen', n: Math.round(n * 0.5) }, { id: 'protected', n: 1 }, { id: 'first_match', n: 0 }];
    b.funnel.activation.prev_steps = [{ id: 'first_start', n: pn }, { id: 'phone_seen', n: Math.round(pn * 0.8) }, { id: 'protected', n: 1 }, { id: 'first_match', n: 0 }];
  };
  assert.ok(rerun(base, drop(20, 20)).includes('funnel_drop'));
  assert.ok(!rerun(base, drop(19, 40)).includes('funnel_drop'));
  assert.ok(!rerun(base, drop(40, 19)).includes('funnel_drop'));
  const shift = (n, pn) => (b) => { b.stuck.n = n; b.stuck.prev_n = pn; b.stuck.buckets[0].share = 0.5; b.stuck.buckets[0].prev_share = 0.35; };
  assert.ok(rerun(base, shift(20, 20)).includes('stuck_shift'));
  assert.ok(!rerun(base, shift(19, 20)).includes('stuck_shift'));
});

test('v4 rules: spikes and drops against the 14-day median (5 or more), a release with no downloads, downloads with no new install', async () => {
  const base = await briefOf(launch());
  const ser = (list) => (b) => { b.series.new_installs = list; };
  const flat = Array(22).fill(6);
  assert.ok(!rerun(base, ser(flat)).includes('metric_spike'));
  assert.ok(rerun(base, ser([...flat.slice(0, 21), 20])).includes('metric_spike'), 'today, still going, upwards');
  assert.ok(!rerun(base, ser([...flat.slice(0, 21), 0])).includes('metric_drop'), 'today is never a drop');
  assert.ok(rerun(base, ser([...flat.slice(0, 20), 0, 6])).includes('metric_drop'));
  assert.ok(!rerun(base, ser([...Array(20).fill(4), 30, 4])).includes('metric_spike'), 'median under 5: quiet');
  assert.ok(rerun(base, null, [{ tag: 'v0.1.0-beta.11', date: ago(3), exe: 0 }]).includes('release_no_downloads'));
  assert.ok(!rerun(base, null, [{ tag: 'v0.1.0-beta.11', date: ago(0), exe: 0 }]).includes('release_no_downloads'));
  assert.ok(rerun(base, (b) => { b.kpis.exe_downloads.yesterday = 10; b.kpis.new_installs.yesterday = 0; }).includes('zero_new_after_downloads'));
  assert.ok(!rerun(base, (b) => { b.kpis.exe_downloads.yesterday = 9; b.kpis.new_installs.yesterday = 0; }).includes('zero_new_after_downloads'));
});

test('v4 rules: ranked by severity, at most 3 suggested actions, and the number of rules that ran', async () => {
  const b = clone(await briefOf(launch()));
  b.quality.home_ip_back_d7 = 2;
  Object.assign(H(b, 'freshness'), { status: 'bad', value: 900 });
  Object.assign(H(b, 'github_token'), { status: 'warn', value: 'empty' });
  const r = rules(b, { now: NOW });
  assert.deepEqual(r.anomalies.slice(0, 2).map((a) => a.id), ['stale_data', 'leak']);
  assert.ok(r.actions.length <= 3 && r.actions.every((x, i) => x.rank === i + 1 && x.text && x.text_pt));
  assert.equal(r.checked, 16);
  assert.ok(r.anomalies.every((a) => a.page && a.rule && a.message && a.text_pt && ['data', 'product'].includes(a.kind)));
});

test('v4 brief: a past date is that day as at its end, without live and GitHub; a date out of range is today', async () => {
  const DB = launch();
  const fetchFn = async () => { throw new Error('no network'); };
  const b = await readBrief({ DB }, { now: NOW, date: ago(2), fetchFn });
  assert.deepEqual([b.window.today, b.scope.historical, b.kpis.live_now.reason, b.kpis.new_installs.today], [ago(2), true, 'not_historical', 4]);
  const t = await readBrief({ DB }, { now: NOW, date: ago(200), fetchFn });
  assert.equal(t.window.today, ago(0));
});

// ------------------------------------------------------------------ the critics' cases (4 Nov.), each kept as a test

test('critics: the same behaviour every day gives no funnel drop and no stuck shift (cohorts compared at the same age)', async () => {
  // 10 new a day for 21 days; 6 get the phone and Protected on day 0, 4 on day 2: nothing changes from day to day
  const rows = [];
  let id = 1;
  for (let d = 20; d >= 0; d--) for (let k = 0; k < 10; k++, id++) {
    const late = k >= 6;
    rows.push([d, id, late ? 'none' : 'usb', { first_start: 1, accounts: 1, ...(late ? {} : { tp_lt10: 1 }) }]);
    if (late && d >= 2) rows.push([d - 2, id, 'usb', { accounts: 1, tp_lt10: 1 }]);
  }
  const b = await briefOf(seed(rows));
  const ids = b.anomalies.map((a) => a.id.split(':')[0]);
  assert.ok(!ids.includes('funnel_drop') && !ids.includes('stuck_shift'), ids.join(','));
  assert.deepEqual([b.kpis.activation_7d.value, b.kpis.activation_7d.prev.value], [1, 1]);
  assert.ok(b.kpis.activation_7d.running.value < 1, 'the running week is lower only because its days are not over');
});

test('critics: a past day\'s brief reads nothing from after that day', async () => {
  const DB = seed([[5, 1, 'none', { first_start: 1, accounts: 1 }], [1, 1, 'usb', { accounts: 1, tp_lt10: 1, m_valve: 1 }]], { errors: [[1, 1, 'Later', 'X.Y', 3]] });
  const b = await readBrief({ DB }, { now: NOW, date: ago(3), fetchFn: async () => { throw new Error('no network'); } });
  assert.deepEqual(b.funnel.activation.running_steps.map((x) => x.n), [1, 0, 0, 0], 'the phone, Protected and the match came after that day');
  assert.ok(!b.quality.top_errors_d7.some((e) => e.type === 'Later'));
  assert.ok(b.window.today_through <= `${ago(3)}T23:59:59Z`, b.window.today_through);
  assert.equal(b.window.requested_date, ago(3));
  assert.equal(b.health.checks.find((c) => c.id === 'github_token').status, 'ok', 'GitHub is not historical: not a problem');
});

test('critics: spikes and drops stay quiet on steady noise (scaled MAD), at most a few false alerts in 300 days', async () => {
  const base = await briefOf(launch());
  let x = 9, alerts = 0;
  const rnd = () => { x = (x * 1103515245 + 12345) % 2147483648; return x / 2147483648; };
  const poisson = (l) => { let k = 0, p = 1; const L = Math.exp(-l); do { k++; p *= rnd(); } while (p > L); return k - 1; };
  for (let t = 0; t < 300; t++) {
    const b = clone(base);
    b.series.new_installs = [...Array(22)].map(() => poisson(12));
    if (rules(b, { now: NOW }).anomalies.some((a) => /^metric_/.test(a.id))) alerts++;
  }
  assert.ok(alerts <= 15, `${alerts} of 300 days with a false alert`);
});

test('critics: the old-cadence check counts installs, not reports (old builds send fewer)', async () => {
  const rows = [];
  for (let id = 1; id <= 10; id++) rows.push([0, id, 'usb', { accounts: 1 }, id <= 6 ? '0.1.0-beta.9' : '0.1.0-beta.10']);
  const DB = seed(rows);
  for (let id = 7; id <= 10; id++) for (let r = 0; r < 8; r++) DB.raw.prepare("INSERT INTO pings (day, ts, id, app, link, minutes, dev) VALUES (?, ?, ?, '0.1.0-beta.10', 'usb', 1, 0)").run(ago(0), NOW - r * 60e3, ID(id));
  const c = (await briefOf(DB)).health.checks.find((x) => x.id === 'old_cadence_share_today');
  assert.deepEqual([c.value, c.num, c.den, c.status], [0.6, 6, 10, 'warn']);
});

test('critics: no GitHub picture gives null stars added (not 0), and a missing budget table null budgets', async () => {
  const DB = fakeD1();
  DB.raw.exec('DROP TABLE budget');
  const b = await briefOf(DB, { G: null, L: null });
  assert.deepEqual([b.kpis.stars.d7_added, b.kpis.stars.d7_reason], [null, 'no_source_rows']);
  assert.deepEqual([b.health.checks.find((c) => c.id === 'budget_report').value, b.health.checks.find((c) => c.id === 'budget_report').reason], [null, 'table_missing']);
});

test('critics: stale data says why (nothing arrived at all, or only your PCs report), and one action per kind of rule', async () => {
  const b = await briefOf(fakeD1(), { G: null, L: null });
  assert.equal(b.health.checks.find((c) => c.id === 'freshness').stale_kind, 'nothing_arrived');
  const r = clone(await briefOf(launch()));
  r.quality.new_errors = [1, 2, 3, 4].map((k) => ({ type: 'E' + k, at: 'A.B', first_day: ago(2), first_version: 'x', last_day: ago(1), times_d7: 9, installs_d7: 5, new_installs_day1: 1, versions: [] }));
  const out = rules(r, { now: NOW });
  assert.equal(out.actions.filter((a) => a.anomaly.startsWith('new_error')).length, 1);
  assert.match(out.actions.find((a) => a.anomaly.startsWith('new_error')).text, /and 3 more/);
});
