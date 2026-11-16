// The 3D world's work per frame (15 Nov. 2026, session 4b): three.js worked out the program of a material again and again, on
// a phone that was most of what a frame cost. node --test tools/world-3d-perf.test.mjs
// Measured on the home (CPU 4x, Chrome, an unminified build counting why setProgram looked a program up again): every second,
// 1464 times because the wet floor's reflection is drawn from inside the main render (a nested render has its own light state),
// 1342 times because an InstancedMesh and a plain Mesh shared one material, and 1098 times because three.js draws a see-through
// two-sided material twice and sets needsUpdate each time. After the fixes: none of the three. tools/site-perf.mjs measures the rest;
// the pictures are the same (tools/site-compare.mjs: 8 pixels in a million, none of them over 40 of 255).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

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
