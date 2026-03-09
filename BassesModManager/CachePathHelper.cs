using System;
using System.IO;

namespace BassesModManager
{
    /// <summary>
    /// Cache path - uses BaseDirectory/Caches like Frosty Mod Manager (works without admin when app is in user-writable location).
    /// </summary>
    public static class CachePathHelper
    {
        public static string GetCacheBasePath() => AppDomain.CurrentDomain.BaseDirectory;

        public static string GetCacheFilePath() => Path.Combine(GetCacheBasePath(), "Caches", "starwars.cache");

        /// <summary>
        /// Ensures Caches dir exists. Frosty SDK uses relative "Caches/..." so we rely on BaseDirectory.
        /// </summary>
        public static void EnsureCachesDirectory()
        {
            var cachesDir = Path.Combine(GetCacheBasePath(), "Caches");
            if (!Directory.Exists(cachesDir))
                Directory.CreateDirectory(cachesDir);
        }
    }
}
