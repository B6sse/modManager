using System;
using System.Windows;
using System.Windows.Controls;

namespace BassesModManager
{
    /// <summary>
    /// Small speaker icon toggling Sounds.IsMuted. Shared by MainWindow and
    /// GameSelectionWindow; intentionally has no sound of its own, so muting/unmuting
    /// never plays a confusing click.
    /// </summary>
    public partial class MuteToggleButton : UserControl
    {
        public MuteToggleButton()
        {
            InitializeComponent();
            Refresh();
            Loaded += (s, e) => Sounds.MuteChanged += OnMuteChanged;
            Unloaded += (s, e) => Sounds.MuteChanged -= OnMuteChanged;
        }

        private void OnMuteChanged(object sender, EventArgs e) => Dispatcher.Invoke(Refresh);

        private void Refresh()
        {
            WavesIcon.Visibility = Sounds.IsMuted ? Visibility.Collapsed : Visibility.Visible;
            MuteIcon.Visibility = Sounds.IsMuted ? Visibility.Visible : Visibility.Collapsed;
            ToggleButton.ToolTip = Sounds.IsMuted ? "Unmute sound effects" : "Mute sound effects";
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e) => Sounds.ToggleMute();
    }
}
