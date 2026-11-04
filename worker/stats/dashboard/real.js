// Dados reais do dashboard v3 (4 nov. 2026): /api/summary, /api/live e /api/github, atrás do mesmo Access que a página.
// Sem dados de exemplo: o que a API não dá fica com um estado vazio honesto ("ainda não recolhido") e a fonte ao lado.
// ?dev=1 mostra só os teus PCs (builds de dev e os de EXCLUDE_IDS), como na v2.
'use strict';
(function () {
  const DEV = new URLSearchParams(location.search).get('dev') === '1';
  const cache = new Map();
  let GH, GHat = 0, GHp = null, LV = null, LVat = 0;
  const get = async (u) => {
    const r = await fetch(u, { credentials: 'same-origin', cache: 'no-store' });
    if (!r.ok) throw new Error(`${u.split('?')[0]} respondeu ${r.status}`);
    return r.json();
  };
  const qs = (F, period) => new URLSearchParams({ period, version: F.version, country: F.country, mode: F.mode, link: F.link, ...(DEV ? { dev: 1 } : {}) }).toString();

  window.V3 = {
    DEV,
    /** O resumo de um período, guardado até forget() (o painel volta a ler a cada 5 min e ao clicar na faixa do topo). */
    async summary(F, period) {
      const k = qs(F, period);
      if (!cache.has(k)) cache.set(k, get('/api/summary?' + k).catch((e) => { cache.delete(k); throw e; }));
      return cache.get(k);
    },
    /**
     * Ao vivo. Nov. 2026 (revisão de desempenho): mudar de página usa o que foi lido há menos de 30 s, em vez de esperar pela rede
     * a cada clique; o relógio de 1 min pede sempre fresco (fresh).
     */
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
    /** O GitHub já lido e fresco, ou undefined (ainda a ler): a página desenha-se sem esperar por ele. */
    githubNow() { return GH !== undefined && Date.now() - GHat < 300_000 ? GH : undefined; },
    forget() { cache.clear(); GHat = 0; LVat = 0; },
  };
})();
