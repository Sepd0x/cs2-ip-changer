// Dados inventados para a maquete, gerados com semente (os mesmos filtros dão sempre os mesmos números).
// Em produção, makeData sai e entra: DATA = await (await fetch('/api/summary?' + new URLSearchParams(filtros))).json()
// A forma do objeto que makeData devolve é o contrato com a API. O app.js só lê DATA, nunca gera números.
//
// Regras do contrato (privacidade, 2 nov., "Depois da crítica" em dashboard-recolha):
// - a unidade é a instalação-dia (ID novo todos os dias); nada se conta como "pessoas" ao longo de vários dias
// - percentagens vêm com o n por baixo (num e den), para o dashboard mostrar "poucos dados" quando o n é pequeno
// - ao vivo: só contagens agregadas; nenhuma linha por instalação; o feed não leva país
// - env (ambiente): relatório à parte, sem ID, só contagens; não cruza com nada nem responde aos filtros
// - blocos marcados unfiltered: true não respondem aos filtros (o dashboard diz "não filtrado")
'use strict';

function mulberry(seed) {
  return function () {
    seed |= 0; seed = (seed + 0x6D2B79F5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
function hashStr(s) { let h = 2166136261; for (const c of s) { h ^= c.charCodeAt(0); h = Math.imul(h, 16777619); } return h >>> 0; }

const MOCK = {
  today: '2026-12-13', now: '21:42', dayFrac: (21 * 60 + 42) / 1440,
  appStart: '2026-10-27',
  releases: [
    { tag: 'beta.5', date: '2026-10-29' }, { tag: 'beta.6', date: '2026-10-31' }, { tag: 'beta.8', date: '2026-11-01' },
    { tag: 'beta.9', date: '2026-11-10' }, { tag: 'beta.10', date: '2026-11-24' }, { tag: 'beta.11', date: '2026-12-08' },
  ],
  options: {
    version: [['all', 'Todas'], ['beta.11', 'beta.11'], ['beta.10', 'beta.10'], ['beta.9', 'beta.9'], ['older', 'Anteriores']],
    country: [['all', 'Todos'], ['PT', 'Portugal'], ['BR', 'Brasil'], ['DE', 'Alemanha'], ['ES', 'Espanha'], ['PL', 'Polónia'], ['other', 'Outros']],
    mode: [['all', 'Todos'], ['game', 'Game only'], ['cs2open', 'Whole PC while CS2 is open'], ['always', 'Whole PC, always'], ['nophone', 'No phone']],
    link: [['all', 'Todas'], ['usb', 'USB'], ['hotspot', 'Hotspot'], ['other', 'Outro adaptador']],
  },
  share: {
    version: { all: 1, 'beta.11': 0.62, 'beta.10': 0.24, 'beta.9': 0.09, older: 0.05 },
    country: { all: 1, PT: 0.48, BR: 0.17, DE: 0.09, ES: 0.07, PL: 0.06, other: 0.13 },
    mode: { all: 1, game: 0.38, cs2open: 0.44, always: 0.12, nophone: 0.06 },
    link: { all: 1, usb: 0.58, hotspot: 0.36, other: 0.06 },
  },
};

const DAY = 864e5;
const iso = (t) => new Date(t).toISOString().slice(0, 10);
const addDays = (d, n) => iso(Date.parse(d + 'T00:00:00Z') + n * DAY);
const diffDays = (a, b) => Math.round((Date.parse(a + 'T00:00:00Z') - Date.parse(b + 'T00:00:00Z')) / DAY);
const COUNT_KEYS = ['users', 'fresh', 'freshPrev', 'back1', 'minutes', 'sessions', 'crashes', 'ipNew', 'ipSame', 'ipNone', 'matches', 'laggy', 'errors'];

/** Histórico de 180 dias com semente fixa: os filtros só escalam, a história não muda ao trocar de período. */
function baseHistory() {
  const r = mulberry(20261213);
  const out = [];
  for (let i = 179; i >= 0; i--) {
    const date = addDays(MOCK.today, -i);
    const k = diffDays(date, MOCK.appStart);
    const o = { date, sessMed: null };
    for (const c of COUNT_KEYS) o[c] = 0;
    if (k >= 0) {
      const dow = new Date(date + 'T00:00:00Z').getUTCDay();
      const weekend = dow === 0 || dow === 6 ? 1.25 : dow === 5 ? 1.12 : 1;
      o.users = Math.max(1, Math.round((1.6 + k * 0.2) * weekend * (0.78 + r() * 0.44)));
      o.fresh = Math.min(o.users, k < 3 ? 2 + Math.round(r() * 2) : Math.round(r() * 1.4 + (k > 30 ? 0.5 : 0.2) + (r() > 0.85 ? 2 : 0)));
      o.sessions = Math.round(o.users * (1.25 + r() * 0.35));
      o.minutes = Math.round(o.users * (40 + r() * 22));
      o.sessMed = Math.round(28 + r() * 22);
      const ipAll = Math.round(o.users * (8.5 + r() * 3.5));
      o.ipNone = Math.round(ipAll * (0.03 + r() * 0.03));
      o.ipSame = Math.round(ipAll * (0.06 + r() * 0.05));
      o.ipNew = ipAll - o.ipNone - o.ipSame;
      const after11 = date >= '2026-12-08';
      o.crashes = r() < (after11 ? 0.42 : 0.12) ? 1 + (r() > 0.75 ? 1 : 0) : 0;
      o.matches = Math.round(o.users * (2.1 + r() * 1.3));
      o.laggy = Math.round(o.matches * (0.27 + r() * 0.1));
      o.errors = Math.round(o.users * (0.2 + r() * 0.3) + (after11 ? 2 : 0));
      const prev = out[out.length - 1];
      o.freshPrev = prev ? prev.fresh : 0;
      o.back1 = Math.round(o.freshPrev * (0.33 + r() * 0.28));
    }
    out.push(o);
  }
  return out;
}

const med = (a) => { const v = a.filter((x) => x != null).sort((x, y) => x - y); if (!v.length) return null; const m = v.length >> 1; return v.length % 2 ? v[m] : (v[m - 1] + v[m]) / 2; };
/** Banda mediana de uma distribuição em bandas: a banda onde a soma acumulada passa metade. */
function medianBand(rows, key = 'n') { const t = rows.reduce((s, x) => s + x[key], 0); let c = 0; for (const x of rows) { c += x[key]; if (c >= t / 2) return x.band; } return null; }

function makeData(filters) {
  const F = Object.assign({ period: 7, version: 'all', country: 'all', mode: 'all', link: 'all' }, filters || {});
  const r = mulberry(hashStr(JSON.stringify(F)));
  const f = MOCK.share.version[F.version] * MOCK.share.country[F.country] * MOCK.share.mode[F.mode] * MOCK.share.link[F.link];
  const P = F.period;
  const chartN = P === 1 ? 14 : P;
  const hist = baseHistory();
  // escala determinística por campo (o mesmo dia dá sempre o mesmo número com os mesmos filtros)
  const sc = (v, salt) => { if (f === 1) return v; const x = v * f; const fr = x % 1; return Math.floor(x) + (mulberry(hashStr(salt))() < fr ? 1 : 0); };
  const scaleDay = (d, mult = 1) => {
    const o = { date: d.date, sessMed: d.sessMed };
    for (const k of COUNT_KEYS) o[k] = sc(d[k] * mult, `${d.date}${k}${f}`);
    if (o.fresh > o.users) o.fresh = o.users;
    if (o.back1 > o.freshPrev) o.back1 = o.freshPrev;
    if (o.crashes > o.sessions) o.crashes = o.sessions;
    if (o.laggy > o.matches) o.laggy = o.matches;
    if (!o.users) { o.sessions = 0; o.minutes = 0; o.sessMed = null; o.crashes = 0; o.matches = 0; o.laggy = 0; o.ipNew = 0; o.ipSame = 0; o.ipNone = 0; }
    o.back = Math.max(0, o.users - o.fresh);
    return o;
  };
  const days = hist.slice(-chartN).map((d) => scaleDay(d));
  const win = hist.slice(-P).map((d) => scaleDay(d));
  // "Hoje" compara com ontem até à mesma hora; os outros períodos com o período igual antes
  const prev = P === 1 ? [scaleDay(hist.at(-2), MOCK.dayFrac)] : hist.slice(-2 * P, -P).map((d) => scaleDay(d));
  const sum = (a, k) => a.reduce((s, d) => s + d[k], 0);
  const tot = (a) => { const o = {}; for (const k of [...COUNT_KEYS, 'back']) o[k] = sum(a, k); o.daysWithData = a.filter((d) => d.users > 0).length; return o; };
  const T = tot(win), PT = tot(prev);
  const UD = T.users; // instalações-dia na janela
  const sf = UD / 77; // 77 = instalações-dia de 7 dias sem filtros: escala os cliques mortos, de raiva, etc.
  const jitter = (v, a = 0.15) => v * (1 - a + r() * 2 * a);
  const pct = (a, b) => (b ? (100 * a) / b : null);

  // ---------- KPIs: valor, n e o período antes (null quando não há dados antes)
  const kv = (t, w) => ({
    usersPerDay: { v: t.daysWithData ? t.users / t.daysWithData : null, n: t.daysWithData },
    fresh: { v: t.fresh, n: t.fresh },
    nextDay: { v: pct(t.back1, t.freshPrev), n: t.freshPrev },
    minPerSession: { v: med(w.map((d) => (d.sessions ? d.sessMed : null))), n: t.sessions },
    ipNew: { v: pct(t.ipNew, t.ipNew + t.ipSame + t.ipNone), n: t.ipNew + t.ipSame + t.ipNone },
    lag: { v: pct(t.laggy, t.matches), n: t.matches },
    crashFree: { v: t.sessions ? 100 - pct(t.crashes, t.sessions) : null, n: t.sessions },
  });
  const K1 = kv(T, win), K0 = PT.users ? kv(PT, prev) : null;
  const daily = (k) => days.slice(-14).map((d) => kv(tot([d]), [d])[k].v);
  const kdef = [
    ['usersPerDay', 'Instalações abertas por dia', 1, '', 'up', 'uso', 'dias com dados', 1],
    ['fresh', 'Instalações novas', 0, '', 'up', 'uso', 'novas', 0],
    ['nextDay', 'Voltam no dia seguinte', 0, '%', 'up', 'uso', 'novas na véspera', 5],
    ['minPerSession', 'Minutos por sessão', 0, '', 'up', 'uso', 'sessões · mediana', 5],
    ['ipNew', 'Trocas com IP novo', 0, '%', 'up', 'telemovel', 'trocas', 5],
    ['lag', 'Partidas com lag', 0, '%', 'down', 'cs2', 'partidas', 5],
    ['crashFree', 'Sessões sem crash', 1, '%', 'up', 'fiab', 'sessões', 5],
  ];
  const kpis = kdef.map(([id, label, dec, unit, better, page, nUnit, min]) => ({
    id, label, dec, unit, better, page, nUnit, min,
    value: K1[id].n >= min ? K1[id].v : null, n: K1[id].n,
    prev: K0 && K0[id].n >= min ? K0[id].v : null, prevN: K0 ? K0[id].n : 0,
    series: daily(id),
  }));

  // ---------- ao vivo: só contagens (o dashboard mostra "menos de 5" abaixo de 5)
  const liveN = Math.max(0, Math.round((f === 1 ? 8 : 8 * f + r() * 1.2)));
  const liveSt = mockLiveState(r, liveN, F);
  const lastOpen = [], lastGame = [];
  let o = liveN, g = liveSt.inGame;
  for (let i = 59; i >= 0; i--) {
    lastOpen[i] = o; lastGame[i] = g;
    o = Math.max(0, Math.min(11, o + (r() < 0.07 ? (r() > 0.55 ? 1 : -1) : 0)));
    g = Math.min(o, Math.max(0, g + (r() < 0.07 ? (r() > 0.5 ? 1 : -1) : 0)));
  }
  const feed = [];
  let t = Date.parse(MOCK.today + 'T21:40:00Z');
  for (let i = 0; i < 14; i++) { feed.push(mockLiveEvent(r, t, F)); t -= (r() > 0.5 ? 300 : 0) * 1000 + (r() > 0.8 ? 300000 : 0); }

  // ---------- quando usam (dia da semana × hora, Lisboa), em sessões
  const when = [...Array(7)].map((_, d) => [...Array(24)].map((_, h) => {
    const base = h < 2 ? 0.35 : h < 8 ? 0.02 : h < 14 ? 0.12 : h < 19 ? 0.35 : 0.9;
    return Math.max(0, Math.round((base * (d > 4 ? 1.3 : 1) - r() * 0.3) * 6 * (P / 7) * f));
  }));

  // ---------- funil do 1.º dia: cada passo é um subconjunto do anterior, no mesmo dia
  const ready = Math.floor(T.fresh * 0.72 + 0.4), firstIp = Math.floor(ready * 0.88 + 0.4);
  const funnel = [
    { label: '1.º arranque', n: T.fresh },
    { label: 'Telemóvel pronto', n: ready },
    { label: '1.ª troca de IP', n: firstIp },
  ];

  // ---------- coortes: instalações novas por semana e quantas abriram a app N dias depois (contagens)
  const cohorts = [];
  for (let w = 0; w < 7; w++) {
    const start = addDays(MOCK.appStart, w * 7);
    const wk = hist.filter((d) => d.date >= start && d.date < addDays(start, 7));
    const n = wk.reduce((s, d) => s + sc(d.fresh, `${d.date}coh${f}`), 0);
    const at = [0, 1, 3, 7, 14, 30], p = [1, 0.44 + r() * 0.12, 0.33 + r() * 0.08, 0.26 + r() * 0.07, 0.2 + r() * 0.06, 0.14 + r() * 0.05];
    let last = n;
    cohorts.push({ week: start, n, back: at.map((a, i) => { if (i && addDays(start, a + 6) > MOCK.today) return null; const c = Math.min(last, Math.round(n * p[i])); last = c; return c; }) });
  }
  const activeDays = [1, 2, 3, 4, 5, 6, 7].map((d) => ({ band: String(d), n: Math.max(0, Math.round([9, 7, 6, 5, 4, 4, 6][d - 1] * sf * jitter(1))) }));
  const installAge = [['0', 0.11], ['1', 0.06], ['2-3', 0.1], ['4-6', 0.12], ['7-13', 0.2], ['14-29', 0.27], ['30+', 0.14]].map(([b, p]) => ({ band: b, n: Math.round(UD * p * jitter(1, 0.1)) }));
  const sessionLen = [['<5 min', 14], ['5-15', 18], ['15-30', 21], ['30-60', 26], ['1-2 h', 15], ['>2 h', 6]].map(([b, p]) => ({ band: b, n: Math.round((T.sessions * p) / 100) }));
  const startKind = { windows: Math.round(T.sessions * 0.62), hand: 0 }; startKind.hand = T.sessions - startKind.windows;
  const visible = { min: T.minutes, visible: Math.round(T.minutes * 0.34) };
  const countries = [['PT', 'Portugal', 0.48], ['BR', 'Brasil', 0.17], ['DE', 'Alemanha', 0.09], ['ES', 'Espanha', 0.07], ['PL', 'Polónia', 0.06], ['other', 'Outros', 0.13]]
    .filter(([c]) => F.country === 'all' || F.country === c).map(([c, k, p]) => ({ c, k, n: Math.round(UD * (F.country === 'all' ? p : 1)) }));

  // ---------- a app com calor
  const RATES = {
    connection: { visits: 3.1, share: 57, users: 100, scroll: [38, 12, 4], b: { Quick: [0.8, 33], ChangeIp: [3.1, 86], Rejoin: [1.2, 41], LeakTest: [0.22, 22], Restore: [0.3, 28], CopyEgressIp: [0.34, 24], Reveal: [0.12, 15], Tether: [0.42, 30], PhoneHelp: [0.15, 18], LinkPick: [0.08, 9], ModeGame: [0.05, 6], ModeCs2: [0.1, 12], ModeAlways: [0.04, 5], ModeNoPhone: [0.025, 3], ClearHistory: [0.012, 2] },
      dead: [[0, 34], [1, 21], [2, 12], [3, 6], [4, 8]], rage: [['ChangeIp', 14], ['Tether', 6]] },
    servers: { visits: 1.9, share: 19, users: 72, scroll: [71, 44, 19], b: { Quick: [0.25, 14], PasteBox: [0.3, 28], Port: [0.03, 4], AddServers: [0.28, 27], PasteServers: [0.24, 21], ImportServers: [0.02, 2], Search: [0.18, 16], AutoQuery: [0.05, 9], QueryServers: [1.3, 58], CopyServers: [0.1, 8], RemoveServers: [0.06, 7], SrvFav: [0.15, 18], SrvPlayers: [0.5, 37], SrvConnect: [1.9, 64], PlayerFlag: [0.2, 19] },
      dead: [[0, 15], [1, 5]], deadBtn: [['RemoveServers', 27]], rage: [['QueryServers', 9], ['SrvConnect', 5]] },
    regions: { visits: 0.6, share: 6, users: 41, scroll: [62, 30, 9], b: { Quick: [0.05, 4], RegionsRefresh: [0.3, 24], RegionsClosest: [0.24, 22], RegionsContinent: [0.18, 17], RegionsAllowAll: [0.12, 13], PingSlider: [0.3, 19], RegionsUnder: [0.2, 15], RegionToggle: [0.6, 30], RegionOnly: [0.05, 5] },
      dead: [[0, 9], [1, 14]], rage: [['RegionsRefresh', 3]] },
    accounts: { visits: 0.7, share: 8, users: 38, scroll: [22, 5, 1], b: { Quick: [0.1, 7], AccSearch: [0.08, 7], Filters: [0.2, 15], Sort: [0.05, 5], AccOpt: [0.04, 6], SteamMin: [0.03, 5], Offline: [0.02, 3], HideNames: [0.03, 4], AccAdd: [0.06, 9], AccRefresh: [0.1, 9], SwitchSteps: [0.02, 3], NewIpNext2: [0.18, 11], AccSwitch: [1.2, 34], AccFav: [0.08, 8], AccToggleQuick: [0.1, 9] },
      dead: [[0, 19]], rage: [['AccSwitch', 7]] },
    configs: { visits: 0.35, share: 4, users: 21, scroll: [57, 26, 8], b: { Quick: [0.03, 2], CopyFrom: [0.1, 12], CopyTo: [0.12, 12], WhatToCopy: [0.08, 9], CfgToAll: [0.03, 4], CfgToNone: [0.02, 2], CfgCopy: [0.1, 11], SyncMode: [0.05, 8], MainPick: [0.03, 5], CfgSyncNow: [0.12, 10], CfgUndo: [0.02, 2], Restore: [0.03, 3], CfgSnapSave: [0.04, 4], CfgSnapImport: [0.01, 1], SnapIcons: [0.02, 2], Compare: [0.06, 6], AccSync: [0.03, 4] },
      dead: [[0, 6], [1, 2]], rage: [] },
    profiles: { visits: 0.15, share: 2.5, users: 9, scroll: [40, 11, 3], b: { Quick: [0.01, 1], Template: [0.04, 5], TplIcons: [0.01, 1], TplPickAvatar: [0.03, 4], TplClearAvatar: [0.01, 1], SteamName: [0.05, 6], Bio: [0.02, 3], Privacy: [0.03, 3], ChangeWhat: [0.05, 5], LookLike: [0.02, 2], ProfIdentical: [0.06, 6], ProfSignIn: [0.1, 7], ProfForgetAll: [0.01, 1], ProfRestoreSelected: [0.02, 2], ClearNames: [0.004, 0], ProfApply: [0.07, 6] },
      dead: [[0, 11], [1, 4]], rage: [['ProfApply', 4]] },
    log: { visits: 0.2, share: 1.5, users: 14, b: { LogCopy: [0.05, 6], LogFilter: [0.03, 3], LogFolder: [0.02, 2], LogClear: [0.01, 1], LogScroll: [0.01, 1] } },
    settings: { visits: 0.3, share: 1, users: 26, b: { StartWin: [0.06, 14], Hotkeys: [0.05, 9], CheckUpdate: [0.04, 8], Tray: [0.03, 7], UsageStats: [0.01, 2], ResetAll: [0.002, 0] } },
  };
  const cellOf = (x, y) => [Math.min(UI.COLS - 1, Math.max(0, Math.floor(((x - UI.X0) / (UI.W - UI.X0)) * UI.COLS))), Math.min(UI.ROWS - 1, Math.max(0, Math.floor((y / UI.H) * UI.ROWS)))];
  const screens = {};
  const WINS = [['Pequena', 0.31, 18], ['Média', 0.48, 0], ['Grande', 0.21, -16]];
  for (const s of UI.screens) {
    const R = RATES[s.id];
    const grid = [...Array(UI.ROWS)].map(() => Array(UI.COLS).fill(0));
    const hit = (x, y, n = 1) => { const [c, rr] = cellOf(x, y); grid[rr][c] += n; };
    const scatter = (rect, n, pad = 4) => { for (let i = 0; i < n; i++) hit(rect[0] - pad + r() * (rect[2] + 2 * pad), rect[1] - pad + r() * (rect[3] + 2 * pad)); };
    const buttons = {};
    for (const b of s.buttons) {
      const [rate, users] = R.b[b.id] || [0, 0];
      const n = Math.round(rate * UD * jitter(1, 0.12));
      const usedDays = Math.min(n, UD, Math.round((users / 100) * UD * jitter(1, 0.08)));
      const tr = [...Array(14)].map((_, i) => Math.max(0, rate * (UD / Math.max(1, T.daysWithData)) * (0.7 + i * 0.03) * (0.6 + r() * 0.8)));
      buttons[b.id] = { n, days: usedDays, trend: tr, prev: Math.round(n * (0.75 + r() * 0.5)), kb: b.kb ? Math.round(n * 0.58) : 0 };
      if (s.img && n) {
        const rects = [b.r, ...(b.more || [])];
        const w = rects.map((_, i) => 1 / (1 + i * 0.45)); const ws = w.reduce((a, c) => a + c, 0);
        rects.forEach((rc, i) => scatter(rc, Math.round((n * w[i]) / ws)));
      }
    }
    const dead = [], rage = [];
    if (s.img) {
      const mk = (rect, nn, label, list, pt) => { const n = Math.round(nn * sf * jitter(1, 0.15)); if (!n) return; const [c, rr] = cellOf(pt[0], pt[1]); if (list === dead) scatter(rect, n, 2); list.push({ c, r: rr, n, days: Math.min(n, UD, Math.max(1, Math.round(n * 0.55))), x: pt[0], y: pt[1], label }); };
      for (const [ti, nn] of R.dead || []) { const tx = s.text?.[ti]; if (!tx) continue; mk(tx.r, nn, tx.label, dead, [tx.r[0] + tx.r[2] / 2, tx.r[1] + tx.r[3] / 2]); }
      for (const [bid, nn] of R.deadBtn || []) { const b = s.buttons.find((q) => q.id === bid); if (!b?.r) continue; mk(b.r, nn, `${b.label} (desligado)`, dead, [b.r[0] + b.r[2] / 2, b.r[1] + b.r[3] / 2]); }
      for (const [bid, nn] of R.rage || []) { const b = s.buttons.find((q) => q.id === bid); if (!b?.r) continue; mk(b.r, nn, b.label, rage, [b.r[0] + 8, b.r[1] + b.r[3]]); }
      const noise = Math.round(UD * 0.12);
      for (let i = 0; i < noise; i++) hit(UI.X0 + r() * (UI.W - UI.X0), 40 + r() * (UI.H - 40));
    }
    const visits = Math.round(R.visits * UD);
    // scroll em bandas de 25%, por tamanho de janela (a app manda a banda da janela com o scroll)
    const scroll = R.scroll ? WINS.map(([k, sh, d]) => ({ k, n: Math.round(visits * sh), reach: [100, ...R.scroll.map((v) => Math.max(0, Math.min(100, Math.round(v + d * (v / 60)))))] })) : null;
    screens[s.id] = { minutes: Math.round((T.minutes * R.share) / 100), share: R.share, visits, days: Math.min(UD, Math.round((R.users / 100) * UD)),
      buttons, grid: s.img ? grid : null, dead, rage, scroll };
  }
  const pathsBase = [['connection', 'servers', 140], ['servers', 'connection', 118], ['connection', 'regions', 46], ['regions', 'connection', 30], ['regions', 'servers', 14],
    ['connection', 'accounts', 38], ['accounts', 'connection', 26], ['accounts', 'configs', 12], ['configs', 'accounts', 8], ['configs', 'connection', 9],
    ['connection', 'settings', 22], ['settings', 'connection', 19], ['connection', 'log', 13], ['log', 'connection', 11], ['servers', 'accounts', 17],
    ['accounts', 'servers', 13], ['connection', 'profiles', 7], ['profiles', 'accounts', 4], ['profiles', 'connection', 3], ['servers', 'regions', 10]];
  const paths = pathsBase.map(([a, b, n]) => ({ from: a, to: b, n: Math.max(0, Math.round(n * sf * jitter(1, 0.12))) })).filter((p) => p.n > 0);
  const firstScreen = [['servers', 0.46], ['connection', 0.28], ['accounts', 0.11], ['regions', 0.08], ['settings', 0.04], ['configs', 0.03]].map(([s, p]) => ({ screen: s, n: Math.round(T.sessions * p) }));
  const shortcuts = [
    { action: 'Change IP and rejoin', keys: 'Ctrl+Shift+F10', kb: Math.round(screens.connection.buttons.Rejoin.n * 1.38), mouse: screens.connection.buttons.Rejoin.n },
    { action: 'Abrir a app', keys: 'bandeja contra atalho', kb: Math.round(T.sessions * 0.37), mouse: Math.round(T.sessions * 0.63) },
  ];
  const settingsChanged = [['Start with Windows', 'ligado', 0.62], ['Mode', 'Whole PC while CS2 is open', 0.44], ['Refresh every 30 s', 'desligado', 0.35], ['Sync', 'From a main account', 0.31],
    ['Start Steam minimized', 'ligado', 0.27], ['Hide usernames', 'ligado', 0.18], ['Offline mode', 'ligado', 0.06]].map(([name, value, p]) => ({ name, value, n: Math.round(UD * p) }));

  // ---------- telemóvel
  const ipAll = T.ipNew + T.ipSame + T.ipNone;
  const lk = F.link;
  const linkSplit = (u, h) => ({ usb: lk === 'hotspot' || lk === 'other' ? 0 : u, hotspot: lk === 'usb' || lk === 'other' ? 0 : h });
  const phone = {
    results: { novo: T.ipNew, mesmo: T.ipSame, sem: T.ipNone },
    series: days.map((d) => ({ date: d.date, novo: d.ipNew, mesmo: d.ipSame, sem: d.ipNone })),
    method: [{ k: 'Modo avião', n: Math.round(ipAll * 0.81) }, { k: 'Desligar e ligar os dados', n: ipAll - Math.round(ipAll * 0.81) }],
    duration: [['<5 s', 0.13, 0.08], ['5-10 s', 0.4, 0.36], ['10-20 s', 0.29, 0.31], ['20-40 s', 0.13, 0.18], ['>40 s', 0.05, 0.07]].map(([b, u, h]) => ({ band: b, ...linkSplit(Math.round(ipAll * 0.62 * u), Math.round(ipAll * 0.38 * h)) })),
    attempts: [['1', 0.87], ['2', 0.09], ['3', 0.03], ['4+', 0.01]].map(([b, p]) => ({ band: b, n: Math.round(ipAll * p) })),
    quality: { boa: Math.round(ipAll * 0.67), razoavel: Math.round(ipAll * 0.22), ma: Math.round(ipAll * 0.11), retry: Math.round(ipAll * 0.13) },
    setup: [],
    drops: [['0', 0.71], ['1', 0.19], ['2', 0.06], ['3+', 0.04]].map(([b, p]) => ({ band: b, n: Math.round(T.sessions * p) })),
    data: [['<250 MB', 0.31], ['250-500', 0.27], ['0,5-1 GB', 0.22], ['1-2 GB', 0.14], ['>2 GB', 0.06]].map(([b, p]) => ({ band: b, n: Math.round(UD * p) })),
    toProtected: [['<10 s', 0.22], ['10-30 s', 0.41], ['30-60 s', 0.21], ['1-3 min', 0.12], ['>3 min', 0.04]].map(([b, p]) => ({ band: b, n: Math.round(T.sessions * p) })),
  };
  { let last = Math.round(41 * sf); phone.setup = [['Abriram o Phone setup', 1], ['Cabo ligado', 0.9], ['Tethering desligado (tether off)', 0.6], ['Tethering ligado', 0.91], ['Rede vista no PC', 0.95], ['Pronto', 0.95]].map(([k, p]) => { last = Math.floor(last * p); return { k, n: last }; }); }
  phone.toProtectedMedian = medianBand(phone.toProtected);

  // ---------- CS2 (lag por ligação e servidor com o n de partidas de cada célula)
  const M = T.matches;
  const mU = lk === 'hotspot' || lk === 'other' ? 0 : Math.round(M * (lk === 'usb' ? 1 : 0.62)), mH = lk === 'usb' || lk === 'other' ? 0 : M - mU - (lk === 'all' ? Math.round(M * 0.04) : 0);
  const cell = (n, p) => ({ n, lag: Math.round(n * p) });
  const cs2 = {
    sessions: Math.round(T.sessions * 0.78), hoursProtected: Math.round((T.minutes * 0.71) / 60), matches: M, laggy: T.laggy,
    valve: Math.round(M * 0.38), community: M - Math.round(M * 0.38),
    lag: { usb: { valve: cell(Math.round(mU * 0.38), 0.18), community: cell(mU - Math.round(mU * 0.38), 0.24) }, hotspot: { valve: cell(Math.round(mH * 0.38), 0.41), community: cell(mH - Math.round(mH * 0.38), 0.52) } },
    ping: [['<30', 6, 1], ['30-50', 18, 6], ['50-80', 22, 11], ['80-120', 9, 10], ['>120', 3, 6]].map(([b, u, h]) => ({ band: b, ...linkSplit(Math.round((u * mU) / 58), Math.round((h * mH) / 34)) })),
    jitter: [['<5', 9, 2], ['5-10', 20, 6], ['10-20', 17, 10], ['20-40', 7, 9], ['>40', 3, 7]].map(([b, u, h]) => ({ band: b, ...linkSplit(Math.round((u * mU) / 56), Math.round((h * mH) / 34)) })),
    jitterDown: [['<5', 14, 5], ['5-10', 22, 9], ['10-20', 13, 10], ['20-40', 5, 6], ['>40', 2, 4]].map(([b, u, h]) => ({ band: b, ...linkSplit(Math.round((u * mU) / 56), Math.round((h * mH) / 34)) })),
    late: [['0%', 0.41], ['<1%', 0.27], ['1-3%', 0.18], ['3-10%', 0.1], ['>10%', 0.04]].map(([b, p]) => ({ band: b, n: Math.round(M * p) })),
    loss: [['0%', 0.58], ['<0,5%', 0.22], ['0,5-2%', 0.13], ['>2%', 0.07]].map(([b, p]) => ({ band: b, n: Math.round(M * p) })),
    rejoin: { tries: Math.round(64 * sf), back: 0 },
    regions: [['gru', 'São Paulo', 82], ['iad', 'Sterling', 79], ['ord', 'Chicago', 77], ['atl', 'Atlanta', 76], ['lax', 'Los Angeles', 74], ['dfw', 'Dallas', 74], ['sea', 'Seattle', 71], ['hkg', 'Hong Kong', 69], ['sgp', 'Singapura', 68], ['tyo', 'Tóquio', 66], ['dxb', 'Dubai', 52], ['mad', 'Madrid', 9]].map(([c, n, p]) => ({ code: c, name: n, n: Math.round((UD * p) / 100) })),
    regionsDen: UD,
    kicks: [{ k: 'Kick (servidor cheio, AFK…)', n: Math.round(41 * sf) }, { k: 'VPN visto pelo servidor', n: Math.round(12 * sf), tone: 'warn' }, { k: 'Ban marcado', n: Math.round(3 * sf), tone: 'bad' }],
  };
  cs2.rejoin.back = Math.round(cs2.rejoin.tries * 0.8);

  // ---------- fiabilidade (erros e crashes filtrados pela versão escolhida)
  const verOk = (v) => F.version === 'all' || F.version === v || (F.version === 'older' && !['beta.9', 'beta.10', 'beta.11'].includes(v));
  const crashPlaces = [{ at: 'MainWindow.QueryServers', p: 0.6, ver: 'beta.11', fresh: true }, { at: 'PhoneLink.OnDisconnect', p: 0.25, ver: 'beta.10' }, { at: 'Store.Save', p: 0.15, ver: 'beta.9' }]
    .filter((x) => verOk(x.ver)).map((x) => ({ ...x, n: Math.round(T.crashes * x.p) })).filter((x) => x.n);
  const rel = {
    crashFree: K1.crashFree.v, crashes: T.crashes, sessions: T.sessions,
    crashSeries: hist.slice(-(chartN + 6)).map((d) => scaleDay(d)).map((d) => ({ date: d.date, crashes: d.crashes, sessions: d.sessions })),
    crashPlaces,
    errors: [
      { type: 'NullReferenceException', at: 'MainWindow.QueryServers', ver: 'beta.11', p: 0.34, days: 6, fresh: true, up: true },
      { type: 'IOException', at: 'Store.Save', ver: 'beta.10', p: 0.18, days: 4 },
      { type: 'TaskCanceledException', at: 'Updater.Check', ver: 'beta.11', p: 0.15, days: 5 },
      { type: 'SocketException', at: 'Quality.Measure', ver: 'beta.11', p: 0.12, days: 3, up: true },
      { type: 'Win32Exception', at: 'Firewall.Apply', ver: 'beta.10', p: 0.08, days: 2 },
      { type: 'TimeoutException', at: 'PhoneLink.Detect', ver: 'beta.9', p: 0.05, days: 1 },
    ].filter((e) => verOk(e.ver)).map((e) => { const n = Math.round(T.errors * e.p); return { ...e, n, days: Math.min(n, Math.max(1, Math.round(e.days * sf))), trend: [...Array(14)].map((_, i) => Math.max(0, (e.up ? 0.4 + i / 10 : 1.2 - i / 30) * (0.5 + r()))) }; }).filter((e) => e.n > 0),
    checksRed: [['Block if the phone drops', 14], ['Windows Firewall', 9], ['Mobile data', 6], ['Servers through the phone', 5], ['Phone IP is not your home IP', 3], ['CS2 only through the phone', 2]].map(([k, n]) => ({ k, n: Math.round(n * sf) })),
    leak: { pass: Math.round(96 * sf), fail: Math.round(4 * sf) },
    updates: { seen: Math.round(34 * sf), accepted: Math.round(27 * sf), done: Math.round(25 * sf), declined: Math.round(5 * sf), failed: Math.round(2 * sf), from: [['beta.10', 19], ['beta.9', 9], ['beta.8', 4], ['beta.6', 2]].map(([v, n]) => ({ v, n: Math.round(n * sf) })) },
    startup: [['<1 s', 0.18], ['1-2 s', 0.47], ['2-4 s', 0.24], ['4-8 s', 0.08], ['>8 s', 0.03]].map(([b, p]) => ({ band: b, n: Math.round(T.sessions * p) })),
  };
  // Alertas: calculados no servidor sobre tudo, não respondem aos filtros
  // contas sem filtros, nos últimos 7 dias
  const base7 = hist.slice(-7), sf7 = base7.reduce((s, d) => s + d.users, 0) / 77;
  const leak7 = Math.round(4 * sf7), crash7 = Math.round(base7.filter((d) => d.date >= '2026-12-08').reduce((s, d) => s + d.crashes, 0) * 0.6);
  const alerts = {
    unfiltered: true,
    list: [
      ...(leak7 ? [{ tone: 'bad', kind: 'leak', text: 'Teste de fugas falhou', n: leak7, nText: `${leak7} ${leak7 === 1 ? 'falha' : 'falhas'} em 7 dias, todas com hotspot`, when: '12 dez.' }] : []),
      ...(crash7 ? [{ tone: 'bad', kind: 'crash', text: 'Crash novo na beta.11 em MainWindow.QueryServers', n: crash7, nText: `${crash7} ${crash7 === 1 ? 'crash' : 'crashes'} em 7 dias desde a beta.11`, when: '9 dez.' }] : []),
      { tone: 'warn', kind: 'errors', text: 'Erros em Quality.Measure 3× acima da média dos 14 dias', n: 11, nText: '11 erros', when: '12 dez.' },
    ],
  };

  // ---------- versões (não filtrado: é o próprio corte por versão)
  const vlist = MOCK.releases;
  const adoption = [];
  const mass = {};
  for (const d of hist) {
    const released = vlist.filter((v) => v.date <= d.date);
    if (!released.length) { adoption.push({ date: d.date, shares: {} }); continue; }
    const newest = released.at(-1).tag;
    if (!Object.keys(mass).length) mass[released[0].tag] = 1;
    for (const v of released) if (v.tag !== newest && mass[v.tag]) { const m = mass[v.tag] * (v.tag === released.at(-2)?.tag ? 0.3 : 0.22); mass[v.tag] -= m; mass[newest] = (mass[newest] || 0) + m; }
    if (!mass[newest]) mass[newest] = 0;
    adoption.push({ date: d.date, shares: { ...mass } });
  }
  const versions = {
    unfiltered: true,
    adoption: adoption.slice(-Math.max(chartN, 14)),
    metrics: {
      'beta.11': { days: 31, sessions: 44, crashes: 1, ipNew: [356, 400], lag: [31, 107], minMed: 46, errors: 5, toProtected: '10-30 s', startup: '1-2 s' },
      'beta.10': { days: 22, sessions: 31, crashes: 0, ipNew: [258, 300], lag: [26, 79], minMed: 44, errors: 2, toProtected: '10-30 s', startup: '1-2 s' },
      'beta.9': { days: 14, sessions: 19, crashes: 0, ipNew: [146, 180], lag: [17, 49], minMed: 41, errors: 2, toProtected: '30-60 s', startup: '2-4 s' },
      'beta.8': { days: 9, sessions: 12, crashes: 0, ipNew: [86, 110], lag: [12, 32], minMed: 39, errors: 1, toProtected: '30-60 s', startup: '2-4 s' },
    },
  };

  // ---------- ambiente: relatório à parte, sem ID, uma vez por dia; só contagens; não filtrado
  const ER = 312; // relatórios de ambiente no período (um por instalação por dia)
  const envRows = (rows) => rows.map(([k, p]) => ({ k, n: Math.round(ER * (P / 7) * p) }));
  const env = {
    unfiltered: true, reports: Math.round(ER * (P / 7)),
    windows: envRows([['Windows 11 24H2', 0.49], ['Windows 11 23H2', 0.22], ['Windows 10 22H2', 0.26], ['Outro', 0.03]]),
    lang: envRows([['pt', 0.46], ['en', 0.18], ['ru', 0.09], ['es', 0.08], ['de', 0.06], ['pl', 0.05], ['tr', 0.04], ['outro', 0.04]]),
    window: envRows([['Pequena', 0.31], ['Média', 0.48], ['Grande', 0.21]]),
    scale: envRows([['100%', 0.44], ['125%', 0.37], ['150%', 0.16], ['175%+', 0.03]]),
    network: envRows([['5G', 0.52], ['4G', 0.44], ['3G', 0.04]]),
    brands: envRows([['Samsung', 0.38], ['Xiaomi', 0.21], ['Google', 0.12], ['Apple', 0.09], ['Motorola', 0.07], ['OnePlus', 0.05], ['Outra', 0.08]]),
    android: envRows([['15', 0.34], ['14', 0.31], ['13', 0.14], ['12 ou antes', 0.12], ['iOS (hotspot)', 0.09]]),
  };

  // ---------- GitHub (não filtrado)
  const gdays = [...Array(14)].map((_, i) => ({ date: addDays(MOCK.today, i - 13), n: Math.round(18 + r() * 30 + (i === 9 ? 40 : 0)), u: Math.round(8 + r() * 12) }));
  const github = {
    unfiltered: true, stars: 11, forks: 1, watchers: 3, issues: 2,
    views: gdays.reduce((a, d) => a + d.n, 0), uniques: 163, clones: 38, cloneUniques: 21, days: gdays,
    downloadsPeriod: Math.round(37 * (P / 7) ** 0.85),
    releases: [...vlist].reverse().map((v, i) => ({ tag: v.tag, date: v.date, exe: [24, 41, 33, 18, 9, 6][i] || 3 })),
    referrers: [['reddit.com', 141, 62], ['google.com', 88, 40], ['youtube.com', 51, 23], ['github.com', 37, 19], ['steamcommunity.com', 22, 11], ['discord.com', 14, 9]].map(([k, n, u]) => ({ k, n, u })),
    paths: [['/', 402, 151], ['/releases', 188, 97], ['/blob/main/docs/PRIVACY-AND-SECURITY.md', 61, 38], ['/issues', 24, 14]].map(([k, n, u]) => ({ k, n, u })),
  };

  return { asOf: MOCK.today, now: MOCK.now, period: P, chartN, filters: F, factor: f, options: MOCK.options, releases: vlist,
    days, totals: T, prevTotals: PT, kpis,
    live: { ...liveSt, lastHour: { open: lastOpen, game: lastGame }, feed },
    when, funnel, cohorts, activeDays, installAge, sessionLen, startKind, visible, countries,
    screens, paths, firstScreen, shortcuts, settingsChanged, phone, cs2, rel, alerts, versions, env, github };
}

/** Estado ao vivo só em contagens. Em produção vem de /api/live; aqui sai de instalações fingidas que nunca saem desta função. */
function mockLiveState(r, n, F) {
  const MODES = { game: 'Game only', cs2open: 'Whole PC while CS2 is open', always: 'Whole PC, always', nophone: 'No phone' };
  const LINKS = { usb: 'USB', hotspot: 'Hotspot', other: 'Outro adaptador' };
  const pick = (a) => a[Math.floor(r() * a.length)];
  const s = [...Array(n)].map(() => {
    const cs2 = r() > 0.3, tray = r() > 0.65;
    return {
      version: F.version === 'all' ? (r() > 0.25 ? 'beta.11' : 'beta.10') : F.version === 'older' ? 'beta.8' : F.version,
      mode: F.mode !== 'all' ? MODES[F.mode] : pick(['Game only', 'Whole PC while CS2 is open', 'Whole PC while CS2 is open', 'Whole PC, always']),
      link: F.link !== 'all' ? LINKS[F.link] : pick(['USB', 'USB', 'Hotspot']),
      where: tray ? 'Na bandeja' : pick(['Connection', 'Connection', 'Connection', 'Servers', 'Servers', 'Accounts', 'Regions', 'Configs']),
      cs2, prot: cs2 || r() > 0.4,
    };
  });
  const count = (key) => { const m = {}; for (const x of s) m[x[key]] = (m[x[key]] || 0) + 1; return Object.entries(m).map(([k, c]) => ({ k, n: c })).sort((a, b) => b.n - a.n); };
  return { now: n, inGame: s.filter((x) => x.cs2).length, protectedN: s.filter((x) => x.prot).length, byVersion: count('version'), byMode: count('mode'), byLink: count('link'), byScreen: count('where') };
}

/** Um evento do feed ao vivo: tipo, versão e resultado, sem país. A hora vem arredondada aos 5 min do sinal. */
function mockLiveEvent(r, t, F) {
  const v = F && F.version !== 'all' && F.version !== 'older' ? F.version : r() > 0.25 ? 'beta.11' : 'beta.10';
  const x = r();
  let kind, text, tone;
  if (x < 0.46) { const s = Math.round(4 + r() * 18); const q = r(); kind = 'ip'; tone = q < 0.7 ? 'good' : q < 0.9 ? 'warn' : 'bad'; text = `troca de IP em ${s} s · ${q < 0.7 ? 'boa' : q < 0.9 ? 'razoável' : 'má'}`; }
  else if (x < 0.56) { kind = 'ip'; tone = 'warn'; text = 'troca de IP · ficou o mesmo IP'; }
  else if (x < 0.68) { kind = 'match'; const lag = r() < 0.3; tone = lag ? 'warn' : 'good'; text = `partida ${r() > 0.6 ? 'Valve' : 'comunitária'} acabou${lag ? ' · com lag' : ' · sem lag'}`; }
  else if (x < 0.78) { kind = 'start'; tone = 'acc'; text = `app aberta · ${r() > 0.4 ? 'com o Windows' : 'à mão'}`; }
  else if (x < 0.86) { kind = 'kick'; tone = 'warn'; text = r() > 0.5 ? 'kick · o Rejoin voltou a entrar' : 'kick'; }
  else if (x < 0.93) { kind = 'phone'; tone = 'bad'; text = 'telemóvel caiu · tráfego bloqueado'; }
  else if (x < 0.975) { kind = 'update'; tone = 'acc'; text = 'atualização concluída'; }
  else { kind = 'crash'; tone = 'bad'; text = 'crash'; }
  return { t: t - (t % 300000), kind, version: v, tone, text };
}
