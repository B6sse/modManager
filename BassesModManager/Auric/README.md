# Auric\version.dll

A copy of `Kyber.dll` from the Auric repo's **public** build (`C:\Auric\Auric2`,
`out\build\x64-Release`, `AURIC_DEV_BUILD=OFF`; or the `Auric-dll` CI artifact). Axon
installs it next to the game's exe as `version.dll`; the game loads it at process start
and it is what loads the mods listed in `auric-launch.json` in memory. The developer
build (`x64-Dev`) has console commands a host could use against players and must never
ship; CI scans the DLL for their names and fails the build if any is present.

**To update:** build Auric (public), then run

```
powershell -ExecutionPolicy Bypass -File BassesModManager\Auric\update-auric.ps1
```

It runs CI's scan, copies the DLL here and into `bin\Release\Auric`, and prints the
SHA-256 and the Auric commit. Rebuild, commit (`chore: bundle Auric <commit>`), and note
the hash in `docs\memory-mod-loading.md`. Players get the new file on their next launch
(one administrator prompt, since the game lives in Program Files).

Keep this in step with the Auric repo: a mod loader change on the Auric side does
nothing for players until the build is bundled here.
