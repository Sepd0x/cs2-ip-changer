// Usage stats for CS2 IP Changer (beta), with a random install ID (pseudonymous; the heat map and /v1/env have no ID). README > Telemetry says what the app sends.
// Nov. 2026: also the website's visit counter (/v1/site), with no ID and no cookie, only counts per day. Rows with an install ID are
// deleted after 12 months by the dashboard Worker's daily cron (src/dashboard.js), the added-up counts stay.
//
// What is kept: a random ID made on the PC (fixed per install until beta.7, new every day in beta.8, fixed per install again
// since beta.9 so returns can be counted; never made from the PC or the user, and Delete my stats in the app deletes it all here), the app and Windows versions, the mode, how the phone is connected,
// whether the sender's network is a hosting or cloud one (only yes or no, from Cloudflare's network number, to keep online virus scanners apart),
// minutes the app was open, the country (two letters, from Cloudflare), how many times each button or hotkey was used, a few counters (IP changes, kicks, bans marked,
// list sizes: servers, accounts, flagged players, "Open with CS2" programs and turned-off regions) and errors as type + code location. Never kept: the sender's IP (Cloudflare needs it to deliver the
// request and works out the country from it; this code only gets the country, never the IP), and nothing the app doesn't send (no IPs, servers, accounts, names). Logs are off (wrangler.jsonc).

const MAX_BODY = 16 * 1024;
// Rows written a day, index updates included (D1 counts them). Well under D1's free 100,000: the per-install limit below is keyed
// on an id the sender picks, so a flood with fresh ids stops here instead of using up the day's writes (or costing money).
export const DAILY_ROWS = 40000;
// Nov. 2026: the live signal (every 5 minutes from each open app) may use at most this much of the day, so it can never
// leave nothing for the reports. Delete my stats has its own small budget outside the day's, so a flood can't stop it.
export const LIVE_ROWS = 12000;
export const FORGET_ROWS = 2000;
// Nov. 2026: the website's visit counter (POST /v1/site) has its own small budget, outside the day's, so a flood of made-up
// visits can't take anything from the app's reports
export const SITE_ROWS = 3000;
// The website: the only origin that may send visits, and its pages (site/ and tools/site-pages.mjs; a test checks the list)
export const SITE_ORIGIN = 'https://sepd0x.github.io';
export const SITE_PAGES = ['/', '/change-ip-with-phone/', '/copy-cs2-config/', '/cs2-ip-ban/', '/cs2-region-picker/', '/hide-ip-cs2/', '/is-it-safe/',
  '/steam-account-switcher/', '/steam-profile-editor/'];
const SITE_TABLE = 'CREATE TABLE IF NOT EXISTS site (day TEXT NOT NULL, page TEXT NOT NULL, ref TEXT NOT NULL, country TEXT NOT NULL, event TEXT NOT NULL, n INTEGER NOT NULL, PRIMARY KEY (day, page, ref, country, event))';
const HOSTNAME = /^(?=.{3,80}$)[a-z0-9-]+(\.[a-z0-9-]+)+$/;
const BOT = /bot|crawl|spider|slurp|headless|lighthouse|preview/i;
// Same as schema.sql. wrangler deploy doesn't run the schema, so a database made before the budget existed gets the table here.
const BUDGET_TABLE = 'CREATE TABLE IF NOT EXISTS budget (day TEXT PRIMARY KEY, rows INTEGER NOT NULL)';
// The cap inside the database (schema.sql has the same text): the budget row can't go over its cap, and since every write is in
// one batch (a transaction) with its budget row, requests sent at the same moment can't all pass on the same reading.
const CAP = `CASE WHEN NEW.day LIKE '%:live' THEN ${LIVE_ROWS} WHEN NEW.day LIKE '%:forget' THEN ${FORGET_ROWS} ELSE ${DAILY_ROWS} END`;
// Nov. 2026: the website's share has triggers of its own (a database made before keeps the two above as they were, and these
// add the site's cap next to them, so nothing old has to be dropped)
export const BUDGET_TRIGGERS = [...['INSERT', 'UPDATE'].map((op) => `CREATE TRIGGER IF NOT EXISTS budget_cap_${op.toLowerCase()} BEFORE ${op} ON budget `
  + `WHEN NEW.rows > ${CAP} BEGIN SELECT RAISE(ABORT, 'over budget'); END`),
...['INSERT', 'UPDATE'].map((op) => `CREATE TRIGGER IF NOT EXISTS budget_site_${op.toLowerCase()} BEFORE ${op} ON budget `
  + `WHEN NEW.day LIKE '%:site' AND NEW.rows > ${SITE_ROWS} BEGIN SELECT RAISE(ABORT, 'over budget'); END`)];
// The database's budget table and triggers made once per database here (a no-op when schema.sql already made them)
const budgetReady = new WeakSet();
// One report per install every 10 minutes; one more, marked exit, inside that time for the report at close, sleep or sign-out
const GAP = 10 * 60 * 1000;
const ID = /^[0-9a-f]{32}$/;
const VERSION = /^\d+\.\d+\.\d+(-[0-9A-Za-z.-]{1,30})?$/;
const NAME = /^[A-Za-z0-9_]{1,48}$/;
const WORD = /^[a-z0-9_.-]{1,24}$/;
const LOCATION = /^[A-Za-z0-9_.<>`|+-]{1,120}$/;
const COUNTRY = /^[A-Z][A-Z0-9]$/;   // ISO 3166 (PT, BR, US…), or Cloudflare's XX (unknown) and T1 (Tor)
// Minutes in one report: what one day can hold. The app sends the minutes since its last report, and since it also reports at close
// that is under a day; more is someone inflating the numbers.
const MAX_MINUTES = 24 * 60;
// beta.9: the app's screens (Nav_Checked), the heat map grid (same as the dashboard's ui-map.js) and install age bands
const SCREEN = /^[a-z]{1,12}$/;
const CELL = /^(\d{1,2}),(\d{1,2})$/;
const COLS = 24, ROWS = 16;
const AGES = { '0': '0', '1': '1', '2-3': '2_3', '4-6': '4_6', '7-13': '7_13', '14-29': '14_29', '30+': '30p' };
// Hosting and cloud networks (Google, Amazon, Microsoft, OVH, Hetzner, DigitalOcean, Linode, Vultr, Alibaba, Tencent, Oracle, M247,
// Datacamp, Contabo, Scaleway, netcup). Online virus scanners run the exe there. Only a yes/no is kept (count net_dc), never the network.
const HOSTING = new Set([15169, 396982, 16509, 14618, 8075, 8068, 16276, 24940, 14061, 63949, 20473, 45102, 132203, 31898, 9009, 212238, 60068, 51167, 12876, 197540]);
const HEAT_TABLE = 'CREATE TABLE IF NOT EXISTS heat (day TEXT NOT NULL, app TEXT NOT NULL, screen TEXT NOT NULL, kind TEXT NOT NULL, cell TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (day, app, screen, kind, cell, dev))';
const LIVE_TABLE = 'CREATE TABLE IF NOT EXISTS live (id TEXT PRIMARY KEY, ts INTEGER NOT NULL, app TEXT NOT NULL, mode TEXT, link TEXT, screen TEXT, cs2 INTEGER, prot INTEGER, tray INTEGER, dev INTEGER NOT NULL DEFAULT 0)';
const LIVE_GAP = 4 * 60 * 1000;   // the app sends every 5 minutes
// beta.9 part 2: counters the app sends in bands (src/Core/Telemetry.cs). A name in one of these families must be one of its bands,
// so nothing else (an exact number, a made-up name) comes in through them. Names the Worker makes itself can't be sent as counters.
const BANDS = {
  m: ['valve', 'valve_lag', 'comm', 'comm_lag'],
  ping: ['lt30', '30_50', '50_80', '80_120', 'gt120'],
  jit: ['lt5', '5_10', '10_20', '20_40', 'gt40'],
  jitd: ['lt5', '5_10', '10_20', '20_40', 'gt40'],
  late: ['0', 'lt1', '1_3', '3_10', 'gt10'],
  loss: ['0', '1_10', '11_100', 'gt100'],
  sess: ['lt5', '5_15', '15_30', '30_60', '1_2h', 'gt2h'],
  st: ['lt1', '1_2', '2_4', '4_8', 'gt8'],
  tp: ['lt10', '10_30', '30_60', '1_3m', 'gt3m'],
  start: ['auto', 'hand'],
  upd: ['seen', 'accepted', 'declined', 'failed', 'done'],
  // Change IP (method, time, attempts, quality measured after it, retried for jitter), phone drops per session, Rejoin, the
  // first screen picked after the start, the cable hint
  chg: ['air', 'data', 'retry'],
  chgt: ['lt5', '5_10', '10_20', '20_40', 'gt40'],
  chga: ['1', '2', '3', '4p'],
  chgq: ['good', 'fair', 'bad'],
  drop: ['0', '1', '2', '3p'],
  rej: ['try', 'back', 'wait'],
  fs: ['connection', 'servers', 'regions', 'accounts', 'configs', 'profiles', 'log', 'settings'],
  cable: ['hint'],
  // The Checks panel's red rows when the report is built (Telemetry.CheckKeys) and Run leak test, passed or failed
  chk: ['egress', 'routes', 'cs2', 'kill', 'firewall', 'diff', 'data', 'quality', 'steam', 'pc', 'regions'],
  leak: ['ok', 'bad'],
};
const UPD_FROM = /^updfrom_\d+_\d+_\d+(_[0-9A-Za-z_]{1,20})?$/;
const DATA = ['lt250m', '250_500m', '500m_1g', '1_2g', 'gt2g'];
const REGION = /^[a-z]{3,4}[0-9]?$/;
const SCROLL = /^scl_[a-z]{1,12}_(0|25|50|75|100)$/;
// v4: rep_exit is the Worker's own (a report marked exit), never the app's
const RESERVED = /^(scr|path|age|act|set|reg|data)_|^(net_dc|id_install|rep_exit)$/;
// Same list as Telemetry.SettingNames: on/off settings and the mode, by name only
const SETTINGS = new Set(['GameMode', 'SteamOnEgress', 'KeepPcOffPhone', 'RequireGoodLink', 'CheckUpdates', 'SteamSilent', 'SteamOffline', 'StreamerMode',
  'AutoStreamer', 'AutoQuery', 'CleanupOnExit', 'CloseToTray', 'AutoJoinFollowed', 'SwitchIp', 'SwitchAccount', 'SwitchRejoin',
  'SwitchOpenWith', 'CommandSounds', 'AutoModeByQuality', 'NoPhone', 'Mode']);

/** A counter name the app may send: not one the Worker makes, and inside its family's bands when it has one. */
export function counterOk(name) {
  if (!NAME.test(name) || RESERVED.test(name)) return false;
  if (name.startsWith('scl_')) return SCROLL.test(name);
  if (name.startsWith('updfrom_')) return UPD_FROM.test(name);
  const cut = name.indexOf('_');
  const fam = cut > 0 ? name.slice(0, cut) : null;
  return fam && Object.hasOwn(BANDS, fam) ? BANDS[fam].includes(name.slice(cut + 1)) : true;
}

// The environment report (POST /v1/env): no ID and no country, once a day per install. Only these values are kept, as counts per day.
const ENV = {
  lang: (x) => typeof x === 'string' && /^[a-z]{2,3}$/.test(x),
  win: (x) => ['s', 'm', 'l'].includes(x),
  scale: (x) => ['100', '125', '150', '175p'].includes(x),
  build: (x) => Number.isInteger(x) && x >= 10000 && x <= 99999,
  brand: (x) => ['samsung', 'google', 'xiaomi', 'oneplus', 'motorola', 'oppo', 'realme', 'vivo', 'huawei', 'honor', 'nothing', 'sony', 'asus', 'nokia', 'zte', 'tcl', 'other'].includes(x),
  android: (x) => typeof x === 'string' && /^\d{1,2}$/.test(x) && +x >= 4 && +x <= 40,
  net: (x) => ['5g', '4g', '3g', '2g'].includes(x),
};
const ENV_TABLE = 'CREATE TABLE IF NOT EXISTS env (day TEXT NOT NULL, k TEXT NOT NULL, v TEXT NOT NULL, n INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (day, k, v, dev))';

/**
 * The body, read up to max bytes and no further. Content-Length can be missing (chunked) or wrong, and request.text() would
 * first take the whole thing into memory. Null when it is too big.
 */
export async function readCapped(request, max) {
  if (!request.body) return '';
  const reader = request.body.getReader();
  const parts = [];
  let size = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > max) { try { await reader.cancel(); } catch { /* gone already */ } return null; }
    parts.push(value);
  }
  const all = new Uint8Array(size);
  let at = 0;
  for (const p of parts) { all.set(p, at); at += p.byteLength; }
  return new TextDecoder('utf-8', { fatal: true }).decode(all);
}

const plain = (o) => o !== null && typeof o === 'object' && !Array.isArray(o);
const word = (x) => (typeof x === 'string' && WORD.test(x) ? x : null);
const int = (x, max) => (Number.isInteger(x) && x >= 0 && x <= max ? x : null);

/** The JSON body, or a Response to send back when it isn't one. */
async function body(request) {
  // Only JSON: a web page can't send that to another site without asking first (CORS preflight, which this Worker never
  // allows), so a page someone visits can't make their browser send made-up reports. The app sends application/json.
  if (!/^application\/json\s*(;|$)/i.test(request.headers.get('content-type') || '')) return new Response('json only', { status: 415 });
  const len = Number(request.headers.get('content-length') || 0);
  if (len > MAX_BODY) return new Response('too big', { status: 413 });
  let b;
  try {
    const text = await readCapped(request, MAX_BODY);
    if (text === null) return new Response('too big', { status: 413 });
    b = JSON.parse(text);
  } catch { return new Response('bad json', { status: 400 }); }
  if (!plain(b) || b.v !== 1 || !ID.test(b.id) || !VERSION.test(b.app)) return new Response('bad', { status: 400 });
  return b;
}

/** The budget rows a kind of request uses, with their caps: a report the day's, the live signal also its share, Delete my stats its own. */
function budgetKeys(day, kind) {
  if (kind === 'forget') return [[`${day}:forget`, FORGET_ROWS]];
  if (kind === 'site') return [[`${day}:site`, SITE_ROWS]];
  if (kind === 'live') return [[day, DAILY_ROWS], [`${day}:live`, LIVE_ROWS]];
  return [[day, DAILY_ROWS]];
}

const overBudget = (e) => /over budget/i.test(String(e && e.message));

/** Writes the batch inside the day's row budget (see DAILY_ROWS). cost = rows the statements write, index updates included. */
async function withinBudget(env, day, stmts, cost, kind = 'report') {
  if (!budgetReady.has(env.DB)) {
    // The table first, on its own: the reports need it. The triggers only close the race between requests at the same moment,
    // so a database that refuses them still takes reports with the read check below (and they are asked again next time).
    await env.DB.prepare(BUDGET_TABLE).run();
    try {
      await env.DB.batch(BUDGET_TRIGGERS.map((s) => env.DB.prepare(s)));
      budgetReady.add(env.DB);
    } catch { /* tried again on the next request */ }
  }
  const keys = budgetKeys(day, kind);
  cost += 2 * keys.length;   // the budget rows
  const used = new Map(((await env.DB.prepare(`SELECT day, rows FROM budget WHERE day IN (${keys.map(() => '?').join(',')})`)
    .bind(...keys.map(([k]) => k)).all()).results ?? []).map((r) => [r.day, r.rows]));
  // Read first, so a refused report writes nothing: the budget only grows with reports that are kept. The triggers stop the
  // ones that read the same number at the same moment.
  if (keys.some(([k, cap]) => (used.get(k) || 0) + cost > cap)) return new Response('later', { status: 429 });
  for (const [k] of keys)
    stmts.unshift(env.DB.prepare('INSERT INTO budget (day, rows) VALUES (?, ?) ON CONFLICT (day) DO UPDATE SET rows = rows + excluded.rows').bind(k, cost));
  try { await env.DB.batch(stmts); } catch (e) {
    if (overBudget(e)) return new Response('later', { status: 429 });
    // wrangler deploy doesn't run schema.sql: the beta.9 tables are made the first time they're needed
    if (!/no such table/i.test(String(e && e.message))) throw e;
    await env.DB.prepare(HEAT_TABLE).run();
    await env.DB.prepare(LIVE_TABLE).run();
    await env.DB.prepare(ENV_TABLE).run();
    await env.DB.prepare(SITE_TABLE).run();
    try { await env.DB.batch(stmts); } catch (e2) { if (overBudget(e2)) return new Response('later', { status: 429 }); throw e2; }
  }
  return new Response(null, { status: 204 });
}

/** POST /v1/live, every 5 minutes while the app is open: what is on now. One row per install-day ID, written over. */
async function live(request, env) {
  const b = await body(request);
  if (b instanceof Response) return b;
  const now = Date.now();
  const day = new Date(now).toISOString().slice(0, 10);
  let last = null;
  try { last = await env.DB.prepare('SELECT ts FROM live WHERE id = ?').bind(b.id).first(); } catch (e) {
    if (!/no such table/i.test(String(e && e.message))) throw e;
  }
  if (last && now - last.ts < LIVE_GAP) return new Response('later', { status: 429 });
  const yes = (x) => (x === true ? 1 : x === false ? 0 : null);
  const screen = typeof b.screen === 'string' && SCREEN.test(b.screen) ? b.screen : null;
  const stmts = [env.DB.prepare('INSERT INTO live (id, ts, app, mode, link, screen, cs2, prot, tray, dev) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?) '
    + 'ON CONFLICT (id) DO UPDATE SET ts = excluded.ts, app = excluded.app, mode = excluded.mode, link = excluded.link, screen = excluded.screen, '
    + 'cs2 = excluded.cs2, prot = excluded.prot, tray = excluded.tray, dev = excluded.dev')
    .bind(b.id, now, b.app, word(b.mode), word(b.link), screen, yes(b.cs2), yes(b.prot), yes(b.tray), b.dev === true ? 1 : 0)];
  return withinBudget(env, day, stmts, 3, 'live');
}

/**
 * POST /v1/env, once a day: the environment with no ID and no country (Cloudflare's country is not read here), added into the day's
 * counts. Nothing ties it to the daily report. Only the day's row budget limits it (there is no ID to limit by).
 */
async function envReport(request, env) {
  if (!/^application\/json\s*(;|$)/i.test(request.headers.get('content-type') || '')) return new Response('json only', { status: 415 });
  let b;
  try {
    const text = await readCapped(request, 2048);
    if (text === null) return new Response('too big', { status: 413 });
    b = JSON.parse(text);
  } catch { return new Response('bad json', { status: 400 }); }
  if (!plain(b) || b.v !== 1 || !VERSION.test(b.app)) return new Response('bad', { status: 400 });
  const day = new Date().toISOString().slice(0, 10);
  const dev = b.dev === true ? 1 : 0;
  const stmts = [];
  for (const [k, ok] of Object.entries(ENV)) {
    const v = b[k];
    if (v === null || v === undefined || !ok(v)) continue;
    stmts.push(env.DB.prepare('INSERT INTO env (day, k, v, n, dev) VALUES (?, ?, ?, 1, ?) ON CONFLICT (day, k, v, dev) DO UPDATE SET n = n + 1').bind(day, k, String(v), dev));
  }
  stmts.push(env.DB.prepare('INSERT INTO env (day, k, v, n, dev) VALUES (?, ?, ?, 1, ?) ON CONFLICT (day, k, v, dev) DO UPDATE SET n = n + 1').bind(day, 'reports', 'all', dev));
  return withinBudget(env, day, stmts, 2 * stmts.length);
}

/**
 * POST /v1/forget (Settings > Delete my stats): everything kept under this install's ID goes. The heat map and the environment
 * report have no ID, so nothing there is tied to it. The ID is random and only on that PC, so only that PC can ask.
 * Nov. 2026: an ID with nothing kept is answered at once, with one read on an index and nothing written; for one with rows, the
 * deletes only look at the days it reported (indexed), never the whole tables. Its own budget (FORGET_ROWS), outside the day's.
 */
async function forget(request, env) {
  const b = await body(request);
  if (b instanceof Response) return b;
  const day = new Date().toISOString().slice(0, 10);
  const kept = await env.DB.prepare('SELECT 1 AS y FROM pings WHERE id = ? LIMIT 1').bind(b.id).first();
  let liveRow = null;
  try { liveRow = await env.DB.prepare('SELECT 1 AS y FROM live WHERE id = ?').bind(b.id).first(); } catch (e) {
    if (!/no such table/i.test(String(e && e.message))) throw e;
  }
  if (!kept && !liveRow) return new Response(null, { status: 204 });
  const days = 'SELECT DISTINCT day FROM pings WHERE id = ?';
  const stmts = ['clicks', 'counts', 'errors'].map((t) => env.DB.prepare(`DELETE FROM ${t} WHERE day IN (${days}) AND id = ?`).bind(b.id, b.id));
  stmts.push(env.DB.prepare('DELETE FROM pings WHERE id = ?').bind(b.id));
  if (liveRow) stmts.push(env.DB.prepare('DELETE FROM live WHERE id = ?').bind(b.id));
  return withinBudget(env, day, stmts, 60, 'forget');
}

/** The CORS headers for the website: only its origin, and answers vary with the Origin header. No credentials. */
const siteHeaders = (extra = {}) => ({ 'access-control-allow-origin': SITE_ORIGIN, vary: 'Origin', 'cache-control': 'no-store', ...extra });

/**
 * POST /v1/site (Nov. 2026), from the website's pages: a visit or a click on Download, added into the day's counts by page,
 * referrer host and country. No cookie, no ID, no IP, nothing about the browser: { v: 1, p: page, r: referrer host or "", e: "view" | "download" }.
 * The page sends it with navigator.sendBeacon as text/plain (a simple request, no preflight), only from SITE_ORIGIN, and never
 * when the browser says Do Not Track or Global Privacy Control; the Worker also drops those, hosting networks and bots.
 */
async function siteHit(request, env) {
  if (request.headers.get('origin') !== SITE_ORIGIN) return new Response('origin', { status: 403 });
  const h = siteHeaders();
  const drop = () => new Response(null, { status: 204, headers: h });
  if (request.headers.get('dnt') === '1' || request.headers.get('sec-gpc') === '1') return drop();
  if (BOT.test(request.headers.get('user-agent') || '') || (request.cf && HOSTING.has(request.cf.asn))) return drop();
  if (!/^text\/plain\s*(;|$)/i.test(request.headers.get('content-type') || '')) return new Response('text only', { status: 415, headers: h });
  let b;
  try {
    const text = await readCapped(request, 512);
    if (text === null) return new Response('too big', { status: 413, headers: h });
    b = JSON.parse(text);
  } catch { return new Response('bad json', { status: 400, headers: h }); }
  const keys = plain(b) ? Object.keys(b) : [];
  if (!plain(b) || b.v !== 1 || !keys.every((k) => ['v', 'p', 'r', 'e'].includes(k)) || !SITE_PAGES.includes(b.p) || !['view', 'download'].includes(b.e)
    || (b.r !== undefined && typeof b.r !== 'string')) return new Response('bad', { status: 400, headers: h });
  // The referrer's host only, lower case, without www. The site itself is "" (a move between its own pages); anything odd is "other"
  let ref = String(b.r || '').toLowerCase().replace(/^www\./, '');
  if (ref === new URL(SITE_ORIGIN).hostname) ref = '';
  if (ref && !HOSTNAME.test(ref)) ref = 'other';
  const cc = request.cf && typeof request.cf.country === 'string' && COUNTRY.test(request.cf.country) ? request.cf.country : '?';
  const day = new Date().toISOString().slice(0, 10);
  const stmt = env.DB.prepare('INSERT INTO site (day, page, ref, country, event, n) VALUES (?, ?, ?, ?, ?, 1) ON CONFLICT (day, page, ref, country, event) DO UPDATE SET n = n + 1')
    .bind(day, b.p, ref, cc, b.e);
  const res = await withinBudget(env, day, [stmt], 2, 'site');
  return new Response(res.status === 204 ? null : res.body, { status: res.status, headers: h });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === '/v1/site') {
      if (request.method === 'OPTIONS')
        return request.headers.get('origin') === SITE_ORIGIN
          ? new Response(null, { status: 204, headers: siteHeaders({ 'access-control-allow-methods': 'POST', 'access-control-allow-headers': 'content-type', 'access-control-max-age': '86400' }) })
          : new Response('origin', { status: 403 });
      if (request.method === 'POST') return siteHit(request, env);
    }
    if (request.method === 'POST' && url.pathname === '/v1/live') return live(request, env);
    if (request.method === 'POST' && url.pathname === '/v1/env') return envReport(request, env);
    if (request.method === 'POST' && url.pathname === '/v1/forget') return forget(request, env);
    if (request.method !== 'POST' || url.pathname !== '/v1') return new Response('CS2 IP Changer stats. See the README.', { status: 404 });
    const b = await body(request);
    if (b instanceof Response) return b;
    const now = Date.now();
    const day = new Date(now).toISOString().slice(0, 10);
    const dev = b.dev === true ? 1 : 0;
    const cc = request.cf && typeof request.cf.country === 'string' && COUNTRY.test(request.cf.country) ? request.cf.country : null;
    const hosting = !!(request.cf && HOSTING.has(request.cf.asn));

    // One report per install every 10 minutes at most: more is a loop or someone playing with the endpoint. Nov. 2026: one more
    // inside those 10 minutes when it is marked exit (the app closing, the PC going to sleep or Windows signing out), so a short
    // session isn't lost. Apps before that never mark it and get the old rule.
    const recent = await env.DB.prepare('SELECT COUNT(*) AS n FROM pings WHERE id = ? AND ts > ?').bind(b.id, now - GAP).first();
    if ((recent ? recent.n : 0) > (b.exit === true ? 1 : 0)) return new Response('later', { status: 429 });

    const stmts = [
      env.DB.prepare('INSERT INTO pings (day, ts, id, app, os, mode, link, minutes, country, dev) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)')
        .bind(day, now, b.id, b.app, word(b.os), word(b.mode), word(b.link), int(b.minutes, MAX_MINUTES), cc, dev),
    ];
    // Caps a little above what the app sends (it stops at 140 buttons and 90 counters with their bands, 30 errors). The good
    // names are picked before the cap, so junk first in the body can't push real counters out.
    for (const [table, obj, max, cap, ok] of [['clicks', b.clicks, 100000, 150, (x) => NAME.test(x)], ['counts', b.counts, 1000000, 100, counterOk]]) {
      if (!plain(obj)) continue;
      for (const [name, n] of Object.entries(obj).filter(([name, n]) => ok(name) && int(n, max) !== null).slice(0, cap))
        stmts.push(env.DB.prepare(`INSERT INTO ${table} (day, id, name, n, dev) VALUES (?, ?, ?, ?, ?)`).bind(day, b.id, name, n, dev));
    }
    if (Array.isArray(b.errors))
      for (const e of b.errors.slice(0, 30))
        if (e && NAME.test(e.type || '') && LOCATION.test(e.at || '') && int(e.n, 100000) !== null)
          stmts.push(env.DB.prepare('INSERT INTO errors (day, id, app, type, at, n, dev) VALUES (?, ?, ?, ?, ?, ?, ?)').bind(day, b.id, b.app, e.type, e.at, e.n, dev));
    // beta.9 extras, kept as counters of this install-day: minutes per screen, moves between screens, the install's age band,
    // and whether it came from a hosting network (an online scanner, most likely)
    const extra = [];
    if (plain(b.screens))
      for (const [s, m] of Object.entries(b.screens).slice(0, 12)) if (SCREEN.test(s) && int(m, MAX_MINUTES) !== null && m > 0) extra.push([`scr_${s}`, m]);
    if (plain(b.paths))
      for (const [p, n] of Object.entries(b.paths).slice(0, 60)) {
        const [from, to, more] = p.split('>');
        if (more === undefined && SCREEN.test(from || '') && SCREEN.test(to || '') && from !== to && int(n, 10000) && `path_${from}_${to}`.length <= 48) extra.push([`path_${from}_${to}`, n]);
      }
    if (typeof b.age === 'string' && Object.hasOwn(AGES, b.age)) extra.push([`age_${AGES[b.age]}`, 1]);
    if (hosting) extra.push(['net_dc', 1]);
    // beta.9: the ID is kept per install (before, new every day). Marked, so the dashboard counts returns only for these IDs.
    if (b.idk === 'install') extra.push(['id_install', 1]);
    // v4 (spec R7): a report sent at close, sleep or sign-out, so the dashboard can say what share of the reports arrive that way
    if (b.exit === true) extra.push(['rep_exit', 1]);
    // Mobile data today in a band, and the regions turned off on the Regions page (Valve's public codes, like fra)
    if (DATA.includes(b.data)) extra.push([`data_${b.data}`, 1]);
    if (Array.isArray(b.regions))
      for (const r of [...new Set(b.regions)].slice(0, 60)) if (typeof r === 'string' && REGION.test(r)) extra.push([`reg_${r}`, 1]);
    // Days open in the last 7 (counted on the PC) and the settings changed from their factory value, by name
    if (Number.isInteger(b.active7) && b.active7 >= 1 && b.active7 <= 7) extra.push([`act_${b.active7}`, 1]);
    if (Array.isArray(b.settings))
      for (const s of [...new Set(b.settings)].slice(0, SETTINGS.size)) if (typeof s === 'string' && SETTINGS.has(s)) extra.push([`set_${s}`, 1]);
    for (const [name, n] of extra)
      stmts.push(env.DB.prepare('INSERT INTO counts (day, id, name, n, dev) VALUES (?, ?, ?, ?, ?)').bind(day, b.id, name, n, dev));
    // pings has two indexes, the other tables one: 3 rows for the ping, 2 for each other row
    let cost = 3 + 2 * (stmts.length - 1);
    // The heat map, added into the day's totals without the ID. Not from hosting networks: a scanner clicking around isn't a person.
    if (Array.isArray(b.heat) && !hosting)
      for (const h of b.heat.slice(0, 300)) {
        if (!Array.isArray(h) || h.length !== 4) continue;
        const [s, kind, cell, n] = h;
        const m = typeof cell === 'string' ? CELL.exec(cell) : null;
        if (!SCREEN.test(s || '') || !['c', 'd', 'r'].includes(kind) || !m || +m[1] >= COLS || +m[2] >= ROWS || !int(n, 10000)) continue;
        stmts.push(env.DB.prepare('INSERT INTO heat (day, app, screen, kind, cell, n, dev) VALUES (?, ?, ?, ?, ?, ?, ?) '
          + 'ON CONFLICT (day, app, screen, kind, cell, dev) DO UPDATE SET n = n + excluded.n').bind(day, b.app, s, kind, cell, n, dev));
        cost += 2;
      }
    return withinBudget(env, day, stmts, cost);
  },
};
