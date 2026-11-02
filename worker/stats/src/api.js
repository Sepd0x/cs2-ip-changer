// The new dashboard's data (beta.9): /api/summary and /api/live, behind the same Access check as the page. Only added-up
// numbers leave here, never an install ID or a row per install. Unit: the install-day (the app's ID is new every day).
//
// Real or automated: online virus scanners run the exe in a sandbox, accept the notice by themselves and report (5 of 5 outside
// installs up to 2 nov. 2026 were these: no Steam account, no click, 0 minutes). An install-day counts as real when it has a Steam
// account, a click or a minute; the rest is shown apart as "automated".

const PERIODS = [1, 7, 30, 90];
const MAX_ROWS = 50000;   // enough for now; past this the queries need to add up in SQL instead of here
const SCREENS = ['connection', 'servers', 'regions', 'accounts', 'configs', 'profiles', 'log', 'settings'];
const AGE = { age_0: '0', age_1: '1', age_2_3: '2-3', age_4_6: '4-6', age_7_13: '7-13', age_14_29: '14-29', age_30p: '30+' };
// Counted by the app, not real counters for the charts (list sizes in bands, the beta.9 extras)
const NOT_COUNTERS = /^(servers|accounts|flags|open_with|regions_blocked|scr_.*|path_.*|age_.*|net_dc)$/;

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
export async function summary(db, { period, f, dev, now }) {
  const since = day(now - (period - 1) * 864e5);
  const prevSince = day(now - (2 * period - 1) * 864e5);
  const d = dev ? 1 : 0;
  const all = async (sql, ...args) => (await db.prepare(sql).bind(...args).all()).results ?? [];
  const [pings, counts, clicks, heat, errors] = await Promise.all([
    all(`SELECT day, id, MIN(COALESCE(SUM(minutes), 0), 1440) AS m, MAX(app) AS app, MAX(country) AS country, MAX(mode) AS mode, MAX(link) AS link
         FROM pings WHERE dev = ? AND day >= ? GROUP BY day, id LIMIT ${MAX_ROWS}`, d, prevSince),
    all(`SELECT day, id, name, SUM(n) AS n FROM counts WHERE dev = ? AND day >= ? GROUP BY day, id, name LIMIT ${MAX_ROWS}`, d, prevSince),
    all(`SELECT day, id, name, SUM(n) AS n FROM clicks WHERE dev = ? AND day >= ? GROUP BY day, id, name LIMIT ${MAX_ROWS}`, d, since),
    all(`SELECT screen, kind, cell, app, SUM(n) AS n FROM heat WHERE dev = ? AND day >= ? GROUP BY screen, kind, cell, app`, d, since).catch(() => []),
    all(`SELECT day, id, type, at, app, SUM(n) AS n FROM errors WHERE dev = ? AND day >= ? GROUP BY day, id, type, at, app LIMIT ${MAX_ROWS}`, d, since),
  ]);

  // One entry per install-day, with what decides real or automated
  const key = (r) => `${r.day}|${r.id}`;
  const inst = new Map();
  for (const p of pings) inst.set(key(p), { day: p.day, m: p.m, app: p.app, country: p.country || '?', mode: p.mode || '?', link: p.link || '?', acc: 0, clicks: 0, c: {}, dc: false });
  for (const c of counts) {
    const i = inst.get(key(c)); if (!i) continue;
    if (c.name === 'accounts') i.acc = Math.max(i.acc, c.n);
    else if (c.name === 'net_dc') i.dc = true;
    i.c[c.name] = (i.c[c.name] || 0) + c.n;
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
  for (let t = period - 1; t >= 0; t--) days.push({ date: day(now - t * 864e5), users: 0, fresh: 0, minutes: 0, ipNew: 0, ipSame: 0, ipNone: 0, errors: 0, automated: 0 });
  const byDay = new Map(days.map((x) => [x.date, x]));
  for (const [, i] of inWin) {
    const x = byDay.get(i.day); if (!x) continue;
    x.users++; x.minutes += i.m;
    x.fresh += i.c.first_start ? 1 : 0;
    x.ipNew += i.c.ip_changed || 0; x.ipSame += i.c.ip_same || 0; x.ipNone += i.c.ip_no_network || 0;
  }
  for (const [dd, n] of Object.entries(autoDays)) if (byDay.has(dd)) byDay.get(dd).automated = n;
  const prev = { users: inPrev.length, fresh: inPrev.filter(([, i]) => i.c.first_start).length, minutes: inPrev.reduce((s, [, i]) => s + i.m, 0) };

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

  const btn = {};
  for (const c of clicks) {
    if (!ok.has(key(c))) continue;
    const b = btn[c.name] || (btn[c.name] = { n: 0, days: 0 });
    b.n += c.n; b.days++;
  }

  // The heat map has no install ID (added up on arrival): only the version filter applies to it
  const heatOut = {};
  for (const h of heat) {
    if (f.version !== 'all' && h.app !== f.version) continue;
    const m = /^(\d+),(\d+)$/.exec(h.cell); if (!m) continue;
    const s = heatOut[h.screen] || (heatOut[h.screen] = { c: [], d: [], r: [] });
    if (s[h.kind]) s[h.kind].push([+m[1], +m[2], h.n]);
  }

  const err = {};
  for (const e of errors) {
    if (!ok.has(key(e))) continue;
    const k = `${e.type}|${e.at}|${e.app}`;
    const x = err[k] || (err[k] = { type: e.type, at: e.at, app: e.app, n: 0, days: 0 });
    x.n += e.n; x.days++;
  }

  const versionsAll = [...new Set([...inst.values()].map((i) => i.app))];
  const countriesAll = [...new Set([...inst.values()].map((i) => i.country))].sort();
  return {
    asOf: day(now), now: new Date(now).toISOString().slice(11, 16), period, filters: f, dev,
    options: { version: versionsAll, country: countriesAll },
    days, prev, automated, automatedDc,
    countries: tally((i) => i.country), versions: tally((i) => i.app), modes: tally((i) => i.mode), links: tally((i) => i.link),
    counters, screens, paths, age: Object.entries(age).map(([band, n]) => ({ band, n })),
    buttons: btn, heat: heatOut,
    errors: Object.values(err).sort((a, b) => b.n - a.n).slice(0, 40),
  };
}

/** Who has the app open now: install-days with a signal in the last 6 minutes, only as counts. */
export async function live(db, { dev, now }) {
  let rows = [];
  try {
    rows = (await db.prepare('SELECT app, mode, link, screen, cs2, prot, tray FROM live WHERE dev = ? AND ts >= ?').bind(dev ? 1 : 0, now - 6 * 60_000).all()).results ?? [];
  } catch { rows = []; }
  const count = (get) => { const m = {}; for (const r of rows) { const k = get(r) ?? '?'; m[k] = (m[k] || 0) + 1; } return Object.entries(m).map(([k, n]) => ({ k, n })).sort((a, b) => b.n - a.n); };
  return {
    now: rows.length, inGame: rows.filter((r) => r.cs2 === 1).length, protectedN: rows.filter((r) => r.prot === 1).length,
    byVersion: count((r) => r.app), byMode: count((r) => r.mode), byLink: count((r) => r.link),
    byScreen: count((r) => (r.tray === 1 ? 'tray' : r.screen)),
  };
}

