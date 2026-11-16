// Builds the site's two 3D bundles from their sources and puts the MIT notices back:
//   site/assets/js/scenes.min.js        the home's world        (site/assets/src/scenes.js, which imports world.js)
//   site/assets/guides/world.min.js     the guides' and the 404's world: the same world.js plus site/assets/guides/src/guides.js
// node tools/site-build.mjs [--vendor <folder with node_modules>] [--out <site folder>]
// The vendor folder needs three@0.186.1 and esbuild (npm i three@0.186.1 esbuild in any folder; THREE_VENDOR=<folder> works too).
// The result matches the files in the repo apart from the end of lines, so a change to world.js can be built and tested here.
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
const VENDOR = resolve(arg('vendor', process.env.THREE_VENDOR || join(ROOT, 'node_modules')));
const OUT = resolve(arg('out', join(ROOT, 'site')));
const esbuild = (await import(pathToFileURL(createRequire(join(VENDOR, 'x.js')).resolve('esbuild')).href)).default;
const NEED = ['opts.extend && opts.extend({', 'if (__gx.tick) __gx.tick(dt, time, s, p);', '!lb.tall &&', 'if (!opts.plainScreen) patch(', 'if (opts.ios)'];
const common = { bundle: true, minify: true, format: 'iife', target: 'es2020', legalComments: 'none', nodePaths: [VENDOR], logLevel: 'warning' };

const world = readFileSync(join(ROOT, 'site/assets/src/world.js'), 'utf8');
for (const n of NEED) if (!world.includes(n)) throw new Error('site/assets/src/world.js: the guides need this hook: ' + n);

await esbuild.build({ ...common, entryPoints: [join(ROOT, 'site/assets/src/scenes.js')], outfile: join(OUT, 'assets/js/scenes.min.js') });
await esbuild.build({ ...common, entryPoints: [join(ROOT, 'site/assets/guides/src/guides.js')], outfile: join(OUT, 'assets/guides/world.min.js') });
console.log('scenes.min.js and world.min.js built in', OUT);
if (OUT === join(ROOT, 'site')) spawnSync(process.execPath, [join(ROOT, 'tools/site-legal.mjs')], { stdio: 'inherit' });
else console.log('(the MIT notices are put back by tools/site-legal.mjs, which works on the repo\'s site folder)');
