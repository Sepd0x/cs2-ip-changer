// Production data for the dashboard (beta.9). data.js (the approved mock-up's makeData) only gives the shape: every number in it
// is set to 0 here and then filled from /api/summary and /api/live. What the app doesn't collect yet stays at 0 and is listed
// in the bar at the top. ?dev=1 shows the developer's own test PCs instead of everyone else.
'use strict';
(function () {
  const DEV = new URLSearchParams(location.search).get('dev') === '1';
  let RAW = null, LIVE = null, GH = null;
  const KEEP = new Set(['period', 'chartN', 'factor', 'dec', 'min', 'W', 'H', 'X0', 'COLS', 'ROWS']);
  function zero(o, k) {
    if (Array.isArray(o)) return o.map((v) => zero(v, k));
    if (o && typeof o === 'object') { const out = {}; for (const [kk, v] of Object.entries(o)) out[kk] = zero(v, kk); return out; }
    return typeof o === 'number' && !KEEP.has(k) ? 0 : o;
  }
  const q = (F) => new URLSearchParams({ period: F.period, version: F.version, country: F.country, mode: F.mode, link: F.link, ...(DEV ? { dev: 1 } : {}) });
  const get = async (u) => { const r = await fetch(u, { credentials: 'same-origin', cache: 'no-store' }); if (!r.ok) throw new Error(`${u}: ${r.status}`); return r.json(); };

  window.fetchReal = async function (F) {
    [RAW, LIVE, GH] = await Promise.all([get('/api/summary?' + q(F)), get('/api/live?' + q(F)), GH ? Promise.resolve(GH) : get('/api/github').catch(() => null)]);
  };
  window.fetchLiveReal = async function () { try { LIVE = await get('/api/live' + (DEV ? '?dev=1' : '')); return LIVE; } catch { return null; } };

  const short = (v) => String(v).replace(/^0\.1\.0-/, '');
  const COUNTRY = { PT: 'Portugal', BR: 'Brasil', US: 'EUA', ES: 'Espanha', DE: 'Alemanha', FR: 'França', PL: 'Polónia', RU: 'Rússia', CA: 'Canadá', GB: 'Reino Unido', '?': 'Desconhecido' };
  const pct = (a, b) => (b ? (100 * a) / b : null);

  window.realData = function (F) {
    const R = RAW;
    const D = zero(makeData({ period: F.period }));   // the mock's own filters only: real versions aren't in its tables
    D.asOf = R.asOf; D.now = R.now; D.period = F.period; D.filters = F;
    D.options = {
      version: [['all', 'Todas'], ...R.options.version.sort(compareV).reverse().map((v) => [v, short(v)])],
      country: [['all', 'Todos'], ...R.options.country.map((c) => [c, COUNTRY[c] || c])],
      mode: D.options.mode, link: D.options.link,
    };
    const rel = (GH && GH.releases) ? GH.releases.filter((r) => !r.draft).map((r) => ({ tag: short(r.tag.replace(/^v/, '')), date: r.date, exe: r.exe })) : [];
    D.releases = rel.slice().reverse();

    // Days, totals and the KPIs from real install-days
    const mk = (x) => ({ date: x.date, users: x.users, fresh: x.fresh, sessions: x.users, minutes: x.minutes, sessMed: x.users ? Math.round(x.minutes / x.users) : null,
      ipNew: x.ipNew, ipSame: x.ipSame, ipNone: x.ipNone, crashes: 0, matches: 0, laggy: 0, errors: x.errors, back1: 0, freshPrev: 0, back: Math.max(0, x.users - x.fresh) });
    D.days = R.days.map(mk);
    const T = {}; for (const k of Object.keys(D.days[0] || {})) if (k !== 'date' && k !== 'sessMed') T[k] = D.days.reduce((s, d) => s + (d[k] || 0), 0);
    T.daysWithData = D.days.filter((d) => d.users > 0).length;
    D.totals = Object.assign(D.totals, T);
    D.prevTotals = Object.assign(D.prevTotals, { users: R.prev.users, fresh: R.prev.fresh, minutes: R.prev.minutes });
    const ipAll = T.ipNew + T.ipSame + T.ipNone;
    const val = { usersPerDay: [T.daysWithData ? T.users / T.daysWithData : null, T.daysWithData], fresh: [T.fresh, T.fresh],
      minPerSession: [T.users ? T.minutes / T.users : null, T.users], ipNew: [pct(T.ipNew, ipAll), ipAll] };
    const ser = { usersPerDay: (d) => d.users, fresh: (d) => d.fresh, minPerSession: (d) => (d.users ? d.minutes / d.users : null), ipNew: (d) => pct(d.ipNew, d.ipNew + d.ipSame + d.ipNone) };
    for (const k of D.kpis) {
      const v = val[k.id];
      k.n = v ? v[1] : 0; k.value = v && k.n >= k.min ? v[0] : null; k.prev = null; k.prevN = 0;
      k.series = D.days.slice(-14).map((d) => (ser[k.id] ? ser[k.id](d) : null));
    }

    // Live: only counts
    if (LIVE) { Object.assign(D.live, LIVE); D.live.lastHour.open = D.live.lastHour.open.map(() => 0); D.live.lastHour.open[D.live.lastHour.open.length - 1] = LIVE.now; D.live.lastHour.game = D.live.lastHour.game.map(() => 0); D.live.lastHour.game[D.live.lastHour.game.length - 1] = LIVE.inGame; }
    D.live.feed = [];

    D.countries = R.countries.map((x) => ({ c: x.k, k: COUNTRY[x.k] || x.k, n: x.n }));
    if (Array.isArray(D.installAge)) for (const b of D.installAge) b.n = (R.age.find((a) => a.band === b.band) || { n: 0 }).n;

    // The app's screens: minutes, buttons, the heat map, dead and rage clicks
    const totalMin = Object.values(R.screens).reduce((s, x) => s + x.minutes, 0);
    const visits = {}; for (const p of R.paths) visits[p.to] = (visits[p.to] || 0) + p.n;
    const cellXY = (c, r) => [UI.X0 + ((c + 0.5) * (UI.W - UI.X0)) / UI.COLS, ((r + 0.5) * UI.H) / UI.ROWS];
    for (const s of UI.screens) {
      const out = D.screens[s.id]; if (!out) continue;
      const rs = R.screens[s.id] || { minutes: 0, days: 0 };
      out.minutes = rs.minutes; out.days = rs.days; out.visits = visits[s.id] || 0; out.share = totalMin ? Math.round((100 * rs.minutes) / totalMin) : 0;
      for (const b of s.buttons) {
        const r = R.buttons[b.id + '_Click'] || R.buttons[b.id] || { n: 0, days: 0 };
        out.buttons[b.id] = { n: r.n, days: r.days, trend: Array(14).fill(0), prev: 0, kb: 0 };
      }
      const h = R.heat[s.id] || { c: [], d: [], r: [] };
      if (out.grid) for (const [c, r, n] of h.c) if (out.grid[r] && c < UI.COLS) out.grid[r][c] += n;
      const near = (x, y) => { const b = s.buttons.find((q2) => x >= q2.r[0] - 4 && x <= q2.r[0] + q2.r[2] + 4 && y >= q2.r[1] - 4 && y <= q2.r[1] + q2.r[3] + 4); return b ? `${b.label} (desligado ou ao lado)` : 'Área sem botão'; };
      out.dead = h.d.map(([c, r, n]) => { const [x, y] = cellXY(c, r); return { c, r, n, days: n, x, y, label: near(x, y) }; });
      out.rage = h.r.map(([c, r, n]) => { const [x, y] = cellXY(c, r); return { c, r, n, days: n, x, y, label: near(x, y).replace(' (desligado ou ao lado)', '') }; });
      out.scroll = null;
    }
    D.paths = R.paths;
    D.firstScreen = [];

    // Lists that carry the mock's names (versions, places in the code) go, or get the real ones; dated series follow the real days
    const dates = D.days.map((x) => x.date);
    const T7 = Math.max(1, T.users);
    D.rel.crashPlaces = [];
    D.rel.errors = R.errors.map((e) => ({ type: e.type, at: e.at, ver: short(e.app), p: e.days / T7, days: e.days, fresh: false, up: false, n: e.n, trend: Array(14).fill(0) }));
    D.rel.crashSeries = dates.map((date) => ({ date, crashes: 0, sessions: 0 }));
    const vers = R.versions.map((v) => short(v.k));
    D.versions.adoption = dates.map((date) => ({ date, shares: Object.fromEntries(vers.map((v) => [v, 0])) }));
    const met = {};
    for (const v of R.versions) met[short(v.k)] = { days: v.n, sessions: v.n, crashes: 0, ipNew: [0, 0], lag: [0, 0], minMed: 0, errors: R.errors.filter((e) => e.app === v.k).reduce((s, e) => s + e.n, 0), toProtected: '-', startup: '-' };
    D.versions.metrics = met;
    D.phone.results = { novo: T.ipNew, mesmo: T.ipSame, sem: T.ipNone };
    D.rel.updates.from = [];
    if (!Object.keys(met).length) met['-'] = { days: 0, sessions: 0, crashes: 0, ipNew: [0, 0], lag: [0, 0], minMed: 0, errors: 0, toProtected: '-', startup: '-' };
    D.newest = R.versions.length ? short(R.versions.map((v) => v.k).sort(compareV).at(-1)) : null;
    D.github.releases = rel.map((r) => ({ tag: r.tag, date: r.date, exe: r.exe })); D.github.days = [];
    D.phone.series = D.days.map((x) => ({ date: x.date, novo: x.ipNew, mesmo: x.ipSame, sem: x.ipNone }));
    const kc = R.counters;
    D.cs2.kicks = [{ k: 'Kick (servidor cheio, AFK…)', n: kc.kick_seen || 0 }, { k: 'VPN visto pelo servidor', n: kc.vpn_seen || 0, tone: 'warn' }, { k: 'Ban marcado', n: kc.ban_seen || 0, tone: 'bad' }];

    D.alerts.list = R.automated ? [{ tone: 'warn', kind: 'auto', text: 'Análises automáticas deixadas de fora', n: R.automated,
      nText: `${R.automated} instalações-dia sem conta Steam, sem cliques e sem minutos (antivírus online como o VirusTotal)${R.automatedDc ? `, ${R.automatedDc} de datacenters` : ''}`, when: D.asOf }] : [];

    if (GH && !GH.problems) {
      const g = D.github;
      Object.assign(g, { stars: GH.stars ?? 0, forks: GH.forks ?? 0, watchers: GH.watchers ?? 0, issues: GH.issues ?? 0,
        views: GH.views?.count ?? 0, uniques: GH.views?.uniques ?? 0, clones: GH.clones?.count ?? 0, cloneUniques: GH.clones?.uniques ?? 0,
        days: (GH.views?.days || []).map((x) => ({ date: x.day, n: x.n, u: x.u })),
        downloadsPeriod: rel.reduce((s, r) => s + (r.date >= addDaysR(R.asOf, -(F.period - 1)) ? r.exe : 0), 0),
        releases: rel.map((r) => ({ tag: r.tag, date: r.date, exe: r.exe })),
        referrers: (GH.referrers || []).map((r) => ({ k: r.name, n: r.n, u: r.u })), paths: (GH.paths || []).map((p) => ({ k: p.name, n: p.n, u: p.u })) });
    }
    D.realBar = `${DEV ? 'Os teus PCs de teste (dev). ' : ''}Dados reais desde ${D.days[0]?.date ?? D.asOf}. Ainda sem recolha (beta.10): partidas e lag, crashes, sessões, ambiente, atualizações, scroll.`;
    return D;
  };

  function addDaysR(d, n) { const t = new Date(d + 'T00:00:00Z'); t.setUTCDate(t.getUTCDate() + n); return t.toISOString().slice(0, 10); }
  function compareV(a, b) {
    const p = (v) => { const [m, pre] = String(v).split(/-(.*)/); return [m.split('.').map(Number), pre ? pre.split('.').map((x) => (/^\d+$/.test(x) ? +x : x)) : []]; };
    const [am, ap] = p(a), [bm, bp] = p(b);
    for (let i = 0; i < 3; i++) if ((am[i] || 0) !== (bm[i] || 0)) return (am[i] || 0) - (bm[i] || 0);
    for (let i = 0; i < Math.max(ap.length, bp.length); i++) { if (ap[i] === bp[i]) continue; if (ap[i] === undefined) return -1; if (bp[i] === undefined) return 1; return ap[i] < bp[i] ? -1 : 1; }
    return 0;
  }
})();
