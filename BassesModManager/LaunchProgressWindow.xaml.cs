using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Frosty.Core;
using Frosty.ModSupport;

namespace BassesModManager
{
    /// <summary>
    /// Modal progress window for the full launch flow: initializing the Frosty runtime,
    /// applying the selected mods (creating the ModData folder when needed) and starting
    /// the game. DialogResult is true when the game was launched successfully.
    /// </summary>
    public partial class LaunchProgressWindow : Window
    {
        private readonly string _gamePath;
        private readonly string _modsDirectory;
        private readonly string[] _modFileNames;
        private readonly string _modPackName;
        private readonly CancellationTokenSource _cancelSource = new CancellationTokenSource();

        public LaunchProgressWindow(string gamePath, string modsDirectory, string[] modFileNames, string modPackName)
        {
            InitializeComponent();
            _gamePath = gamePath;
            _modsDirectory = modsDirectory;
            _modFileNames = modFileNames;
            _modPackName = modPackName;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var logger = Progress.CreateLogger(this, barOnAnyProgress: true);
            logger.Status = "Preparing...";

            try
            {
                int result = await Task.Run(() => ApplyModsAndLaunch(logger));

                if (result == 0)
                {
                    DialogResult = true;
                }
                else
                {
                    CustomMessageBox.Show(this,
                        "The mods could not be applied, so the game was not started.\n\n" +
                        "Error code: " + result, "Could not apply mods");
                    DialogResult = false;
                }
            }
            catch (OperationCanceledException)
            {
                DialogResult = false;
            }
            catch (UnauthorizedAccessException)
            {
                // Something had to be written inside the game install (a stale ModData
                // folder Frosty decided to rebuild in place, or the loader shim), which it
                // will not allow a non-elevated app to do. Named for what it is rather than
                // left to the generic message below, which would have reported a permission
                // problem as something going wrong.
                CustomMessageBox.Show(this,
                    "Writing to the game folder needs administrator rights. Restart the app as administrator.",
                    "Administrator needed");
                DialogResult = false;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this,
                    "Something went wrong while applying the mods, so the game was not started.\n\n" +
                    $"Technical details: {ex.Message}", "Could not apply mods");
                DialogResult = false;
            }
            Close();
        }

        private int ApplyModsAndLaunch(SmoothProgressLogger logger)
        {
            // Frosty SDK uses relative "Caches/..." paths; they resolve via CurrentDirectory
            Environment.CurrentDirectory = CachePathHelper.GetCacheBasePath();

            string profileKey = FrostyRuntime.GetProfileKey(_gamePath);
            FrostyRuntime.EnsureInitialized(logger, profileKey);
            FrostyRuntime.EnsureGameRegistered(_gamePath);

            CachePathHelper.EnsureCachesDirectory();

            // The loader shim goes in before anything else: without it the game crashes a
            // few seconds in, whatever state ModData is in
            FrostyRuntime.InstallLoader(_gamePath);

            // Only the FileSystem is set up here, like FrostyModManager does. Run() builds
            // its own ResourceManager/AssetManager, and only when the mods actually need
            // reapplying - loading the cache here too just doubled the work every launch.
            // No trailing separator: FileSystem adds one itself, and a second one ended up
            // in the -dataPath handed to the game.
            var fs = new FrostySdk.FileSystem(_gamePath);
            foreach (var source in FrostySdk.ProfilesLibrary.Sources)
                fs.AddSource(source.Path, source.SubDirs);
            fs.Initialize();

            _cancelSource.Token.ThrowIfCancellationRequested();

            Frosty.Core.App.Logger = logger;

            // The execution actions (LaunchPlatformPlugin) find the mod folder through
            // these two, the same way they do in Frosty Mod Manager
            Frosty.Core.App.FileSystem = fs;
            Frosty.Core.App.SelectedPack = _modPackName;
            FrostyRuntime.ConfigurePlatformLaunch(_gamePath);

            // rootPath is only used to resolve the mod filenames, so it's the mods folder
            string additionalArgs = "";

            // Same shape as Frosty Mod Manager's launch: the plugins' pre-launch actions,
            // then Run() - which launches the game itself at the end - then the
            // post-launch actions, which also run when the launch was cancelled.
            List<ExecutionAction> actions = Frosty.Core.App.PluginManager.ExecutionActions.ToList();
            try
            {
                foreach (ExecutionAction action in actions)
                    action.PreLaunchAction(logger, PluginManagerType.ModManager, _cancelSource.Token);

                var executor = new FrostyModExecutor();
                int result = executor.Run(fs, _cancelSource.Token, logger, _modsDirectory, _modPackName, additionalArgs, silentMode: true, _modFileNames);

                foreach (ExecutionAction action in actions)
                    action.PostLaunchAction(logger, PluginManagerType.ModManager, _cancelSource.Token);

                return result;
            }
            catch (OperationCanceledException)
            {
                foreach (ExecutionAction action in actions)
                    action.PostLaunchAction(logger, PluginManagerType.ModManager, _cancelSource.Token);
                throw;
            }
        }

        private void Progress_Cancelled(object sender, EventArgs e) => _cancelSource.Cancel();
    }
}
