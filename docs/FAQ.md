# CS2 IP Changer: FAQ

Every question and answer in one place. The most common ones are also in the [README](../README.md#faq), and the guides on the [website](https://sepd0x.github.io/cs2-ip-changer/#guides) go deeper on each topic.

- [The basics](#the-basics)
- [Bans](#bans)
- [Your IP](#your-ip)
- [Phone and connection](#phone-and-connection)
- [Running the app](#running-the-app)

## The basics

**Is it a VPN? Does it work on servers that block VPNs?**<br>
It isn't a VPN. A VPN sends your traffic through a datacenter, and many community servers run anti-VPN plugins that check each player's IP against lists of VPN and datacenter addresses; one CS2 plugin ([CS2-AntiVPN](https://github.com/daffyyyy/CS2-AntiVPN)) bans by default. This app sends CS2 and Steam through your own phone's mobile data, so the server sees an ordinary mobile connection from your carrier, the same as someone playing on a phone hotspot, and it usually gets in where a VPN doesn't. It's not a promise: carriers share each IP between many phones, so an IP someone else misused can be flagged; Change IP gets you another one. There's no VPN server in between and no subscription.

**Can this get me VAC banned?**<br>
It never touches the game process, its memory or its program files. It changes Windows routing and firewall rules and which Steam account logs in, and switching accounts never asks for your Steam password. It won't hide a cheat from VAC, and we can't speak for Valve or for a server's admins, who set their own rules. The rest is in [Is it safe?](../README.md#is-it-safe)

**Will my ping be worse?**<br>
Usually, yes. Mobile data adds latency and jitter, and some cells drop packets when they're busy, so expect a higher ping than at home and a game that can feel less smooth. A good 5G or 4G signal helps a lot: put the phone near a window, keep it charging and don't run other heavy stuff on it. The ping column in Servers is measured through the phone, so it shows what you'll get before you join, and Connection quality in Checks tells you when the connection itself is weak. What to do when it stays weak is under [Phone and connection](#phone-and-connection).

**Does the account switcher store my Steam password?**<br>
No. It uses the logins Steam already saved on your PC (the accounts where you ticked Remember me). To switch, it tells Steam which saved account to log into and restarts Steam, the same way other Steam account switchers work. Switching never asks for your password or your Steam Guard codes.

The only place you type a password is Sign in on the Profiles page, and only if you want the app to change a profile. Even there it's used once and never saved: the app keeps an encrypted Steam sign-in token instead ([what happens to your login](FEATURES.md#what-happens-to-my-login)). You can also sign in with a QR code from the Steam app and type nothing.

**How do I change my IP in CS2?**<br>
Connect an Android phone by USB with tethering on, then click Change IP (or press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F10</kbd> in game to change it and rejoin). The phone's mobile data goes off and on (4 seconds by default) until the carrier hands out a new IP. Restarting your home router also changes the IP with many providers, but it can take hours and every device in the house drops.

**Does it need root, or an app on the phone?**<br>
No root and nothing to install on the phone. Change IP uses ADB with USB debugging on (Google's official tool, which the app can download). Without ADB, protection still works; you change the IP by hand with airplane mode.

**Does it work with an iPhone?**<br>
Yes, the protection does: an iPhone's USB tethering (Personal Hotspot) shows up in Windows like an Android one, and so does its Wi-Fi hotspot. Change IP from the PC needs ADB, which only Android has, so on an iPhone you switch airplane mode on and off yourself.

**Is it malware? Why does my antivirus flag it?**<br>
It runs as admin, changes network settings and isn't code-signed, which is what some heuristics look for. Every release is built by GitHub Actions from this source and attested, and [Privacy and security](PRIVACY-AND-SECURITY.md) lists every Windows function it uses with admin rights and why. [Check your download](../README.md#check-your-download) before you run it.

**Can it run in the background or start with Windows?**<br>
Yes. Settings > App: the X can hide the window next to the clock, and the copy installed in `C:\Program Files\CS2 IP Changer` can start with Windows, in the notification area, without a UAC prompt each time. Both are off until you turn them on.

**How much mobile data does it use?**<br>
CS2 itself uses little next to a download, but Steam's downloads also go through the phone while it's connected. The Phone box on the Connection page counts what went through the phone this session and this month, and Settings > Mobile data warns you at a limit you pick.

## Bans

**I'm banned on a server. Will this get me back in?**<br>
If it's an IP ban, change IP and join again. A ban on your Steam account follows the account, not the IP, so a new IP won't lift it, and your other accounts aren't affected by it. The app marks that account as banned on that server and its network, and New IP + next account won't pick it there.

**Some admins ban a whole IP range. Does that matter?**<br>
After each change the app tells you whether the new IP is in a new /16 or in the same one as before. If you want it to keep going until the range changes, turn on Settings > Keep changing until the IP is in a new range.

**Does it stop me from getting banned?**<br>
No app can stop an admin from banning you. What it does is keep your home IP out of every server's ban list, so a ban never sticks to your house and getting back in takes a few seconds.

**Does forcing a region get me banned?**<br>
The Regions page doesn't touch the game, its files or its memory. It adds one outbound Windows Firewall rule that stops `cs2.exe` from reaching Valve's relays in the regions you turned off, so CS2 sees those regions as unreachable and matchmaking plays elsewhere. To the game it looks the same as a bad connection to those relays. Still, we can't speak for Valve and can't promise anything about how they treat it. Allow all (block off) removes the rule, and so does `--restore`. Turn off protection and No phone mode leave it on. More in the [region picker guide](https://sepd0x.github.io/cs2-ip-changer/cs2-region-picker/).

## Your IP

**Why bother hiding my home IP? Who can see it?**<br>
The admins of every community server you join can see your IP, and so can the plugins they run on it. Other players can't. IP bans are tied to it, and it's in the server's logs. With the phone's IP, nobody on the server side gets your home connection to DDoS it, and after a ban you change IP and keep playing on the same servers.

**My carrier uses CGNAT. Is that a problem?**<br>
No. You share a public IP with other customers and servers see that shared one. Change IP still works, but sometimes the carrier gives you the same IP back, so the app keeps trying (you set how many times).

## Phone and connection

**My connection stays weak. What can I do?**<br>
Change IP can put you on another cell, and Game only keeps the rest of the PC off the phone (the app does this on its own while the connection is weak, unless you turn off Fall back to Game only on a weak connection). On the phone you can also check Settings > Connections > Mobile networks > Network mode is set to include 5G. Forcing 5G only helps where the 5G signal is good, and some phones show 5G as 4G (LTE) because 5G there runs on top of 4G.

**I plugged the phone in and the app sees nothing. Why?**<br>
Most often the cable only charges: use a data cable, ideally the one that came with the phone. Unlock the phone, try another USB port (one on the back of the PC), and keep Phone setup open: it checks every 2 seconds and turns the step green as soon as Windows sees the phone. No data cable at hand? Use the phone's Wi-Fi hotspot (next question) and plug in a cable later for Change IP.

**Do I have to download ADB myself?**<br>
No. Phone setup downloads Google's platform-tools (about 7 MB) by itself when you open it, with a progress bar. On the phone's mobile data it waits for you to press Install. If it keeps failing, download "SDK Platform-Tools for Windows" from developer.android.com and unzip it into the app's data folder (Settings > Maintenance > Open data folder).

**Can I use the phone's Wi-Fi hotspot instead of a cable?**<br>
Yes. Connect the PC to the hotspot and pick it under Connection > How the PC connects to the phone. The app remembers the network name, and if Windows jumps back to your home router it blocks everything. Change IP works on the hotspot too if the phone is also plugged in by USB with USB debugging on (the cable is only for ADB, tethering can stay off).

**What about a 4G/5G USB dongle or router?**<br>
It works as the connection. If the app doesn't pick it on its own, choose it under Connection > How the PC connects to the phone: USB network adapters that aren't phones (like a laptop dock's Ethernet) are only used when you pick them. Change IP needs an Android phone with ADB, though.

**In Whole PC mode, websites take a second to open. Why?**<br>
While Whole PC is on, DNS can only leave through the phone and IPv6 is blocked, so nothing goes out from home. Windows may first try your home router's DNS or an IPv6 address, get blocked, and then use the phone. That can add a second or two to the first visit to a site. Game only mode doesn't do this.

## Running the app

**Why does it need admin?**<br>
Routes and firewall rules are system settings, and Windows only lets admins change them. That's also why the exe has to live in a folder only admins can change: from any other folder it doesn't start.

**Why does the app want <code>-condebug -conclearlog</code>?**<br>
`-condebug` makes CS2 write its console to `console.log`, which is how the app sees a kick or a ban. `-conclearlog` empties that file each time CS2 starts, so it doesn't grow forever. The app adds both when it opens CS2. If you start CS2 from Steam, add them to the launch options (CS2 > Properties).

**What happens when I close the app?**<br>
Protection stays on, so closing the app can't leak your IP. If you'd rather have it turn off, there's a setting for that. Until a phone has been connected once, CS2 isn't locked, so trying the app without a phone never leaves CS2 unable to connect at home.
