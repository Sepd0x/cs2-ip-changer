// The new dashboard's data (beta.9): /api/summary and /api/live, behind the same Access check as the page. Only added-up
// numbers leave here, never an install ID or a row per install. Unit: the install-day (one install open on one day). Since
// beta.9 the app keeps its random ID per install (pseudonymous), so installs, returns and cohorts are also counted exactly.
//
// Real or automated: online virus scanners run the exe in a sandbox, accept the notice by themselves and report (5 of 5 outside
// installs up to 2 nov. 2026 were these: no Steam account, no click, 0 minutes). An install-day counts as real when it has a Steam
// account, a click or a minute; the rest is shown apart as "automated".

import { DAILY_ROWS, LIVE_ROWS } from './index.js';

const PERIODS = [1, 7, 30, 90];
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
export function lisbonSlot(hourIndex) {
  const parts = Object.fromEntries(LISBON.formatToParts(new Date(hourIndex * 3600_000)).map((p) => [p.type, p.value]));
  return [WEEK[parts.weekday] ?? 0, Number(parts.hour) % 24];
}
/** A time (ms) as the date and hour in Lisbon: the v3 "Por hora" counts in Lisbon hours, like "Quando usam". */
const LISBON_DAY = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Lisbon', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', hourCycle: 'h23' });
export function lisbonHour(ts) {
  const p = Object.fromEntries(LISBON_DAY.formatToParts(new Date(ts)).map((x) => [x.type, x.value]));
  return { date: `${p.year}-${p.month}-${p.day}`, hour: Number(p.hour) % 24 };
}
const SCREENS = ['connection', 'servers', 'regions', 'accounts', 'configs', 'profiles', 'log', 'settings'];
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
// Phone setup steps (PhoneSetup.Key of PhoneStep), in the app's order
const SETUP = [['noadbtool', 'Sem ADB'], ['adbfailed', 'ADB falhou'], ['nousb', 'Sem cabo USB'], ['usbdrivererror', 'Driver USB'], ['nousbdebugging', 'Depuração USB desligada'],
  ['unauthorized', 'Depuração por autorizar'], ['offline', 'Telemóvel offline'], ['tetheroff', 'Tethering desligado'], ['wifion', 'Wi-Fi do telemóvel ligado'], ['pconphone', 'PC no telemóvel'], ['done', 'Pronto']];
// The Checks panel's rows (chk_ + Telemetry.CheckKeys), by the title the app shows
const CHECKS = [['egress', 'Phone connection'], ['routes', 'Servers through the phone'], ['cs2', 'CS2 only through the phone'], ['kill', 'Block if the phone drops'],
  ['firewall', 'Windows Firewall'], ['diff', 'Phone IP is not your home IP'], ['data', 'Mobile data'], ['quality', 'Connection quality'],
  ['steam', 'Steam through the phone'], ['pc', 'Other apps'], ['regions', 'Matchmaking regions']];
// Install age bands to the cohort table's columns (days after the install: 0, 1, 3, 7, 14, 30) and the band's first day
const COHORT = { age_0: [0, 0], age_1: [1, 1], age_2_3: [2, 2], age_7_13: [3, 7], age_14_29: [4, 14], age_30p: [5, 30] };
// Valve's public datacenter codes (Regions page) to the city, for the dashboard; any other shows as its code
const CITY = { fra: 'Frankfurt', mad: 'Madrid', waw: 'Varsóvia', ams: 'Amesterdão', lhr: 'Londres', par: 'Paris', sto: 'Estocolmo', vie: 'Viena', hel: 'Helsínquia',
  iad: 'Sterling', ord: 'Chicago', atl: 'Atlanta', lax: 'Los Angeles', dfw: 'Dallas', sea: 'Seattle', gru: 'São Paulo', scl: 'Santiago', lim: 'Lima', eze: 'Buenos Aires',
  hkg: 'Hong Kong', sgp: 'Singapura', tyo: 'Tóquio', syd: 'Sydney', dxb: 'Dubai', bom: 'Mumbai', maa: 'Chennai', jnb: 'Joanesburgo', sha: 'Xangai', can: 'Cantão', seo: 'Seul' };
const WINDOWS = { 19045: 'Windows 10 22H2', 22621: 'Windows 11 22H2', 22631: 'Windows 11 23H2', 26100: 'Windows 11 24H2', 26200: 'Windows 11 25H2' };
const ENV_LABEL = { win: { s: 'Pequena', m: 'Média', l: 'Grande' }, scale: { '100': '100%', '125': '125%', '150': '150%', '175p': '175%+' }, net: { '5g': '5G', '4g': '4G', '3g': '3G', '2g': '2G' },
  brand: { samsung: 'Samsung', google: 'Google', xiaomi: 'Xiaomi', oneplus: 'OnePlus', motorola: 'Motorola', oppo: 'OPPO', realme: 'realme', vivo: 'vivo', huawei: 'Huawei',
    honor: 'Honor', nothing: 'Nothing', sony: 'Sony', asus: 'ASUS', nokia: 'Nokia', zte: 'ZTE', tcl: 'TCL', other: 'Outra' } };

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
export async function summary(db, { period, f, dev, now, exclude = [] }) {
  const since = day(now - (period - 1) * 864e5);
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
  const [pings, counts, clicks, heat, errors, envRows, installDays, hours, last] = await Promise.all([
    // D7: the version and the mode of the day are the newest report's, not the highest text ("0.1.0-beta.10" < "0.1.0-beta.9" as
    // text). D3: the link is the best the install-day had (USB, then hotspot), so a report from before the phone was up doesn't win.
    capped('pings', `SELECT day, id, MIN(COALESCE(SUM(minutes), 0), 1440) AS m, SUBSTR(MAX(ts || '|' || app), 15) AS app, MAX(country) AS country,
         SUBSTR(MAX(ts || '|' || COALESCE(mode, '')), 15) AS mode, MAX(CASE link WHEN 'usb' THEN 3 WHEN 'hotspot' THEN 2 WHEN 'other' THEN 1 WHEN 'none' THEN 0 END) AS lk,
         MAX(os) AS os FROM pings WHERE ${W.sql} AND day >= ? GROUP BY day, id ORDER BY day DESC`, ...W.args, prevSince),
    capped('counts', `SELECT day, id, name, SUM(n) AS n, MAX(n) AS mx FROM counts WHERE ${W.sql} AND day >= ? GROUP BY day, id, name ORDER BY day DESC`, ...W.args, prevSince),
    capped('clicks', `SELECT day, id, name, SUM(n) AS n FROM clicks WHERE ${W.sql} AND day >= ? GROUP BY day, id, name ORDER BY day DESC`, ...W.args, since),
    soft('heat', all(`SELECT screen, kind, cell, app, SUM(n) AS n FROM heat WHERE dev = ? AND day >= ? GROUP BY screen, kind, cell, app`, d, since)),
    capped('errors', `SELECT day, id, type, at, app, SUM(n) AS n FROM errors WHERE ${W.sql} AND day >= ? GROUP BY day, id, type, at, app ORDER BY day DESC`, ...W.args, since),
    soft('env', all('SELECT k, v, SUM(n) AS n FROM env WHERE dev = ? AND day >= ? GROUP BY k, v', d, since)),
    capped('installDays', `SELECT DISTINCT p.day, p.id FROM pings p JOIN (SELECT DISTINCT id FROM counts WHERE name = 'id_install' AND ${W.sql}) k ON k.id = p.id
         WHERE ${Wp.sql} ORDER BY p.day DESC`, ...W.args, ...Wp.args),
    // D9: when the app is open, from the reports' times (each install counts once an hour)
    capped('hours', `SELECT day, id, CAST(ts / 3600000 AS INTEGER) AS h FROM pings WHERE ${W.sql} AND day >= ? GROUP BY day, id, h ORDER BY day DESC`, ...W.args, since),
    // The dashboard's freshness bar: the newest report in this view (last 90 days, on the day index)
    all(`SELECT MAX(ts) AS t FROM pings WHERE ${W.sql} AND day >= ?`, ...W.args, day(now - 89 * 864e5)),
  ]);
  // v3 (Nov. 2026), all small reads on the day index: the newest report from any PC (dev builds and EXCLUDE_IDS too), the day's
  // row budget (index.js), the reports of the last 3 days with their times (Por hora, yesterday at the same time) and, for each
  // error in the last 90 days, the first day and version it was seen in
  const [lastAnyR, budgetR, recentR, errFirstR] = await Promise.all([
    all('SELECT MAX(ts) AS t FROM pings WHERE day >= ?', day(now - 89 * 864e5)),
    soft('budget', all('SELECT day, rows FROM budget WHERE day IN (?, ?)', day(now), `${day(now)}:live`)),
    capped('recent', `SELECT day, id, ts, minutes FROM pings WHERE ${W.sql} AND day >= ? ORDER BY day DESC`, ...W.args, day(now - 2 * 864e5)),
    all(`SELECT type, at, MIN(day) AS first, SUBSTR(MIN(day || '|' || app), 12) AS app FROM errors WHERE ${W.sql} AND day >= ? GROUP BY type, at`, ...W.args, day(now - 89 * 864e5)),
  ]);

  // One entry per install-day, with what decides real or automated
  const key = (r) => `${r.day}|${r.id}`;
  const inst = new Map();
  for (const p of pings) inst.set(key(p), { day: p.day, id: p.id, m: p.m, app: p.app, country: p.country || '?', mode: p.mode || '?', link: LINK_RANK[p.lk] || '?', os: p.os || '?', acc: 0, clicks: 0, c: {}, dc: false });
  for (const c of counts) {
    const i = inst.get(key(c)); if (!i) continue;
    if (c.name === 'accounts') i.acc = Math.max(i.acc, c.mx ?? c.n);
    else if (c.name === 'net_dc') i.dc = true;
    // A list size is a state, sent again in later reports of the day: the highest band, never the sum of the bands
    i.c[c.name] = SIZES.includes(c.name) ? Math.max(i.c[c.name] || 0, c.mx ?? c.n) : (i.c[c.name] || 0) + c.n;
  }
  for (const c of clicks) { const i = inst.get(key(c)); if (i) i.clicks += c.n; }
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
  for (let t = period - 1; t >= 0; t--) days.push({ date: day(now - t * 864e5), users: 0, fresh: 0, minutes: 0, ipNew: 0, ipSame: 0, ipNone: 0, errors: 0, automated: 0, sessions: 0, crashes: 0, matches: 0, laggy: 0 });
  const byDay = new Map(days.map((x) => [x.date, x]));
  for (const [, i] of inWin) {
    const x = byDay.get(i.day); if (!x) continue;
    x.users++; x.minutes += i.m;
    x.fresh += i.c.first_start ? 1 : 0;
    x.ipNew += i.c.ip_changed || 0; x.ipSame += i.c.ip_same || 0; x.ipNone += i.c.ip_no_network || 0;
    x.sessions += i.c.app_start || 0; x.crashes += i.c.crash || 0;
    x.matches += (i.c.m_valve || 0) + (i.c.m_comm || 0); x.laggy += (i.c.m_valve_lag || 0) + (i.c.m_comm_lag || 0);
  }
  for (const [dd, n] of Object.entries(autoDays)) if (byDay.has(dd)) byDay.get(dd).automated = n;
  // The period before, with the same numbers the KPIs compare (D9: before only users, new and minutes)
  const sumPrev = (name) => inPrev.reduce((s, [, i]) => s + (i.c[name] || 0), 0);
  const prev = { users: inPrev.length, fresh: inPrev.filter(([, i]) => i.c.first_start).length, minutes: inPrev.reduce((s, [, i]) => s + i.m, 0),
    daysWithData: new Set(inPrev.map(([, i]) => i.day)).size, ipNew: sumPrev('ip_changed'), ipSame: sumPrev('ip_same'), ipNone: sumPrev('ip_no_network'),
    matches: sumPrev('m_valve') + sumPrev('m_comm'), laggy: sumPrev('m_valve_lag') + sumPrev('m_comm_lag'), sessions: sumPrev('app_start'), crashes: sumPrev('crash') };

  // D9: the first day of a new install, each step inside the one before on the same install-day: the notice accepted
  // (first_start), the phone on (a report with USB or hotspot, or Phone setup done) and a first Change IP with a new IP
  const f1 = inWin.filter(([, i]) => i.c.first_start);
  const f2 = f1.filter(([, i]) => i.link === 'usb' || i.link === 'hotspot' || i.c.phone_setup_done);
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

  // Counters and screens over the real install-days only
  const counters = {}, screens = {}, pathsM = {}, age = {};
  for (const s of SCREENS) screens[s] = { minutes: 0, days: 0 };
  for (const [, i] of inWin)
    for (const [name, n] of Object.entries(i.c)) {
      if (name.startsWith('scr_')) { const s = name.slice(4); if (screens[s]) { screens[s].minutes += n; screens[s].days++; } }
      else if (name.startsWith('path_')) { pathsM[name.slice(5)] = (pathsM[name.slice(5)] || 0) + n; }
      else if (AGE[name]) age[AGE[name]] = (age[AGE[name]] || 0) + 1;
      else if (!NOT_COUNTERS.test(name)) counters[name] = (counters[name] || 0) + n;
    }
  const paths = Object.entries(pathsM).map(([p, n]) => {
    const [from, to] = SCREENS.flatMap((a) => SCREENS.filter((b) => b !== a && p === `${a}_${b}`).map((b) => [a, b]))[0] || [];
    return from ? { from, to, n } : null;
  }).filter(Boolean).sort((a, b) => b.n - a.n);

  // Each button with its clicks per day too (the dashboard's 14-day trend)
  const btn = {};
  for (const c of clicks) {
    if (!ok.has(key(c))) continue;
    const b = btn[c.name] || (btn[c.name] = { n: 0, days: 0, perDay: {} });
    b.n += c.n; b.days++; b.perDay[c.day] = (b.perDay[c.day] || 0) + c.n;
  }

  // The heat map has no install ID (added up on arrival): only the version filter applies to it
  const heatOut = {};
  for (const h of heat) {
    if (f.version !== 'all' && h.app !== f.version) continue;
    const m = /^(\d+),(\d+)$/.exec(h.cell); if (!m) continue;
    const s = heatOut[h.screen] || (heatOut[h.screen] = { c: [], d: [], r: [] });
    if (s[h.kind]) s[h.kind].push([+m[1], +m[2], h.n]);
  }

  // beta.9 part 2. Matches by how the phone was connected that install-day (its last report), sessions, scroll, settings, days active
  const C = (name) => inWin.reduce((s, [, i]) => s + (i.c[name] || 0), 0);
  const days_ = (name) => inWin.filter(([, i]) => i.c[name]).length;
  const byLink = (name) => {
    const o = { usb: 0, hotspot: 0 };
    for (const [, i] of inWin) if (i.link === 'usb' || i.link === 'hotspot') o[i.link] += i.c[name] || 0;
    return o;
  };
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
  for (const [, i] of inWin) for (const name of Object.keys(i.c)) if (name.startsWith('set_')) settings[name.slice(4)] = (settings[name.slice(4)] || 0) + 1;
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
  for (const [, ds] of seen) {
    ds.sort();
    const first = ds[0];
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
  for (const [, i] of inWin) for (const name of Object.keys(i.c)) if (name.startsWith('reg_')) regionsM[name.slice(4)] = (regionsM[name.slice(4)] || 0) + 1;
  const regions = Object.entries(regionsM).map(([code, n]) => ({ code, name: CITY[code.replace(/\d$/, '')] || code.toUpperCase(), n })).sort((a, b) => b.n - a.n).slice(0, 15);
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
    const v = perVer[i.app] || (perVer[i.app] = { days: 0, sessions: 0, crashes: 0, ipNew: [0, 0], lag: [0, 0], min: [], errors: 0, tp: {}, st: {} });
    const c = (n) => i.c[n] || 0;
    v.days++; v.sessions += c('app_start'); v.crashes += c('crash');
    v.ipNew[0] += c('ip_changed'); v.ipNew[1] += c('ip_changed') + c('ip_same') + c('ip_no_network');
    v.lag[0] += c('m_valve_lag') + c('m_comm_lag'); v.lag[1] += c('m_valve') + c('m_comm');
    if (i.m >= 1) v.min.push(i.m / Math.max(1, c('app_start')));
    for (const [b] of BANDS.tp) v.tp[b] = (v.tp[b] || 0) + c(`tp_${b}`);
    for (const [b] of BANDS.st) v.st[b] = (v.st[b] || 0) + c(`st_${b}`);
    const a = adoptM[i.day] || (adoptM[i.day] = {}); a[i.app] = (a[i.app] || 0) + 1;
  }
  for (const e of errors) if (ok.has(key(e)) && !e.type.startsWith('Fatal_') && perVer[e.app]) perVer[e.app].errors += e.n;
  const versionMetrics = Object.fromEntries(Object.entries(perVer).map(([app, v]) => {
    const m = v.min.sort((a, b) => a - b);
    return [app, { days: v.days, sessions: v.sessions, crashes: v.crashes, ipNew: v.ipNew, lag: v.lag, errors: v.errors,
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
  for (const r of recentR) {
    const i = okAll.get(key(r)); if (!i) continue;
    const h = lisbonHour(r.ts);
    if (hourSets[h.date]) hourSets[h.date][h.hour].add(r.id);
    if (r.day === yday && r.ts <= cut) { const x = same.get(r.id) || same.set(r.id, { m: 0, fresh: !!i.c.first_start }).get(r.id); x.m = Math.min(1440, x.m + (r.minutes || 0)); }
  }
  const hourly = { today: hourSets[lt.date].map((s, h) => (h > lt.hour ? null : s.size)), yday: hourSets[ly].map((s) => s.size), hour: lt.hour };
  // Counted once a day with no time of their own: the new ones are those whose day has first_start; IP changes have no time at all
  const sameTime = { users: same.size, fresh: [...same.values()].filter((x) => x.fresh).length, minutes: [...same.values()].reduce((s, x) => s + x.m, 0) };
  const used = new Map(budgetR.map((r) => [r.day, r.rows]));
  const fresh = { lastTs: last[0]?.t ?? null, lastAny: lastAnyR[0]?.t ?? null,
    budget: { day: today, rows: used.get(today) || 0, cap: DAILY_ROWS, live: used.get(`${today}:live`) || 0, liveCap: LIVE_ROWS } };
  // Each error with the first day and version it was seen in, in this view and the last 90 days
  const firstOf = new Map(errFirstR.map((r) => [`${r.type}|${r.at}`, { day: r.first, app: r.app }]));
  const errOut = Object.values(err).sort((a, b) => b.n - a.n).slice(0, 40).map((e) => ({ ...e, first: firstOf.get(`${e.type}|${e.at}`) || null }));

  return {
    asOf: day(now), now: new Date(now).toISOString().slice(11, 16), period, filters: f, dev,
    lastTs: last[0]?.t ?? null, truncated, warnings, excluded: (exclude || []).length,
    options: { version: versionsAll, country: countriesAll },
    days, prev, automated, automatedDc, funnel, when, sizes,
    countries: tally((i) => i.country), versions: tally((i) => i.app), modes: tally((i) => i.mode), links: tally((i) => i.link), oses: tally((i) => i.os),
    counters, screens, paths, age: Object.entries(age).map(([band, n]) => ({ band, n })),
    buttons: btn, heat: heatOut,
    errors: errOut,
    // v3 (Nov. 2026): added, nothing above changed
    fresh, recentDays, hourly, sameTime, funnelProtected, funnelDays, cohortsDaily,
    cs2: { ...cs2, rejoin, regions, regionsDen: inWin.length, sessions: C('cs2_start'), hoursProtected: Math.round(C('min_prot') / 60), hoursCs2: Math.round(C('min_cs2') / 60) }, phone, firstScreen, sessions, updates, scroll, settings: Object.entries(settings).map(([name, n]) => ({ name, n })).sort((a, b) => b.n - a.n),
    active, setup, setupOpened: opened, cohorts: cohortList, cohortsExact, installs, env,
    checks, versionMetrics, adoption,
    crashPlaces: Object.values(crashPlaces).sort((a, b) => b.n - a.n).slice(0, 20),
  };
}

/** Who has the app open now: installs with a signal in the last 6 minutes, only as counts, and when the last signal came. */
export async function live(db, { dev, now, exclude = [] }) {
  const W = who(dev, exclude);
  let rows = [], lastTs = null;
  try {
    rows = (await db.prepare(`SELECT app, mode, link, screen, cs2, prot, tray FROM live WHERE ${W.sql} AND ts >= ?`).bind(...W.args, now - 6 * 60_000).all()).results ?? [];
    lastTs = (await db.prepare(`SELECT ts FROM live WHERE ${W.sql} ORDER BY ts DESC LIMIT 1`).bind(...W.args).first())?.ts ?? null;
  } catch { rows = []; }
  const count = (get) => { const m = {}; for (const r of rows) { const k = get(r) ?? '?'; m[k] = (m[k] || 0) + 1; } return Object.entries(m).map(([k, n]) => ({ k, n })).sort((a, b) => b.n - a.n); };
  return {
    now: rows.length, inGame: rows.filter((r) => r.cs2 === 1).length, protectedN: rows.filter((r) => r.prot === 1).length, lastTs,
    byVersion: count((r) => r.app), byMode: count((r) => r.mode), byLink: count((r) => r.link),
    byScreen: count((r) => (r.tray === 1 ? 'tray' : r.screen)),
  };
}

