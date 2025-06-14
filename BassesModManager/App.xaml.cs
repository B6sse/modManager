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
            string versionUrl = "https://raw.githubusercontent.com/B6sse/modManagerUpdates/main/version.txt";
            if (UpdateChecker.IsUpdateRequired(versionUrl))
            {
                var result = CustomMessageBox.Show(Application.Current.MainWindow, "There is a new version of the mod manager available. You must install the latest version. Press OK to go to the download page, or cancel to close the app.", "Update required", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.OK)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/B6sse/modManagerUpdates/releases/latest",
                        UseShellExecute = true
                    });
                }
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);
            
            // Initialize any required services or configurations here
            if (!System.IO.Directory.Exists("Mods"))
            {
                System.IO.Directory.CreateDirectory("Mods");
            }
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