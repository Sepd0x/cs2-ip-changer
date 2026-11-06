# Releasing

The app only installs an update whose `CS2IPChanger.exe.sha256` is signed with the project's key. The private half of that key never goes on GitHub, so someone who gets into the repo, its releases or the Actions token still can't ship an exe that installed copies accept.

## Once: the key

```
node tools/release-key.mjs new
```

It asks for a passphrase, saves the private key encrypted in your user folder (`--out <file>` to pick another place, never inside the repo) and writes the public half to `src/Core/UpdateKey.cs`. Commit that file. Keep a copy of the `.pem` offline: without it the installed apps can't update by themselves any more. The tests fail while `UpdateKey.cs` has no key, so no release goes out without one.

## Two channels

- **Stable**: the release GitHub marks **Latest**. The Download links of the site and the README (`releases/latest/download`) give it, and the app's update offers it by default.
- **Test**: a release marked **pre-release**. The app only offers it to people who turned on Settings > App > Get test versions (from beta.12 on). Off, as it comes and in an older settings file, the app only sees the stable releases.

Every new version goes out first as a pre-release (test), and becomes stable only after the test rounds. No version becomes stable without the test suite, the audit and the test rounds with the owner on his PC.

A release taken back is a **draft**: no channel and no Download link sees a draft.

Older copies: beta.11 already leaves the pre-releases out, so it only gets stable versions (it has no Get test versions). Copies of beta.10 or older only leave out drafts, so they are offered a pre-release as if it were stable. The dashboard says how many of them still open the app.

## Each release

0. Colors ([DESIGN.md](DESIGN.md)): `node tools/design-tokens.mjs --check` and `node --test worker/stats/test/`. Then the
   [smoke test](SMOKE.md) on the release candidate, on the real PC with the phone: nothing goes out that broke what worked before.
1. Bump `<Version>` in `src/ServerRouteTool.csproj`, then `node tools/shots.mjs` (the app's sidebar shows the version, and `tests/DesignSyncTests.cs` fails in CI and in the Release workflow until the captures are of the new one; the 3D renders and `node tools/shots.mjs --renders` too if Connection changed). Turn `[Unreleased]` in the CHANGELOG into `[<version>] - <date>` (a new empty `[Unreleased]` above it), write the release notes in `docs/release-<version>.md` (the Release workflow publishes them; `tests/VersaoTests.cs` fails without them), commit all of it, push the tag `v<version>`.
2. The Release workflow runs the tests, builds the exe, attests it and puts the exe and its `.sha256` on a **draft** release.
3. On your PC:

   ```
   node tools/release-key.mjs sign v<version>
   ```

   It downloads the exe and the `.sha256`, checks they match and that the exe's attestation comes from this repo's Release workflow, asks for the passphrase, signs, uploads `CS2IPChanger.exe.sha256.sig` and publishes the draft as a **pre-release** (`gh release edit v<version> --draft=false --prerelease`): a test version. `--no-publish` leaves it as a draft. On GitHub, check that the release says Pre-release and that the one marked Latest is still the previous stable version.
4. Publishing starts the VirusTotal workflow (needs the `VT_API_KEY` repository secret): it sends the exe and writes "N of M engines flag it" with the link in the run's summary. Put that line in `README.md` (Safety), `site/is-it-safe` (in `tools/site-pages.mjs`) and the release notes. Again by hand: `gh workflow run virustotal.yml -f tag=v<version>`.
5. The test rounds: the copies with Get test versions on (the owner's first) update to it. Each round is the owner on his PC with the phone, the [smoke test](SMOKE.md) and what changed in this version. A problem found here is fixed in a new version (`-beta.N+1`), which goes out as a pre-release again from step 1; the one with the problem goes back to a draft (step 1 of [If a release breaks something](#if-a-release-breaks-something)).
6. Stable, only when the test suite passes, the audit is done and the test rounds with the owner are OK:

   ```
   gh release edit v<version> --prerelease=false --latest
   ```

   Now it is Latest: every copy is offered it, and the Download links give it. Then `node tools/site-pages.mjs --release v<version> --vt <flagged>/<engines>` updates `site/release.json` and every version line on the site and in the README.

## If a release breaks something

First, how bad is it:

| Level | Examples | What to do |
|---|---|---|
| **Critical** | the home IP reaches the servers with protection on, the app won't start, the PC loses internet after closing it, Steam files or accounts damaged | at once: steps 1 to 3 below, an issue pinned with what to do, then a fix release |
| **Serious** | a main command fails for many (Change IP, Join, the account switch), a crash many people hit | a fix release within a day or two; step 1 only if the fix takes longer |
| **Minor** | a wrong text, a layout problem, a rare case with a way around it | into the next release, with its test |

The Log, the dashboard (crashes, errors by place in the code, where people stop) and the issues say how many it hits.

Installed copies only update to the newest stable release (and the newest test one, with Get test versions on), and only when the user says yes. So:

1. Take the broken release back without deleting it: `gh release edit v<version> --draft=true`. A draft is left out by every copy's update check, in both channels and in every version of the app, and by the Download links. Marking it pre-release is not a way back any more: that is the test channel, so the copies with Get test versions on would still get it. If it was stable, the previous stable release becomes Latest again.
2. Users who already updated: the fix goes out as the next version (`-beta.N+1`). The updater never offers an older version, so a step back is not possible from inside the app; tell them in the release notes and the issue to download the previous exe by hand if they need it now.
3. The site and the README show `site/release.json`: run `node tools/site-pages.mjs --release v<previous> --vt <its result>` so the download lines match the release that is Latest again.

## The recording build

`dotnet publish src/ServerRouteTool.csproj -c Release -p:Recording=true -o <folder>` gives `CS2IPChanger-Recording.exe`: personal data never reaches the screen, and it never updates itself. It isn't part of the releases.
