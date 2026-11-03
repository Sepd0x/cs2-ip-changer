## CS2 IP Changer 0.1.0-beta.9

The app no longer shuts CS2 out of Valve's servers when the phone isn't there yet, accounts can line up as rows, Join works on servers the list calls full, and the usage stats say more about what in the app works.

### Waiting for the phone instead of blocking CS2
Started before the phone is connected (with Start with Windows, say), the app used to lock CS2 to a phone that wasn't there, so even Valve's servers didn't work. Now each start waits: until the phone is connected and its IP checked, only the servers on your Servers page are blocked and the rest of CS2 uses your home network. Then protection turns on fully by itself. If CS2 was already open, the rest of CS2 follows when you close it, so a match isn't cut. **Change IP** or **Run leak test** turns it on right away. Once on, a phone drop blocks everything as before.

An update now keeps protection as it was: still waiting for the phone, or off if you had turned it off.

### Accounts
- **Rows** instead of cards (the button next to the order): one line per account, so bans, cooldowns and who is in rotation line up. From 12 accounts the page offers it once.
- A **Ready** filter and a **Ready first** order: ready, then the cooldown that ends first, banned last.

### Servers
- A marked server can be unmarked (click it again, click the empty part of the list or press Escape). The button says what it asks: Refresh all, Refresh 1 or Refresh 3.
- **Join anyway** next to Wait for a slot on a server the list shows as full: many servers keep spectator or reserved slots the list doesn't count.
- Rejoin turned away with "server is full" right after you tried tries again every 15 seconds for 2 minutes.

### Also
- **Log > Report a problem** opens a GitHub issue with the app and Windows versions filled in. Nothing is sent until you press Submit there.
- The first start checks that Steam and CS2 are on the PC.
- When the phone's USB connection drops 3 times in 10 minutes, the app says it's most likely the cable or the USB port.
- On the Connection page both columns now end on one line.

### Usage stats
The ID is now kept for the install instead of new every day, so the stats can tell people who come back from new installs. It is still random and has no name, and **Settings > Delete my stats** deletes everything the server keeps under it. Because of this change, if you accepted the notice in beta.8 it shows once more, saying what changed.

The stats also say a bit more, all in rough bands or counts: minutes on each screen, where people click (a coarse grid, added up for everyone with no ID), how matches went (ping, jitter, lag, never the server), crashes, how IP changes went and which Checks rows are red. A separate daily setup report has no ID at all. Never IPs, servers, accounts, names or anything you type. The full list is in [Telemetry](https://github.com/Sepd0x/cs2-ip-changer#telemetry).

### Upgrading
From beta.4 or later the app offers this update by itself and checks its signature first. Your settings carry over.

### Check the download
`CS2IPChanger.exe.sha256` has the exe's SHA-256 and `CS2IPChanger.exe.sha256.sig` its signature. The build attestation proves it came from this repo's workflow: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

VirusTotal: [0 of 69 engines](https://www.virustotal.com/gui/file/441e9ae0c8def2de90c83cfdec3c531a963a8370d5418a5ae0c7f43887bd6517/detection) flag it.

Full list in `CHANGELOG.md`.
