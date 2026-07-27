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
                MessageText.Text = $"Update {UpdateService.ReadyVersionText} is ready - the app reopens itself after installing.";
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
