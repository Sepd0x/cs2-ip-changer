## CS2 IP Changer 0.1.0-beta.11

iPhone support, a new logo, kicks and bans seen however CS2 opens, an option to use the app with protection off, and a safer account switch.

### iPhone
Phone setup now has the iPhone's own steps: Apple's driver, the cable, Trust This Computer and Personal Hotspot. Change IP on an iPhone is guided: the app tells you when to turn Airplane Mode on and off, waits for the iPhone to come back and shows the new IP. This is new and not yet tested on many iPhones: if something doesn't work, open an issue.

### Kicks and bans, however CS2 opens
The app adds -condebug -conclearlog to CS2's launch options in Steam for every account on this PC, only while Steam is closed, and keeps what you already had there (with a backup). So a kick or a ban is seen even when you open CS2 from Steam. The ban notification names the account you rejoin with. To turn it off: **Settings > Steam > See kicks and bans when Steam opens CS2**.

### Use the app with protection off
**Settings > Protection > Use the app with protection off**: while protection is off, Servers, Join and Log in with CS2 work on your home connection and the app blocks nothing. Servers then see your home IP, so it asks you first. Off by default.

New IP + next account follows protection: with protection off it no longer changes the IP for nothing. With the option on it only switches the account; otherwise it asks to turn protection on first. After a ban it always asks, because your home IP is the banned one. **Cancel** in the top bar stops it after the step that is running.

### Phone setup and Status
- Turn on protection with USB tethering off now turns tethering on by itself (and the phone's Wi-Fi off first). If it can't, Phone setup opens on what's missing.
- Phone setup shows one step at a time, with the menus of your phone's brand.
- Checks only show what needs you, under one line ("All 11 checks OK."). Show all brings every row back.
- "IP servers see" keeps the last IP, faint, when a check fails, instead of "—".
- Log > What the app changed lists the Firewall rules, routes, folder and task the app made, with the command that undoes them.

### Fixes
- The account switch no longer goes ahead while Steam is still closing, which could leave Steam on the old account or asking for the password.
- Closing a question with X or Esc never joins a server.
- The New IP + next account button and the switch now pick the same account.
- Join automatically when a followed player moves only offers the join, and only while CS2 is open.
- Change IP on the phone's hotspot never turns on airplane mode, which dropped the hotspot on some phones.
- A Refresh on Accounts that Steam rate-limits keeps each account's bans instead of clearing them.
- After "Server is full", the retry stops as soon as you join another server.
- The update check works on mobile data again (GitHub limits its API per IP), and never offers a release that was taken back.
- Install no longer copies old programs from SRT_Dados; it offers to recycle the old folder afterwards.

### A new logo and website
A new logo in the app, the website and the README. The website tells the route from your PC through the phone to the server as you scroll, with a page for iPhone users, and on a phone it has its own layout: the app's pages side by side at a size you can read, and a tap opens any picture with zoom. https://sepd0x.github.io/cs2-ip-changer/

### Usage stats notice
The notice at the first start is shorter: what is sent and never sent in two lines, who keeps the stats, why and for how long, with the full list one click away. Because its text changed, it shows once more.

### Usage stats
Nothing new about you is collected. The stats now count the Phone setup step you were at when you closed it, to see where people get stuck. Rows with an install ID are deleted after 12 months. The full list is in [Telemetry](https://github.com/Sepd0x/cs2-ip-changer#telemetry).

### Upgrading
From beta.4 or later the app offers this update by itself and checks its signature first. Your settings carry over.

### Check the download
`CS2IPChanger.exe.sha256` has the exe's SHA-256 and `CS2IPChanger.exe.sha256.sig` its signature. The build attestation proves it came from this repo's workflow: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

VirusTotal: (filled in after the release).

Full list in `CHANGELOG.md`.
