import { test } from 'node:test';
import assert from 'node:assert/strict';
const R = new URL('../', import.meta.url).href;
const { fakeD1 } = await import(R + 'test/d1.mjs');
const { default: ingest, MAX_COUNT } = await import(R + 'src/index.js');
const req = (b) => new Request('https://s.example.workers.dev/v1', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(b) });
test('Auditoria 26 nov. W4: a move between screens is capped at MAX_COUNT like every other counter', async () => {
  const DB = fakeD1();
  assert.equal((await ingest.fetch(req({ v: 1, id: 'a'.repeat(32), app: '1.0.0', paths: { 'servers>regions': 10000 } }), { DB })).status, 204);
  const n = DB.raw.prepare("SELECT n FROM counts WHERE name = 'path_servers_regions'").get().n;
  assert.ok(n <= MAX_COUNT, `kept ${n}, cap ${MAX_COUNT}`);
});
