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

## The site and the README (after the push)

11. The home page and one guide on a phone (390 px) and a PC: the loading screen goes away, the scroll story plays once, nothing overlaps.
12. Download goes to the release's exe; the README on GitHub shows the pictures of this version, on a PC and on a phone.
