// Usage report from the stats database. Leaves out dev builds and the install IDs listed in a local file (one per line, # for
// notes). Run from this folder:
//   node report.mjs [--mine ../../publish/stats-meus.txt] [--days 30]
//
// Since beta.8 the app sends a random ID that is new every day (Telemetry.DailyId), so an ID is one install on one day, never a
// person: "install-days" below, and nothing can say whether someone came back. Only versions before beta.8 had a fixed ID; the
// --mine list (those fixed IDs of the PCs of the people who make the app) only matters for that older data. Since beta.8 those PCs
// send dev = 1 ("Mine" in telemetry.json) and are left out by the query.
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

// One entry per ID and day: with the daily ID that is one install on one day.
const installDays = new Map();   // "day|id" -> { minutes, country, app, mode }
for (const r of rows) {
  const k = `${r.day}|${r.id}`;
  const i = installDays.get(k) ?? { minutes: 0 };
  i.minutes += r.minutes ?? 0;   // each ping carries the minutes since the one before
  i.country = r.country ?? i.country; i.app = r.app; i.mode = r.mode;
  installDays.set(k, i);
}
const count = (f) => [...installDays.values()].reduce((m, i) => m.set(f(i) ?? '?', (m.get(f(i) ?? '?') ?? 0) + 1), new Map());
const show = (title, m) => console.log(`${title}: ${[...m].sort((a, b) => b[1] - a[1]).map(([k, n]) => `${k} ${n}`).join(' · ') || 'none'}`);

console.log(`Install-days since ${since} (one install open on one day; left out: dev builds and ${mine.length} fixed IDs of yours): ${installDays.size}`);
show('Countries (install-days)', count((i) => i.country));
show('Version (install-days)', count((i) => i.app));
show('Mode (install-days)', count((i) => i.mode));
const byDay = new Map();
for (const r of rows) byDay.set(r.day, (byDay.get(r.day) ?? new Set()).add(r.id));
console.log('Active per day: ' + ([...byDay].map(([d, s]) => `${d.slice(5)} ${s.size}`).join(' · ') || 'none'));
const mins = [...installDays.values()].reduce((s, i) => s + i.minutes, 0);
console.log(`Minutes open in total: ${mins} · Average per install-day: ${installDays.size ? Math.round(mins / installDays.size) : 0}`);
