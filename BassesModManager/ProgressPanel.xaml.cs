using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BassesModManager
{
    /// <summary>
    /// The frame both progress windows are made of. The windows themselves are left with
    /// nothing but the flow they run, so the banner and the layout are decided in one
    /// place instead of two that have to be kept in step by hand. Each window is still as
    /// tall as its own content: the two are the same thing, not the same size.
    /// </summary>
    public partial class ProgressPanel : UserControl
    {
        public ProgressPanel()
        {
            InitializeComponent();
            LoadBanner();
        }

        /// <summary>The line under the bar telling the user what to expect.</summary>
        public string HintText
        {
            get => HintTextBlock.Text;
            set => HintTextBlock.Text = value;
        }

        /// <summary>
        /// Whether the flow can be called off. Off for the cache build, which has to
        /// finish before there is anything to use.
        /// </summary>
        public bool ShowCancel
        {
            get => CancelButton.Visibility == Visibility.Visible;
            set => CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Raised at most once: the button takes itself out on the way.</summary>
        public event EventHandler Cancelled;

        /// <summary>
        /// Hands the panel's own status line, bar and spinner to the logger the flow
        /// reports through, so the parts stay private to the panel.
        /// </summary>
        internal SmoothProgressLogger CreateLogger(Window window, bool barOnAnyProgress = false)
        {
            return new SmoothProgressLogger(window, ProgressBar, StatusText, SpinnerPanel, barOnAnyProgress);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // A second press has nothing left to do, and the flow needs a moment to wind
            // down - disabling says that plainly
            CancelButton.IsEnabled = false;
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        private void LoadBanner()
        {
            try
            {
                string bannerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Banners", "SWBF.png");
                if (!File.Exists(bannerPath))
                    return;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(bannerPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                BannerImage.Source = bmp;
            }
            catch
            {
                // A missing or unreadable banner is not worth failing a launch over
            }
        }
    }
}
