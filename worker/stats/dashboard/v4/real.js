// Dados reais do dashboard v4 (4 nov. 2026): /api/summary, /api/live, /api/github e /api/brief, atrás do mesmo Access que a
// página. Sem dados de exemplo. ?dev=1 mostra só os teus PCs. O brief (a saúde dos dados, os KPIs do Hoje e as regras) é o mesmo
// JSON que o agente lê: a página mostra o que ele diz, não faz contas à parte.
'use strict';
(function () {
  const DEV = new URLSearchParams(location.search).get('dev') === '1';
  const cache = new Map();
  let GH, GHat = 0, GHp = null, LV = null, LVat = 0, BR, BRat = 0, BRp = null, MD, MDat = 0;
  const get = async (u) => {
    const r = await fetch(u, { credentials: 'same-origin', cache: 'no-store' });
    if (!r.ok) throw new Error(`${u.split('?')[0]} respondeu ${r.status}`);
    return r.json();
  };
  const qs = (F, period) => new URLSearchParams({ period, version: F.version, country: F.country, mode: F.mode, link: F.link, ...(DEV ? { dev: 1 } : {}) }).toString();

  window.V4 = {
    DEV,
    /** O resumo de um período, guardado até forget() (o painel volta a ler a cada 5 min e ao clicar na faixa de saúde). */
    async summary(F, period) {
      const k = qs(F, period);
      if (!cache.has(k)) cache.set(k, get('/api/summary?' + k).catch((e) => { cache.delete(k); throw e; }));
      return cache.get(k);
    },
    /** Ao vivo: mudar de página usa o que foi lido há menos de 30 s; o relógio de 1 min pede sempre fresco (fresh). */
    async live(fresh) {
      if (!fresh && LV && Date.now() - LVat < 30_000) return LV;
      const r = await get('/api/live' + (DEV ? '?dev=1' : ''));
      LV = r; LVat = Date.now();
      return r;
    },
    /** GitHub: guardado 10 min no Worker; aqui 5 min. Se falhar, null e o resto da página continua. Um pedido de cada vez. */
    async github() {
      if (GH !== undefined && Date.now() - GHat < 300_000) return GH;
      if (!GHp) GHp = get('/api/github').catch(() => null).then((g) => { GH = g; GHat = Date.now(); GHp = null; return g; });
      return GHp;
    },
    githubNow() { return GH !== undefined && Date.now() - GHat < 300_000 ? GH : undefined; },
    /** O brief: guardado 5 min no Worker e aqui. null se falhar (a faixa de saúde diz "sem resposta"). */
    async brief() {
      if (BR !== undefined && Date.now() - BRat < 300_000) return BR;
      if (!BRp) BRp = get('/api/brief' + (DEV ? '?dev=1' : '')).catch(() => null).then((b) => { BR = b; BRat = Date.now(); BRp = null; return b; });
      return BRp;
    },
    /** v5: o mesmo brief em Markdown (a página Brief da página de dados), guardado 5 min como o JSON. */
    async briefMd() {
      if (MD !== undefined && Date.now() - MDat < 300_000) return MD;
      const r = await fetch('/api/brief?format=md' + (DEV ? '&dev=1' : ''), { credentials: 'same-origin', cache: 'no-store' });
      if (!r.ok) throw new Error(`/api/brief respondeu ${r.status}`);
      MD = await r.text(); MDat = Date.now();
      return MD;
    },
    briefNow() { return BR !== undefined && Date.now() - BRat < 300_000 ? BR : undefined; },
    forget() { cache.clear(); GHat = 0; LVat = 0; BRat = 0; MDat = 0; },
  };
})();
