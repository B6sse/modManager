using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
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
        private readonly string[] _modPaths;
        private readonly string _modPackName;
        private readonly CancellationTokenSource _cancelSource = new CancellationTokenSource();

        public LaunchProgressWindow(string gamePath, string[] modPaths, string modPackName)
        {
            InitializeComponent();
            _gamePath = gamePath;
            _modPaths = modPaths;
            _modPackName = modPackName;
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
            var logger = new SmoothProgressLogger(this, ProgressBar, StatusText, SpinnerPanel, barOnAnyProgress: true);
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
                        "Try closing the app and reopening it as administrator (right-click the icon, choose 'Run as administrator').\n\n" +
                        "Error code: " + result, "Could not apply mods");
                    DialogResult = false;
                }
            }
            catch (OperationCanceledException)
            {
                DialogResult = false;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this,
                    "Something went wrong while applying the mods, so the game was not started.\n\n" +
                    "Try closing the app and reopening it as administrator (right-click the icon, choose 'Run as administrator').\n\n" +
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
            FrostyRuntime.EnsureGameRegistered(profileKey, _gamePath);

            CachePathHelper.EnsureCachesDirectory();

            // Set up FileSystem, ResourceManager and AssetManager like FrostyModManager does
            var fs = new FrostySdk.FileSystem(_gamePath + Path.DirectorySeparatorChar);
            foreach (var source in FrostySdk.ProfilesLibrary.Sources)
                fs.AddSource(source.Path, source.SubDirs);
            fs.Initialize();

            _cancelSource.Token.ThrowIfCancellationRequested();

            var rm = new FrostySdk.Managers.ResourceManager(fs);
            rm.SetLogger(logger);
            rm.Initialize();

            var am = new FrostySdk.Managers.AssetManager(fs, rm);
            am.SetLogger(logger);
            am.Initialize(false);

            _cancelSource.Token.ThrowIfCancellationRequested();

            Frosty.Core.App.Logger = logger;

            string rootPath = _gamePath + Path.DirectorySeparatorChar;
            string additionalArgs = "";

            // Run FrostyModExecutor in silent mode. Note: Run() launches the game itself
            // at the end (there is no separate LaunchGame call here - the old extra call
            // caused the game to be started twice).
            var executor = new FrostyModExecutor();
            return executor.Run(fs, _cancelSource.Token, logger, rootPath, _modPackName, additionalArgs, silentMode: true, _modPaths);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelButton.IsEnabled = false;
            _cancelSource.Cancel();
        }
    }
}
