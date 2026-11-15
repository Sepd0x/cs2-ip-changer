// 15 Nov. 2026 (audit F-01): a day that has ended is read once (src/roll.js), and the summary reads it from there. The proof that
// matters: the dashboard's numbers are the same. The summary is worked out both ways, from the report tables and from the written
// days, over made-up data that has every awkward case (a PC with a dev build and a release build the same day, an excluded PC,
// automated installs, kept IDs, errors, several reports a day) and over a grid of periods and filters, and the two must be equal.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fakeD1 } from './d1.mjs';
import * as api from '../src/api.js';
import * as dash from '../src/dashboard.js';
import * as ingest from '../src/index.js';
import { rollMissing, rollDay, lastRolled, rawFromDay, GRACE_MS } from '../src/roll.js';

const NOW = Date.UTC(2026, 10, 12, 22, 0);
const ID = (n) => n.toString(16).padStart(32, '0');
const ymd = (t) => new Date(t).toISOString().slice(0, 10);
const ALL = { version: 'all', country: 'all', mode: 'all', link: 'all' };

/** A small deterministic generator (so a failure repeats). */
function rng(seed) { let s = seed >>> 0; return () => { s = (Math.imul(s, 1664525) + 1013904223) >>> 0; return s / 2 ** 32; }; }

/** Made-up use over `days` days up to NOW, 40 installs, with the awkward cases. Returns the fake D1. */
function seed(days = 25, installs = 40) {
  const DB = fakeD1();
  const r = rng(7), pick = (a) => a[Math.floor(r() * a.length)];
  const ins = {
    p: DB.raw.prepare('INSERT INTO pings (day, ts, id, app, os, mode, link, minutes, dev, country) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'),
    c: DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, ?)'),
    k: DB.raw.prepare('INSERT INTO clicks (day, id, name, n, dev) VALUES (?, ?, ?, ?, ?)'),
    e: DB.raw.prepare('INSERT INTO errors (day, id, app, type, at, n, dev) VALUES (?, ?, ?, ?, ?, ?, ?)'),
  };
  const apps = ['0.1.0-beta.9', '0.1.0-beta.10', '0.1.0-beta.13'], modes = [null, 'game', 'cs2open', 'always', 'nophone'], links = [null, 'usb', 'hotspot', 'other', 'none'];
  const names = ['ip_changed', 'ip_same', 'app_start', 'crash', 'm_valve', 'm_comm', 'm_valve_lag', 'min_prot', 'first_start', 'servers', 'accounts', 'scr_connection', 'path_connection_regions',
    'tp_lt10', 'set_theme', 'reg_fra', 'chk_egress', 'act_3', 'age_1', 'tour_done', 'ping_lt30', 'unclean_exit', 'phone_setup_done', 'data_lt250m'];
  DB.raw.exec('BEGIN');
  for (let d = days; d >= 0; d--) {
    const day = ymd(NOW - d * 864e5);
    for (let i = 1; i <= installs; i++) {
      if (r() < 0.45) continue;
      const id = ID(i), kept = i % 4 !== 0, auto = i % 9 === 0, app = pick(apps), country = pick(['PT', 'BR', 'ES', null]);
      const reports = 1 + Math.floor(r() * 5);
      // install 2 is the developer's PC (excluded): both a release and a dev build, some days the same day
      const devs = i === 2 ? [0, 1] : [i % 11 === 0 ? 1 : 0];
      for (const dev of devs) {
        for (let q = 0; q < reports; q++) {
          const ts = Date.parse(day + 'T00:00:00Z') + Math.floor(r() * 864e5);
          ins.p.run(day, ts, id, q ? pick(apps) : app, '10.0.26100', pick(modes), pick(links), auto ? 0 : Math.floor(r() * 40), dev, country);
          if (kept) ins.c.run(day, id, 'id_install', 1, dev);
          for (const n of names) if (r() < 0.3) ins.c.run(day, id, n, n === 'accounts' || n === 'servers' ? Math.floor(r() * 8) : 1 + Math.floor(r() * 4), dev);
          if (!auto && r() < 0.4) ins.k.run(day, id, pick(['ChangeIp_Click', 'Setup_usb', 'NavServers']), 1 + Math.floor(r() * 3), dev);
          if (r() < 0.1) ins.e.run(day, id, app, pick(['E_a', 'Fatal_b']), 'A.B', 1, dev);
        }
      }
    }
  }
  // Rows with no report (counters of an install-day that has no ping): ignored by the summary, and by the written days
  ins.c.run(ymd(NOW - 3 * 864e5), ID(77), 'ip_changed', 5, 0);
  DB.raw.exec('COMMIT');
  return DB;
}

const cases = () => {
  const out = [];
  for (const period of [1, 7, 30, 90]) for (const dev of [false, true]) for (const exclude of [[], [ID(2)]]) out.push({ period, f: ALL, dev, exclude });
  for (const f of [{ ...ALL, version: '0.1.0-beta.13' }, { ...ALL, country: 'PT' }, { ...ALL, mode: 'game' }, { ...ALL, link: 'usb' }, { ...ALL, country: '?' }, { ...ALL, version: '0.1.0-beta.10', link: 'hotspot' }])
    for (const dev of [false, true]) out.push({ period: 30, f, dev, exclude: [ID(2)] });
  return out;
};
const run = async (db, c, rolledTo = null) => JSON.parse(JSON.stringify(await api.summary(db, { ...c, now: NOW, rolledTo })));

test('F-01: the summary from the written days is the same as from the report tables (periods, filters, dev, excluded PC)', async () => {
  const DB = seed();
  const ref = [];
  for (const c of cases()) ref.push(await run(DB, c));
  const days = await rollMissing(DB, NOW, 1000);
  assert.equal(days.length, 25, 'every day that has ended: today stays in the tables');
  assert.equal(await lastRolled(DB), ymd(NOW - 864e5));
  const to = await lastRolled(DB);
  for (const [i, c] of cases().entries()) assert.deepEqual(await run(DB, c, to), ref[i], JSON.stringify(c));
  assert.ok(ref.some((s) => s.installs.active > 0 && s.cohortsExact.length) && ref.some((s) => s.errors.length), 'the data has returns, cohorts and errors to compare');
});

test('F-01: half-written (some days from the written copy, the rest from the tables) is the same too, whatever day it stops at', async () => {
  const DB = seed(20, 25);
  const c = { period: 30, f: ALL, dev: false, exclude: [] };
  const ref = await run(DB, c);
  for (const k of [1, 5, 12, 19]) {
    DB.raw.exec('DELETE FROM iday; DELETE FROM iday_done');
    await rollMissing(DB, NOW, k);
    assert.deepEqual(await run(DB, c, await lastRolled(DB)), ref, `${k} days written`);
  }
});

test('F-01: the closed days are not read from the report tables any more (only the rows of the days not written)', async () => {
  const DB = seed();
  await rollMissing(DB, NOW, 1000);
  const to = await lastRolled(DB);
  const seen = [];
  const spy = { ...DB, prepare: (sql) => {
    const m = /FROM (pings|counts|clicks|errors)/.exec(sql), st = DB.prepare(sql);
    if (!m) return st;
    return { bind: (...a) => { const bound = st.bind(...a); return { all: async () => { const r = await bound.all(); seen.push([m[1], r.results.length, a, sql]); return r; }, first: () => bound.first() }; } };
  } };
  await api.summary(spy, { period: 30, f: ALL, dev: false, now: NOW, rolledTo: to });
  const today = ymd(NOW), todayRows = (t) => DB.raw.prepare(`SELECT COUNT(*) n FROM ${t} WHERE day = ?`).get(today).n;
  assert.ok(seen.length >= 6, 'the table reads still happen, for today');
  let recent = 0;
  for (const [t, rows, args, sql] of seen) {
    // the one read that is not from the written days by design: the last 8 days of reports with their times (Por hora, the same time yesterday and a week ago)
    if (/SELECT day, id, ts, minutes FROM pings/.test(sql)) { recent++; assert.ok(args.includes(ymd(NOW - 8 * 864e5))); continue; }
    // the first day each error type was seen (90 days of the errors table: the app rarely sends any, a few rows)
    if (sql.includes('MIN(day) AS first')) continue;
    assert.ok(args.includes(today), `${t}: starts at the first day not written (${args.filter((a) => /^\d{4}-/.test(a))})`);
    assert.ok(rows <= todayRows(t) + 1, `${t}: ${rows} rows, today has ${todayRows(t)}`);
  }
  assert.equal(recent, 1);
  // and what a day of 25 holds is far more than what the summary read for it
  assert.ok(DB.raw.prepare('SELECT COUNT(*) n FROM counts').get().n > 20 * todayRows('counts'));
});

test('F-01: a day is written when it has ended (10 minutes after midnight), oldest first, never the open day, and again gives the same rows', async () => {
  const DB = seed(5, 10);
  const today = ymd(NOW);
  // the first 10 minutes of a day: yesterday may still have a report in flight, so it is not written yet
  const early = seed(5, 10);
  assert.deepEqual(await rollMissing(early, Date.parse(today + 'T00:05:00Z'), 99), [-5, -4, -3, -2].map((d) => ymd(NOW + d * 864e5)));
  const first = await rollMissing(DB, NOW, 2);
  assert.deepEqual(first, [ymd(NOW - 5 * 864e5), ymd(NOW - 4 * 864e5)], 'two a run, the oldest first');
  const rows = () => JSON.stringify(DB.raw.prepare('SELECT * FROM iday ORDER BY day, id, dev').all());
  const before = rows();
  await rollDay(DB, ymd(NOW - 5 * 864e5));
  assert.equal(rows(), before, 'writing a day again changes nothing');
  assert.deepEqual(await rollMissing(DB, NOW, 99), [ymd(NOW - 3 * 864e5), ymd(NOW - 2 * 864e5), ymd(NOW - 864e5)]);
  assert.deepEqual(await rollMissing(DB, NOW, 99), [], 'nothing left but today');
  assert.equal(rawFromDay(ymd(NOW - 864e5), NOW), today);
  assert.equal(rawFromDay(ymd(NOW - 864e5), Date.parse(today + 'T00:05:00Z')), ymd(NOW - 864e5), 'inside the grace the day before is still read from the tables');
  assert.ok(GRACE_MS >= 5 * 60_000);
  // no counters without a report in the copy
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM iday WHERE r = 0').get().n, 0);
});

test('F-01: the copy has no table, a broken row or a failing read: the summary still answers from the report tables, and the Workers make the table', async () => {
  const DB = seed(6, 12);
  const c = { period: 7, f: ALL, dev: false, exclude: [] };
  const ref = await run(DB, c);
  await rollMissing(DB, NOW, 99);
  const to = await lastRolled(DB);
  DB.raw.exec("UPDATE iday SET j = '{broken' WHERE day = (SELECT MAX(day) FROM iday)");
  assert.deepEqual(await run(DB, c, to), ref, 'a row that does not read: the old way, same numbers');
  DB.raw.exec('DROP TABLE iday; DROP TABLE iday_done');
  assert.deepEqual(await run(DB, c, to), ref, 'no table');
  // with the table missing, rollMissing makes it
  const DB2 = seed(3, 6);
  DB2.raw.exec('DROP TABLE iday; DROP TABLE iday_done');
  assert.ok((await rollMissing(DB2, NOW, 99)).length >= 2);
  assert.ok(DB2.raw.prepare('SELECT COUNT(*) n FROM iday').get().n > 0);
});

test('F-01: Delete my stats and the 12-month cleanup remove the written days too, and the summary has no install ID', async () => {
  const DB = seed(8, 12);
  await rollMissing(DB, NOW, 99);
  const n = (id) => DB.raw.prepare('SELECT COUNT(*) n FROM iday WHERE id = ?').get(id).n;
  assert.ok(n(ID(3)) > 0);
  const post = (b) => new Request('https://stats.example.workers.dev/v1/forget', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(b) });
  assert.equal((await ingest.default.fetch(post({ v: 1, id: ID(3), app: '0.1.0-beta.13' }), { DB })).status, 204);
  assert.equal(n(ID(3)), 0, 'forget: the written days of that install go');
  assert.ok(n(ID(4)) > 0, 'the others stay');
  const old = DB.raw.prepare('SELECT MIN(day) d FROM iday').get().d;
  const out = await dash.prune(DB, Date.parse(old + 'T00:00:00Z') + (dash.RETAIN_DAYS + 1) * 864e5);
  assert.ok(out && DB.raw.prepare('SELECT COUNT(*) n FROM iday WHERE day <= ?').get(old).n === 0, 'prune');
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM iday_done WHERE day <= ?').get(old).n, 0);
  // the answer that leaves has no ID, from the written days too
  const to = await lastRolled(DB);
  const text = JSON.stringify(await api.summary(DB, { period: 30, f: ALL, dev: false, now: NOW, rolledTo: to }));
  for (let i = 1; i <= 12; i++) assert.ok(!text.includes(ID(i)), `ID ${i}`);
});

test('F-01: cachedSummary writes a day after working the summary out, the 10-minute cron one and the daily cron two', async () => {
  const DB = seed(10, 8);
  const done = () => DB.raw.prepare('SELECT COUNT(*) n FROM iday_done').get().n;
  await dash.cachedSummary(DB, { period: 7, f: ALL, dev: false, now: NOW, exclude: [] }, NOW);
  assert.equal(done(), 1);
  await dash.cachedSummary(DB, { period: 30, f: ALL, dev: false, now: NOW, exclude: [] }, NOW);
  assert.equal(done(), 2);
  await dash.default.scheduled({ cron: '*/10 * * * *', scheduledTime: NOW }, { DB });
  assert.equal(done(), 3, 'the 10-minute cron writes one');
  globalThis.fetch = async () => new Response('{}', { status: 403 });
  await dash.default.scheduled({ cron: dash.DAILY_CRON, scheduledTime: NOW }, { DB, GITHUB_REPO: 'a/b' });
  assert.equal(done(), 5, 'the daily one two');
  // a brief for a past day does not use or write them
  const before = done();
  await dash.cachedSummary(DB, { period: 30, f: ALL, dev: false, now: NOW - 3 * 864e5, exclude: [], until: ymd(NOW - 3 * 864e5) }, NOW);
  assert.equal(done(), before);
});

// ------------------------------------------------------------------ F-03: schema.sql has every index and table of the migrations
test('F-03: schema.sql has every index, table and trigger the migrations make (a database made from schema.sql alone is as fast)', () => {
  const schema = readFileSync(new URL('../schema.sql', import.meta.url), 'utf8');
  const dir = new URL('../migrations/', import.meta.url);
  const files = readdirSync(dir).filter((f) => f.endsWith('.sql'));
  assert.ok(files.length >= 2);
  const norm = (s) => s.replace(/\s+/g, ' ').trim();
  for (const f of files) {
    const sql = readFileSync(new URL(f, dir), 'utf8').replace(/^--.*$/gm, '');
    const stmts = sql.split(';').map(norm).filter(Boolean);
    assert.ok(stmts.length, f);
    // (a DROP in a migration replaces an object that exists in old databases only; its CREATE is what schema.sql must have)
    for (const s of stmts.filter((x) => !/^DROP /i.test(x))) assert.ok(norm(schema).includes(s), `${f}: schema.sql lacks: ${s.slice(0, 90)}`);
  }
});

test('F-01: the free plan stops a request at 50 D1 queries: a cold brief and the crons, with days still to write, stay under it', async () => {
  const DB = seed(12, 10);
  let q = 0;
  const counted = { ...DB, prepare: (sql) => { q++; return DB.prepare(sql); }, batch: async (l) => { q -= l.length - 1; return DB.batch(l); } };
  // (every statement of a batch is prepared but the batch is one query)
  globalThis.fetch = async () => new Response('{}', { status: 403 });
  await dash.readBrief({ DB: counted }, { now: NOW, fetchFn: globalThis.fetch });
  assert.ok(q <= 50, `a cold brief: ${q} queries`);
  q = 0;
  await dash.default.scheduled({ cron: '*/10 * * * *', scheduledTime: NOW }, { DB: counted });
  assert.ok(q <= 50, `the 10-minute cron: ${q}`);
  q = 0;
  await dash.default.scheduled({ cron: dash.DAILY_CRON, scheduledTime: NOW }, { DB: counted, GITHUB_REPO: 'a/b' });
  assert.ok(q <= 50, `the daily cron: ${q}`);
});
