using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;

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
        // run from the launch marker this app writes (AppPaths.GetLaunchMarkerPath);
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

        /// <summary>The game's install folder name under EA, Origin and Steam alike.</summary>
        private const string InstallFolderName = "STAR WARS Battlefront";

        /// <summary>
        /// Every copy of the game this PC knows about, best source first: the EA app's and
        /// Origin's registry entries, the uninstall entry, Steam's libraries, and the usual
        /// folders on every fixed drive. Each path is checked to really be the game, and
        /// the same folder is reported once.
        /// </summary>
        public static List<GameCandidate> FindInstalls()
        {
            var found = new List<GameCandidate>();

            void Add(string path, string source)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                path = path.Trim().Trim('"').TrimEnd('\\', '/');
                if (!IsValidBattlefrontInstall(path))
                    return;
                if (found.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
                    return;
                found.Add(new GameCandidate(path, source));
            }

            // The EA app and Origin write the same key the game's installer did
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                Add(ReadRegistry(RegistryHive.LocalMachine, view, @"SOFTWARE\EA Games\" + InstallFolderName, "Install Dir"), "EA app");
                Add(ReadRegistry(RegistryHive.LocalMachine, view,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E402D891-4E45-4ce9-B41F-DD35864EF170}", "InstallLocation"), "EA app");
            }

            // Steam: the client's own folder plus every library in libraryfolders.vdf
            var steamRoots = new List<string>
            {
                ReadRegistry(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"),
                ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath"),
                ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath")
            };
            foreach (string root in steamRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Replace('/', '\\')))
            {
                foreach (string library in SteamLibraries(root))
                    Add(Path.Combine(library, "steamapps", "common", InstallFolderName), "Steam");
            }

            // Last resort: the folders people end up with on each drive
            string[] parents =
            {
                @"Program Files\EA Games", @"EA Games", @"Games\EA Games",
                @"Program Files (x86)\Origin Games", @"Origin Games", @"Games\Origin Games",
                @"SteamLibrary\steamapps\common", @"Program Files (x86)\Steam\steamapps\common",
                @"Steam\steamapps\common", @"Games\Steam\steamapps\common", @"Games"
            };
            foreach (DriveInfo drive in SafeDrives())
            {
                foreach (string parent in parents)
                    Add(Path.Combine(drive.RootDirectory.FullName, parent, InstallFolderName), "Found on " + drive.Name.TrimEnd('\\'));
            }

            return found;
        }

        private static string ReadRegistry(RegistryHive hive, RegistryView view, string subKey, string valueName)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = baseKey.OpenSubKey(subKey))
                    return key?.GetValue(valueName) as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The Steam root itself plus the extra library folders listed in its libraryfolders.vdf.</summary>
        private static IEnumerable<string> SteamLibraries(string steamRoot)
        {
            yield return steamRoot;

            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            string[] lines;
            try
            {
                if (!File.Exists(vdf))
                    yield break;
                lines = File.ReadAllLines(vdf);
            }
            catch
            {
                yield break;
            }

            // Lines of the form:  "path"  "D:\\SteamLibrary"
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                    continue;
                int open = trimmed.IndexOf('"', 6);
                int close = open < 0 ? -1 : trimmed.IndexOf('"', open + 1);
                if (open < 0 || close < 0)
                    continue;
                yield return trimmed.Substring(open + 1, close - open - 1).Replace(@"\\", @"\");
            }
        }

        private static IEnumerable<DriveInfo> SafeDrives()
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch
            {
                yield break;
            }
            foreach (DriveInfo drive in drives)
            {
                bool usable;
                try
                {
                    usable = drive.DriveType == DriveType.Fixed && drive.IsReady;
                }
                catch
                {
                    usable = false;
                }
                if (usable)
                    yield return drive;
            }
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
