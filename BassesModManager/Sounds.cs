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
        /// <summary>
        /// What each slider step means on MediaPlayer's 0-1 scale, which is linear
        /// amplitude. Hearing is not: spacing these evenly made the top steps almost
        /// indistinguishable, because equal amplitude increases get smaller and smaller in
        /// dB as the level rises.
        /// <para>
        /// So the steps go up by a constant <i>ratio</i> (~1.73x, about +4.8 dB each)
        /// rather than a constant amount. Every step is then the same amount louder to the
        /// ear. 50% is pinned to 0.2, the fixed level the app used before the slider
        /// existed, so an untouched install sounds exactly as it always did.
        /// </para>
        /// One entry per slider position - the slider snaps to 25% steps, so changing the
        /// number of entries means changing TickFrequency in PurpleSliderStyle to match.
        /// </summary>
        private static readonly double[] VolumeSteps = { 0.0, 0.115, 0.2, 0.345, 0.6 };

        private static MediaPlayer hoverPlayer;
        private static MediaPlayer clickPlayer;
        private static bool preloaded;

        /// <summary>
        /// 0-100, where 0 is silent. Persisted in Properties.Settings so it survives app
        /// restarts, and applied to the live players immediately rather than only at load.
        /// </summary>
        public static int VolumePercent
        {
            get => Clamp(Properties.Settings.Default.SoundVolumePercent);
            set
            {
                int clamped = Clamp(value);
                if (Properties.Settings.Default.SoundVolumePercent == clamped)
                    return;

                Properties.Settings.Default.SoundVolumePercent = clamped;
                Properties.Settings.Default.Save();
                ApplyVolume();
            }
        }

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
            ApplyVolume();
        }

        public static void PlayHover() => Play(hoverPlayer);

        public static void PlayClick() => Play(clickPlayer);

        private static int Clamp(int percent) => Math.Min(100, Math.Max(0, percent));

        private static void ApplyVolume()
        {
            int step = Math.Min(VolumeSteps.Length - 1, VolumePercent * (VolumeSteps.Length - 1) / 100);
            double volume = VolumeSteps[step];

            if (hoverPlayer != null)
                hoverPlayer.Volume = volume;
            if (clickPlayer != null)
                clickPlayer.Volume = volume;
        }

        private static MediaPlayer Load(string fileName)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Sounds", fileName);
                if (!File.Exists(path))
                    return null;

                var player = new MediaPlayer();
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
            if (player == null || VolumePercent == 0)
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
