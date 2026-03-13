using System;
using System.Windows;
using System.Net;
using System.Reflection;
using System.Linq;

namespace BassesModManager 
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            string versionUrl = "https://raw.githubusercontent.com/TSL-Battlefront/modManager/main/version.txt";
            if (UpdateChecker.IsUpdateRequired(versionUrl))
            {
                var result = CustomMessageBox.Show(null, "There is a new version of the mod manager available. You must install the latest version. Press OK to go to the download page, or cancel to close the app.", "Update required", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.OK)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://github.com/TSL-Battlefront/modManager/releases/latest",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception)
                    {
                        System.Diagnostics.Process.Start("https://github.com/TSL-Battlefront/modManager/releases/latest");
                    }
                }
                Shutdown();
                return;
            }

            base.OnStartup(e);

            if (!System.IO.Directory.Exists("Mods"))
            {
                System.IO.Directory.CreateDirectory("Mods");
            }

            // StartupUri is removed so we create GameSelectionWindow first (Frosty-style flow: game selection -> cache install if needed -> mod selection)
            var gameSelectionWindow = new GameSelectionWindow();
            MainWindow = gameSelectionWindow;
            gameSelectionWindow.Show();
        }
    }

    public static class UpdateChecker
    {
        public static bool IsUpdateRequired(string versionUrl)
        {
            try
            {
                using (var client = new WebClient())
                {
                    string minRequiredVersion = client.DownloadString(versionUrl).Trim();
                    Version current = Assembly.GetExecutingAssembly().GetName().Version;
                    Version required = new Version(minRequiredVersion);
                    return current < required;
                }
            }
            catch
            {
                // If there is no connection to the server, let the user use the app (or handle as desired)
                return false;
            }
        }
    }
} 