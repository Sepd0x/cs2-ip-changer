// The new dashboard's data (beta.9): /api/summary and /api/live, behind the same Access check as the page. Only added-up
// numbers leave here, never an install ID or a row per install. Unit: the install-day (one install open on one day). Since
// beta.9 the app keeps its random ID per install (pseudonymous), so installs, returns and cohorts are also counted exactly.
//
// Real or automated: online virus scanners run the exe in a sandbox, accept the notice by themselves and report (5 of 5 outside
// installs up to 2 nov. 2026 were these: no Steam account, no click, 0 minutes). An install-day counts as real when it has a Steam
// account, a click or a minute; the rest is shown apart as "automated".

import { DAILY_ROWS, LIVE_ROWS, SITE_ROWS, FORGET_ROWS, contract } from './index.js';

const PERIODS = [1, 7, 30, 90];
// v4 (Nov. 2026, performance review 4.7): the days of each install are read for this long, and its first day apart with one row
// per install (MIN on the (id, ts) index), instead of every ping ever. Long enough for the weekly cohorts (7 weeks) and day 30.
export const INSTALL_DAYS = 120;
// v4: an install's first days (activation, where it gets stuck) are its day 0 to day 6, "within 7 days"
export const ACT_DAYS = 7;
// v4: builds before beta.10 send at start, at a UTC day change and at close only (data audit D1): their share is a data caveat
export const NEW_CADENCE = '0.1.0-beta.10';

/** Order of app versions: 0.1.0-beta.10 after 0.1.0-beta.9, a release after its betas. */
export function compareVersions(a, b) {
  const parse = (v) => { const [main, pre] = String(v).split('+')[0].split(/-(.*)/); return { main: main.split('.').map(Number), pre: pre ? pre.split('.') : null }; };
  const x = parse(a), y = parse(b);
  for (let i = 0; i < 3; i++) if ((x.main[i] || 0) !== (y.main[i] || 0)) return (x.main[i] || 0) - (y.main[i] || 0);
  if (!x.pre || !y.pre) return (x.pre ? -1 : 0) - (y.pre ? -1 : 0);
  for (let i = 0; i < Math.max(x.pre.length, y.pre.length); i++) {
    const p = x.pre[i], q = y.pre[i];
    if (p === undefined || q === undefined) return p === undefined ? -1 : 1;
    const np = /^\d+$/.test(p), nq = /^\d+$/.test(q);
    if (np && nq && Number(p) !== Number(q)) return Number(p) - Number(q);
    if (np !== nq) return np ? -1 : 1;
    if (!np && p !== q) return p < q ? -1 : 1;
  }
  return 0;
}
// Rows read per query. Nov. 2026: newest days first, so a cut drops the oldest, and the answer says which queries were cut
// (truncated) for the dashboard to show it; past this the queries need to add up in SQL instead of here.
export const MAX_ROWS = 50000;
// Sent as list sizes in bands (Telemetry.Band): one value a report, the same all day, so the day's highest is kept (never added up)
const SIZES = ['servers', 'accounts', 'flags', 'open_with', 'open_with_admin', 'regions_blocked'];
const SIZE_BAND = { 0: '0', 1: '1', 2: '2-5', 6: '6-20', 21: '21-100', 101: '100+' };
// How the phone was connected, best first: an install-day that was on the phone at any report counts with that link
const LINK_RANK = ['none', 'other', 'hotspot', 'usb'];

/**
 * The rows of one view: the release builds (dev 0) less the developer's own PCs (EXCLUDE_IDS), or with ?dev=1 the dev builds
 * and those PCs. <prefix> is a table alias ("p.").
 */
export function who(dev, exclude = [], prefix = '') {
  const ids = (exclude || []).filter((x) => /^[0-9a-f]{32}$/.test(x));
  if (!ids.length) return { sql: `${prefix}dev = ?`, args: [dev ? 1 : 0] };
  const list = ids.map(() => '?').join(',');
  return dev ? { sql: `(${prefix}dev = 1 OR ${prefix}id IN (${list}))`, args: ids } : { sql: `(${prefix}dev = 0 AND ${prefix}id NOT IN (${list}))`, args: ids };
}

/** The UTC hour (hours since 1970) as the weekday (0 Monday) and hour in Lisbon, where the dashboard is read. */
const LISBON = new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Lisbon', weekday: 'short', hour: '2-digit', hourCycle: 'h23' });
const WEEK = { Mon: 0, Tue: 1, Wed: 2, Thu: 3, Fri: 4, Sat: 5, Sun: 6 };
// v4 (performance): Intl is slow (about 10 us a call) and called once per report row; Lisbon is a whole number of hours from UTC,
// so each UTC hour is worked out once per isolate
const SLOTS = new Map(), HOURS = new Map();
export function lisbonSlot(hourIndex) {
  const hit = SLOTS.get(hourIndex);
  if (hit) return hit;
  const parts = Object.fromEntries(LISBON.formatToParts(new Date(hourIndex * 3600_000)).map((p) => [p.type, p.value]));
  const out = [WEEK[parts.weekday] ?? 0, Number(parts.hour) % 24];
  if (SLOTS.size > 20000) SLOTS.clear();
  SLOTS.set(hourIndex, out);
  return out;
}
/** A time (ms) as the date and hour in Lisbon: the v3 "Por hora" counts in Lisbon hours, like "Quando usam". */
const LISBON_DAY = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Lisbon', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', hourCycle: 'h23' });
export function lisbonHour(ts) {
  const h = Math.floor(ts / 3600_000);
  const hit = HOURS.get(h);
  if (hit) return { ...hit };
  const p = Object.fromEntries(LISBON_DAY.formatToParts(new Date(h * 3600_000)).map((x) => [x.type, x.value]));
  const out = { date: `${p.year}-${p.month}-${p.day}`, hour: Number(p.hour) % 24 };
  if (HOURS.size > 20000) HOURS.clear();
  HOURS.set(h, out);
  return { ...out };
}
const SCREENS = contract.screens;
const AGE = { age_0: '0', age_1: '1', age_2_3: '2-3', age_4_6: '4-6', age_7_13: '7-13', age_14_29: '14-29', age_30p: '30+' };
// Counted by the app, not real counters for the charts (list sizes in bands, the beta.9 extras)
const NOT_COUNTERS = /^(servers|accounts|flags|open_with|open_with_admin|regions_blocked|scr_.*|path_.*|age_.*|act_.*|set_.*|reg_.*|data_.*|chk_.*|id_install|net_dc)$/;
// beta.9 part 2: the bands the app sends (same as index.js BANDS), in the order the charts draw them, with their labels
const BANDS = {
  ping: [['lt30', '<30'], ['30_50', '30-50'], ['50_80', '50-80'], ['80_120', '80-120'], ['gt120', '>120']],
  jit: [['lt5', '<5'], ['5_10', '5-10'], ['10_20', '10-20'], ['20_40', '20-40'], ['gt40', '>40']],
  late: [['0', '0%'], ['lt1', '<1%'], ['1_3', '1-3%'], ['3_10', '3-10%'], ['gt10', '>10%']],
  loss: [['0', '0'], ['1_10', '1-10'], ['11_100', '11-100'], ['gt100', '>100']],
  sess: [['lt5', '<5 min'], ['5_15', '5-15'], ['15_30', '15-30'], ['30_60', '30-60'], ['1_2h', '1-2 h'], ['gt2h', '>2 h']],
  st: [['lt1', '<1 s'], ['1_2', '1-2 s'], ['2_4', '2-4 s'], ['4_8', '4-8 s'], ['gt8', '>8 s']],
  chgt: [['lt5', '<5 s'], ['5_10', '5-10 s'], ['10_20', '10-20 s'], ['20_40', '20-40 s'], ['gt40', '>40 s']],
  chga: [['1', '1'], ['2', '2'], ['3', '3'], ['4p', '4+']],
  drop: [['0', '0'], ['1', '1'], ['2', '2'], ['3p', '3+']],
  data: [['lt250m', '<250 MB'], ['250_500m', '250-500'], ['500m_1g', '0,5-1 GB'], ['1_2g', '1-2 GB'], ['gt2g', '>2 GB']],
  tp: [['lt10', '<10 s'], ['10_30', '10-30 s'], ['30_60', '30-60 s'], ['1_3m', '1-3 min'], ['gt3m', '>3 min']],
};
// v4: the counters read per install-day (funnel, cohorts, real or automated, per day, per version, list sizes); every other one is
// summed by D1 (summary's sums 'sql')
const INSTALL_NAMES = ['first_start', 'accounts', 'net_dc', 'app_start', 'crash', 'ip_changed', 'ip_same', 'ip_no_network', 'ip_home_back', 'min_prot', 'm_valve', 'm_comm',
  'm_valve_lag', 'm_comm_lag', 'unclean_exit', 'leak_ok', 'leak_bad', 'log_error', 'upd_failed', 'rep_exit', 'cable_hint', 'cs2_start', 'upd_done', ...SIZES];
const INSTALL_GLOBS = ['tp_*', 'phone_*', 'chk_*', 'data_*', 'age_*'];
// Phone setup steps (PhoneSetup.Key of PhoneStep, contract.setup), in the app's order, with the dashboard's label
const SETUP_LABEL = { noadbtool: 'Sem ADB', adbfailed: 'ADB falhou', nousb: 'Sem cabo USB', usbdrivererror: 'Driver USB', nousbdebugging: 'Depuração USB desligada',
  unauthorized: 'Depuração por autorizar', offline: 'Telemóvel offline', tetheroff: 'Tethering desligado', wifion: 'Wi-Fi do telemóvel ligado', pconphone: 'PC no telemóvel', done: 'Pronto' };
const SETUP = contract.setup.map((k) => [k, SETUP_LABEL[k] ?? k]);
// The Checks panel's rows (chk_ + Telemetry.CheckKeys, contract.checks), by the title the app shows
const CHECK_TITLE = { egress: 'Phone connection', routes: 'Servers through the phone', cs2: 'CS2 only through the phone', kill: 'Block if the phone drops',
  firewall: 'Windows Firewall', diff: 'Phone IP is not your home IP', data: 'Mobile data', quality: 'Connection quality',
  steam: 'Steam through the phone', pc: 'Other apps', regions: 'Matchmaking regions' };
const CHECKS = contract.checks.map((k) => [k, CHECK_TITLE[k] ?? k]);
// Install age bands to the cohort table's columns (days after the install: 0, 1, 3, 7, 14, 30; each column runs to the next) and
// the band's first day. 2-3 and 4-6 both land in the "day 3" column (3 to 6 days).
export const COHORT = { age_0: [0, 0], age_1: [1, 1], age_2_3: [2, 2], age_4_6: [2, 4], age_7_13: [3, 7], age_14_29: [4, 14], age_30p: [5, 30] };
// Valve's public datacenter codes (Regions page) to the city, for the dashboard; any other shows as its code
const CITY = { fra: 'Frankfurt', mad: 'Madrid', waw: 'Varsóvia', ams: 'Amesterdão', lhr: 'Londres', par: 'Paris', sto: 'Estocolmo', vie: 'Viena', hel: 'Helsínquia',
  iad: 'Sterling', ord: 'Chicago', atl: 'Atlanta', lax: 'Los Angeles', dfw: 'Dallas', sea: 'Seattle', gru: 'São Paulo', scl: 'Santiago', lim: 'Lima', eze: 'Buenos Aires',
  hkg: 'Hong Kong', sgp: 'Singapura', tyo: 'Tóquio', syd: 'Sydney', dxb: 'Dubai', bom: 'Mumbai', maa: 'Chennai', jnb: 'Joanesburgo', sha: 'Xangai', can: 'Cantão', seo: 'Seul' };
const WINDOWS = { 19045: 'Windows 10 22H2', 22621: 'Windows 11 22H2', 22631: 'Windows 11 23H2', 26100: 'Windows 11 24H2', 26200: 'Windows 11 25H2' };
const ENV_LABEL = { win: { s: 'Pequena', m: 'Média', l: 'Grande' }, scale: { '100': '100%', '125': '125%', '150': '150%', '175p': '175%+' }, net: { '5g': '5G', '4g': '4G', '3g': '3G', '2g': '2G' },
  brand: { samsung: 'Samsung', google: 'Google', xiaomi: 'Xiaomi', oneplus: 'OnePlus', motorola: 'Motorola', oppo: 'OPPO', realme: 'realme', vivo: 'vivo', huawei: 'Huawei',
    honor: 'Honor', nothing: 'Nothing', sony: 'Sony', asus: 'ASUS', nokia: 'Nokia', zte: 'ZTE', tcl: 'TCL', other: 'Outra' } };
/** The dashboard's labels for the contract's names, so test/contract.test.mjs can check none is missing. */
export const LABELS = { bands: BANDS, brand: ENV_LABEL.brand, setup: SETUP_LABEL, checks: CHECK_TITLE };

/** One row per band, in order: { band, n } or, with byLink, { band, usb, hotspot }. */
function bandRows(fam, get) { return BANDS[fam].map(([b, label]) => ({ band: label, ...get(b) })); }

const day = (t) => new Date(t).toISOString().slice(0, 10);

/** Period, filters and dev from the query string: anything odd falls back to the default, never into the SQL. */
export function params(url) {
  const q = url.searchParams;
  const period = PERIODS.includes(Number(q.get('period'))) ? Number(q.get('period')) : 7;
  const f = {};
  for (const k of ['version', 'country', 'mode', 'link']) {
    const v = q.get(k);
    f[k] = v && v !== 'all' && /^[A-Za-z0-9_.+-]{1,40}$/.test(v) ? v : 'all';
  }
  return { period, f, dev: q.get('dev') === '1' };
}

/** Everything the dashboard draws for one period, added up. */
export async function summary(db, { period, f, dev, now, exclude = [], sums = 'sql', until = null }) {
  const since = day(now - (period - 1) * 864e5);
  // v4: a brief for a past day (until) reads nothing after that day (critic, 4 Nov.: it counted later sessions). A checked date,
  // written into the SQL as text, never a value from the request.
  const U = until && /^\d{4}-\d{2}-\d{2}$/.test(until) ? until : null;
  const ud = (col = 'day') => (U ? ` AND ${col} <= '${U}'` : '');
  const ut = (col = 'ts') => (U ? ` AND ${col} <= ${Math.floor(now)}` : '');
  const prevSince = day(now - (2 * period - 1) * 864e5);
  const d = dev ? 1 : 0;
  const W = who(dev, exclude), Wp = who(dev, exclude, 'p.');
  const truncated = [], warnings = [];
  const all = async (sql, ...args) => (await db.prepare(sql).bind(...args).all()).results ?? [];
  // Newest days first (ORDER BY day DESC): a cut drops the oldest days and is said in truncated, never silent
  const capped = async (name, sql, ...args) => {
    const r = await all(`${sql} LIMIT ${MAX_ROWS + 1}`, ...args);
    if (r.length > MAX_ROWS) { r.length = MAX_ROWS; truncated.push(name); }
    return r;
  };
  // Tables made by the Worker the first time (heat, env, live): missing in an old database is a note, not an empty block with no word
  const soft = (name, p) => p.catch((e) => { warnings.push(`${name}: ${String(e?.message || e)}`); return []; });
  const idsK = `SELECT DISTINCT id FROM counts WHERE name = 'id_install' AND ${W.sql}`;
  // v4 (performance review 4.7): with sums 'sql' the counters that only add up (bands, screens, scroll, settings, regions…) are
  // summed by D1 over the real install-days of the view, and only the ones needed per install are read per install-day. At 50
  // install-days a day that is several times fewer rows for the Worker (its CPU, 10 ms on the free plan). 'js' is the old read,
  // kept so a test can show both give the same numbers.
  const SQL = sums !== 'js';
  const Wc = who(dev, exclude, 'c.');
  const perInstall = SQL ? ` AND (name IN (${INSTALL_NAMES.map(() => '?').join(',')}) OR ${INSTALL_GLOBS.map(() => 'name GLOB ?').join(' OR ')})` : '';
  const perInstallArgs = SQL ? [...INSTALL_NAMES, ...INSTALL_GLOBS] : [];
  const [pings, counts, clicks, heat, errors, envRows, installDays, hours, last, lastAnyR, budgetR, recentR, errFirstR, siteR, firstR, sumsR] = await Promise.all([
    // D7: the version and the mode of the day are the newest report's, not the highest text ("0.1.0-beta.10" < "0.1.0-beta.9" as
    // text). D3: the link is the best the install-day had (USB, then hotspot), so a report from before the phone was up doesn't win.
    // v4: r = the reports of the install-day (data health: the share from builds before beta.10)
    capped('pings', `SELECT day, id, MIN(COALESCE(SUM(minutes), 0), 1440) AS m, SUBSTR(MAX(ts || '|' || app), 15) AS app, MAX(country) AS country,
         SUBSTR(MAX(ts || '|' || COALESCE(mode, '')), 15) AS mode, MAX(CASE link WHEN 'usb' THEN 3 WHEN 'hotspot' THEN 2 WHEN 'other' THEN 1 WHEN 'none' THEN 0 END) AS lk,
         MAX(os) AS os, COUNT(*) AS r FROM pings WHERE ${W.sql} AND day >= ?${ud()} GROUP BY day, id ORDER BY day DESC`, ...W.args, prevSince),
    capped('counts', `SELECT day, id, name, SUM(n) AS n, MAX(n) AS mx FROM counts WHERE ${W.sql} AND day >= ?${ud()}${perInstall} GROUP BY day, id, name ORDER BY day DESC`, ...W.args, prevSince, ...perInstallArgs),
    capped('clicks', `SELECT day, id, name, SUM(n) AS n FROM clicks WHERE ${W.sql} AND day >= ?${ud()} GROUP BY day, id, name ORDER BY day DESC`, ...W.args, prevSince),
    soft('heat', all(`SELECT screen, kind, cell, app, SUM(n) AS n FROM heat WHERE dev = ? AND day >= ?${ud()} GROUP BY screen, kind, cell, app`, d, since)),
    capped('errors', `SELECT day, id, type, at, app, SUM(n) AS n FROM errors WHERE ${W.sql} AND day >= ?${ud()} GROUP BY day, id, type, at, app ORDER BY day DESC`, ...W.args, prevSince),
    soft('env', all(`SELECT k, v, SUM(n) AS n FROM env WHERE dev = ? AND day >= ?${ud()} GROUP BY k, v`, d, since)),
    // v4: bounded to INSTALL_DAYS (it read every ping ever); the first day of each install comes from firstR below
    capped('installDays', `SELECT DISTINCT p.day, p.id FROM pings p JOIN (${idsK}) k ON k.id = p.id
         WHERE ${Wp.sql} AND p.day >= ?${ud('p.day')} ORDER BY p.day DESC`, ...W.args, ...Wp.args, day(now - (INSTALL_DAYS - 1) * 864e5)),
    // D9: when the app is open, from the reports' times (each install counts once an hour)
    capped('hours', `SELECT day, id, CAST(ts / 3600000 AS INTEGER) AS h FROM pings WHERE ${W.sql} AND day >= ?${ud()} GROUP BY day, id, h ORDER BY day DESC`, ...W.args, since),
    // The dashboard's freshness bar: the newest report in this view (last 90 days, on the day index)
    all(`SELECT MAX(ts) AS t FROM pings WHERE ${W.sql} AND day >= ?${ut()}`, ...W.args, day(now - 89 * 864e5)),
    // v3 (Nov. 2026), all small reads on the day index: the newest report from any PC (dev builds and EXCLUDE_IDS too), the day's
    // row budget (index.js), the reports of the last 3 days with their times (Por hora, yesterday at the same time) and, for each
    // error in the last 90 days, the first day and version it was seen in
    // Nov. 2026: the website's visit counter (no ID: never filtered, same for ?dev=1) for this period and the one before
    // Nov. 2026 (performance review): in the same Promise.all as the reads above, so the answer waits one D1 round trip, not two
    all(`SELECT MAX(ts) AS t FROM pings WHERE day >= ?${ut()}`, day(now - 89 * 864e5)),
    // v4: the last 14 days of every share (reports, live, site, forget) for Sistema e dados
    soft('budget', all(`SELECT day, rows FROM budget WHERE day >= ?${ud('substr(day, 1, 10)')}`, day(now - 13 * 864e5))),
    // v4: 8 days, for "the same weekday last week at the same time" too
    capped('recent', `SELECT day, id, ts, minutes FROM pings WHERE ${W.sql} AND day >= ?${ut()} ORDER BY day DESC`, ...W.args, day(now - 8 * 864e5)),
    all(`SELECT type, at, MIN(day) AS first, SUBSTR(MIN(day || '|' || app), 12) AS app FROM errors WHERE ${W.sql} AND day >= ?${ud()} GROUP BY type, at`, ...W.args, day(now - 89 * 864e5)),
    soft('site', all(`SELECT day, page, ref, country, event, SUM(n) AS n FROM site WHERE day >= ?${ud()} GROUP BY day, page, ref, country, event`, prevSince)),
    // v4: one row per install, its first report (pings_id_ts index)
    capped('firstSeen', `SELECT p.id, MIN(p.ts) AS t FROM pings p JOIN (${idsK}) k ON k.id = p.id WHERE ${Wp.sql}${ut('p.ts')} GROUP BY p.id`, ...W.args, ...Wp.args),
    // v4: every counter of the period added up by D1, per name, version and link, over the install-days that are real (an account,
    // a click or a minute, as real() below) and pass the filters (as match() below). One row per name, version and link.
    SQL ? capped('sums', `WITH d AS (SELECT day, id, MIN(COALESCE(SUM(minutes), 0), 1440) AS m, SUBSTR(MAX(ts || '|' || app), 15) AS app, MAX(country) AS country,
           SUBSTR(MAX(ts || '|' || COALESCE(mode, '')), 15) AS mode, MAX(CASE link WHEN 'usb' THEN 3 WHEN 'hotspot' THEN 2 WHEN 'other' THEN 1 WHEN 'none' THEN 0 END) AS lk
           FROM pings WHERE ${W.sql} AND day >= ?${ud()} GROUP BY day, id),
         a AS (SELECT day, id, MAX(n) AS a FROM counts WHERE ${W.sql} AND day >= ?${ud()} AND name = 'accounts' GROUP BY day, id),
         k AS (SELECT day, id, SUM(n) AS k FROM clicks WHERE ${W.sql} AND day >= ?${ud()} GROUP BY day, id),
         ok AS (SELECT d.day, d.id, d.app, CASE d.lk WHEN 3 THEN 'usb' WHEN 2 THEN 'hotspot' WHEN 1 THEN 'other' WHEN 0 THEN 'none' ELSE '?' END AS link
           FROM d LEFT JOIN a ON a.day = d.day AND a.id = d.id LEFT JOIN k ON k.day = d.day AND k.id = d.id
           WHERE (COALESCE(a.a, 0) >= 1 OR COALESCE(k.k, 0) > 0 OR d.m >= 1) AND (? = 'all' OR d.app = ?)
             AND (? = 'all' OR COALESCE(NULLIF(d.country, ''), '?') = ?) AND (? = 'all' OR COALESCE(NULLIF(d.mode, ''), '?') = ?))
         SELECT c.name, ok.app, ok.link, SUM(c.n) AS n, COUNT(DISTINCT c.day || '|' || c.id) AS days FROM counts c JOIN ok ON ok.day = c.day AND ok.id = c.id
         WHERE ${Wc.sql} AND c.day >= ?${ud('c.day')} AND (? = 'all' OR ok.link = ?) GROUP BY c.name, ok.app, ok.link`,
      ...W.args, since, ...W.args, since, ...W.args, since, f.version, f.version, f.country, f.country, f.mode, f.mode, ...Wc.args, since, f.link, f.link) : [],
  ]);

  // One entry per install-day, with what decides real or automated
  const key = (r) => `${r.day}|${r.id}`;
  const inst = new Map();
  for (const p of pings) inst.set(key(p), { day: p.day, id: p.id, m: p.m, app: p.app, country: p.country || '?', mode: p.mode || '?', link: LINK_RANK[p.lk] || '?', os: p.os || '?', r: p.r || 1, acc: 0, clicks: 0, c: {}, k: {}, dc: false });
  for (const c of counts) {
    const i = inst.get(key(c)); if (!i) continue;
    if (c.name === 'accounts') i.acc = Math.max(i.acc, c.mx ?? c.n);
    else if (c.name === 'net_dc') i.dc = true;
    // A list size is a state, sent again in later reports of the day: the highest band, never the sum of the bands
    i.c[c.name] = SIZES.includes(c.name) ? Math.max(i.c[c.name] || 0, c.mx ?? c.n) : (i.c[c.name] || 0) + c.n;
  }
  // v4: the Phone setup buttons (Setup_<step>) and its Help link, kept per install-day for "Onde encalham"
  for (const c of clicks) { const i = inst.get(key(c)); if (i) { i.clicks += c.n; if (/^(Setup_|PhoneHelp)/.test(c.name)) i.k[c.name] = (i.k[c.name] || 0) + c.n; } }
  const real = (i) => i.acc >= 1 || i.clicks > 0 || i.m >= 1;
  const match = (i) => (f.version === 'all' || i.app === f.version) && (f.country === 'all' || i.country === f.country)
    && (f.mode === 'all' || i.mode === f.mode) && (f.link === 'all' || i.link === f.link);

  const inWin = [], inPrev = [];
  let automated = 0, automatedDc = 0;
  const autoDays = {};
  for (const [k, i] of inst) {
    if (!real(i)) { if (i.day >= since) { automated++; if (i.dc) automatedDc++; autoDays[i.day] = (autoDays[i.day] || 0) + 1; } continue; }
    if (!match(i)) continue;
    (i.day >= since ? inWin : inPrev).push([k, i]);
  }
  const ok = new Set(inWin.map(([k]) => k));

  // Per day, every day of the period (also the empty ones)
  const days = [];
  // v4 adds, per day: the reports (and those from builds before beta.10), install-days with the link none, unclean exits, leak
  // tests failed, the home IP back, errors written to the Log, failed updates, reports sent at close (rep_exit) and the
  // automated install-days on a hosting network
  const dayRow = (date) => ({ date, users: 0, fresh: 0, minutes: 0, ipNew: 0, ipSame: 0, ipNone: 0, errors: 0, automated: 0, sessions: 0, crashes: 0, matches: 0, laggy: 0,
    reports: 0, oldReports: 0, usersOld: 0, none: 0, unclean: 0, leakBad: 0, leakOk: 0, homeBack: 0, logErr: 0, updFailed: 0, exits: 0, automatedDc: 0, updates: 0 });
  for (let t = period - 1; t >= 0; t--) days.push(dayRow(day(now - t * 864e5)));
  const byDay = new Map(days.map((x) => [x.date, x]));
  const addDay = (x, i) => {
    x.users++; x.minutes += i.m;
    x.fresh += i.c.first_start ? 1 : 0;
    x.ipNew += i.c.ip_changed || 0; x.ipSame += i.c.ip_same || 0; x.ipNone += i.c.ip_no_network || 0;
    x.sessions += i.c.app_start || 0; x.crashes += i.c.crash || 0;
    x.matches += (i.c.m_valve || 0) + (i.c.m_comm || 0); x.laggy += (i.c.m_valve_lag || 0) + (i.c.m_comm_lag || 0);
    x.reports += i.r; if (compareVersions(i.app, NEW_CADENCE) < 0) { x.oldReports += i.r; x.usersOld++; }
    if (i.link === 'none') x.none++;
    x.unclean += i.c.unclean_exit || 0; x.leakBad += i.c.leak_bad || 0; x.leakOk += i.c.leak_ok || 0; x.homeBack += i.c.ip_home_back || 0;
    x.logErr += i.c.log_error || 0; x.updFailed += i.c.upd_failed || 0; x.exits += i.c.rep_exit || 0;
    x.updates += i.c.upd_done || 0;   // beta.13: the app updated itself (a download of the exe from GitHub, beside the exe downloads)
  };
  for (const [, i] of inWin) { const x = byDay.get(i.day); if (x) addDay(x, i); }
  for (const [dd, n] of Object.entries(autoDays)) if (byDay.has(dd)) byDay.get(dd).automated = n;
  for (const i of inst.values()) if (!real(i) && i.dc && byDay.has(i.day)) byDay.get(i.day).automatedDc++;
  // v4: the same numbers for each day of the period before (the brief compares 7 days with the 7 before)
  const prevDays = [];
  for (let t = 2 * period - 1; t >= period; t--) prevDays.push(dayRow(day(now - t * 864e5)));
  { const pd = new Map(prevDays.map((x) => [x.date, x])); for (const [, i] of inPrev) { const x = pd.get(i.day); if (x) addDay(x, i); } }
  // The period before, with the same numbers the KPIs compare (D9: before only users, new and minutes)
  const sumPrev = (name) => inPrev.reduce((s, [, i]) => s + (i.c[name] || 0), 0);
  const prev = { users: inPrev.length, fresh: inPrev.filter(([, i]) => i.c.first_start).length, minutes: inPrev.reduce((s, [, i]) => s + i.m, 0),
    daysWithData: new Set(inPrev.map(([, i]) => i.day)).size, ipNew: sumPrev('ip_changed'), ipSame: sumPrev('ip_same'), ipNone: sumPrev('ip_no_network'),
    matches: sumPrev('m_valve') + sumPrev('m_comm'), laggy: sumPrev('m_valve_lag') + sumPrev('m_comm_lag'), sessions: sumPrev('app_start'), crashes: sumPrev('crash') };

  // D9: the first day of a new install, each step inside the one before on the same install-day: the notice accepted
  // (first_start), the phone on (a report with USB or hotspot) and a first Change IP with a new IP. v4 (spec R5): Phone setup
  // done no longer counts as the phone on, phone_setup_done only means the pane was opened when it was already done.
  const f1 = inWin.filter(([, i]) => i.c.first_start);
  const f2 = f1.filter(([, i]) => i.link === 'usb' || i.link === 'hotspot');
  const f3 = f2.filter(([, i]) => i.c.ip_changed);
  const funnel = [{ label: '1.º arranque', n: f1.length }, { label: 'Telemóvel ligado', n: f2.length }, { label: '1.ª troca com IP novo', n: f3.length }];
  // v3: the step after, on the same install-day (Protected: a time to Protected was counted), and the steps per day of the period
  const TP = BANDS.tp.map(([b]) => `tp_${b}`);
  const f4 = f3.filter(([, i]) => TP.some((n) => i.c[n]));
  const funnelProtected = f4.length;
  const fd = new Map(days.map((x) => [x.date, { date: x.date, fs: 0, ph: 0, ip: 0, prot: 0 }]));
  for (const [list, k] of [[f1, 'fs'], [f2, 'ph'], [f3, 'ip'], [f4, 'prot']]) for (const [, i] of list) if (fd.has(i.day)) fd.get(i.day)[k]++;
  const funnelDays = [...fd.values()];

  // D9: when the app is open, weekday by hour in Lisbon: install-hours with a report, only the real install-days of the view
  const when = [...Array(7)].map(() => Array(24).fill(0));
  for (const r of hours) {
    if (!ok.has(key(r))) continue;
    const [wd, h] = lisbonSlot(r.h);
    when[wd][h]++;
  }

  const tally = (get) => { const m = {}; for (const [, i] of inWin) { const k = get(i); m[k] = (m[k] || 0) + 1; } return Object.entries(m).map(([k, n]) => ({ k, n })).sort((a, b) => b.n - a.n); };

  // Every counter of the period: its sum, the install-days that have it, by link (USB, hotspot) and by version. From D1's sums
  // (sums 'sql') or, the old way, from the install-days read above; the blocks below read only this.
  const AG = new Map();
  const agAdd = (name, app, link, n, days) => {
    const e = AG.get(name) || AG.set(name, { n: 0, days: 0, usb: 0, hotspot: 0, app: {} }).get(name);
    e.n += n; e.days += days; if (link === 'usb' || link === 'hotspot') e[link] += n; e.app[app] = (e.app[app] || 0) + n;
  };
  if (SQL) for (const r of sumsR) agAdd(r.name, r.app, r.link, r.n || 0, r.days || 0);
  else for (const [, i] of inWin) for (const [name, n] of Object.entries(i.c)) agAdd(name, i.app, i.link, n, 1);
  const AGs = [...AG.entries()].sort((a, b) => (a[0] < b[0] ? -1 : 1));

  // Counters and screens over the real install-days only
  const counters = {}, screens = {}, pathsM = {}, age = {};
  for (const s of SCREENS) screens[s] = { minutes: 0, days: 0 };
  for (const [name, { n, days: dn }] of AGs) {
    if (name.startsWith('scr_')) { const s = name.slice(4); if (screens[s]) { screens[s].minutes += n; screens[s].days += dn; } }
    else if (name.startsWith('path_')) { pathsM[name.slice(5)] = (pathsM[name.slice(5)] || 0) + n; }
    else if (AGE[name]) age[AGE[name]] = (age[AGE[name]] || 0) + dn;
    else if (!NOT_COUNTERS.test(name)) counters[name] = (counters[name] || 0) + n;
  }
  const paths = Object.entries(pathsM).map(([p, n]) => {
    const [from, to] = SCREENS.flatMap((a) => SCREENS.filter((b) => b !== a && p === `${a}_${b}`).map((b) => [a, b]))[0] || [];
    return from ? { from, to, n } : null;
  }).filter(Boolean).sort((a, b) => b.n - a.n || (a.from + a.to < b.from + b.to ? -1 : 1));

  // Each button with its clicks per day too (the dashboard's 14-day trend)
  const btn = {};
  for (const c of clicks) {
    if (!ok.has(key(c))) continue;
    const b = btn[c.name] || (btn[c.name] = { n: 0, days: 0, perDay: {} });
    b.n += c.n; b.days++; b.perDay[c.day] = (b.perDay[c.day] || 0) + c.n;
  }

  // The heat map has no install ID (added up on arrival): only the version filter applies to it
  // beta.13: heat is the old window grid (beta.9 to beta.12), heatPage the page grid with the scroll ("s" cells); never mixed
  const heatOut = {}, heatPage = {};
  for (const h of heat) {
    if (f.version !== 'all' && h.app !== f.version) continue;
    const m = /^(s?)(\d+),(\d+)$/.exec(h.cell); if (!m) continue;
    const to = m[1] ? heatPage : heatOut;
    const s = to[h.screen] || (to[h.screen] = { c: [], d: [], r: [] });
    if (s[h.kind]) {
      const same = s[h.kind].find((x) => x[0] === +m[2] && x[1] === +m[3]);   // the same cell from two versions: one dot
      if (same) same[2] += h.n; else s[h.kind].push([+m[2], +m[3], h.n]);
    }
  }

  // beta.9 part 2. Matches by how the phone was connected that install-day (its last report), sessions, scroll, settings, days active
  const C = (name) => (AG.get(name) || { n: 0 }).n;
  const days_ = (name) => (AG.get(name) || { days: 0 }).days;
  const byLink = (name) => { const e = AG.get(name); return { usb: e ? e.usb : 0, hotspot: e ? e.hotspot : 0 }; };
  const lagCell = (link, k) => ({ n: byLink(`m_${k}`)[link], lag: byLink(`m_${k}_lag`)[link] });
  const cs2 = {
    matches: C('m_valve') + C('m_comm'), laggy: C('m_valve_lag') + C('m_comm_lag'), valve: C('m_valve'), community: C('m_comm'),
    lag: { usb: { valve: lagCell('usb', 'valve'), community: lagCell('usb', 'comm') }, hotspot: { valve: lagCell('hotspot', 'valve'), community: lagCell('hotspot', 'comm') } },
    ping: bandRows('ping', (b) => byLink(`ping_${b}`)), jitter: bandRows('jit', (b) => byLink(`jit_${b}`)), jitterDown: bandRows('jit', (b) => byLink(`jitd_${b}`)),
    late: bandRows('late', (b) => ({ n: C(`late_${b}`) })), loss: bandRows('loss', (b) => ({ n: C(`loss_${b}`) })),
  };
  const sessions = {
    starts: C('app_start'), auto: C('start_auto'), hand: C('start_hand'), unclean: C('unclean_exit'), crashes: C('crash'),
    len: bandRows('sess', (b) => ({ n: C(`sess_${b}`) })), startup: bandRows('st', (b) => ({ n: C(`st_${b}`) })), toProtected: bandRows('tp', (b) => ({ n: C(`tp_${b}`) })),
  };
  const updates = { seen: C('upd_seen'), accepted: C('upd_accepted'), done: C('upd_done'), declined: C('upd_declined'), failed: C('upd_failed') };
  const scroll = {};
  for (const s of SCREENS) {
    const n = [0, 25, 50, 75, 100].map((b) => C(`scl_${s}_${b}`));
    const visits = n.reduce((a, b) => a + b, 0);
    if (visits) scroll[s] = { visits, reach: [25, 50, 75].map((_, q) => Math.round((100 * n.slice(q + 1).reduce((a, b) => a + b, 0)) / visits)) };
  }
  const settings = {};
  const active = [1, 2, 3, 4, 5, 6, 7].map((n) => ({ band: String(n), n: days_(`act_${n}`) }));
  for (const [name, { days: dn }] of AGs) if (name.startsWith('set_') && dn) settings[name.slice(4)] = (settings[name.slice(4)] || 0) + dn;
  const setup = SETUP.map(([k, label]) => ({ k: label, key: k, n: days_(`phone_setup_${k}`) }));
  const opened = inWin.filter(([, i]) => Object.keys(i.c).some((n) => n.startsWith('phone_setup_'))).length;

  // Cohorts from the install age bands: the week each install-day's install was made (its day less the band's first day), and
  // install-days seen at each age. Install-days, not people: the bands of more than a day count each day open.
  const cohorts = {};
  const monday = (t) => { const x = new Date(t); x.setUTCDate(x.getUTCDate() - ((x.getUTCDay() + 6) % 7)); return day(x); };
  for (const [, i] of [...inPrev, ...inWin])
    for (const [name, [col, first]] of Object.entries(COHORT)) {
      if (!i.c[name]) continue;
      const w = monday(Date.parse(i.day + 'T00:00:00Z') - first * 864e5);
      const c = cohorts[w] || (cohorts[w] = { week: w, n: 0, back: [0, 0, 0, 0, 0, 0] });
      c.back[col]++;
      if (col === 0) c.n++;
    }
  // beta.9: installs with a kept ID (id_install). Every day each one was open, so installs, new against returning and the cohorts
  // are exact for them. Only installs real in the fetched days count (a scanner's ID stays out).
  const realIds = new Set([...inPrev, ...inWin].map(([, i]) => i.id));
  const seen = new Map();
  for (const r of installDays) if (realIds.has(r.id)) (seen.get(r.id) || seen.set(r.id, []).get(r.id)).push(r.day);
  // v4: the first day of each install from its first report ever (the days above stop at INSTALL_DAYS)
  const firstDay = new Map(firstR.map((r) => [r.id, day(r.t)]));
  const bound = day(now - (INSTALL_DAYS - 1) * 864e5);
  // nextDay (D9): installs first seen in the period up to the day before yesterday (their next day is over), and how many of
  // them were open again the day after
  const installs = { active: 0, fresh: 0, back: 0, prev: 0, perDay: {}, nextDay: { n: 0, back: 0 }, day7: { n: 0, back: 0 } };
  const exact = {};
  const AT = [0, 1, 3, 7, 14, 30];
  const today = day(now);
  // v3: cohorts by the day of install while there are few a day (the last 14 days): open again at 1-2, 3-6, 7-13, 14-29 days,
  // null while that age has not come for the day yet
  const daily = {};
  const dailyFrom = day(now - 13 * 864e5);
  for (const [id, ds] of seen) {
    ds.sort();
    const first = firstDay.get(id) && firstDay.get(id) < ds[0] ? firstDay.get(id) : ds[0];
    const inW = ds.some((x) => x >= since), inP = ds.some((x) => x >= prevSince && x < since);
    if (inW) { installs.active++; if (first >= since) installs.fresh++; else installs.back++; }
    if (inP) installs.prev++;
    // Only when the day after is over (it is before today): an install first seen yesterday may still come back today
    const after = day(Date.parse(first + 'T00:00:00Z') + 864e5);
    if (first >= since && after < today) { installs.nextDay.n++; if (ds.includes(after)) installs.nextDay.back++; }
    // v3: the same for a week: new in the period with day 7 over, and open again on day 7 or later
    const after7 = day(Date.parse(first + 'T00:00:00Z') + 7 * 864e5);
    if (first >= since && after7 < today) { installs.day7.n++; if (ds.some((x) => x >= after7)) installs.day7.back++; }
    if (first >= dailyFrom) {
      const t1 = Date.parse(first + 'T00:00:00Z');
      const c = daily[first] || (daily[first] = { day: first, n: 0, back: [0, 0, 0, 0] });
      c.n++;
      const ages = ds.map((x) => Math.round((Date.parse(x + 'T00:00:00Z') - t1) / 864e5));
      for (let q = 1; q <= 4; q++) if (ages.some((a) => a >= AT[q] && a < AT[q + 1])) c.back[q - 1]++;
    }
    for (const x of ds) if (x >= since) { const p = installs.perDay[x] || (installs.perDay[x] = { n: 0, fresh: 0 }); p.n++; if (x === first) p.fresh++; }
    // Older than the days read: its returns would be missing, so it is not drawn (the table shows the last 7 weeks)
    if (first < bound) continue;
    const t0 = Date.parse(first + 'T00:00:00Z');
    const w = monday(t0);
    const c = exact[w] || (exact[w] = { week: w, n: 0, back: [0, 0, 0, 0, 0, 0] });
    c.n++; c.back[0]++;
    const ages = new Set(ds.map((x) => Math.round((Date.parse(x + 'T00:00:00Z') - t0) / 864e5)));
    for (let q = 1; q < AT.length; q++) if ([...ages].some((a) => a >= AT[q] && (q === AT.length - 1 || a < AT[q + 1]))) c.back[q]++;
  }
  const cohortsExact = Object.values(exact).sort((a, b) => (a.week < b.week ? -1 : 1)).slice(-7)
    .map((c) => ({ ...c, back: c.back.map((v, q) => (q && Date.parse(c.week + 'T00:00:00Z') + (AT[q] + 6) * 864e5 > now ? null : v)) }));

  const cohortsDaily = Object.values(daily).sort((a, b) => (a.day < b.day ? -1 : 1))
    .map((c) => ({ ...c, back: c.back.map((v, q) => (day(Date.parse(c.day + 'T00:00:00Z') + AT[q + 1] * 864e5) < today ? v : null)) }));

  const cohortList = Object.values(cohorts).sort((a, b) => (a.week < b.week ? -1 : 1)).slice(-7)
    .map((c) => ({ ...c, back: c.back.map((v, q) => (q && Date.parse(c.week + 'T00:00:00Z') + ([0, 1, 3, 7, 14, 30][q] + 6) * 864e5 > now ? null : v)) }));

  // The environment report: no ID, not filtered (only dev and the period)
  const env = { reports: 0, windows: [], lang: [], window: [], scale: [], network: [], brands: [], android: [] };
  const envKey = { build: 'windows', lang: 'lang', win: 'window', scale: 'scale', net: 'network', brand: 'brands', android: 'android' };
  for (const r of envRows) {
    if (r.k === 'reports') { env.reports += r.n; continue; }
    const list = env[envKey[r.k]]; if (!list) continue;
    const k = r.k === 'build' ? (WINDOWS[r.v] || `Build ${r.v}`) : ENV_LABEL[r.k]?.[r.v] ?? r.v;
    const have = list.find((x) => x.k === k);
    if (have) have.n += r.n; else list.push({ k, n: r.n });
  }
  for (const k of Object.values(envKey)) env[k].sort((a, b) => b.n - a.n);

  // Change IP, phone drops, mobile data (each install-day's highest band), Rejoin, regions turned off, first screen, updated from
  const phone = {
    method: [{ k: 'Modo avião', n: C('chg_air') }, { k: 'Desligar e ligar os dados', n: C('chg_data') }],
    duration: bandRows('chgt', (b) => byLink(`chgt_${b}`)),
    attempts: bandRows('chga', (b) => ({ n: C(`chga_${b}`) })),
    quality: { boa: C('chgq_good'), razoavel: C('chgq_fair'), ma: C('chgq_bad'), retry: C('chg_retry') },
    drops: bandRows('drop', (b) => ({ n: C(`drop_${b}`) })),
    data: bandRows('data', (b) => ({ n: inWin.filter(([, i]) => BANDS.data.map(([x]) => x).filter((x) => i.c[`data_${x}`]).at(-1) === b).length })),
    cableHints: C('cable_hint'),
  };
  const rejoin = { tries: C('rej_try'), back: C('rej_back'), waits: C('rej_wait') };
  const regionsM = {};
  for (const [name, { days: dn }] of AGs) if (name.startsWith('reg_') && dn) regionsM[name.slice(4)] = (regionsM[name.slice(4)] || 0) + dn;
  const regions = Object.entries(regionsM).map(([code, n]) => ({ code, name: CITY[code.replace(/\d$/, '')] || code.toUpperCase(), n })).sort((a, b) => b.n - a.n || (a.code < b.code ? -1 : 1)).slice(0, 15);
  const firstScreen = SCREENS.map((sc) => ({ screen: sc, n: C(`fs_${sc}`) })).filter((x) => x.n).sort((a, b) => b.n - a.n);
  const fromM = {};
  for (const [name, n] of Object.entries(counters)) if (name.startsWith('updfrom_')) fromM[name.slice(8)] = (fromM[name.slice(8)] || 0) + n;
  updates.from = Object.entries(fromM).map(([v, n]) => ({ v: v.replace(/^(\d+)_(\d+)_(\d+)_?/, '$1.$2.$3-').replace(/-$/, '').replace(/_/g, '.'), n })).sort((a, b) => b.n - a.n);

  const crashPlaces = {};
  const err = {};
  for (const e of errors) {
    if (!ok.has(key(e))) continue;
    if (e.type.startsWith('Fatal_')) {
      const k = `${e.at}|${e.app}`;
      const x = crashPlaces[k] || (crashPlaces[k] = { at: e.at, type: e.type.slice(6), app: e.app, n: 0 });
      x.n += e.n;
      continue;
    }
    const k = `${e.type}|${e.at}|${e.app}`;
    const x = err[k] || (err[k] = { type: e.type, at: e.at, app: e.app, n: 0, days: 0, perDay: {} });
    x.n += e.n; x.days++; x.perDay[e.day] = (x.perDay[e.day] || 0) + e.n;
  }

  // Checks red when a report was built (install-days with that row red) and Run leak test
  const checks = { red: CHECKS.map(([k, title]) => ({ k: title, key: k, n: days_(`chk_${k}`) })).filter((x) => x.n).sort((a, b) => b.n - a.n),
    leak: { pass: C('leak_ok'), fail: C('leak_bad') } };

  // The Versões page: the same real install-days, per version, and each day's share of each version
  const perVer = {}, adoptM = {};
  for (const [, i] of inWin) {
    const v = perVer[i.app] || (perVer[i.app] = { days: 0, sessions: 0, crashes: 0, ipNew: [0, 0], lag: [0, 0], min: [], errors: 0, tp: {}, st: {}, unclean: 0, logErr: 0 });
    const c = (n) => i.c[n] || 0;
    v.days++; v.sessions += c('app_start'); v.crashes += c('crash'); v.unclean += c('unclean_exit'); v.logErr += c('log_error');
    v.ipNew[0] += c('ip_changed'); v.ipNew[1] += c('ip_changed') + c('ip_same') + c('ip_no_network');
    v.lag[0] += c('m_valve_lag') + c('m_comm_lag'); v.lag[1] += c('m_valve') + c('m_comm');
    if (i.m >= 1) v.min.push(i.m / Math.max(1, c('app_start')));
    for (const [b] of BANDS.tp) v.tp[b] = (v.tp[b] || 0) + c(`tp_${b}`);
    const a = adoptM[i.day] || (adoptM[i.day] = {}); a[i.app] = (a[i.app] || 0) + 1;
  }
  for (const e of errors) if (ok.has(key(e)) && !e.type.startsWith('Fatal_') && perVer[e.app]) perVer[e.app].errors += e.n;
  // The startup bands per version, from the sums (they are not read per install-day)
  for (const [app, v] of Object.entries(perVer)) for (const [b] of BANDS.st) { const e = AG.get(`st_${b}`); v.st[b] = e ? e.app[app] || 0 : 0; }
  const versionMetrics = Object.fromEntries(Object.entries(perVer).map(([app, v]) => {
    const m = v.min.sort((a, b) => a - b);
    return [app, { days: v.days, sessions: v.sessions, crashes: v.crashes, ipNew: v.ipNew, lag: v.lag, errors: v.errors, unclean: v.unclean, logErr: v.logErr,
      minMed: m.length ? Math.round(m[(m.length - 1) >> 1]) : 0,
      toProtected: bandRows('tp', (b) => ({ n: v.tp[b] })), startup: bandRows('st', (b) => ({ n: v.st[b] })) }];
  }));
  const adoption = days.map((x) => ({ date: x.date, by: adoptM[x.date] || {} }));

  const versionsAll = [...new Set([...inst.values()].map((i) => i.app))];
  const countriesAll = [...new Set([...inst.values()].map((i) => i.country))].sort();
  // D8: the list sizes (only in /classic before): install-days in each band
  const sizes = SIZES.map((name) => {
    const m = {};
    for (const [, i] of inWin) if (i.c[name] !== undefined) { const b = SIZE_BAND[i.c[name]] ?? String(i.c[name]); m[b] = (m[b] || 0) + 1; }
    return { name, bands: Object.values(SIZE_BAND).filter((b) => m[b]).map((b) => ({ band: b, n: m[b] })) };
  });

  // ---------------------------------------------------------------- v3 (Nov. 2026): the Hoje page and the freshness bar
  // The real install-days of the view with the filters, in the period and the one before (today and yesterday are always in)
  const okAll = new Map([...inPrev, ...inWin]);
  const yday = day(now - 864e5);
  // Today and yesterday (UTC days, like the charts): only sums and counts per value, never a row per install
  const dayBlock = (dt) => {
    const list = [...okAll.values()].filter((i) => i.day === dt);
    const t = (get) => { const m = {}; for (const i of list) { const k = get(i); m[k] = (m[k] || 0) + 1; } return Object.entries(m).map(([k, n]) => ({ k, n })).sort((a, b) => b.n - a.n || (a.k < b.k ? -1 : 1)); };
    const c = (n) => list.reduce((s, i) => s + (i.c[n] || 0), 0);
    return { date: dt, users: list.length, fresh: list.filter((i) => i.c.first_start).length, minutes: list.reduce((s, i) => s + i.m, 0),
      ip: c('ip_changed') + c('ip_same') + c('ip_no_network'), versions: t((i) => i.app), countries: t((i) => i.country), links: t((i) => i.link), modes: t((i) => i.mode) };
  };
  const recentDays = [dayBlock(today), dayBlock(yday)];
  // Por hora: installs with a report in each Lisbon hour, today (null for the hours still to come) and yesterday. The live table
  // keeps one row per ID, so this is from the reports, not the live signal.
  const lt = lisbonHour(now), ly = lisbonHour(now - 864e5).date;
  const hourSets = { [lt.date]: [...Array(24)].map(() => new Set()), [ly]: [...Array(24)].map(() => new Set()) };
  // Yesterday up to this time (UTC day before, reports sent before now less a day): installs, the new ones, minutes
  const cut = now - 864e5;
  const same = new Map();
  // v4: also the same weekday a week ago up to this time (only when that day was read: a period of 4 days or more)
  const wday = day(now - 7 * 864e5), wcut = now - 7 * 864e5, sameW = new Map();
  for (const r of recentR) {
    const i = okAll.get(key(r)); if (!i) continue;
    const h = lisbonHour(r.ts);
    if (hourSets[h.date]) hourSets[h.date][h.hour].add(r.id);
    if (r.day === yday && r.ts <= cut) { const x = same.get(r.id) || same.set(r.id, { m: 0, fresh: !!i.c.first_start }).get(r.id); x.m = Math.min(1440, x.m + (r.minutes || 0)); }
    if (r.day === wday && r.ts <= wcut) { const x = sameW.get(r.id) || sameW.set(r.id, { m: 0, fresh: !!i.c.first_start }).get(r.id); x.m = Math.min(1440, x.m + (r.minutes || 0)); }
  }
  const hourly = { today: hourSets[lt.date].map((s, h) => (h > lt.hour ? null : s.size)), yday: hourSets[ly].map((s) => s.size), hour: lt.hour };
  // Counted once a day with no time of their own: the new ones are those whose day has first_start; IP changes have no time at all
  const sameTime = { users: same.size, fresh: [...same.values()].filter((x) => x.fresh).length, minutes: [...same.values()].reduce((s, x) => s + x.m, 0) };
  const sameTimeWeek = wday >= prevSince ? { date: wday, users: sameW.size, fresh: [...sameW.values()].filter((x) => x.fresh).length, minutes: [...sameW.values()].reduce((s, x) => s + x.m, 0) } : null;
  const used = new Map(budgetR.map((r) => [r.day, r.rows]));
  const fresh = { lastTs: last[0]?.t ?? null, lastAny: lastAnyR[0]?.t ?? null,
    budget: { day: today, rows: used.get(today) || 0, cap: DAILY_ROWS, live: used.get(`${today}:live`) || 0, liveCap: LIVE_ROWS,
      // Security review (Nov. 2026): the website counter's share, so a flood of made-up visits shows on the dashboard
      site: used.get(`${today}:site`) || 0, siteCap: SITE_ROWS,
      // v4: Delete my stats requests cost 60 rows each (index.js forget), so rows / 60 is the requests of the day
      forget: used.get(`${today}:forget`) || 0, forgetCap: FORGET_ROWS } };
  // v4: each of the last 14 days, every share
  const budgetDays = [];
  for (let t = 13; t >= 0; t--) { const d = day(now - t * 864e5); budgetDays.push({ date: d, rows: used.get(d) || 0, live: used.get(`${d}:live`) || 0, site: used.get(`${d}:site`) || 0, forget: used.get(`${d}:forget`) || 0 }); }
  // Each error with the first day and version it was seen in, in this view and the last 90 days
  const firstOf = new Map(errFirstR.map((r) => [`${r.type}|${r.at}`, { day: r.first, app: r.app }]));
  const errOut = Object.values(err).sort((a, b) => b.n - a.n).slice(0, 40).map((e) => ({ ...e, first: firstOf.get(`${e.type}|${e.at}`) || null }));

  // The website (Nov. 2026): visits and Download clicks per day, page, referrer host and country. Counts only, with no ID
  const site = { views: 0, downloads: 0, prevViews: 0, prevDownloads: 0, days: days.map((x) => ({ date: x.date, views: 0, downloads: 0 })), pages: [], refs: [], countries: [], since: null };
  {
    const sd = new Map(site.days.map((x) => [x.date, x])), pg = {}, rf = {}, ct = {};
    for (const r of siteR) {
      const v = r.event === 'view', n = r.n || 0;
      if (r.day < since) { if (v) site.prevViews += n; else site.prevDownloads += n; continue; }
      if (!site.since || r.day < site.since) site.since = r.day;
      if (v) site.views += n; else site.downloads += n;
      const x = sd.get(r.day); if (x) x[v ? 'views' : 'downloads'] += n;
      const p = pg[r.page] || (pg[r.page] = { k: r.page, views: 0, downloads: 0 }); p[v ? 'views' : 'downloads'] += n;
      if (v) { rf[r.ref || ''] = (rf[r.ref || ''] || 0) + n; ct[r.country || '?'] = (ct[r.country || '?'] || 0) + n; }
    }
    const list = (m) => Object.entries(m).map(([k, n]) => ({ k, n })).sort((a, b) => b.n - a.n || (a.k < b.k ? -1 : 1));
    site.pages = Object.values(pg).sort((a, b) => b.views - a.views || b.downloads - a.downloads);
    site.refs = list(rf).slice(0, 20);
    site.countries = list(ct).slice(0, 20);
    // v4: the referrers of the last 7 days (the brief), whatever the period
    const r7 = {}, from7 = day(now - 6 * 864e5);
    for (const r of siteR) if (r.event === 'view' && r.day >= from7) r7[r.ref || ''] = (r7[r.ref || ''] || 0) + (r.n || 0);
    site.refs7 = list(r7).slice(0, 20);
  }

  const v4 = v4Blocks({ now, today, since, prevSince, period, okAll, inWin, seen, firstDay, errors, errFirstR, heat, f });

  return {
    asOf: day(now), now: new Date(now).toISOString().slice(11, 16), period, filters: f, dev,
    lastTs: last[0]?.t ?? null, truncated, warnings, excluded: (exclude || []).length,
    options: { version: versionsAll, country: countriesAll },
    days, prev, automated, automatedDc, funnel, when, sizes,
    countries: tally((i) => i.country), versions: tally((i) => i.app), modes: tally((i) => i.mode), links: tally((i) => i.link), oses: tally((i) => i.os),
    counters, screens, paths, age: Object.values(AGE).filter((b) => age[b]).map((band) => ({ band, n: age[band] })),
    buttons: btn, heat: heatOut, heatPage,
    errors: errOut,
    // v3 (Nov. 2026): added, nothing above changed
    fresh, recentDays, hourly, sameTime, funnelProtected, funnelDays, cohortsDaily, site,
    cs2: { ...cs2, rejoin, regions, regionsDen: inWin.length, sessions: C('cs2_start'), hoursProtected: Math.round(C('min_prot') / 60), hoursCs2: Math.round(C('min_cs2') / 60) }, phone, firstScreen, sessions, updates, scroll, settings: Object.entries(settings).map(([name, n]) => ({ name, n })).sort((a, b) => b.n - a.n || (a.name < b.name ? -1 : 1)),
    active, setup, setupOpened: opened, cohorts: cohortList, cohortsExact, installs, env,
    checks, versionMetrics, adoption,
    crashPlaces: Object.values(crashPlaces).sort((a, b) => b.n - a.n).slice(0, 20),
    // v4 (Nov. 2026): added, nothing above changed
    prevDays, sameTimeWeek, budgetDays, ...v4,
  };
}

/** Who has the app open now: installs with a signal in the last 6 minutes, only as counts, and when the last signal came. */
export async function live(db, { dev, now, exclude = [] }) {
  const W = who(dev, exclude);
  let rows = [], lastTs = null;
  // Nov. 2026 (performance review): the three reads at once, one D1 round trip instead of three in a row
  const peaksP = livePeaks(db, { dev, now });
  peaksP.catch(() => {});
  try {
    const [r, last] = await Promise.all([
      db.prepare(`SELECT app, mode, link, screen, cs2, prot, tray FROM live WHERE ${W.sql} AND ts >= ?`).bind(...W.args, now - 6 * 60_000).all(),
      db.prepare(`SELECT ts FROM live WHERE ${W.sql} ORDER BY ts DESC LIMIT 1`).bind(...W.args).first(),
    ]);
    rows = r.results ?? [];
    lastTs = last?.ts ?? null;
  } catch (e) {
    // Only a database without the live table yet counts as nobody: any other error (a broken query, D1 down) goes up, so the
    // page says it couldn't read instead of "0 now".
    if (!/no such table/i.test(String(e && e.message))) throw e;
    rows = []; lastTs = null;
  }
  const count = (get) => { const m = {}; for (const r of rows) { const k = get(r) ?? '?'; m[k] = (m[k] || 0) + 1; } return Object.entries(m).map(([k, n]) => ({ k, n })).sort((a, b) => b.n - a.n); };
  return {
    now: rows.length, inGame: rows.filter((r) => r.cs2 === 1).length, protectedN: rows.filter((r) => r.prot === 1).length, lastTs,
    byVersion: count((r) => r.app), byMode: count((r) => r.mode), byLink: count((r) => r.link),
    byScreen: count((r) => (r.tray === 1 ? 'tray' : r.screen)),
    peaks: await peaksP,
  };
}

/**
 * Nov. 2026: the live signal's history, from the cron's samples (live_hourly, the highest of each UTC hour). In Lisbon hours:
 * today and yesterday hour by hour (null = no sample in that hour, not 0), yesterday's highest and its hour, and the highest of
 * each of the last 14 days. Counts only.
 */
export async function livePeaks(db, { dev, now }) {
  let rows = [];
  try { rows = (await db.prepare('SELECT hour, n, game, prot FROM live_hourly WHERE dev = ? AND hour >= ? ORDER BY hour').bind(dev ? 1 : 0, Math.floor(now / 3600_000) - 15 * 24).all()).results ?? []; }
  catch (e) { if (!/no such table/i.test(String(e && e.message))) throw e; }
  const t = lisbonHour(now), y = lisbonHour(now - 864e5).date;
  const today = Array(24).fill(null), yday = Array(24).fill(null), byDay = {};
  for (const r of rows) {
    const h = lisbonHour(r.hour * 3600_000);
    if (h.date === t.date) today[h.hour] = Math.max(today[h.hour] ?? 0, r.n);
    if (h.date === y) yday[h.hour] = Math.max(yday[h.hour] ?? 0, r.n);
    byDay[h.date] = Math.max(byDay[h.date] ?? 0, r.n);
  }
  let ydayMax = null;
  yday.forEach((v, h) => { if (v != null && (!ydayMax || v > ydayMax.n)) ydayMax = { n: v, hour: h }; });
  const days14 = [];
  for (let i = 13; i >= 0; i--) { const d = lisbonHour(now - i * 864e5).date; days14.push({ date: d, max: byDay[d] ?? null }); }
  // v4: last = the newest hour with a sample (the brief's cron check)
  return { today, yday, hour: t.hour, ydayMax, days: days14, first: rows[0] ? rows[0].hour * 3600_000 : null, last: rows.length ? rows.at(-1).hour * 3600_000 : null };
}

// ---------------------------------------------------------------- v4 (Nov. 2026): installs, not install-days
// Spec dashboard-v4 6.2 and 6.4. An install's first day (day 0) is the day its first_start was counted (the stats notice
// accepted); its first days are day 0 to day 6. Each step may happen on any of those days, not only on day 0 (v3 counted only
// day 0, so someone who installed at night and plugged the phone in the next morning counted as lost). Only counts leave here.
const addD = (d, n) => day(Date.parse(d + 'T00:00:00Z') + n * 864e5);
const TPN = BANDS.tp.map(([b]) => `tp_${b}`);
// The steps in order, each inside the one before. Protected does not need a Change IP (the Status box says Protected when the
// phone's connection is good, MainWindow.Protection.cs), so the IP change is shown beside the steps, not as one of them.
export const ACT_STEPS = ['first_start', 'phone_seen', 'protected', 'first_match'];
export const STUCK = ['never_phone', 'phone_not_protected', 'protected_no_match', 'reached_match'];
const SETUP_KEYS = SETUP.map(([k]) => k);
// The return curve: open again on day 1, 2 to 6, 7 to 13, 14 to 29, 30 to 59 (the windows of the v3 cohort table)
const RET_WIN = [[1, 1], [2, 6], [7, 13], [14, 29], [30, 59]];

function v4Blocks({ now, today, since, prevSince, okAll, inWin, seen, errors, errFirstR, heat, f }) {
  const byId = new Map();
  for (const i of okAll.values()) (byId.get(i.id) || byId.set(i.id, []).get(i.id)).push(i);
  for (const l of byId.values()) l.sort((a, b) => (a.day < b.day ? -1 : 1));
  // Errors of the real install-days of the view, by install and day (read from the start of the period on)
  const errAt = new Set();
  for (const e of errors) if (okAll.has(`${e.day}|${e.id}`) && !e.type.startsWith('Fatal_')) errAt.add(`${e.id}|${e.day}`);
  const leftMeasured = [...okAll.values()].some((i) => Object.keys(i.c).some((n) => n.startsWith('phone_left_')));

  // One entry per new install: what it reached in its first 7 days. Never leaves the function: only the sums below do.
  const members = [];
  const d0Of = new Map();
  for (const [id, list] of byId) {
    const d0i = list.find((i) => i.c.first_start);
    if (!d0i) continue;
    const d0 = d0i.day, end = addD(d0, ACT_DAYS - 1);
    const win = list.filter((i) => i.day >= d0 && i.day <= end);
    const sum = (n) => win.reduce((s, i) => s + (i.c[n] || 0), 0);
    const names = (pre) => new Set(win.flatMap((i) => Object.keys(i.c).filter((n) => n.startsWith(pre)).map((n) => n.slice(pre.length))));
    const lk = Math.max(...win.map((i) => LINK_RANK.indexOf(i.link)));
    const prot = win.some((i) => TPN.some((n) => i.c[n]) || (i.c.min_prot || 0) > 0);
    // Protected needs a working phone connection (the egress), even when the link is reported as another adapter (critic)
    const phone = lk >= 2 || prot;
    const match = sum('m_valve') + sum('m_comm') > 0;
    const ds = seen.get(id) || list.map((i) => i.day);
    const pressed = new Set(win.flatMap((i) => Object.keys(i.k).filter((n) => n.startsWith('Setup_')).map((n) => n.slice(6))));
    members.push({
      d0, phone, prot, match,
      ipNew: sum('ip_changed') > 0, ipSame: sum('ip_same') + sum('ip_no_network') > 0,
      bucket: !phone ? 0 : !prot ? 1 : !match ? 2 : 3,
      mature: end < today,
      d1: addD(d0, 1) < today ? ds.includes(addD(d0, 1)) : null,
      // D7: open again on day 7 to 13 (a fixed window, so an older cohort gets no extra days to come back); only when day 13 is over
      d7: addD(d0, 13) < today ? ds.some((x) => x >= addD(d0, 7) && x <= addD(d0, 13)) : null,
      ret: RET_WIN.map(([a, b]) => (addD(d0, b) < today ? ds.some((x) => x >= addD(d0, a) && x <= addD(d0, b)) : null)),
      seg: { link: d0i.link, country: d0i.country, version: d0i.app, mode: d0i.mode },
      open: names('phone_setup_'), left: names('phone_left_'), pressed,
      help: win.some((i) => i.k.PhoneHelp_Click), cs2: sum('cs2_start') > 0,
      red: names('chk_'),
      day1: !!d0i.c.crash || errAt.has(`${id}|${d0}`),
    });
    d0Of.set(id, d0);
  }

  // Sums over a set of members: the steps (each inside the one before), the IP change beside them, D1 and D7 (only installs
  // whose day has come), the last step reached (each install in exactly one), how many are under 7 days old
  const agg = (ms) => {
    const n = ms.length;
    const c = [n, ms.filter((m) => m.phone).length, ms.filter((m) => m.phone && m.prot).length, ms.filter((m) => m.phone && m.prot && m.match).length];
    const steps = ACT_STEPS.map((id, k) => ({ id, n: c[k], of_first: n ? c[k] / n : null, of_prev: k ? (c[k - 1] ? c[k] / c[k - 1] : null) : (n ? 1 : null) }));
    const d1 = ms.filter((m) => m.d1 !== null), d7 = ms.filter((m) => m.d7 !== null);
    return {
      n, steps,
      // Reached in any order (Protected without a report with the link, a match without Protected): only beside the steps
      any: { protected: ms.filter((m) => m.prot).length, first_match: ms.filter((m) => m.match).length },
      ip_changed: { n: ms.filter((m) => m.phone && m.ipNew).length, of: c[1], same_only: ms.filter((m) => m.phone && !m.ipNew && m.ipSame).length },
      d1: { n: d1.length, back: d1.filter((m) => m.d1).length }, d7: { n: d7.length, back: d7.filter((m) => m.d7).length },
      buckets: STUCK.map((_, b) => ms.filter((m) => m.bucket === b).length),
      immature: ms.filter((m) => !m.mature).length,
    };
  };
  const cur = members.filter((m) => m.d0 >= since), prv = members.filter((m) => m.d0 >= prevSince && m.d0 < since);
  const byD0 = {};
  for (const m of members) (byD0[m.d0] || (byD0[m.d0] = [])).push(m);
  const cohorts = Object.keys(byD0).sort().map((d0) => {
    const a = agg(byD0[d0]);
    // The Phone setup step at open of the installs that never got the phone up, per day (the brief's top step)
    const setup = {};
    for (const m of byD0[d0]) if (m.bucket === 0) for (const s of m.open) setup[s] = (setup[s] || 0) + 1;
    return { day: d0, n: a.n, steps: a.steps.map((s) => s.n), ip_changed: a.ip_changed.n, d1: a.d1.n ? a.d1 : null, d7: a.d7.n ? a.d7 : null, buckets: a.buckets, immature: a.immature, setup };
  });
  const segOf = (k) => {
    const m = {};
    for (const x of cur) { const v = x.seg[k] || '?'; const s = m[v] || (m[v] = { k: v, n: 0, protected: 0, first_match: 0 }); s.n++; if (x.phone && x.prot) s.protected++; if (x.phone && x.prot && x.match) s.first_match++; }
    return Object.values(m).sort((a, b) => b.n - a.n || (a.k < b.k ? -1 : 1)).slice(0, 10);
  };
  const A = agg(cur), P = agg(prv);
  const activation = {
    basis: 'installs whose first_start day is in the period; each step within its first 7 days (day 0 to day 6), inside the step before',
    window_days: ACT_DAYS, ...A, prev: P, cohorts,
    segments: { link: segOf('link'), country: segOf('country'), version: segOf('version'), mode: segOf('mode') },
  };

  // Onde encalham: the last step reached, the Phone setup of those who never got the phone up, and the frustration signals
  const never = cur.filter((m) => m.bucket === 0);
  const setup = SETUP_KEYS.map((step) => ({ step, open: never.filter((m) => m.open.has(step)).length, left: leftMeasured ? never.filter((m) => m.left.has(step)).length : null, pressed: never.filter((m) => m.pressed.has(step)).length }));
  const notProt = cur.filter((m) => m.bucket === 1);
  const title = (key) => (CHECKS.find(([k]) => k === key) || [key, key])[1];
  const redTop = (ms) => { const r = {}; for (const m of ms) for (const k of m.red) r[k] = (r[k] || 0) + 1; return Object.entries(r).map(([key, n]) => ({ key, title: title(key), installs: n })).sort((a, b) => b.installs - a.installs).slice(0, 3); };
  const ids = (pred) => new Set(inWin.filter(([, i]) => pred(i)).map(([, i]) => i.id)).size;
  const tot = (n) => inWin.reduce((s, [, i]) => s + (i.c[n] || 0), 0);
  const cells = (kind) => {
    const out = new Map();
    for (const h of heat) if (h.kind === kind && (f.version === 'all' || h.app === f.version)) {
      // beta.13: page cells ("s") too, marked page, so the dashboard finds the button on the right grid
      const m = /^(s?)(\d+),(\d+)$/.exec(h.cell); if (!m) continue;
      const k = `${h.screen}|${h.cell}`;
      if (out.has(k)) out.get(k).n += h.n; else out.set(k, { screen: h.screen, c: +m[2], r: +m[3], n: h.n, ...(m[1] ? { page: true } : {}) });
    }
    return [...out.values()].sort((a, b) => b.n - a.n);
  };
  const rage = cells('r'), dead = cells('d');
  const sig = (id, name) => ({ id, n: tot(name), installs: ids((i) => i.c[name]) });
  const allRed = {};
  for (const [, i] of inWin) for (const n of Object.keys(i.c)) if (n.startsWith('chk_')) (allRed[n.slice(4)] || (allRed[n.slice(4)] = new Set())).add(i.id);
  const stuck = {
    basis: 'new installs in the period (first_start day), the last step reached in their first 7 days; each install in exactly one bucket',
    n: cur.length, prev_n: prv.length,
    buckets: STUCK.map((id, b) => ({ id, n: A.buckets[b], share: cur.length ? A.buckets[b] / cur.length : null, prev: P.buckets[b], prev_share: prv.length ? P.buckets[b] / prv.length : null })),
    setup, never_opened: never.filter((m) => !m.open.size && !m.help).length, left_measured: leftMeasured,
    phone_not_protected: { n: notProt.length, ip_new: notProt.filter((m) => m.ipNew).length, ip_same_only: notProt.filter((m) => !m.ipNew && m.ipSame).length, red: redTop(notProt) },
    protected_no_match: { n: A.buckets[2], cs2_opened: cur.filter((m) => m.bucket === 2 && m.cs2).length },
    signals: [
      { id: 'rage_clicks', n: rage.reduce((s, x) => s + x.n, 0), installs: null, top: rage.slice(0, 20) },
      { id: 'dead_clicks', n: dead.reduce((s, x) => s + x.n, 0), installs: null, top: dead.slice(0, 20) },
      sig('cable_hint', 'cable_hint'), sig('home_ip_back', 'ip_home_back'), sig('leak_test_failed', 'leak_bad'), sig('update_failed', 'upd_failed'), sig('crash', 'crash'),
      { id: 'red_checks', n: Object.values(allRed).reduce((s, x) => s + x.size, 0), installs: ids((i) => Object.keys(i.c).some((n) => n.startsWith('chk_'))),
        top: Object.entries(allRed).map(([key, s]) => ({ key, title: title(key), installs: s.size })).sort((a, b) => b.installs - a.installs).slice(0, 3) },
      { id: 'day1_crash_or_error', n: cur.filter((m) => m.day1).length, installs: cur.filter((m) => m.day1).length },
    ],
  };

  // Errors of the last 7 days: times, installs, new installs hit on their first day, and when the error was first seen
  const from7 = addD(today, -6);
  const firstErr = new Map(errFirstR.map((r) => [`${r.type}|${r.at}`, { day: r.first, app: r.app }]));
  const er = {};
  for (const e of errors) {
    if (e.day < from7 || e.type.startsWith('Fatal_') || !okAll.has(`${e.day}|${e.id}`)) continue;
    const k = `${e.type}|${e.at}`;
    const x = er[k] || (er[k] = { type: e.type, at: e.at, times: 0, ids: new Set(), day1: new Set(), last: e.day, apps: {} });
    x.times += e.n; x.ids.add(e.id); if (e.day > x.last) x.last = e.day;
    const ap = x.apps[e.app] || (x.apps[e.app] = { times: 0, ids: new Set(), day1: new Set() });
    ap.times += e.n; ap.ids.add(e.id);
    if (d0Of.get(e.id) === e.day) { x.day1.add(e.id); ap.day1.add(e.id); }
  }
  const errorsRecent = Object.entries(er).map(([k, x]) => {
    const fe = firstErr.get(k) || null;
    return { type: x.type, at: x.at, times: x.times, installs: x.ids.size, new_installs_day1: x.day1.size, first_day: fe ? fe.day : null, first_version: fe ? fe.app : null, is_new: !!(fe && fe.day >= from7),
      last_day: x.last, by_version: Object.entries(x.apps).sort((a, b) => compareVersions(b[0], a[0])).map(([version, a]) => ({ version, times: a.times, installs: a.ids.size, new_installs_day1: a.day1.size })) };
  }).sort((a, b) => b.installs - a.installs || b.times - a.times).slice(0, 20);

  // Distinct installs in fixed windows (only when the window was read), the median minutes of an install over the window,
  // DAU and WAU per day, installs not seen for 14 to 30 days, and the pooled return curve of the new installs
  const win = (a, b) => {
    if (a < prevSince) return null;
    const m = new Map();
    let fresh = 0;
    for (const i of okAll.values()) if (i.day >= a && i.day <= b) { m.set(i.id, (m.get(i.id) || 0) + i.m); if (i.c.first_start) fresh++; }
    const v = [...m.values()].sort((x, y) => x - y);
    return { from: a, to: b, active: m.size, minutes_median: v.length ? v[(v.length - 1) >> 1] : null, fresh };
  };
  const windows = { d7: win(from7, today), prev7: win(addD(today, -13), addD(today, -7)), d30: win(addD(today, -29), today),
    // v4: 7 complete days (to yesterday) and the 7 before them
    c7: win(addD(today, -7), addD(today, -1)), cp7: win(addD(today, -14), addD(today, -8)) };
  const idsByDay = new Map();
  for (const i of okAll.values()) (idsByDay.get(i.day) || idsByDay.set(i.day, new Set()).get(i.day)).add(i.id);
  const stick = [];
  for (let d = since; d <= today; d = addD(d, 1)) {
    const w = new Set();
    for (let q = 0; q < 7; q++) for (const id of idsByDay.get(addD(d, -q)) || []) w.add(id);
    stick.push({ date: d, dau: (idsByDay.get(d) || new Set()).size, wau: addD(d, -6) >= prevSince ? w.size : null });
  }
  let inactive = null;
  if (addD(today, -30) >= prevSince) {
    inactive = 0;
    for (const list of byId.values()) { const lastDay = list.at(-1).day; if (lastDay >= addD(today, -30) && lastDay <= addD(today, -14)) inactive++; }
  }
  const retention = { basis: 'new installs (first_start) of the days read, open again inside each window (day 1, 2 to 6, 7 to 13, 14 to 29, 30 to 59); only installs whose window is over',
    points: RET_WIN.map(([a, b], q) => { const due = members.filter((m) => m.ret[q] !== null); return { day: a, to: b, n: due.length, back: due.filter((m) => m.ret[q]).length }; }) };

  return { activation, stuck, errorsRecent, windows, stick, inactive, retention };
}

