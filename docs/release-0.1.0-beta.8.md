## CS2 IP Changer 0.1.0-beta.8

The app now installs itself in a safe place, tells a kick from a ban, can live next to the clock, and closes the holes a full security review found.

### Install from Downloads
Started from Downloads, the Desktop or any folder that programs without admin rights can change, the app shows an **Install** button. It copies `CS2IPChanger.exe` and your `SRT_Dados` folder to `C:\Program Files\CS2 IP Changer` and starts from there. Nothing is moved, an older version never replaces a newer one, and you can delete the Downloads copy afterwards.

### Anonymous usage stats, required to use the app
At the first start a notice lists what is sent when the app opens and closes, with **Accept** and **Quit**. Nothing is sent before you accept, and Quit closes the app without changing anything. The ID is random and new every day, so one day can't be linked to the next. Never IPs, servers, accounts, Steam names or IDs, or anything you type.

### Kick or ban, told apart
On community servers the app says why CS2 left: a ban (with its length when the server says it), a VPN block, a kick, a full server, a timeout, a map change or you leaving. Only a ban or a VPN block offers **New IP + next account**; a kick offers a rejoin. Valve servers are left alone. Needs `-condebug`, as before.

### In the background
- **Icon next to the clock** with the protection state and a dark menu: New IP + next account, Change IP, Regions, Turn off protection, Open, Quit.
- In Settings > Background and data, all off by default: **Close to the notification area**, **Start with Windows** (installed copy, no UAC prompt) and a **mobile data warning** at the GB a month you pick. The Phone box shows the data used this session and this month.
- Opening the app while it runs brings its window back.

### Uninstall in one step
The installed app is listed in Windows Settings > Installed apps. Uninstall there, or **Remove CS2 IP Changer** at the bottom of Settings, takes out the rules, routes, the task, `C:\ProgramData\CS2IPChanger` and the app itself. You pick whether your settings and accounts stay. No files left to delete by hand.

### Lag on mobile data
- A **CS2 network report** in the Log after each server: ping, jitter, lost packets and the IP the match used. Needs `-condebug`.
- **Keep changing until the connection is good** (Settings > Change IP, off by default).
- Tips after a laggy match: USB instead of the hotspot, battery saver off, 5G, phone away from the PC.

### Security
- Saved Profiles sign-ins and the Steam Web API key are now readable only by admins or only by your Windows user.
- Files the app writes as admin (configs, reports, zips) can't be redirected through a link, and logs are never read through one.
- Open Windows Firewall can't be hijacked to run another program as admin, and the app never opens CS2's memory.
- Updates only accept the exact checksum line the release workflow writes.
- Limit: .NET itself looks next to the exe for some Windows DLLs, and the app can't change that. Use Install instead of running from Downloads.

What the app sends, what it does as admin and why an antivirus may flag it: [Privacy and security](https://github.com/Sepd0x/cs2-ip-changer/blob/main/docs/PRIVACY-AND-SECURITY.md).

### Also
- Hide usernames turns on by itself while OBS or Streamlabs is open.
- Copy CS2 friend code in each account's menu.
- If an antivirus turned Windows Firewall off, the app says which one.
- Turn on USB tethering no longer resets a phone that already has it on.
- Settings is tidier: what you change most first, maintenance buttons in a grid, Remove last and on its own.

### Upgrading
From beta.4 or later the app offers this update by itself and checks its signature first. Your settings carry over. You'll see the usage stats notice once.

### Check the download
`CS2IPChanger.exe.sha256` has the exe's SHA-256 and `CS2IPChanger.exe.sha256.sig` its signature. The build attestation proves it came from this repo's workflow: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

VirusTotal: [0 of 69 engines](https://www.virustotal.com/gui/file/b5de48fe17e48e8c38fb30e10aca2c193b0423ca43b4a765018cc178ab884490/detection) flag it.

Full list in `CHANGELOG.md`.
