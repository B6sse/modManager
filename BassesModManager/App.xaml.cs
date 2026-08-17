using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace BassesModManager
{
    public partial class App : Application
    {
        // Handlers for the app-wide PurpleButtonStyle in App.xaml, so every button gets
        // the same hover/click sounds without each window wiring it up itself
        private void Button_PlayHoverSound(object sender, MouseEventArgs e) => Sounds.PlayHover();

        private void Button_PlayClickSound(object sender, MouseButtonEventArgs e) => Sounds.PlayClick();

        // The slider's knob is the part you actually grab, so the hover sound belongs to it
        // rather than to the whole control
        private void SliderThumb_PlayHoverSound(object sender, MouseEventArgs e) => Sounds.PlayHover();

        // Held for the app's lifetime so Inno Setup's AppMutex check can detect a
        // running instance and close/replace it cleanly during silent auto-updates.
        // The name must match AppMutex in installer.iss.
        private static Mutex appMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            appMutex = new Mutex(false, "BassesModManagerAppMutex");

            // .NET user settings (GamePath) are stored per assembly version.
            // Migrate them forward once after an app upgrade so the user doesn't have to
            // re-select the game folder every time a new version is installed.
            if (BassesModManager.Properties.Settings.Default.UpgradeRequired)
            {
                try
                {
                    BassesModManager.Properties.Settings.Default.Upgrade();
                }
                catch
                {
                    // never block startup on settings migration
                }
                // Sound used to be a plain on/off flag and is now a volume level. Carry the
                // old choice across, or someone who had deliberately silenced the app would
                // get sound back after updating. Clearing the old flag keeps this from
                // firing again on the next upgrade.
                if (BassesModManager.Properties.Settings.Default.SoundMuted)
                {
                    BassesModManager.Properties.Settings.Default.SoundVolumePercent = 0;
                    BassesModManager.Properties.Settings.Default.SoundMuted = false;
                }

                // The crosshair choice and the scoreboard flag used to be separate settings
                // and are now one list of switched-on mods. Carry them across so nobody
                // finds their selection wiped after an update.
                if (string.IsNullOrEmpty(BassesModManager.Properties.Settings.Default.EnabledMods))
                {
                    // Fully qualified: inside App, a bare "MainWindow" is the inherited
                    // Application.MainWindow property rather than the window type
                    BassesModManager.Properties.Settings.Default.EnabledMods = BassesModManager.MainWindow.MigrateLegacySelection(
                        BassesModManager.Properties.Settings.Default.LastModFileName,
                        BassesModManager.Properties.Settings.Default.ScoreboardEnabled);
                }

                BassesModManager.Properties.Settings.Default.UpgradeRequired = false;
                BassesModManager.Properties.Settings.Default.Save();
            }

            base.OnStartup(e);

            // Load the UI sounds once up front so the first hover isn't delayed
            Sounds.Preload();

            // Custom cursor: hide the real OS cursor app-wide and let each window's
            // CustomCursorOverlay draw Assets/Images/cursor.png in its place. Overriding
            // here (rather than per-window) beats any per-control Cursor="Hand" setter,
            // so the same image shows everywhere regardless of what's being hovered.
            CustomCursor.Preload();
            Mouse.OverrideCursor = Cursors.None;

            // Mods live under ProgramData, not next to the exe: the app runs non-elevated
            // and has to be able to delete rejected files and download the Auric set
            CachePathHelper.EnsureModsDirectory();

            // Discord-style update flow (Plans/AUTO_UPDATE_PLAN.md, Spor A): check and
            // download quietly in the background; UpdateNotificationBar offers the
            // one-click install once a verified download is ready. Never blocks startup.
            _ = UpdateService.CheckAndPrepareAsync();

            // StartupUri is removed so the first window is picked here. There is only ever
            // the one: game selection and the cache install used to be separate windows,
            // and MainWindow now decides for itself which of its pages the app opens on.
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
        }
    }
}
