# Releasing

The app only installs an update whose `CS2IPChanger.exe.sha256` is signed with the project's key. The private half of that key never goes on GitHub, so someone who gets into the repo, its releases or the Actions token still can't ship an exe that installed copies accept.

## Once: the key

```
node tools/release-key.mjs new
```

It asks for a passphrase, saves the private key encrypted in your user folder (`--out <file>` to pick another place, never inside the repo) and writes the public half to `src/Core/UpdateKey.cs`. Commit that file. Keep a copy of the `.pem` offline: without it the installed apps can't update by themselves any more. The tests fail while `UpdateKey.cs` has no key, so no release goes out without one.

## Each release

0. Colors ([DESIGN.md](DESIGN.md)): `node tools/design-tokens.mjs --check` and `node --test worker/stats/test/`. Then the
   [smoke test](SMOKE.md) on the release candidate, on the real PC with the phone: nothing goes out that broke what worked before.
1. Bump `<Version>` in `src/ServerRouteTool.csproj`, then `node tools/shots.mjs` (the app's sidebar shows the version, and `tests/DesignSyncTests.cs` fails in CI and in the Release workflow until the captures are of the new one; the 3D renders and `node tools/shots.mjs --renders` too if Connection changed). Turn `[Unreleased]` in the CHANGELOG into `[<version>] - <date>` (a new empty `[Unreleased]` above it), write the release notes in `docs/release-<version>.md` (the Release workflow publishes them; `tests/VersaoTests.cs` fails without them), commit all of it, push the tag `v<version>`. After the release is published and VirusTotal ran: `node tools/site-pages.mjs --release v<version> --vt <flagged>/<engines>` updates `site/release.json` and every version line on the site and in the README.
2. The Release workflow runs the tests, builds the exe, attests it and puts the exe and its `.sha256` on a **draft** release.
3. On your PC:

   ```
   node tools/release-key.mjs sign v<version>
   ```

   It downloads the exe and the `.sha256`, checks they match and that the exe's attestation comes from this repo's Release workflow, asks for the passphrase, signs, uploads `CS2IPChanger.exe.sha256.sig` and publishes the draft. `--no-publish` leaves it as a draft.
4. Publishing starts the VirusTotal workflow (needs the `VT_API_KEY` repository secret): it sends the exe and writes "N of M engines flag it" with the link in the run's summary. Put that line in `README.md` (Safety), `site/is-it-safe` (in `tools/site-pages.mjs`) and the release notes. Again by hand: `gh workflow run virustotal.yml -f tag=v<version>`.

## If a release breaks something

First, how bad is it:

| Level | Examples | What to do |
|---|---|---|
| **Critical** | the home IP reaches the servers with protection on, the app won't start, the PC loses internet after closing it, Steam files or accounts damaged | at once: steps 1 to 3 below, an issue pinned with what to do, then a fix release |
| **Serious** | a main command fails for many (Change IP, Join, the account switch), a crash many people hit | a fix release within a day or two; step 1 only if the fix takes longer |
| **Minor** | a wrong text, a layout problem, a rare case with a way around it | into the next release, with its test |

The Log, the dashboard (crashes, errors by place in the code, where people stop) and the issues say how many it hits.

Installed copies only update to the release GitHub marks as **Latest**, and only when the user says yes. So:

1. On GitHub, edit the broken release and tick **Set as a pre-release** (or untick Latest): the previous release becomes Latest again, new downloads and the update check go back to it. Nothing is deleted.
2. Users who already updated: the fix goes out as the next version (`-beta.N+1`). The updater never offers an older version, so a step back is not possible from inside the app; tell them in the release notes and the issue to download the previous exe by hand if they need it now.
3. The site and the README show `site/release.json`: run `node tools/site-pages.mjs --release v<previous> --vt <its result>` so the download lines match the release that is Latest again.

## The recording build

`dotnet publish src/ServerRouteTool.csproj -c Release -p:Recording=true -o <folder>` gives `CS2IPChanger-Recording.exe`: personal data never reaches the screen, and it never updates itself. It isn't part of the releases.
