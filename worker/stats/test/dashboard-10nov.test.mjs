// 10 nov. 2026 (session 9, publish/DASHBOARD-2026-11-10.md): the dashboard's counting checked against the real data. Each fix here
// has its test, which failed before. P3 and P8 are from the code audit (ServerRouteToolCodex/lab/reports/dashboard-audit-2026-11-10.md).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeD1 } from './d1.mjs';
import { counterOk } from '../src/index.js';
import { summary, live } from '../src/api.js';
import { sampleLive, readBrief } from '../src/dashboard.js';
import { seed } from './v4.test.mjs';

const ID = (n) => n.toString(16).padStart(32, '0');
const F0 = { version: 'all', country: 'all', mode: 'all', link: 'all' };
const NOW = Date.parse('2026-11-10T12:00:00Z');
const today = new Date(NOW).toISOString().slice(0, 10);

// The real data of 10 nov.: 4 antivirus sandboxes (no Steam accounts, no click, 0 minutes, one report) were in Agora and the hourly peaks
function sandboxAndReal() {
  const DB = fakeD1();
  const live1 = DB.raw.prepare("INSERT INTO live (id, ts, app, mode, link, screen, cs2, prot, tray, dev) VALUES (?, ?, '0.1.0-beta.11', 'game', 'none', 'connection', 0, 0, 0, 0)");
  const ping = DB.raw.prepare("INSERT INTO pings (day, ts, id, app, minutes, dev) VALUES (?, ?, ?, '0.1.0-beta.11', ?, 0)");
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)');
  // 1: a sandbox. 2: a new person, 30 s in (0 minutes yet, 2 Steam accounts). 3: someone with a click only. 4: an older install with minutes.
  for (const id of [1, 2, 3, 4]) { live1.run(ID(id), NOW - 60_000); ping.run(today, NOW - 90_000, ID(id), id === 4 ? 35 : 0); }
  for (const [id, name, n] of [[1, 'accounts', 0], [1, 'first_start', 1], [1, 'chk_firewall', 1], [2, 'accounts', 2], [2, 'first_start', 1], [3, 'accounts', 0]]) cnt.run(today, ID(id), name, n);
  DB.raw.prepare("INSERT INTO clicks (day, id, name, n, dev) VALUES (?, ?, 'NavRegions', 1, 0)").run(today, ID(3));
  return DB;
}

test('10 nov. (P3): Agora leaves out an install that is not real (no account, no click, no minute), as the reports do', async () => {
  const l = await live(sandboxAndReal(), { dev: false, now: NOW });
  assert.equal(l.now, 3, 'the new person with accounts, the one with a click and the one with minutes; not the sandbox');
});

test('10 nov. (P3): the hourly peak (cron) leaves the sandbox out too', async () => {
  const DB = sandboxAndReal();
  await sampleLive(DB, { now: NOW });
  assert.equal(DB.raw.prepare('SELECT n FROM live_hourly WHERE dev = 0').get().n, 3);
});

test('10 nov. (P8): a match on the home connection (m_home) is taken, kept apart, and left out of the lag rate', async () => {
  assert.ok(counterOk('m_home'), 'the Worker takes m_home');
  assert.ok(!counterOk('m_house'), 'and still nothing else in the m_ family');
  const NOW4 = Date.parse('2026-11-04T15:30:00Z');
  // 4 matches: 2 on the home connection, 2 on the phone with 1 lagged
  const DB = seed([[1, 1, 'none', { accounts: 2, m_valve: 2, m_home: 2 }], [1, 2, 'usb', { accounts: 2, m_comm: 2, m_comm_lag: 1 }]], { now: NOW4 });
  const s = await summary(DB, { period: 7, f: F0, dev: false, now: NOW4 });
  assert.deepEqual([s.cs2.matches, s.cs2.home, s.cs2.laggy], [4, 2, 1]);
  const b = await readBrief({ DB }, { now: NOW4, fetchFn: async () => new Response('{}', { status: 503 }) });
  assert.deepEqual([b.quality.laggy_match_rate_d7.num, b.quality.laggy_match_rate_d7.den, b.quality.laggy_match_rate_d7.value], [1, 2, 0.5], '1 lagged of the 2 on the phone, not of all 4');
});
