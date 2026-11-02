# Privacy and security

What CS2 IP Changer sends where, what it does with admin rights, and what it can't protect you from. This page describes the code in this repo; if they ever disagree, the code is right and this page is a bug ([open an issue](https://github.com/Sepd0x/cs2-ip-changer/issues)).

No software is risk-free, and this app runs as admin. Read this, [check your download](../README.md#check-your-download), and decide for yourself.

## What leaves your PC, and to whom

There's no account and no server of ours that sees your IPs. The app talks to these services, all over HTTPS with normal certificate checks (the app never turns them off):

| Service | When | What it receives |
|---|---|---|
| api.ipify.org, ipv4.icanhazip.com, checkip.amazonaws.com | Measuring your home IP | Your home IP (it's where the request comes from) |
| 1.0.0.1 (Cloudflare, `/cdn-cgi/trace`) | Measuring the phone's IP | The phone's IP. A firewall rule keeps the app's requests to 1.0.0.1 on the phone connection |
| ipwho.is | Showing the carrier and city of an IP | **The IP being looked up**: your home IP, and the phone's IP. Each one is asked only through its own connection (home through home, phone through phone), so ipwho.is never sees both coming from one place. A home IP the app only remembers (not measured just now) is never sent |
| api.steampowered.com | Regions (Valve's relay list, no key); bans and profiles if you add a Steam Web API key; Profiles sign-in | The key in the request, if you added one; your Steam login and the password encrypted with Steam's own key, only when you sign in on the Profiles page |
| steamcommunity.com, login.steampowered.com, Steam's picture servers (`*.steamstatic.com`) | Account profiles, avatars and bans; profile changes on the Profiles page | Public profile requests; for accounts you signed in to on Profiles, that account's Steam session |
| dl.google.com | Only if you ask the app to download ADB | A normal download |
| api.github.com, github.com | Update check, once a day (can be turned off) | A normal request; release files come from GitHub's download servers |
| stats.cs2ipchanger.workers.dev (Cloudflare Worker, source in [`worker/stats`](../worker/stats)) | Anonymous usage stats, required in the beta, after you accept | What's listed in [Telemetry](../README.md#telemetry). Never IPs, servers, accounts or names |

Pings and A2S queries go to the game servers in your list and to Valve's relays, through the phone (through home in No phone mode). Links like Leetify or csstats.gg open in your browser; the app itself doesn't contact them.

DNS: names like `ipwho.is` are looked up by Windows' normal DNS, which is your home router or provider unless you're in Whole PC mode. Your provider can see that a lookup of `ipwho.is` happened, not the phone's IP (that goes inside HTTPS).

## What stays on your PC

Everything is in `SRT_Dados` next to the exe, a folder only admins can change:
- settings, servers, accounts list, ban marks, flags, logs (they hold your home IP, Steam names and servers; Settings > Delete log files removes them);
- the Steam Web API key, encrypted with Windows DPAPI for your Windows user;
- Profiles sign-ins: a Steam sign-in token per account (never the password), encrypted with DPAPI, in a folder only Administrators and SYSTEM can open, so a program of yours without admin rights can't read it;
- config backups and snapshots.

`C:\ProgramData\CS2IPChanger` holds the copy of ADB the app runs, the Windows files the exe unpacks, and nothing personal.

Save bug report writes a `.zip` to your Desktop and sends nothing. It leaves out your current and last home IP, the API key, logins, names, SteamIDs, e-mails, Wi-Fi names and your Windows user. Older log lines can still hold an older home IP: look through the zip before you post it.

## Why it needs admin, and what it does with it

Routes and firewall rules are system settings that only admins can change. Because it runs as admin, the app:
- only starts from a folder that programs without admin rights can't change (`C:\Program Files\CS2 IP Changer`); started from Downloads it offers to install itself there and doesn't run;
- loads Windows DLLs only from System32, and runs `netsh`, `powershell`, `schtasks` and `mmc` only from System32;
- copies ADB to an admin-only folder and runs it only if Google's signature checks out;
- writes files in folders other programs can change (Steam's `userdata`, your Desktop) by handle, checking where each file really is, so a folder turned into a link while the app writes can't send the write or a delete somewhere else (a Desktop that already is a link is followed, as Windows does: the bug report zip goes where it points);
- opens programs you add to Open with CS2, links and Explorer as you, never with its admin rights.

## Why an antivirus may flag it

Heuristics look at what a program can do, not why. Every sensitive thing the app does, and what for:

| What it uses | What for |
|---|---|
| Windows Firewall (COM `HNetCfg.FwPolicy2`) | The kill switch, the CS2 lock and the Regions block: outbound rules named `ServerRouteTool v9 - ...` |
| `netsh interface ipv4 ... route` | Routes that send your servers (or the whole PC) through the phone |
| PowerShell `NetTCPIP\Set-NetIPInterface` (module loaded by full path) | Keeps the rest of the PC off the phone's mobile data, and undoes it |
| `GetExtendedTcpTable`, `SetTcpEntry` | Closes Steam's own connections so Steam reconnects through the phone after an IP change. Steam only, never CS2 |
| `IcmpSendEcho2Ex` | Pings to Valve's relays and servers |
| `OpenProcess` with `PROCESS_QUERY_LIMITED_INFORMATION` only | Finds where `cs2.exe` is installed. The app never reads or writes CS2's memory and never injects anything |
| `Process.Kill` on `cs2.exe`, `steam.exe` | Closes CS2 or Steam when you switch account or restart Steam through the phone. It asks first |
| Registry `HKCU\Software\Valve\Steam` (`AutoLoginUser`, `RememberPassword`) | Switches the Steam account, the same keys Steam's login screen sets |
| `schtasks` (scheduled task "CS2 IP Changer") | Start with Windows, only if you turn it on and only for the installed copy |
| `powershell.exe` (System32), once, after Remove | Waits for the app to close, then deletes the files it still used, one by one (never a folder with what is in it) |
| `MoveFileEx` (delete at restart) | Remove: only a file Windows still holds after that is deleted at the next restart |
| Registry `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\CS2IPChanger` | The installed copy's entry in Windows Settings > Installed apps. Remove deletes it |
| `Shell_NotifyIcon` | The icon next to the clock |
| `RegisterHotKey` | Your hotkeys. Not a keyboard hook and not a keylogger |
| WMI `root\SecurityCenter2` (read) | Only when Windows Firewall is off: which antivirus firewall took over, to tell you |
| `WinVerifyTrust` | Checks that `adb.exe` is signed by Google |
| `SetDefaultDllDirectories`, a DLL import resolver | Loads Windows DLLs from System32 only |
| Process start of itself | Restarts itself with safe settings, installs itself to Program Files, or starts a signed update |
| Clipboard | Only when you click Copy |

Not used anywhere: `WriteProcessMemory`, `ReadProcessMemory`, `CreateRemoteThread`, `VirtualAllocEx`, keyboard hooks, screen capture, services or drivers.

## Updates

The app installs an update only if the exe matches the release's SHA-256 and that `.sha256` file is signed with the project's own key, which never goes to GitHub. The signed message includes the version, so an older release can't come back under a newer tag. Every release exe is built by GitHub Actions from this source and has a [build attestation](https://github.com/Sepd0x/cs2-ip-changer/attestations).

## Known limits

- **Programs you already run with admin rights** can do anything on your PC, this app included. The protections above are against programs without admin rights.
- **Environment variables set for your Windows user** (the .NET profiler ones) can load a DLL into the first process before the app's own code runs. The app restarts itself without them, so the window and everything after is clean, but that first moment isn't. A native launcher to close this is planned for 1.0.
- **Started as admin from Downloads**, Windows and .NET load some DLLs from the exe's folder before the app can check anything; this is why it only runs from Program Files. Keep Downloads clean of DLLs you don't know.
- **The kill switch needs Windows Firewall on.** A third-party firewall that turns it off leaves the app without one, and the app says so.
- **IPv6 isn't routed through the phone.** CS2's game traffic is IPv4; in Whole PC mode IPv6 is blocked so nothing leaves from home.
- **Steam's web sign-ins on the Profiles page** use your normal connection outside Whole PC mode.
- **The usage stats** reach Cloudflare from your IP, like any website; the Worker gets only the country and stores no IP.
- The exe isn't code-signed with an Authenticode certificate, so SmartScreen may warn you.
