// 27 Nov. 2026, audit of what 1.0.0 had online. A stable release (no suffix in site/release.json) is not called a beta
// anywhere on the site or in the README, and the VirusTotal sentence reads right with any number of engines, 1 included.
// node --test tools/site-estavel.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = (f) => readFileSync(join(ROOT, f), 'utf8').replace(/\r\n/g, '\n');
const walk = (d) => readdirSync(join(ROOT, d)).flatMap((n) => { const p = `${d}/${n}`; return statSync(join(ROOT, p)).isDirectory() ? walk(p) : [p]; });
const pages = walk('site').filter((f) => f.endsWith('.html'));
// what a person reads: no comments, scripts, styles or tags
const text = (h) => h.replace(/<!--[\s\S]*?-->/g, '').replace(/<(script|style)\b[\s\S]*?<\/\1>/g, '').replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ');
const release = JSON.parse(read('site/release.json'));
const stable = !release.version.includes('-');

test('a stable release is not called a beta on any page of the site', () => {
  if (!stable) return;
  assert.ok(pages.length >= 80, `only ${pages.length} pages found`);
  const hits = pages.flatMap((f) => [...text(read(f)).matchAll(/.{0,40}(?<![\w.-])(?:beta|бета)(?![\w.]).{0,30}/gi)].map((m) => `${f}: ${m[0]}`));
  assert.deepEqual(hits.slice(0, 8), [], `${hits.length} places still say beta`);
});

test('a stable release is not called a beta in the README', () => {
  if (stable) assert.ok(!/this is a beta/i.test(read('README.md')));
});

test('the VirusTotal sentence does not depend on how many engines flag the release', () => {
  // "1 of 67 flag it" and the Turkish suffixes made for 0 of 70 ("1'ı", "67'te") read wrong with another number
  // ("Why some engines flag it", a README heading with no number, is right)
  const wrong = /\d+ of \d+[^\n]{0,160}?\bflag it\b|<\/em> engines flag it|\d(?:<[^>]*>)*'ı işaretliyor|\d+'te \d/;
  for (const f of [...pages, 'README.md', 'site/llms.txt', 'site/assets/guides/src/guides.js']) assert.doesNotMatch(read(f).replace(/<!--[\s\S]*?-->/g, ''), wrong, f);
  const { flagged, engines } = release.virustotal;
  assert.match(text(read('site/index.html')), new RegExp(`VirusTotal: flagged by ${flagged} of ${engines} engines`));
  assert.ok(read('README.md').includes(`flagged by [${flagged} of ${engines} engines]`), 'README > Safety');
});

test('the release notes of the current version carry the VirusTotal line', () => {
  const notes = read(`docs/release-${release.version}.md`);
  assert.ok(notes.includes(`https://www.virustotal.com/gui/file/${release.sha256}/detection`), 'docs/RELEASING.md step 4');
});
