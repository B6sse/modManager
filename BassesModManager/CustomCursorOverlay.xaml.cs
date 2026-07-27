using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BassesModManager
{
    public partial class CustomCursorOverlay : UserControl
    {
        private Window owner;

        public CustomCursorOverlay()
        {
            InitializeComponent();
            CursorImage.Source = CustomCursor.Image;
            Loaded += CustomCursorOverlay_Loaded;
        }

        private void CustomCursorOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            owner = Window.GetWindow(this);
            if (owner == null)
                return;

            owner.PreviewMouseMove += Owner_PreviewMouseMove;
            owner.MouseLeave += (s, args) => CursorImage.Visibility = Visibility.Collapsed;
        }

        private void Owner_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(owner);
            CursorPosition.X = pos.X;
            CursorPosition.Y = pos.Y;
            CursorImage.Visibility = Visibility.Visible;
        }
    }
}
