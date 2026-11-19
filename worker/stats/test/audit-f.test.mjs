// 15 Nov. 2026, the final audit's report F (publish/auditoria-final-2026-11-13/F-workers-dashboard.md): F-02, F-06, F-10 and the
// comments of F-04. F-01 and F-03 are in roll.test.mjs, F-09 in worker.test.mjs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeD1 } from './d1.mjs';
import ingest, { SITE_ROWS, SITE_ORIGIN, DAILY_ROWS, FORGET_ROWS, BUDGET_TRIGGERS } from '../src/index.js';
import * as api from '../src/api.js';
import * as dash from '../src/dashboard.js';

const ID = (n) => n.toString(16).padStart(32, '0');
const ymd = (t) => new Date(t).toISOString().slice(0, 10);
const siteReq = (body, ip) => {
  const r = new Request('https://stats.example.workers.dev/v1/site', { method: 'POST', headers: { origin: SITE_ORIGIN, 'content-type': 'text/plain;charset=UTF-8', 'cf-connecting-ip': ip },
    body: JSON.stringify(body) });
  Object.defineProperty(r, 'cf', { value: { country: 'PT', asn: 3243 } });
  return r;
};

// ------------------------------------------------------------------ F-02
test('F-02: the site counter takes 751 visits and more in a day (it stopped at 750), up to 5,000, then the cap', async () => {
  const DB = fakeD1();
  const status = async (i) => (await ingest.fetch(siteReq({ v: 1, p: i % 2 ? '/' : '/cs2-ip-ban/', r: i % 7 ? '' : 'www.google.com', e: i % 5 ? 'view' : 'download' }, `203.0.113.${i % 250}`), { DB })).status;
  for (let i = 0; i < 751; i++) assert.equal(await status(i), 204, `visit ${i + 1}`);
  const budget = () => DB.raw.prepare("SELECT rows FROM budget WHERE day LIKE '%:site'").get().rows;
  assert.equal(budget(), 751 * 4, 'a visit costs 4 of the budget');
  // up to the cap without 5,000 requests: the budget row at the point where exactly one more visit fits, then none
  DB.raw.prepare("UPDATE budget SET rows = ? WHERE day LIKE '%:site'").run(SITE_ROWS - 4);
  assert.equal(await status(1), 204, 'the last visit that fits');
  assert.equal(await status(2), 429, 'then it stops, as before, at its own cap');
  assert.equal(SITE_ROWS / 4, 5000);
});

test('F-02: the cap is the same in the Worker, schema.sql and the migration that changes it in a database made before', () => {
  const schema = readFileSync(new URL('../schema.sql', import.meta.url), 'utf8');
  const mig = readFileSync(new URL('../migrations/0003-site-budget.sql', import.meta.url), 'utf8');
  for (const t of BUDGET_TRIGGERS.filter((x) => x.includes(':site'))) {
    assert.ok(schema.includes(t + ';'), `schema.sql: ${t.slice(0, 70)}`);
    assert.ok(mig.includes(t + ';'), `migration: ${t.slice(0, 70)}`);
  }
  assert.ok(/DROP TRIGGER IF EXISTS budget_site_insert;/.test(mig) && /DROP TRIGGER IF EXISTS budget_site_update;/.test(mig), 'an old database keeps its triggers unless they are dropped');
});

test('F-02: the day budgets, at their real cost in writes, stay inside D1\'s free 100,000 a day', () => {
  // a visit: costs 4 of the budget, writes at most 3 (the site row and its index when the combination is new, the budget row)
  const site = (SITE_ROWS / 4) * 3;
  // a report: counts have 3 index writes (counts_day, counts_day_name, and counts_install for id_install) and the budget counts 2: up to 1.25 times
  const reports = DAILY_ROWS * 1.25;
  const other = 5000;   // the crons (live_hourly, gh_daily, memo, the written days), the env report
  const total = site + reports + FORGET_ROWS + other;
  assert.ok(total <= 80_000, `${total} writes a day at the caps`);
  assert.ok(total < 100_000);
});

// ------------------------------------------------------------------ F-06
test('F-06: the live signal counts the same installs as before and reads the accounts and clicks once, not for each live row', async () => {
  const DB = fakeD1();
  const NOW = Date.UTC(2026, 10, 12, 22, 0), today = ymd(NOW), yday = ymd(NOW - 864e5), old = ymd(NOW - 5 * 864e5);
  const live = DB.raw.prepare('INSERT INTO live (id, ts, app, dev) VALUES (?, ?, ?, 0)');
  for (let i = 1; i <= 12; i++) live.run(ID(i), NOW - 60_000, '0.1.0-beta.13');
  // 1 minutes, 2 account today, 3 account yesterday, 4 click today, 5 click yesterday, 6 nothing, 7 an account five days ago (not counted), 8 zero accounts, 9 a click five days ago, 10 minutes 0
  const ping = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, minutes, dev) VALUES (?, ?, ?, ?, ?, 0)');
  ping.run(today, NOW, ID(1), '0.1.0-beta.13', 3); ping.run(today, NOW, ID(10), '0.1.0-beta.13', 0); ping.run(today, NOW, ID(6), '0.1.0-beta.13', 0);
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)');
  cnt.run(today, ID(2), 'accounts', 2); cnt.run(yday, ID(3), 'accounts', 1); cnt.run(old, ID(7), 'accounts', 4); cnt.run(today, ID(8), 'accounts', 0); cnt.run(today, ID(11), 'ip_changed', 3);
  const clk = DB.raw.prepare('INSERT INTO clicks (day, id, name, n, dev) VALUES (?, ?, ?, 1, 0)');
  clk.run(today, ID(4), 'NavServers'); clk.run(yday, ID(5), 'NavServers'); clk.run(old, ID(9), 'NavServers');
  const ids = async (sql) => (await DB.prepare(sql).bind(NOW - 360_000, ...api.liveReal(NOW).args).all()).results.map((r) => r.id).sort();
  const R = api.liveReal(NOW);
  const OLD = `SELECT id FROM live WHERE ts >= ? AND (EXISTS (SELECT 1 FROM pings p WHERE p.id = live.id AND p.minutes >= 1) OR EXISTS (SELECT 1 FROM counts c WHERE c.day >= '${yday}' AND c.name = 'accounts' AND c.id = live.id AND c.n >= 1) OR EXISTS (SELECT 1 FROM clicks k WHERE k.day >= '${yday}' AND k.id = live.id))`;
  const NEW = `SELECT id FROM live WHERE ts >= ? AND ${R.sql}`;
  const was = (await DB.prepare(OLD).bind(NOW - 360_000).all()).results.map((r) => r.id).sort();
  assert.deepEqual(was, [1, 2, 3, 4, 5].map(ID), 'what the old rule counted');
  assert.deepEqual(await ids(NEW), was, 'the new one counts the same');
  // The plan: the accounts through counts_day_name with both days as equalities, and neither subquery depends on the live row
  const plan = DB.raw.prepare(`EXPLAIN QUERY PLAN ${NEW}`).all(NOW - 360_000, ...R.args).map((r) => r.detail).join(' | ');
  assert.ok(/counts_day_name \(day=\? AND name=\?\)/.test(plan), plan);
  assert.equal((plan.match(/LIST SUBQUERY/g) || []).length, 2, plan);
  // and through the functions that use it
  const L = await api.live(DB, { dev: false, now: NOW });
  assert.equal(L.now, 5);
});

// ------------------------------------------------------------------ F-10
test('F-10: an answer kept because nothing was written is worked out again after SUMMARY_MAX_STALE (its hour and clock would be hours old)', async () => {
  const DB = fakeD1();
  const NOW = Date.UTC(2026, 10, 12, 22, 0), today = ymd(NOW);
  DB.raw.prepare("INSERT INTO pings (day, ts, id, app, os, mode, link, minutes, dev, country) VALUES (?, ?, ?, '0.1.0-beta.13', '10.0.26100', 'game', 'usb', 20, 0, 'PT')").run(today, NOW - 3600_000, ID(1));
  DB.raw.prepare("INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, 'accounts', 1, 0)").run(today, ID(1));
  DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, 10)').run(today);
  let reads = 0;
  const db = { ...DB, prepare: (sql) => { if (/FROM (pings|counts|clicks)\b/.test(sql)) reads++; return DB.prepare(sql); } };
  const opt = (now) => ({ period: 7, f: { version: 'all', country: 'all', mode: 'all', link: 'all' }, dev: false, now, exclude: [] });
  await dash.cachedSummary(db, opt(NOW), NOW);
  const r0 = reads;
  const t1 = NOW + dash.SUMMARY_MAX_AGE + 60_000;
  await dash.cachedSummary(db, opt(t1), t1);
  assert.equal(reads, r0, 'an hour or so later, nothing written: kept');
  const t2 = NOW + dash.SUMMARY_MAX_STALE + 60_000;
  const s = await dash.cachedSummary(db, opt(t2), t2);
  assert.ok(reads > r0, 'after the limit it is worked out again');
  assert.equal(s.now, new Date(t2).toISOString().slice(11, 16), 'with the clock of that moment');
  assert.ok(dash.SUMMARY_MAX_STALE >= 2 * 3600_000 && dash.SUMMARY_MAX_STALE <= 6 * 3600_000);
});

// ------------------------------------------------------------------ F-04 (the comments of this folder; README and docs are the text session's)
test('F-04: nothing in the Worker\'s own text says it never reads the IP (ipAllowed does, for the rate limiter)', () => {
  const src = readFileSync(new URL('../src/index.js', import.meta.url), 'utf8');
  const schema = readFileSync(new URL('../schema.sql', import.meta.url), 'utf8');
  assert.ok(src.includes("request.headers.get('cf-connecting-ip')"), 'the code reads it');
  for (const [name, text] of [['index.js', src], ['schema.sql', schema]]) assert.ok(!/never reads the sender|never the IP\)|never reads the IP/i.test(text), `${name} still says it never reads the IP`);
});
