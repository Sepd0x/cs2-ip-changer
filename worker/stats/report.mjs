// Usage report from the stats database. Leaves out dev builds and the install IDs listed in a local file (one per line, # for
// notes). Run from this folder:
//   node report.mjs [--mine ../../publish/stats-meus.txt] [--days 30]
//
// The unit is the install-day: one install open on one day. beta.8 sent an ID that was new every day; since beta.9 the ID is kept per
// install (random, never from the PC or the person), so the dashboard can say who came back. The --mine list (fixed IDs of the PCs
// of the people who make the app) only matters for versions before beta.8; since then those PCs send dev = 1 and the query leaves
// them out. Like the dashboard (src/api.js), an install-day with no Steam account, no click and no minute is an online virus
// scanner running the exe in a sandbox: counted apart, out of the numbers.
import { execSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';

const arg = (name, def) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : def; };
const minePath = arg('--mine', '../../publish/stats-meus.txt');
const days = Number(arg('--days', '30'));
const mine = existsSync(minePath)
  ? readFileSync(minePath, 'utf8').split(/\r?\n/).map((l) => l.replace(/#.*/, '').trim()).filter((l) => /^[0-9a-f]{32}$/.test(l))
  : [];

const since = new Date(Date.now() - days * 864e5).toISOString().slice(0, 10);
const sql = `SELECT p.day, p.id, p.app, p.mode, p.minutes, p.country,
  (SELECT MAX(n) FROM counts c WHERE c.day = p.day AND c.id = p.id AND c.name = 'accounts') AS acc,
  (SELECT SUM(n) FROM clicks k WHERE k.day = p.day AND k.id = p.id) AS clicks
  FROM pings p WHERE p.dev = 0 AND p.day >= '${since}' ORDER BY p.ts`.replace(/\s+/g, ' ');   // one line for the shell
const out = execSync(`npx wrangler d1 execute cs2ipc-stats --remote --json --command ${JSON.stringify(sql)}`,
  { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
const rows = JSON.parse(out)[0].results.filter((r) => !mine.includes(r.id));

// One entry per ID and day: one install on one day.
const all = new Map();   // "day|id" -> { minutes, country, app, mode, acc, clicks }
for (const r of rows) {
  const k = `${r.day}|${r.id}`;
  const i = all.get(k) ?? { minutes: 0, acc: 0, clicks: 0 };
  i.minutes += r.minutes ?? 0;   // each ping carries the minutes since the one before
  i.country = r.country ?? i.country; i.app = r.app; i.mode = r.mode;
  i.acc = Math.max(i.acc, r.acc ?? 0); i.clicks = Math.max(i.clicks, r.clicks ?? 0);
  all.set(k, i);
}
const real = (i) => i.acc >= 1 || i.clicks > 0 || i.minutes >= 1;   // the same rule as src/api.js
const installDays = new Map([...all].filter(([, i]) => real(i)));
const count = (f) => [...installDays.values()].reduce((m, i) => m.set(f(i) ?? '?', (m.get(f(i) ?? '?') ?? 0) + 1), new Map());
const show = (title, m) => console.log(`${title}: ${[...m].sort((a, b) => b[1] - a[1]).map(([k, n]) => `${k} ${n}`).join(' · ') || 'none'}`);

console.log(`Install-days since ${since} (one install open on one day; left out: dev builds and ${mine.length} fixed IDs of yours): ${installDays.size}`);
console.log(`Automated (virus scanners: no account, no click, no minute), not counted: ${all.size - installDays.size}`);
show('Countries (install-days)', count((i) => i.country));
show('Version (install-days)', count((i) => i.app));
show('Mode (install-days)', count((i) => i.mode));
const byDay = new Map();
for (const r of rows) if (installDays.has(`${r.day}|${r.id}`)) byDay.set(r.day, (byDay.get(r.day) ?? new Set()).add(r.id));
console.log('Active per day: ' + ([...byDay].map(([d, s]) => `${d.slice(5)} ${s.size}`).join(' · ') || 'none'));
const mins = [...installDays.values()].reduce((s, i) => s + i.minutes, 0);
console.log(`Minutes open in total: ${mins} · Average per install-day: ${installDays.size ? Math.round(mins / installDays.size) : 0}`);
