using System;
using System.IO;
using System.Linq;
using FrostySdk.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BassesModManager
{
    /// <summary>
    /// The file Auric reads at process start, before the game's own code runs. It is both
    /// Auric's launch signal (the file's write time counts for ten minutes, so a copy left
    /// behind by a crash cannot turn a later plain start into an Auric one) and the mod list
    /// Auric's in-memory loader serves from.
    /// </summary>
    internal static class LaunchMarker
    {
        public static string Path => CachePathHelper.GetLaunchMarkerPath();

        /// <summary>
        /// Writes the marker for one launch. <paramref name="modFileNames"/> is the apply
        /// order: a later mod overrides an earlier one.
        /// </summary>
        public static void Write(string gamePath, string modsDirectory, string[] modFileNames, ILogger logger)
        {
            string[] modPaths = modFileNames.Select(name => System.IO.Path.Combine(modsDirectory, name)).ToArray();
            bool ebxOnly = MemoryLoading.AreAllEbxOnly(modPaths);

            var marker = new JObject
            {
                ["writtenAt"] = DateTime.UtcNow.ToString("o"),
                // Nothing is built any more; Auric loads from fbmods, not from here
                ["modPackPath"] = JValue.CreateNull(),
                ["mods"] = new JArray(modFileNames),
                // Absolute paths with the hash of the file as it is on disk (cached by path,
                // size and write time, so a normal launch pays nothing). Auric skips a mod
                // whose file no longer hashes to this.
                ["fbmods"] = new JArray(modPaths.Select(path => new JObject
                {
                    ["path"] = path,
                    ["sha256"] = FileHash.OfFileCached(path)
                })),
                ["ebxOnly"] = ebxOnly
            };

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, marker.ToString(Formatting.Indented));

            logger?.Log("Launch marker written: {0} mod(s)", modFileNames.Length);
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
    }
}
