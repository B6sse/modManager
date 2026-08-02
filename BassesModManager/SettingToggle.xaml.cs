using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BassesModManager
{
    /// <summary>
    /// Two-option switch used on the settings page: both labels stay visible, the active
    /// one gets the app's purple fill. Backed by a single CheckBox so hovering or
    /// clicking either half behaves identically, rather than two independent controls.
    /// </summary>
    public partial class SettingToggle : UserControl
    {
        /// <summary>Raised only when the value actually changes, never on a repeat click.</summary>
        public event EventHandler Toggled;

        private bool isOn = true;

        public SettingToggle()
        {
            InitializeComponent();
            Refresh();
        }

        public bool IsOn
        {
            get => isOn;
            set
            {
                if (isOn == value)
                    return;
                isOn = value;
                Refresh();
                Toggled?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Refresh() => ToggleCheckBox.IsChecked = isOn;

        private void ToggleCheckBox_MouseEnter(object sender, MouseEventArgs e) => Sounds.PlayHover();

        private void ToggleCheckBox_Click(object sender, RoutedEventArgs e)
        {
            // Apply first, play after: switching sound back on should be audible, and
            // switching it off should be silent rather than clicking one last time.
            IsOn = ToggleCheckBox.IsChecked == true;
            Sounds.PlayClick();
        }
    }
}
