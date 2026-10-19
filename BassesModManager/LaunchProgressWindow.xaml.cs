using System;
using System.Threading.Tasks;
using System.Windows;

namespace BassesModManager
{
    /// <summary>
    /// Modal progress window for the launch: placing Auric in the game folder, writing the
    /// launch marker and starting the game. DialogResult is true when the game was started.
    /// </summary>
    public partial class LaunchProgressWindow : Window
    {
        private readonly string _gamePath;
        private readonly string _modsDirectory;
        private readonly string[] _modFileNames;

        public LaunchProgressWindow(string gamePath, string modsDirectory, string[] modFileNames, LaunchPlan plan)
        {
            InitializeComponent();
            _gamePath = gamePath;
            _modsDirectory = modsDirectory;
            _modFileNames = modFileNames;

            Progress.HintText = plan.Reason + ".";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var logger = Progress.CreateLogger(this);
            logger.Status = "Preparing...";

            try
            {
                await Task.Run(() => PrepareAndLaunch(logger));
                DialogResult = true;
            }
            catch (UnauthorizedAccessException)
            {
                // Auric's DLL had to be replaced inside the game install, which a non-elevated
                // app is not allowed to do. MainWindow asks for elevation up front; this is
                // the case where that check and the copy disagreed.
                CustomMessageBox.Show(this,
                    "Writing to the game folder needs administrator rights. Restart the app as administrator.",
                    "Administrator needed");
                DialogResult = false;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this,
                    "Something went wrong before the game could start.\n\n" +
                    $"Technical details: {ex.Message}", "Could not launch");
                DialogResult = false;
            }
            Close();
        }

        private void PrepareAndLaunch(SmoothProgressLogger logger)
        {
            // Auric goes in first: it is the game-folder DLL that reads the marker and loads
            // the mods, so without the current build nothing below means anything
            logger.Status = "Preparing Auric...";
            GameFolder.InstallAuric(_gamePath);

            // Auric reads this at process start, before the game's own code runs, so it has
            // to be on disk before the game is started. Without the file Auric stays passive
            // for this session, so a failure here fails the launch.
            LaunchMarker.Write(_modsDirectory, _modFileNames, logger);

            // The game is started as a player would start it: no arguments, the EA app takes
            // over. The mods ride along in memory, from the marker.
            logger.Status = "Starting the game...";
            GameFolder.StartGame(_gamePath);
        }
    }
}
