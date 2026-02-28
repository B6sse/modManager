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
            string versionUrl = "https://raw.githubusercontent.com/B6sse/modManager/main/BassesModManager/version.txt";
            if (UpdateChecker.IsUpdateRequired(versionUrl))
            {
                var result = CustomMessageBox.Show(null, "There is a new version of the mod manager available. You must install the latest version. Press OK to go to the download page, or cancel to close the app.", "Update required", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.OK)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://github.com/B6sse/modManager/releases/latest",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception)
                    {
                        System.Diagnostics.Process.Start("https://github.com/B6sse/modManager/releases/latest");
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

            // StartupUri is removed so we create MainWindow manually (avoids WPF creating it during update path and causing "Application shutting down" exception)
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
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