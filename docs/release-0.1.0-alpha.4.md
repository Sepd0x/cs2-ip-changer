## CS2 IP Changer 0.1.0-alpha.4

This one is about the people on your servers and about getting back in fast after a kick or ban.

### New
- See who's on each server: click the player count in Servers for names, score and time played. Some communities hide their list and only give the count; the app shows "hidden" there.
- Flag players as Staff or with your own label and color. Paste a Steam profile link or SteamID64 and the flag follows name changes. Without a profile the flag matches anyone with that name, because servers only share names.
- Follow a player: you get a sound and a notice with Join when they show up on another server in your list. Joining them automatically is off by default and always asks while CS2 is open.
- Server bans stay on the account. When CS2's console shows a ban, that account gets marked for that server and for the rest of the same community (same IP or the same tag in the name). Switch skips it there, and joining with it asks first.
- Full server? Join offers to wait for a slot and gets you in when one opens.
- One Switch for everything: new IP, next account and back to the last server, from the top bar, Accounts or Ctrl+Alt+Q in game. In Settings > Switch you pick which of those steps it does. Ctrl+Alt+R stays as new IP with the same account.
- Every command now answers right away: a short sound when it's taken, the current step in the top bar while it runs, and a sound when it's done or failed. If you press something while another command is still running, it tells you why. Handy in game, where you can't see the window. The sounds can be turned off.
- Open with CS2: pick programs like Discord, Spotify or a stats tracker and they open when the app opens CS2. They start as you through Windows Explorer, never with the app's admin rights.

### What's fixed
- Join with CS2 closed now really joins: the app opens CS2, waits for the main menu and then connects.
- Change IP and rejoin no longer gets stuck when Steam doesn't log in again on the new IP. After 20 seconds the app restarts Steam through the phone and joins from the menu.

### Upgrading
Replace `CS2IPChanger.exe` with this one in the same folder and keep `SRT_Dados` next to it. Your servers, accounts and settings carry over, and Switch starts with every step on, like before.

### Heads-up
Playing over mobile data usually means a higher ping and a less stable connection than at home. Nothing here hides you from anti-cheat or from a server's staff; a ban on your Steam account follows the account, not the IP.

### Check the download
This exe was built by GitHub Actions from the tagged code. `CS2IPChanger.exe.sha256` next to it has its SHA-256, and the build attestation proves where it came from: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

Full list in `CHANGELOG.md`.
