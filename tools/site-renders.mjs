// The site's 3D pictures, rendered from the live scene (site/assets/js/scenes.min.js) with the Connection capture
// (docs/screenshots/connection.png) on the monitor:
//   site/img/poster-<w>.webp   the cover's first frame, shown under the canvas until the scene is ready (the home itself)
//   site/img/world-<name>.webp stills of the same world for the sections after the story (a lab page with only the scene)
// The script serves site/ the way .github/workflows/pages.yml builds it (img/ from docs/screenshots) on a free port, takes
// the pictures with Playwright's Chromium, makes the WebP with ffmpeg and, when it made every file of RENDERS in
// tools/shots.mjs, runs node tools/shots.mjs --renders (tests/DesignSyncTests.cs then knows which Connection they show).
//   node tools/site-renders.mjs                 the RENDERS (posters, world-tower, world-plug), then --renders
//   node tools/site-renders.mjs --all           also the stills without the monitor (mast, racks, iphone, android)
//   node tools/site-renders.mjs world-plug ...  only those (no --renders unless they are all of RENDERS)
//   node tools/site-renders.mjs --out <folder>  a trial: the WebP go to <folder>, nothing in the repo changes
//   node tools/site-renders.mjs --serve         only the server, to look at the home and the lab (/cs2-ip-changer/__lab.html)
// The PNG it shot first stay in %TEMP%/cs2-site-renders (or --png <folder>).
// Needs: Playwright with its Chromium (npm i playwright && npx playwright install chromium, in the repo or in any folder;
// PLAYWRIGHT=<that folder> when it is not the repo or the global npm), and ffmpeg on the PATH.
import { createServer } from 'node:http';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { basename, extname, join, resolve, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { ROOT, RENDERS } from './shots.mjs';

const SITE = join(ROOT, 'site');
const BASE = '/cs2-ip-changer';

// The home at each size the cover's <picture> picks: [width, height, device scale, phone]
const POSTERS = {
  'poster-1280': [1280, 800, 1, false],
  'poster-1366': [1366, 768, 1, false],
  'poster-1440': [1440, 900, 1, false],
  'poster-1536': [1536, 864, 1, false],
  'poster-1920': [1920, 1080, 1, false],
  'poster-390': [390, 844, 2, true],
};
// The lab page: p is the story position (the state of the route), cam replaces the camera of shot i, extra are url flags
const STILLS = {
  'world-tower': { w: 800, h: 1000, p: 1, cam: { i: 1, layout: 'tall', pos: [-12.5, 3.4, 9.5], tgt: [-6.2, 1.5, 0.9], fov: 34, keys: false }, extra: '&full' },
  'world-plug': { w: 1680, h: 720, p: 1, cam: { i: 1, pos: [-2.2, 3.0, 9.6], tgt: [-4.0, 0.7, 1.9], fov: 30, keys: false }, extra: '&full' },
  'world-mast': { w: 1000, h: 1000, p: 2, cam: { i: 2, pos: [2, 1.6, 5.2], tgt: [5.5, 4.6, -6.5], fov: 46, keys: false }, extra: '&full' },
  'world-racks': { w: 1000, h: 1000, p: 3, noarc: true, cam: { i: 3, pos: [13.5, 2.4, -1.5], tgt: [17, 2.1, -9.5], fov: 42, keys: false }, extra: '&full' },
  'world-iphone': { w: 1000, h: 1000, p: 1, cam: { i: 1, pos: [-3.9, 2.5, 7.8], tgt: [-0.62, 1.05, 2.78], fov: 21, keys: false }, extra: '&full&ios' },
  'world-android': { w: 1000, h: 1000, p: 1, cam: { i: 1, pos: [-3.9, 2.5, 7.8], tgt: [-0.62, 1.05, 2.78], fov: 21, keys: false }, extra: '&full' },
};
// world-iphone/android: camera and target 0.1 higher since 9 Nov., with the phone (LIFT in world.js), the same framing
const STILL_SCALE = 1.5, STILL_OUT = 1.25;   // shot at 1.5x, the WebP at 1.25x of the css size

// Only the scene on the page's ground, with the site's own fonts and tokens (read from site/index.html when served)
const LAB = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<style>
/*TOKENS*/
@font-face{font-family:"Funnel Sans";src:url(fonts/funnel-sans.woff2) format("woff2");font-weight:300 800}
@font-face{font-family:"Fragment Mono";src:url(fonts/fragment-mono.woff2) format("woff2")}
html,body{margin:0;background:var(--t-bg);height:100%;overflow:hidden}
.st{position:fixed;inset:0}canvas{display:block;width:100%;height:100%}
.nolab .wd-labels{display:none}</style></head><body>
<div class="st"><canvas id="c"></canvas></div>
<script src="assets/js/scenes.min.js"></script>
<script>
var q=new URLSearchParams(location.search),p=+(q.get('p')||0),sub=+(q.get('sub')||0);
if(q.has('nolab'))document.documentElement.classList.add('nolab');
function frame(W,H,i,lay){if(q.has('full'))return{box:[W*.04,H*.06,W*.96,H*.94]};var b=lay==='tall'?[12,72,W-12,H*.52]:[W*.42,80,W-24,H-60];if(i===5)b=lay==='tall'?[8,72,W-8,H*.5]:[W*.4,90,W-40,H-80];return{box:b}}
var api=WorldScene.mount(document.getElementById('c'),{still:q.has('still'),frame:frame,debug:1,ios:q.has('ios'),cam:q.get('cam')?JSON.parse(q.get('cam')):null});
api.set({p:p,sub:sub});
document.getElementById('c').addEventListener('worldready',function(){window.__ready=1});
</script></body></html>`;

const TYPES = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.mjs': 'text/javascript', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.avif': 'image/avif', '.jpg': 'image/jpeg', '.woff2': 'font/woff2', '.json': 'application/json', '.txt': 'text/plain', '.xml': 'application/xml', '.mp4': 'video/mp4', '.webm': 'video/webm' };
// What the Pages workflow adds to img/: every docs/screenshots/*.png (with a lossless WebP of each) and three of docs/assets
const PAGES_IMG = ['docs/screenshots/', 'docs/assets/icon.png', 'docs/assets/social-preview.png', 'docs/assets/how-it-works-dark.svg',
  'docs/assets/favicon.svg', 'docs/assets/favicon.ico', 'docs/assets/apple-touch-icon.png'];

function pagesImg(name) {
  for (const p of PAGES_IMG) {
    const f = p.endsWith('/') ? join(ROOT, p, name) : (basename(p) === name ? join(ROOT, p) : null);
    if (f && existsSync(f)) return f;
  }
  // the lossless WebP has the PNG's pixels, so the PNG itself stands in for it (the browser reads the bytes, not the name)
  if (name.endsWith('.webp')) return pagesImg(name.replace(/\.webp$/, '.png'));
  return null;
}

/** A url of the site -> [file or text, content type], or null. */
function route(url) {
  let p = decodeURIComponent(url.split('?')[0]);
  if (!p.startsWith(BASE)) return null;
  p = p.slice(BASE.length) || '/';
  if (p === '/__lab.html') {
    const tokens = /\/\* tokens:[\s\S]*?\/\* \/tokens \*\//.exec(readFileSync(join(SITE, 'index.html'), 'utf8'));
    if (!tokens) throw new Error('site/index.html has no tokens block (node tools/design-tokens.mjs)');
    return [{ text: LAB.replace('/*TOKENS*/', `:root{${tokens[0]}}`) }, TYPES['.html']];
  }
  let f = resolve(SITE, '.' + p);
  if (f !== SITE && !f.startsWith(SITE + sep)) return null;
  if (p.startsWith('/img/') && !existsSync(f)) f = pagesImg(p.slice(5));
  if (f && existsSync(f) && statSync(f).isDirectory()) f = join(f, 'index.html');
  if (!f || !existsSync(f)) return null;
  return [f, TYPES[extname(f)] || 'application/octet-stream'];
}

function serve() {
  const server = createServer((q, s) => {
    const r = route(q.url);
    if (!r) { s.writeHead(404, { 'content-type': 'text/plain' }); return s.end('not found'); }
    s.writeHead(200, { 'content-type': r[1], 'cache-control': 'no-store' });
    s.end(typeof r[0] === 'string' ? readFileSync(r[0]) : r[0].text);
  });
  return new Promise((ok) => server.listen(0, '127.0.0.1', () => ok(server)));
}

/** Playwright from PLAYWRIGHT (a folder with it, or its own folder), the repo, or the global npm. */
async function playwright() {
  const where = [process.env.PLAYWRIGHT, ROOT];
  const g = spawnSync('npm root -g', { encoding: 'utf8', shell: true });
  if (g.status === 0 && g.stdout.trim()) where.push(g.stdout.trim());
  for (const w of where.filter(Boolean)) {
    try {
      const m = await import(pathToFileURL(createRequire(join(resolve(w), 'x.js')).resolve('playwright')).href);
      return m.chromium || m.default.chromium;
    } catch { /* not there */ }
  }
  console.error('No Playwright. In any folder: npm i playwright && npx playwright install chromium, then PLAYWRIGHT=<that folder> node tools/site-renders.mjs');
  process.exit(1);
}

function ffmpeg(args, quiet = false) {
  const r = spawnSync('ffmpeg', ['-y', '-loglevel', 'error', ...args], { stdio: quiet ? 'ignore' : 'inherit' });
  if (r.status !== 0) { console.error(r.error ? 'No ffmpeg on the PATH (winget install Gyan.FFmpeg).' : `ffmpeg failed: ${args.join(' ')}`); process.exit(1); }
}

const args = process.argv.slice(2);
const opt = (n) => { const i = args.indexOf(n); return i >= 0 ? resolve(args[i + 1]) : null; };
const trial = opt('--out'), pngDir = opt('--png') || join(tmpdir(), 'cs2-site-renders');
const server = await serve();
const origin = `http://127.0.0.1:${server.address().port}${BASE}/`;

if (args.includes('--serve')) {
  console.log(`Serving site/ on ${origin} (the lab: ${origin}__lab.html?p=1&full). Ctrl+C to stop.`);
} else {
  const wanted = RENDERS.map((f) => basename(f, '.webp'));
  const named = args.filter((a, i) => !a.startsWith('--') && !['--out', '--png'].includes(args[i - 1]));
  const names = named.length ? named : args.includes('--all') ? [...Object.keys(POSTERS), ...Object.keys(STILLS)] : wanted;
  const unknown = names.filter((n) => !POSTERS[n] && !STILLS[n]);
  if (unknown.length) { console.error(`Unknown: ${unknown.join(', ')}. Known: ${[...Object.keys(POSTERS), ...Object.keys(STILLS)].join(', ')}`); process.exit(1); }
  const imgDir = trial || join(ROOT, 'site/img');
  for (const d of [imgDir, pngDir]) mkdirSync(d, { recursive: true });
  ffmpeg(['-version'], true);

  const chromium = await playwright();
  // the GPU, as a visitor has it (on Windows through D3D11); without it the scene falls back to software and looks different
  const gpu = process.platform === 'win32' ? ['--use-angle=d3d11', '--ignore-gpu-blocklist', '--enable-gpu'] : ['--ignore-gpu-blocklist', '--enable-gpu'];
  let browser;
  try { browser = await chromium.launch({ args: gpu }); } catch (e) {
    console.error(`Chromium did not start (npx playwright install chromium in the Playwright folder): ${e.message.split('\n')[0]}`); process.exit(1);
  }
  let failed = false;
  for (const n of names) {
    const png = join(pngDir, `${n}.png`), webp = join(imgDir, `${n}.webp`);
    try {
      if (POSTERS[n]) {
        const [w, h, dpr, phone] = POSTERS[n];
        const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: dpr, isMobile: phone, hasTouch: phone });
        const p = await ctx.newPage();
        p.on('pageerror', (e) => console.log(`  ${n}: pageerror ${e.message}`));
        await p.goto(origin, { waitUntil: 'networkidle' });
        await p.waitForFunction(() => document.querySelector('.stage.ready'), null, { timeout: 30000 });
        await p.waitForTimeout(2500);
        // only the scene: no page over it, no labels, no poster under it
        await p.addStyleTag({ content: 'header,main,.rail,.pause,.cols,.wd-labels,.stage-still,.ld-scr{visibility:hidden!important}.stage::after{display:none!important}' });
        await p.waitForTimeout(400);
        await p.screenshot({ path: png });
        await ctx.close();
        ffmpeg(['-i', png, '-c:v', 'libwebp', '-quality', '82', webp]);
      } else {
        const c = STILLS[n];
        const p = await browser.newPage({ viewport: { width: c.w, height: c.h }, deviceScaleFactor: STILL_SCALE });
        p.on('pageerror', (e) => console.log(`  ${n}: pageerror ${e.message}`));
        const cam = '&cam=' + encodeURIComponent(JSON.stringify([c.cam]));
        await p.goto(`${origin}__lab.html?p=${c.p}&still&nolab${cam}${c.extra}`);
        await p.waitForFunction(() => window.__ready, null, { timeout: 30000 });
        await p.waitForTimeout(2500);
        if (c.noarc) {   // the racks without the route over them
          await p.evaluate(() => { const w = window.__wd; w.scene.traverse((o) => { if (o.material === w.mArc || o.material === w.mAir) o.visible = false; }); w.request(); });
          await p.waitForTimeout(800);
        }
        await p.screenshot({ path: png });
        await p.close();
        ffmpeg(['-i', png, '-vf', `scale=${Math.round(c.w * STILL_OUT)}:-2`, '-c:v', 'libwebp', '-quality', '84', webp]);
      }
      console.log(`  ${trial ? webp : `site/img/${n}.webp`}`);
    } catch (e) {
      failed = true;
      console.error(`  ${n}: ${e.message.split('\n')[0]}`);
    }
  }
  await browser.close();
  server.close();
  console.log(`PNG in ${pngDir}`);
  if (failed) { console.error('Some renders failed: tools/shots.mjs --renders not run.'); process.exit(1); }
  if (trial) console.log(`Trial in ${trial}: site/img and the manifest not changed.`);
  else if (wanted.every((n) => names.includes(n))) {
    const r = spawnSync(process.execPath, [join(ROOT, 'tools/shots.mjs'), '--renders'], { stdio: 'inherit', cwd: ROOT });
    process.exit(r.status ?? 1);
  } else console.log(`Not all of RENDERS: run node tools/site-renders.mjs (all of them) before node tools/shots.mjs --renders.`);
}
