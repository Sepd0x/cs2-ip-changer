// Dashboard v5 (5 nov. 2026): desenha a página do dono a partir do model.js. Três pedidos (/api/brief, /api/github, /api/live),
// o ao vivo outra vez a cada minuto e o resto a cada 5. Gráficos em SVG feitos aqui (sem bibliotecas: a CSP só deixa 'self').
// Os números contam até ao valor ao aparecer (menos com "reduzir movimento"). Nada de jargão: o detalhe está em /data.
import { buildHome, num, dayPt } from './model.js';

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const REDUCE = matchMedia('(prefers-reduced-motion: reduce)').matches;
const store = { get: (k) => { try { return localStorage.getItem('v5.' + k); } catch { return null; } }, set: (k, v) => { try { localStorage.setItem('v5.' + k, v); } catch { /* sem armazenamento */ } } };

let H = null;
let metric = store.get('metric') || 'active';
let first = true;

const get = async (u) => {
  const r = await fetch(u, { credentials: 'same-origin', cache: 'no-store' });
  if (!r.ok) throw new Error(`${u} respondeu ${r.status}`);
  return r.json();
};

// ------------------------------------------------ números que contam até ao valor
function countTo(el, to) {
  if (to == null) { el.textContent = '—'; el._v = null; return; }
  const from = el._v ?? 0;
  el._v = to;
  if (REDUCE || from === to) { el.textContent = num(to); return; }
  const t0 = performance.now(), dur = first ? 1100 : 500;
  const step = (t) => {
    const k = Math.min(1, (t - t0) / dur), e = 1 - Math.pow(1 - k, 4);
    el.textContent = num(from + (to - from) * e);
    if (k < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}
const counters = (root) => { for (const el of $$('[data-count]', root)) countTo(el, el.dataset.count === '' ? null : +el.dataset.count); };
const unit = (t, n) => (t.unit ? t.unit[n === 1 ? 0 : 1] : '');
const chip = (d) => (d ? `<span class="chip ${d.dir}">${d.dir === 'up' && !/mais$/.test(d.text) ? '↑ ' : d.dir === 'down' ? '↓ ' : ''}${esc(d.text)}</span>` : '');

/** Arredonda o topo do eixo para 1, 2, 2,5 ou 5 vezes uma potência de 10; nunca abaixo de 10 (com 1 ou 2, cada um parecia um pico). */
function niceMax(m) {
  if (m <= 10) return 10;
  const p = Math.pow(10, Math.floor(Math.log10(m))), f = m / p;
  return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * p;
}
/** Coluna com 4 px arredondados em cima e quadrada na base. */
function barPath(x, y, w, base) {
  const h = base - y; if (h <= 0.5) return '';
  const r = Math.min(4, w / 2, h);
  return `M${x.toFixed(1)},${base}V${(y + r).toFixed(1)}Q${x.toFixed(1)},${y.toFixed(1)} ${(x + r).toFixed(1)},${y.toFixed(1)}H${(x + w - r).toFixed(1)}Q${(x + w).toFixed(1)},${y.toFixed(1)} ${(x + w).toFixed(1)},${(y + r).toFixed(1)}V${base}Z`;
}

// ------------------------------------------------ o gráfico: uma coluna por dia (as estrelas, que são um total, numa linha)
function drawChart(host, tile, markers) {
  const W = Math.max(280, host.clientWidth), small = W < 560, Hh = small ? 190 : 250;
  const L = small ? 28 : 36, R = 4, T = 10, Bm = 34;
  const pts = tile.series, n = pts.length;
  const vals = pts.map((p) => p.v);
  const ok = vals.filter((x) => x != null);
  if (!n || !ok.length) { host.innerHTML = `<p class="none">${tile.id === 'downloads' ? 'O GitHub ainda não deu os downloads por dia (chegam com o retrato diário).' : 'Ainda não há dias com este número.'}</p>`; return; }
  const top = niceMax(Math.max(0, ...ok));
  const base = Hh - Bm;
  const slot = (W - L - R) / n, bw = Math.max(2, Math.min(24, slot - 2));
  const X = (i) => L + i * slot + (slot - bw) / 2, CX = (i) => X(i) + bw / 2;
  const Y = (v) => T + (1 - v / top) * (base - T);
  const grid = [0, top / 2, top].map((g) => `<line class="gl" x1="${L}" x2="${W - R}" y1="${Y(g).toFixed(1)}" y2="${Y(g).toFixed(1)}"/><text class="yl" x="${L - 8}" y="${(Y(g) + 4).toFixed(1)}">${num(g)}</text>`).join('');
  let marks = '';
  if (tile.cumulative) {
    let d = '', area = '', s = null, pen = false;
    vals.forEach((v, i) => { if (v == null) { pen = false; return; } d += `${pen ? 'L' : 'M'}${CX(i).toFixed(1)},${Y(v).toFixed(1)}`; if (s == null) s = i; pen = true; });
    const e = vals.reduce((a, v, i) => (v != null ? i : a), -1);
    if (s != null && e > s) area = `${d}L${CX(e).toFixed(1)},${base}L${CX(s).toFixed(1)},${base}Z`;
    marks = `<path class="area" d="${area}"/><path class="line" d="${d}"/>${e >= 0 ? `<circle class="end" cx="${CX(e).toFixed(1)}" cy="${Y(vals[e]).toFixed(1)}" r="4"/>` : ''}`;
  } else marks = vals.map((v, i) => (v ? `<path class="bar" data-i="${i}" d="${barPath(X(i), Y(v), bw, base)}"/>` : '')).join('');
  const xi = [0, Math.round((n - 1) / 2), n - 1];
  const xl = xi.map((i, k) => `<text class="xl" x="${(k === 0 ? X(i) : k === 2 ? X(i) + bw : CX(i)).toFixed(1)}" y="${Hh - 6}" text-anchor="${k === 0 ? 'start' : k === 2 ? 'end' : 'middle'}">${dayPt(pts[i].date)}</text>`).join('');
  const mk = [...new Set(markers.map((m) => m.date))].map((d) => { const i = pts.findIndex((p) => p.date === d); return i < 0 ? '' : `<circle class="mk" cx="${CX(i).toFixed(1)}" cy="${base + 8}" r="3.5"/>`; }).join('');
  host.innerHTML = `<svg class="big" viewBox="0 0 ${W} ${Hh}" width="${W}" height="${Hh}" role="img" aria-label="${esc(tile.what)}, últimos ${n} dias">
    <defs><linearGradient id="fade" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="var(--acc)" stop-opacity=".22"/><stop offset="1" stop-color="var(--acc)" stop-opacity="0"/></linearGradient></defs>
    ${grid}<g class="plot">${marks}</g><line class="base" x1="${L}" x2="${W - R}" y1="${base}" y2="${base}"/>${mk}${xl}
    <line class="cross" x1="0" x2="0" y1="${T}" y2="${base}" visibility="hidden"/><rect class="hit" x="${L}" y="0" width="${W - L - R}" height="${Hh}"/></svg>`;
  const svg = $('svg', host), cross = $('.cross', svg), tip = $('#tip');
  if (!REDUCE) { const g = $('.plot', svg); g.classList.add('draw'); requestAnimationFrame(() => requestAnimationFrame(() => g.classList.remove('draw'))); }
  const show = (ev) => {
    const r = svg.getBoundingClientRect(), px = ((ev.clientX - r.left) / r.width) * W;
    const i = Math.max(0, Math.min(n - 1, Math.floor((px - L) / slot)));
    const p = pts[i];
    cross.setAttribute('x1', CX(i)); cross.setAttribute('x2', CX(i)); cross.setAttribute('visibility', 'visible');
    for (const b of $$('.bar', svg)) b.classList.toggle('on', +b.dataset.i === i);
    svg.classList.add('hovering');
    const m = markers.filter((x) => x.date === p.date).map((x) => x.label);
    tip.innerHTML = `<div class="t-d">${dayPt(p.date)}</div><div class="t-v">${p.v == null ? 'sem dados' : `<b>${num(p.v)}</b> ${esc(unit(tile, p.v))}`}</div>${m.length ? `<div class="t-m">${esc(m.join(' · '))}</div>` : ''}`;
    tip.hidden = false;
    const tx = r.left + (CX(i) / W) * r.width, tw = tip.offsetWidth;
    tip.style.left = `${Math.max(8, Math.min(innerWidth - tw - 8, tx - tw / 2)) + scrollX}px`;
    tip.style.top = `${r.top + scrollY + ((p.v != null ? Y(p.v) : base) / Hh) * r.height - tip.offsetHeight - 12}px`;
  };
  const hide = () => { cross.setAttribute('visibility', 'hidden'); svg.classList.remove('hovering'); for (const b of $$('.bar', svg)) b.classList.remove('on'); tip.hidden = true; };
  const hit = $('.hit', svg);
  hit.addEventListener('pointermove', show); hit.addEventListener('pointerdown', show); hit.addEventListener('pointerleave', (e) => { if (e.pointerType !== 'touch') hide(); });
  // No telemóvel o balão fica até tocar noutro sítio (o dedo a sair do ecrã também é um pointerleave)
  host._hide = hide;
}

// ------------------------------------------------ blocos
function nowHtml() {
  const v = H.live;
  if (v.stale) return `<div class="now-n"><span class="dot" aria-hidden="true"></span><span class="nn">—</span></div><div class="now-t">a usar agora</div><div class="now-s">${esc(v.staleText)}</div>`;
  if (v.now == null) return '<div class="now-n"><span class="dot" aria-hidden="true"></span><span class="nn">—</span></div><div class="now-t">a usar agora</div><div class="now-s">sem sinal ao vivo</div>';
  return `<div class="now-n"><span class="dot${v.now > 0 ? ' on' : ''}" aria-hidden="true"></span><span class="nn" data-count="${v.now}">${first ? '0' : num(v.now)}</span></div>
    <div class="now-t">${v.now === 1 ? 'pessoa a usar agora' : 'pessoas a usar agora'}</div>
    <div class="now-s">${[v.now ? `${num(v.inGame)} em jogo · ${num(v.protectedN)} com a proteção ligada` : '', v.peakToday ? `máximo de hoje: ${num(v.peakToday)} ao mesmo tempo` : ''].filter(Boolean).join('<br>')}</div>`;
}
function renderHero() {
  const s = H.status;
  $('#hero').innerHTML = `<div class="hl"><h1>${esc(H.headline.text)}</h1><p class="sub">${H.updated ? `Atualizado às ${esc(H.updated)}` : ''}</p></div>
    ${H.live.stale || H.empty ? '' : `<div class="now" id="now" aria-live="polite">${nowHtml()}</div>`}
    ${s ? `<div class="banner ${s.tone}" role="status"><span class="bd" aria-hidden="true"></span><span>${esc(s.text)}${s.link ? ` <a href="${esc(s.link)}">Ver a causa</a>` : ''}</span></div>` : ''}`;
  counters($('#hero'));
}
function pill() {
  const v = H.live;
  $('#pill').innerHTML = v.stale ? `<span class="dot" aria-hidden="true"></span>${esc(v.staleText)}`
    : `<span class="dot${v.now > 0 ? ' on' : ''}" aria-hidden="true"></span>${v.now > 0 ? `${num(v.now)} a usar agora` : v.now === 0 ? 'ninguém a usar agora' : 'sem sinal ao vivo'}`;
}
function renderChanges() {
  const l = H.changes;
  $('#changes').hidden = !l.length;
  $('#changes').innerHTML = `<h2 class="vh">O que mudou</h2><ul>${l.map((c) => `<li class="${c.tone}"><span class="bd" aria-hidden="true"></span><span>${esc(c.text)}${c.link ? ` <a href="${esc(c.link)}">Ver detalhe</a>` : ''}</span></li>`).join('')}</ul>`;
}
function renderBoard() {
  if (!H.tiles.some((t) => t.id === metric)) metric = 'active';
  $('#tiles').innerHTML = H.tiles.map((t) => `<button class="tile" role="tab" id="t-${t.id}" aria-selected="${t.id === metric}" aria-controls="panel" tabindex="${t.id === metric ? 0 : -1}" data-m="${t.id}">
    <span class="t-l">${esc(t.label)}</span>
    <span class="t-n" data-count="${t.value ?? ''}">${first ? '0' : num(t.value)}</span>
    <span class="t-s">${chip(t.delta)}${t.sub ? `<span class="t-p">${esc(t.sub)}</span>` : ''}</span></button>`).join('');
  counters($('#tiles'));
  for (const b of $$('.tile')) b.addEventListener('click', () => select(b.dataset.m));
  $('#tiles').onkeydown = (e) => {
    if (!['ArrowRight', 'ArrowLeft', 'ArrowDown', 'ArrowUp'].includes(e.key)) return;
    e.preventDefault();
    const l = $$('.tile'), i = l.findIndex((x) => x.dataset.m === metric), nx = l[(i + (e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : l.length - 1)) % l.length];
    select(nx.dataset.m); nx.focus();
  };
  renderPanel();
}
function select(m) {
  metric = m; store.set('metric', m);
  for (const x of $$('.tile')) { const on = x.dataset.m === m; x.setAttribute('aria-selected', String(on)); x.tabIndex = on ? 0 : -1; }
  renderPanel();
}
function renderPanel() {
  const t = H.tiles.find((x) => x.id === metric);
  $('#panel').setAttribute('aria-labelledby', `t-${t.id}`);
  $('#panel').innerHTML = `<div class="p-h"><p>${esc(t.what)}, no último mês</p>${t.today != null ? `<p class="p-today">Hoje, até agora: <b>${num(t.today)}</b> ${esc(unit(t, t.today))}</p>` : ''}</div>
    <div class="plotbox"></div>${H.markers.length ? '<p class="legend"><i class="lg-mk"></i>dia com versão nova ou vídeo</p>' : ''}`;
  drawChart($('#panel .plotbox'), t, H.markers);
}
function renderArrive() {
  const r = H.rate && H.rate.den >= 20 && H.rate.value > 0 ? Math.max(1, Math.round(1 / H.rate.value)) : null;
  const bar = (label, n, w, val, note) => `<div class="br"><span class="br-l">${esc(label)}</span><span class="br-t"><i style="width:${w.toFixed(1)}%"></i></span><span class="br-n">${val}</span>${note ? `<span class="br-s">${esc(note)}</span>` : ''}</div>`;
  // Beta.13: two groups with their own bars (the site; GitHub and the app), never one funnel: the site doesn't know who installs
  const group = (g) => { const max = Math.max(1, ...g.rows.map((x) => x.n || 0));
    return `<h3 class="grp">${esc(g.title)}</h3><div class="bars">${g.rows.map((x) => bar(x.label, x.n, x.n ? Math.max(1.5, (100 * x.n) / max) : 0, x.n == null ? '<span class="na">sem dados</span>' : num(x.n), x.note)).join('')}</div>`; };
  $('#arrive').innerHTML = `<div class="col"><h2>Como chegam</h2><p class="lead">Últimos 7 dias completos</p>
      ${H.arrive.map(group).join('')}
      ${r ? `<p class="note">Cerca de 1 em cada ${num(r)} visitas ao site acaba num clique em Download.</p>` : ''}</div>
    <div class="col"><h2>De onde vêm as visitas</h2><p class="lead">Últimos 7 dias, em percentagem</p>
      ${H.refs.length ? `<div class="bars refs">${H.refs.map((x) => bar(x.name, x.n, 100 * x.share, `${Math.round(100 * x.share)}%`)).join('')}</div>` : '<p class="none">Ainda sem visitas com origem.</p>'}</div>`;
}
function renderJourney() {
  const j = H.journey;
  if (!j) { $('#journey').innerHTML = '<h2>Depois de instalar</h2><p class="none">Aparece quando houver pessoas com uma semana de uso. Volta daqui a uns dias.</p>'; return; }
  $('#journey').innerHTML = `<h2>Depois de instalar</h2><p class="lead">${esc(j.text)}</p>
    <ol class="steps">${j.steps.map((s) => `<li><div class="st-top"><span class="st-l">${esc(s.label)}</span><span class="st-v"><b>${num(s.n)}</b> de ${num(j.base)}${j.base >= 10 ? ` <span class="st-p">${Math.round(100 * s.share)}%</span>` : ''}</span></div>
      <span class="st-t"><i style="width:${(100 * s.share).toFixed(1)}%"></i></span></li>`).join('')}</ol>
    ${j.note ? `<p class="note">${esc(j.note)}</p>` : ''}`;
}
function renderEmpty() {
  $('#welcome').innerHTML = `<h2>Ainda não há números</h2><p>Quando alguém instalar e abrir a app, aparecem aqui:</p>
    <ul><li>quantas pessoas a usam e quantas são novas;</li><li>os downloads e as estrelas no GitHub;</li><li>quantas chegam a jogar com a proteção ligada.</li></ul>
    <p class="dim">Esta página atualiza-se sozinha.</p>`;
}

function render() {
  document.body.classList.toggle('empty', H.empty);
  renderHero(); pill(); renderChanges();
  const cap = document.querySelector('.b-cap'); if (cap && H.cap) cap.textContent = H.cap;   // beta.13: the cards' days, said with their dates
  if (H.empty) renderEmpty(); else { renderBoard(); renderArrive(); renderJourney(); }
  document.body.classList.add('ready');
  first = false;
}

async function load() {
  try {
    const [B, G, L] = await Promise.all([get('/api/brief'), get('/api/github').catch(() => null), get('/api/live').catch(() => null)]);
    H = buildHome({ B, G, L });
    render();
  } catch (e) {
    $('#hero').innerHTML = `<div class="hl"><h1>Não deu para ler os números.</h1><p class="sub">${esc(e.message || e)}. Tenta outra vez daqui a um minuto.</p></div>`;
    document.body.classList.add('ready', 'failed');
  }
}
async function liveTick() {
  if (!H || document.hidden || H.live.stale) return;
  try {
    const L = await get('/api/live');
    Object.assign(H.live, { now: L.now, inGame: L.inGame, protectedN: L.protectedN, peakToday: Math.max(H.live.peakToday || 0, L.now || 0) });
    $('#now').innerHTML = nowHtml(); counters($('#now')); pill();
  } catch { /* o próximo minuto tenta outra vez */ }
}
let rz = 0, lastW = innerWidth;
addEventListener('resize', () => { if (innerWidth === lastW) return; lastW = innerWidth; clearTimeout(rz); rz = setTimeout(() => { if (H && !H.empty) renderPanel(); }, 120); });
addEventListener('pointerdown', (e) => { if (!e.target.closest || !e.target.closest('.hit')) { const h = $('#panel .plotbox'); if (h && h._hide) h._hide(); } });
addEventListener('scroll', () => { const t = $('#tip'); if (t && !t.hidden) t.hidden = true; }, { passive: true });
load();
setInterval(liveTick, 60_000);
// 12 Nov. 2026 (D1 over its free reads): a tab left open behind others no longer asks every 5 minutes; back in view, it asks
// at once if its numbers are older than that
let loadedAt = Date.now();
const reload = () => { loadedAt = Date.now(); load(); };
setInterval(() => { if (!document.hidden && Date.now() - loadedAt >= 290_000) reload(); }, 60_000);
document.addEventListener('visibilitychange', () => { if (!document.hidden && Date.now() - loadedAt >= 300_000) reload(); });
