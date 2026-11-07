## CS2 IP Changer 0.1.0-beta.13 (test version)

**This is a test version.** It fixes what came up while using beta.12, and every fix has its own test, but it hasn't had its test rounds on a real PC with a phone yet. Only copies with **Settings > App > Get test versions** on are offered it. To try it, download it here; once it is tested it becomes the stable version.

### Accounts and start
- With **Start Steam minimized** on, logging in to another account made Steam ask for the password, even for accounts saved with Remember me. The app now starts Steam the way that uses the saved sign-in.
- After **Start with Windows**, a double click on the icon next to the clock opened the window as a tiny bar in the corner. It now opens whole, in the middle of the screen.

### Servers and Configs
- The time a player has been on a server now has its unit: **38m**, **1h 05m** (it read like minutes and seconds before), and a server that sent a strange time no longer hides its whole player list.
- A server that asks for its challenge twice gets its ping and players again.
- The crosshair preview in Configs follows CS2's new crosshair sizes (September 2026 update): the whole crosshair fits, with the same proportions as in the game.

### Log
Fewer repeated lines (Kill switch, Sync now with CS2 open, the home IP) and clearer ones (launch options, "CS2 network for the last N min").

### Usage stats
The coarse click map counts how far the page is scrolled and only clicks while the app is the window in front; the stats count how many times CS2 joined a server (only the number); updating no longer sends the last stats twice. README > Telemetry has the full list.

### Website
A more real 3D scene (materials, light, detailed PC, phone, mast and servers, depth and smoother camera moves), a cleaner light-up of the name at the end of each page, nothing jumps while you read, and the 404 page offers Play motion.

The full list is in the [CHANGELOG](https://github.com/Sepd0x/cs2-ip-changer/blob/main/CHANGELOG.md).
