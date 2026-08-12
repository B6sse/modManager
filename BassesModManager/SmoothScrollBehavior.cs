using System.Windows;
using System.Windows.Controls;

namespace BassesModManager
{
    /// <summary>
    /// ScrollViewer.VerticalOffset isn't a dependency property, so it can't be animated
    /// directly. This exposes an animatable stand-in that forwards to
    /// ScrollToVerticalOffset, which is what lets mouse-wheel scrolling glide to its
    /// target instead of jumping straight there.
    /// </summary>
    public static class SmoothScrollBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset",
                typeof(double),
                typeof(SmoothScrollBehavior),
                new PropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static double GetVerticalOffset(ScrollViewer scrollViewer) => (double)scrollViewer.GetValue(VerticalOffsetProperty);

        public static void SetVerticalOffset(ScrollViewer scrollViewer, double value) => scrollViewer.SetValue(VerticalOffsetProperty, value);

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as ScrollViewer)?.ScrollToVerticalOffset((double)e.NewValue);
        }
    }
}
