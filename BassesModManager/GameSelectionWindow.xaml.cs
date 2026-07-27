using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace BassesModManager
{
    public partial class GameSelectionWindow : Window
    {
        private ObservableCollection<GameEntry> gameEntries;

        public GameSelectionWindow() : this(false)
        {
        }

        public GameSelectionWindow(bool autoProceedIfSingle)
        {
            InitializeComponent();
            gameEntries = new ObservableCollection<GameEntry>();
            GameList.ItemsSource = gameEntries;

            LoadGamePaths();

            // With exactly one saved game there is nothing to choose - skip straight ahead
            // (only on startup; the BACK button opens this window without auto-proceed)
            if (autoProceedIfSingle && gameEntries.Count == 1)
            {
                Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    GameList.SelectedIndex = 0;
                    ProceedWithSelection();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void LoadGamePaths()
        {
            var bannerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images", "swbf.png");
            var saved = Properties.Settings.Default.GamePaths ?? "";
            var paths = saved.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in paths.Where(Directory.Exists))
                gameEntries.Add(new GameEntry { Path = p, BannerPath = bannerPath });
            if (gameEntries.Count == 0 && !string.IsNullOrEmpty(Properties.Settings.Default.GamePath) && Directory.Exists(Properties.Settings.Default.GamePath))
                gameEntries.Add(new GameEntry { Path = Properties.Settings.Default.GamePath, BannerPath = bannerPath });
        }

        private void SaveGamePaths()
        {
            Properties.Settings.Default.GamePaths = string.Join("|", gameEntries.Select(e => e.Path));
            Properties.Settings.Default.Save();
        }

        // Buttons get their sounds from the app-wide PurpleButtonStyle; this handler is for
        // the game list items, which aren't buttons
        private void PlayHoverSound(object sender, MouseEventArgs e) => Sounds.PlayHover();

        private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveButton.IsEnabled = SelectButton.IsEnabled = GameList.SelectedIndex >= 0;
        }

        private void GameList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Sounds.PlayClick();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Game Executable|*.exe",
                Title = "Select Star Wars Battlefront Executable"
            };
            if (dialog.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !gameEntries.Any(ge => ge.Path == dir))
                {
                    var bannerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images", "swbf.png");
                    gameEntries.Add(new GameEntry { Path = dir, BannerPath = bannerPath });
                    SaveGamePaths();
                }
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (GameList.SelectedItem is GameEntry entry)
            {
                gameEntries.Remove(entry);
                SaveGamePaths();
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            ProceedWithSelection();
        }

        private void GameList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Sounds.PlayClick();
            if (GameList.SelectedItem != null)
                ProceedWithSelection();
        }

        private void ProceedWithSelection()
        {
            if (GameList.SelectedItem is GameEntry entry)
            {
                var path = entry.Path;
                Properties.Settings.Default.GamePath = path;
                Properties.Settings.Default.Save();

                var cachePath = CachePathHelper.GetCacheFilePath();
                if (!File.Exists(cachePath))
                {
                    var cacheWin = new CacheInstallWindow(path);
                    cacheWin.Owner = this;
                    cacheWin.Closed += (s, args) =>
                    {
                        if (cacheWin.DialogResult == true)
                        {
                            var main = new MainWindow();
                            Application.Current.MainWindow = main;
                            main.Show();
                            Close();
                        }
                        else
                        {
                            Show();
                        }
                    };
                    Hide();
                    cacheWin.ShowDialog();
                }
                else
                {
                    var main = new MainWindow();
                    Application.Current.MainWindow = main;
                    main.Show();
                    Close();
                }
            }
        }

    }
}
