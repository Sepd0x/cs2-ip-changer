// The site in other languages, made from the English pages: site/pt-br/, site/es/ and site/ru/ (the home page, every guide
// and the 404), with hreflang on every page, the language picker in the header and the footer, and sitemap.xml with the
// alternates. English stays the main site (x-default). One source of truth: the English HTML (site/index.html, site/404.html
// and the guides that tools/site-pages.mjs writes) plus one file of translations per language, tools/i18n/<lang>.json. The
// text of each page is cut into units (a paragraph, a heading, a list item, an alt, a description...), the inline tags in a
// unit become placeholders (<a1>…</a1>, <code2/>), and each unit is looked up by its English text. A changed English text has
// no translation yet, so the check fails until it has one (node tools/site-i18n.mjs --check, and tools/site-i18n.test.mjs).
//
//   node tools/site-i18n.mjs                 writes the language folders, stamps the English pages, rewrites sitemap.xml
//   node tools/site-i18n.mjs --check         changes nothing; fails if a translation is missing or a page is out of date
//   node tools/site-i18n.mjs --todo <dir>    writes <dir>/<lang>.json with the units each language is missing (for translators)
//   node tools/site-i18n.mjs --prune         takes out translations of English texts that changed or went
//   node tools/site-i18n.mjs --merge <dir>   puts <dir>/<lang>-out/<group>.json (the translators' answers) into tools/i18n/
//
// tools/site-pages.mjs runs this after it writes the guides. What stays English on purpose: the app's name, its buttons and
// pages (the app is in English), code and commands (<code>, <kbd>), the 3D scene (its phone shows Android's own words) and
// the URLs (the same slugs in every language).
import { readFileSync, writeFileSync, mkdirSync, existsSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const SITE = join(root, 'site');
const I18N = join(root, 'tools', 'i18n');
export const BASE = 'https://sepd0x.github.io/cs2-ip-changer/';
const PATH = '/cs2-ip-changer/';

// The languages. hint/go/close: the line an English page shows when the browser asks for that language (never a redirect).
export const LANGS = [
  { code: 'en', hreflang: 'en', og: 'en_US', name: 'English', short: 'EN', label: 'Language', intl: 'en-GB' },
  { code: 'pt-br', hreflang: 'pt-BR', og: 'pt_BR', name: 'Português (Brasil)', short: 'PT', label: 'Idioma', intl: 'pt-BR', mb: 'MB', of: 'de',
    match: '^pt', hint: 'Este site também está em português.', go: 'Ver em português', close: 'Fechar' },
  { code: 'es', hreflang: 'es', og: 'es_LA', name: 'Español', short: 'ES', label: 'Idioma', intl: 'es-419', mb: 'MB', of: 'de',
    match: '^es', hint: 'Este sitio también está en español.', go: 'Ver en español', close: 'Cerrar' },
  { code: 'ru', hreflang: 'ru', og: 'ru_RU', name: 'Русский', short: 'RU', label: 'Язык', intl: 'ru-RU', mb: 'МБ', of: 'из',
    match: '^(ru|be|kk|ky)\\b', hint: 'Этот сайт есть и на русском.', go: 'Открыть на русском', close: 'Закрыть' },
];
const OTHER = LANGS.slice(1);
const CODES = new Set(OTHER.map((l) => l.code));

// ------------------------------------------------------------ the pages
/** Every English page: file (under site/), dir (its folder from the site root, '' for the home page), group (its key in the
 *  translation files) and abs (the 404 uses root paths, since GitHub Pages serves it at any address). */
export function pagesOf(site = SITE) {
  const out = [{ file: 'index.html', dir: '', group: 'index', url: '' }, { file: '404.html', dir: '', group: '404', abs: true }];
  for (const d of readdirSync(site, { withFileTypes: true }))
    if (d.isDirectory() && !CODES.has(d.name) && existsSync(join(site, d.name, 'index.html')))
      out.push({ file: `${d.name}/index.html`, dir: `${d.name}/`, group: d.name, url: `${d.name}/` });
  return out;
}

// ------------------------------------------------------------ HTML, read as tokens
const RAW = new Set(['script', 'style']);
// inline tags: a unit runs through them, and they become placeholders
const INLINE = new Set('a abbr b bdi bdo br cite data del dfn em i img ins mark q s small span strong sub sup time u var wbr'.split(' '));
// kept whole: their insides are never translated (code, commands, drawings)
const ATOMIC = new Set(['code', 'kbd', 'samp', 'svg', 'pre', 'math']);
const VOID = new Set('area base br col embed hr img input link meta source track wbr'.split(' '));
const letters = (s) => /\p{L}/u.test(s);

function parseTag(s) {
  const m = /^<(\/?)([a-zA-Z][\w:-]*)/.exec(s);
  const end = s.endsWith('/>') ? -2 : -1, body = s.slice(m[0].length, end), attrs = [];
  const re = /([^\s=/>"']+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>"']+)))?/g;
  let a;
  while ((a = re.exec(body))) attrs.push({ n: a[1].toLowerCase(), raw: a[1], v: a[2] ?? a[3] ?? a[4] ?? null, q: a[2] != null ? '"' : a[3] != null ? "'" : '' });
  return { k: 'tag', s, tn: m[2], name: m[2].toLowerCase(), close: !!m[1], self: s.endsWith('/>'), attrs };
}
const tagString = (t) => !t.dirty ? t.s : `<${t.close ? '/' : ''}${t.tn}${t.attrs.map((a) => a.v == null ? ` ${a.raw}` : a.q === "'" ? ` ${a.raw}='${a.v}'` : ` ${a.raw}="${a.v.replace(/"/g, '&quot;')}"`).join('')}${t.self ? '/' : ''}>`;
const attr = (t, n) => t.attrs.find((a) => a.n === n);

function tokenize(h) {
  const t = [];
  let i = 0;
  while (i < h.length) {
    if (h.startsWith('<!--', i)) { const j = h.indexOf('-->', i + 4), e = j < 0 ? h.length : j + 3; t.push({ k: 'c', s: h.slice(i, e) }); i = e; continue; }
    if (h[i] === '<' && h[i + 1] === '!') { const j = h.indexOf('>', i); t.push({ k: 'd', s: h.slice(i, j + 1) }); i = j + 1; continue; }
    if (h[i] === '<' && /[a-zA-Z/]/.test(h[i + 1] || '')) {
      let j = i + 1, q = null;
      for (; j < h.length; j++) { const c = h[j]; if (q) { if (c === q) q = null; } else if (c === '"' || c === "'") q = c; else if (c === '>') break; }
      const tag = parseTag(h.slice(i, j + 1));
      t.push(tag); i = j + 1;
      if (!tag.close && RAW.has(tag.name)) { const k = h.toLowerCase().indexOf(`</${tag.name}`, i); t.push({ k: 'raw', s: h.slice(i, k), of: tag }); i = k; }
      continue;
    }
    let j = i + 1;
    while (j < h.length && !(h[j] === '<' && /[a-zA-Z/!]/.test(h[j + 1] || ''))) j++;
    t.push({ k: 't', s: h.slice(i, j) }); i = j;
  }
  // code, svg, pre...: one token each, from the open tag to its close
  const out = [];
  for (let n = 0; n < t.length; n++) {
    const x = t[n];
    if (x.k === 'tag' && !x.close && !x.self && ATOMIC.has(x.name)) {
      let depth = 0, m = n;
      for (; m < t.length; m++) { const y = t[m]; if (y.k === 'tag' && y.name === x.name && !y.self) depth += y.close ? -1 : 1; if (!depth) break; }
      out.push({ k: 'atom', name: x.name, open: x, rest: t.slice(n + 1, m + 1).map((y) => y.s).join('') }); n = m; continue;
    }
    out.push(x);
  }
  return out;
}

// ------------------------------------------------------------ values that change at each release, kept out of the keys
const MASKS = [
  ['version', /\bv?\d+\.\d+\.\d+-(?:alpha|beta|rc)\.\d+\b/g],
  ['date', /\b\d{1,2} (?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4}\b/g],
  ['iso', /\b20\d\d-\d\d-\d\d\b/g],
  ['size', /\b\d+(?:\.\d+)? MB\b/g],
  ['vt', /\b\d+ of \d+(?= engines\b)/g],
];
const MONTHS = 'Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec'.split(' ');
function mask(s) {
  const vals = {}, seen = {};
  for (const [name, re] of MASKS) s = s.replace(re, (m) => { seen[name] = (seen[name] || 0) + 1; const tok = `{${name}${seen[name] > 1 ? seen[name] : ''}}`; vals[tok] = [name, m]; return tok; });
  return { key: s, vals };
}
function localize([name, v], L) {
  if (name === 'date') { const [d, mo, y] = v.split(' '); return new Date(Date.UTC(+y, MONTHS.indexOf(mo), +d, 12)).toLocaleDateString(L.intl, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }); }
  if (name === 'size') return `${(+v.split(' ')[0]).toLocaleString(L.intl, { maximumFractionDigits: 1 })} ${L.mb}`;
  if (name === 'vt') { const [a, , b] = v.split(' '); return `${a} ${L.of} ${b}`; }
  return v;
}

// ------------------------------------------------------------ the translations
const norm = (s) => s.replace(/\s+/g, ' ').trim();
const unesc = (s) => s.replace(/&quot;/g, '"').replace(/&#39;|&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&nbsp;/g, '\u00a0').replace(/&amp;/g, '&');
const escText = (s) => s.replace(/&(?![a-zA-Z]+;|#\d+;|#x[0-9a-fA-F]+;)/g, '&amp;');
const PH = /<\/?[a-z]+\d+\/?>/g;
const placeholders = (s) => (s.match(PH) || []).sort().join(' ');
const tokens = (s) => (s.match(/\{[a-z]+\d*\}/g) || []).sort().join(' ');

export function loadDicts(dir = I18N) {
  const d = {};
  for (const L of OTHER) { const f = join(dir, `${L.code}.json`); d[L.code] = existsSync(f) ? JSON.parse(readFileSync(f, 'utf8')) : {}; }
  return d;
}

/** A page's translation context: the language, the page, and what was looked up (for --check and --todo). */
function context(L, page, dict, report) { return { L, page, dict, report, plain: new Map(), plainEn: new Set() }; }
function lookup(ctx, key, kind) {
  const { dict, page, report } = ctx;
  const tr = dict[page.group]?.[key] ?? dict.shared?.[key];
  report.used.push({ group: page.group, key, kind });
  if (tr == null) { report.missing.push({ lang: ctx.L.code, group: page.group, key }); return null; }
  if (placeholders(tr) !== placeholders(key) || tokens(tr) !== tokens(key)) { report.bad.push({ lang: ctx.L.code, group: page.group, key, tr, why: 'placeholders' }); return null; }
  return tr;
}
const unmask = (s, vals, L) => s.replace(/\{[a-z]+\d*\}/g, (m) => vals[m] ? localize(vals[m], L) : m);

/** A plain string (an attribute, a description, a script's text): translated, or the English if there's none. */
function trPlain(ctx, s, kind = 'attr') {
  if (!letters(s)) return s;
  const { key, vals } = mask(norm(s));
  const tr = lookup(ctx, key, kind);
  const out = tr == null ? s : s.match(/^\s*/)[0] + unmask(tr, vals, ctx.L) + s.match(/\s*$/)[0];
  ctx.plain.set(norm(unesc(s)), norm(unesc(out)));
  return out;
}

// ------------------------------------------------------------ addresses
let PAGE_DIRS = new Set();
const isPage = (p) => p === '/' || p === '/index.html' || PAGE_DIRS.has(p);
/** An address on a page of the English site, as it must read on the same page in language L. */
function url(u, ctx) {
  if (!u || u.startsWith('#') || /^(data|mailto|tel|javascript|blob|about):/i.test(u)) return u;
  const lang = ctx.L.code;
  let path, kind;
  if (u.startsWith(BASE)) { path = u.slice(BASE.length - 1); kind = 'full'; }
  else if (/^(https?:)?\/\//i.test(u)) return u;
  else if (u.startsWith(PATH)) { path = u.slice(PATH.length - 1); kind = 'root'; }
  else if (u.startsWith('/')) return u;
  else { path = new URL(u, `http://x/${ctx.page.dir}`).pathname; kind = 'rel'; }
  const p = path.split(/[?#]/)[0] || '/';
  if (kind === 'rel') return isPage(p) ? u : `../${u}`;
  if (!isPage(p)) return u;
  return (kind === 'full' ? BASE : PATH) + lang + '/' + path.slice(1);
}
const cssUrls = (css, ctx) => css.replace(/url\(\s*(['"]?)([^'")]+)\1\s*\)/g, (m, q, u) => `url(${q}${url(u, ctx)}${q})`);

// ------------------------------------------------------------ a tag's attributes
const TATTR = new Set(['alt', 'title', 'aria-label', 'placeholder', 'data-label', 'aria-roledescription', 'aria-description']);
const UATTR = new Set(['href', 'src', 'poster', 'data-src', 'action', 'xlink:href', 'data-copy']);
const META_T = new Set(['description', 'keywords', 'twitter:title', 'twitter:description', 'twitter:image:alt', 'og:title', 'og:description', 'og:image:alt']);
function tagOut(t, ctx) {
  if (t.close) return t.s;
  const head = t.name === 'link' || t.name === 'meta' || t.name === 'script' || t.name === 'style';
  for (const a of t.attrs) {
    if (a.v == null) continue;
    let v = a.v;
    if (t.name === 'html' && a.n === 'lang') v = ctx.L.hreflang;
    else if (!head && TATTR.has(a.n)) v = trPlain(ctx, a.v);
    else if (a.n === 'data-peek') { const i = a.v.indexOf('|'); if (i > 0) v = a.v.slice(0, i + 1) + trPlain(ctx, a.v.slice(i + 1)); }
    else if (t.name === 'meta' && a.n === 'content') {
      const which = (attr(t, 'name') || attr(t, 'property'))?.v;
      if (META_T.has(which)) v = trPlain(ctx, a.v);
      else if (which === 'og:locale') v = ctx.L.og;
      else if (which === 'og:url') v = url(a.v, ctx);
    }
    else if (UATTR.has(a.n)) v = url(a.v, ctx);
    else if (a.n === 'srcset' || a.n === 'data-srcset') v = a.v.split(',').map((x) => x.trim().replace(/^\S+/, (u) => url(u, ctx))).join(', ');
    else if (a.n === 'style') v = cssUrls(a.v, ctx);
    if (v !== a.v) { a.v = v; t.dirty = true; }
  }
  return tagString(t);
}
// the page's address as text (<code class="url">, to type on the PC): with the language folder in it
const atomOut = (x, ctx) => tagOut(x.open, ctx) + (x.name === 'code' && /\burl\b/.test(attr(x.open, 'class')?.v || '')
  ? x.rest.replace(/cs2-ip-changer(\/<wbr>)?/, (m, sep) => `cs2-ip-changer/<wbr>${ctx.L.code}${sep ? '/<wbr>' : ''}`) : x.rest);

// ------------------------------------------------------------ a run of text and inline tags
function runOut(run, ctx) {
  return itemsOut(run.map((x) => ({ x, s: x.k === 'tag' ? tagOut(x, ctx) : x.k === 'atom' ? atomOut(x, ctx) : x.s })), ctx);
}
function itemsOut(items, ctx) {
  const raw = () => items.map((i) => i.s).join('');
  if (!items.some((i) => i.x.k === 't' && letters(i.x.s))) return raw();
  const isTag = (i, close) => i.x.k === 'tag' && !i.x.self && !VOID.has(i.x.name) && i.x.close === close;
  const loose = (i) => (i.x.k === 'tag' && (i.x.self || VOID.has(i.x.name))) || i.x.k === 'c' || (i.x.k === 'atom' && i.x.name === 'svg');
  const ws = (i) => i.x.k === 't' && !i.x.s.trim();
  const match = (arr, a) => {   // the close of the open tag at a, or the open of the close tag at a
    const it = arr[a], dir = it.x.close ? -1 : 1;
    for (let b = a + dir, d = 0; b >= 0 && b < arr.length; b += dir) {
      const y = arr[b];
      if (y.x.k !== 'tag' || y.x.name !== it.x.name || y.x.self || VOID.has(y.x.name)) continue;
      if (y.x.close === it.x.close) d++; else if (d) d--; else return b;
    }
    return -1;
  };
  let mid = items.slice(), pre = [], post = [];
  for (;;) {
    while (mid.length && ws(mid[0])) pre.push(mid.shift());
    while (mid.length && ws(mid[mid.length - 1])) post.unshift(mid.pop());
    if (!mid.length) break;
    const f = mid[0], l = mid[mid.length - 1];
    if (loose(f) || isTag(f, true)) { pre.push(mid.shift()); continue; }
    if (isTag(f, false)) { const j = match(mid, 0); if (j < 0) { pre.push(mid.shift()); continue; } if (j === mid.length - 1) { pre.push(mid.shift()); post.unshift(mid.pop()); continue; } }
    if (loose(l) || isTag(l, false)) { post.unshift(mid.pop()); continue; }
    if (isTag(l, true) && match(mid, mid.length - 1) < 0) { post.unshift(mid.pop()); continue; }
    break;
  }
  if (!mid.some((i) => i.x.k === 't' && letters(i.x.s))) return raw();
  // side by side elements with no words between them (the links of a menu, a card's title and line): one unit each
  const parts = [];
  for (let a = 0, d = 0, from = 0, words = false; a < mid.length; a++) {
    const i = mid[a];
    if (i.x.k === 't' && !d && letters(i.x.s)) words = true;
    if (isTag(i, false)) d++; else if (isTag(i, true) && d) d--;
    if (!d && !(i.x.k === 't' && !i.x.s.trim())) { parts.push(mid.slice(from, a + 1)); from = a + 1; }
    else if (!d && a === mid.length - 1) parts.push(mid.slice(from));
    if (a === mid.length - 1 && words) parts.length = 0;
  }
  if (parts.length > 1) return pre.map((i) => i.s).join('') + parts.map((p) => itemsOut(p, ctx)).join('') + post.map((i) => i.s).join('');
  // the key: text as it is, every tag a numbered placeholder
  const map = {}, opened = new Map();
  let n = 0;
  const key0 = mid.map((i, a) => {
    if (i.x.k === 't') return i.x.s;
    const nm = i.x.k === 'c' ? 'c' : i.x.name;
    let ph;
    if (i.x.k === 'tag' && !i.x.self && !VOID.has(i.x.name)) {
      if (!i.x.close) { ph = `<${nm}${++n}>`; opened.set(a, n); }
      else { const o = match(mid, a); ph = `</${nm}${o >= 0 ? opened.get(o) : ++n}>`; }
    } else ph = `<${nm}${++n}/>`;
    map[ph] = i.s;
    return ph;
  }).join('');
  const { key, vals } = mask(norm(key0));
  const tr = lookup(ctx, key, 'html');
  const body = tr == null ? mid.map((i) => i.s).join('') : unmask(escText(tr), vals, ctx.L).replace(PH, (m) => map[m] ?? m);
  // for the structured data: the same text without its tags
  const plainOf = (s) => norm(unesc(s.replace(PH, (m) => (map[m] || '').startsWith('<code') || (map[m] || '').startsWith('<kbd') ? map[m].replace(/<[^>]+>/g, '') : '').replace(/<[^>]+>/g, '')));
  ctx.plainEn.add(plainOf(key0));
  if (tr != null) ctx.plain.set(plainOf(key0), plainOf(unmask(tr, vals, ctx.L)));
  return pre.map((i) => i.s).join('') + body + post.map((i) => i.s).join('');
}

// ------------------------------------------------------------ scripts and styles
const LD_TEXT = new Set(['name', 'description', 'text', 'headline', 'alternateName', 'caption', 'featureList', 'softwareRequirements', 'applicationSubCategory', 'acceptedAnswer']);
function ldOut(src, ctx) {
  const ind = (/\n( +)"/.exec(src) || [, ' '])[1].length;
  const walk = (v, k) => {
    if (Array.isArray(v)) return v.map((x) => walk(x, k));
    if (v && typeof v === 'object') return Object.fromEntries(Object.entries(v).map(([kk, x]) => [kk, walk(x, kk)]));
    if (typeof v !== 'string') return v;
    if (k === 'inLanguage') return ctx.L.hreflang;
    if (/^https?:\/\//.test(v)) return url(v, ctx);
    if (LD_TEXT.has(k) && letters(v)) { const p = ctx.plain.get(norm(v)); return p ?? (ctx.plainEn.has(norm(v)) ? v : trPlain(ctx, v, 'ld')); }
    return v;
  };
  const lead = src.match(/^\s*/)[0], tail = src.match(/\s*$/)[0];
  return lead + JSON.stringify(walk(JSON.parse(src), ''), null, ind) + tail;
}
const jsLit = (q, s) => s.replace(/\\/g, '\\\\').replace(new RegExp(q, 'g'), `\\${q}`);
function scriptOut(src, ctx) {
  // a text the page's own script shows, marked /*t*/'...' in the English page: translated in place (HTML in it too)
  src = src.replace(/\/\*t\*\/(['"])((?:\\.|(?!\1)[^\\])*)\1/g, (m, q, s) => {
    const v = s.replace(/\\(.)/g, '$1');
    return `/*t*/${q}${jsLit(q, v.includes('<') ? htmlOut(v, ctx) : trPlain(ctx, v, 'js'))}${q}`;
  });
  // the site's own files, by a relative address: one folder up
  return src.replace(/(['"])((?:\.\.\/)*(?:assets|img|fonts)\/[^'"]*)\1/g, (m, q, u) => `${q}${url(u, ctx)}${q}`);
}

// ------------------------------------------------------------ a whole page (or a piece of HTML)
function htmlOut(html, ctx) {
  const out = [], defer = [];
  let run = [];
  const flush = () => { if (run.length) out.push(runOut(run, ctx)); run = []; };
  for (const x of tokenize(html)) {
    if (x.k === 't' || x.k === 'c' || x.k === 'atom' || (x.k === 'tag' && INLINE.has(x.name))) { run.push(x); continue; }
    flush();
    if (x.k === 'tag') out.push(tagOut(x, ctx));
    else if (x.k === 'raw' && x.of.name === 'style') out.push(cssUrls(x.s, ctx));
    else if (x.k === 'raw' && /ld\+json/.test(attr(x.of, 'type')?.v || '')) { const i = out.push('') - 1; defer.push(() => { out[i] = ldOut(x.s, ctx); }); }
    else if (x.k === 'raw') out.push(scriptOut(x.s, ctx));
    else out.push(x.s);
  }
  flush();
  defer.forEach((f) => f());
  return out.join('');
}

// ------------------------------------------------------------ the parts this script owns on every page
const GLOBE = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="8.5"/><path d="M3.5 12h17M12 3.5c2.4 2.3 3.6 5.2 3.6 8.5s-1.2 6.2-3.6 8.5c-2.4-2.3-3.6-5.2-3.6-8.5s1.2-6.2 3.6-8.5z"/></svg>';
/** The address of page p in language L, as seen from page `from` in language F (relative, or from the root on the 404). */
function hrefTo(L, p, F, from) {
  const target = (L.code === 'en' ? '' : `${L.code}/`) + (p.url ?? '');
  if (from.abs) return PATH + target;
  const depth = (F.code === 'en' ? 0 : 1) + (from.dir ? 1 : 0);
  return '../'.repeat(depth) + target || './';
}
const fullUrl = (L, p) => BASE + (L.code === 'en' ? '' : `${L.code}/`) + p.url;
function picker(F, page, home) {
  const to = page.abs ? home : page;
  const li = LANGS.map((L) => `<li><a href="${hrefTo(L, to, F, page)}" hreflang="${L.hreflang}" lang="${L.hreflang}" data-lang="${L.code}"${L === F ? ' aria-current="true"' : ''}>${L.name}</a></li>`).join('');
  return `<details class="lang-pick"><summary aria-label="${F.label}: ${F.name}">${GLOBE}<span>${F.short}</span></summary><ul>${li}</ul></details>`;
}
function footerLangs(F, page, home) {
  const to = page.abs ? home : page;
  return `<nav class="lang-foot" aria-label="${F.label}">${LANGS.map((L) => `<a href="${hrefTo(L, to, F, page)}" hreflang="${L.hreflang}" lang="${L.hreflang}" data-lang="${L.code}"${L === F ? ' aria-current="true"' : ''}>${L.name}</a>`).join('')}</nav>`;
}
const CSS = `<style>
/* the language picker (tools/site-i18n.mjs): a quiet link in the header, the four names in the footer */
.lang-pick{position:relative;display:inline-flex;margin:0;counter-increment:none}
details.lang-pick{border:0!important}
.lang-pick::details-content{block-size:auto;overflow:visible;transition:none}
.lang-pick>summary::before,.lang-pick>summary::after{content:none}
.lang-pick>summary{position:relative;list-style:none;display:inline-flex;transition:color .2s;align-items:center;gap:.35rem;min-height:44px;min-width:44px;justify-content:center;padding:0 .5rem;border-radius:999px;color:var(--ink2);font:500 .8125rem/1 var(--mono,ui-monospace,monospace);letter-spacing:.04em;cursor:pointer;-webkit-user-select:none;user-select:none}
.lang-pick>summary::-webkit-details-marker{display:none}
.lang-pick>summary svg{width:1rem;height:1rem;flex:none}
.lang-pick[open]>summary{color:var(--ink)}
.lang-pick>summary:focus-visible{outline:2px solid var(--t-accent);outline-offset:2px}
.lang-pick ul{position:absolute;top:calc(100% + .35rem);right:0;z-index:60;margin:0;padding:.35rem;list-style:none;min-width:12.5rem;background:var(--bg2,var(--t-surface));border:1px solid var(--line,var(--t-line));border-radius:12px;box-shadow:0 12px 32px rgba(0,0,0,.45)}
.lang-pick li a{display:flex;align-items:center;min-height:44px;padding:0 .75rem;border-radius:8px;color:var(--ink2);text-decoration:none;font:400 .9375rem/1.2 var(--sans,system-ui,sans-serif);white-space:nowrap}
.lang-pick li a[aria-current]{color:var(--ink)}
.lang-pick li a[aria-current]::after{content:"";width:.375rem;height:.375rem;margin-left:auto;border-radius:50%;background:var(--t-accent)}
.lang-pick li a:focus-visible{outline:2px solid var(--t-accent);outline-offset:-2px}
.lang-foot{display:flex;flex-wrap:wrap;gap:.25rem 1.25rem}
.lang-foot a{display:inline-flex;align-items:center;min-height:44px;color:inherit;text-decoration:none}
.lang-foot a[aria-current]{color:var(--ink)}
.lang-foot a:focus-visible{outline:2px solid var(--t-accent);outline-offset:2px}
.lang-hint{position:fixed;left:50%;bottom:calc(1rem + env(safe-area-inset-bottom,0px));z-index:70;transform:translateX(-50%);display:flex;align-items:center;gap:.5rem;width:max-content;max-width:calc(100vw - 2rem);padding:.375rem .375rem .375rem 1rem;background:var(--bg2,var(--t-surface));border:1px solid var(--line,var(--t-line));border-radius:999px;box-shadow:0 12px 32px rgba(0,0,0,.45);color:var(--ink2);font:400 .875rem/1.3 var(--sans,system-ui,sans-serif)}
.lang-hint a{display:inline-flex;align-items:center;min-height:44px;padding:0 .875rem;border-radius:999px;background:var(--t-accent-deep);color:#fff;text-decoration:none;font-weight:600;white-space:nowrap}
.lang-hint button{display:inline-flex;align-items:center;justify-content:center;width:44px;height:44px;flex:none;border:0;border-radius:50%;background:none;color:inherit;font-size:1.25rem;cursor:pointer}
.lang-hint a:focus-visible,.lang-hint button:focus-visible{outline:2px solid var(--t-accent);outline-offset:2px}
@media (hover:hover){.lang-pick>summary:hover,.lang-pick li a:hover,.lang-foot a:hover{color:var(--ink)}.lang-pick li a:hover{background:var(--bg3,var(--t-surface2))}.lang-hint button:hover{color:var(--ink)}}
@media (max-width:420px){.lang-hint{left:1rem;right:1rem;transform:none;width:auto}}
.menu-langs{display:none}
@media (max-width:759px),(max-height:540px) and (orientation:landscape){.top .lang-pick{display:none}.menu-langs{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.25rem;grid-column:1/-1;margin-top:.5rem;padding-top:.75rem;border-top:1px solid var(--line,var(--t-line))}.menu-langs a[aria-current]{color:var(--ink)}}</style>`;
// a picked language is remembered (so the hint stays away); the menu closes on a click outside it or on Escape
const PICK_JS = `<script>(function(d){d.addEventListener('click',function(e){var a=e.target.closest&&e.target.closest('a[data-lang]');if(a){try{localStorage.setItem('cs2-lang',a.getAttribute('data-lang'))}catch(x){}}d.querySelectorAll('details.lang-pick[open]').forEach(function(p){if(!p.contains(e.target))p.open=false})});d.addEventListener('keydown',function(e){if(e.key==='Escape')d.querySelectorAll('details.lang-pick[open]').forEach(function(p){p.open=false;p.querySelector('summary').focus()})})})(document)</script>`;
// English pages only: if the browser asks for one of the other languages first, a line offers that version (a click away,
// never a redirect, and gone for good once closed or once a language was picked)
function hintJs(page, home) {
  const to = page.abs ? home : page;
  const opts = OTHER.map((L) => ({ m: L.match, u: hrefTo(L, to, LANGS[0], page), l: L.hreflang, t: L.hint, g: L.go, c: L.close }));
  return `<script>addEventListener('load',function(){try{if(localStorage.getItem('cs2-lang'))return}catch(x){return}var O=${JSON.stringify(opts)},w=(navigator.languages&&navigator.languages[0]||navigator.language||'').toLowerCase(),o=null;if(/^en/.test(w))return;O.forEach(function(x){if(!o&&new RegExp(x.m).test(w))o=x});if(!o)return;var d=document,b=d.createElement('div');b.className='lang-hint';b.lang=o.l;b.setAttribute('role','note');b.innerHTML='<span></span><a data-lang></a><button type="button">\\u00d7</button>';b.firstChild.textContent=o.t;var a=b.querySelector('a'),x=b.querySelector('button');a.href=o.u;a.textContent=o.g;a.setAttribute('data-lang',o.u.replace(/^.*?(pt-br|es|ru)\\/.*$/,'$1'));x.setAttribute('aria-label',o.c);x.addEventListener('click',function(){try{localStorage.setItem('cs2-lang','en')}catch(e){}b.remove()});setTimeout(function(){d.body.appendChild(b)},1500)})</script>`;
}
// the 404: GitHub Pages serves the English one for every missing address, so under /pt-br/, /es/ or /ru/ it opens that
// language's 404 and puts the address that was asked for back in the address bar
const NF_JS = `<script>(function(l){var m=/^\\/cs2-ip-changer\\/(pt-br|es|ru)\\//.exec(l.pathname);if(m&&!/\\/404\\.html$/.test(l.pathname))l.replace('/cs2-ip-changer/'+m[1]+'/404.html#from='+encodeURIComponent(l.pathname+l.search))})(location)</script>`;
const NF_BACK = (code) => `<script>(function(l){var m=/^#from=(.+)$/.exec(l.hash);if(m){var p=decodeURIComponent(m[1]);if(p.indexOf('/cs2-ip-changer/${code}/')===0)history.replaceState(null,'',p)}})(location)</script>`;

/** Puts this script's parts on a page (English or translated): hreflang, the picker, the footer names, the hint. */
export function stamp(html, F, page, all) {
  const home = all.find((p) => p.group === 'index');
  html = html.replace(/<!--i18n(:[a-z]+)?-->[\s\S]*?<!--\/i18n\1-->/g, '');
  // <head>: the alternates (not on the 404: it has no address of its own) and the picker's style
  let head = '';
  if (!page.abs) {
    head += LANGS.map((L) => `<link rel="alternate" hreflang="${L.hreflang}" href="${fullUrl(L, page)}">`).join('') + `<link rel="alternate" hreflang="x-default" href="${fullUrl(LANGS[0], page)}">`;
    head += LANGS.filter((L) => L !== F).map((L) => `<meta property="og:locale:alternate" content="${L.og}">`).join('');
  }
  head += CSS.replace(/\n/g, '');
  if (page.abs) {
    if (F.code === 'en') html = html.replace(/(<meta charset="utf-8">)/, `$1<!--i18n:nf-->${NF_JS}<!--/i18n:nf-->`);
    else html = html.replace(/(<meta charset="utf-8">)/, `$1<!--i18n:nf-->${NF_BACK(F.code)}<!--/i18n:nf-->`);
  }
  html = page.abs || !/<link rel="canonical"[^>]*>/.test(html)
    ? html.replace('</head>', `<!--i18n-->${head}\n<!--/i18n--></head>`)
    : html.replace(/(<link rel="canonical"[^>]*>)/, `$1<!--i18n-->${head}<!--/i18n-->`);
  // the header: the picker before the GitHub button (the 404: before Send to PC); the footer: the names in the last line
  // (every part, with its own line breaks, sits between its two marks, so taking it out gives back the page as it was)
  const pick = (nl) => `<!--i18n:pick-->${nl}${picker(F, page, home)}<!--/i18n:pick-->`;
  if (page.abs) html = html.replace(/(<header class="top">[\s\S]*?)(<button class="btn sm get-top")/, `$1${pick('')}$2`);
  else html = html.replace(/(<header class="top">[\s\S]*?)(\n?\s*<a class="btn btn-s btn-ghost")/, `$1${pick('\n      ')}$2`);
  html = html.replace(/(<div class="wrap legal">[\s\S]*?)(<\/div>\s*<\/footer>)/, `$1<!--i18n:foot-->${footerLangs(F, page, home)}<!--/i18n:foot-->$2`);
  html = html.replace(/(<div class="menu-links"[\s\S]*?)(\s*<\/div>\s*<\/details>)/, `$1<!--i18n:menu--><div class="menu-langs" lang="${F.hreflang}"><p>${F.label}</p>${LANGS.map((L) => `<a href="${hrefTo(L, page, F, page)}" hreflang="${L.hreflang}" lang="${L.hreflang}" data-lang="${L.code}"${L === F ? ' aria-current="true"' : ''}>${L.name}</a>`).join('')}</div><!--/i18n:menu-->$2`);
  html = html.replace('</body>', `<!--i18n:js-->${PICK_JS}${F.code === 'en' ? hintJs(page, home) : ''}\n<!--/i18n:js--></body>`);
  return html;
}

// ------------------------------------------------------------ the shared scripts' own words: T('...') in assets/js/*.js
const SHARED_JS = ['assets/js/home.js', 'assets/js/readout.js', 'assets/js/mobile-tour.js'];
function sharedStrings(site) {
  const s = new Set();
  for (const f of SHARED_JS) {
    const src = readFileSync(join(site, f), 'utf8');
    for (const m of src.matchAll(/\bT\((['"])((?:\\.|(?!\1)[^\\])*)\1\)/g)) s.add(m[2].replace(/\\(.)/g, '$1'));
  }
  return [...s];
}

/** The page in language L: translated, its addresses one folder down, the parts of this script stamped for L. */
export function translate(enHtml, L, page, all, dict, report, site = SITE) {
  const ctx = context(L, page, dict, report);
  // the stamped parts are made again for L; the shared scripts' words go in a small table before them
  let html = enHtml.replace(/<!--i18n(:[a-z]+)?-->[\s\S]*?<!--\/i18n\1-->/g, '');
  html = htmlOut(html, ctx);
  const words = {};
  for (const s of sharedStrings(site)) { const t = trPlain({ ...ctx, page: { ...page, group: 'js' } }, s, 'js'); if (t !== s) words[s] = t; }
  const table = `<script>window.__i18n=${JSON.stringify(words).replace(/</g, '\\u003c')};window.__root='../'</script>`;
  html = html.replace(/(<meta charset="utf-8">)/, `$1${table}`);
  return stamp(html, L, page, all);
}

// ------------------------------------------------------------ sitemap.xml with the alternates
export function sitemap(enMap, all) {
  const urls = [...enMap.matchAll(/<url>([\s\S]*?)<\/url>/g)].map((m) => m[1]);
  const byUrl = new Map(all.filter((p) => !p.abs).map((p) => [BASE + p.url, p]));
  const alt = (p) => LANGS.map((L) => `    <xhtml:link rel="alternate" hreflang="${L.hreflang}" href="${fullUrl(L, p)}"/>`).join('\n') + `\n    <xhtml:link rel="alternate" hreflang="x-default" href="${fullUrl(LANGS[0], p)}"/>`;
  const out = [];
  for (const L of LANGS) for (const u of urls) {
    const loc = /<loc>([^<]+)<\/loc>/.exec(u)[1], p = byUrl.get(loc);
    if (!p) throw new Error(`sitemap.xml: ${loc} is not a page of the site`);
    const mod = /<lastmod>([^<]+)<\/lastmod>/.exec(u)?.[1];
    const imgs = [...u.matchAll(/<image:image>[\s\S]*?<\/image:image>/g)].map((m) => `    ${m[0]}`).join('\n');
    out.push(`  <url>\n    <loc>${fullUrl(L, p)}</loc>\n${mod ? `    <lastmod>${mod}</lastmod>\n` : ''}${alt(p)}\n${imgs ? imgs + '\n' : ''}  </url>`);
  }
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">\n${out.join('\n')}\n</urlset>\n`;
}

// ------------------------------------------------------------ everything, in memory
/** Every file this script writes, as { 'site/<path>': text }, and what is missing. Nothing is written here. */
export function build({ site = SITE, dicts = loadDicts() } = {}) {
  const all = pagesOf(site);
  PAGE_DIRS = new Set(all.filter((p) => p.dir).map((p) => `/${p.dir}`));
  const files = {}, report = { used: [], missing: [], bad: [] };
  for (const page of all) {
    const en = stamp(readFileSync(join(site, page.file), 'utf8'), LANGS[0], page, all);
    files[page.file] = en;
    for (const L of OTHER) files[`${L.code}/${page.file}`] = translate(en, L, page, all, dicts[L.code], report, site);
  }
  const map = readFileSync(join(site, 'sitemap.xml'), 'utf8');
  // the English sitemap as tools/site-pages.mjs writes it is the list; written again with every language
  const enOnly = map.includes('xmlns:xhtml') ? map.replace(/<url>\s*<loc>https:\/\/sepd0x\.github\.io\/cs2-ip-changer\/(pt-br|es|ru)\/[\s\S]*?<\/url>\s*/g, '').replace(/\s*<xhtml:link[^>]*\/>/g, '') : map;
  files['sitemap.xml'] = sitemap(enOnly, all);
  // the same addresses as plain text, one per line (12 Nov. 2026: Search Console kept saying "Couldn't fetch" for the XML
  // of this github.io project site; a text sitemap is the simplest thing a crawler can read, and robots.txt names both)
  files['sitemap.txt'] = [...files['sitemap.xml'].matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1]).filter((u) => !u.includes('/img/')).join('\n') + '\n';
  return { files, report, all };
}

// line ends don't count: a Windows checkout (Git's autocrlf) has CRLF
export const same = (a, b) => a.replace(/\r\n/g, '\n') === b.replace(/\r\n/g, '\n');
/** Writes every file; returns the report. */
export function write(opts) {
  const r = build(opts);
  for (const [f, s] of Object.entries(r.files)) { const p = join(opts?.site || SITE, f); mkdirSync(dirname(p), { recursive: true }); if (!existsSync(p) || !same(readFileSync(p, 'utf8'), s)) writeFileSync(p, s); }
  return r;
}

// ------------------------------------------------------------ command line
if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  const arg = (n) => { const i = process.argv.indexOf(n); return i > 0 ? process.argv[i + 1] : null; };
  const uniq = (list) => [...new Map(list.map((x) => [`${x.lang}|${x.group}|${x.key}`, x])).values()];
  if (process.argv.includes('--prune')) {
    // takes out of tools/i18n/<lang>.json the translations of texts the site no longer has (the English changed or went)
    const { report } = build(), used = new Set(report.used.map((u) => u.key)), dicts = loadDicts();
    for (const L of OTHER) {
      let n = 0;
      for (const g of Object.values(dicts[L.code])) for (const k of Object.keys(g)) if (!used.has(k)) { delete g[k]; n++; }
      writeFileSync(join(I18N, `${L.code}.json`), JSON.stringify(dicts[L.code], null, 1) + '\n');
      console.log(`${L.code}: ${n} old translations taken out`);
    }
  } else if (process.argv.includes('--merge')) {
    // <dir>/<lang>-out/<group>[.<part>].json, as translators write them, into tools/i18n/<lang>.json (a value there is replaced);
    // --from fix reads <lang>-fix/ instead (a reviewer's corrections)
    const dir = arg('--merge'), from = arg('--from') || 'out';
    for (const L of OTHER) {
      const out = join(dir, `${L.code}-${from}`);
      if (!existsSync(out)) continue;
      const f = join(I18N, `${L.code}.json`), d = existsSync(f) ? JSON.parse(readFileSync(f, 'utf8')) : {};
      let n = 0;
      for (const file of readdirSync(out).filter((x) => x.endsWith('.json')).sort()) {
        const g = file.split('.')[0];
        for (const [k, v] of Object.entries(JSON.parse(readFileSync(join(out, file), 'utf8')))) if (typeof v === 'string' && v) { (d[g] ||= {})[k] = v; n++; }
      }
      mkdirSync(I18N, { recursive: true });
      const sorted = Object.fromEntries(Object.keys(d).sort((a, b) => (a === 'shared' ? -1 : b === 'shared' ? 1 : a === 'js' ? -1 : b === 'js' ? 1 : a.localeCompare(b))).map((g) => [g, d[g]]));
      writeFileSync(f, JSON.stringify(sorted, null, 1) + '\n');
      console.log(`${L.code}: ${n} translations merged into tools/i18n/${L.code}.json`);
    }
  } else if (process.argv.includes('--todo')) {
    const out = arg('--todo'), { report } = build();
    mkdirSync(out, { recursive: true });
    const counts = new Map();
    for (const u of report.used) { const k = u.key; const g = counts.get(k) || new Set(); g.add(u.group); counts.set(k, g); }
    for (const L of OTHER) {
      const todo = {};
      for (const m of uniq(report.missing.filter((x) => x.lang === L.code).concat(report.bad.filter((x) => x.lang === L.code)))) {
        const g = m.group === 'js' ? 'js' : counts.get(m.key).size > 1 ? 'shared' : m.group;
        (todo[g] ||= {})[m.key] = '';
      }
      writeFileSync(join(out, `${L.code}.json`), JSON.stringify(todo, null, 1) + '\n');
      console.log(`${L.code}: ${Object.values(todo).reduce((n, g) => n + Object.keys(g).length, 0)} units to translate`);
    }
  } else {
    const check = process.argv.includes('--check');
    const r = check ? build() : write();
    const miss = uniq(r.report.missing), bad = uniq(r.report.bad);
    let stale = [];
    if (check) stale = Object.entries(r.files).filter(([f, s]) => !existsSync(join(SITE, f)) || !same(readFileSync(join(SITE, f), 'utf8'), s)).map(([f]) => f);
    for (const L of OTHER) {
      const m = miss.filter((x) => x.lang === L.code).length, b = bad.filter((x) => x.lang === L.code).length;
      console.log(`${L.code}: ${r.all.length} pages, ${m} missing, ${b} with wrong placeholders`);
    }
    for (const b of bad.slice(0, 20)) console.log(`  bad ${b.lang} ${b.group}: ${b.key.slice(0, 90)}`);
    if (stale.length) console.log(`out of date (run node tools/site-i18n.mjs): ${stale.join(', ')}`);
    if (check && (miss.length || bad.length || stale.length)) process.exit(1);
  }
}
