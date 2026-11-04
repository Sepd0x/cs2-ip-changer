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
    const mk = (x) => ({ date: x.date, users: x.users, fresh: x.fresh, sessions: x.sessions, minutes: x.minutes, sessMed: x.users ? Math.round(x.minutes / x.users) : null,
      ipNew: x.ipNew, ipSame: x.ipSame, ipNone: x.ipNone, crashes: x.crashes, matches: x.matches, laggy: x.laggy, errors: x.errors, back1: 0, freshPrev: 0, back: Math.max(0, x.users - x.fresh) });
    D.days = R.days.map(mk);
    const T = {}; for (const k of Object.keys(D.days[0] || {})) if (k !== 'date' && k !== 'sessMed') T[k] = D.days.reduce((s, d) => s + (d[k] || 0), 0);
    T.daysWithData = D.days.filter((d) => d.users > 0).length;
    D.totals = Object.assign(D.totals, T);
    D.prevTotals = Object.assign(D.prevTotals, { users: R.prev.users, fresh: R.prev.fresh, minutes: R.prev.minutes });
    const ipAll = T.ipNew + T.ipSame + T.ipNone;
    const val = { usersPerDay: [T.daysWithData ? T.users / T.daysWithData : null, T.daysWithData], fresh: [T.fresh, T.fresh],
      minPerSession: [T.users ? T.minutes / T.users : null, T.users], ipNew: [pct(T.ipNew, ipAll), ipAll],
      lag: [pct(T.laggy, T.matches), T.matches], crashFree: [T.sessions ? 100 - pct(T.crashes, T.sessions) : null, T.sessions] };
    const ser = { usersPerDay: (d) => d.users, fresh: (d) => d.fresh, minPerSession: (d) => (d.users ? d.minutes / d.users : null), ipNew: (d) => pct(d.ipNew, d.ipNew + d.ipSame + d.ipNone),
      lag: (d) => pct(d.laggy, d.matches), crashFree: (d) => (d.sessions ? 100 - pct(d.crashes, d.sessions) : null) };
    // Nov. 2026 (audit D4, D9, D10, D11): per day over the whole period (not only the days with data); the day after from the kept
    // IDs; the period before from the server; exact numbers with their n (the page is private, behind Access); labels that say
    // what is counted. With "Hoje" the day before is a whole day, so only the rates compare.
    const P = R.prev || {}, ipPrev = (P.ipNew || 0) + (P.ipSame || 0) + (P.ipNone || 0), nd = (R.installs && R.installs.nextDay) || { n: 0, back: 0 };
    val.usersPerDay = [T.users / F.period, T.users];
    val.nextDay = [pct(nd.back, nd.n), nd.n];
    const prevVal = { usersPerDay: F.period > 1 ? [P.users / F.period, P.users] : null, fresh: F.period > 1 ? [P.fresh, P.fresh] : null,
      minPerSession: [P.users ? P.minutes / P.users : null, P.users], ipNew: [pct(P.ipNew, ipPrev), ipPrev], lag: [pct(P.laggy, P.matches), P.matches],
      crashFree: [P.sessions ? 100 - pct(P.crashes, P.sessions) : null, P.sessions] };
    const LABEL = { usersPerDay: ['Instalações abertas por dia', 'instalações-dia no período'], fresh: ['1.º arranque', 'aceitaram o aviso'],
      nextDay: ['Voltam no dia seguinte', 'novas até anteontem'], minPerSession: ['Minutos por inst.-dia', 'instalações-dia · média'] };
    for (const k of D.kpis) {
      const v = val[k.id], pv = prevVal[k.id];
      if (LABEL[k.id]) [k.label, k.nUnit] = LABEL[k.id];
      k.n = v ? v[1] : 0; k.value = v && (k.n >= 1 || k.id === 'fresh') ? v[0] : null;
      k.prev = pv && pv[1] >= 1 ? pv[0] : null; k.prevN = pv ? pv[1] : 0;
      k.series = D.days.slice(-14).map((d) => (ser[k.id] ? ser[k.id](d) : null));
    }

    // Live: only counts. No history is kept, so the last hour starts empty and fills while the page is open; no event feed yet.
    if (LIVE) { Object.assign(D.live, LIVE); D.live.lastHour.open = D.live.lastHour.open.map(() => null); D.live.lastHour.open[D.live.lastHour.open.length - 1] = LIVE.now; D.live.lastHour.game = D.live.lastHour.game.map(() => null); D.live.lastHour.game[D.live.lastHour.game.length - 1] = LIVE.inGame; }
    D.live.feed = [];
    D.live.noFeed = true;
    // D9: the first day's steps and when the app is open, from the server (before both stayed at 0)
    if (Array.isArray(R.funnel)) D.funnel = R.funnel;
    if (Array.isArray(R.when) && R.when.length === 7) D.when = R.when;
    D.whenReal = true;

    D.countries = R.countries.map((x) => ({ c: x.k, k: COUNTRY[x.k] || x.k, n: x.n }));
    if (Array.isArray(D.installAge)) for (const b of D.installAge) b.n = (R.age.find((a) => a.band === b.band) || { n: 0 }).n;

    // A count per day over the last 14 days of the chart (buttons, errors), from the server's perDay
    const last14 = D.days.slice(-14).map((x) => x.date);
    const trend = (perDay) => last14.map((dt) => (perDay && perDay[dt]) || 0);
    // A band's median only when there is something to take it from (data.js gives the first band for an empty list)
    const median = (rows) => (rows && rows.reduce((s, x) => s + (x.n || 0), 0) ? medianBand(rows) : null);

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
        out.buttons[b.id] = { n: r.n, days: r.days, trend: trend(r.perDay), prev: 0, kb: 0 };
      }
      const h = R.heat[s.id] || { c: [], d: [], r: [] };
      if (out.grid) for (const [c, r, n] of h.c) if (out.grid[r] && c < UI.COLS) out.grid[r][c] += n;
      const near = (x, y) => { const b = s.buttons.find((q2) => q2.r && x >= q2.r[0] - 4 && x <= q2.r[0] + q2.r[2] + 4 && y >= q2.r[1] - 4 && y <= q2.r[1] + q2.r[3] + 4); return b ? `${b.label} (desligado ou ao lado)` : 'Área sem botão'; };
      out.dead = h.d.map(([c, r, n]) => { const [x, y] = cellXY(c, r); return { c, r, n, days: n, x, y, label: near(x, y) }; });
      out.rage = h.r.map(([c, r, n]) => { const [x, y] = cellXY(c, r); return { c, r, n, days: n, x, y, label: near(x, y).replace(' (desligado ou ao lado)', '') }; });
      const sc = R.scroll && R.scroll[s.id];
      out.scroll = sc ? [{ k: 'Todas as janelas', n: sc.visits, reach: [100, ...sc.reach] }] : null;
    }
    D.paths = R.paths;

    // beta.9 part 2: sessions, retention, matches, crashes, updates, Phone setup, settings, shortcuts and the environment
    D.sessionLen = R.sessions.len; D.startKind = { windows: R.sessions.auto, hand: R.sessions.hand };
    D.visible = { min: T.minutes, visible: totalMin };
    D.activeDays = R.active;
    // beta.9 keeps the ID per install: exact cohorts (each install once per column); before that, the age bands' estimate
    const exact = (R.cohortsExact || []).some((c) => c.n > 0);
    D.cohorts = exact ? R.cohortsExact : R.cohorts;
    D.installs = R.installs;
    const I0 = R.installs || { active: 0, fresh: 0, back: 0, prev: 0 };
    Object.assign(D.cs2, R.cs2);
    D.settingsChanged = R.settings.map((x) => ({ name: x.name, value: 'mudado', n: x.n }));
    const bt = (n) => (R.buttons[n] || { n: 0 }).n;
    D.shortcuts = [{ action: 'Change IP and rejoin', keys: 'atalho', kb: bt('Hotkey_Rejoin'), mouse: bt('Rejoin_Click') },
      { action: 'New IP + next account', keys: 'atalho', kb: bt('Hotkey_Switch'), mouse: bt('Quick_Click') }];
    Object.assign(D.phone, { method: R.phone.method, duration: R.phone.duration, attempts: R.phone.attempts, quality: R.phone.quality, drops: R.phone.drops, data: R.phone.data });
    D.firstScreen = R.firstScreen;
    D.phone.toProtected = R.sessions.toProtected; D.phone.toProtectedMedian = median(R.sessions.toProtected) || '-';
    const done = (R.setup.find((x) => x.key === 'done') || { n: 0 }).n;
    D.phone.setup = [{ k: 'Abriram o Phone setup', n: R.setupOpened }, { k: 'Pronto', n: done }];
    D.phone.steps = R.setup.filter((x) => x.n && x.key !== 'done');
    D.env = Object.assign({ unfiltered: true }, R.env);

    // Lists that carry the mock's names (versions, places in the code) go, or get the real ones; dated series follow the real days
    const dates = D.days.map((x) => x.date);
    const T7 = Math.max(1, T.users);
    D.rel.crashPlaces = R.crashPlaces.map((c) => ({ at: c.at, ver: short(c.app), n: c.n, fresh: false }));
    Object.assign(D.rel, { crashes: T.crashes, sessions: T.sessions, crashFree: T.sessions ? 100 - pct(T.crashes, T.sessions) : null, startup: R.sessions.startup });
    Object.assign(D.rel.updates, R.updates);
    D.rel.errors = R.errors.map((e) => ({ type: e.type, at: e.at, ver: short(e.app), p: e.days / T7, days: e.days, fresh: false, up: false, n: e.n, trend: trend(e.perDay) }));
    if (R.checks) { D.rel.checksRed = R.checks.red.map((c) => ({ k: c.k, n: c.n })); D.rel.leak = R.checks.leak; }
    D.rel.crashSeries = D.days.map((x) => ({ date: x.date, crashes: x.crashes, sessions: x.sessions }));
    const vers = R.versions.map((v) => short(v.k));
    const adopt = new Map((R.adoption || []).map((x) => [x.date, x.by]));
    D.versions.adoption = dates.map((date) => {
      const by = adopt.get(date) || {}, t = Object.values(by).reduce((s, n) => s + n, 0);
      return { date, shares: Object.fromEntries(vers.map((v) => [v, 0]).concat(Object.entries(by).map(([k, n]) => [short(k), t ? n / t : 0]))) };
    });
    const met = {};
    for (const v of R.versions) {
      const m = (R.versionMetrics || {})[v.k];
      met[short(v.k)] = m ? { ...m, toProtected: median(m.toProtected) || '-', startup: median(m.startup) || '-' }
        : { days: v.n, sessions: v.n, crashes: 0, ipNew: [0, 0], lag: [0, 0], minMed: 0, errors: R.errors.filter((e) => e.app === v.k).reduce((s, e) => s + e.n, 0), toProtected: '-', startup: '-' };
    }
    D.versions.metrics = met;
    D.phone.results = { novo: T.ipNew, mesmo: T.ipSame, sem: T.ipNone };
    D.rel.updates.from = (R.updates.from || []).map((x) => ({ v: short(x.v), n: x.n }));
    if (!Object.keys(met).length) met['-'] = { days: 0, sessions: 0, crashes: 0, ipNew: [0, 0], lag: [0, 0], minMed: 0, errors: 0, toProtected: '-', startup: '-' };
    D.newest = R.versions.length ? short(R.versions.map((v) => v.k).sort(compareV).at(-1)) : null;
    D.github.releases = rel.map((r) => ({ tag: r.tag, date: r.date, exe: r.exe })); D.github.days = [];
    D.phone.series = D.days.map((x) => ({ date: x.date, novo: x.ipNew, mesmo: x.ipSame, sem: x.ipNone }));
    const kc = R.counters;
    D.cs2.kicks = [{ k: 'Kick (servidor cheio, AFK…)', n: kc.kick_seen || 0 }, { k: 'VPN visto pelo servidor', n: kc.vpn_seen || 0, tone: 'warn' }, { k: 'Ban marcado', n: kc.ban_seen || 0, tone: 'bad' }];

    D.alerts.list = R.automated ? [{ tone: 'warn', kind: 'auto', text: 'Análises automáticas deixadas de fora', n: R.automated,
      nText: `${R.automated} instalações-dia sem conta Steam, sem cliques e sem minutos (antivírus online como o VirusTotal)${R.automatedDc ? `, ${R.automatedDc} de datacenters` : ''}`, when: D.asOf }] : [];

    if (GH && GH.stars != null) {   // problems is a list: empty when all went well
      const g = D.github;
      Object.assign(g, { stars: GH.stars ?? 0, forks: GH.forks ?? 0, watchers: GH.watchers ?? 0, issues: GH.issues ?? 0,
        views: GH.views?.count ?? 0, uniques: GH.views?.uniques ?? 0, clones: GH.clones?.count ?? 0, cloneUniques: GH.clones?.uniques ?? 0,
        days: (GH.views?.days || []).map((x) => ({ date: x.day, n: x.n, u: x.u })),
        downloadsPeriod: rel.reduce((s, r) => s + (r.date >= addDaysR(R.asOf, -(F.period - 1)) ? r.exe : 0), 0),
        releases: rel.map((r) => ({ tag: r.tag, date: r.date, exe: r.exe })),
        referrers: (GH.referrers || []).map((r) => ({ k: r.name, n: r.n, u: r.u })), paths: (GH.paths || []).map((p) => ({ k: p.name, n: p.n, u: p.u })) });
    }
    D.github.problems = (GH && GH.problems) || [];

    // D8: counted and kept, but with no block of their own until now: one plain list, so nothing the app sends stays unseen
    const SHOWN = /^(ip_changed|ip_same|ip_no_network|app_start|crash|first_start|min_prot|cs2_start|kick_seen|vpn_seen|ban_seen|rej_try|rej_back|leak_ok|leak_bad|(m|ping|jit|jitd|late|loss|sess|st|tp|start|upd|updfrom|phone_setup|chg|chgt|chga|chgq|drop|data|scl|fs)_.*)$/;
    const NAMES = { unclean_exit: 'Saídas sem fechar (crash ou forçadas)', log_error: 'Erros escritos no Log', ip_home_back: 'O IP voltou a ser o de casa',
      rej_wait: 'Rejoin à espera de vaga', cable_hint: 'Aviso do cabo USB mostrado', min_cs2: 'Minutos com o CS2 aberto' };
    const mapped = new Set(['Hotkey_Rejoin', 'Rejoin_Click', 'Hotkey_Switch', 'Quick_Click']);
    for (const s of UI.screens) for (const b of s.buttons) { mapped.add(b.id + '_Click'); mapped.add(b.id); }
    const LISTN = { servers: 'Servidores', accounts: 'Contas', flags: 'Jogadores marcados', open_with: 'Open with CS2', open_with_admin: 'Open with CS2 como admin', regions_blocked: 'Regiões desligadas' };
    D.others = {
      counters: Object.entries(R.counters || {}).filter(([k]) => !SHOWN.test(k)).map(([k, n]) => ({ k: NAMES[k] || k, code: k, n })).sort((a, b) => b.n - a.n),
      clicks: Object.entries(R.buttons || {}).filter(([k]) => !mapped.has(k)).map(([k, b]) => ({ k, n: b.n, days: b.days })).sort((a, b) => b.n - a.n),
      sizes: (R.sizes || []).filter((z) => z.bands.length).map((z) => ({ k: LISTN[z.name] || z.name, bands: z.bands })),
      oses: R.oses || [], prevInstalls: I0.prev || 0,
    };

    // Dashboard D1: how fresh the data is, at the top. Red past 12 hours without a report.
    const ago = (t) => { if (!t) return null; const m = Math.max(0, Math.round((Date.now() - t) / 60000)); return m < 60 ? `${m} min` : m < 48 * 60 ? `${Math.round(m / 60)} h` : `${Math.round(m / 1440)} dias`; };
    const at = (t) => new Date(t).toISOString().slice(0, 16).replace('T', ' ');
    const lastRep = R.lastTs, lastLive = LIVE && LIVE.lastTs;
    D.realStale = !lastRep || Date.now() - lastRep > 12 * 3600_000;
    const fresh = lastRep ? `Último relatório há ${ago(lastRep)} (${at(lastRep)} UTC)` : 'Nenhum relatório nos últimos 90 dias';
    const freshLive = lastLive ? `último sinal ao vivo há ${ago(lastLive)}` : 'nenhum sinal ao vivo';
    const cut = (R.truncated || []).length ? ` Atenção: leitura cortada nos dias mais antigos (${R.truncated.join(', ')}).` : '';
    const warn = (R.warnings || []).length ? ` Sem dados de: ${R.warnings.map((w) => w.split(':')[0]).join(', ')}.` : '';
    D.realBar = `${DEV ? 'Os teus PCs (dev e os de EXCLUDE_IDS). ' : R.excluded ? `Sem os teus ${R.excluded} PCs (EXCLUDE_IDS). ` : ''}${fresh} · ${freshLive}. ${I0.active ? `${I0.active} ${I0.active === 1 ? 'instalação diferente' : 'instalações diferentes'} no período (beta.9+): ${I0.fresh} com ID novo, ${I0.back} já vistas antes. ` : ''}Partidas, sessões, crashes, ambiente, scroll e atualizações só a partir da beta.9.${cut}${warn}`;
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
