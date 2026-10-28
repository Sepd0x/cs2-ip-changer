# Releasing

The app only installs an update whose `CS2IPChanger.exe.sha256` is signed with the project's key. The private half of that key never goes on GitHub, so someone who gets into the repo, its releases or the Actions token still can't ship an exe that installed copies accept.

## Once: the key

```
node tools/release-key.mjs new
```

It asks for a passphrase, saves the private key encrypted in your user folder (`--out <file>` to pick another place, never inside the repo) and writes the public half to `src/Core/UpdateKey.cs`. Commit that file. Keep a copy of the `.pem` offline: without it the installed apps can't update by themselves any more. The tests fail while `UpdateKey.cs` has no key, so no release goes out without one.

## Each release

1. Bump `<Version>` in `src/ServerRouteTool.csproj`, add the CHANGELOG section, commit, push the tag `v<version>`.
2. The Release workflow runs the tests, builds the exe, attests it and puts the exe and its `.sha256` on a **draft** release.
3. On your PC:

   ```
   node tools/release-key.mjs sign v<version>
   ```

   It downloads the exe and the `.sha256`, checks they match and that the exe's attestation comes from this repo's Release workflow, asks for the passphrase, signs, uploads `CS2IPChanger.exe.sha256.sig` and publishes the draft. `--no-publish` leaves it as a draft.

## The recording build

`dotnet publish src/ServerRouteTool.csproj -c Release -p:Recording=true -o <folder>` gives `CS2IPChanger-Recording.exe`: personal data never reaches the screen, and it never updates itself. It isn't part of the releases.
