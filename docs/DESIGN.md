# Design: one set of colors

The app, the site and the stats dashboard share one set of colors. The source is the app's
[`src/Theme.xaml`](../src/Theme.xaml). The site and the dashboard never type those colors by hand: they read them as CSS tokens.

## Where each color comes from

`node tools/design-tokens.mjs` reads `Theme.xaml` and writes the tokens (`--t-…`) between `/* tokens` and `/* /tokens */` in
`site/index.html`, `site/404.html` and `worker/stats/dashboard/style.css`, and the home page's `theme-color`.
The guide pages copy the home page's style (`tools/site-pages.mjs`), so they get the same tokens.

| Token | Theme.xaml key | Site name | Dashboard name |
|---|---|---|---|
| `--t-bg` | `BgColor` | `--bg` | `--bg` |
| `--t-edge` | `Edge` | | `--edge` |
| `--t-surface` | `SurfaceColor` | `--bg2` | `--panel` |
| `--t-surface2` | `Surface2` | `--bg3` | |
| `--t-line` | `Line` | `--line` | `--line` |
| `--t-line-strong` | `LineStrong` | `--line2` | `--line2` |
| `--t-muted`, `--t-faint` | `Muted`, `Faint` | `--ink2`, `--ink3` | |
| `--t-good`, `--t-warn` | `Good`, `Warn` | | `--good`, `--warn` |
| `--t-accent` | `AccentColor` | `--accent`, `--route` | `--acc` |
| `--t-accent-deep` | `AccentDeepColor` (web only: filled buttons with white text) | `--sand` | `--acc-d` |
| `--t-accent-rgb` | `AccentColor` as `r,g,b`, for `rgba(var(--t-accent-rgb),.1)` | | |
| `--t-top1`…`--t-top3`, `--t-top` | `Top1`…`Top3`, the line at the top of the window | body top line | |

The block also has `--t-side`, `--t-surface3`, `--t-text`, `--t-bad`, `--t-accent-light` and `--t-accent-dark`, for new rules.
The same table is `WEB` in `tools/design-tokens.mjs`.

Each page keeps its own colors where it needs them: the text grey of the site and the dashboard (`#e4e4e4`), the site's
button hover, the dashboard's charts, its heat map and its two other palettes (`quente`, `contraste`). The dashboard's
copy of the app (the sidebar and placeholders drawn over the captures) uses the tokens directly, so it stays the app's
color under every palette.

## Change a color

1. Change it in `src/Theme.xaml` (the app picks it up on the next build).
2. `node tools/design-tokens.mjs`: writes the tokens, then rebuilds the guide pages and the dashboard assets if needed.
3. `node --test worker/stats/test/worker.test.mjs`: the `design:` tests fail if a file is behind `Theme.xaml`, if a
   shared name stops pointing at its token, or if a page has a shared color (or a near copy of the accent or the top
   line) typed by hand.

`node tools/design-tokens.mjs --check` only checks.

## Captures

`node tools/shots.mjs` takes every capture of the app from the demo of the dev build (needs `src/Privado`; the Debug
build has no UAC prompt), at 1240x800:

- `docs/screenshots/`: README, `docs/FEATURES.md` and the site (the Pages workflow copies them to `img/`);
- `worker/stats/dashboard/ui/`: the dashboard's "Na app" page, with Log and Settings;
- `publish/ui-mapa/*.json`: every button's handler, text and box, to update `worker/stats/dashboard/ui-map.js` by hand.

Then it frames the README copies (`python tools/readme-shots.py`) and packs the dashboard (`node tools/dashboard-assets.mjs`).
`node tools/shots.mjs --out <folder>` is a trial run that changes nothing in the repo.

## When

At every release, before the commit: `node tools/design-tokens.mjs --check`, the Worker tests, and `node tools/shots.mjs`
if any screen changed. Look at the new captures before committing them.
