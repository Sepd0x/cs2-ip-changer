## Pickroute 1.0.1

Small fixes after 1.0.0: a narrower sidebar, texts that fit on small screens in every language, a safer Enter key in questions, and a few rare cases that could stop a page from showing. Nothing to do when you update: your settings, regions, accounts and updates carry over, and the download is still `CS2IPChanger.exe`.

### Looks
- The sidebar is narrower (104 px instead of 168), so every page has more room. On short screens (1080p at 200 %) a page's icon no longer touches its highlight, and with an update waiting Profiles stays above Log.
- Regions: the warning sign next to "If CS2 is open, restart it after changing regions" is centred.
- Text that doesn't fit ends in "..." instead of being cut through a word: the status line at the bottom, Configs' Sync line and long names in the Settings list. Servers' paste box starts with Ctrl+Enter, Configs' note on backups wraps, and Regions' Refresh stays in the window.
- Phone setup: on short screens the steps and the drawn guide scroll, and Close stays at the bottom.

### In your language
- Dates show in your language (Configs' last sync, cooldowns, bans, snapshots), and mobile data uses your decimal sign ("1,24 GB").
- Portuguese and Spanish: "2 copied, 3 already the same" agrees with the number.
- The maximize button says Restore down while the window is maximized.

### Fixed
- In a question, Enter on the X or on Cancel no longer answers yes (Tab to the X and Enter could delete a Configs snapshot).
- Accounts: a Steam sign-in date saved in milliseconds no longer stops the list from showing; with your only account hidden and Show hidden on, it no longer shows under "No accounts yet".
- Configs finds your accounts' configs when Steam is installed after the app opened.
- Settings > Mobile data: "NaN" in Warn me after no longer stops the settings from being saved.
- Accounts > Cooldown: "-2h" or "1h 30" are no longer taken as 2 hours and 1 hour.
- The usage stats notice: with the full list open on a small screen, Accept and Turn off stay on screen.
- Servers > Import: a list that can't be read says so, and you can choose it again.
- Log: the filtered count follows Clear and new lines.
- An avatar's letter shows a whole emoji. Settings > Steam: with no Steam account on the PC, adding the launch options says so.
- After a weak connection, the message says which Whole PC mode came back.

### Accessibility
- Narrator and other screen readers read a name for buttons that show only an icon (Minimize, Close, Copy IP, Delete...), and those names are translated.

### Also
- The exe now says Pickroute, its publisher and its copyright in Properties > Details.
- Website: a Screenshots page with every page of the app, and Pickroute is on GameBanana (CS2 > Tools).

The full list is in the [CHANGELOG](https://github.com/Sepd0x/pickroute/blob/main/CHANGELOG.md).
