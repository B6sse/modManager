using System;
using System.IO;
using System.Windows.Media;

namespace BassesModManager
{
    /// <summary>
    /// Shared UI sound effects. The MediaPlayers are created once at startup and kept for
    /// the lifetime of the app - loading them per window (or worse, per dialog) makes the
    /// first playback noticeably late, which is very audible on hover.
    /// </summary>
    public static class Sounds
    {
        private const double Volume = 0.2;

        private static MediaPlayer hoverPlayer;
        private static MediaPlayer clickPlayer;
        private static bool preloaded;

        /// <summary>Raised whenever IsMuted changes, so any visible mute button can update.</summary>
        public static event EventHandler MuteChanged;

        /// <summary>Persisted in Properties.Settings so it survives app restarts.</summary>
        public static bool IsMuted
        {
            get => Properties.Settings.Default.SoundMuted;
            set
            {
                if (Properties.Settings.Default.SoundMuted == value)
                    return;
                Properties.Settings.Default.SoundMuted = value;
                Properties.Settings.Default.Save();
                MuteChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static void ToggleMute() => IsMuted = !IsMuted;

        /// <summary>
        /// Must be called once from the UI thread at startup (MediaPlayer needs a Dispatcher).
        /// </summary>
        public static void Preload()
        {
            if (preloaded)
                return;
            preloaded = true;

            hoverPlayer = Load("hover.mp3");
            clickPlayer = Load("click.mp3");
        }

        public static void PlayHover() => Play(hoverPlayer);

        public static void PlayClick() => Play(clickPlayer);

        private static MediaPlayer Load(string fileName)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Sounds", fileName);
                if (!File.Exists(path))
                    return null;

                var player = new MediaPlayer { Volume = Volume };
                player.Open(new Uri(path, UriKind.Absolute));
                return player;
            }
            catch
            {
                return null; // sounds are cosmetic - never let them break the UI
            }
        }

        private static void Play(MediaPlayer player)
        {
            if (player == null || IsMuted)
                return;

            try
            {
                player.Position = TimeSpan.Zero;
                player.Play();
            }
            catch
            {
            }
        }
    }
}
