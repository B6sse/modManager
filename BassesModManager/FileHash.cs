using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace BassesModManager
{
    /// <summary>
    /// SHA-256 of files on disk, behind the app's "is this file approved" checks.
    /// </summary>
    internal static class FileHash
    {
        // SHA256.Create() returns the managed implementation, which hashes in software and
        // ignores the CPU's SHA instructions: it needed ~6.6s for the mods folder where the
        // CNG one needs ~0.3s. Same algorithm and same digest either way. The 4KB default
        // FileStream buffer accounted for a good part of the rest.
        private const int ReadBufferSize = 1024 * 1024;

        private static readonly object cacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private struct CacheEntry
        {
            public long Length;
            public DateTime LastWriteUtc;
            public string Hash;
        }

        /// <summary>
        /// Opens a file for hashing. FileShare.Read keeps other readers working while
        /// blocking anything that would write, rename or delete it, so a caller that holds
        /// the returned stream can rely on the bytes it hashed staying put.
        /// </summary>
        public static FileStream OpenForReading(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.SequentialScan);
        }

        public static string Compute(Stream stream)
        {
            using (var sha256 = new SHA256Cng())
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Hash of a file, read fresh. Use for one-off checks of a file that just changed.</summary>
        public static string OfFile(string path)
        {
            using (FileStream stream = OpenForReading(path))
                return Compute(stream);
        }

        /// <summary>
        /// Hash of a file, reusing the previous result for as long as the file's size and
        /// timestamp are unchanged. MainWindow is rebuilt from scratch every time the user
        /// comes back from the settings page, and re-reading half a gigabyte of mods on
        /// each of those is what made the app feel slow.
        /// </summary>
        public static string OfFileCached(string path)
        {
            var info = new FileInfo(path);
            string key = info.FullName;

            lock (cacheLock)
            {
                if (cache.TryGetValue(key, out CacheEntry cached) &&
                    cached.Length == info.Length && cached.LastWriteUtc == info.LastWriteTimeUtc)
                {
                    return cached.Hash;
                }
            }

            string hash;
            using (FileStream stream = OpenForReading(path))
                hash = Compute(stream);

            lock (cacheLock)
            {
                cache[key] = new CacheEntry
                {
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    Hash = hash
                };
            }

            return hash;
        }
    }
}
