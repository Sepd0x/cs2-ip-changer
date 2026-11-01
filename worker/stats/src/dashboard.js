// Stats page for the person who runs the project: a second Worker (wrangler.dashboard.jsonc) on the same database, only reads.
// It is its own Worker because Cloudflare Access protects a whole workers.dev address: on the stats Worker it would also stop
// the app's reports to /v1. Access sits in front of all of it, and every request checks the Access token too (access.js).
// GitHub numbers come from the API with a read-only token (secret GITHUB_TOKEN). No scripts and no outside files on the page.

import { verifyAccess } from './access.js';

const REPO = /^[A-Za-z0-9_.-]{1,100}\/[A-Za-z0-9_.-]{1,100}$/;
const DAYS = [7, 30, 90];
// Sent as list sizes in bands (Telemetry.Band), not as counters: shown as how many install-days were in each band
const SIZES = ['servers', 'accounts', 'flags', 'open_with', 'regions_blocked'];
const BAND = { 0: '0', 1: '1', 2: '2-5', 6: '6-20', 21: '21-100', 101: '100+' };

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const gate = await verifyAccess(request, env);
    if (!gate.ok) return text('Forbidden', 403);
    if (request.method !== 'GET' && request.method !== 'HEAD') return text('Method not allowed', 405);
    if (url.pathname === '/') return new Response(null, { status: 302, headers: { location: '/dashboard' } });
    if (url.pathname !== '/dashboard') return text('Not found', 404);
    const days = DAYS.includes(Number(url.searchParams.get('days'))) ? Number(url.searchParams.get('days')) : 30;
    const now = Date.now();
    const [stats, gh] = await Promise.all([
      collect(env.DB, { days, now, exclude: excludeIds(env.EXCLUDE_IDS) }).catch((e) => ({ error: String(e?.message || e) })),
      github(env, fetch, now),
    ]);
    return new Response(request.method === 'HEAD' ? null : page({ days, stats, gh, now, email: gate.email }), {
      headers: {
        'content-type': 'text/html; charset=utf-8',
        'cache-control': 'no-store',
        'content-security-policy': "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'self'; frame-ancestors 'none'",
        'x-content-type-options': 'nosniff',
        'referrer-policy': 'no-referrer',
        'x-frame-options': 'DENY',
      },
    });
  },
};

const text = (body, status) => new Response(body, { status, headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store' } });

/** Old fixed install IDs of the developer's PCs (before beta.8), as a secret: comma or space separated. */
export function excludeIds(s) {
  return String(s || '').split(/[\s,]+/).filter((x) => /^[0-9a-f]{32}$/.test(x)).slice(0, 50);
}

/** Every number on the page from the database, leaving out dev builds. An ID on one day = one install-day (the ID is daily). */
export async function collect(db, { days, now, exclude = [] }) {
  const since = new Date(now - (days - 1) * 864e5).toISOString().slice(0, 10);
  const not = exclude.length ? ` AND id NOT IN (${exclude.map(() => '?').join(',')})` : '';
  const where = `dev = 0 AND day >= ?${not}`;
  const args = [since, ...exclude];
  const all = async (sql) => (await db.prepare(sql).bind(...args).all()).results ?? [];
  const installDays = "COUNT(DISTINCT day || '|' || id)";
  // One install can't be open more than a day in a day: made-up reports can't push the minutes past that
  const perInstallDay = `SELECT day, id, MIN(COALESCE(SUM(minutes), 0), 1440) AS m FROM pings WHERE ${where} GROUP BY day, id`;
  const [totals, perDay, versions, countries, modes, links, clicks, counts, sizes, errors] = await Promise.all([
    all(`SELECT COUNT(*) AS n, COALESCE(SUM(m), 0) AS minutes FROM (${perInstallDay})`),
    all(`SELECT day, COUNT(*) AS n, COALESCE(SUM(m), 0) AS minutes FROM (${perInstallDay}) GROUP BY day ORDER BY day`),
    all(`SELECT app AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY app ORDER BY n DESC, name`),
    all(`SELECT COALESCE(country, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT COALESCE(mode, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT COALESCE(link, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, SUM(n) AS n FROM clicks WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, SUM(n) AS n FROM counts WHERE ${where} AND name NOT IN (${SIZES.map((s) => `'${s}'`).join(',')}) GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, n AS band, ${installDays} AS days FROM counts WHERE ${where} AND name IN (${SIZES.map((s) => `'${s}'`).join(',')}) GROUP BY name, n ORDER BY name, n`),
    all(`SELECT type, at, app, SUM(n) AS n, ${installDays} AS days FROM errors WHERE ${where} GROUP BY type, at, app ORDER BY n DESC LIMIT 40`),
  ]);
  const t = totals[0] ?? { n: 0, minutes: 0 };
  // Every day of the range, also the ones nobody opened the app
  const byDay = new Map(perDay.map((r) => [r.day, r]));
  const series = [];
  for (let i = days - 1; i >= 0; i--) {
    const d = new Date(now - i * 864e5).toISOString().slice(0, 10);
    series.push({ day: d, n: byDay.get(d)?.n ?? 0, minutes: byDay.get(d)?.minutes ?? 0 });
  }
  return {
    since,
    installDays: t.n,
    minutes: t.minutes,
    avgMinutes: t.n ? Math.round(t.minutes / t.n) : 0,
    perDay: series,
    versions, countries, modes, links, clicks, counts,
    sizes: SIZES.map((name) => ({ name, bands: sizes.filter((r) => r.name === name).map((r) => ({ band: BAND[r.band] ?? String(r.band), days: r.days })) })),
    errors,
  };
}

let ghCache = { at: 0, data: null };
/** For the tests. */
export function resetGithubCache() { ghCache = { at: 0, data: null }; }

/** Stars, 14-day views and clones, referrers, downloads per release. Kept 10 minutes. A missing token or permission is said on the page. */
export async function github(env, fetchFn, now) {
  if (ghCache.data && now - ghCache.at < 600_000) return ghCache.data;
  const repo = REPO.test(env.GITHUB_REPO || '') ? env.GITHUB_REPO : 'Sepd0x/cs2-ip-changer';
  if (!env.GITHUB_TOKEN) return { repo, problems: ['No GITHUB_TOKEN secret: GitHub numbers are off.'] };
  const problems = [];
  const get = async (path) => {
    try {
      const res = await fetchFn(`https://api.github.com/repos/${repo}${path}`, {
        headers: { authorization: `Bearer ${env.GITHUB_TOKEN}`, accept: 'application/vnd.github+json', 'user-agent': 'cs2ipc-stats-dashboard', 'x-github-api-version': '2022-11-28' },
      });
      if (!res.ok) { problems.push(`${path || '/'}: GitHub said ${res.status}`); return null; }
      return await res.json();
    } catch { problems.push(`${path || '/'}: no answer from GitHub`); return null; }
  };
  const [info, views, clones, referrers, paths, releases] = await Promise.all([
    get(''), get('/traffic/views'), get('/traffic/clones'), get('/traffic/popular/referrers'), get('/traffic/popular/paths'), get('/releases?per_page=30'),
  ]);
  const num = (x) => (Number.isFinite(x) ? x : 0);
  const data = {
    repo,
    problems,
    stars: info ? num(info.stargazers_count) : null,
    forks: info ? num(info.forks_count) : null,
    watchers: info ? num(info.subscribers_count) : null,
    issues: info ? num(info.open_issues_count) : null,
    views: views ? { count: num(views.count), uniques: num(views.uniques), days: (views.views ?? []).map((v) => ({ day: String(v.timestamp).slice(0, 10), n: num(v.count), u: num(v.uniques) })) } : null,
    clones: clones ? { count: num(clones.count), uniques: num(clones.uniques), days: (clones.clones ?? []).map((v) => ({ day: String(v.timestamp).slice(0, 10), n: num(v.count), u: num(v.uniques) })) } : null,
    referrers: Array.isArray(referrers) ? referrers.map((r) => ({ name: String(r.referrer), n: num(r.count), u: num(r.uniques) })) : null,
    paths: Array.isArray(paths) ? paths.slice(0, 10).map((p) => ({ name: String(p.path), n: num(p.count), u: num(p.uniques) })) : null,
    releases: Array.isArray(releases) ? releases.map((r) => ({
      tag: String(r.tag_name), date: String(r.published_at || r.created_at || '').slice(0, 10), draft: !!r.draft,
      downloads: (r.assets ?? []).reduce((s, a) => s + num(a.download_count), 0),
      exe: (r.assets ?? []).filter((a) => /\.exe$/i.test(a.name)).reduce((s, a) => s + num(a.download_count), 0),
    })) : null,
  };
  ghCache = { at: now, data };
  return data;
}

// ------------------------------------------------------------------ page

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const fmt = (n) => (n == null ? '-' : Number(n).toLocaleString('en-US'));

function bars(rows, { max = 12, label = (r) => r.name, value = (r) => r.n, note = () => '' } = {}) {
  if (!rows?.length) return '<p class="none">Nothing yet.</p>';
  const top = Math.max(1, ...rows.map(value));
  return `<ul class="bars">${rows.slice(0, max).map((r) => `<li><span class="k" title="${esc(label(r))}">${esc(label(r))}</span>`
    + `<span class="bar"><i style="width:${Math.max(2, Math.round((100 * value(r)) / top))}%"></i></span><span class="v">${fmt(value(r))}${note(r)}</span></li>`).join('')}</ul>`;
}

function columns(series, key) {
  const top = Math.max(1, ...series.map((d) => d[key]));
  const w = 100 / series.length;
  const rects = series.map((d, i) => {
    const h = Math.round((d[key] / top) * 100);
    return `<rect x="${(i * w + w * 0.15).toFixed(2)}" y="${100 - h}" width="${(w * 0.7).toFixed(2)}" height="${Math.max(h, d[key] ? 1 : 0)}"><title>${esc(d.day)}: ${fmt(d[key])}</title></rect>`;
  }).join('');
  return `<svg class="cols" viewBox="0 0 100 100" preserveAspectRatio="none" role="img" aria-label="per day">${rects}</svg>`
    + `<div class="axis"><span>${esc(series[0]?.day.slice(5))}</span><span>top ${fmt(top)}</span><span>${esc(series.at(-1)?.day.slice(5))}</span></div>`;
}

const tile = (label, value, sub = '') => `<div class="tile"><div class="tl">${esc(label)}</div><div class="tv">${value}</div>${sub ? `<div class="ts">${sub}</div>` : ''}</div>`;
const card = (title, body, wide = false) => `<section class="card${wide ? ' wide' : ''}"><h2>${esc(title)}</h2>${body}</section>`;

export function page({ days, stats, gh, now, email }) {
  const s = stats || {};
  const tabs = DAYS.map((d) => (d === days ? `<b>${d} days</b>` : `<a href="/dashboard?days=${d}">${d} days</a>`)).join('');
  // The ones not already in the top 15
  const least = s.clicks?.length > 15 ? s.clicks.slice(15).reverse().slice(0, 10) : [];
  const body = s.error
    ? card('Database', `<p class="bad">Could not read the stats: ${esc(s.error)}</p>`, true)
    : [
      `<div class="tiles">${tile('Install-days', fmt(s.installDays), `since ${esc(s.since)}`)}${tile('Minutes open', fmt(s.minutes), `${fmt(s.avgMinutes)} per install-day`)}`
        + `${tile('Stars', fmt(gh.stars))}${tile('Views, 14 days', fmt(gh.views?.count), gh.views ? `${fmt(gh.views.uniques)} people` : '')}`
        + `${tile('Clones, 14 days', fmt(gh.clones?.count), gh.clones ? `${fmt(gh.clones.uniques)} people` : '')}`
        + `${tile('Downloads', fmt(gh.releases?.reduce((a, r) => a + r.exe, 0)), 'exe, all releases')}</div>`,
      card('Install-days per day', columns(s.perDay, 'n'), true),
      card('Minutes open per day', columns(s.perDay, 'minutes'), true),
      card('Versions', bars(s.versions)),
      card('Countries', bars(s.countries, { max: 20 })),
      card('Modes', bars(s.modes)),
      card('Phone link', bars(s.links)),
      card('Most used buttons', bars(s.clicks, { max: 15 })),
      card('Least used buttons', least.length ? bars(least, { max: 10 }) + '<p class="none">Buttons nobody pressed are not here: the app only sends what was used.</p>' : '<p class="none">Not enough buttons yet.</p>'),
      card('Counters', bars(s.counts, { max: 25 })),
      card('List sizes (install-days per band)', s.sizes.map((z) => `<h3>${esc(z.name)}</h3>${bars(z.bands, { label: (r) => r.band, value: (r) => r.days })}`).join('')),
      card('Errors', s.errors?.length ? `<div class="scroll"><table><tr><th>Error</th><th>Where</th><th>Version</th><th>Times</th><th>Install-days</th></tr>${s.errors.map((e) => `<tr><td>${esc(e.type)}</td><td class="mono">${esc(e.at)}</td><td>${esc(e.app)}</td><td>${fmt(e.n)}</td><td>${fmt(e.days)}</td></tr>`).join('')}</table></div>` : '<p class="none">No errors.</p>', true),
    ].join('');
  const ghBody = [
    gh.views ? card('GitHub views per day (14 days)', columns(gh.views.days.map((d) => ({ day: d.day, n: d.n })), 'n')) : '',
    gh.clones ? card('GitHub clones per day (14 days)', columns(gh.clones.days.map((d) => ({ day: d.day, n: d.n })), 'n')) : '',
    gh.referrers ? card('Where visitors came from (14 days)', bars(gh.referrers, { note: (r) => ` <small>(${fmt(r.u)})</small>` })) : '',
    gh.paths ? card('Most seen pages (14 days)', bars(gh.paths, { label: (r) => r.name.replace(`/${gh.repo}`, '') || '/', note: (r) => ` <small>(${fmt(r.u)})</small>` })) : '',
    gh.releases ? card('Downloads per release', bars(gh.releases.filter((r) => !r.draft), { max: 30, label: (r) => `${r.tag} · ${r.date}`, value: (r) => r.downloads })) : '',
    gh.stars != null ? card('Repository', `<p>${fmt(gh.stars)} stars · ${fmt(gh.forks)} forks · ${fmt(gh.watchers)} watching · ${fmt(gh.issues)} open issues and PRs</p>`) : '',
    gh.problems?.length ? card('GitHub problems', `<ul class="plain">${gh.problems.map((p) => `<li class="bad">${esc(p)}</li>`).join('')}</ul>`, true) : '',
  ].join('');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex,nofollow"><meta name="referrer" content="no-referrer"><title>CS2 IP Changer · stats</title><style>
:root{--bg:#0e0e0e;--bg2:#171717;--bg3:#1d1d1d;--line:#2a2a2a;--ink:#e4e4e4;--ink2:#adadad;--ink3:#888;--accent:#5ba8f0;--blue:#2b74c2;--bad:#f07a6a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.45 "Segoe UI",system-ui,-apple-system,Roboto,sans-serif}
header{display:flex;flex-wrap:wrap;gap:12px;align-items:baseline;justify-content:space-between;padding:20px 20px 8px;max-width:1200px;margin:auto}
h1{font-size:20px;margin:0;font-weight:600}h1 span{color:var(--accent)}nav{display:flex;gap:6px}nav a,nav b{padding:5px 10px;border:1px solid var(--line);border-radius:8px;color:var(--ink2);text-decoration:none;font-weight:500}
nav b{background:var(--blue);border-color:var(--blue);color:#fff}main{max-width:1200px;margin:auto;padding:8px 20px 40px;display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:14px}
.tiles{grid-column:1/-1;display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:14px}
.tile,.card{min-width:0;background:var(--bg2);border:1px solid var(--line);border-radius:12px;padding:14px 16px}.tl{color:var(--ink3);font-size:13px}.tv{font-size:28px;font-weight:600;margin-top:2px}.ts{color:var(--ink2);font-size:13px}
.wide{grid-column:1/-1}h2{font-size:14px;color:var(--ink2);font-weight:600;margin:0 0 10px;text-transform:uppercase;letter-spacing:.04em}h3{font-size:13px;color:var(--ink3);margin:10px 0 4px;font-weight:500}
.bars{list-style:none;margin:0;padding:0}.bars li{display:grid;grid-template-columns:minmax(0,42%) 1fr auto;gap:8px;align-items:center;padding:3px 0}
.k{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--ink2);font-size:13px}.bar{background:var(--bg3);border-radius:4px;height:10px;overflow:hidden}.bar i{display:block;height:100%;background:var(--accent);border-radius:4px}
.v{font-variant-numeric:tabular-nums;font-size:13px;min-width:3ch;text-align:right}small{color:var(--ink3)}
.cols{width:100%;height:140px;display:block;background:var(--bg3);border-radius:8px}.cols rect{fill:var(--accent)}.axis{display:flex;justify-content:space-between;color:var(--ink3);font-size:12px;margin-top:4px}
.scroll{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);vertical-align:top}th{color:var(--ink3);font-weight:500}
.mono{font-family:Consolas,ui-monospace,monospace;word-break:break-all}.none{color:var(--ink3);font-size:13px;margin:6px 0 0}.bad{color:var(--bad)}.plain{margin:0;padding-left:18px}
footer{max-width:1200px;margin:auto;padding:0 20px 30px;color:var(--ink3);font-size:12px}
@media(max-width:520px){header,main,footer{padding-left:12px;padding-right:12px}main{grid-template-columns:minmax(0,1fr)}.tiles{grid-template-columns:repeat(2,minmax(0,1fr))}.tv{font-size:22px}}
</style></head><body><header><h1>CS2 IP Changer <span>stats</span></h1><nav>${tabs}</nav></header>
<main>${body}${ghBody}</main><footer>Dev builds left out. One install-day = one install open on one day (the app's ID is new every day, so nobody can be followed from one day to the next). GitHub numbers kept 10 minutes. ${esc(new Date(now).toISOString().slice(0, 16).replace('T', ' '))} UTC · ${esc(email)}</footer></body></html>`;
}
