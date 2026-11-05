// Dashboard v4 (4 nov. 2026, publish/auditoria-2026-11-04/dashboard-v4/spec.md): 8 páginas pelas perguntas do dono (está tudo
// bem e a crescer, onde se perdem, se ficam, onde encalham, o que falha, de onde vêm, o que fazem na app, a saúde dos dados).
// Os números vêm de /api/summary, /api/live, /api/github e /api/brief (real.js). A faixa de saúde, os seis números do Hoje e as
// regras vêm do brief: o mesmo JSON que o agente lê. Não gera números: o que não tem fonte diz "não medido" (tracejado) e o que
// não tem linhas diz "sem dados"; 0 só quando a fonte disse 0. A v3 fica em /v3 e a v2 em /v2.
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
  const pc = (x) => (x == null ? null : `${nf.format(Math.round(1000 * x) / 10)}%`);
  const short = (v) => String(v).replace(/^0\.1\.0-/, '');
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
  const LX = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Lisbon', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
  const lx = (t) => { const p = Object.fromEntries(LX.formatToParts(new Date(t)).map((x) => [x.type, x.value])); return { date: `${p.year}-${p.month}-${p.day}`, hm: `${p.hour}:${p.minute}`, h: +p.hour }; };
  const whenTxt = (t) => { if (!t) return 'nunca'; const a = lx(t), n = lx(Date.now()); const y = lx(Date.now() - 864e5).date; return `${a.date === n.date ? 'hoje' : a.date === y ? 'ontem' : `a ${dd(a.date)}`} às ${a.hm}`; };
  const agoT = (t) => (t ? ago(Math.max(0, Math.round((Date.now() - (typeof t === 'string' ? Date.parse(t) : t)) / 60000))) : 'nunca');
  /** Intervalo de Wilson a 95% de uma taxa (o mesmo que o brief), em texto. */
  const wilson = (a, n) => { if (!n) return null; const z = 1.96, p = a / n, z2 = z * z; const c = (p + z2 / (2 * n)) / (1 + z2 / n), h = (z * Math.sqrt((p * (1 - p)) / n + z2 / (4 * n * n))) / (1 + z2 / n); return [Math.max(0, c - h), Math.min(1, c + h)]; };
  const ciTxt = (a, n) => { const w = n && n < 30 ? wilson(a, n) : null; return w ? `<span class="ci">entre ${Math.round(100 * w[0])} e ${Math.round(100 * w[1])}%</span>` : ''; };

  const COUNTRY = { PT: 'Portugal', BR: 'Brasil', US: 'EUA', ES: 'Espanha', DE: 'Alemanha', FR: 'França', PL: 'Polónia', RU: 'Rússia', CA: 'Canadá', GB: 'Reino Unido', TR: 'Turquia', IT: 'Itália', NL: 'Países Baixos', '?': 'Desconhecido' };
  const MODES = { game: 'Só o jogo', cs2open: 'PC inteiro com o CS2', always: 'PC inteiro, sempre', nophone: 'Sem telemóvel', '?': 'Desconhecido' };
  const LINKS = { usb: 'USB', hotspot: 'Hotspot', other: 'Outro adaptador', none: 'none (sem telemóvel)', '?': 'Desconhecida' };
  const SCREEN = Object.fromEntries(UI.screens.map((s) => [s.id, s.name]));
  const scrName = (s) => (s === 'tray' ? 'Na bandeja' : SCREEN[s] || s);
  const SETUPN = { noadbtool: 'Sem ADB', adbfailed: 'ADB falhou', nousb: 'Sem cabo USB', usbdrivererror: 'Driver USB', nousbdebugging: 'Depuração USB desligada', unauthorized: 'Depuração por autorizar',
    offline: 'Telemóvel offline', tetheroff: 'Tethering desligado', wifion: 'Wi-Fi do telemóvel ligado', pconphone: 'PC no telemóvel', done: 'Pronto' };
  const STEPN = { first_start: '1.º arranque (aceitou o aviso)', phone_seen: 'Viu o telemóvel', protected: 'Protected', first_match: '1.ª partida' };
  const STUCKN = { never_phone: 'Nunca viu o telemóvel', phone_not_protected: 'Viu o telemóvel, nunca Protected', protected_no_match: 'Protected, sem partida', reached_match: 'Chegou a uma partida' };

  const PREFIX = { e: 'api', n: 'por recolher', x: 'externa', b: 'brief' };
  const TIP = { e: 'Vem do /api/summary ou /api/live', n: 'Ainda não é recolhido', x: 'Fonte de fora do Worker', b: 'Vem do /api/brief (o mesmo que o agente lê)' };
  const src = (k, t) => `<i class="src s-${k === 'b' ? 'e' : k}" title="${TIP[k]}">${PREFIX[k]} · ${esc(t)}</i>`;
  const box = (title, sub, srcs, body, cls = '') =>
    `<section class="gb ${cls}"><h2>${title}${sub ? `<small>${sub}</small>` : ''}</h2>${srcs && srcs.length ? `<div class="srcs">${srcs.map(([k, t]) => src(k, t)).join('')}</div>` : ''}${body}</section>`;
  const empty = (what, act) => `<div class="empty"><b>${what}</b>${act ? `<span class="act">${act}</span>` : ''}</div>`;
  const NM = 'não medido';
  const nm = (why) => `<span class="nm" title="${esc(why || 'Ainda não há fonte para isto')}">${NM}</span>`;

  function delta(cur, prev, fmt) {
    if (prev == null || cur == null) return '';
    if (cur === prev) return '<span class="same">igual</span>';
    const d = cur - prev;
    if (prev < 20) return `<span class="nd" title="Base abaixo de 20: só a diferença, sem cor">${d > 0 ? '+' : '-'}${fmt ? fmt(Math.abs(d)) : nf.format(Math.abs(d))}</span>`;
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
    return `<div class="bl">${rows.map(([k, v, extra, tone]) => `<span class="k" title="${esc(k)}">${esc(k)}</span><span class="t">${v ? `<i class="${tone || opt.tone || ''}" style="width:${(100 * v) / m}%"></i>` : ''}</span><span class="v">${opt.fmt ? opt.fmt(v) : N(v)}${opt.pct && tot >= 10 ? `<small>${Math.round((100 * v) / tot)}%</small>` : ''}${extra != null && opt.extra ? `<small>${opt.extra(extra)}</small>` : ''}</span>`).join('')}</div>`;
  }
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
      ${marks.filter((m) => ix.has(m.date)).map((m) => `<div class="mark ${m.cls || ''}${ix.get(m.date) / days.length > 0.6 ? ' r' : ''}" style="left:calc(26px + (100% - 26px) * ${ix.get(m.date) / days.length})" data-tip="${esc(`${dd(m.date)}: ${m.label}`)}"><span>${esc(m.label)}</span></div>`).join('')}
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
    <div class="legendrow"><span><i style="background:var(--acc)"></i>hoje</span><span><i style="border:1px solid var(--line2)"></i>ontem</span></div>`;
  }
  const tally = (rows, names) => rows.map((x) => [names ? names[x.k] || x.k : x.k, x.n]);
  const sumN = (rows) => rows.reduce((s, x) => s + (x.n || 0), 0);
  const bandBl = (rows, none) => (sumN(rows) ? bl(rows.map((x) => [x.band, x.n]), { pct: true }) : empty(none || 'Nada no período.'));
  const medianBand = (rows) => { const t = sumN(rows); if (!t) return null; let a = 0; for (const x of rows) { a += x.n || 0; if (a >= t / 2) return x.band; } return null; };
  function linkTab(rows, none) {
    const tu = rows.reduce((s, x) => s + (x.usb || 0), 0), th = rows.reduce((s, x) => s + (x.hotspot || 0), 0);
    if (!tu && !th) return empty(none || 'Sem partidas com USB ou hotspot no período.');
    const cellP = (v, t) => (!t ? '<td class="n dim">-</td>' : `<td class="n">${share(v, t) && t >= 10 ? share(v, t) : N(v)}</td>`);
    return `<div class="tw"><table class="t"><thead><tr><th>Banda</th><th class="n">USB <small class="dim">n=${N(tu)}</small></th><th class="n">Hotspot <small class="dim">n=${N(th)}</small></th></tr></thead><tbody>${rows.map((x) => `<tr><td>${esc(x.band)}</td>${cellP(x.usb || 0, tu)}${cellP(x.hotspot || 0, th)}</tr>`).join('')}</tbody></table></div>`;
  }
  function btnAt(S, c, r) {
    const { W, H, X0, COLS, ROWS } = UI;
    const x = X0 + (c + 0.5) * ((W - X0) / COLS), y = (r + 0.5) * (H / ROWS);
    const b = S.buttons.find((q2) => q2.r && [q2.r, ...(q2.more || [])].some((rr) => x >= rr[0] - 4 && x <= rr[0] + rr[2] + 4 && y >= rr[1] - 4 && y <= rr[1] + rr[3] + 4));
    return b ? b.label : null;
  }
  function whenMap(when) {
    const days = ['seg', 'ter', 'qua', 'qui', 'sex', 'sáb', 'dom'];
    const all = when.flat(), m = Math.max(1, ...all), tot = all.reduce((s, v) => s + v, 0);
    if (!tot) return empty('Nenhum relatório no período.');
    return `<div class="tw"><div class="wmap">${days.map((dn, di) => `<span class="hl">${dn}</span>${when[di].map((v, h) => `<i style="background:${v ? `rgba(var(--t-accent-rgb),${(0.15 + (0.85 * v) / m).toFixed(2)})` : 'var(--btn)'}" data-tip="${dn} ${h}h: ${N(v)} ${v === 1 ? 'instalação' : 'instalações'}">${tot < 60 && v ? v : ''}</i>`).join('')}`).join('')}<span></span>${[...Array(24)].map((_, h) => `<span class="hx">${h % 3 === 0 ? h + 'h' : ''}</span>`).join('')}</div></div>
      <p class="note">${N(tot)} instalações-hora: cada instalação conta uma vez em cada hora com um relatório. Hora de Lisboa.</p>`;
  }
  const peakTxt = (L) => {
    const P = L && L.peaks;
    if (!P || P.first == null) return 'Pico: ainda sem amostras. O cron do stats-dashboard conta o ao vivo de 10 em 10 min e guarda o máximo de cada hora.';
    const t = P.today.filter((v) => v != null);
    return `Pico de ontem: ${P.ydayMax ? `<b>${N(P.ydayMax.n)}</b> às ${P.ydayMax.hour}h` : 'sem amostras ontem'}. Hoje até agora: ${t.length ? `<b>${N(Math.max(...t))}</b>` : 'sem amostras'}. Amostras de 10 em 10 min, o máximo de cada hora.`;
  };
  const siteOk = (D) => !(D.R.warnings || []).some((w) => /^site:/.test(w));
  function ghPeriod(D) {
    const hist = (D.G && D.G.history && D.G.history.days) || [];
    const from = D.R.days[0].date, to = D.R.asOf;
    // The picture dated D (03:07 UTC) minus the one before is the downloads of D-1: each number goes on the day it happened
    const byDay = new Map(hist.filter((x) => x.exeDay != null).map((x) => [addDays(x.day, -1), x.exeDay]).filter(([d]) => d >= from && d <= to));
    const stars = hist.filter((x) => x.day >= from && x.day <= to && x.starsDay != null).reduce((s, x) => s + x.starsDay, 0);
    return { byDay, covered: byDay.size, downloads: byDay.size ? [...byDay.values()].reduce((s, v) => s + v, 0) : null, stars, hist };
  }
  const host = (h) => { const x = String(h || '').toLowerCase().replace(/^www\./, ''); return !x ? 'direto' : x.includes('.') ? x : `${x}.com`; };
  const LANG = (() => { try { return new Intl.DisplayNames(['pt-PT'], { type: 'language' }); } catch { return null; } })();
  const SHADES = ['#2b74c2', '#6fb2f2', '#3f6f9f', '#c6e2fb', '#1f3346', '#8fa3b6', '#4b5a69'];
  const NOT_RARE = new Set(['ClearHistory', 'CfgUndo', 'ResetAll', 'LogClear', 'RemoveServers', 'TplClearAvatar', 'ProfRestoreSelected', 'CfgRestore', 'TplNew', 'TplCopyFrom', 'TplDelete', 'ProfForgetAll', 'Restore', 'CfgSnapUse', 'CfgSnapExport', 'CfgSnapDelete', 'DeleteStats', 'ReportProblem', 'Quick']);
  const LISTN = { servers: 'Servidores', accounts: 'Contas', flags: 'Jogadores marcados', open_with: 'Open with CS2', open_with_admin: 'Open with CS2 como admin', regions_blocked: 'Regiões desligadas' };
  // Os contadores que já têm um bloco na v4; os outros vão para "Outros contadores" (Sistema)
  const SHOWN = /^(ip_changed|ip_same|ip_no_network|ip_home_back|log_error|app_start|crash|first_start|min_prot|min_cs2|cs2_start|kick_seen|vpn_seen|ban_seen|rej_try|rej_back|rej_wait|leak_ok|leak_bad|unclean_exit|cable_hint|rep_exit|(m|ping|jit|jitd|late|loss|sess|st|tp|start|upd|updfrom|phone_setup|phone_left|chg|chgt|chga|chgq|drop|data|scl|fs|chk)_.*)$/;

  // ------------------------------------------------ estado e navegação
  // v5 (5 nov. 2026): esta é a página de dados (/data), para quem faz a app; a do dono é o /dashboard. Abre no Brief.
  const PAGES = [
    ['brief', 'Brief', '<path d="M6 3h9l4 4v14H6z"/><path d="M14 3v5h5M9 12h7M9 16h7"/>'],
    ['hoje', 'Hoje', '<path d="M12 3v3M12 18v3M3 12h3M18 12h3"/><circle cx="12" cy="12" r="5"/>'],
    ['funil', 'Funil', '<path d="M3 5h18l-7 8v6l-4-2v-4z"/>'],
    ['crescimento', 'Crescimento', '<path d="M3 20h18M5 16l5-5 4 3 6-7"/><path d="M16 7h4v4"/>'],
    ['encalham', 'Onde encalham', '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5v5M12 15.8v.4"/>'],
    ['qualidade', 'Qualidade', '<path d="M12 3l8 3v6c0 4.5-3.4 8-8 9-4.6-1-8-4.5-8-9V6z"/><path d="M8.5 12l2.5 2.5 4.5-5"/>'],
    ['aquisicao', 'Aquisição', '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>'],
    ['app', 'Na app', '<rect x="3" y="4" width="18" height="14" rx="1"/><path d="M8 21h8M10 9l5 3-5 3z"/>'],
    ['sistema', 'Sistema e dados', '<ellipse cx="12" cy="6" rx="7.5" ry="2.8"/><path d="M4.5 6v12c0 1.5 3.4 2.8 7.5 2.8s7.5-1.3 7.5-2.8V6M4.5 12c0 1.5 3.4 2.8 7.5 2.8s7.5-1.3 7.5-2.8"/>'],
  ];
  const store = { get: (k) => { try { return localStorage.getItem('v4.' + k); } catch { return null; } }, set: (k, v) => { try { localStorage.setItem('v4.' + k, v); } catch { /* sem armazenamento */ } } };
  const q = new URLSearchParams(location.search);
  const st = { period: 7, page: 'hoje', appTab: 'connection', cmpA: null, cmpB: null, f: { version: 'all', country: 'all', mode: 'all', link: 'all' } };
  if ([1, 7, 30, 90].includes(+store.get('period'))) st.period = +store.get('period');
  if ([1, 7, 30, 90].includes(+q.get('p'))) st.period = +q.get('p');
  for (const k of Object.keys(st.f)) { const v = q.get(k); if (v && /^[A-Za-z0-9_.+-]{1,40}$/.test(v)) st.f[k] = v; }
  $('#srcs').checked = q.get('src') === '1' || store.get('srcs') === '1';
  $('#mine').checked = V4.DEV;
  let DATA = null;   // { R: resumo do período, H: resumo de 30 dias, L: ao vivo, G: GitHub, B: brief }
  let seq = 0;

  const pageNow = () => { const p = location.hash.slice(1).split('/')[0]; return PAGES.some((x) => x[0] === p) ? p : 'brief'; };
  async function load() {
    const my = ++seq;
    const page = pageNow();
    const need30 = page === 'hoje' || (['crescimento', 'qualidade', 'sistema', 'aquisicao'].includes(page) && st.period < 14);
    // O GitHub e o brief não seguram a página (o GitHub pode demorar meio segundo a frio): desenha-se já e outra vez quando chegam.
    // Só a Aquisição espera pelo GitHub.
    const G0 = V4.githubNow(), B0 = V4.briefNow();
    const ghP = G0 === undefined ? V4.github() : null;
    const brP = B0 === undefined ? V4.brief() : null;
    // v5: a página Brief mostra também o Markdown do brief (null se falhar)
    const [R, H, L, G, M] = await Promise.all([V4.summary(st.f, st.period), need30 ? V4.summary(st.f, 30) : null, V4.live().catch(() => null), page === 'aquisicao' && ghP ? ghP : G0,
      page === 'brief' ? V4.briefMd().catch(() => null) : undefined]);
    if (my !== seq) return false;
    DATA = { R, H: H || R, L, G, B: B0, M };
    const again = () => { if (my !== seq || !DATA) return; const y = scrollY; render(); scrollTo(0, y); };
    if (G === undefined && ghP) ghP.then((g) => { if (DATA && my === seq) { DATA.G = g; again(); } });
    if (B0 === undefined && brP) brP.then((b) => { if (DATA && my === seq) { DATA.B = b; again(); } });
    return true;
  }
  async function refresh() {
    try { if (!(await load())) return; } catch (e) { showErr(e); return; }
    render();
  }
  function showErr(e) {
    $('#main').innerHTML = `<div class="g">${box('Não deu para ler os dados', '', [], `<p class="note">${esc(String((e && e.message) || e))}. Tenta outra vez daqui a um minuto; se continuar, o Worker stats-dashboard ou a D1 estão em baixo.</p>`, 's12')}</div>`;
    $('#health').innerHTML = '<span class="hc bad"><b>sem resposta da API</b></span>';
  }
  function ghOk(G) { return !!(G && G.stars != null); }
  const HC = (B, id) => (B && B.health ? B.health.checks.find((c) => c.id === id) : null);

  /** A faixa de saúde no topo: dados, orçamento, GitHub, cron, cadência (as mesmas verificações do brief.health). */
  function healthStrip(D) {
    const B = D.B;
    if (B === undefined) return '<span class="hc">a ler a saúde dos dados…</span>';
    if (!B) return '<span class="hc bad"><b>brief sem resposta</b></span>';
    const c = (id) => HC(B, id) || { status: 'ok' };
    const f = c('freshness'), cr = c('cron_live_hourly'), tk = c('github_token'), sn = c('github_snapshot'), oc = c('old_cadence_share_today');
    const bud = ['budget_report', 'budget_live', 'budget_site'].map(c).sort((a, b) => (b.value || 0) - (a.value || 0))[0];
    const worst = (...l) => (l.includes('bad') ? 'bad' : l.includes('warn') ? 'warn' : 'ok');
    const tokTxt = { ok: 'ok', missing: 'sem token', empty: 'token vazio', no_answer: 'sem resposta', public_only: 'só público', no_traffic_access: 'sem tráfego' }[tk.value] || `token ${tk.value}`;
    return [
      `<span class="hc ${f.status}" title="Último relatório real (fora dos teus PCs). Amarelo acima de 3 h, vermelho acima de 12 h.">dados <b>${f.value == null ? 'nenhum em 90 dias' : ago(f.value)}</b></span>`,
      `<span class="hc ${bud.status}" title="A maior parte do orçamento diário de linhas da D1 (relatórios, ao vivo, site)">orçamento <b>${Math.round(100 * (bud.value || 0))}%</b></span>`,
      `<span class="hc ${worst(tk.status, sn.status)}" title="${esc(tk.impact || 'GitHub com token')} · último retrato ${esc(sn.value || 'nenhum')}">GitHub <b>${esc(tokTxt)}</b></span>`,
      `<span class="hc ${cr.status}" title="Última amostra do cron de 10 em 10 min (guardada por hora UTC)">cron <b>${cr.value == null ? 'sem amostras' : ago(cr.value)}</b></span>`,
      `<span class="hc ${oc.status}" title="Instalações de hoje em versões antes da beta.10 (enviam pouco)">versões <b>${oc.value == null ? 'poucas hoje' : `${Math.round(100 * oc.value)}% antigas`}</b></span>`,
    ].join('');
  }
  /** As regras desta página (do motor do brief), numa faixa por cima dos blocos. */
  function rulesBand(D, page, opt = {}) {
    const B = D.B;
    if (!B) return '';
    const list = B.anomalies.filter((a) => a.page === '#' + page && (!opt.kind || a.kind === opt.kind));
    if (!list.length) return '';
    return box('Regras desta página', `${list.length} de ${B.rules_checked} · do /api/brief, últimos 7 dias, sem filtros`, [['b', 'anomalies']],
      `<div class="rband">${list.map((a) => `<div class="${esc(a.severity)}">${esc(a.text_pt)}${a.base != null ? `<div class="rr">base ${N(a.base)}</div>` : ''}</div>`).join('')}</div>`, 's12');
  }
  const fEcho = () => { const a = Object.entries(st.f).filter(([, v]) => v !== 'all'); return a.length ? ` <span class="fecho">Filtro: ${a.map(([k, v]) => esc(k === 'version' ? short(v) : k === 'country' ? COUNTRY[v] || v : k === 'mode' ? MODES[v] || v : LINKS[v] || v)).join(' · ')}</span>` : ''; };
  const title = (h, p) => `<div class="ptitle"><h1>${h}</h1><p>${p}${fEcho()}</p></div>`;

  function render() {
    const D = DATA; if (!D) return;
    st.page = pageNow();
    document.body.classList.toggle('nosrc', !$('#srcs').checked);
    document.body.dataset.page = st.page;
    for (const b of $$('#period button')) { b.setAttribute('aria-pressed', String(+b.dataset.p === st.period)); b.disabled = st.page === 'hoje' || st.page === 'brief'; }
    $('#health').innerHTML = healthStrip(D);
    const B = D.B;
    const cnt = {};
    if (B) for (const a of B.anomalies) { const p = (a.page || '').slice(1); if (a.kind === 'product' && (a.severity === 'high' || a.severity === 'bad')) cnt[p] = (cnt[p] || 0) + 1; }
    if (B && B.health.status !== 'ok') cnt.sistema = B.health.checks.filter((c) => c.status !== 'ok').length;
    $('#nav').innerHTML = PAGES.map(([id, label, icon]) => `<a href="#${id}"${id === st.page ? ' aria-current="page"' : ''}><svg viewBox="0 0 24 24">${icon}</svg>${label}${cnt[id] ? `<span class="cnt ${id === 'sistema' && B.health.status === 'warn' ? 'warn' : 'bad'}">${cnt[id]}</span>` : ''}</a>`).join('')
      + '<div class="foot">Unidade: a instalação (ID fixo desde a beta.9); instalação-dia só onde o diz. Dias em UTC, horas de Lisboa. Só tu vês este painel: números exatos.</div>'
      + '<div class="links"><a href="/dashboard">← Resumo (a página do dono)</a></div>';
    $('#crumbPage').textContent = PAGES.find((p) => p[0] === st.page)[1];
    document.title = `${PAGES.find((p) => p[0] === st.page)[1]} · CS2 IP Changer dados`;
    renderFilters(D);
    const fn = { brief: pBrief, hoje: pHoje, funil: pFunil, crescimento: pGrowth, encalham: pStuck, qualidade: pQual, aquisicao: pAcq, app: pApp, sistema: pSys }[st.page];
    const devband = V4.DEV ? '<div class="devband">Só os teus PCs: builds de desenvolvimento e os IDs de EXCLUDE_IDS. Os números de toda a gente ficam de fora.<a href="?">ver toda a gente</a></div>' : '';
    $('#main').innerHTML = devband + fn(D) + foot(D);
    bindPage();
    document.documentElement.style.setProperty('--hdr', $('#hdr').offsetHeight + 'px');
  }
  const periodTxt = (D) => (D.R.period === 1 ? 'hoje' : `últimos ${D.R.period} dias`);
  const foot = (D) => `<div class="foot"><span>Lido às ${lx(Date.now()).hm} de ${dd(lx(Date.now()).date)}${D.B ? ` · brief das ${lx(Date.parse(D.B.generated_at)).hm}` : ''} · ${V4.DEV ? 'só os teus PCs' : D.R.excluded ? `sem os teus ${D.R.excluded} PCs (EXCLUDE_IDS) e sem builds de dev` : 'sem builds de dev'}${D.R.automated ? ` · ${N(D.R.automated)} ${D.R.automated === 1 ? 'análise automática deixada' : 'análises automáticas deixadas'} de fora` : ''}</span>
    <details><summary>Como se conta</summary><p>Instalação: um ID de instalação (fixo desde a beta.9), contado uma vez no período. Instalação-dia: um ID num dia (a mesma pessoa em 3 dias conta 3). Nova: aceitou o aviso nesse dia (1.º arranque); quem atualizou de uma beta antiga não conta como nova. Os primeiros 7 dias de uma instalação nova são o dia 0 ao dia 6; cada passo do funil pode acontecer em qualquer um deles. Os teus PCs (builds de dev e EXCLUDE_IDS) ficam de fora; "Os meus PCs" mostra só esses. Análises automáticas (0 contas Steam, 0 cliques, 0 minutos) nunca contam. Percentagens só com 10 ou mais casos; abaixo disso, "3 de 4"; com menos de 30, o intervalo. "Não medido" (tracejado): não há fonte. "Sem dados": a fonte existe mas não tem linhas. 0 só quando a fonte disse 0. Nunca somar contagens do site (sem ID) com instalações (por ID). Dias em UTC, horas de Lisboa. As definições de cada número estão em Sistema e dados.</p></details></div>`;

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
      + `<p>O mapa de cliques só segue a versão; o ambiente, o ao vivo, o site, o GitHub e o brief (faixa de saúde, números do Hoje, regras) não seguem filtros.</p>${act ? '<button class="btn" id="resetF">Limpar filtros</button>' : ''}`;
    for (const s of $$('[data-f]')) s.onchange = () => { st.f[s.dataset.f] = s.value; refresh(); };
    const rb = $('#resetF'); if (rb) rb.onclick = () => { for (const k of Object.keys(st.f)) st.f[k] = 'all'; refresh(); };
  }
  /** As marcas das séries por dia: as versões publicadas (GitHub) e as anotações (vídeos, posts, incidentes). */
  function marksOf(D) {
    const rel = ghOk(D.G) && D.G.releases ? D.G.releases.filter((r) => !r.draft).map((r) => ({ date: r.date, label: short(r.tag.replace(/^v/, '')) })) : [];
    const ann = ((D.G && D.G.annotations) || []).filter((a) => a.kind !== 'release').map((a) => ({ date: a.date, label: a.label, cls: a.kind === 'video' ? 'v' : '' }));
    return [...rel, ...ann].filter((m, i, l) => l.findIndex((x) => x.date === m.date) === i);
  }

  // ------------------------------------------------ Hoje: em 5 segundos, está tudo bem e a crescer
  function pHoje(D) {
    const H = D.H, L = D.L, B = D.B, now = lx(Date.now());
    const early = now.h < 12;
    const K = B ? B.kpis : null;
    const spark14 = (k) => mini(H.days.slice(-15, -1).map((d) => d[k]));
    const wait = (label) => `<div class="kc wait"><div class="kl">${label}</div><div class="kv">${B === undefined ? 'a ler…' : 'sem resposta do brief'}</div></div>`;
    const FR = HC(B, 'freshness'), stale = !!(FR && FR.status === 'bad');
    const STALE = { nothing_arrived: 'Hoje não se escreveu nada, nem dos teus PCs: vê se o Worker stats responde.', only_owner_or_dev_pcs: 'Os teus PCs enviam, os outros não: ninguém mais está a usar a app, ou as versões deles enviam pouco.', quiet: 'Chegam relatórios, mas nenhum real.' };
    const cmpLine = (a, lbl) => `<div class="kd">${lbl} <b>${a == null ? 'sem dados' : N(a)}</b></div>`;
    // Antes do meio-dia (Lisboa) o dia de hoje está quase vazio: o número grande é ontem, dia inteiro, contra anteontem
    const dayK = (label, k, key) => {
      if (!K) return wait(label);
      const x = K[k];
      const main = early ? x.yesterday : x.today;
      const cmp = early ? H.days.at(-3)[key] : x.yesterday_same_time;
      return `<div class="kc" data-k="${k}.${early ? 'yesterday' : 'today'}" data-v="${main}"><div class="kl">${label} ${early ? '<small>ontem</small>' : ''}</div><div class="kv${stale && !main ? ' na' : ''}">${stale && !main ? 'sem dados' : `${N(main)}${delta(main, cmp)}`}</div>
        ${early ? `${cmpLine(cmp, 'anteontem')}<div class="kd">hoje até agora <b>${N(x.today)}</b></div>` : `${cmpLine(x.yesterday_same_time, 'ontem à mesma hora')}${cmpLine(x.same_weekday_last_week_same_time, `${wday(addDays(H.asOf, -7)).slice(0, 3)}. passada à mesma hora`)}`}${spark14(key)}</div>`;
    };
    const live = L ? `<div class="kc" data-k="live_now.value"><div class="kl">A usar agora</div><div class="kv">${N(L.now)}</div><div class="kd">pico hoje <b>${K && K.live_now.peak_today != null ? `${N(K.live_now.peak_today)} às ${K.live_now.peak_today_hour}h` : 'sem amostras'}</b></div><div class="kd">pico ontem <b>${L.peaks && L.peaks.ydayMax ? `${N(L.peaks.ydayMax.n)} às ${L.peaks.ydayMax.hour}h` : 'sem amostras'}</b></div></div>`
      : '<div class="kc"><div class="kl">A usar agora</div><div class="kv na">sem resposta</div></div>';
    const ex = K && K.exe_downloads, sc = K && K.site_download_clicks;
    const dl = !K ? wait('Downloads do exe') : `<div class="kc" data-k="exe_downloads.yesterday" data-v="${ex.yesterday ?? ''}"><div class="kl" title="Ontem, dia completo (de retrato a retrato, 03:07 UTC)">Downloads do exe <small>ontem</small></div><div class="kv${ex.yesterday == null ? ' na' : ''}">${ex.yesterday == null ? 'sem retrato' : N(ex.yesterday)}</div>
      <div class="kd">7 dias <b>${ex.d7 == null ? 'sem dados' : N(ex.d7)}</b>${ex.days_covered_d7 && ex.days_covered_d7 < 7 ? ` (${ex.days_covered_d7} dias)` : ''}</div><div class="kd">site hoje: <b>${sc && sc.today != null ? N(sc.today) : 'sem dados'}</b> cliques em Download</div>${mini(B.series.exe_downloads.slice(-15, -1).map((v) => v || 0))}</div>`;
    const ac = K && K.activation_7d;
    const rt = (x) => (!x || !x.den ? 'sem novas' : x.den < 10 ? `${x.num} de ${x.den}` : pc(x.value));
    const act = !K ? wait('Ativação') : `<div class="kc" data-k="activation_7d.value" data-v="${ac.value ?? ''}"><div class="kl" title="Novas de há 7 a 13 dias (os 7 dias já fecharam) que chegaram a Protected">Ativação <small>novas de há 7 a 13 dias</small></div><div class="kv${ac.value == null ? ' na' : ''}">${ac.value == null ? 'nenhuma nova' : rt(ac)}${ac.den >= 10 ? ciTxt(ac.num, ac.den) : ''}</div>
      <div class="kd" title="As novas de há 14 a 20 dias, à mesma idade">semana antes <b>${rt(ac.prev)}</b></div><div class="kd" title="As novas dos últimos 7 dias: ainda nos primeiros 7 dias, só sobem">a decorrer (7 dias) <b>${rt(ac.running)}</b></div></div>`;
    const cf = K && K.crash_free_sessions_7d;
    const crash = !K ? wait('Sem crash') : `<div class="kc" data-k="crash_free_sessions_7d.value" data-v="${cf.value ?? ''}"><div class="kl">Sessões sem crash <small>7 dias</small></div><div class="kv${cf.value == null ? ' na' : ''}">${cf.value == null ? 'sem sessões' : cf.den < 10 ? `${cf.num} de ${cf.den}` : pc(cf.value)}</div><div class="kd"><b>${N(cf.den - cf.num)}</b> ${cf.den - cf.num === 1 ? 'crash' : 'crashes'} em <b>${N(cf.den)}</b> ${cf.den === 1 ? 'sessão' : 'sessões'}</div><div class="kd">barras: crashes por dia</div>${mini(B.series.crashes.slice(-15, -1))}</div>`;
    const kp = box(early ? 'Ontem, dia inteiro, e hoje até agora' : `Hoje até às ${now.hm}`, early ? `hoje ainda vai às ${now.hm} (Lisboa)` : 'contra ontem e contra a semana passada, à mesma hora',
      [['b', 'kpis'], ['e', '/api/live']], `${stale ? `<div class="al" style="margin-bottom:12px"><div class="bad"><b>${FR.value == null ? 'Nenhum relatório real nos últimos 90 dias' : `Não chegam relatórios reais ${ago(FR.value)}`}: os números de hoje não valem.</b><p>${esc(STALE[FR.stale_kind] || '')} Detalhe em <a href="#sistema">Sistema e dados</a>.</p></div></div>` : ''}<div class="k6">${live}${dayK('Novas', 'new_installs', 'fresh')}${dayK('Ativas', 'active_installs', 'users')}${dl}${act}${crash}</div>
      <p class="note">Novas: aceitaram o aviso. Ativas: instalações diferentes com um relatório real. Barras: 14 dias até ontem; o traço no fundo é um dia a zero. Os downloads do exe só se sabem no retrato do dia seguinte (03:07 UTC).</p>`, 's12 kpis4');
    // O que mudou: só as regras de produto (as de dados estão na faixa de saúde, não se repetem)
    // Os erros novos numa linha só (crítica: cinco erros tiravam o lugar às outras regras)
    const prod = B ? B.anomalies.filter((a) => a.kind === 'product') : [];
    const errs = prod.filter((a) => a.id.startsWith('new_error:'));
    const grouped = errs.length > 1 ? [{ severity: errs.some((a) => a.severity === 'high') ? 'high' : 'medium', page: '#qualidade', text_pt: `${errs.length} erros novos nos últimos 7 dias: ${errs.reduce((a, e) => a + (e.observed || 0), 0)} vezes. O maior: ${errs[0].text_pt}`, base: null }] : errs;
    const ch = [...grouped, ...prod.filter((a) => !a.id.startsWith('new_error:'))].slice(0, 5);
    const changed = box('O que mudou', B ? (ch.length ? `${prod.length} de ${B.rules_checked} regras` : '') : 'a ler…', [['b', 'anomalies']], !B ? empty(B === undefined ? 'A ler o brief…' : 'O brief não respondeu.')
      : ch.length ? `<div class="al">${ch.map((a) => `<div class="${a.severity === 'high' ? 'bad' : a.severity === 'info' ? 'acc' : ''}"><b>${esc(a.text_pt)}</b><p><a href="${esc(a.page)}">ver</a>${a.base != null ? ` · base ${N(a.base)}` : ''}</p></div>`).join('')}</div>`
      : stale ? empty('Sem dados recentes para verificar as regras.', 'Primeiro tem de voltar a chegar relatórios (faixa vermelha acima).')
      : empty(`Nada fora do normal (${B.rules_checked} regras verificadas).`, 'Cada regra tem o seu n mínimo: com poucos dados ficam caladas.'), 's7');
    // Ao vivo, sempre: "0 a usar · último sinal há 24 h" também é informação
    const liveBox = box('Ao vivo', 'sinal nos últimos 6 min, número exato', [['e', '/api/live']], L ? `<div class="bigline"><span class="big">${N(L.now)}</span><span>a usar · ${N(L.inGame)} em jogo · ${N(L.protectedN)} Protected<br><span class="dim">último sinal ${agoT(L.lastTs)}</span></span></div>
      ${L.now ? `<h3>Onde estão</h3>${bl(L.byScreen.map((x) => [scrName(x.k), x.n]))}<h3>Ligação</h3>${bl(tally(L.byLink, LINKS))}<h3>Versão</h3>${bl(L.byVersion.map((x) => [short(x.k), x.n]))}` : ''}
      <p class="note">${peakTxt(L)}</p>` : empty('O /api/live não respondeu.'), 's5 live4');
    // O funil e onde encalham em 7 dias (do brief, sem filtros)
    let fun = empty(B === undefined ? 'A ler o brief…' : 'O brief não respondeu.');
    let stk = fun;
    if (B) {
      const A = B.funnel.activation, Q = B.funnel.acquisition;
      const acq = Q.steps.filter((s) => s.id !== 'search_impressions' && s.id !== 'search_clicks');
      fun = linFunnel([...acq.map((s) => ({ k: { site_visits: 'Visitas ao site', site_download_clicks: 'Cliques em Download', exe_downloads: 'Downloads do exe' }[s.id], n: s.n, ext: true, open: s.id === 'exe_downloads' ? 'todas as origens' : null, none: s.reason }))], { label: 'Aquisição · contagens, sem ID' })
        + (A.steps[0].n ? linFunnel(A.steps.map((s) => ({ id: s.id, k: STEPN[s.id], n: s.n })), { label: 'Ativação · novas de há 7 a 13 dias', worst: A.biggest_drop && A.biggest_drop.to })
          : `<p class="note">Ativação: nenhuma instalação nova de há 7 a 13 dias.</p>`)
        + `<p class="note">Ativação: as novas de há 7 a 13 dias, que já fecharam os 7 primeiros dias. A decorrer (últimos 7 dias): ${N(A.running_steps[0].n)} novas, ${N(A.running_steps[2].n)} já em Protected. Os dois blocos não se ligam (o site não tem ID, por escolha): nunca somar.</p>`;
      const S = B.stuck;
      stk = S.n ? `<div class="bl">${S.buckets.map((x) => `<span class="k">${esc(STUCKN[x.id])}</span><span class="t">${x.n ? `<i class="${x.id === 'reached_match' ? 'good' : 'warn'}" style="width:${(100 * x.n) / S.n}%"></i>` : ''}</span><span class="v">${S.n < 10 ? `${N(x.n)} de ${N(S.n)}` : pc(x.share)}${x.prev_share != null && S.prev_n >= 10 ? `<small>antes ${S.prev_n < 20 ? `${N(x.prev)} de ${N(S.prev_n)}` : pc(x.prev_share)}</small>` : ''}</span>`).join('')}</div>
        <p class="note">As ${N(S.n)} novas de há 7 a 13 dias, cada uma no último passo a que chegou nos 7 primeiros dias; antes: as de há 14 a 20 dias. A decorrer: ${N(S.running)} novas nos últimos 7 dias. Detalhe em <a href="#encalham">Onde encalham</a>.</p>`
        : empty('Nenhuma instalação nova de há 7 a 13 dias.', `A decorrer: ${N(S.running)} novas nos últimos 7 dias.`);
    }
    const funBox = box('Funil', 'aquisição: 7 dias completos · ativação: coorte fechada · barras lineares', [['b', 'funnel']], fun, 's7');
    const stkBox = box('Onde encalham', 'novas de há 7 a 13 dias', [['b', 'stuck']], stk, 's5');
    const hourly = H.hourly || { today: Array(24).fill(null), yday: Array(24).fill(0), hour: now.h };
    const hourSmall = isSmall([...hourly.today, ...hourly.yday]);
    const hourBox = box('Por hora', 'instalações com relatório · hoje contra ontem', [['e', 'hourly']], `<details class="box"><summary>Mostrar</summary>${hourSmall ? hstrip(hourly, hourly.hour) : hbars(hourly, hourly.hour)}</details>`, 's12');
    return `${title('Hoje', `${wday(now.date)}, ${dd(now.date)} · ${now.hm} (Lisboa)${early ? ' · antes do meio-dia o número grande é ontem' : ''}`)}
    <div class="g">${kp}${changed}${liveBox}${funBox}${stkBox}${hourBox}</div>`;
  }
  /** Barras lineares de um bloco do funil, normalizadas ao 1.º passo do bloco; % do 1.º e do anterior; "não medido" a tracejado. */
  function linFunnel(steps, opt = {}) {
    const first = steps.find((s) => s.n != null);
    const top = Math.max(1, first ? first.n : 1);
    let prev = null;
    const rows = steps.map((s, i) => {
      const na = s.n == null;
      const pf = !na && s !== first && first && first.n && !s.open ? share(s.n, first.n) : '';
      const pp = !na && prev != null && !s.open && prev !== top ? share(s.n, prev) : '';
      if (!na) prev = s.n;
      return `<div class="k">${esc(s.k)}${s.sub ? `<small>${esc(s.sub)}</small>` : ''}</div><div><div class="t ${na ? 'na' : ''} ${s.ext ? 'ext' : ''}">${na || !s.n ? '' : `<i style="width:${Math.min(100, (100 * s.n) / top)}%"></i>`}</div></div>
        <div class="n ${na ? 'na' : ''}">${na ? (s.none === 'not_measured' ? NM : 'sem dados') : N(s.n)}</div><div class="p pf">${pf}</div><div class="p ${opt.worst && s.id === opt.worst ? 'worst' : ''}">${s.open ? `<span class="dim">${esc(s.open)}</span>` : i && pp ? pp : ''}</div>`;
    }).join('');
    return `<div class="lf"><div class="hd">${esc(opt.label || 'Passo')}</div><div class="hd"></div><div class="hd n">Número</div><div class="hd n pf">do 1.º</div><div class="hd n">do anterior</div>${rows}</div>`;
  }

  // ------------------------------------------------ Funil: aquisição por dia (contagens) e ativação por instalação
  function pFunil(D) {
    const R = D.R, A = R.activation, SI = R.site || { views: 0, downloads: 0, days: [] }, GP = ghPeriod(D), sOk = siteOk(D);
    const acq = [
      { k: 'Impressões no Google', n: null, none: 'not_measured', sub: 'precisa do Search Console' },
      { k: 'Cliques no Google', n: null, none: 'not_measured', sub: 'vem com o Search Console' },
      { k: 'Visitas ao site', n: sOk ? SI.views : null, ext: true, sub: 'sem cookies e sem ID' },
      { k: 'Cliques em Download', n: sOk ? SI.downloads : null, ext: true },
      { k: 'Downloads do exe', n: GP.downloads, ext: true, open: 'todas as origens', sub: GP.covered ? `${GP.covered} de ${R.period} dias com retrato` : 'sem retratos seguidos' },
    ];
    const big = (() => { let w = null; for (let k = 1; k < A.steps.length; k++) { const a = A.steps[k - 1].n, b = A.steps[k].n; if (a < 20) continue; const d = 1 - b / a; if (!w || d > w.d) w = { k, d, a, b }; } return w; })();
    const pv = A.prev, pvOk = big && pv.steps[big.k - 1].n >= 20;
    const act = [...A.steps.map((s) => ({ id: s.id, k: STEPN[s.id], n: s.n }))];
    const seg = (name, rows, names) => rows.length ? `<div><h3>${name}</h3><div class="tw"><table class="t"><thead><tr><th></th><th class="n">Novas</th><th class="n">Protected</th><th class="n">1.ª partida</th></tr></thead><tbody>${rows.map((r) => `<tr><td>${esc(names ? names[r.k] || r.k : short(r.k))}</td><td class="n">${N(r.n)}</td><td class="n">${share(r.protected, r.n) || '-'}</td><td class="n">${share(r.first_match, r.n) || '-'}</td></tr>`).join('')}</tbody></table></div></div>` : '';
    const coh = (A.cohorts || []).filter((c) => c.day >= R.days[0].date).slice().reverse();
    const d1 = A.d1, d7 = A.d7;
    return `${title('Funil', `${periodTxt(D)} · dois blocos que não se ligam: as contagens do site e as instalações`)}
    <div class="g">${rulesBand(D, 'funil')}
      ${box('A. Aquisição', `${periodTxt(D)} · contagens, sem ID`, [['e', 'site'], ['e', 'github.history'], ['x', 'Search Console']], linFunnel(acq, { label: 'Passo' }) + '<p class="note">Barras normalizadas às visitas ao site. Os downloads do exe vêm também do README, do GitHub direto, dos teus e de scanners: por isso não têm %.</p>', 's6')}
      ${box('B. Ativação', `novas no período · cada passo nos 7 primeiros dias`, [['e', 'activation']], A.n ? linFunnel(act, { label: 'Passo', worst: big && act[big.k].id }) + `
        <div class="lf"><div class="side">Ao lado: ${N(A.ip_changed.n)} das ${N(A.ip_changed.of)} que viram o telemóvel trocaram para um IP novo${A.ip_changed.same_only ? `; ${N(A.ip_changed.same_only)} trocaram e ficaram com o mesmo IP` : ''}. Protected não precisa de trocar de IP.</div></div>
        <p class="note">${A.immature ? `${A.immature === A.n ? 'Todas' : `${N(A.immature)} das ${N(A.n)}`} ainda estão nos primeiros 7 dias: estes números ainda podem subir (vê 30 dias para ver coortes fechadas). ` : ''}Cada passo conta quem também passou os anteriores. Fora de ordem: ${N(A.any.protected)} chegaram a Protected e ${N(A.any.first_match)} a uma partida.</p>` : empty('Nenhuma instalação nova no período.'), 's6')}
      <div class="s4 stack">
        ${box('Onde se perde mais', '', [['e', 'activation.steps']], big ? `<div class="bigline"><span class="big bad">${Math.round(big.d * 100)}%</span><span>ficam entre<br><b>${esc(STEPN[A.steps[big.k - 1].id])}</b> e <b>${esc(STEPN[A.steps[big.k].id])}</b></span></div><p class="note">${N(big.a)} → ${N(big.b)}.${A.immature ? ' Sem comparação: as novas deste período ainda não fecharam os 7 dias (a comparação justa está no Hoje e no /api/brief, com coortes fechadas).' : pvOk ? ` No período antes: ${Math.round(100 * (1 - pv.steps[big.k].n / pv.steps[big.k - 1].n))}% (${N(pv.steps[big.k - 1].n)} → ${N(pv.steps[big.k].n)}).` : ' Sem comparação: menos de 20 no período antes.'}</p>`
          : empty('Ainda não dá para dizer.', `Só com 20 ou mais no passo anterior (${N(A.n)} ${A.n === 1 ? 'nova' : 'novas'} no período).`))}
        ${box('Voltam', 'só as novas cujo dia já passou', [['e', 'activation.d1/d7']], `<div class="kv2"><span>No dia seguinte (D1)</span><span>${d1.n ? `${share(d1.back, d1.n)}${d1.n >= 10 && d1.n < 30 ? ` <small class="dim">${ciTxt(d1.back, d1.n).replace(/<[^>]+>/g, '')}</small>` : ''}` : '<span class="dim">sem base</span>'}</span><span>Entre o dia 7 e o 13 (D7)</span><span>${d7.n ? share(d7.back, d7.n) : '<span class="dim">sem base</span>'}</span></div><p class="note">D1: de ${N(d1.n)} com o dia seguinte passado. D7: de ${N(d7.n)} com o 13.º dia passado.</p>`)}
      </div>
      ${box('Por segmento', 'novas no período: ligação, país, versão e modo do 1.º dia', [['e', 'activation.segments']], A.n ? `<div class="envg">${seg('Ligação', A.segments.link, LINKS)}${seg('País', A.segments.country, COUNTRY)}${seg('Versão', A.segments.version)}${seg('Modo', A.segments.mode, MODES)}</div>` : empty('Nenhuma instalação nova no período.'), 's8')}
      ${box('Por dia de instalação', 'cada linha: as novas desse dia e onde chegaram nos 7 primeiros dias', [['e', 'activation.cohorts']], coh.length ? `<div class="tw"><table class="t"><thead><tr><th>1.º dia</th><th class="n">Novas</th><th class="n">Telemóvel</th><th class="n">Protected</th><th class="n">Partida</th><th class="n">Troca IP novo</th><th class="n">D1</th></tr></thead><tbody>${coh.map((c) => `<tr><td>${dd(c.day)}${c.immature ? ' <small class="dim">a decorrer</small>' : ''}</td><td class="n">${N(c.n)}</td>${c.steps.slice(1).map((v) => `<td class="n">${share(v, c.n) || '-'}</td>`).join('')}<td class="n">${share(c.ip_changed, c.steps[1]) || '-'}</td><td class="n">${c.d1 ? share(c.d1.back, c.d1.n) : '<span class="dim">ainda não</span>'}</td></tr>`).join('')}</tbody></table></div>` : empty('Nenhuma instalação nova no período.'), 's12')}
      ${box('Aquisição por dia', 'site e GitHub, contagens · os downloads no dia em que aconteceram', [['e', 'site.days'], ['e', 'github.history']], `<div class="tw"><table class="t"><thead><tr><th>Dia</th><th class="n">Visitas ao site</th><th class="n">Cliques em Download</th><th class="n">Downloads do exe</th><th class="n">Novas (aceitaram)</th></tr></thead><tbody>${R.days.slice(-14).reverse().map((d) => { const sd = (SI.days || []).find((x) => x.date === d.date); const gd = GP.byDay.get(d.date); return `<tr><td>${dd(d.date)}${d.date === R.asOf ? ' <small class="dim">até agora</small>' : ''}</td><td class="n">${sOk && sd ? N(sd.views) : '<span class="dim">-</span>'}</td><td class="n">${sOk && sd ? N(sd.downloads) : '<span class="dim">-</span>'}</td><td class="n">${gd == null ? '<span class="dim">-</span>' : N(gd)}</td><td class="n">${N(d.fresh)}</td></tr>`; }).join('')}</tbody></table></div><p class="note">Downloads do exe por dia: a diferença entre o retrato das 03:07 UTC do dia seguinte e o desse dia (traço: falta um dos dois; o de hoje só se sabe amanhã). As novas estão aqui ao lado, não na mesma conta.</p>`, 's12')}
    </div>`;
  }

  // ------------------------------------------------ Crescimento: quantas, se ficam
  function pGrowth(D) {
    const R = D.R, I = R.installs, A = R.activation, src30 = R.period < 14 ? D.H : R, GP = ghPeriod(D);
    const k = (label, v, sub, extra = '') => `<div class="kc"><div class="kl">${label}</div><div class="kv">${v}${extra}</div><div class="kd">${sub}</div></div>`;
    const fresh = R.days.reduce((s, d) => s + d.fresh, 0), prevFresh = R.prev ? R.prev.fresh : null;
    const W = R.period === 30 ? R.windows.d30 : R.windows.d7;
    const days = src30.days.slice(-Math.max(R.period, 14)).map((d) => ({ date: d.date, fresh: d.fresh, back: Math.max(0, d.users - d.fresh) }));
    const marks = marksOf(D);
    const stick = (src30.stick || []).filter((x) => x.wau != null);
    const wauOk = stick.length && Math.max(...stick.map((x) => x.wau)) >= 20;
    const daily = (R.cohortsDaily || []).filter((c) => c.n > 0);
    const useDaily = !daily.length || Math.max(...daily.map((c) => c.n)) < 50;
    const ch = useDaily ? daily.slice().reverse().map((c) => ({ k: dd(c.day), n: c.n, back: c.back }))
      : (R.cohortsExact || []).filter((c) => c.n > 0).slice().reverse().map((c) => ({ k: `sem. de ${dd(c.week)}`, n: c.n, back: c.back.slice(1, 6) }));
    const cell = (v, n) => (v == null ? '<td class="c na">ainda não</td>' : n < 10 ? `<td class="c"><span>${v} de ${n}</span></td>` : `<td class="c"><span style="background:rgba(var(--t-accent-rgb),${Math.min(0.75, 0.08 + (0.9 * v) / n)})">${Math.round((100 * v) / n)}%</span></td>`);
    const RT = (R.retention || { points: [] }).points;
    const act = (R.active || []).filter((a) => a.n > 0);
    const C = R.cs2;
    return `${title('Crescimento', `quantas, novas e as que voltam · ${periodTxt(D)}, contra os ${R.period} dias antes`)}
    <div class="g">${rulesBand(D, 'crescimento')}
      ${box('Resumo', periodTxt(D), [['e', 'installs'], ['e', 'activation'], ['e', 'windows'], ['e', 'github.history']], `<div class="kstrip">
        ${k('Instalações ativas', N(I.active), `diferentes no período · antes <b>${N(I.prev)}</b>`, delta(I.active, I.prev))}
        ${k('Novas', N(fresh), `aceitaram o aviso · antes <b>${N(prevFresh)}</b>`, R.period > 1 ? delta(fresh, prevFresh) : '')}
        ${k('Downloads do exe', GP.downloads == null ? 'sem dados' : N(GP.downloads), GP.downloads == null ? 'sem dois retratos seguidos' : `${GP.covered} de ${R.period} dias com retrato`)}
        ${k('Voltam no dia seguinte', A.d1.n ? share(A.d1.back, A.d1.n) : 'sem base', `de <b>${N(A.d1.n)}</b> com o dia 1 passado`, A.d1.n >= 10 ? ciTxt(A.d1.back, A.d1.n) : '')}
        ${k('Voltam entre o dia 7 e o 13', A.d7.n ? share(A.d7.back, A.d7.n) : 'sem base', `das <b>${N(A.d7.n)}</b> com o dia 13 passado`, A.d7.n >= 10 ? ciTxt(A.d7.back, A.d7.n) : '')}
        ${k('Minutos por instalação', W && W.minutes_median != null ? N(W.minutes_median) : 'sem dados', `mediana em ${R.period === 30 ? 30 : 7} dias, não média`)}
      </div>`, 's12')}
      ${box('Por dia', `${days.length} dias · instalações em cada dia: novas e as que voltaram`, [['e', 'days']], dbars(days, [['fresh', 'novas', 'f'], ['back', 'voltaram', 'b']], marks, { todayPartial: true }) + `<div class="legendrow"><span><i style="background:var(--acc)"></i>novas</span><span><i style="background:var(--acc-d)"></i>voltaram</span><span>traço no fundo = 0</span>${marks.length ? '<span>riscos: versões e anotações</span>' : ''}</div>`, 's12')}
      ${box('Pegajosidade', 'ativas no dia a dividir pelas ativas em 7 dias (DAU/WAU)', [['e', 'stick']], wauOk ? `<div class="tw"><table class="t"><thead><tr><th>Dia</th><th class="n">No dia</th><th class="n">Em 7 dias</th><th class="n">DAU/WAU</th></tr></thead><tbody>${stick.slice(-10).reverse().map((x) => `<tr><td>${dd(x.date)}</td><td class="n">${N(x.dau)}</td><td class="n">${N(x.wau)}</td><td class="n">${x.wau ? pc(x.dau / x.wau) : '-'}</td></tr>`).join('')}</tbody></table></div>` : empty(`Poucos dados (${N(stick.length ? Math.max(...stick.map((x) => x.wau)) : 0)} em 7 dias).`, 'Aparece a partir de 20 instalações ativas em 7 dias.'), 's4')}
      ${box('Retenção', 'novas abertas outra vez em cada janela', [['e', 'retention']], RT.some((p) => p.n) ? `<div class="curve">${RT.map((p) => `<div><div class="cv">${p.n ? (p.n < 10 ? `${p.back} de ${p.n}` : pc(p.back / p.n)) : '<span class="dim">ainda não</span>'}</div>${p.n >= 10 && p.n < 30 ? `<div class="cl">${ciTxt(p.back, p.n).replace(/<[^>]+>/g, '')}</div>` : ''}<div class="cl">${p.day === p.to ? `dia ${p.day}` : `dias ${p.day} a ${p.to}`} · n=${N(p.n)}</div></div>`).join('')}</div><p class="note">Cada janela só conta as novas cujo último dia da janela já passou, por isso o n desce para a direita e as colunas não se somam. Janelas fixas: uma coorte antiga não ganha dias a mais para voltar.</p>` : empty('Nenhuma instalação nova com a 1.ª janela fechada.'), 's8')}
      ${box(useDaily ? 'Voltam (por dia do 1.º ID)' : 'Voltam (por semana)', useDaily ? 'últimos 14 dias' : 'semana do 1.º dia do ID fixo', [['e', useDaily ? 'cohortsDaily' : 'cohortsExact']],
        ch.length ? `<div class="tw"><table class="t coh"><thead><tr><th>${useDaily ? '1.º dia' : 'Semana'}</th><th class="n">IDs</th><th>dia 1</th><th>dia 3</th><th>dia 7</th><th>dia 14</th>${useDaily ? '' : '<th>dia 30</th>'}</tr></thead><tbody>
        ${ch.map((c) => `<tr><td>${c.k}</td><td class="n">${N(c.n)}</td>${c.back.map((v) => cell(v, c.n)).join('')}</tr>`).join('')}</tbody></table></div><p class="note">Pelo 1.º dia do ID fixo (beta.9+). Abaixo de 10, o número sem cor. Dia 1: 1 a 2 dias depois; dia 3: 3 a 6; dia 7: 7 a 13; dia 14: 14 a 29. Por dia enquanto há menos de 50 IDs novos num dia; depois por semana.</p>` : empty('Nenhum ID novo nos últimos 14 dias.'), 's6')}
      ${box('Países', 'instalações-dia', [['e', 'countries']], bl(R.countries.map((c) => [COUNTRY[c.k] || c.k, c.n]), { pct: true }), 's3')}
      ${box('Inativas', 'vistas há 14 a 30 dias, não depois', [['e', 'inactive']], R.inactive == null ? empty('Precisa do período de 30 ou 90 dias.') : `<div class="bigline"><span class="big">${N(R.inactive)}</span><span>instalações<br><span class="dim">"inativa", nunca "desinstalada": a app não sabe</span></span></div>`, 's3')}
      ${box('Dias com a app aberta', 'últimos 7 dias, por instalação', [['e', 'active']], act.length ? bl(act.map((a) => [`${a.band} ${a.band === '1' ? 'dia' : 'dias'}`, a.n]), { pct: true }) : empty('Ainda sem dados.'), 's4')}
      ${box('Idade da instalação', 'dias desde o 1.º arranque, por instalação-dia', [['e', 'age']], R.age.length ? bl(R.age.map((a) => [a.band === '0' ? 'no dia em que instalou' : `${a.band} ${a.band === '1' ? 'dia' : 'dias'}`, a.n]), { pct: true }) : empty('Ainda sem dados.'), 's4')}
      ${sessBox(R)}
      ${box('Horas de jogo e de proteção', periodTxt(D), [['e', 'cs2']], `<div class="kv2"><span>Com o CS2 aberto</span><span>${N(C.hoursCs2)} h</span><span>Sessões com o CS2 aberto</span><span>${N(C.sessions)}</span><span>Com a proteção ligada</span><span>${N(C.hoursProtected)} h</span><span>Partidas</span><span>${N(C.matches)}</span></div>`, 's4')}
      ${box('Quando usam', 'dia da semana por hora de Lisboa', [['e', 'when']], whenMap(R.when || []), 's8')}
      ${liveHist(D)}
    </div>`;
  }

  // ------------------------------------------------ Onde encalham: o último passo de cada instalação nova
  function pStuck(D) {
    const R = D.R, S = R.stuck, A = R.activation;
    const tot = S.n;
    const nextOf = { never_phone: ['#e-setup', 'o Phone setup de quem nunca ligou'], phone_not_protected: ['#e-prot', 'a troca de IP e os Checks a vermelho'], protected_no_match: ['#e-match', 'se abriram o CS2'], reached_match: ['#qualidade', 'a qualidade das partidas'] };
    const rows = S.buckets.map((b) => {
      const d = b.prev_share != null && b.share != null && S.prev_n >= 20 && tot >= 20 && !A.immature ? b.share - b.prev_share : null;
      return `<div>${esc(STUCKN[b.id])}</div><div><div class="t">${b.n ? `<i class="${b.id === 'reached_match' ? 'good' : ''}" style="width:${(100 * b.n) / tot}%"></i>` : ''}</div></div><div class="n">${N(b.n)}</div><div class="p ${d == null || Math.abs(d) < 0.01 ? '' : (d > 0) === (b.id !== 'reached_match') ? 'bad' : 'good'}">${tot < 10 ? `${N(b.n)} de ${N(tot)}` : pc(b.share)}</div><div class="pp">${b.prev_share == null ? '<span class="dim">-</span>' : S.prev_n < 10 ? `${N(b.prev)} de ${N(S.prev_n)}` : pc(b.prev_share)}</div><div class="nx"><a href="${nextOf[b.id][0]}">${esc(nextOf[b.id][1])}</a></div>`;
    }).join('');
    const never = S.buckets[0].n;
    const setupRows = S.setup.filter((x) => x.open || x.left || x.pressed);
    const sig = Object.fromEntries(S.signals.map((x) => [x.id, x]));
    const cellName = (x) => { const sc = UI.screens.find((s) => s.id === x.screen); return `${scrName(x.screen)} · ${sc ? btnAt(sc, x.c, x.r) || 'fora dos botões' : 'ecrã desconhecido'}`; };
    const NP = S.phone_not_protected, PM = S.protected_no_match;
    const sug = improve(D).filter((x) => x.href !== '#sistema');
    return `${title('Onde encalham', `cada instalação nova no último passo a que chegou nos 7 primeiros dias · novas: ${periodTxt(D)}`)}
    <div class="g">${rulesBand(D, 'encalham')}
      ${box('Último passo', `${N(tot)} ${tot === 1 ? 'nova' : 'novas'} · antes ${N(S.prev_n)}`, [['e', 'stuck.buckets']], tot ? `<div class="stk"><div class="hd">Grupo</div><div class="hd"></div><div class="hd n">Novas</div><div class="hd p">Agora</div><div class="hd pp">Antes</div><div class="hd nx">O que ver</div>${rows}</div>
        <p class="note">A soma dá ${N(tot)} (100%). ${A.immature ? `${A.immature === tot ? 'Todas' : N(A.immature)} ainda estão nos primeiros 7 dias e podem passar ao grupo seguinte: sem cor, porque o antes já fechou os 7 dias (a comparação à mesma idade está no Hoje). ` : 'Agora a vermelho: pior do que antes (20 ou mais em cada). '}Antes: o mesmo cálculo no período anterior. Protected não precisa de trocar de IP.</p>` : empty('Nenhuma instalação nova no período.'), 's12')}
      <div class="s6 stack" id="e-setup">${box('Phone setup de quem nunca viu o telemóvel', `${N(never)} ${never === 1 ? 'instalação' : 'instalações'}`, [['e', 'stuck.setup'], ['n', S.left_measured ? 'phone_left_' : 'phone_left_ (próxima beta)']], never ? `<div class="tw"><table class="t"><thead><tr><th>Passo</th><th class="n">Ao abrir</th><th class="n">Ao fechar${S.left_measured ? '' : ` <span class="nm" title="Precisa do contador phone_left_ na app (próxima beta)">${NM}</span>`}</th><th class="n">Carregou no botão do passo</th></tr></thead><tbody>${setupRows.map((x) => `<tr><td>${esc(SETUPN[x.step] || x.step)}</td><td class="n">${N(x.open)}</td><td class="n${x.left == null ? ' dim' : ''}">${x.left == null ? '-' : N(x.left)}</td><td class="n">${N(x.pressed)}</td></tr>`).join('') || '<tr><td colspan="4" class="dim">Nenhum abriu o Phone setup.</td></tr>'}</tbody></table></div>
        <p class="note">Nunca abriram o Phone setup (nem a ajuda): <b>${N(S.never_opened)}</b> de ${N(never)}. "Ao abrir": o passo em que o painel estava quando o abriram (não é onde desistiram). "Ao fechar": ${S.left_measured ? 'o passo quando o fecharam.' : 'ainda não medido; chega com o contador phone_left_ na próxima beta.'} Uma instalação pode ter aberto em mais de um passo.</p>` : empty(tot ? 'Todas as novas viram o telemóvel.' : 'Nenhuma instalação nova no período.'), 'grow')}</div>
      <div class="s6 stack" id="e-prot">
        ${box('Viram o telemóvel, nunca Protected', `${N(NP.n)}`, [['e', 'stuck.phone_not_protected']], NP.n ? `<div class="kv2"><span>Trocaram para um IP novo</span><span>${N(NP.ip_new)}</span><span>Trocaram e ficou o mesmo IP (ou sem rede)</span><span>${N(NP.ip_same_only)}</span><span>Nunca trocaram de IP</span><span>${N(NP.n - NP.ip_new - NP.ip_same_only)}</span></div>${NP.red.length ? `<h3>Checks a vermelho</h3>${bl(NP.red.map((c) => [c.title, c.installs]), { tone: 'warn' })}` : '<p class="note">Nenhum Check a vermelho nestas instalações.</p>'}` : empty('Ninguém neste grupo.'))}
        <div id="e-match">${box('Protected, sem partida', `${N(PM.n)}`, [['e', 'stuck.protected_no_match']], PM.n ? `<div class="kv2"><span>Abriram o CS2 com a app aberta</span><span>${N(PM.cs2_opened)}</span><span>Nunca abriram o CS2</span><span>${N(PM.n - PM.cs2_opened)}</span></div><p class="note">A partida vem do resumo de rede que o CS2 escreve ao sair de um servidor. "1.ª entrada num servidor" não é recolhida.</p>` : empty('Ninguém neste grupo.'))}</div>
      </div>
      ${box('Sinais de frustração', periodTxt(D), [['e', 'stuck.signals'], ['e', 'heat']], `<div class="tw"><table class="t"><thead><tr><th>Sinal</th><th class="n">Vezes</th><th class="n">Instalações</th><th>Onde</th></tr></thead><tbody>
        <tr><td>Cliques repetidos (3 ou mais em 1 s)</td><td class="n">${N(sig.rage_clicks.n)}</td><td class="n dim">sem ID</td><td>${sig.rage_clicks.top.slice(0, 3).map((x) => `${esc(cellName(x))} (${N(x.n)})`).join(' · ') || '-'}</td></tr>
        <tr><td>Cliques sem efeito</td><td class="n">${N(sig.dead_clicks.n)}</td><td class="n dim">sem ID</td><td>${sig.dead_clicks.top.slice(0, 3).map((x) => `${esc(cellName(x))} (${N(x.n)})`).join(' · ') || '-'}</td></tr>
        <tr><td>Aviso do cabo USB (caiu 3 vezes em 10 min)</td><td class="n">${N(sig.cable_hint.n)}</td><td class="n">${N(sig.cable_hint.installs)}</td><td></td></tr>
        <tr class="${sig.home_ip_back.n ? 'bad' : ''}"><td>O IP de casa voltou</td><td class="n ${sig.home_ip_back.n ? 'bad' : ''}">${N(sig.home_ip_back.n)}</td><td class="n">${N(sig.home_ip_back.installs)}</td><td></td></tr>
        <tr><td>Checks a vermelho</td><td class="n">${N(sig.red_checks.n)}</td><td class="n">${N(sig.red_checks.installs)}</td><td>${sig.red_checks.top.map((c) => `${esc(c.title)} (${N(c.installs)})`).join(' · ') || '-'}</td></tr>
        <tr><td>Crash ou erro no 1.º dia (novas)</td><td class="n ${sig.day1_crash_or_error.n ? 'bad' : ''}">${N(sig.day1_crash_or_error.n)}</td><td class="n">${N(sig.day1_crash_or_error.installs)}</td><td>${sig.day1_crash_or_error.n ? `${tot >= 10 ? `${pc(sig.day1_crash_or_error.n / tot)} das novas · ` : ''}<a href="#qualidade">ver os erros</a>` : ''}</td></tr>
        <tr><td>Atualização falhou</td><td class="n">${N(sig.update_failed.n)}</td><td class="n">${N(sig.update_failed.installs)}</td><td></td></tr>
      </tbody></table></div><p class="note">Os cliques não têm ID (são somados ao chegar): só o número. O botão mais perto vem do mapa das capturas da app.</p>`, 's12')}
      ${box('O que fazer', sug.length ? `${sug.length} ${sug.length === 1 ? 'sugestão' : 'sugestões'} de produto · ${periodTxt(D)}` : periodTxt(D), [['e', 'regras de produto sobre o resumo']], sug.length ? `<div class="al">${sug.map((x) => `<div class="${x.tone}"><b>${esc(x.t)}</b><p>${esc(x.d)}</p><p class="dim">Regra: ${esc(x.rule)} · <a href="${x.href}">ver</a></p></div>`).join('')}</div>` : empty('Nenhuma sugestão no período.', 'Cada regra tem o seu n mínimo; com poucos dados ficam caladas.'), 's12')}
    </div>`;
  }

  // ------------------------------------------------ Qualidade: o que falha
  function pQual(D) {
    const R = D.R, P = R.phone, C = R.cs2, kc = R.counters || {};
    const T = R.days.reduce((a, d) => { for (const k2 of ['ipNew', 'ipSame', 'ipNone', 'sessions', 'crashes', 'unclean', 'leakBad', 'leakOk', 'homeBack', 'logErr']) a[k2] += d[k2] || 0; return a; }, { ipNew: 0, ipSame: 0, ipNone: 0, sessions: 0, crashes: 0, unclean: 0, leakBad: 0, leakOk: 0, homeBack: 0, logErr: 0 });
    const ipAll = T.ipNew + T.ipSame + T.ipNone;
    const per100 = (a, s) => (s >= 10 ? nf.format(Math.round((1000 * a) / s) / 10) : null);
    const k = (label, v, sub, tone = '') => `<div class="kc"><div class="kl">${label}</div><div class="kv ${tone}">${v}</div><div class="kd">${sub}</div></div>`;
    const kicks = kc.kick_seen || 0;
    const since = R.days[0].date;
    const er7 = new Map((R.errorsRecent || []).map((e) => [`${e.type}|${e.at}`, e]));
    const errs = R.errors.map((e) => ({ ...e, fresh: !!(e.first && e.first.day >= since), r7: er7.get(`${e.type}|${e.at}`) }));
    const series = (R.period < 14 ? D.H : R).days.slice(-14);
    const trend = (perDay) => series.map((d) => (perDay && perDay[d.date]) || 0);
    const met = R.versionMetrics || {};
    const vs = Object.keys(met).sort(compareV).reverse();
    const lagC = (x) => (x.n ? `<td class="n ${x.n >= 30 ? (x.lag / x.n >= 0.4 ? 'bad' : x.lag / x.n >= 0.2 ? 'warn' : 'good') : ''}">${share(x.lag, x.n)}<small class="dim"> n=${N(x.n)}</small></td>` : '<td class="n dim">-</td>');
    const U = R.updates;
    const qy = P.quality, qAll = qy.boa + qy.razoavel + qy.ma;
    const cfr = !T.sessions ? 'sem sessões' : T.sessions >= 10 ? `${nf.format(Math.round(1000 - (1000 * T.crashes) / T.sessions) / 10)}%` : `${N(T.sessions - T.crashes)} de ${N(T.sessions)}`;
    return `${title('Qualidade', `o que falha, na app, no telemóvel e no jogo · ${periodTxt(D)}`)}
    <div class="g">${rulesBand(D, 'qualidade')}
      ${box('Resumo', periodTxt(D), [['e', 'days'], ['e', 'cs2']], `<div class="kstrip k4">
        ${k('Sessões sem crash', cfr, `${N(T.crashes)} ${T.crashes === 1 ? 'crash' : 'crashes'} em ${N(T.sessions)} sessões`)}
        ${k('Saídas sem fecho', per100(T.unclean, T.sessions) ? `${per100(T.unclean, T.sessions)}<small>/100</small>` : N(T.unclean), `${N(T.unclean)} em ${N(T.sessions)} sessões`, T.sessions && T.unclean > T.sessions * 0.05 ? 'warn' : '')}
        ${k('Trocas com IP novo', share(T.ipNew, ipAll) || 'sem trocas', `${N(T.ipNew)} de ${N(ipAll)} trocas`)}
        ${k('Partidas com lag', C.matches ? share(C.laggy, C.matches) : 'sem partidas', `${N(C.laggy)} de ${N(C.matches)} partidas`)}
        ${k('Kicks', C.matches >= 10 ? `${per100(kicks, C.matches)}<small>/100 partidas</small>` : N(kicks), `${N(kicks)} em ${N(C.matches)} partidas · ${N(kc.ban_seen || 0)} bans · ${N(kc.vpn_seen || 0)} VPN`)}
        ${T.leakBad + T.leakOk ? k('Teste de fugas falhado', N(T.leakBad), `em ${N(T.leakBad + T.leakOk)} ${T.leakBad + T.leakOk === 1 ? 'teste' : 'testes'} (Run leak test)`, T.leakBad ? 'bad' : 'good') : k('Teste de fugas', '<span class="dim">não testado</span>', 'ninguém carregou em Run leak test')}
        ${k('O IP de casa voltou', N(T.homeBack), 'devia ir pelo telemóvel', T.homeBack ? 'bad' : '')}
        ${k('Erros escritos no Log', per100(T.logErr, T.sessions) ? `${per100(T.logErr, T.sessions)}<small>/100</small>` : N(T.logErr), `${N(T.logErr)} em ${N(T.sessions)} sessões`)}
      </div><p class="note">Fuga e IP de casa: a vermelho com 1 ou mais, nunca escondidos. Por 100 só com 10 sessões ou partidas ou mais.</p>`, 's12')}
      ${box('Por versão', `${periodTxt(D)} · taxas com o n`, [['e', 'versionMetrics']], vs.length ? `<div class="tw"><table class="t"><thead><tr><th>Versão</th><th class="n">Inst.-dia</th><th class="n">Sessões</th><th class="n">Crash /100</th><th class="n">Sem fecho /100</th><th class="n">Erros /100</th><th class="n">Log /100</th><th class="n">IP novo</th><th class="n">Lag</th><th class="n">Arranque</th><th class="n">Até Protected</th></tr></thead><tbody>${vs.map((v) => { const m = met[v]; const r = (a) => per100(a, m.sessions) || '<span class="dim">-</span>';
        return `<tr><td>${esc(short(v))}</td><td class="n">${N(m.days)}</td><td class="n">${N(m.sessions)}</td><td class="n">${r(m.crashes)}</td><td class="n">${r(m.unclean)}</td><td class="n">${r(m.errors)}</td><td class="n">${r(m.logErr)}</td><td class="n">${m.ipNew[1] ? share(m.ipNew[0], m.ipNew[1]) : '-'}</td><td class="n">${m.lag[1] ? share(m.lag[0], m.lag[1]) : '-'}</td><td class="n">${esc(medianBand(m.startup) || '-')}</td><td class="n">${esc(medianBand(m.toProtected) || '-')}</td></tr>`; }).join('')}</tbody></table></div><p class="note">Por 100 sessões só com 10 ou mais sessões. Arranque e até Protected: a banda do meio. Comparar duas versões lado a lado: Sistema e dados.</p>` : empty('Nenhuma versão no período.'), 's12')}
      ${box('Erros', 'por tipo e sítio no código', [['e', 'errors[]'], ['e', 'errorsRecent']], errs.length ? `<div class="tw"><table class="t"><thead><tr><th>Erro</th><th class="hm">Onde</th><th>Versão</th><th class="n">Inst.-dia</th><th class="n">Vezes</th><th class="n">Novas no 1.º dia <small class="dim">7 d</small></th><th>1.º visto</th><th>14 dias</th></tr></thead><tbody>
        ${errs.map((e) => { const tr = trend(e.perDay); return `<tr><td>${esc(e.type)}${e.fresh ? '<span class="newtag">novo</span>' : ''}</td><td class="mono hm">${esc(e.at)}</td><td>${esc(short(e.app))}</td><td class="n">${N(e.days)}</td><td class="n">${N(e.n)}</td>${(() => { const v = e.r7 && (e.r7.by_version || []).find((x) => x.version === e.app); return `<td class="n ${v && v.new_installs_day1 ? 'bad' : ''}">${v ? N(v.new_installs_day1) : '<span class="dim">-</span>'}</td>`; })()}<td>${e.first ? `${dd(e.first.day)} · ${esc(short(e.first.app))}` : ''}</td><td>${isSmall(tr) ? nums(tr, '14 dias, um número por dia') : spark(tr, 84, 18, e.fresh ? 'bad' : 'acc')}</td></tr>`; }).join('')}</tbody></table></div><p class="note">Novo: visto pela 1.ª vez neste período (procura nos últimos 90 dias). Novas no 1.º dia: instalações novas que tiveram este erro no dia em que instalaram (últimos 7 dias).</p>` : empty('Nenhum erro no período.'), 's8')}
      ${box('Crashes', `por dia, ${series.length} dias`, [['e', 'days.crashes'], ['e', 'crashPlaces']], `${isSmall(series.map((d) => d.crashes)) ? nums(series.map((d) => d.crashes)) : mini(series.map((d) => d.crashes))}<div class="xax dim" style="display:flex;justify-content:space-between;font-size:10.5px;margin-top:2px"><span>${dshort(series[0].date)}</span><span>${dshort(series.at(-1).date)}</span></div><h3>Onde</h3>${R.crashPlaces.length ? `<div class="tw"><table class="t"><tbody>${R.crashPlaces.map((c) => `<tr><td class="mono">${esc(c.at)}</td><td>${esc(short(c.app))}</td><td class="n">${N(c.n)}</td></tr>`).join('')}</tbody></table></div>` : '<p class="note">Nenhum crash no período.</p>'}`, 's4')}
      ${box('Trocas de IP', `${N(ipAll)} ${ipAll === 1 ? 'troca' : 'trocas'}`, [['e', 'days.ip*'], ['e', 'phone.method']], `${ipAll ? bl([['IP novo', T.ipNew, null, 'good'], ['Ficou o mesmo IP', T.ipSame, null, 'warn'], ['Sem rede', T.ipNone, null, 'bad']], { pct: true }) : empty('Nenhuma troca no período.')}<h3>Método</h3>${sumN(P.method) ? bl(P.method.map((x) => [x.k, x.n]), { pct: true }) : empty('Sem método registado.')}`, 's4')}
      ${box('Quanto demora a troca', 'por ligação', [['e', 'phone.duration']], linkTab(P.duration, 'Sem tempos de troca no período.'), 's4')}
      ${box('Qualidade depois da troca', 'o teste da app', [['e', 'phone.quality'], ['e', 'phone.attempts']], `${qAll ? bl([['Boa', qy.boa, null, 'good'], ['Razoável', qy.razoavel, null, 'warn'], ['Má', qy.ma, null, 'bad']], { pct: true }) : empty('Sem testes depois da troca.')}<h3>Tentativas até ficar boa</h3>${bandBl(P.attempts, 'Sem tentativas registadas.')}<p class="note">Trocaram outra vez por jitter: ${N(qy.retry)} ${qy.retry === 1 ? 'vez' : 'vezes'}.</p>`, 's4')}
      ${box('Partidas com lag', 'por ligação e servidor', [['e', 'cs2.lag']], C.matches ? `<div class="tw"><table class="t"><thead><tr><th></th><th class="n">Valve</th><th class="n">Comunitário</th></tr></thead><tbody><tr><td>USB</td>${lagC(C.lag.usb.valve)}${lagC(C.lag.usb.community)}</tr><tr><td>Hotspot</td>${lagC(C.lag.hotspot.valve)}${lagC(C.lag.hotspot.community)}</tr></tbody></table></div><p class="note">Cor só com 30 partidas ou mais.</p>` : empty('Nenhuma partida no período.', 'As partidas chegam do resumo de rede do CS2, com a ligação USB ou hotspot.'), 's4')}
      ${box('Ping por partida', 'P50 em ms', [['e', 'cs2.ping']], linkTab(C.ping), 's4')}
      ${box('Jitter', 'envio e receção, ms', [['e', 'cs2.jitter'], ['e', 'cs2.jitterDown']], `<h3>Envio</h3>${linkTab(C.jitter)}<h3>Receção</h3>${sumN(C.jitterDown.map((x) => ({ n: (x.usb || 0) + (x.hotspot || 0) }))) || !sumN(C.jitter.map((x) => ({ n: (x.usb || 0) + (x.hotspot || 0) }))) ? linkTab(C.jitterDown) : `<p class="note">${nm('As versões destas partidas não mandam o jitter de receção (jitd_)')}: há partidas, mas sem jitter de receção.</p>`}`, 's4')}
      ${box('Mensagens atrasadas e pacotes perdidos', 'por partida', [['e', 'cs2.late'], ['e', 'cs2.loss']], `<h3>Mensagens atrasadas</h3>${bandBl(C.late, 'Sem partidas.')}<h3>Pacotes perdidos</h3>${bandBl(C.loss, 'Sem partidas.')}`, 's4')}
      ${box('Rejoin, kicks, bans e VPN', '', [['e', 'cs2.rejoin'], ['e', 'counters']], `<div class="kv2"><span>Rejoin voltou a entrar</span><span>${C.rejoin.tries ? share(C.rejoin.back, C.rejoin.tries) : '-'} <small class="dim">${N(C.rejoin.back)} de ${N(C.rejoin.tries)}</small></span><span>À espera de vaga</span><span>${N(C.rejoin.waits)}</span><span>Kick (servidor cheio, AFK…)</span><span>${N(kicks)}</span><span>VPN visto pelo servidor</span><span>${N(kc.vpn_seen || 0)}</span><span>Ban marcado</span><span class="${kc.ban_seen ? 'bad' : ''}">${N(kc.ban_seen || 0)}</span></div><p class="note">Kicks, bans e VPN são contadores da app; as partidas vêm do resumo de rede do CS2 (podem não bater).</p>`, 's4')}
      ${box('Quedas do telemóvel', `por sessão`, [['e', 'phone.drops'], ['e', 'phone.data']], `${bandBl(P.drops, 'Sem sessões fechadas no período.')}<h3>Dados móveis por dia</h3>${bandBl(P.data, 'Sem dados móveis no período.')}<p class="note">Aviso do cabo USB: ${N(P.cableHints)} ${P.cableHints === 1 ? 'vez' : 'vezes'}.</p>`, 's4')}
      ${box('Arranque e Protected', 'sessões', [['e', 'sessions.startup'], ['e', 'sessions.toProtected']], `<h3>Até a app estar pronta</h3>${bandBl(R.sessions.startup, 'Sem arranques medidos.')}<h3>Do arranque a Protected</h3>${bandBl(R.sessions.toProtected, 'Nenhuma sessão chegou a Protected.')}`, 's4')}
      ${box('Atualizações', 'o aviso de atualização da app', [['e', 'updates']], `${bl([['Viram o aviso', U.seen], ['Aceitaram', U.accepted], ['Concluíram', U.done, null, 'good'], ['Recusaram', U.declined, null, 'warn'], ['Falharam', U.failed, null, 'bad']])}<h3>Vieram de</h3>${(U.from || []).length ? bl(U.from.map((x) => [short(x.v), x.n, null, 'dim'])) : '<p class="note">Nenhuma atualização concluída no período.</p>'}`, 's4')}
    </div>`;
  }

  // ------------------------------------------------ Aquisição: de onde vêm
  function pAcq(D) {
    const G = D.G, ok = ghOk(G), R = D.R, SI = R.site || { views: 0, downloads: 0, days: [], pages: [], refs: [], countries: [] }, GP = ghPeriod(D), sOk = siteOk(D);
    const rel = ok && G.releases ? G.releases.filter((r) => !r.draft) : null;
    const TR = (G && G.history && G.history.traffic) || { days: [], refs: [], paths: [] };
    const tv = TR.days.filter((x) => x.day >= R.days[0].date);
    const views = ok && G.views ? G.views : null;
    const k = (label, v, sub, na) => `<div class="kc${na ? ' none' : ''}"><div class="kl">${label}</div><div class="kv${na ? ' na' : ''}">${v}</div><div class="kd">${sub}</div></div>`;
    const problems = (G && G.problems) || [];
    const tvSum = tv.reduce((s, x) => s + x.views, 0), tvU = tv.reduce((s, x) => s + x.uniques, 0);
    const out = [rulesBand(D, 'aquisicao')];
    if (!ok) out.push(`<div class="s12 errbox"><b>GitHub sem resposta</b><span>${problems.length ? esc(problems.slice(0, 3).join(' · ')) : 'O /api/github não respondeu'}. Estrelas e downloads ficam sem dados, não são zeros.</span>${src('x', 'github.problems')}</div>`);
    else if (problems.length) out.push(`<div class="s12 errbox"><b>GitHub em parte</b><span>${esc(problems.slice(0, 3).join(' · '))}. Sem o token o tráfego (visitas, de onde vêm) não é guardado e o GitHub só o mostra 14 dias: o pico dos vídeos perde-se.</span>${src('x', 'github.problems')}</div>`);
    out.push(box('Resumo', periodTxt(D), [['e', 'site'], ['e', 'github.history'], ['x', 'GitHub']], `<div class="kstrip">
        ${k('Visitas ao site', sOk ? N(SI.views) : 'sem dados', sOk ? `antes ${N(SI.prevViews)} · sem cookies e sem ID` : 'a tabela site não existe', !sOk)}
        ${k('Cliques em Download', sOk ? N(SI.downloads) : 'sem dados', sOk ? (SI.views ? `${share(SI.downloads, SI.views)} das visitas` : 'sem visitas') : 'vem com o contador do site', !sOk)}
        ${k('Downloads do exe', GP.downloads != null ? N(GP.downloads) : 'sem dados', GP.downloads != null ? `${GP.covered} de ${R.period} dias com retrato` : 'sem dois retratos seguidos', GP.downloads == null)}
        ${k('Estrelas', ok ? N(G.stars) : 'sem dados', GP.hist.length ? `${GP.stars >= 0 ? '+' : ''}${N(GP.stars)} no período` : 'sem retratos', !ok)}
        ${k('Visitas ao GitHub', tv.length ? N(tvSum) : views ? N(views.count) : 'precisa de token', tv.length ? `${N(tvU)} únicos somados por dia · guardado` : views ? `${N(views.uniques)} únicos · 14 dias` : 'o tráfego só com o token', !tv.length && !views)}
        ${k('Google', NM, 'precisa do Search Console', true)}
      </div>`, 's12'));
    const merge = (l) => { const m = new Map(); for (const r of l) { const k = host(r.k); const x = m.get(k) || m.set(k, { k, n: 0, u: r.u == null ? null : 0 }).get(k); x.n += r.n; if (r.u != null) x.u = (x.u || 0) + r.u; } return [...m.values()].sort((a, b) => b.n - a.n); };
    const refsS = merge(sOk ? SI.refs : []), refsG = merge(ok && G.referrers ? G.referrers.map((r) => ({ k: r.name, n: r.n, u: r.u })) : TR.refs.map((r) => ({ k: r.name, n: r.n, u: r.u })));
    const hosts = [...new Set([...refsS.map((r) => r.k), ...refsG.map((r) => r.k)])];
    out.push(box('De onde vêm', 'duas fontes lado a lado, nunca somadas', [['e', 'site.refs'], ['x', 'GitHub referrers']], hosts.length ? `<div class="tw"><table class="t"><thead><tr><th>Origem</th><th class="n">Visitas ao site <small class="dim">${periodTxt(D)}</small></th><th class="n">Visitas ao GitHub <small class="dim">14 dias${!G || !G.referrers ? (TR.refsDay ? `, guardado a ${dd(TR.refsDay)}` : '') : ''}</small></th></tr></thead><tbody>${hosts.slice(0, 15).map((h) => { const s = refsS.find((r) => r.k === h), g = refsG.find((r) => r.k === h); return `<tr><td>${esc(h)}</td><td class="n">${s ? N(s.n) : '<span class="dim">-</span>'}</td><td class="n">${g ? `${N(g.n)}${g.u != null ? ` <small class="dim">${N(g.u)} ún.</small>` : ''}` : '<span class="dim">-</span>'}</td></tr>`; }).join('')}</tbody></table></div><p class="note">O site só guarda o nome do site de origem; "direto" é quem não diz de onde vem. Os do GitHub precisam do token${refsG.length ? '' : ' (ainda nenhum guardado)'}.</p>` : empty('Ainda sem origens.', 'O site conta-as quando o contador está publicado; o GitHub precisa do token.'), 's6'));
    // Vídeos: 3 dias depois contra 3 dias antes, em dias inteiros (as somas do site são por dia). Não é atribuição.
    const vids = ((G && G.annotations) || []).filter((a) => a.kind === 'video');
    const H = D.H.period >= R.period ? D.H : R;
    const first = H.days[0].date, siteFrom = H.site && H.site.since;
    const win = (from, to) => { if (from < first) return null; const ds = H.days.filter((d) => d.date >= from && d.date <= to); const sd = (H.site && H.site.days) || []; const s = (k2) => (siteFrom && from >= siteFrom ? sd.filter((d) => d.date >= from && d.date <= to).reduce((a, d) => a + d[k2], 0) : null);
      const ex = GP.hist.filter((x) => x.exeDay != null && addDays(x.day, -1) >= from && addDays(x.day, -1) <= to); return { visits: s('views'), clicks: s('downloads'), exe: ex.length ? ex.reduce((a, x) => a + x.exeDay, 0) : null, fresh: ds.reduce((a, d) => a + d.fresh, 0), days: ds.length }; };
    out.push(box('Vídeos', '3 dias depois contra os 3 antes · não é atribuição', [['e', 'annotations (src/annotations.js)']], vids.length ? `<div class="tw"><table class="t"><thead><tr><th>Vídeo</th><th>Publicado</th><th class="n">Visitas</th><th class="n">Cliques</th><th class="n">Downloads</th><th class="n">Novas</th></tr></thead><tbody>${vids.slice().reverse().map((v) => { const a = win(v.date, addDays(v.date, 2)) || { visits: null, clicks: null, exe: null, fresh: null }, b = win(addDays(v.date, -3), addDays(v.date, -1)); const c = (k2) => `${a[k2] == null ? '<span class="dim">sem dados</span>' : N(a[k2])} <small class="dim">antes ${!b ? 'fora dos dias lidos' : b[k2] == null ? 'sem dados' : N(b[k2])}</small>`; return `<tr><td>${v.url ? `<a href="${esc(v.url)}" rel="noreferrer" target="_blank">${esc(v.label)}</a>` : esc(v.label)}</td><td>${dd(v.date)}</td><td class="n">${c('visits')}</td><td class="n">${c('clicks')}</td><td class="n">${c('exe')}</td><td class="n">${c('fresh')}</td></tr>`; }).join('')}</tbody></table></div><p class="note">Janelas fixas para comparar vídeos entre si (dia da publicação e os 2 seguintes, contra os 3 antes). Só dentro dos 30 dias lidos; antes de o contador do site existir, "sem dados".</p>`
      : empty('Nenhum vídeo anotado.', 'Quando sair um, uma linha em worker/stats/src/annotations.js (data, video, título, link) põe-no aqui e como risco nas séries.'), 's6'));
    const sdays = (SI.days || []).slice(-Math.max(R.period, 1));
    out.push(sOk ? box('Visitas ao site por dia', `${N(SI.views)} visitas · ${N(SI.downloads)} cliques em Download`, [['e', 'site.days']], dbars(sdays.map((d) => ({ date: d.date, v: d.views })), [['v', 'visitas', 'f']], marksOf(D), { todayPartial: true }) + '<p class="note">Uma visita é uma página aberta (sem cookies e sem ID). Quem tem Do Not Track ou Global Privacy Control fica de fora. Dias em UTC.</p>', 's8')
      : box('Visitas ao site por dia', '', [['e', 'site.days']], empty('Ainda sem o contador do site.', 'A tabela site ainda não existe na D1.'), 's8 dashed'));
    // Por versão publicada: total, 1.º dia e 7 primeiros dias dos retratos, e as instalações nessa versão
    const BR = (G && G.history && G.history.byRelease) || {};
    const used = new Map(R.versions.map((v) => [short(v.k), v.n]));
    const relRows = (rel || []).slice(0, 10).map((r) => {
      const pics = (BR[r.tag] || []).slice().sort((a, b) => (a.day < b.day ? -1 : 1));
      const at = (d) => { const p = pics.filter((x) => x.day <= d).at(-1); return p ? p.n : null; };
      const first = pics[0] && r.date && pics[0].day <= addDays(r.date, 1) ? at(addDays(r.date, 1)) : null;
      const s7 = pics[0] && r.date && pics[0].day <= addDays(r.date, 1) && pics.at(-1).day >= addDays(r.date, 7) ? at(addDays(r.date, 7)) : null;
      const v = short(r.tag.replace(/^v/, ''));
      return `<tr><td>${esc(v)}</td><td>${r.date ? dd(r.date) : ''}</td><td class="n">${N(r.exe)}</td><td class="n">${first == null ? '<span class="dim">-</span>' : N(first)}</td><td class="n">${s7 == null ? '<span class="dim">-</span>' : N(s7)}</td><td class="n">${used.has(v) ? N(used.get(v)) : '<span class="dim">0</span>'}</td></tr>`;
    }).join('');
    out.push(box('Por versão publicada', 'downloads do exe e uso no período', [['x', 'GitHub releases'], ['e', 'github.history.byRelease'], ['e', 'versions']], rel && rel.length ? `<div class="tw"><table class="t"><thead><tr><th>Versão</th><th>Saiu</th><th class="n">Total</th><th class="n">Até ao dia seguinte</th><th class="n">7 primeiros dias</th><th class="n">Inst.-dia no período</th></tr></thead><tbody>${relRows}</tbody></table></div><p class="note">Total de sempre (inclui os teus e os scanners). 1.º dia e 7 dias só quando há retrato desde o dia em que saiu.</p>` : empty('GitHub sem resposta.'), 's6'));
    if (sOk) {
      out.push(box('Páginas do site', 'visitas e cliques em Download', [['e', 'site.pages']], SI.pages.length ? `<div class="tw"><table class="t"><thead><tr><th>Página</th><th class="n">Visitas</th><th class="n">Download</th><th class="n">Taxa</th></tr></thead><tbody>${SI.pages.map((p) => `<tr><td class="mono">${esc(p.k)}</td><td class="n">${N(p.views)}</td><td class="n">${N(p.downloads)}</td><td class="n">${p.views ? share(p.downloads, p.views) : '<span class="dim">-</span>'}</td></tr>`).join('')}</tbody></table></div>` : empty('Nenhuma visita no período.'), 's6'));
      out.push(box('Países do site', 'visitas', [['e', 'site.countries']], bl(SI.countries.map((c) => [COUNTRY[c.k] || c.k, c.n]), { pct: true, none: 'Nenhuma visita no período.' }), 's3'));
    }
    const paths = ok && G.paths && G.paths.length ? G.paths : TR.paths;
    out.push(box('Páginas mais vistas no GitHub', '14 dias', [['x', 'GitHub traffic/popular']], paths && paths.length ? bl(paths.map((p) => [p.name.replace(`/${(G && G.repo) || ''}`, '') || '/', p.n])) : empty('Precisa do token.'), 's3'));
    const hd = GP.hist.filter((x) => x.day >= addDays(R.asOf, -(Math.max(R.period, 14) - 1)));
    out.push(GP.hist.length ? box('Downloads do exe por dia', 'dos retratos diários do GitHub', [['e', 'github.history']], dbars(hd.map((x) => ({ date: x.day, e: x.exeDay || 0 })), [['e', 'downloads', 'f']], marksOf(D)) + `<p class="note">A diferença entre dois retratos seguidos (03:07 UTC). 1.º retrato a ${dd(GP.hist[0].day)}.</p>`, 's6')
      : box('Downloads do exe por dia', '', [['e', 'github.history']], empty('Ainda sem retratos diários.', 'O cron do stats-dashboard tira um por dia, sem token.'), 's6 dashed'));
    out.push(tv.length ? box('Visitas ao GitHub por dia', 'guardadas pelo cron (ficam além dos 14 dias)', [['e', 'github.history.traffic']], dbars(tv.map((d) => ({ date: d.day, u: d.uniques, r: Math.max(0, d.views - d.uniques) })), [['u', 'únicos', 'f'], ['r', 'repetidas', 'b']], marksOf(D)), 's6')
      : views && views.days && views.days.length ? box('Visitas ao GitHub por dia', '14 dias, ao vivo (ainda não guardadas)', [['x', 'GitHub traffic/views']], dbars(views.days.map((d) => ({ date: d.day, u: d.u, r: Math.max(0, d.n - d.u) })), [['u', 'únicos', 'f'], ['r', 'repetidas', 'b']]), 's6')
        : box('Visitas ao GitHub por dia', '', [['x', 'GitHub traffic']], empty('Precisa do token.', 'Com o GITHUB_TOKEN o cron guarda as visitas de cada dia, de onde vêm e as páginas; sem ele perdem-se ao fim de 14 dias.'), 's6 dashed'));
    return `${title('Aquisição', `de onde vêm · ${periodTxt(D)}; o tráfego do GitHub ao vivo é sempre de 14 dias`)}
    <div class="g">${out.join('')}</div>`;
  }

  // ------------------------------------------------ Na app (como na v3, mais regiões, listas e "fora do mapa" por tipo)
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
    const nameAt = (c, r) => btnAt(S, c, r) || 'fora dos botões';
    const dots = total < 30;
    const m = Math.max(1, ...h.c.map((x) => x[2]));
    const pos = (c, r) => ({ x: ((X0 + c * cw) / W) * 100, y: ((r * chh) / H) * 100, w: (cw / W) * 100, hh: (chh / H) * 100 });
    const cells = h.c.map(([c, r, n]) => {
      const p = pos(c, r), name = nameAt(c, r);
      if (dots) return `<div class="cell" style="left:${p.x}%;top:${p.y}%;width:${p.w}%;height:${p.hh}%" data-tip="${N(n)} ${n === 1 ? 'clique' : 'cliques'} · ${esc(name)}"><span class="dot">${n}</span></div>`;
      const a = 0.04 + (0.8 * n) / m;
      return `<div class="hot" style="left:${p.x - p.w * 0.6}%;top:${p.y - p.hh * 0.6}%;width:${p.w * 2.2}%;height:${p.hh * 2.2}%;background:radial-gradient(circle, rgba(255,241,184,${a}) 0, rgba(240,90,79,${a * 0.6}) 45%, transparent 70%)"></div><div class="cell" style="left:${p.x}%;top:${p.y}%;width:${p.w}%;height:${p.hh}%" data-tip="${N(n)} cliques · ${esc(name)}"></div>`;
    }).join('');
    const rings = [...h.d.map((x) => [...x, 'd']), ...h.r.map((x) => [...x, 'r'])].map(([c, r, n, kind]) => {
      const p = pos(c, r);
      return `<div class="ring ${kind}" style="left:${p.x}%;top:${p.y}%;width:${p.w}%;height:${p.hh}%" data-tip="${N(n)} ${kind === 'd' ? (n === 1 ? 'clique sem efeito' : 'cliques sem efeito') : (n === 1 ? 'clique repetido' : 'cliques repetidos')} · ${esc(nameAt(c, r))}"></div>`;
    }).join('');
    const dN = h.d.reduce((s, x) => s + x[2], 0), rN = h.r.reduce((s, x) => s + x[2], 0);
    const map = `<div class="map" style="background-image:url(/ui/${S.id}.png)"><div class="dim"></div>${cells}${rings}${total || dN || rN ? '' : '<div class="mapmsg">Sem cliques neste ecrã no período.</div>'}</div>`;
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
    // Fora do mapa, por tipo: bandeja, navegação, diálogos, Phone setup, atalhos, atualização, o resto
    const KIND = [['Bandeja', /^Tray_/], ['Navegação', /^Nav/], ['Diálogos', /^Dialog/], ['Phone setup', /^(Setup_|PhoneHelp)/], ['Atalhos', /^Hotkey_/], ['Atualização', /^Update/], ['Fechar', /^Close/]];
    const groups = {};
    for (const [k2, b] of Object.entries(R.buttons || {})) { if (mapped.has(k2)) continue; const g = (KIND.find(([, re]) => re.test(k2)) || ['Outros'])[0]; (groups[g] || (groups[g] = [])).push([k2, b.n]); }
    const C = R.cs2, sizes = (R.sizes || []).filter((z) => z.bands.length);
    return `${title('Na app', `o que clicam e por onde andam · ${periodTxt(D)}`)}
    <div class="g">${rulesBand(D, 'app')}
      ${box('Onde clicam', `${esc(S.name)} · ${N(total)} ${total === 1 ? 'clique' : 'cliques'}`, [['e', `heat.${S.id}`]], `<div class="tabs">${tabs.map((s) => `<button data-tab="${s.id}" aria-pressed="${s.id === S.id}">${esc(s.name)}<span class="c">${N(totalOf(s.id))}</span></button>`).join('')}</div>${map}
        <p class="note">${dots ? 'Menos de 30 cliques neste ecrã: o número em cada célula, sem mancha de calor.' : 'Mapa de calor: 30 ou mais cliques neste ecrã.'}${dN || rN ? ` Círculos: ${N(dN)} sem efeito (tracejado) e ${N(rN)} repetidos (cheio).` : ''} Grelha de 24 × 16 sobre a captura da app; só segue o filtro de versão.</p>`, 's8')}
      <div class="s4 stack">${box(`Botões do ${esc(S.name)}`, 'vezes · instalações-dia', [['e', 'buttons']], bl(btns, { extra: (u) => `${N(u)} inst.-dia${UD >= 10 ? ` · ${Math.round((100 * u) / UD)}%` : ''}`, none: 'Nenhum botão deste ecrã clicado no período.' }) + (rare.length ? `<h3>Quase não se usa <small class="dim">3% das instalações-dia ou menos</small></h3><div class="chips">${rare.map((x) => `<span>${esc(x)}</span>`).join('')}</div>` : UD < 30 ? `<p class="note">Poucos dados para dizer o que não se usa (${N(UD)} instalações-dia; a partir de 30).</p>` : '') + `<h3>Atalho contra rato</h3><div class="tw"><table class="t"><thead><tr><th>Ação</th><th class="n">Atalho</th><th class="n">Rato</th></tr></thead><tbody>${shortcuts.map(([a2, kb, ms]) => `<tr><td>${a2}</td><td class="n">${N(kb)}</td><td class="n">${N(ms)}</td></tr>`).join('')}</tbody></table></div>`)}
        ${box('1.º ecrã', 'o primeiro que abrem no 1.º arranque', [['e', 'firstScreen']], fsTot ? bl(R.firstScreen.map((x) => [scrName(x.screen), x.n]), { pct: true }) : empty('Nenhuma instalação nova com 1.º ecrã no período.'), 'grow')}</div>
      ${thisScreen(D, S, total, dN, rN, UD)}
      ${box('Até onde fazem scroll', `${esc(S.name)}`, [['e', `scroll.${S.id}`]], scrollBox(R, S), 's4')}
      ${box('Tempo por ecrã', 'minutos com a janela à vista', [['e', 'screens.minutes']], bl(scr, { fmt: mins, pct: true, none: 'Ainda sem minutos por ecrã.' }) + `<p class="note">${mins(visible)} à vista de ${mins(appMin)} com a app aberta.</p>`, 's4')}
      ${box('Caminhos', 'de → para', [['e', 'paths']], R.paths.length ? `<div class="tw"><table class="t"><thead><tr><th>De</th><th>Para</th><th class="n">Vezes</th></tr></thead><tbody>${R.paths.slice(0, 12).map((p) => `<tr><td>${esc(scrName(p.from))}</td><td>${esc(scrName(p.to))}</td><td class="n"><span class="bar" style="width:${Math.max(2, (40 * p.n) / pathsMax)}px"></span>${N(p.n)}</td></tr>`).join('')}</tbody></table></div>` : empty('Nenhum caminho no período.'), 's4')}
      ${box('Cliques fora do mapa', 'por tipo', [['e', 'buttons (sem ui-map)']], Object.keys(groups).length ? `<div class="envg">${Object.entries(groups).map(([g, l]) => `<div><h3>${esc(g)}</h3>${bl(l.sort((a, b) => b[1] - a[1]).slice(0, 8))}</div>`).join('')}</div>` : empty('Nenhum clique fora do mapa no período.'), 's8')}
      ${box('Definições mudadas', 'instalações-dia que mudaram cada uma', [['e', 'settings']], R.settings.length ? `<div class="chips">${R.settings.map((x) => `<span>${esc(x.name)} <b>${N(x.n)}</b>${UD >= 10 ? ` <small class="dim">${Math.round((100 * x.n) / UD)}%</small>` : ''}</span>`).join('')}</div>` : empty('Nenhuma definição mudada no período.'), 's4')}
      ${box('Regiões mais bloqueadas', `instalações-dia · de ${N(C.regionsDen)}`, [['e', 'cs2.regions']], C.regions.length ? bl(C.regions.map((r) => [`${r.code} · ${r.name}`, r.n]), { fmt: (v) => (C.regionsDen >= 10 ? `${Math.round((100 * v) / C.regionsDen)}%` : `${v} de ${C.regionsDen}`) }) : empty('Ninguém bloqueou regiões no período.'), 's6')}
      ${box('Tamanho das listas', 'instalações-dia em cada banda', [['e', 'sizes']], sizes.length ? `<div class="tw"><table class="t"><tbody>${sizes.map((z) => `<tr><td>${esc(LISTN[z.name] || z.name)}<div class="note" style="margin:2px 0 0">${z.bands.map((b) => `${esc(b.band)}: ${N(b.n)}`).join(' · ')}</div></td></tr>`).join('')}</tbody></table></div>` : empty('Nada no período.'), 's6')}
    </div>`;
  }
  function sessBox(R) {
    const S = R.sessions, st2 = S.auto + S.hand;
    const vis = Object.values(R.screens || {}).reduce((a, x) => a + x.minutes, 0), all = R.days.reduce((a, d) => a + d.minutes, 0);
    return box('Sessões', `${N(S.starts)} ${S.starts === 1 ? 'arranque' : 'arranques'} da app`, [['e', 'sessions']],
      `<h3>Quanto duram</h3>${bandBl(S.len, 'Sem sessões fechadas no período.')}
      <h3>Como arranca</h3>${st2 ? bl([['com o Windows', S.auto], ['à mão', S.hand]], { pct: true }) : empty('Sem arranques no período.')}
      <h3>Janela à vista</h3>${all ? bl([['à vista', Math.min(vis, all)], ['só na bandeja ou minimizada', Math.max(0, all - vis)]], { pct: true, fmt: mins }) : empty('Sem minutos no período.')}`, 's4');
  }
  function liveHist(D) {
    const P = D.L && D.L.peaks;
    if (!P || P.first == null) return box('Ao vivo por hora', 'pico de cada hora', [['e', 'live.peaks (live_hourly)']], empty('Ainda sem amostras.', 'O cron do stats-dashboard conta o ao vivo de 10 em 10 min e guarda o máximo de cada hora.'), 's12 dashed');
    const all = [...P.today, ...P.yday].map((v) => v || 0);
    const h = { today: P.today, yday: P.yday.map((v) => v || 0) };
    const days = P.days.map((x) => x.max);
    return box('Ao vivo por hora', 'pico de cada hora · hoje contra ontem', [['e', 'live.peaks (live_hourly)']],
      (isSmall(all) ? hstrip(h, P.hour, 'O máximo de instalações com a app aberta ao mesmo tempo em cada hora de Lisboa. Ponto: 0.') : hbars(h, P.hour)) + `<h3>Pico de cada dia <small class="dim">14 dias</small></h3>${nums(days.map((v) => v || 0), 'o máximo de pessoas com a app aberta ao mesmo tempo, em cada dia')}
      <div class="xax dim" style="display:flex;justify-content:space-between;font-size:10.5px;margin-top:2px"><span>${dshort(P.days[0].date)}</span><span>${dshort(P.days.at(-1).date)}</span></div>
      <p class="note">${peakTxt(D.L)} Em branco: hora sem amostra (o cron não correu), não é zero.</p>`, 's12');
  }
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
    if (!sc) return empty('Sem medição de scroll neste ecrã no período.', 'Só os ecrãs com scroll a mandam.');
    return `${bl([['passa de 25%', sc.reach[0]], ['passa de 50%', sc.reach[1]], ['passa de 75%', sc.reach[2]]], { max: 100, fmt: (v) => (sc.visits >= 10 ? `${v}%` : `${Math.round((v * sc.visits) / 100)} de ${sc.visits}`) })}<p class="note">${N(sc.visits)} ${sc.visits === 1 ? 'visita' : 'visitas'} a este ecrã com scroll medido.</p>`;
  }
  /** Sugestões de produto (as da v2 e da v3, com o seu n mínimo): vivem em "Onde encalham", "O que fazer". */
  function improve(D) {
    const R = D.R, out = [];
    const UD = R.days.reduce((a, d) => a + d.users, 0);
    for (const S of UI.screens) {
      const h = (R.heat || {})[S.id]; if (!h) continue;
      for (const [kind, min, label] of [['d', 5, 'Cliques sem efeito'], ['r', 3, 'Cliques repetidos']]) {
        const by = {};
        for (const [c, r, n] of h[kind] || []) { const name = btnAt(S, c, r) || ''; by[name] = (by[name] || 0) + n; }
        for (const [name, n] of Object.entries(by)) if (n >= min) out.push({ tone: 'warn', t: `${label} ${name ? `em ${name}` : 'numa área sem botão'} (${S.name})`, d: `${N(n)} ${kind === 'd' ? 'cliques que não fizeram nada' : 'rajadas de cliques'} no período.`, rule: `${kind === 'd' ? 'cliques sem efeito' : 'cliques repetidos'} no mesmo sítio: ${min} ou mais`, href: '#app' });
      }
    }
    const L = R.cs2.lag, nU = L.usb.valve.n + L.usb.community.n, nH = L.hotspot.valve.n + L.hotspot.community.n;
    if (nU >= 30 && nH >= 30) {
      const lu = (L.usb.valve.lag + L.usb.community.lag) / nU, lh = (L.hotspot.valve.lag + L.hotspot.community.lag) / nH;
      if (lu > 0 && lh / lu >= 1.5) out.push({ tone: 'acc', t: 'Recomenda o cabo USB no Phone setup', d: `Partidas com lag: hotspot ${Math.round(100 * lh)}% (n=${N(nH)}), USB ${Math.round(100 * lu)}% (n=${N(nU)}).`, rule: 'lag do hotspot 1,5 vezes o do USB, com 30 partidas ou mais de cada', href: '#qualidade' });
    }
    const S = R.stuck;
    if (S.n >= 10 && S.buckets[0].n / S.n >= 0.3) {
      const top = S.setup.filter((x) => x.step !== 'done' && x.open).sort((a, b) => b.open - a.open)[0];
      out.push({ tone: 'warn', t: 'Ajuda a ligar o telemóvel nos primeiros dias', d: `${N(S.buckets[0].n)} de ${N(S.n)} novas nunca viram o telemóvel.${top ? ` No Phone setup, o passo mais visto ao abrir: ${SETUPN[top.step] || top.step} (${N(top.open)}).` : ''}${S.never_opened ? ` ${N(S.never_opened)} nem abriram o Phone setup.` : ''}`, rule: '30% ou mais das novas nunca viram o telemóvel (10 ou mais novas)', href: '#encalham' });
    }
    const cr = R.checks.red[0];
    if (cr && cr.n >= 5) out.push({ tone: 'warn', t: `Explica o que fazer com "${cr.k}" a vermelho`, d: `${N(cr.n)} instalações-dia com esta linha do painel Checks a vermelho.`, rule: 'Check a vermelho em 5 instalações-dia ou mais', href: '#encalham' });
    const nb = (n) => ((R.buttons || {})[n] || { n: 0 }).n;
    const kb = nb('Hotkey_Rejoin'), ms = nb('Rejoin_Click');
    if (kb + ms >= 30 && kb / (kb + ms) >= 0.5) out.push({ tone: 'acc', t: 'Dá um atalho também ao Change IP', d: `O Change IP and rejoin faz-se ${Math.round((100 * kb) / (kb + ms))}% das vezes pelo atalho (n=${N(kb + ms)}).`, rule: 'ação feita 50% ou mais pelo atalho, 30 ou mais', href: '#app' });
    if (R.updates.declined >= 3) out.push({ tone: 'dim', t: 'Diz o que muda no aviso de atualização', d: `${N(R.updates.declined)} recusas e ${N(R.updates.failed)} falhas no período.`, rule: 'recusas de atualização: 3 ou mais', href: '#qualidade' });
    if (UD >= 30) {
      const rare = [];
      // Os botões de usar uma vez (Phone setup, instalar o ADB, Wi-Fi do telemóvel) usam-se pouco por natureza: ficam de fora
      const ONCE = /Adb|Setup|Phone|Wifi|WiFi|Install|Help/;
      for (const Sc of UI.screens) for (const b of Sc.buttons) { const r = (R.buttons || {})[b.id + '_Click'] || (R.buttons || {})[b.id] || { days: 0 }; if (!NOT_RARE.has(b.id) && !ONCE.test(b.id) && r.days / UD <= 0.03) rare.push(`${b.label} (${Sc.name})`); }
      if (rare.length) out.push({ tone: 'dim', t: 'Junta ou esconde o que quase não se usa', d: `${rare.length} ${rare.length === 1 ? 'botão usado' : 'botões usados'} em 3% das instalações-dia ou menos: ${rare.slice(0, 4).join(', ')}${rare.length > 4 ? ' e mais' : ''}.`, rule: 'botão em 3% das instalações-dia ou menos (30 ou mais); os de usar uma vez ficam de fora', href: '#app' });
    }
    const E = R.env || { lang: [], reports: 0 }, lang = (E.lang || [])[0];
    if (lang && E.reports >= 10 && lang.k !== 'en' && lang.n / E.reports >= 0.25) out.push({ tone: 'acc', t: `A 1.ª tradução da app devia ser ${LANG ? LANG.of(lang.k) || lang.k : lang.k}`, d: `${Math.round((100 * lang.n) / E.reports)}% dos relatórios de ambiente têm o Windows em ${lang.k} (n=${N(E.reports)}).`, rule: 'idioma do Windows com 25% ou mais (10 relatórios ou mais)', href: '#sistema' });
    const sc = R.scroll && R.scroll.connection;
    if (sc && sc.visits >= 30 && sc.reach[1] <= 30) out.push({ tone: 'acc', t: 'Faz o Connection caber na janela', d: `Só ${sc.reach[1]}% das visitas passam de metade do Connection (n=${N(sc.visits)}).`, rule: 'menos de 30% a passar de metade do ecrã principal, 30 visitas ou mais', href: '#app' });
    const SI = R.site;
    if (SI && SI.views >= 30 && SI.downloads / SI.views < 0.05) out.push({ tone: 'acc', t: 'O botão Download do site quase não é clicado', d: `${N(SI.downloads)} cliques em ${N(SI.views)} visitas (${Math.round((100 * SI.downloads) / SI.views)}%).`, rule: 'menos de 5% das visitas clicam em Download, 30 visitas ou mais', href: '#aquisicao' });
    const rank = { bad: 0, warn: 1, acc: 2, dim: 3, ok: 4 };
    return out.sort((a, b) => rank[a.tone] - rank[b.tone]);
  }

  // ------------------------------------------------ Sistema e dados: a saúde completa, versões, ambiente, definições
  /**
   * v5: o brief inteiro numa página, para quem faz a app: o Markdown do /api/brief tal como o agente o lê, as verificações de
   * saúde com a regra de cada uma, as anomalias com a regra e a base, e as definições. Sem contas à parte: é o mesmo JSON.
   */
  function pBrief(D) {
    const B = D.B;
    const links = '<a href="/api/brief" target="_blank" rel="noopener">JSON</a> · <a href="/api/brief?format=md" target="_blank" rel="noopener">Markdown</a> · <a href="/api/brief?definitions=0" target="_blank" rel="noopener">JSON sem definições</a>';
    if (B === undefined || D.M === undefined) return `<div class="g">${box('Brief', 'a ler…', [['b', 'brief']], empty('A ler o /api/brief…'), 's12')}</div>`;
    if (!B) return `<div class="g">${box('Brief', '', [['b', 'brief']], empty('O /api/brief não respondeu', 'Tenta outra vez daqui a um minuto.'), 's12')}</div>`;
    const sev = { bad: 0, high: 1, medium: 2, warn: 3, info: 4 };
    const an = [...B.anomalies].sort((a, b) => (sev[a.severity] ?? 9) - (sev[b.severity] ?? 9));
    const hc = B.health.checks;
    // Os valores tal como vêm no JSON (0,857 não é "1"); listas e sim/não em JSON
    const val = (c) => (c.value == null ? '—' : typeof c.value === 'number' ? `${esc(Number.isInteger(c.value) ? nf.format(c.value) : String(c.value))}${c.unit ? ' ' + esc(c.unit) : ''}` : typeof c.value === 'string' ? esc(c.value) : `<span class="mono">${esc(JSON.stringify(c.value))}</span>`);
    return title('Brief', `${esc(B.schema)} · gerado ${esc(B.generated_at)} · hoje UTC ${esc(B.window.today)} · Lisboa ${esc(B.window.lisbon_now)} · ${links}`) + `<div class="g">
      ${box('Anomalias', `${an.length} de ${B.rules_checked} regras · ordenadas por gravidade`, [['b', 'anomalies']], an.length ? `<div class="brief-wrap"><table class="t brief-t"><thead><tr><th>gravidade</th><th>tipo</th><th>id</th><th>o que diz</th><th class="r">observado</th><th class="r">esperado</th><th class="r">base</th><th>página</th><th>regra</th></tr></thead><tbody>${an.map((a) => `<tr class="${esc(a.severity)}"><td>${esc(a.severity)}</td><td>${esc(a.kind)}</td><td class="mono">${esc(a.id)}</td><td>${esc(a.text_pt || a.message)}${a.action_pt ? `<div class="dim">→ ${esc(a.action_pt)}</div>` : ''}</td><td class="r num">${a.observed == null ? '—' : esc(a.observed)}</td><td class="r num">${a.expected == null ? '—' : esc(a.expected)}</td><td class="r num">${a.base == null ? '—' : N(a.base)}</td><td>${a.page ? `<a href="${esc(a.page)}">${esc(a.page.slice(1))}</a>` : ''}</td><td class="dim">${esc(a.rule)}</td></tr>`).join('')}</tbody></table></div>` : empty('Nenhuma regra disparou'), 's12')}
      ${box('Saúde dos dados', `${esc(B.health.status)} · ${hc.length} verificações`, [['b', 'health.checks']], `<div class="brief-wrap"><table class="t brief-t"><thead><tr><th>estado</th><th>id</th><th class="r">valor</th><th>regra</th><th>impacto</th></tr></thead><tbody>${hc.map((c) => `<tr class="${esc(c.status)}"><td>${esc(c.status)}</td><td class="mono">${esc(c.id)}</td><td class="r num">${val(c)}</td><td class="dim">${esc(c.rule || '')}</td><td>${esc(c.impact || '')}</td></tr>`).join('')}</tbody></table></div>`, 's12')}
      ${box('O brief em Markdown', 'o texto que o agente lê, tal como vem', [['b', 'format=md']], `<pre class="md">${esc(D.M || '')}</pre>`, 's12')}
      ${box('Definições', `${Object.keys(B.definitions || {}).length} · as mesmas do JSON, em inglês`, [['b', 'definitions']], `<details class="box"><summary>Mostrar as ${Object.keys(B.definitions || {}).length} definições</summary><dl class="defs">${Object.entries(B.definitions || {}).map(([k2, v]) => `<dt>${esc(k2)}</dt><dd>${esc(v)}</dd>`).join('')}</dl></details>`, 's12')}
    </div>`;
  }

  function pSys(D) {
    const R = D.R, B = D.B, G = D.G;
    const fr = R.fresh || {};
    const statusOf = (s) => `<td class="st"><i class="${esc(s)}"></i></td>`;
    const RS = { not_measured: 'não medido', no_source_rows: 'sem dados', not_mature: 'ainda não', table_missing: 'tabela em falta', low_n: 'poucos dados', not_historical: 'não é histórico', no_answer: 'sem resposta' };
    const valOf = (c) => (c.id === 'tables' ? (c.missing.length ? esc(c.missing.join(', ')) : 'nenhuma') : c.value == null ? `<span class="dim">${esc(RS[c.reason] || c.reason || 'sem valor')}</span>` : Array.isArray(c.value) ? esc(c.value.join(', ') || 'nenhuma')
      : c.unit === 'min' ? esc(ago(c.value)) : typeof c.value === 'number' && c.value <= 1 && !/^(automated|excluded)/.test(c.id) ? pc(c.value) : esc(c.value));
    const STN = { ok: 'ok', warn: 'atenção', bad: 'mau' };
    const LBL = { freshness: 'Último relatório real', any_pc_freshness: 'Último relatório de qualquer PC', live_freshness: 'Último sinal ao vivo', budget_report: 'Orçamento: relatórios', budget_live: 'Orçamento: ao vivo', budget_site: 'Orçamento: site', cron_live_hourly: 'Cron de 10 min (ao vivo por hora)', github_snapshot: 'Último retrato do GitHub', github_token: 'Token do GitHub', tables: 'Tabelas em falta', truncated: 'Leituras cortadas', old_cadence_share_today: 'Relatórios de hoje de versões antigas', link_none_share_7d: 'Ligação none em 7 dias', automated_7d: 'Análises automáticas em 7 dias', exit_share_7d: 'Relatórios enviados ao fechar (7 dias)', excluded_ids: 'IDs dos teus PCs (EXCLUDE_IDS)' };
    const health = B ? `<div class="tw"><table class="t"><thead><tr><th></th><th>Verificação</th><th class="n">Valor</th><th>Regra</th><th>Nota</th></tr></thead><tbody>${B.health.checks.map((c) => `<tr>${statusOf(c.status)}<td>${esc(LBL[c.id] || c.id)}<div class="note mono" style="margin:0">${esc(c.id)}</div></td><td class="n">${valOf(c)}${c.num != null && c.den != null && c.den ? ` <small class="dim">${N(c.num)} de ${N(c.den)}</small>` : ''}${c.datacenter != null ? ` <small class="dim">${N(c.datacenter)} de rede de alojamento</small>` : ''}</td><td class="wrap dim">${esc(c.rule || '')}</td><td class="wrap">${esc(c.impact || c.note || (c.read_warnings && c.read_warnings.length ? c.read_warnings.join('; ') : ''))}</td></tr>`).join('')}</tbody></table></div><p class="note">O estado geral é o pior destes: <b class="${B.health.status === 'ok' ? 'good' : B.health.status}">${esc(STN[B.health.status])}</b>. As regras e as notas ficam em inglês, como no /api/brief que o agente lê.</p>` : empty(B === undefined ? 'A ler o brief…' : 'O brief não respondeu.');
    const bd = R.budgetDays || [];
    const budget = `<div class="tw"><table class="t"><thead><tr><th>Dia</th><th class="n">Relatórios</th><th class="n">Ao vivo</th><th class="n">Site</th><th class="n">Pedidos de apagar</th></tr></thead><tbody>${bd.slice().reverse().map((x) => `<tr><td>${dd(x.date)}</td><td class="n">${N(x.rows)} <small class="dim">${Math.round((100 * x.rows) / (fr.budget ? fr.budget.cap : 40000))}%</small></td><td class="n">${N(x.live)}</td><td class="n">${N(x.site)}</td><td class="n">${N(Math.round(x.forget / 60))}</td></tr>`).join('')}</tbody></table></div><p class="note">Linhas escritas por dia (UTC) contra os tetos: ${N(fr.budget ? fr.budget.cap : 40000)} relatórios, ${N(fr.budget ? fr.budget.liveCap : 12000)} ao vivo, ${N(fr.budget ? fr.budget.siteCap : 3000)} site. Cada pedido de "Delete my stats" custa 60 linhas. As linhas do orçamento com mais de 30 dias apagam-se.</p>`;
    // Versões: adoção, duas lado a lado e as publicadas (a página Versões da v3)
    const met = R.versionMetrics || {};
    const vs = Object.keys(met).sort(compareV).reverse();
    if (!vs.includes(st.cmpA)) st.cmpA = vs[0] || null;
    if (!vs.includes(st.cmpB)) st.cmpB = vs[1] || vs[0] || null;
    const A = met[st.cmpA], Bv = met[st.cmpB];
    const s14 = R.period < 14 ? D.H : R;
    const ad = (s14.adoption || []).slice(-Math.max(R.period, 14));
    const allV = [...new Set(ad.flatMap((x) => Object.keys(x.by)))].sort(compareV).reverse();
    const color = (v) => SHADES[allV.indexOf(v) % SHADES.length];
    const adopt = ad.some((x) => Object.keys(x.by).length) ? `<div class="adopt">${ad.map((x) => { const t = Object.values(x.by).reduce((a, n) => a + n, 0);
      return `<div class="col${t ? '' : ' zero'}" data-tip="${esc(`${dd(x.date)}: ${t ? allV.filter((v) => x.by[v]).map((v) => `${short(v)} ${N(x.by[v])}`).join(' · ') : 'ninguém'}`)}">${allV.filter((v) => x.by[v]).map((v) => `<i style="height:${(100 * x.by[v]) / t}%;background:${color(v)}"></i>`).join('')}</div>`; }).join('')}</div>
      <div class="legendrow">${allV.map((v) => `<span><i style="background:${color(v)}"></i>${esc(short(v))}</span>`).join('')}</div>` : empty('Nenhuma instalação-dia no período.');
    const rate = (a, b) => (b ? a / b : null);
    const pp = (x, n) => (x == null ? '-' : n < 10 ? `${N(Math.round(x * n))} de ${N(n)}` : `${nf.format(Math.round(1000 * x) / 10)}%`);
    const diff = (a, b, better, n1, n2) => { if (a == null || b == null) return '<td class="n dim">-</td>'; const d = a - b; const low = n1 < 30 || n2 < 30; const tone = low || !d ? 'dim' : (better === 'up' ? d > 0 : d < 0) ? 'good' : 'bad'; return `<td class="n ${tone}">${d > 0 ? '+' : d < 0 ? '−' : ''}${nf.format(Math.abs(Math.round(1000 * d) / 10))} pp</td>`; };
    const opt = (sel) => vs.map((v) => `<option value="${esc(v)}"${v === sel ? ' selected' : ''}>${esc(short(v))}</option>`).join('');
    const cmp = A && Bv ? `<div class="cmphead"><select id="cmpA" aria-label="Versão A">${opt(st.cmpA)}</select> contra <select id="cmpB" aria-label="Versão B">${opt(st.cmpB)}</select></div>
      <div class="tw"><table class="t"><thead><tr><th></th><th class="n">${esc(short(st.cmpA))}</th><th class="n">${esc(short(st.cmpB))}</th><th class="n">Diferença</th></tr></thead><tbody>
      <tr><td>Instalações-dia</td><td class="n">${N(A.days)}</td><td class="n">${N(Bv.days)}</td><td class="n dim">${A.days - Bv.days >= 0 ? '+' : '−'}${N(Math.abs(A.days - Bv.days))}</td></tr>
      <tr><td>Sessões sem crash</td><td class="n">${pp(A.sessions ? 1 - A.crashes / A.sessions : null, A.sessions)}</td><td class="n">${pp(Bv.sessions ? 1 - Bv.crashes / Bv.sessions : null, Bv.sessions)}</td>${diff(A.sessions ? 1 - A.crashes / A.sessions : null, Bv.sessions ? 1 - Bv.crashes / Bv.sessions : null, 'up', A.sessions, Bv.sessions)}</tr>
      <tr><td>Trocas com IP novo</td><td class="n">${pp(rate(...A.ipNew), A.ipNew[1])}</td><td class="n">${pp(rate(...Bv.ipNew), Bv.ipNew[1])}</td>${diff(rate(...A.ipNew), rate(...Bv.ipNew), 'up', A.ipNew[1], Bv.ipNew[1])}</tr>
      <tr><td>Partidas com lag</td><td class="n">${pp(rate(...A.lag), A.lag[1])}</td><td class="n">${pp(rate(...Bv.lag), Bv.lag[1])}</td>${diff(rate(...A.lag), rate(...Bv.lag), 'down', A.lag[1], Bv.lag[1])}</tr>
      <tr><td>Minutos por sessão <small class="dim">mediana</small></td><td class="n">${N(A.minMed)}</td><td class="n">${N(Bv.minMed)}</td><td class="n dim">${A.minMed - Bv.minMed >= 0 ? '+' : '−'}${N(Math.abs(A.minMed - Bv.minMed))} min</td></tr>
      </tbody></table></div><p class="note">Verde: a ${esc(short(st.cmpA))} está melhor. A cinzento: menos de 30 sessões, partidas ou trocas numa das duas.</p>` : empty('Precisa de uma versão com instalações-dia no período.');
    const rel = ghOk(G) && G.releases ? G.releases.filter((r) => !r.draft) : [];
    const env = R.env || {};
    const envRows = [['Windows (do relatório de ambiente)', env.windows], ['Idioma', env.lang], ['Janela', env.window], ['Escala', env.scale], ['Rede do telemóvel', env.network], ['Marca do telemóvel', env.brands], ['Android', (env.android || []).map((x) => ({ k: /^\d+$/.test(x.k) ? `Android ${x.k}` : x.k, n: x.n }))]].filter(([, r]) => r && r.length);
    const tot = (r) => r.reduce((s, x) => s + x.n, 0);
    const others = Object.entries(R.counters || {}).filter(([c]) => !SHOWN.test(c)).sort((a, b) => b[1] - a[1]);
    const defs = B ? Object.entries(B.definitions) : [];
    return `${title('Sistema e dados', `a saúde dos dados, as versões, o ambiente e o que cada número quer dizer · ${periodTxt(D)}`)}
    <div class="g">
      ${box('Saúde dos dados', B ? `brief das ${lx(Date.parse(B.generated_at)).hm} · ${B.health.checks.filter((c) => c.status !== 'ok').length} a pedir atenção` : '', [['b', 'health']], health, 's12')}
      ${box('Regras', B ? `${B.anomalies.length} de ${B.rules_checked} dispararam · últimos 7 dias, sem filtros` : '', [['b', 'anomalies']], B ? (B.anomalies.length ? `<div class="rband">${B.anomalies.map((a) => `<div class="${esc(a.severity)}"><b>${esc(a.kind === 'data' ? 'Dados' : 'Produto')}</b> · ${esc(a.text_pt)}<div class="rr">${esc(a.id)} · ${esc(a.rule)} · <a href="${esc(a.page)}">ver</a></div></div>`).join('')}</div>${B.suggested_actions.length ? `<h3>A seguir</h3><div class="kv2">${B.suggested_actions.map((s) => `<span>${s.rank}. ${esc(s.text_pt)}</span><span></span>`).join('')}</div>` : ''}` : empty('Nenhuma regra disparou.')) : empty('A ler o brief…'), 's6')}
      ${box('Orçamento da D1', '14 dias', [['e', 'budgetDays']], budget, 's6')}
      ${box('Adoção ao longo do tempo', '% das instalações abertas em cada dia, por versão', [['e', 'adoption']], adopt, 's12')}
      ${box('Comparar duas versões', periodTxt(D), [['e', 'versionMetrics']], cmp, 's7')}
      ${box('Versões publicadas', 'downloads do exe e uso', [['x', 'GitHub releases'], ['e', 'versions']], rel.length ? `<div class="tw"><table class="t"><thead><tr><th>Versão</th><th>Saiu</th><th class="n">Downloads</th><th class="n">Inst.-dia</th></tr></thead><tbody>${rel.map((r) => { const v = short(r.tag.replace(/^v/, '')); const u = R.versions.find((x) => short(x.k) === v); return `<tr><td>${esc(v)}</td><td>${r.date ? dd(r.date) : ''}</td><td class="n">${N(r.exe)}</td><td class="n">${u ? N(u.n) : '<span class="dim">0</span>'}</td></tr>`; }).join('')}</tbody></table></div>` : empty(G === undefined ? 'A ler o GitHub…' : 'GitHub sem resposta.'), 's5')}
      ${(() => { const tr = improve(D).filter((x) => x.href === '#sistema'); return tr.length ? box('Sugestão', '', [], `<div class="al">${tr.map((x) => `<div class="${x.tone}"><b>${esc(x.t)}</b><p>${esc(x.d)}</p></div>`).join('')}</div>`, 's12') : ''; })()}
      ${box('Ambiente', `${N(env.reports || 0)} ${env.reports === 1 ? 'relatório' : 'relatórios'}, um por dia, sem ID`, [['e', 'env'], ['e', 'oses']], envRows.length || (R.oses || []).length ? `<div class="envg w4">${envRows.map(([t2, rows]) => `<div><h3>${t2}</h3><div class="kv2">${rows.map((x) => `<span>${esc(x.k)}</span><span>${N(x.n)}${tot(rows) >= 10 ? ` <small class="dim">${Math.round((100 * x.n) / tot(rows))}%</small>` : ''}</span>`).join('')}</div></div>`).join('')}<div><h3>Windows (do relatório com ID)</h3><div class="kv2">${(R.oses || []).map((o) => `<span>${esc(o.k)}</span><span>${N(o.n)}</span>`).join('')}</div></div></div><p class="note">Não segue os filtros. Da marca, nunca o modelo.</p>` : empty('Nenhum relatório de ambiente no período.'), 's12')}
      ${box('Outros contadores', 'sem bloco próprio', [['e', 'counters']], others.length ? `<div class="tw"><table class="t"><tbody>${others.map(([code, n]) => `<tr><td class="mono">${esc(code)}</td><td class="n">${N(n)}</td></tr>`).join('')}</tbody></table></div>` : empty('Todos os contadores têm o seu bloco.'), 's4')}
      ${box('Definições', 'o que cada número quer dizer (as mesmas do /api/brief, em inglês)', [['b', 'definitions']], defs.length ? `<details class="box"><summary>Mostrar as ${defs.length} definições</summary><dl class="defs">${defs.map(([k2, v]) => `<dt>${esc(k2)}</dt><dd>${esc(v)}</dd>`).join('')}</dl></details>` : empty('A ler o brief…'), 's8')}
    </div>`;
  }

  // ------------------------------------------------ CSV da tabela principal da página aberta
  function csv() {
    const D = DATA; if (!D) return;
    const R = D.R;
    let rows;
    if (st.page === 'hoje') rows = [['dia', 'instalacoes', 'novas', 'minutos', 'sessoes', 'crashes', 'relatorios', 'relatorios_antigos'], ...D.H.days.map((d) => [d.date, d.users, d.fresh, d.minutes, d.sessions, d.crashes, d.reports, d.oldReports])];
    else if (st.page === 'funil') rows = [['primeiro_dia', 'novas', 'telemovel', 'protected', 'partida', 'troca_ip_novo', 'd1_base', 'd1_voltaram'], ...(R.activation.cohorts || []).map((c) => [c.day, ...c.steps, c.ip_changed, c.d1 ? c.d1.n : '', c.d1 ? c.d1.back : ''])];
    else if (st.page === 'crescimento') rows = [['dia', 'instalacoes', 'novas', 'voltaram', 'dau', 'wau'], ...R.days.map((d) => { const s = (R.stick || []).find((x) => x.date === d.date) || {}; return [d.date, d.users, d.fresh, d.users - d.fresh, s.dau ?? '', s.wau ?? '']; })];
    else if (st.page === 'encalham') rows = [['grupo', 'novas', 'parte', 'antes', 'parte_antes'], ...R.stuck.buckets.map((b) => [b.id, b.n, b.share ?? '', b.prev, b.prev_share ?? ''])];
    else if (st.page === 'app') { const S = UI.screens.find((s) => s.id === st.appTab); rows = [['ecra', 'botao', 'cliques', 'instalacoes_dia'], ...S.buttons.map((b) => { const r = R.buttons[b.id + '_Click'] || R.buttons[b.id] || { n: 0, days: 0 }; return [S.name, b.label, r.n, r.days]; })]; }
    else if (st.page === 'qualidade') rows = [['erro', 'onde', 'versao', 'vezes', 'instalacoes_dia', 'primeiro_dia'], ...R.errors.map((e) => [e.type, e.at, e.app, e.n, e.days, e.first ? e.first.day : ''])];
    else if (st.page === 'aquisicao') { const GP = ghPeriod(D); rows = [['dia', 'visitas_site', 'cliques_download', 'downloads_exe'], ...(R.site ? R.site.days : []).map((d) => [d.date, d.views, d.downloads, GP.byDay.get(d.date) ?? ''])]; }
    else { const B = D.B; rows = [['verificacao', 'estado', 'valor'], ...(B ? B.health.checks.map((c) => [c.id, c.status, Array.isArray(c.value) ? c.value.join(' ') : c.value ?? '']) : [])]; }
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
    // As ligações "#e-…" dentro de Onde encalham levam ao bloco, sem mudar de página
    for (const a of $$('#main a[href^="#e-"]')) a.onclick = (e) => { e.preventDefault(); const t = document.getElementById(a.getAttribute('href').slice(1)); if (t) t.scrollIntoView({ behavior: 'smooth', block: 'start' }); };
  }
  $('#period').onclick = (e) => { const b = e.target.closest('button'); if (b && !b.disabled) { st.period = +b.dataset.p; store.set('period', st.period); refresh(); } };
  $('#mine').onchange = (e) => { const u = new URL(location.href); if (e.target.checked) u.searchParams.set('dev', '1'); else u.searchParams.delete('dev'); location.href = u.toString(); };
  $('#srcs').onchange = (e) => { store.set('srcs', e.target.checked ? '1' : '0'); render(); };
  $('#csv').onclick = csv;
  document.addEventListener('click', (e) => { const f = $('#flt'); if (f.open && !f.contains(e.target)) f.open = false; });
  addEventListener('hashchange', () => { refresh().then(() => scrollTo(0, 0)); });
  // Ao vivo a cada minuto; o resto (e o brief) a cada 5 minutos, só com a página à vista
  setInterval(async () => {
    if (document.hidden || !DATA) return;
    try { DATA.L = await V4.live(true); } catch { return; }
    const y = scrollY; render(); scrollTo(0, y);
  }, 60_000);
  setInterval(() => { if (!document.hidden) { V4.forget(); const y = scrollY; refresh().then(() => scrollTo(0, y)); } }, 300_000);
  let rt = 0;
  addEventListener('resize', () => { clearTimeout(rt); rt = setTimeout(() => document.documentElement.style.setProperty('--hdr', $('#hdr').offsetHeight + 'px'), 160); });
  refresh();
})();
