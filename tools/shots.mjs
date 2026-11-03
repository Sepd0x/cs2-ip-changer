// Every capture of the app in one run, from the demo of the dev build (needs src/Privado; Debug has no UAC prompt):
//   docs/screenshots/<page>.png           README, docs/FEATURES.md and the site (.github/workflows/pages.yml copies them)
//   worker/stats/dashboard/ui/<page>.png  the dashboard's "Na app" page; the button map of each (.json, --mapa) goes to
//                                         publish/ui-mapa/, to bring worker/stats/dashboard/ui-map.js up to date by hand
// then the framed copies for the README (python tools/readme-shots.py) and the dashboard assets (node tools/dashboard-assets.mjs).
//   node tools/shots.mjs                  build Debug, capture, frame, pack
//   node tools/shots.mjs --no-build       with the Debug build already there
//   node tools/shots.mjs --out <folder>   a trial: everything under <folder>, nothing in the repo changes
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, copyFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('../', import.meta.url));
const args = process.argv.slice(2);
const o = args.indexOf('--out');
const trial = o >= 0 ? resolve(args[o + 1]) : null;
const EXE = join(ROOT, 'src/bin/Debug/net8.0-windows/win-x64/CS2IPChanger.exe');

// capture name -> the demo's page; the README has the first six, the dashboard all eight
const PAGES = { connection: 'net', servers: 'servers', regions: 'regions', accounts: 'accounts', configs: 'configs', profiles: 'profiles', log: 'log', settings: 'settings' };
const README = ['connection', 'servers', 'regions', 'accounts', 'configs', 'profiles'];

const run = (cmd, a, opts = {}) => {
  const r = spawnSync(cmd, a, { stdio: 'inherit', cwd: ROOT, timeout: 120000, ...opts });
  if (r.status !== 0) { console.error(`Failed: ${cmd} ${a.join(' ')}`); process.exit(1); }
};

if (!existsSync(join(ROOT, 'src/Privado'))) { console.error('The demo is in src/Privado (outside git): no captures without it.'); process.exit(1); }
if (!args.includes('--no-build')) run('dotnet', ['build', 'src/ServerRouteTool.csproj', '-c', 'Debug', '-v', 'q', '-nologo']);
if (!existsSync(EXE)) { console.error(`No ${EXE}: run without --no-build.`); process.exit(1); }

const shots = trial ? join(trial, 'screenshots') : join(ROOT, 'docs/screenshots');
const ui = trial ? join(trial, 'ui') : join(ROOT, 'worker/stats/dashboard/ui');
const map = trial ? join(trial, 'ui-mapa') : join(ROOT, 'publish/ui-mapa');
for (const d of [shots, ui, map]) mkdirSync(d, { recursive: true });

for (const [name, page] of Object.entries(PAGES)) {
  // with --mapa the demo writes <name>.json next to the capture: capture into the map folder, then copy the png out
  run(EXE, ['--demo', '--shot', join(map, `${name}.png`), page, '--size', '1240x800', '--mapa']);
  copyFileSync(join(map, `${name}.png`), join(ui, `${name}.png`));
  if (README.includes(name)) copyFileSync(join(map, `${name}.png`), join(shots, `${name}.png`));
  console.log(`  ${name}`);
}

if (trial) console.log(`Trial in ${trial}: README frames and dashboard assets not made.`);
else {
  run('python', ['tools/readme-shots.py']);
  run(process.execPath, ['tools/dashboard-assets.mjs']);
  console.log('Done. Compare the new captures before committing; publish/ui-mapa/*.json has the buttons for ui-map.js.');
}
