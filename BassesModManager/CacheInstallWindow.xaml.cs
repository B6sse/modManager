using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using FrostySdk.Managers;
using Frosty.Core;

namespace BassesModManager
{
    /// <summary>
    /// Builds the asset cache the mod flow needs, once, before the mod list can do
    /// anything. DialogResult is true when the cache is ready to use.
    /// </summary>
    public partial class CacheInstallWindow : Window
    {
        private readonly string _gamePath;

        public CacheInstallWindow(string gamePath)
        {
            InitializeComponent();
            _gamePath = gamePath;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var logger = Progress.CreateLogger(this);
            logger.Status = "Initializing...";

            try
            {
                await Task.Run(() => CreateCache(logger));
                DialogResult = true;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this,
                    $"First-time setup could not finish.\n\nTechnical details: {ex.Message}", "Setup failed");
                DialogResult = false;
            }
            Close();
        }

        private void CreateCache(SmoothProgressLogger logger)
        {
            // Use the actual exe filename as profile/config key so the entry matches what
            // Frosty Mod Manager itself would create (avoids duplicate game entries in FMM)
            string profileKey = FrostyRuntime.GetProfileKey(_gamePath);
            FrostyRuntime.EnsureInitialized(logger, profileKey);
            FrostyRuntime.EnsureGameRegistered(profileKey, _gamePath);

            CachePathHelper.EnsureCachesDirectory();
            // Frosty SDK uses relative "Caches/..." paths; they resolve via CurrentDirectory
            Environment.CurrentDirectory = CachePathHelper.GetCacheBasePath();

            var fs = new FrostySdk.FileSystem(_gamePath + Path.DirectorySeparatorChar);
            foreach (var source in FrostySdk.ProfilesLibrary.Sources)
                fs.AddSource(source.Path, source.SubDirs);
            fs.Initialize();
            Frosty.Core.App.FileSystem = fs;

            Frosty.Core.App.ResourceManager = new ResourceManager(fs);
            Frosty.Core.App.ResourceManager.SetLogger(logger);
            Frosty.Core.App.ResourceManager.Initialize();

            Frosty.Core.App.AssetManager = new AssetManager(fs, Frosty.Core.App.ResourceManager);
            Frosty.Core.App.AssetManager.SetLogger(logger);
            Frosty.Core.App.AssetManager.Initialize(false);

            Frosty.Core.App.AssetManager = null;
            Frosty.Core.App.ResourceManager = null;
            Frosty.Core.App.FileSystem = null;
        }
    }
}
