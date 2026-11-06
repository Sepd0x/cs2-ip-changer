// Tests of both Workers, no network: node --test (from worker/stats)
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { fakeD1 } from './d1.mjs';
import ingest, { readCapped, BUDGET_TRIGGERS, DAILY_ROWS, LIVE_ROWS, FORGET_ROWS, SITE_ROWS, SITE_ORIGIN, SITE_PAGES } from '../src/index.js';
import { summary, live as liveNow, who, MAX_ROWS, livePeaks, lisbonHour } from '../src/api.js';
import dashboard, { collect, page, github, excludeIds, resetGithubCache, compareVersions, readable, sampleLive, prune, snapshotGithub, githubHistory, RETAIN_DAYS, DAILY_CRON, assetsVersion, resetBriefCache, trafficRows } from '../src/dashboard.js';
import { renderMd } from '../src/brief.js';
import { verifyAccess, resetAccessCache } from '../src/access.js';
import { ASSETS } from '../src/assets.js';
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { readTheme, tokens, apply, FILES, WEB, BLOCK, ROOT } from '../../../tools/design-tokens.mjs';

const ID = (n) => n.toString(16).padStart(32, '0');
const report = (over = {}) => ({ v: 1, id: ID(1), app: '0.1.0-beta.8', os: '10.0.26300.0', mode: 'cs2open', link: 'usb', minutes: 12, dev: false, clicks: { ChangeIp_Click: 3 }, counts: { ip_changed: 2, servers: 6 }, errors: [], ...over });
const post = (body, { type = 'application/json', headers = {}, cf = { country: 'PT' } } = {}) => {
  const r = new Request('https://stats.example.workers.dev/v1', { method: 'POST', headers: { 'content-type': type, ...headers }, body: typeof body === 'string' ? body : JSON.stringify(body) });
  Object.defineProperty(r, 'cf', { value: cf });
  return r;
};

// ------------------------------------------------------------------ the stats Worker (/v1)

test('ingest: a good report is kept, with the country and no IP anywhere', async () => {
  const DB = fakeD1();
  const res = await ingest.fetch(post(report(), { headers: { 'cf-connecting-ip': '203.0.113.77', 'x-forwarded-for': '203.0.113.77' } }), { DB });
  assert.equal(res.status, 204);
  const row = DB.raw.prepare('SELECT * FROM pings').get();
  assert.equal(row.country, 'PT');
  assert.equal(row.minutes, 12);
  const cols = DB.raw.prepare('PRAGMA table_info(pings)').all().map((c) => c.name);
  assert.ok(!cols.some((c) => /ip|addr/i.test(c)), 'no IP column');
  const dump = JSON.stringify(['pings', 'clicks', 'counts', 'errors', 'budget'].map((t) => DB.raw.prepare(`SELECT * FROM ${t}`).all()));
  assert.ok(!dump.includes('203.0.113.77'), 'the IP is not stored');
});

test('ingest: only JSON (a web page cannot post it without a preflight)', async () => {
  const res = await ingest.fetch(post(report(), { type: 'text/plain' }), { DB: fakeD1() });
  assert.equal(res.status, 415);
});

test('ingest: a body over 16 KB without Content-Length is cut off (413)', async () => {
  const big = new ReadableStream({ start(c) { for (let i = 0; i < 40; i++) c.enqueue(new Uint8Array(1024).fill(32)); c.close(); } });
  const r = new Request('https://x/v1', { method: 'POST', headers: { 'content-type': 'application/json' }, body: big, duplex: 'half' });
  assert.equal((await ingest.fetch(r, { DB: fakeD1() })).status, 413);
  assert.equal(await readCapped(new Request('https://x', { method: 'POST', body: 'abc' }), 2), null);
  assert.equal(await readCapped(new Request('https://x', { method: 'POST', body: 'abc' }), 3), 'abc');
});

test('ingest: junk is refused and writes nothing', async () => {
  const DB = fakeD1();
  for (const body of ['nope', '[]', 'null', '123', JSON.stringify({ ...report(), v: 2 }), JSON.stringify({ ...report(), id: 'x' }), JSON.stringify({ ...report(), app: '1.0<script>' })])
    assert.equal((await ingest.fetch(post(body), { DB })).status, 400, body.slice(0, 30));
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings').get().n, 0);
});

test('ingest: bad fields are dropped, not stored (arrays as maps, odd names, minutes over a day)', async () => {
  const DB = fakeD1();
  const res = await ingest.fetch(post(report({ minutes: 5000, mode: 'DROP TABLE', clicks: ['a', 'b'], counts: { 'bad name': 1, ok_one: 2, neg: -1 } })), { DB });
  assert.equal(res.status, 204);
  const row = DB.raw.prepare('SELECT * FROM pings').get();
  assert.equal(row.minutes, null);
  assert.equal(row.mode, null);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM clicks').get().n, 0);
  assert.deepEqual(DB.raw.prepare('SELECT name FROM counts').all().map((r) => r.name), ['ok_one']);
});

test('ingest: one report per ID every 10 minutes', async () => {
  const DB = fakeD1();
  assert.equal((await ingest.fetch(post(report()), { DB })).status, 204);
  assert.equal((await ingest.fetch(post(report()), { DB })).status, 429);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings').get().n, 1);
});

test('ingest: a flood of fresh IDs stops at the daily budget and writes nothing more', async () => {
  const DB = fakeD1();
  const day = new Date().toISOString().slice(0, 10);
  DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, ?)').run(day, 39990);
  let refused = 0;
  for (let i = 0; i < 50; i++) if ((await ingest.fetch(post(report({ id: ID(1000 + i) })), { DB })).status === 429) refused++;
  assert.ok(refused >= 49, `refused ${refused}`);
  assert.ok(DB.raw.prepare('SELECT rows FROM budget').get().rows <= 40000);
});

test('ingest: anything but POST /v1 is 404, also /dashboard (the page is its own Worker)', async () => {
  for (const r of [new Request('https://x/v1'), new Request('https://x/dashboard'), new Request('https://x/', { method: 'POST' })])
    assert.equal((await ingest.fetch(r, { DB: fakeD1() })).status, 404);
});

test('ingest beta.9: heat map added up without the ID, junk cells dropped', async () => {
  const DB = fakeD1();
  const heat = [['connection', 'c', '3,5', 4], ['connection', 'd', '23,15', 1], ['servers', 'r', '10,2', 2],
    ['connection', 'c', '24,0', 1], ['connection', 'x', '1,1', 1], ['Bad', 'c', '1,1', 1], ['connection', 'c', '1,1', 0], ['connection', 'c', '1,1']];
  assert.equal((await ingest.fetch(post(report({ heat })), { DB })).status, 204);
  assert.equal((await ingest.fetch(post(report({ id: ID(2), heat: [['connection', 'c', '3,5', 6]] })), { DB })).status, 204);
  const rows = DB.raw.prepare('SELECT screen, kind, cell, n FROM heat ORDER BY screen, kind, cell').all().map((r) => `${r.screen}|${r.kind}|${r.cell}|${r.n}`);
  assert.deepEqual(rows, ['connection|c|3,5|10', 'connection|d|23,15|1', 'servers|r|10,2|2']);
  const cols = DB.raw.prepare('PRAGMA table_info(heat)').all().map((c) => c.name);
  assert.ok(!cols.includes('id'), 'no install ID in the heat map');
});

test('ingest beta.9: screens, paths and age go in as counters; odd ones dropped', async () => {
  const DB = fakeD1();
  const res = await ingest.fetch(post(report({ screens: { connection: 30, servers: 5, Bad: 3, log: 2000 }, paths: { 'connection>servers': 3, 'servers>servers': 1, 'a>b>c': 1, 'connection>Log': 1 }, age: '7-13' })), { DB });
  assert.equal(res.status, 204);
  const names = DB.raw.prepare("SELECT name, n FROM counts WHERE name LIKE 'scr_%' OR name LIKE 'path_%' OR name LIKE 'age_%' ORDER BY name").all().map((r) => `${r.name}=${r.n}`);
  assert.deepEqual(names, ['age_7_13=1', 'path_connection_servers=3', 'scr_connection=30', 'scr_servers=5']);
  assert.equal((await ingest.fetch(post(report({ id: ID(3), age: '8' })), { DB })).status, 204);
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM counts WHERE id = ? AND name LIKE 'age_%'").get(ID(3)).n, 0);
});

test('ingest beta.9: a hosting network is marked and its clicks stay out of the heat map', async () => {
  const DB = fakeD1();
  await ingest.fetch(post(report({ heat: [['connection', 'c', '1,1', 3]] }), { cf: { country: 'US', asn: 16509 } }), { DB });
  await ingest.fetch(post(report({ id: ID(2) }), { cf: { country: 'PT', asn: 2860 } }), { DB });
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM counts WHERE name = 'net_dc'").get().n, 1);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM heat').get().n, 0);
});

test('ingest beta.9: the live signal keeps one row per ID, at most every 4 minutes', async () => {
  const DB = fakeD1();
  const livePost = (b) => { const r = post(b); return new Request('https://stats.example.workers.dev/v1/live', r); };
  const sig = { v: 1, id: ID(1), app: '0.1.0-beta.9', mode: 'cs2open', link: 'usb', screen: 'connection', cs2: true, prot: true, tray: false, dev: false };
  assert.equal((await ingest.fetch(livePost(sig), { DB })).status, 204);
  assert.equal((await ingest.fetch(livePost({ ...sig, screen: 'servers' }), { DB })).status, 429);
  DB.raw.prepare('UPDATE live SET ts = ts - 300000').run();
  assert.equal((await ingest.fetch(livePost({ ...sig, screen: 'servers', ip: '203.0.113.7' }), { DB })).status, 204);
  const rows = DB.raw.prepare('SELECT * FROM live').all();
  assert.equal(rows.length, 1);
  assert.equal(rows[0].screen, 'servers');
  assert.ok(!JSON.stringify(rows).includes('203.0.113.7'));
  assert.equal((await ingest.fetch(livePost({ ...sig, id: 'nope' }), { DB })).status, 400);
});

test('ingest beta.9 part 2: band counters only inside their family, reserved names refused, days active and settings by name', async () => {
  const DB = fakeD1();
  const counts = { m_comm: 1, m_comm_lag: 1, ping_50_80: 1, ping_63: 1, jit_lt5: 1, late_gt10: 1, loss_0: 1, sess_1_2h: 1, sess_97: 1, st_1_2: 1, tp_10_30: 1,
    start_auto: 1, app_start: 1, crash: 1, unclean_exit: 1, upd_accepted: 1, upd_hacked: 1, scl_servers_75: 2, scl_servers_60: 1, scl_Bad_25: 1,
    scr_connection: 999, age_30p: 1, net_dc: 1, set_AsAdmin: 1, act_7: 1, phone_setup_tetheroff: 1 };
  const res = await ingest.fetch(post(report({ counts, active7: 3, settings: ['CloseToTray', 'Mode', 'Mode', 'Cs2Path', 42] })), { DB });
  assert.equal(res.status, 204);
  const names = DB.raw.prepare('SELECT name FROM counts ORDER BY name').all().map((r) => r.name);
  assert.deepEqual(names, ['act_3', 'app_start', 'crash', 'jit_lt5', 'late_gt10', 'loss_0', 'm_comm', 'm_comm_lag', 'phone_setup_tetheroff', 'ping_50_80',
    'scl_servers_75', 'sess_1_2h', 'set_CloseToTray', 'set_Mode', 'st_1_2', 'start_auto', 'tp_10_30', 'unclean_exit', 'upd_accepted']);
  await ingest.fetch(post(report({ id: ID(4), active7: 9, settings: 'Mode' })), { DB });
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM counts WHERE id = ? AND (name LIKE 'act_%' OR name LIKE 'set_%')").get(ID(4)).n, 0);
});

test('ingest beta.9 part 2: at most 100 counters a report', async () => {
  const DB = fakeD1();
  const counts = Object.fromEntries([...Array(150)].map((_, i) => [`c${i}`, 1]));
  await ingest.fetch(post(report({ counts })), { DB });
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM counts').get().n, 100);
});

test('ingest beta.9 part 2: the environment report has no ID, keeps only counts per day, drops odd values', async () => {
  const DB = fakeD1();
  const envPost = (b, type = 'application/json') => { const r = post(b, { type, cf: { country: 'PT' } }); return new Request('https://stats.example.workers.dev/v1/env', r); };
  const e = { v: 1, app: '0.1.0-beta.9', lang: 'pt', win: 'm', scale: '125', build: 26100, brand: 'samsung', android: '15', net: '5g', dev: false };
  assert.equal((await ingest.fetch(envPost(e), { DB })).status, 204);
  assert.equal((await ingest.fetch(envPost({ ...e, id: ID(9), lang: 'Português', scale: '133', build: 26100.5, brand: 'SM-S918B', android: '15.1', net: 'wifi', model: 'SM-S918B' }), { DB })).status, 204);
  const rows = DB.raw.prepare('SELECT k, v, n FROM env ORDER BY k, v').all().map((r) => `${r.k}=${r.v}:${r.n}`);
  assert.deepEqual(rows, ['android=15:1', 'brand=samsung:1', 'build=26100:1', 'lang=pt:1', 'net=5g:1', 'reports=all:2', 'scale=125:1', 'win=m:2']);
  const cols = DB.raw.prepare('PRAGMA table_info(env)').all().map((c) => c.name);
  assert.deepEqual(cols, ['day', 'k', 'v', 'n', 'dev'], 'no id, no country');
  assert.ok(!JSON.stringify(DB.raw.prepare('SELECT * FROM env').all()).includes('SM-S918B'));
  assert.equal((await ingest.fetch(envPost(e, 'text/plain'), { DB })).status, 415);
  assert.equal((await ingest.fetch(envPost({ ...e, v: 2 }), { DB })).status, 400);
  assert.equal((await ingest.fetch(envPost('x'.repeat(3000)), { DB })).status, 413);
});

test('ingest beta.9 part 2: the environment report stops at the daily budget', async () => {
  const DB = fakeD1();
  DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, ?)').run(new Date().toISOString().slice(0, 10), 39999);
  const r = new Request('https://stats.example.workers.dev/v1/env', post({ v: 1, app: '0.1.0-beta.9', lang: 'pt' }));
  assert.equal((await ingest.fetch(r, { DB })).status, 429);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM env').get().n, 0);
});

// ------------------------------------------------------------------ Access

const TEAM = 'https://example-team.cloudflareaccess.com';
const AUD = 'a'.repeat(64);
const EMAIL = 'owner@example.com';
const env0 = () => ({ TEAM_DOMAIN: TEAM, POLICY_AUD: AUD, DASHBOARD_EMAIL: EMAIL });
const b64 = (bytes) => Buffer.from(bytes).toString('base64url');
const keyPair = () => crypto.subtle.generateKey({ name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['sign', 'verify']);
const good = await keyPair();
const other = await keyPair();
const jwk = { ...(await crypto.subtle.exportKey('jwk', good.publicKey)), kid: 'k1', alg: 'RS256', use: 'sig' };

async function jwt(payload, { key = good.privateKey, header = { alg: 'RS256', kid: 'k1', typ: 'JWT' } } = {}) {
  const head = b64(JSON.stringify(header)) + '.' + b64(JSON.stringify(payload));
  const sig = await crypto.subtle.sign('RSASSA-PKCS1-v1_5', key, new TextEncoder().encode(head));
  return head + '.' + b64(new Uint8Array(sig));
}
const claims = (over = {}) => { const t = Math.floor(Date.now() / 1000); return { aud: [AUD], iss: TEAM, email: EMAIL, iat: t, nbf: t, exp: t + 3600, sub: 'x', ...over }; };

let certCalls = 0;
const realFetch = globalThis.fetch;
function fakeNet({ github: gh = {} } = {}) {
  globalThis.fetch = async (url) => {
    url = String(url);
    if (url === `${TEAM}/cdn-cgi/access/certs`) { certCalls++; return Response.json({ keys: [jwk], public_cert: {}, public_certs: [] }); }
    if (url.startsWith('https://api.github.com/')) {
      const path = url.replace(/^https:\/\/api\.github\.com\/repos\/[^/]+\/[^/]+/, '');
      if (gh[path] === undefined) return new Response('{}', { status: 403 });
      return Response.json(gh[path]);
    }
    throw new Error('no network in tests: ' + url);
  };
}
beforeEach(() => { resetAccessCache(); resetGithubCache(); certCalls = 0; fakeNet(); });
process.on('exit', () => { globalThis.fetch = realFetch; });

const get = (path, token) => new Request('https://stats-dashboard.example.workers.dev' + path, { headers: token ? { 'cf-access-jwt-assertion': token } : {} });

test('dashboard: without the Access header it is 403', async () => {
  const res = await dashboard.fetch(get('/dashboard'), { ...env0(), DB: fakeD1() });
  assert.equal(res.status, 403);
  assert.ok(!(await res.text()).includes('Install-days'));
});

test('dashboard: an invalid JWT is 403 (garbage, other key, alg none, HS256, wrong aud, iss, email, expired, not yet)', async () => {
  const t = Math.floor(Date.now() / 1000);
  const none = b64(JSON.stringify({ alg: 'none', kid: 'k1' })) + '.' + b64(JSON.stringify(claims())) + '.';
  const hs = b64(JSON.stringify({ alg: 'HS256', kid: 'k1' })) + '.' + b64(JSON.stringify(claims())) + '.' + b64(new Uint8Array(32));
  const tampered = (await jwt(claims())).split('.');
  tampered[1] = b64(JSON.stringify(claims({ email: 'someone@example.net' })));
  const cases = {
    garbage: 'not.a.jwt',
    'two parts': 'abc.def',
    'other key': await jwt(claims(), { key: other.privateKey }),
    'unknown kid': await jwt(claims(), { header: { alg: 'RS256', kid: 'zz' } }),
    'alg none': none,
    HS256: hs,
    'payload changed': tampered.join('.'),
    'wrong aud': await jwt(claims({ aud: ['b'.repeat(64)] })),
    'wrong iss': await jwt(claims({ iss: 'https://evil.cloudflareaccess.com' })),
    'wrong email': await jwt(claims({ email: 'someone@example.net' })),
    expired: await jwt(claims({ exp: t - 3600 })),
    'not yet valid': await jwt(claims({ nbf: t + 3600 })),
    'too long': 'a'.repeat(9000),
  };
  for (const [name, token] of Object.entries(cases)) {
    const res = await dashboard.fetch(get('/dashboard', token), { ...env0(), DB: fakeD1() });
    assert.equal(res.status, 403, name);
  }
});

test('dashboard: a good token but the secrets missing is still 403 (never open by default)', async () => {
  const token = await jwt(claims());
  for (const drop of ['TEAM_DOMAIN', 'POLICY_AUD', 'DASHBOARD_EMAIL']) {
    const env = { ...env0(), DB: fakeD1() };
    delete env[drop];
    assert.equal((await dashboard.fetch(get('/dashboard', token), env)).status, 403, drop);
  }
  assert.equal((await verifyAccess(get('/', token), { ...env0(), TEAM_DOMAIN: 'https://evil.example.com' })).ok, false);
});

test('dashboard: a good token opens it; the keys are fetched once', async () => {
  const token = await jwt(claims({ email: 'Owner@Example.com' }));
  const env = { ...env0(), DB: fakeD1() };
  const res = await dashboard.fetch(get('/dashboard', token), env);
  assert.equal(res.status, 200);
  assert.match(res.headers.get('content-security-policy'), /default-src 'none'/);
  assert.equal(res.headers.get('cache-control'), 'no-store');
  assert.equal((await dashboard.fetch(get('/', token), env)).status, 302);
  assert.equal((await dashboard.fetch(get('/other', token), env)).status, 404);
  assert.equal((await dashboard.fetch(new Request('https://x/dashboard', { method: 'POST', headers: { 'cf-access-jwt-assertion': token } }), env)).status, 405);
  assert.equal(certCalls, 1);
});

// ------------------------------------------------------------------ numbers

function seed(DB, now) {
  const day = (ago) => new Date(now - ago * 864e5).toISOString().slice(0, 10);
  const ping = (d, id, app, mode, minutes, country, dev = 0) =>
    DB.raw.prepare('INSERT INTO pings (day, ts, id, app, os, mode, link, minutes, country, dev) VALUES (?,?,?,?,?,?,?,?,?,?)').run(d, now, id, app, '10', mode, 'usb', minutes, country, dev);
  // today: install A twice (start 0 min, close 30 min), install B once; yesterday: C with an inflated 5000 over two pings
  ping(day(0), ID(0xa), '0.1.0-beta.8', 'cs2open', 0, 'PT');
  ping(day(0), ID(0xa), '0.1.0-beta.8', 'cs2open', 30, 'PT');
  ping(day(0), ID(0xb), '0.1.0-beta.7', 'game', 10, 'US');
  ping(day(1), ID(0xc), '0.1.0-beta.8', 'cs2open', 1000, 'US');
  ping(day(1), ID(0xc), '0.1.0-beta.8', 'cs2open', 1000, 'US');
  // left out: a dev build, an excluded old ID, and a day outside 7 days
  ping(day(0), ID(0xd), '0.1.0-beta.8', 'always', 500, 'PT', 1);
  ping(day(0), ID(0xe), '0.1.0-beta.6', 'always', 500, 'PT');
  ping(day(20), ID(0xf), '0.1.0-beta.5', 'always', 500, 'BR');
  const click = (d, id, name, n, dev = 0) => DB.raw.prepare('INSERT INTO clicks (day, id, name, n, dev) VALUES (?,?,?,?,?)').run(d, id, name, n, dev);
  click(day(0), ID(0xa), 'ChangeIp_Click', 5); click(day(0), ID(0xb), 'ChangeIp_Click', 2); click(day(0), ID(0xb), 'Rejoin_Click', 1); click(day(0), ID(0xd), 'Rejoin_Click', 99, 1);
  const count = (d, id, name, n) => DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?,?,?,?,0)').run(d, id, name, n);
  count(day(0), ID(0xa), 'ip_changed', 4); count(day(0), ID(0xb), 'ip_changed', 1); count(day(0), ID(0xb), 'first_start', 1); count(day(0), ID(0xb), 'first_start', 1); count(day(0), ID(0xa), 'servers', 6); count(day(0), ID(0xb), 'servers', 6); count(day(1), ID(0xc), 'servers', 0);   // first_start twice in a day: still one new user
  DB.raw.prepare('INSERT INTO errors (day, id, app, type, at, n, dev) VALUES (?,?,?,?,?,?,0)').run(day(0), ID(0xa), '0.1.0-beta.8', 'NullReferenceException', 'MainWindow.<b>Query', 3);
}

test('dashboard: the numbers come out right from a test database', async () => {
  const DB = fakeD1();
  const now = Date.UTC(2026, 10, 1, 12);
  seed(DB, now);
  const s = await collect(DB, { days: 7, now, exclude: excludeIds(`${ID(0xe)}, junk`) });
  assert.equal(s.installDays, 3);                      // A and B today, C yesterday
  assert.equal(s.minutes, 30 + 10 + 1440);             // C capped at a day
  assert.equal(s.avgMinutes, Math.round(1480 / 3));
  assert.equal(s.perDay.length, 7);
  assert.deepEqual(s.perDay.at(-1), { day: '2026-11-01', n: 2, minutes: 40, fresh: 1 });
  assert.equal(s.newUsers, 1);                         // first_start: B accepted the notice today
  assert.equal(s.newest, '0.1.0-beta.8');
  assert.equal(s.newestDays, 2);
  assert.deepEqual(s.perDay.at(-2), { day: '2026-10-31', n: 1, minutes: 1440, fresh: 0 });
  assert.equal(s.perDay[0].n, 0);
  assert.deepEqual(s.versions.map((r) => [r.name, r.n]), [['0.1.0-beta.8', 2], ['0.1.0-beta.7', 1]]);
  assert.deepEqual(s.countries.map((r) => [r.name, r.n]), [['US', 2], ['PT', 1]]);
  assert.deepEqual(s.modes.map((r) => [r.name, r.n]), [['cs2open', 2], ['game', 1]]);
  assert.deepEqual(s.clicks.map((r) => [r.name, r.n]), [['ChangeIp_Click', 7], ['Rejoin_Click', 1]]);
  assert.deepEqual(s.counts.map((r) => [r.name, r.n]), [['ip_changed', 5]]);          // list sizes and first_start are not summed as counters
  assert.deepEqual(s.sizes.find((z) => z.name === 'servers').bands, [{ band: '0', days: 1 }, { band: '6-20', days: 2 }]);
  assert.equal(s.errors[0].n, 3);
  const html = page({ days: 7, stats: s, gh: { problems: [] }, now, email: EMAIL });
  assert.ok(html.includes('MainWindow.&lt;b&gt;Query') && !html.includes('<b>Query'), 'text from the database is escaped');
  assert.ok(!/<script/i.test(html), 'no scripts');
});

test('dashboard: GitHub numbers, and a missing permission is said on the page', async () => {
  fakeNet({ github: {
    '': { stargazers_count: 42, forks_count: 3, subscribers_count: 5, open_issues_count: 1 },
    '/traffic/views': { count: 120, uniques: 40, views: [{ timestamp: '2026-10-31T00:00:00Z', count: 70, uniques: 20 }] },
    '/releases?per_page=30': [{ tag_name: 'v0.1.0-beta.7', published_at: '2026-10-31T10:00:00Z', assets: [{ name: 'CS2IPChanger.exe', download_count: 17 }, { name: 'CS2IPChanger.exe.sha256', download_count: 4 }] }],
    '/traffic/popular/referrers': [{ referrer: 'reddit.com', count: 9, uniques: 6 }],
  } });
  const gh = await github({ GITHUB_TOKEN: 'test' }, globalThis.fetch, Date.now());
  assert.equal(gh.stars, 42);
  assert.equal(gh.views.count, 120);
  assert.equal(gh.releases[0].downloads, 21);
  assert.equal(gh.releases[0].exe, 17);
  assert.equal(gh.referrers[0].name, 'reddit.com');
  assert.equal(gh.clones, null);
  assert.ok(gh.problems.some((p) => p.includes('/traffic/clones') && p.includes('403')));
  resetGithubCache();
  assert.ok((await github({}, globalThis.fetch, Date.now())).problems[0].includes('GITHUB_TOKEN'));
  resetGithubCache();
  assert.ok((await github({ GITHUB_TOKEN: ' \n ' }, globalThis.fetch, Date.now())).problems[0].includes('GITHUB_TOKEN'));
});

test('dashboard: a token pasted with a BOM, spaces or a line break still works', async () => {
  let auth;
  globalThis.fetch = async (url, init) => { auth ??= init.headers.authorization; return Response.json({ stargazers_count: 1 }); };   // the first call (with the token); the rest may be the public fallback
  await github({ GITHUB_TOKEN: '\uFEFF github_pat_x \r\n' }, globalThis.fetch, Date.now());
  assert.equal(auth, 'Bearer github_pat_x');
});

test('dashboard: the old page (/classic) with a good token shows the numbers', async () => {
  const DB = fakeD1();
  seed(DB, Date.now());
  const res = await dashboard.fetch(get('/classic?days=7', await jwt(claims())), { ...env0(), DB, EXCLUDE_IDS: ID(0xe) });
  const html = await res.text();
  assert.equal(res.status, 200);
  assert.ok(html.includes('<b>7 days</b>'));
  assert.match(html, /Users per day<\/div><div class="tv">0.4</);   // 3 users in 7 days
  assert.ok(html.includes('>Change IP<') && html.includes('title="ChangeIp_Click"'), 'button names readable, the code name on hover');
  assert.match(html, /New users<\/div><div class="tv">1</);
});

test('dashboard: versions in order, names from the code readable', () => {
  assert.deepEqual(['0.1.0', '0.1.0-beta.10', '0.1.0-alpha.4', '0.1.0-beta.9', '0.1.0-beta.8+dev'].sort(compareVersions),
    ['0.1.0-alpha.4', '0.1.0-beta.8+dev', '0.1.0-beta.9', '0.1.0-beta.10', '0.1.0']);
  assert.equal(readable('ChangeIp_Click'), 'Change IP');
  assert.equal(readable('Tray_open'), 'Tray: open');
  assert.equal(readable('ip_changed'), 'IP changed');
  assert.equal(readable('vpn_seen'), 'VPN seen');
});

// ------------------------------------------------------------------ beta.9: the new dashboard and its API

test('dashboard beta.9: page, scripts and captures are served with a strict CSP, all behind Access', async () => {
  const token = await jwt(claims());
  const env = { ...env0(), DB: fakeD1() };
  for (const [path, type] of [['/dashboard', 'text/html'], ['/v3', 'text/html'], ['/d/app.js', 'text/javascript'], ['/d/real.js', 'text/javascript'], ['/d/style.css', 'text/css'], ['/d/v4/app.js', 'text/javascript'], ['/d/v4/real.js', 'text/javascript'], ['/d/v4/v4.css', 'text/css'], ['/ui/connection.png', 'image/png']]) {
    const res = await dashboard.fetch(get(path, token), env);
    assert.equal(res.status, 200, path);
    assert.ok(res.headers.get('content-type').startsWith(type), path);
    assert.match(res.headers.get('content-security-policy'), /script-src 'self';/);
    assert.equal((await dashboard.fetch(get(path), env)).status, 403, `${path} without Access`);
  }
  assert.equal((await dashboard.fetch(get('/d/../src/index.js', token), env)).status, 404);
  assert.equal((await dashboard.fetch(get('/api/summary'), env)).status, 403);
  assert.equal((await dashboard.fetch(get('/api/live'), env)).status, 403);
  assert.equal((await dashboard.fetch(get('/api/brief'), env)).status, 403);
  assert.equal((await dashboard.fetch(get('/api/brief?format=md'), env)).status, 403);
});

test('dashboard beta.9: summary keeps the scanners apart, no install ID leaves, dev only with ?dev=1', async () => {
  const DB = fakeD1();
  const token = await jwt(claims());
  const env = { ...env0(), DB };
  const at = (id, over, cf) => ingest.fetch(post(report({ id: ID(id), ...over }), { cf }), { DB });
  await at(1, { minutes: 20, clicks: { ChangeIp_Click: 2 }, counts: { accounts: 2, ip_changed: 1 }, screens: { connection: 15 }, paths: { 'connection>servers': 2 }, age: '0', heat: [['connection', 'c', '1,4', 2], ['connection', 'd', '10,10', 1]] }, { country: 'PT' });
  await at(2, { minutes: 0, clicks: {}, counts: { accounts: 0 } }, { country: 'US', asn: 16509 });
  await at(3, { minutes: 0, clicks: {}, counts: { accounts: 0 } }, { country: 'BR' });
  await at(4, { minutes: 9, dev: true, counts: { accounts: 5 } }, { country: 'PT' });
  const s = await (await dashboard.fetch(get('/api/summary?period=7', token), env)).json();
  assert.equal(s.days.at(-1).users, 1);
  assert.equal(s.automated, 2);
  assert.equal(s.automatedDc, 1);
  assert.deepEqual(s.countries, [{ k: 'PT', n: 1 }]);
  assert.equal(s.buttons.ChangeIp_Click.n, 2);
  assert.equal(s.screens.connection.minutes, 15);
  assert.deepEqual(s.paths, [{ from: 'connection', to: 'servers', n: 2 }]);
  assert.deepEqual(s.heat.connection.c, [[1, 4, 2]]);
  assert.deepEqual(s.age, [{ band: '0', n: 1 }]);
  assert.ok(!JSON.stringify(s).includes(ID(1)), 'no install ID in the answer');
  const d = await (await dashboard.fetch(get('/api/summary?period=7&dev=1', token), env)).json();
  assert.equal(d.days.at(-1).users, 1);
  assert.equal(d.automated, 0);
  const odd = await (await dashboard.fetch(get("/api/summary?period=5&version=x'%20OR%201=1", token), env)).json();
  assert.equal(odd.period, 7);
  assert.equal(odd.filters.version, 'all', 'an odd filter falls back to all, never goes into the SQL');
  const other = await (await dashboard.fetch(get('/api/summary?period=7&version=0.1.0-beta.7', token), env)).json();
  assert.equal(other.days.at(-1).users, 0, 'a version filter keeps only that version');
});

test('dashboard beta.9 part 2: matches by link, sessions, crashes, scroll, setup, settings, cohorts and the environment', async () => {
  const DB = fakeD1();
  const token = await jwt(claims());
  const env = { ...env0(), DB };
  const at = (id, over) => ingest.fetch(post(report({ id: ID(id), ...over })), { DB });
  await at(1, { link: 'usb', counts: { accounts: 2, m_comm: 3, m_comm_lag: 1, m_valve: 1, ping_50_80: 2, ping_lt30: 2, late_gt10: 1, app_start: 2, start_auto: 1, start_hand: 1,
    crash: 1, sess_15_30: 2, st_1_2: 2, tp_10_30: 1, upd_seen: 1, upd_declined: 1, scl_servers_0: 1, scl_servers_75: 1, phone_setup_tetheroff: 1, phone_setup_done: 1 },
    errors: [{ type: 'Fatal_NullReferenceException', at: 'MainWindow.QueryServers', n: 1 }, { type: 'IOException', at: 'Store.Save', n: 2 }], active7: 3, settings: ['CloseToTray'], age: '0' });
  await at(2, { link: 'hotspot', counts: { accounts: 1, m_comm: 2, m_comm_lag: 2, ping_gt120: 2, app_start: 1, phone_setup_nousb: 1 }, age: '1' });
  const envPost = (b) => new Request('https://stats.example.workers.dev/v1/env', post(b));
  await ingest.fetch(envPost({ v: 1, app: '0.1.0-beta.9', lang: 'pt', build: 26100, brand: 'samsung', scale: '125' }), { DB });
  await ingest.fetch(envPost({ v: 1, app: '0.1.0-beta.9', lang: 'pt', build: 12345, dev: true }), { DB });
  const s = await (await dashboard.fetch(get('/api/summary?period=7', token), env)).json();
  assert.equal(s.cs2.matches, 6);
  assert.equal(s.cs2.laggy, 3);
  assert.deepEqual(s.cs2.lag.usb.community, { n: 3, lag: 1 });
  assert.deepEqual(s.cs2.lag.hotspot.community, { n: 2, lag: 2 });
  assert.deepEqual(s.cs2.ping.find((x) => x.band === '>120'), { band: '>120', usb: 0, hotspot: 2 });
  assert.equal(s.days.at(-1).sessions, 3);
  assert.equal(s.days.at(-1).crashes, 1);
  assert.equal(s.days.at(-1).matches, 6);
  assert.deepEqual([s.sessions.auto, s.sessions.hand], [1, 1]);
  assert.equal(s.sessions.len.find((x) => x.band === '15-30').n, 2);
  assert.deepEqual(s.crashPlaces, [{ at: 'MainWindow.QueryServers', type: 'NullReferenceException', app: '0.1.0-beta.8', n: 1 }]);
  assert.ok(s.errors.every((e) => !e.type.startsWith('Fatal_')), 'crashes apart from the errors');
  assert.deepEqual(s.scroll.servers, { visits: 2, reach: [50, 50, 50] });
  assert.equal(s.setupOpened, 2);
  assert.equal(s.setup.find((x) => x.key === 'done').n, 1);
  assert.deepEqual(s.settings, [{ name: 'CloseToTray', n: 1 }]);
  assert.equal(s.active.find((x) => x.band === '3').n, 1);
  assert.deepEqual([s.updates.seen, s.updates.declined], [1, 1]);
  assert.ok(s.cohorts.length >= 1 && s.cohorts.some((c) => c.n === 1));
  assert.equal(s.env.reports, 1, 'dev env reports stay apart');
  assert.deepEqual(s.env.windows, [{ k: 'Windows 11 24H2', n: 1 }]);
  assert.deepEqual(s.env.scale, [{ k: '125%', n: 1 }]);
  assert.equal(s.counters.m_comm, 5, 'band counters still add up');
  const d = await (await dashboard.fetch(get('/api/summary?period=7&dev=1', token), env)).json();
  assert.deepEqual(d.env.windows, [{ k: 'Build 12345', n: 1 }]);
});

test('ingest beta.9: the kept install ID is marked, and Delete my stats removes all its rows', async () => {
  const DB = fakeD1();
  await ingest.fetch(post(report({ idk: 'install', counts: { ip_changed: 1, id_install: 5 }, errors: [{ type: 'X', at: 'A.B', n: 1 }] })), { DB });
  await ingest.fetch(post(report({ id: ID(2) })), { DB });
  assert.deepEqual(DB.raw.prepare("SELECT n FROM counts WHERE name = 'id_install'").all().map((r) => r.n), [1], 'marked once, never sent as a counter');
  const forget = (b) => new Request('https://stats.example.workers.dev/v1/forget', post(b));
  assert.equal((await ingest.fetch(forget({ v: 1, id: ID(1), app: '0.1.0-beta.9' }), { DB })).status, 204);
  for (const t of ['pings', 'clicks', 'counts', 'errors']) assert.equal(DB.raw.prepare(`SELECT COUNT(*) n FROM ${t} WHERE id = ?`).get(ID(1)).n, 0, t);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings WHERE id = ?').get(ID(2)).n, 1, 'other installs stay');
  assert.equal((await ingest.fetch(forget({ v: 1, id: 'nope', app: '0.1.0-beta.9' }), { DB })).status, 400);
});

test('dashboard beta.9: with the kept ID, installs, new against returning and exact cohorts', async () => {
  const DB = fakeD1();
  // A fixed Tuesday noon (UTC): the cohort of a week only shows its day 7 once the whole week is 7 days old (api.js), so with
  // Date.now() this test passed or failed by the day of the week (W12, D14)
  const now = Date.parse('2026-11-03T12:00:00Z');
  const dayAgo = (n) => new Date(now - n * 864e5).toISOString().slice(0, 10);
  // install 1: first seen 9 days ago, back today; install 2: new today; a daily beta.8 ID stays out of these numbers
  for (const [id, n] of [[1, 9], [1, 0], [2, 0]]) {
    DB.raw.prepare('INSERT INTO pings (day, ts, id, app, minutes, dev) VALUES (?, ?, ?, ?, ?, 0)').run(dayAgo(n), now - n * 864e5, ID(id), '0.1.0-beta.9', 10);
    DB.raw.prepare("INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, 'id_install', 1, 0)").run(dayAgo(n), ID(id));
  }
  DB.raw.prepare('INSERT INTO pings (day, ts, id, app, minutes, dev) VALUES (?, ?, ?, ?, ?, 0)').run(dayAgo(0), now, ID(3), '0.1.0-beta.8', 10);
  const s = JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: { version: 'all', country: 'all', mode: 'all', link: 'all' }, dev: false, now })));
  assert.deepEqual([s.installs.active, s.installs.fresh, s.installs.back, s.installs.prev], [2, 1, 1, 1]);
  const first = s.cohortsExact.find((c) => c.n === 1 && c.back[3] === 1);
  assert.ok(first, 'install 1 counts once in the day 7 column');
  assert.ok(!JSON.stringify(s).includes(ID(1)), 'no install ID in the answer');
});

test('ingest beta.9 part 3: IP changes, drops, Rejoin, first screen, updated from, data and regions; odd ones dropped', async () => {
  const DB = fakeD1();
  const token = await jwt(claims());
  const counts = { accounts: 1, chg_air: 2, chgt_10_20: 2, chga_1: 1, chga_2: 1, chgq_good: 1, chgq_great: 1, chg_retry: 1, drop_3p: 1, drop_9: 1, rej_try: 2, rej_back: 1, rej_wait: 1,
    fs_servers: 1, fs_Bad: 1, cable_hint: 1, updfrom_0_1_0_beta_8: 1, 'updfrom_x': 1, reg_fra: 9, data_gt2g: 1, cs2_start: 2, min_prot: 120, min_cs2: 90 };
  await ingest.fetch(post(report({ link: 'usb', counts, data: '1_2g', regions: ['fra', 'mad', 'mad', 'Paris', 'x'] })), { DB });
  await ingest.fetch(post(report({ id: ID(2), data: '7 GB', regions: 'fra', counts: { accounts: 1 } })), { DB });
  const names = DB.raw.prepare('SELECT name FROM counts WHERE id = ? ORDER BY name').all(ID(1)).map((r) => r.name);
  for (const n of ['chgq_great', 'drop_9', 'fs_Bad', 'updfrom_x', 'reg_fra', 'data_gt2g']) assert.ok(!names.includes(n) || n === 'reg_fra' && names.filter((x) => x === 'reg_fra').length === 1, n);
  assert.ok(names.includes('data_1_2g') && names.includes('reg_mad') && names.includes('updfrom_0_1_0_beta_8') && names.includes('cable_hint'));
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM counts WHERE id = ? AND (name LIKE 'reg_%' OR name LIKE 'data_%')").get(ID(2)).n, 0);
  const s = await (await dashboard.fetch(get('/api/summary?period=7', token), { ...env0(), DB })).json();
  assert.deepEqual(s.phone.method[0], { k: 'Modo avião', n: 2 });
  assert.deepEqual(s.phone.duration.find((x) => x.band === '10-20 s'), { band: '10-20 s', usb: 2, hotspot: 0 });
  assert.equal(s.phone.data.find((x) => x.band === '1-2 GB').n, 1);
  assert.deepEqual([s.cs2.rejoin.tries, s.cs2.rejoin.back, s.cs2.rejoin.waits], [2, 1, 1]);
  assert.deepEqual(s.cs2.regions.map((r) => r.code).sort(), ['fra', 'mad']);
  assert.deepEqual(s.firstScreen, [{ screen: 'servers', n: 1 }]);
  assert.deepEqual(s.updates.from, [{ v: '0.1.0-beta.8', n: 1 }]);
  assert.deepEqual([s.cs2.sessions, s.cs2.hoursProtected], [2, 2]);
});

test('dashboard beta.9: live is only counts of the last 6 minutes', async () => {
  const DB = fakeD1();
  const token = await jwt(claims());
  const sig = (id, over) => ingest.fetch(new Request('https://x/v1/live', post({ v: 1, id: ID(id), app: '0.1.0-beta.9', mode: 'cs2open', link: 'usb', screen: 'connection', cs2: true, prot: true, tray: false, dev: false, ...over })), { DB });
  await sig(1); await sig(2, { cs2: false, tray: true }); await sig(3);
  DB.raw.prepare('UPDATE live SET ts = ts - 3600000 WHERE id = ?').run(ID(3));
  const l = await (await dashboard.fetch(get('/api/live', token), { ...env0(), DB })).json();
  assert.equal(l.now, 2);
  assert.equal(l.inGame, 1);
  assert.deepEqual(l.byScreen, [{ k: 'connection', n: 1 }, { k: 'tray', n: 1 }]);
  assert.ok(!JSON.stringify(l).includes(ID(1)));
});

test('beta.9 part 4: red Checks and the leak test only inside their families; per-version metrics from the server', async () => {
  const DB = fakeD1();
  const token = await jwt(claims());
  await ingest.fetch(post(report({ counts: { accounts: 1, chk_firewall: 1, chk_data: 1, chk_Windows: 1, chk_firewall_x: 1, leak_ok: 2, leak_bad: 1, leak_maybe: 1,
    app_start: 2, crash: 1, ip_changed: 3, ip_same: 1, m_comm: 4, m_comm_lag: 1, tp_10_30: 2, st_1_2: 2 }, minutes: 20 })), { DB });
  await ingest.fetch(post(report({ id: ID(2), app: '0.1.0-beta.9', counts: { accounts: 1, chk_firewall: 1, app_start: 1, tp_lt10: 1, st_lt1: 1 }, minutes: 5 })), { DB });
  const names = DB.raw.prepare('SELECT name FROM counts WHERE id = ? ORDER BY name').all(ID(1)).map((r) => r.name);
  for (const n of ['chk_Windows', 'chk_firewall_x', 'leak_maybe']) assert.ok(!names.includes(n), n);
  assert.ok(names.includes('chk_firewall') && names.includes('leak_bad'));
  const s = await (await dashboard.fetch(get('/api/summary?period=7', token), { ...env0(), DB })).json();
  assert.deepEqual(s.checks.red, [{ k: 'Windows Firewall', key: 'firewall', n: 2 }, { k: 'Mobile data', key: 'data', n: 1 }]);
  assert.deepEqual(s.checks.leak, { pass: 2, fail: 1 });
  assert.ok(!('chk_firewall' in s.counters), 'red Checks are install-days, not counters');
  const b8 = s.versionMetrics['0.1.0-beta.8'];
  assert.deepEqual([b8.days, b8.sessions, b8.crashes, b8.ipNew, b8.lag, b8.minMed], [1, 2, 1, [3, 4], [1, 4], 10]);
  assert.equal(b8.toProtected.find((x) => x.band === '10-30 s').n, 2);
  assert.equal(s.versionMetrics['0.1.0-beta.9'].startup.find((x) => x.band === '<1 s').n, 1);
  assert.deepEqual(s.adoption.at(-1).by, { '0.1.0-beta.8': 1, '0.1.0-beta.9': 1 });
  assert.ok(!JSON.stringify(s).includes(ID(1)), 'no install ID leaves');
});

// ------------------------------------------------------------------ one set of colors: src/Theme.xaml -> site and dashboard (tools/design-tokens.mjs)

const readRoot = (p) => readFileSync(ROOT + p, 'utf8');
const styleOf = (p) => [...readRoot(p).matchAll(/<style>([\s\S]*?)<\/style>/g)].map((m) => m[1]).join('\n');
const guides = readdirSync(ROOT + 'site').filter((d) => existsSync(ROOT + `site/${d}/index.html`)).map((d) => `site/${d}/index.html`);
const DASH_CSS = 'worker/stats/dashboard/style.css';

test('design: the site and the dashboard have the colors of src/Theme.xaml now (node tools/design-tokens.mjs)', () => {
  const theme = readTheme();
  for (const f of FILES) assert.equal(apply(readRoot(f.path), f.style, theme), readRoot(f.path), `${f.path} is behind Theme.xaml: node tools/design-tokens.mjs`);
  const home = readRoot('site/index.html').match(BLOCK)[0];
  for (const g of ['site/index.html', ...guides]) {
    assert.ok(readRoot(g).includes(home), `${g} has old tokens: node tools/site-pages.mjs`);
    assert.equal(readRoot(g).match(/<meta name="theme-color" content="([^"]+)">/)[1], theme.BgColor, `${g} theme-color`);
  }
  assert.equal(ASSETS['style.css'].body, readRoot(DASH_CSS), 'the Worker serves an old style.css: node tools/dashboard-assets.mjs');
});

test('design: the shared names of the site and the dashboard are the tokens, set once', () => {
  for (const [kind, text] of [['site', styleOf('site/index.html')], ['dashboard', readRoot(DASH_CSS)]]) {
    const root = text.match(/:root\s*\{([\s\S]*?)\}/)[1].replace(BLOCK, '');
    const decl = Object.fromEntries(root.split(';').map((d) => d.split(/:(.*)/s).map((x) => x.trim())).filter(([n]) => n.startsWith('--')));
    // the other palettes of the dashboard set them again on purpose; nothing else may
    const rest = text.replace(/:root\s*\{[\s\S]*?\}/, '').replace(/html\[data-pal="[a-z]+"\]\s*\{[^}]*\}/g, '');
    for (const [name, token] of Object.entries(WEB[kind])) {
      assert.equal(decl[name], `var(--t-${token})`, `${kind} ${name}`);
      assert.ok(!new RegExp(`[{;\s]${name}\s*:`).test(rest), `${kind} sets ${name} again`);
    }
  }
});

test('design: no shared color copied by hand (charts and the other palettes keep their own)', () => {
  const t = Object.fromEntries(tokens());
  const rgb = (h) => { h = h.slice(1); if (h.length === 3) h = [...h].map((c) => c + c).join(''); return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)); };
  // background, surfaces, lines, accent and top line: use var(--t-…), never the value
  const exact = ['bg', 'edge', 'surface', 'surface2', 'surface3', 'line', 'line-strong', 'good', 'warn', 'accent', 'accent-light', 'accent-dark', 'accent-deep', 'top1', 'top2', 'top3'];
  // and no near copy of the accent or the top line (a hand-picked #5ca8f0 is drift)
  const near = ['accent', 'accent-light', 'accent-dark', 'accent-deep', 'top1', 'top2', 'top3'];
  // Known: the copy of the app on "Na app" splits its top line in two (sidebar and page), each piece picked from a capture.
  const KNOWN = new Set([`${DASH_CSS} #3cb3e5`, `${DASH_CSS} #d8df3b`]);
  const files = { 'site/index.html': styleOf('site/index.html'), 'site/404.html': styleOf('site/404.html'), [DASH_CSS]: readRoot(DASH_CSS).replace(/html\[data-pal="[a-z]+"\]\s*\{[^}]*\}/g, '') };
  for (const g of guides) files[g] = styleOf(g);
  const found = [];
  for (const [p, css] of Object.entries(files)) {
    const s = css.replace(BLOCK, '');
    const lits = [...s.matchAll(/#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b/g)].map((m) => [m[0].toLowerCase(), rgb(m[0])])
      .concat([...s.matchAll(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/g)].map((m) => [m[0], [+m[1], +m[2], +m[3]]]));
    for (const [lit, c] of lits) {
      for (const name of exact) {
        const d = Math.max(...rgb(t[name]).map((x, i) => Math.abs(x - c[i])));
        if ((d === 0 || (d <= 3 && near.includes(name))) && !KNOWN.has(`${p} ${lit}`)) found.push(`${p}: ${lit} is ${d ? 'near ' : ''}--t-${name} (${t[name]})`);
      }
    }
  }
  assert.deepEqual(found, []);
});

// ------------------------------------------------------------------ auditoria de 4 nov. 2026 (equipa telemetria)

const F0 = { version: 'all', country: 'all', mode: 'all', link: 'all' };
const sum0 = async (DB, over = {}) => JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: F0, dev: false, now: Date.now(), ...over })));
const heavy = (n) => report({ id: ID(5000 + n), clicks: Object.fromEntries([...Array(150)].map((_, i) => [`B${i}_Click`, 1])),
  counts: Object.fromEntries([...Array(100)].map((_, i) => [`c${i}`, 1])), errors: [...Array(30)].map((_, i) => ({ type: 'E' + i, at: 'A.B', n: 1 })),
  heat: [...Array(300)].map((_, i) => ['connection', 'c', `${i % 24},${Math.floor(i / 24) % 16}`, 1]) });
const budgetOf = (DB, k) => DB.raw.prepare('SELECT rows FROM budget WHERE day = ?').get(k)?.rows ?? 0;
const today0 = () => new Date().toISOString().slice(0, 10);
const liveReq = (n) => new Request('https://stats.example.workers.dev/v1/live', post({ v: 1, id: ID(n), app: '0.1.0-beta.10', mode: 'game', link: 'usb', screen: 'connection', cs2: false, prot: false, tray: false, dev: false }));

test('W2: reports sent at the same moment cannot pass the day budget together (the cap is inside the write)', async () => {
  const DB = fakeD1();
  const res = await Promise.all([...Array(120)].map((_, i) => ingest.fetch(post(heavy(i)), { DB })));
  const ok = res.filter((r) => r.status === 204).length;
  assert.ok(res.some((r) => r.status === 429), 'some are refused');
  assert.ok(budgetOf(DB, today0()) <= DAILY_ROWS, `budget ${budgetOf(DB, today0())}`);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings').get().n, ok, 'a refused report wrote nothing');
});

test('W2: schema.sql has the same budget triggers as the Worker, and an old database gets them', async () => {
  const schema = readFileSync(new URL('../schema.sql', import.meta.url), 'utf8');
  for (const t of BUDGET_TRIGGERS) assert.ok(schema.includes(t + ';'), t.slice(0, 60));
  const DB = fakeD1();
  DB.raw.exec('DROP TRIGGER budget_cap_insert; DROP TRIGGER budget_cap_update;');
  await ingest.fetch(post(report()), { DB });
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'budget_cap_%'").get().n, 2);
});

test('D6: the live signal has its share of the day, and the reports keep the rest', async () => {
  const DB = fakeD1();
  DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, ?), (?, ?)').run(today0(), LIVE_ROWS - 2, `${today0()}:live`, LIVE_ROWS - 2);
  assert.equal((await ingest.fetch(liveReq(1), { DB })).status, 429, 'live is out of its share');
  assert.equal((await ingest.fetch(post(report()), { DB })).status, 204, 'a report still goes in');
  assert.ok(budgetOf(DB, `${today0()}:live`) <= LIVE_ROWS);
});

test('W1/W3: Delete my stats has its own budget, answers an unknown ID without writing, and never scans the tables', async () => {
  const DB = fakeD1();
  await ingest.fetch(post(report({ counts: { ip_changed: 1 }, errors: [{ type: 'X', at: 'A.B', n: 1 }] })), { DB });
  DB.raw.prepare('UPDATE budget SET rows = ? WHERE day = ?').run(DAILY_ROWS, today0());
  const forget = (id) => new Request('https://stats.example.workers.dev/v1/forget', post({ v: 1, id, app: '0.1.0-beta.10' }));
  assert.equal((await ingest.fetch(forget(ID(77)), { DB })).status, 204, 'unknown ID: nothing to delete');
  assert.equal(budgetOf(DB, `${today0()}:forget`), 0, 'and nothing written');
  assert.equal((await ingest.fetch(forget(ID(1)), { DB })).status, 204, 'the day budget is full, the delete still works');
  for (const t of ['pings', 'clicks', 'counts', 'errors']) assert.equal(DB.raw.prepare(`SELECT COUNT(*) n FROM ${t} WHERE id = ?`).get(ID(1)).n, 0, t);
  assert.ok(budgetOf(DB, `${today0()}:forget`) > 0 && budgetOf(DB, `${today0()}:forget`) <= FORGET_ROWS);
  for (const t of ['clicks', 'counts', 'errors']) {
    const plan = DB.raw.prepare(`EXPLAIN QUERY PLAN DELETE FROM ${t} WHERE day IN (SELECT DISTINCT day FROM pings WHERE id = ?) AND id = ?`).all(ID(1), ID(1)).map((r) => r.detail).join(' | ');
    assert.ok(!new RegExp(`SCAN ${t}\\b`).test(plan) && !/SCAN pings\b/.test(plan), `${t}: ${plan}`);
  }
});

test('D1/D5: one more report marked exit inside the 10 minutes; unmarked or a third one is refused', async () => {
  const DB = fakeD1();
  assert.equal((await ingest.fetch(post(report()), { DB })).status, 204);
  assert.equal((await ingest.fetch(post(report({ minutes: 4 })), { DB })).status, 429, 'unmarked: the old rule');
  assert.equal((await ingest.fetch(post(report({ minutes: 4, exit: true })), { DB })).status, 204, 'the exit report');
  assert.equal((await ingest.fetch(post(report({ minutes: 1, exit: true })), { DB })).status, 429, 'only one');
  assert.deepEqual(DB.raw.prepare('SELECT minutes FROM pings ORDER BY ts').all().map((r) => r.minutes), [12, 4]);
});

test('D3/D7: the link of the day is the best one, and the version the newest report, not the highest text', async () => {
  const DB = fakeD1();
  const t = Date.now(), d = today0();
  const ins = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, mode, link, minutes, dev) VALUES (?, ?, ?, ?, ?, ?, ?, 0)');
  ins.run(d, t - 3000, ID(1), '0.1.0-beta.9', 'game', 'none', 5);
  ins.run(d, t - 2000, ID(1), '0.1.0-beta.10', 'cs2open', 'usb', 5);
  ins.run(d, t - 1000, ID(1), '0.1.0-beta.10', 'cs2open', 'none', 5);
  const s = await sum0(DB);
  assert.deepEqual(s.links, [{ k: 'usb', n: 1 }]);
  assert.deepEqual(s.versions, [{ k: '0.1.0-beta.10', n: 1 }]);
  assert.deepEqual(s.modes, [{ k: 'cs2open', n: 1 }]);
});

test('D2: the developer PCs (EXCLUDE_IDS) stay out of summary and live, and are in ?dev=1', async () => {
  const DB = fakeD1();
  await ingest.fetch(post(report({ id: ID(1), counts: { accounts: 3 } })), { DB });
  await ingest.fetch(post(report({ id: ID(2), counts: { accounts: 3 } })), { DB });
  for (const n of [1, 2]) await ingest.fetch(liveReq(n), { DB });
  const exclude = [ID(2)];
  assert.equal((await sum0(DB, { exclude })).days.at(-1).users, 1);
  assert.equal((await sum0(DB, { exclude, dev: true })).days.at(-1).users, 1, 'the excluded PC shows with dev=1');
  assert.equal((await liveNow(DB, { dev: false, now: Date.now(), exclude })).now, 1);
  assert.deepEqual(who(false, ['bad']), { sql: 'dev = ?', args: [0] }, 'only real IDs reach the SQL');
  // and through the Worker, with the secret
  const token = await jwt(claims());
  const s = await (await dashboard.fetch(get('/api/summary?period=7', token), { ...env0(), DB, EXCLUDE_IDS: ID(2) })).json();
  assert.equal(s.days.at(-1).users, 1);
  assert.ok(!JSON.stringify(s).includes(ID(2)), 'the excluded ID never goes out');
  const l = await (await dashboard.fetch(get('/api/live', token), { ...env0(), DB, EXCLUDE_IDS: ID(2) })).json();
  assert.equal(l.now, 1);
});

test('W4: a cut keeps the newest days and says so', async () => {
  const DB = fakeD1();
  const d = today0(), old = new Date(Date.now() - 5 * 864e5).toISOString().slice(0, 10);
  DB.raw.prepare('INSERT INTO pings (day, ts, id, app, minutes, dev) VALUES (?, ?, ?, ?, 5, 0)').run(d, Date.now(), ID(1), '0.1.0-beta.10');
  DB.raw.prepare("INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, 'ip_changed', 4, 0)").run(d, ID(1));
  DB.raw.exec(`WITH RECURSIVE k(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM k WHERE x < ${MAX_ROWS + 10})
    INSERT INTO counts (day, id, name, n, dev) SELECT '${old}', printf('%032x', x), 'ip_changed', 1, 0 FROM k`);
  const s = await sum0(DB);
  assert.ok(s.truncated.includes('counts'), JSON.stringify(s.truncated));
  assert.equal(s.days.at(-1).ipNew, 4, "today's counter is kept");
});

test('W9: names of the object prototype are not files of the dashboard', async () => {
  const token = await jwt(claims());
  const env = { ...env0(), DB: fakeD1() };
  for (const p of ['/d/__proto__', '/d/constructor', '/d/toString', '/ui/hasOwnProperty']) assert.equal((await dashboard.fetch(get(p, token), env)).status, 404, p);
  assert.equal((await dashboard.fetch(get('/d/app.js', token), env)).status, 200);
});

test('W13: when the Access keys cannot be fetched, the failure is kept a minute too', async () => {
  globalThis.fetch = async () => { certCalls++; return new Response('down', { status: 503 }); };
  const token = await jwt(claims());
  for (let i = 0; i < 10; i++) assert.equal((await dashboard.fetch(get('/dashboard', token), { ...env0(), DB: fakeD1() })).status, 403);
  assert.equal(certCalls, 1);
});

test('W11: the tabs of /classic stay on /classic', async () => {
  const token = await jwt(claims());
  const html = await (await dashboard.fetch(get('/classic?days=7', token), { ...env0(), DB: fakeD1() })).text();
  assert.ok(html.includes('href="/classic?days=30"') && !html.includes('href="/dashboard?days='));
});

test('D9: first day steps, when they use it, the day after, the period before, trends and list sizes', async () => {
  const DB = fakeD1();
  const now = Date.parse('2026-11-04T15:30:00Z');
  const dayAgo = (n) => new Date(now - n * 864e5).toISOString().slice(0, 10);
  const ping = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, link, minutes, dev) VALUES (?, ?, ?, ?, ?, 10, 0)');
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)');
  // install 1 new 2 days ago with the phone and a new IP, back the next day; install 2 new today without a phone; one 9 days ago (before)
  ping.run(dayAgo(2), now - 2 * 864e5, ID(1), '0.1.0-beta.10', 'usb');
  for (const [k, n] of [['first_start', 1], ['ip_changed', 2], ['id_install', 1], ['accounts', 2], ['accounts', 2]]) cnt.run(dayAgo(2), ID(1), k, n);
  ping.run(dayAgo(1), now - 864e5, ID(1), '0.1.0-beta.10', 'usb');
  cnt.run(dayAgo(1), ID(1), 'id_install', 1);
  ping.run(dayAgo(0), now, ID(2), '0.1.0-beta.10', 'none');
  for (const [k, n] of [['first_start', 1], ['id_install', 1]]) cnt.run(dayAgo(0), ID(2), k, n);
  ping.run(dayAgo(9), now - 9 * 864e5, ID(3), '0.1.0-beta.9', 'usb');
  for (const [k, n] of [['ip_changed', 3], ['app_start', 2]]) cnt.run(dayAgo(9), ID(3), k, n);
  DB.raw.prepare("INSERT INTO clicks (day, id, name, n, dev) VALUES (?, ?, 'ChangeIp_Click', 3, 0)").run(dayAgo(1), ID(1));
  const s = JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: F0, dev: false, now })));
  assert.deepEqual(s.funnel.map((x) => x.n), [2, 1, 1]);
  assert.equal(s.when.flat().reduce((a, b) => a + b, 0), 3, 'one install-hour per report');
  assert.equal(s.when[2][15], 1, 'today 15:30 UTC is Wednesday 15h in Lisbon (winter)');
  assert.deepEqual(s.installs.nextDay, { n: 1, back: 1 });
  assert.equal(s.prev.ipNew, 3); assert.equal(s.prev.sessions, 2);
  assert.deepEqual(s.buttons.ChangeIp_Click.perDay, { [dayAgo(1)]: 3 });
  assert.deepEqual(s.sizes.find((x) => x.name === 'accounts').bands, [{ band: '2-5', n: 1 }], 'a size sent twice in a day is one band, not added up');
  assert.ok(s.lastTs === now && Array.isArray(s.truncated) && Array.isArray(s.warnings));
});

test('K3: the body the app makes (Telemetry.Body, app-report.json) goes through the Worker and every field it carries shows in summary()', async () => {
  // tests/TelemetryTests.cs fails when Telemetry.Body no longer makes this file; this test fails when a field in it is lost on the way
  const body = JSON.parse(readFileSync(new URL('./app-report.json', import.meta.url), 'utf8'));
  const DB = fakeD1();
  assert.equal((await ingest.fetch(post(body), { DB })).status, 204);
  const s = await sum0(DB);
  const checks = {
    v: () => true, id: () => !JSON.stringify(s).includes(body.id), dev: () => s.dev === false && s.days.at(-1).users === 1,
    idk: () => s.installs.active === 1,
    app: () => s.versions[0]?.k === body.app, os: () => s.oses[0]?.k === body.os, mode: () => s.modes[0]?.k === body.mode, link: () => s.links[0]?.k === body.link,
    minutes: () => s.days.at(-1).minutes === body.minutes,
    clicks: () => Object.entries(body.clicks).every(([k, n]) => s.buttons[k]?.n === n),
    counts: () => true,   // name by name below
    errors: () => body.errors.every((e) => (e.type.startsWith('Fatal_') ? s.crashPlaces.some((c) => c.at === e.at && c.n === e.n) : s.errors.some((x) => x.type === e.type && x.at === e.at && x.n === e.n))),
    heat: () => body.heat.every(([sc, kind, cell, n]) => s.heat[sc]?.[kind]?.some(([c, r, v]) => `${c},${r}` === cell && v === n)),
    screens: () => Object.entries(body.screens).every(([k, m]) => s.screens[k]?.minutes === m),
    paths: () => Object.entries(body.paths).every(([p, n]) => { const [from, to] = p.split('>'); return s.paths.some((x) => x.from === from && x.to === to && x.n === n); }),
    age: () => s.age.some((a) => a.band === body.age && a.n === 1),
    active7: () => s.active.find((a) => a.band === String(body.active7))?.n === 1,
    settings: () => body.settings.every((n) => s.settings.some((x) => x.name === n)),
    regions: () => body.regions.every((c) => s.cs2.regions.some((x) => x.code === c)),
    data: () => s.phone.data.reduce((a, x) => a + x.n, 0) === 1,
    exit: () => true,
  };
  const missing = Object.keys(body).filter((k) => !checks[k]);
  assert.deepEqual(missing, [], 'a field the app sends that this test does not check yet');
  for (const [k, ok] of Object.entries(checks)) if (k in body) assert.ok(ok(), `field ${k} lost on the way`);
  // Every counter: a list size in sizes, a red Checks row in checks, everything else under its own name in counters
  const SIZE = ['servers', 'accounts', 'flags', 'open_with', 'open_with_admin', 'regions_blocked'];
  const BAND = { 0: '0', 1: '1', 2: '2-5', 6: '6-20', 21: '21-100', 101: '100+' };
  for (const [name, n] of Object.entries(body.counts)) {
    if (SIZE.includes(name)) assert.ok(s.sizes.find((z) => z.name === name)?.bands.some((b) => b.band === BAND[n] && b.n === 1), `size ${name}`);
    else if (name.startsWith('chk_')) assert.ok(s.checks.red.some((c) => c.key === name.slice(4)), name);
    else assert.equal(s.counters[name], n, `counter ${name}`);
  }
});

// ------------------------------------------------------------------ revisão da equipa telemetria (4 nov. 2026)

test('Revisão: a database that refuses the budget triggers still takes reports (the read check stays)', async () => {
  const DB = fakeD1();
  DB.raw.exec('DROP TRIGGER budget_cap_insert; DROP TRIGGER budget_cap_update;');
  const batch = DB.batch.bind(DB);
  let refused = 0;
  DB.batch = async (list) => {
    if (list.some((s) => /CREATE TRIGGER/i.test(s.sql))) { refused++; throw new Error('D1_ERROR: not authorized'); }
    return batch(list);
  };
  assert.equal((await ingest.fetch(post(report()), { DB })).status, 204, 'the report goes in');
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings').get().n, 1);
  assert.ok(refused >= 1, 'the triggers were asked for');
  DB.raw.prepare('UPDATE budget SET rows = ? WHERE day = ?').run(DAILY_ROWS, today0());
  assert.equal((await ingest.fetch(post(report({ id: ID(2) })), { DB })).status, 429, 'the read check still stops a full day');
});

test('Revisão: "Voltam no dia seguinte" only counts installs whose next day is over', async () => {
  const DB = fakeD1();
  const now = Date.parse('2026-11-04T15:30:00Z');
  const dayAgo = (n) => new Date(now - n * 864e5).toISOString().slice(0, 10);
  const ping = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, link, minutes, dev) VALUES (?, ?, ?, ?, ?, 10, 0)');
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)');
  // install 1 new 2 days ago and not back yesterday; install 2 new yesterday (today is not over, so it can't count yet)
  ping.run(dayAgo(2), now - 2 * 864e5, ID(1), '0.1.0-beta.10', 'usb');
  ping.run(dayAgo(1), now - 864e5, ID(2), '0.1.0-beta.10', 'usb');
  for (const [d, id] of [[dayAgo(2), ID(1)], [dayAgo(1), ID(2)]]) for (const k of ['first_start', 'id_install']) cnt.run(d, id, k, 1);
  const s = JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: F0, dev: false, now })));
  assert.deepEqual(s.installs.nextDay, { n: 1, back: 0 });
});

// ------------------------------------------------------------------ dashboard v3 (4 nov. 2026): os campos novos do /api/summary

test('v3: freshness (any PC and this view), the day budget, Por hora, yesterday at the same time and today in sums', async () => {
  const DB = fakeD1();
  const now = Date.parse('2026-11-04T15:30:00Z');   // inverno: Lisboa = UTC
  const at = (d, hm) => Date.parse(`${d}T${hm}:00Z`);
  const T = '2026-11-04', Y = '2026-11-03';
  const ping = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, minutes, dev, country, link, mode) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)');
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)');
  ping.run(Y, at(Y, '10:00'), ID(1), '0.1.0-beta.9', 20, 0, 'PT', 'none', 'game');
  ping.run(Y, at(Y, '18:00'), ID(1), '0.1.0-beta.9', 30, 0, 'PT', 'none', 'game');
  for (const [k, n] of [['first_start', 1], ['accounts', 1]]) cnt.run(Y, ID(1), k, n);
  ping.run(T, at(T, '09:10'), ID(1), '0.1.0-beta.9', 5, 0, 'PT', 'none', 'game');
  cnt.run(T, ID(1), 'ip_changed', 2);
  ping.run(T, at(T, '14:05'), ID(2), '0.1.0-beta.10', 12, 0, 'BR', 'usb', 'cs2open');
  cnt.run(T, ID(2), 'first_start', 1);
  ping.run(T, at(T, '13:00'), ID(3), '0.1.0-beta.10', 0, 0, 'US', 'none', 'game');   // análise automática: 0 contas, 0 cliques, 0 min
  ping.run(T, at(T, '15:20'), ID(9), '0.1.0-beta.10', 3, 1, 'PT', 'usb', 'game');     // build de dev
  DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, ?), (?, ?)').run(T, 30000, `${T}:live`, 500);
  const s = JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: F0, dev: false, now })));
  assert.equal(s.fresh.lastAny, at(T, '15:20'), 'any PC: the dev build too');
  assert.equal(s.fresh.lastTs, at(T, '14:05'), 'this view: without the dev build');
  assert.equal(s.lastTs, s.fresh.lastTs, 'the old field stays');
  assert.deepEqual(s.fresh.budget, { day: T, rows: 30000, cap: DAILY_ROWS, live: 500, liveCap: LIVE_ROWS, site: 0, siteCap: SITE_ROWS, forget: 0, forgetCap: FORGET_ROWS });
  assert.equal(s.hourly.hour, 15);
  assert.equal(s.hourly.today[9], 1); assert.equal(s.hourly.today[14], 1);
  assert.equal(s.hourly.today[13], 0, 'the scanner stays out');
  assert.equal(s.hourly.today[15], 0); assert.equal(s.hourly.today[16], null, 'hours still to come are null');
  assert.equal(s.hourly.yday[10], 1); assert.equal(s.hourly.yday[18], 1);
  assert.deepEqual(s.sameTime, { users: 1, fresh: 1, minutes: 20 }, 'yesterday up to 15:30: only the 10:00 report');
  const [td, yd] = s.recentDays;
  assert.equal(td.date, T); assert.equal(td.users, 2); assert.equal(td.fresh, 1); assert.equal(td.ip, 2); assert.equal(td.minutes, 17);
  assert.deepEqual(td.countries, [{ k: 'BR', n: 1 }, { k: 'PT', n: 1 }]);
  assert.deepEqual(td.links, [{ k: 'none', n: 1 }, { k: 'usb', n: 1 }]);
  assert.equal(yd.date, Y); assert.equal(yd.users, 1); assert.equal(yd.minutes, 50);
  assert.ok(!JSON.stringify(s).includes(ID(1)) && !JSON.stringify(s).includes(ID(2)), 'no install ID in the answer');
  // a period of 1 day still has yesterday for Por hora and the same time
  const one = JSON.parse(JSON.stringify(await summary(DB, { period: 1, f: F0, dev: false, now })));
  assert.equal(one.hourly.yday[10], 1); assert.equal(one.sameTime.users, 1); assert.equal(one.recentDays[1].users, 1);
  // with no budget table yet (an old database) the bar says 0, and the answer still comes
  DB.raw.exec('DROP TABLE budget');
  const nb = JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: F0, dev: false, now })));
  assert.equal(nb.fresh.budget.rows, 0);
  assert.ok(nb.warnings.some((w) => w.startsWith('budget')));
});

test('v3: the funnel reaches Protected and is per day, back after 7 days, cohorts by day, the first day of each error', async () => {
  const DB = fakeD1();
  const now = Date.parse('2026-11-04T15:30:00Z');
  const ago = (n) => new Date(now - n * 864e5).toISOString().slice(0, 10);
  const ping = DB.raw.prepare('INSERT INTO pings (day, ts, id, app, minutes, dev, link) VALUES (?, ?, ?, ?, 10, 0, ?)');
  const cnt = DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)');
  const err = DB.raw.prepare('INSERT INTO errors (day, id, app, type, at, n, dev) VALUES (?, ?, ?, ?, ?, ?, 0)');
  // A: new 9 days ago with the phone, a new IP and Protected; open again 7 days later. B: new 3 days ago, a new IP, no Protected.
  ping.run(ago(9), now - 9 * 864e5, ID(1), '0.1.0-beta.9', 'usb');
  for (const [k, n] of [['first_start', 1], ['id_install', 1], ['ip_changed', 1], ['tp_10_30', 1]]) cnt.run(ago(9), ID(1), k, n);
  ping.run(ago(2), now - 2 * 864e5, ID(1), '0.1.0-beta.9', 'usb');
  cnt.run(ago(2), ID(1), 'id_install', 1);
  ping.run(ago(3), now - 3 * 864e5, ID(2), '0.1.0-beta.10', 'usb');
  for (const [k, n] of [['first_start', 1], ['id_install', 1], ['ip_changed', 2]]) cnt.run(ago(3), ID(2), k, n);
  err.run(ago(9), ID(1), '0.1.0-beta.9', 'E1', 'X.Y', 1);
  err.run(ago(2), ID(1), '0.1.0-beta.9', 'E1', 'X.Y', 2);
  err.run(ago(3), ID(2), '0.1.0-beta.10', 'E2', 'A.B', 1);
  const s = JSON.parse(JSON.stringify(await summary(DB, { period: 30, f: F0, dev: false, now })));
  assert.deepEqual(s.funnel.map((x) => x.n), [2, 2, 2], 'the three steps before stay as they were');
  assert.equal(s.funnelProtected, 1);
  assert.equal(s.funnelDays.length, 30);
  assert.deepEqual(s.funnelDays.find((d) => d.date === ago(9)), { date: ago(9), fs: 1, ph: 1, ip: 1, prot: 1 });
  assert.deepEqual(s.funnelDays.find((d) => d.date === ago(3)), { date: ago(3), fs: 1, ph: 1, ip: 1, prot: 0 });
  assert.deepEqual(s.installs.day7, { n: 1, back: 1 }, 'B has no day 7 yet');
  assert.deepEqual(s.cohortsDaily, [{ day: ago(9), n: 1, back: [0, 0, 1, null] }, { day: ago(3), n: 1, back: [0, null, null, null] }]);
  const e1 = s.errors.find((e) => e.type === 'E1'), e2 = s.errors.find((e) => e.type === 'E2');
  assert.deepEqual(e1.first, { day: ago(9), app: '0.1.0-beta.9' });
  assert.deepEqual(e2.first, { day: ago(3), app: '0.1.0-beta.10' });
  // with 7 days E1 is only from 2 days ago in the list, but its first day is still the one 9 days ago (not "new")
  const w = JSON.parse(JSON.stringify(await summary(DB, { period: 7, f: F0, dev: false, now })));
  assert.equal(w.errors.find((e) => e.type === 'E1').first.day, ago(9));
  assert.deepEqual(w.installs.day7, { n: 0, back: 0 }, 'with 7 days no new install has its day 7 over');
});

test('v3: the v3 page is at /v3 (v4: /dashboard is the v4), the v2 stays at /v2 with its own files, all behind Access', async () => {
  const token = await jwt(claims());
  const env = { ...env0(), DB: fakeD1() };
  const v3 = await (await dashboard.fetch(get('/v3', token), env)).text();
  assert.equal((await dashboard.fetch(get('/v3'), env)).status, 403, '/v3 without Access');
  assert.ok(v3.includes('/d/real.js') && v3.includes('/d/app.js') && !v3.includes('/d/data.js'), 'the v3 has no mock data');
  assert.ok(!/makeV3|makeData|\bSCEN\b|GH_REAL|mockbar|id="scale"/.test(ASSETS['app.js'].body + ASSETS['real.js'].body + v3), 'no mock-up switch or example data');
  const v2 = await dashboard.fetch(get('/v2', token), env);
  assert.equal(v2.status, 200);
  const html = await v2.text();
  for (const f of ['/d/v2/app.js', '/d/v2/data.js', '/d/v2/real.js', '/d/v2/style.css', '/d/ui-map.js']) {
    assert.ok(html.includes(f), f);
    assert.equal((await dashboard.fetch(get(f, token), env)).status, 200, f);
  }
  assert.equal((await dashboard.fetch(get('/v2'), env)).status, 403, '/v2 without Access');
  assert.equal((await dashboard.fetch(get('/d/v2/app.js'), env)).status, 403);
});

test('Revisão v3: what the reports bring (error type, place in the code, version) is escaped on the page and in the CSV', () => {
  const app = ASSETS['app.js'].body;
  // LOCATION (index.js) lets < > ` | through: "Erro novo" in Pede atenção draws its text as HTML, so the place and version go through esc()
  const alert = app.split('\n').find((l) => l.includes('Erro novo:'));
  assert.ok(alert.includes('esc(e.at)') && alert.includes('esc(short(e.first.app))') && !/\$\{e\.at\}/.test(alert), alert);
  // Nothing from a report reaches the HTML of a table raw
  assert.ok(!/<td[^>]*>\$\{(e|c)\.(at|type|app)\}/.test(app));
  // A cell starting with = + - @ opens as text in a spreadsheet
  assert.ok(app.includes(String.raw`/^[=+\-@]/.test(v) ? ` + "`'${v}`"), 'CSV guard');
});

// ------------------------------------------------------------------ medir (4 nov.): the website counter, the cron, GitHub without token, 12 months, v2 -> v3

const siteReq = (body, { origin = SITE_ORIGIN, type = 'text/plain;charset=UTF-8', headers = {}, cf = { country: 'PT', asn: 3243 }, method = 'POST' } = {}) => {
  const r = new Request('https://stats.example.workers.dev/v1/site', { method, headers: { ...(origin ? { origin } : {}), ...(method === 'POST' ? { 'content-type': type } : {}), ...headers },
    ...(method === 'POST' ? { body: typeof body === 'string' ? body : JSON.stringify(body) } : {}) });
  Object.defineProperty(r, 'cf', { value: cf });
  return r;
};
const siteRows = (DB) => DB.raw.prepare('SELECT day, page, ref, country, event, n FROM site ORDER BY page, ref, event').all().map((r) => ({ ...r }));

test('medir: a visit from the website is added into the day by page, referrer host and country, with no ID and no IP', async () => {
  const DB = fakeD1();
  const res = await ingest.fetch(siteReq({ v: 1, p: '/', r: 'www.Google.com', e: 'view' }, { headers: { 'cf-connecting-ip': '203.0.113.9' } }), { DB });
  assert.equal(res.status, 204);
  assert.equal(res.headers.get('access-control-allow-origin'), SITE_ORIGIN);
  assert.equal(res.headers.get('access-control-allow-credentials'), null, 'no credentials');
  await ingest.fetch(siteReq({ v: 1, p: '/', r: 'google.com', e: 'view' }), { DB });
  await ingest.fetch(siteReq({ v: 1, p: '/is-it-safe/', r: 'sepd0x.github.io', e: 'download' }, { cf: { country: 'BR' } }), { DB });
  await ingest.fetch(siteReq({ v: 1, p: '/cs2-ip-ban/', r: 'not a host!', e: 'view' }), { DB });
  const rows = siteRows(DB);
  assert.deepEqual(rows.map((r) => [r.page, r.ref, r.country, r.event, r.n]), [['/', 'google.com', 'PT', 'view', 2], ['/cs2-ip-ban/', 'other', 'PT', 'view', 1], ['/is-it-safe/', '', 'BR', 'download', 1]]);
  const cols = DB.raw.prepare('PRAGMA table_info(site)').all().map((c) => c.name);
  assert.deepEqual(cols, ['day', 'page', 'ref', 'country', 'event', 'n'], 'no ID, no IP, nothing about the browser');
  assert.ok(!JSON.stringify(rows).includes('203.0.113.9'));
  assert.equal(budgetOf(DB, today0()), 0, 'the app reports keep the whole day budget');
  assert.ok(budgetOf(DB, `${today0()}:site`) > 0, 'the site has its own budget');
});

test('medir: a link with ?ref=<name> (TikTok sends no referrer) is counted as ref:<name>, and only short plain names', async () => {
  const DB = fakeD1();
  await ingest.fetch(siteReq({ v: 1, p: '/', r: 'ref:tiktok', e: 'view' }), { DB });
  await ingest.fetch(siteReq({ v: 1, p: '/', r: 'ref:tiktok', e: 'download' }), { DB });
  await ingest.fetch(siteReq({ v: 1, p: '/', r: 'ref:<script>', e: 'view' }), { DB });
  await ingest.fetch(siteReq({ v: 1, p: '/', r: 'ref:' + 'a'.repeat(21), e: 'view' }), { DB });
  const rows = siteRows(DB);
  assert.deepEqual(rows.map((r) => [r.ref, r.event, r.n]), [['other', 'view', 2], ['ref:tiktok', 'download', 1], ['ref:tiktok', 'view', 1]]);
});

test('medir: the site counter refuses other origins, odd bodies and JSON, and drops Do Not Track, GPC, bots and hosting networks', async () => {
  const DB = fakeD1();
  const ok = { v: 1, p: '/', r: '', e: 'view' };
  assert.equal((await ingest.fetch(siteReq(ok, { origin: 'https://evil.example' }), { DB })).status, 403);
  assert.equal((await ingest.fetch(siteReq(ok, { origin: null }), { DB })).status, 403);
  assert.equal((await ingest.fetch(siteReq(ok, { type: 'application/json' }), { DB })).status, 415);
  for (const bad of [{ ...ok, p: '/nope/' }, { ...ok, e: 'click' }, { ...ok, v: 2 }, { ...ok, id: ID(1) }, { ...ok, r: 5 }, 'nope', '[]'])
    assert.equal((await ingest.fetch(siteReq(bad), { DB })).status, 400, JSON.stringify(bad));
  assert.equal((await ingest.fetch(siteReq('x'.repeat(600)), { DB })).status, 413);
  for (const opt of [{ headers: { dnt: '1' } }, { headers: { 'sec-gpc': '1' } }, { headers: { 'user-agent': 'Googlebot/2.1' } }, { cf: { country: 'US', asn: 15169 } }]) {
    const res = await ingest.fetch(siteReq(ok, opt), { DB });
    assert.equal(res.status, 204, JSON.stringify(opt));
  }
  assert.equal(siteRows(DB).length, 0, 'nothing kept');
});

test('medir: the preflight answers only the website, without credentials', async () => {
  const r = await ingest.fetch(siteReq(null, { method: 'OPTIONS', headers: { 'access-control-request-method': 'POST', 'access-control-request-headers': 'content-type' } }), { DB: fakeD1() });
  assert.equal(r.status, 204);
  assert.equal(r.headers.get('access-control-allow-origin'), SITE_ORIGIN);
  assert.equal(r.headers.get('access-control-allow-methods'), 'POST');
  assert.match(r.headers.get('vary'), /Origin/);
  assert.equal(r.headers.get('access-control-allow-credentials'), null);
  assert.equal((await ingest.fetch(siteReq(null, { method: 'OPTIONS', origin: 'https://evil.example' }), { DB: fakeD1() })).status, 403);
  // The app's endpoints still never answer a preflight
  const pre = new Request('https://x/v1', { method: 'OPTIONS', headers: { origin: SITE_ORIGIN } });
  assert.equal((await ingest.fetch(pre, { DB: fakeD1() })).status, 404);
});

test('medir: the site counter stops at its own budget and the app reports go on', async () => {
  const DB = fakeD1();
  DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, ?)').run(`${today0()}:site`, SITE_ROWS - 3);
  assert.equal((await ingest.fetch(siteReq({ v: 1, p: '/', r: '', e: 'view' }), { DB })).status, 429);
  assert.equal(siteRows(DB).length, 0);
  assert.equal((await ingest.fetch(post(report()), { DB })).status, 204, 'a report still goes in');
  // The cap is in the database too (the site's own triggers, next to the old ones)
  assert.throws(() => DB.raw.prepare('UPDATE budget SET rows = ? WHERE day = ?').run(SITE_ROWS + 1, `${today0()}:site`), /over budget/);
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'budget_site_%'").get().n, 2);
});

test('medir: every page of the site is known to the Worker and sends the counter only without Do Not Track or GPC', () => {
  const root = new URL('../../../site/', import.meta.url);
  const dirs = readdirSync(root, { withFileTypes: true }).filter((d) => d.isDirectory() && existsSync(new URL(`${d.name}/index.html`, root))).map((d) => `/${d.name}/`);
  assert.deepEqual([...SITE_PAGES].sort(), ['/', ...dirs].sort(), 'SITE_PAGES is the list of the site pages');
  for (const p of SITE_PAGES) {
    const html = readFileSync(new URL(p === '/' ? 'index.html' : `${p.slice(1)}index.html`, root), 'utf8');
    const js = html.slice(html.indexOf('/* visit counter'), html.indexOf('/* /shared */')).replace(/\/\*[\s\S]*?\*\//g, '');
    assert.ok(js.includes("sendBeacon('https://stats.cs2ipchanger.workers.dev/v1/site'"), p);
    assert.ok(/doNotTrack!=='1'/.test(js) && /globalPrivacyControl!==true/.test(js), `${p}: Do Not Track and GPC`);
    assert.ok(!/cookie|localStorage|sessionStorage|indexedDB/i.test(js), `${p}: nothing kept in the browser`);
    assert.ok(!/http-equiv="Content-Security-Policy"/i.test(html), `${p}: no CSP to block it`);
  }
});

test('medir: summary() gives the site visits per day, page, referrer and country, and the period before', async () => {
  const DB = fakeD1();
  const now = Date.now(), d0 = new Date(now).toISOString().slice(0, 10), d8 = new Date(now - 8 * 864e5).toISOString().slice(0, 10);
  const ins = DB.raw.prepare('INSERT INTO site (day, page, ref, country, event, n) VALUES (?, ?, ?, ?, ?, ?)');
  ins.run(d0, '/', 'google.com', 'PT', 'view', 5); ins.run(d0, '/', '', 'BR', 'view', 2); ins.run(d0, '/is-it-safe/', 'github.com', 'PT', 'view', 1);
  ins.run(d0, '/', 'google.com', 'PT', 'download', 2); ins.run(d8, '/', '', 'PT', 'view', 4);
  const s = await summary(DB, { period: 7, f: { version: 'all', country: 'all', mode: 'all', link: 'all' }, dev: false, now });
  assert.equal(s.site.views, 8);
  assert.equal(s.site.downloads, 2);
  assert.equal(s.site.prevViews, 4);
  assert.deepEqual(s.site.days.at(-1), { date: d0, views: 8, downloads: 2 });
  assert.deepEqual(s.site.pages[0], { k: '/', views: 7, downloads: 2 });
  assert.deepEqual(s.site.refs.map((r) => r.k), ['google.com', '', 'github.com']);
  assert.deepEqual(s.site.countries, [{ k: 'PT', n: 6 }, { k: 'BR', n: 2 }]);
  // A database without the table: a warning, never zeros said as data
  const old = fakeD1();
  old.raw.exec('DROP TABLE site');
  const w = await summary(old, { period: 7, f: { version: 'all', country: 'all', mode: 'all', link: 'all' }, dev: false, now });
  assert.ok(w.warnings.some((x) => x.startsWith('site:')));
});

test('medir: the 10-minute cron keeps the highest live count of each hour, per view, and /api/live shows the peaks', async () => {
  const DB = fakeD1();
  const now = Date.now();
  const put = (id, ts, over = {}) => DB.raw.prepare('INSERT OR REPLACE INTO live (id, ts, app, cs2, prot, dev) VALUES (?, ?, ?, ?, ?, ?)').run(ID(id), ts, '0.1.0-beta.10', over.cs2 ?? 1, over.prot ?? 1, over.dev ?? 0);
  put(1, now); put(2, now); put(3, now, { dev: 1 }); put(4, now, { cs2: 0 }); put(0xe, now);
  await sampleLive(DB, { now, exclude: [ID(0xe)] });
  DB.raw.exec('DELETE FROM live WHERE id IN (' + [2, 4].map((n) => `'${ID(n)}'`).join(',') + ')');
  await sampleLive(DB, { now: now + 60_000, exclude: [ID(0xe)] });
  const h = Math.floor(now / 3600_000);
  const rows = DB.raw.prepare('SELECT hour, dev, n, game, prot FROM live_hourly ORDER BY dev').all().map((r) => ({ ...r }));
  if (Math.floor((now + 60_000) / 3600_000) === h) {
    assert.deepEqual(rows, [{ hour: h, dev: 0, n: 3, game: 2, prot: 3 }, { hour: h, dev: 1, n: 2, game: 2, prot: 2 }], 'the highest of the hour stays');
  }
  assert.ok(!JSON.stringify(DB.raw.prepare('SELECT * FROM live_hourly').all()).includes(ID(1)), 'no ID');
  // Yesterday's peak, in Lisbon hours, and hours with no sample are null (never 0)
  const y = now - 864e5;
  DB.raw.prepare('INSERT INTO live_hourly (hour, dev, n, game, prot) VALUES (?, 0, 7, 3, 7)').run(Math.floor(y / 3600_000));
  const p = await livePeaks(DB, { dev: false, now });
  assert.equal(p.ydayMax.n, 7);
  assert.equal(p.ydayMax.hour, lisbonHour(Math.floor(y / 3600_000) * 3600_000).hour);
  assert.equal(p.today[lisbonHour(now).hour], 3);
  assert.ok(p.yday.filter((v) => v === null).length >= 20, 'no sample = null');
  const token = await jwt(claims());
  const l = await (await dashboard.fetch(get('/api/live', token), { ...env0(), DB })).json();
  assert.equal(l.peaks.ydayMax.n, 7);
  // An old database without live_hourly: no peaks, no error
  const old = fakeD1(); old.raw.exec('DROP TABLE live_hourly');
  assert.equal((await livePeaks(old, { dev: false, now })).first, null);
});

const ghPublic = (stars, exe, { other = 4 } = {}) => async (url, init) => {
  if (String(url) === `${TEAM}/cdn-cgi/access/certs`) return Response.json({ keys: [jwk], public_cert: {}, public_certs: [] });
  if (init?.headers?.authorization) return new Response('{}', { status: 401 });
  const path = String(url).replace(/^https:\/\/api\.github\.com\/repos\/[^/]+\/[^/]+/, '');
  if (path === '') return Response.json({ stargazers_count: stars, forks_count: 1, subscribers_count: 2, open_issues_count: 3 });
  if (path.startsWith('/releases')) return Response.json([{ tag_name: 'v0.1.0-beta.10', assets: [{ name: 'CS2IPChanger.exe', download_count: exe }, { name: 'CS2IPChanger.exe.sha256', download_count: other }] },
    { tag_name: 'v0.1.0-beta.9', draft: true, assets: [{ name: 'CS2IPChanger.exe', download_count: 999 }] }]);
  return new Response('{}', { status: 404 });
};

test('medir: the daily cron keeps a picture of GitHub (no token), and downloads per day are the difference of two days in a row', async () => {
  const DB = fakeD1();
  const t0 = Date.parse('2026-11-01T03:07:00Z');
  await snapshotGithub(DB, {}, ghPublic(10, 100), t0);
  await snapshotGithub(DB, {}, ghPublic(12, 130), t0 + 864e5);
  await snapshotGithub(DB, {}, ghPublic(12, 131), t0 + 864e5 + 3600_000);   // again the same day: written over
  await snapshotGithub(DB, {}, ghPublic(13, 150), t0 + 3 * 864e5);           // a day missed before this one
  const h = await githubHistory(DB, t0 + 3 * 864e5);
  assert.deepEqual(h.days.map((d) => [d.day, d.exe, d.exeDay, d.stars, d.starsDay]), [
    ['2026-11-01', 100, null, 10, null], ['2026-11-02', 131, 31, 12, 2], ['2026-11-04', 150, null, 13, null]]);
  assert.equal(h.days[0].all, 104, 'all files, the draft left out');
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM gh_daily WHERE name LIKE '%beta.9%'").get().n, 0, 'drafts are not counted');
  // The picture of the public numbers never sends the token (v4: only the traffic reads, which need it, do)
  const auth = {};
  await snapshotGithub(DB, { GITHUB_TOKEN: 'secret' }, async (url, init) => { auth[String(url).replace(/^.*cs2-ip-changer/, '')] = init.headers.authorization; return ghPublic(1, 1)(url, init); }, t0);
  assert.equal(auth[''], undefined);
  assert.equal(auth['/releases?per_page=100'], undefined);
  assert.equal(auth['/traffic/views'], 'Bearer secret');
});

test('medir: /api/github without the token, or with it refused, takes stars and downloads from the public API; the traffic says it needs the token', async () => {
  globalThis.fetch = ghPublic(42, 17);
  const a = await github({}, globalThis.fetch, Date.now());
  assert.equal(a.stars, 42);
  assert.equal(a.releases.find((r) => !r.draft).exe, 17);
  assert.equal(a.views, null);
  assert.equal(a.source, 'public');
  assert.match(a.traffic, /GITHUB_TOKEN/);
  assert.ok(a.problems[0].includes('GITHUB_TOKEN'));
  resetGithubCache();
  const b = await github({ GITHUB_TOKEN: 'bad' }, globalThis.fetch, Date.now());
  assert.equal(b.stars, 42);
  assert.equal(b.source, 'public (the token failed)');
  assert.ok(b.problems[0].includes('refused (401)') && b.problems.some((p) => p.includes('/traffic/views')), b.problems.join(' | '));
  assert.ok(!b.problems.some((p) => /^\/: |^\/releases/.test(p)), 'one line for the token, not one per path');
  // The page: /api/github carries the history from gh_daily
  resetGithubCache();
  const DB = fakeD1();
  await snapshotGithub(DB, {}, ghPublic(1, 5), Date.now() - 864e5);
  await snapshotGithub(DB, {}, ghPublic(2, 9), Date.now());
  const g = await (await dashboard.fetch(get('/api/github', await jwt(claims())), { ...env0(), DB })).json();
  assert.equal(g.history.days.at(-1).exeDay, 4);
});

test('medir: the daily cron deletes rows with an install ID older than 12 months and keeps the counts with no ID', async () => {
  const DB = fakeD1();
  const now = Date.now();
  const old = new Date(now - (RETAIN_DAYS + 2) * 864e5).toISOString().slice(0, 10), fresh = new Date(now - 10 * 864e5).toISOString().slice(0, 10);
  for (const [d, id] of [[old, ID(1)], [fresh, ID(2)]]) {
    DB.raw.prepare('INSERT INTO pings (day, ts, id, app) VALUES (?, ?, ?, ?)').run(d, Date.parse(d), id, '0.1.0-beta.9');
    DB.raw.prepare('INSERT INTO clicks (day, id, name, n) VALUES (?, ?, ?, 1)').run(d, id, 'ChangeIp_Click');
    DB.raw.prepare('INSERT INTO counts (day, id, name, n) VALUES (?, ?, ?, 1)').run(d, id, 'ip_changed');
    DB.raw.prepare('INSERT INTO errors (day, id, app, type, at, n) VALUES (?, ?, ?, ?, ?, 1)').run(d, id, '0.1.0-beta.9', 'E', 'A.B');
    DB.raw.prepare('INSERT INTO live (id, ts, app) VALUES (?, ?, ?)').run(id, Date.parse(d), '0.1.0-beta.9');
    DB.raw.prepare('INSERT INTO heat (day, app, screen, kind, cell, n) VALUES (?, ?, ?, ?, ?, 1)').run(d, '0.1.0-beta.9', 'connection', 'c', '1,1');
    DB.raw.prepare('INSERT INTO env (day, k, v, n) VALUES (?, ?, ?, 1)').run(d, 'lang', 'pt');
    DB.raw.prepare('INSERT INTO site (day, page, ref, country, event, n) VALUES (?, ?, ?, ?, ?, 1)').run(d, '/', '', 'PT', 'view');
    DB.raw.prepare('INSERT INTO gh_daily (day, kind, name, n) VALUES (?, ?, ?, 1)').run(d, 'repo', 'stars');
    DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, 1)').run(d);
  }
  const out = await prune(DB, now, { slice: 1 });
  assert.deepEqual(out, { pings: 1, clicks: 1, counts: 1, errors: 1, live: 1 });
  for (const t of ['pings', 'clicks', 'counts', 'errors', 'live']) assert.deepEqual(DB.raw.prepare(`SELECT id FROM ${t}`).all().map((r) => r.id), [ID(2)], t);
  for (const t of ['heat', 'env', 'site', 'gh_daily']) assert.equal(DB.raw.prepare(`SELECT COUNT(*) n FROM ${t}`).get().n, 2, `${t} has no ID and stays`);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM budget').get().n, 1, 'old budget rows go');
});

test('medir: the crons are in wrangler.dashboard.jsonc, and scheduled() runs the right one', async () => {
  const cfg = readFileSync(new URL('../wrangler.dashboard.jsonc', import.meta.url), 'utf8');
  assert.match(cfg, /"triggers": \{ "crons": \["\*\/10 \* \* \* \*", "7 3 \* \* \*"\] \}/);
  assert.ok(cfg.includes(`"${DAILY_CRON}"`));
  assert.ok(!/"triggers"/.test(readFileSync(new URL('../wrangler.jsonc', import.meta.url), 'utf8')), 'the ingest Worker has no cron');
  const DB = fakeD1();
  const now = Date.now();
  DB.raw.prepare('INSERT INTO live (id, ts, app) VALUES (?, ?, ?)').run(ID(1), now, '0.1.0-beta.10');
  DB.raw.prepare('INSERT INTO pings (day, ts, id, app) VALUES (?, ?, ?, ?)').run('2020-01-01', 1, ID(9), '0.1.0-alpha.1');
  globalThis.fetch = ghPublic(5, 50);
  await dashboard.scheduled({ cron: '*/10 * * * *', scheduledTime: now }, { DB });
  assert.equal(DB.raw.prepare('SELECT n FROM live_hourly WHERE dev = 0').get().n, 1);
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings').get().n, 1, 'the 10-minute one never deletes');
  await dashboard.scheduled({ cron: DAILY_CRON, scheduledTime: now }, { DB });
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings').get().n, 0, 'the daily one deletes the old rows');
  assert.ok(DB.raw.prepare("SELECT n FROM gh_daily WHERE name = 'stars'").get().n === 5, 'and keeps the picture of GitHub');
  // The page itself still only reads: a POST is refused
  assert.equal((await dashboard.fetch(new Request('https://x/api/summary', { method: 'POST', headers: { 'cf-access-jwt-assertion': await jwt(claims()) } }), { ...env0(), DB })).status, 405);
});

test('medir: the v3 shows every block of the v2, and the Fontes switch starts off', () => {
  const app = ASSETS['app.js'].body, html = ASSETS['index.html'].body;
  assert.match(html, /<input type="checkbox" id="srcs">/, 'Fontes off');
  assert.ok(!/id="srcs" checked/.test(html));
  // One name per block or number of the v2 (worker/stats/dashboard/v2/app.js), as the v3 calls it
  const V2 = ['Instalações-dia', 'Novas', 'Voltam no dia seguinte', 'Minutos por instalação-dia', 'Trocas com IP novo', 'Partidas com lag', 'Sessões sem crash',
    'Por dia', 'Agora', 'Pico de ontem', 'Modo', 'O que melhorar', 'Onde clicam', 'Telemóvel ligado', 'Este ecrã', 'Até onde fazem scroll', 'Quase não se usa', 'Tempo por ecrã', '1.º ecrã',
    'Atalho contra rato', 'Caminhos', 'Definições mudadas', 'Quando usam', 'Voltam (por', 'dia 30', 'Idade da instalação', 'Dias com a app aberta', 'Sessões', 'Quanto duram',
    'Como arranca', 'Janela à vista', 'Países', 'Trocas por dia', 'Método', 'Quanto demora a troca', 'Tentativas até ficar boa', 'Qualidade depois da troca',
    'Trocaram outra vez por jitter', 'Phone setup', 'Do arranque a Protected', 'Quedas do telemóvel', 'Dados móveis por dia', 'Sessões com o CS2 aberto',
    'Horas com proteção ligada', 'Partidas', 'Rejoin voltou a entrar', 'Ping por partida', 'Jitter de envio', 'Jitter de receção', 'Mensagens atrasadas', 'Pacotes perdidos',
    'Kicks, bans e VPN vistos', 'Regiões mais bloqueadas', 'Crashes', 'Crashes por 100 sessões', 'Erros', 'Checks a vermelho', 'Fuga de IP de casa', 'Arranque até a app estar pronta',
    'Atualizações', 'Vieram de', 'Outros contadores', 'Cliques fora do mapa', 'Tamanho das listas', 'Versão do Windows', 'Adoção ao longo do tempo', 'Comparar duas versões',
    'Versões', 'Idioma', 'Windows', 'Rede do telemóvel', 'Janela', 'Escala', 'Marca do telemóvel', 'Android', 'Estrelas', 'Visitas ao repositório', 'Clones', 'Downloads do exe',
    '1.º arranque por download', 'Issues abertas', 'Visitas ao repositório por dia', 'De onde vêm ao GitHub', 'Downloads por versão', 'Páginas mais vistas no GitHub', 'GitHub em parte'];
  const miss = V2.filter((t) => !app.includes(t));
  assert.deepEqual(miss, [], 'blocks of the v2 missing from the v3');
  // And what is new: the site, downloads per day, the live history
  for (const t of ['Visitas ao site', 'Cliques em Download', 'Downloads no período', 'Ao vivo por hora', 'Páginas do site', 'De onde vêm ao site', 'Do site à app', 'Downloads do exe por dia'])
    assert.ok(app.includes(t), t);
  for (const p of ['ligacao', 'versoes']) assert.ok(app.includes(`['${p}',`), p);
});

// ------------------------------------------------------------------ security review (4 nov.): the cron, the site counter's budget, escaping

const forgetReq = (id) => Object.defineProperty(new Request('https://x/v1/forget', { method: 'POST', headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ v: 1, id, app: '0.1.0-beta.10' }) }), 'cf', { value: {} });

test('security: a cleanup cut short never leaves rows Delete my stats can no longer find (pings go last)', async () => {
  const DB = fakeD1();
  const now = Date.UTC(2027, 11, 1, 3, 7);
  const old = new Date(now - (RETAIN_DAYS + 3) * 864e5).toISOString().slice(0, 10);
  DB.raw.prepare('INSERT INTO pings (day, ts, id, app, minutes) VALUES (?, ?, ?, ?, 5)').run(old, 1, ID(0xabc), '0.1.0-beta.10');
  for (let i = 0; i < 3; i++) DB.raw.prepare('INSERT INTO clicks (day, id, name, n) VALUES (?, ?, ?, 1)').run(old, ID(0xabc), `B${i}`);
  DB.raw.prepare('INSERT INTO counts (day, id, name, n) VALUES (?, ?, ?, 1)').run(old, ID(0xabc), 'ip_changed');
  // A run that stops after 2 rows (the same as an error or a limit half way)
  const out = await prune(DB, now, { slice: 1, maxRows: 2 });
  assert.deepEqual(out, { clicks: 2, counts: 0, errors: 0, pings: 0, live: 0 });
  assert.equal(DB.raw.prepare('SELECT COUNT(*) n FROM pings').get().n, 1, 'the ping that finds the day is still there');
  assert.equal((await ingest.fetch(forgetReq(ID(0xabc)), { DB })).status, 204);
  for (const t of ['pings', 'clicks', 'counts']) assert.equal(DB.raw.prepare(`SELECT COUNT(*) n FROM ${t}`).get().n, 0, `${t}: Delete my stats still deletes it all`);
});

test('security: one cleanup run stays inside the free plan (rows deleted and queries), and a backlog goes over the next days', async () => {
  const DB = fakeD1();
  let q = 0;
  const counted = { ...DB, prepare: (sql) => { q++; return DB.prepare(sql); } };
  const now = Date.UTC(2027, 11, 1, 3, 7);
  const old = new Date(now - (RETAIN_DAYS + 3) * 864e5).toISOString().slice(0, 10);
  const ins = DB.raw.prepare('INSERT INTO counts (day, id, name, n) VALUES (?, ?, ?, 1)');
  DB.raw.exec('BEGIN'); for (let i = 0; i < 30000; i++) ins.run(old, ID(i % 500), 'x'); DB.raw.exec('COMMIT');
  const first = await prune(counted, now);
  assert.equal(first.counts, 20000, 'at most 20,000 rows a run');
  assert.ok(q <= 15, `${q} queries`);
  const second = await prune(DB, now + 864e5);
  assert.equal(second.counts, 10000, 'the rest the next day');
});

test('security: the daily cron takes the GitHub picture even when the cleanup fails, and still ends as failed', async () => {
  const DB = fakeD1();
  const broken = { ...DB, prepare: (sql) => (/^DELETE/.test(sql) ? { bind: () => ({ run: async () => { throw new Error('D1_ERROR: limit'); } }) } : DB.prepare(sql)) };
  globalThis.fetch = ghPublic(5, 50);
  await assert.rejects(dashboard.scheduled({ cron: DAILY_CRON, scheduledTime: Date.now() }, { DB: broken }), /D1_ERROR/);
  assert.equal(DB.raw.prepare("SELECT n FROM gh_daily WHERE name = 'stars'").get().n, 5);
});

test('security: the website counter\'s share of the budget is on the dashboard, with an alert when it fills up', async () => {
  const DB = fakeD1();
  const now = Date.now();
  DB.raw.prepare('INSERT INTO budget (day, rows) VALUES (?, ?)').run(`${today0()}:site`, 2800);
  const s = await summary(DB, { period: 7, f: F0, dev: false, now });
  assert.equal(s.fresh.budget.site, 2800);
  assert.equal(s.fresh.budget.siteCap, SITE_ROWS);
  const app = ASSETS['app.js'].body;
  assert.ok(app.includes("src: ['e', 'fresh.budget.site']") && app.includes('Fr.budget.siteCap'), 'Pede atenção says it');
});

test('security: every data-tip of the v3 that carries a version goes through esc(), and the privacy text names the country the site counter keeps', () => {
  const app = ASSETS['app.js'].body;
  for (const l of app.split('\n').filter((x) => /data-tip="\$\{/.test(x) && /short\(v\)/.test(x))) assert.ok(/data-tip="\$\{esc\(/.test(l), l.trim().slice(0, 90));
  const doc = readFileSync(new URL('../../../docs/PRIVACY-AND-SECURITY.md', import.meta.url), 'utf8');
  const row = doc.split('\n').find((l) => l.includes('from the website (not the app)'));
  assert.ok(/country/.test(row) && /no IP/.test(row), row);
});

// ------------------------------------------------------------------ Nov. 2026: performance review (publish/auditoria-2026-11-04/dashboard-v4/desempenho.md)

test('performance: the pages ask for their files with the version; those are kept, the rest checked by ETag, pages and API stay no-store', async () => {
  const token = await jwt(claims());
  const env = { ...env0(), DB: fakeD1() };
  const v = assetsVersion();
  assert.match(v, /^[0-9a-z]{1,8}$/);
  for (const [path, files] of [['/dashboard', ['/d/v5/home.css', '/d/v5/home.js']], ['/data', ['/d/style.css', '/d/v4/v4.css', '/d/ui-map.js', '/d/v4/real.js', '/d/v4/app.js']], ['/v3', ['/d/style.css', '/d/ui-map.js', '/d/real.js', '/d/app.js']], ['/v2', ['/d/v2/style.css', '/d/v2/app.js']]]) {
    const res = await dashboard.fetch(get(path, token), env);
    assert.equal(res.headers.get('cache-control'), 'no-store', path);
    const html = await res.text();
    for (const f of files) assert.ok(html.includes(`"${f}?v=${v}"`), `${path} asks for ${f}?v=`);
    assert.ok(!/"\/d\/[^"?]+"/.test(html), `${path}: no file left without the version`);
  }
  const kept = await dashboard.fetch(get(`/d/app.js?v=${v}`, token), env);
  assert.equal(kept.headers.get('cache-control'), 'private, max-age=31536000, immutable');
  assert.equal(await kept.text(), ASSETS['app.js'].body);
  // An old version (a tab opened before a deploy) or no version: checked every time, never kept as if it were this one
  for (const p of ['/d/app.js', '/d/app.js?v=old', '/ui/connection.png']) assert.equal((await dashboard.fetch(get(p, token), env)).headers.get('cache-control'), 'private, no-cache', p);
  const png = await dashboard.fetch(get('/ui/connection.png', token), env);
  assert.equal(png.headers.get('etag'), `W/"${v}"`);
  assert.deepEqual(new Uint8Array(await png.arrayBuffer()), new Uint8Array(Buffer.from(ASSETS['ui/connection.png'].body, 'base64')));
  // Decoded once per isolate: the second answer is the same bytes
  assert.deepEqual(new Uint8Array(await (await dashboard.fetch(get('/ui/connection.png', token), env)).arrayBuffer()), new Uint8Array(Buffer.from(ASSETS['ui/connection.png'].body, 'base64')));
  const same = new Request('https://stats-dashboard.example.workers.dev/ui/connection.png', { headers: { 'cf-access-jwt-assertion': token, 'if-none-match': `"${v}"` } });
  const r304 = await dashboard.fetch(same, env);
  assert.equal(r304.status, 304);
  assert.equal(await r304.text(), '');
  assert.equal((await dashboard.fetch(new Request('https://stats-dashboard.example.workers.dev/d/app.js', { headers: { 'cf-access-jwt-assertion': token, 'if-none-match': 'W/"other"' } }), env)).status, 200);
  // Still behind Access, also with the version or an ETag
  assert.equal((await dashboard.fetch(new Request(`https://stats-dashboard.example.workers.dev/d/app.js?v=${v}`, { headers: { 'if-none-match': `W/"${v}"` } }), env)).status, 403);
  for (const p of ['/api/live', '/api/summary', '/api/github']) assert.equal((await dashboard.fetch(get(p, token), env)).headers.get('cache-control'), 'no-store', p);
});

/** A D1 whose answers come on a later turn, counting how many round trips in a row an answer needed. */
function roundTrips(DB) {
  let done = 0, depth = 0;
  const wrap = (s) => ({
    bind: (...a) => wrap(s.bind(...a)),
    ...Object.fromEntries(['all', 'first', 'run'].map((m) => [m, () => { const d = done + 1; depth = Math.max(depth, d); return new Promise((r) => setTimeout(r, 1)).then(() => s[m]()).then((x) => { done = Math.max(done, d); return x; }); }])),
  });
  return { db: { prepare: (sql) => wrap(DB.prepare(sql)), batch: (l) => DB.batch(l) }, depth: () => depth };
}

test('performance: the summary, the live block and the GitHub history each wait one D1 round trip, not two or three', async () => {
  const DB = fakeD1();
  await ingest.fetch(post(report()), { DB });
  for (const [name, fn] of [['summary', (db) => summary(db, { period: 7, f: F0, dev: false, now: Date.now() })], ['live', (db) => liveNow(db, { dev: false, now: Date.now() })]]) {
    const rt = roundTrips(DB);
    const out = await fn(rt.db);
    assert.ok(out, name);
    assert.equal(rt.depth(), 1, name);
  }
  // /api/github: GitHub and the D1 read at once (GitHub here answers 403, so the public API is tried too)
  const token = await jwt(claims());
  const rt = roundTrips(DB);
  const t0 = Date.now();
  globalThis.fetch = async (url) => { url = String(url); if (url.includes('/cdn-cgi/access/certs')) return Response.json({ keys: [jwk] }); await new Promise((r) => setTimeout(r, 40)); return new Response('{}', { status: 403 }); };
  const res = await dashboard.fetch(get('/api/github', token), { ...env0(), DB: rt.db });
  assert.equal(res.status, 200);
  assert.ok(Array.isArray((await res.json()).history.days));
  assert.equal(rt.depth(), 1);
  assert.ok(Date.now() - t0 < 200, 'GitHub cold: two GitHub rounds, the D1 read inside them');
});

test('performance: the v3 draws without waiting for GitHub (only Aquisição and Versões wait), and moving between pages reuses the live block', () => {
  const app = ASSETS['app.js'].body, real = ASSETS['real.js'].body;
  assert.ok(app.includes("const ghWait = page === 'aquisicao' || page === 'versoes';") && app.includes('ghWait && ghP ? ghP : G0'));
  assert.ok(app.includes("if (D.G !== undefined && !ghOk(D.G)) out.push({ tone: 'bad', t: 'GitHub sem acesso'"), 'no "GitHub sem acesso" alert while it is still being read');
  assert.ok(app.includes('DATA.L = await V3.live(true)'), 'the 1-minute tick always reads the live block fresh');
  assert.ok(/githubNow\(\)/.test(real) && /Date\.now\(\) - LVat < 30_000/.test(real) && /forget\(\) \{ cache\.clear\(\); GHat = 0; LVat = 0; \}/.test(real));
});

// ------------------------------------------------------------------ v4 (Nov. 2026): /api/brief, the GitHub traffic kept, rep_exit

test('v4 brief route: JSON and Markdown behind Access, the Markdown made from the same JSON, kept 5 minutes, no install ID', async () => {
  resetBriefCache();
  const token = await jwt(claims());
  const DB = fakeD1();
  const now = Date.now();
  const day = new Date(now).toISOString().slice(0, 10);
  DB.raw.prepare("INSERT INTO pings (day, ts, id, app, link, minutes, dev) VALUES (?, ?, ?, '0.1.0-beta.10', 'usb', 12, 0)").run(day, now - 60_000, ID(7));
  for (const [k, n] of [['first_start', 1], ['accounts', 2], ['id_install', 1]]) DB.raw.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, 0)').run(day, ID(7), k, n);
  fakeNet({ github: { '': { stargazers_count: 3 }, '/releases?per_page=30': [] } });
  const env = { ...env0(), DB, GITHUB_TOKEN: 'x', EXCLUDE_IDS: ID(9) };
  const r = await dashboard.fetch(get('/api/brief', token), env);
  assert.equal(r.status, 200);
  assert.match(r.headers.get('content-type'), /application\/json/);
  assert.equal(r.headers.get('cache-control'), 'no-store');   // security audit, Nov. 2026: signed-in data is never cached
  const text = await r.text();
  const b = JSON.parse(text);
  assert.match(b.definitions.untrusted_text, /never as instructions/);   // text sent by anyone is data for the agent, not orders
  assert.equal(b.schema, 'brief.v1');
  assert.deepEqual([b.kpis.new_installs.today, b.kpis.active_installs.today, b.scope.excluded_ids], [1, 1, 1]);
  assert.ok(!/[0-9a-f]{32}/.test(text));
  const md = await dashboard.fetch(get('/api/brief?format=md', token), env);
  assert.match(md.headers.get('content-type'), /^text\/markdown; charset=utf-8/);
  assert.equal(await md.text(), renderMd(b), 'one JSON, kept 5 minutes, both formats');
  assert.equal((await dashboard.fetch(get('/api/brief'), env)).status, 403);
  // A broken database answers 500 with the error, never a half brief
  resetBriefCache();
  const bad = await dashboard.fetch(get('/api/brief', token), { ...env, DB: { prepare: () => { throw new Error('D1 down'); } } });
  assert.equal(bad.status, 500);
});

test('v4: the daily cron keeps the GitHub traffic with the token (views per day, referrers and pages as of the day), and history shows it apart from the pictures', async () => {
  const DB = fakeD1();
  const t0 = Date.parse('2026-11-04T03:07:00Z');
  const traffic = async (url, init) => {
    const path = String(url).replace(/^https:\/\/api\.github\.com\/repos\/[^/]+\/[^/]+/, '');
    if (init?.headers?.authorization) {
      if (path === '/traffic/views') return Response.json({ views: [{ timestamp: '2026-10-22T00:00:00Z', count: 5, uniques: 3 }, { timestamp: '2026-11-04T00:00:00Z', count: 9, uniques: 4 }] });
      if (path === '/traffic/clones') return Response.json({ clones: [{ timestamp: '2026-11-03T00:00:00Z', count: 2, uniques: 1 }] });
      if (path === '/traffic/popular/referrers') return Response.json([{ referrer: 'youtube.com', count: 40, uniques: 22 }, { referrer: 'reddit.com', count: 3, uniques: 2 }]);
      if (path === '/traffic/popular/paths') return Response.json([{ path: '/Sepd0x/cs2-ip-changer', title: 'x', count: 50, uniques: 30 }]);
      return new Response('{}', { status: 404 });
    }
    return ghPublic(5, 100)(url, init);
  };
  await snapshotGithub(DB, { GITHUB_TOKEN: 'good' }, traffic, t0);
  const h = await githubHistory(DB, t0);
  assert.deepEqual(h.days.map((d) => d.day), ['2026-11-04'], 'a day with only views is not a picture (no fake download peak)');
  assert.deepEqual(h.traffic.days, [{ day: '2026-10-22', views: 5, uniques: 3, clones: 0, clones_u: 0 }, { day: '2026-11-03', views: 0, uniques: 0, clones: 2, clones_u: 1 }, { day: '2026-11-04', views: 9, uniques: 4, clones: 0, clones_u: 0 }]);
  assert.deepEqual(h.traffic.refs, [{ name: 'youtube.com', n: 40, u: 22 }, { name: 'reddit.com', n: 3, u: 2 }]);
  assert.equal(h.traffic.refsDay, '2026-11-04');
  assert.equal(h.traffic.paths[0].name, '/Sepd0x/cs2-ip-changer');
  assert.deepEqual(h.byRelease['v0.1.0-beta.10'], [{ day: '2026-11-04', n: 100 }]);
  // Without the token, or refused: the picture as before and no traffic rows (never an error)
  const DB2 = fakeD1();
  await snapshotGithub(DB2, { GITHUB_TOKEN: 'bad' }, ghPublic(5, 100), t0);
  assert.equal(DB2.raw.prepare("SELECT COUNT(*) n FROM gh_daily WHERE kind LIKE 'traffic%'").get().n, 0);
  assert.equal(DB2.raw.prepare("SELECT COUNT(*) n FROM gh_daily WHERE kind = 'repo'").get().n, 4);
  assert.deepEqual(await trafficRows({}, traffic, '2026-11-04'), []);
  // The hourly retry looks for the day's repo row, not for any row of the day (today's views are keyed by today)
  const DB3 = fakeD1();
  DB3.raw.prepare("INSERT INTO gh_daily (day, kind, name, n) VALUES ('2026-11-04', 'traffic_views', 'count', 3)").run();
  let asked = 0;
  const orig = globalThis.fetch;
  globalThis.fetch = async (u, i) => { asked++; return ghPublic(5, 100)(u, i); };
  try { await dashboard.scheduled({ cron: '*/10 * * * *', scheduledTime: Date.parse('2026-11-04T05:02:00Z') }, { DB: DB3 }); } finally { globalThis.fetch = orig; }
  assert.ok(asked >= 2, 'the picture was taken again');
});

test('v4: a report marked exit is counted as rep_exit (data health), and the app cannot send rep_exit itself', async () => {
  const DB = fakeD1();
  assert.equal((await ingest.fetch(post(report({ id: ID(3), exit: true, counts: { rep_exit: 5, ip_changed: 1 } })), { DB })).status, 204);
  const rows = DB.raw.prepare("SELECT name, n FROM counts WHERE id = ? AND name IN ('rep_exit', 'ip_changed') ORDER BY name").all(ID(3));
  assert.deepEqual(rows.map((r) => [r.name, r.n]), [['ip_changed', 1], ['rep_exit', 1]]);
  assert.equal((await ingest.fetch(post(report({ id: ID(4), counts: { rep_exit: 1 } })), { DB })).status, 204);
  assert.equal(DB.raw.prepare("SELECT COUNT(*) n FROM counts WHERE id = ? AND name = 'rep_exit'").get(ID(4)).n, 0);
});

test('v4: an Access service token (no e-mail, its Client ID in common_name) reads /api/brief only, and only when listed', async () => {
  resetBriefCache();
  const svc = (cn) => { const c = claims({ common_name: cn }); delete c.email; return jwt(c); };
  const env = { ...env0(), DB: fakeD1(), SERVICE_TOKEN_IDS: 'abcd1234.access, other99.access' };
  const ok = await svc('abcd1234.access');
  assert.equal((await dashboard.fetch(get('/api/brief', ok), env)).status, 200);
  assert.equal((await dashboard.fetch(get('/api/brief?format=md', ok), env)).status, 200);
  for (const p of ['/dashboard', '/api/summary', '/api/live', '/api/github', '/classic', '/d/v4/app.js']) assert.equal((await dashboard.fetch(get(p, ok), env)).status, 403, p);
  assert.equal((await dashboard.fetch(get('/api/brief', await svc('stranger.access')), env)).status, 403, 'a Client ID not listed');
  assert.equal((await dashboard.fetch(get('/api/brief', ok), { ...env, SERVICE_TOKEN_IDS: '' })).status, 403, 'no list, no service token');
  // The owner's e-mail token still opens everything; a token with an e-mail is never taken as a service token
  assert.equal((await dashboard.fetch(get('/dashboard', await jwt(claims())), env)).status, 200);
  assert.equal((await dashboard.fetch(get('/api/brief', await jwt(claims({ email: 'x@y.z', common_name: 'abcd1234.access' }))), env)).status, 403);
});

import './v4.test.mjs';
import './brief.test.mjs';
import './v5.test.mjs';

// ------------------------------------------------------------------ v5 (5 Nov. 2026): two pages, the owner's and the data one
test('v5: /dashboard is the owner\'s page and /data (and /dados) the data page, all behind Access, with the strict CSP', async () => {
  const token = await jwt(claims());
  const env = { ...env0(), DB: fakeD1() };
  const home = await dashboard.fetch(get('/dashboard', token), env);
  assert.equal(home.status, 200);
  assert.match(home.headers.get('content-security-policy'), /script-src 'self';/);
  const html = await home.text();
  assert.ok(html.includes('<script type="module" src="/d/v5/home.js?v=') && html.includes('href="/data"'), 'the v5 page, with a link to the data page');
  for (const p of ['/data', '/dados']) {
    const r = await dashboard.fetch(get(p, token), env);
    assert.equal(r.status, 200, p);
    const t = await r.text();
    assert.ok(t.includes('/d/v4/app.js?v=') && t.includes('dados'), p);
    assert.equal((await dashboard.fetch(get(p), env)).status, 403, `${p} without Access`);
  }
  for (const f of ['/d/v5/home.js', '/d/v5/model.js', '/d/v5/home.css']) {
    const r = await dashboard.fetch(get(f, token), env);
    assert.equal(r.status, 200, f);
    assert.equal((await dashboard.fetch(get(f), env)).status, 403, `${f} without Access`);
  }
  // A service token (the agent) still reads /api/brief and nothing else, the two new pages included
  assert.equal((await dashboard.fetch(get('/dashboard'), env)).status, 403);
});

test('v5: no page links to the old versions any more (they still answer by address), and the owner\'s page has no jargon', async () => {
  const token = await jwt(claims());
  const env = { ...env0(), DB: fakeD1() };
  const v5 = ASSETS['v5/index.html'].body + ASSETS['v5/home.js'].body + ASSETS['v5/model.js'].body;
  const v4 = ASSETS['v4/index.html'].body + ASSETS['v4/app.js'].body;
  for (const s of [v5, v4]) assert.ok(!/href="\/(v2|v3|classic)"/.test(s), 'a link to v2, v3 or classic');
  for (const p of ['/v3', '/v2', '/classic']) assert.equal((await dashboard.fetch(get(p, token), env)).status, 200, p);
  // What the owner called useless on the v4: methodology, units, time zones, links to old versions (it all lives on /data)
  const words = /instalação-dia|install-day|UTC|Lisboa|coorte|cohort|EXCLUDE_IDS|clássico|não medido|Unidade:|brief/i;
  const visible = v5.replace(/^\s*\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '').replace(/'\/api\/brief'|\{ B, G, L \}|timeZone: 'Europe\/Lisbon'/g, '');
  assert.ok(!words.test(visible), `jargon on the owner's page: ${(visible.match(words) || [])[0]}`);
});

test('v5: /api/brief still answers (JSON and Markdown) and its series now covers 30 days with matches and minutes', async () => {
  const token = await jwt(claims());
  const env = { ...env0(), DB: fakeD1() };
  resetBriefCache();
  const r = await dashboard.fetch(get('/api/brief', token), env);
  assert.equal(r.status, 200);
  const b = await r.json();
  assert.equal(b.schema, 'brief.v1');
  assert.equal(b.series.days.length, 30);
  for (const k of ['matches', 'minutes', 'new_installs', 'active_installs']) assert.equal(b.series[k].length, 30, k);
  const md = await dashboard.fetch(get('/api/brief?format=md', token), env);
  assert.equal(md.status, 200);
  assert.ok((await md.text()).startsWith('# CS2 IP Changer · brief '));
});
