using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using FrostySdk.Managers;
using FrostySdk.Interfaces;
using Frosty.ModSupport;
using Frosty.Core;

namespace BassesModManager
{
    public partial class CacheInstallWindow : Window
    {
        private readonly string _gamePath;

        public CacheInstallWindow(string gamePath)
        {
            InitializeComponent();
            _gamePath = gamePath;
            LoadBanner();
        }

        private void LoadBanner()
        {
            try
            {
                var bannerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Banners", "SWBF.png");
                if (File.Exists(bannerPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(bannerPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    BannerImage.Source = bmp;
                }
            }
            catch { }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var logger = new CacheInstallLogger(this);
            logger.Status = "Initializing...";

            try
            {
                await Task.Run(() => CreateCache(logger));
                DialogResult = true;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Cache creation failed: {ex.Message}", "Error");
                DialogResult = false;
            }
            Close();
        }

        private void CreateCache(CacheInstallLogger logger)
        {
            var pluginManager = new PluginManager(logger, PluginManagerType.ModManager);
            Frosty.Core.App.PluginManager = pluginManager;
            FrostySdk.ProfilesLibrary.Initialize(pluginManager.Profiles);
            FrostySdk.ProfilesLibrary.Initialize("StarWarsBattlefront");

            // Ensure Frosty config dir and file exist before Config.Load() (first-run fix)
            string configDir = Frosty.Core.App.GlobalSettingsPath;
            string configFile = Path.Combine(configDir, "manager_config.json");
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);
            if (!File.Exists(configFile))
                File.WriteAllText(configFile, "{\n  \"Games\": {},\n  \"GlobalOptions\": {}\n}");

            Frosty.Core.Config.Load();
            if (!Frosty.Core.Config.Current.Games.ContainsKey("StarWarsBattlefront"))
                Frosty.Core.Config.AddGame("StarWarsBattlefront", _gamePath);

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
