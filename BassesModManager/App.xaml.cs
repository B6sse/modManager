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

        private void Button_PlayClickSound(object sender, RoutedEventArgs e) => Sounds.PlayClick();

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

            if (!System.IO.Directory.Exists("Mods"))
            {
                System.IO.Directory.CreateDirectory("Mods");
            }

            // Discord-style update flow (Plans/AUTO_UPDATE_PLAN.md, Spor A): check and
            // download quietly in the background; UpdateNotificationBar offers the
            // one-click install once a verified download is ready. Never blocks startup.
            _ = UpdateService.CheckAndPrepareAsync();

            // StartupUri is removed so we create GameSelectionWindow first (Frosty-style flow: game selection -> cache install if needed -> mod selection).
            // With a valid game already saved, the selection window skips itself automatically.
            var gameSelectionWindow = new GameSelectionWindow();
            MainWindow = gameSelectionWindow;
            gameSelectionWindow.Show();
        }
    }
}
