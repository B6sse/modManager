using System;
using System.Windows.Media.Imaging;

namespace BassesModManager
{
    // Decoded once at startup (like Sounds.Preload()) so every CustomCursorOverlay
    // instance shares the same bitmap instead of re-decoding the file per window.
    public static class CustomCursor
    {
        public static BitmapImage Image { get; private set; }

        public static void Preload()
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri("pack://application:,,,/Assets/Images/cursor.png");
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            Image = bitmap;
        }
    }
}
