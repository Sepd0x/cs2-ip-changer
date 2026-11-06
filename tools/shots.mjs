// Every capture of the app in one run, from the demo of the dev build (needs src/Privado; Debug has no UAC prompt):
//   docs/screenshots/<page>.png           at 2x (2480x1600): the site (.github/workflows/pages.yml copies them to img/, with a
//                                         WebP each; the 3D monitor shows connection) and the README frames
//   docs/assets/shots/<page>.png|webp     the framed copies for README.md and docs/FEATURES.md (python tools/readme-shots.py)
//   worker/stats/dashboard/ui/<page>.png  at 1x (1240x800): the dashboard's click map. 1x on purpose: the Worker carries them
//                                         inside src/assets.js. The button map of each (.json, --mapa) goes to publish/ui-mapa/,
//                                         to bring worker/stats/dashboard/ui-map.js up to date by hand
//   docs/assets/shots.json                the manifest: the app version and the fingerprint of its XAML the captures were made
//                                         from, and every file's size and SHA-256. tests/DesignSyncTests.cs fails when the XAML or
//                                         the version moved on, or when a capture no longer matches its line.
// then it packs the dashboard (node tools/dashboard-assets.mjs).
//   node tools/shots.mjs                  build Debug, capture, frame, pack, write the manifest
//   node tools/shots.mjs --no-build       with the Debug build already there
//   node tools/shots.mjs --out <folder>   a trial: everything under <folder>, nothing in the repo changes
//   node tools/shots.mjs --social         after docs/assets/src/render.cmd: records which Status the social card shows (without
//                                         Chrome the full run leaves the old card, says so and ends with an error)
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, copyFileSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const ROOT = fileURLToPath(new URL('../', import.meta.url));
export const MANIFEST = 'docs/assets/shots.json';
const EXE = join(ROOT, 'src/bin/Debug/net8.0-windows/win-x64/CS2IPChanger.exe');

// capture name -> the demo's page; the site and the README have the first six, the dashboard all eight
export const PAGES = { connection: 'net', servers: 'servers', regions: 'regions', accounts: 'accounts', configs: 'configs', profiles: 'profiles', log: 'log', settings: 'settings' };
export const SITE = ['connection', 'servers', 'regions', 'accounts', 'configs', 'profiles'];
export const SIZE = [1240, 800], SCALE = 2;

const sha = (buf) => createHash('sha256').update(buf).digest('hex');
const lf = (p) => readFileSync(p, 'utf8').replace(/^\uFEFF/, '').replaceAll('\r\n', '\n');

/** The app's XAML files (src/*.xaml, src/Views/*.xaml), the ones a capture shows. Same list in tests/DesignSyncTests.cs. */
export function uiFiles() {
  const list = [];
  for (const d of ['src', 'src/Views']) for (const n of readdirSync(join(ROOT, d)).sort()) if (n.endsWith('.xaml')) list.push(`${d}/${n}`);
  return list.sort();
}

/** SHA-256 over "<path>\n<text with LF>\n" of each XAML file in order: changes when anything in the app's UI changes. */
export function uiFingerprint() {
  return sha(uiFiles().map((f) => `${f}\n${lf(join(ROOT, f))}\n`).join(''));
}

export function appVersion() {
  return /<Version>([^<]+)<\/Version>/.exec(readFileSync(join(ROOT, 'src/ServerRouteTool.csproj'), 'utf8'))[1];
}

/** PNG width and height from its header. */
function pngSize(buf) { return [buf.readUInt32BE(16), buf.readUInt32BE(20)]; }

function entry(rel) {
  const buf = readFileSync(join(ROOT, rel));
  const e = { file: rel, sha256: sha(buf) };
  if (rel.endsWith('.png')) e.size = pngSize(buf);
  return e;
}

// The site's 3D pictures with the Connection capture on the monitor: rendered from the live scene, not by this script but
// by node tools/site-renders.mjs, which then runs node tools/shots.mjs --renders (records which connection.png they show).
export const RENDERS = ['poster-1280', 'poster-1366', 'poster-1440', 'poster-1536', 'poster-1920', 'poster-390', 'world-tower', 'world-plug'].map((n) => `site/img/${n}.webp`);
const CONNECTION = 'docs/screenshots/connection.png';

// The social preview card (the site's og:image) is rendered by Chrome from the Status piece readme-shots.py cuts from connection.png.
const STATUS = 'docs/assets/src/social-preview-status.png', SOCIAL = 'docs/assets/social-preview.png';

/**
 * Which Status piece the social preview card shows (SHA-256): the one it was just rendered from, else what the manifest had (null:
 * not known). Without Chrome the old card stays, and the manifest must not say it shows the new Status.
 */
export function socialFrom(old, rendered, statusSha) { return rendered ? statusSha : old?.social?.from ?? null; }

/** The manifest of the captures as they are now in the repo; the renders and the social card keep the capture they were made from. */
export function manifest(stampRenders = false, socialRendered = false) {
  const old = existsSync(join(ROOT, MANIFEST)) ? JSON.parse(readFileSync(join(ROOT, MANIFEST), 'utf8')) : {};
  const renders = stampRenders || !old.renders ? { from: entry(CONNECTION).sha256, files: RENDERS.map(entry) } : old.renders;
  const social = { from: socialFrom(old, socialRendered, entry(STATUS).sha256) };
  const files = [];
  for (const n of SITE) files.push(`docs/screenshots/${n}.png`);
  for (const n of readdirSync(join(ROOT, 'docs/assets/shots')).sort()) if (/\.(png|webp)$/.test(n)) files.push(`docs/assets/shots/${n}`);
  for (const n of Object.keys(PAGES)) files.push(`worker/stats/dashboard/ui/${n}.png`);
  files.push(STATUS, SOCIAL);
  return {
    note: 'Made by tools/shots.mjs. Do not edit: tests/DesignSyncTests.cs checks it against the app and the files.',
    app: appVersion(), ui: uiFingerprint(), uiFiles: uiFiles(), made: new Date().toISOString().slice(0, 10), scale: SCALE, base: SIZE,
    files: files.map(entry), renders, social,
  };
}

/** The buttons of worker/stats/dashboard/ui-map.js whose box is no longer where the new --mapa capture has it. */
export function mapDrift(mapDir) {
  const UI = new Function(readFileSync(join(ROOT, 'worker/stats/dashboard/ui-map.js'), 'utf8') + ';return UI;')();
  const out = [];
  for (const s of UI.screens) {
    const f = join(mapDir, `${s.id}.json`);
    if (!existsSync(f)) continue;
    const now = {};
    for (const b of JSON.parse(readFileSync(f, 'utf8'))) (now[b.click.replace(/_Click$/, '')] ||= []).push(b.r);
    for (const b of s.buttons) {
      if (!b.r || !now[b.id]) continue;   // a button the capture doesn't show (a panel, another state) keeps its hand-made box
      if (!now[b.id].some((r) => r.every((v, i) => Math.abs(v - b.r[i]) <= 2))) out.push(`${s.id} ${b.id}: [${b.r}] -> [${now[b.id][0]}]`);
    }
    const known = new Set(UI.screens.flatMap((x) => x.buttons.map((b) => b.id)).concat(['Min', 'Max', 'Close']));
    for (const [id, rs] of Object.entries(now)) if (!known.has(id) && rs[0][0] >= UI.X0) out.push(`${s.id} ${id}: new button at [${rs[0]}]`);
  }
  return out;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  const args = process.argv.slice(2);
  const o = args.indexOf('--out');
  const trial = o >= 0 ? resolve(args[o + 1]) : null;
  const run = (cmd, a, opts = {}) => {
    const r = spawnSync(cmd, a, { stdio: 'inherit', cwd: ROOT, timeout: 120000, ...opts });
    if (r.status !== 0) { console.error(`Failed: ${cmd} ${a.join(' ')}`); process.exit(1); }
  };
  if (args.includes('--renders')) {
    const m = { ...JSON.parse(readFileSync(join(ROOT, MANIFEST), 'utf8')), renders: manifest(true).renders };
    writeFileSync(join(ROOT, MANIFEST), JSON.stringify(m, null, 1) + '\n');
    console.log(`${MANIFEST}: the renders show ${CONNECTION} ${m.renders.from.slice(0, 12)}`);
    process.exit(0);
  }
  if (args.includes('--social')) {
    // after docs/assets/src/render.cmd made the social card by hand (no Chrome here when shots.mjs ran)
    const old = JSON.parse(readFileSync(join(ROOT, MANIFEST), 'utf8'));
    const m = { ...old, files: old.files.map((f) => (f.file === STATUS || f.file === SOCIAL ? entry(f.file) : f)), social: { from: entry(STATUS).sha256 } };
    writeFileSync(join(ROOT, MANIFEST), JSON.stringify(m, null, 1) + '\n');
    console.log(`${MANIFEST}: ${SOCIAL} shows ${STATUS} ${m.social.from.slice(0, 12)}`);
    process.exit(0);
  }

  if (!existsSync(join(ROOT, 'src/Privado'))) { console.error('The demo is in src/Privado (outside git): no captures without it.'); process.exit(1); }
  if (!args.includes('--no-build')) run('dotnet', ['build', 'src/ServerRouteTool.csproj', '-c', 'Debug', '-v', 'q', '-nologo']);
  if (!existsSync(EXE)) { console.error(`No ${EXE}: run without --no-build.`); process.exit(1); }

  const shots = trial ? join(trial, 'screenshots') : join(ROOT, 'docs/screenshots');
  const ui = trial ? join(trial, 'ui') : join(ROOT, 'worker/stats/dashboard/ui');
  const map = trial ? join(trial, 'ui-mapa') : join(ROOT, 'publish/ui-mapa');
  for (const d of [shots, ui, map]) mkdirSync(d, { recursive: true });

  const size = SIZE.join('x');
  for (const [name, page] of Object.entries(PAGES)) {
    // 1x for the dashboard, with --mapa (the button boxes are in 1x pixels, as ui-map.js has them)
    run(EXE, ['--demo', '--shot', join(map, `${name}.png`), page, '--size', size, '--scale', '1', '--mapa']);
    copyFileSync(join(map, `${name}.png`), join(ui, `${name}.png`));
    // 2x for the site and the README
    if (SITE.includes(name)) run(EXE, ['--demo', '--shot', join(shots, `${name}.png`), page, '--size', size, '--scale', String(SCALE)]);
    console.log(`  ${name}`);
  }

  const drift = mapDrift(map);
  if (drift.length) console.log(`worker/stats/dashboard/ui-map.js is behind the captures (fix it by hand):\n  ${drift.join('\n  ')}`);

  if (trial) console.log(`Trial in ${trial}: README frames, dashboard assets and the manifest not made.`);
  else {
    run('python', ['tools/readme-shots.py']);
    // the social preview card shows Status from the new connection capture (as docs/assets/src/render.cmd renders it)
    const chrome = join(process.env.ProgramFiles || 'C:/Program Files', 'Google/Chrome/Application/chrome.exe');
    const socialRendered = existsSync(chrome);
    if (socialRendered) {
      run(chrome, ['--headless=new', '--hide-scrollbars', '--disable-lcd-text', '--allow-file-access-from-files', '--force-device-scale-factor=1', '--window-size=1280,640',
        `--screenshot=${join(ROOT, SOCIAL)}`, 'file:///' + join(ROOT, 'docs/assets/src/social-preview.html').replaceAll('\\', '/')]);
      console.log(`  ${SOCIAL} (the site's og:image; upload it by hand on GitHub: Settings > General > Social preview)`);
    }
    run(process.execPath, ['tools/dashboard-assets.mjs']);
    writeFileSync(join(ROOT, MANIFEST), JSON.stringify(manifest(false, socialRendered), null, 1) + '\n');
    const m = JSON.parse(readFileSync(join(ROOT, MANIFEST), 'utf8'));
    if (m.renders.from !== entry(CONNECTION).sha256) console.log(`The 3D posters and stills still show the old ${CONNECTION}: render them with node tools/site-renders.mjs.`);
    if (m.social.from !== entry(STATUS).sha256) {
      // not a quiet line among the others: the card everyone sees when the site is shared would stay the old one
      console.error(`No Chrome: ${SOCIAL} still shows the old Status. Run docs/assets/src/render.cmd, then node tools/shots.mjs --social.`);
      process.exitCode = 1;
    }
    console.log(`Done, ${MANIFEST} written. Compare the new captures before committing; publish/ui-mapa/*.json has the buttons for ui-map.js.`);
  }
}
