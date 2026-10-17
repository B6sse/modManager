# In-memory mod loading for Axon: feasibility notes

Branch: `memory-mod-loading`. Written 2026-10-15 after reading the Kyber V2 sources
(ArmchairDevelopers/Kyber, GPL-3.0) and Frosty's executor.

## How Axon loads mods today

Frosty rebuilds the game's data layout on disk: a `ModData\<pack>` folder inside the
game install with directory symlinks to `Data` and `Update`, file symlinks to the
original `.cas` archives, rewritten `.toc`/`.sb` superbundles and `cas.cat` catalogs,
plus new `cas_NN.cas` files holding the mod payload. The game is started with
`-dataPath <that folder>` and Frosty's `CryptBase.dll` shim next to the exe makes it
honour the argument. Consequences: administrator rights for the symlinks and for
writing inside the game folder, one rebuild per mod combination, nothing can change
while the game runs.

## How Kyber V2 does it (Module/Source/ModLoader, C++)

Nothing is written to the game folder. A DLL injected into the game process:

1. Parses `.fbmod` files with `FBRuntimeTweaker` (MIT, vendored under
   `Module/ThirdParty`): `RT_FrostyLoadMod`, `RT_FrostyParseResources`.
2. Mounts the mods folder into the engine's own virtual file system
   (`VirtualFileSystem_mount(vfs, backend, "/kyber_mods")`), so the engine can read
   mod payload through its normal file layer.
3. Hooks about twenty engine functions by hardcoded address in the Battlefront II
   executable: `ResourceManager::beginChunkRead/endChunkRead/pollChunkOperation`,
   `RuntimeDatabaseDomain::readEbxPartitionFromStream`, `EbxPartitionReader::process`,
   `BundleManager::loadBundle`, `MainLoop::initResourceManager`, shader block depot
   fixups, and more. When the engine asks for a chunk or EBX partition a mod replaced,
   the hook answers from memory instead of letting the read reach disk
   (`s_modifiedChunksMap`, `s_pendingChunksMap`, `MemoryUtils::Patch`).
4. Merges bundle and layout manifests in memory (`BundleMerger`, `ManifestMerger`,
   `ResourceMerger`) so added assets show up in the bundles the engine enumerates.
5. Custom handlers for asset types that need merging rather than replacement
   (localization, mesh variation DB, network registry, profile options, UI metadata).
6. Gets into the process early: Kyber's launcher starts the game through Maxima, their
   own GPL replacement for the EA app, and injects the module via Maxima's background
   service before the resource manager initializes.

Hooking uses `safetyhook` (inline hooks with trampolines). Addresses are compile-time
constants for one exe build; the merge code is written against Battlefront II's
manifest-based layout.

## What a port to Battlefront 2015 would need

- **Engine reverse engineering.** Every hooked function and struct layout has to be
  found again in `starwarsbattlefront.exe`. In our favour: the exe is build
  1.0.7.64833 from 2017-01-03 and has not changed since, so addresses found once stay
  valid for everyone. Auric already hooks parts of this exe (client state, present,
  server backend, player weapon), so some SDK knowledge for 2015 exists.
- **A different data format.** Battlefront 2015 is pre-manifest Frostbite: `layout.toc`
  plus per-superbundle `.toc`/`.sb` and `cas.cat` catalogs. Kyber's mergers do not
  apply. The reference for what has to happen in memory is Frosty's
  `FrostyModSupport/Actions/Standard.cs` (1659 lines) and the chunk/catalog handling in
  `FrostyModExecutor`.
- **Early injection.** Auric is injected after the game is up; a mod loader must be in
  before the resource manager initializes. Options: a sideloaded DLL in the game
  folder (the trick Frosty's `CryptBase.dll` already relies on; placed once by the
  installer, which runs elevated), or a Maxima-style launcher that owns process
  creation. Note the EA app relaunches the game itself, so Axon never owns the real
  game process today.
- **fbmod parsing** can be taken from FBRuntimeTweaker as is; the format is the same
  across games.
- **Where it lives.** This is native code in the game process. It belongs in Auric
  (C++), with Axon reduced to the launcher that selects mods and hands the list over.

Licenses line up: Frosty, Kyber and Auric's Kyber heritage are all GPL-3.0;
FBRuntimeTweaker is MIT.

## A cheaper intermediate step: keep Frosty's output, move it out of the game folder

`-dataPath` accepts an absolute path (Frosty CLI docs), so `ModData` can live under
`%LocalAppData%`. That removes all writing inside the game install. What remains is
the symlinks:

- `Data` and `Update` are directory links. Directory junctions do the same job and
  need no privileges.
- The `.cas` files are file symlinks, which need either administrator rights or
  Developer Mode. Two ways out: (a) test whether the engine actually needs them, by
  deleting the `.cas` links from a working ModPack and launching; (b) if it does, a
  small sideloaded DLL that redirects opens of `<ModData>\Patch\...\cas_XX.cas` to the
  original file in the game folder. That is a few hundred lines, not a mod loader.
- `CryptBase.dll` itself still has to sit next to the exe. The installer runs
  elevated and can place it, so no UAC prompt is ever needed at runtime.

Result: no administrator rights after install, mod combinations rebuilt in seconds
by Frosty's existing code, and the ModPack folders out of the game install. Most of
what players notice about Kyber, at a fraction of the cost and risk. True in-memory
loading in Auric can follow as phase two.

## Where the work goes (decided 2026-10-15)

The owner's Auric source (`C:\Auric\Auric2`, private repo B6sse/Auric2) is a C++ CMake DLL
for Battlefront 2015 with 108 known engine addresses, MinHook, a trimmed port of Kyber's
ModLoader (three ResourceManager hooks already found in the 2015 exe), website integration
over HTTPS, a host-side anti-cheat and a C++ launcher that injects by CreateRemoteThread.
Everything native in the plan (mod loader, early start, anti-cheat) belongs there.

Early start without a Maxima-style launcher: ship Auric as a sideloaded DLL in the game
folder, placed once by Axon's installer (which runs elevated). The game loads it at
process start, the same way it loads Frosty's `CryptBase.dll` today, so EA's DRM has
already been shown to tolerate this. The DLL waits for the engine to reach resource
manager init, then hooks. With in-memory loading there is no `-dataPath`, no Frosty shim
and no ModData at all.

Axon's share afterwards: write the selected mod list to a file Auric reads, start the
game plainly through the EA app, show status from Auric, keep the catalog/hash
verification and the auto-update. The Frosty executor, ModPack folders, symlinks and
the admin gate go away.

## Status 2026-10-16: goal 1 done, Axon installs Auric as version.dll

Auric's side is tested in the game: its `Kyber.dll`, placed in the game folder as
`version.dll` (a static import of the game, not a KnownDLL, never touched by Frosty Mod
Manager), is loaded at process start and starts Auric in stages as the engine comes up
(`C:\Auric\Auric2\docs\sideload-plan.md`). Frosty's `CryptBase.dll` stays Frosty's.
An earlier variant shipped Auric under the `CryptBase.dll` name; it worked, but Frosty
Mod Manager overwrites that name on every launch it makes (its copy only compares
sizes), so every switch between the tools cost an administrator prompt. `wintrust.dll`
was tried and failed (KnownDLL by dependency), `version.dll` was the answer.

Axon's part, done on this branch:

- `BassesModManager\Auric\version.dll` is a copy of Auric's public build (since the
  evening of 2026-10-16 the build of Auric commit 891eccb, sha256 `5f35faa9...`, with
  level A and level C; the first bundle, `7714f3ae...`, had the EBX loader only and left
  res and chunks to ModData), copied to `bin\Release\Auric\version.dll` by a content item and
  shipped by the installer as `Auric\*`. CI refuses a developer build by scanning the
  DLL for its command names.
- `FrostyRuntime.InstallAuric` / `IsAuricInstalled` place and hash-check it in the game
  folder before every launch, next to `InstallLoader` for Frosty's shim. Both need an
  elevated run, asked for up front in `MainWindow` when either file is missing or
  differs.
- `LaunchMarker.Write` (called from `LaunchProgressWindow` once the Frosty runtime is up)
  writes `%LocalAppData%\BassesModManager\auric-launch.json` right before each launch:
  `writtenAt` (ISO-8601), `modPackPath` (`<game>\ModData\<pack>`), `mods` (file names in
  apply order), `fbmods` (one `{ "path", "sha256" }` per mod, absolute path and lowercase
  hex of the file on disk, same order; the hash comes from the cache the catalog check
  already filled, so a normal launch pays nothing) and `ebxOnly` (true when every mod
  holds only EBX resources besides its embedded icon and screenshots, read with Frosty's
  `FrostyMod`; res, chunk or bundle changes, legacy mods and unreadable mods make it
  false). Auric treats the file as the launch signal for ten minutes after its write
  time and, since 2026-10-16 (tested in the game with a hand-written marker: two mods,
  four partitions served, intro skipped, no -dataPath, no ModData), reads `fbmods` at S1
  and serves the EBX partitions from memory, skipping a mod whose hash does not match or
  whose profile is not `starwarsbattlefront`. Axon deletes the file when the game exits,
  on a failed launch, at app start and at app exit, so a plain start from the EA app
  stays vanilla.
- The INJECT AURIC button, `DllInjector.cs` and the old `BassesModManager\Kyber.dll`
  are gone.

## Status 2026-10-16, later: Auric loads mods in memory, Axon skips ModData when it can

Auric's loader is complete for what the catalog ships, tested in the game 2026-10-16 with
the public build started from Axon: level A (EBX partitions served from memory by name)
and level C (a virtual CAS with a rewritten `cas.cat` and a virtual `cas_21.cas` for res
and chunk payloads, and superbundles rewritten in memory at `.toc`/`.sb` open, following
Frosty's Modify logic in `Standard.cs`). Improved-Game-Startup and Green Dot (EBX + res +
chunk) load with no ModData, no `-dataPath` and no Frosty executor. Auric's log,
`%APPDATA%\Auric\auric.log`, says `[ModLoader] Mod <title> ...: N EBX partition(s) served
from memory; R res, C chunk(s) through the virtual CAS` per mod and `X of Y mod(s) from
auric-launch.json loaded`. Not covered yet: resources added to a bundle they were not in
before (a non-empty `addedBundles` list) and new bundles (resources of type bundle). No
catalog mod uses either. The Auric-side plan is `C:\Auric\Auric2\docs\memory-mod-loading-plan.md`.

This is step 2 of 3 in retiring Frosty from Axon (step 1: `fbmods` in the marker; step 3:
remove the executor once Auric handles added bundles). Axon's launch rule, in
`MemoryLoading.cs`:

- A mod is **memory-safe** when Frosty's reader opens it (new format, this game's profile)
  and no resource is of type bundle or has a non-empty `AddedBundles` list. The answer is
  cached per file (path, size, write time); the files are read on the verification thread
  at start-up, which is also what brings the Frosty runtime up for the session.
- When **every** selected mod is memory-safe, `LaunchProgressWindow` writes the marker,
  places the two game-folder DLLs as before, and starts the game plainly
  (`FrostyRuntime.StartGame`: the exe with no arguments, the EA app takes over; on Steam
  the `steam://run` URL). No ModPack folder, no executor, no symlinks, no `-dataPath`, and
  no administrator rights for a new mod combination. An empty selection starts plainly too.
- When **at least one** is not, or the Settings switch "Use ModData (Frosty) for all mods"
  (`ForceModData`, off by default) is on, the flow is today's: ModPack folder, executor,
  `-dataPath`. The marker is written with `fbmods` either way.
- The decision is one line, shown under the progress bar for the whole launch and logged
  as a status line: `Starting without ModData: all N mods load in memory`, or
  `Using ModData: <mod> adds resources to bundles` / `adds a bundle` /
  `is in the legacy mod format` / `could not be read (...)` / `forced in Settings`. The
  marker carries it as `launchDecision`, next to `usesModData` (bool) and `modPackPath`
  (null when nothing is built), so a session can be read back afterwards.

Checked headlessly 2026-10-16: all seven catalog mods are memory-safe. Tested in the game
the same evening (owner) with Red Dot + Improved-Game-Startup, a combination no ModPack
held: switch off gave `Starting without ModData: all 2 mods load in memory`, the exe was
started with no arguments, ModData was untouched, Auric logged `2 EBX partition(s) served
from memory; 1 res, 1 chunk(s) through the virtual CAS` for Red Dot and `2 of 2 mod(s)
from auric-launch.json loaded`, intro skipped and red dot in the sight. Switch on gave
`Using ModData: forced in Settings`, a new ModPack, `-dataPath`, and the same result in
the game. Note: the first run of this test loaded the earlier bundle (`7714f3ae...`,
level A only) and logged `not supported yet: 1 res, 1 chunk(s)`; the dot still showed,
since its colour lives in the EBX, and the bundle was replaced the same evening.

## Open questions

1. Is Auric's source a Kyber v1 fork in C++, and which 2015 engine functions does it
   already know?
2. Does the 2015 engine fall back to the original `.cas` when the one referenced from
   `ModData` is missing? (One launch test answers this.)
3. Does `starwarsbattlefront.exe` open `.cas` files through Win32 `CreateFileW`, so a
   redirect hook is enough? (Procmon during a launch answers this.)
