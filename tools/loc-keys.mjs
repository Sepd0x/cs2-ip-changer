// The visible English texts of the XAML (the keys of src/Lang/*.json), with the line of their first use.
//   node tools/loc-keys.mjs            prints the count
//   node tools/loc-keys.mjs --json     the list as JSON: [{key, line, file}]
// tests/LocTests.cs does the same extraction in C# (keep the two the same).
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
const ROOT = fileURLToPath(new URL('../', import.meta.url));
// constants the code hands to Loc.T through a variable, a return value or an exception message (the call can't be read as a literal)
const CONSTS = new Set(['NotUnderstood', 'NoSteam', 'SteamLinked', 'SsidLocationText', 'NoticeTitle', 'UsbDebuggingSteps', 'Cs2NotLockedYet']);
const dec = s => s.replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCodePoint(parseInt(h, 16))).replace(/&#(\d+);/g, (_, d) => String.fromCodePoint(+d))
  .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');
export function xamlKeys() {
  const files = ['src/MainWindow.xaml', ...readdirSync(join(ROOT, 'src/Views')).filter(f => f.endsWith('.xaml')).map(f => 'src/Views/' + f)];
  const out = new Map();
  const add = (raw, file, line, attr) => {
    let v = dec(attr ? raw.replace(/[\r\n\t]/g, ' ') : raw.replace(/\s+/g, ' ').trim());
    const braced = v.startsWith('{}');   // "{}" up front is how XAML starts a text with a brace
    if (braced) v = v.slice(2);
    if (!v || !braced && v.startsWith('{') || !/\p{L}{2}/u.test(v) || /^[\p{L}\d_.]+$/u.test(v) && /^[a-z]/.test(v) && !v.includes(' ') && attr === 'x') return;
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
    // texts a style or a trigger sets (<Setter Property="Content" Value="In use"/>)
    for (const m of clean.matchAll(/<Setter\s+Property="(Text|Content|ToolTip|Header)"\s+Value="([^"]*)"/g)) add(m[2], f, lineOf(m.index), 'a');
    // the grey hint inside an empty TextBox is its Tag (the style draws it; Loc.ApplyOne translates it)
    for (const m of clean.matchAll(/<(TextBox|PasswordBox)\b(?:[^>"]|"[^"]*")*?\bTag="([^"]*)"/g)) add(m[2], f, lineOf(m.index), 'a');
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
  const re2 = new RegExp(String.raw`Fmt\.Count\([^,;]+,\s*` + LIT, 'g');
  // Calls whose first argument is a text the app shows: Loc.T/Loc.F, the Steam web errors (their constructor runs Loc.T), the
  // parts a run stops after. The argument can be a ternary (cond ? "a" : "b"): every branch is a key.
  const HEADS = new RegExp(String.raw`(?:\bLoc\.[TF]|\bnew SteamWebException|\bnew StopRun|\bStopAfterStep)\(`, 'g');
  // constants the code passes to Loc.T through a variable, a return value or an exception message
  const consts = new Map();
  const files = walk('src').filter(f => f !== 'src/Loc.cs');
  for (const f of files) {
    const t = readFileSync(join(ROOT, f), 'utf8');
    for (const m of t.matchAll(new RegExp(String.raw`\bconst string (\w+)\s*=\s*((?:` + LIT + String.raw`\s*\+?\s*)+);`, 'g')))
      consts.set(m[1], [...m[2].matchAll(new RegExp(LIT, 'g'))].map(x => un(x[1])).join(''));
  }
  for (const n of CONSTS) if (consts.has(n)) out.add(consts.get(n));
  for (const f of files) {
    const t = readFileSync(join(ROOT, f), 'utf8');
    for (const m of t.matchAll(HEADS)) for (const k of callKeys(t, m.index + m[0].length, consts)) out.add(k);
    // the words the kick, ban and VPN toasts put in a sentence, and the reasons the IP stays
    for (const m of t.matchAll(/var what = kind switch \{([^\n]*)\}/g)) for (const x of m[1].matchAll(new RegExp('=>\\s*' + LIT, 'g'))) out.add(un(x[1]));
    for (const m of t.matchAll(/string IpLeftOutWhy =>([^\n]*);/g)) for (const x of m[1].matchAll(new RegExp(LIT, 'g'))) out.add(un(x[1]));
    for (const m of t.matchAll(re2)) out.add(un(m[1]));
    // files that hold texts in arrays and records (guides, tour, menu paths): every plain literal that reads as a sentence or a name
    if (ALL.includes(f)) for (const m of t.matchAll(new RegExp('(?<![$@' + BS + ']|[A-Za-z])' + LIT, 'g'))) {
      const v = un(m[1]);
      if (/[A-Za-z]{2}/.test(v) && (v.includes(' ') || /^[A-Z][a-z]{3,}/.test(v)) && !v.includes('{') && !/^[A-Za-z]+[.:]/.test(v)) out.add(v);
    }
  }
  // Configs > Compare: the setting names (Labels), what the crosshair ones lose in the table (the category column says it), the words of the
  // two columns and the name of a key row
  const cfg = readFileSync(join(ROOT, 'src/Core/Steam/Cs2Configs.cs'), 'utf8');
  const at = cfg.indexOf('static readonly Dictionary<string, string> Labels');
  for (const m of cfg.slice(at, cfg.indexOf('};', at)).matchAll(new RegExp(String.raw`\]\s*=\s*` + LIT, 'g'))) {
    const v = un(m[1]);
    out.add(v.startsWith('Crosshair: ') ? v[11].toUpperCase() + v.slice(12) : v);
  }
  const show = cfg.slice(cfg.indexOf('static string Show('), cfg.indexOf('/// <summary>Leaf settings'));
  for (const m of show.matchAll(new RegExp(LIT, 'g'))) { const v = un(m[1]); if (/^[a-z][a-z ]+$/.test(v) && v !== 'true' && v !== 'false') out.add(v); }
  for (const m of cfg.matchAll(/ColorNames = \{([^}]*)\}/g)) for (const x of m[1].matchAll(new RegExp(LIT, 'g'))) out.add(un(x[1]));
  out.add('Key {0}');
  for (const m of cfg.matchAll(/\("(\w+)", CfgPart\.\w+\)/g)) if (m[1] !== "game" && m[1] !== "hud") out.add(m[1]);   // the category column (game and hud have their own words in CfgDiffRow)
  // the continents: the Regions page groups by them and the "<continent> only" button names one
  const reg = readFileSync(join(ROOT, 'src/Core/Network/Regions.cs'), 'utf8');
  for (const m of reg.matchAll(/ContinentOrder = \{([^}]*)\}/g)) for (const x of m[1].matchAll(new RegExp(LIT, 'g'))) out.add(un(x[1]));
  return [...out];
}

/** The texts one call shows, from its first argument: leading literals joined with "+", a named constant, or every branch of a ternary. */
function callKeys(t, i, consts) {
  const keys = [];
  let d = 0, j = i;
  const toks = [];                                       // string literals, names and single characters of the first argument
  for (; j < t.length; j++) {
    const c = t[j];
    if (c === '"') {                                     // a plain string literal (an interpolated or verbatim one has its $ or @ just before)
      const pre = t[j - 1];
      let k = j + 1; while (k < t.length && t[k] !== '"') k += t[k] === '\\' ? 2 : 1;
      toks.push(pre === '$' || pre === '@' ? { s: 'x' } : { lit: t.slice(j + 1, k) });
      j = k; continue;
    }
    if (/\s/.test(c)) continue;
    if (/[A-Za-z_]/.test(c)) { let k = j; while (k < t.length && /[\w.]/.test(t[k])) k++; toks.push({ id: t.slice(j, k) }); j = k - 1; continue; }
    if (c === '$' || c === '@') continue;
    if ('([{'.includes(c)) d++;
    else if (')]}'.includes(c)) { if (d === 0) break; d--; }
    else if (c === ',' && d === 0) break;
    toks.push({ s: c });
  }
  const un = s => { let r = ''; for (let n = 0; n < s.length; n++) { if (s[n] !== '\\') { r += s[n]; continue; } const c = s[++n]; r += c === 'n' ? '\n' : c === 't' ? '\t' : c === 'u' ? (n += 4, String.fromCharCode(parseInt(s.slice(n - 3, n + 1), 16))) : c; } return r; };
  // a group: literals joined with "+"
  const groups = [];
  for (let n = 0; n < toks.length; n++) {
    if (toks[n].lit === undefined) continue;
    const prev = toks[n - 1], pp = toks[n - 2];
    if (prev && prev.s === '+' && pp && pp.lit !== undefined) continue;   // not the first of its group
    let v = un(toks[n].lit), m = n;
    while (toks[m + 1]?.s === '+' && toks[m + 2]?.lit !== undefined) { v += un(toks[m + 2].lit); m += 2; }
    const next = toks[m + 1];
    groups.push({ v, prev, next, first: n === 0 });
  }
  const ternary = toks.some((x, n) => x.s === '?' && toks[n + 1]?.s !== '?' && toks[n + 1]?.s !== '.' && toks[n + 1]?.s !== '[' && toks[n - 1]?.s !== '?');
  for (const g of groups) {
    if (!/\p{L}{2}/u.test(g.v)) continue;
    // "+" after a lone literal that is not the whole text (a name or a value glued on): the head of the text only
    if (g.first || ternary && (g.prev?.s === '?' || g.prev?.s === ':')) keys.push(g.v);
  }
  const last = toks.length === 1 && toks[0].id ? toks[0].id.split('.').pop() : null;
  if (last && CONSTS.has(last) && consts.has(last)) keys.push(consts.get(last));
  return keys;
}
export const allKeys = () => [...new Set([...xamlKeys().map(x => x.key), ...codeKeys()])];
