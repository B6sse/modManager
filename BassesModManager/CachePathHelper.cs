using System;
using System.IO;

namespace BassesModManager
{
    /// <summary>
    /// The two folders this app keeps outside its own install: one shared by all users
    /// under ProgramData (mods, update downloads), one per user under LocalAppData (the
    /// launch marker). Named for the asset cache it once held; the cache is gone, the paths
    /// stayed so existing installs keep finding their files.
    /// </summary>
    public static class CachePathHelper
    {
        // Shared folder for all users: %ProgramData%\BassesModManager
        public static string GetCacheBasePath()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "BassesModManager");
        }

        /// <summary>
        /// Where the mod files live. Deliberately not next to the exe: the app runs
        /// non-elevated and has to be able to delete rejected mods and download the Auric
        /// set, neither of which is possible inside Program Files. The installer grants
        /// Users write access to this folder.
        /// </summary>
        public static string GetModsPath() => Path.Combine(GetCacheBasePath(), "Mods");

        /// <summary>Per-user folder next to the app's user settings: %LocalAppData%\BassesModManager.</summary>
        public static string GetUserDataPath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "BassesModManager");
        }

        /// <summary>
        /// The file that tells Auric (the DLL the game loads from its folder) that this app
        /// started the game, and with which mods. Auric only runs its early mode when this
        /// file exists; a plain start from the EA app has none and stays vanilla. Written
        /// right before a launch, removed when the game exits, at app start and at app exit,
        /// so it never outlives the session it was for.
        /// </summary>
        public static string GetLaunchMarkerPath() => Path.Combine(GetUserDataPath(), "auric-launch.json");

        public static void EnsureModsDirectory()
        {
            var modsDir = GetModsPath();
            if (!Directory.Exists(modsDir))
                Directory.CreateDirectory(modsDir);
        }
    }
}
