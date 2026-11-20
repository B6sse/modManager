using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BassesModManager
{
    /// <summary>
    /// Colours the standard Windows title bar to match the app, through the desktop
    /// window manager rather than by drawing a bar of our own: the minimize, maximize
    /// and close buttons, Snap Layouts, dragging and double-click to maximize all stay
    /// Windows' own. Windows 11 takes every colour; Windows 10 only takes dark mode,
    /// which is still a dark bar. Older systems ignore the calls and keep the default.
    /// </summary>
    internal static class TitleBar
    {
        // DWMWINDOWATTRIBUTE values. 19 is the dark-mode attribute's number on
        // Windows 10 builds before 20H1, 20 from then on.
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        // COLORREF is 0x00BBGGRR
        private static readonly int Caption = ColorRef(0x00, 0x00, 0x00);
        private static readonly int Text = ColorRef(0xFF, 0xFF, 0xFF);
        private static readonly int Border = ColorRef(0x00, 0x00, 0x00);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// Applies the colours as soon as the window has a handle, before it is first
        /// shown, so the default light bar never flashes. Call from the constructor.
        /// </summary>
        public static void Use(Window window)
        {
            window.SourceInitialized += (s, e) => Apply(new WindowInteropHelper(window).Handle);
        }

        private static void Apply(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));

                int caption = Caption, text = Text, border = Border;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch (DllNotFoundException)
            {
                // No desktop window manager to ask; the default bar is fine
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
    }
}
