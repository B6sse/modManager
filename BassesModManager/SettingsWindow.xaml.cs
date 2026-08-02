using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace BassesModManager
{
    /// <summary>
    /// Full settings screen, navigated to from the gear button in MainWindow or
    /// GameSelectionWindow. Changes apply and persist immediately - there is no
    /// OK/Cancel, so leaving the page can never lose a change.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // Builds a fresh instance of whichever window opened us (MainWindow or
        // GameSelectionWindow) once we're done here. A factory rather than a kept
        // reference because the opener already closed itself before showing us - it
        // can't just be reshown. Relies on the relevant state (crosshair selection,
        // sound, etc.) being persisted to Settings rather than living only in memory.
        private readonly Func<Window> createOrigin;

        // True only when BACK or Escape triggered the close - i.e. an actual "go back"
        // request. The title bar's X (or Alt+F4) closes this same window without ever
        // setting it, so OnClosing can tell a real navigation apart from the user
        // actually wanting to quit the app, and let the latter close for real.
        private bool goingBack;

        public SettingsWindow(Func<Window> createOrigin)
        {
            InitializeComponent();
            this.createOrigin = createOrigin;

            // Sound is stored as "muted", but shown as "sound effects on/off"
            SoundToggle.IsOn = !Sounds.IsMuted;
            RestoreToggle.IsOn = Properties.Settings.Default.RestoreAfterGame;
            RefreshGamePathText();
        }

        private void RefreshGamePathText()
        {
            string path = Properties.Settings.Default.GamePath;
            GamePathText.Text = string.IsNullOrEmpty(path) ? "No game folder configured yet" : path;
        }

        private void SoundToggle_Toggled(object sender, EventArgs e)
        {
            // Sounds.IsMuted persists itself
            Sounds.IsMuted = !SoundToggle.IsOn;
        }

        private void RestoreToggle_Toggled(object sender, EventArgs e)
        {
            Properties.Settings.Default.RestoreAfterGame = RestoreToggle.IsOn;
            Properties.Settings.Default.Save();
        }

        private void ChangeGameButton_Click(object sender, RoutedEventArgs e)
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
            RefreshGamePathText();
        }

        private void OpenModDataButton_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = Properties.Settings.Default.GamePath;
            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                CustomMessageBox.Show(this, "Select your game folder first before opening the mod data folder.", "No game selected");
                return;
            }

            string modDataPath = Path.Combine(gamePath, "ModData");
            if (!Directory.Exists(modDataPath))
            {
                CustomMessageBox.Show(this, "No mod data yet - launch the game with a crosshair selected at least once first.", "Nothing to show");
                return;
            }

            try
            {
                Process.Start("explorer.exe", modDataPath);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Could not open the ModData folder.\n\nTechnical details: {ex.Message}", "Error");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            goingBack = true;
            Close();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                goingBack = true;
                Close();
                e.Handled = true;
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel || !goingBack)
                return;

            // Show the window we're returning to *before* this one actually closes (same
            // order the existing MainWindow<->GameSelectionWindow navigation already
            // uses). Application.ShutdownMode defaults to OnLastWindowClose, and this is
            // normally the only open window - doing this in OnClosed instead (after the
            // window is already gone) let the open-window count hit zero and shut the
            // whole app down before the new window ever got a chance to show.
            var origin = createOrigin();
            Application.Current.MainWindow = origin;
            origin.Show();
        }
    }
}
