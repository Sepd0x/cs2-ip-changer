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
