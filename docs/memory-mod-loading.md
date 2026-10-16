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

## Status 2026-10-16: goal 1 done, Axon ships Auric as the shim

Auric's side is tested in the game: its `Kyber.dll`, placed in the game folder as
`CryptBase.dll`, is loaded at process start, hooks `BCryptVerifySignature` like Frosty's
shim did (so ModData mods still load) and starts Auric in stages as the engine comes up
(`C:\Auric\Auric2\docs\sideload-plan.md`).

Axon's part, done on this branch: `BassesModManager\Auric\CryptBase.dll` is a copy of
Auric's build, the `CopyAuricCryptBase` target in the csproj puts it in
`bin\Release\ThirdParty\CryptBase.dll` over Frosty's copy (override with
`-p:AuricCryptBaseDll=<path>`), the installer ships `ThirdParty\*` as before, and
`FrostyRuntime.InstallLoader` places and hash-checks it in the game folder exactly as it
did for Frosty's file. The INJECT AURIC button, `DllInjector.cs` and the old
`BassesModManager\Kyber.dll` are gone. The launch itself is unchanged: `-dataPath` to a
ModPack built by Frosty's executor. To update Auric, copy the new public `Kyber.dll`
(`AURIC_DEV_BUILD=OFF`) over `BassesModManager\Auric\CryptBase.dll` and rebuild; CI
refuses a developer build by scanning the DLL for its command names.

The bundled copy is Auric 1.4.0, the first with the opt-in gate: early mode only when
the game's command line has `-dataPath` or
`%LocalAppData%\BassesModManager\auric-launch.json` exists. A plain start from the EA
app stays passive (proxy exports only), so leaving the file in the game folder no
longer changes normal play. Frosty Mod Manager overwrites it with Frosty's own shim
when it launches (size differs), and Axon puts Auric's back on its next launch; both
need the one administrator prompt they already ask for.

Still open on the Auric side for goal 2: the three ModLoader hooks from K.dll log nothing
even when installed before the engine starts, so the real ResourceManager addresses in
this exe have to be found before in-memory loading can begin.

## Open questions

1. Is Auric's source a Kyber v1 fork in C++, and which 2015 engine functions does it
   already know?
2. Does the 2015 engine fall back to the original `.cas` when the one referenced from
   `ModData` is missing? (One launch test answers this.)
3. Does `starwarsbattlefront.exe` open `.cas` files through Win32 `CreateFileW`, so a
   redirect hook is enough? (Procmon during a launch answers this.)
