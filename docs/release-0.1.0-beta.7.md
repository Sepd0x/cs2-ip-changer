## CS2 IP Changer 0.1.0-beta.7

A security and clarity release: the app is harder to abuse from the folder it runs in, ipwho.is can no longer tie your phone's IP to your home IP, and the app is easier to read the first time you open it.

### Where the app runs from
Run as admin, the app no longer starts from Downloads, the Desktop or another folder that programs without admin rights can change. A file put next to the exe there would run with admin rights. Copy `CS2IPChanger.exe` to `C:\Program Files\CS2 IP Changer` and start it from there; the app tells you this if you start it from the wrong place. `--restore` still works from anywhere.

### Security
- Windows DLLs the app loads by name now only come from System32, never from the exe's folder, the current folder or PATH.
- .NET's runtime variables (`CORECLR_PROFILER` and the like) in your user environment no longer reach the app. They can load a DLL into an app run as admin. The Log says when the app had to drop them.
- Files in `SRT_Dados` are never written, moved or deleted through a link, and the ADB download is unpacked with the same check.
- `owned.json` (the routes the app added) is only read from a folder only admins can change, so a forged file can't make the app delete other routes.
- The `%ProgramData%\CS2IPChanger` folders are created admin-only in one step.
- ipwho.is is asked about each IP only through that IP's own connection. Before, both questions went through home, so ipwho.is could link the phone's IP to your home IP.

### Clearer for new users
- Until a phone has been connected once, Status says **Not protected yet** and Phone setup is the one button to press.
- Switch is now **New IP + the account** it logs in to ("New IP + atlas"), because it changes the IP first.
- Accounts: an **In rotation** tick on each card replaces the arrows.
- **Turn off protection** is in the Status buttons while protection is on, and closing the app with protection on asks once whether to keep it or turn it off.
- Connection > Checks puts the three rows that decide whether you are protected first, with plainer names, and a new row says how many matchmaking regions CS2 can play in.
- While the IP changes you see each step: the attempt, mobile data off and back on, and the new IP in green.
- Status markers have a shape as well as a colour, so they read without telling green from amber.

### Fixed
- The Protected line in Status said only the servers on your Servers page saw the phone's IP. With Whole PC while CS2 is open, every server does; the line now follows the mode you picked.
- Checks > CS2 only through the phone showed a green square next to "Not locked yet". It now waits until CS2 is really locked to the phone.

### Upgrading
If you have beta.4 or later, the app offers this update by itself and checks its signature first. Your settings carry over.

If your copy runs from Downloads, the Desktop or another folder like them, the updated app won't start there and shows what to do: create `C:\Program Files\CS2 IP Changer`, copy `CS2IPChanger.exe` and your `SRT_Dados` folder into it (copy, don't move) and start it from there. Your settings, accounts and lists are in `SRT_Dados`.

### Check the download
`CS2IPChanger.exe.sha256` has the exe's SHA-256 and `CS2IPChanger.exe.sha256.sig` its signature. The build attestation proves it came from this repo's workflow: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

Full list in `CHANGELOG.md`.
