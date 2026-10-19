# Axon

A mod manager for **Star Wars Battlefront (2015)** built for competitive play: players
pick from a fixed, SHA-256-verified set of approved mods, nothing can be imported, and the
game is started with the mods loaded in memory by Auric, the in-game half.

## Download

Grab the latest installer from [Releases](https://github.com/B6sse/modManager/releases)
and run the newest `.exe`. The app updates itself afterwards.

## How it works

- Axon keeps the approved `.fbmod` files under `%ProgramData%\BassesModManager\Mods` and
  verifies each against the catalog's hash before it can be used.
- On launch it places Auric's `version.dll` next to the game's exe (once per Auric
  update, with an administrator prompt), writes the selected mods to
  `%LocalAppData%\BassesModManager\auric-launch.json` and starts the game plainly. The
  game loads Auric at process start, and Auric lays the mods over the game's files in
  memory: no files are written to the game install.
- Details, what Auric supports and how to test: [`docs/memory-mod-loading.md`](docs/memory-mod-loading.md).

## Building

Visual Studio 2022 or newer with .NET Framework 4.8:

```
msbuild BassesModManager\BassesModManager.sln -t:Restore,Build -p:Configuration=Release -p:Platform="Any CPU"
```

The solution is one C# WPF project. The installer is built with Inno Setup from
`BassesModManager\installer.iss`; CI (`.github/workflows/build-installer.yml`) does
both and refuses a developer build of Auric.

## History

Axon started as a trimmed fork of the Frosty Toolsuite (GPL-3.0,
https://github.com/CadeEvs/FrostyToolsuite) and used Frosty's executor to build a
`ModData` folder the game was started against. Since 2026-10-18 no Frosty code is left:
Axon reads the `.fbmod` header itself and Auric applies the mods. The last version with
the disk path is kept as the tag `disk-loading-last` and the branch
`frosty-disk-loading`.

## License

GPL-3.0, see the LICENSE file.
