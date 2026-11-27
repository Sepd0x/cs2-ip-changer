// 27 Nov. 2026 (second Codex pass, B1): a daily roll-up (src/roll.js) that read a day before Delete my stats (src/index.js forget)
// must not write that install's row back after the delete said it was done. Two normal operations on SQLite in memory: the delete
// lands after the roll-up read the day and right before it writes its first slice.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeD1 } from './d1.mjs';
import ingest from '../src/index.js';
import { rollDay } from '../src/roll.js';

const id = '0'.repeat(31) + '1';
const day = '2026-01-12';

function erase(db) {
  const request = new Request('https://example.invalid/v1/forget', {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ v: 1, id, app: '1.0.0' }),
  });
  return ingest.fetch(request, { DB: db });
}

test('B1: a roll-up under way respects Delete my stats', async () => {
  const db = fakeD1();
  db.raw.prepare('INSERT INTO pings(day, ts, id, app, minutes) VALUES (?, ?, ?, ?, ?)').run(day, Date.parse(day), id, '1.0.0', 5);
  let erased = false;
  const dbForRoll = {
    ...db,
    async batch(statements) {
      if (!erased) { assert.equal((await erase(db)).status, 204); erased = true; }
      return db.batch(statements);
    },
  };
  await rollDay(dbForRoll, day);
  assert.ok(erased);
  assert.equal(db.raw.prepare('SELECT COUNT(*) AS n FROM pings WHERE id = ?').get(id).n, 0);
  assert.equal((await erase(db)).status, 204);
  assert.equal(db.raw.prepare('SELECT COUNT(*) AS n FROM iday WHERE id = ?').get(id).n, 0,
    'a roll-up under way put the install row back after the delete was confirmed');
});

test('B1: a summary row left with no reports behind it goes when Delete my stats is asked again', async () => {
  const db = fakeD1();
  db.raw.prepare('INSERT INTO pings(day, ts, id, app, minutes) VALUES (?, ?, ?, ?, ?)').run(day, Date.parse(day), id, '1.0.0', 5);
  await rollDay(db, day);
  db.raw.prepare('DELETE FROM pings WHERE id = ?').run(id);   // what the old order left behind
  assert.equal(db.raw.prepare('SELECT COUNT(*) AS n FROM iday WHERE id = ?').get(id).n, 1);
  assert.equal((await erase(db)).status, 204);
  assert.equal(db.raw.prepare('SELECT COUNT(*) AS n FROM iday WHERE id = ?').get(id).n, 0);
});

test('B1: the roll-up still writes the rows of installs nobody deleted', async () => {
  const db = fakeD1();
  db.raw.prepare('INSERT INTO pings(day, ts, id, app, minutes) VALUES (?, ?, ?, ?, ?)').run(day, Date.parse(day), id, '1.0.0', 5);
  assert.equal(await rollDay(db, day), 1);
  assert.equal(db.raw.prepare('SELECT COUNT(*) AS n FROM iday WHERE id = ?').get(id).n, 1);
});
