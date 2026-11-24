# Smoke test before a release

The tests (`CI=true dotnet run --project tests`, `node --test worker/stats/test/`) check the code. This list checks the things only a
real PC, a real phone and a real Steam can show, the ones that broke between versions before. Run it on the release candidate
(`dotnet publish src/ServerRouteTool.csproj -c Release -o <folder>`) on the PC that plays CS2, with the phone, before the tag.
About 20 minutes. Write the result in the release's session note; anything that fails gets fixed with a test that failed first.

## The app

1. Start: protection off, Status says so, no question pops up on its own.
2. Turn on protection with the phone plugged in and tethering off: tethering turns on, Status goes green, Checks fold to one line.
3. Change IP: a new IP shows in "IP servers see"; the Log says how long it took.
4. Servers: Refresh shows players and ping. Join a server: CS2 opens and joins through the phone.
5. Accounts: Log in to a second account (one Steam already remembers): Steam restarts on it **without asking for the password**, CS2 opens if asked.
   Then Settings > Privacy > Hide usernames: the usernames on Accounts and the home IP on Log hide. Hide an account
   (right-click > Hide from list): Show hidden appears next to Refresh and brings it back, then goes away.
6. New IP + next account (the hotkey) while CS2 is open: new IP, next account, rejoin.
7. A kick or ban from a server shows up in the app (or trust the last real one in the Log).
8. Turn off protection: Status says servers see your home IP. With Settings > Use the app with protection off on, Servers and Join work on the home connection; with it off, Join asks to turn protection on.
9. Close the app with protection on: it asks what to keep; Log > What the app changed is empty after turning off.
10. Update: an older installed copy finds this version (after the release), checks its signature and restarts on it.

## First run, phone, languages, bans, install (beta.13 and 1.0.0)

11. **First start** on a PC with no SRT_Dados next to the exe (copy the exe alone to a new folder in Program Files): the Usage stats notice shows before the app opens, with Accept and Turn off (Turn off opens the app and sends nothing; Settings > Send usage stats shows it off); After Accept, the short tour (5 stops) opens once and points at the real buttons; Skip and Esc end it; Settings > App > Show the tour again brings it back.
12. **Phone setup, Android.** Phone tool (ADB) removed (delete the platform-tools folder inside SRT_Dados): open Phone setup, the row downloads ADB by itself with the MB and a bar, then the steps follow (data cable, USB debugging with the screens for your brand, Allow, USB tethering). On the phone's mobile data it waits for Install instead. With USB debugging revoked, Wi-Fi of the phone on the home network and no other network on the PC, Status says Mobile data not confirmed, never Protected.
13. **Phone setup, iPhone.** With an iPhone on the cable and no ADB: no download starts, the steps are Apple Devices, Trust, Personal Hotspot; Change IP walks through Airplane Mode with the iPhone's own words.
14. **Languages.** Settings > App > Language: pick Portuguese, Spanish and Russian in turn. The toast says to close and open the app (with Close to the notification area on: to Quit from the icon's menu). After the restart the window, the toasts, Checks, the server states (Blocked, Unprotected, No kill switch, Route failed) and Configs > Compare are all in that language, with no English left over; the Log stays in English. Look for cut text on Settings, Servers, Profiles and Configs (SRT_LANG=ru and SRT_PSEUDO=1 show it on any PC).
15. **A banned IP.** After a ban seen in the console.log (the app keeps the IP the server saw), Join to that server and to its community stops with the IP, the date and what to do. Change IP lifts it; the Firewall rule named CS2 IP Changer - ... (banned IP) is gone from wf.msc after the IP changes, on Turn off protection, on close and on --restore.
16. **Install and SmartScreen.** The exe from the release page, on a PC where it never ran: SmartScreen says it is not recognised (More info > Run anyway is expected, the exe has no signature; README says so). From Downloads the app shows Install, copies itself to Program Files with its SRT_Dados and starts from there. Uninstall in Windows Settings > Installed apps lists CS2 IP Changer.
17. **--restore.** With protection on and a region off, close with Keep. In an admin terminal: CS2IPChanger.exe --restore. wf.msc has no rule of the app left (new names, and ServerRouteTool v9 ones from before 1.0.0), routes and DNS are back, CS2 connects from home. tools/restore.cmd does the same with the exe deleted.
18. **Update from the beta.13 with protection on** (an upgrade needs the old copy): after the update wf.msc has only CS2 IP Changer - ... rules and the group CS2 IP Changer, none missing at any moment; Turn off protection and Allow all remove them all.
19. **Whole PC and a crash.** Whole PC always, protection on; end the app from a debug build with an exception (or unplug the phone after a crash): websites still load (the DNS and IPv6 rules go with the crash; the kill switch stays). Ended in Task Manager: open the app once to put it back.

## The site and the README (after the push)

20. The home page and one guide on a phone (390 px) and a PC: the loading screen goes away, the scroll story plays once, nothing overlaps.
21. Download goes to the release's exe; the README on GitHub shows the pictures of this version, on a PC and on a phone.
