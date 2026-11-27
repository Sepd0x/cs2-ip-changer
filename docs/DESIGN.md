# Design: one system for the app, the site, the dashboard and the README

The app is the source. Its colors live in [`src/Theme.xaml`](../src/Theme.xaml); the site, the stats dashboard and the
README's pictures never type them by hand: they read them as CSS tokens (`--t-…`). The pictures of the app on the site,
in the README and on the dashboard all come from one script, from the app as it is now. Two sets of tests fail when
something is left behind (see [Checks](#checks)).

## Tokens

### Colors (from `src/Theme.xaml`)

`node tools/design-tokens.mjs` reads `Theme.xaml` and writes these between `/* tokens` and `/* /tokens */` in every file
listed in `FILES` there. Values as of 26 Nov 2026 (the palette of the new GUI: a neutral black base (violet until 26 Nov at night) and one accent, the
app blue; see [App layout](#app-layout)):

| Token | Theme.xaml key | Value | What for |
|---|---|---|---|
| `--t-bg` | `BgColor` | `#0b0b0c` | the window, the page |
| `--t-side` | `Side` | `#070708` | the app's sidebar and status bar |
| `--t-edge` | `Edge` | `#040405` | the dark rim of a dialog and a toast |
| `--t-surface` | `SurfaceColor` | `#131314` | cards, panels, a row under the mouse |
| `--t-surface2`, `--t-surface3` | `Surface2`, `Surface3` | `#1a1a1c`, `#242426` | the page picked, the filter picked, hover |
| `--t-line`, `--t-line-strong` | `Line`, `LineStrong` | `#1e1e20`, `#2e2e31` | the thin lines (white at 7 % on the page); pill outlines (14 %) |
| `--t-text`, `--t-muted`, `--t-faint` | `Text`, `Muted`, `Faint` | `#ededef`, `#a9abb1`, `#8d9096` | body text, secondary, hints |
| `--t-good`, `--t-warn`, `--t-bad` | `Good`, `Warn`, `Bad` | `#46c58a`, `#f0a948`, `#ef6461` | protected / attention / blocked, banned |
| `--t-accent` | `AccentColor` | `#5ba8f0` | the one accent: the filled button, links, ticks and switches, the allowed regions |
| `--t-accent-light`, `--t-accent-dark` | `AccentLightColor`, `AccentDarkColor` | `#74b6f3`, `#3e87d0` | hover |
| `--t-accent-deep` | `AccentDeepColor` | `#2b74c2` | web only: filled buttons with white text, the accent on light pages |
| `--t-top1`, `--t-top2`, `--t-top3` | `Top1`…`Top3` | `#3bb4e5`, `#c64fc6`, `#d8e03a` | the route's colours: blue, magenta, yellow |
| `--t-top` | | `linear-gradient(90deg, top1, top2 50%, top3)` | the web's top line, ready to use |
| `--t-accent-rgb` | `AccentColor` | `91,168,240` | `rgba(var(--t-accent-rgb), .1)` |

The app also has keys with alpha (`AccentSoft`, `GoodSoft`, `WarnSoft`, `BadSoft`, `BadDim`, …), the button fills (`BoxFill`,
`BtnFill`, `BtnFillHover`, flat neutral greys; no gradient but the top line since 26 Nov at night) and keys of its own that the web doesn't take:

| Theme.xaml key | Value | What for |
|---|---|---|
| `Info` | `#cfd1d6` | IPs and numbers next to text, a plain word button (`LinkBtn`) |
| `AccentText` | `#06111d` | the text on the filled accent button |
| `Field` | `#161617` | a field (`FieldBox`, `FieldCombo`, `FieldPwd`), the mode picked on Connection, a note |
| `AccentLine` | `#595ba8f0` | the accent at 35 %: the outline of the mode picked and of the Servers bars |
| `RouteMid`, `RouteOff`, `RouteDash` | `AccentColor`, `#6c6c72`, `#37373b` | the route on Connection: its middle dot; the grey rings and dashes while not protected (`RouteOff` also rings a tick or a switch that is off) |
| `Lift` | `#1d1d1f` | the row or card under the mouse or picked, the letter box, the tags, the Settings group in view |
| `LineSoft` | `#171719` | white at 4 %: the line between the rows of a table (Log, Profiles, Compare) |
| `TopLine` | `Top1`, `Top2` at 88 %, `Top3` | the window's 2 px top line, in full (42 % until 26 Nov at night); the toast's strip |
| `NavMark` | `AccentColor` (`Top1` to `Top2` until 26 Nov at night) | the 3 x 14 px mark of the page picked in the sidebar |

The web makes its soft colors with `color-mix(in srgb, var(--t-good) 12%, transparent)` or `rgba(var(--t-accent-rgb), …)`.

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

Kept on purpose, page by page: the text white (site `#e8e8e8`, dashboards `#ededed` and `#e4e4e4`: neutral greys, where the
the app's `#ededef` is neutral too since 26 Nov at night; the README card takes the app's); the dashboard's charts, heat map and its other palettes (`quente`, `contraste`);
the v5 page's light "Papel" palette (its greys, green and red are made for a white page; only its blue is the app's
`accent-deep`); the README SVGs' greys (`docs/assets/src/components.mjs`, tuned for GitHub's light and dark pages; their
blue, green, red and top line come from `Theme.xaml`).

### Type

| | Text | Titles | Numbers, IPs, code |
|---|---|---|---|
| App | Segoe UI Variable Text, Segoe UI (`UiFont`); 13 px on the pages, the line under a title 12.5, hints 12, buttons 12 SemiBold | Segoe UI Variable Display (`DisplayFont`), SemiBold: the page 22 px, a Settings group or a Profiles step 16, a section 14 | Cascadia Mono, Consolas (`Mono`): IPs, pings, times, keys; the IP servers see 40 px |
| Site | Funnel Sans (`--sans`), then Segoe UI Variable | Funnel Display (`--disp`) | Fragment Mono (`--mono`); the copies of app text use Cascadia Mono |
| Dashboards | Segoe UI Variable Text, system-ui (`--font`) | Segoe UI Variable Display (`--font-d`) | tabular numbers |
| README pictures | Segoe UI Variable Text | Segoe UI Variable Display | Cascadia Mono |

Icons: Segoe Fluent Icons in the app; the site and the dashboard draw their own SVGs. `Theme.xaml` also has `Numbers`
(Bahnschrift) for big figures; no page uses it yet. WPF has no letter spacing, so the app's small capitals ("IP SERVERS
SEE", "START AND CLOSE") have none.

### Shape and space

| | Corners | Borders | Spacing |
|---|---|---|---|
| App | buttons are 28 px pills (26 in the top bar), fields 28 px rounded 6, rows and the sidebar's pages 8, cards, panels and menus 10, tags 4; the window and its dialogs square | no boxes: a section is its title and a 1 px `Line` (`LineSoft` between table rows); 1 px `LineStrong` round a pill | the page frame 24,16,24,18; 22 to 28 px between sections; buttons 8 apart |
| Site | 999px pills (buttons, chips), 8 to 16 px cards (16 on a phone), 2 to 6 px small parts | 1 px `--line` | `--gutter: clamp(1.25rem, .4rem + 4.2vw, 5rem)`, `--max: 88rem`, top bar `--top-h: 4rem` |
| Dashboard v5 | `--r: 14px` cards, 10 px tabs, 999px chips | 1 px `--line` | `--gap: 20px` (14 px on phones) |
| README pictures | 8 to 10 px frames around the captures, 8 px nodes | 1 px grey edge | |

The line at the top (blue, magenta, yellow, `TopLine`) is the one shape all four share: 2 px in full across the top of the
app's window (and 2 px on its toasts), 3 to 5 px on the web. In the app it runs blue to magenta and the yellow only at its
end (88 %); on the web the magenta is at 50 %.

On the site (26 Nov 2026, the owner: gradients other than the top line read as AI slop): the three colours only on the page's
top line (the body's 2 px border, the header's reading line, the loading screen and line) and where the site shows the app's own
window (its 2 px top line on screenshots and close-ups, the route's key). Every other line, mark and underline is the flat
`--t-accent`: the open FAQ question, the VPN table's head, the first Setup number, the guides' underlines and headings, the
guide cards and the reading line of a guide's contents, the 404's rule, the peek card and the story rail. The footer's name is
drawn in `--t-accent` with no blur glow (its drawing, light pass and reflection stay). Nothing glows: the marks on the app
window are plain rings, and buttons have no light edge or coloured shadow. The scrims that keep text readable over the 3D
scene (a fade to `--bg`) and the masks that fade a strip's edge are not colour gradients and stay. Before 26 Nov the SVG name
used the line with middle stops `color-mix(in oklch, ...)`, because SVG blends in sRGB. The marks on the app window (Features)
are 1.5 px `--t-accent` rings with a numbered `--t-accent` dot, and the same numbers in the page's text. The 3D scene's
labels sit on a solid `--t-bg` (no blur behind them). A label's box stays off the objects (the tower, the screen, the phone,
the mast and its site, the racks, the router) and off the route's line: it picks its side for the shot at rest and may take a
longer stem (44, 76, 112 or 150 px; 30, 62, 98 or 136 on a phone) to clear them (10 Nov 2026, `tools/world-3d.test.mjs`).
The floor mirrors every object that stands on it, sharper near the camera's eye line and never above 9.5 m (the arc is
mirrored only where it comes down); the racks' lights are rows of short dashes with a halo of their own, not the bloom's. The loading screen is `--t-bg` with the top line, the name and a 2 px `--t-top` bar on a `--t-line` track, and since 26 Nov 2026 only on the home on a PC (a mouse). On a touch screen, and on every guide and the 404, there is no screen: the page shows at once over the poster, input is free, the same bar fills as a 3 px line at the very top (`html.ldp`), and the live scene fades in over the poster at `worldready` (on a phone the screen had held the page 4 to 9 s).

### Where they differ, and what was aligned (5 Nov 2026)

Aligned: the v5 dashboard typed the app's colors by hand, and its dark palette had near copies (`#151515` for `#171717`,
`#252525`/`#333333` for the lines, `#b4b4b4`/`#8c8c8c` for the greys): it now takes the tokens. The README card
(`social-preview.css`), `base.css` and `components.mjs` typed the same values by hand: they now read them too (no picture
changed). The dashboard's click map (`ui-map.js`) had five buttons in the beta.10 places and no box for Show all and What the app changed.

Different on purpose (bigger than a token, a design call): the site's fonts (Funnel) against the app's Segoe; the site's
filled buttons in `accent-deep` with white text against the app's in `accent` with dark text; the web's neutral text white.
Since 26 Nov 2026 both have round buttons (pills). Change these only with the owner.

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

The window draws the logo at the top of the sidebar, above Connection (`SideLogo` in `src/MainWindow.xaml`, 26 px, the
paths of `logo-24.svg` with the theme's brushes): change both together. The README card (`docs/assets/src/social-preview.html`)
signs with `logo.svg` in its lower left corner, and so does `banner.png` (`docs/assets/src/banner.html`, `render.cmd`). The
README's animated demo (`demo.gif`, `demo-mobile.gif` and their `.png` stills) draws `logo-24.svg` (desktop) and `logo.svg`
(phone) in its header; it is made outside git (`publish/readme-gif-2026-11-26-neutro/LEIA-ME.md`; recorded again on 26 Nov 2026 with the Dial colours and the name Pickroute, and that night with the neutral black), so record it again when the logo
changes. The GitHub avatar (`publish/logo-2026-11-06/avatar-460.png`) is uploaded by
hand. `tests/LogoTests.cs` fails if any of these stop being the same logo, or if the logo gains a background, glow, mask or a
colour outside the table.

README badges: one row under the title, at most 6, all from shields.io in the flat style with the grey label and
`AccentDeepColor` (`2b74c2`) as the value colour; the build badge keeps its pass or fail colour. Release and downloads read
GitHub live; the VirusTotal badge sits in a `<!--rel:vtbadge-->` mark that `node tools/site-pages.mjs` fills from
`site/release.json`. `tests/DesignSyncTests.cs` checks all of this.

## App layout

Where each thing goes in the app, so every page reads the same way. The look of the window and its eight pages (26 Nov
2026) was drawn as HTML mock-ups first and ported to WPF page by page; the mock-ups and the steps are kept outside git. `tests/OrganizacaoTests.cs` reads the two tables
below and fails when `src/MainWindow.xaml` leaves them: a section out of order or in the wrong column, a Settings option
in another group, a name used twice, a section drawn as a box, a red button that is not last, a frame or a page title that
is off, a list that is missing, a route colour on something that isn't live, or a text that points to a place that is not
there.

### The window

- A 2 px line across the very top, over the sidebar and the top bar: `TopLine` in full (it was at 42 % until 26 Nov at night), no glow.
- The sidebar, 104 px (168 px until 27 Nov at night, when the owner found it wide to the left and right of the icons), `Side` with a 1 px `Line` on its right (26 Nov at night, the owner: the logo was weak in its corner,
  the pages stuck under it and an empty black below; he asked for the logo and the version centred, the icon over the name, the
  pages under the logo with a clean gap, and Log and Settings at the foot): the logo centred on top, 40 px; 24 px of air; the six
  pages you use (Connection to Profiles); at the foot Log and Settings, then Update and the version, centred. Everything fits
  at the 660 px minimum height. A page: a 66 px row with a 58 px tile 8 px in from each side (`NavTile`), rounded 10, holding the icon
  (`Tag`, Segoe Fluent 22 px) over the name (12 px, 6 px under), `Muted`; under the pointer the tile in `Surface` and the name
  in `Text`; the page picked has the tile in `Surface2`, the icon in `Accent`, the name white SemiBold and a 3 x 32 px
  `NavMark` on the sidebar's left edge. With the keyboard the tile gets a 1 px `Accent` ring rounded 10 (`NavFocusRing`).
  The tile is the row less 4 px over and under. A window under 660 px (1366x768 at 125 % leaves 576, 1080p at 200 %
  leaves 516) takes the gap under the logo to 8 and shares the rest between the eight rows, down to 44 px (`NavFit`; 48 until 27 Nov, when Update showing at 1080p and 200 % still pushed Profiles under Log); under 56 px the tile keeps 2 px over and under and the name sits 2 px under the icon (`NavTileFit`, `NavNameGap`), so the icon never touches the tile, and
  Profiles never goes under Log (27 Nov).
- The keyboard focus ring (`FocusRing`, 1 px `Accent`, 2 px out) shows only after Tab or an arrow key, and goes at the next
  click or when the window is activated again (27 Nov, the owner: a blue rectangle stayed on the sidebar after Alt+Tab; WPF
  draws it whenever the last input was a key, Alt included). `FocusRingShown` in `Theme.xaml` holds it for every ring.
- The top bar, 40 px: "Pickroute / Page" in 13 px (`Muted`, the "/" in `Faint`, the page in `Text`), the activity pill
  while something runs, then on the right the state chip (24 px, rounded, a 10 % wash of the state's colour, its
  `StatusMark`, the state SemiBold and the IP in mono; a click goes to Connection), the quick switch (`TopPill`, 26 px:
  New IP + next account and the next account's name after a person mark, both in `Accent`) and the window buttons (`Chrome`).
  A dialog covers the page, not this bar (`Dialog`, Phone setup's `SetupPane` and the doors' `StartPane`, all 40 px from
  the top, so the bar keeps its colours and the window buttons stay clickable; 26 Nov 2026). A dialog box (`DialogGroup`: a question, Phone
  setup, the two doors, the tour) is square, `Surface` with the `Edge` rim and a `LineStrong` line inside it, its title inside
  the box (`DialogTitle`: 16 px Display SemiBold on a 20 px line, 8 px over the text), the X 8 px from its right edge; its
  overlay has `Look.Round`, so its buttons are the pages' pills and its one `Primary` is filled (26 Nov 2026: the title chip
  on the border and the square OK read as a Windows group box).
- The status bar, 28 px, `Side` with a 1 px `Line` above it, 24 px in: the phone as Connection > Phone shows it (its mark,
  the state SemiBold, the detail in `Muted`) and the mobile data, and on the right New IP + next account with its hotkey in
  a key box (Settings > Hotkeys and sounds). It only repeats what the app already shows. Its left part is a `DockPanel`: in a narrow window the phone's detail and the mobile data end in "..." (27 Nov; a `StackPanel` cut them in the middle of a word).

### The page

- The frame: 24 px at the sides, 16 on top, 18 under the last thing. A page that ends in a list scrolling to the window's
  edge has 0 at the bottom (Regions, Accounts: the list keeps its own end clear). Profiles scrolls whole and ends 24 under
  Apply. Settings: its group list has 24,16,12,16, the groups sit 20 px to its right with 32 at the right and the bottom.
- The page's name first, 22 px Display SemiBold, and under it one line in 12.5 px `Muted` that says where things stand
  (12 servers on 2 IPs · all protected; 8 of 8 · 2 banned · nightfall in use; 3 entries · no warnings or errors). The top
  bar names the page too, so nothing else repeats it as a title.
- A section is a title and a thin line, never a box: the title 14 px Display SemiBold on Connection and Configs, 16 px in
  Settings and on the steps of Profiles; a 1 px `Line` above a section that follows another (22 px apart on Connection,
  28 in Settings); the rows in it divided by `Line`, a table's rows by `LineSoft`. A list has no box round it: its head
  (26 px, 12 px `Faint`, a line under it) and its rows (a row under the mouse or picked in `Lift`, rounded 8).
- One filled button per page and state (`Primary`, `PrimaryFill`: the accent with dark text). On Connection: Turn on
  protection while it is off, Change IP, or Change IP and rejoin when there is a last server, and Phone setup while no
  phone is ready (see First run). Regions: Block slower. Servers: Join, on the row under the mouse or picked. Accounts:
  Log in, on the card under the mouse. Configs: Copy config…, and Copy to N accounts in its panel. Profiles: Apply to N
  accounts. Log: Save bug report. Settings has none. The rest are pills (`Pill`; `Btn` and `Primary` turn into pills on a
  page with `Look.Round`), plain words (`LinkBtn`) or a red word (`DangerLink`). A word at the right end of a line has a
  -8 px right margin, so its text ends where the pills end (its hover wash keeps the 8 px).
- The route's colours (`Top1` to `Top3`) only on the window's top line and the toast's strip, the brand's mark. Since 26 Nov at
  night (the owner: gradients elsewhere read as AI slop) the sidebar's mark, the route on Connection (`RouteOn`, `RouteMid`) and the
  dial's lit arc are the flat accent, and no other fill is a gradient. Everything else uses the one accent.
- A state shows with `StatusMark` (a filled square when all is well, other shapes for the rest, so it reads without its
  colour), not a dot. Green, amber and red only for a state, a ping against Regions > Ping under, or a ban.
- Fields are `FieldBox`, `FieldCombo` and `FieldPwd` (28 px, `Field`, rounded 6), ticks `Check` (a 14 px square) or
  `RoundCheck` (Regions), switches the Settings `Switch` (32 x 18), tabs `Tabs` (the game tabs on Regions), filters a capsule of
  radio buttons (the one picked in `Surface3`, SemiBold).

### Four page shapes

1. **List** (Servers, Regions, Accounts, Configs, Log): the title, a bar, then the list down to the bottom. What you do
   before the list sits on the title's line or in the bar (Add, Paste and Import on Servers; On login and Add on
   Accounts; the game tabs, Preset and the quick picks on Regions; the Sync line on Configs). The bar: search first, then
   filters, the count, view and order, then the actions on the whole list, the red one last. Regions has its dial on the
   right (`RegionDialView`); Configs opens Copy config… and Compare as a panel from the right over the page dimmed
   (`CfgCopyPanel`, 440 px; `CfgComparePanel`, 460), and keeps Backups and snapshots folded under the list. Log has nothing
   to add: the list, and its buttons under it.
2. **Two columns** (Connection): left is what you do (the state, the IP servers see, the route, the buttons, What goes
   through the phone), right is what you look at and undo (Checks, Phone, Previous IPs), How it works last.
3. **Steps** (Profiles): one page that scrolls, three numbered steps (a 26 px ring, a faint line down to the next), each
   with its title and one line: The look, Accounts, Review and apply. Apply at the end of the last one.
4. **Options** (Settings): the groups' names in a list on the left (`SetNav`, 196 px; the group in view lit in `Lift`, a
   click goes to it; over it, Find a setting, 28 px, whose results take the list's place while it has text: the name over
   its group, a click scrolls to it and blinks it twice; Ctrl+F on the page; a notification that names a group or an option
   gets Open Settings; each of the six pages has Settings for this page beside its title, `PageSettingsLink`: a 12 px gear
   and the words in `LinkBtn`, 10 px after the title, which opens Settings with that page's groups listed in the list's
   place, "Settings for Accounts" and All groups on top, then each group's name once in 11 px SemiBold capitals with its
   options under it, `PageSettingsGroups` in `MainWindow.SettingsFind.cs`), the groups one under the other on the right, at most 900 px wide (780 until 27 Nov); on a wide
   window the list and the groups sit together in the middle, the scroll bar at the window's edge (`SettingsPad`); the list
   scrolls when the window is short. The list picks the group under a line 40 px from the top, and in the last screen of
   scroll that line slides to the bottom, so the end picks the last group: how you play first (protection,
   mobile data, the IP, the commands and their keys), then what is around the game (programs, flags, Steam), then the
   app; Remove the app last, in red. A new group goes next to the group it changes. Each option is a row with the line
   above it: the name 13 px, the grey line in 12 px `Faint` under it, the switch or the field on the right.

### Page by page

- **Connection:** under the title, the mode and how the PC reaches the phone ("Game only · USB cable"). Left: the state
  (its mark, a 16 px title in the state's colour, one sentence), IP SERVERS SEE and the IP at 40 px mono with Copy, the
  route This PC > phone > Servers (each with one or two short lines: the home IP hidden, the phone and its carrier, what
  the servers see), the row of buttons with Turn off protection as a red word at its right end, then What goes through the
  phone (the four modes, the one picked in `Field` with an `AccentLine` outline). Right: Checks (only the rows that need
  you, unless Show all), Phone (its buttons on the title's line), Previous IPs, How it works (it folds).
- **Servers:** the paste box with Default port, Add, Paste, Import and Where to find IPs? on one line; the bar with the
  search, Refresh every 30 s, Refresh all, Copy IPs and Remove; the follow and slot bars when they show (`Field`, rounded 8,
  an `AccentLine` outline); the table Server, Map, Players 140, Ping 78, Route 112, Join 80 (46 px rows, Protected with its
  mark, cut with an ellipsis 8 px before Join; the ping in mono coloured against Regions > Ping under).
- **Searches:** 200 px on every page (Servers, Accounts, Configs, Log).
- **Regions:** the title's line counts (8 allowed · best 24 ms · block on), the list's source and Refresh on its right;
  the bar with the game tabs, Preset (a pill that opens the presets) and the pills Closest 3, Europe only and Allow all
  (block off); Ping under with Block slower; the regions by continent (a continent folds to one line), each with a round
  tick, its ping and a short bar; Only this region on the row under the mouse; the warning and the tip as one line each
  that opens; the dial on the right.
- **Accounts:** the title's line counts and says the account in use; On login (Open CS2, Start Steam minimized, Offline
  mode) and Add on its right (its arrow holds New IP + next account and Choose what New IP + next account does); the bar
  with the search, the filters, the cards or rows switch, Most recent, Refresh and Show hidden; cards 236 px wide
  (`Surface`, rounded 10, the account in use with an accent outline).
- **Configs:** the Sync line ("Sync: from main account nightfall · last run …") opens its menu (the four modes, When,
  What it copies, Sync now, Undo last sync); the bar with the search (from 8 accounts), the count, Compare and Copy
  config…; the table Account, Last change, Sync status, Gets sync (54 px rows, the crosshair in a 36 px box, … for more;
  a custom crosshair colour said as a word, the box draws it; with Sync off every Gets sync switch draws off at 40 % and keeps
  its value); Backups and snapshots folded under it (Backups, then Snapshots folded inside).
- **Profiles:** 1 The look (the template and its fields, the picture, Privacy; Copy the look of an account…), 2 Accounts
  (Pick All, Signed in, None and the count on the step's line; two columns of accounts with Sign in), 3 Review and apply
  (Fields to change, Now, After and Status for the accounts picked, Use this template on new accounts, Apply; Restore
  originals and Sign out of all as words under it).
- **Log:** the search and the filters on the title's line; rows of time (mono), mark and text (the entry SemiBold, the
  rest in `Info`); under a line, Copy all, What the app changed, Save bug report, Report a problem, and Clear screen as a
  word at the right.
- **Settings:** see Four page shapes and the table below.

### Settings, or the page?

- An option that only changes one page lives on that page, on its title's line or in its bar (On login on Accounts,
  Refresh every 30 s on Servers, Sync on Configs).
- An option that changes the app, or more than one page, lives in Settings. When it fits two groups, it goes in the
  group of the thing it changes.
- One option, one place. A command can show in more than one place (New IP + next account in the top bar and under
  Accounts' Add); an option can't. Another page can point to it (Choose what New IP + next account does, under Accounts'
  Add, opens Settings > New IP + next account; the grey line under the modes on Connection names Settings > Mobile data).
- A button that only makes sense sometimes shows only then (Show hidden, while some accounts are hidden).
- A button that shows something switches back with the opposite name (Show hidden, then Hide hidden), never a one-way
  button that disappears.
- A command's name says every step a click runs (New IP + atlas + rejoin), so it never does more than it says.
- A state that exposes the home IP on purpose (Settings > Use the app with protection off, with protection off) is red
  in the top bar's chip and in Connection's state for as long as it lasts: "Home IP: servers see it".
- A section has something to do in it. Text that only explains goes under the option it explains (the grey line) or in
  its tooltip. The one exception is How it works, on Connection.

### Inside a section

- Most used first; a red button (`Danger`, `DangerLink`) last in its row.
- In Settings, at most 7 things to click or fill in a group; with more, split it.
- A section's name is 1 to 5 words, no brackets, used once in the whole app. It takes another page's name only when it
  holds this page's list of that page's things (Profiles > Accounts, the accounts the look goes to).
- Point with names, not places: `Page > Section`, `Page > Section > Option` or `Page > Option`, as they are on screen.
  Never "above", "below" or "on the right" for another section (the columns move with the window). Outside the app (the
  phone, Steam, CS2, Windows, GitHub) the texts only name these: `About phone`, `Developer options`, `System`,
  `Network & internet`, `Additional settings`, `Connection & sharing`, `Connections`, `Personal Hotspot`, `Security`,
  `Game`, `Apps`, `Installed apps`, `General`. A new one goes here first.
- A part that opens and closes is an `Expander` with a style of its own: a section's title line with the chevron at its
  right (Connection > How it works, Configs > Backups and snapshots), a note's one line (the warning and the tip on
  Regions) or a pill that drops a panel (Regions > Preset). The chevron is 7 px, `Muted`, `Text` under the mouse; no
  circle, no box. Every control type a window uses has a style in `Theme.xaml`: one without it makes WPF load Windows'
  whole theme (the Expander cost about 250 ms at start, 19 Nov 2026; `tests/DesempenhoTests.cs`).

### Pages

The test knows a section by its style: `NetSection` and `NetFold` (Connection), `CfgFold` (Configs), `ProfStepTitle`
(Profiles, the whole step), `Group` (Settings; the box of the same name in `Theme.xaml` is only for what has no page of
its own). Frame is the margin of what holds the page's title; List is the page's list, under its search.

| Page | Element | Shape | Frame | List | Sections, left (or only) column | Right column |
|---|---|---|---|---|---|---|
| Connection | `PageNet` | two columns | `24,16,24,18` | | What goes through the phone | Checks, Phone, Previous IPs, How it works |
| Servers | `PageServers` | list | `24,16,24,18` | `ServerList` | | |
| Regions | `PageRegions` | list | `24,16,24,0` | `RegionList` | | |
| Accounts | `PageAccounts` | list | `24,16,24,0` | `AccountsPanel` | | |
| Configs | `PageConfigs` | list | `24,16,24,18` | `CfgAccountsList` | Backups and snapshots, Snapshots | |
| Profiles | `PageProfiles` | steps | `24,16,24,24` | `ProfList` | The look, Accounts, Review and apply | |
| Log | `PageLog` | list | `24,16,24,18` | `LogList` | | |
| Settings | `PageSettings` | options | `24,16,12,16` | | Protection, Mobile data, Change IP, New IP + next account, Hotkeys and sounds, Open with CS2, Player flags, Steam, Privacy, App, Maintenance, Remove the app | |

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
| Privacy | `OptStreamer`, `AutoStreamerBox`, `StatsBox` |
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
  is clickable but its box. The box stays 360 px in every language: "2 of 5" sits in its title (`Faint`, Cascadia Mono), and
  the buttons go to a second line when a language doesn't fit them (24 Nov 2026: ru, es and pt-BR cut them).
- **The first start's two doors** (`StartPane`, `src/Views/MainWindow.Start.cs`, 24 Nov 2026): where the tour opened by itself,
  a `DialogGroup` box 640 px wide over the same `#B0000000` shade, titled with the question. Two equal tiles in the `Door` style
  (`Surface2`, 1 px `LineStrong`, `Accent` border under the mouse or the keyboard, square), 12 px apart: a 112 px drawing of
  the page the door opens, made of the app's own parts (the Regions rows with real check boxes and green or red pings; the
  phone as Phone setup draws it with the home IP and the carrier's IP), the title 16 px SemiBold, one line in
  `Sub`, and the action in `Accent` with a chevron at the bottom. Show me around and Later on the right under them. One door
  = one page; nothing else is decided there.
- **The page the window opens on:** Connection, but Regions while no phone was ever checked on this PC (unless the phone door
  was picked). Until then Connection's first button is "Set up the phone" in grey, and Phone setup (on the title's line of
  Connection > Phone) is the page's filled button; the notification area menu says "Set up the phone" on that line too.
  The same with the phone lost and protection on: Change IP greyed out, Phone setup filled.
- **The page never changes by itself** (25 Nov 2026): the page the window opens on is picked before the window is on screen
  (`PickStartPage`, in the constructor), and after that only a click changes it. Later on the doors closes them and leaves
  the page as it is.
- **A second way in a Phone setup row** (Use the hotspot on the cable row) is a grey `Btn`; the row's main action stays `Primary`.
- **How Phone setup ends** (`SetupEnd`, 24 Nov 2026): one `Primary` button at most, on the right. A step still to do: Close
  alone, grey (`Btn`). Nothing left to do with protection off: Close grey and Turn on protection `Primary` (`SetupTurnOnBtn`),
  the step that comes next. Nothing left to do with protection on, or in No phone mode: Done alone, `Primary`.
- **A row of Phone setup never says done for something the PC can't see** (25 Nov 2026): a hotspot that is picked with the PC
  off it is the red step (Connection, with Wi-Fi list), so the summary can't say "ready" and Turn on protection isn't offered.
- **A note on a page** (26 Nov 2026): a warning or a tip is one line with its sign and a chevron that opens the rest under
  it (Regions' warning and tip, `FoldLine`; it opens by itself when the rule failed). The sign is drawn, 16 px: a 1.5 px ring in `Warn` or `Accent` with a 1.75 px stroke and a dot inside (27 Nov: a 10 px "!" of text sat low with its dot on the stem). What the app offers is `Surface2` with
  a 2 px `Accent` left border, rounded 6 on the right (the phone's offer under the Regions list, `RegionsPitch`); a bar that
  asks something now (Servers' follow and slot bars, Accounts' Show as rows) is `Field`, rounded 8. Buttons on its right.
  One note per page part.
- **Drawn phone screens** (Phone setup's USB debugging, `src/Views/PhoneGuide.cs`): square, 132x150, `Bg` with a 1 px `LineStrong` border and a short `Line` bar for the earpiece. Only the line to tap has words
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
Since 26 Nov 2026 (direction D, the owner: keep the story in scroll with its 3D and effects, and take the best parts of the
Claude Design proposals A, B and C; then reviewed by a designer and a critic): under 760 px Is it safe?, And the rest, the
two reasons of Phone or VPN and Setup's four steps are shelves that swipe sideways, cards 82 % wide and top-aligned so the
top of the next one peeks, with a dot per card right under them; the two pictures of Phone or VPN sit side by side; the VPN
table reads one row at a time, Pickroute's answer first and the VPN's under it (both always shown, no switch); the guides
stay a list; the footer's three lists open in place under their heading and its name is centred; and "Send the link to my
PC" waits at the bottom, in thumb reach, from the app to the guides (never over the story's cards; the header's Send to PC
steps aside meanwhile). No new text: the bar copies the cover's button (`assets/js/home.js`, `assets/js/footer-fold.js`).
The 3D stills (`.still`) no longer carry the three-colour line: it is not the app's window.
On 27 Nov 2026 the owner saw it on his Samsung S21 and turned it down (cards of different heights, text squeezed into small
boxes, things cut, the spacing of the 01s, the borders, the guides). Since then, under 760 px:

- **One card for every shelf:** 16 px corners, a 1 px `--line` border, 20 px inside, `--t-surface`. The cards of a shelf
  are all as tall as the tallest (`align-items: stretch`), and their parts sit on the same lines (Is it safe?: the reading
  4.4rem, then its picture 6.75rem, then the words; And the rest: the demo on a 7rem band over a hairline; the VPN
  reasons: both drawings 8.5rem tall, the source at the card's foot). A shelf has 28 px of room on its right and a card is as wide as
  the rest, so the next one shows 16 px of its edge and none of its words; the last snaps to the same place as the first. The dots sit
  16 px under the cards.
- **Setup's steps:** the number on its own line (2.5rem mono, `--t-accent`) and the words under it, never beside it.
- **Pictures of the world** (`.still`): 16 px corners (12 for the two side by side), one hairline, the caption under the
  picture in the text face (.875rem, `--ink3`; .8125rem under the pair), never in a box or in mono.
- **The VPN table** has a key on top (Pickroute with an accent dot, VPN or proxy with a grey one), then each row's topic and
  its two answers, each with its dot.
- **The app tour's close-up** shows only the marked part of the real window with 4 px of its own ground (`clip-path`),
  scaled to fit its box (`--s`, up to 2x, the screenshot's own pixels) and centred on `--t-bg`, so nothing next to the mark
  shows cut; its frame is 16 px, no three-colour line. The six pages are pills like Android | iPhone (the open one
  `--t-accent` with dark text). The marks (`.marks .mk` in `index.html`, the same for the PC's window) are on the Dial
  captures (the content starts at 15.5 % of the width; the sidebar is 0 to 13.6 %), and the two patches put the story's
  IP over the big IP and over the top bar's pill.
- **The language notice** on a phone (up to 560 px) is a 16 px card at the bottom: the line on its own row with the close
  button, "Ver em português" across under it (48 px); it rises above the Send bar when that is on.
- **The cover:** "It's a Windows app: open this page on your PC." on its own line, then the address in a small mono chip
  and Download the .exe anyway.
- **The header** on a touch screen is the ground itself, without the blur (it redid an 18 px blur of the moving 3D on every
  frame, and text showed through it). Send to PC sits left of the menu, so the menu keeps its place when the button steps
  aside, and it steps aside too while the close's own Send to PC is on screen.
- **The footer's languages** are two columns.
- **The guides** show their own picture of the 3D band (`site/img/gp-<slug>.webp`, `node tools/guide-posters.mjs`) until
  the live scene fades in over it; a section's number sits with a 20 px accent line on one row over its heading (.8125rem
  mono, `--ink3`); the header is as tall as the home's; one rule between the questions and the related guides; and their
  crop of the app has 16 px corners, no three-colour line, its caption in the text face.
- **One row for every list of links** (27 Nov 2026: the home's guides, its FAQ's more answers, a guide's related guides and
  the 404's guides were three kinds of line): a hairline under each, 16 px above and below, the title in the display face at
  18 px (`--ink`), a short `--ink3` line under it in the text face (.875rem) when there is one, a .5rem chevron at the end. No
  01 to 14 or 01 to 05 in mono before the rows (the guides had none). The FAQ's questions keep their + on the same lines.
- **The same menu in every header** (`assets/site-menu.css`, `assets/js/site-navigation.js`): the home's lists its own
  parts; a guide's and the 404's list the home's eight (Overview, How it works, The app, Is it safe?, Phone or VPN, Setup,
  Guides, FAQ) under "Pickroute"; tools/site-i18n.mjs adds the five languages under them. The panel has 16 px corners.
- **The 404:** Go to Pickroute and Send to PC one under the other, full width, 48 px; the guides in a 16 px card
  with 20 px inside; no Send to PC in its header (the page's own button is on the first screen).
- **The page's address** (`code.url`, to type on the PC) breaks only after a slash: each part is a `.nw` span.
- **Mono** stays for addresses, code, numbers, the readouts' labels and the app's own words; sentences after the story
  (Android's Mobile data off and on, a guide's note under its IP) are in the text face.
- **One name for the button that sends the page to the PC** (27 Nov 2026): "Send to PC" in the header, on the cover, in the
  bar at the bottom, on the close, on the 404 and in the guides (it was Send to PC, Send the link to my PC and Send to my PC),
  with the line "It's a Windows app: open this page on your PC." under the cover's. The old name, CS2 IP Changer, is shown
  nowhere on the site but in the install folder's path; it stays for machines in the JSON-LD `alternateName` and `llms.txt`.
- **Other languages:** nothing is cut or shortened for them; a long word may run past a heading's measure (the close's
  11ch), never past the screen (`overflow-wrap: normal` there). Checked at 360 px in the five languages by
  `tools/site-direcao-d.test.mjs`.
Rules, checked by `node --test tools/site-touch.test.mjs` with taps and flicks (10 Nov 2026):

- every `:hover` style sits in `@media (hover:hover)`, so a phone, which keeps `:hover` on what was tapped, leaves nothing
  lit; a `:focus-visible` that shared the rule stays outside it;
- `html` has `scroll-padding-top: calc(var(--top-h) + 1rem)`, so a heading's `scroll-margin-top` never adds the header
  again;
- every target is at least 44 px; a label never breaks inside a word;
- by touch the page has no scroll snap: it stays where the finger leaves it. From 6 to 25 Nov 2026 the beats' cards
  snapped (`proximity`, `snap-stop: always`); they are a screen apart, so a swipe shorter than half a screen was undone
  and the page was pulled back up to 235 px;
- a trip started by a tap (a link of the menu, a step of the chip) never holds the finger: Lenis locks it only when the
  main pointer is a mouse (`pointer: coarse` has no `lock`), so a finger that drags stops it;
- held upright on a phone each step of "How it works" is `78svh` (the home's own style; no other stylesheet gives
  `#how .beat` a height), and a step's card is readable or gone: `home.js` sets its opacity to 0 once its bottom has
  risen above 42 % of the screen under the header, with a 0.18 s fade (none with reduced motion). It is never half faded
  over the scene;
- a step picked on the rail or the chip rests on that step's shot (`A[i].s` in `home.js`), not where its anchor would go;
- the close-up of the app tour (`.tour-crop`, `clamp(132px, 19svh, 168px)` tall; 130 px on a phone on its side) shows
  only its mark with 4 px round it, clipped and scaled to fit (27 Nov 2026; until then a 1:1 window whose faded edges
  still showed words cut in half). Every mark stays whole, 8 px clear of the box's edges.

## The site in other languages

English is the site (x-default). `site/pt-br/`, `site/es/`, `site/ru/` and `site/tr/` (Brazilian Portuguese, Spanish for Latin
America, Russian, Turkish; chosen 10 Nov 2026 from where the real users came from, Turkish added 24 Nov) are made by `node tools/site-i18n.mjs` from the
English pages and `tools/i18n/<lang>.json`; never edit them by hand. `node tools/site-pages.mjs` runs it after the guides.
Same design, same `assets/`, same 3D script; only the words change.

- A text is looked up by its English words. Change the English, and that text has no translation until
  `tools/i18n/<lang>.json` has one: `node tools/site-i18n.mjs --todo <folder>` lists what is missing,
  `--merge <folder>` takes the answers in, `--check` (and `node --test tools/site-i18n.test.mjs`) fails until then.
- Inline tags in a text are placeholders (`<a1>…</a1>`, `<code2/>`): a translation keeps every one. Version, size, dates
  and the VirusTotal count are `{version}`, `{size}`, `{date}`, `{vt}`, filled at build time in the language's format.
- A text a page's script shows is marked `/*t*/'…'` in the page; the shared scripts call `T('…')` and read
  `window.__i18n`, and reach the site's files through `window.__root` (`../` in a language folder).
- What stays English: the app's name and everything the app shows (pages, buttons, options). The app itself is in English,
  Brazilian Portuguese, Spanish, Russian and Turkish (see Languages below), but the site quotes its English names in every language;
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
- `tests/OrganizacaoTests.cs`: fails when the app leaves [App layout](#app-layout) (a section's place, a Settings option's
  group, a name, a section drawn as a box, a red button, a page's frame, title or list, a route colour off the live
  route, a text that points to a place that is not there).

## When

Any visual change to the app (a color, a text, a layout, anything in a `.xaml`) goes out in the same step as:

1. this file, if a token, a font, a size or a rule changed, and the App layout tables if a section, a frame or a Settings option moved;
2. `node tools/design-tokens.mjs` (if `Theme.xaml` changed);
3. `node tools/shots.mjs`, a look at the new captures, `ui-map.js` if it lists moved buttons, and `node tools/site-renders.mjs` (the 3D renders) if
   Connection changed;
4. the tests: `CI=true dotnet run --project tests` and `node --test worker/stats/test/*.test.mjs` (Node 24 on Windows
   refuses the folder alone); for the site also `node --test tools/site.test.mjs tools/site-touch.test.mjs
   tools/site-loading.test.mjs tools/world-3d.test.mjs`.

At every release the version changes, so the captures (the sidebar shows it) are taken again before the release commit.

### Languages (11 Nov 2026)

- The app is in English (the source and the fallback), pt-BR, es (neutral, Latin America), ru and tr. The language follows Windows the
  first time; Settings > App > Language changes it (it applies the next time the app opens).
- One mechanism, `src/Loc.cs`: the English text is the key, `src/Lang/<code>.json` hold the translations, and a text with no entry
  shows in English. XAML needs no marker: `Loc.Apply` swaps the text of each window once, when it opens. Texts the code builds go
  through `Loc.T("text")` and `Loc.F("text {0}", value)`. The Log, the telemetry and internal names stay in English
  (`Loc.En` gives the English back).
- Spanish and Russian run about 40% longer (Turkish about 20%, with long words that cannot break): size a box for the longest language, never for English. `SRT_PSEUDO=1` stretches every
  text 40% to check the layout.
- Menu names of Windows and Android that a text quotes are the ones the user sees on screen in that language.
- Dates the app shows go through `Fmt.When` ("6 Nov 16:40", "6 нояб. 16:40"): the month in the app's language (`Loc.Culture`), never
  the culture of Windows and never English in another language (27 Nov). A text on one line that can run out of room (a list of
  names, a status line) ends in "..." (`TextTrimming`) inside a panel that gives it a width (`DockPanel`, `Grid`), never a horizontal
  `StackPanel`.
- A button that shows only an icon has a name for screen readers: its tooltip (`IconNames`, 27 Nov), or its own
  `AutomationProperties.Name`.
- `tools/loc-keys.mjs` lists the keys, `tools/loc-merge.mjs` folds translations in, `tests/LocTests.cs` fails when a key is missing
  in a language or a Toast carries a raw literal.
