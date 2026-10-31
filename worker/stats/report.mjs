// Usage report from the stats database: real installs only. Leaves out dev builds and the install IDs listed in a local
// file (one per line, # for notes), the PCs of the people who make the app. Run from this folder:
//   node report.mjs [--mine ../../publish/stats-meus.txt] [--days 30]
import { execSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';

const arg = (name, def) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : def; };
const minePath = arg('--mine', '../../publish/stats-meus.txt');
const days = Number(arg('--days', '30'));
const mine = existsSync(minePath)
  ? readFileSync(minePath, 'utf8').split(/\r?\n/).map((l) => l.replace(/#.*/, '').trim()).filter((l) => /^[0-9a-f]{32}$/.test(l))
  : [];

const since = new Date(Date.now() - days * 864e5).toISOString().slice(0, 10);
const sql = `SELECT day, id, app, mode, link, minutes, country FROM pings WHERE dev = 0 AND day >= '${since}' ORDER BY ts`;
const out = execSync(`npx wrangler d1 execute cs2ipc-stats --remote --json --command ${JSON.stringify(sql)}`,
  { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
const rows = JSON.parse(out)[0].results.filter((r) => !mine.includes(r.id));

const installs = new Map();   // id -> { first, last, days, minutes, country, app }
for (const r of rows) {
  const i = installs.get(r.id) ?? { first: r.day, days: new Set(), minutes: 0 };
  i.last = r.day; i.days.add(r.day); i.minutes += r.minutes ?? 0; i.country = r.country ?? i.country; i.app = r.app; i.mode = r.mode;
  installs.set(r.id, i);
}
const count = (f) => [...installs.values()].reduce((m, i) => m.set(f(i) ?? '?', (m.get(f(i) ?? '?') ?? 0) + 1), new Map());
const show = (title, m) => console.log(`${title}: ${[...m].sort((a, b) => b[1] - a[1]).map(([k, n]) => `${k} ${n}`).join(' · ') || 'none'}`);

console.log(`Real installs since ${since} (left out: dev builds and ${mine.length} of yours): ${installs.size}`);
show('Countries', count((i) => i.country));
show('Version', count((i) => i.app));
show('Mode', count((i) => i.mode));
const byDay = new Map();
for (const r of rows) byDay.set(r.day, (byDay.get(r.day) ?? new Set()).add(r.id));
console.log('Active per day: ' + ([...byDay].map(([d, s]) => `${d.slice(5)} ${s.size}`).join(' · ') || 'none'));
const back = [...installs.values()].filter((i) => i.days.size > 1).length;
const mins = [...installs.values()].reduce((s, i) => s + i.minutes, 0);
console.log(`Came back another day: ${back} · Minutes open in total: ${mins}`);
