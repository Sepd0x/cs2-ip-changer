// The home's 3D world (site/assets/src/world.js), what must hold after the 7 Nov. 2026 pass on materials, light and camera.
//   node --test tools/world-3d.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const src = readFileSync(join(ROOT, 'site/assets/src/world.js'), 'utf8');
const home = readFileSync(join(ROOT, 'site/assets/js/scenes.min.js'), 'utf8');
const guides = readFileSync(join(ROOT, 'site/assets/guides/world.min.js'), 'utf8');

test('the route keeps its glow: the same gain, the same bloom (the owner: never less glow)', () => {
  assert.match(src, /uGain: \{ value: 1\.25 \}/);
  assert.match(src, /uGain\.value = 1\.25 \+ 0\.9 \* s\.fresh/);
  assert.match(src, /new UnrealBloomPass\(new Vector2\(256, 256\), 0\.95, 0\.55, 0\.12\)/);
  assert.match(src, /float I = \(\.5 \+ 1\.9\*pulse\) \* \(\.3 \+ \.7\*core\) \* on \* uGain;/);
});

test('every visitor gets the studio reflections, not only when asked', () => {
  assert.match(src, /scene\.environment = envRT\.texture;/);
  assert.doesNotMatch(src, /if \(opts\.env\)/);
});

test('the bloom sees only what glows: no studio light, no key or rim on the metals in its render', () => {
  const i = src.indexOf('function renderAll()'), j = src.indexOf('renderer.setRenderTarget(bloomIn)', i);
  assert.ok(i > 0 && j > i);
  const before = src.slice(i, j);
  assert.match(before, /scene\.environmentIntensity = 0; key\.intensity = rim\.intensity = hemi\.intensity = 0;/);
  assert.doesNotMatch(before, /L[123]\.intensity|intensity \*=/,'the route lamps stay at full strength in the bloom (the mast head keeps its halo)');
});

test('no blinking red lights on the far masts: red means blocked or banned in this story', () => {
  assert.doesNotMatch(src, /avMat|avPts|aPh/);
});

test('reduced motion stays still: no clock without motion, cuts between shots, no sway or lens bump', () => {
  assert.match(src, /if \(!still\) time \+= dt;/);
  assert.match(src, /const e = calm \? Math\.round\(f\) : ease5\(f\);/);
  assert.match(src, /camera\.fov = lerp\(a\.fov, b\.fov, e\) \+ \(calm \|\| k >= 4 \? 0 :/);
  const sway = src.indexOf('a held camera, not a locked one'), calmIf = src.lastIndexOf('if (!calm) {', sway);
  assert.ok(calmIf > 0 && sway - calmIf < 600, 'the sway lives inside if (!calm)');
});

test('the shots are still met exactly, so the fitted frames and the labels do not move', () => {
  assert.match(src, /if \(e <= 0\) \{ camPos\.copy\(a\.pos\); camTgt\.copy\(a\.tgt\); return; \}/);
  assert.match(src, /if \(e >= 1\) \{ camPos\.copy\(b\.pos\); camTgt\.copy\(b\.tgt\); return; \}/);
});

test('the built scenes are this world.js: the home and the guides (and the 404) both', () => {
  for (const [name, built] of [['site/assets/js/scenes.min.js', home], ['site/assets/guides/world.min.js', guides]]) {
    for (const mark of ['RouteMirror', 'hzGlow', 'vSpW', '52.9829189']) assert.ok(built.includes(mark), `${name} is not built from the current world.js (missing ${mark})`);
  }
});

test('the USB cable and its plugs share their contact points (9 Nov.; measured by node tools/world-3d-contacts.mjs)', () => {
  assert.doesNotMatch(src, /uCut/, 'the pulled cable is not cut off in the shader any more: its last stretch moves with the plug');
  assert.match(src, /const qIn = qOf\(PH_X, PH_Y\);/, 'the phone plug sits on the port\'s own axes, not on the cable\'s slant');
  assert.match(src, /placePhonePlug\(s\.cut\);/);
  assert.match(src, /tailCur = tailTo\(pose\(plugPhone, plugPos, plugQ\)\);/, 'the last stretch is rebuilt from where the plug is');
  assert.match(src, /plugPhone\.g\.visible = tailMesh\.visible = phone\.visible;/, 'no plug in mid air when the phone hides');
});

test('OLED (6c, 3D-011): no sine hashes in the shaders, the dither last and never on black, no dot lattice on the floor', () => {
  for (const bad of ['fract(sin(dot(gl_FragCoord', 'float h2(vec2 p){ return fract(sin', 'float hs(float n){ return fract(sin', 'uDot*dotm'])
    assert.ok(!src.includes(bad), 'gone: ' + bad + ' (a sine hash turns into a grid on mobile GPUs; the 1 m dot lattice read as points on an OLED)');
  assert.ok(src.includes('sRGBTransferOETF( gl_FragColor ); { float n = fract(52.9829189'), 'the dither runs after the sRGB step, in the screen\'s own 8-bit steps');
  assert.ok(src.includes('smoothstep(.5/255., 4./255., max(gl_FragColor.r'), 'no dither on black: an OLED\'s black pixels stay off');
});

test('OLED (6c, 3D-010): the route\'s bloom is the same, only its two widest layers (the veil over the frame) are low', () => {
  assert.ok(src.includes('new UnrealBloomPass(new Vector2(256, 256), 0.95, 0.55, 0.12)'));
  assert.ok(src.includes('[1, 0.9, 0.55, 0.2, 0.05].forEach((k, i) => bloom.bloomTintColors[i].setScalar(k))'));
  assert.ok(src.includes('c.rgb *= (l + .002)/(l + .005);'), 'the OLED toe: darks down, the route (far brighter) kept');
});

test('the horizon (6c, 3D-012): the floor runs out to the town, no unlit ring of hills in between', () => {
  assert.ok(!src.includes('new RingGeometry(38, 112'));
  assert.ok(src.includes('const skyline = new Mesh(new CylinderGeometry(130, 130, 30'));
  assert.ok(src.includes('new PlaneGeometry(260, 260), new MeshLambertMaterial'), 'the floor reaches the town at 130 m in every direction');
});

test('the route stays neon (6c-bis): over 1 the brightest channel rolls off and the others keep their ratio, a fifth of the old clip kept', () => {
  assert.ok(src.includes('if (mx > .75) { float f = .75 + .25*(1. - exp(-(mx - .75)/.25)); c.rgb = mix(c.rgb*(f/mx), min(c.rgb, vec3(1.)), .2); }'));
  assert.ok(src.includes('new UnrealBloomPass(new Vector2(256, 256), 0.95, 0.55, 0.12)'), 'the bloom itself is the same');
});

// 10 Nov. 2026, the pass on the references (3D-005, 3D-007, 3D-008)
const code = src.split(/\r?\n/).map((l) => l.replace(/^\s*\/\/.*$/, '')); // whole-line comments out; a statement after // on a code line is still a comment
const live = (re) => code.some((l) => { const i = l.search(re); if (i < 0) return false; const c = l.indexOf('//'); return c < 0 || c > i; });

test('the key light stands where it was set: its position is code, not the tail of a comment (from 6b it shone straight down, no side lit, no shadow)', () => {
  assert.ok(live(/key\.position\.set\(-14, 26, 16\)/), 'key.position.set(-14, 26, 16) runs');
  assert.ok(live(/key\.target\.position\.set\(2, 0, -3\)/), 'key.target.position.set(2, 0, -3) runs');
  assert.ok(live(/key\.castShadow = false/), 'no shadow map: it cost a slow phone its first frame for shadows nobody saw; the contact shadows are baked');
});

test('what stands on the desk mat is not drawn into the floor\'s mirror (it shows nothing there), what glows on it still is', () => {
  assert.ok(src.includes('for (const g of [pc, phone, plugPC.g, plugPhone.g]) g.traverse(o => { if ((o.isMesh || o.isPoints) && !o.layers.isEnabled(REFL)) o.layers.set(FARL); });'));
  assert.ok(src.includes('aoCam.layers.enableAll();'), 'the baked contact shadows still see those parts');
});

test('the racks\' lights (3D-008): a row on every unit, each light with its own halo, little of them in the bloom, no sine hash', () => {
  assert.ok(src.includes('for (const b of [1, 3, 5, 7]) ledPts.push('), 'four drive lights on every unit, next to its power light');
  assert.ok(src.includes('float halo = exp('), 'the light draws its own tight halo');
  assert.ok(src.includes('leds.uniforms.uK.value = 0.16 + 0.84 * leds.uniforms.uBan.value;'), 'the bloom takes a sixth of them (the ban\'s red all of it)');
  assert.ok(!/fract\(sin\(/.test(src), 'no fract(sin()) anywhere: on mobile GPUs it turns into a grid (3D-011)');
});

test('the floor\'s mirror (3D-008): every object, not only what glows; nothing high up comes back; glows as strong as they were', () => {
  assert.ok(live(/mirror\.getReflectionCamera\(camera\)\.layers\.set\(0\)/), 'the mirror\'s camera sees every object');
  for (const o of ['sky', 'skyline', 'floor', 'dots', 'mirror', 'deskMat', 'stitch']) assert.ok(src.includes(`noMirror(${o})`), `${o} is not drawn into the mirror`);
  assert.ok(src.includes("const MIRROR_FADE = 'smoothstep(4.6, 9.5, ';"), 'in the mirror a thing fades out with its height');
  assert.ok(src.includes('if (cameraPosition.y < 0.) gl_FragColor.rgb = mix(gl_FragColor.rgb, fogColor, ${MIRROR_FADE}vFogY));'), 'lit things fade');
  assert.ok(src.includes('if (cameraPosition.y < 0.) gl_FragColor.rgb *= 1. - ${MIRROR_FADE}vWY);'), 'the route fades: mirrored only where it comes down');
  assert.ok(src.includes('uK: { value: 7 }') && src.includes('mix(uSolid*(.1 + 3.6*f), uK*(.35+.65*f), hot)'), 'what glows keeps its strength (uK 7)');
  assert.ok(src.includes('for (int j = -2; j <= 2; j++)'), 'five taps (it was seven)');
});

test('fewer draw calls (3D-008 pays for the mirror with them): the still parts of a thing that share a material are one mesh', () => {
  assert.ok(src.includes('function fuse(group, keep = [], also = [])'));
  for (const call of ['fuse(t);', 'fuse(mon);', 'fuse(s, [pad]);', 'fuse(s, [slab], [doorMat]);', 'fuse(r);']) assert.ok(src.includes(call), call);
  assert.ok(src.includes('if (!o.visible || q.userData.noBake) return;') || src.includes('if (!q.visible || q.userData.noBake) return;'), 'nothing that moves or hides is fused');
  const g = readFileSync(join(ROOT, 'site/assets/guides/src/guides.js'), 'utf8');
  assert.ok(g.includes('o.children[0].position.x - c.SX'), 'the guides still find the server by its first child, the slab, which fuse keeps');
});

test('labels (3D-005): a box stays off the objects\' outlines, chosen for the shot at rest, with a longer stem when that clears it', () => {
  assert.ok(src.includes('const OBJ = [') && src.includes('const hitHull = (q, h, m) =>') && src.includes('const hullOf = pts =>'));
  for (const k of ['tower', 'screen', 'phone', 'mast', 'site', 'racks', 'router']) assert.ok(src.includes(`{ k: '${k}', pts:`), `the ${k} has an outline`);
  assert.ok(src.includes('function restOf(i)') && src.includes('restCam(i, S);'), 'places are chosen with the fitted camera of the beat');
  assert.ok(src.includes("el.style.setProperty('--stem', stem + 'px')"), 'the stem\'s length reaches the CSS (it never did: on a phone the code counted 30 px and the CSS drew 44)');
  assert.ok(src.includes('if (s.side && s.bi !== bi) s.side = null;'), 'after a cut to another beat a shared label chooses again');
  assert.ok(src.includes('.wd-lab.wd-dn>div{transform:translateY(calc(100% + var(--stem,44px)*1.5 + 22px))}'), 'a box under its point follows the stem too');
});

test('the 404 on a wide page (3D-007): its own low, near shot and its own room for the labels', () => {
  const g = readFileSync(join(ROOT, 'site/assets/guides/src/guides.js'), 'utf8');
  assert.ok(g.includes('wide: { pos: [-10, 4.5, 13.5], tgt: [8, 4.6, -8], fov: 40 },'));
  assert.ok(g.includes("keys: (c, K, X, layout) => layout === 'wide'"), 'the points kept in the picture differ by layout');
  assert.ok(g.includes('if (G.keys) base[layout] = G.keys(c, c.K, X, layout);'));
  assert.ok(g.includes('tall: { pos: [-11, 5, 30], tgt: [5, 4.2, -5], fov: 46 },'), 'the phone\'s shot is as it was');
});

test('the phone\'s cover is seen from lower down (proposal A, chosen by the owner on 11 Nov.), with the same points kept in the picture', () => {
  assert.ok(src.includes('{ pos: V3(-13, 2.6, 19), tgt: V3(5, 4.6, -5), fov: 40, keys: [K(apex, 6), K(mastTop, 8), K(serverTop, 6), K(PORT, 0, 0, 10), ...towerPts.map(p => K(p, 0, 0, 6, 6)), K(V3(SX + 2.95, RH, SZ + 0.75), 0, 4)] },'));
  assert.ok(src.includes('{ pos: V3(-21, 10.5, 26), tgt: V3(3.5, 3.4, -4), fov: 30,'), 'the PC\'s cover is as it was');
});

test('the built scenes carry the 10 Nov. round, the home and the guides both', () => {
  for (const [name, built] of [['site/assets/js/scenes.min.js', home], ['site/assets/guides/world.min.js', guides]]) {
    for (const mark of ['smoothstep(4.6, 9.5,', 'uSolid', 'aRow', '--stem']) assert.ok(built.includes(mark), `${name} is not built from the current world.js (missing ${mark})`);
  }
});
