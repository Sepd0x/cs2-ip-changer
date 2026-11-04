## CS2 IP Changer 0.1.0-beta.10

Protection is now something you turn on, new Steam accounts show up without a restart, Phone setup checks each step by itself, and a new install keeps OBS, Discord and downloads at home.

### Protection you turn on
Protection now starts off every time the app opens. Turn it on with **Turn on protection** in Status or in the tray icon's menu. Once on, it stays on until you turn it off: a phone drop blocks your servers and CS2, it never turns protection off. Want it on by itself? **Settings > Protection > Turn on protection when the app starts**.

With protection off the app still checks the phone, so Status shows whether it is ready and Change IP works. Joining a server with protection off asks first and turns it on for you. An update made while it was on, or CS2 still open with the block from the last run, keeps it on at start, so a match isn't moved to your home IP.

### Game only for new installs
A new install starts in **Game only**: only CS2 and Steam use the phone, so OBS, Discord and downloads stay on your home connection. **Whole PC while CS2 is open** is one click away in Connection, and an install that already had it keeps it.

### Phone setup
Phone setup is now a list of steps that check themselves every 2 seconds: the phone tool, the USB cable, USB debugging, allowing this PC, USB tethering and the phone's Wi-Fi. Each step is green when it's done and red when it's next, with a button where the app can do it for you.

### Accounts, Configs, Profiles and Regions
- A new Steam account shows up on Accounts, Configs and Profiles without restarting the app.
- Steam names with symbols show as they are, and an account whose profile was never set up shows its name instead of a long number.
- **Configs** with many accounts: accounts with the same crosshair show as one line, with Show to see each one, and there is a search.
- **Regions** works without the phone: with protection off or in No phone mode, the ping is measured on your home connection, and the page says where it was measured.

### Also
- Change IP with "Keep changing until the connection is good": a retry for a jittery connection uses airplane mode, so the phone can land on another cell.
- Log: filter by warnings or errors, and search.

### Usage stats
The stats now also go out every 30 minutes while the app is open and before the PC sleeps, not only when it opens and closes. Before, an app left in the tray for hours was counted with a minute or two. Because of this, the usage stats notice shows once more. Nothing new is collected: never IPs, servers, accounts, names or anything you type. The full list is in [Telemetry](https://github.com/Sepd0x/cs2-ip-changer#telemetry).

### Upgrading
From beta.4 or later the app offers this update by itself and checks its signature first. Your settings carry over.

### Check the download
`CS2IPChanger.exe.sha256` has the exe's SHA-256 and `CS2IPChanger.exe.sha256.sig` its signature. The build attestation proves it came from this repo's workflow: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

VirusTotal: [0 of 68 engines](https://www.virustotal.com/gui/file/fae35fdfe338757fbed1813feb0d90be9552f5e3f09c7e1a6fe5b1f32bf3407b/detection) flag it.

Full list in `CHANGELOG.md`.
