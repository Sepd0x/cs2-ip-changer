// Security audit of 6 Nov. 2026 (publish/seguranca-2026-11-06/RELATORIO.md, W1, W2, W3, W5): per-IP limits, the share of the day for
// installs new today, and the caps on what one report may add. No network.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fakeD1 } from './d1.mjs';
import ingest, { ipAllowed, DAILY_ROWS, NEW_ROWS, MAX_COUNT, MAX_HEAT, SITE_ORIGIN } from '../src/index.js';

const ID = (n) => n.toString(16).padStart(32, '0');
const today = () => new Date().toISOString().slice(0, 10);
const report = (over = {}) => ({ v: 1, id: ID(1), app: '0.1.0-beta.11', os: '10.0.26300.0', mode: 'cs2open', link: 'usb', minutes: 12, dev: false, clicks: { ChangeIp_Click: 3 }, counts: { ip_changed: 2 }, errors: [], ...over });
const req = (path, body, { ip = '203.0.113.50', type = 'application/json', headers = {} } = {}) => {
  const r = new Request('https://stats.example.workers.dev' + path, { method: 'POST', headers: { 'content-type': type, 'cf-connecting-ip': ip, ...headers }, body: typeof body === 'string' ? body : JSON.stringify(body) });
  Object.defineProperty(r, 'cf', { value: { country: 'PT', asn: 3243 } });
  return r;
};
const budgetOf = (DB, k) => DB.raw.prepare('SELECT rows FROM budget WHERE day = ?').get(k)?.rows ?? 0;

/** Cloudflare's Rate Limiting binding as the Worker sees it: limit({ key }) -> { success }. Records the keys it was given. */
function fakeLimiter(max) {
  const seen = new Map();
  const keys = [];
  return { keys, async limit({ key }) { keys.push(key); const n = (seen.get(key) || 0) + 1; seen.set(key, n); return { success: n <= max }; } };
}

test('W1: past the per-IP limit the app routes answer 429, and other IPs still get through', async () => {
  const DB = fakeD1();
  const RL_APP = fakeLimiter(2);
  const env = { DB, RL_APP };
  assert.equal((await ingest.fetch(req('/v1', report({ id: ID(1) })), env)).status, 204);
  assert.equal((await ingest.fetch(req('/v1', report({ id: ID(2) })), env)).status, 204);
  const third = await ingest.fetch(req('/v1', report({ id: ID(3) })), env);
  assert.equal(third.status, 429);
  assert.equal(third.headers.get('retry-after'), '60');
  for (const path of ['/v1/live', '/v1/env', '/v1/forget']) assert.equal((await ingest.fetch(req(path, report({ id: ID(4) })), env)).status, 429, path);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings').get().n, 2, 'a refused report writes nothing');
  assert.equal((await ingest.fetch(req('/v1', report({ id: ID(5) }), { ip: '198.51.100.7' }), env)).status, 204);
});

test('W1: the IP never reaches the limiter, the database or the response; only a hash of it is the key', async () => {
  const DB = fakeD1();
  const RL_APP = fakeLimiter(100);
  await ingest.fetch(req('/v1', report(), { ip: '203.0.113.77' }), { DB, RL_APP });
  assert.equal(RL_APP.keys.length, 1);
  assert.match(RL_APP.keys[0], /^[0-9a-f]{32}$/);
  assert.ok(!RL_APP.keys[0].includes('203') && !RL_APP.keys[0].includes('113'));
  const dump = JSON.stringify(['pings', 'clicks', 'counts', 'budget'].map((t) => DB.raw.prepare(`SELECT * FROM ${t}`).all()));
  assert.ok(!dump.includes('203.0.113.77') && !dump.includes(RL_APP.keys[0]));
});

test('W1: without the binding, or when it fails, nothing is refused (fail open)', async () => {
  const r = req('/v1', report());
  assert.equal(await ipAllowed(r, undefined), true);
  assert.equal(await ipAllowed(r, { limit: async () => { throw new Error('down'); } }), true);
  assert.equal(await ipAllowed(new Request('https://x/v1', { method: 'POST' }), fakeLimiter(0)), true, 'no cf-connecting-ip (local dev)');
  assert.equal(await ipAllowed(r, fakeLimiter(0)), false);
});

test('W3: the website counter has its own per-IP limit, with the site CORS headers on the 429', async () => {
  const DB = fakeD1();
  const RL_SITE = fakeLimiter(1);
  const hit = () => {
    const r = new Request('https://stats.example.workers.dev/v1/site', { method: 'POST', headers: { origin: SITE_ORIGIN, 'content-type': 'text/plain;charset=UTF-8', 'cf-connecting-ip': '203.0.113.9' }, body: JSON.stringify({ v: 1, p: '/', r: '', e: 'view' }) });
    Object.defineProperty(r, 'cf', { value: { country: 'PT', asn: 3243 } });
    return r;
  };
  assert.equal((await ingest.fetch(hit(), { DB, RL_SITE })).status, 204);
  const second = await ingest.fetch(hit(), { DB, RL_SITE });
  assert.equal(second.status, 429);
  assert.equal(second.headers.get('access-control-allow-origin'), SITE_ORIGIN);
});

test('W1: installs new today stop at NEW_ROWS; an install seen before today still reports', async () => {
  const DB = fakeD1();
  const yesterday = new Date(Date.now() - 86400000).toISOString().slice(0, 10);
  DB.raw.prepare("INSERT INTO pings (day, ts, id, app, minutes, dev) VALUES (?, ?, ?, '0.1.0-beta.10', 5, 0)").run(yesterday, Date.now() - 86400000, ID(9));
  assert.equal((await ingest.fetch(req('/v1', report({ id: ID(1) })), { DB })).status, 204);
  assert.ok(budgetOf(DB, `${today()}:new`) > 0, 'a new install counts against the new share');
  DB.raw.prepare('UPDATE budget SET rows = ? WHERE day = ?').run(NEW_ROWS, `${today()}:new`);
  assert.equal((await ingest.fetch(req('/v1', report({ id: ID(2) })), { DB })).status, 429, 'the new share is used up');
  const before = budgetOf(DB, today());
  assert.equal((await ingest.fetch(req('/v1', report({ id: ID(9) })), { DB })).status, 204, 'the install from yesterday still gets in');
  assert.ok(budgetOf(DB, today()) > before && budgetOf(DB, `${today()}:new`) === NEW_ROWS);
  assert.ok(NEW_ROWS < DAILY_ROWS, 'returning installs always keep a part of the day');
});

test('W1: the database trigger holds NEW_ROWS too (two requests reading the same number)', async () => {
  const DB = fakeD1();
  await ingest.fetch(req('/v1', report({ id: ID(1) })), { DB });
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'budget_new_%'").get().n, 2);
  assert.throws(() => DB.raw.prepare('UPDATE budget SET rows = ? WHERE day = ?').run(NEW_ROWS + 1, `${today()}:new`), /over budget/);
});

test('W2: one report adds at most MAX_COUNT to a counter, button or error, and MAX_HEAT to a heat cell', async () => {
  const DB = fakeD1();
  const body = report({ clicks: { ChangeIp_Click: 99999 }, counts: { ip_changed: 999999, servers: 4 }, errors: [{ type: 'IOException', at: 'Adb.Run', n: 99999 }],
    heat: [['servers', 'c', '3,4', 9999]] });
  assert.equal((await ingest.fetch(req('/v1', body), { DB })).status, 204);
  assert.equal(DB.raw.prepare("SELECT n FROM clicks WHERE name = 'ChangeIp_Click'").get().n, MAX_COUNT);
  assert.equal(DB.raw.prepare("SELECT n FROM counts WHERE name = 'ip_changed'").get().n, MAX_COUNT);
  assert.equal(DB.raw.prepare("SELECT n FROM counts WHERE name = 'servers'").get().n, 4, 'real values stay as they are');
  assert.equal(DB.raw.prepare('SELECT n FROM errors').get().n, MAX_COUNT);
  assert.equal(DB.raw.prepare('SELECT n FROM heat').get().n, MAX_HEAT);
});
