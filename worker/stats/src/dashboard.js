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
const BANDS = Object.values(BAND);
// Counted once by the app on the day its usage stats notice is accepted (MainWindow): new users
const FIRST = 'first_start';

/** Order of app versions: 0.1.0-beta.10 after 0.1.0-beta.9, a release after its betas. */
export function compareVersions(a, b) {
  const parse = (v) => { const [main, pre] = String(v).split('+')[0].split(/-(.*)/); return { main: main.split('.').map(Number), pre: pre ? pre.split('.') : null }; };
  const x = parse(a), y = parse(b);
  for (let i = 0; i < 3; i++) if ((x.main[i] || 0) !== (y.main[i] || 0)) return (x.main[i] || 0) - (y.main[i] || 0);
  if (!x.pre || !y.pre) return (x.pre ? -1 : 0) - (y.pre ? -1 : 0);
  for (let i = 0; i < Math.max(x.pre.length, y.pre.length); i++) {
    const p = x.pre[i], q = y.pre[i];
    if (p === undefined || q === undefined) return p === undefined ? -1 : 1;
    const np = /^\d+$/.test(p), nq = /^\d+$/.test(q);
    if (np && nq && Number(p) !== Number(q)) return Number(p) - Number(q);
    if (np !== nq) return np ? -1 : 1;
    if (!np && p !== q) return p < q ? -1 : 1;
  }
  return 0;
}

/** "ChangeIp_Click" → "Change IP", "Tray_open" → "Tray: open", "ip_changed" → "IP changed": names from the app's code, readable. */
export function readable(name) {
  const s = String(name).replace(/_Click$/, '').replace(/^Tray_/, 'Tray: ').replace(/^Hotkey_/, 'Hotkey: ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/_/g, ' ').trim();
  const w = s.replace(/\b(ip|Ip)\b/g, 'IP').replace(/\bcs2\b/gi, 'CS2').replace(/\bvpn\b/gi, 'VPN').replace(/\bapi\b/gi, 'API');
  return w.charAt(0).toUpperCase() + w.slice(1);
}

const MODES = { game: 'Game only', cs2open: 'Whole PC while CS2 is open', always: 'Whole PC, always', nophone: 'No phone' };
const LINKS = { usb: 'USB cable', hotspot: 'Wi-Fi hotspot', other: 'Other adapter', none: 'Phone not connected' };
const LISTS = { servers: 'Servers', accounts: 'Accounts', flags: 'Flagged players', open_with: 'Open with CS2', regions_blocked: 'Regions off' };

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
  const [totals, perDay, fresh, versions, countries, modes, links, clicks, counts, sizes, errors] = await Promise.all([
    all(`SELECT COUNT(*) AS n, COALESCE(SUM(m), 0) AS minutes FROM (${perInstallDay})`),
    all(`SELECT day, COUNT(*) AS n, COALESCE(SUM(m), 0) AS minutes FROM (${perInstallDay}) GROUP BY day ORDER BY day`),
    // New users: the app counts first_start once, on the day its notice is accepted (one per install-day at most)
    all(`SELECT day, COUNT(DISTINCT id) AS n FROM counts WHERE ${where} AND name = '${FIRST}' AND n > 0 GROUP BY day`),
    all(`SELECT app AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY app ORDER BY n DESC, name`),
    all(`SELECT COALESCE(country, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT COALESCE(mode, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT COALESCE(link, '?') AS name, ${installDays} AS n FROM pings WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, SUM(n) AS n FROM clicks WHERE ${where} GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, SUM(n) AS n FROM counts WHERE ${where} AND name NOT IN (${[...SIZES, FIRST].map((s) => `'${s}'`).join(',')}) GROUP BY name ORDER BY n DESC, name`),
    all(`SELECT name, n AS band, ${installDays} AS days FROM counts WHERE ${where} AND name IN (${SIZES.map((s) => `'${s}'`).join(',')}) GROUP BY name, n ORDER BY name, n`),
    all(`SELECT type, at, app, SUM(n) AS n, ${installDays} AS days FROM errors WHERE ${where} GROUP BY type, at, app ORDER BY n DESC LIMIT 40`),
  ]);
  const t = totals[0] ?? { n: 0, minutes: 0 };
  // Every day of the range, also the ones nobody opened the app
  const byDay = new Map(perDay.map((r) => [r.day, r]));
  const freshBy = new Map(fresh.map((r) => [r.day, r.n]));
  const series = [];
  for (let i = days - 1; i >= 0; i--) {
    const d = new Date(now - i * 864e5).toISOString().slice(0, 10);
    series.push({ day: d, n: byDay.get(d)?.n ?? 0, minutes: byDay.get(d)?.minutes ?? 0, fresh: freshBy.get(d) ?? 0 });
  }
  const newest = versions.map((r) => r.name).sort(compareVersions).at(-1) ?? null;
  return {
    since,
    installDays: t.n,
    minutes: t.minutes,
    avgMinutes: t.n ? Math.round(t.minutes / t.n) : 0,
    newUsers: series.reduce((a, d) => a + d.fresh, 0),
    newest,
    newestDays: versions.find((r) => r.name === newest)?.n ?? 0,
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

function bars(rows, { max = 12, label = (r) => r.name, value = (r) => r.n, note = () => '', title = label } = {}) {
  if (!rows?.length) return '<p class="none">Nothing yet.</p>';
  const top = Math.max(1, ...rows.map(value));
  return `<ul class="bars">${rows.slice(0, max).map((r) => `<li><span class="k" title="${esc(title(r))}">${esc(label(r))}</span>`
    + `<span class="bar"><i style="width:${Math.max(2, Math.round((100 * value(r)) / top))}%"></i></span><span class="v">${fmt(value(r))}${note(r)}</span></li>`).join('')}</ul>`;
}

/** Columns per day. part: a second number drawn at the bottom of each column in a lighter colour (new users). */
function columns(series, key, { part = null, tall = false } = {}) {
  const top = Math.max(1, ...series.map((d) => d[key]));
  const w = 100 / series.length;
  const rects = series.map((d, i) => {
    const h = Math.round((d[key] / top) * 100);
    const x = (i * w + w * 0.15).toFixed(2), cw = (w * 0.7).toFixed(2);
    const p = part ? Math.round((Math.min(d[part], d[key]) / top) * 100) : 0;
    const tip = `${d.day}: ${fmt(d[key])}${part ? `, ${fmt(d[part])} new` : ''}`;
    return `<rect x="${x}" y="${100 - h}" width="${cw}" height="${Math.max(h, d[key] ? 1 : 0)}"><title>${esc(tip)}</title></rect>`
      + (p ? `<rect class="part" x="${x}" y="${100 - p}" width="${cw}" height="${p}"><title>${esc(tip)}</title></rect>` : '');
  }).join('');
  return `<svg class="cols${tall ? ' tall' : ''}" viewBox="0 0 100 100" preserveAspectRatio="none" role="img" aria-label="per day">${rects}</svg>`
    + `<div class="axis"><span>${esc(series[0]?.day.slice(5))}</span><span>highest ${fmt(top)}</span><span>${esc(series.at(-1)?.day.slice(5))}</span></div>`;
}

/** List sizes as one table: a row per list, a column per band, how many users had that many. */
function sizesTable(sizes) {
  const used = BANDS.filter((b) => sizes.some((z) => z.bands.some((r) => r.band === b)));
  if (!used.length) return '<p class="none">Nothing yet.</p>';
  return `<table class="grid"><tr><th></th>${used.map((b) => `<th>${esc(b)}</th>`).join('')}</tr>${sizes.map((z) => `<tr><td>${esc(LISTS[z.name] ?? z.name)}</td>`
    + used.map((b) => { const v = z.bands.find((r) => r.band === b)?.days; return `<td class="num">${v ? fmt(v) : '<span class="dim">·</span>'}</td>`; }).join('') + '</tr>').join('')}</table>`;
}

const one = (n) => (n >= 10 ? fmt(Math.round(n)) : (Math.round(n * 10) / 10).toLocaleString('en-US'));
const tile = (label, value, sub = '', big = false) => `<div class="tile${big ? ' big' : ''}"><div class="tl">${esc(label)}</div><div class="tv">${value}</div>${sub ? `<div class="ts">${sub}</div>` : ''}</div>`;
const card = (title, body, cls = '') => `<section class="card${cls ? ' ' + cls : ''}"><h2>${esc(title)}</h2>${body}</section>`;

export function page({ days, stats, gh, now, email }) {
  const s = stats || {};
  const tabs = DAYS.map((d) => (d === days ? `<b>${d} days</b>` : `<a href="/dashboard?days=${d}">${d} days</a>`)).join('');
  // The ones not already in the top 12
  const least = s.clicks?.length > 12 ? s.clicks.slice(12).reverse().slice(0, 6) : [];
  const today = s.perDay?.at(-1), yesterday = s.perDay?.at(-2);
  const btn = { label: (r) => readable(r.name), title: (r) => r.name };
  const body = s.error
    ? card('Database', `<p class="bad">Could not read the stats: ${esc(s.error)}</p>`, 'wide')
    : [
      `<div class="tiles">`
        + tile('Users per day', one(s.installDays / days), `today ${fmt(today?.n)} · yesterday ${fmt(yesterday?.n)}`, true)
        + tile('New users', fmt(s.newUsers), `today ${fmt(today?.fresh)} · in ${days} days`, true)
        + tile('Minutes per user', fmt(s.avgMinutes), `a day · ${fmt(Math.round(s.minutes / 60))} hours in all`, true)
        + tile(s.newest ? `On ${s.newest.replace(/^0\.1\.0-/, '')}` : 'Newest version', s.installDays ? `${Math.round((100 * s.newestDays) / s.installDays)}%` : '-',
          s.newest ? `${fmt(s.newestDays)} of ${fmt(s.installDays)} users` : '', true)
        + `</div>`,
      card('Users per day', columns(s.perDay, 'n', { part: 'fresh', tall: true }) + '<p class="legend"><i></i>users <i class="part"></i>new users</p>', 'span2'),
      card('Versions', bars(s.versions, { max: 5 }) + '<h3>Countries</h3>' + bars(s.countries, { max: 6 })),
      card('Minutes open per day', columns(s.perDay, 'minutes'), 'span2'),
      card('Modes', bars(s.modes, { label: (r) => MODES[r.name] ?? r.name, title: (r) => r.name })
        + '<h3>Phone connected by</h3>' + bars(s.links, { label: (r) => LINKS[r.name] ?? r.name, title: (r) => r.name })),
      card('Most used buttons', bars(s.clicks, { max: 12, ...btn })),
      card('Counters', bars(s.counts, { max: 12, label: (r) => readable(r.name), title: (r) => r.name })
        + (least.length ? '<h3>Least used buttons</h3>' + bars(least, { max: 6, ...btn }) : '')),
      card('List sizes (users per size)', sizesTable(s.sizes)),
      card('Errors', s.errors?.length ? `<div class="scroll"><table><tr><th>Error</th><th>Where</th><th>Version</th><th>Times</th><th>Users</th></tr>${s.errors.map((e) => `<tr><td>${esc(e.type)}</td><td class="mono">${esc(e.at)}</td><td>${esc(e.app)}</td><td class="num">${fmt(e.n)}</td><td class="num">${fmt(e.days)}</td></tr>`).join('')}</table></div>` : '<p class="none">No errors.</p>', 'wide'),
    ].join('');
  const downloads = gh.releases?.filter((r) => !r.draft);
  const ghBody = [
    `<h2 class="section">GitHub</h2>`,
    `<div class="tiles">${tile('Stars', fmt(gh.stars), gh.stars != null ? `${fmt(gh.forks)} forks · ${fmt(gh.watchers)} watching` : '')}`
      + `${tile('Visits, 14 days', fmt(gh.views?.count), gh.views ? `${fmt(gh.views.uniques)} people` : '')}`
      + `${tile('Clones, 14 days', fmt(gh.clones?.count), gh.clones ? `${fmt(gh.clones.uniques)} people` : '')}`
      + `${tile('Downloads', fmt(downloads?.reduce((a, r) => a + r.exe, 0)), 'the exe, every release')}</div>`,
    gh.views ? card('Visits per day', columns(gh.views.days.map((d) => ({ day: d.day, n: d.n })), 'n')) : '',
    gh.clones ? card('Clones per day', columns(gh.clones.days.map((d) => ({ day: d.day, n: d.n })), 'n')) : '',
    gh.referrers ? card('Where visitors came from', bars(gh.referrers, { note: (r) => ` <small>(${fmt(r.u)})</small>` })) : '',
    gh.paths ? card('Most seen pages', bars(gh.paths, { label: (r) => r.name.replace(`/${gh.repo}`, '') || '/', note: (r) => ` <small>(${fmt(r.u)})</small>` })) : '',
    downloads ? card('Downloads per release', bars(downloads, { max: 30, label: (r) => `${r.tag} · ${r.date}`, value: (r) => r.downloads }), 'span2') : '',
    gh.problems?.length ? card('GitHub problems', `<ul class="plain">${gh.problems.map((p) => `<li class="bad">${esc(p)}</li>`).join('')}</ul>`, 'wide') : '',
  ].join('');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex,nofollow"><meta name="referrer" content="no-referrer"><title>CS2 IP Changer · stats</title><style>
:root{--bg:#0e0e0e;--bg2:#171717;--bg3:#1d1d1d;--line:#2a2a2a;--ink:#e4e4e4;--ink2:#adadad;--ink3:#888;--accent:#5ba8f0;--part:#a9d2f8;--blue:#2b74c2;--bad:#f07a6a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.45 "Segoe UI",system-ui,-apple-system,Roboto,sans-serif}
header{display:flex;flex-wrap:wrap;gap:12px;align-items:baseline;justify-content:space-between;padding:20px 20px 8px;max-width:1200px;margin:auto}
h1{font-size:20px;margin:0;font-weight:600}h1 span{color:var(--accent)}nav{display:flex;gap:6px}nav a,nav b{padding:5px 10px;border:1px solid var(--line);border-radius:8px;color:var(--ink2);text-decoration:none;font-weight:500}
nav b{background:var(--blue);border-color:var(--blue);color:#fff}main{max-width:1200px;margin:auto;padding:8px 20px 40px;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}
.tiles{grid-column:1/-1;display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}
.tile,.card{min-width:0;background:var(--bg2);border:1px solid var(--line);border-radius:12px;padding:14px 16px}.tl{color:var(--ink3);font-size:13px}.tv{font-size:28px;font-weight:600;margin-top:2px}.ts{color:var(--ink2);font-size:13px}
.tile.big .tv{font-size:36px}.wide{grid-column:1/-1}.span2{grid-column:span 2}
h2{font-size:14px;color:var(--ink2);font-weight:600;margin:0 0 10px;text-transform:uppercase;letter-spacing:.04em}h2.section{grid-column:1/-1;margin:14px 0 -4px}h3{font-size:13px;color:var(--ink3);margin:14px 0 4px;font-weight:500}
.bars{list-style:none;margin:0;padding:0}.bars li{display:grid;grid-template-columns:minmax(0,46%) 1fr auto;gap:8px;align-items:center;padding:3px 0}
.k{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--ink2);font-size:13px}.bar{background:var(--bg3);border-radius:4px;height:10px;overflow:hidden}.bar i{display:block;height:100%;background:var(--accent);border-radius:4px}
.v{font-variant-numeric:tabular-nums;font-size:13px;min-width:3ch;text-align:right}small{color:var(--ink3)}
.cols{width:100%;height:140px;display:block;background:var(--bg3);border-radius:8px}.cols.tall{height:220px}.cols rect{fill:var(--accent)}.cols rect.part{fill:var(--part)}
.axis{display:flex;justify-content:space-between;color:var(--ink3);font-size:12px;margin-top:4px}
.legend{margin:6px 0 0;color:var(--ink3);font-size:12px}.legend i{display:inline-block;width:10px;height:10px;border-radius:2px;background:var(--accent);margin:0 5px -1px 0}.legend i.part{background:var(--part);margin-left:12px}
.scroll{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);vertical-align:top}th{color:var(--ink3);font-weight:500}
.grid td:first-child{white-space:nowrap}.grid th,.grid td{padding:6px 4px}.grid th:not(:first-child),.num{text-align:right;font-variant-numeric:tabular-nums}.dim{color:var(--line)}
.mono{font-family:Consolas,ui-monospace,monospace;word-break:break-all}.none{color:var(--ink3);font-size:13px;margin:6px 0 0}.bad{color:var(--bad)}.plain{margin:0;padding-left:18px}
footer{max-width:1200px;margin:auto;padding:0 20px 30px;color:var(--ink3);font-size:12px}
@media(max-width:900px){main{grid-template-columns:repeat(2,minmax(0,1fr))}.tiles{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:600px){header,main,footer{padding-left:12px;padding-right:12px}main{grid-template-columns:minmax(0,1fr)}.span2{grid-column:auto}.tv,.tile.big .tv{font-size:24px}}
</style></head><body><header><h1>CS2 IP Changer <span>stats</span></h1><nav>${tabs}</nav></header>
<main>${body}${ghBody}</main><footer>Dev builds left out. A user = one install open on one day: the app's ID is new every day, so nobody can be followed from one day to the next, and someone who opens the app on 3 days counts 3 times. New users = installs that accepted the usage stats notice that day (also once for someone coming from beta.7). Days in UTC. GitHub numbers kept 10 minutes. ${esc(new Date(now).toISOString().slice(0, 16).replace('T', ' '))} UTC · ${esc(email)}</footer></body></html>`;
}
