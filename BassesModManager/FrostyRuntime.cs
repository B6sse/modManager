using System;
using System.IO;
using System.Linq;
using Frosty.Core;
using FrostySdk.Interfaces;

namespace BassesModManager
{
    /// <summary>
    /// One-time bootstrap of the shared Frosty runtime (plugin manager, profiles, config)
    /// plus helpers tied to the game profile. Used by both the cache install flow and the
    /// launch flow so the logic lives in one place.
    /// </summary>
    public static class FrostyRuntime
    {
        public const string DefaultProfileKey = "StarWarsBattlefront";

        private static readonly object initLock = new object();
        private static bool initialized;

        // The config key must match what Frosty Mod Manager itself would use for the same
        // game: the actual exe filename (verbatim case) from the game folder. A hardcoded
        // literal here previously made the game show up twice in FMM when the on-disk exe
        // was cased differently.
        public static string GetProfileKey(string gamePath)
        {
            try
            {
                string exe = Directory.EnumerateFiles(gamePath, "*.exe")
                    .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), DefaultProfileKey, StringComparison.OrdinalIgnoreCase));
                if (exe != null)
                    return Path.GetFileNameWithoutExtension(exe);
            }
            catch
            {
            }
            return DefaultProfileKey;
        }

        public static void EnsureInitialized(ILogger logger, string profileKey)
        {
            lock (initLock)
            {
                if (!initialized)
                {
                    var pluginManager = new PluginManager(logger, PluginManagerType.ModManager);
                    Frosty.Core.App.PluginManager = pluginManager;
                    FrostySdk.ProfilesLibrary.Initialize(pluginManager.Profiles);

                    // Ensure Frosty config dir and file exist before Config.Load() (first-run fix)
                    string configDir = Frosty.Core.App.GlobalSettingsPath;
                    string configFile = Path.Combine(configDir, "manager_config.json");
                    if (!Directory.Exists(configDir))
                        Directory.CreateDirectory(configDir);
                    if (!File.Exists(configFile))
                        File.WriteAllText(configFile, "{\n  \"Games\": {},\n  \"GlobalOptions\": {}\n}");

                    Config.Load();
                    initialized = true;
                }
            }

            FrostySdk.ProfilesLibrary.Initialize(profileKey);
        }

        public static void EnsureGameRegistered(string profileKey, string gamePath)
        {
            // Drop the legacy hardcoded key so the game doesn't appear twice in FMM's list
            if (!string.Equals(profileKey, DefaultProfileKey, StringComparison.Ordinal) &&
                Config.Current.Games.ContainsKey(DefaultProfileKey))
            {
                Config.RemoveGame(DefaultProfileKey);
            }

            if (!Config.Current.Games.ContainsKey(profileKey))
                Config.AddGame(profileKey, gamePath);
        }
    }
}
