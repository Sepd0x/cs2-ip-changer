## CS2 IP Changer 0.1.0-beta.3

Fixes from the beta.2 tests on a second PC: joins that got stuck, bans that weren't marked, the phone's Wi-Fi cutting connections, and a connection quality check.

### New
- Connection quality: every 30 s the app pings through the phone and shows ping, jitter and loss in Checks (Good, Fair or Weak), and warns you when it turns weak.
- Pick the mode by connection quality (Settings > Protection, on by default): with a weak connection, Whole PC drops to Game only so CS2 doesn't share the phone with the rest of the PC.
- A server that stops answering while you join or play (how a ban at the server's firewall looks) is reported, with Change IP and rejoin as the way out.

### What's fixed
- The phone's Wi-Fi turning on for a second to scan no longer moves Steam and the PC off the phone and back, which cut every open connection.
- A join that got stuck no longer locks the app: a new command stops one that is only waiting.
- Bans that weren't marked: the strongest line of a disconnect counts, a kick before you get into the game counts as a ban, and a ban on a server outside your list is marked by its address.
- Going back to the menu or to a Valve server now counts as leaving the community server, so a later kick isn't put on the wrong one.
- `NETWORK_DISCONNECT_STEAM_BANNED` counts as a server ban: the account is marked and the app offers a new IP with another account.
- Many smaller fixes around Steam restarts, rejoin questions that stayed open and mobile data that stayed off.

### Upgrading
If you have beta.2, the app offers this update by itself. Otherwise replace `CS2IPChanger.exe` in the same folder and keep `SRT_Dados` next to it.

Full list in `CHANGELOG.md`.
