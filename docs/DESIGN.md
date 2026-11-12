# Design: one system for the app, the site, the dashboard and the README

The app is the source. Its colors live in [`src/Theme.xaml`](../src/Theme.xaml); the site, the stats dashboard and the
README's pictures never type them by hand: they read them as CSS tokens (`--t-…`). The pictures of the app on the site,
in the README and on the dashboard all come from one script, from the app as it is now. Two sets of tests fail when
something is left behind (see [Checks](#checks)).

## Tokens

### Colors (from `src/Theme.xaml`)

`node tools/design-tokens.mjs` reads `Theme.xaml` and writes these between `/* tokens` and `/* /tokens */` in every file
listed in `FILES` there. Values as of 5 Nov 2026:

| Token | Theme.xaml key | Value | What for |
|---|---|---|---|
| `--t-bg` | `BgColor` | `#0e0e0e` | the window, the page |
| `--t-side` | `Side` | `#0b0b0b` | the app's sidebar |
| `--t-edge` | `Edge` | `#050505` | the dark rim outside a group box |
| `--t-surface` | `SurfaceColor` | `#171717` | group boxes, cards |
| `--t-surface2`, `--t-surface3` | `Surface2`, `Surface3` | `#1d1d1d`, `#262626` | rows, fields, hover |
| `--t-line`, `--t-line-strong` | `Line`, `LineStrong` | `#2a2a2a`, `#383838` | borders; button and input borders |
| `--t-text`, `--t-muted`, `--t-faint` | `Text`, `Muted`, `Faint` | `#dddddd`, `#adadad`, `#888888` | body text, secondary, hints |
| `--t-good`, `--t-warn`, `--t-bad` | `Good`, `Warn`, `Bad` | `#7cc95a`, `#e3bf4b`, `#f05a4f` | protected / attention / blocked, banned |
| `--t-accent` | `AccentColor` | `#5ba8f0` | links, the selected page, Change IP and rejoin |
| `--t-accent-light`, `--t-accent-dark` | `AccentLightColor`, `AccentDarkColor` | `#7ebcf5`, `#3e87d0` | hover, the accent gradient |
| `--t-accent-deep` | `AccentDeepColor` | `#2b74c2` | web only: filled buttons with white text, the accent on light pages |
| `--t-top1`, `--t-top2`, `--t-top3` | `Top1`…`Top3` | `#3bb4e5`, `#c64fc6`, `#d8e03a` | the line at the top of the window: blue, magenta, yellow |
| `--t-top` | | `linear-gradient(90deg, top1, top2 50%, top3)` | that line, ready to use |
| `--t-accent-rgb` | `AccentColor` | `91,168,240` | `rgba(var(--t-accent-rgb), .1)` |

The app also has keys with alpha (`AccentSoft`, `GoodSoft`, `BadSoft`, …) and the button fills (`BtnFill`…); the web makes
its soft colors with `color-mix(in srgb, var(--t-good) 12%, transparent)` or `rgba(var(--t-accent-rgb), …)`.

### Each page's own names

Each page keeps short names of its own; the shared ones must be `var(--t-…)` (the table is `WEB` in `tools/design-tokens.mjs`).

| Token | Site (`site/index.html`, `404.html`, guides) | Dashboard `/data` (`style.css`, v4) | Dashboard `/dashboard` (v5, dark) | README card (`social-preview.css`) |
|---|---|---|---|---|
| `--t-bg` | `--bg` | `--bg` | `--bg` | `--bg` |
| `--t-surface` / `surface2` | `--bg2` / `--bg3` | `--panel` | `--card` / `--card2` | `--surface` / `--surface2` |
| `--t-line` / `line-strong` | `--line` / `--line2` | `--line` / `--line2` | `--line` / `--line2` | `--line` / `--line2` |
| `--t-muted` / `faint` | `--ink2` / `--ink3` | `--ink2` / `--ink3` | `--ink2` / `--ink3` | `--muted` / `--faint` |
| `--t-accent` | `--accent`, `--route` | `--acc` | `--acc`, `--acc-bar` | `--acc` |
| `--t-accent-deep` | `--sand` | `--acc-d` | `--acc` in the light palette | |
| `--t-good`, `warn`, `bad` | (used directly) | `--good`, `--warn` | `--good`, `--warn`, `--bad`, `--live` | `--good`, `--warn`, `--bad` |
| `--t-top` | the page's top line | `--gradient` | the logo square (`.mark`) | `--top1`…`--top3` |

Kept on purpose, page by page: the text white (site `#e8e8e8`, dashboards `#ededed`, README `#f2f2f2`: a little brighter
than the app's `#dddddd` on large screens); the dashboard's charts, heat map and its other palettes (`quente`, `contraste`);
the v5 page's light "Papel" palette (its greys, green and red are made for a white page; only its blue is the app's
`accent-deep`); the README SVGs' greys (`docs/assets/src/components.mjs`, tuned for GitHub's light and dark pages; their
blue, green, red and top line come from `Theme.xaml`).

### Type

| | Text | Titles | Numbers, IPs, code |
|---|---|---|---|
| App | Segoe UI Variable Text, Segoe UI; 12 px, buttons 12, small 11 | the same, 16 to 20 px | Cascadia Mono, Consolas |
| Site | Funnel Sans (`--sans`), then Segoe UI Variable | Funnel Display (`--disp`) | Fragment Mono (`--mono`); the copies of app text use Cascadia Mono |
| Dashboards | Segoe UI Variable Text, system-ui (`--font`) | Segoe UI Variable Display (`--font-d`) | tabular numbers |
| README pictures | Segoe UI Variable Text | Segoe UI Variable Display | Cascadia Mono |

Icons: Segoe Fluent Icons in the app; the site and the dashboard draw their own SVGs.

### Shape and space

| | Corners | Borders | Spacing |
|---|---|---|---|
| App | square (no `CornerRadius`; the window chrome is 0) | 1 px `Line`; group boxes 1 px `Edge` outside, 1 px `Line` inside, the title on the line | group padding 14,16,14,12, 12 between groups; buttons 28 px high, padding 14,0, 6 apart |
| Site | 999px pills (buttons, chips), 8 to 16 px cards, 2 to 6 px small parts | 1 px `--line` | `--gutter: clamp(1.25rem, .4rem + 4.2vw, 5rem)`, `--max: 88rem`, top bar `--top-h: 4rem` |
| Dashboard v5 | `--r: 14px` cards, 10 px tabs, 999px chips | 1 px `--line` | `--gap: 20px` (14 px on phones) |
| README pictures | 8 to 10 px frames around the captures, 8 px nodes | 1 px grey edge | |

The line at the top (blue, magenta, yellow, `TopLine`) is the one shape all four share: 3 px in the app (2 px on its
toasts), 3 to 5 px on the web.

On the site (6 Nov 2026): where the line is drawn in SVG (the footer's name, its horizon and reflection), its middle stops
are `color-mix(in oklch, top1, top2)` and `color-mix(in oklch, top2, top3)`, because SVG blends in sRGB and the plain
three stops go muddy; CSS gradients of the line use `in oklch` for the same reason. The marks on the app window (Features)
are 1.5 px `--t-accent` rings with a numbered `--t-accent` dot, and the same numbers in the page's text. The 3D scene's
labels sit on a solid `--t-bg` (no blur behind them). The loading screen (home, guides, 404) is `--t-bg` with the top line, the name and a 2 px `--t-top` bar on a `--t-line` track.

### Where they differ, and what was aligned (5 Nov 2026)

Aligned: the v5 dashboard typed the app's colors by hand, and its dark palette had near copies (`#151515` for `#171717`,
`#252525`/`#333333` for the lines, `#b4b4b4`/`#8c8c8c` for the greys): it now takes the tokens. The README card
(`social-preview.css`), `base.css` and `components.mjs` typed the same values by hand: they now read them too (no picture
changed). The dashboard's click map (`ui-map.js`) had five buttons in the beta.10 places and no box for Show all and What the app changed.

Different on purpose (bigger than a token, a design call): the site's fonts (Funnel) and its round buttons (pills, filled
accent) against the app's Segoe and square buttons; the web's brighter text white. Change these only with the owner.

## Logo

A disc crossed by one jump: the connection leaves the ground (light blue), crosses the sky (deep blue) and lands on a new
point, the carrier's IP (a yellow core in a deep blue ring). Chosen by the owner on 6 Nov 2026 after three rounds and three
independent critics (the study, the sketches and the scores are in `publish/logo-2026-11-06/`, outside git).

| Part | Colour | Theme.xaml key |
|---|---|---|
| the ground (lower piece) | `#5ba8f0` | `AccentColor` |
| the sky (upper crescent) and the ring round the point | `#2b74c2` | `AccentDeepColor` |
| the point's core | `#d8e03a` | `Top3` |

Rules: a free shape on a transparent background (no tile, no square behind it), no glow, no shadow, no gradient, no mask;
flat colours only from the table; it must read in one colour (the point becomes a hole). The yellow never touches the
background (it sits in its ring), so the same `.ico` works on light and dark taskbars. Drawn on a 32 grid: the disc's top on
y 2, the cut 4 wide (2 px at 16), a clear gap of 2 round the point, no tip thinner than 2.2. 16 and 24 px have their own
drawings, fitted to whole pixels (`logo-16.svg`, `logo-24.svg`); the other sizes are the master.

`node tools/logo.mjs` makes every file from `tools/logo-shape.mjs` (the numbers) and the three keys above (Playwright and
paper.js 0.12.18, see the top of the script; `--out <folder>` is a trial):

| File | Where it is used |
|---|---|
| `docs/assets/logo.svg` (master), `logo-black.svg`, `logo-white.svg` | the README card's signature; one-colour uses |
| `docs/assets/logo-16.svg`, `logo-24.svg` | the small sizes; `logo-24.svg` is also drawn by the window (below) |
| `docs/assets/app.ico` = `src/app.ico` | the exe, the taskbar, Alt+Tab and the tray: 16, 20, 24, 32, 40, 48, 64, 256 |
| `docs/assets/icon.png` (256) | the README header; the site's `img/icon.png` (header, loading screen, guide window bars) |
| `docs/assets/favicon.svg`, `favicon.ico`, `apple-touch-icon.png` | the site's `<head>` (the Pages workflow copies them to `img/`) |

The window draws the logo in the empty corner above Connection (`SideLogo` in `src/MainWindow.xaml`, 24 px, the paths of
`logo-24.svg` with the theme's brushes): change both together. The README card (`docs/assets/src/social-preview.html`)
signs with `logo.svg` in its lower left corner, and so does `banner.png` (`docs/assets/src/banner.html`, `render.cmd`). The
README's animated demo (`demo.gif`, `demo-mobile.gif` and their `.png` stills) draws `logo-24.svg` (desktop) and `logo.svg`
(phone) in its header; it is made outside git (`publish/readme-gif-2026-11-05/LEIA-ME.md`), so record it again when the logo
changes. The GitHub avatar (`publish/logo-2026-11-06/avatar-460.png`) is uploaded by
hand. `tests/LogoTests.cs` fails if any of these stop being the same logo, or if the logo gains a background, glow, mask or a
colour outside the table.

README badges: one row under the title, at most 6, all from shields.io in the flat style with the grey label and
`AccentDeepColor` (`2b74c2`) as the value colour; the build badge keeps its pass or fail colour. Release and downloads read
GitHub live; the VirusTotal badge sits in a `<!--rel:vtbadge-->` mark that `node tools/site-pages.mjs` fills from
`site/release.json`. `tests/DesignSyncTests.cs` checks all of this.

## App layout

Where each thing goes in the app, so every page reads the same way. `tests/OrganizacaoTests.cs` reads the two tables
below and fails when `src/MainWindow.xaml` leaves them: a group out of order or in the wrong column, a Settings option in
another group, a name used twice, a column that ends short, a red button that is not last, or a text that points to a
place that is not there.

### Four page shapes

1. **List** (Servers, Regions, Accounts, Log): a group on top for what you do before the list (add, choose, the options
   that only change this page), then the list group, named after the page, down to the bottom. The list's bar: search
   first, then filters, the count, view and order, then the actions on the whole list. Log has nothing to add, so it is
   the list alone.
2. **Two columns** (Connection, Configs): left is what you do, right is what you look at and undo, with the history
   (Previous IPs, Backups) last.
3. **Options** (Settings): left is how you play (protection, mobile data, the IP, the commands and their keys); right is
   what is around the game (programs, flags, Steam), then the app; Remove the app last. A new group goes next to the
   group it changes.
4. **Editor** (Profiles): a narrow column for the template (260 to 320 px), a wide one for what it changes, Apply at
   the bottom right.

In two columns and options the columns are equal, 12 px apart, and end on one line: the last group of each column takes
the rest of the height (since beta.9), so a list or a short group goes last, not a long text.

### Settings, or the page?

- An option that only changes one page lives on that page, in its top group (On login on Accounts, Refresh every 30 s on
  Servers, Sync on Configs).
- An option that changes the app, or more than one page, lives in Settings. When it fits two groups, it goes in the
  group of the thing it changes.
- One option, one place. A command can show in more than one place (New IP + next account in the top bar and on
  Accounts); an option can't. Another page can point to it (the gear on Accounts opens Settings > New IP + next account,
  the grey line under the mode on Connection names Settings > Mobile data).
- A button that only makes sense sometimes shows only then (Show hidden, while some accounts are hidden).
- A button that shows something switches back with the opposite name (Show hidden, then Hide hidden), never a one-way
  button that disappears.
- A command's name says every step a click runs (New IP + atlas + rejoin), so it never does more than it says.
- A state that exposes the home IP on purpose (Settings > Use the app with protection off, with protection off) is red
  in the top bar and in Status for as long as it lasts: "Home IP: servers see it".
- A group has something to do in it. Text that only explains goes under the option it explains (the grey line) or in its
  tooltip. The one exception is How it works, on Connection.

### Inside a group

- Most used first; a red (`Danger`) button last.
- In Settings, at most 7 things to click or fill in a group; with more, split it.
- A group name is 1 to 5 words, no brackets, used once in the whole app. A group takes a page's name only when it is
  that page's list.
- Point with names, not places: `Page > Group`, `Page > Group > Option` or `Page > Option`, as they are on screen. Never
  "above", "below" or "on the right" for another group (the columns move with the window). Outside the app (the phone,
  Steam, CS2, Windows, GitHub) the texts only name these: `About phone`, `Developer options`, `System`,
  `Network & internet`, `Additional settings`, `Connection & sharing`, `Connections`, `Personal Hotspot`, `Security`,
  `Game`, `Apps`, `Installed apps`, `General`. A new one goes here first.
- The page frame: 16 px at the sides and 18 px under the last group (`16,0,16,6` plus the group's 12; `16,0,16,18` when
  the page is one group or its groups have no margin).

### Pages

| Page | Element | Shape | Left (or only) column | Right column |
|---|---|---|---|---|
| Connection | `PageNet` | two columns | Status, Phone, What goes through the phone | Checks, How it works, Previous IPs |
| Servers | `PageServers` | list | Add servers, Servers | |
| Regions | `PageRegions` | list | Matchmaking regions, Regions | |
| Accounts | `PageAccounts` | list | Add and log in, Accounts | |
| Configs | `PageConfigs` | two columns | Copy, Sync, Snapshots | Account configs, Compare, Backups |
| Profiles | `PageProfiles` | editor | Template | Apply to accounts |
| Log | `PageLog` | list | Log | |
| Settings | `PageSettings` | options | Protection, Mobile data, Change IP, New IP + next account, Hotkeys and sounds | Open with CS2, Player flags, Steam, Privacy, App, Maintenance, Remove the app |

### Settings, option by option

Every named option in Settings (a switch, a box, a list to pick from) is in this table, in its group. The lists (flags,
programs) and the buttons are not named here.

| Group | Options |
|---|---|
| Protection | `LockCs2Box`, `SteamEgressBox`, `UseOffBox`, `ProtectOnStartBox`, `CleanupBox` |
| Mobile data | `KeepOffBox`, `AutoModeBox`, `DataWarnBox` |
| Change IP | `MethodCombo`, `OffSecondsBox`, `AttemptsBox`, `New16Box`, `GoodLinkBox` |
| New IP + next account | `SwIpBox`, `SwAccountBox`, `SwRejoinBox`, `SwOpenWithBox` |
| Hotkeys and sounds | `HotkeySwitchBox`, `HotkeyRejoinBox`, `SoundsBox` |
| Open with CS2 | |
| Player flags | `AutoJoinBox` |
| Steam | `ApiKeyPwd`, `ApiKeyBox`, `LaunchOptsBox` |
| Privacy | `OptStreamer`, `AutoStreamerBox` |
| App | `LanguageBox`, `StartWithWindowsBox`, `CloseToTrayBox`, `CheckUpdatesBox`, `TestVersionsBox` |
| Maintenance | |
| Remove the app | |

To move or add an option: change it here and in `MainWindow.xaml` in the same step, and the texts that point to it
(the test lists them).

### First run, notes and drawn phone screens (10 Nov 2026)

- **Texts.** Every visible text added since then lives in `src/Texts.cs`, and the XAML reads it with
  `{x:Static local:Texts.Name}`, so the app can be translated in one place. `tests/OrganizacaoTests.cs` reads them the same way.
- **The tour** (`TourPane`, `src/Views/MainWindow.Tour.cs`): the window under a `#B0000000` shade (the same as the dialogs)
  with a hole 6 px larger than the real control it points at, a 1 px `Accent` ring round the hole, and a `DialogGroup` box
  360 px wide, 14 px from the control (right, else below, else above, else left) and at least 12 px inside the window.
  4 to 6 stops; the buttons are Skip tour, Back and Next, the last stop's are Later and Open Phone setup. Nothing in it
  is clickable but its box.
- **A note inside a group** (the Regions note): `Surface2` with a 2 px left border, `Warn` for a warning, `Accent` for what
  the app offers; buttons on its right. One note per group.
- **Drawn phone screens** (Phone setup's USB debugging, `src/Views/PhoneGuide.cs`): square like the rest of the app,
  132x150, `Bg` with a 1 px `LineStrong` border and a short `Line` bar for the earpiece. Only the line to tap has words
  (`AccentSoft`, 2 px `Accent` left border, its mark in `Accent`: "tap 7×", a switch that is on, or Allow); the other
  lines are `Surface3` bars, since they differ from model to model. Three screens in a row, the step's text under each.

## Captures

`node tools/shots.mjs` takes every capture of the app in one run, from the demo of the dev build (needs `src/Privado`;
the Debug build has no UAC prompt). The window is 1240x800:

- `docs/screenshots/<page>.png` at **2x** (2480x1600, `--scale 2`): the site (the Pages workflow copies them to `img/`
  with a WebP each; the pages show them at 1240x800, so they are sharp on 2x screens; the 3D monitor shows `connection`);
- `docs/assets/shots/`: the framed copies for `README.md` and `docs/FEATURES.md` (`tools/readme-shots.py`, also at 2x). The
  six `crop-*` panels of the README's Features carry a 30 px bar on top, like the app's title bar: the 2 px top line and
  the feature's name in `Text` on `BgColor` (Segoe UI Semibold 13 px). The two of a pair (Protection | Change IP,
  Regions | Configs) keep the same shape; Servers and Accounts are wide strips;
- `docs/assets/src/social-preview-status.png` and `docs/assets/social-preview.png`: the README/og card, rendered with
  Chrome (upload it by hand on GitHub: Settings > General > Social preview);
- `worker/stats/dashboard/ui/<page>.png` at 1x: the dashboard's click map (1x so the Worker bundle stays small), packed by
  `tools/dashboard-assets.mjs`. The button boxes of each page go to `publish/ui-mapa/*.json`, and the script lists the
  buttons of `ui-map.js` that moved: fix those by hand;
- `docs/assets/shots.json`: the manifest. The app version, a fingerprint of every `src/*.xaml` and `src/Views/*.xaml`, and
  each file's size and SHA-256.

The 3D posters and stills of the site (`site/img/poster-*.webp`, `world-tower.webp`, `world-plug.webp`) show the
Connection capture on the monitor. `node tools/site-renders.mjs` renders them from the live scene: it serves `site/` as
the Pages workflow builds it (on a free port, with a lab page that has only the scene for the stills), shoots them with
Playwright's Chromium on the GPU, makes the WebP with ffmpeg and then runs `node tools/shots.mjs --renders`, which records
which `connection.png` they show. It needs Playwright with its Chromium (`npm i playwright && npx playwright install
chromium` in any folder, `PLAYWRIGHT=<that folder>` when it is not the repo or the global npm) and ffmpeg. `--all` also
renders the stills without the monitor (mast, racks, iPhone, Android), `--out <folder>` is a trial and `--serve` only
serves the site and the lab to look at.

`node tools/shots.mjs --out <folder>` is a trial run that changes nothing in the repo.

## The site on a phone

Under 760 px, and on a phone held on its side (up to 999 px wide and 540 px tall), the home has its own parts: the menu
button with every section, the app as six tabs with a close-up of each page and More details (`assets/js/mobile-tour.js`,
`assets/mobile-tour.css`), Android | iPhone as a switch of two equal halves, and each guide opens with its contents.
Rules, checked by `node --test tools/site-touch.test.mjs` with taps and flicks (10 Nov 2026):

- every `:hover` style sits in `@media (hover:hover)`, so a phone, which keeps `:hover` on what was tapped, leaves nothing
  lit; a `:focus-visible` that shared the rule stays outside it;
- `html` has `scroll-padding-top: calc(var(--top-h) + 1rem)`, so a heading's `scroll-margin-top` never adds the header
  again;
- every target is at least 44 px; a label never breaks inside a word;
- by touch the beats' cards snap with `proximity` and `snap-stop: always`: a flick rests on a card, never between two and
  never back against the finger. A hard flick can pass a card (Chrome keeps snap-stop only when it snaps); the plain
  sections scroll freely;
- a step picked on the rail or the chip rests on that step's shot (`A[i].s` in `home.js`), not where its anchor would go.

## The site in other languages

English is the site (x-default). `site/pt-br/`, `site/es/` and `site/ru/` (Brazilian Portuguese, Spanish for Latin
America, Russian; chosen 10 Nov 2026 from where the real users came from) are made by `node tools/site-i18n.mjs` from the
English pages and `tools/i18n/<lang>.json`; never edit them by hand. `node tools/site-pages.mjs` runs it after the guides.
Same design, same `assets/`, same 3D script; only the words change.

- A text is looked up by its English words. Change the English, and that text has no translation until
  `tools/i18n/<lang>.json` has one: `node tools/site-i18n.mjs --todo <folder>` lists what is missing,
  `--merge <folder>` takes the answers in, `--check` (and `node --test tools/site-i18n.test.mjs`) fails until then.
- Inline tags in a text are placeholders (`<a1>…</a1>`, `<code2/>`): a translation keeps every one. Version, size, dates
  and the VirusTotal count are `{version}`, `{size}`, `{date}`, `{vt}`, filled at build time in the language's format.
- A text a page's script shows is marked `/*t*/'…'` in the page; the shared scripts call `T('…')` and read
  `window.__i18n`, and reach the site's files through `window.__root` (`../` in a language folder).
- What stays English: the app's name and everything the app shows (pages, buttons, options: the app is in English);
  code and commands; the 3D scene's own words; the addresses (the same slugs in every language). Windows, Android and
  iPhone menus keep their English name, with the phone's own name in that language in brackets the first time.
- The picker: a globe and the language's code in the header, before the GitHub button, as quiet as the nav links; on a
  phone (the same breakpoint as the phone layout) it moves into the menu; the four names, each in its own language, sit
  in the footer's last line. No redirect by IP or by browser: an English page offers the browser's language once, in a
  pill at the bottom that can be closed, and a picked or closed choice is remembered (`cs2-lang`).
- The 404: GitHub Pages serves the English one everywhere, so under a language folder it opens that language's 404 and
  puts the address that was asked for back in the address bar.

## Checks

- `CI=true dotnet run --project tests` (`tests/DesignSyncTests.cs`): fails if a file's tokens are behind `Theme.xaml`, a
  shared name stops pointing at its token, a page types an app color by hand (or a near copy of the accent or the top
  line), the Worker serves an old CSS, map or capture; and if the captures are of another version or another XAML, a
  capture changed outside the script, one the site or the README shows is missing from the manifest, or the 3D posters
  show an older Connection.
- `node --test worker/stats/test/worker.test.mjs`: the same color checks for the site and the `/data` dashboard.
- `node tools/design-tokens.mjs --check` only checks the colors.
- `tests/OrganizacaoTests.cs`: fails when the app leaves [App layout](#app-layout) (a group's place, a Settings option's
  group, a name, a column, a red button, a text that points to a place that is not there).

## When

Any visual change to the app (a color, a text, a layout, anything in a `.xaml`) goes out in the same step as:

1. this file, if a token, a font, a size or a rule changed, and the App layout tables if a group or a Settings option moved;
2. `node tools/design-tokens.mjs` (if `Theme.xaml` changed);
3. `node tools/shots.mjs`, a look at the new captures, `ui-map.js` if it lists moved buttons, and `node tools/site-renders.mjs` (the 3D renders) if
   Connection changed;
4. the tests: `CI=true dotnet run --project tests` and `node --test worker/stats/test/*.test.mjs` (Node 24 on Windows
   refuses the folder alone); for the site also `node --test tools/site.test.mjs tools/site-touch.test.mjs
   tools/site-loading.test.mjs tools/world-3d.test.mjs`.

At every release the version changes, so the captures (the sidebar shows it) are taken again before the release commit.

### Languages (11 Nov 2026)

- The app is in English (the source and the fallback), pt-BR, es (neutral, Latin America) and ru. The language follows Windows the
  first time; Settings > App > Language changes it (it applies the next time the app opens).
- One mechanism, `src/Loc.cs`: the English text is the key, `src/Lang/<code>.json` hold the translations, and a text with no entry
  shows in English. XAML needs no marker: `Loc.Apply` swaps the text of each window once, when it opens. Texts the code builds go
  through `Loc.T("text")` and `Loc.F("text {0}", value)`. The Log, the telemetry and internal names stay in English
  (`Loc.En` gives the English back).
- Spanish and Russian run about 40% longer: size a box for the longest language, never for English. `SRT_PSEUDO=1` stretches every
  text 40% to check the layout.
- Menu names of Windows and Android that a text quotes are the ones the user sees on screen in that language.
- `tools/loc-keys.mjs` lists the keys, `tools/loc-merge.mjs` folds translations in, `tests/LocTests.cs` fails when a key is missing
  in a language or a Toast carries a raw literal.
