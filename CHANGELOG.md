# Changelog

Notable changes to this project are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- The app is organized the same way on every page, so things are where you expect them. Nothing was removed and every option keeps its value; some moved:
  - Settings now reads like the sidebar: on the left Protection, Mobile data, Change IP, New IP + next account and Hotkeys and sounds; on the right Open with CS2, Player flags, Steam, Privacy, App, Maintenance and, last, Remove the app.
  - Mobile data (new) has Save mobile data, Fall back to Game only on a weak connection (both were in Protection) and the monthly warning (was in Background and data). A grey line under What goes through the phone on Connection points to it.
  - Hotkeys and sounds (new) has both hotkeys and Sounds for commands, which were inside New IP + next account. That group now only has its steps.
  - Privacy (new) has Hide usernames (moved from the Accounts page), Hide usernames while OBS or Streamlabs is open (was in Steam), the usage stats with Delete my stats, and Delete log files.
  - App (new) has Start with Windows, Close to the notification area and Check for updates. Background and data and Updates are gone.
  - How it works moved from Settings to the Connection page, between Checks and Previous IPs.
  - Accounts has two boxes like Servers: Add and log in on top (On login, Add, New IP + next account), the list below with Refresh. Show hidden (was Settings > Maintenance > Show hidden accounts) is next to Refresh and only shows while some accounts are hidden.
  - Configs: Copy, Sync and Snapshots on the left; Account configs, Compare and Backups on the right. Both columns end on one line.
- The usage stats also count whether Turn on protection when the app starts and Use the app with protection off are changed from their default.

- README: a row of badges under the title (latest release, downloads, Windows 10 and 11, the VirusTotal result, build status, license), the "not for VAC or game bans" note as a highlighted box, and a Support section with where to report a bug, an idea or a security problem. The animated demo and the banner show the new logo.

### Fixed
- An iPhone used only through its hotspot over Wi-Fi, with no cable, got the Android steps (install the phone tool, USB debugging) and Change IP stayed off. The app now remembers the phone is an iPhone (seen on the cable once, or from a hotspot named like an iPhone's) and shows the iPhone's steps and its guided Change IP.
- If Windows refused to give the phone's adapter its default route back when the app closed, nothing said so and the next start forgot about it, so without the home network the PC had no internet through the phone until a restart. Now the Log says so, and the next start or `--restore` gives it back.
- On a hotspot or a USB network, a route of the app could stay behind after Turn off protection or No phone when the app had lost its list of routes, so servers kept going through the phone. Those routes are now found on the phone's connection too (only the app's own kind: your servers, Whole PC and Valve's blocks, never another program's).
- A route Windows refused to delete was forgotten, so the app never tried it again, and on a hotspot Turn off protection still said the network was restored. Now the Log says which route was left (once, not every few seconds), the app tries it again, and Turn off protection lists it.
- On the hotspot, while the app was still checking the phone's IP or with protection off, the Mobile data row of Checks could say the phone's Wi-Fi was on, or ask for the USB cable, although the hotspot was fine. It now says the connection goes through the hotspot, as it does once the phone is protected.
- When the app closed CS2 to restart Steam through the phone or to add the launch options, the Log said "CS2 closed by the account switch". It now says what closed it.

## [0.1.0-beta.11] - 2026-11-06

### Added
- A new logo: a disc crossed by one jump that lands on a new point, the carrier's IP, in the app's blues with a yellow point. It is the app's icon (taskbar, Alt+Tab, next to the clock), sits in the corner of the window above Connection, and is on the website, in the browser tab, in the README and on the link preview card.
- The website counts visits and clicks on Download, with no cookies and no ID, only as counts per day. With Do Not Track or Global Privacy Control on, it sends nothing. README > Telemetry says what it sends.
- A new website: the route from your PC through the phone to the server, told as you scroll, on the home page and in every guide, with a page for iPhone users and a Pause motion button. It works from 320 px phones to wide screens, and with reduced motion it shows still pictures. On a phone, a swipe rests on each step of the story and each page of the app window, one card at a time, and a reload or a turn of the phone keeps the page you were reading. On a PC, Page Down and Space go one step of the story and one page of the window at a time. The 3D scene is built in small steps, so a slow phone stays responsive while it loads.
- iPhone support in the app. Phone setup has the iPhone's own steps (Apple's driver, the cable, Trust This Computer, Personal Hotspot) instead of the Android ones, and the Phone box says "iPhone connected". Change IP on an iPhone is guided: the app tells you when to turn Airplane Mode on and off, waits for the iPhone to come back and shows the new IP.
- Kicks and bans are now seen however CS2 is opened. The app adds -condebug -conclearlog to CS2's launch options in Steam for every account on this PC, only while Steam is closed (Steam would overwrite them otherwise). What you already have there stays, and a backup of each file is kept. Settings > Steam > See kicks and bans when Steam opens CS2 turns it off, and its button adds them right away (Steam closes and opens again, it asks first).
- Log > What the app changed: the Firewall rules the app made and what each one blocks (your servers' IPs), the routes it added, the app's folder and the Start with Windows task, and a button that copies the command that undoes all of it.
- Joined a server in CS2 that isn't in your list? The app offers to add it, with an Add button, once per server. The same button comes with a kick or ban from a server that isn't in your list.
- A VPN app that carries your PC's traffic now shows up in Checks as "VPN app: on". Your servers and CS2 still go through the phone, but the app isn't tested with VPNs yet: if CS2 or your servers don't connect, turn the VPN off while you play. A VPN that is only installed, or Tailscale without an exit node, isn't flagged.
- Settings > Protection > Use the app with protection off. When it's on and protection is off, Servers, Join and Log in with CS2 work on your home connection and the app blocks nothing. Servers then see your home IP, so it asks you first. Off by default.
- After Install, the app offers to send the old SRT_Dados folder (with your saved Steam sign-ins) to the Recycle Bin. Only if every file was copied, and only if you say yes.

### Fixed
- Turn on protection with the phone plugged in but USB tethering off did nothing you could see. Now the app turns USB tethering on by itself (and the phone's Wi-Fi off first, if it's on), and if it can't, Phone setup opens and shows what's missing.
- Right after protection turned on, the app could join the server of a player you follow without you asking, and open CS2 to do it. Join automatically when a followed player moves now only offers the join, only when the player really moved to another server, and only while CS2 is open. Seeing the player for the first time just tells you.
- A question in the app (like "Server is full") covered the Cancel at the top, and some had no button to back out. Every question now has an X, Esc and a click outside the box to close it without choosing, and the Cancel at the top stays clickable.
- "IP servers see" could stay on "—" ("Checking the IP through the phone…") for minutes while you played. One failed check no longer clears it: the last IP stays, faint, with "Not confirmed since 19:30: weak connection?", and the Log says why the checks fail. A slow check is no longer overtaken by the next one forever.
- New IP + next account with protection off said it would change the IP and then changed it for nothing (servers saw your home IP). Now its name and what it does follow protection: with Use the app with protection off it only switches the account (and tells you), otherwise it asks to turn protection on first. After a ban it always asks, because your home IP is the banned one. Cancel in the top bar now also stops it, after the step that is running.
- The button showed one account and the switch could log in to another (one banned on your last server). Both now pick the same.
- Closing a question with X or Esc ("CS2 crashed on this server recently", "Steam is on another network") joined the server anyway. Now it doesn't.
- Wait for a slot, the match lag report and server timeouts now work on your home connection too (No phone, or protection off with the new option).
- "The carrier kept giving the same IP" showed when the phone's connection hadn't come back at all.
- An account switch no longer goes ahead while Steam is still closing, which could leave Steam on the old account or asking for the password. It also waits a moment after closing CS2.
- Change IP on the phone's hotspot could turn on airplane mode (on phones without "mobile data off"), which drops the hotspot and the PC with it. On the hotspot it now only turns mobile data off and on. With protection off, Change IP also knows you're on the hotspot.
- A Refresh on Accounts that Steam rate-limited halfway could clear an account's game ban and put it back in the rotation. Now that account keeps what it had and the refresh counts an error.
- After "Server is full", the app kept trying that server for a few minutes even after you joined another one yourself, and could pull you out of it. It stops as soon as you're on another server.
- If an update fails at the last step, putting the old version back no longer depends on moving the new file first, and the Log says where the app is if it can't.
- The update check skips a release marked pre-release, so a release that is taken back is never offered.
- The update check could fail for hours on mobile data: GitHub's API allows 60 checks an hour per IP, and many phones share one IP. When the API says no, the app now asks github.com which release is the latest. Updates are still only installed with the project's signature.
- Install copied everything from the old SRT_Dados to Program Files, an old copy of the phone tool (ADB) included. Programs (.exe, .dll, scripts) and the platform-tools folder are now left out: the app downloads the phone tool again from Google, checked.
- If an older copy left its protection on (after an update), the Install window now says so: CS2 and your servers stay blocked outside the phone until you install, and it gives the command that turns it off.

### Changed
- The usage stats notice is shorter: what is sent and never sent in two lines, who keeps the stats (the author, on Cloudflare), why, for 12 months, how to delete them or ask, and the full list under See the full list. It shows once more because its text changed.
- The pictures of the app on the website and in the README now show this version, and stay sharp on high-resolution screens (they are twice as detailed).
- The README reads in the website's order: what it is, how it works, IP banned, the features with a picture of each, Quick start, safety, VPN, FAQ. Each fact is said once, and the long parts (the full Telemetry list, how to check a download) open when you want them.
- On a phone, the README shows its own animation, tall and narrow, so the words in it can be read. With reduced motion on, a still picture.
- Phone setup shows one step at a time: only the step to do now has its text and button. Done steps say "Done", the ones after it show only their name until it's their turn.
- Phone setup's menu paths follow your phone: a Samsung gets the Samsung menus, every other phone the standard Android ones, with "Can't find it? Use the search at the top of Settings." Paths for other brands come once they are checked on a real phone.
- Checks only show what needs you, with one line on top ("All 11 checks OK.", "1 check needs you.", "Protection is off, so there is little to check yet."). Show all brings every row back, and the app remembers it. Regions you turned off yourself don't count as a problem.
- An empty Servers page explains what to do when you only know the server's name: join it once in CS2 with protection off and the app offers to add it, or, if you're banned there, ask on its Discord or website for the IP. It no longer tells a banned player to join the server and type status.
- Connection > Phone hides Turn on USB tethering while the phone's tethering is already on.
- The ban notification names the account you rejoin with: "Banned. Press Ctrl+Shift+F10 to rejoin with a new IP as atlas." With no account left, it says so instead of pointing to the key.
- While the phone's Wi-Fi is on or the phone is gone, the fix (Turn off phone Wi-Fi, Phone setup) is the only blue button. Change IP and rejoin go back to blue once nothing blocks the game.
- Usage stats count the Phone setup step you were at when you closed it (or closed the app with it open), next to the step it opened at, to see where people get stuck. Nothing new about you is sent: it's the name of a step. README > Telemetry lists it.
- Usage stats rows with an install ID are now deleted after 12 months. Counts added up for everyone, with no ID, stay.
- README > Telemetry, Privacy and security and the "Is it safe?" page now say who keeps the stats, what for, on what basis, where they are stored, for how long, and how to ask about them.
- Website: a short loading screen (the name and the app's three-color line filling up) while the 3D scene gets ready, so the page comes in once, ready, instead of stuttering as it loads. It shows only with JavaScript and WebGL, never more than 6 seconds.
- Website: on the home page each page of the app now has numbered marks on the real window (1, 2, 3) with the same numbers in its text; the name at the bottom of every page lights up in the app's three colors; the labels in the 3D scene stay in one place with a clean box (the Windows Firewall's now sits on the red gate where the home cable leaves the PC); the app's pages and their marks stay up longer as you scroll, and the small demos play once they are well on screen; the IP in the guides list stays inside its box when you point at a guide; the ban's last words no longer sit on a dark band across the scene; on the iPhone and IP ban guides the route is drawn once, not twice; and the Android and iPhone cards line up.
- Website: on a phone, a small chip ("2/6 Phone") shows where you are in the story and opens its steps, Setup and Guides. The address follows the step you stop on (#carrier, #ban, #servers...), so a shared link opens there. On a phone on its side, the app's window and the text next to it now always show the same page.
- Website: on a phone the first words show after about 1.5 s instead of waiting up to 6 s for the 3D scene, which still comes in as before. Under each Download button, a short line says what to do when Windows shows "Windows protected your PC" (More info, then Run anyway), with a link to Is it safe?. Clearer titles for the Android, iPhone, IP ban and region guides, and every guide has a full preview card when you share it.
- Website: a link with ?ref=<name> (from a bio on TikTok, for example) counts the visit as coming from that name, and the parameter leaves the address bar at once.
- Website on a phone held upright: the app's six pages sit side by side, one swipe each (or tap 01 to 06), and each page's three numbered things are shown cut from the real window at full size, so the app's text can be read. The story's steps and the IP ban part need less scrolling, with the same text and scenes, the Pause button and the step chip move away from any text, and the footer is in two columns. The home page is about a sixth shorter. The whole app window stays on top while its pages are read, and a tap opens the picture the width of the screen, to zoom with two fingers or a double tap; Back or a swipe down closes it. Android or iPhone has a switch on a phone, one card at a time, and opens on the iPhone if you visit from one.

## [0.1.0-beta.10] - 2026-11-04

### Changed
- Protection now starts off every time the app opens, and you turn it on with the new Turn on protection button in Status (also in the tray icon's menu). Once on, it stays on until you turn it off: a phone drop blocks your servers and CS2, it never turns protection off. This replaces the beta.9 "waiting for the phone" start.
- Settings > Protection > Turn on protection when the app starts, for those who want it on by itself (also with Start with Windows). It is off unless you tick it.
- With protection off, the app still checks the phone, so Status shows whether it is ready and Change IP works. Joining a server with protection off asks first, and turns it on for you.
- Two cases keep protection on at start: an update made while it was on, and CS2 still open with the block from the last run in place (you closed the app with Keep, or it crashed), so a match isn't moved to your home IP.
- Phone setup is now a list of steps that check themselves every 2 seconds: the phone tool, the USB cable, USB debugging, allowing this PC, USB tethering and the phone's Wi-Fi. Each one is green when it's done and red when it's next. Where the app can do the step, it has a button (install the tool, turn tethering on, turn the phone's Wi-Fi off).
- Configs with many accounts: accounts with the same crosshair show as one line ("26 accounts with the same crosshair as your main"), with Show to see each one. The main account and the one in use come first, the ones that differ stay in view, and there is a search. The ticks under Copy > To sit in a box with a filter and a count.
- Regions works without the phone: with protection off or in No phone mode, the ping is measured on your home connection, and the page says where it was measured. With protection on it is still measured only through the phone, never from your home IP.
- Regions: "Only this region" shows on the row under the pointer, or the one you click, instead of on every row. The list's header says so.
- Profiles: the web sign-in line and its button share one place. "Signed in · 187 d left" is grey when all is well and amber when it runs out in less than 14 days or has run out; on the row under the pointer it turns into Sign out. Sign in stays in view.
- Log: show all, warnings and errors, or errors only, and search the Log. Copy all still copies everything.
- Settings > Protection: the two options for when protection turns on or off by itself sit together under "Start and close".
- Change IP with "Keep changing until the connection is good": when the new IP's connection is too jittery, the next try uses airplane mode, so the phone looks for the network again and can land on another cell. Mobile data off and on keeps the same cell. Normal changes still use the method you picked, and the hotspot never uses airplane mode (it would turn the hotspot off). If the phone turns USB tethering off in airplane mode, the app turns it back on once.
- The usage stats notice shows once more, so everyone who accepted it before reads that the stats now also go out every 30 minutes.
- Usage stats are now sent every 30 minutes while the app is open (when something new was counted), before the PC goes to sleep and when Windows signs out or shuts down, as well as at the start and at close. Before, an app left in the tray for hours was counted with a minute or two, and a session shorter than 10 minutes sent nothing at close. README > Telemetry says when it sends.
- Usage stats send your list sizes, turned-off regions and changed settings in the first report of the day, and again only when they change, instead of in every report.
- A new install starts in Game only: only CS2 and Steam use the phone, so OBS, Discord and downloads stay on your home connection. Whole PC while CS2 is open is still one click away in Connection, and an install that already had it keeps it.
- The Download button on the website and in the README now downloads the exe directly, with its version, size and date next to it.
- New guide content: how to change your region in CS2, step by step, and how to do the same block by hand with Windows Firewall.
- On a phone, the guides now offer to copy the page link for your PC instead of a Windows download.
- Screenshots on the website open at full size when you click them.
- The README is shorter. The full FAQ and the setup details now live in docs/FAQ.md and docs/SETUP.md.

### Fixed
- The VirusTotal link on the "Is it safe?" page now points to the current release's exe, and the README no longer mixes up an old result with the new one.
- Usage stats: the regions you turned off never reached the stats server. The app sent the Regions page's own names; it now sends Valve's public codes (like `fra`), as README > Telemetry always said.
- Usage stats: how the phone is connected was taken before the phone was found, so it always said "not connected". The report now says the best connection seen since the last report.
- Usage stats: the time the PC was asleep no longer counts as minutes the app was open.
- Usage stats: a large report is cut to what the stats server takes, so nothing the app counted as sent is dropped on the way. What doesn't fit goes in the next report.
- Settings > Delete my stats still works on a day the stats server is busy (it has its own daily limit), and answers at once when nothing is kept under the ID.
- A new Steam account now shows up on Accounts, Configs and Profiles without restarting the app. Turning protection off stopped the check that sees new accounts, a read while Steam was writing its account list could miss one until Steam wrote it again, and Configs and Profiles didn't redraw while they were open. A new account also moves to its place in the list instead of the end.
- Steam names with symbols show as they are: `&lt; blank &gt;` no longer appears instead of `< blank >`. Profile names, real names and locations from Steam's profile page are decoded, also for names saved by earlier versions. Make all look like copies the real name too.
- An account whose Steam profile was never set up shows its name instead of a long number (its SteamID).
- In Whole PC, always, a first start could never read your home IP, so the phone was never trusted. With protection off at start, the app reads it before you turn protection on.

## [0.1.0-beta.9] - 2026-11-03

### Added
- Settings > Delete my stats: deletes everything the stats server keeps under this install's ID and gives the app a new one.
- Accounts: a Ready filter (accounts New IP + next account can use now) and a "Ready first" order (ready, then the cooldown that ends first, banned last), for when the list grows.
- Accounts can show as rows instead of cards (the button next to the order): one line per account, so bans, cooldowns and who is in rotation line up. From 12 accounts the page offers it once.
- Log > Report a problem opens a new GitHub issue in your browser with the app and Windows versions filled in. Nothing is sent until you press Submit there.
- At the first start the app checks that Steam and CS2 are on the PC and, if one is missing, says what to do.
- When the phone's USB connection drops 3 times in 10 minutes, the app says it's most likely the cable or the USB port.

### Fixed
- Servers: a marked server can be unmarked again (click it again, click the empty part of the list or press Escape). The button now says what it asks: Refresh all, Refresh 1 or Refresh 3.
- Join on a server the list shows as full now offers Join anyway, next to Wait for a slot. Many servers keep spectator or reserved slots the list doesn't count; with the teams full you come in as a spectator.
- Rejoin (or Join) turned away with "server is full" right after you tried no longer just stops: the app tries again every 15 seconds for 2 minutes. The list's player count can be old, or count spectators.
- The click that brings the app to the front, and clicks while it doesn't have the focus, no longer count as rage clicks in the usage stats.
- The app no longer blocks all of CS2, Valve's servers too, when it starts before the phone is connected (with Start with Windows, say). Until the phone is connected and its IP checked, the servers on your Servers page stay blocked and the rest of CS2 uses your home network. Then protection turns on fully by itself; if CS2 was already open, the rest of CS2 follows when you close it, so a match isn't cut. Change IP or Run leak test turns it on right away. Once on, a phone drop blocks everything as before.
- An update keeps protection as it was: still waiting for the phone, or off if you had turned it off. Before, the new version always turned it on.

### Changed
- Connection: What goes through the phone and Previous IPs always reach the bottom of the window, so both columns end on one line.
- The usage stats say a bit more, to see what in the app works and what confuses people: minutes on each screen, moves between screens, where on each screen people click (a coarse grid, added up for everyone with no ID) and clicks that hit nothing, how long ago the app was set up (in rough bands), and every 5 minutes while the app is open a short signal with what is on. Still never IPs, servers, accounts, names or anything you type. [Telemetry](README.md#telemetry) has the full list.
- The usage stats ID is now kept for the install instead of new every day, so the stats can tell people who come back from new installs. It is still random, has no name and is never made from your PC or from you. Because it is kept, the stats are no longer called anonymous (the notice is now just "Usage stats"): one install's days link up, though never to a person. Only the click map and the setup report, which carry no ID, are anonymous. Settings > Delete my stats deletes everything the server keeps under it and gives the app a new ID.
- If you accepted the usage stats in beta.8, the notice shows once more after this update, saying what changed: that notice said the ID was new every day.
- The stats also count Change IP (method, time, attempts, quality after it), phone connection drops, mobile data per day, Rejoin, the first screen opened, minutes with CS2 open and with protection on, the regions turned off (Valve's public codes), the version updated from and how many "Open with CS2" programs open as administrator, in rough bands.
- The stats also say which rows of the Checks panel are red when a report is made (by a short name, never their text) and whether Run leak test passed or failed. The stats dashboard's Versions page now compares versions with real numbers (crashes, lag, IP changes that worked, time to Protected and to start).
- Reports from online virus scanners (they run the exe in a sandbox and accept the notice by themselves) are now told apart from people in the stats.
- The usage stats also say, in rough bands: for each CS2 match whether it was a Valve or community server, whether it lagged and its ping, jitter, late messages and lost packets; how the app started, how long sessions last, how long the start and getting to Protected take, and crashes as their type and place in the code; how far down each screen people scroll; what happened with the update notice; which on/off settings were changed (by name); and on how many of the last 7 days the app was open. Never the server, the map or the time.
- A separate daily report about the setup goes with no ID and no country, and is only added into counts: Windows language, window size and screen scale in bands, Windows build, the phone's brand (from a short list, never the model), Android version and 4G or 5G. [Telemetry](README.md#telemetry) has the full list.

## [0.1.0-beta.8] - 2026-11-01

### Security
- If Windows refuses to limit where the app loads DLLs from (System32 only), the app, when run as admin, now stops with a message instead of carrying on. Before, it went on as if the limit were there. `--restore` still runs, without that limit, so you can always undo the network changes.
- The Steam Web API key is now saved encrypted for your Windows user (Windows DPAPI) in `settings.json`, not as plain text. A key saved by an older version is encrypted at the first start, and the plain copy goes from `settings.json.prev` too. If `SRT_Dados` comes from another PC or Windows user, the key can't be read there: Settings > Steam says so and you paste it again (until you do, the saved one is kept, so it still works for the user or PC it came from). Bug reports leave the key out, encrypted or not.
- `--restore` run from a folder that programs without admin rights can change (Downloads, say) no longer writes the log or anything else into `SRT_Dados` there; it only undoes the network changes.
- Limit, found while testing this release: .NET itself looks next to the exe for some Windows DLLs, before and after the app's own code starts, and the app can't change that. Run as admin from Downloads, a DLL put there by another program would run with admin rights. That's why the app shouldn't stay in Downloads: use Install, then delete the copy in Downloads.
- The phone tool (ADB) only runs when its signature's Organization field is exactly Google LLC (and the country US). The check read the signer's name as text, so a certificate that only had "O=Google LLC" inside another quoted field would also have passed. The ADB you already have is still accepted.
- Saved Profiles sign-ins (`SRT_Dados/profiles/sessions`) are now in a folder only Administrators and SYSTEM can open. Before, your own Windows user had full access to it, so a program of yours without admin rights could read a saved sign-in and decrypt it, or swap the folder for a link while the app was writing there as admin. Sign-ins saved by an older version are moved into the new folder at the next start; nothing to do.
- Open Windows Firewall (Settings) now starts Windows' own console from System32. Before, it asked Windows to "open" `wf.msc`, and what opens a `.msc` file can be changed in your user's part of the registry by any program of yours, which would then run as admin.
- Updates: the app only accepts a `.sha256` file with exactly one line, and the release signing tool only signs the exact line the release workflow writes, and only an exe built from that release's tag. Before, the tool checked the first line and the app used the line with the exe's name, so a second line slipped into a release before signing could have carried another hash.
- CS2 configs (Configs page) are written and undone through the file's own handle, checking where each file really is, so a Steam folder emptied and turned into a link while the app writes can't send the write or the delete somewhere else. The same for the bug report and snapshot `.zip` on your Desktop, which are now built in memory. Steam's and CS2's logs in a bug report are only read if they are the file itself, never through a link (a file only admins can read could otherwise end up in the zip).
- Steam account IDs read from Steam's `loginusers.vdf` must be real SteamID64s, and avatars only come from Steam's picture servers over HTTPS, 2 MB at most. A made-up ID could have made the app write a picture anywhere on the PC.
- The ADB copy is read from its folder without following links, and a copy that fails Google's signature check is deleted instead of left where everyone can read it.
- The app finds where `cs2.exe` is installed with the least access Windows has (it can't read CS2's memory). Before, .NET opened the running game with read-memory access to get its path.
- Windows Firewall's COM objects are created by their fixed IDs, not looked up by name, and server IPs must be plain digits (no spaces, signs or leading zeros) before they reach `netsh`.
- When the home IP can't be measured, the app no longer asks ipwho.is about the last home IP it remembers (from another network, maybe a laptop elsewhere, that would tell ipwho.is both).
- The usage stats server (worker/stats) now only takes JSON (so a web page you visit can't make your browser send made-up reports), stops reading a request at 16 KB even when its size isn't given, refuses more than a day of minutes in one report, and has preview addresses turned off. It still never reads or stores your IP, and its logs are off.

### Added
- An icon next to the clock (notification area) with the protection state when you point at it, and a menu: New IP + next account, Change IP, Regions, Turn off protection, Open and Quit. Click it to open the window. While the window is hidden, the app's notices come as Windows notifications, and a question opens the window. Its menu is dark, like the app (Windows 10 1903 and later).
- Settings > Background and data:
  - **Close to the notification area** (off by default): the X hides the window and the app keeps protecting. Quit is in the icon's menu.
  - **Start with Windows** (off by default): the app opens in the notification area when you sign in, with its admin rights and without the UAC prompt each time, through a scheduled task named "CS2 IP Changer". Only for the copy installed in `C:\Program Files\CS2 IP Changer` (a task that starts an exe from Downloads would let other programs swap it). Turning it off deletes the task, and so do `--restore` and Remove CS2 IP Changer.
  - **Mobile data warning**: the Phone box on the Connection page shows what went through the phone since the app opened and this month (everything on the phone's adapter: CS2, Steam and, in Whole PC, the rest), and the app warns once a month when it passes the GB you pick.
- **Remove CS2 IP Changer**, at the bottom of Settings, and Uninstall in Windows Settings > Installed apps (the installed copy is listed there, with its version). It removes every rule and route the app made (Regions too), the Start with Windows task, `C:\ProgramData\CS2IPChanger`, the Installed apps entry and the app itself, and stops the phone tool. You pick: keep `SRT_Dados` (settings and accounts, so installing again brings them back) or delete it too. Files the app still uses go a few seconds after it closes; only a file Windows still holds waits for the next restart. Not while CS2 is open.
- Hide usernames turns on by itself while OBS Studio or Streamlabs is open, and off when it closes (Settings > Steam, on by default).
- Accounts: Copy CS2 friend code, in each account's menu.
- When Windows Firewall is off because an antivirus runs its own, the app says which one (from Windows Security) instead of only "turn it on".
- After a match that lagged on the phone, and at the end of Phone setup, a few tips: the USB cable instead of the hotspot, the phone's battery saver off, 5G if you have it, the phone a little away from the PC case.
- Starting the app while it already runs (in the notification area too) brings its window back instead of saying it's already running.
- Install: started from a folder that programs without admin rights can change (Downloads, the Desktop), the app no longer shows a Windows error with steps to follow. It opens its own window with an Install button, which creates `C:\Program Files\CS2 IP Changer`, copies `CS2IPChanger.exe` and your `SRT_Dados` folder there (copied, never moved) and starts the app from there. An `SRT_Dados` already there is kept, and a newer version already installed is never replaced with an older one. No Start menu shortcut.
- How Install stays safe: the copy in Downloads opens no window and writes nothing there, not even the log. It checks that it is the file that is actually running (a file swapped in at the same place is refused), copies itself to `%ProgramData%\CS2IPChanger\install` (only admins can change it) and starts that copy, which shows the window. `SRT_Dados` is copied without going through links, junctions or hard links, and without `owned.json` (the app's route list must come from a folder only admins can change). The installed app removes the `install` copy at its next start.
- CS2 network report in the Log: when you leave a server, CS2 writes a network summary to its console. The app now reads it and writes one line: ping, upload and download jitter, how many of your messages reached the server late, how many were lost for being late, how many server ticks were lost for being late, packets dropped, and the IP the match went out on. On mobile data the ping is usually fine and the lag is upload jitter; with this line you can compare where the phone was, which IP you had and the time. It needs `-condebug` in CS2's launch options, like kick detection.
- Settings > Change IP > Keep changing until the connection is good (off by default). After each new IP the app pings for about 2 s, as in Checks > Connection quality, and changes again when jitter is 20 ms or more or 2 of the 10 pings are lost: up to 3 more changes on top of the attempts above it, so a jittery IP never uses up the attempts for a new one. Each check adds 5 to 15 s.
- Kicks and bans are told apart, and only on community servers. CS2 only gets a reason code from a server, so the app reads that code and what the server said in the half minute before it: a ban (with its length when the server says it, as CS2-SimpleAdmin does; the ban mark on that account then ends with the ban), a VPN or proxy block ("VPN/Proxy detected"), a kick with its reason (AFK, a vote, team kills, an admin), a full server or a reserved slot, a timeout or a lost connection, a map change, or you leaving. Only a ban or a VPN block offers New IP + next account. A kick offers a plain rejoin with the same account and IP. A timeout says it was the connection. A full server says so and offers nothing; a map change or leaving yourself say nothing. On Valve's matchmaking servers (reached through Valve's relays) nothing is shown, counted or offered.

### Changed
- Settings is tidier: what you change most is on the left, Maintenance is four buttons of the same size, and Remove CS2 IP Changer is last and on its own. Turn off protection is no longer in Settings (it is on the Connection page and in the icon's menu).
- Usage stats also count `first_start` once, on the day you accept the notice, so the number of new users can be seen (README > Telemetry).
- Usage stats are now anonymous, and required to use the app while it's in beta. The ID is random and new every day (UTC), never made from your PC or from you, so the ID never links one day's report to the next; the fixed install ID of older versions is never sent again. At the first start, before the app opens, a notice lists what is sent, with Accept and Quit. Until you accept, nothing is sent and no ID is made; Quit (or Escape) closes the app without changing anything on your PC. Right after an update from inside the app, Quit also turns off the protection the version before left on, so your servers aren't left blocked without the app (not while CS2 is open). Accept is saved and the notice doesn't show again. People coming from beta.7 see it once too. Sent when the app opens and closes, and once a day if it stays open: the daily ID, the app and Windows versions, your country as two letters, the mode, how the phone is connected, minutes open, how often each button and hotkey is used, a few counters, how long your lists are (in rough bands, never the exact number), and errors as their type and place in the code. Never IPs, servers, accounts, Steam names or IDs, your carrier or anything you typed. Settings > Updates has a line saying this instead of the Send usage stats switch.
- "Already running", the result of `--restore` and "its data folder can be changed by programs without admin rights" are now shown in the app's own window instead of a Windows message box. A few messages stay as Windows boxes because they show up before the app can load its window safely: Windows refusing to limit the DLL search, the "couldn't restart from a folder only admins can change" errors, and the errors of the copy started from Downloads (the Install window couldn't open, or is already open).
- README: a "What it solves" table at the top, more FAQ answers (changing the IP, root, iPhone, antivirus warnings, running in the background, mobile data), and a new [Privacy and security](docs/PRIVACY-AND-SECURITY.md) page: every service the app talks to and what each one receives (ipwho.is gets your home IP and the phone's IP), what it does with admin rights, why an antivirus may flag it, and the known limits.
- While a command is only waiting (CS2's main menu before a join, Steam reconnecting, a question), the top bar shows Cancel. It stops the wait without starting anything else; before, the only way out was another command, and nothing said so.

### Fixed
- Profiles and Accounts at the smallest window size: the Preview button and the account count were cut off on the right.
- A greyed-out menu item (like Clear server bans with no bans) looked the same as the others.
- Quit in the icon's menu did nothing while the window was hidden and the app had to ask what to do with protection first (the question couldn't open the window). It now opens the window with the question.
- Turn on USB tethering when tethering was already on asked the phone again, which resets its USB link and dropped the connection for up to a minute. It now checks first (Windows sees the phone and the phone says tethering is on, so a USB modem or another phone isn't taken for it) and says it's already on.
- "Windows doesn't see the phone's USB tethering" showed 3 s after asking for it, while Windows was still adding the phone (it took 5 s). It now only says so when the phone has been missing for more than 30 s, counted from the request or from the first check that missed it (tethering turned on in the phone, or the cable plugged back in, gets the same time). Windows also gets 30 s instead of 15 to add the phone.
- Many notifications (about 130 of them) only showed for a few seconds and never reached the Log. Every notification now goes to the Log too, unless the app has just written its own line about the same thing.
- Usage stats: minutes open were always 0 for someone who opened the app once. The app only sent its report 20 s after starting (and once a day), so the minutes and button counts of a session waited for the next start, which may never come. It now also sends when it closes, at most 3 s, if 10 minutes have passed since the last report.

## [0.1.0-beta.7] - 2026-11-01

### Security
- Run as admin, the app no longer starts from a folder that programs without admin rights can change (Downloads, the Desktop, a folder you made yourself). A DLL put next to the exe there would run with admin rights. It says what to do instead: copy `CS2IPChanger.exe` to `C:\Program Files\CS2 IP Changer` and start it from there. `--restore` still works from anywhere. Until now the app only warned.
- DLLs that Windows loads by name (`UMPDC.dll`, `WindowsCodecs.dll`, `DEVOBJ.dll`, `ncrypt.dll` and the like) now only come from System32, never from the exe's folder, the current folder or PATH. Limit: what Windows and .NET load before the app's own code runs isn't covered (a planted `CS2IPChanger.runtimeconfig.dev.json`, for example, is read before that), which is why the folder itself has to be one only admins can change.
- The app no longer passes .NET's runtime variables (`CORECLR_PROFILER`, `CORECLR_ENABLE_PROFILING`, and the other `CORECLR_`, `COR_`, `COMPlus_` and `DOTNET_` ones) from your user environment to the copy of itself that runs, and starts again without them if they are set. They can load a DLL into a .NET app run as admin. The Log says when that happened.
- Files in `SRT_Dados` (settings, lists, the Log, crash.txt, the ADB download) are never written, moved or deleted through a junction, a symbolic link or a hard link. The ADB zip is unpacked file by file, with the same check.
- `owned.json` (the routes the app added) is only read from a folder only admins can change, and only entries shaped like the app's own routes are used. A forged file could have made the app delete other routes.
- The `%ProgramData%\CS2IPChanger` folders are created with admin-only permissions in one step, and one that another program creates in between is never used. The ADB folder is checked again before every adb command.
- ipwho.is (ISP and country of an IP) is now asked about each IP only through that IP's own connection: the home IP through home, the phone's IP through the phone. Before, both questions went out through home, so ipwho.is could tie the phone's IP to your home IP. When the phone only carries the game, the phone's line shows the country from the app's own IP test instead of the ISP.

### Changed
- The Switch button is now New IP + the account it logs in to ("New IP + atlas"), in the title bar and on Accounts, because it changes the IP first. Settings > Switch is now Settings > New IP + next account, its hotkey is the New IP + next account hotkey, and "Switch mode on a weak connection" is now "Fall back to Game only on a weak connection". With fewer than two accounts the button is hidden. If Settings leaves out a step it says so: "Log in to atlas" without the IP step, "New IP" without the account step.
- Accounts: the arrows on each card are gone. Each card has an In rotation tick instead, next to Log in; "skipped" is now "not in rotation", the Skipped tab is Not in rotation and the Clean tab is No bans. "limited" explains itself when you point at it. "no Steam bans" is no longer green: colour is kept for bans and cooldowns.
- First start: until a phone has been connected once, Status says Not protected yet in amber (servers would see your home IP), Phone setup is the one main button, and Install phone tool and Turn on USB tethering stay hidden (Phone setup does both). "Phone tool (ADB) not installed" is now "Phone tool not installed", with the size on the line under it.
- Turn on USB tethering only shows while the phone is found over USB debugging.
- Connection: Turn off protection is now in the Status buttons too, and only while there is protection to turn off. After you turn it off, Run leak test reads Turn protection back on.
- Connection > Checks: the three rows that decide whether you are protected come first, and the names are plainer: Kill switch is Block if the phone drops, Firewall is Windows Firewall, Different from home IP is Phone IP is not your home IP, Rest of the PC is Other apps. A new row, Matchmaking regions, says how many regions CS2 can play in (grey when all are allowed), because that block stays on after you turn protection off or close the app. Open Windows Firewall moved to Settings.
- Connection: What goes through the phone sits under Phone, and Previous IPs under Checks. The default option now says it uses more mobile data while CS2 is open. Previous IPs shows the ISP and city (the country is in the tooltip). The last group of each column only reaches the bottom of the window when that fills a small gap or the window is wide, so a short Previous IPs is no longer a tall empty box.
- While the IP changes, Status says "Changing IP… attempt 1/3" in the title and the title bar, the old IP turns grey, Checks > Mobile data says "Off for 4 s" and then "Coming back on…", and the progress line is no longer cut short. The new IP shows in green for two seconds, next to the notice.
- Once you have joined a server, Change IP and rejoin is the main button in Status, and its in-game hotkey (Ctrl+Shift+F10 unless you changed it) is written under it.
- Closing the app with protection on asks once: Keep and close or Turn off and close. The app remembers the answer in Settings > Protection > Turn off protection when closing the app. Escape keeps the app open. An update or Windows shutting down never waits for this question.
- Settings: Maintenance is now Undo and maintenance, first in the left column, with Open Windows Firewall. Long option texts are one line now, with the rest when you point at them. A player flag's colour is a small swatch at the right of its row; click it to change the flag.
- Configs: Copy comes first, with What to copy in the same box (Sync uses the same ticks). Copy is the page's one main button. Backups is now Backups (undo), at the top right, so you can reach it without scrolling. Times are written the same way everywhere on the page.
- Profiles: Clear name history (can't undo) moved down next to Apply and unticks itself after every Apply. Forget is now Sign out, and Forget all sign-ins is Sign out of all. "Make all look like" is now "Make the others look like", which is what it does: it copies that account's look into a new template, picks every other account and shows the preview. Use this template on new accounts moved under the template. Account rows are plain rows like the other lists.
- Servers: Port if missing is now Default port. With no servers, the page says how to find a server's IP:port (type `status` in the CS2 console). Under an open server, the players fill the whole row. A player flag and a ban show before "You played here" on narrow windows. The Players header says how to open the list and flag a player. Route says Protected in grey and only colours the rows that aren't.
- Regions: Allow all is now Allow all (block off). The ping slider is a filled bar you click or drag anywhere.
- Log: messages are in normal text, with a marker before the time for successes, warnings and errors.
- Look: the page behind the groups is darker, secondary text is easier to tell from labels, and the app uses fewer text sizes. Hovered and selected rows are easier to see, and the active tab has a blue bar. Status markers have a shape as well as a colour (filled square: OK, hollow square: waiting or off, diamond: warning, red square: problem), so they read without telling green from amber. Buttons have two heights, the confirm button of a dialog that turns something off or deletes it is red, a disabled red button is grey, and radio buttons are square like the checkboxes. Dialogs keep the coloured line at the top of the window lit, notices have the same frame as the rest with a coloured strip, dropdown lists are framed like the right-click menus, player flags are tinted instead of solid, and the Regions and Servers notices are no longer filled boxes. The Steam sign-in window lost its empty band above the buttons.

### Fixed
- The Protected line in Status said only the servers on your Servers page saw the phone's IP. With the default Whole PC while CS2 is open, every server does; the line now follows the mode you picked.
- Checks > CS2 only through the phone showed a green square next to "Not locked yet". It now shows the waiting marker until CS2 is really locked to the phone.

## [0.1.0-beta.6] - 2026-10-31

### Changed
- Usage stats now include the country, as two letters. Cloudflare works it out from the connection and gives the Worker only the country; the Worker still never reads or stores the IP (README > Telemetry).
- Reports from a PC whose `SRT_Dados/telemetry.json` has `"Mine": true` count as development builds and stay out of the numbers.

### Added
- Website: guide pages for the region picker, copying your CS2 config, the account switcher, Steam profiles in bulk, changing IP with your phone, CS2 IP bans and "Is it safe?", linked from the home page and the sitemap.
- crash.txt in SRT_Dados keeps the details of an error the app caught (once per kind, about 64 KB at most), and Save bug report includes it. If the last session didn't close normally, the Log says so at the next start.
- The log file keeps about 14 days: older lines go to the .old copy and then away. Settings > Maintenance > Delete log files deletes them now (they hold your home IP, Steam names and servers).

### Fixed
- Opening the app for the first time without a phone and closing it left CS2 unable to connect at home (the CS2 lock stays after closing). CS2 is now only locked once a phone connection has been checked on that PC.
- Settings, accounts and lists could go back to the defaults without a word: a file locked for a moment (an antivirus or OneDrive) was read as damaged and then saved over with the defaults. Now a locked file is tried again, and if it still can't be opened the app doesn't save over it until the next start.
- A power loss right after a save could leave a file empty. Saves are now flushed to the disk before they replace the old file, the file before is kept as `.prev`, and a damaged file is kept as `.broken-…` while the `.prev` copy is loaded.
- The server list accepted local network addresses (the home router, `192.168.x.x`, `10.x.x.x` and so on). The kill switch then blocked them outside the phone and could cut the PC's DNS. They are refused now, and old lists lose them with a line in the Log. The "Ignored" line shows 20 at most.
- Configs: after 20 syncs the account's config from before the app ever changed it was gone. The oldest backup of each account is now kept for good, marked Original, and always shown in Backups.
- A clock change (summer time ending, a big time correction) made the app forget the routes it had added, so Whole PC routes could stay on the phone after going back to Game only. The app now keeps that in UTC.
- A clock that was once ahead stopped the daily update check and the account refresh until that date came round. A date in the future now counts as due.
- The same Log line on every tick (a route that keeps failing, an error every few seconds) filled the log file in hours. It is now written once, and the next line says how many times it repeated.
- The adb server the app started kept running as admin after the app closed, and kept its folder locked. It is now stopped on exit; an adb server the app never used (Android Studio's, say) is left alone.

## [0.1.0-beta.5] - 2026-10-29

### Added
- Regions page: pick where CS2 matchmaking (Premier, Competitive, Casual, Deathmatch, Wingman) can put you. The list of regions comes from Valve (`GetSDRConfig`, app 730), grouped by city and continent, with each region's ping measured through the phone. Turn regions on or off one by one, or use Only this region, Closest 3, your continent only, Allow all or Ping under X ms. Turned-off regions are blocked with one Windows Firewall rule for `cs2.exe` only and only on Valve's relay IPs, so Steam, friends and community servers keep working. The last list is saved for offline use, the rule is rebuilt when Valve changes the list, and it comes back at every start. Allow all and `--restore` remove it. Changes apply at once; CS2 re-measures the regions on its next start.
- Configs page: copies your CS2 config between your accounts. Binds, crosshair, game settings, video, and HUD, radar and audio can each be copied on their own (the crosshair without the sensitivity, too). Sync modes: from a main account, from the account used before, or latest change wins; it runs when a new account shows up, before the app logs in or opens CS2, or with Sync now, and an account can be left out. Nothing is written while CS2 is open (the copy waits until it closes), the Steam Cloud copy is written the way CS2 does, and every change keeps a backup with Undo last sync. Snapshots save a config under a name and export or import it as a .zip; Compare lists the settings that differ between two accounts or snapshots, with a crosshair preview. Right-click an account for Copy config to and Use as main config.
- Profiles page: change the name, picture, real name, bio, country, privacy and custom URL of the accounts you pick, from templates you save. A preview shows each account now and after; Apply changes them one after another with a pause, and each result is shown in plain words next to the account and in the Log.
- Make identical: every picked account gets one account's look in one click (also Accounts > right-click > Copy look to…). Copy from account reads an account's look into a template.
- `{name}` (the name now) and `{n}` (1, 2, 3…) in names, and `{n}` in custom URLs. There is no `{login}`, and a name, bio or URL that would show any login saved on the PC is refused.
- Undo: a copy of each profile (with its picture) is kept before the first change; Restore original and Undo the last change put it back. Templates, their pictures, the undo copies and the sign-ins all stay on the PC in `SRT_Dados/profiles`.
- Templates for new accounts: an account that shows up later is offered a template and asked to sign in once.
- Clear old names ("previously known as"), off by default, with a warning that it's permanent.
- Usage stats also count how many regions are turned off (`regions_blocked`, the number only).
- Phone setup now does the work instead of showing a list. It checks the cable, the USB driver, USB debugging, tethering, the phone's Wi-Fi and where the rest of the PC goes, step by step in the same window, does every step it can (installs ADB if you say yes, waits for you to allow USB debugging, turns on USB tethering, turns off the phone's Wi-Fi after asking, keeps the rest of the PC on your home network) and tells you the one thing left when it can't. A phone whose USB driver failed in Windows (code 10 on Samsung until a restart) now says so, instead of "Phone not found".
- No phone mode: under What goes through the phone, "No phone: play on your home connection". Nothing is blocked, servers and CS2 see your home IP, Change IP is off, and the page says plainly that you're not protected. Accounts, Configs, Profiles and Regions work as usual. The default stays as it was.
- Profiles: copy a look from any Steam profile link (`steamcommunity.com/id/…`, `/profiles/…` or a SteamID64), for example an account on another PC. The copy button next to the template > From a Steam profile link. Only the public profile is read, only from steamcommunity.com; what it keeps private stays as it is on each account.
- Regions: the continent shortcut is your continent (from the region with the lowest ping, or your home country), so outside Europe it says North America only, Asia only and so on. Without either, the button hides.

### Changed
- Profiles: while Apply runs, the button turns into Stop (it stops after the account being changed). Closing the app during a run asks first and waits for that account to finish.
- Configs: saving a snapshot under a name that already exists asks before it replaces the old one.
- Regions: Valve's list is read again every hour while regions are turned off, so new relays get blocked too, and a rule that was removed outside the app is put back.
- Regions no longer depend on the phone or on protection: Turn off protection, closing the app with "Turn off protection when closing the app" on and No phone mode all leave the regions you turned off as they are. Only Allow all lifts them, and `--restore` and `tools/restore.cmd` still remove everything. Region ping is measured on your home connection in No phone mode.
- "Turn on USB tethering" now waits up to 15 s for Windows to see the phone and tells you if it worked.
- Profiles: Restore only sends what differs from the profile now. The picture isn't uploaded again when it's the same one (Steam re-encodes every upload), and privacy isn't set again when it already matches.
- The app removes the unpacked files of older versions from `C:\ProgramData\CS2IPChanger\net` when it starts (only inside that folder, never through a link).
- Clearer names: Restore network is now Turn off protection (last in Settings > Maintenance), Export diagnostics (Log page) is now Save bug report, Hide logins is now Hide usernames, Make identical is now Preview, and Clear old names is now Clear name history (in the Change row).
- Accounts: the Switch button says which account it logs in to ("Switch to atlas"), and the list counts hidden accounts. Configs: the From account no longer shows under To, and with Sync off its When options and Sync now are greyed out. Regions and Profiles: the buttons are greyed out while there is nothing to act on.

### Fixed
- Cooldown hours are read the same in every language: "1.5" and "1,5" are both an hour and a half. Before, "1.5" on a PC set to Portuguese was 15 hours.
- A failed account switch puts Steam's previous account back, so Steam doesn't stay set to log in to the account that failed.
- False "Phone Wi-Fi is on" alarms with CS2 open: an adb answer that wasn't a value (an error, a timeout, an empty reply) counted as Wi-Fi on and blocked servers and CS2 for a few seconds. Now only a real "on" counts, it has to show up twice in a row, and anything else keeps the state it had.
- With tethering on, Windows could move the whole PC onto the phone in Game only and No phone (it used mobile data while CS2 was closed). A high metric on the phone's adapter wasn't enough: on a real PC Windows kept the phone whatever its metric. Save mobile data now makes the phone's adapter ignore its default route (`IgnoreDefaultRoutes`, until the next restart only), so the rest of the PC stays on your home network while the game, Steam and the IP test still go through the phone on their own routes. It is undone in Whole PC, with the option off, when the phone is unplugged and when there is no home network (the PC is never left without internet), and on Turn off protection, on closing the app, by `--restore` and by `tools/restore.cmd`. With the phone ignoring its default route Windows no longer lists its gateway, so the app reads the one DHCP gave it, only when it is on the phone's own network.
- Closing the app with protection left on (the default) now gives the phone's connection back its default route, so the PC can still use the phone's internet once the home network is gone.
- After tethering was turned off, the Log said "Steam through the phone turned off" every 5 seconds: the routes Windows dropped with the phone's adapter were still counted as the app's. They are let go now, and the line only shows when a route is really removed.
- No phone mode logged "Servers blocked" (nothing is blocked there) and "The phone has USB tethering on, but Windows doesn't see it" while Windows did see the phone.
- A PowerShell error in the Log is one line ("Access is denied.") instead of six lines of position and error codes.
- Profiles: restoring one profile asks "Put 1 profile back to how it was", not "how they were".
- Checks > Rest of the PC called the phone's adapter "Home network" while the phone's IP was still being checked. It now says when the PC is using the phone.
- Account switching landed on the wrong account with today's Steam client (also in beta.4): the app wrote `MostRecent` and `AllowAutoLogin`, which the client no longer reads, so Steam came back on the account it had. It now sets `AutoLogin` and `timestamp` the way the client does (older files keep their keys), waits until Steam is really on the new account before it says "Switched" or opens CS2, and tells you when Steam logged in to another account instead.
- Account switching from one account to another could still land back on the first: Steam was closed the hard way, and a killed Steam put its own account back when it started. The app now waits up to 20 s for Steam to close by itself, waits a second, writes, checks the file again right before Steam starts, and if Steam still comes up on another account it tries once more before telling you.
- The real cause of the switch above landing on the old account: Steam started through Explorer (which the app uses so Steam never gets admin rights) ignores the account marked in loginusers.vdf and logs back in to the one it had. The app now starts Steam with `-login <account>` (the account name only; Steam uses its saved sign-in, no password). Tested on a real PC: both directions, every time.
- Accounts: with "Most recent" the cards no longer jump around right after a switch (the next click landed on another account). They keep their places while the page is open; the new order shows on your next visit or when you change the sort. The account in use is marked again with today's client, which leaves Steam's ActiveUser at 0. After a switch you start in the app with no CS2 to open, the app comes back in front of Steam.
- Profiles: Copy look from a link only ticks what the profile really has, so Apply can't wipe your accounts' bio or real name with an empty one. Steam's default picture (the "?") is never uploaded: copying it leaves the picture unticked, and Restore of a profile that had it says to pick it again in Steam > Edit Profile > Avatar instead of uploading a copy that only looks like it.
- Configs: Compare no longer lists a bind as different when one side has the key unbound and the other doesn't have it at all. Copying Video keeps this PC's refresh rate and monitor (CS2 warned that a 144 Hz screen ran at 60 Hz); the resolution is still copied. Picking "From a main account" before choosing one logs "pick one below" instead of "(an account)", and names with spaces or blank padding no longer leave a double space in messages.

### Security
- At every start the app checks its own folder. When it runs as admin from a folder any program can change (like Downloads or the Desktop), Checks, a toast and the Log say so and tell you to copy the exe with its `SRT_Dados` folder to `C:\Program Files\CS2 IP Changer`. The app still starts.
- WPF's own files are unpacked to `%ProgramData%\CS2IPChanger\net`, a folder only admins can change, instead of your `%TEMP%`. To do that the exe starts itself a second time at launch.
- Signing in for the Profiles page works like Steam's login page (password with Steam Guard, or a QR code from the Steam app). The password goes from a SecureString into Steam's RSA key and is wiped; it's never saved or logged. Only Steam's sign-in token is kept, DPAPI-encrypted for your Windows user (with the app's own entropy and the SteamID), one file per account that only your Windows user can read. Forget and Forget all sign-ins delete them.
- The Profiles page only talks to Steam's hosts over HTTPS with normal certificate checks, never follows redirects on its own and sends the session cookies only to steamcommunity.com. Pictures are decoded and saved again as a new JPG, so no photo data reaches Steam.
- Sign-in tokens are masked on screen in the recording build, in Export diagnostics and in Copy all, and are never written to the Log. Signing in is off in the recording build and the demo.

## [0.1.0-beta.4] - 2026-10-29

Anonymous usage stats while the app is in beta, a recording build for videos, and signed updates.

### Added
- Anonymous usage stats while the app is in beta (README > Telemetry): a random install ID, versions, the mode, minutes open, how often each button and hotkey is used, a few counters and errors as type and place in the code. Never IPs, servers, accounts, names or anything you typed. The server's code is in `worker/stats` and keeps no IP. Settings > Updates says so too.
- Recording build (`dotnet publish -c Release -p:Recording=true`, exe `CS2IPChanger-Recording.exe`) for videos and streams. Every text in the window, tooltips and menus is filtered before it is drawn: all IPs (the phone's too), MACs, SteamIDs, e-mails, the API key, your Windows user in paths, logins, Steam names, players' names, carrier, city, phone and Wi-Fi names. Hide logins can't be turned off, Steam pictures aren't shown, Copy all is filtered, Export diagnostics is off and it never updates itself. Put it next to the normal exe to use the same data.

### Security
- Updates are only installed when the release's `.sha256` is signed with the project's key (ECDSA P-256, with the version inside so an old release can't come back under a new tag). The key isn't on GitHub, so access to the repo or its releases isn't enough to push an exe to installed copies. Releases are made as drafts and published after signing (docs/RELEASING.md).
- ADB runs only from a copy in `%ProgramData%\CS2IPChanger\adb`, a folder only admins can change, with `adb.exe` and both DLLs checked for Google's signature there. Before, it ran (as admin) from folders any of your programs could write to, so a swapped DLL would have run as admin. A folder with that name made earlier by someone else is moved aside and made again.
- The updater checks the new exe again in place and holds it locked until it has started, so nothing can swap it in between.
- No browser or Explorer window is started as admin any more (a link that can't open goes to the clipboard; Open data folder opens as you).
- Export diagnostics also leaves out logins, Steam names, players' names, SteamIDs, e-mails, Wi-Fi names and your Windows user.
- Account switching refuses to write to a `loginusers.vdf` (or its config folder) that is a link or junction.
- A Steam file nested too deep no longer closes the app.

## [0.1.0-beta.3] - 2026-10-28

Fixes from the beta.2 tests on a second PC: joins that got stuck, bans that weren't marked, the phone's Wi-Fi cutting connections, and a connection quality check.

### Fixed
- The phone's Wi-Fi turning on for a second or two without joining a network (some phones do this on their own to scan) no longer blocks everything. Each time it did, Steam and the whole PC moved off the phone and back within a second, which cut every open connection: the ping jumped, and programs opened with CS2 lost their connection. Now only a Wi-Fi that is connected to a network blocks, as before. On Android 10 and older, where the phone doesn't say, any Wi-Fi still blocks.
- A join that got stuck no longer locks the app. When the app was waiting (for your answer to a question you couldn't see in game, for CS2's menu, for Steam or for a server after a crash), every hotkey only got "still busy". Now a new command stops the one that's only waiting and runs. Changing the IP or the account is never cut in half.
- Waiting for Steam after a new IP no longer repeats on every join when Steam's log doesn't show the new login: Steam holding its connection to Valve through the phone for 5 seconds also counts.
- Bans that weren't marked: one disconnect writes several console lines, and only the first one counted, so a "kicked" line before the ban code hid the ban. Now the strongest one counts, and a ban seen right after a kick still gets marked. A kick before you even get into the game (how ban plugins refuse a banned player) counts as a ban. A ban on a server that isn't in your list is marked by its address, and the app no longer marks it on the last server from your list by mistake. An account Steam logged in to a moment ago is picked up before marking.
- The server CS2 was on is forgotten when CS2 closes, so a kick in the next session isn't put on the old server.
- The "You were kicked, rejoin?" question no longer stays open after you rejoin with the hotkey. Clicking it later (or pressing Enter in the window) changed the IP mid-match, and while it stayed open the next kick got no offer. Any command now closes an old question.
- Going back to the menu, or to a Valve server (Premier, Deathmatch), now counts as leaving the community server. A kick there was put on the last community server: it offered to rejoin it, and a kick while loading could even mark a ban there. Following a player on that server also went quiet.
- A kick from a server that isn't in your list no longer promises that the rejoin hotkey takes you back, and a server you joined days ago no longer stands in for the one you were on.
- The "No Steam logon" kick is no longer silent: the app plays the sound and tells you Steam lost its session (no ban mark, no new IP offer).
- Restarting Steam through the phone closes CS2 on purpose, and that's no longer taken for a crash (which made the next rejoin wait up to 5 minutes).
- Steam dropping and logging on again in the same second (right after a new IP) no longer leaves it shown as offline, which held every join back.
- A join right after Change IP no longer fails with "protection is not confirmed" because it read the protection state from before the new IP was checked.
- After a new IP, joins aren't held back for good when Steam doesn't log a new login: with Steam on the home network it isn't needed, and after 2 minutes it no longer holds joins back.
- Turning mobile data (or airplane mode) back on is retried for about 20 s, so the phone isn't left without data when ADB drops for a moment.
- `NETWORK_DISCONNECT_STEAM_BANNED` is the server's own ban list refusing the SteamID, not Steam or VAC. It now counts as a server ban: the account gets the ban mark and the app offers a new IP with another account.
- The "As admin" switch in Open with CS2 uses the app's own style.
- Settings > Switch said the rejoin hotkey always keeps the same account. Since beta.2 it switches account when that one is banned on the server.

### Added
- When the server stops answering while you join or play (a timeout, which is how a ban at the server's firewall looks), the app tells you and points to Change IP and rejoin. Not while the app itself is changing the IP or the phone dropped.
- Connection quality: every 30 s the app pings through the phone and shows ping, jitter and loss in Checks (Good, Fair or Weak). Weak needs two bad measurements in a row: 150 ms or more, jitter of 40 ms or more, 2 of 8 pings lost, or a 3G/2G network. The app warns you when it turns weak and when it's fine again.
- Pick the mode by connection quality (Settings > Protection, on by default): with a weak connection, Whole PC drops to Game only so CS2 doesn't share the phone with the rest of the PC, and comes back when the connection is fine and CS2 is closed. It never drops while CS2 is on a server outside your list (Game only would block it), and picking a mode yourself always wins.

### Changed
- With Whole PC on, the Log says that the programs opened with CS2 also go through the phone, and that Game only keeps them on the home network if a new IP cuts their connection.

## [0.1.0-beta.2] - 2026-10-28

Fixes from the first beta.1 tests.

### Added
- Open with CS2: an "As admin" switch for each program. Windows asks first (UAC) and the program gets its own admin rights, never the app's. For programs that don't work without admin.

### Changed
- After a ban, the app offers a new IP **and** the next account the server (or its community) hasn't banned, then rejoins. A ban is on the IP and the account, so a new IP with the same account only got refused again. The rejoin hotkey does the same when the account in use is marked as banned on the last server.
- "CS2 only through the phone" now covers CS2's game traffic (UDP: servers, invites, Valve's relays). CS2's web requests over TCP, and programs loaded into the game, use the normal connection again. In Game only mode they had no way out and failed with network errors. Servers in your list stay blocked off the phone on every protocol.

### Fixed
- A Steam account added or logged in outside the app (Steam's own login, another switcher) now shows up within a few seconds and becomes the account in use. Before, the app kept the old one until it was restarted, so ban marks and Switch acted on the wrong account.
- Player lists that showed up and then turned "hidden" on the next refresh: lists split over several packets (full servers, long names) are put back together, a late answer from the server's previous reply no longer gets in the way, and an empty answer is asked again once before calling the list hidden.

## [0.1.0-beta.1] - 2026-10-28

Fixes from a full review of alpha.4: Steam and CS2 no longer run as admin, hotkeys that don't clash with AltGr, Whole PC mode that also covers DNS and IPv6, and a diagnostics export for bug reports.

### Added
- Hotkeys you can change (Settings > Switch): click the box and press the keys. Ctrl+Alt combinations are refused, because on many keyboards that is AltGr.
- Community names for servers: right-click a server > Set community name. Servers with the same name share ban marks. Empty goes back to the tag from the server name.
- Export diagnostics (Log page): a .zip on the Desktop with the log, the app's settings, rules and routes, Steam's connection log and CS2's console. The Steam Web API key and your home IP are left out.
- Change IP works on the phone's Wi-Fi hotspot when the phone is also plugged in by USB for ADB.
- Whole PC mode now covers DNS and IPv6: DNS can only leave through the phone and all IPv6 is blocked while it's on. The Rest of the PC check and the top of Connection say which part isn't confirmed. The first visit to a site can take a second or two longer while Windows gives up on the home DNS.
- The Steam Web API key is hidden in Settings until you click Show (and stays hidden while Hide logins is on).

### Changed
- The hotkeys are now Ctrl+Shift+F9 (Switch) and Ctrl+Shift+F10 (Change IP and rejoin). The old Ctrl+Alt+Q and Ctrl+Alt+R also fired on AltGr+Q and AltGr+R, which type @ and ® on German and US-International keyboards: typing an email in Discord could close CS2 and Steam mid-match.
- The carrier and city of an IP come from ipwho.is over HTTPS instead of ip-api.com over plain HTTP. The "flagged as VPN/proxy" note is gone (ipwho.is doesn't give it for free).
- CS2 starts with `-condebug -conclearlog`, so `console.log` starts empty each time instead of growing forever. Add both to the launch options if you start CS2 from Steam.
- USB network adapters that aren't phones (a laptop dock's USB-Ethernet, for example) are no longer picked as the phone connection on their own. Pick them by hand if you want them.
- VAC and Steam refusals in CS2's console (`NETWORK_DISCONNECT_KICKED_VAC…`, `…VACBANNED`, `…STEAM_…`) are no longer treated as kicks. The app says a new IP won't help and doesn't offer to rejoin or mark a server ban.
- Bans no longer spread to other servers just because they share an IP (shared hosting puts different communities on one IP), or because of generic names like "Retakes" or "Mirage 24/7".
- The phone status uses one ADB command instead of two, so there are fewer `adb.exe` processes every 2 seconds during a match.

### Fixed
- Steam, CS2 and the browser links no longer start with the app's admin rights: they open through Windows Explorer as you. If Explorer can't, Steam still starts as before and the Log says so.
- The rule that keeps the IP test on the phone blocked 1.0.0.1 for the whole PC when no phone was connected (so 1.0.0.1 as a DNS server stopped working). It now only applies to the app itself.
- `--restore` now works when a stuck copy of the app is still running, and it tells you to close that copy.
- Update and the ADB download tell you the size, and warn you when the download would go through the phone's mobile data.
- "Keep changing until the IP is in a new /16" now compares with every earlier IP, like the range note after the change. Before, it could accept an IP the note then called "Same range".
- `loginusers.vdf` is only rewritten when the app can read it back exactly, and a dated backup is kept before each write (the 10 newest).
- On Windows in other languages, adding a route that already exists no longer shows a false warning in the Log.
- With Hide logins on, the Log page also covers your home IP and the account logins (the log file keeps everything).
- With CS2 already open at the first start (or in Whole PC mode), the phone connection was trusted without a home IP to compare it with. Now servers and CS2 stay blocked until the home IP has been read once.

### Upgrading
- The hotkeys changed to Ctrl+Shift+F9 and Ctrl+Shift+F10. If you'd rather have others, set them in Settings > Switch.
- Add `-conclearlog` next to `-condebug` in CS2's launch options if you start CS2 from Steam.
- Nothing else to do: settings, servers and accounts carry over, and the firewall rules are updated on the first start.

## [0.1.0-alpha.4] - 2026-10-28

Players on your servers, flags, following a player, one switch for IP, account and rejoin that you can set up, sounds and progress for every command, and programs that open with CS2.

### Added
- Servers: click the player count to see who is on each server, with score and time played. The list comes from the server (A2S), through the phone like everything else.
- Player flags: right-click a player to flag them as Staff or with your own flag name and color. Flags stay on this PC (`SRT_Dados/flags.json`) and show next to the server name. Servers only share player names, never the SteamID, so a flag without a Steam profile matches anyone with that name. The app says so where you flag.
- Flags by Steam profile: paste a profile link (`/profiles/` or `/id/`) or a SteamID64 when you flag someone, or add a flag from Settings > Player flags. The flag then follows name changes. With a Steam Web API key the app reads the current name in one call, and when the player's game details are public it also sees which server they're on.
- Follow a player: when a followed player shows up on another server in your list, you get a sound, a notice on Servers with Join, and a Log line. "Join automatically when a followed player moves" is off by default, only uses servers in your list and the normal protected join, and always asks first while CS2 is open.
- Bans by a server stay on the account: when CS2's console shows a ban, the account in use gets "banned on 1 server" (it isn't a Steam ban) and while you're logged in with it, the server row shows "Banned here". Communities usually share one ban list, so the mark also counts on the other servers of the same network: same IP, or the same tag at the start of the name (like "EXAMPLE.GG" or "MY COMMUNITY"). Joining one of them with that account asks first, and Switch skips it there. You can also mark or clear it by hand (right-click the server or the account).
- Full server: Join on a full server offers to wait for a slot. The app checks it every 5 seconds and joins as soon as one opens (with the usual checks), or only plays a sound and shows a notice if you turn "Join automatically" off. Cancel any time.
- One switch: Switch (top bar, Accounts and Ctrl+Alt+Q, also in game) now changes the IP, logs into the next account by the rotation rules and joins the last server again, with the usual protection checks. Without a phone ready for Change IP it only changes the account. Ctrl+Alt+R still changes the IP and rejoins with the same account.
- You choose what Switch does (Settings > Switch, or the gear next to Switch in Accounts): change the IP, log in to the next account, rejoin the last server, open your "Open with CS2" programs. All on by default.
- Every command answers at once: a short sound when a hotkey or button is taken, the current step in the top bar (and a busy taskbar icon) while it runs, and a sound for done or failed at the end. A command pressed while another one runs says why it didn't start, and a question that needs you plays a sound, so you notice it in game too. Turn the sounds off in Settings > Switch. The sounds are from Kenney's "Interface Sounds" (CC0).
- Open with CS2 (Settings): programs you pick, like Discord, Spotify or a stats tracker, open when the app opens CS2. They start through Windows Explorer as you, never with the app's admin rights, with no extra arguments, and only if they aren't running yet.

### Fixed
- Change IP and rejoin got stuck after the IP change when Steam didn't log in again on the new IP (and the question it asked can't be seen in game). Now, if Steam isn't back within 20 seconds, the app restarts Steam through the phone and joins from CS2's menu.
- Join with CS2 closed opened the game but didn't join the server (CS2 drops a connect sent while its main menu loads). Now the app opens CS2, waits for the menu and then joins.
- The Switch buttons no longer go grey when no account is free: a click (or the hotkey) now says why nothing happened.
- Servers that hide their player list (they only give the count) show "hidden" next to the count instead of an empty list. A list seen in the last 2 minutes stays when a server skips one answer.

## [0.1.0-alpha.3] - 2026-10-27

Fixes from two security reviews of the protection code, plus kick detection, a range check after Change IP and in-app updates.

### Added
- Kicks and bans are spotted in CS2's console (`console.log`, written when CS2 starts with `-condebug`). You get a sound, a Log line and an offer to change the IP and rejoin the last server. If CS2 runs without `-condebug`, the app tells you once to add it to the launch options.
- The kick check knows which server CS2 is really on, also when you joined from the console, and it was tested against real CS2 console lines (a real `NETWORK_DISCONNECT_KICKBANADDED` ban).
- If the phone has USB tethering on but Windows doesn't see it (it happens after the cable flickers), the app tells you to replug the cable instead of only showing "no connection".
- Change IP and rejoin: a button on Connection and the Ctrl+Alt+R hotkey (works in game). It changes the IP, then joins the last server with the usual protection checks.
- After Change IP, the Log and the notice say whether the new IP is in a new /16 range or in the same /16 as an earlier IP. New option in Settings: "Keep changing until the IP is in a new /16" (off by default, uses the normal attempts).
- Update check: once a day the app looks at the GitHub releases and shows Update under the version when there's a newer one. It downloads `CS2IPChanger.exe`, checks it against the release's `.sha256` file and only then swaps it in and restarts. Without a matching hash nothing is installed. Can be turned off in Settings.

### Fixed
- A phone connection is only trusted after the app has measured its public IP and seen it isn't your home IP. Until then (at startup, after plugging the phone back in, after the phone's local IP changes) your servers and CS2 stay blocked and the screen says "Checking the phone's IP". A failed check never lifts a block.
- If the phone's own Wi-Fi is on, it may share your home network instead of mobile data. The app now blocks the servers and CS2 as soon as it sees that over ADB. During a match it checks every 2 seconds, and the public IP every 15 seconds instead of 90.
- Firewall rules are no longer deleted and recreated when the connection changes. The new rule goes in first and the old one comes out after, so there's no gap where a packet could leave with your home IP.
- The rule check now compares every field (direction, protocol, profiles, interfaces, program, destinations and the full source ranges). A rule edited by hand no longer counts as verified.
- Restore network now stays restored. Before, unplugging the phone afterwards put the rules back while the screen said protection was off. It also reads everything back and tells you if something was left.
- The routes the app adds are saved to `SRT_Dados/owned.json`, so after a crash or a kill from Task Manager the app still finds and removes them.
- Joining a server after Restore network is refused until you press Run leak test, and the screen can no longer go back to "Protected" on its own.
- With "Turn off protection when closing the app" on, closing the app while CS2 is open now asks first instead of sending CS2's traffic home mid-match.
- Only one copy of the app can run at a time (two would fight over the same rules).
- Change IP no longer says "New IP" when the phone came back with your home IP, and the connection stays blocked until the new IP is measured.
- After the phone's Wi-Fi turns off again, the app checks the phone's IP right away instead of waiting up to 90 seconds.
- A quick switch (Ctrl+Alt+Q) closing CS2 is no longer reported as a crash.
- New `--restore` option and `tools/restore.cmd`, to undo the firewall rules when the window won't open or the app is already deleted.
- netsh, PowerShell and the Firewall console always run from System32, and only an adb.exe signed by Google is started. Before, a file planted next to the exe could run with admin rights. .NET startup hooks are turned off for the same reason.

## [0.1.0-alpha.2] - 2026-10-27

### Changed
- Renamed from ServerRouteTool to CS2 IP Changer. The exe is now `CS2IPChanger.exe`.
- The whole interface is in English now, including logs, checks and tooltips.
- README, changelog and release notes rewritten in English.

### Upgrading
- Put `CS2IPChanger.exe` in the same folder as the old `ServerRouteTool.exe`. It reads the same `SRT_Dados` folder, so your servers, accounts and settings carry over. The firewall rules keep their old names, so nothing needs to be redone.

## [0.1.0-alpha.1] - 2026-10-27

First public release, as ServerRouteTool.

### Added
- Phone connection (USB tethering or Wi-Fi hotspot) with three modes: game only, whole PC while CS2 is open, whole PC always.
- Windows Firewall kill switch for the listed servers, and `cs2.exe` locked to the phone connection.
- Steam on the same connection as CS2, to avoid the "No Steam logon" kick.
- Live checks: firewall, kill switch, routes, IP different from home, phone on mobile data, CS2, Steam and the rest of the PC.
- Change IP over ADB (mobile data or airplane mode), with an IP history and a leak test.
- Server list with name, map, players and ping, favorites and one-click join. It starts empty.
- Steam account switcher with Ctrl+Alt+Q quick switch, bans (VAC, game ban, trade ban, limited account) and cooldowns.
- Restore network, to undo every route and rule in one click.
- Dark interface in Portuguese.

[Unreleased]: ../../compare/v0.1.0-beta.11...HEAD
[0.1.0-beta.11]: ../../releases/tag/v0.1.0-beta.11
[0.1.0-beta.10]: ../../releases/tag/v0.1.0-beta.10
[0.1.0-beta.9]: ../../releases/tag/v0.1.0-beta.9
[0.1.0-beta.8]: ../../releases/tag/v0.1.0-beta.8
[0.1.0-beta.7]: ../../releases/tag/v0.1.0-beta.7
[0.1.0-beta.6]: ../../releases/tag/v0.1.0-beta.6
[0.1.0-beta.5]: ../../releases/tag/v0.1.0-beta.5
[0.1.0-beta.4]: ../../releases/tag/v0.1.0-beta.4
[0.1.0-beta.3]: ../../releases/tag/v0.1.0-beta.3
[0.1.0-beta.2]: ../../releases/tag/v0.1.0-beta.2
[0.1.0-beta.1]: ../../releases/tag/v0.1.0-beta.1
[0.1.0-alpha.4]: ../../releases/tag/v0.1.0-alpha.4
[0.1.0-alpha.3]: ../../releases/tag/v0.1.0-alpha.3
[0.1.0-alpha.2]: ../../releases/tag/v0.1.0-alpha.2
[0.1.0-alpha.1]: ../../releases/tag/v0.1.0-alpha.1
