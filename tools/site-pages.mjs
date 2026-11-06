// Builds the guide pages of the site (site/<slug>/index.html) and site/sitemap.xml from the pages below, and puts the
// latest release (site/release.json: version, size, date, SHA-256, VirusTotal) into the home page, the guides, README.md
// and site/llms.txt, so the download line and the VirusTotal link are never typed by hand.
// The look comes from site/index.html (its <style>), so the pages always match the home page. Run: node tools/site-pages.mjs
// After a release: node tools/site-pages.mjs --release v0.1.0-beta.10 --vt 0/69 (the VirusTotal run's summary gives N/M).
import { readFileSync, writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { readTheme } from './design-tokens.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const site = join(root, 'site');
const BASE = 'https://sepd0x.github.io/cs2-ip-changer/';
const REPO = 'https://github.com/Sepd0x/cs2-ip-changer';
const LATEST = `${REPO}/releases/latest`;
const today = new Date().toISOString().slice(0, 10);

// ------------------------------------------------------------ the latest release, from one file
const RELEASE = join(site, 'release.json');
const arg = (name) => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : null; };
if (arg('--release')) {
  // Read from GitHub's public API, so the size, the date and the SHA-256 are the release's own.
  const tag = arg('--release'), vt = arg('--vt'), was = JSON.parse(readFileSync(RELEASE, 'utf8'));
  const r = await (await fetch(`https://api.github.com/repos/Sepd0x/cs2-ip-changer/releases/tags/${encodeURIComponent(tag)}`, { headers: { accept: 'application/vnd.github+json' } })).json();
  const exe = r.assets?.find((a) => a.name === was.asset);
  if (!exe) throw new Error(`${tag}: no ${was.asset} in the release (is it published?)`);
  let sha = exe.digest?.startsWith('sha256:') ? exe.digest.slice(7) : null;
  if (!sha) { const s = r.assets.find((a) => a.name === `${was.asset}.sha256`); sha = s && (await (await fetch(s.browser_download_url)).text()).split(/\s/)[0]; }
  if (!/^[0-9a-f]{64}$/.test(sha || '')) throw new Error(`${tag}: no SHA-256 found`);
  const m = /^(\d+)\/(\d+)$/.exec(vt || '');
  if (!m && sha !== was.sha256) throw new Error('--vt <flagged>/<engines> is needed for a new exe (from the VirusTotal run of this release)');
  const next = { ...was, tag, version: tag.replace(/^v/, ''), published: r.published_at.slice(0, 10), size: exe.size, sha256: sha,
    virustotal: m ? { flagged: +m[1], engines: +m[2] } : was.virustotal };
  writeFileSync(RELEASE, JSON.stringify(next, null, 1) + '\n');
  console.log(`site/release.json: ${next.tag}, ${next.size} bytes, ${next.sha256.slice(0, 12)}…, VirusTotal ${next.virustotal.flagged}/${next.virustotal.engines}`);
}
const rel = JSON.parse(readFileSync(RELEASE, 'utf8'));
const EXE = `${REPO}/releases/latest/download/${rel.asset}`;   // GitHub sends it to the newest release's exe (the name never changes, .github/workflows/release.yml)
const VT = `https://www.virustotal.com/gui/file/${rel.sha256}/detection`;
const size = `${(rel.size / 1048576).toFixed(1)} MB`;   // counted like GitHub's release page and the browsers (1 MB = 1024 KB)
const day = (iso) => new Date(`${iso}T12:00:00Z`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
const short = rel.version.replace(/^\d+\.\d+\.\d+-/, '');   // beta.9
const vtText = `${rel.virustotal.flagged} of ${rel.virustotal.engines} engines`;
// The line under each Download button: what you get, and where the notes and checksums are.
const relLine = `v${rel.version} · ${size} · ${day(rel.published)} · <a href="${LATEST}">Release notes and checksums</a>`;

// git only reads here: when a file was first added, and when it last changed.
const git = (...a) => { try { return execFileSync('git', a, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim(); } catch { return ''; } };
const firstAdded = (file) => git('log', '--diff-filter=A', '--format=%cs', '--', file).split('\n').filter(Boolean).pop() || today;
const lastChanged = (file) => (git('status', '--porcelain', '--', file) ? today : git('log', '-1', '--format=%cs', '--', file)) || today;

// One marked spot in a file: <!--rel:name-->...<!--/rel-->. A missing mark stops the build, so nothing is left old by mistake.
const fill = (textIn, name, value, file) => {
  const re = new RegExp(`<!--rel:${name}-->[\\s\\S]*?<!--/rel-->`, 'g');
  if (!re.test(textIn)) throw new Error(`${file}: the mark <!--rel:${name}--> is missing`);
  return textIn.replace(re, `<!--rel:${name}-->${value}<!--/rel-->`);
};
const vtLinks = (s) => s.replace(/https:\/\/www\.virustotal\.com\/gui\/file\/[0-9a-f]{64}\/detection/g, VT);

// The home page: the download lines, the VirusTotal link and the release facts in its JSON-LD.
{
  const file = join(site, 'index.html');
  let s = readFileSync(file, 'utf8');
  s = fill(s, 'line', relLine, 'site/index.html');
  s = fill(s, 'vt', `${rel.virustotal.flagged} of ${rel.virustotal.engines}`, 'site/index.html');
  s = fill(s, 'ver', `v${rel.version}`, 'site/index.html');
  s = fill(s, 'sha', rel.sha256, 'site/index.html');
  if (!/data-peek="\d+\/\d+\|engines/.test(s)) throw new Error('site/index.html: the guides index has no VirusTotal peek');
  s = s.replace(/data-peek="\d+\/\d+\|engines/, `data-peek="${rel.virustotal.flagged}/${rel.virustotal.engines}|engines`);
  s = vtLinks(s)
    // The link card with the version in its address: Discord, X and others keep a card for days, a new address fetches it again
    .replaceAll(/img\/social-preview\.png(\?v=[^"]*)?"/g, `img/social-preview.png?v=${rel.version}"`)
    .replaceAll(/https:\/\/github\.com\/Sepd0x\/cs2-ip-changer\/releases\/latest\/download\/[^"<\s]+/g, EXE)
    .replace(/("softwareVersion": )"[^"]*"/, `$1"${rel.version}"`)
    .replace(/("datePublished": )"[^"]*"/, `$1"${rel.firstRelease}"`)
    .replace(/("dateModified": )"[^"]*"/, `$1"${rel.published}"`)
    .replace(/("fileSize": )"[^"]*"/, `$1"${size}"`)
    .replace(/("downloadUrl": )"[^"]*"/, `$1"${EXE}"`);
  writeFileSync(file, s);
}
// README.md: the download line under the button, the size in Quick start and the VirusTotal result.
{
  const file = join(root, 'README.md');
  let s = readFileSync(file, 'utf8');
  s = fill(s, 'line', `<a href="${EXE}">${rel.asset}</a> · v${rel.version} · ${size} · ${day(rel.published)} · <a href="${LATEST}">release notes and checksums</a>`, 'README.md');
  s = fill(s, 'size', size, 'README.md');
  s = fill(s, 'vt', `${short}: [${vtText}](${VT}) flag it`, 'README.md');
  // the VirusTotal badge in the row under the title (the other badges read GitHub live, through shields.io)
  const deep = readTheme().AccentDeepColor.slice(1);
  s = fill(s, 'vtbadge', `<a href="${VT}"><img src="https://img.shields.io/badge/VirusTotal-${rel.virustotal.flagged}%2F${rel.virustotal.engines}-${deep}" alt="VirusTotal: ${vtText} flag it"></a>`, 'README.md');
  writeFileSync(file, vtLinks(s));
}
// site/llms.txt: the status line and the direct download.
{
  const file = join(site, 'llms.txt');
  let s = readFileSync(file, 'utf8');
  if (!/^Status: .*$/m.test(s) || !/^Download: .*$/m.test(s)) throw new Error('site/llms.txt: the Status: or Download: line is missing');
  s = s.replace(/^Status: .*$/m, `Status: beta. Latest release ${rel.version} (${rel.published}). Free, no account. Not affiliated with Valve.`)
    .replace(/^Download: .*$/m, `Download: ${EXE} (${rel.asset}, ${size}, SHA-256 ${rel.sha256}, VirusTotal: ${vtText} flag it, ${VT})`);
  writeFileSync(file, s);
}

// Guide layout: one article in a text column, with a sticky rail (download, contents, a crop of the app) on the right.
// The fonts are the site's own (site/fonts), so the copied style points one folder up.
const home = readFileSync(join(site, 'index.html'), 'utf8');
const themeColor = home.match(/<meta name="theme-color" content="([^"]+)">/)[1];   // from src/Theme.xaml (tools/design-tokens.mjs)
// The home page's own blocks (hero map, viewer, sections) are left out; its media queries stay, they hold the table rules too.
const style = home.match(/<style>[\s\S]*?<\/style>/)[0].replace(/\/\* home: [\s\S]*?(?=@media \(max-width:1000px\))/, '').replace(/url\((fonts|assets)\//g, 'url(../$1/').replace('</style>', `/* guides */
/* a fallback with Funnel Display's width, so the H1 breaks in the same place before the font arrives (no shift) */
@font-face{font-family:"Funnel Display fb";src:local("Segoe UI Variable Display"),local("Segoe UI"),local("Arial");size-adjust:105.5%}
@font-face{font-family:"Funnel Sans fb";src:local("Segoe UI Variable Text"),local("Segoe UI"),local("Arial");size-adjust:101.4%}
:root{--sans:"Funnel Sans","Funnel Sans fb","Segoe UI Variable Text","Segoe UI",system-ui,-apple-system,Roboto,"Helvetica Neue",Arial,sans-serif;--disp:"Funnel Display","Funnel Display fb","Segoe UI Variable Display","Segoe UI",system-ui,-apple-system,Roboto,"Helvetica Neue",Arial,sans-serif}
.gmain{container-type:inline-size}
/* the stage: this page's piece of proposal A's world behind the hero, under the glass header as on the home cover */
.gstage{position:relative;overflow:hidden;margin-top:calc(-1*var(--top-h));padding-top:var(--top-h)}
.gw{position:absolute;inset:0;z-index:0;pointer-events:none}
.gw canvas{display:block;width:100%;height:100%;opacity:0;transition:opacity 1.4s var(--ease-out)}
.gw.ready canvas{opacity:1}
/* the veil: text never sits on the picture without the page's own ground behind it; under the copy it is the ground itself
   (6 Nov.: parts of the world outside the shot, a monitor or a leg of the route, showed through behind the headline) */
.gw::after{content:"";position:absolute;inset:0;background:linear-gradient(90deg,var(--bg) 0,var(--bg) calc(var(--gw-l,44%) - 12%),color-mix(in srgb,var(--bg) 90%,transparent) calc(var(--gw-l,44%) - 3%),transparent calc(var(--gw-l,44%) + 6%)),linear-gradient(transparent 84%,var(--bg))}
.ghero{position:relative;z-index:1;display:grid;grid-template-columns:repeat(12,minmax(0,1fr));align-items:center;min-height:min(calc(100svh - var(--top-h)),64rem);padding-block:clamp(2.25rem,1rem + 3.2vw,4.5rem) clamp(5.5rem,4rem + 3vw,7.5rem)}
.ghero-copy{grid-column:1/7;min-width:0;padding-right:clamp(0rem,2vw,2.5rem)}
.ghero-copy>*{position:relative;z-index:2}
.groom{display:none}
.skip{min-height:44px;display:inline-flex;align-items:center}
@media (pointer:coarse){.gstar a,.crumbs a,.checks a{display:inline-flex;align-items:center;min-height:44px;margin-block:-.7rem}}
@media (min-width:600px) and (max-width:900px){.top nav a.hide-s[href$="is-it-safe/"]{display:inline-flex;align-items:center;min-height:44px}}
.crumbs{font:400 .86rem/1.4 var(--sans);color:var(--ink3);margin:0 0 1.5rem;display:flex;flex-wrap:wrap;gap:.2rem .6rem}
.crumbs a{color:var(--ink2);text-decoration:none;display:inline-flex;align-items:center;min-height:24px}.crumbs a:hover{color:var(--ink)}
@media (pointer:coarse){.crumbs a{min-height:44px}}
.crumbs i{font-style:normal;color:var(--line2)}
.ghero h1{font:500 clamp(2.4rem,1rem + 3.1vw,4.9rem)/.98 var(--disp);letter-spacing:-.045em;margin:0 0 1.4rem;max-width:16ch}
@media (min-width:1100px){.ghero h1{max-width:18ch}.ghero h1.long{max-width:21ch;font-size:clamp(2.4rem,1rem + 2.7vw,4.4rem)}}
.ghero .lead{margin:0;font-size:clamp(1.04rem,.98rem + .25vw,1.16rem);max-width:56ch}
/* under the picture: what it shows in words, and its two buttons */
.gw-foot{position:absolute;right:var(--gutter);bottom:clamp(1.25rem,3svh,2.25rem);width:min(50%,46rem);display:flex;align-items:flex-end;justify-content:flex-end;gap:.75rem 1rem;flex-wrap:wrap}
.gw-cap{flex:1 1 18rem;margin:0;max-width:42ch;font:400 .86rem/1.5 var(--sans);color:var(--ink2);padding:.6rem .85rem;border-radius:8px;background:color-mix(in srgb,var(--bg) 90%,transparent);-webkit-backdrop-filter:blur(8px);backdrop-filter:blur(8px);box-shadow:inset 0 0 0 1px color-mix(in srgb,var(--t-text) 10%,transparent)}
.gw-ctl{display:flex;flex-wrap:wrap;gap:.6rem}
/* with motion the two buttons keep their place before the picture is up, so nothing moves when they appear */
html.fx .gw-ctl button[hidden]{display:inline-flex!important;visibility:hidden}
.gw-ctl .demo{background:color-mix(in srgb,var(--bg) 88%,transparent);-webkit-backdrop-filter:blur(8px);backdrop-filter:blur(8px)}
.gw-pause{width:44px;padding:0;gap:0;justify-content:center;font-size:0;color:var(--ink3)}.gw-pause:hover,.gw-pause:focus-visible{color:var(--ink)}
.gw-pause::before{content:"";flex:none;width:8px;height:10px;border-inline:2.5px solid currentColor}
.gw-pause[aria-pressed=true]::before,.gw-pause.play::before{border:0;border-left:9px solid currentColor;border-block:5px solid transparent;width:0;height:0}
.gw-pause[aria-pressed=true],.gw-pause.play{background:color-mix(in srgb,var(--t-accent) 16%,transparent);box-shadow:inset 0 0 0 1px var(--t-accent)}
.no-gl .gw,.no-gl .groom,.no-gl .gw-ctl{display:none}
@media (max-width:760px){.gw .wd-lab>div{padding:.4rem .55rem;width:max-content;max-width:min(11.5rem,46vw);white-space:normal}.gw .wd-lab b{font-size:.82rem}.gw .wd-lab span,.gw .wd-lab small{font-size:.8125rem}}
/* the app's own screen, at the top of the text: the real screenshot, the crop this page is about */
.gshot{margin:0 0 clamp(2.5rem,1.6rem + 2vw,3.75rem);max-width:42rem}
.gwin{position:relative;margin:0;border-radius:12px;overflow:hidden;background:var(--t-surface);box-shadow:0 0 0 1px rgba(255,255,255,.08),0 40px 90px -40px rgba(0,0,0,.9)}
.gwin::before{content:"";display:block;height:2px;background:var(--t-top)}
.gwin-bar{display:flex;align-items:center;gap:.5rem;padding:.6rem .9rem;background:var(--t-side);border-bottom:1px solid var(--line);font:500 .8rem/1 var(--sans);color:var(--ink2);white-space:nowrap}
.gwin-bar img{width:16px;height:16px;border-radius:4px;flex:none}
.gwin-bar i{font-style:normal;color:var(--ink3)}
.gwin-bar b{font-weight:500;color:var(--ink)}
.gwin-view:focus-visible{outline-offset:-3px}
.gwin-view{display:block;overflow:hidden;aspect-ratio:var(--pa);background:var(--t-side)}
.gwin-view img{max-width:none;width:var(--pw);margin:var(--py) 0 0 var(--px)}
.gshot-cap{padding:.75rem .9rem .8rem;background:var(--t-side);border-top:1px solid var(--line);margin:0;font:400 .8rem/1.5 var(--mono);color:var(--ink3)}
/* contents on a phone (the rail is a PC thing): a short numbered list before the text */
.gtoc-m{display:none}
/* the page's readout: the same instrument as the home page, with one number from this page of the app */
.ro-sig{margin-top:clamp(1.25rem,.8rem + 1.2vw,2rem)}
/* the state beside the number in the ink colour: the scene's light can come through the veil (AA) */
.ghero .ro-state{color:var(--ink);padding:.15rem .45rem;margin:-.15rem -.45rem;border-radius:4px;background:color-mix(in srgb,var(--bg) 88%,transparent)}
.ro-sig .ro-head{padding-top:.9rem;border-top:1px solid var(--line)}
.ro-sig .ro-foot p{margin:0}
.ro-sig .led{background:var(--t-accent)}
.fx .ro-sig .digits{visibility:hidden}
@media (min-width:760px){.ro-sig .ro-num{--ro-size:min(calc((min(100vw,var(--max) + 2*var(--gutter)) - 2*var(--gutter))*6/12/var(--em) - .08em),16svh)}}
/* the number keeps its height while readout.js builds its digits (no shift) */
.ro-sig .ro-num{min-height:.87em}
/* the article on the left, the rail (download and contents) on the right */
.gl{display:grid;grid-template-columns:minmax(0,1fr) 19rem;column-gap:clamp(2.5rem,1rem + 4vw,7rem);align-items:start;padding-top:clamp(2.75rem,1.5rem + 3vw,5rem);padding-bottom:clamp(3.5rem,2rem + 4vw,7rem)}
.gline{border-top:1px solid var(--line)}
.gl>*{grid-column:1;min-width:0}
.rail{grid-column:2;grid-row:1/span 4;align-self:stretch}
.rail-in{position:sticky;top:5.5rem;display:flex;flex-direction:column;gap:2.25rem}
.gcard{position:relative;padding:1.2rem 0 1.1rem;border-top:2px solid transparent;border-image:var(--t-top) 1;border-bottom:1px solid var(--line)}
.rail .btn,.gget .btn{width:100%}
.rail .copyrow .btn,.gget .copyrow .btn,.gmore .end .copyrow .btn{width:auto}
.gcard .checks{margin-top:1.1rem;font-size:.86rem;gap:.35rem 1rem;padding:0}
.gcard .checks li{margin:0}
@media (pointer:fine){.get-phone{display:none}}
.toc h2{font:500 .86rem/1.4 var(--sans);color:var(--ink3);margin:0 0 .8rem}
.toc ul{list-style:none;margin:0;padding:0 0 0 1rem;border-left:1px solid var(--line)}
.toc li{position:relative;margin:.45rem 0;font-size:.92rem;line-height:1.4}
.toc a{color:var(--ink3);text-decoration:none;transition:color .2s}
.toc a:hover{color:var(--ink)}
.toc a::before{content:"";position:absolute;left:calc(-1rem - 1px);top:.1em;bottom:.1em;width:2px;border-radius:2px;background:var(--accent);transform:scaleY(0);transition:transform .3s var(--ease-out)}
.toc a.on{color:var(--ink)}
.toc a.on::before{transform:none}
/* the text: a 68ch column, numbered sections */
.gtext{color:var(--ink2);max-width:42rem;counter-reset:sec}
/* below the first screen the browser lays these out only when they come near (6 Nov.: the first layout of the whole guide was
   one 600 ms task on a slow phone); the sizes are guesses until each part has been on screen once, then remembered */
.gtext{content-visibility:auto;contain-intrinsic-size:auto 3400px}.gfaq{content-visibility:auto;contain-intrinsic-size:auto 420px}.gmore{content-visibility:auto;contain-intrinsic-size:auto 800px}footer .fgrid{content-visibility:auto;contain-intrinsic-size:auto 1250px}
@media (max-width:759px){footer .fgrid{contain-intrinsic-size:auto 770px}}@media (min-width:1000px){.gtext{contain-intrinsic-size:auto 2100px}.gmore{contain-intrinsic-size:auto 380px}footer .fgrid{contain-intrinsic-size:auto 480px}}.gshot{content-visibility:auto;contain-intrinsic-size:auto 400px}.gtoc-m{content-visibility:auto;contain-intrinsic-size:auto 260px}footer .legal{content-visibility:auto;contain-intrinsic-size:auto 120px}
.gtext h2::before{content:counter(sec,decimal-leading-zero);content:counter(sec,decimal-leading-zero) / "";display:inline-block;width:3.2rem;font:400 .8rem/1 var(--mono);letter-spacing:0;color:var(--ink3);vertical-align:.55em}
.gtext h2{background:var(--t-top) 0 0/3rem 2px no-repeat;counter-increment:sec;color:var(--ink);font:500 clamp(1.7rem,1.1rem + 1.7vw,2.8rem)/1.06 var(--disp);letter-spacing:-.035em;margin:clamp(3.25rem,2.4rem + 2.6vw,5rem) 0 1.2rem;padding-top:clamp(1rem,.8rem + .6vw,1.4rem);border-top:0}
.gtext h2:first-child{margin-top:0;padding-top:clamp(1rem,.8rem + .6vw,1.4rem)}
.gtext h2::before{color:var(--accent)}
.gtext p,.gtext ul,.gtext ol{max-width:none;margin:0 0 1rem}
/* lists read as ledger rows: a hairline between items, no bullets */
.gtext ul{list-style:none;padding:0;border-top:1px solid var(--line)}
.gtext ul>li{margin:0;padding:.85rem 0;border-bottom:1px solid var(--line)}
.gtext ol:not(.tl){padding-left:1.3em}.gtext ol:not(.tl)>li{margin:0 0 .6rem}
.gtext li::marker{color:var(--ink3)}
.gtext b,.gtext strong{color:var(--ink);font-weight:500}
.gtext code{overflow-wrap:anywhere}
/* steps: numbered dots on a line that fills as each step reaches the screen */
.gtext ol.tl{list-style:none;padding:0;margin:1.6rem 0 1.4rem;counter-reset:hs}
.gtext ol.tl>li{counter-increment:hs;position:relative;padding:0 0 1.5rem 3.4rem;margin:0;background:linear-gradient(var(--line),var(--line)) no-repeat .6rem 1.9rem/1px calc(100% - 2.1rem)}
.gtext ol.tl>li:last-child{padding-bottom:0;background:none}
.gtext ol.tl>li::before{content:counter(hs,decimal-leading-zero);content:counter(hs,decimal-leading-zero) / "";position:absolute;left:0;top:.15rem;width:2.2rem;font:400 .8rem/1.4 var(--mono);color:var(--ink3);font-variant-numeric:tabular-nums;transition:color .6s .15s}
.gtext ol.tl>li::after{content:"";position:absolute;left:.6rem;top:1.9rem;bottom:.2rem;width:1px;background:linear-gradient(var(--accent),color-mix(in srgb,var(--t-top2) 70%,transparent));transform-origin:50% 0;transition:transform 1.2s var(--ease-out) .35s}
.gtext ol.tl>li:last-child::after{display:none}
.gtext ol.tl>li.in::before{color:var(--accent)}
@media (prefers-reduced-motion:no-preference){.js .gtext ol.tl>li.rv:not(.in)::after{transform:scaleY(0)}}
.fx-opt .gtext ol.tl>li.rv:not(.in)::after{transform:scaleY(0)}
.gtext ol.howto>li>b:first-child{display:block;font:500 1.08rem/1.35 var(--disp);letter-spacing:-.01em;margin-bottom:.3rem}
/* tables in a frame on a PC */
.gtext .tbl{margin:1.5rem 0 1.25rem}
@media (min-width:761px){
  .gtext .tbl{padding:0;border-block:1px solid var(--line)}
  .gtext tbody tr:last-child>*{border-bottom:0}
  .gtext td.us{padding-left:0;background:none;font-weight:500}
}
/* FAQ and related guides */
.gl .gfaq{max-width:42rem;margin-top:clamp(3.25rem,2.4rem + 2.6vw,5rem);padding:clamp(2rem,1.6rem + 1.4vw,3rem) 0 0;border-top:1px solid var(--line)}
.gfaq h2,.gmore h2{font:500 clamp(1.6rem,1.15rem + 1.4vw,2.4rem)/1.08 var(--disp);letter-spacing:-.03em;margin:0 0 1.4rem}
.gfaq details[open] summary{color:var(--ink)}
.gmore{grid-column:1/-1;margin-top:clamp(4rem,2.6rem + 4vw,7rem);padding-top:clamp(2rem,1.6rem + 1.4vw,3rem);border-top:1px solid var(--line)}
.gmore ul{list-style:none;margin:0;padding:0;display:grid;grid-template-columns:minmax(0,1fr);border-top:1px solid var(--line)}
.gmore li{margin:0;min-width:0}
.gmore ul a{position:relative;display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr) 1.25rem;gap:1.5rem;align-items:baseline;padding:1.25rem 0;border-bottom:1px solid var(--line);color:var(--ink);text-decoration:none}
.gmore ul a::before{content:"";position:absolute;left:0;right:0;bottom:-1px;height:1px;background:var(--t-top);transform:scaleX(0);transform-origin:left;transition:transform .55s var(--ease-out)}
.gmore ul a b{font:500 clamp(1.12rem,1rem + .4vw,1.35rem)/1.25 var(--disp);letter-spacing:-.015em}
.gmore ul a span{color:var(--ink3);font-size:.92rem;line-height:1.45}
.gmore ul a svg{width:1.1rem;height:1.1rem;align-self:center;justify-self:end;color:var(--ink3);transition:transform .45s var(--ease-out),color .3s}
@media (hover:hover){
  .gmore ul a:hover::before{transform:scaleX(1)}
  .gmore ul a:hover svg{transform:translateX(6px);color:var(--accent)}
}
.gmore ul a:focus-visible::before{transform:scaleX(1)}
/* a phone: the guide's name over its line, the arrow on the name's row (three columns left the line four lines tall) */
@media (max-width:520px){.gmore ul a{grid-template-columns:minmax(0,1fr) 1.25rem;gap:.35rem 1rem}.gmore ul a span{grid-column:1;grid-row:2}.gmore ul a svg{grid-column:2;grid-row:1}}
.gmore .end{display:none}
.gmore .end .btn{width:100%}
.gmore .end .link,.gstar a{font:inherit;text-decoration:underline;text-decoration-color:var(--line2);text-decoration-thickness:1px;text-underline-offset:.22em}
.gmore .end .link{font-weight:500;font-size:.92rem}
.gstar{color:var(--ink3);font-size:.95rem;margin:2rem 0 0}
.gstar a{color:var(--ink2)}
/* on a phone the download comes after the first answer, inside the text */
.gget{display:none}
.gget p{max-width:none}
.gget .rel,.gget .safe{margin:.8rem 0 0}
.gget .req{margin:.45rem 0 0}
/* contents: the line beside them fills in the three colours as you read */
.toc ul::after{content:"";position:absolute;left:-1px;top:0;width:1px;height:100%;background:linear-gradient(var(--t-top1),var(--t-top2),var(--t-top3));transform-origin:50% 0;transform:scaleY(var(--rp,0))}
.toc ul{position:relative}
@media (max-width:999px){
  /* phone and tablet: title and answer first, then the picture in its own band (never under the text), then the number */
  .gstage{margin-top:0;padding-top:0}
  .ghero{display:block;min-height:0;padding-block:1.75rem 2rem}
  .ghero h1{max-width:22ch}
  .groom{display:block;height:min(clamp(17rem,84vw,31rem),62svh);margin:1.25rem calc(-1*var(--gutter)) 0}
  .gw{inset:auto 0;top:var(--gw-top,0px);height:var(--gw-h,0px)}
  .gw::after{background:linear-gradient(var(--bg),transparent 12%,transparent 88%,var(--bg))}
  .gw-foot{position:static;width:auto;margin-top:.9rem;justify-content:flex-start}
  .gw-cap{flex-basis:100%;max-width:none;background:none;-webkit-backdrop-filter:none;backdrop-filter:none;box-shadow:none;padding:0}
}
@media (max-width:900px){
  .gwin{border-radius:10px}
  .gwin-bar{padding:.5rem .75rem;font-size:.76rem}
  .gl{grid-template-columns:minmax(0,1fr);padding-top:1.9rem;padding-bottom:3rem}
  .rail{display:none}
  .gget{display:block;margin:2.25rem 0 0}
  .gmore ul{grid-template-columns:repeat(2,minmax(0,1fr))}
  .gmore .end{display:block;margin-top:2rem}
  .gtoc-m{display:block;margin:0 0 2.5rem;padding-top:1rem;border-top:2px solid transparent;border-image:var(--t-top) 1}
  .gtoc-m p{margin:0 0 .3rem;font:500 .86rem/1.4 var(--sans);color:var(--ink3)}
  .gtoc-m ol{list-style:none;margin:0;padding:0;counter-reset:t;border-bottom:1px solid var(--line)}
  .gtoc-m li{counter-increment:t;margin:0}
  .gtoc-m a{display:flex;gap:1rem;align-items:baseline;min-height:44px;padding:.65rem 0;color:var(--ink2);text-decoration:none;border-top:1px solid var(--line);font-size:.98rem;line-height:1.4}
  .gtoc-m li:first-child a{border-top:0}
  .gtoc-m a::before{content:counter(t,decimal-leading-zero);flex:none;width:1.6rem;font:400 .78rem/1 var(--mono);color:var(--ink3)}
}
@media (max-width:760px){
  .crumbs{margin-bottom:1.1rem}
  .ghero h1{font-size:clamp(2rem,1.2rem + 4.2vw,2.5rem);line-height:1.04;margin-bottom:1rem}
  .gtext h2,.gfaq h2,.gmore h2{font-size:clamp(1.5rem,1.1rem + 2vw,1.8rem)}
  .gtext h2{margin-top:2.75rem;padding-top:1.9rem}
  .gtext h2::before{display:block;margin-bottom:.75rem}
  .gtext ol.tl>li{padding-left:2.9rem}
  .gmore ul{grid-template-columns:minmax(0,1fr);gap:.75rem}
  .gmore ul a{padding:1.15rem 3.2rem 1.15rem 1.2rem}
  .gmore ul a svg{position:absolute;right:1.1rem;top:50%;margin:-.6rem 0 0;padding:0}
  .gl .gfaq{margin-top:2.75rem;padding-top:1.9rem}
  .gmore{margin-top:3rem;padding-top:1.9rem}
  .gcard{padding:1rem 0}
}
</style>`);

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const text = (html) => html.replace(/<[^>]+>/g, '').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&').replace(/\s+/g, ' ').trim();
const slug = (s) => s.toLowerCase().replace(/'/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const icon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11"/><path d="m7 10 5 5 5-5"/><path d="M5 20h14"/></svg>';
const star = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" aria-hidden="true"><path d="m12 3.6 2.6 5.3 5.8.8-4.2 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z"/></svg>';
// On a PC: the exe itself, with what you get under it. On a phone (a Windows app is no use there): copy this page's link for the PC.
const download = (url) => `<div class="get-desk"><a class="btn" href="${EXE}">${icon}Download for Windows</a><p class="rel">${relLine}</p><p class="rel-ss">Windows may say "Windows protected your PC": click More info, then Run anyway. <a href="../is-it-safe/">Why it's safe</a></p></div>
          <div class="get-phone"><p class="get-note">It's a Windows app. Open this page on your PC:</p><div class="copyrow"><code class="url">${url.replace(/^https:\/\//, '').replace(/\/$/, '').replace(/\//g, '/<wbr>')}</code><button class="btn" type="button" data-share data-copy="${url}">Send to my PC</button></div><a class="link" href="${EXE}">Download the .exe anyway</a></div>`;
const req = 'Windows 10/11 · Android or iPhone, by USB cable or hotspot · uses mobile data · admin';
// The copy button and the screenshot viewer: the same code as the home page (between its /* shared */ marks).
const shared = readFileSync(join(site, 'index.html'), 'utf8').match(/\/\* shared \*\/([\s\S]*?)\/\* \/shared \*\//)[1];
// The footer is the home page's, with its links pointing one folder up.
const footer = home.match(/<footer>[\s\S]*?<\/footer>/)[0].replace(/(href|src)="(?!https?:|#|\.\.\/)([^"]*)"/g, (m, a, u) => `${a}="../${u.replace(/^\.\//, '')}"`);
// The trust line, the same as under the home page's buttons. Facts as in the README (the license isn't open source, so "Public source").
const checks = `<ul class="checks" aria-label="At a glance"><li>Free</li><li>Public source</li><li>No account</li><li>Doesn't touch cs2.exe</li><li><a href="${VT}">VirusTotal report</a></li></ul>`;
const safe = (here) => `Windows warns once: the .exe isn't code-signed yet. Updates are checked with a signature. ${here ? '<a href="#check-your-download">Check your download</a>' : '<a href="../is-it-safe/">How to check it</a>'}`;

// On a phone the tables turn into one block per row, so each body cell gets its column's name (from the table head) as data-label.
const labelTables = (html) => html.replace(/<table>([\s\S]*?)<\/table>/g, (table, inner) => {
  const head = (inner.match(/<thead>([\s\S]*?)<\/thead>/) || [, ''])[1];
  const names = [...head.matchAll(/<(?:th|td)[^>]*>([\s\S]*?)<\/(?:th|td)>/g)].map((m) => text(m[1]));
  const body = (inner.match(/<tbody>([\s\S]*?)<\/tbody>/) || [, ''])[1];
  const labelled = body.replace(/<tr>([\s\S]*?)<\/tr>/g, (row, cells) => {
    let i = 0;
    return '<tr>' + cells.replace(/<(th|td)([^>]*)>/g, (m, tag, attrs) => {
      const name = names[i++];
      return tag === 'td' && name ? `<td${attrs} data-label="${esc(name)}">` : m;
    }) + '</tr>';
  });
  return table.replace(body, labelled);
});

// A crop of a 1240x800 screenshot: [name, x, y, width, height, alt]. The box keeps the crop's shape; the image is scaled and moved inside it.
// It is the biggest picture on a phone (the LCP), so it loads at once and first.
const crop = ([name, x, y, w, h, alt]) => `<a class="crop zoom" href="../img/${name}.png" style="aspect-ratio:${w}/${h}" aria-label="${esc(alt)} (full screenshot)"><picture><source type="image/webp" srcset="../img/${name}.webp"><img src="../img/${name}.png" alt="${esc(alt)}" width="1240" height="800" loading="eager" fetchpriority="high" style="width:${(1240 / w * 100).toFixed(2)}%;margin:${(-y / w * 100).toFixed(2)}% 0 0 ${(-x / w * 100).toFixed(2)}%"></picture></a>`;

// Each page answers one search and has one job. Facts come from the README and CHANGELOG; keep them in step when the app changes.
// A question lives on one page only; the home FAQ links here.
export const pages = [
  {
    slug: 'cs2-region-picker',
    title: 'CS2 region picker: change your matchmaking region',
    desc: 'Pick your CS2 matchmaking region: block the ones you don\'t want and see the ping of each. Free Windows app, one firewall rule for cs2.exe on Valve relays.',
    nav: 'CS2 region picker',
    blurb: 'How to change your region in CS2.',
    eyebrow: 'CS2 server picker',
    h1: 'CS2 server picker: how to change your region in CS2, free',
    lead: 'CS2 has no region setting: matchmaking picks the region for you, by ping. The Regions page of CS2 IP Changer lists Valve\'s matchmaking regions with the ping of each one, and blocks the ones you turn off. Premier, Competitive, Casual, Deathmatch and Wingman then only find matches in the regions you left on.',
    crop: ['regions', 91, 44, 760, 466,'The Regions page: Valve\'s matchmaking regions by continent, each with its ping, and the ones turned off blocked for cs2.exe'],
    related: ['copy-cs2-config', 'steam-account-switcher', 'is-it-safe'],
    sections: [
      ['How CS2 picks your region', `<p>When you queue, the game measures your ping to Valve's relays (Steam Datagram Relay) in each region and looks for a match where the ping is good. Max Acceptable Matchmaking Ping, in Settings &gt; Game, only sets the highest ping you accept; it doesn't choose a region.</p><p>So the only way to choose is to make the regions you don't want unreachable. If <code>cs2.exe</code> can't reach a region's relays, CS2 treats that region as unavailable and looks elsewhere. That's what a CS2 region picker or server picker does, and it's what the Regions page does, with one Windows Firewall rule.</p>`],
      ['What the Regions page does', `<ul><li><b>The real list from Valve.</b> The regions come from Valve's own relay list (<code>GetSDRConfig</code>, app 730), grouped by city and continent. A city with more than one relay site, like Stockholm, is one row, and turning it off blocks all of them. The last list is saved, so the page works offline.</li><li><b>Ping for each region</b>, measured the way the game will feel it: through the phone while protection is on, through your home connection when it's off or in No phone mode.</li><li><b>Shortcuts:</b> Only this region, Closest 3, your continent only (Europe only, North America only and so on), Ping under X ms, and Allow all (block off).</li><li><b>One firewall rule, for <code>cs2.exe</code> only.</b> Turned-off regions are blocked on Valve's relay IPs only. Steam, your friends list and community servers are not touched.</li><li><b>Kept up to date.</b> While regions are off, Valve's list is read again every hour, and when it changes the rule is rebuilt, so new relays in a region you turned off are blocked too. After a shortcut, a region Valve adds later stays off as well.</li><li><b>Checked every 30 seconds.</b> If something else removes or changes the rule, the app puts it back, with or without protection.</li></ul>`],
      ['Do it by hand, without the app', `<p>You can build the same block yourself with Windows Firewall:</p><ol><li>Open Valve's relay list for CS2: <code>https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=730</code>. Each site (a "pop", like <code>fra</code> for Frankfurt) lists the IPs of its relays.</li><li>Open Windows Defender Firewall with Advanced Security and add an Outbound rule: Program, the path to <code>cs2.exe</code> (in <code>steamapps\\common\\Counter-Strike Global Offensive\\game\\bin\\win64</code>), Block the connection.</li><li>In the rule's Scope, add the relay IPs of every region you don't want as remote IP addresses.</li></ol><p>It works, but it's dozens of IPs, some cities have more than one pop (Stockholm has two; Frankfurt, Vienna and London also have a partner's relays), and Valve changes the list from time to time, so a rule made by hand goes out of date. You also don't see the ping of each region. The app reads the list, groups the pops by city, shows the ping and rebuilds the rule when Valve changes it.</p>`],
      ['Things to know', `<ul><li>Changes apply at once, and CS2 measures the regions again on its next start. Don't change them during a match.</li><li>Blocking too much means long queues or no match at all, and Premier can take longer. Leave a few regions on; the app won't let you turn them all off.</li><li>CS2's Max Acceptable Matchmaking Ping still counts. If it's lower than the ping of your slowest region left on, CS2 never uses that region, and the page tells you which number to set.</li><li>Windows Firewall has to be on. If it's off, the page says the rule is in place but not enforced until the Firewall is back on.</li><li>It works with or without the phone. Regions don't depend on protection: they stay off until you click Allow all (block off).</li></ul>`],
    ],
    // A how-to, as steps on the page and as HowTo in the JSON-LD. It goes after the first section.
    // id: the anchor stays the one the page always had, so old links to it still land. A step's third item is its text on
    // the page with links; the JSON-LD keeps the plain text.
    howto: { h: 'How to use the CS2 server picker, step by step', id: 'change-your-region-step-by-step', steps: [
      ['Get the app', 'Download CS2IPChanger.exe and run it. It asks for admin, because firewall rules are a system setting. For Regions you don\'t need the phone or protection.',
        'Download CS2IPChanger.exe and run it. It asks for admin, because firewall rules are a system setting (<a href="../is-it-safe/">what it changes on your PC</a>). For Regions you don\'t need the phone or protection.'],
      ['Open Regions and measure the ping', 'The list comes from Valve\'s own relay list, by continent and city. Press Refresh: the app pings each region the way the game will reach it.'],
      ['Turn off the regions you don\'t want', 'Untick them one by one, or use a shortcut: Only this region, Closest 3, your continent only (like Europe only) or Ping under X ms. Keep at least one region on.'],
      ['Check the block', 'Under the list the page says how many regions CS2 can play in, and "Block is on. Only CS2 is affected."'],
      ['Start CS2 and queue', 'CS2 measures the regions when it starts, so start it after the change. If your slowest region left on is above CS2\'s Max Acceptable Matchmaking Ping, the page tells you which number to set in Settings > Game.'],
      ['Undo it', 'Click Allow all (block off) on Regions. CS2IPChanger.exe --restore also removes the rule.'],
    ] },
    faq: [
      ['How do I change my region in CS2?', 'CS2 has no region setting. Matchmaking puts you where your ping to Valve\'s relays is good, and Max Acceptable Matchmaking Ping only caps the ping. To choose, block the regions you don\'t want: open Regions in CS2 IP Changer, turn off what you don\'t want, and matchmaking can only find you a match where you left regions on.'],
      ['How do I force CS2 to only put me in EU servers?', 'Open the Regions page and click your continent\'s shortcut (Europe only in Europe), or Only this region on one region. Every other region is blocked for cs2.exe, so matchmaking can only find you a match where you left regions on. Max Acceptable Matchmaking Ping in CS2\'s settings only limits the ping; it doesn\'t pick a region.'],
      ['Is there a region lock in CS2?', 'Not one you can set in the game. To lock yourself to one region, click Only this region on it: every other region stays blocked for cs2.exe until you click Allow all (block off).'],
      ['Does it work for Premier?', 'Yes. Premier, Competitive, Casual, Deathmatch and Wingman all pick a server by ping to Valve\'s relays, so they all follow the regions you left on. With few regions on, Premier can take longer to find a match.'],
      ['My server picker doesn\'t block anything. Why?', 'Most of the time Windows Firewall is off, or another firewall has taken over (some antivirus suites do), so block rules do nothing. The Regions page checks Windows Firewall and tells you when the rule isn\'t enforced. Also start CS2 again after a change: it measures the regions when it starts.'],
      ['Can I choose which players I meet?', 'No. It picks the regions your matches are played in, not the players. Players from anywhere can queue into a region they can reach.'],
      ['Do I need the phone for Regions?', 'No. Regions works with or without the phone and doesn\'t depend on protection. The ping is measured through the phone while protection is on, and through your home connection when it\'s off.'],
      ['Does forcing a region get me banned?', 'The Regions page doesn\'t touch the game, its files or its memory. It adds one outbound Windows Firewall rule that stops cs2.exe from reaching Valve\'s relays in the regions you turned off, so to the game it looks like a bad connection to those relays. We can\'t speak for Valve and can\'t promise anything about how they treat it.'],
      ['Does it change community servers or my friends list?', 'No. The rule only covers cs2.exe and only Valve\'s relay IPs. Community servers, Steam and friends work as before.'],
      ['How do I undo it?', 'Click Allow all (block off) on the Regions page. Running the app with --restore, or tools/restore.cmd, also removes every rule the app made. Turn off protection and No phone mode leave the region rule on.'],
    ],
  },
  {
    slug: 'copy-cs2-config',
    title: 'Copy your CS2 settings and crosshair to your alt account',
    desc: 'Copy CS2 binds, crosshair, sensitivity and video settings between your Steam accounts in one click, with backups and undo. Free Windows app.',
    nav: 'Copy CS2 settings',
    blurb: 'Crosshair, binds and video on every account.',
    eyebrow: 'CS2 config manager',
    h1: 'Copy your CS2 settings and crosshair to your alt account',
    lead: 'Got a new account and want your binds, crosshair and video settings on it? The Configs page of CS2 IP Changer copies your CS2 config between the accounts on your PC, one part at a time or all of it, and keeps a backup of everything it changes.',
    crop: ['configs', 657, 370, 566, 290,'The Configs page: two accounts compared setting by setting, with a crosshair preview'],
    related: ['steam-account-switcher', 'steam-profile-editor', 'cs2-region-picker'],
    sections: [
      ['Where CS2 keeps each account\'s config', `<p>CS2 saves settings per Steam account in <code>Steam\\userdata\\&lt;id&gt;\\730\\local\\cfg</code>:</p><ul><li>binds in <code>cs2_user_keys_0_slot0.vcfg</code></li><li>crosshair and game settings in <code>cs2_user_convars_0_slot0.vcfg</code></li><li>video in <code>cs2_video.txt</code></li><li>HUD, radar and audio in <code>cs2_machine_convars.vcfg</code></li></ul><p>Copying these by hand works, but it's easy to copy the wrong folder, and Steam Cloud can bring the old files back. The app writes the same files CS2 writes when you change a setting, and never the game itself (<a href="../is-it-safe/">what it changes</a>).</p>`],
      ['What the Configs page does', `<ul><li><b>Pick what to copy:</b> binds, crosshair, the other game settings (sensitivity, viewmodel), video, and HUD, radar and audio, each on its own. You can copy only the crosshair and keep each account's sensitivity.</li><li><b>Sync modes:</b> from a main account, from the account you used before, or latest change wins. It runs when a new account shows up, before the app logs in or opens CS2, or when you click Sync now.</li><li><b>Never with CS2 open.</b> CS2 saves its config when it closes and would undo the copy, so the copy waits until it closes.</li><li><b>Steam Cloud too.</b> The app writes the cloud copy the same way CS2 does, so Steam uploads the new one on the next launch.</li><li><b>Backups.</b> Every change keeps a backup (the 20 newest per account), and Undo last sync puts it all back.</li><li><b>Snapshots and compare.</b> Save a config under a name, export it as a .zip for another PC, and compare two accounts setting by setting with a crosshair preview.</li><li><b>Video stays this PC's.</b> Each account keeps this PC's refresh rate and monitor, so a 144 Hz screen doesn't end up at 60 Hz.</li></ul>`],
    ],
    faq: [
      ['Can\'t I just use the crosshair share code?', 'The share code only moves the crosshair. Your binds, sensitivity, viewmodel, video, HUD, radar and audio stay behind, and you have to paste it on every account. The Configs page copies all of it, or only the parts you tick, to every account at once.'],
      ['My settings keep going back. Why?', 'Steam Cloud. If an account was played on another PC since, Steam may bring that version back or ask which one to keep. If it keeps happening, turn off Steam Cloud for CS2 (CS2 > Properties > General).'],
      ['Can I move my config to another PC?', 'Yes. Save a snapshot and export it as a .zip. The .zip only has the config files and the name, never the account.'],
    ],
  },
  {
    slug: 'steam-account-switcher',
    title: 'Steam account switcher for CS2, with ban marks per server',
    desc: 'Switch Steam accounts for CS2 without typing passwords. See VAC, game and trade bans and cooldowns, and which account is banned on which server.',
    nav: 'Steam account switcher',
    blurb: 'No passwords, ban marks per server.',
    eyebrow: 'Alt accounts for CS2',
    h1: 'Steam account switcher for CS2, with ban marks per server',
    lead: 'CS2 IP Changer switches between the Steam accounts saved on your PC without asking for passwords, and knows which account is banned where. One hotkey gets a new IP, logs into the next clean account and joins the server again.',
    crop: ['accounts', 91, 44, 569, 296, 'The Accounts page: saved Steam accounts with ban status, cooldowns and the In rotation checkbox'],
    related: ['cs2-ip-ban', 'copy-cs2-config', 'steam-profile-editor'],
    sections: [
      ['What it does', `<ul><li><b>No passwords.</b> It uses the logins Steam already saved (the accounts where you ticked Remember me). Switching never asks for or stores a password.</li><li><b>Bans and cooldowns at a glance:</b> VAC, game and trade bans and the competitive cooldown of each account, with tabs for No bans, Banned, Cooldown and Not in rotation.</li><li><b>Server ban marks.</b> When CS2's console shows a ban, the account in use is marked for that server and for the rest of the same community. New IP + next account skips accounts on cooldown or marked on that server, and joining there with a marked account asks first.</li><li><b>In rotation.</b> Untick In rotation on an account's card and New IP + next account never picks it. The Not in rotation tab lists those accounts.</li><li><b>New IP + next account.</b> The button names the account it will use, like New IP + atlas, and gets a new IP, logs into that account and joins the last server again. <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F9</kbd> (you can change it) does it from inside the game. In Settings &gt; New IP + next account you pick which of those steps it does.</li><li><b>Checks the result.</b> CS2 only opens once Steam is really on the new account; if Steam logs in to another one, the app tells you.</li><li><b>Backups of Steam's account list.</b> Before each switch the app keeps a copy of <code>loginusers.vdf</code>, and a failed switch puts Steam's previous account back.</li></ul>`],
      ['Made for community servers', `<p>A ban on a community server is usually on your Steam account, your IP, or both. The switcher pairs with the rest of the app: after a ban, a new IP from your phone's mobile data and the next account that server hasn't banned, in one step. Your crosshair, binds and video settings can follow you to every account (<a href="../copy-cs2-config/">copy your CS2 config between accounts</a>), and so can a name and avatar (<a href="../steam-profile-editor/">Steam profile editor</a>). <a href="../cs2-ip-ban/">How to tell which ban you have</a>.</p>`],
    ],
    faq: [
      ['How do I switch Steam accounts without typing my password?', 'Log in to each account once with Remember me ticked. After that the app switches between them in one click, or with Ctrl+Shift+F9 from inside the game, without asking for a password.'],
      ['Does it store my Steam password?', 'No. It uses the logins Steam already saved on your PC, and switching never asks for your password or your Steam Guard codes. The only place you type a password is Sign in on the Profiles page: it\'s used once and never saved, and only Steam\'s sign-in token is kept, encrypted with Windows.'],
      ['Will switching get my account banned?', 'Switching only changes which saved account Steam logs in to, the same as picking it in Steam yourself.'],
    ],
  },
  {
    slug: 'steam-profile-editor',
    title: 'Change name and avatar on all your Steam accounts at once',
    desc: 'Bulk edit Steam profiles: name, avatar, real name, bio, country, privacy and custom URL on many accounts from one template, with preview and restore.',
    nav: 'Steam profile editor',
    blurb: 'Name, avatar and privacy on many accounts.',
    eyebrow: 'Steam profile editor',
    h1: 'Change the name and avatar on all your Steam accounts at once',
    lead: 'The Profiles page of CS2 IP Changer changes the name, picture, real name, bio, country, privacy and custom URL of the Steam accounts you pick, from templates you save. You see each account now and after before anything is sent.',
    crop: ['profiles', 700, 96, 528, 300, 'The Profiles page: Make the others look like, each account after the change, and its sign-in with Sign out'],
    related: ['steam-account-switcher', 'copy-cs2-config', 'is-it-safe'],
    sections: [
      ['What you can do', `<ul><li><b>Templates.</b> Save a look (name, picture, real name, bio, country, privacy, custom URL) and apply it to any accounts.</li><li><b>Make the others look like</b> one account in one click, or copy the look of any public Steam profile from its link.</li><li><b>Numbers and names:</b> <code>nightfall {n}</code> gives nightfall 1, nightfall 2 and so on; <code>{name}</code> keeps each account's current name, so <code>{name} | NV</code> adds a tag.</li><li><b>Preview first.</b> Each account is shown now and after. Apply changes them one after another with a pause, and each result is shown in plain words.</li><li><b>Undo.</b> A copy of each profile, picture included, is kept before the first change. Restore original puts it back.</li><li><b>New accounts</b> can get a template on their own the first time they show up.</li></ul>`],
      ['Your login stays yours', `<p>Changing a profile needs a Steam web session, like your browser has. You sign in the way Steam's own login page works (password and Steam Guard, or a QR code from the Steam app). The password goes straight into Steam's encryption for that sign-in and is never saved. The app keeps only Steam's sign-in token, encrypted with Windows so only your Windows user can open it, and you can end it any time with Sign out on the Profiles page, or in Steam &gt; Settings &gt; Security &gt; Authorized devices.</p><p>The app refuses a name, bio or URL that would show a login saved on your PC, and pictures are saved again as a new JPG, so photo data like GPS never reaches Steam.</p>`],
    ],
    faq: [
      ['Can I change the name on all my Steam accounts at once?', 'Steam itself has no way to edit several accounts in one go. The Profiles page does it: pick the accounts, pick a template or one account\'s look, check the preview and click Apply. The accounts are changed one after another, and Restore original puts each one back.'],
      ['Can it clear "previously known as"?', 'Yes, with Clear name history, which is off unless you tick it. That one is permanent: Steam has no way back.'],
      ['Where are my templates kept?', 'On your PC only, in SRT_Dados/profiles. Nothing leaves your PC except what you apply to Steam.'],
    ],
  },
  {
    slug: 'change-ip-with-phone',
    title: 'Change your CS2 IP with an Android phone (USB tethering)',
    desc: "Set up your Android phone's mobile data for CS2 only: USB tethering, game-only routing, a kill switch and a new IP in one click over ADB. Free for Windows.",
    nav: 'Change IP with your phone',
    blurb: 'Setup, game-only routing and a new IP in one click.',
    eyebrow: 'Setup guide',
    h1: 'Set up an Android phone for CS2: USB tethering, and a new IP in one click',
    lead: 'The whole setup, start to finish: the phone shares its mobile data over USB, only CS2, Steam and your servers use it, and Change IP gets a new carrier IP from the PC.',
    crop: ['connection', 91, 44, 569, 452,'The Connection page: protection status, the IP servers see next to your home IP, and the phone ready to change IP'],
    related: ['cs2-iphone-hotspot', 'cs2-ip-ban', 'is-it-safe'],
    sections: [
      ['What you need', `<ul><li>Windows 10 or 11 (64-bit) and admin rights: routes and firewall rules are system settings.</li><li>Windows Firewall turned on. Third-party firewalls that turn it off (some antivirus suites) leave the app without a kill switch, and it says so.</li><li>An Android phone or iPhone with mobile data, and a USB cable or the phone's Wi-Fi hotspot.</li><li>USB debugging on the phone, only if you want to change IP from the PC.</li></ul>`],
      ['Set it up', `<ol><li><b>Download <code>CS2IPChanger.exe</code></b> from the <a href="${LATEST}">latest release</a> into a folder only admins can change, like <code>C:\\Program Files\\CS2 IP Changer</code>. It runs as admin, so it doesn't run from Downloads: started there, it offers to install itself in that folder in one click. Windows warns once: the exe isn't code-signed yet, so SmartScreen asks first. More info &gt; Run anyway, or <a href="../is-it-safe/#check-your-download">check the download</a> first. Updates are checked with a signature.</li><li><b>Plug the phone in by USB,</b> turn on USB tethering (Personal Hotspot on an iPhone) and turn off the phone's Wi-Fi, so it shares mobile data and not your home Wi-Fi. While the phone's Wi-Fi is on, the app keeps your servers and CS2 blocked.</li><li><b>Run the app.</b> Protection starts off, so nothing is blocked yet. Paste the servers you play on into Servers: <code>IP:port</code>, <code>connect IP:port</code> or <code>steam://connect</code> links (an IP without a port gets the Default port, 27015 unless you change it).</li><li><b>Something missing?</b> Phone setup under Phone lists the steps (the cable and its driver, USB debugging, tethering, the phone's Wi-Fi, where other apps go). Each one checks itself and turns green, and where the app can do it there is a button.</li><li><b>Turn on protection</b> in Status on Connection. With CS2 and Steam running, every check should be green, including Phone IP is not your home IP. Run leak test to be sure.</li></ol><p>On the hotspot instead of a cable? Connect the PC to it and pick it under Connection &gt; How the PC connects to the phone.</p>`],
      ['Change IP from the PC', `<p>On an iPhone, switch Airplane Mode on and off on the phone instead: only Android lets a PC do it (<a href="../cs2-iphone-hotspot/">CS2 with an iPhone</a>). On Android, turn on USB debugging on the phone and allow the PC when the phone asks. If ADB isn't installed, the app can download Google's platform-tools for you (about 7 MB).</p><p>Change IP then turns mobile data (or airplane mode) off and on until the carrier hands you a new IP, and keeps the old IPs in a list so you can see it changed. It also tells you if the new IP is in a new /16 range, because some admins ban whole ranges; Settings &gt; Keep changing until the IP is in a new range keeps going until it is. On the hotspot, Change IP works if the phone is also plugged in by USB with USB debugging on.</p>`],
      ['Game only, whole PC or no phone', `<ul><li><b>Game only:</b> CS2, Steam and your servers use the phone. The browser, Discord and downloads stay on your home connection and don't eat mobile data.</li><li><b>Whole PC:</b> everything goes through the phone, DNS included, and IPv6 is blocked so nothing leaves from home.</li><li><b>No phone:</b> play on your home connection. Nothing is blocked, servers see your home IP, and the app says so. Accounts, Configs, Profiles and Regions keep working.</li></ul><p>Once a phone has been connected, the kill switch (Block if the phone drops, in Checks) keeps your servers and CS2's game traffic on it: a pulled cable cuts CS2 off instead of falling back to your home IP. Before that, CS2 isn't locked, so trying the app without a phone never leaves CS2 unable to connect at home.</p>`],
    ],
    faq: [
      ['Can Windows send only CS2 through my phone\'s hotspot?', 'Not by itself: when you tether a phone, Windows has no setting to pick one app for it. CS2 IP Changer adds the routes for CS2, Steam and your servers, so only they use the phone and the rest of the PC stays on your home internet. You can still send the whole PC through the phone if you want.'],
      ['My carrier uses CGNAT. Is that a problem?', 'No. You share a public IP with other customers and servers see that shared one. Change IP still works, and when the carrier gives you the same IP back, the app keeps trying.'],
      ['Will my ping be worse?', 'Usually, yes. Mobile data adds latency and jitter, so expect a higher ping than at home. A good 4G or 5G signal helps a lot: put the phone near a window and keep it charging. The ping in Servers is measured through the phone, so you see it before you join, and Connection quality tells you when the connection itself is weak. While it is weak, Whole PC drops to Game only so CS2 doesn\'t share the phone with the rest of the PC; turn off Fall back to Game only on a weak connection in Settings > Mobile data to stop that.'],
      ['Does it work with a 4G or 5G USB dongle?', 'It works as the connection: if the app doesn\'t pick it on its own, choose it under Connection > How the PC connects to the phone. Change IP needs an Android phone with ADB, though.'],
    ],
  },
  {
    // Facts only from README.md (Quick start, the FAQ "Does it work with an iPhone?" and the VPN table). The iPhone steps of
    // Phone setup come with beta.11, so this page doesn't mention them until that release is out.
    slug: 'cs2-iphone-hotspot',
    title: "Play CS2 on your iPhone's Personal Hotspot, not your home IP",
    desc: "CS2 IP Changer works with an iPhone: Personal Hotspot by USB or Wi-Fi, only CS2 and Steam on it, a kill switch, and a new IP with Airplane Mode.",
    nav: 'CS2 with an iPhone',
    blurb: 'Personal Hotspot, and a new IP by Airplane Mode.',
    h1: "Play CS2 on your iPhone's mobile data, not your home IP",
    lead: "Yes, it works with an iPhone. The iPhone's USB tethering (Personal Hotspot) shows up in Windows like an Android phone, and so does its Wi-Fi hotspot, so community servers see your carrier's IP and not your home IP. One thing is different: for a new IP you switch Airplane Mode on and off on the iPhone yourself.",
    crop: ['connection', 91, 44, 569, 180, 'The Connection page: the IP servers see next to your masked home IP'],
    related: ['change-ip-with-phone', 'cs2-ip-ban', 'is-it-safe'],
    sections: [
      ['What works with an iPhone', `<ul><li><b>The protection, the same as on Android.</b> CS2, Steam and your servers go through the iPhone's mobile data (or the whole PC if you pick it), and Windows Firewall blocks them on your home connection, so those servers only see the carrier's IP.</li><li><b>The kill switch.</b> If the iPhone drops, CS2 loses the connection instead of using your home IP.</li><li><b>Live checks and the leak test</b> on the Connection page show the IP servers see.</li><li><b>Everything that doesn't need a phone:</b> Servers, Accounts, Configs, Profiles and Regions work the same.</li></ul>`],
      ['Set it up', `<ol><li><b>Download <code>CS2IPChanger.exe</code></b> from the <a href="${LATEST}">latest release</a> and put it in a folder only admins can change, like <code>C:\\Program Files\\CS2 IP Changer</code>. Started from Downloads or the Desktop, it offers to install itself there in one click. Windows warns once: the exe isn't code-signed yet, so SmartScreen asks first (<a href="../is-it-safe/#check-your-download">check the download</a>).</li><li><b>Plug the iPhone in by USB</b> and turn on Personal Hotspot, then turn off the iPhone's Wi-Fi, so it shares mobile data.</li><li><b>Open the app</b> (it asks for admin). Paste the servers you play on into Servers, then press Turn on protection in Status.</li><li><b>Check.</b> Open Connection with CS2 and Steam running. Every check should be green, and the IP servers see must not be your home IP.</li></ol>`],
      ['A new IP on an iPhone', `<p>Change IP from the PC needs ADB, which only Android has. On an iPhone, switch Airplane Mode on, then off again: when the iPhone is back on mobile data, the carrier usually hands out a new IP. The Connection page shows the IP servers see, so you can check it changed before you join again.</p><p>Carriers share each IP between many phones, so the same IP can come back; switch Airplane Mode on and off again. On Android, one click does all of this, and Change IP and rejoin (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F10</kbd>) joins the server again (<a href="../change-ip-with-phone/">the Android setup</a>).</p>`],
      ['iPhone or Android', `<div class="tbl"><table><thead><tr><td></td><th scope="col">iPhone</th><th scope="col">Android</th></tr></thead><tbody>
<tr><th scope="row">Server sees</th><td class="us">Your carrier's mobile IP</td><td class="us">Your carrier's mobile IP</td></tr>
<tr><th scope="row">How it connects</th><td>Personal Hotspot, by USB cable or Wi-Fi</td><td>USB tethering or the hotspot</td></tr>
<tr><th scope="row">Kill switch</th><td>Yes, in Windows Firewall</td><td>Yes, in Windows Firewall</td></tr>
<tr><th scope="row">New IP</th><td>Airplane Mode on and off, on the iPhone</td><td>One click from the PC (mobile data off and on over ADB)</td></tr>
</tbody></table></div>`],
    ],
    faq: [
      ['Does CS2 IP Changer work with an iPhone?', "Yes, the protection does: an iPhone's USB tethering (Personal Hotspot) shows up in Windows like an Android one, and so does its Wi-Fi hotspot. Change IP from the PC needs ADB, which only Android has, so on an iPhone you switch Airplane Mode on and off yourself."],
      ['Why can\'t the app change the IP on my iPhone?', 'Change IP turns mobile data off and on from the PC over ADB, and only Android has ADB. On an iPhone, Airplane Mode on and off does the same by hand.'],
      ['Is it a VPN?', "No. Servers see an ordinary mobile connection from your carrier, the same as someone playing on a phone hotspot, so it usually gets in on servers that block VPNs. It's not a promise: carriers share each IP between many phones, so an IP someone else misused can be flagged."],
    ],
  },
  {
    slug: 'cs2-ip-ban',
    title: 'Does CS2 IP ban? Community server bans and getting back in',
    desc: 'Does CS2 IP ban? Valve doesn\'t, community servers can. Tell an IP ban from a SteamID or range ban, and get back in with your phone\'s mobile data.',
    nav: 'CS2 IP ban',
    blurb: 'Which ban you have and how to get back in.',
    eyebrow: 'CS2 IP ban bypass',
    h1: 'Does CS2 IP ban? VAC no, community servers yes',
    lead: 'Valve doesn\'t IP ban: VAC bans, game bans and cooldowns are on your Steam account, not your IP. Community servers can: their admins and plugins ban your account, your IP or a whole range. Find out which one you have first; when it\'s the IP, a new one gets you back in, for example by changing your IP with your phone.',
    leadHtml: 'Valve doesn\'t IP ban: VAC bans, game bans and cooldowns are on your Steam account, not your IP. Community servers can: their admins and plugins ban your account, your IP or a whole range. Find out which one you have first; when it\'s the IP, a new one gets you back in, for example by <a href="../change-ip-with-phone/">changing your IP with your phone</a>.',
    crop: ['accounts', 655, 146, 552, 192, 'The Accounts page: one account banned on one server and on cooldown, another with a VAC ban'],
    related: ['change-ip-with-phone', 'steam-account-switcher', 'hide-ip-cs2'],
    sections: [
      ['Which ban do you have?', `<div class="tbl fit"><table><thead><tr><th scope="col">What you see</th><th scope="col">The ban</th><th scope="col">What gets you back in</th></tr></thead><tbody>
<tr><td>Another Steam account on the same connection is refused too</td><td>IP ban</td><td class="us">A new IP</td></tr>
<tr><td>The new IP is refused too</td><td>Range ban, often a whole /16</td><td class="us">An IP in a new range</td></tr>
<tr><td>The same account is refused from another connection</td><td>Ban on that SteamID</td><td class="us">Another account; a new IP won't lift it</td></tr>
<tr><td>VAC or game ban on the profile, or a competitive cooldown</td><td>Valve ban on the account</td><td>Nothing on this page: a new IP doesn't change it</td></tr>
</tbody></table></div><p>If CS2's console shows <code>NETWORK_DISCONNECT_STEAM_BANNED</code>, that's the server's own ban list refusing your SteamID, not Steam or VAC. Many servers ban the IP and the account together.</p>`],
      ['Get back in, step by step', `<ol><li><b>Without any app:</b> restarting the router sometimes gives you a new IP, if your provider hands out dynamic IPs, but it's still your home line. Playing on your phone's hotspot works too, since the server then sees the carrier's IP. Before you download anything, <a href="../is-it-safe/">check that it is safe</a>.</li><li><b>With CS2 IP Changer:</b> plug in the phone once (<a href="../change-ip-with-phone/">setup</a>), and from then on Change IP and rejoin does it in one click: mobile data off and on over ADB, a new carrier IP, and back on the server. <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F10</kbd> does it from inside the game.</li><li><b>The account too:</b> after a ban the app logs into the next account that server hasn't banned, because the ban is often on the IP and the account.</li><li><b>Range ban:</b> the app tells you whether the new IP is in a new /16 and can keep trying until it is.</li><li><b>Valve ban:</b> when Steam or VAC refuses the account instead, the app says so and doesn't offer a new IP, because it wouldn't help.</li></ol>`],
      ['What the app remembers', `<ul><li>The account in use is marked as banned on that server and on the rest of the same community (the same tag in the server name, or a community name you set). New IP + next account skips it there, and joining with it asks first.</li><li>Your home IP never reaches the server, so it never lands on a ban list (<a href="../hide-ip-cs2/">every way to hide your IP from CS2 servers, compared</a>). No app can stop an admin from banning you; this keeps the ban off your house.</li></ul>`],
    ],
    faq: [
      ['Does CS2 IP ban?', 'Valve\'s bans (VAC bans, game bans and cooldowns) are on your Steam account, not your IP. Community servers are different: their admin plugins can ban a SteamID, an IP address or a whole IP range.'],
      ['Will a new IP lift a VAC ban?', 'No. VAC and game bans are on the Steam account, not the IP, and nothing in CS2 IP Changer hides cheats from VAC or any other anti-cheat.'],
      ['Some admins ban a whole IP range. Does that matter?', 'After each change the app tells you whether the new IP is in a new /16 or in the same one as before. If you want it to keep going until the range changes, turn on Settings > Keep changing until the IP is in a new range.'],
    ],
  },
  {
    slug: 'hide-ip-cs2',
    title: 'Hide your home IP from CS2 community servers without a VPN',
    desc: 'Who sees your IP on CS2 community servers, what they can do with it, and every way to hide it compared: VPN, gaming networks, proxies and mobile data.',
    nav: 'Hide your IP without a VPN',
    blurb: 'Who sees your IP, and every method compared.',
    eyebrow: 'Home IP protection',
    h1: 'Hide your home IP from CS2 community servers, without a VPN',
    lead: 'On a community server you join directly, the server admins and their plugins see your IP. You can\'t hide that an IP is there; you can choose which one it is.',
    crop: ['connection', 91, 44, 569, 227,'The Connection page: the IP servers see next to your masked home IP'],
    related: ['change-ip-with-phone', 'cs2-ip-ban', 'is-it-safe'],
    sections: [
      ['Who sees your IP', `<ul><li><b>Community server admins and their plugins.</b> CS2 game traffic is UDP, sent straight between your PC and the server, and the server needs an address to send packets back. Whoever runs it can see and log that address, and their admin tools act on it.</li><li><b>Not official matchmaking.</b> Valve's matchmaking goes through Steam Datagram Relay, so other players there don't get your IP.</li></ul>`],
      ['What they can do with it', `<ul><li><b>Ban it.</b> An IP ban covers every account you play from that connection, and some admins ban a whole range (<a href="../cs2-ip-ban/">how bans work</a>).</li><li><b>Place it.</b> An IP shows your provider and a rough location.</li><li><b>Aim at it.</b> An IP is what a DDoS hits. With your home IP, that's your home connection.</li></ul>`],
      ['The options, compared', `<div class="tbl"><table><thead><tr><td></td><th scope="col">Server sees</th><th scope="col">Works where VPNs are blocked</th><th scope="col">Cost</th></tr></thead><tbody>
<tr><th scope="row">Phone's mobile data with CS2 IP Changer</th><td class="us">Your carrier's IP, like any player on a hotspot</td><td class="us">Yes: it's an ordinary mobile connection</td><td class="us">Free app, your data plan</td></tr>
<tr><th scope="row">VPN</th><td>A datacenter IP</td><td>Often blocked</td><td>Usually a subscription</td></tr>
<tr><th scope="row">Gaming network (GPN)</th><td>The provider's exit IP</td><td>Depends on the provider</td><td>Usually a subscription</td></tr>
<tr><th scope="row">SOCKS or HTTP proxy</th><td>Your home IP (game traffic is UDP)</td><td>Not useful</td><td>Varies</td></tr>
<tr><th scope="row">Restarting the router</th><td>A new home IP, still yours</td><td>Yes, but it's still your home line</td><td>Free, if your IP is dynamic</td></tr>
</tbody></table></div>`],
      ['Why mobile data', `<p>The server sees an ordinary mobile connection from your carrier, not a datacenter, so it usually gets past anti-VPN plugins. Playing on the phone's hotspot by hand gets you that much. It works with an Android phone or an iPhone (on an iPhone the new IP comes from Airplane Mode: <a href="../cs2-iphone-hotspot/">CS2 with an iPhone</a>). The app adds what is hard by hand: only the game uses the phone, a kill switch keeps it there if the cable comes out, live checks and a leak test show the IP servers see, and Change IP gets a new one. <a href="../change-ip-with-phone/">Set it up</a>.</p>`],
    ],
    faq: [
      ['Does official matchmaking show my IP?', 'Valve\'s matchmaking goes through Steam Datagram Relay, so other players don\'t get your IP there. Community servers you join directly are the ones that see it.'],
      ['Can a community server admin see my IP?', 'Yes. The server needs your IP to send you game packets, so the admins and their plugins can see it, log it and ban it. With your phone\'s mobile data, what they get is your carrier\'s IP.'],
      ['What does cl_hideserverip do?', 'It is a console setting for streamers: with cl_hideserverip 1, CS2 hides the IP of the server you are on from your own screen (console and status), so viewers can\'t see it. It doesn\'t hide your IP: the server still sees your home IP. To hide that, the server has to see another connection, like your phone\'s mobile data.',
        'It is a console setting for streamers: with <code>cl_hideserverip 1</code>, CS2 hides the IP of the server you are on from your own screen (console and status), so viewers can\'t see it. It doesn\'t hide your IP: the server still sees your home IP. To hide that, the server has to see another connection, like <a href="../change-ip-with-phone/">your phone\'s mobile data</a>.'],
    ],
  },
  {
    slug: 'is-it-safe',
    title: 'Is CS2 IP Changer safe? What it changes and how to check it',
    desc: 'What CS2 IP Changer changes on your PC, why it needs admin, how to verify the download (SHA-256, build attestation), signed updates, and how to undo it.',
    nav: 'Is it safe?',
    blurb: 'What it changes and how to check the download.',
    eyebrow: 'Built to be checked',
    h1: 'Is CS2 IP Changer safe?',
    lead: 'A tool that asks for admin and changes your network should explain itself. Here is what CS2 IP Changer changes on your PC, what it never touches, and how you can check every release yourself.',
    crop: ['connection', 660, 44, 569, 322, 'The Connection page: live checks for Windows Firewall, Block if the phone drops, the routes and the IP servers see'],
    related: ['change-ip-with-phone', 'hide-ip-cs2', 'steam-account-switcher'],
    sections: [
      ['What it changes, and what it never touches', `<ul><li><b>Changes:</b> Windows routes and Windows Firewall rules (all named with the app's prefix), which saved Steam account logs in, and, only if you use those pages, CS2's config files and your Steam profiles.</li><li><b>Never touches:</b> the game process, its memory or its program files. It hides nothing from VAC or any anti-cheat.</li><li><b>Why admin:</b> routes and firewall rules are system settings, and Windows only lets admins change them. Steam, CS2, your browser and the programs it opens for you still run as you, not as admin.</li></ul>`],
      ['Check your download', `<ul><li>Every release is built by GitHub Actions from the public source and comes with a <b>build attestation</b>. With the GitHub CLI: <code>gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer</code>.</li><li>Each exe has a <b>SHA-256</b> file next to it in the release. In PowerShell: <code>Get-FileHash .\\CS2IPChanger.exe</code>, and compare.</li><li><b>Updates checked with a signature.</b> The app only installs an update whose SHA-256 matches the release and is signed with the project's offline key, with the version inside so an old release can't come back under a new tag. The key isn't on GitHub, so access to the repo alone can't push an exe to installed copies.</li><li><b>VirusTotal</b> for each release: <a href="${VT}">${vtText}</a> flag ${short} (SHA-256 <code>${rel.sha256.slice(0, 12)}…</code>, the same as its <code>.sha256</code> file).</li><li>The exe isn't code-signed, so Windows SmartScreen may warn the first time. More info &gt; Run anyway.</li></ul>`],
      ['Your data', `<ul><li>No account, and no passwords kept. The account switcher uses the logins Steam already saved.</li><li>Usage stats, required to use the app in the beta: the first start shows what is sent, with Accept and Quit (Quit closes the app without changing anything). It sends them when the app has been open for 20 seconds, every 30 minutes while it stays open and when it closes, with a short signal every 5 minutes of what is on (protection, CS2 open, the screen): a random ID made for this install, with no name (Settings > Delete my stats deletes everything kept under it), versions, your country, the mode, how the phone is connected, minutes open, button counts, where on each screen people click (a coarse grid, added up with everyone, with no ID), a few counters, list sizes and errors, and in rough bands how long ago the app was set up, each CS2 match's ping, jitter and lag (never the server), session length and crashes. Once a day a separate report with no ID and no country says the Windows language, window and screen scale, the Windows build, the phone's brand (never the model), Android version and 4G or 5G, and is only added into counts. Never IPs, servers, accounts or names. The server code is public (<a href="${REPO}/tree/main/worker/stats">worker/stats</a>).</li><li>Who keeps them: the author of this project (Sepd0x on GitHub), only to find and fix bugs and make the app better, never sold or shared. The basis is that you accept the notice. They are stored on Cloudflare, which processes them for the project. Rows with an ID are deleted after 12 months; counts with no ID stay. Delete my stats deletes yours at once; for anything else, open an <a href="${REPO}/issues">issue on GitHub</a>, or complain to your data protection authority.</li><li>This website counts visits and clicks on Download with no cookies and no ID: the page, the name of the site you came from (never the page there), or the short name in the link when there is none (like <code>?ref=tiktok</code>), and the country, only as counts per day. With Do Not Track or Global Privacy Control on, it sends nothing. GitHub Pages, which hosts it, keeps its own logs.</li><li>To show the carrier and city, the app asks ipwho.is about your home IP and the phone's IP, so ipwho.is receives both, each one only through its own connection. Your public IP is measured with ipify, icanhazip, Amazon and Cloudflare. The full list, and what the app does with admin rights: <a href="${REPO}/blob/main/docs/PRIVACY-AND-SECURITY.md">Privacy and security</a>.</li><li>Save bug report saves a .zip on your Desktop and sends nothing. It leaves out your current and last home IP, API key, logins, names, SteamIDs, e-mails, Wi-Fi names and your Windows user; look through it before you post it.</li></ul>`],
      ['Undo everything', `<p>Turn off protection (on the Connection page, or in the icon&#39;s menu next to the clock) removes the routes and the kill switch, and Allow all (block off) on the Regions page lifts the region block. To remove everything at once, run <code>CS2IPChanger.exe --restore</code> (or <code>tools/restore.cmd</code> from the repo): every route and firewall rule the app made is removed. To uninstall, use Uninstall in Windows Settings &gt; Installed apps, or Remove CS2 IP Changer at the bottom of Settings: it removes the rules, routes, the Start with Windows task, <code>C:\\ProgramData\\CS2IPChanger</code> and the app, and you pick whether <code>SRT_Dados</code> (settings and accounts) stays.</p>`],
    ],
    faq: [
      ['Can this get me VAC banned?', 'It never touches the game process, its memory or its program files. It changes Windows routing and firewall rules and which Steam account logs in. If you use them, the Configs page copies CS2\'s config files between your accounts and the Profiles page changes your Steam profiles. It also won\'t hide a cheat from VAC or any other anti-cheat, so don\'t use it for that.'],
      ['Is it open source?', 'The source is public on GitHub so anyone can read and build it, but the license is All rights reserved during the beta: you can use the releases, not copy or redistribute the code.'],
      ['Why does my antivirus complain?', 'The exe is not code-signed and it changes firewall rules and runs ADB, which some antivirus engines flag on heuristics alone. Check the attestation and SHA-256 above; if a detection looks wrong, open an issue.'],
    ],
  },
];

const bySlug = Object.fromEntries(pages.map((p) => [p.slug, p]));

// The page's sections, with the how-to (if any) after the first one.
const sectionsOf = (p) => p.howto
  ? [p.sections[0], [p.howto.h, `<ol class="howto">${p.howto.steps.map(([n, t, html], i) => `<li id="step-${i + 1}"><b>${esc(n)}.</b> ${html || esc(t)}</li>`).join('')}</ol>`, p.howto.id], ...p.sections.slice(1)]
  : p.sections;

// Each guide's readout: one number from its page of the app (example values, as on the screenshots) or the release itself.
const SIG = {
  'change-ip-with-phone': ['198.51.100.23', 'IP servers see', 'Example IP, not yours', 'From the Connection page of the app. Example IPs only.'],
  'cs2-iphone-hotspot': ['192.0.2.140', 'IP servers see after Airplane Mode', 'Example IP, not yours', 'Airplane Mode on and off on the iPhone. Example IPs only.'],
  'cs2-ip-ban': ['192.0.2.140', 'New IP after a ban', 'Example IP, new range (was 198.51)', 'From the log of Change IP and rejoin. Example IPs only.'],
  'hide-ip-cs2': ['203.0.•••.•••', 'Your home IP, as the app shows it', 'Masked', "Servers see the carrier's IP; your home IP stays masked in the app too."],
  'is-it-safe': [`${rel.virustotal.flagged}/${rel.virustotal.engines}`, 'VirusTotal engines that flag it', rel.tag, 'The latest release, scanned when it comes out.'],
  'steam-account-switcher': ['8/8', 'Accounts', 'Example', 'From the Accounts page of the app, with example names.'],
  'copy-cs2-config': ['7', 'Settings that differ between two accounts', 'Example', 'From Compare on the Configs page of the app.'],
  'cs2-region-picker': ['8/30', 'Regions CS2 can play in', 'Example', 'From the Regions page of the app.'],
  'steam-profile-editor': ['6/8', 'Accounts picked for one template', 'Example', 'From the Profiles page of the app, with example names.'],
};

// Each guide's piece of proposal A's world (site/assets/guides/src/world.js): the scene, what it shows in words (on screen
// under the picture, and the canvas's text for screen readers), and its replay button. Example values only, as in the README.
const WORLD = {
  'change-ip-with-phone': ['phone', "Only CS2, Steam and your servers take the phone; the browser, Discord and downloads stay home. Example IP.", 'Play it again'],
  'cs2-iphone-hotspot': ['iphone', "Airplane Mode on and off on the iPhone, and the server usually sees another carrier IP. Example IPs.", 'Play it again'],
  'cs2-ip-ban': ['ban', "Demo: banned, mobile data off and on (Airplane Mode on an iPhone), back on a new IP with the next account.", 'Play it again'],
  'hide-ip-cs2': ['hide', "Windows Firewall blocks CS2 and your servers at home, so the server only sees the carrier's IP. Example IPs.", 'Play it again'],
  'is-it-safe': ['safe', "Public source, a GitHub Actions build with an attestation, a SHA-256 and a VirusTotal scan for each release.", 'Play it again'],
  'steam-account-switcher': ['accounts', "Example: the account banned on that server is skipped and the next one logs in, with no password typed.", 'Play it again'],
  'copy-cs2-config': ['configs', "Example: binds, crosshair, settings, video and HUD go to another account, with a backup of each change.", 'Play it again'],
  'cs2-region-picker': ['regions', "Example: the regions you turn off go dark, blocked for cs2.exe only; CS2 matches in the ones left on.", 'Play it again'],
  'steam-profile-editor': ['profiles', "Example: one template goes onto the accounts you pick, one after another, after a preview.", 'Play it again'],
};

// The caption under the app's own screen in the text: which page of the app it is, and that it is the real one.
const capOf = (p) => `The ${p.crop[0][0].toUpperCase() + p.crop[0].slice(1)} page of the app, a real screenshot${/accounts|profiles|configs/.test(p.crop[0]) ? ' with example names' : p.crop[0] === 'connection' ? ' with example IPs' : ''}. Select it to see it full size.`;

const CARD = `${BASE}img/social-preview.png?v=${rel.version}`, CARD_ALT = "CS2 IP Changer: IP banned on a CS2 server? New IP in one click, with the app's Connection screen";
function render(p, modified) {
  const url = `${BASE}${p.slug}/`;
  const image = `${BASE}img/${p.crop[0]}.png`;
  const ld = {
    '@context': 'https://schema.org',
    '@graph': [
      { '@type': 'WebPage', '@id': `${url}#page`, url, name: p.title, description: p.desc, inLanguage: 'en',
        isPartOf: { '@id': `${BASE}#website` }, about: { '@id': `${BASE}#app` }, author: { '@id': `${BASE}#author` },
        datePublished: firstAdded(`site/${p.slug}/index.html`), dateModified: modified,
        breadcrumb: { '@id': `${url}#crumbs` }, primaryImageOfPage: { '@type': 'ImageObject', url: image, width: 1240, height: 800 } },
      // The author once more here, so the page's own JSON-LD says who wrote it (a search engine doesn't follow an @id to another page).
      { '@type': 'Person', '@id': `${BASE}#author`, name: 'Sepd0x', url: 'https://github.com/Sepd0x' },
      { '@type': 'BreadcrumbList', '@id': `${url}#crumbs`, itemListElement: [
        { '@type': 'ListItem', position: 1, name: 'CS2 IP Changer', item: BASE },
        { '@type': 'ListItem', position: 2, name: p.nav, item: url }] },
      ...(p.howto ? [{ '@type': 'HowTo', '@id': `${url}#howto`, name: p.howto.h, description: p.desc, inLanguage: 'en', image,
        tool: [{ '@type': 'HowToTool', name: 'CS2 IP Changer (free, Windows 10 and 11)' }],
        step: p.howto.steps.map(([n, t], i) => ({ '@type': 'HowToStep', position: i + 1, name: n, text: t, url: `${url}#step-${i + 1}` })) }] : []),
      ...(p.faq?.length ? [{ '@type': 'FAQPage', '@id': `${url}#faq`, mainEntity: p.faq.map(([q, a]) => ({ '@type': 'Question', name: q, acceptedAnswer: { '@type': 'Answer', text: a } })) }] : []),
    ],
  };
  const sig = SIG[p.slug], world = WORLD[p.slug];
  if (!world) throw new Error(`${p.slug}: no scene in WORLD`);
  if (!sig) throw new Error(`${p.slug}: no readout in SIG`);
  const sections = sectionsOf(p);
  const toc = [...sections.map(([h, , id]) => [id || slug(h), h]),...(p.faq?.length ? [['faq', 'FAQ']] : [])];
  const related = p.related.map((s) => bySlug[s]);
  // The hero shot: the app's screen in a window. On a phone it shows the crop; on a PC it starts at the crop and runs on to
  // the screenshot's right edge, sized so the crop fills ~92% of the column and the rest runs off the screen, never
  // scaled past 1.5x (then it moves right, so it still runs off).
  const [cn, cx, cy, cw, ch, calt] = p.crop;
  const vx = cx, vw = 1239 - vx, pc = (n) => `${(n * 100).toFixed(2)}%`;
  const shotVars = [`--pa:${cw}/${ch}`, `--pw:${pc(1240 / cw)}`, `--px:${pc(-cx / cw)}`, `--py:${pc(-cy / cw)}`,
    `--da:${vw}/${ch}`, `--dw:${pc(1240 / vw)}`, `--dx:${pc(-vx / vw)}`, `--dy:${pc(-cy / vw)}`,
    `--dk:${(vw / (cx + cw - vx) / 0.92).toFixed(3)}`, `--wmax:${Math.round(vw * 1.5)}px`].join(';');
  const page = cn[0].toUpperCase() + cn.slice(1);
  const shot = `<figure class="gwin" style="${shotVars}"><div class="gwin-bar" aria-hidden="true"><img src="../img/icon.png" alt="" width="16" height="16">CS2 IP Changer <i>/</i> <b>${esc(page)}</b></div><a class="gwin-view zoom" href="../img/${cn}.png"><picture><source type="image/webp" srcset="../img/${cn}.webp"><img src="../img/${cn}.png" alt="${esc(calt)}" width="1240" height="800" loading="eager" fetchpriority="high"></picture><span class="vh">, open full size</span></a><figcaption class="gshot-cap">${esc(capOf(p))}</figcaption></figure>`;
  // Steps (every ordered list in the text) become a timeline: each step comes in, and the line to the next one fills.
  const steps = (html) => html.replace(/<ol( class="howto")?>([\s\S]*?)<\/ol>/g, (m, howto, inner) => `<ol class="tl${howto ? ' howto' : ''}">${inner.replace(/<li( |>)/g, '<li class="rv"$1')}</ol>`);
  const getBlock = `${download(url)}
          ${checks}
          <p class="safe">${safe(p.slug === 'is-it-safe')}</p>
          <p class="req">Beta · ${req}</p>`;
  const arrow = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 12h16"/><path d="m14 6 6 6-6 6"/></svg>';
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(p.title)}</title>
<meta name="description" content="${esc(p.desc)}">
<meta name="robots" content="index, follow, max-image-preview:large">
<link rel="canonical" href="${url}">
<link rel="icon" href="../img/favicon.ico" sizes="32x32"><link rel="icon" href="../img/favicon.svg" type="image/svg+xml"><link rel="apple-touch-icon" href="../img/apple-touch-icon.png">
<meta name="theme-color" content="${themeColor}">
<meta property="og:type" content="article">
<meta property="og:site_name" content="CS2 IP Changer">
<meta property="og:title" content="${esc(p.title)}">
<meta property="og:description" content="${esc(p.desc)}">
<meta property="og:url" content="${url}">
<meta property="og:image" content="${CARD}">
<meta property="og:image:width" content="1280">
<meta property="og:image:height" content="640">
<meta property="og:image:alt" content="${esc(CARD_ALT)}">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:image" content="${CARD}">
<meta name="twitter:image:alt" content="${esc(CARD_ALT)}">
<link rel="preload" href="../fonts/funnel-sans.woff2" as="font" type="font/woff2" crossorigin>
<link rel="preload" href="../fonts/funnel-display.woff2" as="font" type="font/woff2" crossorigin>
<link rel="preload" href="../fonts/fragment-mono.woff2" as="font" type="font/woff2" crossorigin>
<script>(function(h){h.classList.add('js');var o=0;try{o=localStorage.getItem('cs2-fx')==='on'}catch(x){}if(!matchMedia('(prefers-reduced-motion: reduce)').matches)h.classList.add('fx');else if(o)h.classList.add('fx','fx-opt')})(document.documentElement)</script>
<script>
/* the loading screen: only with JS and WebGL. The line fills (its own transform: a custom property on <html> made every step restyle the whole page) as the page gets ready (HTML, fonts, the 3D script) and the page
   comes in on the world's first frame, or after 6 s (on a phone or tablet it is gone by about 1.5 s, 6 Nov.: the first words wait no longer), or at once without WebGL. While it shows, the wheel, touch and the
   scroll keys wait, so nothing moves under it. Reduced motion: the line steps and the screen goes at once */
(function(h){try{var c=document.createElement('canvas');if(!(c.getContext('webgl2')||c.getContext('webgl')))return}catch(x){return}
h.classList.add('ld','ldu');var t0=performance.now(),p=0,done=false,K={32:1,33:1,34:1,35:1,36:1,37:1,38:1,39:1,40:1},O={capture:true,passive:false},
hold=function(e){if(e.type!=='keydown'||K[e.keyCode]){e.preventDefault();e.stopImmediatePropagation()}},
set=function(v){if(v>p){p=v;var e=document.querySelector('.ld-bar b');if(e)e.style.transform='scaleX('+v+')'}},
end=function(e){if(done)return;done=true;if(e&&e.type==='worldready')h.classList.add('ldw');set(1);['wheel','touchmove','keydown'].forEach(function(t){removeEventListener(t,hold,O)});
setTimeout(function(){h.classList.add('ld-out');setTimeout(function(){h.classList.remove('ld','ld-out')},400)},Math.max(90,300-(performance.now()-t0)))};
['wheel','touchmove','keydown'].forEach(function(t){addEventListener(t,hold,O)});
window.__ldStep=set;window.__ldEnd=end;set(.12);
document.addEventListener('worldready',end,true);setTimeout(end,matchMedia('(pointer:coarse)').matches?1100:6000);
document.addEventListener('DOMContentLoaded',function(){set(.25)});
document.addEventListener('worldprogress',function(e){set(.45+.52*(+e.detail||0))},true);
if(document.fonts)document.fonts.ready.then(function(){set(.35)});
new MutationObserver(function(){if(h.classList.contains('no-gl'))end()}).observe(h,{attributes:true,attributeFilter:['class']});
addEventListener('pageshow',function(e){if(e.persisted)end()})})(document.documentElement)
</script>
<script type="application/ld+json">
${JSON.stringify(ld, null, 1)}
</script>
${style}
</head>
<body>
<a class="skip" href="#main">Skip to content</a>
<div class="ld-scr" aria-hidden="true"><div class="ld-in"><span class="ld-name"><img src="../img/icon.png" alt="" width="28" height="28">CS2 IP Changer</span><span class="ld-bar"><b></b><i></i></span></div></div>
<header class="top">
  <div class="wrap">
    <a class="brand" href="../"><img src="../img/icon.png" alt="" width="28" height="28">CS2 IP Changer</a>
    <nav aria-label="Main">
      <a class="hide-s" href="../#features">Features</a>
      <a class="hide-s" href="../#how">How it works</a>
      <a class="hide-s" href="../is-it-safe/">Is it safe?</a>
      <a class="hide-s" href="../#guides">Guides</a>
      <a class="btn btn-s btn-ghost" href="${REPO}" title="Star CS2 IP Changer on GitHub">${star}<span>Star<span class="hide-s"> on GitHub</span></span></a>
      <a class="btn btn-s get-desk" href="${EXE}">Download</a>
      <button class="btn btn-s get-top" type="button" data-share data-copy="${url}">Send to PC</button>
    </nav>
  </div>
  <span class="prog" aria-hidden="true"></span>
</header>
<main id="main" class="gmain">
  <div class="gstage" data-world="${world[0]}">
  <div class="gw" aria-hidden="true"><canvas class="gw-cv"></canvas></div>
  <div class="wrap ghero">
    <div class="ghero-copy">
      <p class="crumbs"><a href="../">CS2 IP Changer</a><i aria-hidden="true">/</i><span>${esc(p.nav)}</span></p>
      <h1${p.h1.length > 56 ? ' class="long"' : ''}>${esc(p.h1)}</h1>
      <p class="lead">${p.leadHtml || esc(p.lead)}</p>
      <div class="groom" aria-hidden="true"></div>
      <div class="gw-foot">
        <p class="gw-cap">${esc(world[1])}</p>
        <div class="gw-ctl"><button class="demo gw-pause" type="button" hidden aria-pressed="false" title="Pause motion">Pause motion</button><button class="demo gw-play" type="button" hidden><i><svg viewBox="0 0 10 10" aria-hidden="true"><path d="M2.5 1.5v7l6-3.5z" fill="currentColor"/></svg></i>${esc(world[2])}</button></div>
        <p class="vh" role="status" aria-live="polite"></p>
      </div>
      <div class="ro ro-sig">
      <div class="ro-head"><span>${esc(sig[1])}</span><span class="ro-state"><i class="led"></i>${esc(sig[2])}</span></div>
      <p class="ro-num" style="--em:${[...sig[0]].reduce((a, c) => a + (c === '.' || c === ' ' ? .34 : c === '/' ? .44 : .6), 0).toFixed(2)}"><span class="vh">${esc(sig[1])}: ${esc(sig[0].replace("/", " of "))}${/example/i.test(sig[2]) ? ' (example)' : ''}</span><span class="digits" aria-hidden="true">${esc(sig[0])}</span></p>
      <div class="ro-rule" aria-hidden="true"></div>
      <div class="ro-foot"><p>${esc(sig[3])}</p></div>
      </div>
    </div>
  </div>
  </div>
  <div class="gline">
  <div class="wrap gl">
    <aside class="rail" aria-label="Download and contents">
      <div class="rail-in">
        <div class="gcard">
          ${getBlock}
        </div>
        <nav class="toc" aria-labelledby="toc-h">
          <h2 id="toc-h">On this page</h2>
          <ul>
            ${toc.map(([id, h]) => `<li><a href="#${id}">${esc(h)}</a></li>`).join('\n            ')}
          </ul>
        </nav>
      </div>
    </aside>
    <nav class="gtoc-m" aria-label="On this page"><p aria-hidden="true">On this page</p><ol>
      ${toc.map(([id, h]) => `<li><a href="#${id}">${esc(h)}</a></li>`).join('\n      ')}
    </ol></nav>
    <div class="gshot">${shot}</div>
    <article class="gtext">
${sections.map(([h, html, id], i) => `      <h2 id="${id || slug(h)}">${esc(h)}</h2>
      ${steps(labelTables(html))}${i ? '' : `
      <aside class="gget gcard" aria-label="Download">
          ${getBlock}
      </aside>`}`).join('\n')}
    </article>
${p.faq?.length ? `    <section class="gfaq" aria-labelledby="faq">
      <h2 id="faq">FAQ</h2>
      ${p.faq.map(([q, a, html]) => `<details><summary>${esc(q)}</summary><p>${html || esc(a)}</p></details>`).join('\n      ')}
    </section>` : ''}
    <nav class="gmore" aria-labelledby="more-h">
      <h2 id="more-h">Related guides</h2>
      <ul>
        ${related.map((r, i) => `<li class="rv" style="--i:${i}"><a href="../${r.slug}/"><b>${esc(r.nav)}</b><span>${esc(r.blurb)}</span>${arrow}</a></li>`).join('\n        ')}
      </ul>
      <div class="end">${download(url)}<p class="req">Free beta · ${req}</p></div>
      <p class="gstar">Useful? A <a href="${REPO}">star on GitHub</a> helps other players find it.</p>
    </nav>
  </div>
  </div>
</main>
${footer}
<script>
(function(){var b='../assets/js/',l=(document.documentElement.classList.contains('fx')?(matchMedia('(pointer: fine)').matches?['gsap.min.js','ScrollTrigger.min.js','lenis.min.js']:[]):[]).concat(['readout.js','../guides/world.min.js']);l.forEach(function(f){var s=document.createElement('script');s.src=b+f;s.async=false;if(/world/.test(f)){s.onload=function(){window.__ldStep&&__ldStep(.45);window.__gw&&window.__gw()};s.onerror=function(){document.documentElement.classList.add('no-gl')}}document.body.appendChild(s)})})();
</script>
<script>
(function(){
  var d=document;${shared}/* /shared */
  /* guides: the contents mark the section you are reading (the last heading above the top third of the screen) */
  var tl=[].slice.call(d.querySelectorAll('.toc a')),hs=tl.map(function(a){return d.getElementById(a.getAttribute('href').slice(1))});
  if(tl.length&&'IntersectionObserver' in window&&hs.every(Boolean)){
    var on=-1,mark=function(){var y=innerHeight*.34,k=0;hs.forEach(function(h,i){if(h.getBoundingClientRect().top<y)k=i});if(k!==on){if(on>=0){tl[on].classList.remove('on');tl[on].removeAttribute('aria-current')}tl[k].classList.add('on');tl[k].setAttribute('aria-current','location');on=k}};
    var tio=new IntersectionObserver(mark,{rootMargin:'0px 0px -66% 0px'});
    hs.forEach(function(h){tio.observe(h)});
  }
  /* guides: the line beside the contents fills as you read the text */
  var tu=d.querySelector('.toc ul'),art=d.querySelector('.gtext');
  if(tu&&art){var rq=0,rp=function(){rq=0;var r=art.getBoundingClientRect(),k=(innerHeight*.34-r.top)/Math.max(1,r.height);tu.style.setProperty('--rp',Math.min(1,Math.max(0,k)).toFixed(3))};addEventListener('scroll',function(){if(!rq){rq=1;requestAnimationFrame(rp)}},{passive:true});rp()}
  /* guides: this page's piece of proposal A's world, fitted to the free part of the hero (the right side on a PC, its own band on a phone) */
  var stg=d.querySelector('.gstage');
  window.__gw=function(){
    if(!stg||!window.GuideWorld||window.__gwOn)return;
    if(!window.__gwIdle){window.__gwIdle=1;(window.requestIdleCallback||function(f){setTimeout(f,150)})(function(){window.__gw()},{timeout:400});return}
    window.__gwOn=1;
    var gw=stg.querySelector('.gw'),cv=gw.querySelector('canvas'),room=stg.querySelector('.groom'),copy=stg.querySelector('.ghero-copy'),foot=stg.querySelector('.gw-foot'),top=d.querySelector('.top'),
      wide=function(){return innerWidth>=1000},
      place=function(){
        if(wide()){gw.style.removeProperty('--gw-top');gw.style.removeProperty('--gw-h');var sr=stg.getBoundingClientRect(),cr=copy.getBoundingClientRect();gw.style.setProperty('--gw-l',((cr.right-sr.left)/Math.max(1,sr.width)*100).toFixed(1)+'%');return}
        var s=stg.getBoundingClientRect(),r=room.getBoundingClientRect();gw.style.setProperty('--gw-top',(r.top-s.top)+'px');gw.style.setProperty('--gw-h',r.height+'px');
      },
      frame=function(W,H){
        if(!wide())return{box:[14,14,W-14,H-10]};
        var sr=cv.getBoundingClientRect(),cr=copy.getBoundingClientRect(),fr=foot.getBoundingClientRect(),th=top?top.offsetHeight:64;
        return{box:[cr.right-sr.left+24,th+18,W-Math.max(20,(W-cr.right+sr.left)*.04),fr.top-sr.top-14]};
      };
    place();addEventListener('resize',place);
    var fx=d.documentElement.classList.contains('fx'),opt=d.documentElement.classList.contains('fx-opt'),api=null,live=stg.querySelector('[role=status]'),pz=stg.querySelector('.gw-pause'),pl=stg.querySelector('.gw-play'),held=false;
    try{held=localStorage.getItem('cs2-motion')==='paused'}catch(x){}
    var vt=${JSON.stringify(rel.virustotal)};
    try{api=window.GuideWorld.mount(cv,{scene:stg.getAttribute('data-world'),still:!fx,img:'../img/',vt:vt,frame:frame})}catch(x){api=null}
    if(!api){d.documentElement.classList.add('no-gl');return}
    cv.addEventListener('worldready',function(){gw.classList.add('ready')},{once:true});
    if(held)api.pause();
    if(!api.still)pl.hidden=false;
    pl.addEventListener('click',function(){api.play();if(held){held=false;api.resume();if(pz){pz.setAttribute('aria-pressed','false')}try{localStorage.setItem('cs2-motion','')}catch(x){}}live.textContent='';setTimeout(function(){live.textContent='Playing it again.'},60)});
    if(fx&&pz){pz.hidden=false;pz.setAttribute('aria-pressed',held);pz.addEventListener('click',function(){held=!held;pz.setAttribute('aria-pressed',held);try{localStorage.setItem('cs2-motion',held?'paused':'');if(opt)localStorage.setItem('cs2-fx',held?'':'on')}catch(x){}if(held)api.pause();else api.resume()})}
    // the system asks for less motion: the page starts still and the same button is Play motion (remembered in this browser, then a reload loads the motion)
    else if(pz&&matchMedia('(prefers-reduced-motion: reduce)').matches&&(function(){try{localStorage.getItem('cs2-fx');return true}catch(x){return false}})()){pz.hidden=false;pz.removeAttribute('aria-pressed');pz.classList.add('play');pz.textContent=pz.title='Play motion';pz.addEventListener('click',function(){try{localStorage.setItem('cs2-fx','on');localStorage.setItem('cs2-motion','')}catch(x){return}location.reload()})}
  };
  if(window.GuideWorld)window.__gw();
})();
</script>
</body>
</html>
`;
}

// Each page keeps its date until its content changes: rendered with the old date, an unchanged page comes out the same.
// That date is the page's dateModified and its lastmod in the sitemap, so search engines see a real one.
for (const p of pages) {
  const file = join(site, p.slug, 'index.html');
  mkdirSync(dirname(file), { recursive: true });
  const old = existsSync(file) ? readFileSync(file, 'utf8') : '';
  const was = (old.match(/"dateModified": "(\d{4}-\d\d-\d\d)"/) || [])[1];
  // a new page (no file yet, or no date in it) is rendered with today's date
  let html = was ? render(p, was) : null;
  p.modified = html === old ? was : today;
  if (html !== old) html = render(p, today);
  writeFileSync(file, html);
}

// Sitemap: the home page with its screenshots, then every guide page. The home page's date is its last change in git
// (today while it has changes that aren't committed yet).
const shots = ['connection', 'servers', 'accounts', 'regions', 'configs', 'profiles'];
const sitemap = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">
  <url>
    <loc>${BASE}</loc>
    <lastmod>${lastChanged('site/index.html')}</lastmod>
${shots.map((s) => `    <image:image><image:loc>${BASE}img/${s}.png</image:loc></image:image>`).join('\n')}
  </url>
${pages.map((p) => `  <url>
    <loc>${BASE}${p.slug}/</loc>
    <lastmod>${p.modified}</lastmod>
  </url>`).join('\n')}
</urlset>
`;
writeFileSync(join(site, 'sitemap.xml'), sitemap);
const long = pages.filter((p) => p.title.length > 60 || p.desc.length > 155).map((p) => p.slug);
console.log(`${pages.length} pages and sitemap.xml written (titles ${pages.map((p) => p.title.length).join(' ')}; descriptions ${pages.map((p) => p.desc.length).join(' ')})${long.length ? `; too long: ${long.join(', ')}` : ''}`);
