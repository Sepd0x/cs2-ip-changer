## CS2 IP Changer 0.1.0-beta.14

Fixes from the full check before 1.0.0: "Protected" only when the phone really gives mobile data, a crash in Whole PC no longer leaves websites blocked, the Firewall rules get an English name, and much more of the app in Portuguese, Spanish and Russian.

### Protection
- With an Android phone on a USB cable, USB debugging not allowed and no other network on the PC, the app could say **Protected** while the phone was sharing its Wi-Fi (your home connection). It now says **Mobile data not confirmed** and tells you to turn off the phone's Wi-Fi or allow USB debugging. It also compares the phone's IP with the last home IP it measured, and blocks your servers if they match.
- **Whole PC:** if the app crashed, its DNS and IPv6 rules stayed and websites stopped loading once the phone was unplugged. A crash now takes them off within 2 seconds (the kill switch and the CS2 lock stay). If the app is ended in Task Manager or the PC loses power, open the app once.
- **Save mobile data:** a phone the app only knew by its USB address stopped counting as the phone, so servers and CS2 were blocked for a few seconds every minute. Fixed.
- **Banned IP block:** two quick changes could leave the block on the old list. Changes now go in one at a time.

### Windows Firewall
The app's rules are now named **CS2 IP Changer - ...** (they were "ServerRouteTool v9 - ...", partly in Portuguese). The app renames the old ones by itself, and each new rule is in place before the old one goes, so nothing is ever less blocked. **If you go back to an older version, run `CS2IPChanger.exe --restore` first**: older versions don't know the new names.

### Phone setup
- After **Cancel**, Turn on USB tethering no longer says "Cancelled." later on its own.
- When the cable came out in the middle of a check, an adb error could show as the phone's model. It now says the phone isn't connected, and the phone's serial number never shows on screen, in the Log or in a bug report.

### Languages
Many words that stayed in English in Portuguese, Spanish and Russian are translated: server states, kick, ban and VPN notices, Steam errors, iPhone steps, the grey hints in the search boxes, continents, the settings in Configs > Compare. The rows of lists are translated too, and labels cut off in Russian and Spanish fit.

### Website
Guides on CS2 on mobile data, the CS2 ping test and where CS2 keeps its config; server pickers compared; the 3D scenes cost a phone about half the work per frame (14 to 31 frames a second on the home page on a mid-range phone), with the same pictures.

The full list is in the [CHANGELOG](https://github.com/Sepd0x/cs2-ip-changer/blob/main/CHANGELOG.md).

VirusTotal: [0 of 70 engines](https://www.virustotal.com/gui/file/95122c779dd0eb0ef1fbd08a339c766711ef312112a899c1e233f5dcfbb3d99e/detection) flag it.
