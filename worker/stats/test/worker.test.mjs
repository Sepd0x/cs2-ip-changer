// Tests of both Workers, no network: node --test (from worker/stats)
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { fakeD1 } from './d1.mjs';
import ingest, { readCapped } from '../src/index.js';
import dashboard, { collect, page, github, excludeIds, resetGithubCache, compareVersions, readable } from '../src/dashboard.js';
import { verifyAccess, resetAccessCache } from '../src/access.js';

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
  globalThis.fetch = async (url, init) => { auth = init.headers.authorization; return Response.json({ stargazers_count: 1 }); };
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
  for (const [path, type] of [['/dashboard', 'text/html'], ['/d/app.js', 'text/javascript'], ['/d/real.js', 'text/javascript'], ['/d/style.css', 'text/css'], ['/ui/connection.png', 'image/png']]) {
    const res = await dashboard.fetch(get(path, token), env);
    assert.equal(res.status, 200, path);
    assert.ok(res.headers.get('content-type').startsWith(type), path);
    assert.match(res.headers.get('content-security-policy'), /script-src 'self';/);
    assert.equal((await dashboard.fetch(get(path), env)).status, 403, `${path} without Access`);
  }
  assert.equal((await dashboard.fetch(get('/d/../src/index.js', token), env)).status, 404);
  assert.equal((await dashboard.fetch(get('/api/summary'), env)).status, 403);
  assert.equal((await dashboard.fetch(get('/api/live'), env)).status, 403);
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
