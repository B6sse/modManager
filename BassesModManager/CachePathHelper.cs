using System;
using System.IO;

namespace BassesModManager
{
    public static class CachePathHelper
    {
        // Shared cache folder for all users: %ProgramData%\BassesModManager\Caches
        public static string GetCacheBasePath()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "BassesModManager");
        }

        public static string GetCacheFilePath() => Path.Combine(GetCacheBasePath(), "Caches", "starwars.cache");

        /// <summary>
        /// Where the mod files live. Deliberately not next to the exe: the app runs
        /// non-elevated and has to be able to delete rejected mods and download the Auric
        /// set, neither of which is possible inside Program Files. The installer grants
        /// Users write access to this folder.
        /// </summary>
        public static string GetModsPath() => Path.Combine(GetCacheBasePath(), "Mods");

        public static void EnsureCachesDirectory()
        {
            var cachesDir = Path.Combine(GetCacheBasePath(), "Caches");
            if (!Directory.Exists(cachesDir))
                Directory.CreateDirectory(cachesDir);
        }

        public static void EnsureModsDirectory()
        {
            var modsDir = GetModsPath();
            if (!Directory.Exists(modsDir))
                Directory.CreateDirectory(modsDir);
        }
    }
}
