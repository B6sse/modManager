using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Linq;
using System.Collections.Generic;
using Frosty.ModSupport;
using FrostySdk.Interfaces;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Security.Principal;

namespace BassesModManager 
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<ModItem> mods;
        private string gamePath;
        private string modsDirectory;

        private readonly HashSet<string> approvedModHashes = new HashSet<string>
        {
            "b0152a45d8dd1cc995fdc92d7f517ce17b08a260a9f9062ff9d6ec17902a1694",
            "c8748886884ae0f3f2a372fc130bf9bb3794dc2ca908a16ad8a6d2d01d16d719",
            "88bc98b8604f993e058ff848ba267b1e72530a3938037f9dc4b58d6471aa337a"
        };

        public MainWindow()
        {
            InitializeComponent();
            mods = new ObservableCollection<ModItem>();
            ModListControl.ItemsSource = mods;

            // Load saved game path if it exists
            gamePath = Properties.Settings.Default.GamePath;
            if (!string.IsNullOrEmpty(gamePath))
            {
                GamePathTextBox.Text = gamePath;
            }

            modsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
            
            // Load available mods
            LoadMods();

            LaunchGameButton.ToolTip = "Select a mod before launching the game";
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Game Executable|*.exe",
                Title = "Select Game Executable"
            };

            if (dialog.ShowDialog() == true)
            {
                gamePath = Path.GetDirectoryName(dialog.FileName);
                GamePathTextBox.Text = gamePath;
                Properties.Settings.Default.GamePath = gamePath;
                Properties.Settings.Default.Save();
            }
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
                var modFiles = Directory.GetFiles(modsDirectory, "*.fbmod");
                List<string> unauthorizedMods = new List<string>();
                foreach (var modFile in modFiles)
                {
                    string fileHash = GetSHA256Hash(modFile);
                    if (approvedModHashes.Contains(fileHash))
                    {
                        mods.Add(new ModItem
                        {
                            Name = Path.GetFileNameWithoutExtension(modFile),
                            FileName = Path.GetFileName(modFile),
                            Description = "SWBF2015 Crosshair Mod",
                            Author = "Flash",
                            Version = "1.0",
                            IsEnabled = false
                        });
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
                    CustomMessageBox.Show(this, $"Some unauthorized mods could not be deleted: {string.Join(", ", failedDeletes)}.\nPlease run the app as administrator.", "Warning");
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Error loading mods: {ex.Message}", "Error");
            }
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
            try
            {
                // Initialize Frosty profile for Battlefront 2015 with PluginManager
                var logger = new SimpleLogger();
                var pluginManager = new Frosty.Core.PluginManager(logger, Frosty.Core.PluginManagerType.ModManager);
                Frosty.Core.App.PluginManager = pluginManager;
                FrostySdk.ProfilesLibrary.Initialize(pluginManager.Profiles);
                FrostySdk.ProfilesLibrary.Initialize("StarWarsBattlefront");

                // Initialize config system
                Frosty.Core.Config.Load();

                // Set up FileSystem, ResourceManager and AssetManager like FrostyModManager does
                var fs = new FrostySdk.FileSystem(gamePath + Path.DirectorySeparatorChar);
                foreach (var source in FrostySdk.ProfilesLibrary.Sources)
                    fs.AddSource(source.Path, source.SubDirs);
                fs.Initialize();

                var rm = new FrostySdk.Managers.ResourceManager(fs);
                rm.SetLogger(logger);
                rm.Initialize();

                var am = new FrostySdk.Managers.AssetManager(fs, rm);
                am.SetLogger(logger);
                am.Initialize(false);

                // Set up necessary parameters for mod executor
                var cancelToken = new System.Threading.CancellationToken();
                string rootPath = gamePath + Path.DirectorySeparatorChar;
                string additionalArgs = "";

                Frosty.Core.App.Logger = logger;
                
                // Run FrostyModExecutor in silent mode
                var executor = new FrostyModExecutor();
                var modPaths = selectedMods.Where(m => m.IsEnabled)
                                           .Select(m => Path.Combine(modsDirectory, m.FileName))
                                           .ToArray();

                // Run mod application in background
                Task.Run(() => {
                    int result = executor.Run(fs, cancelToken, logger, rootPath, modPackName, additionalArgs, silentMode: true, modPaths);
                    
                    if (result == 0)
                    {
                        // Start game automatically in silent mode
                        string modDataPath = Path.Combine(gamePath, "ModData", modPackName);
                        FrostyModExecutor.LaunchGame(gamePath + Path.DirectorySeparatorChar, modPackName, modDataPath, additionalArgs);
                    }
                    else
                    {
                        Dispatcher.Invoke(() => {
                            CustomMessageBox.Show(this, "Something went wrong while patching mods.", "Error");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Error applying mods: {ex.Message}", "Error");
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
            var modNames = selectedMods.Where(m => m.IsEnabled)
                                      .Select(m => m.FileName)
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
                    CustomMessageBox.Show(this, "Since this is the first time you are launching the game with this mod combination, the app requires administrator privileges. Please restart the app as administrator.", "Admin required");
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
            try
            {
                string gamePath = Properties.Settings.Default.GamePath;
                if (string.IsNullOrEmpty(gamePath))
                {
                    CustomMessageBox.Show(this, "Please set the game path first!", "Error");
                    return;
                }
                var selectedMods = mods.Where(m => m.IsEnabled).ToList();
                if (!selectedMods.Any())
                {
                    CustomMessageBox.Show(this, "You must select a mod before launching the game!", "Error");
                    return;
                }

                // Here: check if the starwars.cache file in the Caches folder exists in the path of this application (BassesModManager where it was installed)
                string cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caches", "starwars.cache");
                if (!File.Exists(cachePath) && !IsRunAsAdmin())
                {
                    CustomMessageBox.Show(this, "Please run the app as administrator to let the app create a necessary cache file. (This will drastically improve the performance for later launches.)", "Admin required");
                    return;
                }

                // Find or create the correct ModPack folder
                string modPackName = GetModPackNameForSelection(selectedMods, gamePath);
                if (modPackName == null)
                {
                    return;
                }
                ApplyModsAndLaunch(gamePath, selectedMods, modPackName);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"An error occurred: {ex.Message}\n (Running the app as administrator will most likely fix this. If not, please report the error to the developer.)", "Error");
            }
        }
    }

    public class ModItem
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public bool IsEnabled { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
    }

       public class SimpleLogger : ILogger
   {
       public void Log(string text, params object[] vars)
       {
           Console.WriteLine(text, vars);
       }

       public void LogWarning(string text, params object[] vars)
       {
           Console.WriteLine("WARNING: " + text, vars);
       }

       public void LogError(string text, params object[] vars)
       {
           Console.WriteLine("ERROR: " + text, vars);
       }
   }
} 