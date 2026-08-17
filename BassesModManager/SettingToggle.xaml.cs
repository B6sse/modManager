using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BassesModManager
{
    /// <summary>
    /// A switch in one of two shapes, sharing everything but their looks: the same value,
    /// the same event, the same sounds, and one CheckBox underneath so hovering or
    /// clicking anywhere on it behaves identically rather than half at a time.
    /// <para>
    /// By default both choices are named and both stay on screen, which is what the mode
    /// switch needs - EA SERVERS and AURIC are two options, not on and off. Set
    /// <see cref="Compact"/> for a plain on/off, which drops the words and takes well
    /// under half the width.
    /// </para>
    /// </summary>
    public partial class SettingToggle : UserControl
    {
        /// <summary>Raised only when the value actually changes, never on a repeat click.</summary>
        public event EventHandler Toggled;

        /// <summary>
        /// Whether to use the narrow, wordless shape. Only sensible when the switch really
        /// is on and off: with a knob and a colour standing in for the labels, there is
        /// nowhere left to say what the two states are.
        /// </summary>
        public static readonly DependencyProperty CompactProperty =
            DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(SettingToggle), new PropertyMetadata(false));

        /// <summary>
        /// How tall the compact shape is drawn. Everything else about it follows, so this
        /// is the only number a caller ever sets: the mod list leaves it at the default,
        /// which is deliberately shorter than its rows, while the settings page raises it
        /// to match the buttons standing next to it.
        /// </summary>
        public static readonly DependencyProperty CompactHeightProperty =
            DependencyProperty.Register(nameof(CompactHeight), typeof(double), typeof(SettingToggle),
                new PropertyMetadata(DefaultCompactHeight, OnCompactHeightChanged));

        private static readonly DependencyPropertyKey CompactWidthPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(CompactWidth), typeof(double), typeof(SettingToggle),
                new PropertyMetadata(TrackWidthFor(DefaultCompactHeight)));

        /// <summary>Track width for the current <see cref="CompactHeight"/>. Bound by the template.</summary>
        public static readonly DependencyProperty CompactWidthProperty = CompactWidthPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey KnobSizePropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(KnobSize), typeof(double), typeof(SettingToggle),
                new PropertyMetadata(KnobSizeFor(DefaultCompactHeight)));

        /// <summary>Knob size for the current <see cref="CompactHeight"/>. Bound by the template.</summary>
        public static readonly DependencyProperty KnobSizeProperty = KnobSizePropertyKey.DependencyProperty;

        private const double DefaultCompactHeight = 28;

        /// <summary>
        /// Gap between the knob and the track on every side. The same 3px the labelled
        /// shape's thumb and the mod list's variant segments are inset by, so the three
        /// controls read as one family however tall any of them happens to be.
        /// </summary>
        private const double KnobInset = 3;

        private const int SlideMilliseconds = 150;

        private static double KnobSizeFor(double height) => height - 2 * KnobInset;

        /// <summary>
        /// Two knobs side by side, plus the insets and the gap they leave between the two
        /// resting positions. Written out rather than kept as a ratio because it makes the
        /// travel come out at exactly <c>height - 4</c>, which is what the slide animates.
        /// </summary>
        private static double TrackWidthFor(double height) => 2 * height - 4;

        private static double KnobTravelFor(double height) => height - 4;

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

            // The knob cannot be placed from the constructor: Compact is set by the parent
            // markup afterwards, so the compact template is not the one in use yet.
            Loaded += (s, e) => PlaceKnob(animate: false);
        }

        private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var toggle = (SettingToggle)d;
            toggle.Refresh();

            // Only slide once the control is up. Restoring a saved selection sets this on
            // every switch in the list at once, and animating that would have the whole
            // window twitch on open.
            toggle.PlaceKnob(animate: toggle.IsLoaded);
            toggle.Toggled?.Invoke(toggle, EventArgs.Empty);
        }

        private static void OnCompactHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var toggle = (SettingToggle)d;
            double height = (double)e.NewValue;

            toggle.SetValue(CompactWidthPropertyKey, TrackWidthFor(height));
            toggle.SetValue(KnobSizePropertyKey, KnobSizeFor(height));
            toggle.PlaceKnob(animate: false);
        }

        public bool Compact
        {
            get => (bool)GetValue(CompactProperty);
            set => SetValue(CompactProperty, value);
        }

        public double CompactHeight
        {
            get => (double)GetValue(CompactHeightProperty);
            set => SetValue(CompactHeightProperty, value);
        }

        public double CompactWidth => (double)GetValue(CompactWidthProperty);

        public double KnobSize => (double)GetValue(KnobSizeProperty);

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

        /// <summary>
        /// Moves the compact shape's knob to the side the current value calls for, sliding
        /// there or snapping.
        /// <para>
        /// Driven from here rather than by a storyboard in the template, which is where the
        /// rest of the switch's states live. A slide has to animate to an absolute offset,
        /// and a storyboard inside a template cannot have that offset bound to the
        /// control's height - it would have to be a literal, correct for exactly one size.
        /// </para>
        /// </summary>
        private void PlaceKnob(bool animate)
        {
            TranslateTransform shift = KnobShift();
            if (shift == null)
                return;

            double target = IsOn ? KnobTravelFor(CompactHeight) : 0;

            if (animate)
            {
                shift.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(target, TimeSpan.FromMilliseconds(SlideMilliseconds))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });
                return;
            }

            // Cleared first: an animated value outranks a direct set, so without this the
            // knob would stay wherever the last slide left it.
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.X = target;
        }

        /// <summary>
        /// The transform the knob is moved by, created on first use. Null whenever the
        /// labelled template is in use, which has no knob to move.
        /// <para>
        /// Made here rather than declared in the template because a Freezable written into
        /// markup can be frozen, and a frozen transform cannot be animated.
        /// </para>
        /// </summary>
        private TranslateTransform KnobShift()
        {
            ToggleCheckBox.ApplyTemplate();
            if (!(ToggleCheckBox.Template?.FindName("knob", ToggleCheckBox) is Border knob))
                return null;

            if (!(knob.RenderTransform is TranslateTransform shift))
            {
                shift = new TranslateTransform();
                knob.RenderTransform = shift;
            }

            return shift;
        }

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
