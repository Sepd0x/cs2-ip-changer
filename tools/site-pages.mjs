// Builds the guide pages of the site (site/<slug>/index.html) and site/sitemap.xml from the pages below.
// The look comes from site/index.html (its <style>), so the pages always match the home page. Run: node tools/site-pages.mjs
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const site = join(root, 'site');
const BASE = 'https://sepd0x.github.io/cs2-ip-changer/';
const REPO = 'https://github.com/Sepd0x/cs2-ip-changer';
const LATEST = `${REPO}/releases/latest`;
const today = new Date().toISOString().slice(0, 10);

// Guide layout: one article in a text column, with a sticky rail (download, contents, a crop of the app) on the right.
// The fonts are the site's own (site/fonts), so the copied style points one folder up.
const home = readFileSync(join(site, 'index.html'), 'utf8');
// The home page's own blocks (hero map, viewer, sections) are left out; its media queries stay, they hold the table rules too.
const style = home.match(/<style>[\s\S]*?<\/style>/)[0].replace(/\/\* home: [\s\S]*?(?=@media \(max-width:1000px\))/, '').replace(/url\((fonts|assets)\//g, 'url(../$1/').replace('</style>', `/* guides */
.gl{display:grid;grid-template-columns:minmax(0,1fr) 19rem;column-gap:3.5rem;padding-top:2.75rem;padding-bottom:4.5rem}
.gl>*{grid-column:1}
.ghead{padding-bottom:2.5rem}
.crumbs{font:400 .75rem/1.4 var(--mono);letter-spacing:.06em;color:var(--ink3);margin:0 0 1.4rem}.crumbs a{color:var(--ink2);text-decoration:none}.crumbs a:hover{color:var(--ink)}
.ghead h1{font:500 clamp(2rem,3.7vw,2.75rem)/1.06 var(--disp);letter-spacing:-.03em;margin:0 0 1.1rem;max-width:24ch}
.ghead .lead{margin:0}
.rail{grid-column:2;grid-row:1/span 4}
.rail-in{position:sticky;top:1.5rem;display:flex;flex-direction:column;gap:1.9rem}
.rail .btn{width:100%}
.rail .checks{margin-top:1.1rem;font-size:.86rem;gap:.35rem 1rem}
.toc h2{font:400 .72rem/1.4 var(--mono);letter-spacing:.12em;text-transform:uppercase;color:var(--ink3);margin:0 0 .6rem}
.toc ul{list-style:none;margin:0;padding:0 0 0 .9rem;border-left:1px solid var(--line)}
.toc li{margin:.35rem 0;font-size:.92rem;line-height:1.4}
.toc a{color:var(--ink2);text-decoration:none}.toc a:hover{color:var(--ink)}
.crop{display:block;overflow:hidden;border-radius:8px;background:var(--bg2);box-shadow:0 0 0 1px var(--line2),0 24px 50px -24px var(--shadow)}
.crop img{max-width:none}
.cropcap{display:block;color:var(--ink3);font-size:.85rem;margin-top:.6rem}
.gtext{color:var(--ink2)}
.gtext h2{color:var(--ink);font-size:clamp(1.4rem,2.3vw,1.7rem);margin:2.75rem 0 .8rem}
.gtext h2:first-child{margin-top:0}
.gtext p,.gtext ul,.gtext ol{max-width:68ch;margin:0 0 .9rem}
.gtext ul,.gtext ol{padding-left:1.3em}
.gtext li{margin:0 0 .5rem}
.gtext li::marker{color:var(--ink3)}
.gtext ol li::marker{font-family:var(--mono);font-size:.85em}
.gtext b,.gtext strong{color:var(--ink);font-weight:500}
.gtext .tbl{margin:1.2rem 0 1rem}
@media (min-width:761px){.gtext td.us{padding-left:0;background:none;font-weight:500}}
.gfaq,.gmore{border-top:1px solid var(--line);margin-top:3rem;padding:2.5rem 0 0}
.gfaq h2,.gmore h2{font-size:clamp(1.4rem,2.3vw,1.7rem);margin-bottom:1.1rem}
.gfaq{max-width:calc(68ch + 2.5rem)}
.gmore ul{list-style:none;margin:0;padding:0;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1rem 2rem}
.gmore li{border-top:1px solid var(--line);padding-top:.8rem}
.gmore a{font:500 1.04rem/1.35 var(--disp);text-decoration:none}
.gmore a:hover{color:var(--sand-hi)}
.gmore span{display:block;color:var(--ink3);font-size:.9rem;line-height:1.45;margin-top:.2rem}
.gmore .end{display:none}
.gmore .end .btn{width:100%}
@media (max-width:900px){
  .gl{grid-template-columns:minmax(0,1fr);padding-top:1.75rem;padding-bottom:3rem}
  .rail{grid-column:1;grid-row:auto;order:1}
  .ghead{order:0;padding-bottom:1.5rem}.gtext{order:2}.gfaq{order:3}.gmore{order:4}
  .rail-in{position:static;gap:1.4rem;margin-bottom:2.5rem}
  .toc{display:none}
  .gfaq,.gmore{margin-top:2.25rem;padding:2rem 0 0}
  .gmore .end{display:block;margin-top:2rem}
}
@media (max-width:760px){
  .gmore ul{grid-template-columns:1fr;gap:.8rem}
  .gtext h2{margin-top:2.1rem}
}
</style>`);

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const text = (html) => html.replace(/<[^>]+>/g, '').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&').replace(/\s+/g, ' ').trim();
const slug = (s) => s.toLowerCase().replace(/'/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const icon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11"/><path d="m7 10 5 5 5-5"/><path d="M5 20h14"/></svg>';
const download = `<a class="btn" href="${LATEST}">${icon}Download for Windows</a>`;
const req = 'Windows 10/11 · Android phone + USB cable or hotspot · uses mobile data · admin';
const VT = 'https://www.virustotal.com/gui/file/b5de48fe17e48e8c38fb30e10aca2c193b0423ca43b4a765018cc178ab884490/detection';
// The trust line, the same as under the home page's buttons. Facts as in the README (the license isn't open source, so "Public source").
const checks = `<ul class="checks" aria-label="At a glance"><li>Free</li><li>Public source</li><li>No account</li><li>Doesn't touch cs2.exe</li><li>Signed updates</li><li><a href="${VT}">VirusTotal report</a></li></ul>`;
const safe = (here) => `Not code-signed, so SmartScreen warns the first time: ${here ? '<a href="#check-your-download">check your download</a>' : '<a href="../is-it-safe/">how to check it</a>'}`;

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
const crop = ([name, x, y, w, h, alt]) => `<a class="crop" href="../img/${name}.png" style="aspect-ratio:${w}/${h}" aria-label="${esc(alt)} (full screenshot)"><picture><source type="image/webp" srcset="../img/${name}.webp"><img src="../img/${name}.png" alt="${esc(alt)}" width="1240" height="800" loading="lazy" style="width:${(1240 / w * 100).toFixed(2)}%;margin:${(-y / w * 100).toFixed(2)}% 0 0 ${(-x / w * 100).toFixed(2)}%"></picture></a>`;

// Each page answers one search and has one job. Facts come from the README and CHANGELOG; keep them in step when the app changes.
// A question lives on one page only; the home FAQ links here.
export const pages = [
  {
    slug: 'cs2-region-picker',
    title: 'CS2 region picker: choose your matchmaking region (free)',
    desc: 'Pick which regions CS2 matchmaking can put you in, with the ping of each region. Free Windows app: one firewall rule for cs2.exe on Valve relays only.',
    nav: 'CS2 region picker',
    blurb: 'Choose where matchmaking puts you.',
    eyebrow: 'CS2 server picker',
    h1: 'CS2 region picker: choose where matchmaking puts you',
    lead: 'The Regions page of CS2 IP Changer lists Valve\'s matchmaking regions with the ping of each one, and lets you turn off the ones you don\'t want. Premier, Competitive, Casual, Deathmatch and Wingman then only find matches in the regions you left on.',
    crop: ['regions', 91, 44, 760, 466,'The Regions page: Valve\'s matchmaking regions by continent, each with its ping, and the ones turned off blocked for cs2.exe'],
    related: ['copy-cs2-config', 'steam-account-switcher', 'is-it-safe'],
    sections: [
      ['How CS2 picks a server', `<p>CS2 matchmaking doesn't connect you straight to a server. It measures your ping to Valve's relays (Steam Datagram Relay) in each region and queues you where the ping is good. If a region's relays can't be reached, CS2 treats that region as unavailable and looks elsewhere.</p><p>That's what a CS2 server picker uses: it makes the regions you don't want unreachable, so the game never picks them.</p>`],
      ['What the Regions page does', `<ul><li><b>The real list from Valve.</b> The regions come from Valve's own relay list (<code>GetSDRConfig</code>, app 730), grouped by city and continent. The last list is saved, so the page works offline.</li><li><b>Ping for each region</b>, measured the way the game will feel it: through the phone when you play on mobile data, through your home connection in No phone mode.</li><li><b>Shortcuts:</b> Only this region, Closest 3, your continent only (Europe only, North America only and so on), Ping under X ms, and Allow all (block off).</li><li><b>One firewall rule, for <code>cs2.exe</code> only.</b> Turned-off regions are blocked on Valve's relay IPs only. Steam, your friends list and community servers are not touched.</li><li><b>Kept up to date.</b> When Valve changes the relay list, the rule is rebuilt, so new relays in a region you turned off are blocked too.</li></ul>`],
      ['Things to know', `<ul><li>Changes apply at once, and CS2 measures the regions again on its next start. Don't change them during a match.</li><li>Blocking too much means long queues or no match at all, and Premier can take longer. Leave a few regions on.</li><li>It works with or without the phone. Regions don't depend on protection: they stay off until you click Allow all (block off).</li></ul>`],
    ],
    faq: [
      ['How do I force CS2 to only put me in EU servers?', 'Open the Regions page and click your continent\'s shortcut (Europe only in Europe), or Only this region on one region. Every other region is blocked for cs2.exe, so matchmaking can only find you a match where you left regions on. Max Acceptable Matchmaking Ping in CS2\'s settings only limits the ping; it doesn\'t pick a region.'],
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
      ['What the Configs page does', `<ul><li><b>Pick what to copy:</b> binds, crosshair, the other game settings (sensitivity, viewmodel), video, and HUD, radar and audio, each on its own. You can copy only the crosshair and keep each account's sensitivity.</li><li><b>Sync modes:</b> from a main account, from the account you used before, or latest change wins. It runs when a new account shows up, before the app logs in or opens CS2, or when you click Sync now.</li><li><b>Never with CS2 open.</b> CS2 saves its config when it closes and would undo the copy, so the copy waits until it closes.</li><li><b>Steam Cloud too.</b> The app writes the cloud copy the same way CS2 does, so Steam uploads the new one on the next launch.</li><li><b>Backups (undo).</b> Every change keeps a backup (the 20 newest per account), and Undo last sync puts it all back.</li><li><b>Snapshots and compare.</b> Save a config under a name, export it as a .zip for another PC, and compare two accounts setting by setting with a crosshair preview.</li><li><b>Video stays this PC's.</b> Each account keeps this PC's refresh rate and monitor, so a 144 Hz screen doesn't end up at 60 Hz.</li></ul>`],
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
      ['Made for community servers', `<p>A ban on a community server is usually on your Steam account, your IP, or both. The switcher pairs with the rest of the app: after a ban, a new IP from your phone's mobile data and the next account that server hasn't banned, in one step. <a href="../cs2-ip-ban/">How to tell which ban you have</a>.</p>`],
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
    title: "Use your phone's mobile data for CS2 only, not the whole PC",
    desc: "Set up your Android phone's mobile data for CS2 only: USB tethering, game-only routing, a kill switch and a new IP in one click over ADB. Free for Windows.",
    nav: 'Change IP with your phone',
    blurb: 'Setup, game-only routing and a new IP in one click.',
    eyebrow: 'Setup guide',
    h1: "Use your phone's mobile data for CS2 only, and change IP in one click",
    lead: 'The whole setup, start to finish: the phone shares its mobile data over USB, only CS2, Steam and your servers use it, and Change IP gets a new carrier IP from the PC.',
    crop: ['connection', 91, 44, 569, 428,'The Connection page: protection status, the IP servers see next to your home IP, and the phone ready to change IP'],
    related: ['cs2-ip-ban', 'hide-ip-cs2', 'is-it-safe'],
    sections: [
      ['What you need', `<ul><li>Windows 10 or 11 (64-bit) and admin rights: routes and firewall rules are system settings.</li><li>Windows Firewall turned on. Third-party firewalls that turn it off (some antivirus suites) leave the app without a kill switch, and it says so.</li><li>An Android phone with mobile data, and a USB cable or the phone's Wi-Fi hotspot.</li><li>USB debugging on the phone, only if you want to change IP from the PC.</li></ul>`],
      ['Set it up', `<ol><li><b>Download <code>CS2IPChanger.exe</code></b> from the <a href="${LATEST}">latest release</a> into a folder only admins can change, like <code>C:\\Program Files\\CS2 IP Changer</code>. It runs as admin, so it doesn't run from Downloads: started there, it offers to install itself in that folder in one click. The exe isn't code-signed, so SmartScreen warns the first time: More info &gt; Run anyway, or <a href="../is-it-safe/#check-your-download">check the download</a> first.</li><li><b>Plug the phone in by USB,</b> turn on USB tethering and turn off the phone's Wi-Fi, so it shares mobile data and not your home Wi-Fi. While the phone's Wi-Fi is on, the app keeps your servers and CS2 blocked.</li><li><b>Run the app.</b> Until a phone is set up it says Not protected yet and points you to Phone setup. Paste the servers you play on into Servers: <code>IP:port</code>, <code>connect IP:port</code> or <code>steam://connect</code> links (an IP without a port gets the Default port, 27015 unless you change it).</li><li><b>Something missing?</b> Phone setup under Phone checks the cable, the USB driver, USB debugging, tethering, the phone's Wi-Fi and where other apps go, does each step it can and tells you the one thing left.</li><li><b>Open Connection.</b> With CS2 and Steam running, every check should be green, including Phone IP is not your home IP. Run leak test to be sure.</li></ol><p>On the hotspot instead of a cable? Connect the PC to it and pick it under Connection &gt; How the PC connects to the phone.</p>`],
      ['Change IP from the PC', `<p>Turn on USB debugging on the phone and allow the PC when the phone asks. If ADB isn't installed, the app can download Google's platform-tools for you (about 7 MB).</p><p>Change IP then turns mobile data (or airplane mode) off and on until the carrier hands you a new IP, and keeps the old IPs in a list so you can see it changed. It also tells you if the new IP is in a new /16 range, because some admins ban whole ranges; Settings &gt; Keep changing until the IP is in a new range keeps going until it is. On the hotspot, Change IP works if the phone is also plugged in by USB with USB debugging on.</p>`],
      ['Game only, whole PC or no phone', `<ul><li><b>Game only:</b> CS2, Steam and your servers use the phone. The browser, Discord and downloads stay on your home connection and don't eat mobile data.</li><li><b>Whole PC:</b> everything goes through the phone, DNS included, and IPv6 is blocked so nothing leaves from home.</li><li><b>No phone:</b> play on your home connection. Nothing is blocked, servers see your home IP, and the app says so. Accounts, Configs, Profiles and Regions keep working.</li></ul><p>Once a phone has been connected, the kill switch (Block if the phone drops, in Checks) keeps your servers and CS2's game traffic on it: a pulled cable cuts CS2 off instead of falling back to your home IP. Before that, CS2 isn't locked, so trying the app without a phone never leaves CS2 unable to connect at home.</p>`],
    ],
    faq: [
      ['Can Windows send only CS2 through my phone\'s hotspot?', 'Not by itself: when you tether a phone, Windows has no setting to pick one app for it. CS2 IP Changer adds the routes for CS2, Steam and your servers, so only they use the phone and the rest of the PC stays on your home internet. You can still send the whole PC through the phone if you want.'],
      ['My carrier uses CGNAT. Is that a problem?', 'No. You share a public IP with other customers and servers see that shared one. Change IP still works, and when the carrier gives you the same IP back, the app keeps trying.'],
      ['Will my ping be worse?', 'Usually, yes. Mobile data adds latency and jitter, so expect a higher ping than at home. A good 4G or 5G signal helps a lot: put the phone near a window and keep it charging. The ping in Servers is measured through the phone, so you see it before you join, and Connection quality tells you when the connection itself is weak. While it is weak, Whole PC drops to Game only so CS2 doesn\'t share the phone with the rest of the PC; turn off Fall back to Game only on a weak connection in Settings > Protection to stop that.'],
      ['Does it work with a 4G or 5G USB dongle?', 'It works as the connection: if the app doesn\'t pick it on its own, choose it under Connection > How the PC connects to the phone. Change IP needs an Android phone with ADB, though.'],
    ],
  },
  {
    slug: 'cs2-ip-ban',
    title: 'CS2 community server IP ban: how to get back in',
    desc: 'IP banned on a CS2 community server? Tell an IP ban from a SteamID or range ban, see what a new IP fixes, and get back in with your phone\'s mobile data.',
    nav: 'CS2 IP ban',
    blurb: 'Which ban you have and how to get back in.',
    eyebrow: 'CS2 IP ban bypass',
    h1: 'CS2 community server IP ban: how to get back in',
    lead: 'Valve\'s bans are on your Steam account. Community servers run their own admins and plugins, and those can ban your account, your IP or a whole range. Find out which one you have first; when it\'s the IP, a new one gets you back in.',
    crop: ['accounts', 655, 146, 552, 192, 'The Accounts page: one account banned on one server and on cooldown, another with a VAC ban'],
    related: ['change-ip-with-phone', 'steam-account-switcher', 'hide-ip-cs2'],
    sections: [
      ['Which ban do you have?', `<div class="tbl fit"><table><thead><tr><th scope="col">What you see</th><th scope="col">The ban</th><th scope="col">What gets you back in</th></tr></thead><tbody>
<tr><td>Another Steam account on the same connection is refused too</td><td>IP ban</td><td class="us">A new IP</td></tr>
<tr><td>The new IP is refused too</td><td>Range ban, often a whole /16</td><td class="us">An IP in a new range</td></tr>
<tr><td>The same account is refused from another connection</td><td>Ban on that SteamID</td><td class="us">Another account; a new IP won't lift it</td></tr>
<tr><td>VAC or game ban on the profile, or a competitive cooldown</td><td>Valve ban on the account</td><td>Nothing on this page: a new IP doesn't change it</td></tr>
</tbody></table></div><p>If CS2's console shows <code>NETWORK_DISCONNECT_STEAM_BANNED</code>, that's the server's own ban list refusing your SteamID, not Steam or VAC. Many servers ban the IP and the account together.</p>`],
      ['Get back in, step by step', `<ol><li><b>Without any app:</b> restarting the router sometimes gives you a new IP, if your provider hands out dynamic IPs, but it's still your home line. Playing on your phone's hotspot works too, since the server then sees the carrier's IP.</li><li><b>With CS2 IP Changer:</b> plug in the phone once (<a href="../change-ip-with-phone/">setup</a>), and from then on Change IP and rejoin does it in one click: mobile data off and on over ADB, a new carrier IP, and back on the server. <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F10</kbd> does it from inside the game.</li><li><b>The account too:</b> after a ban the app logs into the next account that server hasn't banned, because the ban is often on the IP and the account.</li><li><b>Range ban:</b> the app tells you whether the new IP is in a new /16 and can keep trying until it is.</li><li><b>Valve ban:</b> when Steam or VAC refuses the account instead, the app says so and doesn't offer a new IP, because it wouldn't help.</li></ol>`],
      ['What the app remembers', `<ul><li>The account in use is marked as banned on that server and on the rest of the same community (the same tag in the server name, or a community name you set). New IP + next account skips it there, and joining with it asks first.</li><li>Your home IP never reaches the server, so it never lands on a ban list. No app can stop an admin from banning you; this keeps the ban off your house.</li></ul>`],
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
      ['Why mobile data', `<p>The server sees an ordinary mobile connection from your carrier, not a datacenter, so VPN blocks don't apply. Playing on the phone's hotspot by hand gets you that much. The app adds what is hard by hand: only the game uses the phone, a kill switch keeps it there if the cable comes out, live checks and a leak test show the IP servers see, and Change IP gets a new one. <a href="../change-ip-with-phone/">Set it up</a>.</p>`],
    ],
    faq: [
      ['Does official matchmaking show my IP?', 'Valve\'s matchmaking goes through Steam Datagram Relay, so other players don\'t get your IP there. Community servers you join directly are the ones that see it.'],
      ['Can a community server admin see my IP?', 'Yes. The server needs your IP to send you game packets, so the admins and their plugins can see it, log it and ban it. With your phone\'s mobile data, what they get is your carrier\'s IP.'],
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
      ['Check your download', `<ul><li>Every release is built by GitHub Actions from the public source and comes with a <b>build attestation</b>. With the GitHub CLI: <code>gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer</code>.</li><li>Each exe has a <b>SHA-256</b> file next to it in the release. In PowerShell: <code>Get-FileHash .\\CS2IPChanger.exe</code>, and compare.</li><li><b>Signed updates.</b> The app only installs an update whose SHA-256 matches the release and is signed with the project's offline key, with the version inside so an old release can't come back under a new tag. The key isn't on GitHub, so access to the repo alone can't push an exe to installed copies.</li><li><b>VirusTotal</b> for each release: <a href="https://www.virustotal.com/gui/file/b5de48fe17e48e8c38fb30e10aca2c193b0423ca43b4a765018cc178ab884490/detection">0 of 69 engines</a> flag beta.8.</li><li>The exe isn't code-signed, so Windows SmartScreen may warn the first time. More info &gt; Run anyway.</li></ul>`],
      ['Your data', `<ul><li>No account, and no passwords kept. The account switcher uses the logins Steam already saved.</li><li>Usage stats, required to use the app in the beta: the first start shows what is sent, with Accept and Quit (Quit closes the app without changing anything). Then once when it has been open for 20 seconds, when it closes and once a day: a random ID made for this install, with no name (Settings > Delete my stats deletes everything kept under it), versions, your country, the mode, how the phone is connected, minutes open, button counts, where on each screen people click (a coarse grid, added up with everyone, with no ID), a few counters, list sizes and errors, and in rough bands how long ago the app was set up, each CS2 match's ping, jitter and lag (never the server), session length and crashes. Once a day a separate report with no ID and no country says the Windows language, window and screen scale, the Windows build, the phone's brand (never the model), Android version and 4G or 5G, and is only added into counts. Never IPs, servers, accounts or names. The server code is public (<a href="${REPO}/tree/main/worker/stats">worker/stats</a>).</li><li>To show the carrier and city, the app asks ipwho.is about your home IP and the phone's IP, so ipwho.is receives both, each one only through its own connection. Your public IP is measured with ipify, icanhazip, Amazon and Cloudflare. The full list, and what the app does with admin rights: <a href="${REPO}/blob/main/docs/PRIVACY-AND-SECURITY.md">Privacy and security</a>.</li><li>Save bug report saves a .zip on your Desktop and sends nothing. It leaves out your current and last home IP, API key, logins, names, SteamIDs, e-mails, Wi-Fi names and your Windows user; look through it before you post it.</li></ul>`],
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

function render(p) {
  const url = `${BASE}${p.slug}/`;
  const ld = {
    '@context': 'https://schema.org',
    '@graph': [
      { '@type': 'WebPage', '@id': `${url}#page`, url, name: p.title, description: p.desc, inLanguage: 'en',
        isPartOf: { '@id': `${BASE}#website` }, about: { '@id': `${BASE}#app` }, dateModified: today,
        primaryImageOfPage: `${BASE}img/${p.crop[0]}.png` },
      { '@type': 'BreadcrumbList', itemListElement: [
        { '@type': 'ListItem', position: 1, name: 'CS2 IP Changer', item: BASE },
        { '@type': 'ListItem', position: 2, name: p.nav, item: url }] },
      ...(p.faq?.length ? [{ '@type': 'FAQPage', mainEntity: p.faq.map(([q, a]) => ({ '@type': 'Question', name: q, acceptedAnswer: { '@type': 'Answer', text: a } })) }] : []),
    ],
  };
  const image = `${BASE}img/${p.crop[0]}.png`;
  const toc = [...p.sections.map(([h]) => [slug(h), h]), ...(p.faq?.length ? [['faq', 'FAQ']] : [])];
  const related = p.related.map((s) => bySlug[s]);
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(p.title)}</title>
<meta name="description" content="${esc(p.desc)}">
<meta name="robots" content="index, follow, max-image-preview:large">
<link rel="canonical" href="${url}">
<link rel="icon" href="../img/icon.png">
<meta name="theme-color" content="#0e0e0e">
<meta property="og:type" content="article">
<meta property="og:site_name" content="CS2 IP Changer">
<meta property="og:title" content="${esc(p.title)}">
<meta property="og:description" content="${esc(p.desc)}">
<meta property="og:url" content="${url}">
<meta property="og:image" content="${image}">
<meta name="twitter:card" content="summary_large_image">
<link rel="preload" href="../fonts/funnel-sans.woff2" as="font" type="font/woff2" crossorigin>
<link rel="preload" href="../fonts/funnel-display.woff2" as="font" type="font/woff2" crossorigin>
<script type="application/ld+json">
${JSON.stringify(ld, null, 1)}
</script>
${style}
</head>
<body>
<a class="skip" href="#main">Skip to content</a>
<header class="top">
  <div class="wrap">
    <a class="brand" href="../"><img src="../img/icon.png" alt="" width="28" height="28">CS2 IP Changer</a>
    <nav aria-label="Main">
      <a class="hide-s" href="../#features">Features</a>
      <a class="hide-s" href="../#guides">Guides</a>
      <a class="hide-s" href="../is-it-safe/">Is it safe?</a>
      <a href="${REPO}">GitHub</a>
      <a class="btn btn-s" href="${LATEST}">Download</a>
    </nav>
  </div>
</header>
<main id="main">
  <div class="wrap gl">
    <div class="ghead">
      <p class="crumbs"><a href="../">CS2 IP Changer</a> / ${esc(p.nav)}</p>
      <p class="eyebrow">${esc(p.eyebrow)}</p>
      <h1>${esc(p.h1)}</h1>
      <p class="lead">${esc(p.lead)}</p>
    </div>
    <aside class="rail" aria-label="Download and contents">
      <div class="rail-in">
        <div>
          ${download}
          ${checks}
          <p class="safe">${safe(p.slug === 'is-it-safe')}</p>
          <p class="req">Beta · ${req}</p>
        </div>
        <nav class="toc" aria-labelledby="toc-h">
          <h2 id="toc-h">On this page</h2>
          <ul>
            ${toc.map(([id, h]) => `<li><a href="#${id}">${esc(h)}</a></li>`).join('\n            ')}
          </ul>
        </nav>
        <figure style="margin:0">${crop(p.crop)}<figcaption class="cropcap">The ${esc(p.crop[0][0].toUpperCase() + p.crop[0].slice(1))} page of the app</figcaption></figure>
      </div>
    </aside>
    <article class="gtext">
${p.sections.map(([h, html]) => `      <h2 id="${slug(h)}">${esc(h)}</h2>
      ${labelTables(html)}`).join('\n')}
    </article>
${p.faq?.length ? `    <section class="gfaq" aria-labelledby="faq">
      <h2 id="faq">FAQ</h2>
      ${p.faq.map(([q, a]) => `<details><summary>${esc(q)}</summary><p>${esc(a)}</p></details>`).join('\n      ')}
    </section>` : ''}
    <nav class="gmore" aria-labelledby="more-h">
      <h2 id="more-h">Related guides</h2>
      <ul>
        ${related.map((r) => `<li><a href="../${r.slug}/">${esc(r.nav)}</a><span>${esc(r.blurb)}</span></li>`).join('\n        ')}
      </ul>
      <div class="end">${download}<p class="req">Free beta · ${req}</p></div>
    </nav>
  </div>
</main>
<footer>
  <div class="wrap">
    <span>CS2 IP Changer by Sepd0x. Not affiliated with Valve. Counter-Strike and Steam are trademarks of Valve Corporation.</span>
    <span><a href="${REPO}">Source</a> · <a href="${REPO}/blob/main/CHANGELOG.md">Changelog</a> · <a href="${REPO}#privacy">Privacy</a> · <a href="${REPO}/issues/new/choose">Report a bug</a></span>
  </div>
</footer>
</body>
</html>
`;
}

for (const p of pages) {
  const file = join(site, p.slug, 'index.html');
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, render(p));
}

// Sitemap: the home page with its screenshots, then every guide page.
const shots = ['connection', 'servers', 'accounts', 'regions', 'configs', 'profiles'];
const sitemap = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">
  <url>
    <loc>${BASE}</loc>
    <lastmod>${today}</lastmod>
${shots.map((s) => `    <image:image><image:loc>${BASE}img/${s}.png</image:loc></image:image>`).join('\n')}
  </url>
${pages.map((p) => `  <url>
    <loc>${BASE}${p.slug}/</loc>
    <lastmod>${today}</lastmod>
  </url>`).join('\n')}
</urlset>
`;
writeFileSync(join(site, 'sitemap.xml'), sitemap);
const long = pages.filter((p) => p.title.length > 60 || p.desc.length > 155).map((p) => p.slug);
console.log(`${pages.length} pages and sitemap.xml written (titles ${pages.map((p) => p.title.length).join(' ')}; descriptions ${pages.map((p) => p.desc.length).join(' ')})${long.length ? `; too long: ${long.join(', ')}` : ''}`);
