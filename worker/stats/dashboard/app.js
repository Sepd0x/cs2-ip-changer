// Dashboard v3 (4 nov. 2026): as 6 páginas da maquete aprovada (Hoje, Funil, Utilizadores, Na app, Qualidade, Aquisição) sobre
// os dados reais de /api/summary, /api/live e /api/github (real.js). Não gera números: o que a API não dá diz "ainda não
// recolhido" e a fonte. O mapa de cliques usa o ui-map.js e as capturas da app (ui/*.png). A v2 fica em /v2.
'use strict';
(function () {
  const $ = (s) => document.querySelector(s);
  const $$ = (s) => [...document.querySelectorAll(s)];
  const nf = new Intl.NumberFormat('pt-PT');
  const N = (v) => (v == null ? 'sem dados' : nf.format(Math.round(v)));
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const MES = ['jan.', 'fev.', 'mar.', 'abr.', 'mai.', 'jun.', 'jul.', 'ago.', 'set.', 'out.', 'nov.', 'dez.'];
  const DIA = ['domingo', 'segunda', 'terça', 'quarta', 'quinta', 'sexta', 'sábado'];
  const dd = (d) => { const [, m, x] = String(d).split('-'); return `${+x} ${MES[+m - 1]}`; };
  const dshort = (d) => { const [, m, x] = d.split('-'); return `${+x}/${+m}`; };
  const wday = (d) => DIA[new Date(d + 'T00:00:00Z').getUTCDay()];
  const ago = (m) => (m < 1 ? 'agora' : m < 60 ? `há ${m} min` : m < 48 * 60 ? `há ${Math.floor(m / 60)} h${m % 60 ? ` ${m % 60} min` : ''}` : `há ${Math.floor(m / 1440)} dias`);
  const mins = (m) => (m == null ? 'sem dados' : m < 60 ? `${nf.format(m)} min` : m < 6000 ? `${nf.format(Math.round(m / 6) / 10)} h` : `${nf.format(Math.round(m / 60))} h`);
  /** Percentagem só com n de 10 ou mais; abaixo disso, "a de b" (com 4 pessoas, uma vale 25%). */
  const share = (a, b) => (a == null || b == null || !b ? null : b < 10 ? `${N(a)} de ${N(b)}` : `${nf.format(Math.round((1000 * a) / b) / 10)}%`);
  const short = (v) => String(v).replace(/^0\.1\.0-/, '');
  /** Ordem das versões: beta.10 depois de beta.9, a release depois das betas. */
  function compareV(a, b) {
    const p = (v) => { const [m2, pre] = String(v).split(/-(.*)/); return [m2.split('.').map(Number), pre ? pre.split('.').map((x) => (/^\d+$/.test(x) ? +x : x)) : []]; };
    const [am, ap] = p(a), [bm, bp] = p(b);
    for (let i = 0; i < 3; i++) if ((am[i] || 0) !== (bm[i] || 0)) return (am[i] || 0) - (bm[i] || 0);
    if (!ap.length || !bp.length) return (ap.length ? -1 : 0) - (bp.length ? -1 : 0);
    for (let i = 0; i < Math.max(ap.length, bp.length); i++) {
      if (ap[i] === bp[i]) continue;
      if (ap[i] === undefined) return -1;
      if (bp[i] === undefined) return 1;
      if (typeof ap[i] !== typeof bp[i]) return typeof ap[i] === 'number' ? -1 : 1;
      return ap[i] < bp[i] ? -1 : 1;
    }
    return 0;
  }
  const addDays = (d, n) => { const x = new Date(d + 'T00:00:00Z'); x.setUTCDate(x.getUTCDate() + n); return x.toISOString().slice(0, 10); };
  // Horas de Lisboa, onde o painel é lido
  const LX = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Lisbon', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
  const lx = (t) => { const p = Object.fromEntries(LX.formatToParts(new Date(t)).map((x) => [x.type, x.value])); return { date: `${p.year}-${p.month}-${p.day}`, hm: `${p.hour}:${p.minute}`, h: +p.hour }; };
  const whenTxt = (t) => { if (!t) return 'nunca'; const a = lx(t), n = lx(Date.now()); const y = lx(Date.now() - 864e5).date; return `${a.date === n.date ? 'hoje' : a.date === y ? 'ontem' : `a ${dd(a.date)}`} às ${a.hm}`; };
  const agoT = (t) => (t ? ago(Math.max(0, Math.round((Date.now() - t) / 60000))) : 'nunca');

  const COUNTRY = { PT: 'Portugal', BR: 'Brasil', US: 'EUA', ES: 'Espanha', DE: 'Alemanha', FR: 'França', PL: 'Polónia', RU: 'Rússia', CA: 'Canadá', GB: 'Reino Unido', TR: 'Turquia', IT: 'Itália', NL: 'Países Baixos', '?': 'Desconhecido' };
  const MODES = { game: 'Só o jogo', cs2open: 'PC inteiro com o CS2', always: 'PC inteiro, sempre', nophone: 'Sem telemóvel', '?': 'Desconhecido' };
  const LINKS = { usb: 'USB', hotspot: 'Hotspot', other: 'Outro adaptador', none: 'none (sem telemóvel)', '?': 'Desconhecida' };
  const SCREEN = Object.fromEntries(UI.screens.map((s) => [s.id, s.name]));
  const scrName = (s) => (s === 'tray' ? 'Na bandeja' : SCREEN[s] || s);

  const PREFIX = { e: 'api', n: 'por recolher', x: 'externa' };
  const TIP = { e: 'Vem do /api/summary ou /api/live', n: 'Ainda não é recolhido: a app, o Worker ou o api.js não o guardam', x: 'Fonte de fora do Worker' };
  const src = (k, t) => `<i class="src s-${k}" title="${TIP[k]}">${PREFIX[k]} · ${esc(t)}</i>`;
  const box = (title, sub, srcs, body, cls = '') =>
    `<section class="gb ${cls}"><h2>${title}${sub ? `<small>${sub}</small>` : ''}</h2>${srcs && srcs.length ? `<div class="srcs">${srcs.map(([k, t]) => src(k, t)).join('')}</div>` : ''}${body}</section>`;
  const empty = (what, act) => `<div class="empty"><b>${what}</b>${act ? `<span class="act">${act}</span>` : ''}</div>`;
  const NOT = 'ainda não recolhido';

  function delta(cur, prev, fmt) {
    if (prev == null || cur == null) return '';
    if (cur === prev) return '<span class="same">igual</span>';
    const d = cur - prev;
    if (prev < 20) return `<span class="${d > 0 ? 'up' : 'down'}">${d > 0 ? '+' : '-'}${fmt ? fmt(Math.abs(d)) : nf.format(Math.abs(d))}</span>`;
    const p = Math.round((100 * d) / prev);
    return `<span class="${d > 0 ? 'up' : 'down'}">${d > 0 ? '+' : ''}${p}%</span>`;
  }
  function mini(vals) {
    const m = Math.max(1, ...vals.map((v) => v || 0));
    return `<div class="mini">${vals.map((v, i) => (v ? `<i class="${i === vals.length - 1 ? 'last' : ''}" style="height:${Math.max(8, (100 * v) / m)}%" title="${N(v)}"></i>` : '<i class="z"></i>')).join('')}</div>`;
  }
  function spark(vals, w = 84, h = 20, cls = 'acc') {
    const m = Math.max(1, ...vals);
    const pts = vals.map((v, i) => `${((i * w) / Math.max(1, vals.length - 1)).toFixed(1)},${(h - 1 - ((h - 2) * v) / m).toFixed(1)}`).join(' ');
    return `<svg class="spark" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><polyline points="${pts}" fill="none" stroke="var(--${cls === 'bad' ? 'bad' : 'acc'})" stroke-width="1.4"/></svg>`;
  }
  /** Com menos de 10 por dia, uma linha ou uma barra faz de 1 um pico. Aqui fica o número de cada dia (ponto = 0). */
  const isSmall = (vals) => Math.max(0, ...vals.map((v) => v || 0)) < 10;
  const nums = (vals, title) => `<div class="nums" style="--n:${vals.length}"${title ? ` title="${esc(title)}"` : ''}>${vals.map((v) => (v ? `<b>${v}</b>` : '<i>·</i>')).join('')}</div>`;
  function hstrip(h, nowH, note) {
    const row = (lab, vals, isT) => `<span class="hl">${lab}</span>${vals.map((v, i) => (v == null ? '<i class="fu"></i>' : v ? `<b class="${isT && i === nowH ? 'now' : ''}">${v}</b>` : `<i class="${isT && i === nowH ? 'now' : ''}">·</i>`)).join('')}`;
    return `<div class="tw"><div class="hstrip">${row('hoje', h.today, true)}${row('ontem', h.yday)}<span></span>${h.yday.map((_, i) => `<span class="hx">${i % 3 === 0 ? i + 'h' : ''}</span>`).join('')}</div></div>
      <p class="note">${note || 'Instalações com um relatório em cada hora de Lisboa. Ponto: nenhuma. Com 10 ou mais numa hora passa a gráfico de barras.'}</p>`;
  }
  const niceMax = (m) => { if (m <= 4) return 4; if (m <= 6) return 6; if (m <= 10) return 10; if (m <= 20) return 20; const p = 10 ** Math.floor(Math.log10(m)); const s = [1, 2, 2.5, 5, 10].find((x) => x * p >= m); return s * p; };
  function bl(rows, opt = {}) {
    if (!rows.length) return empty(opt.none || 'Nada no período.');
    const m = opt.max || Math.max(1, ...rows.map((r) => r[1]));
    const tot = opt.total || rows.reduce((s, r) => s + r[1], 0);
    return `<div class="bl">${rows.map(([k, v, extra, tone]) => `<span class="k" title="${esc(k)}">${esc(k)}</span><span class="t"><i class="${tone || opt.tone || ''}" style="width:${(100 * v) / m}%"></i></span><span class="v">${opt.fmt ? opt.fmt(v) : N(v)}${opt.pct && tot >= 10 ? `<small>${Math.round((100 * v) / tot)}%</small>` : ''}${extra != null && opt.extra ? `<small>${opt.extra(extra)}</small>` : ''}</span>`).join('')}</div>`;
  }
  /** Barras por dia em HTML: segmentos empilhados, o zero visível, o número em cima enquanto cabe, marcas das versões. */
  function dbars(days, segs, marks = [], opt = {}) {
    const tot = (d) => segs.reduce((s, [k]) => s + (d[k] || 0), 0);
    const max = niceMax(Math.max(1, ...days.map(tot)));
    const showN = days.length <= 31 && max <= 20;
    const every = days.length <= 14 ? 1 : days.length <= 31 ? 3 : 10;
    const ticks = [0, max / 2, max];
    const ix = new Map(days.map((d, i) => [d.date, i]));
    return `<div class="dbars"><div class="plot">
      <div class="yax">${ticks.map((t) => `<span style="bottom:${(100 * t) / max}%">${nf.format(t)}</span>`).join('')}</div>
      ${ticks.slice(1).map((t) => `<div class="grid" style="bottom:${(100 * t) / max}%"></div>`).join('')}
      ${days.map((d, i) => { const t = tot(d); const isT = i === days.length - 1 && opt.todayPartial;
        return `<div class="col${t ? '' : ' zero'}${isT ? ' today' : ''}" data-tip="${dd(d.date)}: ${segs.map(([k, l]) => `${l} ${N(d[k] || 0)}`).join(' · ')}${isT ? ' (até agora)' : ''}">
          ${segs.map(([k, , c]) => (d[k] ? `<i class="${c}" style="height:${(100 * d[k]) / max}%"></i>` : '')).join('')}
          ${showN ? `<span class="lbl" style="bottom:${(100 * t) / max}%">${t}</span>` : ''}</div>`; }).join('')}
      ${marks.filter((m) => ix.has(m.date)).map((m) => `<div class="mark ${m.cls || ''}${ix.get(m.date) / days.length > 0.6 ? ' r' : ''}" style="left:calc(26px + (100% - 26px) * ${ix.get(m.date) / days.length})"><span>${esc(m.label)}</span></div>`).join('')}
    </div><div class="xax">${days.map((d, i) => { const on = i % every === 0 || i === days.length - 1; const o = on && every === 1 && i % 3 !== 0 && i !== days.length - 1; return `<span${o ? ' class="o"' : ''}>${on ? dshort(d.date) : ''}</span>`; }).join('')}</div></div>`;
  }
  function hbars(h, nowH) {
    const max = niceMax(Math.max(1, ...h.today.map((v) => v || 0), ...h.yday));
    return `<div class="dbars hbars"><div class="plot">
      <div class="yax">${[0, max / 2, max].map((t) => `<span style="bottom:${(100 * t) / max}%">${nf.format(t)}</span>`).join('')}</div>
      ${[max / 2, max].map((t) => `<div class="grid" style="bottom:${(100 * t) / max}%"></div>`).join('')}
      ${h.yday.map((y, i) => { const t = h.today[i];
        return `<div class="col${t == null ? ' future' : ''}${i === nowH ? ' nowc' : ''}" data-tip="${String(i).padStart(2, '0')}h: hoje ${t == null ? 'ainda não' : N(t)} · ontem ${N(y)}">
          <i class="y" style="height:${(100 * y) / max}%"></i>${t ? `<i class="t" style="height:${(100 * t) / max}%"></i>` : ''}</div>`; }).join('')}
    </div><div class="xax">${h.yday.map((_, i) => `<span>${i % 3 === 0 ? `${i}h` : ''}</span>`).join('')}</div></div>
    <div class="legendrow"><span><i style="background:var(--acc)"></i>hoje</span><span><i style="border:1px solid #6a6a6a"></i>ontem</span></div>`;
  }
  const tally = (rows, names) => rows.map((x) => [names ? names[x.k] || x.k : x.k, x.n]);
  // 4 nov.: peças das páginas que vieram da v2 (tudo o que a v2 mostrava está agora na v3)
  const sumN = (rows) => rows.reduce((s, x) => s + (x.n || 0), 0);
  /** Bandas { band, n } em barras com a % de cada uma (só com 10 ou mais). */
  const bandBl = (rows, none) => (sumN(rows) ? bl(rows.map((x) => [x.band, x.n]), { pct: true }) : empty(none || 'Nada no período.'));
  /** A banda do meio: a primeira onde a soma passa de metade. */
  const medianBand = (rows) => { const t = sumN(rows); if (!t) return null; let a = 0; for (const x of rows) { a += x.n || 0; if (a >= t / 2) return x.band; } return null; };
  /** Bandas por ligação { band, usb, hotspot }: a % de cada ligação em cada banda, com o n de cada coluna. */
  function linkTab(rows, none) {
    const tu = rows.reduce((s, x) => s + (x.usb || 0), 0), th = rows.reduce((s, x) => s + (x.hotspot || 0), 0);
    if (!tu && !th) return empty(none || 'Sem partidas com USB ou hotspot no período.');
    const cellP = (v, t) => (!t ? '<td class="n dim">-</td>' : `<td class="n">${share(v, t) && t >= 10 ? share(v, t) : N(v)}</td>`);
    return `<div class="tw"><table class="t"><thead><tr><th>Banda</th><th class="n">USB <small class="dim">n=${N(tu)}</small></th><th class="n">Hotspot <small class="dim">n=${N(th)}</small></th></tr></thead><tbody>${rows.map((x) => `<tr><td>${esc(x.band)}</td>${cellP(x.usb || 0, tu)}${cellP(x.hotspot || 0, th)}</tr>`).join('')}</tbody></table></div>`;
  }
  /** O botão do ui-map.js que contém o centro de uma célula da grelha de 24 × 16. */
  function btnAt(S, c, r) {
    const { W, H, X0, COLS, ROWS } = UI;
    const x = X0 + (c + 0.5) * ((W - X0) / COLS), y = (r + 0.5) * (H / ROWS);
    const b = S.buttons.find((q2) => q2.r && [q2.r, ...(q2.more || [])].some((rr) => x >= rr[0] - 4 && x <= rr[0] + rr[2] + 4 && y >= rr[1] - 4 && y <= rr[1] + rr[3] + 4));
    return b ? b.label : null;
  }
  /** Quando usam: dia da semana por hora de Lisboa, instalações-hora com um relatório. */
  function whenMap(when) {
    const days = ['seg', 'ter', 'qua', 'qui', 'sex', 'sáb', 'dom'];
    const all = when.flat(), m = Math.max(1, ...all), tot = all.reduce((s, v) => s + v, 0);
    if (!tot) return empty('Nenhum relatório no período.');
    return `<div class="tw"><div class="wmap">${days.map((dn, di) => `<span class="hl">${dn}</span>${when[di].map((v, h) => `<i style="background:${v ? `rgba(var(--t-accent-rgb),${(0.15 + (0.85 * v) / m).toFixed(2)})` : 'var(--btn)'}" data-tip="${dn} ${h}h: ${N(v)} ${v === 1 ? 'instalação' : 'instalações'}">${tot < 60 && v ? v : ''}</i>`).join('')}`).join('')}<span></span>${[...Array(24)].map((_, h) => `<span class="hx">${h % 3 === 0 ? h + 'h' : ''}</span>`).join('')}</div></div>
      <p class="note">${N(tot)} instalações-hora: cada instalação conta uma vez em cada hora com um relatório. Hora de Lisboa.</p>`;
  }
  /** O pico do ao vivo, das amostras de 10 em 10 min (live_hourly). Sem amostras não é zero: o cron ainda não correu. */
  const peakTxt = (L) => {
    const P = L && L.peaks;
    if (!P || P.first == null) return 'Pico de ontem: ainda sem amostras. O cron do stats-dashboard conta o ao vivo de 10 em 10 min e guarda o máximo de cada hora.';
    const t = P.today.filter((v) => v != null);
    return `Pico de ontem: ${P.ydayMax ? `<b>${N(P.ydayMax.n)}</b> às ${P.ydayMax.hour}h` : 'sem amostras ontem'}. Hoje até agora: ${t.length ? `<b>${N(Math.max(...t))}</b>` : 'sem amostras'}. Amostras de 10 em 10 min, o máximo de cada hora.`;
  };
  /** O contador do site está na D1 (a tabela site existe): sem ela, "sem dados", nunca zeros. */
  const siteOk = (D) => !(D.R.warnings || []).some((w) => /^site:/.test(w));
  /** Downloads do exe por dia no período, dos retratos diários do GitHub (gh_daily): null sem dois dias seguidos. */
  function ghPeriod(D) {
    const hist = (D.G && D.G.history && D.G.history.days) || [];
    const from = D.R.days[0].date, to = D.R.asOf;
    const byDay = new Map(hist.filter((x) => x.day >= from && x.day <= to && x.exeDay != null).map((x) => [x.day, x.exeDay]));
    const stars = hist.filter((x) => x.day >= from && x.day <= to && x.starsDay != null).reduce((s, x) => s + x.starsDay, 0);
    return { byDay, covered: byDay.size, downloads: byDay.size ? [...byDay.values()].reduce((s, v) => s + v, 0) : null, stars, hist };
  }
  const SHADES = ['#2b74c2', '#6fb2f2', '#3f6f9f', '#c6e2fb', '#1f3346', '#8fa3b6', '#4b5a69'];
  const NOT_RARE = new Set(['ClearHistory', 'CfgUndo', 'ResetAll', 'LogClear', 'RemoveServers', 'TplClearAvatar', 'ProfRestoreSelected', 'CfgRestore', 'TplNew', 'TplCopyFrom', 'TplDelete', 'ProfForgetAll', 'Restore', 'CfgSnapUse', 'CfgSnapExport', 'CfgSnapDelete', 'DeleteStats', 'ReportProblem', 'Quick']);
  // Nomes dos contadores sem bloco próprio (os outros têm o seu sítio numa página)
  const SHOWN = /^(ip_changed|ip_same|ip_no_network|app_start|crash|first_start|min_prot|min_cs2|cs2_start|kick_seen|vpn_seen|ban_seen|rej_try|rej_back|rej_wait|leak_ok|leak_bad|unclean_exit|cable_hint|(m|ping|jit|jitd|late|loss|sess|st|tp|start|upd|updfrom|phone_setup|chg|chgt|chga|chgq|drop|data|scl|fs)_.*)$/;
  const CNAMES = { log_error: 'Erros escritos no Log', ip_home_back: 'O IP voltou a ser o de casa' };
  const LISTN = { servers: 'Servidores', accounts: 'Contas', flags: 'Jogadores marcados', open_with: 'Open with CS2', open_with_admin: 'Open with CS2 como admin', regions_blocked: 'Regiões desligadas' };

  // ------------------------------------------------ estado e navegação
  const PAGES = [
    ['hoje', 'Hoje', '<path d="M12 3v3M12 18v3M3 12h3M18 12h3"/><circle cx="12" cy="12" r="5"/>'],
    ['funil', 'Funil', '<path d="M3 5h18l-7 8v6l-4-2v-4z"/>'],
    ['utilizadores', 'Utilizadores', '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c.8-3.6 3.4-5.5 6.5-5.5s5.7 1.9 6.5 5.5M16 4.8a3.3 3.3 0 0 1 0 6.4M18.5 14.8c1.6.8 2.6 2.5 3 5.2"/>'],
    ['app', 'Na app', '<rect x="3" y="4" width="18" height="14" rx="1"/><path d="M8 21h8M10 9l5 3-5 3z"/>'],
    ['ligacao', 'Telemóvel e CS2', '<rect x="7" y="2.5" width="10" height="19" rx="2"/><path d="M11 18.5h2"/>'],
    ['qualidade', 'Qualidade', '<path d="M12 3l8 3v6c0 4.5-3.4 8-8 9-4.6-1-8-4.5-8-9V6z"/><path d="M8.5 12l2.5 2.5 4.5-5"/>'],
    ['versoes', 'Versões', '<path d="M3 12l8.5-8.5H20v8.5L11.5 20.5z"/><circle cx="15.5" cy="8" r="1.4"/>'],
    ['aquisicao', 'Aquisição', '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>'],
  ];
  const store = { get: (k) => { try { return localStorage.getItem('v3.' + k); } catch { return null; } }, set: (k, v) => { try { localStorage.setItem('v3.' + k, v); } catch { /* sem armazenamento */ } } };
  const q = new URLSearchParams(location.search);
  const st = { period: 7, page: 'hoje', appTab: 'connection', cmpA: null, cmpB: null, f: { version: 'all', country: 'all', mode: 'all', link: 'all' } };
  if ([1, 7, 30, 90].includes(+store.get('period'))) st.period = +store.get('period');
  if ([1, 7, 30, 90].includes(+q.get('p'))) st.period = +q.get('p');
  for (const k of Object.keys(st.f)) { const v = q.get(k); if (v && /^[A-Za-z0-9_.+-]{1,40}$/.test(v)) st.f[k] = v; }
  // As fontes começam desligadas (4 nov.): ligam-se no interruptor, e ficam lembradas neste browser
  $('#srcs').checked = q.get('src') === '1' || store.get('srcs') === '1';
  $('#mine').checked = V3.DEV;
  let DATA = null;   // { R: resumo do período, H: resumo de 30 dias (Hoje e as barras por dia), L: ao vivo, G: GitHub }
  let seq = 0;

  const pageNow = () => { const p = location.hash.slice(1).split('/')[0]; return PAGES.some((x) => x[0] === p) ? p : 'hoje'; };
  async function load() {
    const my = ++seq;
    const page = pageNow();
    const need30 = page === 'hoje' || (page === 'utilizadores' && st.period < 14) || (['qualidade', 'ligacao', 'versoes'].includes(page) && st.period < 14);
    // Revisão de desempenho (nov. 2026): o GitHub demora 0,3 a 0,5 s quando o Worker acabou de arrancar. Só Aquisição e Versões
    // esperam por ele; as outras páginas desenham-se já (G undefined = ainda a ler) e voltam a desenhar-se quando ele chega
    const G0 = V3.githubNow();
    const ghP = G0 === undefined ? V3.github() : null;
    const ghWait = page === 'aquisicao' || page === 'versoes';
    const [R, H, L, G] = await Promise.all([V3.summary(st.f, st.period), need30 ? V3.summary(st.f, 30) : null, V3.live().catch(() => null), ghWait && ghP ? ghP : G0]);
    if (my !== seq) return false;
    DATA = { R, H: H || R, L, G };
    if (G === undefined && ghP) ghP.then((g) => { if (my !== seq || !DATA) return; DATA.G = g; const y = scrollY; render(); scrollTo(0, y); });
    return true;
  }
  async function refresh() {
    try { if (!(await load())) return; } catch (e) { showErr(e); return; }
    render();
  }
  function showErr(e) {
    $('#main').innerHTML = `<div class="g">${box('Não deu para ler os dados', '', [], `<p class="note">${esc(String((e && e.message) || e))}. Tenta outra vez daqui a um minuto; se continuar, o Worker stats-dashboard ou a D1 estão em baixo.</p>`, 's12')}</div>`;
    const pill = $('#freshPill'); pill.className = 'fresh-pill bad'; pill.innerHTML = '<span class="sq"></span><span>sem resposta da API</span>';
  }

  /** O que a faixa de frescura, o topo e o "Pede atenção" usam. */
  function freshOf(D) {
    const fr = D.H.fresh || {}, b = fr.budget || { rows: 0, cap: 40000, live: 0, liveCap: 12000 };
    const lastReal = fr.lastTs ?? D.H.lastTs ?? null;
    const realAgo = lastReal ? Math.round((Date.now() - lastReal) / 60000) : null;
    const tone = realAgo == null || realAgo > 720 ? 'bad' : realAgo > 180 ? 'warn' : 'ok';
    return { lastReal, realAgo, lastAny: fr.lastAny ?? null, live: D.L ? D.L.lastTs : null, budget: b, pct: b.cap ? Math.round((100 * b.rows) / b.cap) : 0, tone };
  }
  function ghOk(G) { return !!(G && G.stars != null); }

  function render() {
    const D = DATA; if (!D) return;
    st.page = pageNow();
    document.body.classList.toggle('nosrc', !$('#srcs').checked);
    document.body.dataset.page = st.page;
    for (const b of $$('#period button')) { b.setAttribute('aria-pressed', String(+b.dataset.p === st.period)); b.disabled = st.page === 'hoje'; }
    const Fr = freshOf(D);
    const pill = $('#freshPill');
    pill.className = 'fresh-pill ' + Fr.tone;
    pill.innerHTML = `<span class="sq"></span><span>último relatório <b>${Fr.lastReal ? agoT(Fr.lastReal) : 'nenhum em 90 dias'}</b> · ${D.L ? `${N(D.L.now)} a usar agora` : 'ao vivo sem resposta'}</span>`;
    const al = alerts(D, Fr);
    const newErr = D.R.errors.filter((e) => e.first && e.first.day >= D.R.days[0].date).length;
    const cnt = { hoje: al.filter((a) => a.tone === 'bad').length, qualidade: newErr };
    $('#nav').innerHTML = PAGES.map(([id, label, icon]) => `<a href="#${id}"${id === st.page ? ' aria-current="page"' : ''}><svg viewBox="0 0 24 24">${icon}</svg>${label}${cnt[id] ? `<span class="cnt bad">${cnt[id]}</span>` : ''}</a>`).join('')
      + `<div class="foot">Unidade: a instalação (ID fixo desde a beta.9) e a instalação-dia. Dias em UTC, horas de Lisboa. Só tu vês este painel: números exatos, sem "menos de 5".</div>`
      + '<div class="links"><a href="/dashboard">v4</a><a href="/v2">v2</a><a href="/classic">clássico</a></div>';
    $('#crumbPage').textContent = PAGES.find((p) => p[0] === st.page)[1];
    document.title = `${PAGES.find((p) => p[0] === st.page)[1]} · CS2 IP Changer stats`;
    renderFilters(D);
    const fn = { hoje: pHoje, funil: pFunil, utilizadores: pUsers, app: pApp, ligacao: pLink, qualidade: pQual, versoes: pVer, aquisicao: pAcq }[st.page];
    const devband = V3.DEV ? '<div class="devband">Só os teus PCs: builds de desenvolvimento e os IDs de EXCLUDE_IDS. Os números de toda a gente ficam de fora.<a href="?">ver toda a gente</a></div>' : '';
    $('#main').innerHTML = devband + fn(D, Fr, al) + foot(D);
    bindPage();
    document.documentElement.style.setProperty('--hdr', $('#hdr').offsetHeight + 'px');
  }
  const periodTxt = (D) => (D.R.period === 1 ? 'hoje' : `últimos ${D.R.period} dias`);
  const foot = (D) => `<div class="foot"><span>Lido às ${lx(Date.now()).hm} de ${dd(lx(Date.now()).date)} · ${V3.DEV ? 'só os teus PCs' : D.R.excluded ? `sem os teus ${D.R.excluded} PCs (EXCLUDE_IDS) e sem builds de dev` : 'sem builds de dev'}${D.R.automated ? ` · ${N(D.R.automated)} ${D.R.automated === 1 ? 'análise automática deixada' : 'análises automáticas deixadas'} de fora` : ''}</span>
    <details><summary>Como se conta</summary><p>Instalação diferente: um ID de instalação (fixo desde a beta.9), contado uma vez no período. Instalação-dia: um ID num dia (a mesma pessoa em 3 dias conta 3). Nova: aceitou o aviso nesse dia (1.º arranque); quem atualizou de uma beta antiga não conta como nova. Os teus PCs (builds de dev e EXCLUDE_IDS) ficam de fora; "Os meus PCs" mostra só esses. Análises automáticas (0 contas Steam, 0 cliques, 0 minutos, como o VirusTotal) nunca contam. Percentagens só com 10 ou mais casos; abaixo disso, "3 de 4". Dias em UTC, horas de Lisboa. O que diz "${NOT}" ou "sem dados" não é zero: ainda não há fonte. O site conta visitas e cliques em Download sem cookies e sem ID (Do Not Track respeitado). O ao vivo por hora e os downloads por dia vêm do cron do stats-dashboard. As linhas com ID têm 12 meses; as somas sem ID ficam.</p></details></div>`;

  function renderFilters(D) {
    const o = D.R.options || { version: [], country: [] };
    const opts = {
      version: [['all', 'Todas'], ...o.version.slice().sort(compareV).reverse().map((v) => [v, short(v)])],
      country: [['all', 'Todos'], ...o.country.map((c) => [c, COUNTRY[c] || c])],
      mode: [['all', 'Todos'], ...Object.entries(MODES).filter(([k]) => k !== '?')],
      link: [['all', 'Todas'], ...Object.entries(LINKS).filter(([k]) => k !== '?')],
    };
    for (const k of Object.keys(st.f)) if (st.f[k] !== 'all' && !opts[k].some(([v]) => v === st.f[k])) opts[k].push([st.f[k], st.f[k]]);
    const sel = (k, label) => `<label>${label}<select data-f="${k}">${opts[k].map(([v, t]) => `<option value="${esc(v)}"${st.f[k] === v ? ' selected' : ''}>${esc(t)}</option>`).join('')}</select></label>`;
    const act = Object.values(st.f).filter((v) => v !== 'all').length;
    $('#fltN').textContent = act ? String(act) : '';
    $('#fltBox').innerHTML = sel('version', 'Versão') + sel('country', 'País') + sel('mode', 'Modo') + sel('link', 'Ligação')
      + `<p>O mapa de cliques só segue a versão; o ambiente, o ao vivo, o site e o GitHub não seguem filtros.</p>${act ? '<button class="btn" id="resetF">Limpar filtros</button>' : ''}`;
    for (const s of $$('[data-f]')) s.onchange = () => { st.f[s.dataset.f] = s.value; refresh(); };
    const rb = $('#resetF'); if (rb) rb.onclick = () => { for (const k of Object.keys(st.f)) st.f[k] = 'all'; refresh(); };
  }

  /** Regras do "Pede atenção", cada uma com o seu n mínimo. A faixa de frescura já diz quando nada chega. */
  function alerts(D, Fr) {
    const H = D.H, out = [];
    if (Fr.pct >= 70) out.push({ tone: Fr.pct >= 90 ? 'bad' : 'warn', t: `Orçamento da D1 a ${Fr.pct}%`, d: `${N(Fr.budget.rows)} de ${N(Fr.budget.cap)} linhas hoje. No limite o Worker responde 429 aos relatórios até o dia mudar (UTC).`, src: ['e', 'fresh.budget'] });
    // Revisão de segurança (4 nov.): o contador do site aceita uma Origin forjada fora do browser; uma enxurrada só se vê aqui
    const sc = Fr.budget.siteCap ? Fr.budget.site / Fr.budget.siteCap : 0;
    if (sc >= 0.5) out.push({ tone: sc >= 0.9 ? 'bad' : 'warn', t: `Contador do site a ${Math.round(100 * sc)}% do limite`, d: `${N(Fr.budget.site)} de ${N(Fr.budget.siteCap)} linhas hoje. Muito acima das visitas normais é alguém a mandar visitas falsas (a origem pode ser forjada fora do browser): os números do site de hoje não valem, e no limite deixam de contar até o dia mudar (UTC). Os relatórios da app não são afetados.`, src: ['e', 'fresh.budget.site'] });
    if (D.G !== undefined && !ghOk(D.G)) out.push({ tone: 'bad', t: 'GitHub sem acesso', d: `${D.G && D.G.problems && D.G.problems.length ? esc(D.G.problems.slice(0, 2).join(' · ')) : 'O /api/github não respondeu'}. Estrelas, downloads e visitas ficam sem dados; não são zeros.`, src: ['x', 'github.problems'] });
    const lk = H.links || [], lkN = lk.reduce((s, x) => s + x.n, 0), none = (lk.find((x) => x.k === 'none') || { n: 0 }).n;
    if (lkN >= 5 && none === lkN) out.push({ tone: 'warn', t: 'Ligação chega sempre "none"', d: `${N(none)} de ${N(lkN)} instalações-dia dos últimos 30 dias. Ping e lag por ligação ficam vazios até isto mudar.`, src: ['e', 'links'] });
    const since7 = addDays(H.asOf, -6);
    for (const e of H.errors.filter((x) => x.first && x.first.day >= since7).slice(0, 2))
      out.push({ tone: 'bad', t: `Erro novo: ${e.type}`, d: `Em ${esc(e.at)}, 1.º visto a ${dd(e.first.day)} na ${esc(short(e.first.app))}. ${N(e.n)} ${e.n === 1 ? 'vez' : 'vezes'} em ${N(e.days)} ${e.days === 1 ? 'instalação-dia' : 'instalações-dia'} nos últimos 30 dias.`, src: ['e', 'errors[].first'] });
    const c7 = H.days.slice(-7), cr = c7.reduce((s, x) => s + x.crashes, 0), ss = c7.reduce((s, x) => s + x.sessions, 0);
    if (cr) out.push({ tone: 'warn', t: `${N(cr)} ${cr === 1 ? 'crash' : 'crashes'} nos últimos 7 dias`, d: `em ${N(ss)} ${ss === 1 ? 'sessão' : 'sessões'}. Onde, na página Qualidade.`, src: ['e', 'days.crashes'] });
    if ((H.truncated || []).length) out.push({ tone: 'warn', t: 'Leitura cortada', d: `Os dias mais antigos ficaram de fora em: ${H.truncated.join(', ')}.`, src: ['e', 'truncated'] });
    if ((H.warnings || []).length) out.push({ tone: 'warn', t: 'Tabelas em falta na D1', d: H.warnings.map((w) => w.split(':')[0]).join(', ') + ': esses blocos ficam vazios.', src: ['e', 'warnings'] });
    return out;
  }

  // ------------------------------------------------ Hoje
  // Antes do meio-dia (Lisboa) o dia de hoje está quase vazio: o bloco grande é "ontem, dia inteiro" contra anteontem, e "hoje até
  // agora" fica numa linha pequena. Depois do meio-dia troca, contra ontem à mesma hora.
  function pHoje(D, Fr, al) {
    const H = D.H, L = D.L, now = lx(Date.now());
    const bad = Fr.tone === 'bad', warn = Fr.tone === 'warn';
    const early = now.h < 12;
    const kd = (x) => (x ? { users: x.users, fresh: x.fresh, back: Math.max(0, x.users - x.fresh), minutes: x.minutes, ip: x.ipNew + x.ipSame + x.ipNone } : { users: 0, fresh: 0, back: 0, minutes: 0, ip: 0 });
    const t = kd(H.days.at(-1)), y = kd(H.days.at(-2)), a = kd(H.days.at(-3));
    const s = H.sameTime || { users: 0, fresh: 0, minutes: 0 };
    const main = early ? y : t, cmp = early ? a : { users: s.users, fresh: s.fresh, back: Math.max(0, s.users - s.fresh), minutes: s.minutes, ip: null };
    const side = early ? t : y, sideTxt = early ? 'hoje até agora' : 'ontem';
    const spark14 = H.days.slice(-15, -1).map(kd);
    const k = (label, key, opt = {}) => { const f = opt.fmt || N; const v = main[key];
      return `<div class="kc"><div class="kl">${label}</div><div class="kv">${f(v)}${v || cmp[key] ? delta(v, cmp[key], opt.fmt) : ''}</div>
      <div class="kd">${cmp[key] == null ? 'trocas <b>sem hora</b>' : `${early ? 'anteontem' : 'ontem à mesma hora'} <b>${f(cmp[key])}</b>`}</div><div class="kd">${sideTxt} <b>${f(side[key])}</b></div>${mini(spark14.map((d) => d[key]))}</div>`; };
    const head = !Fr.lastReal ? 'Nenhum relatório nos últimos 90 dias' : bad ? `Nada chega ${agoT(Fr.lastReal)}` : warn ? `Último relatório ${agoT(Fr.lastReal)}` : `A chegar: último relatório ${agoT(Fr.lastReal)}`;
    const hourly = H.hourly || { today: Array(24).fill(null), yday: Array(24).fill(0), hour: now.h };
    const hourSmall = isSmall([...hourly.today, ...hourly.yday]);
    const hourBox = box('Por hora', 'instalações com relatório · hoje contra ontem', [['e', 'hourly']], hourSmall ? hstrip(hourly, hourly.hour) : hbars(hourly, hourly.hour) + '<p class="note">Dos relatórios da app (cada instalação conta uma vez por hora). O ao vivo não guarda histórico.</p>', hourSmall ? 's12' : 's8');
    // v4 passo 1: a caixa Agora fica sempre (0 a usar e o último sinal também é informação)
    const liveBox = L
      ? box('Agora', 'sinal nos últimos 6 min, número exato', [['e', '/api/live']], `<div class="bigline"><span class="big">${N(L.now)}</span><span>a usar · ${N(L.inGame)} em jogo · ${N(L.protectedN)} Protected</span></div>
        <h3>Onde estão</h3>${bl(L.byScreen.map((x) => [scrName(x.k), x.n]))}<h3>Ligação</h3>${bl(tally(L.byLink, LINKS))}<h3>Modo</h3>${bl(tally(L.byMode, MODES))}<h3>Versão</h3>${bl(L.byVersion.map((x) => [short(x.k), x.n]))}<p class="note">${peakTxt(L)}</p>`, 's4')
      : '';
    const alShown = al;
    // Por instalação a API não dá nada (nunca sai um ID nem uma linha por instalação): o dia em somas por valor
    const rd = H.recentDays || [];
    const dayB = rd[0] && rd[0].users ? rd[0] : rd[1] || null;
    const isY = !(rd[0] && rd[0].users);
    const dayD = dayB ? H.days.find((x) => x.date === dayB.date) : null;
    const list4 = (title, rows, names, f) => `<div><h3>${title}</h3>${rows.length ? `<div class="kv2">${rows.map((x) => `<span>${esc(f ? f(x.k) : names ? names[x.k] || x.k : x.k)}</span><span>${N(x.n)}</span>`).join('')}</div>` : '<p class="note">nenhuma</p>'}</div>`;
    const dayBox = box(isY ? 'Instalações de ontem' : 'Instalações de hoje', dayB ? (isY ? `hoje ainda nenhuma · ontem ${N(dayB.users)}` : `${N(dayB.users)} até agora · ${N(dayB.fresh)} ${dayB.fresh === 1 ? 'nova' : 'novas'}`) : '', [['e', 'recentDays']],
      dayB && dayB.users ? `<div class="envg">${list4('Versão', dayB.versions, null, short)}${list4('País', dayB.countries, COUNTRY)}${list4('Ligação', dayB.links, LINKS)}${list4('Modo', dayB.modes, MODES)}</div>
        <p class="note">${N(dayB.minutes)} min na app e ${N(dayB.ip)} ${dayB.ip === 1 ? 'troca' : 'trocas'} de IP${dayD ? ` (${N(dayD.ipNew)} com IP novo, ${N(dayD.ipSame)} com o mesmo, ${N(dayD.ipNone)} sem rede), ${N(dayD.matches)} ${dayD.matches === 1 ? 'partida' : 'partidas'} e ${N(dayD.crashes)} ${dayD.crashes === 1 ? 'crash' : 'crashes'}` : ''}. Uma linha por instalação ${NOT}: a API só devolve somas, nunca um ID.</p>`
        : empty('Nenhuma instalação hoje nem ontem.', `Último relatório ${whenTxt(Fr.lastReal)}.`), hourSmall ? (liveBox ? 's4' : 's8') : (liveBox ? 's8' : 's12'));
    return `<div class="ptitle"><h1>Hoje</h1><p>${wday(now.date)}, ${dd(now.date)} · ${now.hm} (Lisboa)</p></div>
    <div class="g">
      <div class="s12 freshband ${Fr.tone}">
        <div class="fh"><div class="fv ${bad ? 'bad' : warn ? 'warn' : 'good'}">${head}</div>
          <div class="fd">${bad ? `Último fora dos teus PCs ${whenTxt(Fr.lastReal)}. Vermelho acima de 12 h, amarelo acima de 3 h.` : `${L ? N(L.now) : 'sem resposta'} a usar agora · orçamento da D1 a ${Fr.pct}%.`}</div></div>
        <div><div class="fl">Qualquer PC</div><div class="fv2">${agoT(Fr.lastAny)}</div><div class="fd">${whenTxt(Fr.lastAny)}</div></div>
        <div><div class="fl">Ao vivo</div><div class="fv2">${agoT(Fr.live)}</div><div class="fd">${L ? whenTxt(Fr.live) : 'sem resposta do /api/live'}</div></div>
        <div><div class="fl">Orçamento D1</div><div class="fv2 ${Fr.pct >= 70 ? 'warn' : ''}">${Fr.pct}%</div><div class="fd">${N(Fr.budget.rows)} de ${N(Fr.budget.cap)} linhas</div></div>
        <div class="fsrc src-inline">${src('e', 'fresh.lastTs')} ${src('e', 'fresh.lastAny')} ${src('e', 'live.lastTs')} ${src('e', 'fresh.budget')}</div>
      </div>
      ${box(early ? 'Ontem, dia inteiro' : `Hoje até às ${now.hm}`, early ? `${dd(addDays(H.asOf, -1))}, contra anteontem · hoje ainda vai às ${now.hm}` : 'contra ontem à mesma hora', early ? [['e', 'days']] : [['e', 'days'], ['e', 'sameTime']], `<div class="kstrip">
        ${k('Instalações', 'users')}
        ${k('Novas', 'fresh')}
        ${k('Voltaram', 'back')}
        <div class="kc"><div class="kl">A usar agora</div><div class="kv">${L ? N(L.now) : 'sem dados'}</div><div class="kd">pico de ontem <b>${L && L.peaks && L.peaks.ydayMax ? `${N(L.peaks.ydayMax.n)} às ${L.peaks.ydayMax.hour}h` : 'sem amostras'}</b></div><div class="kd">${L && L.now ? `em jogo <b>${N(L.inGame)}</b> · Protected <b>${N(L.protectedN)}</b>` : `último sinal <b>${agoT(Fr.live)}</b>`}</div></div>
        ${k('Minutos na app', 'minutes', { fmt: mins })}
        ${k('Trocas de IP', 'ip')}
      </div><p class="note">Barras: 14 dias até ontem. O traço no fundo é um dia a zero. ${early ? '' : 'Ontem à mesma hora: os relatórios enviados até esta hora; as trocas não têm hora (contadas uma vez por dia).'}</p>`, 's12')}
      ${hourBox}
      ${box('Pede atenção', alShown.length ? `${alShown.length} ${alShown.length === 1 ? 'regra' : 'regras'} com n mínimo` : '', [], alShown.length ? `<div class="al">${alShown.map((x) => `<div class="${x.tone}"><b>${esc(x.t)}</b><p>${x.d}</p><span class="src-inline">${src(x.src[0], x.src[1])}</span></div>`).join('')}</div>` : empty('Nada a pedir atenção.'), 's4')}
      ${dayBox}
      ${liveBox}
    </div>`;
  }

  // ------------------------------------------------ Funil
  function pFunil(D) {
    const R = D.R, nd = R.installs.nextDay, d7 = R.installs.day7 || { n: 0, back: 0 };
    const SI = R.site || { views: 0, downloads: 0, days: [] }, GP = ghPeriod(D);
    const st0 = [
      { k: 'Impressões no Google', n: null, src: ['x', 'Search Console'], none: `${NOT}: falta verificar o site no Search Console` },
      { k: 'Cliques no Google', n: null, src: ['x', 'Search Console'], none: 'vem com o Search Console, no mesmo passo' },
      { k: 'Visitas ao site', open: 'todas as origens', n: siteOk(D) ? SI.views : null, src: ['e', 'site.views (/v1/site)'], sub: 'sem cookies e sem ID; quem tem Do Not Track fica de fora', none: 'a tabela site ainda não existe na D1: o contador do site não foi publicado' },
      { k: 'Cliques em Download', n: siteOk(D) ? SI.downloads : null, src: ['e', 'site.downloads'], sub: 'o botão Download das páginas do site', none: 'vem com o contador do site' },
      { k: 'Downloads do exe', open: 'site e GitHub direto', n: GP.downloads, src: ['e', 'github.history (cron diário)'], sub: GP.covered ? `${GP.covered} de ${R.period} dias com retrato` : '', none: 'o cron diário ainda não tem dois dias seguidos de retrato' },
      { k: '1.º arranque (aceitou o aviso)', n: R.funnel[0].n, src: ['e', 'funnel[0] first_start'] },
      { k: 'Telemóvel ligado', n: R.funnel[1].n, src: ['e', 'funnel[1]'], sub: 'USB ou hotspot, ou o Phone setup acabou' },
      { k: '1.ª troca com IP novo', n: R.funnel[2].n, src: ['e', 'funnel[2]'] },
      { k: 'Protected', n: R.funnelProtected, src: ['e', 'funnelProtected'], sub: 'chegou a Protected no mesmo dia' },
      { k: 'Voltou no dia seguinte', n: nd.n ? nd.back : null, den: nd.n, src: ['e', 'installs.nextDay'], none: 'nenhuma nova com o dia seguinte já passado', sub: `das ${N(nd.n)} novas com o dia seguinte já passado` },
      { k: 'Voltou ao fim de 7 dias', n: d7.n ? d7.back : null, den: d7.n, src: ['e', 'installs.day7'], none: R.period < 30 ? 'precisa de um período de 30 ou 90 dias' : 'nenhuma nova com 7 dias passados', sub: `das ${N(d7.n)} novas com 7 dias passados` },
    ];
    let prevN = null, worst = null;
    const steps = st0.map((s, i) => {
      const den = s.den != null ? s.den : prevN;
      const p = s.open && s.n != null ? `<span class="dim" style="font-size:11px">${s.open}</span>` : s.n != null && den != null ? share(s.n, den) : null;
      if (s.n != null && den != null && den >= 10 && i >= 6 && i <= 8 && s.den == null) { const drop = 1 - s.n / den; if (!worst || drop > worst.drop) worst = { i, drop, k: s.k, from: st0[i - 1].k, a: den, b: s.n }; }
      if (s.n != null && s.den == null) prevN = s.n;
      return { ...s, p, den };
    });
    const top = Math.max(1, ...steps.map((s) => s.n || 0));
    const rows = steps.map((s, i) => {
      const na = s.n == null, ext = s.src[0] === 'x';
      const gapBefore = i === 5 ? '<div class="gap">site e GitHub acima · a app abaixo</div>' : '';
      return `${gapBefore}<div class="i">${i + 1}</div><div class="k">${esc(s.k)}${s.sub && !na ? `<small>${esc(s.sub)}</small>` : ''}</div>
        <div class="tb"><div class="t ${na ? 'na' : ''} ${ext ? 'ext' : ''}">${na ? '' : `<i style="width:${(100 * Math.log10(s.n + 1)) / Math.log10(top + 1)}%"></i>`}</div></div>
        <div class="n ${na ? 'na' : ''}">${na ? (i < 5 ? NOT : 'sem base') : N(s.n)}</div>
        <div class="p ${worst && worst.i === i ? 'worst' : ''}">${i === 5 || na ? '' : s.p == null ? '<span class="dim">sem base</span>' : s.p}</div>
        <div class="src-inline">${src(s.src[0], s.src[1])}</div>`;
    }).join('');
    const todo = [...steps.filter((s, i) => s.n == null && s.none && i < 5), { k: 'Por origem até ao 1.º arranque', none: 'De onde vêm as visitas está na Aquisição. Ligar cada origem à app pedia um ID no site e na app; por escolha, não se liga (sem cookies e sem ID).' }];
    const fs = steps[5].n;
    const days = (R.funnelDays || []).slice(-Math.min(R.period, 14));
    return `<div class="ptitle"><h1>Funil</h1><p>do Google até voltar · ${periodTxt(D)} · cada % é sobre o passo anterior</p></div>
    <div class="g">
      ${box('Do Google à volta', periodTxt(D), [], `<div class="fun"><div class="hd"></div><div class="hd">Passo</div><div class="hd tb">barra em escala log</div><div class="hd" style="text-align:right">Número</div><div class="hd" style="text-align:right">do anterior</div>${rows}</div>`, 's8')}
      <div class="s4 stack">
        ${box('Onde se perde mais', '', [], worst ? `<div class="bigline"><span class="big bad">${Math.round(worst.drop * 100)}%</span><span>ficam entre<br><b>${esc(worst.from)}</b> e <b>${esc(worst.k)}</b></span></div><p class="note">${N(worst.a)} → ${N(worst.b)} no período. Só conta do 1.º arranque ao Protected, com 10 ou mais casos no passo anterior; o voltar está nos passos 10 e 11.</p>`
          : `<div class="empty"><b>Ainda não dá para dizer.</b><span>${N(fs)} ${fs === 1 ? 'instalação nova' : 'instalações novas'} no período${fs ? `: cada uma vale ${Math.round(100 / fs)}% de qualquer passo` : ''}. Leio os números um a um até haver 10 por passo.</span></div>`)}
        ${box('O que falta medir', `${steps.filter((s, i) => s.n == null && s.none && i < 5).length} de 5 passos antes da app`, [], `<div class="al">${todo.map((s) => `<div><b>${esc(s.k)}</b><p>${esc(s.none)}</p></div>`).join('')}</div>`, 'grow')}
      </div>
      ${box('Por dia', 'site, GitHub e os passos da app', [['e', 'funnelDays'], ['e', 'site.days'], ['e', 'github.history']], `<div class="tw"><table class="t"><thead><tr><th>Dia</th><th class="n">Visitas ao site</th><th class="n">Cliques em Download</th><th class="n">Downloads do exe</th><th class="n">1.º arranque</th><th class="n">Telemóvel</th><th class="n">1.ª troca</th><th class="n">Protected</th><th class="n">Telemóvel / arranque</th></tr></thead><tbody>
        ${days.slice().reverse().map((d) => { const sd = (SI.days || []).find((x) => x.date === d.date); const gd = GP.byDay.get(d.date); return `<tr><td>${dd(d.date)}</td><td class="n">${siteOk(D) && sd ? N(sd.views) : '<span class="dim">-</span>'}</td><td class="n">${siteOk(D) && sd ? N(sd.downloads) : '<span class="dim">-</span>'}</td><td class="n">${gd == null ? '<span class="dim">-</span>' : N(gd)}</td><td class="n">${N(d.fs)}</td><td class="n">${N(d.ph)}</td><td class="n">${N(d.ip)}</td><td class="n">${N(d.prot)}</td><td class="n">${d.fs ? share(d.ph, d.fs) : '<span class="dim">0 novas</span>'}</td></tr>`; }).join('')}
        </tbody></table></div><p class="note">Downloads do exe por dia: a diferença entre dois retratos do GitHub às 03:07 UTC (traço: falta um dos dois). Inclui os teus, o VirusTotal e scanners. Visitas e cliques: dias em UTC.</p>`, 's12')}
    </div>`;
  }

  // ------------------------------------------------ Utilizadores
  function pUsers(D) {
    const R = D.R, I = R.installs, P = R.prev || {};
    const T = { users: R.days.reduce((s, d) => s + d.users, 0), fresh: R.days.reduce((s, d) => s + d.fresh, 0), minutes: R.days.reduce((s, d) => s + d.minutes, 0) };
    const k = (label, v, p, sub, fmt) => `<div class="kc"><div class="kl">${label}</div><div class="kv">${fmt ? fmt(v) : N(v)}${delta(v, p)}</div><div class="kd">${sub}</div></div>`;
    const back = Math.max(0, I.active - T.fresh);
    const src30 = R.period < 14 ? D.H : R;
    const days = src30.days.slice(-Math.max(R.period, 14)).map((d) => ({ date: d.date, fresh: d.fresh, back: Math.max(0, d.users - d.fresh) }));
    const rel = ghOk(D.G) && D.G.releases ? D.G.releases.filter((r) => !r.draft).map((r) => ({ date: r.date, label: short(r.tag.replace(/^v/, '')) })) : [];
    // na mesma data, só a versão mais recente (a lista do GitHub vem da mais nova para a mais antiga)
    const marks = rel.filter((m, i) => rel.findIndex((x) => x.date === m.date) === i);
    const daily = (R.cohortsDaily || []).filter((c) => c.n > 0);
    const useDaily = !daily.length || Math.max(...daily.map((c) => c.n)) < 50;
    const ch = useDaily ? daily.slice().reverse().map((c) => ({ k: dd(c.day), n: c.n, back: c.back }))
      : (R.cohortsExact || []).filter((c) => c.n > 0).slice().reverse().map((c) => ({ k: `sem. de ${dd(c.week)}`, n: c.n, back: c.back.slice(1, 6) }));
    const cell = (v, n) => (v == null ? '<td class="c na">ainda não</td>' : `<td class="c"><span style="background:rgba(var(--t-accent-rgb),${n ? Math.min(0.75, 0.08 + (0.9 * v) / n) : 0})">${n < 10 ? `${v} de ${n}` : `${Math.round((100 * v) / n)}%`}</span></td>`);
    const from = new Map((R.updates.from || []).map((x) => [x.v, x.n]));
    const env = R.env || {};
    const envRows = [['Windows', env.windows], ['Idioma', env.lang], ['Janela', env.window], ['Escala', env.scale], ['Rede do telemóvel', env.network], ['Marca do telemóvel', env.brands], ['Android', (env.android || []).map((x) => ({ k: /^\d+$/.test(x.k) ? `Android ${x.k}` : x.k, n: x.n }))]].filter(([, r]) => r && r.length);
    const tot = (r) => r.reduce((s, x) => s + x.n, 0);
    const act = (R.active || []).filter((a) => a.n > 0);
    return `<div class="ptitle"><h1>Utilizadores</h1><p>quantos, se voltam e de onde · ${periodTxt(D)}, contra os ${R.period} dias antes</p></div>
    <div class="g">
      ${box('Resumo', periodTxt(D), [['e', 'installs'], ['e', 'days'], ['e', 'prev']], `<div class="kstrip">
        ${k('Instalações diferentes', I.active, I.prev, `antes <b>${N(I.prev)}</b> · ID fixo, beta.9+`)}
        ${k('Novas', T.fresh, R.period > 1 ? P.fresh : null, `aceitaram o aviso · antes <b>${N(P.fresh)}</b>`)}
        ${k('Voltaram', back, null, 'já tinham a app antes')}
        ${k('Instalações-dia', T.users, R.period > 1 ? P.users : null, `média <b>${nf.format(Math.round((10 * T.users) / R.period) / 10)}</b> por dia do período`)}
        ${k('Minutos por instalação-dia', T.users ? Math.round(T.minutes / T.users) : null, P.users ? Math.round(P.minutes / P.users) : null, 'média, não mediana')}
        <div class="kc"><div class="kl">Voltam no dia seguinte</div><div class="kv${I.nextDay.n ? '' : ' na'}">${share(I.nextDay.back, I.nextDay.n) || 'sem base'}</div><div class="kd">das <b>${N(I.nextDay.n)}</b> novas com o dia seguinte passado</div></div>
      </div>`, 's12')}
      ${box('Por dia', `${days.length} dias · instalações em cada dia, novas e as que voltaram`, [['e', 'days']], dbars(days, [['fresh', 'novas', 'f'], ['back', 'voltaram', 'b']], marks, { todayPartial: true }) + `<div class="legendrow"><span><i style="background:var(--acc)"></i>novas</span><span><i style="background:#3c6a96"></i>voltaram</span><span>traço no fundo = 0 nesse dia</span><span>último dia: até às ${lx(Date.now()).hm}</span>${marks.length ? '<span>riscos: versões publicadas</span>' : ''}</div>`, 's12')}
      ${box(useDaily ? 'Voltam (por dia do 1.º ID)' : 'Voltam (por semana)', useDaily ? 'últimos 14 dias' : 'semana do 1.º dia do ID fixo', [['e', useDaily ? 'cohortsDaily' : 'cohortsExact']],
        ch.length ? `<div class="tw"><table class="t coh"><thead><tr><th>${useDaily ? '1.º dia' : 'Semana'}</th><th class="n">IDs</th><th>dia 1</th><th>dia 3</th><th>dia 7</th><th>dia 14</th>${useDaily ? '' : '<th>dia 30</th>'}</tr></thead><tbody>
        ${ch.map((c) => `<tr><td>${c.k}</td><td class="n">${N(c.n)}</td>${c.back.map((v) => cell(v, c.n)).join('')}</tr>`).join('')}</tbody></table></div><p class="note">Pelo 1.º dia do ID fixo (beta.9+): quem atualizou de uma beta antiga conta como novo nesse dia. Dia 1: 1 a 2 dias depois; dia 3: 3 a 6; dia 7: 7 a 13; dia 14: 14 a 29${useDaily ? '' : '; dia 30: 30 ou mais'}. Por dia enquanto há menos de 50 IDs novos num dia; depois por semana.</p>` : empty('Nenhum ID novo nos últimos 14 dias.'), 's6')}
      ${box('Países', 'instalações-dia', [['e', 'countries']], bl(R.countries.map((c) => [COUNTRY[c.k] || c.k, c.n]), { pct: true }), 's3')}
      ${box('Versões', 'instalações-dia do período', [['e', 'versions'], ['e', 'updates.from']], R.versions.length ? `<div class="tw"><table class="t"><thead><tr><th>Versão</th><th class="n">Inst.-dia</th><th class="n">Atualizaram desta</th></tr></thead><tbody>${R.versions.map((v) => `<tr><td>${esc(short(v.k))}</td><td class="n">${N(v.n)}${T.users >= 10 ? `<small class="dim"> ${Math.round((100 * v.n) / T.users)}%</small>` : ''}</td><td class="n">${from.get(short(v.k)) || from.get(v.k) ? N(from.get(short(v.k)) || from.get(v.k)) : ''}</td></tr>`).join('')}</tbody></table></div><p class="note">Cada dia conta na versão do relatório mais recente desse dia.</p>` : empty('Nada no período.'), 's3')}
      ${box('Dias com a app aberta', 'últimos 7 dias, por instalação', [['e', 'active']], act.length ? bl(act.map((a) => [`${a.band} ${a.band === '1' ? 'dia' : 'dias'}`, a.n]), { pct: true }) : empty('Ainda sem dados.', 'Conta a partir da beta.9 (act_1 a act_7).'), 's4')}
      ${box('Idade da instalação', 'dias desde o 1.º arranque, por instalação-dia', [['e', 'age']], R.age.length ? bl(R.age.map((a) => [a.band === '0' ? 'no dia em que instalou' : `${a.band} ${a.band === '1' ? 'dia' : 'dias'}`, a.n]), { pct: true }) : empty('Ainda sem dados.'), 's4')}
      ${sessBox(R)}
      ${box('Ambiente', `${N(env.reports || 0)} ${env.reports === 1 ? 'relatório' : 'relatórios'}, um por dia, sem ID`, [['e', 'env']], envRows.length ? `<div class="envg w4">${envRows.map(([t2, rows]) => `<div><h3>${t2}</h3><div class="kv2">${rows.slice(0, 5).map((x) => `<span>${esc(x.k)}</span><span>${N(x.n)}${tot(rows) >= 10 ? ` <small class="dim">${Math.round((100 * x.n) / tot(rows))}%</small>` : ''}</span>`).join('')}</div></div>`).join('')}</div><p class="note">Não segue os filtros. Da marca, nunca o modelo; a app só está em inglês.</p>` : empty('Nenhum relatório de ambiente no período.'), 's12')}
      ${box('Quando usam', 'dia da semana por hora de Lisboa', [['e', 'when']], whenMap(R.when || []), 's6')}
      ${box('Minutos por instalação-dia', `por dia · ${Math.max(R.period, 14)} dias`, [['e', 'days.minutes']], dbars(src30.days.slice(-Math.max(R.period, 14)).map((d) => ({ date: d.date, m: d.users ? Math.round(d.minutes / d.users) : 0 })), [['m', 'min por inst.-dia', 'one']], [], { todayPartial: true }) + '<p class="note">Média do dia: os minutos com a app aberta a dividir pelas instalações-dia. Traço no fundo: ninguém abriu.</p>', 's6')}
      ${liveHist(D)}
    </div>`;
  }

  // ------------------------------------------------ Na app
  function pApp(D) {
    const R = D.R;
    const { W, H, X0, COLS, ROWS } = UI;
    const cw = (W - X0) / COLS, chh = H / ROWS;
    const heatOf = (id) => (R.heat && R.heat[id]) || { c: [], d: [], r: [] };
    const totalOf = (id) => heatOf(id).c.reduce((s, x) => s + x[2], 0);
    const tabs = UI.screens.filter((s) => s.id === 'connection' || totalOf(s.id) || heatOf(s.id).d.length || heatOf(s.id).r.length);
    if (!tabs.some((s) => s.id === st.appTab)) st.appTab = 'connection';
    const S = UI.screens.find((s) => s.id === st.appTab);
    const h = heatOf(S.id), total = totalOf(S.id);
    // o botão de cada célula: o retângulo do ui-map.js que contém o centro da célula
    const nameAt = (c, r) => {
      const x = X0 + (c + 0.5) * cw, y = (r + 0.5) * chh;
      const b = S.buttons.find((q2) => q2.r && [q2.r, ...(q2.more || [])].some((rr) => x >= rr[0] - 4 && x <= rr[0] + rr[2] + 4 && y >= rr[1] - 4 && y <= rr[1] + rr[3] + 4));
      return b ? b.label : 'fora dos botões';
    };
    const dots = total < 30;
    const m = Math.max(1, ...h.c.map((x) => x[2]));
    const pos = (c, r) => ({ x: ((X0 + c * cw) / W) * 100, y: ((r * chh) / H) * 100, w: (cw / W) * 100, hh: (chh / H) * 100 });
    const cells = h.c.map(([c, r, n]) => {
      const p = pos(c, r), name = nameAt(c, r);
      if (dots) return `<div class="cell" style="left:${p.x}%;top:${p.y}%;width:${p.w}%;height:${p.hh}%" data-tip="${N(n)} ${n === 1 ? 'clique' : 'cliques'} · ${esc(name)}"><span class="dot">${n}</span></div>`;
      const a = 0.25 + (0.7 * n) / m;
      return `<div class="hot" style="left:${p.x - p.w * 0.6}%;top:${p.y - p.hh * 0.6}%;width:${p.w * 2.2}%;height:${p.hh * 2.2}%;background:radial-gradient(circle, rgba(255,241,184,${a}) 0, rgba(240,90,79,${a * 0.6}) 45%, transparent 70%)"></div><div class="cell" style="left:${p.x}%;top:${p.y}%;width:${p.w}%;height:${p.hh}%" data-tip="${N(n)} cliques · ${esc(name)}"></div>`;
    }).join('');
    const rings = [...h.d.map((x) => [...x, 'd']), ...h.r.map((x) => [...x, 'r'])].map(([c, r, n, kind]) => {
      const p = pos(c, r);
      return `<div class="ring ${kind}" style="left:${p.x}%;top:${p.y}%;width:${p.w}%;height:${p.hh}%" data-tip="${N(n)} ${kind === 'd' ? (n === 1 ? 'clique sem efeito' : 'cliques sem efeito') : (n === 1 ? 'clique repetido' : 'cliques repetidos')} · ${esc(nameAt(c, r))}"></div>`;
    }).join('');
    const dN = h.d.reduce((s, x) => s + x[2], 0), rN = h.r.reduce((s, x) => s + x[2], 0);
    const map = `<div class="map" style="background-image:url(ui/${S.id}.png)"><div class="dim"></div>${cells}${rings}${total || dN || rN ? '' : '<div class="mapmsg">Sem cliques neste ecrã no período.</div>'}</div>`;
    const btn = (id) => R.buttons[id + '_Click'] || R.buttons[id] || null;
    const UD = R.days.reduce((s2, d) => s2 + d.users, 0);
    const btns = S.buttons.map((b) => [b.label, btn(b.id)]).filter(([, r]) => r && r.n).map(([k2, r]) => [k2, r.n, r.days]).sort((a, b) => b[1] - a[1]);
    const rare = UD >= 30 ? S.buttons.filter((b) => !NOT_RARE.has(b.id) && ((btn(b.id) || { days: 0 }).days / UD) <= 0.03).map((b) => b.label) : [];
    const nb = (n) => (R.buttons[n] || { n: 0 }).n;
    const shortcuts = [['Change IP and rejoin', nb('Hotkey_Rejoin'), nb('Rejoin_Click')], ['New IP + next account', nb('Hotkey_Switch'), nb('Quick_Click')]];
    const fsTot = R.firstScreen.reduce((a, x) => a + x.n, 0);
    const scr = Object.entries(R.screens || {}).filter(([, v]) => v.minutes > 0).map(([id, v]) => [scrName(id), v.minutes]).sort((a, b) => b[1] - a[1]);
    const visible = scr.reduce((s, x) => s + x[1], 0);
    const appMin = R.days.reduce((s, d) => s + d.minutes, 0);
    const pathsMax = Math.max(1, ...R.paths.map((p) => p.n));
    const mapped = new Set(['Hotkey_Rejoin', 'Rejoin_Click', 'Hotkey_Switch', 'Quick_Click']);
    for (const s of UI.screens) for (const b of s.buttons) { mapped.add(b.id + '_Click'); mapped.add(b.id); }
    const others = Object.entries(R.buttons || {}).filter(([k2]) => !mapped.has(k2)).map(([k2, b]) => [k2, b.n]).sort((a, b) => b[1] - a[1]).slice(0, 12);
    return `<div class="ptitle"><h1>Na app</h1><p>o que clicam e por onde andam · ${periodTxt(D)}</p></div>
    <div class="g">
      ${box('Onde clicam', `${esc(S.name)} · ${N(total)} ${total === 1 ? 'clique' : 'cliques'}`, [['e', `heat.${S.id}`]], `<div class="tabs">${tabs.map((s) => `<button data-tab="${s.id}" aria-pressed="${s.id === S.id}">${esc(s.name)}<span class="c">${N(totalOf(s.id))}</span></button>`).join('')}</div>${map}
        <p class="note">${dots ? 'Menos de 30 cliques neste ecrã: o número em cada célula, sem mancha de calor.' : 'Mapa de calor: 30 ou mais cliques neste ecrã. Passa o rato para o número exato.'}${dN || rN ? ` Círculos: ${N(dN)} sem efeito (tracejado) e ${N(rN)} repetidos (cheio).` : ''} Grelha de 24 × 16 sobre a captura da app; só segue o filtro de versão.</p>`, 's8')}
      <div class="s4 stack">${box(`Botões do ${esc(S.name)}`, 'vezes · instalações-dia', [['e', 'buttons']], bl(btns, { extra: (u) => `${N(u)} inst.-dia${UD >= 10 ? ` · ${Math.round((100 * u) / UD)}%` : ''}`, none: 'Nenhum botão deste ecrã clicado no período.' }) + (rare.length ? `<h3>Quase não se usa <small class="dim">3% das instalações-dia ou menos</small></h3><div class="chips">${rare.map((x) => `<span>${esc(x)}</span>`).join('')}</div>` : UD < 30 ? `<p class="note">Poucos dados para dizer o que não se usa (${N(UD)} instalações-dia; a partir de 30).</p>` : '') + `<h3>Atalho contra rato</h3><div class="tw"><table class="t"><thead><tr><th>Ação</th><th class="n">Atalho</th><th class="n">Rato</th></tr></thead><tbody>${shortcuts.map(([a2, kb, ms]) => `<tr><td>${a2}</td><td class="n">${N(kb)}</td><td class="n">${N(ms)}</td></tr>`).join('')}</tbody></table></div>`)}
        ${box('1.º ecrã', 'o primeiro que abrem no 1.º arranque', [['e', 'firstScreen']], fsTot ? bl(R.firstScreen.map((x) => [scrName(x.screen), x.n]), { pct: true }) + `<p class="note">Das ${N(fsTot)} instalações novas com 1.º ecrã registado, ${share(R.firstScreen[0].n, fsTot)} começaram no ${esc(scrName(R.firstScreen[0].screen))}.</p>` : empty('Nenhuma instalação nova com 1.º ecrã no período.'), 'grow')}</div>
      ${thisScreen(D, S, total, dN, rN, UD)}
      ${box('Até onde fazem scroll', `${esc(S.name)} · % das visitas que passam de cada quarto`, [['e', `scroll.${S.id}`]], scrollBox(R, S), 's4')}
      ${box('Tempo por ecrã', 'minutos com a janela à vista', [['e', 'screens.minutes']], bl(scr, { fmt: mins, pct: true, none: 'Ainda sem minutos por ecrã.' }) + `<p class="note">${mins(visible)} à vista de ${mins(appMin)} com a app aberta; o resto é bandeja ou janela minimizada.</p>`, 's3')}
      ${box('Caminhos', 'de → para', [['e', 'paths']], R.paths.length ? `<div class="tw"><table class="t"><thead><tr><th>De</th><th>Para</th><th class="n">Vezes</th></tr></thead><tbody>${R.paths.slice(0, 12).map((p) => `<tr><td>${esc(scrName(p.from))}</td><td>${esc(scrName(p.to))}</td><td class="n"><span class="bar" style="width:${Math.max(2, (40 * p.n) / pathsMax)}px"></span>${N(p.n)}</td></tr>`).join('')}</tbody></table></div>` : empty('Nenhum caminho no período.'), 's5')}
      ${box('Cliques fora do mapa', 'navegação, diálogos, bandeja', [['e', 'buttons (sem ui-map)']], bl(others, { none: 'Nenhum clique fora do mapa no período.' }) + '<p class="note">Nomes do código da app, sem lugar nas capturas.</p>', 's4')}
      ${box('Definições mudadas', 'instalações-dia que mudaram cada uma', [['e', 'settings']], R.settings.length ? `<div class="chips">${R.settings.map((x) => `<span>${esc(x.name)} <b>${N(x.n)}</b>${UD >= 10 ? ` <small class="dim">${Math.round((100 * x.n) / UD)}%</small>` : ''}</span>`).join('')}</div><p class="note">Só sim/não e o modo, pelo nome; nunca texto escrito. % das ${N(UD)} instalações-dia do período.</p>` : empty('Nenhuma definição mudada no período.'), 's12')}
    </div>`;
  }

  // ------------------------------------------------ Qualidade
  function pQual(D) {
    const R = D.R;
    const T = R.days.reduce((a, d) => { for (const k2 of ['ipNew', 'ipSame', 'ipNone', 'sessions', 'crashes']) a[k2] += d[k2]; return a; }, { ipNew: 0, ipSame: 0, ipNone: 0, sessions: 0, crashes: 0 });
    const ipAll = T.ipNew + T.ipSame + T.ipNone;
    const cf = T.sessions >= 10 ? `${nf.format(Math.round(1000 - (1000 * T.crashes) / T.sessions) / 10)}%` : null;
    const k = (label, v, sub, tone = '') => `<div class="kc"><div class="kl">${label}</div><div class="kv ${tone}">${v}</div><div class="kd">${sub}</div></div>`;
    const red = R.checks.red.reduce((s, c) => s + c.n, 0);
    const since = R.days[0].date;
    const errs = R.errors.map((e) => ({ ...e, fresh: !!(e.first && e.first.day >= since) }));
    const errTot = errs.reduce((s, e) => s + e.n, 0);
    const series = (D.R.period < 14 ? D.H : R).days.slice(-14);
    const trend = (perDay) => series.map((d) => (perDay && perDay[d.date]) || 0);
    const cs = R.cs2, lagRows = cs.matches ? ['usb', 'hotspot'].map((lk) => { const x = cs.lag[lk]; const n = x.valve.n + x.community.n, l = x.valve.lag + x.community.lag; return [LINKS[lk], n, l]; }).filter((x) => x[1] > 0) : [];
    const dur = R.phone.duration.map((b) => [b.band, (b.usb || 0) + (b.hotspot || 0)]).filter((x) => x[1] > 0);
    const steps = R.setup.filter((x) => x.n && x.key !== 'done').map((x) => [x.k, x.n]);
    const done = (R.setup.find((x) => x.key === 'done') || { n: 0 }).n;
    const unclean = R.sessions.unclean;
    const base = (R.period < 14 ? D.H : R).days, idx = [...Array(Math.min(14, base.length))].map((_, i) => base.length - Math.min(14, base.length) + i);
    const rate7 = idx.map((i) => { const w = base.slice(Math.max(0, i - 6), i + 1); const ss = w.reduce((a, d) => a + d.sessions, 0), cc = w.reduce((a, d) => a + d.crashes, 0); return ss >= 10 && cc ? nf.format(Math.round((1000 * cc) / ss) / 10) : null; });
    const rate7d = idx.map((i) => base[i].date);
    const U = R.updates;
    const others = Object.entries(R.counters || {}).filter(([c]) => !SHOWN.test(c)).sort((a, b) => b[1] - a[1]);
    const sizes = (R.sizes || []).filter((z) => z.bands.length);
    const sug = improve(D);
    return `<div class="ptitle"><h1>Qualidade</h1><p>o que falha, no telemóvel, na troca e no CS2 · ${periodTxt(D)}</p></div>
    <div class="g">
      ${box('Resumo', periodTxt(D), [['e', 'days'], ['e', 'errors'], ['e', 'checks'], ['e', 'cs2']], `<div class="kstrip">
        ${k('Sessões sem crash', cf || `${N(T.crashes)} em ${N(T.sessions)}`, cf ? `${N(T.crashes)} ${T.crashes === 1 ? 'crash' : 'crashes'} em ${N(T.sessions)} sessões` : 'menos de 10 sessões: em número')}
        ${k('Erros', N(errTot), `${errs.length} ${errs.length === 1 ? 'tipo' : 'tipos'} · ${errs.filter((e) => e.fresh).length} novos no período`, errs.some((e) => e.fresh) ? 'bad' : '')}
        ${k('Checks a vermelho', N(red), 'instalações-dia, somando os Checks', red ? 'warn' : '')}
        ${k('Saídas sem fecho', N(unclean), 'unclean_exit', T.sessions && unclean > T.sessions * 0.05 ? 'warn' : '')}
        ${k('Trocas com IP novo', share(T.ipNew, ipAll) || 'sem trocas', `${N(T.ipNew)} de ${N(ipAll)} trocas`)}
        ${k('Partidas com lag', cs.matches ? share(cs.laggy, cs.matches) : 'sem dados', cs.matches ? `${N(cs.laggy)} de ${N(cs.matches)} partidas` : '0 partidas registadas')}
      </div>`, 's12')}
      ${box('O que melhorar', sug.length ? `${sug.length} ${sug.length === 1 ? 'regra disparou' : 'regras dispararam'} · ${periodTxt(D)}` : periodTxt(D), [['e', 'regras sobre o resumo']], sug.length ? `<div class="al">${sug.map((x) => `<div class="${x.tone}"><b>${esc(x.t)}</b><p>${esc(x.d)}</p><p class="dim">Regra: ${esc(x.rule)} · <a href="${x.href}">ver</a></p></div>`).join('')}</div>` : empty('Nenhuma regra disparou no período.', 'Cada regra tem o seu n mínimo; com poucos dados ficam caladas.'), 's12')}
      ${box('Erros', 'por tipo e sítio no código', [['e', 'errors[]'], ['e', 'errors[].first']], errs.length ? `<div class="tw"><table class="t"><thead><tr><th>Erro</th><th class="hm">Onde</th><th>Versão</th><th class="n">Inst.-dia</th><th class="n">Vezes</th><th>14 dias</th></tr></thead><tbody>
        ${errs.map((e) => { const tr = trend(e.perDay); return `<tr><td>${esc(e.type)}${e.fresh ? `<span class="newtag" title="1.º visto a ${dd(e.first.day)} na ${esc(short(e.first.app))}">novo</span>` : ''}</td><td class="mono hm">${esc(e.at)}</td><td>${esc(short(e.app))}</td><td class="n">${N(e.days)}</td><td class="n">${N(e.n)}</td><td>${isSmall(tr) ? nums(tr, '14 dias, um número por dia') : spark(tr, 84, 18, e.fresh ? 'bad' : 'acc')}</td></tr>`; }).join('')}</tbody></table></div><p class="note">Novo: visto pela 1.ª vez neste período (procura nos últimos 90 dias).</p>` : empty('Nenhum erro no período.'), 's8')}
      ${box('Crashes', `por dia, ${series.length} dias`, [['e', 'days.crashes'], ['e', 'crashPlaces']], `${isSmall(series.map((d) => d.crashes)) ? nums(series.map((d) => d.crashes)) : mini(series.map((d) => d.crashes))}<div class="xax dim" style="display:flex;justify-content:space-between;font-size:10.5px;margin-top:2px"><span>${dshort(series[0].date)}</span><span>${dshort(series.at(-1).date)}</span></div><h3>Onde</h3>${R.crashPlaces.length ? `<div class="tw"><table class="t"><tbody>${R.crashPlaces.map((c) => `<tr><td class="mono">${esc(c.at)}</td><td>${esc(short(c.app))}</td><td class="n">${N(c.n)}</td></tr>`).join('')}</tbody></table></div>` : '<p class="note">Nenhum crash no período.</p>'}`, 's4')}
      ${box('Trocas de IP', `${N(ipAll)} ${ipAll === 1 ? 'troca' : 'trocas'}`, [['e', 'days.ip*'], ['e', 'phone.duration']], `${ipAll ? bl([['IP novo', T.ipNew, null, 'good'], ['Ficou o mesmo IP', T.ipSame, null, 'warn'], ['Sem rede', T.ipNone, null, 'bad']], { pct: true }) : empty('Nenhuma troca no período.')}<h3>Quanto demora</h3>${bl(dur, { pct: true, none: 'Sem tempos de troca no período.' })}`, 's4')}
      ${box('Phone setup', 'o passo em que estava ao abrir', [['e', 'setup[]'], ['e', 'setupOpened']], R.setupOpened ? bl([['Abriram o Phone setup', R.setupOpened], ...steps, ['Pronto', done, null, 'good']]) + '<p class="note">Cada passo conta as instalações-dia em que o painel abriu nesse passo. Não é onde desistiram: isso pede um contador novo na app (phone_left_).</p>' : empty('Ninguém abriu o Phone setup no período.'), 's4')}
      <div class="s4 stack">${box('Checks a vermelho', 'instalações-dia', [['e', 'checks.red']], bl(R.checks.red.map((c) => [c.k, c.n]), { tone: 'warn', none: 'Nenhum Check a vermelho no período.' }) + `<p class="note">Fuga de IP de casa: ${N(R.checks.leak.fail)} ${R.checks.leak.fail === 1 ? 'falhado' : 'falhados'} em ${N(R.checks.leak.pass + R.checks.leak.fail)} ${R.checks.leak.pass + R.checks.leak.fail === 1 ? 'teste' : 'testes'} (o botão Run leak test). O IP de casa voltou: <b class="${(R.counters.ip_home_back || 0) ? 'bad' : ''}">${N(R.counters.ip_home_back || 0)}</b> ${(R.counters.ip_home_back || 0) === 1 ? 'vez' : 'vezes'}. Erros escritos no Log: ${N(R.counters.log_error || 0)}.</p>`)}
      ${lagRows.length ? box('Lag por ligação', '% das partidas com lag', [['e', 'cs2.lag']], bl(lagRows.map(([k2, n, l]) => { const p = n ? l / n : 0; return [k2, Math.round(100 * p), [l, n], p > 0.3 ? 'bad' : p > 0.2 ? 'warn' : 'good']; }), { max: 100, fmt: (v) => `${v}%`, extra: (x) => `${N(x[0])} de ${N(x[1])}` }), 'grow')
        : box('Ainda sem dados', '', [], `<div class="empty"><b>Partidas ${N(cs.matches)} · regiões bloqueadas ${N((cs.regions || []).length)}</b><span>As partidas só chegam com sessões longas reportadas e com a ligação USB ou hotspot no relatório. Esta caixa passa a lag por ligação quando houver partidas.</span></div>`, 'dashed grow')}</div>
      ${box('Crashes por 100 sessões', 'janela de 7 dias, um número por dia', [['e', 'days.crashes / days.sessions']], `${nums(rate7.map((v) => (v == null ? 0 : v)), 'crashes por 100 sessões nos 7 dias até esse dia')}<div class="xax dim" style="display:flex;justify-content:space-between;font-size:10.5px;margin-top:2px"><span>${dshort(rate7d[0])}</span><span>${dshort(rate7d.at(-1))}</span></div><p class="note">Ponto: menos de 10 sessões nesses 7 dias, ou nenhum crash.</p>`, 's4')}
      ${box('Arranque até a app estar pronta', 'sessões', [['e', 'sessions.startup']], bandBl(R.sessions.startup, 'Sem arranques medidos no período.'), 's4')}
      ${box('Atualizações', 'o aviso de atualização da app', [['e', 'updates'], ['e', 'updates.from']], `${bl([['Viram o aviso', U.seen], ['Aceitaram', U.accepted], ['Concluíram', U.done, null, 'good'], ['Recusaram', U.declined, null, 'warn'], ['Falharam', U.failed, null, 'bad']])}<h3>Vieram de</h3>${(U.from || []).length ? bl(U.from.map((x) => [x.v, x.n, null, 'dim'])) : '<p class="note">Nenhuma atualização concluída no período.</p>'}`, 's4')}
      ${box('Outros contadores', 'sem bloco próprio', [['e', 'counters']], others.length ? `<div class="tw"><table class="t"><tbody>${others.map(([code, n]) => `<tr><td>${esc(CNAMES[code] || code)}${CNAMES[code] ? `<div class="note mono" style="margin:0">${esc(code)}</div>` : ''}</td><td class="n">${N(n)}</td></tr>`).join('')}</tbody></table></div>` : empty('Nada no período.'), 's4')}
      ${box('Tamanho das listas', 'instalações-dia em cada banda', [['e', 'sizes']], sizes.length ? `<div class="tw"><table class="t"><tbody>${sizes.map((z) => `<tr><td>${esc(LISTN[z.name] || z.name)}<div class="note" style="margin:2px 0 0">${z.bands.map((b) => `${esc(b.band)}: ${N(b.n)}`).join(' · ')}</div></td></tr>`).join('')}</tbody></table></div><p class="note">O tamanho vai em bandas (0, 1, 2 a 5, 6 a 20…), nunca o número exato.</p>` : empty('Nada no período.'), 's4')}
      ${box('Versão do Windows', 'do relatório com ID', [['e', 'oses']], (R.oses || []).length ? bl(R.oses.map((o) => [o.k, o.n]), { pct: true }) : empty('Nada no período.'), 's4')}
    </div>`;
  }

  // ------------------------------------------------ Aquisição
  function pAcq(D) {
    const G = D.G, ok = ghOk(G), R = D.R, SI = R.site || { views: 0, downloads: 0, days: [], pages: [], refs: [], countries: [] }, GP = ghPeriod(D), sOk = siteOk(D);
    const rel = ok && G.releases ? G.releases.filter((r) => !r.draft) : null;
    const exeTot = rel ? rel.reduce((s, r) => s + r.exe, 0) : null;
    const k = (label, v, sub, na) => `<div class="kc${na ? ' none' : ''}"><div class="kl">${label}</div><div class="kv${na ? ' na' : ''}">${v}</div><div class="kd">${sub}</div></div>`;
    const views = ok ? G.views : null;
    const problems = (G && G.problems) || [];
    const fs = R.funnel[0].n;
    const pub = G && G.source && G.source !== 'token';
    const missing = [];
    if (!views) missing.push(['Visitas ao repositório, clones e de onde vêm', ['x', 'GitHub traffic (views, clones, popular)'], `Precisa de token (${esc((G && G.traffic) || 'o GitHub não respondeu')}): o GitHub só dá o tráfego a quem tem acesso ao repositório. Estrelas e downloads não precisam, vêm da API pública. O GitHub só guarda 14 dias.`]);
    if (!sOk) missing.push(['Visitas ao site', ['e', '/v1/site'], 'A tabela site ainda não existe na D1: o contador chega quando o Worker stats e o site forem publicados. Não é zero.']);
    if (!GP.hist.length) missing.push(['Downloads e estrelas por dia', ['e', 'gh_daily (cron diário)'], 'Ainda sem retratos: o cron do stats-dashboard tira um por dia às 03:07 UTC. O 1.º número por dia aparece no dia a seguir ao 1.º retrato.']);
    missing.push(['Pesquisas no Google', ['x', 'Search Console API'], 'Precisa do teu login Google para verificar o site no Search Console (é a única fonte das impressões e dos cliques no Google).']);
    missing.push(['Vídeos', ['x', 'YouTube Studio'], 'Quando sair o 1.º: vistas e CTR aqui, e a marca do vídeo nas barras por dia.']);
    const out = [];
    if (!ok) out.push(`<div class="s12 errbox"><b>GitHub sem resposta</b><span>${problems.length ? esc(problems.slice(0, 3).join(' · ')) : 'O /api/github não respondeu'}. Nem com o token nem pela API pública: estrelas e downloads ficam sem dados, não são zeros. O resto da página não depende do GitHub.</span>${src('x', 'github.problems')}</div>`);
    else if (problems.length) out.push(`<div class="s12 errbox"><b>GitHub em parte</b><span>${esc(problems.slice(0, 3).join(' · '))}. O que respondeu está abaixo${pub ? ' (estrelas e downloads pela API pública)' : ''}.</span>${src('x', 'github.problems')}</div>`);
    out.push(box('Resumo', `${periodTxt(D)} no site e nos retratos; o GitHub em total de sempre`, [['e', 'site'], ['e', 'github.history'], ['x', 'GitHub']], `<div class="kstrip k4">
        ${k('Estrelas', ok ? N(G.stars) : 'sem dados', ok ? `${GP.hist.length ? `${GP.stars >= 0 ? '+' : ''}${N(GP.stars)} no período · ` : ''}${N(G.forks)} forks · ${N(G.watchers)} a seguir` : 'GitHub sem resposta', !ok)}
        ${k('Downloads do exe', exeTot != null ? N(exeTot) : 'sem dados', exeTot != null ? `total de sempre, ${rel.length} versões` : 'GitHub sem resposta', exeTot == null)}
        ${k('Downloads no período', GP.downloads != null ? N(GP.downloads) : 'sem dados', GP.downloads != null ? `${GP.covered} de ${R.period} dias com retrato` : 'o cron diário ainda não tem 2 dias seguidos', GP.downloads == null)}
        ${k('Visitas ao site', sOk ? N(SI.views) : 'sem dados', sOk ? `antes ${N(SI.prevViews)} · sem cookies e sem ID` : 'o contador não está na D1', !sOk)}
        ${k('Cliques em Download', sOk ? N(SI.downloads) : 'sem dados', sOk ? (SI.views ? `${share(SI.downloads, SI.views)} das visitas` : 'sem visitas no período') : 'vem com o contador do site', !sOk)}
        ${k('1.º arranque por download', GP.downloads ? share(fs, GP.downloads) : 'sem base', GP.downloads ? `${N(fs)} novas · ${N(GP.downloads)} downloads` : 'precisa dos downloads por dia', !GP.downloads)}
        ${k('Visitas ao repositório', views ? N(views.count) : 'precisa de token', views ? `${N(views.uniques)} únicos · 14 dias` : 'o tráfego do GitHub só com token', !views)}
        ${k('Issues abertas', ok ? N(G.issues) : 'sem dados', 'no GitHub agora', !ok)}
      </div>`, 's12'));
    const conv = [['Visitas ao site', sOk ? SI.views : null], ['Cliques em Download', sOk ? SI.downloads : null], ['Downloads do exe', GP.downloads], ['1.º arranque', fs]];
    out.push(box('Do site à app', 'cada % é sobre o passo anterior', [['e', 'site'], ['e', 'github.history'], ['e', 'funnel[0]']], `<div class="tw"><table class="t"><tbody>${conv.map(([l, v], i) => { const p = i && conv[i - 1][1] != null && v != null ? share(v, conv[i - 1][1]) : null; return `<tr><td>${l}</td><td class="n">${v == null ? '<span class="dim">sem dados</span>' : N(v)}</td><td class="n">${p || '<span class="dim">-</span>'}</td></tr>`; }).join('')}</tbody></table></div>
      <p class="note">Os downloads do exe também vêm do README e do GitHub direto, por isso podem ser mais do que os cliques no site. Nada liga uma visita a uma instalação: são totais do período.</p>`, 's4'));
    const sdays = (SI.days || []).slice(-Math.max(R.period, 1));
    out.push(sOk ? box('Visitas ao site por dia', `${N(SI.views)} visitas · ${N(SI.downloads)} cliques em Download`, [['e', 'site.days']], dbars(sdays.map((d) => ({ date: d.date, v: d.views })), [['v', 'visitas', 'f']], [], { todayPartial: true }) + '<p class="note">Uma visita é uma página aberta (sem cookies e sem ID, por isso a mesma pessoa em duas páginas conta 2). Quem tem Do Not Track ou Global Privacy Control fica de fora. Dias em UTC.</p>', 's8')
      : box('Visitas ao site por dia', '', [['e', 'site.days']], empty('Ainda sem o contador do site.', 'Chega quando o Worker stats e o site forem publicados.'), 's8 dashed'));
    if (sOk) {
      out.push(box('Páginas do site', 'visitas e cliques em Download', [['e', 'site.pages']], SI.pages.length ? `<div class="tw"><table class="t"><thead><tr><th>Página</th><th class="n">Visitas</th><th class="n">Download</th><th class="n">Taxa</th></tr></thead><tbody>${SI.pages.map((p) => `<tr><td class="mono">${esc(p.k)}</td><td class="n">${N(p.views)}</td><td class="n">${N(p.downloads)}</td><td class="n">${p.views ? share(p.downloads, p.views) : '<span class="dim">-</span>'}</td></tr>`).join('')}</tbody></table></div>` : empty('Nenhuma visita no período.'), 's6'));
      out.push(box('De onde vêm ao site', 'visitas por site de origem', [['e', 'site.refs']], bl(SI.refs.map((r) => [r.k || 'direto ou sem referrer', r.n]), { pct: true, none: 'Nenhuma visita no período.' }) + '<p class="note">Só o nome do site de origem, nunca a página. Muitos sites não dizem de onde vem a visita: fica em "direto".</p>', 's3'));
      out.push(box('Países do site', 'visitas', [['e', 'site.countries']], bl(SI.countries.map((c) => [COUNTRY[c.k] || c.k, c.n]), { pct: true, none: 'Nenhuma visita no período.' }), 's3'));
    }
    const hd = GP.hist.filter((x) => x.day >= addDays(R.asOf, -(Math.max(R.period, 14) - 1)));
    out.push(GP.hist.length ? box('Downloads do exe por dia', 'dos retratos diários do GitHub', [['e', 'github.history']], dbars(hd.map((x) => ({ date: x.day, e: x.exeDay || 0 })), [['e', 'downloads', 'f']]) + `<p class="note">A diferença entre dois retratos seguidos (às 03:07 UTC). Um dia sem o retrato de antes fica a 0 e não entra nos totais. Estrelas hoje: ${N(GP.hist.at(-1).stars)}; 1.º retrato a ${dd(GP.hist[0].day)}.</p>`, 's8')
      : box('Downloads do exe por dia', '', [['e', 'github.history']], empty('Ainda sem retratos diários.', 'O cron do stats-dashboard tira um por dia, sem token.'), 's8 dashed'));
    const exeBox = rel && rel.length ? box('Downloads por versão', 'total de sempre, só o exe', [['x', 'GitHub releases']], `<div class="tw"><table class="t"><thead><tr><th>Versão</th><th>Publicada</th><th class="n">Downloads</th></tr></thead><tbody>${rel.slice(0, 8).map((r) => `<tr><td>${esc(short(r.tag.replace(/^v/, '')))}</td><td>${r.date ? dd(r.date) : ''}</td><td class="n">${N(r.exe)}</td></tr>`).join('')}</tbody></table></div>${rel.length > 8 ? `<div class="more">mais ${rel.length - 8} versões · ${N(rel.slice(8).reduce((s, r) => s + r.exe, 0))} downloads</div>` : ''}<p class="note">Inclui os teus, o workflow do VirusTotal e scanners.${pub ? ' Pela API pública do GitHub.' : ''}</p>`, 's4')
      : box('Downloads por versão', '', [['x', 'GitHub releases']], empty('GitHub sem resposta.', 'Nem com o token nem pela API pública. Não são zeros.'), 's4 dashed');
    out.push(exeBox);
    if (views && views.days && views.days.length) out.push(box('Visitas ao repositório por dia', 'visitas e únicos, 14 dias', [['x', 'GitHub traffic/views']], dbars(views.days.map((d) => ({ date: d.day, u: d.u, r: Math.max(0, d.n - d.u) })), [['u', 'únicos', 'f'], ['r', 'repetidas', 'b']]), 's8'));
    if (ok && G.referrers && G.referrers.length) out.push(box('De onde vêm ao GitHub', 'visitas · únicos, 14 dias', [['x', 'GitHub traffic/popular']], bl(G.referrers.map((r) => [r.name, r.n, r.u]), { extra: (u) => `${N(u)} ún.` }), 's4'));
    if (ok && G.paths && G.paths.length) out.push(box('Páginas mais vistas no GitHub', 'visitas, 14 dias', [['x', 'GitHub traffic/popular']], bl(G.paths.map((p) => [p.name.replace(`/${G.repo}`, '') || '/', p.n])), 's4'));
    if (ok && G.clones) out.push(box('Clones', '14 dias', [['x', 'GitHub traffic/clones']], `<div class="bigline"><span class="big">${N(G.clones.count)}</span><span>${N(G.clones.uniques)} únicos</span></div>`, 's4'));
    out.push(box('Ainda sem medição', `${missing.length} ${missing.length === 1 ? 'fonte' : 'fontes'}`, [], `<div class="al">${missing.map(([t2, s2, d2]) => `<div><b>${t2}</b><p>${d2}</p><span class="src-inline">${src(s2[0], s2[1])}</span></div>`).join('')}</div>`, 's12 dashed'));
    return `<div class="ptitle"><h1>Aquisição</h1><p>de onde vêm · o site e os downloads por dia seguem o período (${periodTxt(D)}); o tráfego do GitHub é sempre de 14 dias</p></div>
    <div class="g">${out.join('')}</div>`;
  }

  // ------------------------------------------------ peças vindas da v2 (4 nov.)
  /** Sessões: quanto duram, como arrancam e quanto do tempo a janela está à vista. */
  function sessBox(R) {
    const S = R.sessions, st = S.auto + S.hand;
    const vis = Object.values(R.screens || {}).reduce((a, x) => a + x.minutes, 0), all = R.days.reduce((a, d) => a + d.minutes, 0);
    return box('Sessões', `${N(S.starts)} ${S.starts === 1 ? 'arranque' : 'arranques'} da app`, [['e', 'sessions']],
      `<h3>Quanto duram</h3>${bandBl(S.len, 'Sem sessões fechadas no período.')}
      <h3>Como arranca</h3>${st ? bl([['com o Windows', S.auto], ['à mão', S.hand]], { pct: true }) : empty('Sem arranques no período.')}
      <h3>Janela à vista</h3>${all ? bl([['à vista', Math.min(vis, all)], ['só na bandeja ou minimizada', Math.max(0, all - vis)]], { pct: true, fmt: mins }) : empty('Sem minutos no período.')}`, 's4');
  }
  /** O ao vivo ao longo do tempo: o pico de cada hora (das amostras do cron) hoje contra ontem e o pico de cada dia. */
  function liveHist(D) {
    const P = D.L && D.L.peaks;
    if (!P || P.first == null) return box('Ao vivo por hora', 'pico de cada hora', [['e', 'live.peaks (live_hourly)']], empty('Ainda sem amostras.', 'O cron do stats-dashboard conta o ao vivo de 10 em 10 min e guarda o máximo de cada hora, a partir de quando for publicado.'), 's12 dashed');
    const all = [...P.today, ...P.yday].map((v) => v || 0);
    const h = { today: P.today, yday: P.yday.map((v) => v || 0) };
    const days = P.days.map((x) => x.max);
    return box('Ao vivo por hora', 'pico de cada hora · hoje contra ontem', [['e', 'live.peaks (live_hourly)']],
      (isSmall(all) ? hstrip(h, P.hour, 'O máximo de instalações com a app aberta ao mesmo tempo em cada hora de Lisboa. Ponto: 0. Com 10 ou mais numa hora passa a gráfico de barras.') : hbars(h, P.hour)) + `<h3>Pico de cada dia <small class="dim">14 dias</small></h3>${nums(days.map((v) => v || 0), 'o máximo de pessoas com a app aberta ao mesmo tempo, em cada dia')}
      <div class="xax dim" style="display:flex;justify-content:space-between;font-size:10.5px;margin-top:2px"><span>${dshort(P.days[0].date)}</span><span>${dshort(P.days.at(-1).date)}</span></div>
      <p class="note">${peakTxt(D.L)} Em branco: hora sem amostra (o cron não correu), não é zero.</p>`, 's12');
  }
  /** Na app: os números do ecrã aberto no mapa. */
  function thisScreen(D, S, total, dN, rN, UD) {
    const R = D.R, sc = (R.screens || {})[S.id] || { minutes: 0, days: 0 };
    const allMin = Object.values(R.screens || {}).reduce((a, x) => a + x.minutes, 0);
    const visits = (R.paths || []).filter((p) => p.to === S.id).reduce((a, p) => a + p.n, 0);
    const k = (label, v, sub, tone = '') => `<div class="kc"><div class="kl">${label}</div><div class="kv ${tone}">${v}</div><div class="kd">${sub}</div></div>`;
    return box('Este ecrã', esc(S.name), [['e', 'screens'], ['e', 'paths'], ['e', `heat.${S.id}`]], `<div class="kstrip k5">
      ${k('Tempo à vista', allMin ? share(sc.minutes, allMin) || '0%' : '-', mins(sc.minutes))}
      ${k('Abrem', share(sc.days, UD) || '-', `das ${N(UD)} inst.-dia`)}
      ${k('Cliques', N(total), `${N(visits)} ${visits === 1 ? 'visita' : 'visitas'}`)}
      ${k('Sem efeito', N(dN), total ? `${share(dN, total + dN) || '0'} dos cliques` : 'sem cliques', dN ? 'warn' : '')}
      ${k('Repetidos', N(rN), '3 ou mais em 1 s', rN ? 'bad' : '')}
    </div>`, 's8');
  }
  function scrollBox(R, S) {
    const sc = R.scroll && R.scroll[S.id];
    if (!sc) return empty('Sem medição de scroll neste ecrã no período.', 'Só os ecrãs com scroll a mandam (a partir da beta.9).');
    return `${bl([['passa de 25%', sc.reach[0]], ['passa de 50%', sc.reach[1]], ['passa de 75%', sc.reach[2]]], { max: 100, fmt: (v) => (sc.visits >= 10 ? `${v}%` : `${Math.round((v * sc.visits) / 100)} de ${sc.visits}`) })}<p class="note">${N(sc.visits)} ${sc.visits === 1 ? 'visita' : 'visitas'} a este ecrã com scroll medido.</p>`;
  }

  /** Regras sobre os números (a "O que melhorar" da v2), cada uma com o número que a dispara e a sua regra. */
  function improve(D) {
    const R = D.R, out = [];
    const UD = R.days.reduce((a, d) => a + d.users, 0);
    const lk = R.checks.leak;
    if (lk.fail >= 1) out.push({ tone: 'bad', t: 'O teste de fugas falhou', d: `${N(lk.fail)} ${lk.fail === 1 ? 'falha' : 'falhas'} em ${N(lk.pass + lk.fail)} testes.`, rule: 'teste de fugas falhado: 1 ou mais', href: '#qualidade' });
    for (const e of R.errors.filter((x) => x.first && x.first.day >= R.days[0].date).slice(0, 3))
      out.push({ tone: 'bad', t: `Corrige o erro novo ${e.type}`, d: `Em ${e.at}, 1.º visto a ${dd(e.first.day)} na ${short(e.first.app)}: ${N(e.n)} ${e.n === 1 ? 'vez' : 'vezes'}.`, rule: 'erro visto pela 1.ª vez no período', href: '#qualidade' });
    // Cliques sem efeito (5 ou mais) e repetidos (3 ou mais) num botão do mapa
    for (const S of UI.screens) {
      const h = (R.heat || {})[S.id]; if (!h) continue;
      for (const [kind, min, label] of [['d', 5, 'Cliques sem efeito'], ['r', 3, 'Cliques repetidos']]) {
        const by = {};
        for (const [c, r, n] of h[kind] || []) { const name = btnAt(S, c, r) || 'uma área sem botão'; by[name] = (by[name] || 0) + n; }
        for (const [name, n] of Object.entries(by)) if (n >= min) out.push({ tone: 'warn', t: `${label} em ${name} (${S.name})`, d: `${N(n)} ${kind === 'd' ? 'cliques que não fizeram nada' : 'rajadas de cliques'} no período.`, rule: `${kind === 'd' ? 'cliques sem efeito' : 'cliques repetidos'} no mesmo sítio: ${min} ou mais`, href: '#app' });
      }
    }
    const L = R.cs2.lag, nU = L.usb.valve.n + L.usb.community.n, nH = L.hotspot.valve.n + L.hotspot.community.n;
    if (nU >= 30 && nH >= 30) {
      const lu = (L.usb.valve.lag + L.usb.community.lag) / nU, lh = (L.hotspot.valve.lag + L.hotspot.community.lag) / nH;
      if (lu > 0 && lh / lu >= 1.5) out.push({ tone: 'acc', t: 'Recomenda o cabo USB no Phone setup', d: `Partidas com lag: hotspot ${Math.round(100 * lh)}% (n=${N(nH)}), USB ${Math.round(100 * lu)}% (n=${N(nU)}).`, rule: 'lag do hotspot 1,5 vezes o do USB, com 30 partidas ou mais de cada', href: '#ligacao' });
    }
    const done = (R.setup.find((x) => x.key === 'done') || { n: 0 }).n;
    if (R.setupOpened >= 10 && done / R.setupOpened < 0.75) {
      const top = R.setup.filter((x) => x.key !== 'done' && x.n).sort((a, b) => b.n - a.n)[0];
      out.push({ tone: 'warn', t: 'Revê o Phone setup', d: `${N(R.setupOpened)} abriram, ${N(done)} ficaram prontos.${top ? ` O passo mais visto ao abrir: ${top.k} (${N(top.n)}).` : ''}`, rule: 'menos de 75% dos que abrem o Phone setup chegam a Pronto (10 ou mais)', href: '#qualidade' });
    }
    const fa = R.funnel[0].n, fb = R.funnel[1].n;
    if (fa >= 10 && 1 - fb / fa >= 0.2) out.push({ tone: 'warn', t: 'Ajuda a ligar o telemóvel no 1.º dia', d: `${N(fa)} instalações novas, ${N(fb)} com o telemóvel pronto no mesmo dia.`, rule: 'passo do 1.º dia que perde 20% ou mais (10 ou mais novas)', href: '#funil' });
    const cr = R.checks.red[0];
    if (cr && cr.n >= 5) out.push({ tone: 'warn', t: `Explica o que fazer com "${cr.k}" a vermelho`, d: `${N(cr.n)} instalações-dia com esta linha do painel Checks a vermelho.`, rule: 'Check a vermelho em 5 instalações-dia ou mais', href: '#qualidade' });
    const nb = (n) => ((R.buttons || {})[n] || { n: 0 }).n;
    const kb = nb('Hotkey_Rejoin'), ms = nb('Rejoin_Click');
    if (kb + ms >= 30 && kb / (kb + ms) >= 0.5) out.push({ tone: 'acc', t: 'Dá um atalho também ao Change IP', d: `O Change IP and rejoin faz-se ${Math.round((100 * kb) / (kb + ms))}% das vezes pelo atalho (n=${N(kb + ms)}).`, rule: 'ação feita 50% ou mais pelo atalho, 30 ou mais', href: '#app' });
    if (R.updates.declined >= 3) out.push({ tone: 'dim', t: 'Diz o que muda no aviso de atualização', d: `${N(R.updates.declined)} recusas e ${N(R.updates.failed)} falhas no período.`, rule: 'recusas de atualização: 3 ou mais', href: '#qualidade' });
    if (UD >= 30) {
      const rare = [];
      for (const S of UI.screens) for (const b of S.buttons) { const r = (R.buttons || {})[b.id + '_Click'] || (R.buttons || {})[b.id] || { days: 0 }; if (!NOT_RARE.has(b.id) && r.days / UD <= 0.02) rare.push(`${b.label} (${S.name})`); }
      if (rare.length) out.push({ tone: 'dim', t: 'Junta ou esconde o que quase não se usa', d: `${rare.length} ${rare.length === 1 ? 'botão usado' : 'botões usados'} em 2% das instalações-dia ou menos: ${rare.slice(0, 4).join(', ')}${rare.length > 4 ? ' e mais' : ''}.`, rule: 'botão em 2% das instalações-dia ou menos (30 ou mais); apagar e desfazer ficam de fora', href: '#app' });
    }
    const E = R.env || { lang: [], reports: 0 }, lang = (E.lang || [])[0];
    if (lang && E.reports >= 10 && lang.k !== 'en' && lang.n / E.reports >= 0.25) out.push({ tone: 'acc', t: `A 1.ª tradução da app devia ser ${lang.k}`, d: `${Math.round((100 * lang.n) / E.reports)}% dos relatórios de ambiente têm o Windows em ${lang.k} (n=${N(E.reports)}).`, rule: 'idioma do Windows com 25% ou mais que a app não tem (10 relatórios ou mais)', href: '#utilizadores' });
    const sc = R.scroll && R.scroll.connection;
    if (sc && sc.visits >= 30 && sc.reach[1] <= 30) out.push({ tone: 'acc', t: 'Faz o Connection caber na janela', d: `Só ${sc.reach[1]}% das visitas passam de metade do Connection (n=${N(sc.visits)}).`, rule: 'menos de 30% a passar de metade do ecrã principal, 30 visitas ou mais', href: '#app' });
    const SI = R.site;
    if (SI && SI.views >= 30 && SI.downloads / SI.views < 0.05) out.push({ tone: 'acc', t: 'O botão Download do site quase não é clicado', d: `${N(SI.downloads)} cliques em ${N(SI.views)} visitas (${Math.round((100 * SI.downloads) / SI.views)}%).`, rule: 'menos de 5% das visitas clicam em Download, 30 visitas ou mais', href: '#aquisicao' });
    const rj = R.cs2.rejoin;
    if (rj.tries >= 30 && rj.back / rj.tries >= 0.7) out.push({ tone: 'ok', t: 'Rejoin: a maioria volta a entrar', d: `${N(rj.back)} de ${N(rj.tries)} tentativas.`, rule: 'Rejoin com 70% ou mais, 30 tentativas ou mais', href: '#ligacao' });
    const rank = { bad: 0, warn: 1, acc: 2, dim: 3, ok: 4 };
    return out.sort((a, b) => rank[a.tone] - rank[b.tone]);
  }

  // ------------------------------------------------ Telemóvel e CS2 (as páginas Telemóvel e IP e CS2 e partidas da v2)
  function pLink(D) {
    const R = D.R, P = R.phone, C = R.cs2, kc = R.counters || {};
    const T = R.days.reduce((a, d) => { a.ipNew += d.ipNew; a.ipSame += d.ipSame; a.ipNone += d.ipNone; return a; }, { ipNew: 0, ipSame: 0, ipNone: 0 });
    const ipAll = T.ipNew + T.ipSame + T.ipNone;
    const k = (label, v, sub, tone = '') => `<div class="kc"><div class="kl">${label}</div><div class="kv ${tone}">${v}</div><div class="kd">${sub}</div></div>`;
    const series = (R.period < 14 ? D.H : R).days.slice(-Math.max(R.period, 14));
    const lagC = (x) => (x.n ? `<td class="n ${x.n >= 30 ? (x.lag / x.n >= 0.4 ? 'bad' : x.lag / x.n >= 0.2 ? 'warn' : 'good') : ''}">${share(x.lag, x.n)}<small class="dim"> n=${N(x.n)}</small></td>` : '<td class="n dim">-</td>');
    const q = P.quality, qAll = q.boa + q.razoavel + q.ma;
    const ses = R.sessions.starts;
    const tpMed = medianBand(R.sessions.toProtected);
    return `<div class="ptitle"><h1>Telemóvel e CS2</h1><p>a troca de IP, o telemóvel e as partidas · ${periodTxt(D)}</p></div>
    <div class="g">
      ${box('Resumo', periodTxt(D), [['e', 'days.ip*'], ['e', 'cs2'], ['e', 'cs2.rejoin']], `<div class="kstrip">
        ${k('Trocas com IP novo', share(T.ipNew, ipAll) || '-', `${N(T.ipNew)} de ${N(ipAll)} trocas`)}
        ${k('Sessões com o CS2 aberto', N(C.sessions), `${N(C.hoursCs2)} h com o CS2 aberto`)}
        ${k('Horas com proteção ligada', N(C.hoursProtected), 'contadas de 5 em 5 min')}
        ${k('Partidas', N(C.matches), `${N(C.valve)} Valve · ${N(C.community)} comunitárias`)}
        ${k('Partidas com lag', C.matches ? share(C.laggy, C.matches) : '-', `${N(C.laggy)} de ${N(C.matches)}`, C.matches >= 10 && C.laggy / C.matches > 0.2 ? 'warn' : '')}
        ${k('Rejoin voltou a entrar', C.rejoin.tries ? share(C.rejoin.back, C.rejoin.tries) : '-', `${N(C.rejoin.back)} de ${N(C.rejoin.tries)} · ${N(C.rejoin.waits)} à espera de vaga`)}
      </div>`, 's12')}
      ${box('Trocas por dia', `${series.length} dias`, [['e', 'days.ipNew/ipSame/ipNone']], dbars(series.map((d) => ({ date: d.date, a: d.ipNew, b: d.ipSame, c: d.ipNone })), [['a', 'IP novo', 'okc'], ['b', 'mesmo IP', 'wac'], ['c', 'sem rede', 'bac']], [], { todayPartial: true })
        + '<div class="legendrow"><span><i style="background:var(--good)"></i>IP novo</span><span><i style="background:var(--warn)"></i>mesmo IP</span><span><i style="background:var(--bad)"></i>sem rede</span></div>', 's8')}
      ${box('Trocas de IP', `${N(ipAll)} ${ipAll === 1 ? 'troca' : 'trocas'}`, [['e', 'phone.method']], `${ipAll ? bl([['IP novo', T.ipNew, null, 'good'], ['Ficou o mesmo IP', T.ipSame, null, 'warn'], ['Sem rede', T.ipNone, null, 'bad']], { pct: true }) : empty('Nenhuma troca no período.')}
        <h3>Método</h3>${sumN(P.method) ? bl(P.method.map((x) => [x.k, x.n]), { pct: true }) : empty('Sem método registado.')}`, 's4')}
      ${box('Quanto demora a troca', 'por ligação', [['e', 'phone.duration']], linkTab(P.duration, 'Sem tempos de troca no período.'), 's4')}
      ${box('Tentativas até ficar boa', 'trocas com IP novo', [['e', 'phone.attempts']], bandBl(P.attempts, 'Sem tentativas registadas.'), 's4')}
      ${box('Qualidade depois da troca', 'o teste da app', [['e', 'phone.quality']], `${qAll ? bl([['Boa', q.boa, null, 'good'], ['Razoável', q.razoavel, null, 'warn'], ['Má', q.ma, null, 'bad']], { pct: true }) : empty('Sem testes depois da troca.')}<p class="note">Trocaram outra vez por jitter: ${N(q.retry)} ${q.retry === 1 ? 'vez' : 'vezes'}.</p>`, 's4')}
      ${box('Do arranque a Protected', tpMed ? `banda do meio: ${esc(tpMed)}` : 'por sessão', [['e', 'sessions.toProtected']], bandBl(R.sessions.toProtected, 'Nenhuma sessão chegou a Protected no período.'), 's4')}
      ${box('Quedas do telemóvel', `por sessão · ${N(ses)} ${ses === 1 ? 'sessão' : 'sessões'}`, [['e', 'phone.drops']], bandBl(P.drops, 'Sem sessões fechadas no período.') + `<p class="note">Aviso do cabo USB mostrado: ${N(P.cableHints)} ${P.cableHints === 1 ? 'vez' : 'vezes'}.</p>`, 's4')}
      ${box('Dados móveis por dia', 'instalações-dia, a banda mais alta do dia', [['e', 'phone.data']], bandBl(P.data, 'Sem dados móveis no período.'), 's4')}
      ${box('Partidas com lag', 'por ligação e servidor', [['e', 'cs2.lag']], C.matches ? `<div class="tw"><table class="t"><thead><tr><th></th><th class="n">Valve</th><th class="n">Comunitário</th></tr></thead><tbody><tr><td>USB</td>${lagC(C.lag.usb.valve)}${lagC(C.lag.usb.community)}</tr><tr><td>Hotspot</td>${lagC(C.lag.hotspot.valve)}${lagC(C.lag.hotspot.community)}</tr></tbody></table></div><p class="note">Lag: a app viu jitter ou mensagens atrasadas acima do limite nessa partida. Cor só com 30 partidas ou mais.</p>` : empty('Nenhuma partida no período.', 'As partidas chegam do resumo de rede que o CS2 escreve ao sair de um servidor, com a ligação USB ou hotspot.'), 's4')}
      ${box('Ping por partida', 'P50 em ms', [['e', 'cs2.ping']], linkTab(C.ping), 's4')}
      ${box('Kicks, bans e VPN vistos', '', [['e', 'counters kick_seen, vpn_seen, ban_seen']], bl([['Kick (servidor cheio, AFK…)', kc.kick_seen || 0], ['VPN visto pelo servidor', kc.vpn_seen || 0, null, 'warn'], ['Ban marcado', kc.ban_seen || 0, null, 'bad']]), 's4')}
      ${box('Jitter de envio', 'ms', [['e', 'cs2.jitter']], linkTab(C.jitter), 's4')}
      ${box('Jitter de receção', 'ms', [['e', 'cs2.jitterDown']], linkTab(C.jitterDown), 's4')}
      ${box('Mensagens atrasadas e pacotes perdidos', 'por partida', [['e', 'cs2.late'], ['e', 'cs2.loss']], `<h3>Mensagens atrasadas</h3>${bandBl(C.late, 'Sem partidas.')}<h3>Pacotes perdidos</h3>${bandBl(C.loss, 'Sem partidas.')}`, 's4')}
      ${box('Regiões mais bloqueadas', `instalações-dia que bloqueiam cada datacenter da Valve · de ${N(C.regionsDen)}`, [['e', 'cs2.regions']], C.regions.length ? `<div class="envg">${[C.regions.slice(0, Math.ceil(C.regions.length / 2)), C.regions.slice(Math.ceil(C.regions.length / 2))].filter((p) => p.length).map((p) => `<div>${bl(p.map((r) => [`${r.code} · ${r.name}`, r.n]), { max: Math.max(1, ...C.regions.map((r) => r.n)), fmt: (v) => (C.regionsDen >= 10 ? `${Math.round((100 * v) / C.regionsDen)}%` : `${v} de ${C.regionsDen}`) })}</div>`).join('')}</div>` : empty('Ninguém bloqueou regiões no período.'), 's12')}
    </div>`;
  }

  // ------------------------------------------------ Versões (a página Versões da v2)
  function pVer(D) {
    const R = D.R, G = D.G;
    const met = R.versionMetrics || {};
    const vs = Object.keys(met).sort(compareV).reverse();
    if (!vs.includes(st.cmpA)) st.cmpA = vs[0] || null;
    if (!vs.includes(st.cmpB)) st.cmpB = vs[1] || vs[0] || null;
    const A = met[st.cmpA], B = met[st.cmpB];
    const src = (R.period < 14 ? D.H : R);
    const ad = (src.adoption || []).slice(-Math.max(R.period, 14));
    const all = [...new Set(ad.flatMap((x) => Object.keys(x.by)))].sort(compareV).reverse();
    const color = (v) => SHADES[all.indexOf(v) % SHADES.length];
    const adopt = ad.some((x) => Object.keys(x.by).length) ? `<div class="adopt">${ad.map((x) => { const t = Object.values(x.by).reduce((a, n) => a + n, 0);
      return `<div class="col${t ? '' : ' zero'}" data-tip="${esc(`${dd(x.date)}: ${t ? all.filter((v) => x.by[v]).map((v) => `${short(v)} ${N(x.by[v])}`).join(' · ') : 'ninguém'}`)}">${all.filter((v) => x.by[v]).map((v) => `<i style="height:${(100 * x.by[v]) / t}%;background:${color(v)}"></i>`).join('')}</div>`; }).join('')}</div>
      <div class="xax dim" style="display:flex;justify-content:space-between;font-size:10.5px;margin-top:2px"><span>${dshort(ad[0].date)}</span><span>${dshort(ad.at(-1).date)}</span></div>
      <div class="legendrow">${all.map((v) => `<span><i style="background:${color(v)}"></i>${esc(short(v))}</span>`).join('')}</div>` : empty('Nenhuma instalação-dia no período.');
    const rate = (a, b) => (b ? a / b : null);
    const pp = (x, n) => (x == null ? '-' : n < 10 ? `${N(Math.round(x * n))} de ${N(n)}` : `${nf.format(Math.round(1000 * x) / 10)}%`);
    const diff = (a, b, better, n1, n2) => { if (a == null || b == null) return '<td class="n dim">-</td>'; const d = a - b; const low = n1 < 30 || n2 < 30; const tone = low || !d ? 'dim' : (better === 'up' ? d > 0 : d < 0) ? 'good' : 'bad'; return `<td class="n ${tone}">${d > 0 ? '+' : d < 0 ? '−' : ''}${nf.format(Math.abs(Math.round(1000 * d) / 10))} pp</td>`; };
    const BAND = ['<1 s', '1-2 s', '2-4 s', '4-8 s', '>8 s', '<10 s', '10-30 s', '30-60 s', '1-3 min', '>3 min'];
    const bandRow = (label, a, b) => { const x = medianBand(a), y = medianBand(b); if (!x || !y) return `<tr><td>${label} <small class="dim">banda do meio</small></td><td class="n">${esc(x || '-')}</td><td class="n">${esc(y || '-')}</td><td class="n dim">-</td></tr>`; const d = BAND.indexOf(x) - BAND.indexOf(y); return `<tr><td>${label} <small class="dim">banda do meio</small></td><td class="n">${esc(x)}</td><td class="n">${esc(y)}</td><td class="n ${d === 0 ? 'dim' : d < 0 ? 'good' : 'bad'}">${d === 0 ? 'igual' : `${Math.abs(d)} ${Math.abs(d) === 1 ? 'banda' : 'bandas'} ${d < 0 ? 'mais rápida' : 'mais lenta'}`}</td></tr>`; };
    const opt = (sel) => vs.map((v) => `<option value="${esc(v)}"${v === sel ? ' selected' : ''}>${esc(short(v))}</option>`).join('');
    const cmp = A && B ? `<div class="cmphead"><select id="cmpA" aria-label="Versão A">${opt(st.cmpA)}</select> contra <select id="cmpB" aria-label="Versão B">${opt(st.cmpB)}</select></div>
      <div class="tw"><table class="t"><thead><tr><th></th><th class="n">${esc(short(st.cmpA))}</th><th class="n">${esc(short(st.cmpB))}</th><th class="n">Diferença</th></tr></thead><tbody>
      <tr><td>Instalações-dia</td><td class="n">${N(A.days)}</td><td class="n">${N(B.days)}</td><td class="n dim">${A.days - B.days >= 0 ? '+' : '−'}${N(Math.abs(A.days - B.days))}</td></tr>
      <tr><td>Sessões sem crash</td><td class="n">${pp(A.sessions ? 1 - A.crashes / A.sessions : null, A.sessions)}</td><td class="n">${pp(B.sessions ? 1 - B.crashes / B.sessions : null, B.sessions)}</td>${diff(A.sessions ? 1 - A.crashes / A.sessions : null, B.sessions ? 1 - B.crashes / B.sessions : null, 'up', A.sessions, B.sessions)}</tr>
      <tr><td>Trocas com IP novo</td><td class="n">${pp(rate(...A.ipNew), A.ipNew[1])}</td><td class="n">${pp(rate(...B.ipNew), B.ipNew[1])}</td>${diff(rate(...A.ipNew), rate(...B.ipNew), 'up', A.ipNew[1], B.ipNew[1])}</tr>
      <tr><td>Partidas com lag</td><td class="n">${pp(rate(...A.lag), A.lag[1])}</td><td class="n">${pp(rate(...B.lag), B.lag[1])}</td>${diff(rate(...A.lag), rate(...B.lag), 'down', A.lag[1], B.lag[1])}</tr>
      <tr><td>Minutos por sessão <small class="dim">mediana</small></td><td class="n">${N(A.minMed)}</td><td class="n">${N(B.minMed)}</td><td class="n dim">${A.minMed - B.minMed >= 0 ? '+' : '−'}${N(Math.abs(A.minMed - B.minMed))} min</td></tr>
      <tr><td>Erros por 100 sessões</td><td class="n">${A.sessions ? nf.format(Math.round((1000 * A.errors) / A.sessions) / 10) : '-'}</td><td class="n">${B.sessions ? nf.format(Math.round((1000 * B.errors) / B.sessions) / 10) : '-'}</td><td class="n dim">${N(A.errors)} · ${N(B.errors)} erros</td></tr>
      ${bandRow('Até Protected', A.toProtected, B.toProtected)}${bandRow('Até a app estar pronta', A.startup, B.startup)}
      </tbody></table></div><p class="note">Verde: a ${esc(short(st.cmpA))} está melhor. A cinzento: menos de 30 sessões, partidas ou trocas numa das duas, a diferença é ruído.</p>` : empty('Precisa de uma versão com instalações-dia no período.');
    const rel = ghOk(G) && G.releases ? G.releases.filter((r) => !r.draft) : [];
    const used = new Map(R.versions.map((v) => [short(v.k), v.n]));
    const tot = R.versions.reduce((a, v) => a + v.n, 0);
    const relRows = rel.length ? rel.map((r) => { const v = short(r.tag.replace(/^v/, '')); return `<tr><td>${esc(v)}</td><td>${r.date ? dd(r.date) : ''}</td><td class="n">${N(r.exe)}</td><td class="n">${used.has(v) ? N(used.get(v)) : '<span class="dim">0</span>'}${tot >= 10 && used.has(v) ? `<small class="dim"> ${Math.round((100 * used.get(v)) / tot)}%</small>` : ''}</td></tr>`; }).join('')
      : R.versions.map((v) => `<tr><td>${esc(short(v.k))}</td><td></td><td class="n dim">-</td><td class="n">${N(v.n)}</td></tr>`).join('');
    return `<div class="ptitle"><h1>Versões</h1><p>adoção de cada versão e duas lado a lado · ${periodTxt(D)}</p></div>
    <div class="g">
      ${box('Adoção ao longo do tempo', '% das instalações abertas em cada dia, por versão', [['e', 'adoption']], adopt + '<p class="note">Cada dia conta na versão do relatório mais recente desse dia. Passa o rato num dia para os números.</p>', 's12')}
      ${box('Comparar duas versões', periodTxt(D), [['e', 'versionMetrics']], cmp, 's7')}
      ${box('Versões', 'publicadas, downloads do exe e uso no período', [['x', 'GitHub releases'], ['e', 'versions']], `<div class="tw"><table class="t"><thead><tr><th>Versão</th><th>Saiu</th><th class="n">Downloads</th><th class="n">Inst.-dia</th></tr></thead><tbody>${relRows}</tbody></table></div><p class="note">Downloads: total de sempre de cada versão (inclui os teus e os scanners).</p>`, 's5')}
    </div>`;
  }

  // ------------------------------------------------ CSV da tabela principal da página aberta
  function csv() {
    const D = DATA; if (!D) return;
    const R = D.R;
    let rows;
    if (st.page === 'hoje') rows = [['dia', 'instalacoes', 'novas', 'minutos', 'trocas_ip_novo', 'trocas_mesmo_ip', 'trocas_sem_rede', 'sessoes', 'crashes'], ...D.H.days.map((d) => [d.date, d.users, d.fresh, d.minutes, d.ipNew, d.ipSame, d.ipNone, d.sessions, d.crashes])];
    else if (st.page === 'funil') rows = [['dia', 'primeiro_arranque', 'telemovel', 'primeira_troca', 'protected'], ...(R.funnelDays || []).map((d) => [d.date, d.fs, d.ph, d.ip, d.prot])];
    else if (st.page === 'app') { const S = UI.screens.find((s) => s.id === st.appTab); rows = [['ecra', 'botao', 'cliques', 'instalacoes_dia'], ...S.buttons.map((b) => { const r = R.buttons[b.id + '_Click'] || R.buttons[b.id] || { n: 0, days: 0 }; return [S.name, b.label, r.n, r.days]; })]; }
    else if (st.page === 'qualidade') rows = [['erro', 'onde', 'versao', 'vezes', 'instalacoes_dia', 'primeiro_dia'], ...R.errors.map((e) => [e.type, e.at, e.app, e.n, e.days, e.first ? e.first.day : ''])];
    else if (st.page === 'aquisicao') { const GP = ghPeriod(D); rows = [['dia', 'visitas_site', 'cliques_download', 'downloads_exe'], ...(R.site ? R.site.days : []).map((d) => [d.date, d.views, d.downloads, GP.byDay.get(d.date) ?? ''])]; }
    else if (st.page === 'ligacao') rows = [['dia', 'trocas_ip_novo', 'trocas_mesmo_ip', 'trocas_sem_rede', 'partidas', 'partidas_com_lag'], ...R.days.map((d) => [d.date, d.ipNew, d.ipSame, d.ipNone, d.matches, d.laggy])];
    else if (st.page === 'versoes') { const vs = [...new Set((R.adoption || []).flatMap((x) => Object.keys(x.by)))].sort(compareV); rows = [['dia', ...vs], ...(R.adoption || []).map((x) => [x.date, ...vs.map((v) => x.by[v] || 0)])]; }
    else rows = [['dia', 'instalacoes', 'novas', 'minutos'], ...R.days.map((d) => [d.date, d.users, d.fresh, d.minutes])];
    // Texto que chega dos relatórios (erro, sítio no código) não abre como fórmula no Excel: começa com ' quando abriria
    const cellOf = (v) => { let x = typeof v === 'string' && /^[=+\-@]/.test(v) ? `'${v}` : String(v ?? ''); if (/[",;\n]/.test(x)) x = `"${x.replace(/"/g, '""')}"`; return x; };
    const text = rows.map((r) => r.map(cellOf).join(',')).join('\n');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }));
    a.download = `cs2ipc-${st.page}${st.page === 'app' ? `-${st.appTab}` : ''}-${st.page === 'hoje' ? 30 : R.period}d.csv`;
    document.body.append(a); a.click(); setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 500);
  }

  // ------------------------------------------------ eventos
  function bindPage() {
    const tip = $('#tip');
    $('#main').onmousemove = (e) => { const t = e.target.closest('[data-tip]'); if (!t) { tip.style.display = 'none'; return; } tip.textContent = t.dataset.tip; tip.style.display = 'block'; tip.style.left = Math.max(4, Math.min(innerWidth - tip.offsetWidth - 8, e.clientX + 12)) + 'px'; tip.style.top = e.clientY + 14 + 'px'; };
    $('#main').onmouseleave = () => { tip.style.display = 'none'; };
    for (const b of $$('.tabs button[data-tab]')) b.onclick = () => { st.appTab = b.dataset.tab; const y = scrollY; render(); scrollTo(0, y); };
    for (const id of ['cmpA', 'cmpB']) { const el = $('#' + id); if (el) el.onchange = () => { st[id] = el.value; const y = scrollY; render(); scrollTo(0, y); }; }
  }
  $('#period').onclick = (e) => { const b = e.target.closest('button'); if (b && !b.disabled) { st.period = +b.dataset.p; store.set('period', st.period); refresh(); } };
  $('#mine').onchange = (e) => { const u = new URL(location.href); if (e.target.checked) u.searchParams.set('dev', '1'); else u.searchParams.delete('dev'); location.href = u.toString(); };
  $('#srcs').onchange = (e) => { store.set('srcs', e.target.checked ? '1' : '0'); render(); };
  $('#csv').onclick = csv;
  $('#freshPill').onclick = (e) => { e.preventDefault(); V3.forget(); if (location.hash !== '#hoje') location.hash = '#hoje'; else refresh(); };
  document.addEventListener('click', (e) => { const f = $('#flt'); if (f.open && !f.contains(e.target)) f.open = false; });
  addEventListener('hashchange', () => { refresh().then(() => scrollTo(0, 0)); });
  // Ao vivo a cada minuto; o resto a cada 5 minutos, só com a página à vista
  setInterval(async () => {
    if (document.hidden || !DATA) return;
    try { DATA.L = await V3.live(true); } catch { return; }
    if (st.page === 'hoje') { const y = scrollY; render(); scrollTo(0, y); } else render();
  }, 60_000);
  setInterval(() => { if (!document.hidden) { V3.forget(); const y = scrollY; refresh().then(() => scrollTo(0, y)); } }, 300_000);
  let rt = 0;
  addEventListener('resize', () => { clearTimeout(rt); rt = setTimeout(() => document.documentElement.style.setProperty('--hdr', $('#hdr').offsetHeight + 'px'), 160); });
  refresh();
})();
