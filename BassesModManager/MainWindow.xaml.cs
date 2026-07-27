using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Security.Cryptography;
using System.Security.Principal;

namespace BassesModManager 
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<ModItem> mods;
        private ModItem scoreboardMod;
        private string modsDirectory;

        private MediaPlayer _hoverPlayer;
        private MediaPlayer _clickPlayer;

        private readonly HashSet<string> approvedModHashes = new HashSet<string>
        {
            // Crosshair mods
            "b0152a45d8dd1cc995fdc92d7f517ce17b08a260a9f9062ff9d6ec17902a1694",
            "c8748886884ae0f3f2a372fc130bf9bb3794dc2ca908a16ad8a6d2d01d16d719",
            "88bc98b8604f993e058ff848ba267b1e72530a3938037f9dc4b58d6471aa337a",
            // Optional Improved_Scoreboard mod
            "4507eb7297053ffb38a65228158189de3260e411e603401b0c1ecb2542b2af76"
        };

        public MainWindow()
        {
            InitializeComponent();
            mods = new ObservableCollection<ModItem>();
            ModListControl.ItemsSource = mods;

            modsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
            
            // Load available mods
            LoadMods();

            LaunchGameButton.ToolTip = "Select a mod before launching the game";

            // Pre-load sounds for snappy playback
            PreloadSounds();
        }

        private void PreloadSounds()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var hoverPath = Path.Combine(baseDir, "Assets", "Sounds", "hover.mp3");
                var clickPath = Path.Combine(baseDir, "Assets", "Sounds", "click.mp3");
                if (File.Exists(hoverPath))
                {
                    _hoverPlayer = new MediaPlayer();
                    _hoverPlayer.Volume = 0.2;
                    _hoverPlayer.Open(new Uri(hoverPath, UriKind.Absolute));
                }
                if (File.Exists(clickPath))
                {
                    _clickPlayer = new MediaPlayer();
                    _clickPlayer.Volume = 0.2;
                    _clickPlayer.Open(new Uri(clickPath, UriKind.Absolute));
                }
            }
            catch { /* ignore preload errors */ }
        }

        private void PlayHoverSound(object sender, MouseEventArgs e) => PlayPreloaded(_hoverPlayer);
        private void PlayClickSound(object sender, RoutedEventArgs e) => PlayPreloaded(_clickPlayer);
        private void PlayClickSound_Mouse(object sender, MouseButtonEventArgs e) => PlayPreloaded(_clickPlayer);

        private static void PlayPreloaded(MediaPlayer player)
        {
            if (player == null) return;
            try
            {
                player.Position = TimeSpan.Zero;
                player.Play();
            }
            catch { /* ignore playback errors */ }
        }

        private void BackToGameSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound(sender, e);
            var gameSelectionWindow = new GameSelectionWindow();
            Application.Current.MainWindow = gameSelectionWindow;
            gameSelectionWindow.Show();
            Close();
        }

        private void LoadMods()
        {
            try
            {
                if (!Directory.Exists(modsDirectory))
                {
                    Directory.CreateDirectory(modsDirectory);
                }

                mods.Clear();
                scoreboardMod = null;
                var modFiles = Directory.GetFiles(modsDirectory, "*.fbmod");
                List<string> unauthorizedMods = new List<string>();
                foreach (var modFile in modFiles)
                {
                    string fileHash = GetSHA256Hash(modFile);
                    if (approvedModHashes.Contains(fileHash))
                    {
                        var modName = Path.GetFileNameWithoutExtension(modFile);
                        var item = new ModItem
                        {
                            Name = modName,
                            FileName = Path.GetFileName(modFile),
                            ImagePath = GetModImagePath(modName),
                            Description = modName.IndexOf("Improved_Scoreboard", StringComparison.OrdinalIgnoreCase) >= 0
                                ? "Improved scoreboard visuals"
                                : "SWBF2015 Crosshair Mod",
                            Author = "Flash",
                            Version = "1.0",
                            IsEnabled = false
                        };

                        if (modName.IndexOf("Improved_Scoreboard", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            scoreboardMod = item;
                        }
                        else
                        {
                            mods.Add(item);
                        }
                    }
                    else
                    {
                        unauthorizedMods.Add(modFile);
                    }
                }
                // Try to delete unauthorized mods
                List<string> failedDeletes = new List<string>();
                foreach (var file in unauthorizedMods)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        failedDeletes.Add(Path.GetFileName(file));
                    }
                }
                if (failedDeletes.Count > 0)
                {
                    CustomMessageBox.Show(this, $"Some files in the Mods folder are not approved mods and could not be removed: {string.Join(", ", failedDeletes)}.\n\nClose the app, right-click its icon and choose 'Run as administrator' to let it clean them up.", "Warning");
                }
                // Order crosshair mods: White, Red, Green (left to right)
                var ordered = mods.OrderBy(m => m.Name.IndexOf("White", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 :
                                                 m.Name.IndexOf("Red", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 2).ToList();
                mods.Clear();
                foreach (var m in ordered) mods.Add(m);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Could not read the mod files. Try reinstalling the app if this keeps happening.\n\nTechnical details: {ex.Message}", "Error");
            }
        }

        private static string GetModImagePath(string modName)
        {
            if (modName.IndexOf("Red", StringComparison.OrdinalIgnoreCase) >= 0) return "Assets/Images/red_dot.png";
            if (modName.IndexOf("Green", StringComparison.OrdinalIgnoreCase) >= 0) return "Assets/Images/green_dot.png";
            else return "Assets/Images/white_dot.png";
        }

        private string GetSHA256Hash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private void ApplyModsAndLaunch(string gamePath, List<ModItem> selectedMods, string modPackName)
        {
            // Pass plain filenames with the mods folder as the root, like FrostyModManager
            // does. mods.json stores these strings verbatim and compares them to decide
            // whether the ModData folder is still valid, so absolute paths would tie it to
            // one install location and force a full rebuild whenever that location differs.
            var modFileNames = selectedMods
                .Select(m => m.FileName)
                .ToArray();

            // Modal progress window runs the whole flow (Frosty init, mod patching, game
            // launch) on a background thread and shows live status/progress
            var launchWindow = new LaunchProgressWindow(gamePath, modsDirectory, modFileNames, modPackName) { Owner = this };
            if (launchWindow.ShowDialog() == true)
            {
                OnGameLaunched(gamePath);
            }
        }

        private DispatcherTimer gameWatchTimer;
        private DateTime gameWatchStart;
        private bool gameSeenRunning;
        private string watchedProcessName;

        // How long to wait for the game process to appear before giving up (Steam/EA app
        // startup can take a while)
        private static readonly TimeSpan GameStartTimeout = TimeSpan.FromMinutes(2);

        private void OnGameLaunched(string gamePath)
        {
            // Prevent re-launch while the game is starting/running, and get out of the way
            LaunchGameButton.IsEnabled = false;
            LaunchGameButton.ToolTip = "The game is starting/running...";
            WindowState = WindowState.Minimized;

            watchedProcessName = FrostyRuntime.GetProfileKey(gamePath);
            gameSeenRunning = false;
            gameWatchStart = DateTime.UtcNow;

            gameWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            gameWatchTimer.Tick += GameWatchTimer_Tick;
            gameWatchTimer.Start();
        }

        private void GameWatchTimer_Tick(object sender, EventArgs e)
        {
            bool running = false;
            try
            {
                Process[] processes = Process.GetProcessesByName(watchedProcessName);
                running = processes.Length > 0;
                foreach (Process p in processes)
                    p.Dispose();
            }
            catch { }

            if (running)
            {
                gameSeenRunning = true;
                return;
            }

            // Not running: either still starting up, or it has exited
            if (!gameSeenRunning && DateTime.UtcNow - gameWatchStart < GameStartTimeout)
                return;

            gameWatchTimer.Stop();
            gameWatchTimer = null;

            LaunchGameButton.IsEnabled = true;
            LaunchGameButton.ToolTip = "Select a mod before launching the game";

            if (gameSeenRunning)
            {
                // Game exited - bring the manager back
                WindowState = WindowState.Normal;
                Activate();
            }
        }

        private static bool IsRunAsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private string GetModPackNameForSelection(List<ModItem> selectedMods, string gamePath)
        {
            // Create a unique hash based on sorted file names for selected combination
            var modNames = selectedMods.Select(m => m.FileName)
                                      .OrderBy(n => n)
                                      .ToArray();
            string comboString = string.Join("|", modNames);
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(comboString));
                string hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                string modDataPath = System.IO.Path.Combine(gamePath, "ModData");
                // Check if mod pack folder exists
                if (System.IO.Directory.Exists(modDataPath))
                {
                    var existing = System.IO.Directory.GetDirectories(modDataPath, "ModPack_*_" + hashString);
                    if (existing.Length > 0)
                    {
                        // Use existing folder
                        return System.IO.Path.GetFileName(existing[0]);
                    }
                }
                                  
                if (!IsRunAsAdmin())
                {
                    CustomMessageBox.Show(this, "First time using this mod combination - the app needs administrator rights to set it up.\n\nClose the app, right-click its icon and choose 'Run as administrator', then try again. This is only needed once per mod combination.", "Administrator needed");
                    return null;
                }
                else
                {
                    return $"ModPack_{DateTime.Now:yyyyMMdd_HHmmss}_{hashString}";
                }
            }
        }

        private void LaunchGameButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPreloaded(_clickPlayer);
            try
            {
                string gamePath = Properties.Settings.Default.GamePath;
                if (string.IsNullOrEmpty(gamePath))
                {
                    CustomMessageBox.Show(this, "Select your game folder before launching. Press BACK and add the game first.", "Game not selected");
                    return;
                }

                // Collect selected crosshair + optional scoreboard mod
                var selectedMods = mods.Where(m => m.IsEnabled).ToList();
                if (scoreboardMod != null)
                {
                    scoreboardMod.IsEnabled = ScoreboardCheckBox.IsChecked == true;
                    if (scoreboardMod.IsEnabled)
                        selectedMods.Add(scoreboardMod);
                }
                if (!selectedMods.Any())
                {
                    CustomMessageBox.Show(this, "Pick a crosshair first, then press LAUNCH GAME.", "No mod selected");
                    return;
                }

                // Cache is created in CacheInstallWindow before MainWindow is shown
                // Find or create the correct ModPack folder
                string modPackName = GetModPackNameForSelection(selectedMods, gamePath);
                if (modPackName == null)
                {
                    return;
                }

                // First time with this mod combination: a permission/script window will appear
                string modPackPath = Path.Combine(gamePath, "ModData", modPackName);
                if (!Directory.Exists(modPackPath))
                {
                    CustomMessageBox.Show(this, "First time using this mod combination: a black command window will pop up for a moment while everything is set up. This is normal - just let it finish.", "First-time setup", MessageBoxButton.OK);
                }

                ApplyModsAndLaunch(gamePath, selectedMods, modPackName);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Something went wrong before the game could start. Try closing the app and reopening it as administrator (right-click the icon, choose 'Run as administrator').\n\nTechnical details: {ex.Message}", "Error");
            }
        }
    }

    public class ModItem
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string ImagePath { get; set; }
        public bool IsEnabled { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
    }
}