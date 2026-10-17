# In-memory mod loading: how Axon launches the game

Branch: `memory-mod-loading`. Current state first; the history that led here is at the
end. The in-game half, Auric, lives in `C:\Auric\Auric2` (private repo B6sse/Auric2); its
plan and test log is `docs\memory-mod-loading-plan.md` there (section 0 is the handoff).

## What Axon does on launch (since 2026-10-17)

Frosty's executor, the `ModData` folders, the symlinks, `-dataPath` and Frosty's
`CryptBase.dll` shim are gone. A launch is three steps, all in `LaunchProgressWindow`:

1. **Auric into the game folder.** `FrostyRuntime.InstallAuric` copies
   `BassesModManager\Auric\version.dll` to `<game>\version.dll` when the hash differs.
   `version.dll` is a static import of the game and not a KnownDLL, so the game loads it
   from its own folder at process start; Auric is in from the first instruction, nothing
   is injected. It also answers the engine's toc signature check itself, which is all
   Frosty's shim ever did. Writing inside Program Files needs an elevated run, asked for
   up front in `MainWindow` only when the file is missing or differs (so once per Auric
   update, never for a new mod combination).
2. **The launch marker.** `LaunchMarker.Write` writes
   `%LocalAppData%\BassesModManager\auric-launch.json`:
   ```json
   { "writtenAt": "<ISO-8601 UTC>",
     "fbmods": [ { "path": "C:\\ProgramData\\BassesModManager\\Mods\\<file>.fbmod", "sha256": "<hex>" }, ... ] }
   ```
   `fbmods` is the apply order (a later mod overrides an earlier one), absolute paths,
   lowercase hex of the file on disk from the `FileHash` cache. Auric treats the file as
   its launch signal for ten minutes after its write time and skips a mod whose hash does
   not match or whose profile is not `starwarsbattlefront`. Axon deletes the file when the
   game exits, on a failed launch, at app start and at app exit, so a plain start from the
   EA app stays vanilla.
3. **The game, plainly.** `FrostyRuntime.StartGame` starts `starwarsbattlefront.exe` with
   no arguments (the EA app takes over), or `steam://run/<appid>` on a Steam install.
   Auric reads the marker at its S1 stage, parses each `.fbmod` itself and lays the mods
   over the game's files in memory through the engine's own virtual file system.

Before the progress window opens, `MemoryLoading.Plan` reads each selected `.fbmod` with
Frosty's reader (`FrostyMod`, which needs the game profile selected in `ProfilesLibrary`;
`FrostyRuntime.EnsureInitialized` does only that, no plugins, SDK files or Frosty config
involved). The answer is cached per file (path, size, write time) and warmed on the
verification thread at start-up. A mod the reader cannot open (legacy format, another
game, a damaged file) stops the launch with a message; resources that only a Frosty
plugin could apply (see below) raise a one-time dialog naming them, and the launch goes
ahead. An empty selection starts the game unmodded through the same path.

What is left of Frosty in the app: `FrostyCore` and `FrostySdk` for reading `.fbmod`
headers, `FrostyControls` for two controls in the progress panel and the update bar,
`FrostyHash` as their native dependency. `FrostyModSupport`, the plugins, the asset cache
(`CacheInstallWindow`) and `ThirdParty\*` are no longer built or shipped.

## What Auric supports, and what it does not

Auric (build of commit `722723e` from 2026-10-17 01:18, bundled the same night, sha256
`50f820f2...`) loads from the `.fbmod` files:

- EBX, res and chunks that replace existing ones, served through a virtual CAS (a
  rewritten `cas.cat` and a virtual `cas_NN.cas`) and superbundles rewritten in memory
  when the engine opens their `.toc`/`.sb`, following Frosty's `Standard.cs` Modify
  logic. Tested in the game 2026-10-16 (Red Dot, Improved-Game-Startup).
- Resources added to bundles they were not in before (`addedBundles`) and the chunk list
  of `Win32/chunks0.toc`, following Frosty's Add actions. Tested in the game 2026-10-17
  with the Axion set (axonmod p1/p2/p3: new weapons with the right meshes). The first
  bundled build with add actions (`cdca9ee`, `3f11e287...`) crashed the game with the
  set: a mesh has an ebx and a res under one name, the res overwrote the ebx in the
  registry and the added meshes never loaded; `722723e` keeps them apart.
- The localization plugin's text changes (handler `0xDECFF314`, Frosty's Fnv1 of
  `fsuitextdatabase`): Auric merges them into the language's text chunk itself. Tested
  in the game 2026-10-17 with the Axion set.

Written but untested: whole new bundles (`.fbmod` bundle resources); no mod has one.

Not supported: resources of any other Frosty plugin (ShaderBlockDepot from the MeshSet
plugin, DifficultyWeaponTableData). Auric skips them with a log line; Axon warns before
the launch (`<mod> uses a Frosty plugin for <resource>, that part is not loaded`). No
catalog mod has any today: the Axion set holds EBX, res, chunks, 151 resources added to
bundles and one localization resource per part.

## How to test

1. Build (`MSBuild ... -p:PlatformToolset=v145`, see CLAUDE.md) and start
   `bin\Release\BassesModManager.exe` as administrator the first time after an Auric
   update, so it can replace `<game>\version.dll`.
2. Switch the mods on, press LAUNCH. The progress window says `Loading N mods in memory`
   and the game starts with no `-dataPath` (check the process command line); no
   `ModData` folder is created or touched.
3. While the game runs, `%LocalAppData%\BassesModManager\auric-launch.json` holds
   `writtenAt` and the `fbmods` list; it is gone after the game exits.
4. `%APPDATA%\Auric\auric.log` (readable while the game runs) should show, in order:
   `==== Auric ... loaded: early (auric-launch.json present)`, one Info line per mod
   `[ModLoader] Mod <title>: N EBX, R res, C chunk(s) served from memory[; A of them
   added to bundles][; B new bundle(s)][; S set(s) of text changes merged]`, then
   `[ModLoader] X of Y mod(s) from auric-launch.json loaded`. For the Axion set expect
   `Mod axionmod p1 ...: 162 EBX, 20 res, 50 chunk(s) served from memory; 150 of them
   added to bundles; 1 set(s) of text changes merged` (162, not 163: the localization
   EBX is counted under text changes), `[Localization] en: 37 string(s) from axionmod
   p1, axionmod p2 merged into the game's text (35 new, 2 changed; chunk 319869 ->
   322267 bytes)` and, as the game opens them, `[BundlePatcher]` Info lines for
   `globals`, `chunks0`, `loc/en`, `frontend`, `weapons` and `characters`. No Warning
   lines from `[ModLoader]`, `[BundlePatcher]`, `[VirtualCas]` or `[Localization]`. A
   skipped plugin resource shows up as `N resource(s) need a Frosty plugin, skipped`.
   A hard crash leaves no `Unloading` line; Windows Error Reporting (Application log,
   `Application Error`) then has the faulting offset in the exe, image base
   `0x140000000`.
5. In the game: Improved-Game-Startup skips the intro, the crosshair dot has the chosen
   colour, the Auric set's new weapons have their names (the localization merge).

Headless check of the launch rule without starting anything: compile a small console
program against `bin\Release\BassesModManager.exe` that calls `MemoryLoading.Plan` by
reflection (it must run from `bin\Release`); done on 2026-10-17 over every installed mod,
all readable, no plugin warnings.

## Updating Auric

`BassesModManager\Auric\update-auric.ps1` copies a public `Kyber.dll` (default: the local
`x64-Release` build) into the repo after the same developer-build scan CI runs, and
prints the hash and the Auric commit; see `BassesModManager\Auric\README.md`. Rebuild,
commit, note the hash here. Players get the file on their next launch, with one
administrator prompt.

## History

- **2026-10-15.** Feasibility notes after reading Kyber V2 (ArmchairDevelopers/Kyber,
  GPL-3.0, `Module/Source/ModLoader`) and Frosty's executor. Kyber hooks about twenty
  engine functions by address in Battlefront II and merges manifests in memory; for
  Battlefront 2015 (pre-manifest Frostbite: `layout.toc`, per-superbundle `.toc`/`.sb`,
  `cas.cat`) the reference for what has to happen in memory is Frosty's
  `FrostyModSupport/Actions/Standard.cs`. The exe is build 1.0.7.64833 from 2017 and
  never changed, so addresses found once hold for everyone. Decided: the native side
  belongs in Auric (C++, already hooking this exe), Axon becomes the launcher that
  selects mods and hands the list over. A cheaper intermediate step (Frosty's output
  moved out of the game folder, junctions instead of symlinks) was noted and not needed.
- **2026-10-16, goal 1.** Auric shipped as a sideloaded DLL. `CryptBase.dll` worked but
  Frosty Mod Manager overwrites that name on every launch it makes; `wintrust.dll` is a
  KnownDLL by dependency; `version.dll` was the answer. Axon installs it before every
  launch when the hash differs; the INJECT AURIC button, `DllInjector.cs` and the old
  `Kyber.dll` went away. The marker grew `fbmods` with hashes.
- **2026-10-16, evening.** Auric's level C (virtual CAS, superbundle rewrite) tested in
  the game. Axon's step 2: a launch rule that skipped ModData when every mod was
  "memory-safe" (no `addedBundles`, no bundle resources), with a Settings switch to force
  the old flow. Tested in the game with Red Dot + Improved-Game-Startup both ways; the
  first run loaded an older bundle with the EBX loader only (`not supported yet: 1 res,
  1 chunk(s)`), the dot still showed since its colour lives in the EBX, and the bundle
  was replaced the same evening.
- **2026-10-17.** Auric added the Add actions, added bundles, the toc chunk list and the
  localization plugin, and dropped its level A (level C serves the same EBX through the
  bundles). Axon's step 3: the launch rule, the switch, the executor, ModData, the shim,
  the plugins and the asset cache removed; the marker trimmed to `writtenAt` + `fbmods`.
  The first launch of the Axion set through this flow crashed the game ten seconds after
  Auric's init (access violation at `0x143E97384`, no Warning in Auric's log); isolated
  on the Auric side to a mesh's res overwriting its ebx in the registry, fixed in
  `722723e`, which also logs the superbundle and localization lines at Info.
