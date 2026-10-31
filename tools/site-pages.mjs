// Builds the guide pages of the site (site/<slug>/index.html) and site/sitemap.xml from the pages below.
// The look comes from site/index.html (its <style>), so the pages always match the home page. Run: node tools/site-pages.mjs
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const site = join(root, 'site');
const BASE = 'https://sepd0x.github.io/cs2-ip-changer/';
const REPO = 'https://github.com/Sepd0x/cs2-ip-changer';
const today = new Date().toISOString().slice(0, 10);

const home = readFileSync(join(site, 'index.html'), 'utf8');
const style = home.match(/<style>[\s\S]*?<\/style>/)[0].replace('</style>',
  '.page{padding-top:56px;padding-bottom:8px}.page h1{font-size:clamp(1.9rem,4.4vw,2.8rem);line-height:1.15;margin:0 0 16px;letter-spacing:-.02em;max-width:26ch;text-wrap:balance}' +
  '.page .lead{color:var(--mu);font-size:1.12rem;max-width:64ch;margin:0 0 26px}.page .actions{justify-content:flex-start}' +
  '.crumbs{color:var(--fa);font-size:.9rem;margin:0 0 18px}.crumbs a{color:var(--mu)}' +
  '.prose{max-width:72ch;color:var(--mu)}.prose h2{color:var(--ink)}.prose p,.prose li{margin:0 0 12px}.prose b,.prose strong{color:var(--ink)}.prose ul,.prose ol{padding-left:1.3em;margin:0 0 12px}' +
  '.prose h3{color:var(--ink);font-size:1.08rem;margin:26px 0 8px}.page .shot{margin:36px 0 0;max-width:980px}' +
  'ul.links{list-style:none;padding:0;margin:0;display:grid;grid-template-columns:repeat(2,1fr);gap:12px}' +
  'ul.links a{display:block;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:16px 18px;color:var(--ink);font-weight:600}' +
  'ul.links a span{display:block;color:var(--mu);font-weight:400;font-size:.94rem;margin-top:4px}ul.links a:hover{border-color:#3a3a40;text-decoration:none}' +
  '@media (max-width:760px){ul.links{grid-template-columns:1fr}}\n</style>');

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const text = (html) => html.replace(/<[^>]+>/g, '').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&').replace(/\s+/g, ' ').trim();
const shot = (name, alt) => `<div class="shot"><picture><source type="image/webp" srcset="../img/${name}.webp"><img src="../img/${name}.png" alt="${esc(alt)}" width="1240" height="800" loading="lazy"></picture></div>`;
const download = `<a class="btn" href="${REPO}/releases"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11"/><path d="m7 10 5 5 5-5"/><path d="M5 20h14"/></svg>Download for Windows</a>`;

// Each page answers one search. Facts come from the README; keep them in step when the app changes.
export const pages = [
  {
    slug: 'cs2-region-picker',
    title: 'CS2 region picker: choose your matchmaking region (free)',
    desc: 'Pick which regions CS2 matchmaking can put you in, with the ping of each region. Free Windows app: one firewall rule for cs2.exe on Valve relays only, removed with one click.',
    nav: 'CS2 region picker',
    blurb: 'Choose where Premier, Competitive and Casual put you, with the ping of each region.',
    eyebrow: 'CS2 server picker',
    h1: 'CS2 region picker: choose where matchmaking puts you',
    lead: 'The Regions page of CS2 IP Changer lists Valve\'s matchmaking regions with the ping of each one, and lets you turn off the ones you don\'t want. Premier, Competitive, Casual, Deathmatch and Wingman then only find matches in the regions you left on.',
    img: ['regions', 'The Regions page: Valve\'s matchmaking regions by continent, each with its ping, and the ones turned off blocked for cs2.exe'],
    sections: [
      ['How CS2 picks a server', `<p>CS2 matchmaking doesn't connect you straight to a server. It measures your ping to Valve's relays (Steam Datagram Relay) in each region and queues you where the ping is good. If a region's relays can't be reached, CS2 treats that region as unavailable and looks elsewhere.</p><p>That's what a CS2 server picker uses: it makes the regions you don't want unreachable, so the game never picks them.</p>`],
      ['What the Regions page does', `<ul><li><b>The real list from Valve.</b> The regions come from Valve's own relay list (<code>GetSDRConfig</code>, app 730), grouped by city and continent. The last list is saved, so the page works offline.</li><li><b>Ping for each region</b>, measured the way the game will feel it: through the phone when you play on mobile data, through your home connection in No phone mode.</li><li><b>Shortcuts:</b> Only this region, Closest 3, your continent only (Europe only, North America only and so on), Ping under X ms, and Allow all.</li><li><b>One firewall rule, for <code>cs2.exe</code> only.</b> Turned-off regions are blocked on Valve's relay IPs only. Steam, your friends list and community servers are not touched.</li><li><b>Kept up to date.</b> When Valve changes the relay list, the rule is rebuilt, so new relays in a region you turned off are blocked too.</li><li><b>One click back.</b> Allow all removes the rule. So do <code>--restore</code> and <code>tools/restore.cmd</code>.</li></ul>`],
      ['Things to know', `<ul><li>Changes apply at once, and CS2 measures the regions again on its next start.</li><li>Blocking too much means long queues or no match at all, and Premier can take longer. Leave a few regions on.</li><li>It works with or without the phone. Regions don't depend on protection: they stay off until you click Allow all.</li></ul>`],
    ],
    faq: [
      ['Does forcing a region get me banned?', 'The Regions page doesn\'t touch the game, its files or its memory. It adds one outbound Windows Firewall rule that stops cs2.exe from reaching Valve\'s relays in the regions you turned off, so to the game it looks like a bad connection to those relays. We can\'t speak for Valve and can\'t promise anything about how they treat it.'],
      ['Does it change community servers or my friends list?', 'No. The rule only covers cs2.exe and only Valve\'s relay IPs. Community servers, Steam and friends work as before.'],
      ['How do I undo it?', 'Click Allow all on the Regions page. Running the app with --restore, or tools/restore.cmd, also removes every rule the app made.'],
    ],
  },
  {
    slug: 'copy-cs2-config',
    title: 'Copy your CS2 config to another account (binds, crosshair, video)',
    desc: 'Copy CS2 binds, crosshair, sensitivity and video settings between your Steam accounts in one click, with backups and undo. Free Windows app, no console commands.',
    nav: 'Copy CS2 config',
    blurb: 'Binds, crosshair, sensitivity and video on every account, with backups and undo.',
    eyebrow: 'CS2 config manager',
    h1: 'Copy your CS2 config to another account',
    lead: 'Got a new account and want your binds, crosshair and video settings on it? The Configs page of CS2 IP Changer copies your CS2 config between the accounts on your PC, one part at a time or all of it, and keeps a backup of everything it changes.',
    img: ['configs', 'The Configs page: sync mode, the accounts with their crosshair, copy, compare, snapshots and backups'],
    sections: [
      ['Where CS2 keeps each account\'s config', `<p>CS2 saves settings per Steam account in <code>Steam\\userdata\\&lt;id&gt;\\730\\local\\cfg</code>:</p><ul><li>binds in <code>cs2_user_keys_0_slot0.vcfg</code></li><li>crosshair and game settings in <code>cs2_user_convars_0_slot0.vcfg</code></li><li>video in <code>cs2_video.txt</code></li><li>HUD, radar and audio in <code>cs2_machine_convars.vcfg</code></li></ul><p>Copying these by hand works, but it's easy to copy the wrong folder, and Steam Cloud can bring the old files back. The app does it for you.</p>`],
      ['What the Configs page does', `<ul><li><b>Pick what to copy:</b> binds, crosshair, the other game settings (sensitivity, viewmodel), video, and HUD, radar and audio, each on its own. You can copy only the crosshair and keep each account's sensitivity.</li><li><b>Sync modes:</b> from a main account, from the account you used before, or latest change wins. It runs when a new account shows up, before the app logs in or opens CS2, or when you click Sync now.</li><li><b>Never with CS2 open.</b> CS2 saves its config when it closes and would undo the copy, so the copy waits until it closes.</li><li><b>Steam Cloud too.</b> The app writes the cloud copy the same way CS2 does, so Steam uploads the new one on the next launch.</li><li><b>Backups and undo.</b> Every change keeps a backup (the 20 newest per account), and Undo last sync puts it all back.</li><li><b>Snapshots and compare.</b> Save a config under a name, export it as a .zip for another PC, and compare two accounts setting by setting with a crosshair preview.</li><li><b>Video stays this PC's.</b> Each account keeps this PC's refresh rate and monitor, so a 144 Hz screen doesn't end up at 60 Hz.</li></ul>`],
    ],
    faq: [
      ['Is copying config files safe for VAC?', 'The app copies the same files CS2 writes when you change a setting. It never touches the game process, its memory or its program files.'],
      ['My settings keep going back. Why?', 'Steam Cloud. If an account was played on another PC since, Steam may bring that version back or ask which one to keep. If it keeps happening, turn off Steam Cloud for CS2 (CS2 > Properties > General).'],
      ['Can I move my config to another PC?', 'Yes. Save a snapshot and export it as a .zip. The .zip only has the config files and the name, never the account.'],
    ],
  },
  {
    slug: 'steam-account-switcher',
    title: 'Steam account switcher for CS2, with ban marks per server',
    desc: 'Switch Steam accounts for CS2 without typing passwords. Shows VAC, game and trade bans and cooldowns, marks server bans per account, and skips banned accounts. Free for Windows.',
    nav: 'Steam account switcher',
    blurb: 'Switch accounts without passwords; bans, cooldowns and server ban marks at a glance.',
    eyebrow: 'Alt accounts for CS2',
    h1: 'Steam account switcher for CS2, with ban marks per server',
    lead: 'CS2 IP Changer switches between the Steam accounts saved on your PC without asking for passwords, and knows which account is banned where. One hotkey gets a new IP, logs into the next clean account and joins the server again.',
    img: ['accounts', 'The Accounts page: saved Steam accounts with ban status and cooldowns'],
    sections: [
      ['What it does', `<ul><li><b>No passwords.</b> It uses the logins Steam already saved (the accounts where you ticked Remember me). Switching never asks for or stores a password.</li><li><b>Bans and cooldowns at a glance:</b> VAC, game and trade bans and the competitive cooldown of each account.</li><li><b>Server ban marks.</b> When CS2's console shows a ban, the account in use is marked for that server and for the rest of the same community. Switch skips accounts on cooldown or marked on that server.</li><li><b>Switch from inside the game.</b> <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F9</kbd> (you can change it) gets a new IP, logs into the next account and joins the last server again. In Settings you pick which of those steps it does.</li><li><b>Checks the result.</b> CS2 only opens once Steam is really on the new account; if Steam logs in to another one, the app tells you.</li><li><b>Backups of Steam's account list.</b> Before each switch the app keeps a copy of <code>loginusers.vdf</code>, and a failed switch puts Steam's previous account back.</li></ul>`],
      ['Made for community servers', `<p>A ban on a community server is usually on your Steam account, your IP, or both. The switcher pairs with the rest of the app: a new IP from your phone's mobile data and the next account that server hasn't banned, in one step.</p>`],
    ],
    faq: [
      ['Does it store my Steam password?', 'No. It uses the logins Steam already saved on your PC. Only the Profiles page asks you to sign in, and then only Steam\'s sign-in token is kept, encrypted with Windows.'],
      ['Will switching get my account banned?', 'Switching only changes which saved account Steam logs in to, the same as picking it in Steam yourself. It never touches the game.'],
      ['Can it skip accounts that are banned on a server?', 'Yes. Accounts marked as banned on a server or its community, and accounts on cooldown, are skipped there. Joining with a marked account asks first.'],
    ],
  },
  {
    slug: 'steam-profile-editor',
    title: 'Change several Steam profiles at once: name, picture, bio, privacy',
    desc: 'Bulk edit Steam profiles: name, avatar, real name, bio, country, privacy and custom URL on many accounts from one template, with a preview and restore. Free Windows app.',
    nav: 'Steam profile editor',
    blurb: 'Name, picture, bio and privacy on many accounts from one template, with undo.',
    eyebrow: 'Steam profiles in bulk',
    h1: 'Change several Steam profiles at once',
    lead: 'The Profiles page of CS2 IP Changer changes the name, picture, real name, bio, country, privacy and custom URL of the Steam accounts you pick, from templates you save. You see each account now and after before anything is sent.',
    img: ['profiles', 'The Profiles page: a template on the left, and each account now and after on the right, with its sign-in'],
    sections: [
      ['What you can do', `<ul><li><b>Templates.</b> Save a look (name, picture, real name, bio, country, privacy, custom URL) and apply it to any accounts.</li><li><b>Make them all look like one account</b> in one click, or copy the look of any public Steam profile from its link.</li><li><b>Numbers and names:</b> <code>nightfall {n}</code> gives nightfall 1, nightfall 2 and so on; <code>{name}</code> keeps each account's current name, so <code>{name} | NV</code> adds a tag.</li><li><b>Preview first.</b> Each account is shown now and after. Apply changes them one after another with a pause, and each result is shown in plain words.</li><li><b>Undo.</b> A copy of each profile, picture included, is kept before the first change. Restore original puts it back.</li><li><b>New accounts</b> can get a template on their own the first time they show up.</li></ul>`],
      ['Your login stays yours', `<p>Changing a profile needs a Steam web session, like your browser has. You sign in the way Steam's own login page works (password and Steam Guard, or a QR code from the Steam app). The password goes straight into Steam's encryption for that sign-in and is never saved. The app keeps only Steam's sign-in token, encrypted with Windows so only your Windows user can open it, and you can end it any time in Steam > Settings > Security > Authorized devices.</p><p>The app refuses a name, bio or URL that would show a login saved on your PC, and pictures are saved again as a new JPG, so photo data like GPS never reaches Steam.</p>`],
    ],
    faq: [
      ['Can it clear "previously known as"?', 'Yes, with Clear name history, which is off unless you tick it. That one is permanent: Steam has no way back.'],
      ['Where are my templates kept?', 'On your PC only, in SRT_Dados/profiles. Nothing leaves your PC except what you apply to Steam.'],
    ],
  },
  {
    slug: 'change-ip-with-phone',
    title: 'Change your IP with your phone\'s mobile data (USB tethering + ADB)',
    desc: 'Get a new public IP on your PC from your Android phone: USB tethering plus mobile data off and on over ADB, in one click. Game-only routing and a kill switch for CS2. Free.',
    nav: 'New IP from your phone',
    blurb: 'USB tethering plus mobile data off and on over ADB: a new public IP in one click.',
    eyebrow: 'Mobile data IP',
    h1: 'Change your IP with your phone\'s mobile data',
    lead: 'Turning mobile data off and on usually gets you a new IP from your carrier. CS2 IP Changer does that from the PC over ADB, while your phone shares its connection over USB, and checks that the IP really changed.',
    img: ['connection', 'The Connection page: protection status, the IP servers see next to your home IP, phone status and live checks'],
    sections: [
      ['How it works', `<ol><li><b>USB tethering</b> (or the phone's hotspot) shares the phone's mobile data with the PC.</li><li><b>Only the game moves.</b> CS2, Steam and your servers go through the phone; the browser, Discord and downloads stay on your home connection and don't eat mobile data. You can send the whole PC through the phone instead.</li><li><b>Change IP</b> turns mobile data (or airplane mode) off and on over ADB until the carrier hands you a new IP, and keeps a list of old IPs so you can see it changed.</li><li><b>New range check.</b> It tells you if the new IP is in a new /16 range, because some admins ban whole ranges, and can keep trying until it is.</li></ol>`],
      ['What you need', `<ul><li>Windows 10 or 11 (64-bit) and admin rights (routes and firewall rules are system settings).</li><li>An Android phone with mobile data and a USB cable. USB debugging is needed only to change IP from the PC.</li><li>Phone setup checks the cable, the USB driver, USB debugging, tethering and the phone's Wi-Fi, and does each step it can.</li></ul>`],
      ['Not a VPN', `<p>Servers see an ordinary mobile connection from your carrier, like any player on a phone hotspot, not a datacenter IP. That's why it also works on servers that block VPNs. A Windows Firewall kill switch keeps CS2 off your home connection if the cable comes out.</p>`],
    ],
    faq: [
      ['My carrier uses CGNAT. Is that a problem?', 'No. You share a public IP with other customers and servers see that shared one. Change IP still works.'],
      ['Will my ping be worse?', 'Usually, yes. Mobile data adds latency and jitter. The app measures ping, jitter and loss every 30 seconds and tells you when the connection is weak.'],
      ['Does it work with a 4G or 5G USB dongle?', 'It works as the connection: if the app doesn\'t pick it on its own, choose it under Connection > How the PC connects to the phone. Change IP needs an Android phone with ADB, though.'],
    ],
  },
  {
    slug: 'cs2-ip-ban',
    title: 'Does CS2 IP ban? Valve bans vs community server IP bans',
    desc: 'VAC and game bans are on your Steam account; community servers can also ban your IP. What each ban means, what a new IP can and can\'t fix, and how to keep your home IP out of ban lists.',
    nav: 'Does CS2 IP ban?',
    blurb: 'Account bans, IP bans and range bans: what a new IP can and can\'t fix.',
    eyebrow: 'CS2 bans explained',
    h1: 'Does CS2 IP ban? Valve bans and community server bans',
    lead: 'There are two kinds of bans in CS2, and they work differently. Valve\'s bans are on your Steam account. Community servers run their own admins and plugins, and they can ban your account, your IP, or both.',
    sections: [
      ['Valve\'s bans are on the account', `<p>VAC bans, game bans and competitive cooldowns from Valve are tied to your Steam account. A new IP doesn't change them, and nothing in CS2 IP Changer hides cheats from VAC or any other anti-cheat. Don't use it for that.</p>`],
      ['Community servers can ban your IP', `<p>Anyone running a community server sees the IP of every player who joins. Their admin tools can ban a SteamID, an IP address, or a whole IP range. An IP ban sticks to your house: every account you play from that connection is shut out.</p><ul><li><b>Account ban on a server:</b> follows that account. A new IP won't lift it, but your other accounts aren't affected.</li><li><b>IP ban:</b> follows your connection. A new IP gets you back in.</li><li><b>Range ban:</b> some admins ban a whole /16. You need an IP in a new range.</li></ul>`],
      ['How CS2 IP Changer handles it', `<ul><li>Your home IP never reaches the server: CS2 and Steam go through your phone's mobile data.</li><li>After a kick or ban the app offers a new IP and joins the server again, and after a ban it logs into the next account that server hasn't banned.</li><li>It tells you whether the new IP is in a new range, and can keep trying until it is.</li><li>It marks which account is banned on which server and community, so you don't walk back in with it.</li></ul><p>No app can stop an admin from banning you. What it does is keep your home IP out of every server's ban list.</p>`],
    ],
    faq: [
      ['Will a new IP lift a VAC ban?', 'No. VAC and game bans are on the Steam account, not the IP.'],
      ['I\'m banned on a community server. Will a new IP get me back in?', 'If it\'s an IP ban, yes: change IP and join again. If the ban is on your Steam account, it follows the account.'],
      ['Why keep my home IP hidden at all?', 'Server owners see every player\'s IP. With the phone\'s IP, nobody gets your home connection to DDoS it, and a ban never sticks to your house.'],
    ],
  },
  {
    slug: 'is-it-safe',
    title: 'Is CS2 IP Changer safe? What it changes and how to check it',
    desc: 'What CS2 IP Changer touches on your PC, why it needs admin, how to verify the download (SHA-256 and GitHub build attestation), signed updates, and how to undo everything.',
    nav: 'Is it safe?',
    blurb: 'What it changes, why it needs admin, how to check the download and undo everything.',
    eyebrow: 'Built to be checked',
    h1: 'Is CS2 IP Changer safe?',
    lead: 'A tool that asks for admin and changes your network should explain itself. Here is what CS2 IP Changer changes on your PC, what it never touches, and how you can check every release yourself.',
    sections: [
      ['What it changes, and what it never touches', `<ul><li><b>Changes:</b> Windows routes and Windows Firewall rules (all named with the app's prefix), which saved Steam account logs in, and, only if you use those pages, CS2's config files and your Steam profiles.</li><li><b>Never touches:</b> the game process, its memory or its program files. It hides nothing from VAC or any anti-cheat.</li><li><b>Why admin:</b> routes and firewall rules are system settings, and Windows only lets admins change them. Steam, CS2, your browser and the programs it opens for you still run as you, not as admin.</li></ul>`],
      ['Check your download', `<ul><li>Every release is built by GitHub Actions from the public source and comes with a <b>build attestation</b>. With the GitHub CLI: <code>gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer</code>.</li><li>Each exe has a <b>SHA-256</b> file next to it in the release. In PowerShell: <code>Get-FileHash .\\CS2IPChanger.exe</code>, and compare.</li><li><b>Signed updates.</b> The app only installs an update whose SHA-256 is signed with the project's key, which is not on GitHub.</li><li>The exe isn't code-signed yet, so Windows SmartScreen may warn the first time. More info &gt; Run anyway.</li></ul>`],
      ['Your data', `<ul><li>No account, and no passwords kept. The account switcher uses the logins Steam already saved.</li><li>While the app is in beta it sends anonymous usage stats once a day: a random install ID, versions, your country, the mode, minutes open and button counts. Never IPs, servers, accounts or names. The server code is public (<a href="${REPO}/tree/main/worker/stats">worker/stats</a>).</li><li>Save bug report strips your home IP, logins, names and SteamIDs before you send anything.</li></ul>`],
      ['Undo everything', `<p>Turn off protection (Settings &gt; Maintenance) removes the routes and the kill switch, and Allow all on the Regions page lifts the region block. To remove everything at once, run <code>CS2IPChanger.exe --restore</code> (or <code>tools/restore.cmd</code> from the repo): every route and firewall rule the app made is removed. The app is portable: to uninstall, run <code>--restore</code>, then delete its folder (with <code>SRT_Dados</code> next to the exe) and <code>C:\\ProgramData\\CS2IPChanger</code>.</p>`],
    ],
    faq: [
      ['Is it open source?', 'The source is public on GitHub so anyone can read and build it, but the license is All rights reserved during the beta: you can use the releases, not copy or redistribute the code.'],
      ['Can this get me VAC banned?', 'It never touches the game process, its memory or its program files. It changes Windows routing and firewall rules and which Steam account logs in.'],
      ['Why does my antivirus complain?', 'The exe is not code-signed and it changes firewall rules and runs ADB, which some antivirus engines flag on heuristics alone. Check the attestation and SHA-256 above; if a detection looks wrong, open an issue.'],
    ],
  },
];

const nav = (current) => pages.filter((p) => p.slug !== current)
  .map((p) => `<li><a href="../${p.slug}/">${esc(p.nav)}<span>${esc(p.blurb)}</span></a></li>`).join('\n          ');

function render(p) {
  const url = `${BASE}${p.slug}/`;
  const ld = {
    '@context': 'https://schema.org',
    '@graph': [
      { '@type': 'WebPage', '@id': `${url}#page`, url, name: p.title, description: p.desc, inLanguage: 'en',
        isPartOf: { '@id': `${BASE}#website` }, about: { '@id': `${BASE}#app` }, dateModified: today,
        ...(p.img ? { primaryImageOfPage: `${BASE}img/${p.img[0]}.png` } : {}) },
      { '@type': 'BreadcrumbList', itemListElement: [
        { '@type': 'ListItem', position: 1, name: 'CS2 IP Changer', item: BASE },
        { '@type': 'ListItem', position: 2, name: p.nav, item: url }] },
      ...(p.faq?.length ? [{ '@type': 'FAQPage', mainEntity: p.faq.map(([q, a]) => ({ '@type': 'Question', name: q, acceptedAnswer: { '@type': 'Answer', text: a } })) }] : []),
    ],
  };
  const image = p.img ? `${BASE}img/${p.img[0]}.png` : `${BASE}img/social-preview.png`;
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
<meta name="theme-color" content="#0e0e0f">
<meta property="og:type" content="article">
<meta property="og:site_name" content="CS2 IP Changer">
<meta property="og:title" content="${esc(p.title)}">
<meta property="og:description" content="${esc(p.desc)}">
<meta property="og:url" content="${url}">
<meta property="og:image" content="${image}">
<meta name="twitter:card" content="summary_large_image">
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
      <a class="hide-s" href="../is-it-safe/">Is it safe?</a>
      <a class="hide-s" href="../#faq">FAQ</a>
      <a href="${REPO}">GitHub</a>
    </nav>
  </div>
</header>
<main id="main">
  <div class="wrap page">
    <p class="crumbs"><a href="../">CS2 IP Changer</a> / ${esc(p.nav)}</p>
    <p class="eyebrow">${esc(p.eyebrow)}</p>
    <h1>${esc(p.h1)}</h1>
    <p class="lead">${esc(p.lead)}</p>
    <div class="actions">${download}<a class="btn2" href="${REPO}">Source on GitHub</a></div>
    <div class="meta">Free · Windows 10 and 11, 64-bit · Beta · No account</div>
    ${p.img ? shot(p.img[0], p.img[1]) : ''}
  </div>
${p.sections.map(([h, html], i) => `  <section aria-labelledby="s${i}">
    <div class="wrap prose">
      <h2 id="s${i}">${esc(h)}</h2>
      ${html}
    </div>
  </section>`).join('\n')}
${p.faq?.length ? `  <section aria-labelledby="faq-h">
    <div class="wrap">
      <h2 id="faq-h">FAQ</h2>
      ${p.faq.map(([q, a]) => `<details><summary>${esc(q)}</summary><p>${esc(a)}</p></details>`).join('\n      ')}
    </div>
  </section>` : ''}
  <section aria-labelledby="more-h">
    <div class="wrap">
      <h2 id="more-h">More from CS2 IP Changer</h2>
      <ul class="links">
          ${nav(p.slug)}
      </ul>
    </div>
  </section>
  <section class="cta" aria-labelledby="cta-h">
    <div class="wrap">
      <h2 id="cta-h">One free app for all of it</h2>
      <p>New IP from your phone, kill switch, account switcher, region picker, config sync and profiles. Every release is built by GitHub Actions from the public source, with signed updates.</p>
      ${download}
    </div>
  </section>
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
console.log(`${pages.length} pages and sitemap.xml written (${pages.map((p) => text(p.title).length + 'c').join(' ')})`);
