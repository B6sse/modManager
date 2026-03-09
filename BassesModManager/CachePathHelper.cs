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

        public static void EnsureCachesDirectory()
        {
            var cachesDir = Path.Combine(GetCacheBasePath(), "Caches");
            if (!Directory.Exists(cachesDir))
                Directory.CreateDirectory(cachesDir);
        }
    }
}
