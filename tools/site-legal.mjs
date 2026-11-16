// The MIT notices of the libraries inside the scripts the site serves. esbuild builds scenes.min.js and world.min.js with
// --legal-comments=none (three.js leaves a note in every file and they add up), and Lenis comes minified with no header. The MIT
// License asks for the copyright line and the permission notice in every copy, so each file starts with them again.
//   node tools/site-legal.mjs           puts the notice at the start of each file that lacks it (run it after a build)
//   node tools/site-legal.mjs --check   changes nothing; fails when a file lacks it
// The same texts are in THIRD-PARTY-NOTICES at the root of the repo, and site/assets/THIRD-PARTY.txt next to the scripts.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const MIT = `Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.`;
const THREE = `/*! three.js r186, https://github.com/mrdoob/three.js. MIT License. Copyright © 2010-2026 three.js authors. ${MIT} */\n`;
const LENIS = `/*! Lenis 1.3.26, https://github.com/darkroomengineering/lenis. MIT License. Copyright (c) 2024 darkroom.engineering. ${MIT} */\n`;
export const FILES = [
  ['site/assets/js/scenes.min.js', THREE],
  ['site/assets/guides/world.min.js', THREE],
  ['site/assets/js/lenis.min.js', LENIS],
];

const check = process.argv.includes('--check');
let bad = 0;
for (const [f, banner] of FILES) {
  const p = join(ROOT, f), s = readFileSync(p, 'utf8');
  if (s.startsWith(banner)) continue;
  bad++;
  if (check) { console.error(`${f}: the MIT notice is missing`); continue; }
  writeFileSync(p, banner + s.replace(/^\/\*![\s\S]*?\*\/\n?/, ''));
  console.log(`${f}: notice added`);
}
if (check && bad) process.exit(1);
