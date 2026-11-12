// Rows D1 reads per dashboard call, measured on a local D1 (Miniflare: the same SQLite and the same rows_read count as
// Cloudflare) loaded from a backup of the production database (npx wrangler d1 export cs2ipc-stats --remote --output ...).
// Nothing leaves the PC. From worker/stats:
//   node tools/rows-read.mjs <backup.sql> [--indexes] [--now=2026-11-12T22:00:00Z]
// --indexes applies migrations/*.sql first (production has them since 12 Nov. 2026, 17:22 UTC).
// Miniflare comes from the global wrangler (npm i -g wrangler) or MINIFLARE=<path to miniflare's dist/src/index.js>.
// test/rows-read.test.mjs uses the same functions to fail when a load reads too much again.
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const HERE = fileURLToPath(new URL('..', import.meta.url));
export const ALL = { version: 'all', country: 'all', mode: 'all', link: 'all' };
// The backup of 12 Nov. 2026 (before the indexes), in publish/ of the main checkout (outside git: it has install IDs)
export const BACKUP_NAME = 'backup-d1-2026-11-12-antes-indices.sql';

/** The backup to measure with: D1_BACKUP, else publish/<BACKUP_NAME> of this checkout or of the main one (from a worktree). */
export function findBackup() {
  if (process.env.D1_BACKUP) return existsSync(process.env.D1_BACKUP) ? process.env.D1_BACKUP : null;
  const root = join(HERE, '..', '..');
  // A git worktree has no publish/ of its own: the main checkout is the folder of the shared .git
  let main = root;
  try { main = join(execSync('git rev-parse --path-format=absolute --git-common-dir', { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim(), '..'); } catch { /* not a git checkout */ }
  return [join(root, 'publish', BACKUP_NAME), join(main, 'publish', BACKUP_NAME)].find((p) => existsSync(p)) || null;
}

/** Miniflare, or null when there is none on this PC. */
export async function loadMiniflare() {
  try {
    if (process.env.MINIFLARE) return await import(pathToFileURL(process.env.MINIFLARE).href);
    const root = execSync('npm root -g', { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
    return await import(pathToFileURL(createRequire(join(root, 'wrangler', 'package.json')).resolve('miniflare')).href);
  } catch { return null; }
}

/** A SQL file's statements, one per line ending in ";" (as wrangler d1 export writes them), comments and PRAGMAs left out. */
function statements(sql) {
  return sql.replace(/^--.*$/gm, '').split(/;\r?\n/).map((s) => s.trim().replace(/;$/, '')).filter((s) => s && !/^PRAGMA/i.test(s));
}

/** A local D1 with the backup in it (and the migrations with indexes): { db, dispose }. */
export async function localD1(Miniflare, file, { indexes = true } = {}) {
  const mf = new Miniflare({ modules: true, script: 'export default { fetch() { return new Response("") } }', d1Databases: ['DB'] });
  const db = await mf.getD1Database('DB');
  const list = statements(readFileSync(file, 'utf8'));
  for (let i = 0; i < list.length; i += 200) await db.batch(list.slice(i, i + 200).map((s) => db.prepare(s)));
  if (indexes) {
    for (const m of readdirSync(join(HERE, 'migrations')).filter((f) => f.endsWith('.sql')).sort()) {
      for (const s of statements(readFileSync(join(HERE, 'migrations', m), 'utf8'))) await db.prepare(s).run();
    }
  }
  return { db, dispose: () => mf.dispose() };
}

/** The same D1, adding up rows_read of every query fn sends: { total, log: [[sql, rows]] }. */
export async function countRows(db, fn) {
  const log = [];
  const wrap = (st, sql) => ({
    _st: st, sql,
    bind: (...a) => wrap(st.bind(...a), sql),
    async all() { const r = await st.all(); log.push([sql, r.meta?.rows_read ?? 0]); return r; },
    async first() { const r = await st.all(); log.push([sql, r.meta?.rows_read ?? 0]); return r.results?.[0] ?? null; },
    async run() { const r = await st.run(); log.push([sql, r.meta?.rows_read ?? 0]); return r; },
  });
  const counted = {
    prepare: (sql) => wrap(db.prepare(sql), sql),
    async batch(l) { const r = await db.batch(l.map((x) => x._st)); r.forEach((x, i) => log.push([`batch: ${l[i].sql}`, x.meta?.rows_read ?? 0])); return r; },
  };
  await fn(counted);
  return { total: log.reduce((s, [, n]) => s + n, 0), log };
}

/**
 * The page loads as the dashboard Worker answers them: /dashboard asks /api/brief, /api/github and /api/live; /data asks
 * /api/summary (7 days, and 30 for some pages) with them. api and dash are src/api.js and src/dashboard.js.
 */
export function loads(api, dash, now) {
  const noGitHub = async () => new Response('{}', { status: 500 });
  const owner = (db) => Promise.all([dash.readBrief({ DB: db }, { now, fetchFn: noGitHub }), dash.cachedGithubHistory(db, now, now),
    api.live(db, { dev: false, now, peaks: dash.cachedPeaks(now) })]);
  const data = (db) => Promise.all([dash.cachedSummary(db, { period: 7, f: ALL, dev: false, now }), dash.cachedSummary(db, { period: 30, f: ALL, dev: false, now }), owner(db)]);
  return { owner, data };
}

async function main() {
  const args = process.argv.slice(2);
  const file = args.find((a) => !a.startsWith('--')) || findBackup();
  if (!file) { console.error('usage: node tools/rows-read.mjs <backup.sql> [--indexes] [--now=ISO]'); process.exit(2); }
  const nowArg = args.find((a) => a.startsWith('--now='));
  const NOW = nowArg ? Date.parse(nowArg.slice(6)) : Date.parse('2026-11-12T22:00:00Z');
  const mfMod = await loadMiniflare();
  if (!mfMod) { console.error('no Miniflare: npm i -g wrangler, or MINIFLARE=<path>'); process.exit(2); }
  const { db, dispose } = await localD1(mfMod.Miniflare, file, { indexes: args.includes('--indexes') });
  const api = await import(pathToFileURL(join(HERE, 'src', 'api.js')).href);
  const dash = await import(pathToFileURL(join(HERE, 'src', 'dashboard.js')).href);
  const label = (sql) => sql.replace(/\s+/g, ' ').slice(0, 90);
  const out = {};
  const report = async (key, name, fn) => {
    const { total, log } = await countRows(db, fn);
    console.log(`\n${name}: ${total} rows read, ${log.length} queries`);
    for (const [sql, n] of log.sort((a, b) => b[1] - a[1]).slice(0, 12)) console.log(`  ${String(n).padStart(8)}  ${label(sql)}`);
    out[key] = total;
  };
  // Each read worked out in full (what every load cost before the memo)
  for (const period of [7, 30]) await report(`summary${period}`, `summary(period ${period}), worked out`, (d) => api.summary(d, { period, f: ALL, dev: false, now: NOW }));
  await report('livePeaks', 'livePeaks(), worked out', (d) => api.livePeaks(d, { dev: false, now: NOW }));
  await report('githubHistory', 'githubHistory(), worked out', (d) => dash.githubHistory(d, NOW));
  const L = loads(api, dash, NOW);
  await report('ownerFirst', '/dashboard, first load (memo empty)', L.owner);
  await report('ownerNext', '/dashboard, next load (memo kept)', L.owner);
  await report('dataFirst', '/data, first load (summary 7 not kept yet)', L.data);
  await report('dataNext', '/data, next load (memo kept)', L.data);
  await report('liveTick', '/api/live, the 1-minute tick', (d) => api.live(d, { dev: false, now: NOW + 60_000, peaks: dash.cachedPeaks(NOW + 60_000) }));
  await report('cron', 'cron every 10 min (sampleLive)', (d) => dash.sampleLive(d, { now: NOW }));
  await report('cronHourly', 'cron, first run of the hour (sampleLive + gh_daily check)', async (d) => {
    await dash.sampleLive(d, { now: NOW });
    await d.prepare("SELECT 1 AS y FROM gh_daily WHERE day = ? AND kind = 'repo' LIMIT 1").bind(new Date(NOW).toISOString().slice(0, 10)).first();
  });
  console.log('\n' + JSON.stringify(out));
  await dispose();
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main();
