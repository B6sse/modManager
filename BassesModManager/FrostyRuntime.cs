using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Frosty.Core;
using FrostySdk;
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
        private static bool assemblyResolverRegistered;

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

        // Used to validate a folder before accepting it as the game install (both when the
        // user picks one and when re-checking a saved path) - the app only ever supports
        // this one game, so anything else should be rejected rather than silently accepted.
        public static bool IsValidBattlefrontInstall(string gamePath)
        {
            try
            {
                return !string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath) &&
                       Directory.EnumerateFiles(gamePath, "*.exe")
                           .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f), DefaultProfileKey, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureInitialized(ILogger logger, string profileKey)
        {
            lock (initLock)
            {
                if (!initialized)
                {
                    RegisterAssemblyResolver();

                    // PluginManager looks for "Plugins" and TypeLibrary for "Profiles",
                    // both relative to the working directory - which the launch flow has
                    // already pointed at the shared cache folder by this time. Pin it to
                    // the app folder for the bootstrap so they find the files that ship
                    // with the app, then hand it back.
                    string previousDirectory = Environment.CurrentDirectory;
                    Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    try
                    {
                        // An absent folder makes PluginManager log a "please reinstall"
                        // warning into the progress window and bail out before loading
                        // any profiles at all
                        if (!Directory.Exists("Plugins"))
                            Directory.CreateDirectory("Plugins");

                        var pluginManager = new PluginManager(logger, PluginManagerType.ModManager);
                        Frosty.Core.App.PluginManager = pluginManager;
                        ProfilesLibrary.Initialize(pluginManager.Profiles);

                        // This app's own Frosty config, never Frosty Mod Manager's. Has to be
                        // set before Config.Load(), which is what resolves the path.
                        Frosty.Core.App.SettingsPathOverride = CachePathHelper.GetFrostySettingsPath();

                        // Ensure Frosty config dir and file exist before Config.Load() (first-run fix)
                        string configDir = Frosty.Core.App.GlobalSettingsPath;
                        string configFile = Path.Combine(configDir, "manager_config.json");
                        if (!Directory.Exists(configDir))
                            Directory.CreateDirectory(configDir);
                        if (!File.Exists(configFile))
                            File.WriteAllText(configFile, "{\n  \"Games\": {},\n  \"GlobalOptions\": {}\n}");

                        Config.Load();

                        // Same order Frosty Mod Manager uses, and it is load-bearing:
                        // TypeLibrary can only pick the right SDK once the profile is
                        // known, and PluginManager.Initialize() needs those SDK types to
                        // map a mod's ebx type name onto the plugin that handles it.
                        // Without this pass no custom handler is ever registered, so mods
                        // built with a plugin (localization, shader block depots, ...)
                        // silently lose the parts that need one.
                        ProfilesLibrary.Initialize(profileKey);
                        TypeLibrary.Initialize();
                        pluginManager.Initialize();

                        initialized = true;
                    }
                    finally
                    {
                        Environment.CurrentDirectory = previousDirectory;
                    }
                }
            }

            ProfilesLibrary.Initialize(profileKey);
        }

        /// <summary>
        /// Resolves the assemblies Frosty loads by name rather than from a file path: the
        /// generated EBX class library, and the plugin DLLs. Plugins are loaded with
        /// <see cref="Assembly.LoadFile"/>, so the CLR cannot find them again on its own -
        /// which it has to do when a mod names a plugin type in its data (custom handlers
        /// store an assembly-qualified type name and revive it via Type.GetType).
        /// </summary>
        private static void RegisterAssemblyResolver()
        {
            if (assemblyResolverRegistered)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string name = args.Name.Contains(",") ? args.Name.Substring(0, args.Name.IndexOf(',')) : args.Name;

                if (name.Equals("EbxClasses", StringComparison.OrdinalIgnoreCase))
                {
                    string sdkPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles", ProfilesLibrary.SDKFilename + ".dll");
                    return File.Exists(sdkPath) ? Assembly.LoadFile(sdkPath) : null;
                }

                if (name.StartsWith("SharpDX", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Newtonsoft", StringComparison.OrdinalIgnoreCase))
                {
                    string thirdPartyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ThirdParty", name + ".dll");
                    return File.Exists(thirdPartyPath) ? Assembly.LoadFile(thirdPartyPath) : null;
                }

                return Frosty.Core.App.PluginManager?.GetPluginAssembly(name);
            };

            assemblyResolverRegistered = true;
        }

        /// <summary>
        /// The DLL the game loads from its own folder at process start, ahead of the one in
        /// System32. Frosty's shim used this name to get into the game and let it read from
        /// ModData; without it the game crashes in EA's DRM stage a few seconds after start.
        /// The file this app ships under that name is Auric's own DLL, which carries the same
        /// proxy exports and signature hook and starts Auric as well - so placing it is both
        /// the mod support and the Auric start, and nothing has to be injected later. Frosty
        /// copies it in itself right before launch too, but this app places it first and
        /// checks it, so a launch never depends on another tool having done so.
        /// </summary>
        public const string LoaderFileName = "CryptBase.dll";

        public static string GetLoaderSourcePath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ThirdParty", LoaderFileName);

        public static string GetLoaderTargetPath(string gamePath) => Path.Combine(gamePath, LoaderFileName);

        /// <summary>True when the game folder holds exactly the shim this app ships.</summary>
        public static bool IsLoaderInstalled(string gamePath)
        {
            try
            {
                string source = GetLoaderSourcePath();
                string target = GetLoaderTargetPath(gamePath);
                return File.Exists(source) && File.Exists(target) &&
                       string.Equals(FileHash.OfFileCached(source), FileHash.OfFileCached(target), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// True when this process can overwrite the shim in the game folder as it is, without
        /// elevation. That is the case once an elevated run has granted Users write access to
        /// that one file (see <see cref="InstallLoader"/>). A missing file is never replaceable
        /// this way: creating one needs rights on the folder, which stays administrator-only.
        /// </summary>
        public static bool CanReplaceLoaderUnelevated(string gamePath)
        {
            string target = GetLoaderTargetPath(gamePath);
            if (!File.Exists(target))
                return false;

            try
            {
                using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                    return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Puts the shim in the game folder if it is missing or differs. Frosty Mod Manager
        /// puts its own shim back every time it launches the game (the sizes differ, which is
        /// all its copy checks), so an elevated run also grants Users write access to this one
        /// file: from then on the swap back works without elevation, and switching between
        /// the two tools stops costing an administrator prompt on this side. Callers check
        /// <see cref="CanReplaceLoaderUnelevated"/> or elevation up front rather than let the
        /// copy throw.
        /// </summary>
        public static void InstallLoader(string gamePath)
        {
            string source = GetLoaderSourcePath();
            if (!File.Exists(source))
                throw new FileNotFoundException(LoaderFileName + " is missing from the app folder. Reinstall the app to restore it.", source);

            string target = GetLoaderTargetPath(gamePath);
            if (!IsLoaderInstalled(gamePath))
                File.Copy(source, target, true);

            if (IsElevated())
                GrantUsersWriteAccess(target);
        }

        /// <summary>
        /// Lets ordinary users overwrite one file in the game folder. The game runs as that
        /// same user, so this hands nobody anything they could not already do by other means,
        /// and the hash check before every launch catches a swapped file regardless. Best
        /// effort: without it the next swap simply asks for administrator rights again.
        /// </summary>
        private static void GrantUsersWriteAccess(string path)
        {
            try
            {
                var file = new FileInfo(path);
                FileSecurity acl = file.GetAccessControl();
                acl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.Modify, AccessControlType.Allow));
                file.SetAccessControl(acl);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Registers the game in the Frosty config under the profile's own name. That is
        /// the key every game-scoped Config lookup in Frosty and its plugins uses, so any
        /// other spelling (older versions used the exe's on-disk casing, or a hardcoded
        /// literal) leaves those lookups throwing. Must run after
        /// <see cref="EnsureInitialized"/>, which is what selects the profile.
        /// </summary>
        public static void EnsureGameRegistered(string gamePath)
        {
            string key = ProfilesLibrary.ProfileName;

            foreach (string legacy in Config.Current.Games.Keys
                         .Where(k => !string.Equals(k, key, StringComparison.Ordinal) &&
                                     string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                Config.RemoveGame(legacy);
            }

            if (!Config.Current.Games.ContainsKey(key))
                Config.AddGame(key, gamePath);
            else
                Config.Add("GamePath", gamePath, ConfigScope.Game);
        }

        /// <summary>Steam copies of the game carry this file; Frosty keys its launch path off it too.</summary>
        public static bool IsSteamInstall(string gamePath) => File.Exists(Path.Combine(gamePath, "steam_appid.txt"));

        /// <summary>
        /// Tells LaunchPlatformPlugin what to do on this launch. On Steam the game is
        /// started by Steam, which hands it to the EA app, and -dataPath does not survive
        /// that chain; the plugin restarts Steam with GAME_DATA_DIR in its environment so
        /// the game inherits the mod folder instead. Everywhere else the plugin stays out
        /// of the way: the exe is started directly and the EA app forwards the argument.
        /// </summary>
        public static void ConfigurePlatformLaunch(string gamePath)
        {
            bool steam = IsSteamInstall(gamePath);
            Config.Add("PlatformLaunchingEnabled", steam, ConfigScope.Game);
            Config.Add("Platform", steam ? "Steam" : "Origin", ConfigScope.Game);
        }
    }
}
