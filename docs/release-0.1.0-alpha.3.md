## CS2 IP Changer 0.1.0-alpha.3

Mostly a security fix release. I went through the protection code twice looking for ways your home IP could still leak, and this fixes everything serious that turned up. There are also a few new things.

### New
- Kicked or banned? The app notices it in CS2's console, plays a sound and offers to change your IP and rejoin. This needs `-condebug` in CS2's launch options (the app adds it when it opens CS2 itself, and reminds you if it's missing).
- Change IP and rejoin, as a button on Connection or with Ctrl+Alt+R in game.
- After a Change IP you see if the new IP is in a new /16 range. Admins often ban whole ranges, so there's an option to keep changing until the range is new.
- The app checks GitHub once a day and offers an Update button. It only installs the new exe if its SHA-256 matches the one published with the release.

### What's fixed
- A phone connection is only trusted after the app has checked its public IP. At startup or after replugging the phone you'll see "Checking the phone's IP" for a second or two while everything stays blocked.
- If the phone's own Wi-Fi turns on, it can share your home network instead of mobile data. The app now blocks your servers and CS2 the moment it sees that, instead of waiting up to 90 seconds for the next IP check. In a match it checks every 2 seconds.
- Firewall rules no longer have a split second without a block when your connection changes.
- A firewall rule edited by hand no longer passes as verified.
- Restore network stays restored, checks that everything is gone, and survives a crash or a kill from Task Manager.
- New `CS2IPChanger.exe --restore` and `tools/restore.cmd` (in the repo) to clean up when the window won't open or the app is already deleted.
- No joining after Restore network until you press Run leak test, and closing the app mid-match no longer drops protection without asking.
- Only one copy of the app can run at a time.
- The app no longer runs a netsh, PowerShell or adb.exe that someone planted next to it.

### Upgrading
Replace `CS2IPChanger.exe` with this one in the same folder. Since it runs as admin, a folder like `C:\Program Files\CS2 IP Changer` is safer than Downloads; move `SRT_Dados` along with it to keep your settings.

### Heads-up
Playing over mobile data usually means a higher ping and a less stable connection than at home, so the game can feel laggier. It depends on your carrier and signal.

### Check the download
This exe was built by GitHub Actions from the tagged code. `CS2IPChanger.exe.sha256` next to it has its SHA-256, and the build attestation proves where it came from: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

Full list in `CHANGELOG.md`.
