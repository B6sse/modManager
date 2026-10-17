using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Frosty.Core.Mod;

namespace BassesModManager
{
    /// <summary>
    /// Looks inside the mods a launch applies. Auric opens the .fbmod files the launch
    /// marker lists and serves everything from memory: EBX, res and chunks, resources added
    /// to bundles they were not in before, whole new bundles, the chunk superbundle's toc
    /// list, and the localization plugin's text changes (which Auric merges itself). What it
    /// does not do is any other Frosty plugin's private data (shader block depots from the
    /// MeshSet plugin, difficulty weapon tables): such resources are skipped with a log line,
    /// and the launch goes ahead with a warning naming them. A mod Frosty's reader cannot
    /// open at all (legacy format, another game, a damaged file) stops the launch, since
    /// Auric could not read it either.
    /// <para>
    /// Reading a .fbmod goes through Frosty's reader, which checks the mod's game profile
    /// and therefore needs the profile selected; the answer is cached per file (path, size,
    /// write time) so a launch pays for the read once.
    /// </para>
    /// </summary>
    internal static class MemoryLoading
    {
        /// <summary>
        /// Frosty's Fnv1 of "fsuitextdatabase": the localization plugin's handler. Its payload
        /// is text changes, which Auric applies itself; every other handler is skipped.
        /// </summary>
        private const uint LocalizationHandler = 0xDECFF314;

        private static readonly object cacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private struct CacheEntry
        {
            public long Length;
            public DateTime LastWriteUtc;
            public ModContents Contents;
        }

        /// <summary>What a .fbmod holds, as far as the launch cares.</summary>
        private sealed class ModContents
        {
            /// <summary>Null when Auric can read the mod; otherwise why not, as a predicate on the mod's name.</summary>
            public string Error;

            /// <summary>Names of the resources that belong to a Frosty plugin Auric does not do; Auric skips them.</summary>
            public List<string> PluginResources = new List<string>();
        }

        /// <summary>
        /// Checks one launch. <paramref name="modPaths"/> is the apply order; the first mod
        /// that cannot be read is the one named in the error.
        /// </summary>
        public static LaunchPlan Plan(IEnumerable<string> modPaths)
        {
            string[] paths = modPaths.ToArray();

            var warnings = new List<string>();
            foreach (string path in paths)
            {
                ModContents contents = Inspect(path);
                string modName = Path.GetFileNameWithoutExtension(path);
                if (contents.Error != null)
                    return LaunchPlan.Refused(modName + " " + contents.Error);

                foreach (string resource in contents.PluginResources)
                    warnings.Add(modName + " uses a Frosty plugin for " + resource + ", that part is not loaded");
            }

            string reason = paths.Length == 0 ? "Starting without mods"
                : paths.Length == 1 ? "Loading 1 mod in memory"
                : "Loading " + paths.Length + " mods in memory";
            return LaunchPlan.Ready(reason, warnings);
        }

        /// <summary>
        /// Reads the files ahead of time, on whatever thread verification runs on, so the
        /// launch button has its answer ready. A failure here is left for the launch to
        /// report in its own words.
        /// </summary>
        public static void Warm(IEnumerable<string> modPaths)
        {
            try
            {
                foreach (string path in modPaths)
                    Inspect(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MemoryLoading.Warm: " + ex);
            }
        }

        private static ModContents Inspect(string path)
        {
            var info = new FileInfo(path);
            string key = info.FullName;

            lock (cacheLock)
            {
                if (cache.TryGetValue(key, out CacheEntry cached) &&
                    cached.Length == info.Length && cached.LastWriteUtc == info.LastWriteTimeUtc)
                {
                    return cached.Contents;
                }
            }

            // Frosty's reader rejects a mod for another game profile, so the profile has to be
            // selected first. After the first time this is a lock and a flag.
            FrostyRuntime.EnsureInitialized();

            ModContents contents = Read(path);

            lock (cacheLock)
            {
                cache[key] = new CacheEntry
                {
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    Contents = contents
                };
            }

            return contents;
        }

        private static ModContents Read(string path)
        {
            var contents = new ModContents();
            try
            {
                var mod = new FrostyMod(path);
                if (!mod.NewFormat)
                {
                    // Also what the reader reports for a mod built for another game; neither
                    // is something Auric's loader reads
                    contents.Error = "is in the legacy mod format, which Auric does not load";
                    return contents;
                }

                foreach (BaseModResource resource in mod.Resources)
                {
                    if (resource.HasHandler && (uint)resource.Handler != LocalizationHandler)
                        contents.PluginResources.Add(resource.Name);
                }
            }
            catch (Exception ex)
            {
                contents.Error = "could not be read (" + ex.Message + ")";
            }
            return contents;
        }
    }

    /// <summary>The verdict on one launch: a line for the progress window, warnings the user should see first, or why it cannot start.</summary>
    public sealed class LaunchPlan
    {
        private LaunchPlan(string reason, List<string> warnings, string error)
        {
            Reason = reason;
            Warnings = warnings;
            Error = error;
        }

        public static LaunchPlan Ready(string reason, List<string> warnings) => new LaunchPlan(reason, warnings, null);

        public static LaunchPlan Refused(string error) => new LaunchPlan(null, new List<string>(), error);

        /// <summary>"Loading N mods in memory", shown under the progress bar.</summary>
        public string Reason { get; }

        /// <summary>
        /// One line per resource a Frosty plugin would have applied and Auric skips. Shown to
        /// the user before the launch; it goes ahead regardless.
        /// </summary>
        public List<string> Warnings { get; }

        /// <summary>Null when the launch can go ahead; otherwise the mod and what is wrong with it.</summary>
        public string Error { get; }
    }
}
