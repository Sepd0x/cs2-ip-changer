# CS2 IP Changer

Sends CS2 and Steam through your Android phone's mobile data, so community servers see your carrier's IP and never your home IP. If you get IP banned, change the IP from the app and join again. There's a Steam account switcher in it too.

> Alpha build, so expect bugs. If something breaks your connection, Settings > Restore network removes every route and firewall rule the app added.

![Connection tab](docs/screenshots/connection.png)

## What it does

- Only CS2, Steam and the servers on your list go through the phone. Your browser, Discord and downloads stay on your home connection and don't eat your mobile data. You can send the whole PC through the phone instead if you want.
- A Windows Firewall rule works as a kill switch. Your listed servers are blocked on every adapter except the phone, so if the cable comes out the game loses connection instead of falling back to your home IP.
- `cs2.exe` itself can only use the phone connection. That covers servers you join from the in-game browser or from an invite.
- Change IP turns mobile data (or airplane mode) off and on over ADB until the carrier hands you a new IP. The old ones stay in a list so you can see they really changed.
- The Connection tab checks everything live: firewall, routes, the IP servers see compared to your home IP, whether the phone is really on mobile data, and whether Steam uses the same connection as the game. That last check is there because otherwise servers kick you with "No Steam logon". There's a leak test as well.
- Paste servers as `IP:port`, `connect IP:port` or `steam://connect/...` links. The list shows name, map, players and ping, and you join with one click.
- The account switcher logs into your saved Steam accounts without typing passwords. Ctrl+Alt+Q closes CS2, logs into the next account and opens the game again, and it works while you're in game. Each account shows VAC, game and trade bans and its cooldown timer. Hide logins keeps usernames off your stream.

![Servers tab](docs/screenshots/servers.png)

![Accounts tab](docs/screenshots/accounts.png)

## Setup

You need Windows 10 or 11 (64-bit), admin rights and an Android phone with mobile data.

1. Download `CS2IPChanger.exe` from [Releases](../../releases) and put it in its own folder.
2. Plug the phone in by USB, turn on USB tethering and turn off the phone's Wi-Fi, so it shares mobile data and not your home Wi-Fi.
3. Run the app (it needs admin) and paste the servers you play on into Servers.
4. Open Connection and check that everything is green and that the IP servers see is not your home IP.

To change IP from the PC, also turn on USB debugging on the phone. If ADB isn't installed, the app can download Google's platform-tools for you (about 7 MB).

The app is portable. Settings, servers and accounts are saved in `SRT_Dados` next to the exe. To uninstall, click Restore network first and then delete the folder.

## FAQ

**Can this get me VAC banned?**
It never touches the game process or its files. All it changes is Windows routing, firewall rules and which Steam account logs in. It also won't hide a cheat from VAC or any other anti-cheat, so don't use it for that.

**I'm banned on a server. Will this get me back in?**
Yes. If it's an IP ban, change IP and join again. If they banned your Steam account too, switch to another account (Ctrl+Alt+Q), change IP and you're back on the server.

**Does it stop me from getting banned?**
No app can stop an admin from banning you. What it does is keep your home IP out of every server's ban list, so a ban never sticks to your house and getting back in takes a few seconds.

**Why bother hiding my home IP?**
Anyone running a community server can see the IP of every player who joins, and IP bans are tied to it. With the phone's IP, nobody gets your home connection to DDoS it, and after a ban you change IP and keep playing on the same servers.

**My carrier uses CGNAT. Is that a problem?**
No. You share a public IP with other customers and servers see that shared one. Change IP still works, but sometimes the carrier gives you the same IP back, so the app keeps trying (you set how many times).

**Can I use the phone's Wi-Fi hotspot instead of a cable?**
Yes. Connect the PC to the hotspot and pick it under Connection > How the PC connects to the phone. The app remembers the network name, and if Windows jumps back to your home router it blocks everything.

**What about a 4G/5G USB dongle or router?**
It works as the connection. Change IP needs an Android phone with ADB, though.

**What happens when I close the app?**
Protection stays on, so closing the app can't leak your IP. If you'd rather have it turn off, there's a setting for that.

## Privacy

There's no telemetry and no account. The app only talks to these services:

| What for | Service |
|---|---|
| Your public IP (home and phone) | api.ipify.org, ipv4.icanhazip.com, checkip.amazonaws.com, 1.0.0.1 (Cloudflare) |
| Carrier and city of an IP | ip-api.com |
| Profiles, avatars and bans of your accounts | steamcommunity.com, Steam's avatar servers and, if you add a key, api.steampowered.com |
| Downloading ADB (only if you ask) | dl.google.com |

Your Steam Web API key stays on your PC, in `SRT_Dados/settings.json`.

## License

All rights reserved. You can download the releases and use them. You can't copy, modify or redistribute the code without permission. Details in [LICENSE](LICENSE).

Not affiliated with Valve. Counter-Strike and Steam are trademarks of Valve Corporation.
