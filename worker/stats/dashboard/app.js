// Desenha o dashboard a partir de DATA. Não gera números: em produção, load() troca makeData por fetch('/api/summary').
// Regras de leitura: unidade = instalação-dia; percentagem sempre com o n; abaixo de MIN mostra "-" e "poucos dados";
// abaixo de LOW o número fica a cinzento; ao vivo só contagens e "menos de 5"; blocos sem filtro dizem "não filtrado".
'use strict';
(() => {
  // ------------------------------------------------------------------ utilidades
  const $ = (s, el = document) => el.querySelector(s);
  const $$ = (s, el = document) => [...el.querySelectorAll(s)];
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const RM = matchMedia('(prefers-reduced-motion: reduce)');
  const MIN = 5, LOW = 30;
  const LIVE_MIN = new URLSearchParams(location.search).get('dev') === '1' ? 1 : 5;   // the developer's own test PCs: exact numbers
  const nf = (n, d = 0) => Number(n).toLocaleString('pt-PT', { minimumFractionDigits: d, maximumFractionDigits: d });
  const nfa = (v) => (v == null ? '-' : Math.abs(v) < 10 && v % 1 ? nf(v, 1) : nf(Math.round(v)));
  const pc = (v, d = 0) => (v == null ? '-' : `${nf(v, d)}%`);
  const plural = (n, one, many) => `${nf(n)} ${n === 1 ? one : many}`;
  const lc = (n) => (n > 0 && n < LIVE_MIN ? 'menos de 5' : nf(n));
  const MES = ['jan.', 'fev.', 'mar.', 'abr.', 'mai.', 'jun.', 'jul.', 'ago.', 'set.', 'out.', 'nov.', 'dez.'];
  const fdate = (iso) => { const [, m, d] = iso.split('-'); return `${+d} ${MES[m - 1]}`; };
  const hhmm = (t) => new Date(t).toISOString().slice(11, 16);
  const cssv = (n) => getComputedStyle(document.documentElement).getPropertyValue(n).trim();
  const sum = (a, f = (x) => x) => a.reduce((s, x) => s + f(x), 0);
  const store = { get(k) { try { return localStorage.getItem(k); } catch { return null; } }, set(k, v) { try { localStorage.setItem(k, v); } catch { /* sem armazenamento */ } } };
  const niceMax = (v) => { const p = 10 ** Math.floor(Math.log10(v || 1)); for (const m of [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) if (m * p >= v) return m * p; return 10 * p; };
  /** Percentagem com o n: '-' e "poucos dados" abaixo de MIN; classe 'low' abaixo de LOW. */
  function ratio(num, den, dec = 0) {
    if (!den || den < MIN) return { v: null, txt: '-', note: `poucos dados (n=${nf(den || 0)})`, cls: 'low' };
    const v = (100 * num) / den;
    return { v, txt: pc(v, dec), note: `n=${nf(den)}`, cls: den < LOW ? 'low' : '' };
  }
  const nline = (r, unit = '') => `<div class="nn">${r.v == null ? esc(r.note) : `${esc(r.note)}${unit ? ` ${esc(unit)}` : ''}`}</div>`;
  const unf = (extra = '') => `<span class="unf" data-tip="Este bloco não responde aos filtros de cima.">não filtrado${extra ? ` · ${esc(extra)}` : ''}</span>`;
  const anyFilter = () => ['version', 'country', 'mode', 'link'].some((k) => S.f[k] !== 'all');

  // ------------------------------------------------------------------ ícones (traço 1.5, como a barra da app)
  const IC = {
    grid: '<rect x="4" y="4" width="7" height="7"/><rect x="13" y="4" width="7" height="7"/><rect x="4" y="13" width="7" height="7"/><rect x="13" y="13" width="7" height="7"/>',
    pulse: '<path d="M3 12h4l2.5-6 4 12 2.5-6H21"/>',
    cursor: '<path d="M7 4l10.5 8.2-4.6.8 2.7 5.4-2.2 1.1-2.7-5.4L7 17.5z"/><path d="M15 4.5l1.3-1.6M18.6 7.6l1.9-.6M12 3.5V1.6" />',
    retention: '<path d="M4 19V5M4 19h16"/><path d="M7 8c2.5 0 3.5 5 6.5 5S17 11 20 11"/>',
    phone: '<rect x="7" y="2.8" width="10" height="18.4" rx="1.6"/><path d="M10.5 18h3"/>',
    crosshair: '<circle cx="12" cy="12" r="7"/><path d="M12 2.5v5M12 16.5v5M2.5 12h5M16.5 12h5"/>',
    shieldcheck: '<path d="M12 3l7 2.6v5.6c0 4.4-3 7.7-7 9.3-4-1.6-7-4.9-7-9.3V5.6z"/><path d="M8.8 12l2.2 2.2 4.2-4.4"/>',
    tag: '<path d="M3.5 12.2V4.5c0-.6.4-1 1-1h7.7l8.3 8.3-8.7 8.7z"/><circle cx="8" cy="8" r="1.4"/>',
    monitor: '<rect x="3" y="4.5" width="18" height="12" rx="1"/><path d="M9 20h6M12 16.5V20"/>',
    flag: '<path d="M5 21V4M5 4.5h11.5l-2.2 3.8 2.2 3.8H5"/>',
    branch: '<circle cx="7" cy="5.5" r="2"/><circle cx="7" cy="18.5" r="2"/><circle cx="17" cy="8.5" r="2"/><path d="M7 7.5v9M17 10.5c0 4-10 2.5-10 6"/>',
    shield: '<path d="M12 3l7.5 2.8v5.6c0 4.6-3.2 8-7.5 9.6-4.3-1.6-7.5-5-7.5-9.6V5.8z"/>',
    server: '<rect x="8.5" y="2.5" width="7" height="12" rx="1"/><path d="M10.5 6h3M10.5 9h3M12 14.5v3.5M5 20.5h14M9 18h6"/>',
    globe: '<circle cx="12" cy="12" r="8.5"/><path d="M3.5 12h17M12 3.5c-2.5 2.6-3.6 5.4-3.6 8.5s1.1 5.9 3.6 8.5c2.5-2.6 3.6-5.4 3.6-8.5S14.5 6.1 12 3.5z"/>',
    users: '<circle cx="9" cy="8.5" r="3.2"/><path d="M3 19.5c.6-3.4 3-5.2 6-5.2s5.4 1.8 6 5.2"/><circle cx="16.5" cy="7.5" r="2.6"/><path d="M16.5 12.5c2.4 0 4.1 1.4 4.6 4"/>',
    books: '<path d="M5 4.5v15M9 4.5v15M13.2 5l3.6 14.4M18.5 4.5v15"/>',
    person: '<circle cx="12" cy="8" r="3.8"/><path d="M5 20.5c.8-4 3.6-6.2 7-6.2s6.2 2.2 7 6.2z"/>',
    clock: '<path d="M4.2 9.5A8.5 8.5 0 1 1 3.5 12"/><path d="M3.5 5.5v4h4M12 7.5V12l3 2"/>',
    gear: '<circle cx="12" cy="12" r="3"/><path d="M12 2.8l1.6 2.3 2.7-.6.6 2.7 2.3 1.6-1.3 2.4 1.3 2.4-2.3 1.6-.6 2.7-2.7-.6L12 21.2l-1.6-2.3-2.7.6-.6-2.7-2.3-1.6 1.3-2.4-1.3-2.4 2.3-1.6.6-2.7 2.7.6z"/>',
  };
  const icon = (k) => `<svg viewBox="0 0 24 24" fill="none" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${IC[k] || ''}</svg>`;

  // ------------------------------------------------------------------ estado
  const PAGES = [
    { id: 'geral', name: 'Visão geral', icon: 'grid', desc: 'Os números do período e o que pede atenção.' },
    { id: 'vivo', name: 'Ao vivo', icon: 'pulse', desc: 'Instalações com a app aberta agora, só em contagens.' },
    { id: 'app', name: 'Na app', icon: 'cursor', desc: 'Cliques por ecrã, cliques mortos e de raiva, caminhos entre ecrãs.' }, null,
    { id: 'uso', name: 'Uso e retenção', icon: 'retention', desc: 'Instalações por dia, horas de uso, 1.º dia e coortes.' },
    { id: 'telemovel', name: 'Telemóvel e IP', icon: 'phone', desc: 'Trocas de IP, Phone setup e quedas.' },
    { id: 'cs2', name: 'CS2 e partidas', icon: 'crosshair', desc: 'Partidas, lag, ping, kicks e regiões.' },
    { id: 'fiab', name: 'Fiabilidade', icon: 'shieldcheck', desc: 'Crashes, erros, verificações, teste de fugas e atualizações.' },
    { id: 'versoes', name: 'Versões', icon: 'tag', desc: 'Adoção de cada versão e comparação entre duas.' },
    { id: 'ambiente', name: 'Ambiente', icon: 'monitor', desc: 'Windows, idioma, janela e telemóvel, de um relatório sem ID.' }, null,
    { id: 'melhorar', name: 'O que melhorar', icon: 'flag', desc: 'Regras sobre os números, ordenadas por instalações-dia afetadas.' },
    { id: 'github', name: 'GitHub', icon: 'branch', desc: 'Repositório, visitas e downloads.' },
  ];
  const S = {
    page: 'geral', screen: 'connection',
    f: { period: 7, version: 'all', country: 'all', mode: 'all', link: 'all' },
    layers: { heat: true, btn: true, dead: true, rage: true }, opacity: 0.85, heatScale: 'lin',
    cmpA: 'beta.11', cmpB: 'beta.10',
  };
  let D = null;
  const shown = new Map();

  function load() {
    // Produção: D = await (await fetch('/api/summary?' + new URLSearchParams(S.f))).json();
    D = window.realData ? realData(S.f) : makeData(S.f);
    const vs = Object.keys(D.versions.metrics);   // the comparison needs versions that exist
    if (!vs.includes(S.cmpA)) S.cmpA = vs[0];
    if (!vs.includes(S.cmpB)) S.cmpB = vs[1] || vs[0];
  }
  const periodLabel = () => (D.period === 1 ? `hoje até às ${D.now}` : `últimos ${D.period} dias`);
  const periodShort = () => (D.period === 1 ? 'hoje' : `em ${D.period} dias`);
  const prevLabel = () => (D.period === 1 ? 'contra ontem até à mesma hora' : 'contra o período antes');
  const scrName = (id) => UI.screens.find((s) => s.id === id)?.name || id;
  const UDn = () => D.totals.users;

  // ------------------------------------------------------------------ tooltip
  const tip = $('#tip');
  function placeTip(e) {
    const w = tip.offsetWidth, h = tip.offsetHeight;
    let x = e.clientX + 14, y = e.clientY + 16;
    if (x + w > innerWidth - 8) x = e.clientX - w - 12;
    if (y + h > innerHeight - 8) y = e.clientY - h - 12;
    tip.style.left = `${Math.max(8, x)}px`; tip.style.top = `${Math.max(8, y)}px`;
  }
  function showTip(html, e) { tip.innerHTML = html; tip.style.display = 'block'; placeTip(e); }
  function hideTip() { tip.style.display = 'none'; }
  document.addEventListener('mousemove', (e) => {
    if (e.target.closest && e.target.closest('[data-rich]')) return;
    const t = e.target.closest && e.target.closest('[data-tip]');
    if (t) showTip(esc(t.dataset.tip).replace(/\n/g, '<br>'), e); else hideTip();
  });
  document.addEventListener('pointerdown', (e) => {
    if (e.pointerType !== 'touch') return;
    const t = e.target.closest('[data-tip]');
    if (t && !e.target.closest('[data-rich]')) showTip(esc(t.dataset.tip).replace(/\n/g, '<br>'), e);
  });
  document.addEventListener('scroll', hideTip, { passive: true });

  // ------------------------------------------------------------------ cor das células de calor com texto legível (AA)
  const hex = (h) => { h = h.replace('#', ''); if (h.length === 3) h = [...h].map((c) => c + c).join(''); return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)); };
  const lum = ([r, g, b]) => { const f = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; }; return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b); };
  function heatCell(t) {
    const a = hex(cssv('--acc')), b = hex(cssv('--btn'));
    const m = t <= 0 ? 0 : 0.12 + t * 0.88;
    const c = a.map((v, i) => Math.round(b[i] + (v - b[i]) * m));
    const L = lum(c);
    const fg = 1.05 / (L + 0.05) >= (L + 0.05) / (lum([8, 19, 30]) + 0.05) ? '#fff' : '#08131e';
    return { bg: `rgb(${c.join(',')})`, fg };
  }

  // ------------------------------------------------------------------ peças de gráfico
  function spark(values, { w = 84, h = 22, color = 'var(--acc)', fill = false } = {}) {
    const v = values.filter((x) => x != null);
    if (v.length < 2) return '';
    const mx = Math.max(...v), mn = Math.min(...v), rg = mx - mn || 1;
    let d = '', open = false;
    values.forEach((x, i) => { if (x == null) { open = false; return; } const p = `${((i / (values.length - 1)) * (w - 2) + 1).toFixed(1)},${(h - 2 - ((x - mn) / rg) * (h - 4)).toFixed(1)}`; d += `${open ? 'L' : 'M'}${p}`; open = true; });
    return `<svg class="spark" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" aria-hidden="true"><path d="${d}" fill="none" style="stroke:${color}" stroke-width="1.5" stroke-linejoin="round"/></svg>`;
  }

  function bars(rows, o = {}) {
    const max = o.max ?? Math.max(1, ...rows.map((r) => r.v));
    const f = o.fmt || nf;
    return `<div class="bl"${o.kw ? ` style="grid-template-columns:minmax(80px,${o.kw}) minmax(40px,1fr) auto"` : ''}>${rows.map((r) => `<div class="row${r.hl ? ' hl' : ''}"${r.attr || ''} data-tip="${esc(r.tip || `${r.k}: ${f(r.v)}`)}"><span class="k">${r.kHtml || esc(r.k)}</span><span class="t"><i class="${r.cls || ''}" style="width:${r.v ? Math.max(1.5, (r.v / max) * 100) : 0}%"></i></span><span class="v${r.low ? ' low' : ''}">${r.vTxt ?? f(r.v)}${r.sub ? `<small>${esc(r.sub)}</small>` : ''}</span></div>`).join('')}</div>`;
  }
  /** Barras de partes de um todo: mostra % e o n; abaixo de MIN mostra '-'. */
  function shareBars(rows, den, o = {}) {
    return bars(rows.map((r) => { const q = ratio(r.n, den); return { k: r.k, kHtml: r.kHtml, v: q.v || 0, vTxt: q.txt, low: q.cls === 'low', sub: o.noSub ? '' : nf(r.n), cls: r.cls, tip: `${r.k}: ${nf(r.n)} de ${nf(den)}` }; }), { max: o.max ?? 100, kw: o.kw });
  }

  function cols(rows, keys, o = {}) {
    if (o.norm) { const tot = Object.fromEntries(keys.map((k) => [k.k, sum(rows, (r) => r[k.k])])); keys = keys.filter((k) => tot[k.k] >= MIN); if (!keys.length) return `<p class="note">Poucos dados (n=${nf(Math.max(0, ...Object.values(tot)))}).</p>`; rows = rows.map((r) => ({ ...r, ...Object.fromEntries(keys.map((k) => [k.k, tot[k.k] ? (100 * r[k.k]) / tot[k.k] : 0])) })); o = { ...o, fmt: (v) => pc(v), tots: tot }; }
    const max = Math.max(1, ...rows.flatMap((r) => keys.map((k) => r[k.k])));
    const f = o.fmt || nf;
    const html = `<div class="cols" style="height:${o.h || 118}px">${rows.map((r) => `<div>${keys.length === 1 || o.labels ? `<span class="vv">${keys.map((k) => f(r[k.k])).join(' · ')}</span>` : ''}<div class="bs">${keys.map((k) => `<i style="height:${(r[k.k] / max) * 100}%;background:${k.color}" data-tip="${esc(`${r.band}${keys.length > 1 ? ` · ${k.name}` : ''}: ${f(r[k.k])}`)}"></i>`).join('')}</div><small>${esc(r.band)}</small></div>`).join('')}</div>`;
    return html + (keys.length > 1 ? legend(keys.map((k) => [`${k.name}${o.tots ? ` (n=${nf(o.tots[k.k])})` : ''}`, k.color])) : '');
  }
  const legend = (items) => `<div class="legend">${items.map(([n, c, t]) => `<span><i class="${t || ''}" style="${t === 'dash' ? '' : `background:${c}`}"></i>${esc(n)}</span>`).join('')}</div>`;
  const stack = (parts) => { const t = sum(parts, (p) => p[1]); if (!t) return '<div class="stack"></div>'; return `<div class="stack">${parts.map(([n, v, c]) => `<i style="width:${(v / t) * 100}%;background:${c}" data-tip="${esc(`${n}: ${nf(v)} (${pc((100 * v) / t)})`)}"></i>`).join('')}</div>`; };

  /** Gráfico ao longo do tempo: linhas, área empilhada ou colunas empilhadas, com as versões marcadas. */
  function chart(el, o) {
    if (!el) return;
    const W = Math.max(260, Math.floor(el.clientWidth || 600)), H = o.h || 170;
    const L = o.left ?? 34, R = 8, T = o.markers === false ? 8 : 20, B = 20;
    const n = o.dates.length, pw = W - L - R, ph = H - T - B;
    const isBars = o.type === 'bars';
    const step = pw / n;
    const x = (i) => (isBars ? L + step * (i + 0.5) : L + (n > 1 ? (i / (n - 1)) * pw : pw / 2));
    const tops = o.stacked ? o.dates.map((_, i) => sum(o.series, (s) => s.values[i] || 0)) : o.series.flatMap((s) => s.values.filter((v) => v != null));
    const vmin = o.yMin ?? 0;
    const vmax = o.yMax ?? niceMax(Math.max(vmin + 1, o.floor || 0, ...tops));
    const y = (v) => T + (1 - (v - vmin) / (vmax - vmin)) * ph;
    const yf = o.yFmt || nfa;
    let s = `<svg viewBox="0 0 ${W} ${H}" height="${H}" role="img" aria-label="${esc(o.label || '')}"><g class="grid">`;
    for (const g of [vmin, (vmin + vmax) / 2, vmax]) s += `<line x1="${L}" x2="${W - R}" y1="${y(g).toFixed(1)}" y2="${y(g).toFixed(1)}"/><text x="${L - 6}" y="${(y(g) + 3.5).toFixed(1)}" text-anchor="end">${yf(g)}</text>`;
    s += '</g>';
    if (o.floor) s += `<rect x="${L}" y="${y(o.floor).toFixed(1)}" width="${pw}" height="${(y(0) - y(o.floor)).toFixed(1)}" class="floor"/><text x="${L + 6}" y="${(y(o.floor / 2) + 3.5).toFixed(1)}" class="floort">menos de ${o.floor}</text>`;
    if (o.xLabels) { o.xLabels.forEach(([i, t, a]) => { s += `<text x="${x(i).toFixed(1)}" y="${H - 5}" text-anchor="${a}">${esc(t)}</text>`; }); } else {
      const ticks = Math.min(n, W < 420 ? 3 : 5);
      for (let k = 0; k < ticks; k++) {
        const i = Math.round((k * (n - 1)) / Math.max(1, ticks - 1));
        s += `<text x="${x(i).toFixed(1)}" y="${H - 5}" text-anchor="${k === 0 && ticks > 1 ? 'start' : k === ticks - 1 && ticks > 1 ? 'end' : 'middle'}">${fdate(o.dates[i])}</text>`;
      }
    }
    if (o.markers !== false) {
      s += '<g class="rel">';
      let lastX = -99;
      for (const r of D.releases) {
        const i = o.dates.indexOf(r.date);
        if (i < 0) continue;
        const xx = isBars ? L + step * i : x(i);
        s += `<line x1="${xx.toFixed(1)}" x2="${xx.toFixed(1)}" y1="${T - 4}" y2="${H - B}"/>`;
        if (xx - lastX > 44) { const end = xx > W - 46; s += `<text x="${(end ? xx - 3 : xx + 3).toFixed(1)}" y="${T - 8}" text-anchor="${end ? 'end' : 'start'}">${esc(r.tag)}</text>`; lastX = xx; }
      }
      s += '</g>';
    }
    if (isBars) {
      const bw = Math.max(1, step * (n > 60 ? 0.78 : 0.64));
      for (let i = 0; i < n; i++) {
        let c = 0;
        for (const se of o.series) {
          const v = se.values[i] || 0;
          if (!v) continue;
          const y0 = y(c), y1 = y(c + v);
          s += `<rect x="${(x(i) - bw / 2).toFixed(1)}" y="${y1.toFixed(1)}" width="${bw.toFixed(1)}" height="${Math.max(0.6, y0 - y1).toFixed(1)}" style="fill:${se.color}"/>`;
          c += v;
        }
      }
    } else if (o.stacked) {
      let base = o.dates.map(() => 0);
      for (const se of o.series) {
        const top = base.map((b, i) => b + (se.values[i] || 0));
        const up = top.map((v, i) => `${x(i).toFixed(1)},${y(v).toFixed(1)}`);
        const back = base.map((v, i) => `${x(i).toFixed(1)},${y(v).toFixed(1)}`).reverse();
        s += `<polygon points="${up.join(' ')} ${back.join(' ')}" style="fill:${se.color}"/>`;
        // rótulo direto no fim de cada faixa com espessura suficiente
        const th = y(base[n - 1]) - y(top[n - 1]);
        if (o.direct && th > 13) s += `<text class="dl" x="${(x(n - 1) - 4).toFixed(1)}" y="${((y(base[n - 1]) + y(top[n - 1])) / 2 + 3.5).toFixed(1)}" text-anchor="end" style="fill:${se.ink || '#fff'}">${esc(se.name)}</text>`;
        base = top;
      }
    } else {
      for (const se of o.series) {
        let d = '', a = '', open = false;
        se.values.forEach((v, i) => {
          if (v == null) { if (open && se.area) a += `L${x(i - 1).toFixed(1)},${y(vmin).toFixed(1)}Z`; open = false; return; }
          const p = `${x(i).toFixed(1)},${y(v).toFixed(1)}`;
          if (!open) { d += `M${p}`; if (se.area) a += `M${x(i).toFixed(1)},${y(vmin).toFixed(1)}L${p}`; open = true; } else { d += `L${p}`; if (se.area) a += `L${p}`; }
        });
        if (open && se.area) a += `L${x(se.values.length - 1).toFixed(1)},${y(vmin).toFixed(1)}Z`;
        if (se.area) s += `<path d="${a}" style="fill:${se.color};opacity:.13"/>`;
        s += `<path d="${d}" fill="none" style="stroke:${se.color}" stroke-width="${se.w || 1.75}" stroke-linejoin="round"${se.dash ? ' stroke-dasharray="4 3"' : ''}/>`;
      }
    }
    s += `<g class="hov" style="display:none"><line y1="${T}" y2="${H - B}"/></g><rect class="hit" x="${L}" y="${T}" width="${pw}" height="${ph}" fill="transparent"/></svg>`;
    el.innerHTML = s;
    el.dataset.rich = '1';
    const svg = el.firstChild, hov = $('.hov', svg), ln = hov.firstChild;
    const f = o.fmt || nfa;
    $('.hit', svg).addEventListener('mousemove', (e) => {
      const b = svg.getBoundingClientRect();
      const px = ((e.clientX - b.left) * W) / b.width;
      const i = Math.max(0, Math.min(n - 1, isBars ? Math.floor((px - L) / step) : Math.round(((px - L) / pw) * (n - 1))));
      hov.style.display = ''; ln.setAttribute('x1', x(i)); ln.setAttribute('x2', x(i));
      const rel = D.releases.find((r) => r.date === o.dates[i]);
      const rows = o.series.filter((se) => !se.hideTip).map((se) => `<div class="tr"><span><i class="sq" style="background:${se.color}"></i>${esc(se.name)}</span><span>${se.values[i] == null ? (o.floor ? `menos de ${o.floor}` : '-') : f(se.values[i])}</span></div>`);
      if (o.stacked && o.total) rows.push(`<div class="tr"><span>${esc(o.total)}</span><span>${f(sum(o.series, (se) => se.values[i] || 0))}</span></div>`);
      const head = o.tipHead ? o.tipHead(i) : `${fdate(o.dates[i])}${rel ? ` · saiu a ${esc(rel.tag)}` : ''}`;
      showTip(`<div class="th">${head}</div>${rows.join('')}${o.tipFoot ? `<div class="tn">${o.tipFoot(i)}</div>` : ''}`, e);
    });
    $('.hit', svg).addEventListener('mouseleave', () => { hov.style.display = 'none'; hideTip(); });
  }

  /** Caminhos entre ecrãs: de (esquerda) para (direita), largura = vezes. */
  function sankey(el, paths) {
    if (!el) return;
    if (!paths.length) { el.innerHTML = '<p class="note">Poucos dados.</p>'; return; }
    const W = Math.max(300, el.clientWidth);
    const outs = {}, ins = {};
    for (const p of paths) { outs[p.from] = (outs[p.from] || 0) + p.n; ins[p.to] = (ins[p.to] || 0) + p.n; }
    const left = Object.keys(outs).sort((a, b) => outs[b] - outs[a]);
    const right = Object.keys(ins).sort((a, b) => ins[b] - ins[a]);
    const total = sum(paths, (p) => p.n) || 1;
    const gap = 9, H = 300, nw = 7;
    const lw = W < 520 ? 92 : 128;
    const xl = lw, xr = W - lw - nw;
    const k = (H - 10 - gap * (Math.max(left.length, right.length) - 1)) / total;
    const pos = (list, tot) => { let yy = 4; const m = {}; for (const id of list) { m[id] = { y: yy, h: Math.max(2, tot[id] * k), off: 0 }; yy += m[id].h + gap; } return m; };
    const PL = pos(left, outs), PR = pos(right, ins);
    const order = (id, m) => m[id]?.y ?? 0;
    const sorted = [...paths].sort((a, b) => order(a.from, PL) - order(b.from, PL) || order(a.to, PR) - order(b.to, PR));
    const byTarget = [...paths].sort((a, b) => order(a.to, PR) - order(b.to, PR) || order(a.from, PL) - order(b.from, PL));
    const tgtOff = new Map(); { const o2 = {}; for (const p of byTarget) { o2[p.to] = o2[p.to] || 0; tgtOff.set(p, o2[p.to]); o2[p.to] += p.n * k; } }
    let s = `<svg class="sankey" viewBox="0 0 ${W} ${H}" height="${H}" role="img" aria-label="Caminhos entre ecrãs">`;
    for (const p of sorted) {
      const a = PL[p.from], b = PR[p.to], h = Math.max(1, p.n * k);
      const y0 = a.y + a.off + h / 2, y1 = b.y + tgtOff.get(p) + h / 2;
      a.off += p.n * k;
      const mx = (xl + nw + xr) / 2;
      s += `<path d="M${xl + nw},${y0.toFixed(1)} C${mx},${y0.toFixed(1)} ${mx},${y1.toFixed(1)} ${xr},${y1.toFixed(1)}" stroke-width="${h.toFixed(1)}" data-from="${p.from}" data-to="${p.to}" data-tip="${esc(`${scrName(p.from)} → ${scrName(p.to)}: ${nf(p.n)} vezes`)}"/>`;
    }
    for (const id of left) s += `<g class="node" data-node="L:${id}"><rect x="${xl}" y="${PL[id].y.toFixed(1)}" width="${nw}" height="${PL[id].h.toFixed(1)}"/><text x="${xl - 8}" y="${(PL[id].y + PL[id].h / 2 + 4).toFixed(1)}" text-anchor="end">${esc(scrName(id))} <tspan class="n">${nf(outs[id])}</tspan></text></g>`;
    for (const id of right) s += `<g class="node" data-node="R:${id}"><rect x="${xr}" y="${PR[id].y.toFixed(1)}" width="${nw}" height="${PR[id].h.toFixed(1)}"/><text x="${xr + nw + 8}" y="${(PR[id].y + PR[id].h / 2 + 4).toFixed(1)}">${esc(scrName(id))} <tspan class="n">${nf(ins[id])}</tspan></text></g>`;
    el.innerHTML = s + '</svg>';
    const svg = el.firstChild;
    $$('g.node', svg).forEach((g) => {
      const [side, id] = g.dataset.node.split(':');
      g.addEventListener('mouseenter', () => { $$('path', svg).forEach((p) => { const on = side === 'L' ? p.dataset.from === id : p.dataset.to === id; p.classList.toggle('on', on); p.classList.toggle('off', !on); }); $('rect', g).classList.add('on'); });
      g.addEventListener('mouseleave', () => { $$('path', svg).forEach((p) => p.classList.remove('on', 'off')); $('rect', g).classList.remove('on'); });
    });
  }

  // ------------------------------------------------------------------ mapa de calor (canvas, desfocado)
  let LUT = null;
  function buildLUT() {
    const st = [[0, cssv('--acc-d'), 0], [0.08, cssv('--acc-d'), 0], [0.25, cssv('--acc-d'), 0.32], [0.5, cssv('--acc'), 0.6], [0.75, cssv('--acc2'), 0.8], [1, cssv('--heat-hi'), 0.94]].map(([t, c, a]) => [t, hex(c), a]);
    LUT = new Uint8ClampedArray(256 * 4);
    for (let i = 0; i < 256; i++) {
      const t = i / 255; let j = 0;
      while (j < st.length - 2 && t > st[j + 1][0]) j++;
      const [t0, c0, a0] = st[j], [t1, c1, a1] = st[j + 1];
      const u = Math.min(1, Math.max(0, (t - t0) / (t1 - t0 || 1)));
      for (let k = 0; k < 3; k++) LUT[i * 4 + k] = c0[k] + (c1[k] - c0[k]) * u;
      LUT[i * 4 + 3] = (a0 + (a1 - a0) * u) * 255;
    }
  }
  /** Células cobertas por um retângulo da captura (para "sem o botão mais clicado"). */
  const cellsOf = (r) => { const out = []; const c0 = Math.floor(((r[0] - UI.X0) / (UI.W - UI.X0)) * UI.COLS), c1 = Math.floor(((r[0] + r[2] - UI.X0) / (UI.W - UI.X0)) * UI.COLS), r0 = Math.floor((r[1] / UI.H) * UI.ROWS), r1 = Math.floor(((r[1] + r[3]) / UI.H) * UI.ROWS); for (let y = Math.max(0, r0); y <= Math.min(UI.ROWS - 1, r1); y++) for (let x = Math.max(0, c0); x <= Math.min(UI.COLS - 1, c1); x++) out.push([y, x]); return out; };
  function heatGrid(sid) {
    const g = D.screens[sid].grid.map((row) => row.slice());
    if (S.heatScale === 'notop') {
      const s = UI.screens.find((q) => q.id === sid), d = D.screens[sid];
      const top = s.buttons.filter((b) => b.r).sort((a, b) => d.buttons[b.id].n - d.buttons[a.id].n)[0];
      if (top) for (const rc of [top.r, ...(top.more || [])]) for (const [y, x] of cellsOf(rc)) g[y][x] = 0;
    }
    if (S.heatScale === 'log') for (const row of g) for (let i = 0; i < row.length; i++) row[i] = Math.log1p(row[i]);
    return g;
  }
  function drawHeat(cv, grid) {
    if (!cv || !grid) return;
    if (!LUT) buildLUT();
    const CW = 240, CH = 166; cv.width = CW; cv.height = CH;
    const ctx = cv.getContext('2d');
    const field = new Float32Array(CW * CH);
    const sx = CW / UI.COLS, sy = CH / UI.ROWS, sgx = sx * 0.95, sgy = sy * 0.95;
    for (let r = 0; r < UI.ROWS; r++) for (let c = 0; c < UI.COLS; c++) {
      const v = grid[r][c]; if (!v) continue;
      const cx = (c + 0.5) * sx, cy = (r + 0.5) * sy;
      const x0 = Math.max(0, Math.floor(cx - 3 * sgx)), x1 = Math.min(CW - 1, Math.ceil(cx + 3 * sgx));
      const y0 = Math.max(0, Math.floor(cy - 3 * sgy)), y1 = Math.min(CH - 1, Math.ceil(cy + 3 * sgy));
      for (let py = y0; py <= y1; py++) {
        const dy = (py - cy) ** 2 / (2 * sgy * sgy);
        for (let px = x0; px <= x1; px++) field[py * CW + px] += v * Math.exp(-((px - cx) ** 2 / (2 * sgx * sgx) + dy));
      }
    }
    let mx = 0; for (let i = 0; i < field.length; i++) if (field[i] > mx) mx = field[i];
    const img = ctx.createImageData(CW, CH);
    for (let i = 0; i < field.length; i++) {
      const t = mx ? Math.pow(field[i] / mx, 0.8) : 0;
      const k = Math.min(255, Math.floor(t * 255)) * 4;
      img.data[i * 4] = LUT[k]; img.data[i * 4 + 1] = LUT[k + 1]; img.data[i * 4 + 2] = LUT[k + 2]; img.data[i * 4 + 3] = LUT[k + 3];
    }
    ctx.putImageData(img, 0, 0);
  }
  function drawRamp(cv) {
    if (!cv) return; if (!LUT) buildLUT();
    cv.width = 256; cv.height = 1; const ctx = cv.getContext('2d'); const img = ctx.createImageData(256, 1);
    for (let i = 0; i < 256; i++) { img.data[i * 4] = LUT[i * 4]; img.data[i * 4 + 1] = LUT[i * 4 + 1]; img.data[i * 4 + 2] = LUT[i * 4 + 2]; img.data[i * 4 + 3] = Math.max(60, LUT[i * 4 + 3]); }
    ctx.putImageData(img, 0, 0);
  }

  const CWI = UI.W - UI.X0;
  const px = (x) => (((x - UI.X0) / CWI) * 100).toFixed(3);
  const py = (y) => ((y / UI.H) * 100).toFixed(3);
  const pw = (w) => ((w / CWI) * 100).toFixed(3);
  const ph = (h) => ((h / UI.H) * 100).toFixed(3);

  function stageHtml(sid, mini) {
    const s = UI.screens.find((q) => q.id === sid), d = D.screens[sid];
    if (!s.img) {
      const btns = s.buttons.map((b) => ({ ...b, n: d.buttons[b.id]?.n || 0 })).sort((a, b) => b.n - a.n);
      return `<div class="placeholder"><div class="ttl">CS2 IP Changer &nbsp;/ <b>${esc(s.name)}</b></div><div class="body"><div class="pgb"><span>${esc(s.name)}</span>
        <div class="todo">Captura por fazer</div><p>Ficam os botões e os cliques. Nomes por confirmar no código.</p></div>
        <div class="pgb"><span>Botões</span><div class="pbtns">${btns.map((b) => `<span class="pb" data-tip="${esc(`${b.label}: ${plural(b.n, 'clique', 'cliques')}`)}">${esc(b.label)}<b>${nf(b.n)}</b></span>`).join('')}</div></div></div></div>`;
    }
    return `<img class="shot" src="${s.img}" alt="Ecrã ${esc(s.name)} da app" draggable="false"><canvas class="heat"></canvas>${mini ? '' : '<div class="ov" data-rich="1"></div>'}`;
  }

  function overlayHtml(sid) {
    const s = UI.screens.find((q) => q.id === sid), d = D.screens[sid];
    if (!s.img) return '';
    const L = S.layers;
    const max = Math.max(1, ...Object.values(d.buttons).map((b) => b.n));
    const top5 = new Set(s.buttons.filter((b) => b.r).sort((a, b) => d.buttons[b.id].n - d.buttons[a.id].n).slice(0, 5).map((b) => b.id));
    let h = '';
    if (L.btn) {
      for (const b of s.buttons) {
        const bd = d.buttons[b.id]; if (!bd || !b.r) continue;
        const t = bd.n / max;
        const rank = t > 0.5 ? 'var(--heat-hi)' : t > 0.12 ? 'var(--acc2)' : '#6d6d6d';
        const rect = (r, badge) => `<div class="bm${bd.n ? '' : ' zero'}${r[1] < 40 ? ' below' : ''}${top5.has(b.id) ? '' : ' minor'}" data-bid="${b.id}" style="left:${px(r[0])}%;top:${py(r[1])}%;width:${pw(r[2])}%;height:${ph(r[3])}%;--rank:${rank}">${badge ? `<span class="bc">${nf(bd.n)}</span>` : ''}</div>`;
        h += rect(b.r, true) + (b.more || []).map((r) => rect(r, false)).join('');
      }
    }
    const ring = (kind) => kind === 'dead'
      ? '<svg viewBox="-12 -12 24 24"><circle r="8" fill="rgba(0,0,0,.45)" stroke="#f0cc5a" stroke-width="1.5" stroke-dasharray="3 2"/><path d="M-3-3l6 6M3-3l-6 6" stroke="#f0cc5a" stroke-width="1.5"/></svg>'
      : '<svg viewBox="-12 -12 24 24"><circle r="9.5" fill="none" stroke="#ff8a7d" stroke-width="1.2" opacity=".55"/><circle r="6" fill="rgba(0,0,0,.45)" stroke="#ff8a7d" stroke-width="1.5"/><circle r="2" fill="#ff8a7d"/></svg>';
    if (L.dead) for (const m of d.dead) h += `<div class="mk dead" data-mk="dead" data-label="${esc(m.label)}" data-n="${m.n}" data-days="${m.days}" style="left:${px(m.x)}%;top:${py(m.y)}%">${ring('dead')}<b>${nf(m.n)}</b></div>`;
    if (L.rage) for (const m of d.rage) h += `<div class="mk rage" data-mk="rage" data-label="${esc(m.label)}" data-n="${m.n}" data-days="${m.days}" style="left:${px(m.x)}%;top:${py(m.y)}%">${ring('rage')}<b>${nf(m.n)}</b></div>`;
    return h;
  }

  function btnTip(sid, bid) {
    const s = UI.screens.find((q) => q.id === sid), b = s.buttons.find((q) => q.id === bid), bd = D.screens[sid].buttons[bid];
    const delta = bd.prev >= MIN ? ((bd.n - bd.prev) / bd.prev) * 100 : null;
    const tone = delta == null || Math.abs(delta) < 5 ? 'dim' : delta > 0 ? 'good' : 'warn';
    const q = ratio(bd.days, UDn());
    return `<div class="th">${esc(b.label)}</div>
      <div class="tr"><span>Cliques ${periodShort()}</span><span>${nf(bd.n)}</span></div>
      <div class="tr"><span>Instalações-dia que o usam</span><span>${q.txt} <small class="dim">${nf(bd.days)} de ${nf(UDn())}</small></span></div>
      <div class="tr"><span>${D.period === 1 ? 'Contra ontem' : 'Contra o período antes'}</span><span class="${tone}">${delta == null ? '-' : `${delta >= 0 ? '+' : '−'}${nf(Math.abs(delta))}%`}</span></div>
      ${bd.kb ? `<div class="tr"><span>Pelo atalho ${esc(b.kb)}</span><span>${pc((100 * bd.kb) / Math.max(1, bd.n + bd.kb))}</span></div>` : ''}
      ${spark(bd.trend, { w: 180, h: 28 })}<div class="tn">Cliques nos últimos 14 dias${b.note ? ` · ${esc(b.note)}` : ''}</div>`;
  }

  function wireOverlay(root, sid) {
    const ov = $('.ov', root); if (!ov) return;
    const d = D.screens[sid];
    const total = Math.max(1, sum(d.grid.flat()));
    ov.addEventListener('mousemove', (e) => {
      const bm = e.target.closest('.bm'), mk = e.target.closest('.mk');
      $$('.bm.hl', root).forEach((x) => { if (!bm || x.dataset.bid !== bm.dataset.bid) x.classList.remove('hl'); });
      $$('.bl .row.hl').forEach((x) => { if (!bm || x.dataset.bid !== bm.dataset.bid) x.classList.remove('hl'); });
      if (mk) {
        const dead = mk.dataset.mk === 'dead';
        showTip(`<div class="th">${dead ? plural(+mk.dataset.n, 'clique morto', 'cliques mortos') : plural(+mk.dataset.n, 'rajada de raiva', 'rajadas de raiva')}</div><div>${esc(mk.dataset.label)}</div><div class="tn">${dead ? 'Clicam aqui e nada acontece.' : '3 ou mais cliques no mesmo sítio em 1 s.'} Em ${plural(+mk.dataset.days, 'instalação-dia', 'instalações-dia')}.</div>`, e);
        return;
      }
      if (bm) {
        $$(`.bm[data-bid="${bm.dataset.bid}"]`, root).forEach((x) => x.classList.add('hl'));
        $$(`.bl .row[data-bid="${bm.dataset.bid}"]`).forEach((x) => x.classList.add('hl'));
        showTip(btnTip(sid, bm.dataset.bid), e);
        return;
      }
      const b = ov.getBoundingClientRect();
      const c = Math.min(UI.COLS - 1, Math.floor(((e.clientX - b.left) / b.width) * UI.COLS));
      const r = Math.min(UI.ROWS - 1, Math.floor(((e.clientY - b.top) / b.height) * UI.ROWS));
      const v = d.grid[r][c];
      showTip(`<div class="th">Zona ${c + 1} × ${r + 1}</div><div class="tr"><span>Cliques</span><span>${nf(v)}</span></div><div class="tr"><span>Do ecrã</span><span>${nf((100 * v) / total, 1)}%</span></div><div class="tn">Célula da grelha 24×16 que a app envia.</div>`, e);
    });
    ov.addEventListener('mouseleave', () => { hideTip(); $$('.hl').forEach((x) => x.classList.remove('hl')); });
  }

  function paintStage(root, sid, mini) {
    const st = $('.rstage', root); if (!st) return;
    st.innerHTML = stageHtml(sid, mini);
    const s = UI.screens.find((q) => q.id === sid);
    if (!s.img) { st.classList.remove('dimmed'); return; }
    const cv = $('canvas.heat', st);
    if (mini || S.layers.heat) { drawHeat(cv, mini ? D.screens[sid].grid : heatGrid(sid)); cv.style.opacity = mini ? 0.85 : S.opacity; } else cv.style.opacity = 0;
    st.classList.toggle('dimmed', mini || S.layers.heat);
    if (!mini) { $('.ov', st).innerHTML = overlayHtml(sid); wireOverlay(st, sid); }
  }

  // ------------------------------------------------------------------ regras: o que melhorar
  // Ordem: o que está partido primeiro (fugas, crashes), depois por instalações-dia afetadas.
  const NOT_RARE = new Set(['ClearHistory', 'CfgUndo', 'ResetAll', 'LogClear', 'RemoveServers', 'TplClearAvatar', 'ProfRestoreSelected', 'CfgRestore', 'TplNew', 'TplCopyFrom', 'TplDelete', 'ProfForgetAll', 'Restore', 'CfgSnapUse', 'CfgSnapExport', 'CfgSnapDelete', 'DeleteStats', 'ReportProblem']);
  function suggestions() {
    const out = [], sc = D.screens, per = periodShort(), UD = UDn();
    const dead = (sid, frag) => (sc[sid].dead || []).find((d) => d.label.includes(frag));
    const rage = (sid, frag) => (sc[sid].rage || []).find((d) => d.label.includes(frag));
    let d;
    const lk = D.rel.leak;
    if (lk.fail >= 1) out.push({ tone: 'bad', pri: 0, affected: lk.fail, title: 'O teste de fugas falhou', why: `${plural(lk.fail, 'falha', 'falhas')} em ${plural(lk.pass + lk.fail, 'teste', 'testes')} ${per}.`, rule: 'teste de fugas falhado: 1 ou mais', href: '#fiab' });
    const cp = D.rel.crashPlaces.find((x) => x.fresh);
    if (cp) out.push({ tone: 'bad', pri: 1, affected: cp.n, title: `Corrige o crash da ${cp.ver} em ${cp.at}`, why: `${plural(cp.n, 'crash', 'crashes')} ${per}; a versão anterior não tinha este.`, rule: 'crash num sítio que a versão anterior não tinha', href: '#fiab' });
    if ((d = dead('connection', 'IP que')) && d.n >= 5) out.push({ tone: 'warn', affected: d.days, title: 'Torna o IP clicável para copiar', why: `${plural(d.n, 'clique morto', 'cliques mortos')} no texto do IP ${per}. O botão de copiar ao lado teve ${nf(sc.connection.buttons.CopyEgressIp.n)}.`, rule: 'cliques mortos num texto: 5 ou mais', href: '#app/connection' });
    if ((d = rage('connection', 'Change IP')) && d.n >= 3) { const dur = D.phone.duration; const all = sum(dur, (x) => x.usb + x.hotspot); out.push({ tone: 'warn', affected: d.days, title: 'Mostra o progresso enquanto o Change IP troca', why: `${plural(d.n, 'rajada', 'rajadas')} de cliques de raiva no Change IP.${all >= MIN ? ` A troca demora 10 s ou mais em ${pc((100 * sum(dur.slice(2), (x) => x.usb + x.hotspot)) / all)} das vezes (n=${nf(all)}).` : ''}`, rule: 'cliques de raiva num botão: 3 ou mais', href: '#app/connection' }); }
    const L = D.cs2.lag, nU = L.usb.valve.n + L.usb.community.n, nH = L.hotspot.valve.n + L.hotspot.community.n;
    if (S.f.link === 'all' && nU >= LOW && nH >= LOW) {
      const lu = (100 * (L.usb.valve.lag + L.usb.community.lag)) / nU, lh = (100 * (L.hotspot.valve.lag + L.hotspot.community.lag)) / nH;
      if (lh / lu >= 1.5) out.push({ tone: 'acc', affected: Math.round(nH / 2.5), title: 'Recomenda o cabo USB no Phone setup', why: `Partidas com lag: hotspot ${pc(lh)} (n=${nf(nH)}), USB ${pc(lu)} (n=${nf(nU)}).`, rule: 'lag do hotspot 1,5× o do USB ou mais, com 30 partidas ou mais de cada', href: '#cs2' });
    }
    const st = D.phone.setup; let worst = null;
    for (let i = 1; i < st.length; i++) { if (st[i - 1].n < MIN) continue; const drop = 1 - st[i].n / st[i - 1].n; if (!worst || drop > worst.drop) worst = { drop, k: st[i].k, from: st[i - 1].n, to: st[i].n }; }
    if (worst && worst.drop >= 0.25) out.push({ tone: 'warn', affected: worst.from - worst.to, title: `Revê o passo "${worst.k}" do Phone setup`, why: `Chegam ao passo antes ${nf(worst.from)}, passam ${nf(worst.to)} (−${pc(worst.drop * 100)}).`, rule: 'passo do setup que perde 25% ou mais', href: '#telemovel' });
    if ((d = dead('servers', 'desligado')) && d.n >= 5) out.push({ tone: 'warn', affected: d.days, title: 'Diz porque o Remove está desligado', why: `${plural(d.n, 'clique', 'cliques')} no Remove sem servidor escolhido.`, rule: 'cliques num botão desligado: 5 ou mais', href: '#app/servers' });
    if ((d = dead('regions', 'nome da região')) && d.n >= 4) out.push({ tone: 'acc', affected: d.days, title: 'Liga a região ao clicar no nome', why: `${plural(d.n, 'clique', 'cliques')} no nome da região em vez da caixa.`, rule: 'cliques mortos num texto ao lado de um controlo', href: '#app/regions' });
    if ((d = dead('accounts', 'cartão')) && d.n >= 4) out.push({ tone: 'acc', affected: d.days, title: 'Dá uma ação ao clique no cartão da conta', why: `${plural(d.n, 'clique', 'cliques')} no corpo do cartão. O Log in teve ${nf(sc.accounts.buttons.AccSwitch.n)}.`, rule: 'cliques mortos num cartão: 4 ou mais', href: '#app/accounts' });
    const fa = D.funnel[0], fb = D.funnel[1];
    if (fa.n >= MIN && 1 - fb.n / fa.n >= 0.2) out.push({ tone: 'warn', affected: fa.n - fb.n, title: 'Ajuda a ligar o telemóvel no 1.º dia', why: `${plural(fa.n, 'instalação nova', 'instalações novas')}, ${nf(fb.n)} com o telemóvel pronto no mesmo dia (−${pc((1 - fb.n / fa.n) * 100)}).`, rule: 'passo do 1.º dia que perde 20% ou mais', href: '#uso' });
    const cr = [...D.rel.checksRed].sort((a, b) => b.n - a.n)[0];
    if (cr && cr.n >= 5) out.push({ tone: 'warn', affected: cr.n, title: `Explica o que fazer com "${cr.k}" a vermelho`, why: `${plural(cr.n, 'relatório', 'relatórios')} com esta linha do painel Checks a vermelho.`, rule: 'verificação a vermelho em 5 relatórios ou mais', href: '#fiab' });
    const sh = D.shortcuts[0], shT = sh.kb + sh.mouse;
    if (shT >= LOW && sh.kb / shT >= 0.5) out.push({ tone: 'acc', affected: Math.round(sh.kb / 3), title: 'Dá um atalho também ao Change IP', why: `O Change IP and rejoin faz-se ${pc((100 * sh.kb) / shT)} das vezes pelo ${sh.keys} (n=${nf(shT)}).`, rule: 'ação feita 50% ou mais pelo atalho', href: '#app/connection' });
    const up = D.rel.updates;
    if (up.declined >= 3) out.push({ tone: 'dim', affected: up.declined, title: 'Diz o que muda no aviso de atualização', why: `${plural(up.declined, 'recusa', 'recusas')} e ${plural(up.failed, 'falha', 'falhas')} ${per}.`, rule: 'recusas de atualização: 3 ou mais', href: '#fiab' });
    if (UD >= LOW) {
      const rare = [];
      for (const s of UI.screens) for (const b of s.buttons) { const bd = sc[s.id].buttons[b.id]; if (bd && bd.days / UD <= 0.02 && b.id !== 'Quick' && !NOT_RARE.has(b.id)) rare.push(`${b.label} (${s.name})`); }
      if (rare.length) out.push({ tone: 'dim', affected: 0, title: 'Junta ou esconde o que quase não se usa', why: `${rare.length} ${rare.length === 1 ? 'botão usado' : 'botões usados'} em 2% das instalações-dia ou menos: ${rare.slice(0, 4).join(', ')}${rare.length > 4 ? ' e mais' : ''}.`, rule: 'botão em 2% das instalações-dia ou menos (n ≥ 30); ficam de fora apagar e desfazer', href: '#app' });
    }
    const E = D.env, lang = [...E.lang].sort((a, b) => b.n - a.n)[0];
    if (lang && lang.k !== 'en' && lang.n / E.reports >= 0.25) out.push({ tone: 'acc', unf: true, affected: Math.round(lang.n / Math.max(1, D.period)), title: 'A 1.ª tradução da app devia ser português', why: `${pc((100 * lang.n) / E.reports)} dos relatórios de ambiente têm o Windows em ${lang.k} (n=${nf(E.reports)}).`, rule: 'idioma do Windows com 25% ou mais que a app não tem (ambiente, não filtrado)', href: '#ambiente' });
    const sm = D.screens.connection.scroll?.find((x) => x.k === 'Pequena');
    if (sm && sm.n >= LOW && sm.reach[2] <= 30) out.push({ tone: 'acc', affected: Math.round(sm.n / 3), title: 'Faz o Connection caber na janela pequena', why: `Com a janela pequena, só ${pc(sm.reach[2])} chegam a meio do Connection (n=${nf(sm.n)} visitas).`, rule: 'janela pequena com menos de 30% a chegar a meio do ecrã principal', href: '#app/connection' });
    const rj = D.cs2.rejoin;
    if (rj.tries >= LOW && rj.back / rj.tries >= 0.7) out.push({ tone: 'good', affected: 0, title: 'Rejoin: a maioria volta a entrar', why: `${nf(rj.back)} de ${nf(rj.tries)} tentativas (${pc((100 * rj.back) / rj.tries)}).`, rule: 'Rejoin com 70% ou mais de sucesso e 30 tentativas ou mais', href: '#cs2' });
    const toneRank = { bad: 0, warn: 1, acc: 1, dim: 2, good: 3 };
    return out.sort((a, b) => (a.tone === 'bad' || b.tone === 'bad' ? toneRank[a.tone] - toneRank[b.tone] || (a.pri ?? 9) - (b.pri ?? 9) : 0) || b.affected - a.affected || toneRank[a.tone] - toneRank[b.tone]);
  }
  const sugHtml = (list) => (list.length ? list.map((s) => `<div class="sug"><span class="sq ${s.tone}"></span><div><b>${esc(s.title)}</b><div class="why">${esc(s.why)}</div><div class="rule">Regra: ${esc(s.rule)}${s.affected ? ` · afeta ~${plural(s.affected, 'instalação-dia', 'instalações-dia')}` : ''}</div></div><a href="${s.href}">Ver</a></div>`).join('') : '<p class="note">Nenhuma regra disparou com estes filtros.</p>');

  // ------------------------------------------------------------------ peças repetidas
  function kpiCell(k) {
    const has = k.value != null;
    const low = has && k.n < LOW;
    let delta;
    if (!has) delta = '<span class="dim">poucos dados</span>';
    else if (k.prev == null) delta = `<span class="dim">${D.period === 1 ? 'sem ontem' : 'sem base antes'}</span>`;
    else {
      const dv = k.value - k.prev;
      const txt = k.unit === '%' ? `${dv >= 0 ? '+' : '−'}${nf(Math.abs(dv), 1)} pp` : `${dv >= 0 ? '+' : '−'}${nf(Math.abs((dv / (k.prev || 1)) * 100))}%`;
      const good = k.better === 'up' ? dv > 0 : dv < 0;
      const tone = low || k.prevN < LOW || Math.abs(dv / (k.prev || 1)) < 0.02 ? 'dim' : good ? 'good' : 'bad';
      delta = `<span class="${tone}">${txt}</span>`;
    }
    return `<a class="kc" href="#${k.page}"><div class="kl">${esc(k.label)}</div><div class="kv${low ? ' low' : ''}" ${has ? `data-anim="${k.id}" data-to="${k.value}" data-dec="${k.dec}" data-unit="${k.unit}"` : ''}>${has ? nf(k.value, k.dec) : '-'}</div><div class="kd">${delta}${spark(k.series, { w: 60, h: 18, color: k.better === 'down' ? 'var(--ink3)' : 'var(--acc)' })}</div><div class="nn">n=${nf(k.n)} ${esc(k.nUnit)}</div></a>`;
  }

  function funnelHtml() {
    const F = D.funnel, top = F[0].n;
    if (top < MIN) return `<p class="note">Poucos dados: ${plural(top, 'instalação nova', 'instalações novas')} ${periodShort()}.</p>`;
    return `<div class="fun">${F.map((s, i) => `${i ? `<span></span><span class="dr${F[i - 1].n && 1 - s.n / F[i - 1].n >= 0.2 ? ' worst' : ''}">−${pc(F[i - 1].n ? (1 - s.n / F[i - 1].n) * 100 : 0)}</span>` : ''}<span class="k" title="${esc(s.label)}">${esc(s.label)}</span><span class="t"><i style="width:${(s.n / top) * 100}%"></i></span><span class="v">${nf(s.n)}<small>${pc((100 * s.n) / top)}</small></span>`).join('')}</div>`;
  }

  const feedLi = (e) => `<li><span class="tm">${hhmm(e.t)}</span><span class="vv">${esc(e.version)}</span><span><span class="sq ${e.tone}"></span>${esc(e.text)}</span></li>`;
  const liveFeedHtml = (items) => `<ul class="feed">${items.map(feedLi).join('')}</ul>`;
  /** Contagens ao vivo: barra só a partir de 5; abaixo disso "menos de 5". */
  // Supressão secundária: se só uma categoria fica abaixo de 5, a menor das outras também se esconde ("5 ou mais"),
  // para não se tirar a pequena por subtração do total.
  const liveRows = (rows) => {
    if (!rows.length) return '<p class="note">Ninguém.</p>';
    const small = rows.filter((r) => r.n < LIVE_MIN), big = rows.filter((r) => r.n >= LIVE_MIN).sort((a, b) => a.n - b.n);
    const hide2 = small.length === 1 && big.length ? big[0] : null;
    return `<div class="bl live">${rows.map((r) => { const sup = r === hide2; const txt = r.n > 0 && r.n < LIVE_MIN ? 'menos de 5' : sup ? '5 ou mais' : nf(r.n); return `<div class="row"><span class="k">${esc(r.k)}</span><span class="t">${r.n >= LIVE_MIN && !sup ? `<i style="width:${(r.n / Math.max(1, D.live.now)) * 100}%"></i>` : ''}</span><span class="v${r.n < LIVE_MIN || sup ? ' low' : ''}">${txt}</span></div>`; }).join('')}</div>`;
  };
  const liveHead = () => `<div class="livebig"><span class="big${D.live.now < LIVE_MIN ? ' bigtxt' : ''}">${lc(D.live.now)}</span><span class="dim">a usar agora · ${lc(D.live.inGame)} em jogo · ${lc(D.live.protectedN)} com proteção ligada</span></div>`;

  function stageBox(sid, mini) {
    return `<div class="rwrap${mini ? ' mini' : ''}" id="${mini ? 'miniRep' : 'rep'}">${mini ? '' : `<div class="rnav">${UI.screens.map((s, i) => `<button style="top:${((52 + 62 * i) / 800 * 100).toFixed(2)}%;--sh:${Math.min(42, Math.round(D.screens[s.id].share * 0.7))}%" data-scr="${s.id}" aria-current="${s.id === sid}" data-tip="${esc(`${s.name}: ${D.screens[s.id].share}% do tempo com a app aberta`)}">${icon(s.icon)}<span>${esc(s.name)}</span><span class="pc">${nf(D.screens[s.id].share, D.screens[s.id].share % 1 ? 1 : 0)}%<i style="width:${Math.max(6, D.screens[s.id].share * 1.4)}%"></i></span></button>`).join('')}<div class="ver">v0.1.0<br>${esc(D.newest || 'beta.11')}</div></div>`}<div class="rstage"></div></div>`;
  }

  // ------------------------------------------------------------------ páginas
  const P = {};

  P.geral = () => {
    const sug = suggestions();
    const cn = D.screens.connection;
    const topB = UI.screens[0].buttons.map((b) => ({ ...b, n: cn.buttons[b.id].n })).sort((a, b) => b.n - a.n).slice(0, 3);
    const T = D.totals, ipAll = T.ipNew + T.ipSame + T.ipNone, ipR = ratio(T.ipNew, ipAll);
    const L = D.cs2.lag, nU = L.usb.valve.n + L.usb.community.n, nH = L.hotspot.valve.n + L.hotspot.community.n;
    const lu = ratio(L.usb.valve.lag + L.usb.community.lag, nU), lh = ratio(L.hotspot.valve.lag + L.hotspot.community.lag, nH);
    const cf = ratio(T.sessions - T.crashes, T.sessions, 1);
    return `<div class="g">
      <section class="gb s12"><h2>Resumo <small>${periodLabel()}, ${prevLabel()}</small></h2><div class="kstrip">${D.kpis.map(kpiCell).join('')}</div></section>
      <section class="gb s8"><h2>Instalações abertas por dia <small>novas e as outras · tracejado = versão nova</small></h2><a class="more" href="#uso">Uso e retenção</a><div class="chart" id="ovUsers"></div>${legend([['as outras', 'var(--acc)'], ['novas', 'var(--acc2)'], ['versão nova', '', 'dash']])}</section>
      <section class="gb s4"><h2>Agora <small>só contagens</small></h2><a class="more" href="#vivo">Ao vivo</a>
        <div data-live-box="head">${liveHead()}</div>
        <div class="chart" id="ovLive"></div>
        <div id="ovFeed">${liveFeedHtml(D.live.feed.slice(0, 4))}</div></section>
      <section class="gb s7"><h2>O que melhorar primeiro <small>${periodLabel()}</small></h2><a class="more" href="#melhorar">Todas (${sug.length})</a>${sugHtml(sug.slice(0, 4))}</section>
      <section class="gb s5"><h2>Onde clicam no Connection</h2><a class="more" href="#app/connection">Abrir o mapa</a>${stageBox('connection', true)}
        <p class="note">${topB[0].n ? `Mais clicados: ${topB.map((b) => `${esc(b.label)} <span class="tab">${nf(b.n)}</span>`).join(' · ')}` : 'Poucos dados com estes filtros.'}</p></section>
      <section class="gb s4"><h2>1.º dia <small>passos no mesmo dia</small></h2>${funnelHtml()}<p class="note">Downloads do exe no GitHub: ${nf(D.github.downloadsPeriod)} ${unf()}</p></section>
      <section class="gb s4"><h2>Telemóvel e partidas</h2><a class="more" href="#telemovel">Detalhe</a>
        <div class="kl">Trocas com IP novo</div><div class="kv ${ipR.cls}">${ipR.txt}</div>${nline(ipR, 'trocas')}
        ${stack([['IP novo', T.ipNew, 'var(--good)'], ['mesmo IP', T.ipSame, 'var(--warn)'], ['sem rede', T.ipNone, 'var(--bad)']])}
        <h3>Partidas com lag</h3>${bars([...(nU ? [{ k: 'USB', v: lu.v || 0, vTxt: lu.txt, low: lu.cls === 'low', sub: `n=${nf(nU)}` }] : []), ...(nH ? [{ k: 'Hotspot', v: lh.v || 0, vTxt: lh.txt, low: lh.cls === 'low', sub: `n=${nf(nH)}`, cls: 'warn' }] : [])], { max: 100 })}</section>
      <section class="gb s4"><h2>Fiabilidade</h2><a class="more" href="#fiab">Detalhe</a>
        <div class="statrow"><div class="stat"><div class="kl">Sessões sem crash</div><div class="kv ${cf.v == null ? 'low' : cf.cls || (cf.v >= 99 ? 'good' : 'warn')}">${cf.txt}</div>${nline(cf, 'sessões')}</div><div class="stat"><div class="kl">Crashes</div><div class="kv">${nf(T.crashes)}</div></div><div class="stat"><div class="kl">Erros</div><div class="kv">${nf(T.errors)}</div></div></div>
        <h3>Alertas ${unf()}</h3>${D.alerts.list.slice(0, 2).map((a) => `<div class="note" style="color:var(--ink2)"><span class="sq ${a.tone}"></span>${esc(a.text)} <span class="dim">· ${esc(a.when)}</span></div>`).join('')}</section>
    </div>`;
  };
  P.geral.after = () => {
    chart($('#ovUsers'), { dates: D.days.map((d) => d.date), type: 'bars', stacked: true, total: 'Instalações abertas', h: 190, label: 'Instalações abertas por dia',
      series: [{ name: 'As outras', values: D.days.map((d) => d.back), color: 'var(--acc)' }, { name: 'Novas', values: D.days.map((d) => d.fresh), color: 'var(--acc2)' }] });
    liveMini($('#ovLive'), 70);
    paintStage($('#miniRep'), 'connection', true);
  };
  function liveMini(el, h) {
    if (!el) return;
    const n = D.live.lastHour.open.length;
    const hide = (v) => (v < LIVE_MIN ? null : v);
    chart(el, { dates: [...Array(n)].map(() => D.asOf), h, markers: false, left: 22, label: 'Última hora', fmt: nf, floor: LIVE_MIN,
      xLabels: [[0, 'há 60 min', 'start'], [n - 1, 'agora', 'end']], tipHead: (i) => (i === n - 1 ? 'agora' : `há ${n - 1 - i} min`),
      series: [{ name: 'Abertas', values: D.live.lastHour.open.map(hide), color: 'var(--acc)' }, { name: 'Em jogo', values: D.live.lastHour.game.map(hide), color: 'var(--good)' }] });
  }

  P.vivo = () => {
    const today = D.days.at(-1);
    return `<div class="g">
      <section class="gb s8"><h2>Agora <small>instalações com sinal nos últimos 6 min</small></h2>
        <div data-live-box="head">${liveHead()}</div>
        <div class="g" style="gap:14px;margin-top:14px" data-live-box="agg">${liveAggHtml()}</div>
        <p class="note">Só contagens. Abaixo de 5 diz "menos de 5". Nenhuma linha por instalação, nenhum país.</p></section>
      <section class="gb s4"><h2>Última hora</h2><div class="chart" id="lvHour"></div>${legend([['abertas', 'var(--acc)'], ['em jogo', 'var(--good)']])}
        <div class="statrow" style="margin-top:10px"><div class="stat"><div class="kl">Pico na última hora</div><div class="kv">${lc(Math.max(...D.live.lastHour.open))}</div></div></div></section>
      <section class="gb s7"><h2>Eventos ao vivo <small>tipo, versão e resultado</small></h2><div id="lvFeed">${liveFeedHtml(D.live.feed.slice(0, 18))}</div><p class="note">A hora é a do sinal, de 5 em 5 min.</p></section>
      <section class="gb s5"><h2>Hoje até às ${esc(D.now)}</h2>
        <div class="statrow"><div class="stat"><div class="kl">Instalações abertas</div><div class="kv">${nf(today.users)}</div></div><div class="stat"><div class="kl">Novas</div><div class="kv">${nf(today.fresh)}</div></div><div class="stat"><div class="kl">Trocas de IP</div><div class="kv">${nf(today.ipNew + today.ipSame + today.ipNone)}</div></div><div class="stat"><div class="kl">Partidas</div><div class="kv">${nf(today.matches)}</div></div><div class="stat"><div class="kl">Crashes</div><div class="kv ${today.crashes ? 'bad' : ''}">${nf(today.crashes)}</div></div></div>
        <h3>Trocas de hoje</h3>${stack([['IP novo', today.ipNew, 'var(--good)'], ['mesmo IP', today.ipSame, 'var(--warn)'], ['sem rede', today.ipNone, 'var(--bad)']])}${legend([['IP novo', 'var(--good)'], ['mesmo IP', 'var(--warn)'], ['sem rede', 'var(--bad)']])}</section>
    </div>`;
  };
  const liveAggHtml = () => `<div class="s6"><h3>Versão</h3>${liveRows(D.live.byVersion)}</div><div class="s6"><h3>Ligação</h3>${liveRows(D.live.byLink)}</div><div class="s6"><h3>Modo</h3>${liveRows(D.live.byMode)}</div><div class="s6"><h3>Ecrã aberto</h3>${liveRows(D.live.byScreen)}</div>`;
  P.vivo.after = () => liveMini($('#lvHour'), 120);

  P.app = () => {
    const sid = S.screen, s = UI.screens.find((q) => q.id === sid), d = D.screens[sid];
    const UD = UDn();
    const btns = s.buttons.map((b) => ({ ...b, ...d.buttons[b.id] })).sort((a, b) => b.n - a.n);
    const max = Math.max(1, ...btns.map((b) => b.n));
    const deadN = sum(d.dead, (x) => x.n), rageN = sum(d.rage, (x) => x.n);
    const clicks = d.grid ? sum(d.grid.flat()) : sum(btns, (b) => b.n);
    const rare = UD >= LOW ? btns.filter((b) => b.days / UD <= 0.03 && !NOT_RARE.has(b.id)) : [];
    const L = S.layers;
    const openR = ratio(d.days, UD), deadR = ratio(deadN, clicks, 1);
    const ck = (k, label, sw) => `<label class="ck"><input type="checkbox" data-layer="${k}"${L[k] ? ' checked' : ''}${!s.img ? ' disabled' : ''}>${sw || ''}${label}</label>`;
    const total = d.grid ? sum(d.grid.flat()) : 0;
    const sb = (v, t) => `<button data-hs="${v}" aria-pressed="${S.heatScale === v}">${t}</button>`;
    return `<div class="appview">
      <section class="gb rep"><h2>A app, com calor <small>${esc(s.name)} · ${periodLabel()}</small></h2>
        <div class="layers">${ck('heat', 'Calor dos cliques')}${ck('btn', 'Botões com contagem')}${ck('dead', 'Cliques mortos', '<span class="sw" style="border:1.5px dashed #f0cc5a;border-radius:50%"></span>')}${ck('rage', 'Cliques de raiva', '<span class="sw" style="border:1.5px solid #ff8a7d;border-radius:50%"></span>')}
          <span class="seg sm" role="group" aria-label="Escala do calor">${sb('lin', 'linear')}${sb('log', 'log')}${sb('notop', 'sem o mais clicado')}</span>
          <label class="opacity">Calor <input type="range" min="0.3" max="1" step="0.05" value="${S.opacity}" id="heatOp" aria-label="Opacidade do calor"></label></div>
        ${stageBox(sid, false)}
        ${s.img ? `<div class="heatleg"><span>menos</span><canvas class="ramp" id="ramp"></canvas><span>mais cliques</span><b class="tab">n = ${plural(total, 'clique', 'cliques')}</b><span class="dim">· escala ${S.heatScale === 'log' ? 'log' : 'linear'} de cada ecrã · grelha 24×16 desfocada · clica na barra da app para trocar de ecrã</span></div>` : '<div class="heatleg">Sem captura, sem mapa: os cliques ficam nos botões e na lista ao lado.</div>'}</section>
      <div class="aside">
        <section class="gb"><h2>Este ecrã <small>${esc(s.name)}</small></h2><div class="scrstats">
          <div><div class="kl">Do tempo aberto</div><div class="kv">${nf(d.share, d.share % 1 ? 1 : 0)}%</div><div class="nn">${nf(d.minutes / 60)} h</div></div>
          <div><div class="kl">Abrem este ecrã</div><div class="kv ${openR.cls}">${openR.txt}</div><div class="nn">${nf(d.days)} de ${nf(UD)} inst.-dia</div></div>
          <div><div class="kl">Cliques</div><div class="kv">${nf(clicks)}</div><div class="nn">${nf(d.visits)} visitas</div></div>
          <div><div class="kl">Cliques mortos</div><div class="kv ${deadN ? 'warn' : ''}">${nf(deadN)}</div><div class="nn">${deadR.v == null ? '-' : `${deadR.txt} dos cliques`}</div></div>
          <div><div class="kl">Rajadas de raiva</div><div class="kv ${rageN ? 'bad' : ''}">${nf(rageN)}</div></div></div></section>
        <section class="gb"><h2>Até onde fazem scroll <small>por tamanho da janela</small></h2>${scrollHtml(d)}</section>
        <section class="gb"><h2>Botões <small>cliques ${periodShort()} · % de instalações-dia</small></h2>
          ${bars(btns.map((b) => { const q = ratio(b.days, UD); return { k: b.label, v: b.n, sub: q.txt, attr: ` data-bid="${b.id}"`, tip: `${b.label}: ${plural(b.n, 'clique', 'cliques')}, ${nf(b.days)} de ${nf(UD)} instalações-dia` }; }), { max })}
          ${rare.length ? `<h3>Quase não se usa (3% das instalações-dia ou menos)</h3><div class="pills">${rare.map((b) => `<span class="chip">${esc(b.label)}</span>`).join('')}</div>` : UD < LOW ? `<p class="note">Poucos dados para dizer o que não se usa (n=${nf(UD)} instalações-dia).</p>` : ''}</section>
      </div>
    </div>
    <div class="g" style="margin-top:22px">
      <section class="gb s4"><h2>Minutos por ecrã</h2>${bars(UI.screens.map((q) => ({ k: q.name, v: D.screens[q.id].minutes, sub: `${nf(D.screens[q.id].share, D.screens[q.id].share % 1 ? 1 : 0)}%`, hl: q.id === sid })).sort((a, b) => b.v - a.v), { fmt: (v) => `${nf(v / 60)} h` })}</section>
      <section class="gb s4"><h2>1.º ecrã depois do arranque <small>a app abre no Connection</small></h2>${shareBars(D.firstScreen.map((x) => ({ k: x.screen === 'connection' ? 'Fica no Connection' : scrName(x.screen), n: x.n })), D.totals.sessions)}<p class="note">n = ${plural(D.totals.sessions, 'sessão', 'sessões')}</p></section>
      <section class="gb s4"><h2>Atalho ou rato <small>a mesma ação</small></h2>${D.shortcuts.map((x) => { const t = x.kb + x.mouse, q = ratio(x.kb, t); return `<div style="margin-bottom:10px"><div class="kl" style="color:var(--ink2)">${esc(x.action)} <span class="dim">· ${esc(x.keys)}</span></div>${stack([['atalho', x.kb, 'var(--acc)'], ['rato', x.mouse, 'var(--line2)']])}<div class="kd"><span class="${q.cls}">atalho ${q.txt}</span><span>${esc(q.note)}</span></div></div>`; }).join('')}</section>
      <section class="gb s8"><h2>Caminhos entre ecrãs <small>de onde para onde · ${periodLabel()} · passa o rato num ecrã</small></h2><div class="chart" id="sankey"></div></section>
      <section class="gb s4"><h2>Mudado de fábrica <small>% de instalações-dia</small></h2>${shareBars(D.settingsChanged.map((x) => ({ k: x.name, n: x.n })), UD)}<p class="note">Só sim/não ou opções de lista; nunca texto escrito. n = ${nf(UD)} instalações-dia.</p></section>
    </div>`;
  };
  function scrollHtml(d) {
    if (!d.scroll) return '<p class="note">Este ecrã não tem medição de scroll.</p>';
    return `<div class="tw"><table class="t scr"><thead><tr><th>Janela</th><th class="n">2.º ¼</th><th class="n">3.º ¼</th><th class="n">4.º ¼</th></tr></thead><tbody>${d.scroll.map((w) => `<tr><td>${esc(w.k)}<div class="nn">${plural(w.n, 'visita', 'visitas')}</div></td>${w.reach.slice(1).map((v, i) => { if (w.n < MIN) return '<td class="n dim">-</td>'; const c = heatCell(v / 100); return `<td class="c" style="background:${c.bg};color:${c.fg}" data-tip="${esc(`${w.k}: ${v}% das visitas passam dos ${[25, 50, 75][i]}% do ecrã`)}">${v}%</td>`; }).join('')}</tr>`).join('')}</tbody></table></div><p class="note">% das visitas que chegam a cada quarto do ecrã. A banda da janela vai com o scroll.</p>`;
  }
  P.app.after = () => {
    const rep = $('#rep');
    paintStage(rep, S.screen, false);
    drawRamp($('#ramp'));
    sankey($('#sankey'), D.paths);
    $$('.rnav button', rep).forEach((b) => b.addEventListener('click', () => { S.screen = b.dataset.scr; history.replaceState(null, '', `#app/${S.screen}`); renderPage(); }));
    $$('[data-layer]').forEach((c) => c.addEventListener('change', () => { S.layers[c.dataset.layer] = c.checked; store.set('layers2', JSON.stringify(S.layers)); paintStage(rep, S.screen, false); }));
    $$('[data-hs]').forEach((b) => b.addEventListener('click', () => { S.heatScale = b.dataset.hs; renderPage(); }));
    const op = $('#heatOp'); if (op) op.addEventListener('input', () => { S.opacity = +op.value; const cv = $('canvas.heat', rep); if (cv && S.layers.heat) cv.style.opacity = S.opacity; });
    $$('.bl .row[data-bid]').forEach((r) => {
      r.addEventListener('mouseenter', () => $$(`.bm[data-bid="${r.dataset.bid}"]`, rep).forEach((x) => x.classList.add('hl')));
      r.addEventListener('mouseleave', () => $$('.bm.hl', rep).forEach((x) => x.classList.remove('hl')));
    });
  };

  P.uso = () => {
    const dmax = Math.max(1, ...D.when.flat());
    const days = ['seg', 'ter', 'qua', 'qui', 'sex', 'sáb', 'dom'];
    const UD = UDn();
    const cohCell = (c, n) => { if (c == null) return '<td class="c empty"></td>'; const t = n ? c / n : 0; const h = heatCell(t); return `<td class="c" style="background:${h.bg};color:${h.fg}" data-tip="${esc(`${nf(c)} de ${nf(n)} abriram a app`)}">${n >= 20 ? pc(100 * t) : `${nf(c)} de ${nf(n)}`}</td>`; };
    return `<div class="g">
      <section class="gb s12"><h2>Instalações abertas por dia <small>uma instalação aberta num dia conta 1</small></h2><div class="chart" id="usUsers"></div>${legend([['as outras', 'var(--acc)'], ['novas', 'var(--acc2)'], ['versão nova', '', 'dash']])}</section>
      <section class="gb s6"><h2>1.º dia <small>cada passo é parte do anterior, no mesmo dia · ${periodLabel()}</small></h2>${funnelHtml()}<p class="note">1.º arranque = aceitaram o aviso. Downloads do GitHub ficam à parte (GitHub, não filtrado). A retenção está nas coortes.</p></section>
      <section class="gb s6"><h2>Quando usam <small>sessões, hora de Lisboa</small></h2><div class="hm">${days.map((dn, di) => `<span>${dn}</span>${D.when[di].map((v, h) => `<i style="background:${heatCell(v / dmax).bg}" data-tip="${dn} ${h}h: ${plural(v, 'sessão', 'sessões')}"></i>`).join('')}`).join('')}<div class="hx"><span>0h</span><span>6h</span><span>12h</span><span>18h</span><span>23h</span></div></div>
        <div class="scale">menos ${[0, 0.25, 0.5, 0.75, 1].map((t) => `<i style="background:${heatCell(t).bg}"></i>`).join('')} mais · n = ${plural(sum(D.when.flat()), 'sessão', 'sessões')}</div></section>
      <section class="gb s8"><h2>Coortes por semana de instalação <small>abriram a app N dias depois · "3 de 6" com menos de 20</small></h2><div class="tw"><table class="t coh"><thead><tr><th>Semana</th><th class="n">Novas</th>${['Dia 1', 'Dia 3', 'Dia 7', 'Dia 14', 'Dia 30'].map((x) => `<th class="n">${x}</th>`).join('')}</tr></thead><tbody>
        ${D.cohorts.map((c) => `<tr><td>${fdate(c.week)}</td><td class="n">${nf(c.n)}</td>${!c.n ? '<td class="dim" colspan="5">sem instalações novas</td>' : c.back[1] == null ? '<td class="dim" colspan="5">ainda não passou uma semana</td>' : c.back.slice(1).map((v) => cohCell(v, c.n)).join('')}</tr>`).join('')}</tbody></table></div>
        <p class="note">Desde a beta.9: cada instalação conta uma vez por coluna (ID fixo e aleatório, sem nada do PC). Antes: estimativa pela idade em bandas, em instalações-dia.</p></section>
      <section class="gb s4"><h2>Idade da instalação <small>instalações-dia, dias desde que foi instalada</small></h2>
        <div class="tw"><table class="t"><thead><tr><th>Dias</th><th class="n">Instalações-dia</th><th class="n">%</th></tr></thead><tbody>${D.installAge.map((a) => { const q = ratio(a.n, UD); return `<tr><td>${esc(a.band)}</td><td class="n">${nf(a.n)}</td><td class="n ${q.cls}">${q.txt}</td></tr>`; }).join('')}</tbody></table></div>
        <h3>Dias com a app aberta nos últimos 7 <small class="dim">quem abriu pelo menos 1</small></h3>${cols(D.activeDays, [{ k: 'n', name: 'Instalações-dia', color: 'var(--acc)' }], { h: 90 })}</section>
      <section class="gb s6"><h2>Minutos por sessão por dia <small>mediana</small></h2><div class="chart" id="usMin"></div></section>
      <section class="gb s3"><h2>Sessões</h2>
        <h3>Duração</h3>${cols(D.sessionLen, [{ k: 'n', name: 'Sessões', color: 'var(--acc)' }], { h: 80 })}
        <h3>Como arranca</h3>${stack([['com o Windows', D.startKind.windows, 'var(--acc)'], ['à mão', D.startKind.hand, 'var(--acc2)']])}<div class="kd"><span>com o Windows ${ratio(D.startKind.windows, D.totals.sessions).txt}</span><span>n=${nf(D.totals.sessions)}</span></div>
        <h3>Janela à vista</h3>${stack([['janela à vista', D.visible.visible, 'var(--acc)'], ['só na bandeja', D.visible.min - D.visible.visible, 'var(--line2)']])}<div class="kd"><span>${ratio(D.visible.visible, D.visible.min).txt} dos minutos</span><span>n=${nf(D.visible.min)} min</span></div></section>
      <section class="gb s3"><h2>Países</h2>${shareBars(D.countries.map((c) => ({ k: c.k, n: c.n })), UD)}<p class="note">n = ${nf(UD)} instalações-dia</p></section>
    </div>`;
  };
  P.uso.after = () => {
    const dates = D.days.map((d) => d.date);
    chart($('#usUsers'), { dates, type: 'bars', stacked: true, total: 'Instalações abertas', h: 210, series: [{ name: 'As outras', values: D.days.map((d) => d.back), color: 'var(--acc)' }, { name: 'Novas', values: D.days.map((d) => d.fresh), color: 'var(--acc2)' }] });
    chart($('#usMin'), { dates, h: 170, fmt: (v) => `${nf(v)} min`, tipFoot: (i) => `n = ${plural(D.days[i].sessions, 'sessão', 'sessões')}`, series: [{ name: 'Mediana', values: D.days.map((d) => (d.sessions >= MIN ? d.sessMed : null)), color: 'var(--acc)', area: true }] });
  };

  P.telemovel = () => {
    const p = D.phone, T = D.totals, ipAll = T.ipNew + T.ipSame + T.ipNone, ipR = ratio(T.ipNew, ipAll);
    const setupTop = Math.max(1, p.setup[0].n), ses = D.totals.sessions;
    const pctCols = (rows, den) => cols(rows.map((x) => ({ band: x.band, pct: den >= MIN ? (100 * x.n) / den : 0 })), [{ k: 'pct', name: '%', color: 'var(--acc)' }], { fmt: (v) => (den >= MIN ? pc(v) : '-') });
    return `<div class="g">
      <section class="gb s4"><h2>Trocas de IP <small>${periodLabel()}</small></h2>
        <div class="kl">Trocas com IP novo</div><div class="big ${ipR.cls}">${ipR.txt}</div>${nline(ipR, 'trocas')}
        ${stack([['IP novo', T.ipNew, 'var(--good)'], ['mesmo IP', T.ipSame, 'var(--warn)'], ['sem rede', T.ipNone, 'var(--bad)']])}${legend([['IP novo', 'var(--good)'], ['mesmo IP', 'var(--warn)'], ['sem rede', 'var(--bad)']])}
        <h3>Método</h3>${shareBars(p.method, ipAll)}</section>
      <section class="gb s8"><h2>Trocas por dia</h2><div class="chart" id="phDay"></div>${legend([['IP novo', 'var(--good)'], ['mesmo IP', 'var(--warn)'], ['sem rede', 'var(--bad)']])}</section>
      <section class="gb s4"><h2>Quanto demora a troca <small>% de cada ligação</small></h2>${cols(p.duration, [{ k: 'usb', name: 'USB', color: 'var(--acc)' }, { k: 'hotspot', name: 'Hotspot', color: 'var(--acc2)' }], { norm: true, labels: true })}</section>
      <section class="gb s4"><h2>Tentativas até ficar boa <small>n=${nf(ipAll)}</small></h2>${pctCols(p.attempts, ipAll)}</section>
      <section class="gb s4"><h2>Qualidade depois da troca <small>teste da app</small></h2>${shareBars([{ k: 'Boa', n: p.quality.boa, cls: 'good' }, { k: 'Razoável', n: p.quality.razoavel, cls: 'warn' }, { k: 'Má', n: p.quality.ma, cls: 'bad' }], ipAll)}
        <h3>Trocaram outra vez por jitter</h3>${shareBars([{ k: 'Trocas repetidas', n: p.quality.retry }], ipAll)}</section>
      <section class="gb s6"><h2>Onde o Phone setup para</h2>${p.setup[0].n < MIN ? `<p class="note">Poucos dados (n=${nf(p.setup[0].n)}).</p>` : `<div class="fun">${p.setup.map((s, i) => `${i ? `<span></span><span class="dr${p.setup[i - 1].n && 1 - s.n / p.setup[i - 1].n >= 0.25 ? ' worst' : ''}">−${pc(p.setup[i - 1].n ? (1 - s.n / p.setup[i - 1].n) * 100 : 0)}</span>` : ''}<span class="k" title="${esc(s.k)}">${esc(s.k)}</span><span class="t"><i style="width:${(s.n / setupTop) * 100}%"></i></span><span class="v">${nf(s.n)}</span>`).join('')}</div>`}${p.steps && p.steps.length ? `<h3>Passos vistos <small class="dim">instalações-dia que passaram por cada passo</small></h3>${shareBars(p.steps.map((x) => ({ k: x.k, n: x.n })), Math.max(1, p.setup[0].n))}` : ''}</section>
      <section class="gb s6"><h2>Do arranque a Protected</h2><div class="kl">Banda mediana</div><div class="kv">${ses >= MIN ? esc(p.toProtectedMedian) : '-'}</div><div class="nn">n=${plural(ses, 'sessão', 'sessões')}</div>${pctCols(p.toProtected, ses)}</section>
      <section class="gb s6"><h2>Quedas do telemóvel por sessão <small>n=${nf(ses)}</small></h2>${pctCols(p.drops, ses)}</section>
      <section class="gb s6"><h2>Dados móveis por dia <small>instalações-dia, n=${nf(UDn())}</small></h2>${pctCols(p.data, UDn())}</section>
    </div>`;
  };
  P.telemovel.after = () => {
    chart($('#phDay'), { dates: D.phone.series.map((d) => d.date), type: 'bars', stacked: true, total: 'Trocas', h: 220,
      series: [{ name: 'IP novo', values: D.phone.series.map((d) => d.novo), color: 'var(--good)' }, { name: 'Mesmo IP', values: D.phone.series.map((d) => d.mesmo), color: 'var(--warn)' }, { name: 'Sem rede', values: D.phone.series.map((d) => d.sem), color: 'var(--bad)' }] });
  };

  P.cs2 = () => {
    const c = D.cs2, M = c.matches, L = c.lag;
    const lagCell = (x) => { const q = ratio(x.lag, x.n); const tone = q.v == null ? 'low' : q.cls === 'low' ? 'low' : q.v >= 40 ? 'bad' : q.v >= 20 ? 'warn' : 'good'; return `<td class="n ${tone}"><span style="font-size:15px">${q.txt}</span><div class="nn">n=${nf(x.n)}</div></td>`; };
    const lagR = ratio(c.laggy, M), valR = ratio(c.valve, M), rjR = ratio(c.rejoin.back, c.rejoin.tries);
    const k2 = [{ k: 'usb', name: 'USB', color: 'var(--acc)' }, { k: 'hotspot', name: 'Hotspot', color: 'var(--acc2)' }];
    const pctCols = (rows) => cols(rows.map((x) => ({ band: x.band, pct: M >= MIN ? (100 * x.n) / M : 0 })), [{ k: 'pct', name: '%', color: 'var(--acc)' }], { fmt: (v) => (M >= MIN ? pc(v) : '-'), h: 100 });
    const rows = [S.f.link === 'hotspot' || S.f.link === 'other' ? '' : `<tr><td>USB</td>${lagCell(L.usb.valve)}${lagCell(L.usb.community)}</tr>`, S.f.link === 'usb' || S.f.link === 'other' ? '' : `<tr><td>Hotspot</td>${lagCell(L.hotspot.valve)}${lagCell(L.hotspot.community)}</tr>`].join('');
    return `<div class="g">
      <section class="gb s12"><h2>CS2 <small>${periodLabel()}</small></h2><div class="kstrip">
        <div class="kc"><div class="kl">Sessões com o CS2 aberto</div><div class="kv">${nf(c.sessions)}</div></div>
        <div class="kc"><div class="kl">Horas com proteção ligada</div><div class="kv">${nf(c.hoursProtected)}</div></div>
        <div class="kc"><div class="kl">Partidas</div><div class="kv">${nf(M)}</div></div>
        <div class="kc"><div class="kl">Com lag</div><div class="kv ${lagR.cls || 'warn'}">${lagR.txt}</div>${nline(lagR, 'partidas')}</div>
        <div class="kc"><div class="kl">Matchmaking Valve</div><div class="kv ${valR.cls}">${valR.txt}</div>${nline(valR, 'partidas')}</div>
        <div class="kc"><div class="kl">Rejoin voltou a entrar</div><div class="kv ${rjR.cls || 'good'}">${rjR.txt}</div><div class="nn">${nf(c.rejoin.back)} de ${nf(c.rejoin.tries)} tentativas</div></div></div></section>
      <section class="gb s5"><h2>Partidas com lag <small>por ligação e servidor</small></h2>${rows ? `<div class="tw"><table class="t"><thead><tr><th></th><th class="n">Valve</th><th class="n">Comunitário</th></tr></thead><tbody>${rows}</tbody></table></div>` : '<p class="note">Sem partidas com USB ou hotspot neste filtro.</p>'}
        <p class="note">Lag = a app viu jitter ou mensagens atrasadas acima do limite nessa partida. A cinzento: menos de 30 partidas.</p></section>
      <section class="gb s7"><h2>Ping por partida <small>P50 em ms · % das partidas de cada ligação</small></h2>${cols(c.ping, k2, { norm: true, labels: true })}</section>
      <section class="gb s6"><h2>Jitter de envio <small>ms · % de cada ligação</small></h2>${cols(c.jitter, k2, { norm: true, labels: true })}</section>
      <section class="gb s6"><h2>Jitter de receção <small>ms · % de cada ligação</small></h2>${cols(c.jitterDown, k2, { norm: true, labels: true })}</section>
      <section class="gb s3"><h2>Mensagens atrasadas <small>n=${nf(M)}</small></h2>${pctCols(c.late)}</section>
      <section class="gb s3"><h2>Pacotes perdidos <small>n=${nf(M)}</small></h2>${pctCols(c.loss)}</section>
      <section class="gb s6"><h2>Kicks, bans e VPN vistos</h2>${bars(c.kicks.map((k) => ({ k: k.k, v: k.n, cls: k.tone || '' })))}</section>
      <section class="gb s12"><h2>Regiões mais bloqueadas <small>% das instalações-dia que bloqueiam cada datacenter da Valve · n=${nf(c.regionsDen)}</small></h2>
        <div style="columns:2 260px;column-gap:28px">${shareBars(c.regions.map((r) => ({ k: r.name, kHtml: `<span class="mono">${esc(r.code)}</span> ${esc(r.name)}`, n: r.n })), c.regionsDen, { noSub: true })}</div></section>
    </div>`;
  };

  P.fiab = () => {
    const R = D.rel, up = R.updates;
    const cf = ratio(R.sessions - R.crashes, R.sessions, 1);
    const lk = ratio(R.leak.fail, R.leak.pass + R.leak.fail, 1);
    return `<div class="g">
      <section class="gb s12"><h2>Alertas ${unf()}</h2>${D.alerts.list.map((a) => `<div class="sug"><span class="sq ${a.tone}"></span><div><b>${esc(a.text)}</b><div class="why">${esc(a.nText)}</div></div><span class="dim">${esc(a.when)}</span></div>`).join('')}</section>
      <section class="gb s4"><h2>Sessões sem crash</h2><div class="big ${cf.v == null || cf.cls ? 'low' : cf.v >= 99 ? 'good' : 'warn'}">${cf.txt}</div><div class="nn">${plural(R.crashes, 'crash', 'crashes')} em ${plural(R.sessions, 'sessão', 'sessões')}</div>
        <h3>Onde caiu</h3>${R.crashPlaces.length ? `<div class="tw"><table class="t"><tbody>${R.crashPlaces.map((c) => `<tr><td class="mono">${esc(c.at)}</td><td class="mono dim">${esc(c.ver)}</td><td class="n">${nf(c.n)}</td><td>${c.fresh ? '<span class="chip bad">novo</span>' : ''}</td></tr>`).join('')}</tbody></table></div>` : '<p class="note">Nenhum crash com estes filtros.</p>'}</section>
      <section class="gb s8"><h2>Crashes por dia <small>colunas · linha = crashes por 100 sessões, janela de 7 dias</small></h2><div class="chart" id="fbCrash"></div><div class="chart" id="fbRate" style="margin-top:8px"></div></section>
      <section class="gb s12"><h2>Erros <small>tipo e sítio no código · ${periodLabel()}</small></h2>${R.errors.length ? `<div class="tw"><table class="t"><thead><tr><th>Erro</th><th>Onde</th><th>Versão</th><th class="n">Vezes</th><th class="n">Instalações-dia</th><th>14 dias</th><th></th></tr></thead><tbody>
        ${R.errors.map((e) => `<tr><td>${esc(e.type)}</td><td class="mono">${esc(e.at)}</td><td class="mono dim">${esc(e.ver)}</td><td class="n">${nf(e.n)}</td><td class="n">${nf(e.days)}</td><td>${spark(e.trend, { w: 90, h: 20, color: e.up ? 'var(--bad)' : 'var(--ink3)' })}</td><td>${e.fresh ? '<span class="chip bad">novo nesta versão</span>' : e.up ? '<span class="chip warn">a subir</span>' : ''}</td></tr>`).join('')}</tbody></table></div>` : '<p class="note">Nenhum erro com estes filtros.</p>'}</section>
      <section class="gb s4"><h2>Verificações a vermelho <small>painel Checks, relatórios</small></h2>${bars(R.checksRed.map((c) => ({ k: c.k, v: c.n, cls: 'bad' })), { kw: '58%' })}</section>
      <section class="gb s4"><h2>Teste de fugas</h2><div class="statrow"><div class="stat"><div class="kl">Passou</div><div class="kv good">${nf(R.leak.pass)}</div></div><div class="stat"><div class="kl">Falhou</div><div class="kv ${R.leak.fail ? 'bad' : ''}">${nf(R.leak.fail)}</div><div class="nn">${lk.v == null ? esc(lk.note) : `${lk.txt} dos testes`}</div></div></div>
        ${stack([['passou', R.leak.pass, 'var(--good)'], ['falhou', R.leak.fail, 'var(--bad)']])}
        <h3>Arranque até a app estar pronta <small class="dim">sessões</small></h3>${cols(R.startup.map((x) => ({ band: x.band, n: x.n })), [{ k: 'n', name: 'Sessões', color: 'var(--acc)' }], { h: 80 })}</section>
      <section class="gb s4"><h2>Atualizações</h2>${bars([{ k: 'Viram o aviso', v: up.seen }, { k: 'Aceitaram', v: up.accepted }, { k: 'Concluíram', v: up.done, cls: 'good' }, { k: 'Recusaram', v: up.declined, cls: 'warn' }, { k: 'Falharam', v: up.failed, cls: 'bad' }])}
        <h3>Vieram de</h3>${bars(up.from.map((x) => ({ k: x.v, v: x.n, cls: 'dim' })))}</section>
    </div>`;
  };
  P.fiab.after = () => {
    const cs = D.rel.crashSeries, off = cs.length - D.chartN, seg = cs.slice(off);
    const dates = seg.map((d) => d.date);
    chart($('#fbCrash'), { dates, type: 'bars', h: 130, fmt: (v) => nf(v), yMax: Math.max(2, ...seg.map((d) => d.crashes)), yFmt: (v) => (v % 1 ? '' : nf(v)), series: [{ name: 'Crashes', values: seg.map((d) => d.crashes), color: 'var(--bad)' }], tipFoot: (i) => `n = ${plural(seg[i].sessions, 'sessão', 'sessões')}` });
    const rate = seg.map((_, i) => { const w = cs.slice(off + i - 6, off + i + 1); const s = sum(w, (d) => d.sessions); return s >= LOW ? (100 * sum(w, (d) => d.crashes)) / s : null; });
    chart($('#fbRate'), { dates, h: 130, markers: false, fmt: (v) => nf(v, 1), series: [{ name: 'Crashes por 100 sessões (7 dias)', values: rate, color: 'var(--ink2)' }] });
  };

  P.versoes = () => {
    const vs = Object.keys(D.versions.metrics);
    const A = D.versions.metrics[S.cmpA], B = D.versions.metrics[S.cmpB];
    const BANDS = ['<1 s', '1-2 s', '2-4 s', '4-8 s', '>8 s', '<10 s', '10-30 s', '30-60 s', '1-3 min', '>3 min'];
    const row = (label, a, b, better, unit) => {
      if (a.v == null || b.v == null) return `<tr><td>${label}</td><td class="n low">${a.txt}</td><td class="n low">${b.txt}</td><td class="d dim">-</td></tr>`;
      const dv = a.v - b.v, good = better === 'up' ? dv > 0 : dv < 0, low = a.cls === 'low' || b.cls === 'low';
      const tone = low || Math.abs(dv) < 1e-9 ? 'dim' : good ? 'good' : 'bad';
      return `<tr><td>${label}</td><td class="n ${a.cls}">${a.txt}</td><td class="n ${b.cls}">${b.txt}</td><td class="d ${tone}">${dv >= 0 ? '+' : '−'}${nf(Math.abs(dv), 1)}${unit}</td></tr>`;
    };
    const band = (label, a, b) => { const d = BANDS.indexOf(a) - BANDS.indexOf(b); return `<tr><td>${label} <small class="dim">banda mediana</small></td><td class="n">${esc(a)}</td><td class="n">${esc(b)}</td><td class="d ${d === 0 ? 'dim' : d < 0 ? 'good' : 'bad'}">${d === 0 ? 'igual' : `${Math.abs(d)} ${Math.abs(d) === 1 ? 'banda' : 'bandas'} ${d < 0 ? 'mais rápida' : 'mais lenta'}`}</td></tr>`; };
    const num = (v, n) => (n >= MIN ? { v, txt: nf(v, v % 1 ? 1 : 0), cls: n < LOW ? 'low' : '' } : { v: null, txt: '-', cls: 'low' });
    const opt = (sel) => vs.map((v) => `<option value="${v}"${v === sel ? ' selected' : ''}>${v}</option>`).join('');
    const gh = Object.fromEntries(D.github.releases.map((r) => [r.tag, r.exe]));
    return `<div class="g">
      <section class="gb s12"><h2>Adoção ao longo do tempo <small>% das instalações abertas em cada dia</small> ${unf()}</h2><div class="chart" id="vsAdopt"></div><div id="vsLeg"></div></section>
      <section class="gb s7"><h2>Comparar duas versões <small>últimos 7 dias</small> ${unf()}</h2>
        <div class="cmphead"><select class="sel" id="cmpA" aria-label="Versão A">${opt(S.cmpA)}</select> contra <select class="sel" id="cmpB" aria-label="Versão B">${opt(S.cmpB)}</select></div>
        <div class="tw"><table class="t cmp"><thead><tr><th></th><th class="n">${esc(S.cmpA)}</th><th class="n">${esc(S.cmpB)}</th><th class="n">Diferença</th></tr></thead><tbody>
        <tr><td>Instalações-dia</td><td class="n">${nf(A.days)}</td><td class="n">${nf(B.days)}</td><td class="d dim">${A.days - B.days >= 0 ? '+' : '−'}${nf(Math.abs(A.days - B.days))}</td></tr>
        ${row('Sessões sem crash', ratio(A.sessions - A.crashes, A.sessions, 1), ratio(B.sessions - B.crashes, B.sessions, 1), 'up', ' pp')}
        ${row('Trocas com IP novo', ratio(...A.ipNew), ratio(...B.ipNew), 'up', ' pp')}
        ${row('Partidas com lag', ratio(...A.lag), ratio(...B.lag), 'down', ' pp')}
        ${row('Minutos por sessão (mediana)', num(A.minMed, A.sessions), num(B.minMed, B.sessions), 'up', ' min')}
        ${row('Erros por 100 sessões', num((100 * A.errors) / A.sessions, A.sessions), num((100 * B.errors) / B.sessions, B.sessions), 'down', '')}
        ${band('Até Protected', A.toProtected, B.toProtected)}${band('Até a app estar pronta', A.startup, B.startup)}</tbody></table></div>
        <p class="note">Verde = a ${esc(S.cmpA)} está melhor. A cinzento: menos de 30 sessões, partidas ou trocas; aí a diferença é ruído.</p></section>
      <section class="gb s5"><h2>Versões ${unf()}</h2><div class="tw"><table class="t"><thead><tr><th>Versão</th><th>Saiu</th><th class="n">Downloads do exe</th></tr></thead><tbody>
        ${[...D.releases].reverse().map((r) => `<tr><td class="mono">${esc(r.tag)}</td><td>${fdate(r.date)}</td><td class="n">${nf(gh[r.tag] ?? 0)}</td></tr>`).join('')}</tbody></table></div></section>
    </div>`;
  };
  P.versoes.after = () => {
    const ad = D.versions.adoption, tags = D.releases.map((r) => r.tag);
    const shades = [['#1f3346', '#cfe0f0'], ['#2d4f70', '#e2eef9'], ['#3f6f9f', '#fff'], ['#2b74c2', '#fff'], ['#6fb2f2', '#08131e'], ['#c6e2fb', '#08131e']];
    const series = tags.map((t, i) => ({ name: t, values: ad.map((d) => (d.shares[t] || 0) * 100), color: shades[i % shades.length][0], ink: shades[i % shades.length][1] })).filter((s) => s.values.some((v) => v > 0.5));
    chart($('#vsAdopt'), { dates: ad.map((d) => d.date), stacked: true, direct: true, h: 220, yMax: 100, fmt: (v) => pc(v), yFmt: (v) => `${nf(v)}%`, series });
    $('#vsLeg').innerHTML = legend(series.map((s) => [s.name, s.color]));
    ['cmpA', 'cmpB'].forEach((id) => $(`#${id}`).addEventListener('change', (e) => { S[id] = e.target.value; renderPage(); }));
  };

  const LANG = { pt: 'português', en: 'inglês', ru: 'russo', es: 'espanhol', de: 'alemão', pl: 'polaco', tr: 'turco', outro: '' };
  P.ambiente = () => {
    const E = D.env, N = E.reports;
    const box = (cls, title, rows, o = {}) => `<section class="gb ${cls}"><h2>${title} ${unf('sem ID')}</h2>${shareBars(rows, N, o)}${o.note ? `<p class="note">${o.note}</p>` : ''}</section>`;
    return `<p class="note" style="margin:-6px 0 16px">Relatório à parte, sem ID, uma vez por dia por instalação; o servidor guarda só contagens. Não cruza com outras dimensões nem responde aos filtros. n = ${plural(N, 'relatório', 'relatórios')} ${periodShort()}.</p>
    <div class="g">
      ${box('s4', 'Idioma do Windows', E.lang.map((r) => ({ k: r.k, kHtml: `<span class="mono">${esc(r.k)}</span> ${esc(LANG[r.k] || '')}`, n: r.n })), { note: 'A app está só em inglês.' })}
      ${box('s4', 'Windows', E.windows)}
      ${box('s4', 'Rede do telemóvel', E.network)}
      ${box('s6', 'Tamanho da janela', E.window)}
      ${box('s6', 'Escala do ecrã', E.scale)}
      ${box('s6', 'Marca do telemóvel', E.brands, { note: 'Nunca o modelo.' })}
      ${box('s6', 'Android', E.android)}
    </div>`;
  };

  P.melhorar = () => {
    const sug = suggestions();
    return `<div class="g"><section class="gb s12"><h2>Sugestões <small>${sug.length} · ${periodLabel()}</small></h2>
      <p class="note" style="margin:0 0 10px">Cada linha diz o número que a justifica e a regra que a disparou. O que está partido vem primeiro; o resto por instalações-dia afetadas. Com filtros, só entram regras que os respeitam.</p>${sugHtml(sug)}
      <div class="legend" style="margin-top:12px"><span><i style="background:var(--bad)"></i>partido</span><span><i style="background:var(--warn)"></i>atrito na UI</span><span><i style="background:var(--acc)"></i>oportunidade</span><span><i style="background:var(--good)"></i>a funcionar</span><span><i style="background:var(--ink3)"></i>arrumação</span></div></section></div>`;
  };

  P.github = () => {
    const G = D.github;
    const dl = sum(G.releases, (r) => r.exe);
    const conv = anyFilter() ? { txt: '-', note: 'só sem filtros' } : (() => { const q = ratio(D.funnel[0].n, G.downloadsPeriod); return { txt: q.txt, note: q.note }; })();
    return `<div class="g">
      <section class="gb s12"><h2>GitHub ${unf()}</h2><div class="kstrip">
        <div class="kc"><div class="kl">Estrelas</div><div class="kv">${nf(G.stars)}</div><div class="nn">${plural(G.forks, 'fork', 'forks')} · ${nf(G.watchers)} a seguir</div></div>
        <div class="kc"><div class="kl">Visitas, 14 dias</div><div class="kv">${nf(G.views)}</div><div class="nn">${nf(G.uniques)} visitantes únicos (GitHub)</div></div>
        <div class="kc"><div class="kl">Clones, 14 dias</div><div class="kv">${nf(G.clones)}</div><div class="nn">${nf(G.cloneUniques)} únicos (GitHub)</div></div>
        <div class="kc"><div class="kl">Downloads do exe</div><div class="kv">${nf(dl)}</div><div class="nn">todas as versões</div></div>
        <div class="kc"><div class="kl">1.º arranque por download</div><div class="kv">${conv.txt}</div><div class="nn">${esc(conv.note)} · ${periodLabel()}</div></div>
        <div class="kc"><div class="kl">Issues abertas</div><div class="kv">${nf(G.issues)}</div></div></div></section>
      <section class="gb s8"><h2>Visitas por dia <small>14 dias, o que o GitHub guarda</small></h2><div class="chart" id="ghViews"></div>${legend([['visitas', 'var(--acc)'], ['visitantes únicos', 'var(--acc2)']])}</section>
      <section class="gb s4"><h2>De onde vêm</h2>${bars(G.referrers.map((r) => ({ k: r.k, v: r.n, sub: `${r.u}`, tip: `${r.k}: ${r.n} visitas, ${r.u} únicos` })))}</section>
      <section class="gb s6"><h2>Downloads por versão</h2>${bars(G.releases.map((r) => ({ k: `${r.tag} · ${fdate(r.date)}`, v: r.exe })))}</section>
      <section class="gb s6"><h2>Páginas mais vistas</h2>${bars(G.paths.map((r) => ({ k: r.k, kHtml: `<span class="mono">${esc(r.k)}</span>`, v: r.n, sub: `${r.u}` })))}</section>
    </div>`;
  };
  P.github.after = () => {
    const G = D.github;
    chart($('#ghViews'), { dates: G.days.map((d) => d.date), h: 180, series: [{ name: 'Visitas', values: G.days.map((d) => d.n), color: 'var(--acc)', area: true }, { name: 'Únicos', values: G.days.map((d) => d.u), color: 'var(--acc2)' }] });
  };

  // ------------------------------------------------------------------ casca: navegação, filtros, cabeçalho
  function renderNav() {
    const n = suggestions().filter((s) => s.tone === 'bad' || s.tone === 'warn').length;
    $('#nav').innerHTML = PAGES.map((p) => (p ? `<a href="#${p.id}" data-page="${p.id}"${p.id === S.page ? ' aria-current="page"' : ''}>${icon(p.icon)}<span>${esc(p.name)}</span>${p.id === 'melhorar' && n ? `<span class="cnt">${n}</span>` : ''}</a>` : '<div class="sep"></div>')).join('')
      + '<div class="navnote">Unidade: instalação-dia; o ID muda todos os dias. O IP chega à Cloudflare como em qualquer pedido e não é lido nem guardado.</div>';
  }
  function renderFilters() {
    const o = D.options, f = S.f;
    const sel = (k, label) => `<label>${label}<select class="sel" data-f="${k}">${o[k].map(([v, t]) => `<option value="${v}"${f[k] === v ? ' selected' : ''}>${esc(t)}</option>`).join('')}</select></label>`;
    const act = ['version', 'country', 'mode', 'link'].filter((k) => f[k] !== 'all').length;
    $('#filters').innerHTML = sel('version', 'Versão') + sel('country', 'País') + sel('mode', 'Modo') + sel('link', 'Ligação')
      + (act ? `<span class="active-n">${act} ${act > 1 ? 'filtros' : 'filtro'} · os blocos "não filtrado" não mudam</span><button class="reset" id="resetF">Limpar</button>` : '');
    document.documentElement.classList.toggle('filtered', act > 0);
    $$('[data-f]').forEach((s) => s.addEventListener('change', () => { S.f[s.dataset.f] = s.value; refresh(); }));
    const rb = $('#resetF'); if (rb) rb.addEventListener('click', () => { Object.assign(S.f, { version: 'all', country: 'all', mode: 'all', link: 'all' }); refresh(); });
    $$('#period button').forEach((b) => b.setAttribute('aria-pressed', String(+b.dataset.p === f.period)));
  }
  function renderFooter() {
    $('#foot').innerHTML = 'Unidade dos gráficos por dia: instalação-dia. Desde a beta.9 o ID é fixo por instalação (aleatório, nunca do PC nem da pessoa; Delete my stats na app apaga tudo dele), por isso instalações diferentes, novas, as que voltaram e as coortes são exatas; na beta.8 o ID mudava todos os dias e essas contas usam a idade em bandas (0, 1, 2-3, 4-6, 7-13, 14-29, 30+). O ambiente vem num relatório à parte, sem ID, só em contagens. O IP chega à Cloudflare em cada pedido, como em qualquer site, e não é lido nem guardado: os logs e a observability do Worker estão desligados. Percentagens com menos de 5 casos mostram "-"; com menos de 30 ficam a cinzento. Builds de desenvolvimento ficam de fora. Dias em UTC; "Quando usam" em hora de Lisboa. Grelha de cliques: 24×16 células sobre a área à direita da barra lateral da app.';
  }
  function setLivePill() { $('#liveN').textContent = lc(D.live.now); }

  function animateNumbers(root) {
    $$('[data-anim]', root).forEach((el) => {
      const id = el.dataset.anim, to = +el.dataset.to, dec = +el.dataset.dec, unit = el.dataset.unit || '';
      const from = shown.has(id) ? shown.get(id) : to;
      shown.set(id, to);
      const put = (v) => { el.innerHTML = `${nf(v, dec)}${unit ? `<small>${unit}</small>` : ''}`; };
      if (RM.matches || from === to) { put(to); return; }
      const t0 = performance.now();
      const step = (t) => { const u = Math.min(1, (t - t0) / 380); put(from + (to - from) * (1 - (1 - u) ** 3)); if (u < 1) requestAnimationFrame(step); };
      requestAnimationFrame(step);
    });
  }

  function renderPage() {
    const page = PAGES.find((p) => p && p.id === S.page) || PAGES[0];
    const main = $('#main');
    main.innerHTML = `<section class="page on" id="page-${page.id}"><div class="ptitle"><h1>${esc(page.name)}</h1><p>${esc(page.desc)}</p></div>${P[page.id]()}</section>`;
    $('#crumbPage').textContent = page.name;
    document.title = `${page.name} · CS2 IP Changer stats`;
    if (P[page.id].after) P[page.id].after();
    animateNumbers(main);
  }

  async function refresh() { if (window.fetchReal) { try { await fetchReal(S.f); } catch (e) { showErr(e); return; } } load(); renderFilters(); renderNav(); renderPage(); setLivePill(); }
  function showErr(e) { $('#main').innerHTML = `<section class="gb s12"><h2>Não deu para ler os dados</h2><p class="note">${esc(String(e && e.message || e))}</p></section>`; }

  function route() {
    const [pg, sub] = location.hash.replace('#', '').split('/');
    S.page = PAGES.some((p) => p && p.id === pg) ? pg : 'geral';
    if (S.page === 'app' && sub && UI.screens.some((s) => s.id === sub)) S.screen = sub;
    store.set('page', S.page);
    $$('#nav a').forEach((a) => (a.dataset.page === S.page ? a.setAttribute('aria-current', 'page') : a.removeAttribute('aria-current')));
    const cur = $(`#nav a[data-page="${S.page}"]`); if (cur && innerWidth <= 900) cur.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    renderPage();
    window.scrollTo(0, 0);
  }

  // ------------------------------------------------------------------ ao vivo (na maquete, simulado; em produção, /api/live a cada 15 s)
  const liveR = mulberry(Date.now() & 0xffff);
  let clock = 0;
  async function liveTick() {
    if (document.hidden || !D) return;
    if (window.fetchLiveReal) {
      const l = await fetchLiveReal(); if (!l) return;
      Object.assign(D.live, l);
      D.live.lastHour.open = [...D.live.lastHour.open.slice(1), l.now];
      D.live.lastHour.game = [...D.live.lastHour.game.slice(1), l.inGame];
      setLivePill();
      $$('[data-live-box="head"]').forEach((el) => { el.innerHTML = liveHead(); });
      $$('[data-live-box="agg"]').forEach((el) => { el.innerHTML = liveAggHtml(); });
      if ($('#lvHour')) liveMini($('#lvHour'), 120);
      if ($('#ovLive')) liveMini($('#ovLive'), 70);
      return;
    }
    // um tick troca o estado inteiro de uma vez: agora, em jogo e as divisões batem sempre umas com as outras
    const next = Math.max(0, Math.min(12, D.live.now + (liveR() < 0.3 ? (liveR() > 0.5 ? 1 : -1) : 0)));
    Object.assign(D.live, mockLiveState(liveR, next, S.f));
    D.live.lastHour.open = [...D.live.lastHour.open.slice(1), D.live.now];
    D.live.lastHour.game = [...D.live.lastHour.game.slice(1), D.live.inGame];
    clock = Math.max(clock, D.live.feed[0]?.t || 0) + (liveR() > 0.6 ? 300000 : 0);
    const ev = mockLiveEvent(liveR, clock, S.f);
    D.live.feed.unshift(ev); D.live.feed.length = Math.min(D.live.feed.length, 40);
    setLivePill();
    $$('[data-live-box="head"]').forEach((el) => { el.innerHTML = liveHead(); });
    $$('[data-live-box="agg"]').forEach((el) => { el.innerHTML = liveAggHtml(); });
    if ($('#lvHour')) liveMini($('#lvHour'), 120);
    if ($('#ovLive')) liveMini($('#ovLive'), 70);
    for (const [id, n] of [['#lvFeed', 18], ['#ovFeed', 4]]) {
      const box = $(id); if (!box) continue;
      const ul = $('ul', box);
      const tmp = document.createElement('ul'); tmp.innerHTML = feedLi(ev);
      const li = tmp.firstChild; if (!RM.matches) li.classList.add('fresh');
      ul.prepend(li);
      while (ul.children.length > n) ul.lastChild.remove();
    }
  }

  // ------------------------------------------------------------------ exportar CSV
  function csv() {
    let rows;
    if (S.page === 'app') {
      const s = UI.screens.find((q) => q.id === S.screen), d = D.screens[S.screen];
      rows = [['ecra', 'botao', 'cliques', 'instalacoes_dia'], ...s.buttons.map((b) => [s.name, b.label, d.buttons[b.id].n, d.buttons[b.id].days])];
    } else if (S.page === 'fiab') {
      rows = [['erro', 'onde', 'versao', 'vezes', 'instalacoes_dia'], ...D.rel.errors.map((e) => [e.type, e.at, e.ver, e.n, e.days])];
    } else {
      const keys = Object.keys(D.days[0]);
      rows = [keys, ...D.days.map((d) => keys.map((k) => d[k] ?? ''))];
    }
    const text = rows.map((r) => r.map((v) => (/[",;\n]/.test(String(v)) ? `"${String(v).replace(/"/g, '""')}"` : v)).join(',')).join('\n');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }));
    a.download = `cs2ipc-${S.page}${S.page === 'app' ? `-${S.screen}` : ''}-${D.period}d.csv`;
    document.body.append(a); a.click(); setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 500);
  }

  // ------------------------------------------------------------------ arranque
  function setHdr() { document.documentElement.style.setProperty('--hdr', `${$('#hdr').offsetHeight}px`); }
  function setPal(p) {
    document.documentElement.dataset.pal = p; store.set('pal', p);
    $$('.pal button').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.pal === p)));
    LUT = null;
  }

  async function init() {
    const qs = new URLSearchParams(location.search);
    const pal = qs.get('pal') || store.get('pal'); setPal(['grafite', 'quente', 'contraste'].includes(pal) ? pal : 'grafite');
    if ([1, 7, 30, 90].includes(+qs.get('p'))) S.f.period = +qs.get('p');
    for (const k of ['version', 'country', 'mode', 'link']) { const v = qs.get(k); if (v && (window.realData || MOCK.options[k].some(([x]) => x === v))) S.f[k] = v; }
    try { const l = JSON.parse(store.get('layers2') || 'null'); if (l) Object.assign(S.layers, l); } catch { /* ignora */ }
    if (window.fetchReal) { try { await fetchReal(S.f); } catch (e) { showErr(e); return; } }
    load();
    if (D.realBar && document.querySelector('.mockbar')) document.querySelector('.mockbar').innerHTML = `<span>${esc(D.realBar)}</span>`;
    renderFilters(); renderNav(); renderFooter(); setLivePill();
    if (!location.hash) { const last = store.get('page'); if (last && last !== 'geral') history.replaceState(null, '', `#${last}`); }
    route();
    addEventListener('hashchange', route);
    $$('#period button').forEach((b) => b.addEventListener('click', () => { S.f.period = +b.dataset.p; refresh(); }));
    $$('.pal button').forEach((b) => b.addEventListener('click', () => { setPal(b.dataset.pal); renderPage(); }));
    $('#csv').addEventListener('click', csv);
    setHdr();
    let rt = 0, lastW = innerWidth;
    addEventListener('resize', () => { clearTimeout(rt); rt = setTimeout(() => { setHdr(); if (innerWidth !== lastW) { lastW = innerWidth; renderPage(); } }, 160); });
    setInterval(liveTick, window.fetchLiveReal ? 60000 : 6000);
  }
  init();
})();
