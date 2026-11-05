// Dashboard v5 (5 nov. 2026), a página do dono: o que se vê num relance, em frases simples. Este ficheiro só faz contas e
// escreve frases a partir do /api/brief, do /api/github e do /api/live (os mesmos números que o agente lê); quem desenha é o
// home.js. Sem jargão: nada de "instalação-dia", fusos, coortes ou regras. O detalhe exato fica em /data.
// É um módulo ES: o browser carrega-o pelo home.js e os testes do Worker importam-no tal como está.
// Críticos (publish/auditoria-2026-11-04/dashboard-v5/criticos/): no máximo 3 frases, o dia de hoje fora do gráfico (ainda a
// meio, parecia uma queda), cada cartão com um só período, "sem dados" dito por extenso e nunca um 0 quando os dados pararam.

const NF = new Intl.NumberFormat('pt-PT');
const MES = ['jan.', 'fev.', 'mar.', 'abr.', 'mai.', 'jun.', 'jul.', 'ago.', 'set.', 'out.', 'nov.', 'dez.'];
const sum = (l) => (l || []).reduce((a, x) => a + (x || 0), 0);
const has = (l) => Array.isArray(l) && l.some((x) => x != null);
const one = (x) => NF.format(Math.round(x * 10) / 10);

/** 1234 -> "1234", 12345 -> "12 345" (pt-PT); null -> "—". */
export const num = (v) => (v == null ? '—' : NF.format(Math.round(v)));
/** "2026-10-31" -> "31 out." */
export const dayPt = (d) => { const [, m, x] = String(d).split('-'); return `${+x} ${MES[+m - 1]}`; };
const pct = (x) => `${Math.round(100 * x)}%`;
const plural = (n, a, b) => `${num(n)} ${n === 1 ? a : b}`;
/** Minutos em texto: "40 min", "16 horas", "3 dias". */
export const span = (m) => (m < 60 ? `${m} min` : m < 2880 ? plural(Math.round(m / 60), 'hora', 'horas') : plural(Math.round(m / 1440), 'dia', 'dias'));

/**
 * A variação contra o período antes: { dir: 'up'|'down'|'new', text } ou null quando não há nada a dizer (0 e 0, sem antes).
 * Em percentagem só com 5 ou mais antes (de 2 para 4 não é "+100%"); abaixo disso, a diferença em número.
 */
export function delta(value, prev) {
  if (value == null || prev == null) return null;
  const d = value - prev;
  if (d === 0) return value ? { dir: 'flat', text: 'igual' } : null;
  if (prev === 0) return { dir: 'new', text: `+${num(value)}` };
  // Abaixo de 20 antes, a diferença em número (o brief marca low_n: 94 contra 17 não é "5,5×", é "+77")
  if (prev < 20) return { dir: d > 0 ? 'up' : 'down', text: `${d > 0 ? '+' : '−'}${num(Math.abs(d))}` };
  const r = value / prev;
  if (r >= 2) return { dir: 'up', text: `${one(r)}× mais` };
  return { dir: d > 0 ? 'up' : 'down', text: `${d > 0 ? '+' : '−'}${Math.round(100 * Math.abs(d / prev))}%` };
}

/** Os últimos 7 dias completos (até ontem) e os 7 antes, de uma série diária que acaba hoje: as mesmas janelas do brief. */
export function weeks(list) {
  if (!has(list)) return { d7: null, prev7: null };
  const a = list.slice(-8, -1), b = list.slice(-15, -8);
  return { d7: a.some((x) => x != null) ? sum(a) : null, prev7: b.length === 7 && b.some((x) => x != null) ? sum(b) : null };
}

const HOSTS = { 'youtube.com': 'YouTube', 'm.youtube.com': 'YouTube', 'google.com': 'Google', 'github.com': 'GitHub', 'reddit.com': 'Reddit', 'discord.com': 'Discord',
  'twitter.com': 'X (Twitter)', 'x.com': 'X (Twitter)', 't.co': 'X (Twitter)', 'tiktok.com': 'TikTok', 'bing.com': 'Bing', 'duckduckgo.com': 'DuckDuckGo', direct: 'Direto (sem link)', '': 'Direto (sem link)' };
export const hostName = (h) => HOSTS[String(h || '').replace(/^www\./, '')] || String(h).replace(/^www\./, '');

const STEP = { first_start: 'Abriram a app', phone_seen: 'Ligaram o telemóvel', protected: 'Ligaram a proteção', first_match: 'Jogaram uma partida' };

/**
 * Tudo o que a página do dono mostra, já em números e frases. B = /api/brief, G = /api/github (ou null), L = /api/live (ou null).
 * Nunca inventa: o que não tem fonte fica null e a página diz "sem dados" ou esconde o bloco.
 */
export function buildHome({ B, G = null, L = null }) {
  const K = B.kpis, S = B.series || { days: [] };
  const all = S.days || [];
  const today = B.window && B.window.today;
  // O gráfico e as contas por dia só com dias completos: o de hoje ainda vai a meio
  const cut = all.length && all.at(-1) === today ? all.length - 1 : all.length;
  const days = all.slice(0, cut);
  const pts = (list) => days.map((date, i) => ({ date, v: list ? list[i] ?? null : null }));
  const todayOf = (list) => (cut < all.length && list ? list[cut] ?? null : null);

  // ---------------- dados parados (o último relatório real é velho): nada de "0 agora"
  const fresh = B.health && B.health.checks ? B.health.checks.find((c) => c.id === 'freshness') : null;
  const staleMin = fresh && fresh.status !== 'ok' && fresh.value != null ? fresh.value : null;

  // ---------------- os seis números (cada um é também um separador do gráfico)
  const ghDays = G && G.history && Array.isArray(G.history.days) ? G.history.days : [];
  const starOn = new Map(ghDays.filter((x) => x.stars != null).map((x) => [x.day, x.stars]));
  let last = null;
  const starsSeries = days.map((d) => { if (starOn.has(d)) last = starOn.get(d); return last; });
  const exeTotal = ghDays.length && ghDays.at(-1).exe != null ? ghDays.at(-1).exe
    : G && Array.isArray(G.releases) && G.releases.length ? sum(G.releases.map((r) => r.exe ?? r.downloads ?? 0)) : null;
  const starsTotal = K.stars && K.stars.total != null ? K.stars.total : G && G.stars != null ? G.stars : null;
  const m7 = weeks(S.matches);
  const dl = K.exe_downloads || {};
  const sv = K.site_visits || {};
  // O período dos cartões diz-se uma vez, por cima deles; aqui só o que for diferente
  const W7 = null;
  const tiles = [
    { id: 'active', label: 'Pessoas a usar', value: K.active_installs.d7, prev: K.active_installs.prev7, series: pts(S.active_installs), today: K.active_installs.today,
      sub: W7, unit: ['pessoa', 'pessoas'], what: 'Pessoas diferentes que abriram a app em cada dia' },
    { id: 'new', label: 'Instalações novas', value: K.new_installs.d7, prev: K.new_installs.prev7, series: pts(S.new_installs), today: K.new_installs.today,
      sub: W7, unit: ['instalação nova', 'instalações novas'], what: 'Pessoas que abriram a app pela primeira vez, em cada dia' },
    { id: 'downloads', label: 'Downloads', value: dl.d7 ?? null, prev: typeof dl.prev7 === 'number' ? dl.prev7 : null, series: pts(S.exe_downloads), today: null,
      sub: dl.d7 == null ? 'o GitHub ainda não deu os downloads por dia' : W7, unit: ['download', 'downloads'], what: `Downloads da app no GitHub, em cada dia${exeTotal != null ? ` (${num(exeTotal)} desde o início)` : ''}` },
    { id: 'matches', label: 'Partidas jogadas', value: m7.d7, prev: m7.prev7, series: pts(S.matches), today: todayOf(S.matches),
      sub: W7, unit: ['partida', 'partidas'], what: 'Partidas de CS2 que acabaram com a app aberta, em cada dia' },
    { id: 'stars', label: 'Estrelas no GitHub', value: starsTotal, prev: null, series: pts(starsSeries), cumulative: true, today: null,
      sub: null, unit: ['estrela', 'estrelas'], what: 'Estrelas no GitHub, o total em cada dia' },
    { id: 'visits', label: 'Visitas ao site', value: sv.d7 ?? null, prev: sv.prev7 ?? null, series: pts(S.site_visits), today: sv.today ?? null,
      sub: W7, unit: ['visita', 'visitas'], what: 'Visitas ao site da app, em cada dia' },
  ];
  for (const t of tiles) {
    t.delta = t.id === 'stars' ? (K.stars && K.stars.d7_added > 0 ? { dir: 'up', text: `+${num(K.stars.d7_added)} esta semana` } : null) : delta(t.value, t.prev);
    if (staleMin != null) t.today = null;
  }

  const anyData = tiles.some((t) => t.value) || !!(L && L.now) || sum(S.active_installs) > 0;
  let status = null;
  if (anyData && staleMin != null) status = { tone: fresh.status === 'bad' ? 'bad' : 'warn', text: `Não chegam dados novos da app há ${span(staleMin)}. Até voltarem, os números de hoje ficam abaixo da realidade.`, link: '/data#sistema' };

  // ---------------- a frase do topo
  const A = tiles[0];
  const people = (n) => plural(n, 'pessoa usou', 'pessoas usaram');
  let headline;
  if (!anyData) headline = { tone: 'flat', text: 'À espera dos primeiros utilizadores.' };
  else if (A.prev != null && A.prev >= 20) {
    const r = A.value / A.prev;
    headline = r >= 1.15 ? { tone: 'up', text: `Semana a crescer: ${people(A.value)} a app, ${r >= 2 ? `${one(r)} vezes mais` : `mais ${num(A.value - A.prev)}`} do que na semana anterior.` }
      : r <= 0.85 ? { tone: 'down', text: `Semana mais calma: ${people(A.value)} a app, menos ${num(A.prev - A.value)} do que na semana anterior.` }
        : { tone: 'flat', text: `Semana estável: ${people(A.value)} a app, como na semana anterior.` };
  } else headline = { tone: A.value ? 'up' : 'flat', text: A.value ? `${people(A.value)} a app nos últimos 7 dias${A.prev ? ` (na semana anterior, ${num(A.prev)})` : ''}.` : 'Ninguém usou a app nos últimos 7 dias.' };

  // ---------------- o que mudou: até 3 frases, primeiro o que pede ação
  const cand = [];
  const AN = B.anomalies || [];
  const errs = AN.filter((a) => /^new_error:/.test(a.id));
  if (errs.length) cand.push({ p: 0, tone: 'bad', link: '/data#qualidade', text: `${errs.length === 1 ? 'Apareceu um erro novo' : `Apareceram ${num(errs.length)} erros novos`} na app${errs.some((a) => /1\.º dia/.test(a.text_pt || '')) ? `, ${errs.length === 1 ? 'que afeta' : 'que afetam'} quem a abre pela primeira vez` : ''}.` });
  const cf = K.crash_free_sessions_7d;
  if (cf && cf.den >= 20 && cf.value < 0.95) cand.push({ p: 1, tone: 'bad', link: '/data#qualidade', text: `A app fechou sozinha em ${pct(1 - cf.value)} das vezes que foi usada esta semana.` });
  // Só se o GitHub disser 0 nas duas fontes (a resposta pública e os retratos diários): a 4 nov. a pública dizia 0 e os retratos 2
  const byRel = (G && G.history && G.history.byRelease) || {};
  const rel = AN.find((a) => /^release_no_downloads:/.test(a.id) && !(byRel[a.id.split(':')[1]] || []).some((x) => x && x.n > 0));
  if (rel) cand.push({ p: 2, tone: 'warn', text: `A ${String(rel.id.split(':')[1]).replace(/^v?0\.1\.0-/, '')} ainda não tem downloads. Vê se o botão de download do site leva a esta versão.` });
  const N7 = tiles[1];
  if (N7.value && N7.prev != null && N7.prev !== N7.value) {
    const r = N7.prev ? N7.value / N7.prev : null;
    cand.push({ p: 3, tone: N7.value > N7.prev ? 'up' : 'down', text: `${plural(N7.value, 'instalação nova', 'instalações novas')} nos últimos 7 dias, ${r != null && r >= 2 && N7.prev >= 20 ? `${one(r)} vezes mais do que na semana anterior` : N7.value > N7.prev ? `mais ${num(N7.value - N7.prev)} do que na semana anterior` : `menos ${num(N7.prev - N7.value)} do que na semana anterior`}.` });
  }
  const spike = AN.find((a) => /^metric_spike:(new_installs|site_visits)@/.test(a.id));
  if (spike) {
    const day = spike.id.split('@')[1];
    cand.push({ p: 4, tone: 'up', text: `${day === today ? 'Hoje' : `A ${dayPt(day)}`} houve um pico de ${spike.metric === 'new_installs' ? 'instalações novas' : 'visitas ao site'}: ${num(spike.observed)}, quando o normal é ${num(spike.expected)}.` });
  }
  if (K.stars && K.stars.d7_added > 0) cand.push({ p: 6, tone: 'up', text: `Ganhaste ${plural(K.stars.d7_added, 'estrela', 'estrelas')} no GitHub esta semana.` });
  if (cf && cf.den >= 20 && cf.value >= 0.95) cand.push({ p: 7, tone: 'up', text: `A app correu sem fechar sozinha em ${pct(cf.value)} das vezes esta semana.` });
  const changes = cand.sort((a, b) => a.p - b.p).slice(0, 3).map(({ p, ...c }) => c);

  // ---------------- depois de instalar (quem instalou na semana anterior: já teve a primeira semana inteira)
  const act = B.funnel && B.funnel.activation;
  let journey = null;
  if (act) {
    const closed = act.steps && act.steps[0] && act.steps[0].n >= 5;
    const st = closed ? act.steps : act.running_steps;
    const w = closed ? act.window : B.window && B.window.running7;
    if (st && st[0] && st[0].n) {
      journey = { base: st[0].n, steps: st.map((x) => ({ id: x.id, label: STEP[x.id] || x.id, n: x.n, share: x.n / st[0].n })),
        text: closed ? `${plural(st[0].n, 'pessoa instalou', 'pessoas instalaram')} a app entre ${dayPt(w[0])} e ${dayPt(w[1])}. Na primeira semana de uso:`
          : `${plural(st[0].n, 'pessoa instalou', 'pessoas instalaram')} a app nos últimos 7 dias. Até agora:` };
      const stk = B.stuck && closed ? B.stuck.buckets.filter((x) => x.id !== 'reached_match' && x.n).sort((a, b) => b.n - a.n)[0] : null;
      if (stk) journey.note = { never_phone: 'Quem desiste, desiste sobretudo antes de ligar o telemóvel', phone_not_protected: 'Quem desiste, desiste sobretudo depois de ligar o telemóvel e antes de ligar a proteção', protected_no_match: 'Quem desiste, desiste sobretudo depois de ligar a proteção e antes de jogar' }[stk.id] + ` (${num(stk.n)} de ${num(st[0].n)}).`;
    }
  }

  // ---------------- como chegam (contagens dos últimos 7 dias; o site não sabe quem instala, por isso não se ligam entre si)
  const arrive = [
    { label: 'Visitas ao site', n: sv.d7 ?? null },
    { label: 'Cliques em Download', n: K.site_download_clicks ? K.site_download_clicks.d7 ?? null : null },
    { label: 'Downloads da app', n: dl.d7 ?? null },
    { label: 'Instalações novas', n: K.new_installs.d7 ?? null },
  ];
  const rate = K.site_download_clicks && K.site_download_clicks.rate_d7;
  const refsSrc = B.acquisition && (B.acquisition.top_site_refs_d7 || B.acquisition.top_github_refs_14d) || [];
  const refs = [];
  for (const r of refsSrc) { const name = hostName(r.host); const o = refs.find((x) => x.name === name); if (o) o.n += r.n; else refs.push({ name, n: r.n }); }
  refs.sort((a, b) => b.n - a.n);
  const refTotal = sum(refs.map((x) => x.n));
  for (const r of refs) r.share = refTotal ? r.n / refTotal : 0;

  // ---------------- agora (o sinal ao vivo, de 10 em 10 min)
  const lv = K.live_now || {};
  const now = L ? L.now : lv.value ?? null;
  const live = { stale: staleMin != null, staleText: staleMin != null ? `sem dados há ${span(staleMin)}` : null,
    now, inGame: L ? L.inGame : lv.in_game ?? null, protectedN: L ? L.protectedN : lv.protected ?? null,
    peakToday: lv.peak_today != null || now != null ? Math.max(lv.peak_today || 0, now || 0) : null };

  // ---------------- versões e vídeos no gráfico (só os dos dias mostrados)
  const markers = (G && Array.isArray(G.releases) ? G.releases.map((r) => ({ date: r.date, kind: 'release', label: `Saiu a ${String(r.tag).replace(/^v?0\.1\.0-/, '')}` })) : [])
    .concat((G && Array.isArray(G.annotations) ? G.annotations : []).filter((a) => a.kind !== 'release').map((a) => ({ date: a.date, kind: a.kind, label: a.label })))
    .filter((m) => m.date && days.includes(m.date));

  const at = B.generated_at ? new Date(B.generated_at) : null;
  const updated = at ? new Intl.DateTimeFormat('pt-PT', { timeZone: 'Europe/Lisbon', hour: '2-digit', minute: '2-digit' }).format(at) : null;
  return { empty: !anyData, status, headline, tiles, changes, journey, arrive, rate, refs: refs.slice(0, 5), live, markers, updated };
}
