using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BassesModManager
{
    /// <summary>
    /// Two-option switch: both labels stay visible, the active one gets the app's purple
    /// fill. Backed by a single CheckBox so hovering or clicking either half behaves
    /// identically, rather than two independent controls. Used for the on/off settings
    /// and, with its labels overridden, for the main window's mode switch.
    /// </summary>
    public partial class SettingToggle : UserControl
    {
        /// <summary>Raised only when the value actually changes, never on a repeat click.</summary>
        public event EventHandler Toggled;

        /// <summary>Label on the left half, shown as active while <see cref="IsOn"/>.</summary>
        public static readonly DependencyProperty OnLabelProperty =
            DependencyProperty.Register(nameof(OnLabel), typeof(string), typeof(SettingToggle), new PropertyMetadata("ON"));

        /// <summary>Label on the right half, shown as active while <see cref="IsOn"/> is false.</summary>
        public static readonly DependencyProperty OffLabelProperty =
            DependencyProperty.Register(nameof(OffLabel), typeof(string), typeof(SettingToggle), new PropertyMetadata("OFF"));

        /// <summary>
        /// A dependency property rather than a plain one so the switch can be data-bound -
        /// the mod list has one per row, bound to whether that mod is selected.
        /// </summary>
        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(SettingToggle),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOnChanged));

        public SettingToggle()
        {
            InitializeComponent();
            Refresh();
        }

        private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var toggle = (SettingToggle)d;
            toggle.Refresh();
            toggle.Toggled?.Invoke(toggle, EventArgs.Empty);
        }

        public string OnLabel
        {
            get => (string)GetValue(OnLabelProperty);
            set => SetValue(OnLabelProperty, value);
        }

        public string OffLabel
        {
            get => (string)GetValue(OffLabelProperty);
            set => SetValue(OffLabelProperty, value);
        }

        public bool IsOn
        {
            get => (bool)GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
        }

        private void Refresh() => ToggleCheckBox.IsChecked = IsOn;

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
