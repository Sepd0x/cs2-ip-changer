import { test } from 'node:test';
import assert from 'node:assert/strict';
const R = new URL('../', import.meta.url).href;
const { fakeD1 } = await import(R + 'test/d1.mjs');
const { livePeaks } = await import(R + 'src/api.js');
const H = (iso) => Math.floor(Date.parse(iso) / 3600_000);
const seed = (DB, rows) => { DB.raw.exec('CREATE TABLE IF NOT EXISTS live_hourly (hour INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, n INTEGER NOT NULL, game INTEGER NOT NULL, prot INTEGER NOT NULL, PRIMARY KEY (hour, dev))');
  for (const [h, n] of rows) DB.raw.prepare('INSERT INTO live_hourly (hour, dev, n, game, prot) VALUES (?, 0, ?, 0, 0)').run(h, n); };

test('Auditoria 26 nov. W3: just after the spring change, "yesterday" in Lisbon is the Sunday, not the Saturday', async () => {
  const DB = fakeD1();
  // Sunday 28 Mar. 2027 (Lisbon, the 23-hour day) had a peak of 7 at 12:00 Lisbon (11:00 UTC); Saturday had 3
  seed(DB, [[H('2027-03-28T11:00:00Z'), 7], [H('2027-03-27T12:00:00Z'), 3]]);
  const now = Date.parse('2027-03-28T23:30:00Z');   // Monday 29 Mar. 00:30 in Lisbon
  const p = await livePeaks(DB, { dev: false, now });
  assert.equal(p.ydayMax && p.ydayMax.n, 7, `ydayMax ${JSON.stringify(p.ydayMax)}`);
  assert.deepEqual(p.days.slice(-2).map((d) => d.date), ['2027-03-28', '2027-03-29'], JSON.stringify(p.days.map((d) => d.date)));
});

test('Auditoria 26 nov. W3: in the last hour of the autumn change day, yesterday is not today', async () => {
  const DB = fakeD1();
  seed(DB, [[H('2026-10-25T20:00:00Z'), 5], [H('2026-10-24T20:00:00Z'), 2]]);
  const now = Date.parse('2026-10-25T23:30:00Z');   // Sunday 25 Oct. 23:30 in Lisbon (UTC+0 again)
  const p = await livePeaks(DB, { dev: false, now });
  assert.equal(p.ydayMax && p.ydayMax.n, 2, `ydayMax ${JSON.stringify(p.ydayMax)} (today's peak is 5)`);
});
