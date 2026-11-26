import { test } from 'node:test';
import assert from 'node:assert/strict';
const R = new URL('../', import.meta.url).href;
const { fakeD1 } = await import(R + 'test/d1.mjs');
const ingest = (await import(R + 'src/index.js')).default;
const { summary } = await import(R + 'src/api.js');
const F0 = { version: 'all', country: 'all', mode: 'all', link: 'all' };
const req = (path, body) => new Request('https://stats.example.workers.dev' + path, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });

test('Auditoria 26 nov. W1: a click named toString in one forged report does not break summary()', async () => {
  const DB = fakeD1();
  const r = await ingest.fetch(req('/v1', { v: 1, id: 'f'.repeat(32), app: '1.0.0', minutes: 5, clicks: { toString: 1 } }), { DB });
  assert.equal(r.status, 204);
  await assert.doesNotReject(() => summary(DB, { period: 7, f: F0, dev: false, now: Date.now() }));
  assert.equal(Object.prototype.n, undefined, 'Object.prototype untouched');
});
test('Auditoria 26 nov. W1: __proto__ as a click name', async () => {
  const DB = fakeD1();
  const r = await ingest.fetch(req('/v1', { v: 1, id: 'e'.repeat(32), app: '1.0.0', minutes: 5, clicks: JSON.parse('{"__proto__":1}') }), { DB });
  assert.equal(r.status, 204);
  let err = null; try { await summary(DB, { period: 7, f: F0, dev: false, now: Date.now() }); } catch (e) { err = e; }
  console.log('proto err:', err && err.message, 'Object.prototype.n =', Object.prototype.n, 'days=', Object.prototype.days);
  assert.equal(err, null);
});
