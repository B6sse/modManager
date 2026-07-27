using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BassesModManager
{
    public partial class GameSelectionWindow : Window
    {
        private ObservableCollection<GameEntry> gameEntries;
        private bool gameValid;

        public GameSelectionWindow() : this(false)
        {
        }

        public GameSelectionWindow(bool autoProceedIfConfigured)
        {
            InitializeComponent();
            gameEntries = new ObservableCollection<GameEntry>();
            GameList.ItemsSource = gameEntries;

            LoadSavedGame();

            // If a valid installation is already saved, there's nothing to choose - skip
            // straight ahead. Only on startup: the BACK arrow opens this window without
            // auto-proceeding, so the user can actually change games from here.
            string savedPath = Properties.Settings.Default.GamePath;
            if (autoProceedIfConfigured && FrostyRuntime.IsValidBattlefrontInstall(savedPath))
            {
                Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => ProceedWithSelection(savedPath)),
                    DispatcherPriority.Background);
            }
        }

        private void LoadSavedGame()
        {
            gameEntries.Clear();

            string path = Properties.Settings.Default.GamePath;
            gameValid = FrostyRuntime.IsValidBattlefrontInstall(path);
            if (gameValid)
                gameEntries.Add(new GameEntry { Path = path, BannerPath = "Assets/Images/swbf.png" });

            // SELECT requires an actual click on the row first, even if a valid game is
            // already configured - re-armed every refresh (new path located, or reopened
            // after the saved path stopped being valid), not tied to GameList.SelectedItem
            // (with only ever one possible entry, real list-selection state doesn't add
            // information, and previously drove a permanent-purple-row bug).
            SelectButton.IsEnabled = false;
            LocateButton.Content = gameValid ? "CHANGE GAME PATH" : "LOCATE BATTLEFRONT";

            EmptyStateText.Visibility = gameValid ? Visibility.Collapsed : Visibility.Visible;
            DoubleClickHintText.Visibility = gameValid ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PlayHoverSound(object sender, MouseEventArgs e) => Sounds.PlayHover();

        private void GameList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Sounds.PlayClick();
            SelectButton.IsEnabled = gameValid;
        }

        private void GameList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (gameEntries.Count > 0)
                ProceedWithSelection(gameEntries[0].Path);
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (gameEntries.Count > 0)
                ProceedWithSelection(gameEntries[0].Path);
        }

        private void LocateButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Game Executable|*.exe",
                Title = "Select Star Wars Battlefront Executable"
            };
            if (dialog.ShowDialog() != true)
                return;

            string dir = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrEmpty(dir) || !FrostyRuntime.IsValidBattlefrontInstall(dir))
            {
                CustomMessageBox.Show(this,
                    "This doesn't look like a Star Wars Battlefront installation. Make sure you selected the folder containing StarWarsBattlefront.exe.",
                    "Wrong game");
                return;
            }

            Properties.Settings.Default.GamePath = dir;
            Properties.Settings.Default.Save();
            LoadSavedGame();
        }

        private void ProceedWithSelection(string path)
        {
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
