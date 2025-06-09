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
                // Prøv å slette ulovlige mods
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
                    MessageBox.Show($"Some unauthorized mods could not be deleted: {string.Join(", ", failedDeletes)}.\nPlease run the app as administrator.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading mods: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                // Initialiser Frosty-profilen for Battlefront 2015 med PluginManager
                var logger = new SimpleLogger();
                var pluginManager = new Frosty.Core.PluginManager(logger, Frosty.Core.PluginManagerType.ModManager);
                Frosty.Core.App.PluginManager = pluginManager;
                FrostySdk.ProfilesLibrary.Initialize(pluginManager.Profiles);
                FrostySdk.ProfilesLibrary.Initialize("StarWarsBattlefront");

                // Initialiser konfigurasjonssystemet
                Frosty.Core.Config.Load();

                // Sett opp FileSystem, ResourceManager og AssetManager slik FrostyModManager gjør det
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

                // Sett opp nødvendige parametre for mod executor
                var cancelToken = new System.Threading.CancellationToken();
                string rootPath = gamePath + Path.DirectorySeparatorChar;
                string additionalArgs = "";

                Frosty.Core.App.Logger = logger;
                
                // Kjør FrostyModExecutor i silent mode
                var executor = new FrostyModExecutor();
                var modPaths = selectedMods.Where(m => m.IsEnabled)
                                           .Select(m => Path.Combine(modsDirectory, m.FileName))
                                           .ToArray();

                // Kjør mod-applikasjonen i bakgrunnen
                Task.Run(() => {
                    int result = executor.Run(fs, cancelToken, logger, rootPath, modPackName, additionalArgs, modPaths);
                    
                    if (result == 0)
                    {
                        // Start spillet automatisk i silent mode
                        string modDataPath = Path.Combine(gamePath, "ModData", modPackName);
                        FrostyModExecutor.LaunchGame(gamePath + Path.DirectorySeparatorChar, modPackName, modDataPath, additionalArgs);
                    }
                    else
                    {
                        Dispatcher.Invoke(() => {
                            MessageBox.Show("Noe gikk galt under patching av mods.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying mods: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaunchGameButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string gamePath = Properties.Settings.Default.GamePath;
                if (string.IsNullOrEmpty(gamePath))
                {
                    MessageBox.Show("Please set the game path first!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create a unique name for this mod combination
                string modPackName = "ModPack_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // Get selected mods
                var selectedMods = mods.Where(m => m.IsEnabled).ToList();
                if (!selectedMods.Any())
                {
                    MessageBox.Show("You must select a mod before launching the game!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // Bruk FrostyModExecutor til å patche og gjøre klart
                ApplyModsAndLaunch(gamePath, selectedMods, modPackName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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