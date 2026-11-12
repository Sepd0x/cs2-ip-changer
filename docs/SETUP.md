# CS2 IP Changer: setup in detail

The [Quick start](../README.md#quick-start) covers the usual case. This page has the details, and what to do when something goes wrong. A full walkthrough with pictures is on the website: [Change IP with your phone](https://sepd0x.github.io/cs2-ip-changer/change-ip-with-phone/).

- [Where to put the exe](#where-to-put-the-exe)
- [The phone, the firewall, ADB, launch options, hotkeys and uninstalling](#the-phone-the-firewall-adb-launch-options-hotkeys-and-uninstalling)
- [If something goes wrong](#if-something-goes-wrong)

## Where to put the exe

It runs as admin, so it only starts from a folder that programs without admin rights can't change. A file put next to the exe in Downloads, on the Desktop or in a folder you made yourself would run with admin rights, so from there the app shows what to do and closes (`--restore` still works). Started from Downloads or the Desktop, it opens a window that offers to install it in `C:\Program Files\CS2 IP Changer` (Install copies the exe and your `SRT_Dados` and starts it from there; nothing is moved).

Already used it from another folder? Close it and copy `CS2IPChanger.exe` together with its `SRT_Dados` folder to the new one: copying, not moving, gives the folder the new place's permissions.

## The phone, the firewall, ADB, launch options, hotkeys and uninstalling

- **The phone's Wi-Fi.** It has to be off, so the phone shares mobile data and not your home Wi-Fi. While it's on, the app keeps your servers and CS2 blocked.
- **Windows Firewall has to be on.** Third-party firewalls that turn it off (some antivirus suites) leave the app without a kill switch, and it says so.
- **An iPhone.** Turn on Personal Hotspot and plug it in by USB (Windows needs Apple's driver, which comes with Apple's Devices app), or join its Wi-Fi hotspot. Protection works the same as on Android. For a new IP you switch Airplane Mode on and off on the iPhone: only Android lets a PC do it.
- **A VPN on the PC.** Turn it off before you turn on protection. The app hasn't been tested alongside one.
- **Your server's address.** Servers takes the `IP:port` of the servers you play on. In CS2's console, `status` shows it for the server you're on.
- **The cable.** It has to be a data cable. The one that came with the phone is best: many cheap cables only charge, and then the PC sees nothing at all. Phone setup shows that it is looking for the phone every 2 seconds and, after 20 seconds with nothing, what to try next.
- **Change IP from the PC.** Also turn on USB debugging on the phone. Phone setup draws the three steps for Samsung, Xiaomi, Pixel and other phones, and turns green by itself when it's on. If ADB isn't installed, Phone setup downloads Google's platform-tools (about 7 MB) as soon as you open it, with a progress bar; on the phone's mobile data it waits for you to press Install. If the download keeps failing, download "SDK Platform-Tools for Windows" from developer.android.com and unzip it into the app's data folder (Settings > Maintenance > Open data folder): the app finds it there.
- **The tour.** The first start shows a short tour of the app. Settings > App > Show the tour again opens it again.
- **Launch options.** To spot kicks and bans, CS2 needs `-condebug -conclearlog` (the second one starts `console.log` fresh each time, so it doesn't grow forever). The app adds both when it opens CS2, and tells you if they're missing when you start the game from Steam.
- **Hotkeys.** <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F9</kbd> for New IP + next account and <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F10</kbd> for Change IP and rejoin. Change them in Settings > Hotkeys and sounds. The app won't take Ctrl+Alt combinations: on many keyboards that's AltGr, which types @ and €.
- **No phone with you?** Pick "No phone: play on your home connection" under What goes through the phone. Nothing is blocked, servers see your home IP, and the app says so. Until a phone has been connected once, CS2 isn't locked, so trying the app without a phone never leaves CS2 unable to connect at home.
- **Hotspot or a 4G/5G dongle** work too: pick them under Connection > How the PC connects to the phone (see the [FAQ](FAQ.md#phone-and-connection)).
- **Portable.** Settings, servers and accounts are saved in `SRT_Dados` next to the exe. To uninstall, use Uninstall in Windows Settings > Installed apps, or **Remove CS2 IP Changer** at the bottom of Settings: it removes the rules and routes (the region block too), the Start with Windows task, `C:\ProgramData\CS2IPChanger` and the app, and you pick whether `SRT_Dados` (settings and accounts) stays. Without the window, `CS2IPChanger.exe --restore` removes the rules, routes and task; then delete the folder and `C:\ProgramData\CS2IPChanger`.

## If something goes wrong

The firewall rules stay in place when you close the app, on purpose, so your IP can't leak while it's closed. To undo everything:

- **In the app:** Turn off protection, on Connection or in the icon's menu next to the clock. It removes the routes and rules and checks that they're really gone. Protection stays off until you press Turn on protection. Regions you turned off stay off: Allow all (block off) on Regions lifts them.
- **If the window won't open:** run `CS2IPChanger.exe --restore`. It removes everything, the region block too.
- **If you already deleted the app:** open [tools/restore.cmd](../tools/restore.cmd), click Download raw file, right-click the file and pick Run as administrator, then restart the PC.

Lag, or programs opened with CS2 losing their connection? Look at Connection quality in Checks. If it says Weak, the phone connection is the problem: see [Will my ping be worse?](FAQ.md#the-basics). In Whole PC mode, programs opened with CS2 go through the phone too, so a new IP cuts their connections: use Game only for them.

Found a bug? [Open an issue](https://github.com/Sepd0x/cs2-ip-changer/issues/new/choose) with the version (bottom left of the window) and the diagnostics (Log page > Save bug report).

### What is in the bug report

Save bug report saves a .zip on your Desktop with the log, the app's rules and routes, Steam's connection log, CS2's console and the details of any error the app caught. Your current and last home IP, API key, logins, Steam and player names, SteamIDs, Wi-Fi names, emails and your Windows user are hidden in it, but look through it before you post (older log lines can still hold an older home IP). If you paste the log instead (Copy all), blank out your IPs first.
