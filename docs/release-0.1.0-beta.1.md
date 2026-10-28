## CS2 IP Changer 0.1.0-beta.1

The first beta. Everything from a full review of alpha.4: Steam and CS2 no longer run as admin, hotkeys that don't fire on AltGr, a Whole PC mode that also covers DNS and IPv6, and a diagnostics export for bug reports.

### New
- Hotkeys you can change in Settings > Switch: click the box and press the keys. Ctrl+Alt combinations are refused, because on many keyboards that is AltGr.
- Community names for servers: right-click a server > Set community name. Servers with the same name share ban marks.
- Export diagnostics on the Log page: a .zip on the Desktop to attach to a bug report. Your Steam Web API key and your home IP are left out.
- Change IP also works on the phone's Wi-Fi hotspot when the phone is plugged in by USB for ADB.
- Whole PC mode now covers DNS and IPv6: DNS can only leave through the phone and IPv6 is blocked while it's on.
- The Steam Web API key stays hidden in Settings until you click Show.

### What changed
- The hotkeys are now Ctrl+Shift+F9 (Switch) and Ctrl+Shift+F10 (Change IP and rejoin). The old Ctrl+Alt+Q and Ctrl+Alt+R also fired on AltGr+Q and AltGr+R, so typing an @ in Discord could close CS2 and Steam mid-match.
- The carrier and city of an IP come from ipwho.is over HTTPS instead of ip-api.com over plain HTTP.
- VAC and Steam refusals in CS2's console are no longer treated as kicks. The app says a new IP won't help and doesn't offer to rejoin.
- Bans no longer spread to other servers just because they share an IP or a generic name like "Retakes".
- USB network adapters that aren't phones (a laptop dock, for example) are no longer picked as the phone on their own.

### What's fixed
- Steam, CS2 and the browser links no longer start with the app's admin rights: they open through Windows Explorer as you.
- With no phone connected, 1.0.0.1 was blocked for the whole PC. The rule now only applies to the app itself.
- `--restore` works when a stuck copy of the app is still running, and tells you to close it.
- Update and the ADB download show the size and warn you when it would go through mobile data.

### Upgrading
Replace `CS2IPChanger.exe` with this one in the same folder and keep `SRT_Dados` next to it. Settings, servers and accounts carry over, and the firewall rules are updated on the first start.
- The hotkeys are now Ctrl+Shift+F9 and Ctrl+Shift+F10. Set others in Settings > Switch if you prefer.
- If you start CS2 from Steam, add `-conclearlog` next to `-condebug` in its launch options.

### Heads-up
This is a beta, so expect bugs. If something breaks your connection, Settings > Restore network removes every route and firewall rule the app added. Nothing here hides you from anti-cheat or from a server's staff; a ban on your Steam account follows the account, not the IP.

### Check the download
This exe was built by GitHub Actions from the tagged code. `CS2IPChanger.exe.sha256` next to it has its SHA-256, and the build attestation proves where it came from: `gh attestation verify CS2IPChanger.exe -R Sepd0x/cs2-ip-changer`.

Full list in `CHANGELOG.md`.
