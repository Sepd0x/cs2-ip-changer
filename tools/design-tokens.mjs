// The colors of the site and the stats dashboard come from the app's src/Theme.xaml, the one source.
// This writes them as CSS tokens (--t-…) between "/* tokens" and "/* /tokens */" in the files below, and the home page's
// theme-color. The rest of each file uses var(--t-…); colors a page has only for itself (charts, the dashboard's other
// palettes) stay where they are. docs/DESIGN.md says which web name comes from which key.
//   node tools/design-tokens.mjs           write, then rebuild the guide pages and the dashboard assets if they changed
//   node tools/design-tokens.mjs --check   exit 1 if a file is behind Theme.xaml (the Worker tests check the same)
import { readFileSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const ROOT = fileURLToPath(new URL('../', import.meta.url));

// token name -> Theme.xaml key
export const TOKENS = [
  ['bg', 'BgColor'], ['side', 'Side'], ['edge', 'Edge'], ['surface', 'SurfaceColor'], ['surface2', 'Surface2'], ['surface3', 'Surface3'],
  ['line', 'Line'], ['line-strong', 'LineStrong'], ['text', 'Text'], ['muted', 'Muted'], ['faint', 'Faint'],
  ['good', 'Good'], ['warn', 'Warn'], ['bad', 'Bad'],
  ['accent', 'AccentColor'], ['accent-light', 'AccentLightColor'], ['accent-dark', 'AccentDarkColor'], ['accent-deep', 'AccentDeepColor'],
  ['top1', 'Top1'], ['top2', 'Top2'], ['top3', 'Top3'],
];

// The web's own names and the token each one is (the rest of their :root is their own).
export const WEB = {
  site: {
    '--bg': 'bg', '--bg2': 'surface', '--bg3': 'surface2', '--line': 'line', '--line2': 'line-strong', '--ink2': 'muted', '--ink3': 'faint',
    '--sand': 'accent-deep', '--route': 'accent', '--accent': 'accent',
  },
  dashboard: {
    '--bg': 'bg', '--edge': 'edge', '--panel': 'surface', '--line': 'line', '--line2': 'line-strong',
    '--acc': 'accent', '--acc-d': 'accent-deep', '--good': 'good', '--warn': 'warn',
  },
};

// The files with a block; min = the site's one-line CSS, css = the dashboard's spaced CSS.
export const FILES = [
  { path: 'site/index.html', style: 'min' },
  { path: 'site/404.html', style: 'min' },
  { path: 'worker/stats/dashboard/style.css', style: 'css' },
];

export const BLOCK = /\/\* tokens:[\s\S]*?\/\* \/tokens \*\//;
const META = /<meta name="theme-color" content="[^"]*">/;

/** Theme.xaml -> { key: '#rrggbb' }, only plain 6-digit colors (the keys with alpha or a StaticResource are left out). */
export function readTheme(xaml = readFileSync(ROOT + 'src/Theme.xaml', 'utf8')) {
  const out = {};
  for (const m of xaml.matchAll(/<Color x:Key="([^"]+)">#([0-9A-Fa-f]{6})<\/Color>/g)) out[m[1]] = '#' + m[2].toLowerCase();
  for (const m of xaml.matchAll(/<SolidColorBrush x:Key="([^"]+)" Color="#([0-9A-Fa-f]{6})"\/>/g)) out[m[1]] = '#' + m[2].toLowerCase();
  return out;
}

/** [[name, value]] in order, with the accent as r,g,b (for rgba) and the top line as a gradient. */
export function tokens(theme = readTheme()) {
  const t = TOKENS.map(([name, key]) => {
    if (!theme[key]) throw new Error(`src/Theme.xaml has no color ${key}`);
    return [name, theme[key]];
  });
  const v = Object.fromEntries(t);
  t.push(['accent-rgb', [1, 3, 5].map((i) => parseInt(v.accent.slice(i, i + 2), 16)).join(',')]);
  t.push(['top', `linear-gradient(90deg,${v.top1},${v.top2} 50%,${v.top3})`]);
  return t;
}

export function block(style, theme) {
  const t = tokens(theme);
  if (style === 'min') return `/* tokens: tools/design-tokens.mjs, from src/Theme.xaml */${t.map(([n, v]) => `--t-${n}:${v};`).join('')}/* /tokens */`;
  const rows = [];
  for (let i = 0; i < t.length; i += 6) rows.push('  ' + t.slice(i, i + 6).map(([n, v]) => `--t-${n}: ${v};`).join(' '));
  return `/* tokens: tools/design-tokens.mjs, from src/Theme.xaml. Don't edit here: change Theme.xaml and run it again */\n${rows.join('\n')}\n  /* /tokens */`;
}

/** The file's text with its block (and the home page's theme-color) as Theme.xaml says now. */
export function apply(text, style, theme = readTheme()) {
  if (!BLOCK.test(text)) throw new Error('no "/* tokens: … /* /tokens */" block');
  let out = text.replace(BLOCK, () => block(style, theme));
  if (META.test(out)) out = out.replace(META, `<meta name="theme-color" content="${theme.BgColor}">`);
  return out;
}

/** The files that are behind Theme.xaml. */
export function behind(theme = readTheme()) {
  return FILES.filter((f) => { const s = readFileSync(ROOT + f.path, 'utf8'); return apply(s, f.style, theme) !== s; }).map((f) => f.path);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const late = behind();
  if (process.argv.includes('--check')) {
    if (late.length) { console.error(`Behind src/Theme.xaml: ${late.join(', ')}. Run: node tools/design-tokens.mjs`); process.exit(1); }
    console.log('Tokens match src/Theme.xaml');
  } else {
    for (const f of FILES.filter((x) => late.includes(x.path))) writeFileSync(ROOT + f.path, apply(readFileSync(ROOT + f.path, 'utf8'), f.style));
    console.log(late.length ? `Written: ${late.join(', ')}` : 'Already up to date');
    const run = (tool) => { const r = spawnSync(process.execPath, [ROOT + tool], { stdio: 'inherit' }); if (r.status) process.exit(r.status); };
    if (late.includes('site/index.html')) run('tools/site-pages.mjs');
    if (late.includes('worker/stats/dashboard/style.css')) run('tools/dashboard-assets.mjs');
  }
}
