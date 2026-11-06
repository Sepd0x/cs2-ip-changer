// Stats page for the person who runs the project: a second Worker (wrangler.dashboard.jsonc) on the same database. The pages
// only read. It is its own Worker because Cloudflare Access protects a whole workers.dev address: on the stats Worker it would also stop
// the app's reports to /v1. Access sits in front of all of it, and every request checks the Access token too (access.js).
// GitHub numbers come from the API with a read-only token (secret GITHUB_TOKEN); without it, or when it fails, from GitHub's
// public API (stars and downloads, never the traffic). No scripts and no outside files on the /classic page.
// Nov. 2026, the cron (scheduled below, triggers in wrangler.dashboard.jsonc): every 10 minutes the live signal is counted into
// live_hourly; once a day a picture of GitHub's public numbers goes into gh_daily and rows with an install ID older than
// RETAIN_DAYS are deleted (added-up counts with no ID stay). The cron is the only thing here that writes.

import { verifyAccess } from './access.js';
import { params, summary, live, who, compareVersions } from './api.js';
import { ASSETS } from './assets.js';
import { buildBrief, renderMd } from './brief.js';
import { annotations } from './annotations.js';

export { compareVersions };

const REPO = /^[A-Za-z0-9_.-]{1,100}\/[A-Za-z0-9_.-]{1,100}$/;
const DAYS = [7, 30, 90];
// Sent as list sizes in bands (Telemetry.Band), not as counters: shown as how many install-days were in each band
const SIZES = ['servers', 'accounts', 'flags', 'open_with', 'regions_blocked'];
const BAND = { 0: '0', 1: '1', 2: '2-5', 6: '6-20', 21: '21-100', 101: '100+' };
const BANDS = Object.values(BAND);
// Counted once by the app on the day its usage stats notice is accepted (MainWindow): new users
const FIRST = 'first_start';
// Rows with an install ID (pings, clicks, counts, errors, live) are kept this long, then the daily cron deletes them
export const RETAIN_DAYS = 365;
export const DAILY_CRON = '7 3 * * *';
const GH_TABLE = 'CREATE TABLE IF NOT EXISTS gh_daily (day TEXT NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL, n INTEGER NOT NULL, PRIMARY KEY (day, kind, name))';
const LIVE_HOURLY_TABLE = 'CREATE TABLE IF NOT EXISTS live_hourly (hour INTEGER NOT NULL, dev INTEGER NOT NULL DEFAULT 0, n INTEGER NOT NULL, game INTEGER NOT NULL, prot INTEGER NOT NULL, PRIMARY KEY (hour, dev))';
const ymd = (t) => new Date(t).toISOString().slice(0, 10);
const noTable = (e) => /no such table/i.test(String(e && e.message));

/** "ChangeIp_Click" → "Change IP", "Tray_open" → "Tray: open", "ip_changed" → "IP changed": names from the app's code, readable. */
export function readable(name) {
  const s = String(name).replace(/_Click$/, '').replace(/^Tray_/, 'Tray: ').replace(/^Hotkey_/, 'Hotkey: ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/_/g, ' ').trim();
  const w = s.replace(/\b(ip|Ip)\b/g, 'IP').replace(/\bcs2\b/gi, 'CS2').replace(/\bvpn\b/gi, 'VPN').replace(/\bapi\b/gi, 'API');
  return w.charAt(0).toUpperCase() + w.slice(1);
}

const MODES = { game: 'Game only', cs2open: 'Whole PC while CS2 is open', always: 'Whole PC, always', nophone: 'No phone' };
const LINKS = { usb: 'USB cable', hotspot: 'Wi-Fi hotspot', other: 'Other adapter', none: 'Phone not connected' };
const LISTS = { servers: 'Servers', accounts: 'Accounts', flags: 'Flagged players', open_with: 'Open with CS2', open_with_admin: 'Open with CS2 (as admin)', regions_blocked: 'Regions off' };

export default {
  /** The cron: the live signal every 10 minutes, GitHub's public numbers and the 12-month cleanup once a day. */
  async scheduled(controller, env) {
    const now = controller?.scheduledTime || Date.now();
    const exclude = excludeIds(env.EXCLUDE_IDS);
    if (controller?.cron === DAILY_CRON) {
      // Each on its own: a cleanup that fails never leaves the day without its GitHub picture (the run still ends as failed)
      let failed = null;
      try { await prune(env.DB, now); } catch (e) { failed = e; }
      await snapshotGithub(env.DB, env, fetch, now).catch(() => false);
      if (failed) throw failed;
      return;
    }
    await sampleLive(env.DB, { now, exclude });
    // The daily picture failed (GitHub's public API has a small limit per address): tried again once an hour until there is one
    if (new Date(now).getUTCMinutes() < 10 && new Date(now).getUTCHours() >= 4) {
      let have = null;
      // v4: the day's picture is its repo row (traffic rows are keyed by the day GitHub counted, today's too)
      try { have = await env.DB.prepare("SELECT 1 AS y FROM gh_daily WHERE day = ? AND kind = 'repo' LIMIT 1").bind(ymd(now)).first(); } catch (e) { if (!noTable(e)) throw e; }
      if (!have) await snapshotGithub(env.DB, env, fetch, now).catch(() => false);
    }
  },
  async fetch(request, env) {
    const url = new URL(request.url);
    const gate = await verifyAccess(request, env);
    if (!gate.ok) return text('Forbidden', 403);
    // v4: a service token (the agent) reads the brief and nothing else
    if (gate.service && url.pathname !== '/api/brief') return text('Forbidden', 403);
    if (request.method !== 'GET' && request.method !== 'HEAD') return text('Method not allowed', 405);
    if (url.pathname === '/') return new Response(null, { status: 302, headers: { location: '/dashboard' } });
    // beta.9: the new dashboard (worker/stats/dashboard, packed into assets.js) and its JSON. The old page stays at /classic.
    const now0 = Date.now();
    if (url.pathname === '/api/summary' || url.pathname === '/api/live') {
      const p = params(url);
      try {
        // D2: the developer's own PCs (EXCLUDE_IDS) stay out of everyone's numbers here too, as on /classic; with ?dev=1 they are in
        const exclude = excludeIds(env.EXCLUDE_IDS);
        const data = url.pathname === '/api/live' ? await live(env.DB, { dev: p.dev, now: now0, exclude }) : await summary(env.DB, { ...p, now: now0, exclude });
        return json(data);
      } catch (e) { return json({ error: String(e?.message || e) }, 500); }
    }
    // v4 (Nov. 2026): the state in one call, for the owner's page and for the agent (spec dashboard-v4 7). An agent comes in with
    // an Access service token (CF-Access-Client-Id and -Secret), which Access turns into the same JWT checked above.
    if (url.pathname === '/api/brief') {
      const q = url.searchParams;
      const fmt = q.get('format') === 'md' ? 'md' : 'json';
      const date = /^\d{4}-\d{2}-\d{2}$/.test(q.get('date') || '') ? q.get('date') : null;
      try {
        const b0 = await cachedBrief(env, { now: now0, dev: q.get('dev') === '1', date });
        // ?definitions=0: the same brief without the 6 KB of definitions (the agent keeps them from one full call)
        const b = q.get('definitions') === '0' ? { ...b0, definitions: {}, definitions_omitted: true } : b0;
        // Nov. 2026 (security audit): signed-in data, so not kept by the browser either (SAFE says no-store, like the rest of the API)
        const head = { ...SAFE };
        if (fmt === 'md') return new Response(request.method === 'HEAD' ? null : renderMd(b), { headers: { 'content-type': 'text/markdown; charset=utf-8', ...head } });
        return new Response(request.method === 'HEAD' ? null : JSON.stringify(b), { headers: { 'content-type': 'application/json; charset=utf-8', ...head } });
      } catch (e) { return json({ error: String(e?.message || e) }, 500); }
    }
    if (url.pathname === '/api/github') {
      // The daily pictures (gh_daily): downloads and stars per day. Read fresh, the cached part is GitHub's answer.
      // Nov. 2026 (performance review): the D1 read runs while GitHub answers, not after it
      const [gh, history] = await Promise.all([github(env, fetch, now0), githubHistory(env.DB, now0).catch((e) => ({ days: [], error: String(e?.message || e) }))]);
      // v4: the markers of the time series (videos, posts, incidents; the releases come with GitHub's answer)
      return json({ ...gh, history, annotations: annotations() });
    }
    // Only the packed files: a name like __proto__ or toString is not one of them (Object.hasOwn)
    const file = (name) => (Object.hasOwn(ASSETS, name) ? ASSETS[name] : null);
    // 5 Nov. 2026 (v5): two pages. /dashboard is the owner's page (v5/: a few big numbers, one chart, plain sentences); /data (and
    // /dados) is the developer's page with every detail (the v4, its files under /d/v4/, now opening on the brief). The v3 (/v3),
    // the v2 (/v2) and /classic still answer by address, but no page links to them any more.
    const PAGES = { '/dashboard': 'v5/index.html', '/data': 'v4/index.html', '/dados': 'v4/index.html', '/v3': 'index.html', '/v2': 'v2/index.html' };
    const asset = Object.hasOwn(PAGES, url.pathname) ? file(PAGES[url.pathname]) : url.pathname.startsWith('/d/') ? file(url.pathname.slice(3))
      : url.pathname.startsWith('/ui/') ? file(url.pathname.slice(1)) : null;
    if (asset) return serveAsset(request, asset, url);
    if (url.pathname !== '/classic') return text('Not found', 404);
    const days = DAYS.includes(Number(url.searchParams.get('days'))) ? Number(url.searchParams.get('days')) : 30;
    const now = Date.now();
    const [stats, gh] = await Promise.all([
      collect(env.DB, { days, now, exclude: excludeIds(env.EXCLUDE_IDS) }).catch((e) => ({ error: String(e?.message || e) })),
      github(env, fetch, now),
    ]);
    return new Response(request.method === 'HEAD' ? null : page({ days, stats, gh, now, email: gate.email }), {
      headers: {
        'content-type': 'text/html; charset=utf-8',
        'cache-control': 'no-store',
        'content-security-policy': "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'self'; frame-ancestors 'none'",
        'x-content-type-options': 'nosniff',
        'referrer-policy': 'no-referrer',
        'x-frame-options': 'DENY',
      },
    });
  },
};

const text = (body, status) => new Response(body, { status, headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store' } });
const SAFE = { 'x-content-type-options': 'nosniff', 'referrer-policy': 'no-referrer', 'x-frame-options': 'DENY', 'cache-control': 'no-store' };
const json = (data, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'content-type': 'application/json; charset=utf-8', ...SAFE } });

// Nov. 2026 (performance review, publish/auditoria-2026-11-04/dashboard-v4/desempenho.md): the files went out with no-store, so
// every visit downloaded them again (the v3's scripts and styles, the v2's capture, 1 MB of captures on Na app), and each capture
// was decoded from base64 on every request (about 8 ms of CPU each, with the free plan's limit at 10 ms). Now: one version for the
// whole packed set (a hash of every file, worked out once per isolate); the two pages ask for their files with ?v=<version> and
// those are kept by this browser only (private, behind Access); any other file is checked with its ETag (304, no body), so a
// deploy shows at once. The pages themselves and the API stay no-store. The captures are decoded once per isolate.
let VERSION = null;
/**
 * The packed files' version: FNV-1a over every name, length and text, once per isolate. The captures (base64 PNG, 80% of the
 * set) only every 7th character: any change to a PNG re-compresses its data from that point on, and the whole set would cost
 * about 7 ms of CPU instead of 2.
 */
export function assetsVersion() {
  if (VERSION) return VERSION;
  let h = 0x811c9dc5;
  for (const name of Object.keys(ASSETS).sort()) {
    const a = ASSETS[name], s = `${name}\0${a.body.length}\0${a.body}`, step = a.b64 ? 7 : 1;
    for (let i = 0; i < s.length; i += i < name.length + 12 ? 1 : step) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); }
  }
  return (VERSION = (h >>> 0).toString(36));
}
const BODIES = new Map();
function bodyOf(a) {
  let b = BODIES.get(a);
  if (b !== undefined) return b;
  if (a.b64) {
    const s = atob(a.body);
    b = new Uint8Array(s.length);
    for (let i = 0; i < s.length; i++) b[i] = s.charCodeAt(i);
  } else if (a.type.startsWith('text/html')) {
    // The page's own files with the version, so the browser can keep them (only "/d/..." in src and href; nothing else changes)
    b = a.body.replace(/\b(src|href)="(\/d\/[^"?#]+)"/g, (_, attr, path) => `${attr}="${path}?v=${assetsVersion()}"`);
  } else b = a.body;
  BODIES.set(a, b);
  return b;
}

/** A file of the new dashboard. Scripts only from here, no inline ones, nothing from outside. */
function serveAsset(request, a, url) {
  const page = a.type.startsWith('text/html');
  const v = assetsVersion();
  const etag = `W/"${v}"`;
  const cache = page ? 'no-store' : url?.searchParams.get('v') === v ? 'private, max-age=31536000, immutable' : 'private, no-cache';
  const headers = {
    'content-type': a.type, ...SAFE, 'cache-control': cache,
    'content-security-policy': "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
  };
  if (!page) {
    headers.etag = etag;
    const inm = request.headers.get('if-none-match') || '';
    if (inm.split(',').some((t) => t.trim().replace(/^W\//, '') === `"${v}"`)) return new Response(null, { status: 304, headers });
  }
  return new Response(request.method === 'HEAD' ? null : bodyOf(a), { headers });
}

const ALL = { version: 'all', country: 'all', mode: 'all', link: 'all' };
/** The tables in the database (the brief's health check), or null when the list can't be read. */
export async function tableNames(db) {
  try { return ((await db.prepare("SELECT name FROM sqlite_master WHERE type = 'table'").all()).results ?? []).map((r) => r.name); } catch { return null; }
}
/**
 * Everything the brief needs, in one D1 round trip next to GitHub: summary(30), live, the GitHub pictures, the table list. With
 * date (a past UTC day, up to 89 days back) the brief is that day's, as at its end: live and GitHub are not historical.
 */
export async function readBrief(env, { now, dev = false, date = null, fetchFn = fetch }) {
  const exclude = excludeIds(env.EXCLUDE_IDS);
  let at = now, historical = false;
  if (date && date < ymd(now) && date >= ymd(now - 89 * 864e5)) { at = Date.parse(date + 'T23:59:59Z'); historical = true; }
  const [S, L, G, GH, tables] = await Promise.all([
    summary(env.DB, { period: 30, f: ALL, dev, now: at, exclude, until: historical ? date : null }),
    historical ? null : live(env.DB, { dev, now: at, exclude }).catch(() => null),
    historical ? null : github(env, fetchFn, now).catch(() => null),
    githubHistory(env.DB, at, 120, historical ? date : null).catch(() => ({ days: [] })),
    tableNames(env.DB),
  ]);
  return buildBrief({ S, L, G, GH, tables, now: at, dev, excluded: exclude.length, historical, requested: date });
}
// The brief kept 5 minutes per isolate (the Cache API does nothing on workers.dev): the page and the agent asking twice cost one read
let briefMemo = new Map();
export function resetBriefCache() { briefMemo = new Map(); }
async function cachedBrief(env, opt) {
  const k = `${opt.dev ? 1 : 0}|${opt.date || ''}`;
  const hit = briefMemo.get(k);
  if (hit && opt.now - hit.at < 300_000) return hit.data;
  const data = await readBrief(env, opt);
  briefMemo.set(k, { at: opt.now, data });
  return data;
}

/** Install IDs of the developer's PCs (release builds without Mine in telemetry.json), as a secret: comma or space separated. */
export function excludeIds(s) {
  return String(s || '').split(/[\s,]+/).filter((x) => /^[0-9a-f]{32}$/.test(x)).slice(0, 50);
}

/** Every number on the page from the database, leaving out dev builds. An ID on one day = one install-day. */
export async function collect(db, { days, now, exclude = [] }) {
  const since = new Date(now - (days - 1) * 864e5).toISOString().slice(0, 10);
  const not = exclude.length ? ` AND id NOT IN (${exclude.map(() => '?').join(',')})` : '';
  const where = `dev = 0 AND day >= ?${not}`;
  const args = [since, ...exclude];
  const all = async (sql) => (await db.prepare(sql).bind(...args).all()).results ?? [];
  const installDays = "COUNT(DISTINCT day || '|' || id)";
  // One install can't be open more than a day in a day: made-up reports can't push the minutes past that
  const perInstallDay = `SELECT day, id, MIN(COALESCE(SUM(minutes), 0), 1440) AS m FROM pings WHERE ${where} GROUP BY day, id`;
  const [totals, perDay, fresh, versions, countries, modes, links, clicks, counts, sizes, errors] = await Promise.all([
    all(`SELECT COUNT(*) AS n, COALESCE(SUM(m), 0) AS minutes FROM (${perInstallDay})`),
    all(`SELECT day, COUNT(*) AS n, COALESCE(SUM(m), 0) AS minutes FROM (${perInstallDay}) GROUP BY day ORDER BY day`),
    // New users: the app counts first_start once, on the day its notice is accepted (one per install-day at most)
    all(`SELECT day, COUNT(DISTINCT id) AS n FROM counts WHERE ${where} AND name = '${FIRST}' AND n > 0 GROUP BY day`),
    all(`SELECT app AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY app ORDER BY n DESC, name`),
    all(`SELECT COALESCE(country, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT COALESCE(mode, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT COALESCE(link, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, SUM(n) AS n FROM clicks WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, SUM(n) AS n FROM counts WHERE ${where} AND name NOT IN (${[...SIZES, FIRST].map((s) => `'${s}'`).join(',')}) GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, n AS band, ${installDays} AS days FROM counts WHERE ${where} AND name IN (${SIZES.map((s) => `'${s}'`).join(',')}) GROUP BY name, n ORDER BY name, n`),
    all(`SELECT type, at, app, SUM(n) AS n, ${installDays} AS days FROM errors WHERE ${where} GROUP BY type, at, app ORDER BY n DESC LIMIT 40`),
  ]);
  const t = totals[0] ?? { n: 0, minutes: 0 };
  // Every day of the range, also the ones nobody opened the app
  const byDay = new Map(perDay.map((r) => [r.day, r]));
  const freshBy = new Map(fresh.map((r) => [r.day, r.n]));
  const series = [];
  for (let i = days - 1; i >= 0; i--) {
    const d = new Date(now - i * 864e5).toISOString().slice(0, 10);
    series.push({ day: d, n: byDay.get(d)?.n ?? 0, minutes: byDay.get(d)?.minutes ?? 0, fresh: freshBy.get(d) ?? 0 });
  }
  const newest = versions.map((r) => r.name).sort(compareVersions).at(-1) ?? null;
  return {
    since,
    installDays: t.n,
    minutes: t.minutes,
    avgMinutes: t.n ? Math.round(t.minutes / t.n) : 0,
    newUsers: series.reduce((a, d) => a + d.fresh, 0),
    newest,
    newestDays: versions.find((r) => r.name === newest)?.n ?? 0,
    perDay: series,
    versions, countries, modes, links, clicks, counts,
    sizes: SIZES.map((name) => ({ name, bands: sizes.filter((r) => r.name === name).map((r) => ({ band: BAND[r.band] ?? String(r.band), days: r.days })) })),
    errors,
  };
}

/** Every 10 minutes: installs with a live signal in the last 6 minutes, the highest of each UTC hour kept. Only counts. */
export async function sampleLive(db, { now, exclude = [] }) {
  await db.prepare(LIVE_HOURLY_TABLE).run();
  const hour = Math.floor(now / 3600_000);
  const stmts = [];
  for (const dev of [false, true]) {
    const W = who(dev, exclude);
    let r = null;
    try {
      r = await db.prepare(`SELECT COUNT(*) AS n, COALESCE(SUM(cs2 = 1), 0) AS game, COALESCE(SUM(prot = 1), 0) AS prot FROM live WHERE ${W.sql} AND ts >= ?`)
        .bind(...W.args, now - 6 * 60_000).first();
    } catch (e) { if (!noTable(e)) throw e; }
    stmts.push(db.prepare('INSERT INTO live_hourly (hour, dev, n, game, prot) VALUES (?, ?, ?, ?, ?) ON CONFLICT (hour, dev) DO UPDATE SET '
      + 'n = MAX(n, excluded.n), game = MAX(game, excluded.game), prot = MAX(prot, excluded.prot)').bind(hour, dev ? 1 : 0, r?.n || 0, r?.game || 0, r?.prot || 0));
  }
  await db.batch(stmts);
}

/**
 * Once a day: rows with an install ID older than RETAIN_DAYS go (pings, clicks, counts, errors on the day index, live by its
 * time), in slices so one run never holds the database long. The added-up tables with no ID (heat, env, site, live_hourly,
 * gh_daily) stay; budget rows older than 30 days go too (they only guard their own day).
 * Nov. 2026 (security review): pings go last, so a run cut short (an error, a limit) never leaves clicks, counts or errors
 * whose day Delete my stats can't find any more (it looks the days up in pings). And at most maxRows a run: deletes are writes
 * too (D1 free: 100,000 a day, index rows included), and the free plan stops a run at 50 queries. A day can write about 20,000
 * rows (DAILY_ROWS counts each with its index), so one run takes a whole day; a backlog goes over the next days.
 */
export async function prune(db, now, { slice = 5000, maxRows = 20000 } = {}) {
  const cut = ymd(now - RETAIN_DAYS * 864e5);
  const out = {};
  let left = maxRows;
  for (const t of ['clicks', 'counts', 'errors', 'pings']) {
    out[t] = 0;
    while (left > 0) {
      const take = Math.min(slice, left);
      const r = await db.prepare(`DELETE FROM ${t} WHERE rowid IN (SELECT rowid FROM ${t} WHERE day < ? LIMIT ${take})`).bind(cut).run();
      const n = Number(r?.meta?.changes ?? 0);
      out[t] += n; left -= n;
      if (n < take) break;
    }
  }
  out.live = 0;
  try { out.live = Number((await db.prepare('DELETE FROM live WHERE ts < ?').bind(now - RETAIN_DAYS * 864e5).run())?.meta?.changes ?? 0); } catch (e) { if (!noTable(e)) throw e; }
  await db.prepare('DELETE FROM budget WHERE substr(day, 1, 10) < ?').bind(ymd(now - 30 * 864e5)).run();
  return out;
}

const GH_HEADERS = { accept: 'application/vnd.github+json', 'user-agent': 'cs2ipc-stats-dashboard', 'x-github-api-version': '2022-11-28' };
const repoOf = (env) => (REPO.test(env.GITHUB_REPO || '') ? env.GITHUB_REPO : 'Sepd0x/cs2-ip-changer');

/** Once a day, from GitHub's public API (no token): stars, forks, watchers, open issues and every release file's download count. */
export async function snapshotGithub(db, env, fetchFn, now) {
  const repo = repoOf(env);
  const get = async (path) => { const r = await fetchFn(`https://api.github.com/repos/${repo}${path}`, { headers: GH_HEADERS }); if (!r.ok) throw new Error(`${path || '/'}: GitHub said ${r.status}`); return r.json(); };
  const [info, releases] = await Promise.all([get(''), get('/releases?per_page=100')]);
  const num = (x) => (Number.isFinite(x) && x >= 0 ? Math.floor(x) : 0);
  const d = ymd(now);
  const rows = [['repo', 'stars', num(info.stargazers_count)], ['repo', 'forks', num(info.forks_count)], ['repo', 'watchers', num(info.subscribers_count)], ['repo', 'issues', num(info.open_issues_count)]];
  for (const r of Array.isArray(releases) ? releases : []) {
    if (r.draft) continue;
    for (const a of r.assets ?? []) rows.push(['asset', `${String(r.tag_name).slice(0, 60)}/${String(a.name).slice(0, 120)}`, num(a.download_count)]);
  }
  // v4 (spec R4): GitHub keeps its traffic 14 days only, so the videos' peak would be gone two weeks later. With the token, the
  // views and clones of each day GitHub counted (keyed by that day, written over while GitHub still updates it) and the 14-day
  // referrers and popular pages as of today. About GitHub's own counts of the repo, never of a person.
  const traffic = await trafficRows(env, fetchFn, d, num);
  await db.prepare(GH_TABLE).run();
  const stmts = [...rows.slice(0, 400).map(([kind, name, n]) => [d, kind, name, n]), ...traffic].map(([day, kind, name, n]) => db.prepare('INSERT INTO gh_daily (day, kind, name, n) VALUES (?, ?, ?, ?) ON CONFLICT (day, kind, name) DO UPDATE SET n = excluded.n').bind(day, kind, name, n));
  await db.batch(stmts);
  return rows.length + traffic.length;
}

const cleanToken = (env) => String(env.GITHUB_TOKEN || '').replace(/^\uFEFF/, '').trim();
const DAY = /^\d{4}-\d{2}-\d{2}$/;
/** The traffic as gh_daily rows [day, kind, name, n], or none without a token or when GitHub refuses it (never an error). */
export async function trafficRows(env, fetchFn, d, num = (x) => (Number.isFinite(x) && x >= 0 ? Math.floor(x) : 0)) {
  const token = cleanToken(env);
  if (!token) return [];
  const repo = repoOf(env);
  const get = async (path) => { try { const r = await fetchFn(`https://api.github.com/repos/${repo}${path}`, { headers: { authorization: `Bearer ${token}`, ...GH_HEADERS } }); return r.ok ? await r.json() : null; } catch { return null; } };
  const [views, clones, refs, paths] = await Promise.all([get('/traffic/views'), get('/traffic/clones'), get('/traffic/popular/referrers'), get('/traffic/popular/paths')]);
  const out = [];
  for (const [kind, list] of [['traffic_views', views && views.views], ['traffic_clones', clones && clones.clones]])
    for (const v of Array.isArray(list) ? list.slice(0, 15) : []) {
      const day = String(v.timestamp || '').slice(0, 10);
      if (DAY.test(day)) out.push([day, kind, 'count', num(v.count)], [day, kind, 'uniques', num(v.uniques)]);
    }
  for (const [kind, list, field] of [['traffic_ref', refs, 'referrer'], ['traffic_path', paths, 'path']])
    for (const r of Array.isArray(list) ? list.slice(0, 10) : []) {
      const name = String(r[field] || '').slice(0, 120);
      if (name) out.push([d, kind, name, num(r.count)], [d, `${kind}_u`, name, num(r.uniques)]);
    }
  return out;
}

/**
 * The daily pictures as series: per day the exe downloads of every release added up, all files, stars, forks and watchers; and
 * the downloads of each day, the difference with the day before (only when both days have a picture, so a missed day never
 * makes a fake peak; the first day has none).
 */
export async function githubHistory(db, now, days = 120, until = null) {
  let rows = [];
  try { rows = (await db.prepare('SELECT day, kind, name, n FROM gh_daily WHERE day >= ? ORDER BY day').bind(ymd(now - (days - 1) * 864e5)).all()).results ?? []; } catch (e) { if (!noTable(e)) throw e; }
  // v4: a past day's brief sees only the pictures up to that day
  if (until) rows = rows.filter((r) => r.day <= until);
  const by = new Map();
  // v4: the traffic rows apart (a day with only views is not a picture: it would make a fake download peak the day after)
  const traffic = { days: [], refs: [], refsDay: null, paths: [], pathsDay: null };
  const tv = new Map();
  for (const r of rows) {
    if (r.kind.startsWith('traffic_')) {
      if (r.kind === 'traffic_views' || r.kind === 'traffic_clones') {
        const x = tv.get(r.day) || tv.set(r.day, { day: r.day, views: 0, uniques: 0, clones: 0, clones_u: 0 }).get(r.day);
        x[r.kind === 'traffic_views' ? (r.name === 'count' ? 'views' : 'uniques') : (r.name === 'count' ? 'clones' : 'clones_u')] = r.n;
      }
      continue;
    }
    if (r.kind !== 'repo' && r.kind !== 'asset') continue;
    const x = by.get(r.day) || by.set(r.day, { day: r.day, exe: 0, all: 0, stars: null, forks: null, watchers: null, issues: null }).get(r.day);
    if (r.kind === 'repo' && r.name in x) x[r.name] = r.n;
    else if (r.kind === 'asset') { x.all += r.n; if (/\.exe$/i.test(r.name)) x.exe += r.n; }
  }
  const list = [...by.values()];
  // v4: each release's exe downloads in each picture (Aquisição: per release, first days)
  const byRelease = {};
  for (const r of rows) if (r.kind === 'asset' && /\.exe$/i.test(r.name)) { const tag = r.name.split('/')[0]; (byRelease[tag] || (byRelease[tag] = [])).push({ day: r.day, n: r.n }); }
  for (let i = 0; i < list.length; i++) {
    const p = list[i - 1], x = list[i];
    const adj = p && ymd(Date.parse(p.day + 'T00:00:00Z') + 864e5) === x.day;
    x.exeDay = adj ? Math.max(0, x.exe - p.exe) : null;
    x.starsDay = adj && x.stars != null && p.stars != null ? x.stars - p.stars : null;
  }
  traffic.days = [...tv.values()].sort((a, b) => (a.day < b.day ? -1 : 1));
  // The referrers and popular pages of the newest snapshot, and every referrer seen in the days read (the videos' traffic stays)
  for (const [kind, key, dayKey] of [['traffic_ref', 'refs', 'refsDay'], ['traffic_path', 'paths', 'pathsDay']]) {
    const mine = rows.filter((r) => r.kind === kind), u = new Map(rows.filter((r) => r.kind === `${kind}_u`).map((r) => [`${r.day}|${r.name}`, r.n]));
    const lastDay = mine.reduce((m, r) => (r.day > m ? r.day : m), '');
    traffic[dayKey] = lastDay || null;
    traffic[key] = mine.filter((r) => r.day === lastDay).map((r) => ({ name: r.name, n: r.n, u: u.get(`${r.day}|${r.name}`) ?? null })).sort((a, b) => b.n - a.n);
  }
  traffic.refsByDay = rows.filter((r) => r.kind === 'traffic_ref').map((r) => ({ day: r.day, name: r.name, n: r.n }));
  return { days: list, first: list[0]?.day ?? null, traffic, byRelease };
}

let ghCache = { at: 0, data: null };
/** For the tests. */
export function resetGithubCache() { ghCache = { at: 0, data: null }; }

/** Stars, 14-day views and clones, referrers, downloads per release. Kept 10 minutes. A missing token or permission is said on the page. */
export async function github(env, fetchFn, now) {
  if (ghCache.data && now - ghCache.at < 600_000) return ghCache.data;
  const repo = repoOf(env);
  // A token pasted from the clipboard can bring a BOM, spaces or a line break with it: GitHub then says 401
  const token = String(env.GITHUB_TOKEN || '').replace(/^\uFEFF/, '').trim();
  let problems = [];
  // 4 Nov.: the secret was there but empty (pasted blank), and the page said it was missing: say which one it is
  if (!token) problems.push((env.GITHUB_TOKEN === undefined ? 'No GITHUB_TOKEN secret' : 'The GITHUB_TOKEN secret is empty (paste the token again with wrangler secret put)') + ': stars and downloads come from the public API; the traffic (views, clones, referrers) needs the token.');
  const get = async (path, auth = true) => {
    try {
      const res = await fetchFn(`https://api.github.com/repos/${repo}${path}`, { headers: auth ? { authorization: `Bearer ${token}`, ...GH_HEADERS } : GH_HEADERS });
      if (!res.ok) { problems.push(`${path || '/'}${auth ? '' : ' (public)'}: GitHub said ${res.status}`); return null; }
      return await res.json();
    } catch { problems.push(`${path || '/'}${auth ? '' : ' (public)'}: no answer from GitHub`); return null; }
  };
  const none = Promise.resolve(null);
  let [info, views, clones, referrers, paths, releases] = await Promise.all(token
    ? [get(''), get('/traffic/views'), get('/traffic/clones'), get('/traffic/popular/referrers'), get('/traffic/popular/paths'), get('/releases?per_page=30')]
    : [none, none, none, none, none, none]);
  // The token missing or refused: stars and releases are public, so they come from there (the traffic stays off)
  let source = token ? 'token' : 'public';
  if (!info || !Array.isArray(releases)) {
    const [pi, pr] = await Promise.all([info ? info : get('', false), Array.isArray(releases) ? releases : get('/releases?per_page=30', false)]);
    if (pi && !info) source = token ? 'public (the token failed)' : 'public';
    if (!pi && !info) source = null;
    info = info || pi; releases = Array.isArray(releases) ? releases : pr;
  }
  // The token refused but the public API answered: one line for it instead of one per path (the traffic ones stay)
  if (token && source && source.startsWith('public')) {
    const tok = problems.filter((p) => /^(\/|\/releases\?per_page=30): GitHub said/.test(p));
    problems = [`The GITHUB_TOKEN was refused (${tok[0] ? tok[0].split('said ')[1] : 'no answer'}): stars and downloads come from the public API.`, ...problems.filter((p) => !tok.includes(p))];
  }
  const traffic = views || clones || referrers || paths ? null : (token ? 'the token has no access to the traffic' : 'needs the GITHUB_TOKEN secret');
  const num = (x) => (Number.isFinite(x) ? x : 0);
  const data = {
    repo,
    problems, source, traffic,
    stars: info ? num(info.stargazers_count) : null,
    forks: info ? num(info.forks_count) : null,
    watchers: info ? num(info.subscribers_count) : null,
    issues: info ? num(info.open_issues_count) : null,
    views: views ? { count: num(views.count), uniques: num(views.uniques), days: (views.views ?? []).map((v) => ({ day: String(v.timestamp).slice(0, 10), n: num(v.count), u: num(v.uniques) })) } : null,
    clones: clones ? { count: num(clones.count), uniques: num(clones.uniques), days: (clones.clones ?? []).map((v) => ({ day: String(v.timestamp).slice(0, 10), n: num(v.count), u: num(v.uniques) })) } : null,
    referrers: Array.isArray(referrers) ? referrers.map((r) => ({ name: String(r.referrer), n: num(r.count), u: num(r.uniques) })) : null,
    paths: Array.isArray(paths) ? paths.slice(0, 10).map((p) => ({ name: String(p.path), n: num(p.count), u: num(p.uniques) })) : null,
    releases: Array.isArray(releases) ? releases.map((r) => ({
      tag: String(r.tag_name), date: String(r.published_at || r.created_at || '').slice(0, 10), draft: !!r.draft,
      downloads: (r.assets ?? []).reduce((s, a) => s + num(a.download_count), 0),
      exe: (r.assets ?? []).filter((a) => /\.exe$/i.test(a.name)).reduce((s, a) => s + num(a.download_count), 0),
    })) : null,
  };
  ghCache = { at: now, data };
  return data;
}

// ------------------------------------------------------------------ page

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const fmt = (n) => (n == null ? '-' : Number(n).toLocaleString('en-US'));

function bars(rows, { max = 12, label = (r) => r.name, value = (r) => r.n, note = () => '', title = label } = {}) {
  if (!rows?.length) return '<p class="none">Nothing yet.</p>';
  const top = Math.max(1, ...rows.map(value));
  return `<ul class="bars">${rows.slice(0, max).map((r) => `<li><span class="k" title="${esc(title(r))}">${esc(label(r))}</span>`
    + `<span class="bar"><i style="width:${Math.max(2, Math.round((100 * value(r)) / top))}%"></i></span><span class="v">${fmt(value(r))}${note(r)}</span></li>`).join('')}</ul>`;
}

/** Columns per day. part: a second number drawn at the bottom of each column in a lighter colour (new users). */
function columns(series, key, { part = null, tall = false } = {}) {
  const top = Math.max(1, ...series.map((d) => d[key]));
  const w = 100 / series.length;
  const rects = series.map((d, i) => {
    const h = Math.round((d[key] / top) * 100);
    const x = (i * w + w * 0.15).toFixed(2), cw = (w * 0.7).toFixed(2);
    const p = part ? Math.round((Math.min(d[part], d[key]) / top) * 100) : 0;
    const tip = `${d.day}: ${fmt(d[key])}${part ? `, ${fmt(d[part])} new` : ''}`;
    return `<rect x="${x}" y="${100 - h}" width="${cw}" height="${Math.max(h, d[key] ? 1 : 0)}"><title>${esc(tip)}</title></rect>`
      + (p ? `<rect class="part" x="${x}" y="${100 - p}" width="${cw}" height="${p}"><title>${esc(tip)}</title></rect>` : '');
  }).join('');
  return `<svg class="cols${tall ? ' tall' : ''}" viewBox="0 0 100 100" preserveAspectRatio="none" role="img" aria-label="per day">${rects}</svg>`
    + `<div class="axis"><span>${esc(series[0]?.day.slice(5))}</span><span>highest ${fmt(top)}</span><span>${esc(series.at(-1)?.day.slice(5))}</span></div>`;
}

/** List sizes as one table: a row per list, a column per band, how many users had that many. */
function sizesTable(sizes) {
  const used = BANDS.filter((b) => sizes.some((z) => z.bands.some((r) => r.band === b)));
  if (!used.length) return '<p class="none">Nothing yet.</p>';
  return `<table class="grid"><tr><th></th>${used.map((b) => `<th>${esc(b)}</th>`).join('')}</tr>${sizes.map((z) => `<tr><td>${esc(LISTS[z.name] ?? z.name)}</td>`
    + used.map((b) => { const v = z.bands.find((r) => r.band === b)?.days; return `<td class="num">${v ? fmt(v) : '<span class="dim">·</span>'}</td>`; }).join('') + '</tr>').join('')}</table>`;
}

const one = (n) => (n >= 10 ? fmt(Math.round(n)) : (Math.round(n * 10) / 10).toLocaleString('en-US'));
const tile = (label, value, sub = '', big = false) => `<div class="tile${big ? ' big' : ''}"><div class="tl">${esc(label)}</div><div class="tv">${value}</div>${sub ? `<div class="ts">${sub}</div>` : ''}</div>`;
const card = (title, body, cls = '') => `<section class="card${cls ? ' ' + cls : ''}"><h2>${esc(title)}</h2>${body}</section>`;

export function page({ days, stats, gh, now, email }) {
  const s = stats || {};
  const tabs = DAYS.map((d) => (d === days ? `<b>${d} days</b>` : `<a href="/classic?days=${d}">${d} days</a>`)).join('');
  // The ones not already in the top 12
  const least = s.clicks?.length > 12 ? s.clicks.slice(12).reverse().slice(0, 6) : [];
  const today = s.perDay?.at(-1), yesterday = s.perDay?.at(-2);
  const btn = { label: (r) => readable(r.name), title: (r) => r.name };
  const body = s.error
    ? card('Database', `<p class="bad">Could not read the stats: ${esc(s.error)}</p>`, 'wide')
    : [
      `<div class="tiles">`
        + tile('Users per day', one(s.installDays / days), `today ${fmt(today?.n)} · yesterday ${fmt(yesterday?.n)}`, true)
        + tile('New users', fmt(s.newUsers), `today ${fmt(today?.fresh)} · in ${days} days`, true)
        + tile('Minutes per user', fmt(s.avgMinutes), `a day · ${fmt(Math.round(s.minutes / 60))} hours in all`, true)
        + tile(s.newest ? `On ${s.newest.replace(/^0\.1\.0-/, '')}` : 'Newest version', s.installDays ? `${Math.round((100 * s.newestDays) / s.installDays)}%` : '-',
          s.newest ? `${fmt(s.newestDays)} of ${fmt(s.installDays)} users` : '', true)
        + `</div>`,
      card('Users per day', columns(s.perDay, 'n', { part: 'fresh', tall: true }) + '<p class="legend"><i></i>users <i class="part"></i>new users</p>', 'span2'),
      card('Versions', bars(s.versions, { max: 5 }) + '<h3>Countries</h3>' + bars(s.countries, { max: 6 })),
      card('Minutes open per day', columns(s.perDay, 'minutes'), 'span2'),
      card('Modes', bars(s.modes, { label: (r) => MODES[r.name] ?? r.name, title: (r) => r.name })
        + '<h3>Phone connected by</h3>' + bars(s.links, { label: (r) => LINKS[r.name] ?? r.name, title: (r) => r.name })),
      card('Most used buttons', bars(s.clicks, { max: 12, ...btn })),
      card('Counters', bars(s.counts, { max: 12, label: (r) => readable(r.name), title: (r) => r.name })
        + (least.length ? '<h3>Least used buttons</h3>' + bars(least, { max: 6, ...btn }) : '')),
      card('List sizes (users per size)', sizesTable(s.sizes)),
      card('Errors', s.errors?.length ? `<div class="scroll"><table><tr><th>Error</th><th>Where</th><th>Version</th><th>Times</th><th>Users</th></tr>${s.errors.map((e) => `<tr><td>${esc(e.type)}</td><td class="mono">${esc(e.at)}</td><td>${esc(e.app)}</td><td class="num">${fmt(e.n)}</td><td class="num">${fmt(e.days)}</td></tr>`).join('')}</table></div>` : '<p class="none">No errors.</p>', 'wide'),
    ].join('');
  const downloads = gh.releases?.filter((r) => !r.draft);
  const ghBody = [
    `<h2 class="section">GitHub</h2>`,
    `<div class="tiles">${tile('Stars', fmt(gh.stars), gh.stars != null ? `${fmt(gh.forks)} forks · ${fmt(gh.watchers)} watching` : '')}`
      + `${tile('Visits, 14 days', fmt(gh.views?.count), gh.views ? `${fmt(gh.views.uniques)} people` : '')}`
      + `${tile('Clones, 14 days', fmt(gh.clones?.count), gh.clones ? `${fmt(gh.clones.uniques)} people` : '')}`
      + `${tile('Downloads', fmt(downloads?.reduce((a, r) => a + r.exe, 0)), 'the exe, every release')}</div>`,
    gh.views ? card('Visits per day', columns(gh.views.days.map((d) => ({ day: d.day, n: d.n })), 'n')) : '',
    gh.clones ? card('Clones per day', columns(gh.clones.days.map((d) => ({ day: d.day, n: d.n })), 'n')) : '',
    gh.referrers ? card('Where visitors came from', bars(gh.referrers, { note: (r) => ` <small>(${fmt(r.u)})</small>` })) : '',
    gh.paths ? card('Most seen pages', bars(gh.paths, { label: (r) => r.name.replace(`/${gh.repo}`, '') || '/', note: (r) => ` <small>(${fmt(r.u)})</small>` })) : '',
    downloads ? card('Downloads per release', bars(downloads, { max: 30, label: (r) => `${r.tag} · ${r.date}`, value: (r) => r.downloads }), 'span2') : '',
    gh.problems?.length ? card('GitHub problems', `<ul class="plain">${gh.problems.map((p) => `<li class="bad">${esc(p)}</li>`).join('')}</ul>`, 'wide') : '',
  ].join('');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex,nofollow"><meta name="referrer" content="no-referrer"><title>CS2 IP Changer · stats</title><style>
:root{--bg:#0e0e0e;--bg2:#171717;--bg3:#1d1d1d;--line:#2a2a2a;--ink:#e4e4e4;--ink2:#adadad;--ink3:#888;--accent:#5ba8f0;--part:#a9d2f8;--blue:#2b74c2;--bad:#f07a6a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.45 "Segoe UI",system-ui,-apple-system,Roboto,sans-serif}
header{display:flex;flex-wrap:wrap;gap:12px;align-items:baseline;justify-content:space-between;padding:20px 20px 8px;max-width:1200px;margin:auto}
h1{font-size:20px;margin:0;font-weight:600}h1 span{color:var(--accent)}nav{display:flex;gap:6px}nav a,nav b{padding:5px 10px;border:1px solid var(--line);border-radius:8px;color:var(--ink2);text-decoration:none;font-weight:500}
nav b{background:var(--blue);border-color:var(--blue);color:#fff}main{max-width:1200px;margin:auto;padding:8px 20px 40px;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}
.tiles{grid-column:1/-1;display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}
.tile,.card{min-width:0;background:var(--bg2);border:1px solid var(--line);border-radius:12px;padding:14px 16px}.tl{color:var(--ink3);font-size:13px}.tv{font-size:28px;font-weight:600;margin-top:2px}.ts{color:var(--ink2);font-size:13px}
.tile.big .tv{font-size:36px}.wide{grid-column:1/-1}.span2{grid-column:span 2}
h2{font-size:14px;color:var(--ink2);font-weight:600;margin:0 0 10px;text-transform:uppercase;letter-spacing:.04em}h2.section{grid-column:1/-1;margin:14px 0 -4px}h3{font-size:13px;color:var(--ink3);margin:14px 0 4px;font-weight:500}
.bars{list-style:none;margin:0;padding:0}.bars li{display:grid;grid-template-columns:minmax(0,46%) 1fr auto;gap:8px;align-items:center;padding:3px 0}
.k{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--ink2);font-size:13px}.bar{background:var(--bg3);border-radius:4px;height:10px;overflow:hidden}.bar i{display:block;height:100%;background:var(--accent);border-radius:4px}
.v{font-variant-numeric:tabular-nums;font-size:13px;min-width:3ch;text-align:right}small{color:var(--ink3)}
.cols{width:100%;height:140px;display:block;background:var(--bg3);border-radius:8px}.cols.tall{height:220px}.cols rect{fill:var(--accent)}.cols rect.part{fill:var(--part)}
.axis{display:flex;justify-content:space-between;color:var(--ink3);font-size:12px;margin-top:4px}
.legend{margin:6px 0 0;color:var(--ink3);font-size:12px}.legend i{display:inline-block;width:10px;height:10px;border-radius:2px;background:var(--accent);margin:0 5px -1px 0}.legend i.part{background:var(--part);margin-left:12px}
.scroll{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);vertical-align:top}th{color:var(--ink3);font-weight:500}
.grid td:first-child{white-space:nowrap}.grid th,.grid td{padding:6px 4px}.grid th:not(:first-child),.num{text-align:right;font-variant-numeric:tabular-nums}.dim{color:var(--line)}
.mono{font-family:Consolas,ui-monospace,monospace;word-break:break-all}.none{color:var(--ink3);font-size:13px;margin:6px 0 0}.bad{color:var(--bad)}.plain{margin:0;padding-left:18px}
footer{max-width:1200px;margin:auto;padding:0 20px 30px;color:var(--ink3);font-size:12px}
@media(max-width:900px){main{grid-template-columns:repeat(2,minmax(0,1fr))}.tiles{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:600px){header,main,footer{padding-left:12px;padding-right:12px}main{grid-template-columns:minmax(0,1fr)}.span2{grid-column:auto}.tv,.tile.big .tv{font-size:24px}}
</style></head><body><header><h1>CS2 IP Changer <span>stats</span></h1><nav>${tabs}</nav></header>
<main>${body}${ghBody}</main><footer>Dev builds left out. A user = one install open on one day, so someone who opens the app on 3 days counts 3 times. The app's ID is random (never from the PC or the person) and, since beta.9, kept for the install (in beta.8 it was new every day). New users = installs that accepted the usage stats notice that day (also once for someone coming from beta.7). Days in UTC. GitHub numbers kept 10 minutes. ${esc(new Date(now).toISOString().slice(0, 16).replace('T', ' '))} UTC · ${esc(email)}</footer></body></html>`;
}
