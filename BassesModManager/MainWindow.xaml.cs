using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BassesModManager
{
    public partial class MainWindow : Window
    {
        /// <summary>Every catalogued mod, in catalog order, which is also apply order.</summary>
        private List<ModItem> allMods;

        /// <summary>What the list shows: every catalogued mod the disk could back up.</summary>
        private ObservableCollection<ModItem> visibleMods;

        private string modsDirectory;
        /// <summary>One mod file. This is the unit of approval: a name and the contents it must have.</summary>
        private sealed class CatalogFile
        {
            public string FileName { get; set; }
            public string Sha256 { get; set; }
        }

        /// <summary>
        /// One row in the mod list, and one thing the user can pick. Usually a single file,
        /// but not always: a mod that was split across several files is still one choice.
        /// </summary>

        private sealed class CatalogEntry
        {
            /// <summary>Stable id used to remember what was selected.</summary>
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }

            /// <summary>Every file the row applies, in apply order.</summary>
            public CatalogFile[] Files { get; set; }
        }

        /// <summary>
        /// Translates the pre-1.7 settings, which carried a separate scoreboard flag, into the
        /// single list of switched-on mods used since. The crosshair the old settings also
        /// named is no longer in the catalog, so that part is dropped.
        /// </summary>
        public static string MigrateLegacySelection(bool scoreboardEnabled)
        {
            var carried = new List<string>();

            if (scoreboardEnabled)
                carried.Add("Improved_Scoreboard.fbmod");

            return string.Join(";", carried);
        }

        private static CatalogEntry SingleFile(string fileName, string displayName, string description,
            string sha256)
        {
            return new CatalogEntry
            {
                Key = fileName,
                DisplayName = displayName,
                Description = description,
                Files = new[] { new CatalogFile { FileName = fileName, Sha256 = sha256 } }
            };
        }
        /// <summary>
        /// Every mod the app ships. The set is fixed between releases, so the window is
        /// drawn straight from this list and appears complete, instead of waiting on a scan
        /// of the mods folder to discover what is there.
        /// <para>
        /// Nothing here is trusted on the strength of being listed: a mod counts as
        /// approved only once <see cref="VerifyMods"/> has confirmed the file on disk
        /// carries this exact name and hashes to this exact value, and only verified mods
        /// can be launched. Listing a file by name and hash is stricter than approving any
        /// file whose hash is known, which is what let differently-named copies of the same
        /// mod - left behind by the rename in older versions - show up twice.
        /// </para>
        /// Order matters twice over: it is the order mods are shown in, and within an entry
        /// the order its files are applied in. Frosty applies a mod list front to back and
        /// lets later entries win. Adding a mod is an entry in this list and nothing
        /// else.
        /// </summary>
        private static readonly CatalogEntry[] modCatalog =
        {

            // TODO: these descriptions are read off the mod names - correct them to what
            // the mods actually do.
            SingleFile("Improved_Scoreboard.fbmod", "Improved Scoreboard",
                "Makes the scoreboard background black to improve readability",
                "4507eb7297053ffb38a65228158189de3260e411e603401b0c1ecb2542b2af76"),
            SingleFile("Improved-Game-Startup.fbmod", "Faster Game Startup",
                "Skips all loading screens on launch",
                "b794e7ede7410f9b898bcc3cd31f178d187ad37b6fe80a294772a554c14920e3"),
            SingleFile("Improved-Low-Health-Visibility.fbmod", "Improved Low Health Visibility",
                "Removes the red background when your health is low",
                "507dd2361913e72e53b3cda3bd27746df72397a146937e9664c2ce9a3564fe14"),
            SingleFile("Improved-Pause-Screen-Effects.fbmod", "Improved Pause Screen",
                "Removes the blurry game background while the game is paused",
                "d7353ae4aa18c92819dbb059cfce7abfef584a620353b2a3c196149b3a47e824")
        };


        private static readonly Dictionary<string, CatalogEntry> catalogByKey =
            modCatalog.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, CatalogFile> catalogFileByName =
            modCatalog.SelectMany(e => e.Files).ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Mods whose file on disk has been confirmed against the catalog. Empty until the
        /// background verification finishes, and the only thing <see cref="GetSelectedMods"/>
        /// will hand to the launcher - so an unverified file cannot reach the game even if
        /// it is on screen.
        /// </summary>
        private HashSet<string> verifiedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True until the mod files have been verified against the catalog.</summary>
        private bool modsLoading = true;
        public MainWindow()
        {
            InitializeComponent();
            TitleBar.Use(this);
            allMods = new List<ModItem>();
            visibleMods = new ObservableCollection<ModItem>();
            ModListControl.ItemsSource = visibleMods;

            candidates = new ObservableCollection<GameCandidate>();
            CandidateList.ItemsSource = candidates;

            modsDirectory = AppPaths.GetModsPath();

            PopulateFromCatalog();
            RefreshVisibleMods();
            RefreshLaunchButton();

            // The mod list is built either way; the game screen is only put in front of it,
            // so there is nothing left to construct on the way through.
            if (NeedsGameSetup())
            {
                RefreshGameSetup();
                ShowView(View.GameSelect);
            }
            else
            {
                ShowView(View.Mods);
            }

            // Started from Loaded, not here: both the cache install and the verification
            // report problems through a message box, and those need a window that has
            // actually been shown.
            Loaded += MainWindow_Loaded;
        }

        /// <summary>
        /// Whether the game folder is still unsettled: none picked yet, or one that is not a
        /// Battlefront install. The mod list has nothing it could act on without it, so the
        /// game screen comes first.
        /// </summary>
        private static bool NeedsGameSetup()
        {
            return !GameFolder.IsValidBattlefrontInstall(Properties.Settings.Default.GamePath);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            StartBackgroundVideo();

            // Left behind if the app was killed mid-session; it must not outlive the launch
            // it was written for
            DeleteLaunchMarker();

            // The first-run screen stays until the user presses CONTINUE; everything else
            // goes straight to the mod list
            if (currentView != View.GameSelect)
                EnterModsView();
        }

        #region -- Background video --

        /// <summary>
        /// Starts the loop behind the window. The file is a content file next to the exe
        /// (Assets\Video\background.mp4), so it is addressed by its absolute path. If it is
        /// missing or Windows cannot decode it, MediaFailed hides the element and the
        /// window's black background is what shows, which is what it was before.
        /// </summary>
        private void StartBackgroundVideo()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Video", "background.mp4");
            if (!File.Exists(path))
            {
                BackgroundVideo.Visibility = Visibility.Collapsed;
                return;
            }

            BackgroundVideo.Source = new Uri(path, UriKind.Absolute);
            BackgroundVideo.Play();

            // No point decoding frames nobody sees: paused while minimized (which is
            // where the window sits for the whole game session), resumed when it is back
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized)
                    BackgroundVideo.Pause();
                else
                    BackgroundVideo.Play();
            };
        }

        /// <summary>The loop: back to the first frame and on again, no gap the user has to notice.</summary>
        private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            BackgroundVideo.Position = TimeSpan.Zero;
            BackgroundVideo.Play();
        }

        private void BackgroundVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            Debug.WriteLine("Background video failed: " + e.ErrorException?.Message);
            BackgroundVideo.Visibility = Visibility.Collapsed;
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            StopGameStartWatch();
            ReleaseGameProcess();
            // A running game has long since read it; a later plain start must not
            DeleteLaunchMarker();
            base.OnClosed(e);
        }

        /// <summary>
        /// Fills the mod lists from the catalog, without reading a single mod file - this
        /// runs before the window is shown, so it has to stay free of anything slow.
        /// Checking that a file exists is a directory lookup and cheap; checking that its
        /// contents are right means hashing it, and that is what the background
        /// verification is for.
        /// </summary>
        private void PopulateFromCatalog()
        {
            var enabled = new HashSet<string>(
                (Properties.Settings.Default.EnabledMods ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            foreach (CatalogEntry entry in modCatalog)
            {
                // Every file or none: an entry that is only half on disk is not usable
                bool present = entry.Files.All(f => File.Exists(Path.Combine(modsDirectory, f.FileName)));

                // Every mod ships with the app, so one that is missing is simply not shown
                if (!present)
                    continue;

                var item = new ModItem
                {
                    Key = entry.Key,
                    Name = entry.DisplayName,
                    Description = entry.Description,
                    FileNames = entry.Files.Select(f => f.FileName).ToArray(),
                    // Optimistic until the background check says otherwise - the files being
                    // there is not yet proof that they are the right ones
                    IsAvailable = present

                };
                item.IsEnabled = present && enabled.Contains(entry.Key);

                allMods.Add(item);
            }
        }

        /// <summary>
        /// Rebuilds the list from <see cref="allMods"/>, which verification prunes to what
        /// the disk can back up. The ModItem objects are kept rather than rebuilt, so a mod
        /// switched on stays on across a refresh.
        /// </summary>
        private void RefreshVisibleMods()
        {
            visibleMods.Clear();
            foreach (ModItem mod in allMods)
                visibleMods.Add(mod);
        }

        // Buttons get their sounds from the app-wide PurpleButtonStyle; these handlers are
        // for the controls that aren't buttons
        private void PlayHoverSound(object sender, MouseEventArgs e) => Sounds.PlayHover();

        private void ModSwitch_Toggled(object sender, EventArgs e)
        {
            if (updatingSwitches)
                return;

            SaveSelection();
        }

        /// <summary>Guards against selection sweeps re-entering through their own writes.</summary>
        private bool updatingSwitches;

        private void SaveSelection()
        {
            var entries = allMods.Where(m => m.IsEnabled).Select(m => m.Key);

            Properties.Settings.Default.EnabledMods = string.Join(";", entries);
            Properties.Settings.Default.Save();
            RefreshLaunchStatus();
        }

        /// <summary>
        /// Single place that decides whether launching is available and what the button
        /// says about it. The only thing that holds it back is not yet knowing which mods
        /// are genuine - a running game is turned away on the press instead, since the
        /// button could never be more than a guess at what the game was doing.
        /// </summary>
        private void RefreshLaunchButton()
        {
            LaunchGameButton.IsEnabled = !modsLoading;
            RefreshLaunchStatus();
        }

        /// <summary>
        /// The line beside the launch button: what the launch will do, or why it cannot
        /// happen yet. Status in plain sight rather than in a tooltip on a disabled button.
        /// </summary>
        private void RefreshLaunchStatus()
        {
            if (modsLoading)
            {
                LaunchStatusText.Text = "Checking the mod files...";
                return;
            }

            int count = GetSelectedMods().Count;
            LaunchStatusText.Text = count == 0 ? "No mods selected, the game starts unmodded"
                : count == 1 ? "1 mod selected"
                : count + " mods selected";
        }

        #region -- Views --

        /// <summary>The pages this window can show. Exactly one of them is visible.</summary>
        private enum View
        {
            Mods,
            Settings,
            GameSelect
        }

        private View currentView;

        /// <summary>True while <see cref="ShowView"/> is updating the tabs itself.</summary>
        private bool syncingNav;

        /// <summary>
        /// Swaps the window over to another page. Nothing is torn down, so coming back is
        /// instant and the mod list keeps the state it already had - settings and game
        /// selection used to be windows of their own, which meant rebuilding MainWindow on
        /// the way back and re-running the whole mod verification with it.
        /// </summary>
        private void ShowView(View view)
        {
            currentView = view;
            MainView.Visibility = view == View.Mods ? Visibility.Visible : Visibility.Collapsed;
            SettingsView.Visibility = view == View.Settings ? Visibility.Visible : Visibility.Collapsed;
            GameSelectView.Visibility = view == View.GameSelect ? Visibility.Visible : Visibility.Collapsed;

            // The tabs follow the page, not the other way round: the first-run screen
            // has no tabs, and Escape or the game setup can change the page without one.
            // Setting IsChecked fires Checked, so the handlers stand down meanwhile.
            NavBar.Visibility = view == View.GameSelect ? Visibility.Collapsed : Visibility.Visible;
            syncingNav = true;
            try
            {
                NavPlay.IsChecked = view == View.Mods;
                NavSettings.IsChecked = view == View.Settings;
            }
            finally
            {
                syncingNav = false;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // Escape backs out of settings. It does nothing on the other two pages, so it
            // can neither close the app by surprise nor skip past the game screen.
            if (e.Key == Key.Escape && currentView == View.Settings)
            {
                ShowView(View.Mods);
                e.Handled = true;
            }
        }

        #endregion

        #region -- Settings view --

        private void NavPlay_Checked(object sender, RoutedEventArgs e)
        {
            if (!syncingNav)
                ShowView(View.Mods);
        }

        private void NavSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (syncingNav)
                return;

            // Read the current values on the way in rather than at construction, so the
            // page always reflects what is actually in effect
            SoundSlider.Value = Sounds.VolumePercent;
            RestoreToggle.IsOn = Properties.Settings.Default.RestoreAfterGame;
            RefreshGamePathText();

            ShowView(View.Settings);
        }

        private void RefreshGamePathText()
        {
            string path = Properties.Settings.Default.GamePath;
            GamePathText.Text = string.IsNullOrEmpty(path) ? "No game folder configured yet" : path;
        }

        private void SoundSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int previous = Sounds.VolumePercent;
            Sounds.VolumePercent = (int)e.NewValue;   // persists itself and updates the live players

            // Compared against the level actually in effect rather than trusting the event:
            // this also fires when opening the page sets the slider to the stored value, and
            // that must not click. Playing afterwards means it comes out at the new level,
            // so the click doubles as a preview - and lands on silence at 0, which is
            // exactly the right feedback for turning the sounds off.
            if (Sounds.VolumePercent != previous)
                Sounds.PlayClick();
        }

        private void RestoreToggle_Toggled(object sender, EventArgs e)
        {
            Properties.Settings.Default.RestoreAfterGame = RestoreToggle.IsOn;
            Properties.Settings.Default.Save();
        }

        private void ChangeGameButton_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = PromptForGameFolder();
            if (gamePath == null)
                return;

            SaveGamePath(gamePath);
            RefreshGamePathText();
        }

        #endregion

        #region -- Game folder view --

        /// <summary>The copies of the game on offer, as the rows on the first-run screen.</summary>
        private ObservableCollection<GameCandidate> candidates;

        /// <summary>The row CONTINUE will save; null while there is nothing to choose from.</summary>
        private GameCandidate chosenCandidate;

        /// <summary>True once mod verification has been started, which happens at most once.</summary>
        private bool verificationStarted;

        /// <summary>
        /// Looks for the game on this PC and lays the screen out for what it found: the
        /// best source first and already chosen, so the usual case is one click on CONTINUE.
        /// </summary>
        private void RefreshGameSetup()
        {
            candidates.Clear();
            chosenCandidate = null;

            foreach (GameCandidate candidate in GameFolder.FindInstalls())
                candidates.Add(candidate);

            if (candidates.Count > 0)
                Choose(candidates[0]);

            LayOutGameSetup();
        }

        private void Choose(GameCandidate candidate)
        {
            foreach (GameCandidate other in candidates)
                other.IsChosen = other == candidate;
            chosenCandidate = candidate;
        }

        /// <summary>Texts and buttons for the two states: something found, or nothing.</summary>
        private void LayOutGameSetup()
        {
            bool found = candidates.Count > 0;

            SetupHeading.Text = !found ? "Where is STAR WARS Battlefront?"
                : candidates.Count == 1 ? "We found STAR WARS Battlefront"
                : "We found " + candidates.Count + " copies of STAR WARS Battlefront";
            SetupHint.Text = found
                ? "Mods load into this copy of the game."
                : "The game was not found on this PC. Choose the folder it is installed in, the one with starwarsbattlefront.exe.";

            CandidateList.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
            ContinueButton.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
            ChangeFolderLink.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
            ChooseFolderButton.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Candidate_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is GameCandidate candidate)
            {
                Sounds.PlayClick();
                Choose(candidate);
            }
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (chosenCandidate == null)
                return;

            SaveGamePath(chosenCandidate.Path);
            EnterModsView();
        }

        /// <summary>
        /// The manual way in, for a copy the search did not find. A valid pick goes to the
        /// top of the list as the chosen row, so CONTINUE is still the one confirming step.
        /// </summary>
        private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = PromptForGameFolder();
            if (gamePath == null)
                return;

            GameCandidate existing = candidates.FirstOrDefault(c => string.Equals(c.Path, gamePath, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new GameCandidate(gamePath, "Chosen by you");
                candidates.Insert(0, existing);
            }
            Choose(existing);
            LayOutGameSetup();
        }

        /// <summary>
        /// Shows the mod list and, the first time it is reached, starts checking the mod
        /// files. Verification waits until here because it reports problems through a
        /// message box, and those must not land on top of the cache install.
        /// </summary>
        private void EnterModsView()
        {
            ShowView(View.Mods);

            if (verificationStarted)
                return;

            // The list settles in once, the first time it is shown: a short fade, no
            // movement. Skipped when Windows has animations off.
            if (SystemParameters.ClientAreaAnimation)
            {
                MainView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }

            verificationStarted = true;
            BeginVerifyMods();
        }

        /// <summary>
        /// Asks for the game's executable and hands back the folder it sits in, or null if
        /// the user backed out or picked something that is not Battlefront. Shared by the
        /// two places a folder can be chosen: this screen and the settings page.
        /// </summary>
        private string PromptForGameFolder()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Game Executable|*.exe",
                Title = "Select Star Wars Battlefront Executable"
            };
            if (dialog.ShowDialog() != true)
                return null;

            string dir = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrEmpty(dir) || !GameFolder.IsValidBattlefrontInstall(dir))
            {
                CustomMessageBox.Show(this,
                    "That folder holds no StarWarsBattlefront.exe. Pick the game's own executable.",
                    "Wrong folder");
                return null;
            }

            return dir;
        }

        private static void SaveGamePath(string gamePath)
        {
            Properties.Settings.Default.GamePath = gamePath;
            Properties.Settings.Default.Save();
        }

        #endregion

        private sealed class VerificationResult
        {
            public HashSet<string> VerifiedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string> FailedDeletes = new List<string>();
        }

        /// <summary>
        /// Confirms the mod files against the catalog on a background thread, then releases
        /// the launch button. Verifying means hashing every file, which cannot happen on the
        /// way into the window - but nothing can
        /// be launched until it has finished and said which files are genuine.
        /// </summary>
        private async void BeginVerifyMods()
        {
            string directory = modsDirectory;

            VerificationResult verification;
            try
            {
                verification = await Task.Run(() =>
                {
                    VerificationResult result = VerifyMods(directory);

                    // With the files known genuine, read what each holds so the launch button
                    // has its verdict (readable, which parts a plugin would have applied) ready
                    MemoryLoading.Warm(result.VerifiedFileNames.Select(name => Path.Combine(directory, name)));
                    return result;
                });
            }
            catch (Exception ex)
            {
                // Leave modsLoading set: with nothing verified, launching is not on offer
                CustomMessageBox.Show(this, $"Could not read the mod files. Reinstall the app if this keeps happening.\n\nTechnical details: {ex.Message}", "Error");
                return;
            }

            verifiedMods = verification.VerifiedFileNames;

            // Settle the list against what the disk actually backed up. In the normal case
            // nothing changes here and it stays exactly as it was drawn - a mod only
            // changes state if its file is genuinely not the real one.
            SettleAgainstVerification();

            modsLoading = false;
            RefreshLaunchButton();

            if (verification.FailedDeletes.Count > 0)
            {
                CustomMessageBox.Show(this, $"These files in the mods folder are not approved and could not be removed: {string.Join(", ", verification.FailedDeletes)}\n\nRun the app as administrator to clean them up.", "Unapproved files");
            }
        }

        /// <summary>
        /// Marks each mod with whether its file passed the check. One that did not is removed:
        /// every mod ships with the app, so there would be nothing the user could do about it.
        /// </summary>
        private void SettleAgainstVerification()
        {
            updatingSwitches = true;
            for (int i = allMods.Count - 1; i >= 0; i--)
            {
                ModItem item = allMods[i];
                item.IsAvailable = IsFullyVerified(item);
                if (item.IsAvailable)
                    continue;

                item.IsEnabled = false;
                allMods.RemoveAt(i);
            }
            updatingSwitches = false;

            RefreshVisibleMods();
        }

        /// <summary>
        /// True only when every file this row applies has been confirmed against the catalog.
        /// </summary>
        private bool IsFullyVerified(ModItem item) => item.FileNames.All(verifiedMods.Contains);
        /// <summary>
        /// Checks every .fbmod in the mods folder against the catalog and deletes the ones
        /// that don't belong. Runs on a background thread, so it touches nothing but its
        /// argument and the file system.
        /// </summary>
        private static VerificationResult VerifyMods(string modsDirectory)
        {
            var result = new VerificationResult();

            if (!Directory.Exists(modsDirectory))
            {
                Directory.CreateDirectory(modsDirectory);
                return result;
            }

            foreach (string file in Directory.GetFiles(modsDirectory, "*.fbmod"))
            {
                string fileName = Path.GetFileName(file);

                // Approved means the catalog expects this name and the contents hash to
                // what it expects them to be - neither on its own is enough
                if (catalogFileByName.TryGetValue(fileName, out CatalogFile entry) &&
                    string.Equals(FileHash.OfFileCached(file), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    result.VerifiedFileNames.Add(fileName);
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch
                {
                    result.FailedDeletes.Add(fileName);
                }
            }

            return result;
        }

        /// <summary>The warning text last shown, so the same selection does not raise the same dialog on every launch.</summary>
        private string lastPluginWarning;

        /// <summary>
        /// Tells the user which parts of the selection Auric will skip (resources that only a
        /// Frosty plugin could apply). Informational: the launch goes ahead after OK. Shown
        /// once per distinct set, since a player who keeps the same mods on would otherwise
        /// read the same dialog before every single game.
        /// </summary>
        private void ShowPluginWarnings(LaunchPlan plan)
        {
            if (plan.Warnings.Count == 0)
                return;

            const int maxLines = 8;
            IEnumerable<string> lines = plan.Warnings.Take(maxLines);
            if (plan.Warnings.Count > maxLines)
                lines = lines.Concat(new[] { "... and " + (plan.Warnings.Count - maxLines) + " more" });
            string text = string.Join("\n", lines);

            if (text == lastPluginWarning)
                return;
            lastPluginWarning = text;

            CustomMessageBox.Show(this,
                text + "\n\nThe game starts with the rest of the mods as usual.",
                "Some parts will not load");
        }

        /// <param name="modFileNames">The files to apply, in apply order, relative to the mods folder.</param>
        private void ApplyModsAndLaunch(string gamePath, string[] modFileNames, LaunchPlan plan)
        {
            // Modal progress window runs the launch (Auric into the game folder, the marker,
            // the game start) on a background thread. It stays down afterwards on success:
            // the game is what the user is now looking at.
            var launchWindow = new LaunchProgressWindow(gamePath, modsDirectory, modFileNames, plan);
            if (RunWindowMinimized(launchWindow))
            {
                OnGameLaunched(gamePath);
            }
            else
            {
                DeleteLaunchMarker();
            }
        }

        /// <summary>The launch marker is written by LaunchProgressWindow; this window only ends its life.</summary>
        private static void DeleteLaunchMarker() => LaunchMarker.Delete();

        /// <summary>
        /// Runs one of the progress windows with this one out of the way.
        /// <para>
        /// The dialog is deliberately not owned by this window: Windows takes an owned
        /// window down together with its owner, so it would go straight to the taskbar
        /// along with this one.
        /// </para>
        /// <para>
        /// Which is why this window goes down on the dialog's first paint rather than
        /// either side of the call. Minimizing after the dialog closed let this window
        /// paint once in between, which read as a flash of the screen just left. Doing it
        /// before the dialog opened handed the foreground to whatever app was behind this
        /// one, and Windows then refuses to let a process that is no longer in front raise
        /// a new window, so the dialog opened at the very back of the stack. Waiting until
        /// it is on screen means the dialog holds the foreground itself when this window
        /// goes down, and nothing is visible in between either.
        /// </para>
        /// Stays down when the flow worked, since being out of the way is the point of a
        /// launch; a flow that failed brings the window back, so the user can see what to
        /// do about it.
        /// </summary>
        private bool RunWindowMinimized(Window dialog)
        {
            dialog.ContentRendered += (s, e) => WindowState = WindowState.Minimized;

            bool succeeded = dialog.ShowDialog() == true;
            if (!succeeded)
            {
                WindowState = WindowState.Normal;
                Activate();
            }

            return succeeded;
        }

        // The process the app starts is not the game that runs: the exe hands itself to
        // the EA app, which starts the real one, so there is no process handle to wait on
        // when the launch returns, and the real process can take seconds to appear. It therefore
        // has to be found by looking, but only until it turns up: from that point Windows
        // reports the exit through Process.Exited, so no more looking is needed.
        //
        // That split matters for how the app feels. Waiting for the game to *appear* is
        // invisible - nobody is watching a minimized window during game startup. Waiting
        // for it to *close* is the part the user sees, and an event gets the window back
        // the instant the game quits instead of up to a poll interval later.
        private static readonly TimeSpan GameStartTimeout = TimeSpan.FromMinutes(2);

        private DispatcherTimer gameStartWatchTimer;
        private DateTime gameStartWatchBegan;
        private string watchedProcessName;
        private Process watchedGameProcess;

        /// <summary>
        /// True between telling the game to start and either finding its process or giving
        /// up on it. In that window nothing can see a running game yet, so this is what
        /// stops a second launch from slipping through.
        /// </summary>
        private bool awaitingGameStart;

        private void OnGameLaunched(string gamePath)
        {
            // The window is already minimized - RunWindowMinimized put it there while the
            // progress window was up.
            watchedProcessName = GameFolder.GetExeName(gamePath);
            gameStartWatchBegan = DateTime.UtcNow;
            awaitingGameStart = true;

            gameStartWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            gameStartWatchTimer.Tick += GameStartWatchTimer_Tick;
            gameStartWatchTimer.Start();
        }

        private void GameStartWatchTimer_Tick(object sender, EventArgs e)
        {
            Process game = FindGameProcess();
            if (game == null)
            {
                // Still on its way up - Steam and the EA app can take a while to get there
                if (DateTime.UtcNow - gameStartWatchBegan < GameStartTimeout)
                    return;

                // Never showed up. Stop looking rather than poll for the rest of the session,
                // and take the launch file with it: whatever starts next was not this launch.
                StopGameStartWatch();
                DeleteLaunchMarker();
                return;
            }

            StopGameStartWatch();
            WatchForExit(game);
        }

        private Process FindGameProcess()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(watchedProcessName);
                if (processes.Length == 0)
                    return null;

                for (int i = 1; i < processes.Length; i++)
                    processes[i].Dispose();
                return processes[0];
            }
            catch
            {
                return null;
            }
        }

        private void WatchForExit(Process game)
        {
            try
            {
                watchedGameProcess = game;
                game.EnableRaisingEvents = true;
                game.Exited += GameProcess_Exited;

                // It may already have gone in the moment between finding it and subscribing
                if (game.HasExited)
                    GameProcess_Exited(game, EventArgs.Empty);
            }
            catch
            {
                // Watching a process needs synchronise rights on it, which can be refused -
                // an elevated game against a non-elevated app, for instance. The window then
                // stays minimized, which is the same thing the setting's "off" state does.
                ReleaseGameProcess();
            }
        }

        private void GameProcess_Exited(object sender, EventArgs e)
        {
            // Raised on a pool thread, so everything below has to be handed to the UI one
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ReleaseGameProcess();
                DeleteLaunchMarker();

                if (Properties.Settings.Default.RestoreAfterGame)
                {
                    // Game exited - bring the manager back (unless the user turned that off
                    // in settings and would rather have it stay out of the way)
                    WindowState = WindowState.Normal;
                    Activate();
                }
            }));
        }

        private void StopGameStartWatch()
        {
            awaitingGameStart = false;

            if (gameStartWatchTimer == null)
                return;

            gameStartWatchTimer.Stop();
            gameStartWatchTimer.Tick -= GameStartWatchTimer_Tick;
            gameStartWatchTimer = null;
        }

        private void ReleaseGameProcess()
        {
            if (watchedGameProcess == null)
                return;

            try
            {
                watchedGameProcess.Exited -= GameProcess_Exited;
                watchedGameProcess.Dispose();
            }
            catch
            {
            }

            watchedGameProcess = null;
        }

        /// <summary>True if the game is up right now. Cheap enough to ask on a button press.</summary>
        private static bool IsGameRunning(string gamePath)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(GameFolder.GetExeName(gamePath));
                foreach (Process p in processes)
                    p.Dispose();
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRunAsAdmin() => GameFolder.IsElevated();

        private void LaunchGameButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string gamePath = Properties.Settings.Default.GamePath;
                if (string.IsNullOrEmpty(gamePath))
                {
                    CustomMessageBox.Show(this, "Set your game folder in Settings before launching.", "No game folder");
                    return;
                }

                // Asked here rather than enforced by grinding the button out: the button
                // only ever reflected what the last poll saw, and it came back by itself if
                // the game took too long to appear or the app was restarted mid-session.
                // Frosty refuses a second launch too, but only after the progress window has
                // opened and done work, and with a message of its own wording.
                if (awaitingGameStart)
                {
                    CustomMessageBox.Show(this, "The game is already starting. Wait for it to open.", "Already starting");
                    return;
                }
                if (IsGameRunning(gamePath))
                {
                    CustomMessageBox.Show(this, "The game is already running. Close it before launching again.", "Already running");
                    return;
                }

                // An empty selection is allowed and launches the game unmodded: the marker
                // is written with no mods and Auric has nothing to lay over the game's files
                List<ModItem> selectedMods = GetSelectedMods();

                if (!StillVerified(selectedMods))
                {
                    CustomMessageBox.Show(this,
                        "The mod files changed after they were checked, so the game was not started. " +
                        "Restart the app to check them again.", "Mod files changed");
                    return;
                }

                // Auric has to sit in the game folder, and only an elevated app can put it
                // there. Asked up front: a clear request beats a launch that fails halfway.
                if (!GameFolder.IsAuricInstalled(gamePath))
                {
                    if (!File.Exists(GameFolder.GetAuricSourcePath()))
                    {
                        CustomMessageBox.Show(this, GameFolder.AuricFileName + " is missing from the app folder. Reinstall the app to restore it.", "File missing");
                        return;
                    }
                    if (!IsRunAsAdmin())
                    {
                        CustomMessageBox.Show(this, "Setting up the game folder needs administrator rights. Restart the app as administrator.", "Administrator needed");
                        return;
                    }
                }

                // Plain filenames relative to the mods folder, flattened in list order and
                // then file order within each mod: a mod split across several files still
                // has to go in front to back, since Auric applies the list in order and lets
                // later entries win.
                string[] modFileNames = selectedMods
                    .SelectMany(m => m.FileNames)
                    .ToArray();

                // What Auric will make of the files: a mod it cannot read stops the launch,
                // parts only a Frosty plugin could apply are named and then skipped
                LaunchPlan plan = MemoryLoading.Plan(modFileNames.Select(name => Path.Combine(modsDirectory, name)));
                if (plan.Error != null)
                {
                    CustomMessageBox.Show(this, plan.Error + ", so the game was not started.", "Could not read a mod");
                    return;
                }
                ShowPluginWarnings(plan);

                ApplyModsAndLaunch(gamePath, modFileNames, plan);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Something went wrong before the game could start.\n\nTechnical details: {ex.Message}", "Could not launch");
            }
        }

        /// <summary>
        /// The mods to apply, in apply order. Anything not in <see cref="verifiedMods"/> is
        /// filtered out here and nowhere else, so only files confirmed against the catalog
        /// are ever handed to the game. That set is empty until verification finishes,
        /// which makes launching before then produce nothing to launch.
        /// </summary>
        private List<ModItem> GetSelectedMods()
        {
            // visibleMods is in catalog order, and Where preserves it: the apply order
            return visibleMods.Where(m => m.IsEnabled && IsFullyVerified(m)).ToList();
        }

        /// <summary>
        /// Re-checks the files about to be applied. The hash cache makes this free unless a
        /// file actually changed since it was verified, so a normal launch pays nothing -
        /// but a mod swapped out while the app sat open does not get through.
        /// </summary>
        private bool StillVerified(List<ModItem> selectedMods)
        {
            foreach (string fileName in selectedMods.SelectMany(m => m.FileNames))
            {
                string path = Path.Combine(modsDirectory, fileName);
                if (!File.Exists(path) ||
                    !string.Equals(FileHash.OfFileCached(path), catalogFileByName[fileName].Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

    }

    public class ModItem : INotifyPropertyChanged
    {
        private bool isAvailable;
        private bool isEnabled;

        /// <summary>Stable id, used to remember what was selected.</summary>
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The files this mod applies, in the order they must be applied. Usually one, but
        /// a mod that was built in several parts is still a single choice to the user.
        /// </summary>
        public string[] FileNames { get; set; }
        /// <summary>Whether the mod is selected for the next launch.</summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set { isEnabled = value; Raise(nameof(IsEnabled)); }
        }

        public string Description { get; set; }

        /// <summary>True when the file is present and has passed the catalog check.</summary>
        public bool IsAvailable
        {
            get => isAvailable;
            set { isAvailable = value; Raise(nameof(IsAvailable)); }
        }
        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
