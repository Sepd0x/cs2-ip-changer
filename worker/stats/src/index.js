// Usage stats for CS2 IP Changer (beta), with a random install ID (pseudonymous; the heat map and /v1/env have no ID). README > Telemetry says what the app sends.
//
// What is kept: a random ID made on the PC (fixed per install until beta.7, new every day in beta.8, fixed per install again
// since beta.9 so returns can be counted; never made from the PC or the user, and Delete my stats in the app deletes it all here), the app and Windows versions, the mode, how the phone is connected,
// minutes the app was open, the country (two letters, from Cloudflare), how many times each button or hotkey was used, a few counters (IP changes, kicks, bans marked,
// list sizes: servers, accounts, flagged players, "Open with CS2" programs and turned-off regions) and errors as type + code location. Never kept: the sender's IP (Cloudflare needs it to deliver the
// request and works out the country from it; this code only gets the country, never the IP), and nothing the app doesn't send (no IPs, servers, accounts, names). Logs are off (wrangler.jsonc).

const MAX_BODY = 16 * 1024;
// Rows written a day, index updates included (D1 counts them). Well under D1's free 100,000: the per-install limit below is keyed
// on an id the sender picks, so a flood with fresh ids stops here instead of using up the day's writes (or costing money).
const DAILY_ROWS = 40000;
// Same as schema.sql. wrangler deploy doesn't run the schema, so a database made before the budget existed gets the table here.
const BUDGET_TABLE = 'CREATE TABLE IF NOT EXISTS budget (day TEXT PRIMARY KEY, rows INTEGER NOT NULL)';
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
const RESERVED = /^(scr|path|age|act|set|reg|data)_|^(net_dc|id_install)$/;
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

/** Writes the batch inside the day's row budget (see DAILY_ROWS). cost = rows the statements write, index updates included. */
async function withinBudget(env, day, stmts, cost) {
  cost += 2;   // the budget row
  const readBudget = () => env.DB.prepare('SELECT rows FROM budget WHERE day = ?').bind(day).first();
  let used;
  try { used = await readBudget(); } catch (e) {
    if (!/no such table/i.test(String(e && e.message))) throw e;
    await env.DB.prepare(BUDGET_TABLE).run();
    used = await readBudget();
  }
  // Read first, so a refused report writes nothing: the budget only grows with reports that are kept
  if ((used ? used.rows : 0) + cost > DAILY_ROWS) return new Response('later', { status: 429 });
  stmts.push(env.DB.prepare('INSERT INTO budget (day, rows) VALUES (?, ?) ON CONFLICT (day) DO UPDATE SET rows = rows + excluded.rows')
    .bind(day, cost));
  try { await env.DB.batch(stmts); } catch (e) {
    // wrangler deploy doesn't run schema.sql: the beta.9 tables are made the first time they're needed
    if (!/no such table/i.test(String(e && e.message))) throw e;
    await env.DB.prepare(HEAT_TABLE).run();
    await env.DB.prepare(LIVE_TABLE).run();
    await env.DB.prepare(ENV_TABLE).run();
    await env.DB.batch(stmts);
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
  return withinBudget(env, day, stmts, 3);
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
 */
async function forget(request, env) {
  const b = await body(request);
  if (b instanceof Response) return b;
  const day = new Date().toISOString().slice(0, 10);
  const stmts = ['pings', 'clicks', 'counts', 'errors', 'live'].map((t) => env.DB.prepare(`DELETE FROM ${t} WHERE id = ?`).bind(b.id));
  return withinBudget(env, day, stmts, 60);
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
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

    // One report per install every 10 minutes at most: more is a loop or someone playing with the endpoint.
    const last = await env.DB.prepare('SELECT ts FROM pings WHERE id = ? ORDER BY ts DESC LIMIT 1').bind(b.id).first();
    if (last && now - last.ts < 10 * 60 * 1000) return new Response('later', { status: 429 });

    const stmts = [
      env.DB.prepare('INSERT INTO pings (day, ts, id, app, os, mode, link, minutes, country, dev) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)')
        .bind(day, now, b.id, b.app, word(b.os), word(b.mode), word(b.link), int(b.minutes, MAX_MINUTES), cc, dev),
    ];
    // Caps a little above what the app sends (about 130 buttons, counters and their bands, at most 30 errors)
    for (const [table, obj, max, cap, ok] of [['clicks', b.clicks, 100000, 150, (x) => NAME.test(x)], ['counts', b.counts, 1000000, 100, counterOk]]) {
      if (!plain(obj)) continue;
      for (const [name, n] of Object.entries(obj).slice(0, cap))
        if (ok(name) && int(n, max) !== null)
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
