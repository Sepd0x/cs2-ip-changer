// The visible English texts of the XAML (the keys of src/Lang/*.json), with the line of their first use.
//   node tools/loc-keys.mjs            prints the count
//   node tools/loc-keys.mjs --json     the list as JSON: [{key, line, file}]
// tests/LocTests.cs does the same extraction in C# (keep the two the same).
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
const ROOT = fileURLToPath(new URL('../', import.meta.url));
const dec = s => s.replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCodePoint(parseInt(h, 16))).replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(+d))
  .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');
export function xamlKeys() {
  const files = ['src/MainWindow.xaml', ...readdirSync(join(ROOT, 'src/Views')).filter(f => f.endsWith('.xaml')).map(f => 'src/Views/' + f)];
  const out = new Map();
  const add = (raw, file, line, attr) => {
    let v = dec(attr ? raw.replace(/[\r\n\t]/g, ' ') : raw.replace(/\s+/g, ' ').trim());
    if (!v || v.startsWith('{') || !/\p{L}{2}/u.test(v) || /^[\p{L}\d_.]+$/u.test(v) && /^[a-z]/.test(v) && !v.includes(' ') && attr === 'x') return;
    if (!out.has(v)) out.set(v, { key: v, file, line });
  };
  for (const f of files) {
    const lines = readFileSync(join(ROOT, f), 'utf8').replace(/^﻿/, '').split(/\r?\n/);
    const text = lines.join('\n');
    const lineOf = i => text.slice(0, i).split('\n').length;
    for (const m of text.matchAll(/<!--[\s\S]*?-->/g)) text.slice(m.index, m.index + m[0].length);
    const clean = text.replace(/<!--[\s\S]*?-->/g, c => c.replace(/[^\n]/g, ' '));
    // text between elements, e.g. a paragraph after a <LineBreak/> (attribute values blanked first: they can hold a ">")
    const bare = clean.replace(/"[^"]*"/g, q => '"' + q.slice(1, -1).replace(/[^\n]/g, '') + '"');
    for (const m of bare.matchAll(/>([^<>]+)</g)) add(m[1], f, lineOf(m.index), null);
    for (const m of clean.matchAll(/\b(Text|Content|ToolTip|Header|Title)="([^"]*)"/g)) add(m[2], f, lineOf(m.index), 'a');
    for (const m of clean.matchAll(/<(TextBlock|Run|Button|CheckBox|RadioButton|Label)\b(?:[^>"]|"[^"]*")*(?<!\/)>([^<]+)</g)) add(m[2], f, lineOf(m.index), null);
  }
  // src/Texts.cs: the constants the XAML reads with {x:Static}; "+" joined literals are one text
  const tx = readFileSync(join(ROOT, 'src/Texts.cs'), 'utf8');
  const LIT = '"((?:[^"\\\\]|\\\\.)*)"';
  for (const m of tx.matchAll(new RegExp('public const string \\w+ =\\s*((?:' + LIT + '\\s*\\+?\\s*)+);', 'g'))) {
    const v = [...m[1].matchAll(new RegExp(LIT, 'g'))].map(x => x[1].replace(/\\n/g, '\n').replace(/\\"/g, '"').replace(/\\\\/g, '\\')).join('');
    if (/\p{L}{2}/u.test(v) && !out.has(v)) out.set(v, { key: v, file: 'src/Texts.cs', line: tx.slice(0, m.index).split('\n').length });
  }
  return [...out.values()];
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const k = xamlKeys();
  if (process.argv.includes('--all')) console.log(JSON.stringify([...new Set([...xamlKeys().map(x => x.key), ...codeKeys()])]));
  else if (process.argv.includes('--json')) console.log(JSON.stringify(k, null, 1)); else console.log(k.length, 'keys', k.reduce((n, x) => n + x.key.length, 0), 'chars');
}

/** The texts the C# code passes to Loc.T / Loc.F (one literal, or literals joined with "+"), and the nouns of Fmt.Count. */
export function codeKeys() {
  const out = new Set();
  const walk = d => readdirSync(join(ROOT, d), { withFileTypes: true }).flatMap(e => e.isDirectory() ? (['Privado', 'obj', 'bin'].includes(e.name) ? [] : walk(d + '/' + e.name)) : e.name.endsWith('.cs') ? [d + '/' + e.name] : []);
  const BS = String.fromCharCode(92);
  const ALL = ['src/Texts.cs', 'src/Core/Phone/PhoneSetup.cs'];
  const LIT = '"((?:[^"' + BS + BS + ']|' + BS + BS + '.)*)"';
  const un = s => { let r = ''; for (let i = 0; i < s.length; i++) { if (s[i] !== BS) { r += s[i]; continue; } const c = s[++i]; r += c === 'n' ? '\n' : c === 't' ? '\t' : c === 'u' ? (i += 4, String.fromCharCode(parseInt(s.slice(i - 3, i + 1), 16))) : c; } return r; };
  const re1 = new RegExp(String.raw`Loc\.[TF]\(\s*((?:` + LIT + String.raw`\s*\+?\s*)+)`, 'g');
  const re2 = new RegExp(String.raw`Fmt\.Count\([^,;]+,\s*` + LIT, 'g');
  for (const f of walk('src')) {
    if (f === 'src/Loc.cs') continue;
    const t = readFileSync(join(ROOT, f), 'utf8');
    for (const m of t.matchAll(re1)) out.add([...m[1].matchAll(new RegExp(LIT, 'g'))].map(x => un(x[1])).join(''));
    for (const m of t.matchAll(re2)) out.add(un(m[1]));
    // files that hold texts in arrays and records (guides, tour, menu paths): every plain literal that reads as a sentence or a name
    if (ALL.includes(f)) for (const m of t.matchAll(new RegExp('(?<![$@' + BS + ']|[A-Za-z])' + LIT, 'g'))) {
      const v = un(m[1]);
      if (/[A-Za-z]{2}/.test(v) && (v.includes(' ') || /^[A-Z][a-z]{3,}/.test(v)) && !v.includes('{') && !/^[A-Za-z]+[.:]/.test(v)) out.add(v);
    }
  }
  return [...out];
}
export const allKeys = () => [...new Set([...xamlKeys().map(x => x.key), ...codeKeys()])];
