## CS2 IP Changer 0.1.0-beta.5

Three new pages (Regions, Configs, Profiles), a Phone setup that does the work, a No phone mode, and the rest of your PC finally stays off the phone.

### New
- **Regions:** pick where CS2 matchmaking can put you (Premier, Competitive, Casual, Deathmatch, Wingman). Turned-off regions are blocked with one Windows Firewall rule for `cs2.exe` only, and only on Valve's relays, so Steam, friends and community servers keep working. Only this region, Closest 3, your continent, Allow all or Ping under X ms.
- **Configs:** copy your CS2 config between your accounts (binds, crosshair, game settings, video, HUD), or keep them in sync from a main account. Never while CS2 is open, always with a backup first. Snapshots, Compare with a crosshair preview, and Undo last sync.
- **Profiles:** change name, picture, bio, country, privacy and custom URL on the accounts you pick, from templates. Sign in like Steam's login page (password with Steam Guard, or a QR code). Copy a look from any Steam profile link. Restore originals puts each profile back.
- **Phone setup** now does the steps itself: the cable, the USB driver, USB debugging, tethering, the phone's Wi-Fi and the rest of the PC, and tells you the one thing left when it can't.
- **No phone mode:** play on your home connection when the phone isn't with you. Not protected (the page says so), but Accounts, Configs, Profiles and Regions still work.

### Fixed
- **The rest of the PC no longer goes through the phone** in Game only and No phone. A high metric wasn't enough: Windows kept the phone anyway. The phone's connection now ignores its default route while you have a home network, and gets it back in Whole PC, without a home network and when the app closes. The game, Steam and the IP test keep their own routes through the phone.
- **Account switching** works with today's Steam client: it used to land on the account Steam already had and still say "Switched". The app now checks which account Steam really logged in to.
- False "Phone Wi-Fi is on" alarms with CS2 open, and Log lines that repeated every few seconds.

### Security
- WPF's files are unpacked to a folder only admins can change, and the app warns when it runs as admin from a folder any program can change (like Downloads).
- Profiles only talks to Steam over HTTPS, sends session cookies only to steamcommunity.com, and never writes sign-in tokens to the Log or the bug report.

### Upgrading
If you have beta.4, the app offers this update by itself and checks its signature first. Your settings carry over; Configs sync starts off and no region is blocked until you choose.

### Check the download
`CS2IPChanger.exe.sha256` has the exe's SHA-256 and `CS2IPChanger.exe.sha256.sig` its signature. The build attestation proves it came from this repo's workflow: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

Full list in `CHANGELOG.md`.
