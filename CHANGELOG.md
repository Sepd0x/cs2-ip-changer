# Changelog

Notable changes to this project are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [0.1.0-alpha.4] - 2026-10-28

Players on your servers, flags, following a player, one switch for IP, account and rejoin that you can set up, sounds and progress for every command, and programs that open with CS2.

### Added
- Servers: click the player count to see who is on each server, with score and time played. The list comes from the server (A2S), through the phone like everything else.
- Player flags: right-click a player to flag them as Staff or with your own flag name and color. Flags stay on this PC (`SRT_Dados/flags.json`) and show next to the server name. Servers only share player names, never the SteamID, so a flag without a Steam profile matches anyone with that name. The app says so where you flag.
- Flags by Steam profile: paste a profile link (`/profiles/` or `/id/`) or a SteamID64 when you flag someone, or add a flag from Settings > Player flags. The flag then follows name changes. With a Steam Web API key the app reads the current name in one call, and when the player's game details are public it also sees which server they're on.
- Follow a player: when a followed player shows up on another server in your list, you get a sound, a notice on Servers with Join, and a Log line. "Join automatically when a followed player moves" is off by default, only uses servers in your list and the normal protected join, and always asks first while CS2 is open.
- Bans by a server stay on the account: when CS2's console shows a ban, the account in use gets "banned on 1 server" (it isn't a Steam ban) and while you're logged in with it, the server row shows "Banned here". Communities usually share one ban list, so the mark also counts on the other servers of the same network: same IP, or the same tag at the start of the name (like "WINDOH.GG" or "TUGA ARMY"). Joining one of them with that account asks first, and Switch skips it there. You can also mark or clear it by hand (right-click the server or the account).
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

[0.1.0-alpha.3]: ../../releases/tag/v0.1.0-alpha.3
[0.1.0-alpha.2]: ../../releases/tag/v0.1.0-alpha.2
[0.1.0-alpha.1]: ../../releases/tag/v0.1.0-alpha.1
