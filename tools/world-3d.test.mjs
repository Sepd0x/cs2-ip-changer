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
    for (const mark of ['RouteMirror', 'hzGlow', 'vSpW', 'smoothstep(4., 9., dist)']) assert.ok(built.includes(mark), `${name} is not built from the current world.js (missing ${mark})`);
  }
});
