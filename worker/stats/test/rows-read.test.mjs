// 12 Nov. 2026: D1 went over its free 5 M rows read a day (9 M in 24 h; 22:00 UTC, D1_ERROR on POST /v1/site) and the app's
// reports and the site's visits were lost until midnight UTC. The 30-day summary read ~66,000 rows per dashboard load (D1 >
// Metrics: 5 M rows in 76 runs). Now the dashboard keeps its answers in the memo table (src/memo.js) and the sums read counts once.
// Two parts: the memo's rules on the fake D1 (always run), and the rows a load reads, measured on a local D1 (Miniflare, the
// same rows_read as Cloudflare) with the backup of 12 Nov. (outside git: publish/, it has install IDs). That part is skipped,
// saying why, on a PC without the backup or without wrangler.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeD1 } from './d1.mjs';
import * as api from '../src/api.js';
import * as dash from '../src/dashboard.js';
import { memo, pruneMemo, forgetMemo, fnv } from '../src/memo.js';
import { findBackup, loadMiniflare, localD1, countRows, loads, ALL } from '../tools/rows-read.mjs';

const NOW = Date.parse('2026-11-12T22:00:00Z');
const ID = 'ab'.repeat(16);

/** A fake D1 with one real install-day today and the day's budget row; reads of the report tables counted. */
function seeded() {
  const DB = fakeD1();
  const today = '2026-11-12';
  DB.raw.prepare("INSERT INTO pings (day, ts, id, app, os, mode, link, minutes, dev, country) VALUES (?, ?, ?, '0.1.0-beta.13', '10.0.26100', 'game', 'usb', 20, 0, 'PT')").run(today, NOW - 3600_000, ID);
  DB.raw.prepare("INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, 'accounts', 1, 0), (?, ?, 'id_install', 1, 0), (?, ?, 'first_start', 1, 0)").run(today, ID, today, ID, today, ID);
  DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, 10)').run(today);
  let reads = 0;
  const db = { ...DB, prepare: (sql) => { if (/FROM (pings|counts|clicks)\b/.test(sql)) reads++; return DB.prepare(sql); } };
  return { DB, db, reads: () => reads };
}
const opt = (now = NOW) => ({ period: 30, f: ALL, dev: false, now, exclude: [] });

test('memo: the summary is read again from the memo table, not from the reports, and it has no install ID', async () => {
  const { DB, db, reads } = seeded();
  const a = await dash.cachedSummary(db, opt());
  const first = reads();
  assert.ok(first > 0);
  const b = await dash.cachedSummary(db, opt(NOW + 60_000), NOW + 60_000);
  assert.deepEqual(b, a);
  assert.equal(reads(), first, 'a minute later: no report table read');
  const rows = DB.raw.prepare('SELECT k, v FROM memo').all();
  assert.equal(rows.length, 1);
  assert.match(rows[0].k, /^sum\|2026-11-12\|30\|0\|/);
  assert.ok(!rows[0].v.includes(ID), 'no install ID kept');
});

test('memo: an old answer stays while nothing was written; after a write it is worked out again', async () => {
  const { DB, db, reads } = seeded();
  await dash.cachedSummary(db, opt());
  const r0 = reads();
  const later = NOW + dash.SUMMARY_MAX_AGE + 60_000;
  await dash.cachedSummary(db, opt(later), later);
  assert.equal(reads(), r0, 'old but the budget is the same: kept');
  // The live signal's share changes every 5 minutes while the app is open: it doesn't count as new data
  DB.raw.prepare("INSERT INTO budget (day, rows) VALUES ('2026-11-12:live', 4)").run();
  await dash.cachedSummary(db, opt(later), later);
  assert.equal(reads(), r0, 'only the live share moved: kept');
  DB.raw.prepare("UPDATE budget SET rows = 30 WHERE day = '2026-11-12'").run();
  await dash.cachedSummary(db, opt(NOW + 60_000), NOW + 60_000);
  assert.equal(reads(), r0, 'a report arrived but the answer is younger than SUMMARY_MAX_AGE: kept');
  await dash.cachedSummary(db, opt(later), later);
  assert.ok(reads() > r0, 'a report arrived and the answer is old: worked out again');
});

test('memo: each key has the day, the period, the view and the filters, never the excluded IDs themselves', async () => {
  const { DB, db } = seeded();
  await dash.cachedSummary(db, opt());
  await dash.cachedSummary(db, { ...opt(), period: 7 });
  await dash.cachedSummary(db, { ...opt(), dev: true });
  await dash.cachedSummary(db, { ...opt(), f: { ...ALL, version: '0.1.0-beta.13' } });
  await dash.cachedSummary(db, { ...opt(), exclude: [ID] });
  const next = NOW + 864e5;
  await dash.cachedSummary(db, opt(next), next);
  const keys = DB.raw.prepare('SELECT k FROM memo ORDER BY k').all().map((r) => r.k);
  assert.equal(keys.length, 6);
  assert.ok(keys.some((k) => k.endsWith(`|${fnv(ID)}`)));
  assert.ok(keys.every((k) => !k.includes(ID)));
  assert.ok(keys.some((k) => k.startsWith('sum|2026-11-13|')), 'a new UTC day is a new key');
});

test('memo: no table, a broken row or a failing write still answer (the old way), and the cron clears old keys', async () => {
  const DB = fakeD1();
  DB.raw.exec('DROP TABLE memo');
  let n = 0;
  const v = await memo(DB, { key: 'x|1', now: NOW, maxAge: 1000, compute: async () => ({ n: ++n }) });
  assert.deepEqual(v, { n: 1 });
  assert.equal(DB.raw.prepare('SELECT COUNT(*) AS c FROM memo').get().c, 1, 'made on the first write');
  DB.raw.prepare("UPDATE memo SET v = '{not json'").run();
  assert.deepEqual(await memo(DB, { key: 'x|1', now: NOW + 1, maxAge: 1000, compute: async () => ({ n: ++n }) }), { n: 2 });
  const failing = { prepare: () => ({ bind: () => ({ first: async () => { throw new Error('D1_ERROR'); }, run: async () => { throw new Error('D1_ERROR'); } }) }) };
  assert.deepEqual(await memo(failing, { key: 'x|1', now: NOW, maxAge: 1000, compute: async () => ({ ok: 1 }) }), { ok: 1 });
  // Two asks at once in one isolate: worked out once
  let c = 0;
  const slow = () => new Promise((r) => setTimeout(() => r({ c: ++c }), 5));
  const [p, q] = await Promise.all([memo(DB, { key: 'y', now: NOW, maxAge: 1000, compute: slow }), memo(DB, { key: 'y', now: NOW, maxAge: 1000, compute: slow })]);
  assert.deepEqual([p, q, c], [{ c: 1 }, { c: 1 }, 1]);
  await memo(DB, { key: 'gh|2026-11-12|120|', now: NOW, maxAge: 1000, compute: async () => ({}) });
  await forgetMemo(DB, 'gh|');
  assert.equal(DB.raw.prepare("SELECT COUNT(*) AS c FROM memo WHERE k LIKE 'gh|%'").get().c, 0);
  assert.equal(await pruneMemo(DB, NOW + 3 * 864e5), 2);
});

test('memo: the live peaks and the GitHub history go through it too, and the daily cron forgets the GitHub one', async () => {
  const { readFileSync } = await import('node:fs');
  const src = readFileSync(new URL('../src/dashboard.js', import.meta.url), 'utf8');
  assert.ok(src.includes('peaks: cachedPeaks(now0)') && src.includes('peaks: cachedPeaks(now)'), '/api/live and the brief');
  assert.ok(src.includes('cachedGithubHistory(env.DB, now0, now0)') && src.includes('cachedGithubHistory(env.DB, at, now, 120'), '/api/github and the brief');
  assert.ok(src.includes("await forgetMemo(env.DB, 'gh|')") && src.includes('await pruneMemo(env.DB, now)'));
  const home = readFileSync(new URL('../dashboard/v5/home.js', import.meta.url), 'utf8');
  assert.ok(!/setInterval\(load, 300_000\)/.test(home) && /if \(!document\.hidden && Date\.now\(\) - loadedAt >= 290_000\) reload\(\)/.test(home), 'a hidden /dashboard tab stops asking');
});

// ---------------------------------------------------------------- rows read, with the backup of 12 Nov. on a local D1

// Limits with the data of 12 Nov. (measured: worked out 38,847 for 30 days and 33,928 for 7, a kept load under 60; before
// 82,347 and 74,137 per load, 72,457 and 64,247 with the indexes)
export const LIMIT = { summary30: 45_000, summary7: 40_000, firstLoad: 45_000, keptLoad: 200, liveTick: 20, cron: 20 };
const backup = findBackup();
const mfMod = backup ? await loadMiniflare() : null;
const skip = !backup ? 'no backup (publish/backup-d1-2026-11-12-antes-indices.sql or D1_BACKUP)' : !mfMod ? 'no Miniflare (npm i -g wrangler, or MINIFLARE=<path>)' : false;

test('rows read: the summary, a dashboard load, the live tick and the cron stay under their limits with the data of 12 Nov.', { skip, timeout: 120_000 }, async () => {
  const { db, dispose } = await localD1(mfMod.Miniflare, backup, { indexes: true });
  try {
    const rows = async (fn) => (await countRows(db, fn)).total;
    const s30 = await rows((d) => api.summary(d, { period: 30, f: ALL, dev: false, now: NOW }));
    const s7 = await rows((d) => api.summary(d, { period: 7, f: ALL, dev: false, now: NOW }));
    assert.ok(s30 <= LIMIT.summary30, `summary(30) read ${s30} rows, limit ${LIMIT.summary30}`);
    assert.ok(s7 <= LIMIT.summary7, `summary(7) read ${s7} rows, limit ${LIMIT.summary7}`);
    const L = loads(api, dash, NOW);
    const first = await rows(L.owner);
    const next = await rows(L.owner);
    const dataNext = await rows(L.data).then(() => rows(L.data));
    assert.ok(first <= LIMIT.firstLoad, `/dashboard first load read ${first}, limit ${LIMIT.firstLoad}`);
    assert.ok(next <= LIMIT.keptLoad, `/dashboard next load read ${next}, limit ${LIMIT.keptLoad}`);
    assert.ok(dataNext <= LIMIT.keptLoad, `/data next load read ${dataNext}, limit ${LIMIT.keptLoad}`);
    const tick = await rows((d) => api.live(d, { dev: false, now: NOW + 60_000, peaks: dash.cachedPeaks(NOW + 60_000) }));
    assert.ok(tick <= LIMIT.liveTick, `/api/live tick read ${tick}, limit ${LIMIT.liveTick}`);
    const cron = await rows((d) => dash.sampleLive(d, { now: NOW }));
    assert.ok(cron <= LIMIT.cron, `cron read ${cron}, limit ${LIMIT.cron}`);
  } finally { await dispose(); }
});
