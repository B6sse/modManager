using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace BassesModManager
{
    public partial class GameSelectionWindow : Window
    {
        private ObservableCollection<GameEntry> gameEntries;
        private MediaPlayer _hoverPlayer;
        private MediaPlayer _clickPlayer;

        public GameSelectionWindow()
        {
            InitializeComponent();
            gameEntries = new ObservableCollection<GameEntry>();
            GameList.ItemsSource = gameEntries;

            LoadGamePaths();
            PreloadSounds();
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
            catch { }
        }

        private void PlayHoverSound(object sender, MouseEventArgs e) => PlayPreloaded(_hoverPlayer);
        private static void PlayPreloaded(MediaPlayer player)
        {
            if (player == null) return;
            try { player.Position = TimeSpan.Zero; player.Play(); } catch { }
        }

        private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveButton.IsEnabled = SelectButton.IsEnabled = GameList.SelectedIndex >= 0;
        }

        private void GameList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            PlayPreloaded(_clickPlayer);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (_clickPlayer != null) { _clickPlayer.Position = TimeSpan.Zero; _clickPlayer.Play(); }
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
            if (_clickPlayer != null) { _clickPlayer.Position = TimeSpan.Zero; _clickPlayer.Play(); }
            if (GameList.SelectedItem is GameEntry entry)
            {
                gameEntries.Remove(entry);
                SaveGamePaths();
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPreloaded(_clickPlayer);
            ProceedWithSelection();
        }

        private void GameList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_clickPlayer != null) { _clickPlayer.Position = TimeSpan.Zero; _clickPlayer.Play(); }
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
