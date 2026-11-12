// Folds the translators' work files (publish/loc/work/*.out.json: {"<English>": {"pt-BR","es","ru"}}) into src/Lang/<code>.json.
//   node tools/loc-merge.mjs            merge
//   node tools/loc-merge.mjs --prune    also drop keys the app no longer uses
import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { allKeys } from './loc-keys.mjs';
const ROOT = fileURLToPath(new URL('../', import.meta.url));
const langs = ['pt-BR', 'es', 'ru'];
const work = join(ROOT, 'publish/loc/work');
const tables = Object.fromEntries(langs.map(l => [l, JSON.parse(readFileSync(join(ROOT, `src/Lang/${l}.json`), 'utf8'))]));
let n = 0;
for (const f of readdirSync(work).filter(f => f.endsWith('.out.json'))) {
  const o = JSON.parse(readFileSync(join(work, f), 'utf8'));
  for (const [en, tr] of Object.entries(o)) for (const l of langs) if (tr[l]) { tables[l][en] = tr[l]; n++; }
}
const keep = process.argv.includes('--prune') ? new Set(allKeys()) : null;
for (const l of langs) {
  const t = tables[l], out = {};
  for (const k of Object.keys(t).sort()) if (!keep || keep.has(k)) out[k] = t[k];
  writeFileSync(join(ROOT, `src/Lang/${l}.json`), JSON.stringify(out, null, 1) + '\n');
}
console.log('merged', n, 'values');
