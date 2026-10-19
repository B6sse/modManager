using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace BassesModManager
{
    /// <summary>
    /// Everything tied to the game's install folder: telling whether a folder is the game,
    /// the Auric DLL that has to sit next to the game's exe, and starting the game.
    /// </summary>
    public static class GameFolder
    {
        /// <summary>The game's exe without extension, as the app expects it; the on-disk casing is read with <see cref="GetExeName"/>.</summary>
        public const string ExeBaseName = "StarWarsBattlefront";

        // The exe filename (verbatim case) from the game folder: the name of the process to
        // watch for, and the file to start.
        public static string GetExeName(string gamePath)
        {
            try
            {
                string exe = Directory.EnumerateFiles(gamePath, "*.exe")
                    .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), ExeBaseName, StringComparison.OrdinalIgnoreCase));
                if (exe != null)
                    return Path.GetFileNameWithoutExtension(exe);
            }
            catch
            {
            }
            return ExeBaseName;
        }

        // Used to validate a folder before accepting it as the game install (both when the
        // user picks one and when re-checking a saved path) - the app only ever supports
        // this one game, so anything else should be rejected rather than silently accepted.
        public static bool IsValidBattlefrontInstall(string gamePath)
        {
            try
            {
                return !string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath) &&
                       Directory.EnumerateFiles(gamePath, "*.exe")
                           .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f), ExeBaseName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        // Auric, the in-game half. version.dll is a static import of the game itself, not a
        // KnownDLL, so the game loads it from its own folder at process start and Auric is in
        // from the first instruction; nothing is injected afterwards. It decides whether to
        // run from the launch marker this app writes (CachePathHelper.GetLaunchMarkerPath);
        // a plain start from the EA app leaves it passive. It is also what lets the engine
        // accept data that is not EA's (it answers the toc signature check itself), which is
        // what Frosty's CryptBase.dll shim used to do. The file is placed and hash-checked
        // before every launch, and writing it needs an elevated run, since the game lives in
        // Program Files.
        public const string AuricFileName = "version.dll";

        public static string GetAuricSourcePath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Auric", AuricFileName);

        public static string GetAuricTargetPath(string gamePath) => Path.Combine(gamePath, AuricFileName);

        /// <summary>True when the game folder holds exactly the Auric build this app ships.</summary>
        public static bool IsAuricInstalled(string gamePath) => IsGameFileCurrent(GetAuricSourcePath(), GetAuricTargetPath(gamePath));

        /// <summary>Puts Auric in the game folder if it is missing or differs. Needs elevation.</summary>
        public static void InstallAuric(string gamePath) => InstallGameFile(GetAuricSourcePath(), GetAuricTargetPath(gamePath));

        public static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static bool IsGameFileCurrent(string source, string target)
        {
            try
            {
                return File.Exists(source) && File.Exists(target) &&
                       string.Equals(FileHash.OfFileCached(source), FileHash.OfFileCached(target), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Callers check elevation up front rather than let the copy throw; a missing source
        /// is a broken install and is reported as such.
        /// </summary>
        private static void InstallGameFile(string source, string target)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException(Path.GetFileName(source) + " is missing from the app folder. Reinstall the app to restore it.", source);

            if (!IsGameFileCurrent(source, target))
                File.Copy(source, target, true);
        }

        /// <summary>Steam copies of the game carry this file.</summary>
        public static bool IsSteamInstall(string gamePath) => File.Exists(Path.Combine(gamePath, "steam_appid.txt"));

        /// <summary>
        /// Starts the game the way a player would, with no arguments: the exe hands itself
        /// to the EA app, which starts the real game process, and on Steam the run URL does
        /// the same through Steam. The game then reads its own files, and Auric, loaded with
        /// it as version.dll, lays the mods from the launch marker over them in memory.
        /// </summary>
        public static void StartGame(string gamePath)
        {
            if (IsSteamInstall(gamePath))
            {
                string steamAppId = File.ReadLines(Path.Combine(gamePath, "steam_appid.txt")).First().Trim();
                using (Process.Start(new ProcessStartInfo("steam://run/" + steamAppId) { UseShellExecute = true }))
                {
                }
                return;
            }

            string exe = Path.Combine(gamePath, GetExeName(gamePath) + ".exe");
            using (Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = gamePath, UseShellExecute = false }))
            {
            }
        }
    }
}
