## CS2 IP Changer 0.1.0-beta.4

Signed updates, safer ADB, a recording build for videos, and anonymous usage stats while the app is in beta.

### New
- **Anonymous usage stats** while in beta: a random install ID, versions, the mode, minutes open, how often each button is used, and errors as type and place in the code. Never IPs, servers, accounts, names or anything you typed. The server keeps no IP. What is sent: README > Telemetry.
- **Recording build** (`CS2IPChanger-Recording.exe`, built from the source) for videos and streams: IPs, names, accounts and anything personal are blurred before they reach the screen.

### Security
- **Updates are signed.** The app only installs an update whose `.sha256` is signed with the project's key, which isn't on GitHub. Access to the repo or its releases isn't enough to push an exe to your PC.
- ADB runs only from a folder only admins can change (`%ProgramData%\CS2IPChanger\adb`), with Google's signature checked.
- The updater keeps the new exe locked until it has started, so nothing can swap it in between.
- No browser or Explorer window is started as admin.
- Export diagnostics also leaves out logins, Steam names, SteamIDs, e-mails, Wi-Fi names and your Windows user.

### Upgrading
If you have beta.3, the app offers this update by itself. From here on it checks the signature first.

### Check the download
`CS2IPChanger.exe.sha256` has the exe's SHA-256 and `CS2IPChanger.exe.sha256.sig` its signature. The build attestation proves it came from this repo's workflow: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

Full list in `CHANGELOG.md`.
