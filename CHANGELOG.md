# Changelog

Notable changes to this project are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[Unreleased]: ../../compare/v0.1.0-beta.5...HEAD
[0.1.0-beta.5]: ../../releases/tag/v0.1.0-beta.5
[0.1.0-beta.4]: ../../releases/tag/v0.1.0-beta.4
[0.1.0-beta.3]: ../../releases/tag/v0.1.0-beta.3
[0.1.0-beta.2]: ../../releases/tag/v0.1.0-beta.2
[0.1.0-beta.1]: ../../releases/tag/v0.1.0-beta.1
[0.1.0-alpha.4]: ../../releases/tag/v0.1.0-alpha.4
[0.1.0-alpha.3]: ../../releases/tag/v0.1.0-alpha.3
[0.1.0-alpha.2]: ../../releases/tag/v0.1.0-alpha.2
[0.1.0-alpha.1]: ../../releases/tag/v0.1.0-alpha.1
