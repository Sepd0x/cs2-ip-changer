// CS2 IP Changer guides and 404: the home's own world (site/assets/src/world.js, proposal A's route with the studio models),
// one shot of it per page, with a short story and a few props that explain that page's subject.
// Built by publish/site-guias-2026-11-05/build.mjs, which adds the guide hooks (opts.extend, the tick) to the home's file.
//
// window.GuideWorld.mount(canvas, { scene, still, img, vt, frame }) -> { play(), pause(), resume(), resize(), destroy(), still } or null
//   scene: phone | iphone | ban | hide | safe | accounts | configs | profiles | regions | lost
//   still: reduced motion: the story's last state, drawn once, no loops
//   img:   the folder of the app's screenshots (…/img/); vt: { flagged, engines } of the latest release
//   frame(W, H) -> { box: [l, t, r, b] }: the part of the canvas the picture fits in, labels included
// Colours only come from the app's tokens (the world reads them from CSS).
import '../../src/world.js';
import {
  Group, Mesh, Points, BoxGeometry, PlaneGeometry, RingGeometry, CylinderGeometry, TubeGeometry, BufferGeometry, BufferAttribute,
  CatmullRomCurve3, QuadraticBezierCurve3, CubicBezierCurve3, Vector3, MeshStandardMaterial, MeshBasicMaterial, CanvasTexture,
  SRGBColorSpace, DoubleSide, InstancedMesh, Object3D, Color,
} from 'three';

const V3 = (x, y, z) => new Vector3(x, y, z);
const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
const smooth = (a, b, v) => { const t = clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); };
const lerp = (a, b, t) => a + (b - a) * t;
const IP_OLD = '198.51.100.23', IP_NEW = '192.0.2.140';

// the label's look comes from the world (.wd-lab); a guide's labels say what its picture shows
const lab = (at, html, o = {}) => ({ at, html, beats: [], tall: true, ...o });
const T = (b, s, sm) => `<b>${b}</b>${s ? `<span>${s}</span>` : ''}${sm ? `<small>${sm}</small>` : ''}`;

// the world's own parts every page may hide, found once in the built scene
function findParts(c) {
  const server = c.scene.children.find(o => o.isGroup && o.children[0] && Math.abs(o.children[0].position.x - c.SX) < 0.01 && Math.abs(o.children[0].position.z - c.SZ) < 0.01);
  const arcMesh = c.scene.children.find(o => o.isMesh && o.material === c.mArc);
  const shadows = c.scene.children.filter(o => o.isMesh && Math.abs(o.position.x - c.SX) < 0.01 && Math.abs(o.position.z - c.SZ) < 0.01 && o.position.y < 0.01);
  return { server, arcMesh, shadows };
}
function routeLike(c, curve, extra) {
  // a leg of the route in the route's own material (a copy, so it can be lit on its own)
  const m = c.mArc.clone();
  for (const k in extra) m.uniforms[k].value = extra[k];
  const mesh = new Mesh(new TubeGeometry(curve, 160, 0.07, 10), m); c.scene.add(mesh);
  return { m, mesh, curve };
}
function only(LAB, keep) { for (const k in LAB) if (!keep.includes(k)) LAB[k].beats = []; }
// shots near the PC see only the cable's end and the first part of the air leg, which are all one purple on the
// home's route; there the visible stretch carries the whole line of three colours (6 Nov.: two guides had a purple line)
function threeColours(c) { c.mCable.uniforms.uS.value = -0.6; c.mCable.uniforms.uE.value = 0.12; c.mAir.uniforms.uS.value = 0.12; c.mAir.uniforms.uE.value = 3.2; }

// ---------------------------------------------------------------- the pages
const PAGES = {
  // Change IP with your phone: the route draws itself from the PC to the server; home keeps the browser, Discord, downloads
  phone: {
    small: ['server'],
    p: 0, end: 3.8,
    shots: {
      wide: { pos: [-19, 12.5, 24], tgt: [3.5, 3.2, -4], fov: 30 },
      tall: { pos: [-12, 5, 31], tgt: [3, 4.2, -4], fov: 44 },
    },
    build(c, X) {
      only(c.LAB, ['pc', 'phone', 'mast', 'server', 'cable', 'router']);
      for (const k of ['cable', 'router']) c.LAB[k].beats = [0];
      c.LAB.cable.html = '<b>USB cable</b><span>USB tethering</span>';
      c.LAB.phone.html = '<b>Phone</b><span data-k="ph">mobile data on</span>';
      for (const k of ['pc', 'phone', 'mast', 'server']) c.LAB[k].tall = true;
    },
    tick(c, X, t) {
      const rev = lerp(0, 1.02, smooth(0.2, 3.2, t));
      for (const m of [c.mCable, c.mAir, c.mArc]) m.uniforms.uReveal.value = rev;
      const at = { pc: 0, cable: 0.12, router: 0.05, phone: 0.3, mast: 0.58, server: 0.98 };
      for (const k in at) X.show[k] = rev > at[k] ? 1 : 0;
    },
  },
  // CS2 with an iPhone: Personal Hotspot; Airplane Mode on and off on the iPhone, the server then sees another carrier IP
  iphone: {
    small: ['phone', 'server'],
    // lit: the route is already drawn when the picture comes in; the story draws it once, after Airplane Mode (it drew twice)
    p: 4, end: 6.2, iphone: true, lit: true,
    shots: {
      wide: { pos: [-6.5, 4.2, 12.5], tgt: [4.5, 3.6, -3.5], fov: 40 },
      tall: { pos: [-7, 5.5, 22], tgt: [4.5, 4.2, -4], fov: 52 },
    },
    keys: (c, K) => [K(c.PHONE_TOP, 90, 140, 20), K(c.mastTop, 96, 40, 0, 10), K(c.serverTop, 110, 10, 0, 180), K(V3(c.SX + 2.95, 0, c.SZ + 0.75), 0, 8, 8), K(c.PORT, 0, 190, 110, 10)],
    build(c) {
      only(c.LAB, ['phone', 'mast', 'server']);
      c.LAB.phone.beats = [4]; c.LAB.phone.html = '<b>iPhone</b><span data-k="ph">Personal Hotspot on</span>';
      c.LAB.phone.at = c.PORT.clone().add(V3(0, -0.06, 0.1)); c.LAB.phone.dn = 1; c.LAB.phone.tall = true; // under the iPhone, so its screen (where Airplane Mode is) stays in view
    },
    sub: t => lerp(0.3, 1, smooth(1.2, 5.2, t)),
    tick(c, X, t, s, sub) {
      c.leds.uniforms.uBan.value = 0;
      // on a phone there is no room under the iPhone: its label goes on the empty floor to its right, never over its screen
      if (!X.ph0) X.ph0 = c.LAB.phone.at.clone();
      const smallPic = c.renderer.domElement.clientWidth < 761;
      c.LAB.phone.at.copy(X.ph0).add(smallPic ? V3(1.1, 0.05, 0.5) : V3(0, 0, 0)); c.LAB.phone.dn = smallPic ? 0 : 1;
      const off = sub >= 0.4 && sub < 0.52, drawing = sub >= 0.52 && sub < 0.62;
      X.txt('ph', off ? 'Airplane Mode on' : sub >= 0.52 && sub < 0.7 ? 'Airplane Mode off again' : 'Personal Hotspot on');
      X.txt('mast', off ? 'no connection from the iPhone' : sub >= 0.62 ? 'usually hands out a new IP' : '4G / 5G');
      X.txt('srv', off ? 'No connection' : drawing ? 'getting a new IP' : `sees <em>${sub >= 0.62 ? IP_NEW : IP_OLD}</em>`);
      X.txt('srvn', off ? 'Airplane Mode is on' : sub >= 0.62 ? 'example IP, a new one' : 'example IP, your carrier\'s');
    },
  },
  // CS2 IP ban: the world's own ban beat, played once: kicked, mobile data off and on, a new IP, back with the next account
  ban: {
    small: ['server'],
    p: 4, end: 6.8, lit: true,
    shots: {},
    build(c) { only(c.LAB, ['mast', 'server']); c.LAB.mast.tall = c.LAB.server.tall = true; },
    sub: t => lerp(0, 1, smooth(0.9, 6.2, t)),
    // the world's own ban beat says it all (kicked, data off and on, new IP, back on the server)
  },
  // Hide your IP: the home route carries the browser and stops at Windows Firewall for CS2; the server only sees the carrier
  hide: {
    small: ['gate', 'router'],
    p: 1, end: 2.6,
    shots: {
      wide: { pos: [-17, 14.5, 16], tgt: [0.5, 1.2, -4], fov: 34 },
      tall: { pos: [-15, 23, 25], tgt: [0.5, 0.8, -4], fov: 52 },
    },
    keys: (c, K) => [K(c.routerPos.clone().add(V3(0, 1.4, 0)), 110, 20, 0, 200), K(c.gatePos.clone().add(V3(0, 1.7, 0)), 110, 200, 0, 20), K(c.mastTop, 96, 40, 0, 10), K(c.serverTop, 110, 230, 0, 10), K(V3(c.SX + 2.95, 0, c.SZ + 0.75), 0, 8, 8), K(c.PORT, 0, 0, 10)],
    build(c) {
      only(c.LAB, ['router', 'gate', 'mast', 'server']);
      c.LAB.router.html = T('Home IP', '203.0.•••.•••', 'never sent to the server'); c.LAB.router.beats = [1]; c.LAB.router.dn = 1; c.LAB.router.r = 1;
      c.LAB.gate.html = T('Windows Firewall', 'CS2 and your servers', 'blocked at home'); c.LAB.gate.beats = [1];
      for (const k of ['mast', 'server']) c.LAB[k].beats = [1];
      // the server's label to its right, where the shot keeps room: on its left the dashed leg from the phone goes up (6 Nov.)
      c.LAB.server.r = 0; c.LAB.server.dn = 0;
      for (const k of ['router', 'gate', 'server']) c.LAB[k].tall = true;
    },
    tick(c, X, t) {
      const hot = smooth(0.3, 1.6, t);
      c.hatch.uniforms.uHot.value = hot; c.tryMat.uniforms.uHot.value = hot; c.gateFrameMat.emissiveIntensity = 0.9 * hot; c.Lbad.intensity = 5 * hot;
      c.gate.visible = true; c.gate.scale.setScalar(Math.max(0.001, smooth(0, 0.5, hot)));
      X.show.gate = t > 1.0 ? 1 : 0;
    },
  },
  // Is it safe?: public source, the GitHub Actions build with its attestation, the exe and its SHA-256, VirusTotal; cs2.exe apart
  safe: {
    small: ['vt', 'exe'],
    p: 1, end: 4.6,
    shots: {
      wide: { pos: [-10, 6.6, 13.5], tgt: [-7.2, 1.4, 1.2], fov: 42 },
      tall: { pos: [-11.5, 8.5, 17], tgt: [-8.4, 1.6, 1.4], fov: 54 },
    },
    keys: (c, K, X) => [K(V3(-15.8, 0, 5.4), 0, 0, 10, 10), K(V3(-15.4, 1.1, 4.6), 0, 0, 90, 20), K(V3(-12.6, 2.1, 5.2), 96, 180, 0, 0), K(V3(-10.2, 1.1, 3.9), 0, 190, 90, 0), K(V3(-11, 4.4, -1.2), 96, 160, 0, 0), K(V3(-2.2, 1.4, 1.0), 96, 0, 0, 190), K(V3(-1.4, 0, 1.7), 0, 8, 8)],
    build(c, X) {
      only(c.LAB, []);
      const edge = (col, k = 1) => new MeshStandardMaterial({ color: c.C.surface2, metalness: 0.4, roughness: 0.5, emissive: col, emissiveIntensity: 0.35 * k });
      // the source: a stack of pages
      const src = new Group(); src.position.set(-15.4, 0, 4.6); src.rotation.y = 0.3; c.scene.add(src);
      for (let i = 0; i < 6; i++) { const pg = c.rbox(1.5, 0.05, 1.05, 0.02, i === 5 ? edge(c.C.top1, 1.2) : c.M.metalDark); pg.position.y = 0.05 + i * 0.09; pg.rotation.y = (i % 2 ? 0.03 : -0.02); src.add(pg); }
      // the build: a gate the code passes through, with the attestation as a ring (its seal)
      const build = new Group(); build.position.set(-12.6, 0, 5.2); build.rotation.y = 0.5; c.scene.add(build);
      for (const x of [-0.8, 0.8]) { const p = c.rbox(0.12, 1.9, 0.12, 0.03, c.M.metal); p.position.set(x, 0.95, 0); build.add(p); }
      { const t2 = c.rbox(1.72, 0.12, 0.12, 0.03, c.M.metal); t2.position.set(0, 1.9, 0); build.add(t2); }
      const seal = new Mesh(new RingGeometry(0.3, 0.36, 64), new MeshBasicMaterial({ color: c.C.top2, transparent: true, opacity: 0.25, side: DoubleSide, toneMapped: false }));
      seal.position.set(0, 1.0, 0); build.add(seal);
      // the exe, with its .sha256 tag
      const exe = c.rbox(0.7, 0.95, 0.1, 0.05, edge(c.C.top3)); exe.position.set(-10.2, 0.5, 3.9); exe.rotation.y = 0.55; c.scene.add(exe);
      const tag = new Mesh(new PlaneGeometry(0.5, 0.07), new MeshBasicMaterial({ color: c.C.top3, toneMapped: false })); tag.position.set(0, -0.25, 0.056); exe.add(tag);
      // the path from the source to your PC, in the route's own colours
      const chain = new CatmullRomCurve3([V3(-14.7, 0.6, 4.75), V3(-13.6, 0.1, 5.2), V3(-12.6, 1.0, 5.2), V3(-11.3, 0.1, 4.6), V3(-10.2, 0.5, 3.95), V3(-9.0, 0.08, 2.7), V3(-8.2, 0.35, 1.6)], false, 'centripetal');
      X.chain = routeLike(c, chain, { uS: 0, uE: 1, uReveal: 0 });
      X.seal = seal;
      // cs2.exe: its own sealed box on the desk, and no line goes into it
      const game = c.rbox(1.25, 1.25, 1.25, 0.08, c.M.metalDark); game.position.set(-2.2, 0.68, 1.0); game.rotation.y = -0.4; game.castShadow = true; c.scene.add(game);
      { const lid = new Mesh(new PlaneGeometry(0.62, 0.62), new MeshBasicMaterial({ color: c.C.good, transparent: true, opacity: 0.12, toneMapped: false })); lid.scale.setScalar(1.45); lid.position.set(0, 0, 0.63); game.add(lid); }
      // the VirusTotal wall: one cell per engine, scanned once; a flagged one would be red
      const vt = X.vt, n = vt.engines, cols = Math.ceil(n / 4);
      const wall = new Group(); wall.position.set(-11, 3.1, -1.2); wall.rotation.y = 0.5; c.scene.add(wall);
      const back = c.rbox(cols * 0.26 + 0.3, 4 * 0.26 + 0.3, 0.06, 0.04, c.M.metalDark); back.position.z = -0.05; wall.add(back);
      const cells = new InstancedMesh(new PlaneGeometry(0.19, 0.19), new MeshBasicMaterial({ toneMapped: false }), n), o = new Object3D();
      for (let i = 0; i < n; i++) { o.position.set((i % cols - (cols - 1) / 2) * 0.26, (1.5 - Math.floor(i / cols)) * 0.26, 0); o.updateMatrix(); cells.setMatrixAt(i, o.matrix); cells.setColorAt(i, c.C.surface3); }
      wall.add(cells); X.cells = { mesh: cells, n, bad: vt.flagged, col: new Color() };
      const L = c.LAB;
      L.src = lab(V3(-15.4, 0.7, 4.6), T('Public source', 'on GitHub'), { beats: [1], dn: 1 });
      L.build = lab(V3(-12.6, 2.05, 5.2), T('GitHub Actions', 'builds it, with an attestation'), { beats: [1] });
      L.exe = lab(V3(-10.2, 1.05, 3.9), T('CS2IPChanger.exe', 'and its .sha256 file'), { beats: [1], dn: 1 });
      L.vt = lab(V3(-11, 3.9, -1.2), T('VirusTotal', `<em>${vt.flagged} of ${vt.engines}</em> engines flag it`, 'the latest release'), { beats: [1] });
      L.game = lab(V3(-2.2, 1.35, 1.0), T('cs2.exe', 'never touched', 'not its process, memory or program files'), { beats: [1], r: 1 });
    },
    tick(c, X, t) {
      X.chain.m.uniforms.uReveal.value = lerp(0, 1.02, smooth(0.2, 2.2, t));
      X.chain.m.uniforms.uTime.value = c.mArc.uniforms.uTime.value;
      X.seal.material.opacity = 0.25 + 0.75 * smooth(0.9, 1.3, t);
      const V = X.cells, sweep = smooth(2.2, 3.8, t) * V.n;
      if (V.last !== sweep) { V.last = sweep; for (let i = 0; i < V.n; i++) { const bad = i < V.bad; V.col.copy(c.C.surface3).lerp(bad ? c.C.bad : c.C.good, clamp(sweep - i) * (bad ? 1 : 0.38)); V.mesh.setColorAt(i, V.col); } V.mesh.instanceColor.needsUpdate = true; }
      const at = { src: 0.2, game: 0.4, build: 0.9, exe: 1.6, vt: 3.7 };
      for (const k in at) X.show[k] = t > at[k] ? 1 : 0;
    },
  },
  // Steam account switcher: the real Accounts page on the monitor; the account banned on this server is skipped, the next logs in
  accounts: {
    small: ['next', 'skip'],
    p: 1, end: 3.2, screen: 'accounts',
    shots: {
      wide: { pos: [0.4, 2.2, 7.4], tgt: [-2.0, 1.2, 1.4], fov: 40 },
      tall: { pos: [0.6, 2.8, 10], tgt: [-2.0, 1.2, 1.4], fov: 50 },
    },
    keys: (c, K) => [K(c.monW(-1.71, 1.39)), K(c.monW(1.71, 3.61), 96, 0, 0, 0), K(V3(-3.3, 1.95, 2.1), 96, 190, 0, 0), K(V3(0.9, 0, 0.4), 0, 8, 8), K(V3(-4.0, 0, 2.4), 0, 0, 10)],
    build(c, X) {
      threeColours(c);
      only(c.LAB, ['phone']); c.LAB.phone.beats = [1]; // the phone in the corner of the shot, named
      const face = (lit) => new MeshStandardMaterial({ color: c.C.surface3, metalness: 0.2, roughness: 0.6, emissive: lit, emissiveIntensity: 0 });
      X.cards = [];
      for (let i = 0; i < 6; i++) {
        const g = new Group(); g.position.set(-3.3 + i * 0.82, 0.05, 2.1 - i * 0.34); g.rotation.y = -0.45; c.scene.add(g);
        const card = c.rbox(1.3, 1.8, 0.07, 0.07, c.M.metalDark); card.position.y = 0.95; card.castShadow = true; g.add(card);
        const av = new Mesh(new PlaneGeometry(0.46, 0.46), face(i === 0 ? c.C.bad : c.C.accent)); av.position.set(-0.3, 1.4, 0.04); g.add(av);
        const line = new Mesh(new PlaneGeometry(0.85, 0.06), new MeshBasicMaterial({ color: c.C.lineStrong })); line.position.set(0.05, 0.85, 0.04); g.add(line);
        const foot = c.rbox(0.8, 0.07, 0.34, 0.02, c.M.metal); foot.position.set(0, 0.035, 0); g.add(foot);
        X.cards.push({ g, card, av, y: 0.05 });
      }
      c.LAB.mon = lab(c.monW(0.2, 3.75), T('Accounts', 'the real page of the app'), { beats: [1] });
      c.LAB.skip = lab(V3(-3.3, 1.9, 2.1), T('Banned on this server', 'skipped'), { beats: [1], hot: 1, r: 1 });
      c.LAB.next = lab(V3(-2.48, 1.9, 1.76), T('Next account', 'logs in, no password typed'), { beats: [1] });
    },
    tick(c, X, t) {
      const skip = smooth(0.5, 0.9, t), next = smooth(1.4, 2.0, t);
      const a = X.cards[0], b = X.cards[1];
      a.av.material.emissiveIntensity = 0.9 * skip; a.g.position.y = a.y - 0.0 * skip; a.g.rotation.z = -0.06 * skip;
      b.av.material.emissiveIntensity = 0.9 * next; b.g.position.y = b.y + 0.45 * next;
      c.LAB.next.at.y = 1.9 + 0.45 * next;
      X.show.skip = t > 0.7 ? 1 : 0; X.show.next = t > 1.8 ? 1 : 0;
    },
  },
  // Copy CS2 settings: the real Configs page; binds, crosshair, settings, video and HUD go from one account's folder to another
  configs: {
    small: ['to', 'from'],
    p: 1, end: 4.2, screen: 'configs',
    shots: {
      wide: { pos: [-3, 7.6, 11.2], tgt: [-3, 0.2, 4.7], fov: 34 },
      tall: { pos: [-3, 7.5, 13], tgt: [-3, 0.5, 4.2], fov: 50 },
    },
    keys: (c, K) => [K(V3(-5.8, 0.2, 5.3), 0, 0, 90, 10), K(V3(-0.2, 0.2, 5.3), 0, 10, 90, 0), K(V3(-3.0, 2.0, 4.5), 8), K(V3(-4.6, 0.2, 3.7), 96, 0, 0, 0)],
    build(c, X) {
      only(c.LAB, ['phone']); c.LAB.phone.beats = [1]; // the phone in the corner of the shot, named
      const folder = (x, z, col) => {
        const g = new Group(); g.position.set(x, 0.05, z); g.rotation.y = -0.25; c.scene.add(g);
        const base = c.rbox(2.3, 0.12, 1.6, 0.05, new MeshStandardMaterial({ color: c.C.surface2, metalness: 0.5, roughness: 0.5, emissive: col, emissiveIntensity: 0.22 })); base.position.y = 0.05; base.castShadow = true; g.add(base);
        const tab = c.rbox(0.5, 0.1, 0.18, 0.03, new MeshStandardMaterial({ color: c.C.surface3, emissive: col, emissiveIntensity: 0.6 })); tab.position.set(-0.7, 0.08, -0.86); g.add(tab);
        return g;
      };
      X.from = folder(-4.6, 4.5, c.C.top1); X.to = folder(-1.4, 4.5, c.C.top2);
      X.hop = new QuadraticBezierCurve3(V3(-4.6, 0.24, 4.5), V3(-3.0, 2.4, 4.5), V3(-1.4, 0.24, 4.5));
      const cols = [c.C.top1, c.C.top1.clone().lerp(c.C.top2, 0.5), c.C.top2, c.C.top2.clone().lerp(c.C.top3, 0.5), c.C.top3];
      X.tiles = cols.map(col => { const m = c.rbox(0.46, 0.05, 0.32, 0.02, new MeshStandardMaterial({ color: c.C.surface3, emissive: col, emissiveIntensity: 0.9 })); c.scene.add(m); return m; });
      c.LAB.mon = lab(c.monW(0.2, 3.75), T('Configs', 'the real page of the app'), { beats: [1] });
      c.LAB.from = lab(V3(-4.6, 0.26, 4.5), T('Main account', 'binds, crosshair, video'), { beats: [1], dn: 1 });
      c.LAB.to = lab(V3(-1.4, 0.26, 4.5), T('Alt account', 'the parts you ticked', 'a backup of each change'), { beats: [1], dn: 1, r: 1 });
    },
    tick(c, X, t) {
      X.tiles.forEach((m, i) => {
        const k = smooth(0.4 + i * 0.55, 1.15 + i * 0.55, t);
        // each part lands on its own place in the alt account's folder, so all five are seen at the end
        X.hop.getPoint(k, m.position); m.position.x += (i - 2) * 0.42 * k; m.position.z += ((i % 2) - 0.5) * 0.5 * k; m.position.y += 0.02 * i;
        m.rotation.y = -0.25 + Math.sin(k * Math.PI) * 0.6;
      });
      X.show.to = t > 3.3 ? 1 : 0;
    },
  },
  // Steam profile editor: the real Profiles page; one template goes onto the accounts you picked, one after another
  profiles: {
    small: ['run', 'tpl'],
    p: 1, end: 5.2, screen: 'profiles',
    shots: {
      wide: { pos: [0.6, 1.5, 7], tgt: [0.6, 1.2, 0.6], fov: 40 },
      tall: { pos: [0.6, 2, 9.5], tgt: [0.6, 1.2, 0.6], fov: 52 },
    },
    keys: (c, K) => [K(c.monW(-1.71, 1.39)), K(c.monW(1.71, 3.61), 96, 0, 0, 0), K(V3(-2.8, 1.5, 2.4), 96, 190, 0, 0), K(V3(-0.68, 0.06, 1.62), 0, 190, 90, 20), K(V3(0.6, 0, 1.0), 0, 8, 8)],
    build(c, X) {
      threeColours(c);
      only(c.LAB, ['phone']); c.LAB.phone.beats = [1]; // the phone in the corner of the shot, named
      const av = document.createElement('canvas'); av.width = av.height = 128;
      const g = av.getContext('2d'), css = getComputedStyle(document.documentElement), v = n => css.getPropertyValue(n).trim();
      const grd = g.createLinearGradient(0, 128, 128, 0); grd.addColorStop(0, v('--t-top1')); grd.addColorStop(0.5, v('--t-top2')); grd.addColorStop(1, v('--t-top3'));
      g.fillStyle = v('--t-surface2'); g.fillRect(0, 0, 128, 128);
      g.strokeStyle = grd; g.lineWidth = 7; g.beginPath(); g.arc(64, 50, 22, 0, Math.PI * 2); g.stroke(); g.beginPath(); g.arc(64, 128, 44, Math.PI, 0); g.stroke();
      const look = new CanvasTexture(av); look.colorSpace = SRGBColorSpace;
      X.plates = [];
      for (let i = 0; i < 6; i++) {
        const st = new Group(); st.position.set(-2.8 + i * 0.64, 0.05, 2.4 - i * 0.26); st.rotation.y = -0.15; c.scene.add(st);
        const leg = c.rbox(0.05, 0.7, 0.05, 0.02, c.M.metal); leg.position.y = 0.35; st.add(leg);
        const foot = c.rbox(0.36, 0.04, 0.24, 0.02, c.M.metal); foot.position.y = 0.02; st.add(foot);
        const p = new Group(); p.position.y = 1.0; st.add(p);
        const frame = c.rbox(0.62, 0.62, 0.05, 0.03, c.M.metalDark); frame.castShadow = true; p.add(frame);
        const blank = new Mesh(new PlaneGeometry(0.52, 0.52), new MeshBasicMaterial({ color: c.C.surface3 })); blank.position.z = 0.028; p.add(blank);
        const done = new Mesh(new PlaneGeometry(0.52, 0.52), new MeshBasicMaterial({ map: look, transparent: true, opacity: 0, toneMapped: false })); done.position.z = 0.03; p.add(done);
        X.plates.push({ p, done });
      }
      c.LAB.mon = lab(c.monW(0.2, 3.75), T('Profiles', 'the real page of the app'), { beats: [1] });
      c.LAB.tpl = lab(V3(-2.8, 1.38, 2.4), T('Template', 'name, picture, privacy'), { beats: [1], r: 1 });
      c.LAB.run = lab(V3(-0.68, 0.06, 1.62), T('Apply', 'one account after another'), { beats: [1], dn: 1 });
    },
    tick(c, X, t) {
      X.plates.forEach((q, i) => { const k = smooth(0.6 + i * 0.7, 0.95 + i * 0.7, t); q.done.material.opacity = k; q.p.rotation.y = Math.sin(k * Math.PI) * 0.9; });
      X.show.run = t > 1.1 ? 1 : 0;
    },
  },
  // CS2 region picker: Valve's relay sites on the horizon; the ones you turn off go dark, blocked for cs2.exe only
  regions: {
    small: ['off', 'on'],
    p: 0, end: 3.8, screen: 'regions',
    shots: {
      wide: { pos: [-9, 15, 19], tgt: [6.5, 1, -11], fov: 40 },
      tall: { pos: [-5, 25, 21], tgt: [6.5, 0, -11], fov: 54 },
    },
    keys: (c, K, X) => [...X.sites.map(s => K(s.top, 0, 8, 8, 8)), K(V3(-8.2, 0, 1.9), 0, 0, 10), K(c.mastTop, 8), K(c.monW(0.2, 3.75), 96, 190)],
    build(c, X) {
      only(c.LAB, ['pc', 'mast']);
      c.LAB.pc.html = T('Your PC', 'one firewall rule for cs2.exe'); c.LAB.pc.beats = [0]; c.LAB.pc.tall = true;
      c.LAB.mast.beats = [0];
      const P = findParts(c); P.server.visible = false; P.arcMesh.visible = false; P.shadows.forEach(o => (o.visible = false)); c.leds.visible = false;
      X.sites = []; const ON = [2, 3, 5];
      for (let i = 0; i < 8; i++) {
        const a = lerp(Math.PI * 1.06, Math.PI * 1.92, i / 7), x = 7 + 16.5 * Math.cos(a), z = -8 + 12.5 * Math.sin(a);
        const g = P.server.clone(); g.visible = true; g.scale.setScalar(0.42);
        g.position.set(x - c.SX * 0.42, 0, z - c.SZ * 0.42); // the copy is built around (SX, SZ): move its middle onto the site
        g.rotation.y = 0; c.scene.add(g);
        const top = V3(x, 2.2, z);
        const curve = new CubicBezierCurve3(c.mastTop.clone(), c.mastTop.clone().lerp(top, 0.33).add(V3(0, 4.5, 0)), c.mastTop.clone().lerp(top, 0.72).add(V3(0, 3, 0)), top);
        const leg = routeLike(c, curve, { uS: 0.58, uE: 1 });
        X.sites.push({ top, lit: ON.includes(i), g, leg, order: [0, 7, 1, 6, 4].indexOf(i) });
      }
      c.LAB.on = lab(X.sites[3].top.clone().add(V3(0, 0.15, 0)), T('Left on', 'CS2 can match here'), { beats: [0] });
      c.LAB.off = lab(X.sites[6].top.clone().add(V3(0, 0.15, 0)), T('Turned off', 'blocked for cs2.exe'), { beats: [0], r: 1, hot: 1 });
    },
    tick(c, X, t) {
      c.L3.intensity = 0;
      for (const s of X.sites) {
        const off = s.lit ? 0 : smooth(0.5 + s.order * 0.45, 0.8 + s.order * 0.45, t);
        const u = s.leg.m.uniforms; u.uOn.value = c.mArc.uniforms.uOn.value * (1 - off); u.uTime.value = c.mArc.uniforms.uTime.value; u.uReveal.value = c.mArc.uniforms.uReveal.value;
      }
      X.show.off = t > 2.4 ? 1 : 0; X.show.on = t > 2.8 ? 1 : 0;
    },
  },
  // 404: the route leaves the phone and the carrier and runs on into the fog: no server, no page
  lost: {
    small: ['end'],
    p: 0, end: 2.8,
    shots: {
      wide: { pos: [-10, 4.5, 13.5], tgt: [8, 4.6, -8], fov: 40 },
      tall: { pos: [-11, 5, 30], tgt: [5, 4.2, -5], fov: 46 },
    },
    // 10 Nov. (3D-007): on a wide page the picture has a short strip above the Guides card, and the room kept above the mast and
    // the route for two labels left the world a third of it, seen from far and above. There the camera comes down and nearer, and
    // the page's label goes to the right of the route's end, a little further along it (the strip has width to spare, not height)
    keys: (c, K, X, layout) => layout === 'wide'
      ? [K(V3(-8.2, 0, 1.9), 0, 0, 12), K(c.mastTop, 112, 40, 0, 96), K(c.mastBase, 0, 0, 10), K(X.lost.curve.getPoint(0.3), 14), K(X.lost.curve.getPointAt(0.56), 112, 226, 0, 16), K(c.PORT, 0, 0, 10)]
      : [K(V3(-8.2, 0, 1.9), 0, 0, 12), K(c.mastTop, 96, 40, 0, 10), K(c.mastBase, 0, 0, 10), K(X.lost.curve.getPoint(0.3), 8), K(X.lost.curve.getPointAt(0.5), 96, 10, 0, 190), K(c.PORT, 0, 0, 10)],
    build(c, X) {
      only(c.LAB, ['phone', 'mast']);
      c.LAB.mast.tall = true;
      const P = findParts(c); P.server.visible = false; P.arcMesh.visible = false; P.shadows.forEach(o => (o.visible = false)); c.leds.visible = false;
      const end = V3(46, 7, -32);
      X.lost = routeLike(c, new CubicBezierCurve3(c.mastTop.clone(), V3(12, 15, -8.5), V3(28, 13, -20), end), { uS: 0.58, uE: 1 });
      c.LAB.end = lab(X.lost.curve.getPointAt(0.56), T('This page', 'not found, no server here'), { beats: [0], r: 1, hot: 1, atTall: X.lost.curve.getPointAt(0.5), side: { wide: [0, 0], tall: [1, 0] } });
      // placed first, so the point of the page always gets its room
      for (const k of ['mast', 'phone']) { const L = c.LAB[k]; delete c.LAB[k]; c.LAB[k] = L; }
    },
    tick(c, X, t) {
      c.L3.intensity = 0;
      const u = X.lost.m.uniforms; u.uTime.value = c.mArc.uniforms.uTime.value;
      u.uDraw.value = lerp(0.05, 1, smooth(0.2, 2.2, t)); u.uOn.value = c.mArc.uniforms.uOn.value; u.uReveal.value = 1.02;
      X.show.end = t > 1.9 ? 1 : 0;
    },
  },
};

function mount(canvas, opts = {}) {
  const kind = PAGES[opts.scene] ? opts.scene : 'phone', G = PAGES[kind];
  // dev only: ?cam=x,y,z,tx,ty,tz,fov frames this page's shot (both layouts) without a build
  try { const q = new URLSearchParams(location.search).get('cam'); if (q) { const [x, y, z, tx, ty, tz, fov] = q.split(',').map(Number); G.shots = { wide: { pos: [x, y, z], tgt: [tx, ty, tz], fov }, tall: { pos: [x, y, z], tgt: [tx, ty, tz], fov } }; } } catch (e) { /* no query */ }
  const still = !!opts.still;
  const IMG = opts.img || new URL('img/', document.baseURI).href;
  const X = { show: {}, vt: opts.vt || { flagged: 0, engines: 68 } };
  let c = null, t0 = null, now = 0, done = still, wrapEl = null;
  const txtSet = {};
  X.txt = (k, html) => { const el = wrapEl && wrapEl.querySelector('.wd-lab>div [data-k="' + k + '"]'); if (el && el.innerHTML !== html) el.innerHTML = html; txtSet[k] = html; };
  let api = null;
  try {
    api = window.WorldScene && window.WorldScene.mount(canvas, {
      still, lit: !!G.lit, ios: !!G.iphone, plainScreen: !!G.screen, screen: IMG + (G.screen || 'connection') + '.webp',
      frame: (W, H) => (opts.frame ? opts.frame(W, H) : { box: [W * 0.04, H * 0.06, W * 0.96, H * 0.94] }),
      extend(ctx) {
        c = ctx;
        // the home's own places for its labels are for the home's shots; each page places its labels with r and dn
        for (const k in c.LAB) { delete c.LAB[k].side; delete c.LAB[k].one; }
        G.build && G.build(c, X);
        // the labels that carry the page's point are placed first, so they get the room
        { const all = Object.entries(c.LAB), first = G.small || [];
          for (const [k] of all) delete c.LAB[k];
          for (const [k, L] of all.filter(([k]) => first.includes(k)).sort((x, y) => first.indexOf(x[0]) - first.indexOf(y[0])).concat(all.filter(([k]) => !first.includes(k)))) c.LAB[k] = L; }
        // this page's shot, in both layouts, and the points (with their labels' room) that must stay in the picture
        // the room each point keeps for its label shrinks with the picture, so a narrow phone still gets a real shot
        const base = {};
        for (const layout of ['wide', 'tall']) {
          const sh = G.shots[layout], S = c.SHOTS[layout][G.p];
          if (sh) Object.assign(S, { pos: V3(...sh.pos), tgt: V3(...sh.tgt), fov: sh.fov });
          base[layout] = S.keys.map(q => ({ ...q }));
          if (G.keys) base[layout] = G.keys(c, c.K, X, layout);
        }
        X.pad = (w, h) => {
          const k = Math.min(w < 420 ? 0.3 : w < 761 ? 0.55 : 1, h && h < 300 ? 0.4 : 1); // a phone, or a phone on its side
          if (k === X.padK) return false; X.padK = k;
          for (const layout of ['wide', 'tall']) { const kk = layout === 'tall' ? k * 0.72 : k; c.SHOTS[layout][G.p].keys = base[layout].map(q => ({ ...q, t: q.t * kk, r: q.r * kk, b: q.b * kk, l: q.l * kk })); }
          return true;
        };
        X.pad(canvas.clientWidth || innerWidth, canvas.clientHeight);
        return {
          tick(dt, time, s, p) {
            if (X.pad(canvas.clientWidth, canvas.clientHeight)) setTimeout(() => api && api.resize(), 0);
            now = time;
            const t = done ? G.end : t0 === null ? 0 : Math.min(G.end, time - t0);
            const sub = G.sub ? G.sub(t) : null;
            if (G.tick) G.tick(c, X, t, s, sub);
            if (sub != null && api) api.set({ sub });
            // a label of this page shows when its part of the story has come (and never before the picture's first frame)
            // in a small picture (a phone) only the labels that carry this page's point, so they always have room
            const sm = canvas.clientWidth < 761 || canvas.clientHeight < 300;
            for (const k in c.LAB) {
              const L = c.LAB[k]; if (L.home === undefined) L.home = L.beats.slice();
              const on = L.home.length && (!(k in X.show) || X.show[k]) && (!sm || G.small.includes(k));
              L.beats = on ? (k in X.show || sm ? [G.p] : L.home) : [-9];
            }
            for (const k in txtSet) X.txt(k, txtSet[k]);
          },
        };
      },
    });
  } catch (e) { api = null; }
  if (!api) return null;
  wrapEl = canvas.nextElementSibling;
  api.set({ p: G.p, sub: G.sub ? G.sub(still ? G.end : 0) : 0 });
  // the story starts when the picture is first on screen; off screen and in a hidden tab the world stops drawing
  let vis = false;
  const io = new IntersectionObserver(es => {
    vis = es[es.length - 1].isIntersecting;
    if (vis && t0 === null && !done) t0 = now;
    api.hide(!vis || document.hidden);
  }, { threshold: 0.06 });
  io.observe(canvas);
  const onVis = () => api.hide(!vis || document.hidden);
  document.addEventListener('visibilitychange', onVis);
  return {
    still,
    play() { if (still) return; done = false; t0 = now; api.resume(); api.hide(!vis || document.hidden); },
    // paused, the picture shows how the story ends, so nothing it explains is missing
    pause() { done = true; api.pause(); },
    resume() { api.resume(); },
    resize: () => api.resize(),
    destroy() { io.disconnect(); document.removeEventListener('visibilitychange', onVis); api.destroy(); },
  };
}

window.GuideWorld = { mount };
