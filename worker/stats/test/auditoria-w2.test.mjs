import { test } from 'node:test';
import assert from 'node:assert/strict';
const R = new URL('../', import.meta.url).href;
const { fakeD1 } = await import(R + 'test/d1.mjs');
const { default: ingest, DAILY_ROWS, NEW_ROWS } = await import(R + 'src/index.js');
const ID = (n) => n.toString(16).padStart(32, '0');
const today = () => new Date().toISOString().slice(0, 10);
const req = (path, body) => new Request('https://stats.example.workers.dev' + path, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
const budgetOf = (DB, k) => DB.raw.prepare('SELECT rows FROM budget WHERE day = ?').get(k)?.rows ?? 0;

test('Auditoria 26 nov. W2: /v1/env (no ID) cannot take the rows NEW_ROWS keeps for the installs already here', async () => {
  const DB = fakeD1();
  // an install that reported yesterday
  const y = new Date(Date.now() - 864e5).toISOString().slice(0, 10);
  DB.raw.prepare('INSERT INTO pings (day, ts, id, app, dev) VALUES (?, ?, ?, ?, 0)').run(y, Date.now() - 864e5, ID(1), '1.0.0');
  const env = { v: 1, app: '1.0.0', lang: 'pt', win: 'm', scale: '100', build: 26100, brand: 'samsung', android: '14', net: '5g' };
  let n = 0;
  while ((await ingest.fetch(req('/v1/env', env), { DB })).status === 204) n++;
  console.log('env requests accepted', n, 'day budget', budgetOf(DB, today()), 'new budget', budgetOf(DB, today() + ':new'));
  const r = await ingest.fetch(req('/v1', { v: 1, id: ID(1), app: '1.0.0', minutes: 30, counts: { app_start: 1 } }), { DB });
  assert.equal(r.status, 204, `a known install gets ${r.status} after ${n} env reports; ${DAILY_ROWS - NEW_ROWS} rows were meant to stay for it`);
});

// The third path (IDs seeded the day before) stays as it is on purpose: see the comment by seen in src/index.js.
