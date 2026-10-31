## CS2 IP Changer 0.1.0-beta.6

A reliability release: your settings can't be wiped any more, trying the app without a phone no longer leaves CS2 blocked, and the log stops growing forever.

### Fixed
- **Trying the app without a phone** and closing it left CS2 unable to connect at home. CS2 is now only locked to the phone once a phone connection has been checked on that PC.
- **Settings, accounts and lists could go back to the defaults** when an antivirus or OneDrive locked a file for a moment. A locked file is now tried again and never saved over. Saves are flushed to the disk first, the file before is kept as `.prev`, and a damaged file is replaced by that copy.
- **Local addresses in the server list** (your router, `192.168.x.x`, `10.x.x.x`) could cut the PC's DNS through the kill switch. They are refused now, and old lists lose them with a line in the Log.
- **Configs** keeps each account's original config (from before the app changed it) for good, marked Original in Backups.
- **Summer time ending** made the app forget its own routes. It now keeps that in UTC, and a clock that was once ahead no longer stops the update check.
- **A line repeating every few seconds** no longer fills the log: it is written once with a count.
- **adb** started by the app is stopped when the app closes.

### New
- `crash.txt` keeps the details of errors for bug reports, and the Log says when the last session didn't close normally.
- The log keeps about 14 days, and Settings > Maintenance > Delete log files deletes it now (it holds your home IP, Steam names and servers).
- Usage stats now include your country (two letters, worked out by Cloudflare; the IP is still never read or stored).

### Upgrading
If you have beta.4 or beta.5, the app offers this update by itself and checks its signature first. Your settings carry over.

### Check the download
`CS2IPChanger.exe.sha256` has the exe's SHA-256 and `CS2IPChanger.exe.sha256.sig` its signature. The build attestation proves it came from this repo's workflow: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

VirusTotal: [2 of 70 engines](https://www.virustotal.com/gui/file/8f8f4197b589dd80d6826a703af863c1684949f610e6acf7805102c3860314a2/detection) (Google and Varist, generic "risk" labels); Microsoft, Kaspersky, ESET, Bitdefender and Avast find nothing.

Full list in `CHANGELOG.md`.
