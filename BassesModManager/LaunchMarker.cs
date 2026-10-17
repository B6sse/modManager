using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Frosty.Core.Mod;
using FrostySdk.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BassesModManager
{
    /// <summary>
    /// The file Auric reads at process start, before the game's own code runs. It is both
    /// Auric's launch signal (the file's write time counts for ten minutes, so a copy left
    /// behind by a crash cannot turn a later plain start into an Auric one) and the mod list
    /// Auric's in-memory loader serves from. Written with the Frosty runtime up, because
    /// telling what a .fbmod contains means reading its header with Frosty's reader.
    /// </summary>
    internal static class LaunchMarker
    {
        public static string Path => CachePathHelper.GetLaunchMarkerPath();

        /// <summary>
        /// Writes the marker for one launch. <paramref name="modFileNames"/> is the apply
        /// order, the same order Frosty's executor gets: a later mod overrides an earlier one.
        /// </summary>
        public static void Write(string gamePath, string modsDirectory, string modPackName, string[] modFileNames, ILogger logger)
        {
            string[] modPaths = modFileNames.Select(name => System.IO.Path.Combine(modsDirectory, name)).ToArray();
            bool ebxOnly = AreAllEbxOnly(modPaths, logger);

            var marker = new JObject
            {
                ["writtenAt"] = DateTime.UtcNow.ToString("o"),
                ["modPackPath"] = System.IO.Path.Combine(gamePath, "ModData", modPackName),
                ["mods"] = new JArray(modFileNames),
                // Absolute paths with the hash of the file as it is on disk (cached by path,
                // size and write time, so a normal launch pays nothing). Auric skips a mod
                // whose file no longer hashes to this.
                ["fbmods"] = new JArray(modPaths.Select(path => new JObject
                {
                    ["path"] = path,
                    ["sha256"] = FileHash.OfFileCached(path)
                })),
                // Groundwork: a launch whose mods only touch EBX could one day skip ModData
                // and -dataPath altogether, since Auric already serves EBX from memory
                ["ebxOnly"] = ebxOnly
            };

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, marker.ToString(Formatting.Indented));

            logger?.Log("Launch marker written: {0} mod(s), EBX-only: {1}", modFileNames.Length, ebxOnly ? "yes" : "no");
        }

        /// <summary>
        /// Removes the marker. Called whenever the session it described is over, so a later
        /// start from the EA app does not find it and bring Auric up uninvited.
        /// </summary>
        public static void Delete()
        {
            try
            {
                File.Delete(Path);
            }
            catch
            {
            }
        }

        /// <summary>
        /// True when every mod consists of EBX resources only (icons and screenshots are
        /// embedded data and do not count). Res, chunk or bundle changes make it false, as
        /// does a mod Frosty's reader cannot open: unknown content is not EBX-only. Needs the
        /// Frosty runtime initialized, since the reader checks the mod's game profile.
        /// </summary>
        public static bool AreAllEbxOnly(IEnumerable<string> modPaths, ILogger logger)
        {
            bool all = true;
            foreach (string path in modPaths)
            {
                try
                {
                    var mod = new FrostyMod(path);
                    if (!mod.NewFormat)
                    {
                        logger?.LogWarning("{0}: legacy mod format, not EBX-only", System.IO.Path.GetFileName(path));
                        all = false;
                        continue;
                    }

                    var other = mod.Resources
                        .Where(r => r.Type != ModResourceType.Ebx && r.Type != ModResourceType.Embedded)
                        .Select(r => r.Type)
                        .Distinct()
                        .ToList();
                    if (other.Count > 0)
                    {
                        logger?.Log("{0}: not EBX-only ({1})", System.IO.Path.GetFileName(path), string.Join(", ", other));
                        all = false;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning("{0}: could not read mod ({1}), not EBX-only", System.IO.Path.GetFileName(path), ex.Message);
                    all = false;
                }
            }
            return all;
        }
    }
}
