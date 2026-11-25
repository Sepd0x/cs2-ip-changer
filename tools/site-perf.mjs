// How heavy the site feels: the home and two guides on a phone (CPU 4x and 6x, 4G) and on a desktop, on the pixels of a real
// Chrome. node tools/site-perf.mjs [--label before] [--runs 3] [--pages home,cs2-ip-ban,cs2-region-picker] [--profiles m4,m6,desktop]
// Writes JSON to the folder given by --out (default: the temp folder) and prints one table. --stages also prints where the draw
// calls of a frame go, pass by pass (the target they are drawn to and its size).
// What it reads per run: first paint, largest paint, time until the 3D world says it is ready (worldready), long tasks (50 ms or
// more) and the blocking time they add, JS and total bytes as gzip (what GitHub Pages sends), the frame time and the CPU time the
// page spends in each requestAnimationFrame, the draw calls per frame, and the same again while scrolling.
// It also reads the machine's CPU load before each run: with other work running (Codex, another build) the wall-clock numbers
// are not comparable; the draw calls and the JS bytes are. Needs Playwright (PLAYWRIGHT=<folder>, the repo or the global npm).
import { createServer } from 'node:http';
import { gzipSync } from 'node:zlib';
import { spawnSync } from 'node:child_process';
import { cpus, tmpdir } from 'node:os';
import { existsSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const SITE = resolve(arg('site', join(ROOT, 'site')));
const SHOTS = join(ROOT, 'docs', 'screenshots');
const BASE = '/cs2-ip-changer';
const LABEL = arg('label', 'run'), RUNS = +arg('runs', 3);
const PAGES = arg('pages', 'home,cs2-ip-ban,cs2-region-picker').split(',');
const PROFILES = {
  m4: { name: 'phone, CPU 4x, 4G', cpu: 4, phone: true },
  m6: { name: 'phone, CPU 6x, 4G', cpu: 6, phone: true },
  desktop: { name: 'desktop 1440x900', cpu: 1, phone: false },
};
const WANT = arg('profiles', 'm4,m6,desktop').split(',');
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.woff2': 'font/woff2', '.json': 'application/json', '.txt': 'text/plain' };
const GZ = new Set(['.html', '.js', '.css', '.svg', '.json', '.txt']);

async function findPlaywright() {
  const where = [process.env.PLAYWRIGHT, ROOT];
  const g = spawnSync('npm root -g', { encoding: 'utf8', shell: true });
  if (g.status === 0 && g.stdout.trim()) where.push(g.stdout.trim(), join(g.stdout.trim(), '@playwright', 'cli'));
  for (const w of where.filter(Boolean)) {
    try { const m = await import(pathToFileURL(createRequire(join(resolve(w), 'x.js')).resolve('playwright')).href); return m.default || m; } catch { /* not there */ }
  }
  throw new Error('no Playwright (PLAYWRIGHT=<folder with it>)');
}

// CPU load of the whole machine over one second, 0..100
async function load() {
  const t = () => cpus().reduce((a, c) => { for (const k in c.times) a.total += c.times[k]; a.idle += c.times.idle; return a; }, { total: 0, idle: 0 });
  const a = t(); await new Promise((r) => setTimeout(r, 1000)); const b = t();
  return Math.round(100 * (1 - (b.idle - a.idle) / (b.total - a.total)));
}

const gzCache = new Map();
function serve() {
  return createServer((q, s) => {
    const p = decodeURIComponent(q.url.split('?')[0]);
    if (!p.startsWith(BASE)) { s.writeHead(404); return s.end(); }
    let f = resolve(SITE, '.' + (p.slice(BASE.length) || '/'));
    if (existsSync(f) && statSync(f).isDirectory()) f = join(f, 'index.html');
    // like the Pages workflow: the screenshots sit in img/, and each also as .webp (here the png stands in for it)
    if (!existsSync(f) && p.startsWith(BASE + '/img/')) {
      const name = p.slice((BASE + '/img/').length);
      for (const c of [join(SHOTS, name), join(SHOTS, name.replace(/\.webp$/, '.png'))]) if (existsSync(c)) { f = c; break; }
    }
    if (!existsSync(f) || (f !== SITE && !f.startsWith(SITE) && !f.startsWith(SHOTS))) { s.writeHead(404); return s.end(); }
    const ext = extname(f), h = { 'content-type': TYPES[ext] || 'application/octet-stream', 'cache-control': 'no-store' };
    let body = readFileSync(f);
    if (GZ.has(ext)) { if (!gzCache.has(f)) gzCache.set(f, gzipSync(body, { level: 6 })); body = gzCache.get(f); h['content-encoding'] = 'gzip'; }
    s.writeHead(200, h); s.end(body);
  });
}

// run in the page before anything: long tasks, paints, the cost of each animation frame, draw calls
const INIT = `(() => {
  const P = window.__perf = { lt: [], lcp: 0, fcp: 0, frames: [], ready: 0, draws: 0, cb: [], on: false };
  try { new PerformanceObserver((l) => { for (const e of l.getEntries()) P.lt.push([Math.round(e.startTime), Math.round(e.duration)]); }).observe({ type: 'longtask', buffered: true }); } catch (e) {}
  try { new PerformanceObserver((l) => { for (const e of l.getEntries()) P.lcp = Math.round(e.startTime); }).observe({ type: 'largest-contentful-paint', buffered: true }); } catch (e) {}
  try { new PerformanceObserver((l) => { for (const e of l.getEntries()) if (e.name === 'first-contentful-paint') P.fcp = Math.round(e.startTime); }).observe({ type: 'paint', buffered: true }); } catch (e) {}
  const raf = window.requestAnimationFrame.bind(window);
  let lastT = -1;   // several callbacks share one frame (GSAP, Lenis, the world): one frame per timestamp
  window.requestAnimationFrame = (cb) => raf((t) => { const a = performance.now(); try { cb(t); } finally { if (P.on) { P.cb.push(performance.now() - a); if (t !== lastT) P.frames.push(t); } lastT = t; } });
  // where each draw call goes (--stages): the target it is bound to (the screen, or the n-th framebuffer the page used) and the
  // viewport's size tell the passes apart: the mirror's own render, the shadow map, the scene, each step of the bloom
  const ids = new WeakMap(); let nFb = 0; P.st = {};
  for (const C of [window.WebGL2RenderingContext, window.WebGLRenderingContext]) {
    if (!C) continue;
    for (const n of ['drawElements', 'drawArrays', 'drawElementsInstanced', 'drawArraysInstanced', 'drawRangeElements']) {
      const o = C.prototype[n]; if (!o) continue;
      C.prototype[n] = function () { if (P.on) { P.draws++; const k = (this.__pfT || 'screen') + ' ' + (this.__pfV || '?'); P.st[k] = (P.st[k] || 0) + 1; } return o.apply(this, arguments); };
    }
    const bind = C.prototype.bindFramebuffer, view = C.prototype.viewport;
    // 0x8D40 FRAMEBUFFER and 0x8CA9 DRAW_FRAMEBUFFER are what a draw writes to; READ_FRAMEBUFFER (a blit's source) is not
    C.prototype.bindFramebuffer = function (t, fb) { if (t === 0x8D40 || t === 0x8CA9) { if (fb && !ids.has(fb)) ids.set(fb, ++nFb); this.__pfT = fb ? 'target ' + ids.get(fb) : 'screen'; } return bind.apply(this, arguments); };
    C.prototype.viewport = function (x, y, w, h) { this.__pfV = w + 'x' + h; return view.apply(this, arguments); };
  }
  document.addEventListener('worldready', () => { if (!P.ready) P.ready = Math.round(performance.now()); }, true);
})();`;

const stat = (a) => {
  if (!a.length) return { n: 0, mean: 0, p95: 0, max: 0 };
  const s = [...a].sort((x, y) => x - y);
  return { n: a.length, mean: +(a.reduce((x, y) => x + y, 0) / a.length).toFixed(2), p95: +s[Math.min(s.length - 1, Math.floor(s.length * 0.95))].toFixed(2), max: +s[s.length - 1].toFixed(2) };
};
const med = (a) => { const s = a.filter((x) => x != null).sort((x, y) => x - y); return s.length ? s[Math.floor(s.length / 2)] : null; };

async function window_(page, ms, action) {
  await page.evaluate(() => { const P = window.__perf; P.frames = []; P.cb = []; P.draws = 0; P.st = {}; P.ltMark = P.lt.length; P.on = true; });
  if (action) await action(); else await page.waitForTimeout(ms);
  return page.evaluate(() => {
    const P = window.__perf; P.on = false;
    const d = []; for (let i = 1; i < P.frames.length; i++) d.push(P.frames[i] - P.frames[i - 1]);
    const lt = P.lt.slice(P.ltMark);
    return { dt: d, cb: P.cb, frames: P.frames.length, draws: P.draws, st: P.st, lt: lt.map((x) => x[1]) };
  });
}

async function one(pw, browser, origin, slug, prof, dev) {
  const url = origin + (slug === 'home' ? '' : slug + '/');
  const opts = prof.phone ? { ...dev } : { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 };
  const ctx = await browser.newContext(opts);
  await ctx.addInitScript(INIT);
  const page = await ctx.newPage();
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Network.enable');
  // 4G: 9 Mbit/s down, 4.5 up, 80 ms round trip (the phone profiles); the desktop has none
  if (prof.phone) await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 80, downloadThroughput: 9e6 / 8, uploadThroughput: 4.5e6 / 8 });
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: prof.cpu });
  await cdp.send('Performance.enable');
  // the main thread's own CPU time (ThreadTime), which does not stretch with the emulated slowdown or with other work on the machine
  const thread = async () => (await cdp.send('Performance.getMetrics')).metrics.find((m) => m.name === 'ThreadTime').value * 1000;
  const bytes = { js: 0, css: 0, img: 0, font: 0, html: 0, other: 0, total: 0 };
  cdp.on('Network.loadingFinished', () => {});
  const reqs = new Map();
  cdp.on('Network.responseReceived', (e) => reqs.set(e.requestId, /\.m?js(\?|$)/.test(e.response.url) ? 'Script' : e.type));
  cdp.on('Network.loadingFinished', (e) => {
    const t = reqs.get(e.requestId); const k = t === 'Script' ? 'js' : t === 'Stylesheet' ? 'css' : t === 'Image' ? 'img' : t === 'Font' ? 'font' : t === 'Document' ? 'html' : 'other';
    bytes[k] += e.encodedDataLength; bytes.total += e.encodedDataLength;
  });
  const t0 = Date.now();
  await page.goto(url, { waitUntil: 'load', timeout: 120000 });
  const loadMs = Date.now() - t0;
  // the world says it is ready (or never, on a page without one) within 20 s of the clock of the emulated CPU
  await page.waitForFunction(() => window.__perf.ready || document.documentElement.classList.contains('no-gl'), null, { timeout: 40000 }).catch(() => {});
  await page.waitForTimeout(1500);
  const threadReady = await thread();
  const head = await page.evaluate(() => ({ fcp: window.__perf.fcp, lcp: window.__perf.lcp, ready: window.__perf.ready, lt: window.__perf.lt.slice(), renderer: (() => { try { const c = document.createElement('canvas').getContext('webgl'); const e = c.getExtension('WEBGL_debug_renderer_info'); return c.getParameter(e.UNMASKED_RENDERER_WEBGL); } catch (e) { return '?'; } })() }));
  const t0i = await thread();
  const idle = await window_(page, 4000);
  const idleThread = ((await thread()) - t0i) / Math.max(1, idle.frames);
  const t0s = await thread();
  const scroll = await window_(page, 0, async () => {
    for (let i = 0; i < 24; i++) { await page.mouse.move(300, 400); await page.mouse.wheel(0, 260); await page.waitForTimeout(190); }
  });
  const scrollThread = ((await thread()) - t0s) / Math.max(1, scroll.frames);
  await ctx.close();
  const tbt = (a) => a.reduce((x, d) => x + Math.max(0, d - 50), 0);
  const ltAll = head.lt.map((x) => x[1]);
  // draw calls per frame of each pass, the biggest first
  const stages = (w) => Object.entries(w.st || {}).map(([k, n]) => [k, w.frames ? +(n / w.frames).toFixed(1) : 0]).sort((a, b) => b[1] - a[1]);
  return {
    idleStages: stages(idle), scrollStages: stages(scroll),
    threadReady: Math.round(threadReady), idleThread: +idleThread.toFixed(2), scrollThread: +scrollThread.toFixed(2),
    loadMs, fcp: head.fcp, lcp: head.lcp, ready: head.ready, renderer: head.renderer,
    longTasks: ltAll.length, tbt: Math.round(tbt(ltAll)), longMax: Math.max(0, ...ltAll),
    kb: Object.fromEntries(Object.entries(bytes).map(([k, v]) => [k, Math.round(v / 1024)])),
    idle: { fps: +(idle.frames / 4).toFixed(1), frame: stat(idle.dt), cpu: stat(idle.cb), drawsPerFrame: idle.frames ? +(idle.draws / idle.frames).toFixed(1) : 0, lt: idle.lt.length },
    scroll: { fps: +(scroll.frames / 4.6).toFixed(1), frame: stat(scroll.dt), cpu: stat(scroll.cb), drawsPerFrame: scroll.frames ? +(scroll.draws / scroll.frames).toFixed(1) : 0, lt: scroll.lt.length, tbt: Math.round(tbt(scroll.lt)) },
  };
}

const pw = await findPlaywright();
const server = serve(); await new Promise((r) => server.listen(0, '127.0.0.1', r));
const origin = `http://127.0.0.1:${server.address().port}${BASE}/`;
let browser = null;
for (const o of [{ channel: 'chrome' }, {}]) { try { browser = await pw.chromium.launch({ ...o, args: ['--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] }); break; } catch (e) { /* next */ } }
if (!browser) throw new Error('Chromium did not start');
const dev = pw.devices['Pixel 7'] || pw.devices['Pixel 5'];
const out = { label: LABEL, when: new Date().toISOString(), runs: RUNS, results: [] };
for (const pk of WANT) {
  const prof = PROFILES[pk];
  for (const slug of PAGES) {
    const rs = [], loads = [];
    for (let i = 0; i < RUNS; i++) { loads.push(await load()); rs.push(await one(pw, browser, origin, slug, prof, dev)); }
    const m = (f) => med(rs.map(f));
    const row = {
      profile: pk, page: slug, cpuLoad: loads, renderer: rs[0].renderer,
      fcp: m((r) => r.fcp), lcp: m((r) => r.lcp), ready: m((r) => r.ready), longTasks: m((r) => r.longTasks), tbt: m((r) => r.tbt), longMax: m((r) => r.longMax),
      kb: rs[0].kb, threadReady: m((r) => r.threadReady), idleThread: m((r) => r.idleThread), scrollThread: m((r) => r.scrollThread),
      idleFps: m((r) => r.idle.fps), idleFrameP95: m((r) => r.idle.frame.p95), idleCpuMean: m((r) => r.idle.cpu.mean), idleCpuP95: m((r) => r.idle.cpu.p95), idleDraws: m((r) => r.idle.drawsPerFrame),
      scrollFps: m((r) => r.scroll.fps), scrollFrameP95: m((r) => r.scroll.frame.p95), scrollCpuMean: m((r) => r.scroll.cpu.mean), scrollCpuP95: m((r) => r.scroll.cpu.p95), scrollDraws: m((r) => r.scroll.drawsPerFrame), scrollLongTasks: m((r) => r.scroll.lt), scrollTbt: m((r) => r.scroll.tbt),
    };
    row.idleStages = rs[0].idleStages; row.scrollStages = rs[0].scrollStages;   // of the first run: the passes do not vary between runs
    out.results.push(row);
    if (process.argv.includes('--stages')) for (const [k, n] of row.idleStages) console.log(`        ${String(n).padStart(6)} draws/frame  ${k}`);
    console.log(`${pk.padEnd(7)} ${slug.padEnd(20)} load ${loads.join('/')}%  FCP ${row.fcp}  LCP ${row.lcp}  ready ${row.ready}  TBT ${row.tbt}  longest ${row.longMax}  JS ${row.kb.js}K total ${row.kb.total}K CPU-to-ready ${row.threadReady}ms | CPU/frame idle ${row.idleThread}ms scroll ${row.scrollThread}ms | idle ${row.idleFps}fps p95 ${row.idleFrameP95}ms cpu ${row.idleCpuMean}/${row.idleCpuP95}ms draws ${row.idleDraws} | scroll ${row.scrollFps}fps p95 ${row.scrollFrameP95}ms cpu ${row.scrollCpuMean}/${row.scrollCpuP95}ms draws ${row.scrollDraws} lt ${row.scrollLongTasks}`);
  }
}
await browser.close(); server.close();
const file = join(arg('out', tmpdir()), `site-perf-${LABEL}.json`);
writeFileSync(file, JSON.stringify(out, null, 1)); console.log('->', file);
