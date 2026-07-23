using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BassesModManager
{
    /// <summary>
    /// Small non-blocking bar shown at the bottom of a window when UpdateService has a
    /// verified update downloaded and ready. Shared by MainWindow and GameSelectionWindow.
    /// </summary>
    public partial class UpdateNotificationBar : UserControl
    {
        private MediaPlayer _hoverPlayer;
        private MediaPlayer _clickPlayer;

        public UpdateNotificationBar()
        {
            InitializeComponent();
            PreloadSounds();
            Loaded += (s, e) =>
            {
                UpdateService.StateChanged += OnUpdateStateChanged;
                Refresh();
            };
            Unloaded += (s, e) => UpdateService.StateChanged -= OnUpdateStateChanged;
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

        private void OnUpdateStateChanged(object sender, EventArgs e) => Dispatcher.Invoke(Refresh);

        private void Refresh()
        {
            Visibility = UpdateService.IsBarVisible ? Visibility.Visible : Visibility.Collapsed;
            if (UpdateService.IsUpdateReady)
                MessageText.Text = $"Update {UpdateService.ReadyVersionText} is ready. Installing takes a few seconds and the app reopens by itself.";
        }

        private void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPreloaded(_clickPlayer);
            UpdateNowButton.IsEnabled = false;
            UpdateService.ApplyUpdateAndRestart();
            UpdateNowButton.IsEnabled = true;
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPreloaded(_clickPlayer);
            UpdateService.Dismiss();
        }
    }
}
