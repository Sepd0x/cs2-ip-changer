// The 3D world's work per frame (15 Nov. 2026, session 4b): three.js worked out the program of a material again and again, on
// a phone that was most of what a frame cost. node --test tools/world-3d-perf.test.mjs
// Measured on the home (CPU 4x, Chrome, an unminified build counting why setProgram looked a program up again): every second,
// 1464 times because the wet floor's reflection is drawn from inside the main render (a nested render has its own light state),
// 1342 times because an InstancedMesh and a plain Mesh shared one material, and 1098 times because three.js draws a see-through
// two-sided material twice and sets needsUpdate each time. After the fixes: none of the three. tools/site-perf.mjs measures the rest;
// the pictures are the same (tools/site-compare.mjs: 8 pixels in a million, none of them over 40 of 255).
// 25 Nov. 2026 (1.0.0): a frame draws the scene three times (the picture, the second render the bloom is taken from, the floor's
// mirror), so every object left on its own costs up to three draw calls. The small instanced parts of each object, what the
// tower, the monitor and the keyboard share, the phone's stand, the plugs and the still props of three guides were joined: 286 to
// 221 calls a frame on the home on a PC, 275 to 212 on a phone, 402 to 257 on the region guide. The last test here counts the calls
// of a frame in a real Chrome, pass by pass, so they do not creep back. The pictures are the same but for a few lone pixels
// (thin edges that fall on the other side of a sample, a last digit of shading): measured picture by picture on the home, the
// 12 guides and the 404, with and without bloom (the session's notes, publish/plano-1-0-0/8-3d-juntar-malhas.md, are outside the repo).
import { test, after } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, extname, join, resolve, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const src = readFileSync(join(ROOT, 'site/assets/src/world.js'), 'utf8').replace(/\r\n/g, '\n');
const bundles = { 'scenes.min.js': readFileSync(join(ROOT, 'site/assets/js/scenes.min.js'), 'utf8'), 'world.min.js': readFileSync(join(ROOT, 'site/assets/guides/world.min.js'), 'utf8') };

test('the wet floor\'s reflection is drawn in renderAll, before the main render, and not from inside it', () => {
  const i = src.indexOf('function renderAll()'), j = src.indexOf('composer.render();', i), k = src.indexOf('mirror.onBeforeRender(renderer, scene, camera)', i);
  assert.ok(i > 0 && j > i && k > i && k < j, 'the floor is drawn by hand before composer.render()');
  assert.ok(src.includes('mirrorAt !== frameNo'), 'once a frame');
  assert.ok(src.includes('_fr.intersectsObject(mirror)'), 'only when the floor is in the picture, as three.js would have drawn it');
  assert.ok(src.includes('if (inBloom || mirrorAt === frameNo) return;'), 'the main render finds it done and draws it no more (and the bloom\'s render never draws it)');
});

test('an InstancedMesh never shares a material with a plain Mesh', () => {
  const i = src.indexOf('An InstancedMesh and a plain Mesh drawn with the same material');
  assert.ok(i > 0);
  const b = src.slice(i, i + 1600);
  assert.ok(b.includes('plain.has(o.material)') && b.includes('o.material = m;'), 'the instanced mesh takes its own copy');
  assert.ok(b.includes('m.onBeforeCompile = src.onBeforeCompile') && b.includes('m.customProgramCacheKey = src.customProgramCacheKey'), 'the copy keeps the shader hooks (clone() leaves them out)');
  assert.ok(b.includes('keep = new Set([gateFrameMat, beacon.material])'), 'what the frame changes by hand stays one material');
});

test('a see-through two-sided flat material is drawn in one pass', () => {
  assert.ok(src.includes('m.forceSinglePass = true'), 'forceSinglePass on the flat ones');
  assert.ok(/Math\.min\(s\.x, s\.y, s\.z\) <= big \* 1e-3/.test(src), 'only on flat shapes: on a closed shape the back faces must be drawn first');
  assert.ok(src.includes('if (!solid.has(m))') || src.includes('!solid.has(m)'), 'never on a material that is also on a solid shape');
  for (const [name, text] of Object.entries(bundles)) assert.ok(/forceSinglePass=!0/.test(text), `${name} is built from this source`);
});

test('the region guide\'s 8 servers, copies that never move, are one fused group', () => {
  const g = readFileSync(join(ROOT, 'site/assets/guides/src/guides.js'), 'utf8').replace(/\r\n/g, '\n');
  assert.ok(src.includes('gate, gateFrameMat, Lbad, L2, L3, leds, fuse, mastTop,'), 'the guides get fuse from the world');
  assert.ok(g.includes('const farm = new Group(); c.scene.add(farm);') && g.includes('farm.add(g);') && g.includes('c.fuse(farm);'), 'the copies go in a group that is fused (the region page drew about 585 calls a frame, the home 285)');
  assert.ok(!g.includes('g.rotation.y = 0; c.scene.add(g);'), 'a copy is no longer added to the scene alone');
});

test('Clouds (19 Nov.): drawn by the sky\'s own shader from one map made once, so no new object, draw call or pass', () => {
  const skyAt = src.indexOf('const sky = new Mesh(');
  assert.ok(skyAt > 0, 'the sky mesh');
  const sky = src.slice(skyAt, src.indexOf('scene.add(noMirror(sky))', skyAt));
  assert.match(sky, /uniform sampler2D uCloud/, 'the sky samples the cloud map');
  assert.equal((src.match(/const cloudMap = cvTex\(/g) || []).length, 1, 'one cloud map, made once');
  assert.ok(!/new (Mesh|Sprite|Points|InstancedMesh)\([^)]*cloud/i.test(src), 'no object of its own for the clouds');
  assert.match(src, /sky\.material\.uniforms\.uDrift\.value = time \* 0\.0004/, 'they drift slowly with the world\'s time');
  for (const [name, b] of Object.entries(bundles)) assert.ok(b.includes('uCloud'), `${name} is built from this world.js (tools/site-build.mjs)`);
});

test('Joined (25 Nov.): the small instanced parts, the desk, the phone and the plugs are fused, and the bloom\'s last quad is not drawn', () => {
  assert.ok(src.includes('function fuse(group, keep = [], also = [], into = group)'));
  assert.ok(src.includes('if (!o.isMesh || o.instanceColor || keep.includes(o)'), 'an instanced part is fused like any other (not one coloured per copy)');
  assert.ok(src.includes('rel.multiplyMatrices(base, im.fromArray(o.instanceMatrix.array, t * 16))'), 'each copy is written out where its own matrix puts it');
  assert.ok(src.includes('ne = nrm.getNormalMatrix(rel).elements'), 'normals are moved with the part, never worked out again from the joined shape');
  assert.ok(!/merged\.computeVertexNormals|mergeVertices/.test(src), 'no computeVertexNormals or mergeVertices on a fused mesh: a hard edge would turn soft');
  assert.ok(src.includes('fuse(pc, [keys], [], tower);'), 'the tower, the monitor and the keyboard share one mesh per material; the 74 keys stay instanced');
  assert.ok(src.includes('const asIs = into !== group && o.parent === into && !o.isInstancedMesh && o.matrix.equals(same);') && src.includes('if (asIs) { P.set(p.subarray(0, c * 3), v0 * 3); N.set(n.subarray(0, c * 3), v0 * 3); }'), 'written in the tower\'s space, the tower\'s own points copied as they are: it is drawn exactly as before');
  assert.ok(src.includes('fuse(phone);') && src.includes('for (const o of [plate, rest, body, ...phParts.hooks]) o.userData.stay = true;'), 'the phone and its stand; the parts the contact check measures stay in the tree');
  assert.ok(src.includes('boot.userData.stay = relief.userData.stay = true; fuse(g);'), 'each plug\'s boot and strain relief');
  assert.ok(src.includes('if (o.userData.stay) o.visible = false; else o.removeFromParent();'), 'a measured part is hidden, not drawn twice');
  assert.ok(src.includes('bloom.blendMaterial.visible = false;'), 'UnrealBloomPass adds its bloom back onto bloomIn, which nothing reads: that quad is not drawn');
  // what is never fused: glass, the screens, what glows and changes, the contact shadows, the route
  for (const own of ['const glass = new Mesh(new PlaneGeometry(TD - 0.24, TH - 0.24), M.glass)', 'mon.add(glows(screen))', 'tilt.add(glows(phScreen))', 'scene.add(glows(new Mesh(tube(cableMain, 210, mainR), mCable)));', 'const airTube = glows(new Mesh(', 'const arcTube = glows(new Mesh('])
    assert.ok(src.includes(own), 'still an object of its own: ' + own);
  assert.ok(src.includes('(o.material.transparent && !also.includes(o.material))'), 'a see-through part is only fused when its material is named');
  for (const [name, b] of Object.entries(bundles)) assert.ok(b.includes('blendMaterial.visible=!1') && b.includes('userData.stay'), `${name} is built from this world.js (tools/site-build.mjs)`);
  // the guides' own props that stand still: the pages and the gate of Is it safe?, four of the six account cards, the profile stands
  const g = readFileSync(join(ROOT, 'site/assets/guides/src/guides.js'), 'utf8').replace(/\r\n/g, '\n');
  for (const call of ['c.fuse(src);', 'c.fuse(build);', 'c.fuse(fixed);', 'c.fuse(stands, frames);']) assert.ok(g.includes(call), 'a guide\'s still props are one mesh per material: ' + call);
  assert.ok(g.includes('(i < 2 ? c.scene : fixed).add(g);'), 'the two account cards the story moves stay their own');
  assert.ok(g.includes('p.add(frame); frames.push(frame);'), 'the profile plates turn: their frames are kept out of the stands\' mesh');
});

// ---- the draw calls of one frame, counted on the WebGL calls of a real Chrome (as tools/site-perf.mjs --stages does) ----
// A frame is: the bloom's own render of the scene (first), the bloom's quads, the floor's mirror (the run right before the
// picture, on a target nothing else draws to), the picture (the big run at the canvas's own size), two more quads.
// With prefers-reduced-motion the world draws only when something changes, so the last frame it drew is the one counted.
// The ceilings are what 25 Nov. measured. A new object costs a call in up to three passes: when one is added on purpose, raise
// the ceiling by what it costs and say so in the commit; when a count rises with nothing added, something stopped being joined.
const CEILINGS = [
  { name: 'the home\'s cover on a PC (1440x900)', path: '', view: { width: 1440, height: 900 }, scale: 1, picture: 86, bloom: 82, mirror: 39, total: 221 },
  { name: 'the home\'s cover on a phone (412x839 at 2x)', path: '', view: { width: 412, height: 839 }, scale: 2, phone: true, picture: 83, bloom: 79, mirror: 36, total: 212 },
  { name: 'the region guide on a PC, the page with the most objects', path: 'cs2-region-picker/', view: { width: 1440, height: 900 }, scale: 1, picture: 98, bloom: 94, mirror: 51, total: 257 },
];
const COUNT = `(() => {
  const P = window.__dc = { ready: 0, frames: [], cur: null };
  document.addEventListener('worldready', () => { P.ready = 1; }, true);
  const raf = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = (cb) => raf((t) => { P.cur = []; try { cb(t); } finally { if (P.cur.length) { P.frames.push(P.cur); if (P.frames.length > 4) P.frames.shift(); } P.cur = null; } });
  const C = window.WebGL2RenderingContext; if (!C) return;
  const ids = new WeakMap(); let nFb = 0;
  for (const n of ['drawElements', 'drawArrays', 'drawElementsInstanced', 'drawArraysInstanced', 'drawRangeElements']) {
    const o = C.prototype[n];
    C.prototype[n] = function () { if (P.cur) { const k = (this.__dcT || 'screen') + ' ' + (this.__dcV || '?'), L = P.cur[P.cur.length - 1]; if (L && L[0] === k) L[1]++; else P.cur.push([k, 1]); } return o.apply(this, arguments); };
  }
  const bind = C.prototype.bindFramebuffer, view = C.prototype.viewport;
  C.prototype.bindFramebuffer = function (t, fb) { if (t === 0x8D40 || t === 0x8CA9) { if (fb && !ids.has(fb)) ids.set(fb, ++nFb); this.__dcT = fb ? 'target ' + ids.get(fb) : 'screen'; } return bind.apply(this, arguments); };
  C.prototype.viewport = function (x, y, w, h) { this.__dcV = w + 'x' + h; return view.apply(this, arguments); };
})();`;

let dcBrowser = null, dcServer = null;
after(async () => { await dcBrowser?.close(); dcServer?.close(); });
async function chrome() {
  const where = [process.env.PLAYWRIGHT, ROOT];
  const g = spawnSync('npm root -g', { encoding: 'utf8', shell: true });
  if (g.status === 0 && g.stdout.trim()) where.push(g.stdout.trim(), join(g.stdout.trim(), '@playwright', 'cli'));
  let pw = null;
  for (const w of where.filter(Boolean)) {
    try { const m = await import(pathToFileURL(createRequire(join(resolve(w), 'x.js')).resolve('playwright')).href); pw = m.default || m; break; } catch { /* not there */ }
  }
  if (!pw) return { why: 'no Playwright (PLAYWRIGHT=<folder with it>)' };
  let why = '';
  for (const o of [{ channel: 'chrome' }, {}]) {
    try { dcBrowser = await pw.chromium.launch({ ...o, args: ['--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] }); break; } catch (e) { why = `Chromium did not start: ${e.message.split('\n')[0]}`; }
  }
  if (!dcBrowser) return { why };
  const SITE = join(ROOT, 'site'), SHOTS = join(ROOT, 'docs', 'screenshots'), BASE = '/cs2-ip-changer';
  const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.woff2': 'font/woff2', '.json': 'application/json', '.txt': 'text/plain' };
  dcServer = createServer((q, s) => {
    const p = decodeURIComponent(q.url.split('?')[0]);
    if (!p.startsWith(BASE)) { s.writeHead(404); return s.end(); }
    let f = resolve(SITE, '.' + (p.slice(BASE.length) || '/'));
    if (f !== SITE && !f.startsWith(SITE + sep)) { s.writeHead(404); return s.end(); }
    if (existsSync(f) && statSync(f).isDirectory()) f = join(f, 'index.html');
    // like the Pages workflow: the app's captures sit in img/ (the png stands in for its webp)
    if (!existsSync(f) && p.startsWith(BASE + '/img/')) { const n = p.slice((BASE + '/img/').length); for (const c of [join(SHOTS, n), join(SHOTS, n.replace(/\.webp$/, '.png'))]) if (existsSync(c)) { f = c; break; } }
    if (!existsSync(f)) { s.writeHead(404); return s.end(); }
    s.writeHead(200, { 'content-type': TYPES[extname(f)] || 'application/octet-stream', 'cache-control': 'no-store' });
    s.end(readFileSync(f));
  });
  await new Promise((ok) => dcServer.listen(0, '127.0.0.1', ok));
  return { pw, origin: `http://127.0.0.1:${dcServer.address().port}${BASE}/` };
}

test('Draw calls (25 Nov.): a frame stays under its ceiling in each pass, on the home (PC and phone) and on the region guide', async (t) => {
  const { pw, origin, why } = await chrome();
  if (!pw) return t.skip(why);
  for (const c of CEILINGS) {
    const ctx = await dcBrowser.newContext({ ...(c.phone ? pw.devices['Pixel 7'] : {}), viewport: c.view, deviceScaleFactor: c.scale, reducedMotion: 'reduce', locale: 'en-US' });
    await ctx.addInitScript(COUNT);
    const page = await ctx.newPage();
    await page.goto(origin + c.path, { waitUntil: 'load', timeout: 120000 });
    await page.waitForFunction(() => window.__dc.ready || document.documentElement.classList.contains('no-gl'), null, { timeout: 90000 });
    if (await page.evaluate(() => document.documentElement.classList.contains('no-gl'))) { await ctx.close(); return t.skip('no WebGL in this browser: nothing is drawn'); }
    await page.waitForTimeout(1500);
    const got = await page.evaluate(() => {
      const cv = document.querySelector('canvas'), full = cv.width + 'x' + cv.height;
      const f = [...window.__dc.frames].reverse().find((x) => x.reduce((a, r) => a + r[1], 0) > 20); if (!f) return null;
      let main = -1; f.forEach((r, i) => { if (r[0].endsWith(' ' + full) && r[1] > 2 && (main < 0 || r[1] > f[main][1])) main = i; });
      const o = { bloom: f[0][1], mirror: 0, picture: main >= 0 ? f[main][1] : 0, total: f.reduce((a, r) => a + r[1], 0), runs: f.map((r) => r.join(' = ')) };
      if (main > 1) { const k = f[main - 1][0]; if (!f.some((r, i) => i !== main - 1 && r[0] === k)) o.mirror = f[main - 1][1]; }
      return o;
    });
    await ctx.close();
    assert.ok(got && got.picture > 0, `${c.name}: a frame was drawn and its passes were told apart`);
    t.diagnostic(`${c.name}: picture ${got.picture}, bloom's render ${got.bloom}, mirror ${got.mirror}, total ${got.total}`);
    for (const k of ['picture', 'bloom', 'mirror', 'total']) assert.ok(got[k] <= c[k], `${c.name}: ${k} draws ${got[k]} calls a frame, the ceiling is ${c[k]} (the frame: ${got.runs.join(', ')})`);
    assert.ok(got.total - got.picture - got.bloom - got.mirror <= 14, `${c.name}: the bloom and the last two passes are 14 quads, not ${got.total - got.picture - got.bloom - got.mirror}`);
  }
});
