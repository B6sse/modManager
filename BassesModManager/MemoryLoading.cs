using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Frosty.Core.Mod;
using FrostySdk.Interfaces;

namespace BassesModManager
{
    /// <summary>
    /// Decides how a launch gets its mods into the game. Auric opens the .fbmod files the
    /// launch marker lists and serves everything from memory: EBX, res and chunks, resources
    /// added to bundles they were not in before, whole new bundles, the chunk superbundle's
    /// toc list, and the localization plugin's text changes (which Auric merges itself). So a
    /// launch Auric covers needs no ModData folder, no Frosty executor and no -dataPath.
    /// What sends a launch through ModData is a mod Frosty's reader cannot open at all (legacy
    /// format, another game, a damaged file) or the switch in Settings that forces it.
    /// <para>
    /// A resource with any other handler hash is a Frosty plugin's private data (shader block
    /// depots from the MeshSet plugin, difficulty weapon tables): Auric skips those with a
    /// log line, and the launch goes ahead with a warning naming them.
    /// </para>
    /// <para>
    /// Reading a .fbmod goes through Frosty's reader, which checks the mod's game profile
    /// and therefore needs the Frosty runtime up; the answer is cached per file (path,
    /// size, write time) so a launch pays for the read once.
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

        /// <summary>What a .fbmod holds, as far as the launch rule cares.</summary>
        private sealed class ModContents
        {
            /// <summary>Null when Auric can load the mod in memory; otherwise why not, as a predicate on the mod's name.</summary>
            public string MemoryBlocker;

            /// <summary>True when the mod holds nothing but EBX besides its embedded icon and screenshots.</summary>
            public bool EbxOnly;

            /// <summary>Names of the resources that belong to a Frosty plugin Auric does not do; Auric skips them.</summary>
            public List<string> PluginResources = new List<string>();
        }

        /// <summary>
        /// Decides for one launch. <paramref name="modPaths"/> is the apply order; the first
        /// mod Auric cannot load in memory is the one named in the reason.
        /// </summary>
        public static LaunchPlan Plan(string gamePath, IEnumerable<string> modPaths, bool forceModData)
        {
            string[] paths = modPaths.ToArray();

            if (forceModData)
                return new LaunchPlan(useModData: true, "Using ModData: forced in Settings", new List<string>());

            var warnings = new List<string>();
            foreach (string path in paths)
            {
                ModContents contents = Inspect(gamePath, path);
                string modName = Path.GetFileNameWithoutExtension(path);
                if (contents.MemoryBlocker != null)
                    return new LaunchPlan(useModData: true, "Using ModData: " + modName + " " + contents.MemoryBlocker, new List<string>());

                foreach (string resource in contents.PluginResources)
                    warnings.Add(modName + " uses a Frosty plugin for " + resource + ", that part is not loaded");
            }

            string reason = paths.Length == 0 ? "no mods selected"
                : paths.Length == 1 ? "1 mod loads in memory"
                : "all " + paths.Length + " mods load in memory";
            return new LaunchPlan(useModData: false, "Starting without ModData: " + reason, warnings);
        }

        /// <summary>True when every mod consists of EBX resources only (icons and screenshots do not count).</summary>
        public static bool AreAllEbxOnly(string gamePath, IEnumerable<string> modPaths)
        {
            return modPaths.All(path => Inspect(gamePath, path).EbxOnly);
        }

        /// <summary>
        /// Reads the files ahead of time, on whatever thread verification runs on, so the
        /// launch button has its answer ready. Also what brings the Frosty runtime up for the
        /// session; a failure here is left for the launch to report in its own words.
        /// </summary>
        public static void Warm(string gamePath, IEnumerable<string> modPaths)
        {
            try
            {
                foreach (string path in modPaths)
                    Inspect(gamePath, path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MemoryLoading.Warm: " + ex);
            }
        }

        private static ModContents Inspect(string gamePath, string path)
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
            // selected first. Once the runtime is up this is a lock and a flag.
            FrostyRuntime.EnsureInitialized(QuietLogger.Instance, FrostyRuntime.GetProfileKey(gamePath));

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
                    contents.MemoryBlocker = "is in the legacy mod format";
                    return contents;
                }

                contents.EbxOnly = mod.Resources.All(r => r.Type == ModResourceType.Ebx || r.Type == ModResourceType.Embedded);

                foreach (BaseModResource resource in mod.Resources)
                {
                    if (resource.HasHandler && (uint)resource.Handler != LocalizationHandler)
                        contents.PluginResources.Add(resource.Name);
                }
            }
            catch (Exception ex)
            {
                // Unknown content is not something to hand to the in-memory loader; the
                // ModData flow has Frosty's own error reporting for it
                contents.MemoryBlocker = "could not be read (" + ex.Message + ")";
                contents.EbxOnly = false;
            }
            return contents;
        }

        /// <summary>
        /// The runtime bootstrap logs at most one line, about a missing Plugins folder, and
        /// outside the progress windows there is nothing to show it in.
        /// </summary>
        private sealed class QuietLogger : ILogger
        {
            public static readonly QuietLogger Instance = new QuietLogger();

            public void Log(string text, params object[] vars) => Debug.WriteLine(string.Format(text, vars));
            public void LogWarning(string text, params object[] vars) => Log(text, vars);
            public void LogError(string text, params object[] vars) => Log(text, vars);
        }
    }

    /// <summary>What one launch does about ModData, the one line that says why, and what the user should hear first.</summary>
    public sealed class LaunchPlan
    {
        public LaunchPlan(bool useModData, string reason, List<string> warnings)
        {
            UseModData = useModData;
            Reason = reason;
            Warnings = warnings;
        }

        /// <summary>True for the Frosty flow: ModPack folder, executor, -dataPath. False starts the game plainly.</summary>
        public bool UseModData { get; }

        /// <summary>"Starting without ModData: ..." or "Using ModData: ...", for the status line and the marker.</summary>
        public string Reason { get; }

        /// <summary>
        /// One line per resource a Frosty plugin would have applied and Auric skips. Shown to
        /// the user before the launch; it goes ahead regardless.
        /// </summary>
        public List<string> Warnings { get; }
    }
}
