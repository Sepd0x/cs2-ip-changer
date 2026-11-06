## CS2 IP Changer 0.1.0-beta.12 (test version)

**This is a test version.** It went through a full review of the app (every page, message, button and tooltip in every protection state, plus the network, Steam and the app's start and close) and every fix has its own test, but it hasn't had its test rounds on a real PC with a phone yet. Copies of beta.11 aren't offered it. To try it, download it here; once it is tested it becomes the stable version.

### Two kinds of versions
From now on every new version comes out first as a test version, like this one. **Settings > App > Get test versions** (off by default) offers them to you. Off, the app only offers stable versions, the ones the website and the README download.

### The app says what is true
- With **Use the app with protection off** on and protection off, the top bar and Status say **Home IP: servers see it** in red.
- With protection off or in No phone mode, nothing says "blocked", "protected" or "through the phone" when it isn't, and rows of Checks that don't apply are grey, not red.
- Leaving No phone mode asks **Turn on protection now?** instead of turning it on by itself.
- **New IP + next account** is named for every step it runs, for example "New IP + atlas + rejoin", in the top bar, on Accounts and in the Log.
- **Show hidden** on Accounts now switches with **Hide hidden**; hidden accounts show dimmed and stay out of the rotation.

### Safer closing and network
- Whole PC: closing the app and keeping protection no longer leaves a DNS rule behind that could leave the PC without internet.
- A second X during "Undoing the routes and the kill switch…" no longer cuts it half way; signing out of Windows takes the Firewall rules first.
- Closing the app during Change IP gives the phone its mobile data back.
- With two copies of CS2 on the disk, the CS2 lock goes on the one that runs.

### Kicks and bans
- A line in the chat ("you got banned") is never read as a kick or a ban.
- A server that turns you away while you join with no reason (whitelist, Prime only) no longer marks the account as banned.
- Steam's own session codes are no longer shown as a VAC refusal.
- Servers full of bots are no longer "full", and the ping on Servers is no longer about double.

### And more
Over a hundred smaller fixes in Configs, Servers, Regions, Settings, the tray, updates and the texts. The full list is in the [CHANGELOG](https://github.com/Sepd0x/cs2-ip-changer/blob/main/CHANGELOG.md).

### Website
When Windows is set to show fewer animations, the website starts still and offers **Play motion**.
