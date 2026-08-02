using System;
using System.Windows;
using System.Windows.Controls;

namespace BassesModManager
{
    /// <summary>
    /// Small non-blocking bar shown at the bottom of a window when UpdateService has a
    /// verified update downloaded and ready. Shared by MainWindow and GameSelectionWindow.
    /// </summary>
    public partial class UpdateNotificationBar : UserControl
    {
        public UpdateNotificationBar()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                UpdateService.StateChanged += OnUpdateStateChanged;
                Refresh();
            };
            Unloaded += (s, e) => UpdateService.StateChanged -= OnUpdateStateChanged;
        }

        private void OnUpdateStateChanged(object sender, EventArgs e) => Dispatcher.Invoke(Refresh);

        private void Refresh()
        {
            Visibility = UpdateService.IsBarVisible ? Visibility.Visible : Visibility.Collapsed;

            if (UpdateService.IsUpdateReady)
            {
                DownloadSpinner.Visibility = Visibility.Collapsed;
                UpdateNowButton.Visibility = Visibility.Visible;
                LaterButton.Visibility = Visibility.Visible;
                MessageText.Text = $"Update {UpdateService.AvailableVersionText} is ready - the app reopens itself after installing.";
            }
            else if (UpdateService.IsDownloading)
            {
                // Neither button does anything useful yet: there's nothing to install,
                // and dismissing wouldn't stop the download anyway - it'd just hide the
                // only feedback the user has that something is happening.
                DownloadSpinner.Visibility = Visibility.Visible;
                UpdateNowButton.Visibility = Visibility.Collapsed;
                LaterButton.Visibility = Visibility.Collapsed;
                MessageText.Text = $"Update {UpdateService.AvailableVersionText} found - downloading...";
            }
        }

        private void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateNowButton.IsEnabled = false;
            UpdateService.ApplyUpdateAndRestart();
            UpdateNowButton.IsEnabled = true;
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e) => UpdateService.Dismiss();
    }
}
