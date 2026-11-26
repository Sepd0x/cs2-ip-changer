## Pickroute 1.0.0

CS2 IP Changer is now **Pickroute**, and this is its first stable version: a new look for the whole app, Regions on one dial for CS2, Dota 2 and Deadlock, Turkish, and a long list of fixes from the checks before 1.0.0. Nothing to do when you update: your settings, regions, accounts and updates carry over, and the download is still `CS2IPChanger.exe`.

### A new name and a new look
- **Pickroute** is the new name. Settings > App says the old one, and the install folder, your data and the stats stay where they were.
- Every page keeps all its buttons and options, laid out more calmly. The window has a thin route line at the top, your protection and the quick button at the top right, and a status line at the bottom (phone, mobile data, hotkey).
- **Regions:** all your regions on one dial. The length of a spoke is its ping, the ring is your ping limit, and the lit arc is where the game can play. Choose CS2, Dota 2 or Deadlock; each keeps its own regions and presets, and a preset can apply when CS2 starts.
- **Connection:** the IP servers see in large type, with the route from this PC through the phone to the servers under it.
- **Servers** is a plain table with one Join per row, **Accounts** shows cards, **Configs** is the list of your accounts with Sync on one line, and **Profiles** goes in three steps.
- Dialogs, Phone setup, the first start and the tour look like the rest of the app.

### First start and Phone setup
- The app asks what you want to do first: pick your regions or play through your phone. Until a phone is set up, it opens on Regions and the top button on Connection says "Set up the phone".
- No USB data cable? "Use the hotspot" is there from the start.
- Without CS2 installed, the first question has a button that opens CS2's install in Steam.

### Protection
- **An Android phone over USB, with USB debugging not allowed and a PC with no home network of its own:** the app can't tell if the phone is sharing your home Wi-Fi. Your servers and CS2 now stay blocked until the mobile data can be confirmed. Connection says what to do, and Phone setup opens on the USB debugging step.
- **Whole PC** only says protected when both halves of the internet go through the phone.
- If the app was stopped in the middle of updating a protection rule, the next start could take that block away while CS2 was still open. It now counts it as a block that is still on.
- **Banned IP block:** it now blocks only the exact servers that banned your IP, not other servers on the same machines.
- **Regions:** Allow all works even when Valve's list can't be loaded.
- The app only runs as administrator from a folder that only administrators can change: groups and other users with write rights now count too.
- **Join** checks protection again right before it connects, so turning protection off while CS2 starts can't send you in through your home connection.
- With Phone setup open, the phone turning its Wi-Fi on blocks at once.

### Privacy
- Log > Copy all keeps your home IP and logins hidden when Hide logins is on, and the Log never writes a home IP whole.
- Usage stats: the time they were off isn't counted, and a phone seen while they were off isn't sent.
- The bug report hides every Wi-Fi network name, also those in older Log lines.

### Also fixed
- An update download that stops receiving data gives up after 30 seconds, so Update can be pressed again.
- Start with Windows shows off when the task was turned off in Task Scheduler.
- Steam accounts with IDs starting 7656120 show in Accounts, and Steam's connection log in UTF-16 is read correctly after the first time.
- Accounts: after logging in to another account from the app, In use and Log in move to the right cards at once.
- Phone setup and the first start's question no longer dim the top bar, so the window buttons stay clickable.
- Whole PC, turned off for a weak connection, comes back after a Change IP or a restart once the connection is good.
- Regions: an allowed region with no ping yet waits for it instead of showing as 200 ms.

### Languages
- The app and the website are now also in **Turkish**.
- Many fixes in Portuguese, Spanish and Russian: country names in Regions, Join, Log in and Sign in each have their own word, and labels that were cut off now fit. Tooltips, dialog hints, the dial's continents and Change IP's progress follow the app's language.

### Also
- Usage stats are no longer required: the first start asks, with Accept and Turn off, and Settings changes it any time.
- Accounts: Refresh's progress is under the title, cooldowns read "2 d" or "1 h 35 min", and the last server fits on one line.
- Connection: a long carrier name is shown short. Configs: a custom crosshair colour is named.
- Windows with a Turkish, Finnish, Danish, Thai, Arabic or Persian regional format: names, phones and log dates now work the same as anywhere else.
- Website: the 3D scenes ask less of a phone, and scrolling on a phone follows your finger.

**If you go back to an older version, run `CS2IPChanger.exe --restore` first:** the app's Windows Firewall rules are now named "Pickroute - ...", and older versions don't know that name.

The full list is in the [CHANGELOG](https://github.com/Sepd0x/cs2-ip-changer/blob/main/CHANGELOG.md).
