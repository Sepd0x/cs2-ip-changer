// The country names of Profiles > Country in the app's languages (audit 26 nov., X7: the list was always in English).
// .NET's RegionInfo only gives a country's name in its own language ("Deutschland"), so the names come from Node's ICU:
//   node tools/countries.mjs        writes src/Lang/countries.json ({ "pt-BR": { "DE": "Alemanha", ... }, "es": ..., "ru": ..., "tr": ... })
// The app embeds it as Lang.countries.json (src/ServerRouteTool.csproj takes Lang\*.json); a code missing here shows in English.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('../', import.meta.url));
const LANGS = ['pt-BR', 'es', 'ru', 'tr'];
const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
const codes = [...A].flatMap((a) => [...A].map((b) => a + b));
const en = new Intl.DisplayNames(['en'], { type: 'region', fallback: 'none' });
const known = codes.filter((c) => { try { return !!en.of(c); } catch { return false; } });
const out = {};
for (const l of LANGS) {
  const d = new Intl.DisplayNames([l], { type: 'region', fallback: 'none' });
  out[l] = Object.fromEntries(known.map((c) => [c, d.of(c)]).filter(([, n]) => n));
}
writeFileSync(ROOT + 'src/Lang/countries.json', JSON.stringify(out, null, 1) + '\n');
console.log(`src/Lang/countries.json: ${known.length} codes in ${LANGS.join(', ')}`);
