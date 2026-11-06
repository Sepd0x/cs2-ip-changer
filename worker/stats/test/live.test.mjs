// Beta.12 (6 nov. 2026, publish/untangle-2026-11-06): /api/live only reads "nobody" when the live table isn't there yet.
// Any other error goes up, so the page says it couldn't read instead of showing 0 people now.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { live } from '../src/api.js';

// The live table fails with this message; the hourly peaks read fine (empty), so only the live read decides.
const failing = (message) => ({
  prepare: (sql) => ({
    bind: () => sql.includes('live_hourly')
      ? { all: async () => ({ results: [] }) }
      : { all: async () => { throw new Error(message); }, first: async () => { throw new Error(message); } },
  }),
});

test('live: a database without the live tables yet is nobody now', async () => {
  const r = await live(failing('D1_ERROR: no such table: live'), { dev: false, now: Date.UTC(2026, 10, 6, 12) });
  assert.equal(r.now, 0);
  assert.equal(r.lastTs, null);
});

test('live: any other error is not swallowed as 0 now', async () => {
  await assert.rejects(live(failing('D1_ERROR: network connection lost'), { dev: false, now: Date.UTC(2026, 10, 6, 12) }), /network connection lost/);
});
