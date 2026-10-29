## CS2 IP Changer 0.1.0-beta.2

Fixes from the first beta.1 tests.

### New
- Open with CS2: an "As admin" switch for each program. Windows asks first (UAC) and the program gets its own admin rights, never the app's.

### What changed
- After a ban, the app offers a new IP **and** the next account the server (or its community) hasn't banned, then rejoins. A ban is on the IP and the account, so a new IP with the same account only got refused again. The rejoin hotkey does the same.
- "CS2 only through the phone" now covers CS2's game traffic (UDP). CS2's web requests and programs loaded into the game use the normal connection again; in Game only mode they had no way out.

### What's fixed
- A Steam account added or logged in outside the app now shows up within a few seconds and becomes the account in use. Before, ban marks and Switch could act on the wrong account until a restart.
- Player lists that showed up and then turned "hidden" on the next refresh.

### Upgrading
Replace `CS2IPChanger.exe` with this one in the same folder and keep `SRT_Dados` next to it.

Full list in `CHANGELOG.md`.
