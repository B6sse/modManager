using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace BassesModManager
{
    /// <summary>
    /// The app's own arrow, Assets/Images/cursor.png, as a real Windows cursor. It used to
    /// be an Image moved by hand on every mouse event inside each window, which is drawn a
    /// frame late and lags visibly over a remote desktop or a VM. A hardware cursor is
    /// drawn by Windows itself on the cursor layer, outside the app's rendering, so it
    /// keeps up the way any cursor does. The PNG is scaled to the screen's DPI once at
    /// start-up, since cursor bitmaps are in device pixels.
    /// </summary>
    public static class CustomCursor
    {
        /// <summary>Hotspot of the arrow: its tip, top-left of the PNG.</summary>
        private const int HotspotX = 0, HotspotY = 0;

        public static Cursor Load()
        {
            try
            {
                // A content file next to the exe (the csproj copies Assets\Images\*.png to the
                // output), so read it from there like the banner is
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Images", "cursor.png");
                using (Stream png = File.OpenRead(path))
                using (var source = new Bitmap(png))
                using (var scaled = ScaleToScreenDpi(source, out double scale))
                {
                    return FromBitmap(scaled, (int)Math.Round(HotspotX * scale), (int)Math.Round(HotspotY * scale));
                }
            }
            catch
            {
                // The stock arrow is better than no cursor at all
                return Cursors.Arrow;
            }
        }

        private static Bitmap ScaleToScreenDpi(Bitmap source, out double scale)
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                scale = screen.DpiX / 96.0;
            if (Math.Abs(scale - 1.0) < 0.01)
            {
                scale = 1.0;
                return new Bitmap(source);
            }

            int width = (int)Math.Round(source.Width * scale);
            int height = (int)Math.Round(source.Height * scale);
            var scaled = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return scaled;
        }

        /// <summary>
        /// An HCURSOR from a 32-bit bitmap: GetHicon gives an icon with the right mask and
        /// colour bitmaps, GetIconInfo takes them apart, and CreateIconIndirect puts them
        /// back together as a cursor with a hotspot.
        /// </summary>
        private static Cursor FromBitmap(Bitmap bitmap, int hotspotX, int hotspotY)
        {
            IntPtr icon = bitmap.GetHicon();
            try
            {
                if (!GetIconInfo(icon, out IconInfo info))
                    throw new InvalidOperationException("GetIconInfo failed");
                try
                {
                    info.fIcon = false;
                    info.xHotspot = hotspotX;
                    info.yHotspot = hotspotY;
                    IntPtr cursor = CreateIconIndirect(ref info);
                    if (cursor == IntPtr.Zero)
                        throw new InvalidOperationException("CreateIconIndirect failed");
                    // Lives for the whole process, so the handle is never released
                    return CursorInteropHelper.Create(new SafeFileHandle(cursor, ownsHandle: false));
                }
                finally
                {
                    if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
                    if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
                }
            }
            finally
            {
                DestroyIcon(icon);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo pIconInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateIconIndirect(ref IconInfo icon);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
