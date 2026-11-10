// The 3D world's USB cable and plugs, measured in the built scene (session 5, 9 Nov. 2026): the contact points are shared and
// nothing goes through anything, plugged in, while the cable is pulled, and pulled out. The text tests (world-3d.test.mjs) cannot
// see this; this one reads the real geometry in Chrome.
//   node tools/world-3d-contacts.mjs [site folder]   (default site/; needs the lab preview on 127.0.0.1:7947 for the app's images)
// Playwright from C:/dev/puredeploy-site-v2 (or PLAYWRIGHT=<folder with it>). Exit code 1 on any failure, with the list.
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
const ROOT = path.resolve(import.meta.dirname, '..');
const siteDir = path.resolve(process.argv[2] || path.join(ROOT, 'site'));
const { chromium } = createRequire(path.join(process.env.PLAYWRIGHT || 'C:/dev/puredeploy-site-v2', 'package.json'))('playwright');
const tokens = /\/\* tokens:[\s\S]*?\/\* \/tokens \*\//.exec(fs.readFileSync(path.join(siteDir, 'index.html'), 'utf8'))[0];
const HARNESS = `<!doctype html><html><head><meta charset="utf-8"><style>:root{${tokens}}html,body{margin:0;height:100%}canvas{width:100%;height:100%;display:block}</style></head>
<body><canvas id="c"></canvas><script src="assets/js/scenes.min.js"></script><script>
var api=WorldScene.mount(document.getElementById('c'),{still:true,debug:1});api.set({p:1});
document.getElementById('c').addEventListener('worldready',function(){window.__ready=1});</script></body></html>`;

const browser = await chromium.launch({ channel: 'chrome' });
let report;
try {
  const page = await browser.newPage({ viewport: { width: 800, height: 600 } });
  await page.route('**/*', r => {
    const u = new URL(r.request().url());
    if (u.hostname !== '127.0.0.1') return r.abort();
    const rel = u.pathname.replace('/cs2-ip-changer/', '');
    if (rel === '__contacts.html') return r.fulfill({ body: HARNESS, contentType: 'text/html' });
    const f = path.join(siteDir, rel);
    if (fs.existsSync(f) && fs.statSync(f).isFile()) return r.fulfill({ path: f });
    return r.continue();
  });
  await page.goto('http://127.0.0.1:7947/cs2-ip-changer/__contacts.html');
  await page.waitForFunction(() => window.__ready === 1 && window.__wd && window.__wd.contact, null, { timeout: 60000 });
  report = await page.evaluate(() => {
    const K = window.__wd.contact, fails = [], notes = {};
    const V = K.PORT.constructor, EPS = 0.002;
    const verts = (mesh) => { mesh.updateMatrixWorld(true); const p = mesh.geometry.attributes.position, out = []; for (let i = 0; i < p.count; i++) out.push(new V().fromBufferAttribute(p, i).applyMatrix4(mesh.matrixWorld)); return out; };
    const boxOf = (mesh) => { mesh.geometry.computeBoundingBox(); mesh.updateMatrixWorld(true); return { mesh, bb: mesh.geometry.boundingBox, inv: mesh.matrixWorld.clone().invert() }; };
    const inside = (b, w, eps = EPS) => { const l = w.clone().applyMatrix4(b.inv), bb = b.bb; return l.x > bb.min.x + eps && l.x < bb.max.x - eps && l.y > bb.min.y + eps && l.y < bb.max.y - eps && l.z > bb.min.z + eps && l.z < bb.max.z - eps; };
    const solids = { plate: boxOf(K.phParts.plate), rest: boxOf(K.phParts.rest), body: boxOf(K.phParts.body), ...Object.fromEntries(K.phParts.hooks.map((h, i) => ['hook' + i, boxOf(h)])) };
    const bodyInv = K.phParts.body.parent.matrixWorld.clone().invert(); // the phone's tilt frame: the port is at its origin
    const inPort = (w) => { const l = w.clone().applyMatrix4(bodyInv); return Math.abs(l.x) < 0.052 && Math.abs(l.z) < 0.018; };
    const attach = (pl) => pl.at.clone().applyMatrix4(pl.g.matrixWorld);
    let worstGap = 0, minPlugY = 9, minTailBottom = 9;
    for (let i = 0; i <= 40; i++) {
      const c = i / 40; K.place(c);
      const tail = K.tail(), tm = K.tailMesh;
      // contact points: J where the cable leaves the mat run, the plug's strain relief where it ends
      const g1 = tail.getPoint(0).distanceTo(K.J), g2 = tail.getPoint(1).distanceTo(attach(K.plugPhone));
      worstGap = Math.max(worstGap, g1, g2);
      if (g1 > 1e-4 || g2 > 1e-4) fails.push(`c=${c}: cable and plug apart (${g1.toFixed(4)}, ${g2.toFixed(4)})`);
      // the plug: its boot and relief in nothing; its metal only in the port; never under the mat
      for (const [n, part] of K.plugPhone.parts.entries()) for (const w of verts(part)) {
        minPlugY = Math.min(minPlugY, w.y);
        if (w.y < K.MAT_Y - EPS) { fails.push(`c=${c}: plug part ${n} under the mat (y=${w.y.toFixed(3)})`); break; }
        let hit = null;
        for (const [sn, b] of Object.entries(solids)) if (inside(b, w) && !(n === 0 && sn === 'body' && inPort(w))) { hit = sn; break; }
        if (hit) { fails.push(`c=${c}: plug part ${n} inside ${hit}`); break; }
      }
      // the cable's last stretch: on or above the mat, in no part of the stand or phone
      const tv = verts(tm); let tb = 9;
      for (const w of tv) {
        tb = Math.min(tb, w.y);
        const hit = Object.entries(solids).find(([, b]) => inside(b, w));
        if (hit) { fails.push(`c=${c}: cable inside ${hit[0]}`); break; }
      }
      minTailBottom = Math.min(minTailBottom, tb);
      if (tb < K.MAT_Y - 0.003) fails.push(`c=${c}: cable under the mat (y=${tb.toFixed(3)})`);
      if (c === 0) {
        const o = K.plugPhone.g.position.distanceTo(K.PORT); if (o > 1e-6) fails.push(`plugged in: the plug is ${o} from the port`);
        const yA = new V(0, 1, 0).applyQuaternion(K.plugPhone.g.quaternion), yP = new V(0, 1, 0).transformDirection(K.phParts.body.parent.matrixWorld);
        const al = yA.dot(yP); notes.axisPlugged = al; if (al < 0.99999) fails.push(`plugged in: the plug is off the port's axis (dot ${al})`);
      }
      if (c === 1) {
        let lo = 9; for (const part of K.plugPhone.parts) for (const w of verts(part)) lo = Math.min(lo, w.y);
        notes.plugOnMat = lo - K.MAT_Y; if (Math.abs(lo - K.MAT_Y) > 0.003) fails.push(`pulled out: the plug ${lo > K.MAT_Y ? 'floats' : 'sinks'} (${(lo - K.MAT_Y).toFixed(4)})`);
        // tip out of the port, a visible gap
        let near = 9; for (const w of verts(K.plugPhone.parts[0])) near = Math.min(near, w.distanceTo(K.PORT)); notes.tipFromPort = near;
      }
    }
    K.place(0);
    // the PC's end: the cable leaves its plug's relief; the plug's metal goes into the tower's face
    const gPC = K.cableMain.getPoint(0).distanceTo(attach(K.plugPC)); notes.gapPC = gPC; if (gPC > 1e-4) fails.push(`PC end: cable and plug apart (${gPC})`);
    const bootPC = verts(K.plugPC.parts[1]); const face = Math.min(...bootPC.map(w => w.x)) - K.USB_PC.x; notes.pcBootToWall = face;
    if (face < 0 || face > 0.006) fails.push(`PC end: the boot is ${face.toFixed(4)} from the tower's face`);
    // the cable's run on the mat: its underside on the cloth, not in it and not over it (from where it reaches the mat to J)
    const L = K.cableMain.getLength(); let lo = 9, hi = -9;
    for (let i = 0; i <= 200; i++) { const s = i / 200; const q = K.cableMain.getPointAt(s); if (q.x < -6.3 + 1e-3 && s < 0.3) continue; if (s * L < 1.4) continue; if (q.y - K.CR < lo) notes.matLowAt = q.toArray().map(v => +v.toFixed(2)); lo = Math.min(lo, q.y - K.CR); hi = Math.max(hi, q.y - K.CR); }
    notes.matRun = [lo - K.MAT_Y, hi - K.MAT_Y];
    // and nowhere, from the PC down to the mat, does it dip into the cloth (taken at its full radius: on the safe side)
    let all = 9; for (let i = 0; i <= 600; i++) all = Math.min(all, K.cableMain.getPointAt(i / 600).y - K.CR);
    notes.mainLowest = all - K.MAT_Y; if (all < K.MAT_Y - 0.003) fails.push(`main cable: dips ${(K.MAT_Y - all).toFixed(4)} into the mat`);
    if (lo < K.MAT_Y - 0.004 || hi > K.MAT_Y + 0.01) fails.push(`mat run: underside from ${(lo - K.MAT_Y).toFixed(4)} to ${(hi - K.MAT_Y).toFixed(4)} off the mat`);
    // the stand's rest meets its plate
    const restLo = Math.min(...verts(K.phParts.rest).map(w => w.y)); notes.restToPlate = restLo - K.PLATE_TOP;
    if (restLo > K.PLATE_TOP + 0.002 || restLo < K.PLATE_TOP - 0.012) fails.push(`stand: the rest ends ${(restLo - K.PLATE_TOP).toFixed(4)} from the plate's top`);
    return { fails, notes: { ...notes, worstGap, minPlugY, minTailBottom } };
  });
  await page.close();
} finally { await browser.close(); }
console.log(JSON.stringify(report.notes, null, 1));
if (report.fails.length) { console.log(`FAIL ${report.fails.length}:\n` + [...new Set(report.fails)].slice(0, 40).join('\n')); process.exit(1); }
console.log('contacts OK: 41 states of the pull, both plugs, the mat run and the stand');
