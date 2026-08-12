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
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BassesModManager
{
    public partial class MainWindow : Window
    {
        /// <summary>Every catalogued mod, in catalog order, which is also apply order.</summary>
        private List<ModItem> allMods;

        /// <summary>The subset on offer in the current mode; what the list actually shows.</summary>
        private ObservableCollection<ModItem> visibleMods;

        private string modsDirectory;

        /// <summary>Which of the window's two modes a mod is offered in.</summary>
        [Flags]
        private enum ModModes
        {
            EaServers = 1,
            Auric = 2,
            Both = EaServers | Auric
        }

        /// <summary>One mod file. This is the unit of approval: a name and the contents it must have.</summary>
        private sealed class CatalogFile
        {
            public string FileName { get; set; }
            public string Sha256 { get; set; }

            /// <summary>Set for files that ship separately and are fetched on demand.</summary>
            public string DownloadUrl { get; set; }

            /// <summary>Download size, shown before the user commits to fetching it.</summary>
            public long SizeBytes { get; set; }
        }

        /// <summary>
        /// One row in the mod list, and one thing the user can pick. Usually a single file,
        /// but not always: a mod that was split across several files is still one choice.
        /// </summary>
        /// <summary>
        /// One of several interchangeable versions of the same mod, picked from a row of
        /// swatches rather than a switch. The variant with no file is the "off" choice.
        /// </summary>
        private sealed class CatalogVariant
        {
            public string Label { get; set; }
            public string ImagePath { get; set; }
            public CatalogFile File { get; set; }
        }

        private sealed class CatalogEntry
        {
            /// <summary>Stable id used to remember what was selected.</summary>
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public ModModes Modes { get; set; }

            /// <summary>Set instead of a plain on/off when the mod comes in versions.</summary>
            public CatalogVariant[] Variants { get; set; }

            /// <summary>
            /// Every file the row can apply. For a variant row that means all of them: any
            /// could end up being the chosen one, so all have to be verified. Derived from
            /// the variants on first use rather than in a static initializer, which would
            /// have made this depend on the order the fields happen to be declared in.
            /// </summary>
            public CatalogFile[] Files
            {
                get => files ?? (files = Variants.Where(v => v.File != null).Select(v => v.File).ToArray());
                set => files = value;
            }

            private CatalogFile[] files;

            public bool HasVariants => Variants != null;
            public bool CanDownload => Files.All(f => f.DownloadUrl != null);
            public long TotalSizeBytes => Files.Sum(f => f.SizeBytes);
        }

        /// <summary>
        /// Translates the pre-1.7 settings, which named a crosshair file and carried a
        /// separate scoreboard flag, into the single list of switched-on mods used since.
        /// Lives here because it needs the catalog: the crosshair is no longer identified
        /// by its file name but by which version of one row is picked.
        /// </summary>
        public static string MigrateLegacySelection(string lastModFileName, bool scoreboardEnabled)
        {
            var carried = new List<string>();

            foreach (CatalogEntry entry in modCatalog)
            {
                if (!entry.HasVariants)
                    continue;

                CatalogVariant match = entry.Variants.FirstOrDefault(v =>
                    string.Equals(v.File?.FileName, lastModFileName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    carried.Add(entry.Key + ":" + match.Label);
            }

            if (scoreboardEnabled)
                carried.Add("Improved_Scoreboard.fbmod");

            return string.Join(";", carried);
        }

        private static CatalogEntry SingleFile(string fileName, string displayName, string description,
            string sha256, ModModes modes)
        {
            return new CatalogEntry
            {
                Key = fileName,
                DisplayName = displayName,
                Description = description,
                Modes = modes,
                Files = new[] { new CatalogFile { FileName = fileName, Sha256 = sha256 } }
            };
        }

        // The Auric mods are hosted separately instead of riding along in the installer:
        // together they are over half a gigabyte, and bundling them meant every auto-update
        // re-downloaded all of it even for a code-only change.
        //
        // The tag is pinned rather than "latest", and names the app version the mod files
        // were published for. That is always meaningful: the catalog below carries each
        // file's hash, so changing a mod means changing this app too, and the two ship
        // together. It does not follow the other way round - a code-only release leaves
        // this alone rather than forcing a pointless second upload of half a gigabyte, so
        // expect this to lag the app version whenever the mods have not changed.
        private const string AuricModsBaseUrl =
            "https://github.com/TSL-Battlefront/auricMods/releases/download/v1.7/";

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
        /// lets later entries win. Adding a mod to a mode is an entry in this list and
        /// nothing else.
        /// </summary>
        private static readonly CatalogEntry[] modCatalog =
        {
            // First in the list, and therefore first to be applied. That is required: the
            // Axon set has to go in before anything else so the rest layers on top of it.
            // It is also why the list is not sorted for display - the order shown is the
            // order applied, and the two must not drift apart.
            //
            // Deliberately one entry over three files, never three choices. The split is an
            // artefact of how the mod was built in Frosty Editor, not something meaningful
            // to a player. The parts are not independent: on their own some of them change
            // weapon damage, which real EA servers would happily accept, and that would be a
            // cheat. Applied together the set also brings new weapons and other changes that
            // EA servers reject, so the whole thing only loads on Kyber's modded servers,
            // which is the point of it. Partial application must not be reachable.
            new CatalogEntry
            {
                Key = "axon",
                DisplayName = "Axon",
                Description = "Full mod set for Kyber private servers",
                Modes = ModModes.Auric,
                Files = new[]
                {
                    new CatalogFile
                    {
                        FileName = "axonmod-p1.fbmod",
                        Sha256 = "6e2fc59bc08d0aa17d72a66ea647b1a5bd47e9afe0c7377f6209611c11858096",
                        DownloadUrl = AuricModsBaseUrl + "axonmod-p1.fbmod", SizeBytes = 15298260
                    },
                    new CatalogFile
                    {
                        FileName = "axonmod-p2.fbmod",
                        Sha256 = "dcc2d8cb0d922709b7d60d43d5e68eb758e30bb01a5204eee7ecf10294aebd0a",
                        DownloadUrl = AuricModsBaseUrl + "axonmod-p2.fbmod", SizeBytes = 573409574
                    },
                    new CatalogFile
                    {
                        FileName = "axonmod-p3.fbmod",
                        Sha256 = "be464930df1a9b8ad5236a9017a1bf651e7519e5230605d6a2266e232f065e6d",
                        DownloadUrl = AuricModsBaseUrl + "axonmod-p3.fbmod", SizeBytes = 492245
                    }
                }
            },

            // One row for all three crosshairs rather than three. They are the same mod in
            // different colours, only one can be on at a time, and three rows for one
            // decision is three rows the list cannot spare as more mods arrive. It is also
            // the shape the planned combined crosshair mod wants, so nothing about the list
            // has to change when three files become one.
            new CatalogEntry
            {
                Key = "crosshair",
                DisplayName = "Crosshair - Added EE3 Dot",
                Description = "Adds a dot to the EE3 crosshair. Choose colour.",
                Modes = ModModes.Both,
                Variants = new[]
                {
                    new CatalogVariant { Label = "OFF" },
                    new CatalogVariant
                    {
                        Label = "WHITE", ImagePath = "Assets/Images/white_dot.png",
                        File = new CatalogFile { FileName = "White Dot.fbmod", Sha256 = "88bc98b8604f993e058ff848ba267b1e72530a3938037f9dc4b58d6471aa337a" }
                    },
                    new CatalogVariant
                    {
                        Label = "RED", ImagePath = "Assets/Images/red_dot.png",
                        File = new CatalogFile { FileName = "Red Dot.fbmod", Sha256 = "c8748886884ae0f3f2a372fc130bf9bb3794dc2ca908a16ad8a6d2d01d16d719" }
                    },
                    new CatalogVariant
                    {
                        Label = "GREEN", ImagePath = "Assets/Images/green_dot.png",
                        File = new CatalogFile { FileName = "Green Dot.fbmod", Sha256 = "b0152a45d8dd1cc995fdc92d7f517ce17b08a260a9f9062ff9d6ec17902a1694" }
                    }
                }
            },

            // TODO: these descriptions are read off the mod names - correct them to what
            // the mods actually do.
            SingleFile("Improved_Scoreboard.fbmod", "Improved Scoreboard",
                "Makes the scoreboard background black to improve readability",
                "4507eb7297053ffb38a65228158189de3260e411e603401b0c1ecb2542b2af76",
                ModModes.Both),
            SingleFile("Improved-Game-Startup.fbmod", "Faster Game Startup",
                "Skips all loading screens on launch",
                "b794e7ede7410f9b898bcc3cd31f178d187ad37b6fe80a294772a554c14920e3",
                ModModes.Both),
            SingleFile("Improved-Low-Health-Visibility.fbmod", "Improved Low Health Visibility",
                "Removes the red background when your health is low",
                "507dd2361913e72e53b3cda3bd27746df72397a146937e9664c2ce9a3564fe14",
                ModModes.Both),
            SingleFile("Improved-Pause-Screen-Effects.fbmod", "Improved Pause Screen",
                "Removes the blurry game background while the game is paused",
                "d7353ae4aa18c92819dbb059cfce7abfef584a620353b2a3c196149b3a47e824",
                ModModes.Both)
        };


        private static readonly Dictionary<string, CatalogEntry> catalogByKey =
            modCatalog.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, CatalogFile> catalogFileByName =
            modCatalog.SelectMany(e => e.Files).ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);

        // Injection loads whatever is at Mods\Kyber.dll straight into the game process, so
        // the file name alone must never be enough to get code in there - dropping some
        // other DLL under that name has to be rejected. DllInjector verifies this against
        // the bytes it has locked open, so the file cannot be swapped after the check.
        private const string approvedKyberHash = "6e0411823f651549e5c8051a3b8ca75058ba2f73a088c961d0c2c216144e6a07";

        /// <summary>
        /// Mods whose file on disk has been confirmed against the catalog. Empty until the
        /// background verification finishes, and the only thing <see cref="GetSelectedMods"/>
        /// will hand to the launcher - so an unverified file cannot reach the game even if
        /// it is on screen.
        /// </summary>
        private HashSet<string> verifiedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True until the mod files have been verified against the catalog.</summary>
        private bool modsLoading = true;

        /// <summary>Aborts downloads still running when the window goes away.</summary>
        private readonly CancellationTokenSource downloadCancellation = new CancellationTokenSource();

        public MainWindow()
        {
            InitializeComponent();
            allMods = new List<ModItem>();
            visibleMods = new ObservableCollection<ModItem>();
            ModListControl.ItemsSource = visibleMods;

            modsDirectory = CachePathHelper.GetModsPath();

            PopulateFromCatalog();

            // Restore the mode the app was last left in. Assigning IsOn only raises Toggled
            // when the value actually changes, so lay the window out explicitly instead of
            // relying on the event to fire.
            ModeToggle.IsOn = !Properties.Settings.Default.AuricMode;
            ApplyMode();

            // Started from Loaded, not here: verification reports problems through a
            // message box, and those need an owner window that has actually been shown.
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            BeginVerifyMods();
        }

        protected override void OnClosed(EventArgs e)
        {
            // A part-downloaded mod is cleaned up by the downloader on cancellation, so
            // closing mid-transfer leaves nothing half-written behind
            downloadCancellation.Cancel();
            StopGameStartWatch();
            ReleaseGameProcess();
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
                // Every file or none: an entry that is only half on disk is not usable, and
                // for the Auric set it must not even look like it might be
                bool present = entry.Files.All(f => File.Exists(Path.Combine(modsDirectory, f.FileName)));

                // A missing mod that can be fetched still gets a row, so there is somewhere
                // to offer the download from. A missing mod that can't be is simply not
                // shown - it ships with the app, so there is nothing the user could do.
                if (!present && !entry.CanDownload)
                    continue;

                var item = new ModItem
                {
                    Key = entry.Key,
                    Name = entry.DisplayName,
                    Description = entry.Description,
                    FileNames = entry.Files.Select(f => f.FileName).ToArray(),
                    // Optimistic until the background check says otherwise - the files being
                    // there is not yet proof that they are the right ones
                    IsAvailable = present,
                    CanDownload = entry.CanDownload,
                    StatusText = present ? "" : FormatSize(entry.TotalSizeBytes)
                };

                if (entry.HasVariants)
                {
                    // Stored as "key:variant" so the chosen version survives a restart
                    string storedVariant = enabled
                        .FirstOrDefault(s => s.StartsWith(entry.Key + ":", StringComparison.OrdinalIgnoreCase))
                        ?.Substring(entry.Key.Length + 1);

                    item.Variants = entry.Variants.Select(v => new ModVariant
                    {
                        Label = v.Label,
                        ImagePath = v.ImagePath,
                        FileName = v.File?.FileName,
                        IsSelected = present && string.Equals(v.Label, storedVariant, StringComparison.OrdinalIgnoreCase)
                    }).ToArray();

                    // Nothing stored, or the file has gone: fall back to the off swatch
                    if (!item.Variants.Any(v => v.IsSelected))
                        item.Variants[0].IsSelected = true;

                    ApplyVariantSelection(item);
                }
                else
                {
                    item.IsEnabled = present && enabled.Contains(entry.Key);
                }

                allMods.Add(item);
            }
        }

        /// <summary>Mirrors the chosen variant onto the row: its picture, and whether it counts as on.</summary>
        private static void ApplyVariantSelection(ModItem item)
        {
            ModVariant chosen = item.Variants.FirstOrDefault(v => v.IsSelected);
            item.ImagePath = chosen?.ImagePath;
            item.IsEnabled = chosen?.FileName != null;
        }

        /// <summary>
        /// Rebuilds the visible list for the active mode. The ModItem objects are shared
        /// between modes rather than rebuilt, so a mod switched on in one mode is still on
        /// when it appears in the other - the mode decides what is on offer, not what is
        /// selected.
        /// </summary>
        private void RefreshVisibleMods()
        {
            ModModes mode = IsAuricMode ? ModModes.Auric : ModModes.EaServers;

            visibleMods.Clear();
            foreach (ModItem mod in allMods)
            {
                if ((catalogByKey[mod.Key].Modes & mode) != 0)
                    visibleMods.Add(mod);
            }
        }

        /// <summary>Left half of the switch is NORMAL, right half is AURIC.</summary>
        private bool IsAuricMode => !ModeToggle.IsOn;

        // Buttons get their sounds from the app-wide PurpleButtonStyle; these handlers are
        // for the controls that aren't buttons
        private void PlayHoverSound(object sender, MouseEventArgs e) => Sounds.PlayHover();

        private void ModSwitch_Toggled(object sender, EventArgs e)
        {
            if (updatingSwitches)
                return;

            SaveSelection();
        }

        /// <summary>
        /// A swatch on a variant row was clicked. Picking one deselects the rest, which is
        /// the whole point of variants: they are versions of one mod, not separate mods.
        /// </summary>
        private void ModVariant_Click(object sender, RoutedEventArgs e)
        {
            if (!(((FrameworkElement)sender).DataContext is ModVariant chosen))
                return;

            ModItem item = allMods.FirstOrDefault(m => m.HasVariants && m.Variants.Contains(chosen));
            if (item == null || !item.IsAvailable)
                return;

            updatingSwitches = true;
            foreach (ModVariant variant in item.Variants)
                variant.IsSelected = variant == chosen;
            ApplyVariantSelection(item);
            updatingSwitches = false;

            Sounds.PlayClick();
            SaveSelection();
        }

        /// <summary>Guards against selection sweeps re-entering through their own writes.</summary>
        private bool updatingSwitches;

        private void SaveSelection()
        {
            // Variant rows record which version is on rather than just that they are
            var entries = allMods
                .Where(m => m.IsEnabled)
                .Select(m => m.HasVariants
                    ? m.Key + ":" + m.Variants.First(v => v.IsSelected).Label
                    : m.Key);

            Properties.Settings.Default.EnabledMods = string.Join(";", entries);
            Properties.Settings.Default.Save();
        }

        private void ModeToggle_Toggled(object sender, EventArgs e)
        {
            Properties.Settings.Default.AuricMode = IsAuricMode;
            Properties.Settings.Default.Save();
            ApplyMode();
        }

        /// <summary>
        /// Swaps the list over to the active mode's mods and reshapes the action row.
        /// Selections survive the switch: the hidden mode's mods keep their state, and
        /// only what is visible can reach a launch.
        /// </summary>
        private void ApplyMode()
        {
            bool auric = IsAuricMode;
            RefreshVisibleMods();

            // Auric only means anything in its own mode, so outside it the button goes away
            // and its columns collapse - otherwise LAUNCH GAME would keep sitting at 70%
            // of the row with empty space beside it.
            InjectKyberButton.Visibility = auric ? Visibility.Visible : Visibility.Collapsed;
            ActionGapColumn.Width = auric ? new GridLength(12) : new GridLength(0);
            InjectKyberColumn.Width = auric ? new GridLength(3, GridUnitType.Star) : new GridLength(0);

            RefreshLaunchButton();
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
            LaunchGameButton.ToolTip =
                modsLoading ? "Checking the mod files..." :
                IsAuricMode ? "Select at least one Auric mod before launching the game"
                            : "Select a mod before launching the game";
        }

        /// <summary>
        /// Fetches every file a mod entry needs, and only counts the mod as available once
        /// all of them have arrived. Each file goes through exactly the same approval as a
        /// bundled one - the catalog's hash, checked before it is allowed into the mods
        /// folder - so a download can no more introduce an unapproved mod than the
        /// installer can.
        /// </summary>
        private async void DownloadMod_Click(object sender, RoutedEventArgs e)
        {
            if (!(((Button)sender).DataContext is ModItem item))
                return;
            if (item.IsDownloading || !catalogByKey.TryGetValue(item.Key, out CatalogEntry entry))
                return;

            item.IsDownloading = true;
            item.StatusText = "Starting...";

            // Only what is actually missing: an earlier attempt may have got part of the way
            List<CatalogFile> pending = entry.Files.Where(f => !verifiedMods.Contains(f.FileName)).ToList();
            long totalBytes = Math.Max(1, pending.Sum(f => f.SizeBytes));
            long doneBytes = 0;
            DownloadResult result = DownloadResult.Ok();

            foreach (CatalogFile file in pending)
            {
                // Captured per file so the reported percentage covers the whole set rather
                // than restarting at zero for each part
                long completedBefore = doneBytes;
                long fileBytes = file.SizeBytes;
                var progress = new Progress<double>(fraction =>
                    item.StatusText = "Downloading... " +
                        ((completedBefore + fraction * fileBytes) * 100d / totalBytes).ToString("0") + "%");

                result = await ModDownloader.DownloadAsync(
                    file.DownloadUrl,
                    Path.Combine(modsDirectory, file.FileName),
                    file.Sha256,
                    progress,
                    downloadCancellation.Token);

                if (!result.Success)
                    break;

                verifiedMods.Add(file.FileName);
                doneBytes += fileBytes;
            }

            item.IsDownloading = false;

            if (result.Success)
            {
                item.IsAvailable = true;
                item.StatusText = "";
                // Asking for a mod is asking to use it - switch it on rather than making
                // the user go back and do it by hand
                item.IsEnabled = true;
                SaveSelection();
                RefreshLaunchButton();
                return;
            }

            // Whatever did arrive is kept and verified, so retrying only fetches the rest
            item.StatusText = FormatSize(entry.Files.Where(f => !verifiedMods.Contains(f.FileName)).Sum(f => f.SizeBytes));
            if (result.Cancelled)
                return;

            CustomMessageBox.Show(this,
                $"{item.Name} could not be downloaded.\n\n" +
                "Check your internet connection and try again - anything already downloaded is kept.\n\n" +
                $"Technical details: {result.Error}", "Download failed");
        }

        #region -- Settings view --

        /// <summary>
        /// Swaps the window over to the settings page. Nothing is torn down, so coming
        /// back is instant and the mod list keeps the state it already had - the settings
        /// page used to be its own window, which meant rebuilding MainWindow on the way
        /// back and re-running the whole mod verification with it.
        /// </summary>
        private void ShowSettingsView(bool show)
        {
            MainView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            SettingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Read the current values on the way in rather than at construction, so the
            // page always reflects what is actually in effect
            SoundSlider.Value = Sounds.VolumePercent;
            RestoreToggle.IsOn = Properties.Settings.Default.RestoreAfterGame;
            RefreshGamePathText();

            ShowSettingsView(true);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => ShowSettingsView(false);

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // Escape backs out of settings; on the mod list it does nothing, so it can
            // never close the app by surprise
            if (e.Key == Key.Escape && SettingsView.Visibility == Visibility.Visible)
            {
                ShowSettingsView(false);
                e.Handled = true;
            }
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
            var dialog = new OpenFileDialog
            {
                Filter = "Game Executable|*.exe",
                Title = "Select Star Wars Battlefront Executable"
            };
            if (dialog.ShowDialog() != true)
                return;

            string dir = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrEmpty(dir) || !FrostyRuntime.IsValidBattlefrontInstall(dir))
            {
                CustomMessageBox.Show(this,
                    "This doesn't look like a Star Wars Battlefront installation. Make sure you selected the folder containing StarWarsBattlefront.exe.",
                    "Wrong game");
                return;
            }

            Properties.Settings.Default.GamePath = dir;
            Properties.Settings.Default.Save();
            RefreshGamePathText();
        }

        private void OpenModDataButton_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = Properties.Settings.Default.GamePath;
            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                CustomMessageBox.Show(this, "Select your game folder first before opening the mod data folder.", "No game selected");
                return;
            }

            string modDataPath = Path.Combine(gamePath, "ModData");
            if (!Directory.Exists(modDataPath))
            {
                CustomMessageBox.Show(this, "No mod data yet - launch the game with a crosshair selected at least once first.", "Nothing to show");
                return;
            }

            try
            {
                Process.Start("explorer.exe", modDataPath);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Could not open the ModData folder.\n\nTechnical details: {ex.Message}", "Error");
            }
        }

        #endregion

        private sealed class VerificationResult
        {
            public HashSet<string> VerifiedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string> FailedDeletes = new List<string>();
        }

        /// <summary>
        /// Confirms the mod files against the catalog on a background thread, then releases
        /// the launch button. Verifying means hashing, and the Auric set alone is well over
        /// half a gigabyte, so it cannot happen on the way into the window - but nothing can
        /// be launched until it has finished and said which files are genuine.
        /// </summary>
        private async void BeginVerifyMods()
        {
            string directory = modsDirectory;

            VerificationResult verification;
            try
            {
                verification = await Task.Run(() => VerifyMods(directory));
            }
            catch (Exception ex)
            {
                // Leave modsLoading set: with nothing verified, launching is not on offer
                CustomMessageBox.Show(this, $"Could not read the mod files. Try reinstalling the app if this keeps happening.\n\nTechnical details: {ex.Message}", "Error");
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
                CustomMessageBox.Show(this, $"Some files in the Mods folder are not approved mods and could not be removed: {string.Join(", ", verification.FailedDeletes)}.\n\nClose the app, right-click its icon and choose 'Run as administrator' to let it clean them up.", "Warning");
            }
        }

        /// <summary>
        /// Marks each mod with whether its file passed the check. A mod that failed but can
        /// be fetched stays on screen as an offer to download it - that covers both "not
        /// downloaded yet" and "the copy on disk is damaged". One that can't be fetched is
        /// removed, since there would be nothing to do about it.
        /// </summary>
        private void SettleAgainstVerification()
        {
            updatingSwitches = true;
            for (int i = allMods.Count - 1; i >= 0; i--)
            {
                ModItem item = allMods[i];

                if (item.HasVariants)
                    SettleVariants(item);

                item.IsAvailable = HasAnythingVerified(item);
                if (item.IsAvailable)
                {
                    item.StatusText = "";
                    continue;
                }

                item.IsEnabled = false;
                if (!item.CanDownload)
                {
                    allMods.RemoveAt(i);
                    continue;
                }

                // Only what is still outstanding, so a half-finished set shows what is left
                item.StatusText = FormatSize(catalogByKey[item.Key].Files
                    .Where(f => !verifiedMods.Contains(f.FileName))
                    .Sum(f => f.SizeBytes));
            }
            updatingSwitches = false;

            RefreshVisibleMods();
        }

        /// <summary>
        /// Grades each version of a variant row on its own and, if the chosen one turns out
        /// not to be genuine, drops the row back to off rather than leaving a selection
        /// that cannot be launched.
        /// </summary>
        private void SettleVariants(ModItem item)
        {
            foreach (ModVariant variant in item.Variants)
                variant.IsAvailable = variant.FileName == null || verifiedMods.Contains(variant.FileName);

            ModVariant chosen = item.Variants.FirstOrDefault(v => v.IsSelected);
            if (chosen == null || !chosen.IsAvailable)
            {
                foreach (ModVariant variant in item.Variants)
                    variant.IsSelected = false;
                item.Variants[0].IsSelected = true;
            }

            ApplyVariantSelection(item);
        }

        /// <summary>
        /// True only when everything this row would actually apply has been confirmed.
        /// Nothing selected means nothing to confirm, which is why an "off" variant row and
        /// an empty launch both pass.
        /// </summary>
        private bool IsFullyVerified(ModItem item) => item.ActiveFileNames.All(verifiedMods.Contains);

        /// <summary>
        /// True when the row has anything usable at all. For the Axon set that means every
        /// part, since it is only safe as a complete set; for a variant row it means at
        /// least one version came through.
        /// </summary>
        private bool HasAnythingVerified(ModItem item)
        {
            return item.HasVariants
                ? item.Variants.Any(v => v.FileName != null && verifiedMods.Contains(v.FileName))
                : item.FileNames.All(verifiedMods.Contains);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / 1024d / 1024 / 1024).ToString("0.0") + " GB";
            if (bytes >= 1024L * 1024)
                return (bytes / 1024d / 1024).ToString("0") + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

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

        private void ApplyModsAndLaunch(string gamePath, List<ModItem> selectedMods, string modPackName)
        {
            // Pass plain filenames with the mods folder as the root, like FrostyModManager
            // does. mods.json stores these strings verbatim and compares them to decide
            // whether the ModData folder is still valid, so absolute paths would tie it to
            // one install location and force a full rebuild whenever that location differs.
            // Flattened in list order, then file order within each mod: a mod split across
            // several files still has to go in front to back, since Frosty applies the list
            // in order and lets later entries win.
            var modFileNames = selectedMods
                .SelectMany(m => m.ActiveFileNames)
                .ToArray();

            // Modal progress window runs the whole flow (Frosty init, mod patching, game
            // launch) on a background thread and shows live status/progress
            var launchWindow = new LaunchProgressWindow(gamePath, modsDirectory, modFileNames, modPackName) { Owner = this };
            if (launchWindow.ShowDialog() == true)
            {
                OnGameLaunched(gamePath);
            }
        }

        // The app never starts the game itself - Frosty does, through Steam or the game's
        // own exe - so there is no process handle to wait on when the launch returns, and
        // with a launcher in the way the process can take seconds to appear. It therefore
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
            // New game process, so nothing is injected into it yet
            InjectKyberButton.Content = "INJECT AURIC";
            WindowState = WindowState.Minimized;

            watchedProcessName = FrostyRuntime.GetProfileKey(gamePath);
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

                // Never showed up. Stop looking rather than poll for the rest of the session.
                StopGameStartWatch();
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
                Process[] processes = Process.GetProcessesByName(FrostyRuntime.GetProfileKey(gamePath));
                foreach (Process p in processes)
                    p.Dispose();
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRunAsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private string GetModPackNameForSelection(List<ModItem> selectedMods, string gamePath)
        {
            // Create a unique hash based on sorted file names for selected combination
            var modNames = selectedMods.SelectMany(m => m.FileNames)
                                      .OrderBy(n => n)
                                      .ToArray();
            string comboString = string.Join("|", modNames);
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(comboString));
                string hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                string modDataPath = System.IO.Path.Combine(gamePath, "ModData");
                // Check if mod pack folder exists
                if (System.IO.Directory.Exists(modDataPath))
                {
                    var existing = System.IO.Directory.GetDirectories(modDataPath, "ModPack_*_" + hashString);
                    if (existing.Length > 0)
                    {
                        // Use existing folder
                        return System.IO.Path.GetFileName(existing[0]);
                    }
                }

                if (!IsRunAsAdmin())
                {
                    CustomMessageBox.Show(this, "First time using this mod combination - the app needs administrator rights to set it up.\n\nClose the app, right-click its icon and choose 'Run as administrator', then try again. This is only needed once per mod combination.", "Administrator needed");
                    return null;
                }
                else
                {
                    return $"ModPack_{DateTime.Now:yyyyMMdd_HHmmss}_{hashString}";
                }
            }
        }

        private void LaunchGameButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string gamePath = Properties.Settings.Default.GamePath;
                if (string.IsNullOrEmpty(gamePath))
                {
                    CustomMessageBox.Show(this, "Select your game folder before launching. Open Settings and set your game folder first.", "Game not selected");
                    return;
                }

                // Asked here rather than enforced by grinding the button out: the button
                // only ever reflected what the last poll saw, and it came back by itself if
                // the game took too long to appear or the app was restarted mid-session.
                // Frosty refuses a second launch too, but only after the progress window has
                // opened and done work, and with a message of its own wording.
                if (awaitingGameStart)
                {
                    CustomMessageBox.Show(this, "The game is already starting. Wait for it to open before launching again.", "Game already starting");
                    return;
                }
                if (IsGameRunning(gamePath))
                {
                    CustomMessageBox.Show(this, "The game is already running. Close it before launching again.", "Game already running");
                    return;
                }

                // An empty selection is allowed and launches the game unmodded, the same as
                // Frosty Mod Manager does. It still goes through the ModData machinery, so
                // the game runs from a clean patched copy rather than the original files.
                List<ModItem> selectedMods = GetSelectedMods();

                if (!StillVerified(selectedMods))
                {
                    CustomMessageBox.Show(this,
                        "The mod files have changed since they were checked, so the game was not started.\n\n" +
                        "Close and reopen the app to have them checked again.", "Mod files changed");
                    return;
                }

                // Cache is created in CacheInstallWindow before MainWindow is shown
                // Find or create the correct ModPack folder
                string modPackName = GetModPackNameForSelection(selectedMods, gamePath);
                if (modPackName == null)
                {
                    return;
                }

                // First time with this mod combination: a permission/script window will appear
                string modPackPath = Path.Combine(gamePath, "ModData", modPackName);
                if (!Directory.Exists(modPackPath))
                {
                    CustomMessageBox.Show(this, "First time using this mod combination: a black command window will pop up for a moment while everything is set up. This is normal - just let it finish.", "First-time setup", MessageBoxButton.OK);
                }

                ApplyModsAndLaunch(gamePath, selectedMods, modPackName);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(this, $"Something went wrong before the game could start. Try closing the app and reopening it as administrator (right-click the icon, choose 'Run as administrator').\n\nTechnical details: {ex.Message}", "Error");
            }
        }

        /// <summary>
        /// The mods to apply, in apply order. Two things are filtered out here and this is
        /// the only place either can be: the inactive mode's mods, so a selection made on
        /// the other side cannot leak into the launch, and anything not in
        /// <see cref="verifiedMods"/>, so only files confirmed against the catalog are ever
        /// handed to the game. That set is empty until verification finishes, which makes
        /// launching before then produce nothing to launch.
        /// </summary>
        private List<ModItem> GetSelectedMods()
        {
            // visibleMods is the active mode's mods in catalog order, and Where preserves
            // it - which is what puts the Axon set ahead of everything else in Auric mode
            return visibleMods.Where(m => m.IsEnabled && IsFullyVerified(m)).ToList();
        }

        /// <summary>
        /// Re-checks the files about to be applied. The hash cache makes this free unless a
        /// file actually changed since it was verified, so a normal launch pays nothing -
        /// but a mod swapped out while the app sat open does not get through.
        /// </summary>
        private bool StillVerified(List<ModItem> selectedMods)
        {
            foreach (string fileName in selectedMods.SelectMany(m => m.ActiveFileNames))
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

        private void InjectKyberButton_Click(object sender, RoutedEventArgs e)
        {
            string gamePath = Properties.Settings.Default.GamePath;
            if (string.IsNullOrEmpty(gamePath))
            {
                CustomMessageBox.Show(this, "Select your game folder in Settings first.", "Game not selected");
                return;
            }

            // Alongside the exe rather than in the mods folder: it is a fixed part of the
            // app, not something the user manages, and the program folder needs
            // administrator rights to write to - a better place for a DLL that gets loaded
            // into the game than the mods folder the app itself can write.
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kyber.dll");
            InjectionResult result = DllInjector.Inject(FrostyRuntime.GetProfileKey(gamePath), dllPath, approvedKyberHash);

            switch (result.Status)
            {
                case InjectionStatus.Success:
                    // No dialog on success - it would only land in front of a user who is
                    // about to alt-tab into the game. The button label carries the result
                    // instead, and OnGameLaunched puts it back for the next session.
                    InjectKyberButton.Content = "AURIC INJECTED";
                    break;
                case InjectionStatus.AlreadyLoaded:
                    CustomMessageBox.Show(this, "Auric is already loaded in the running game - no need to inject it again.", "Already injected");
                    break;
                case InjectionStatus.GameNotRunning:
                    CustomMessageBox.Show(this, "The game isn't running yet. Press LAUNCH GAME, wait until the game is up, then inject Auric.", "Game not running");
                    break;
                // The two file cases name Kyber.dll on purpose - that is the actual file on
                // disk the user would have to go and look at
                case InjectionStatus.DllNotFound:
                    CustomMessageBox.Show(this, $"Kyber.dll is missing from the app folder ({AppDomain.CurrentDomain.BaseDirectory}). Reinstall the app if this keeps happening.", "File missing");
                    break;
                case InjectionStatus.DllNotApproved:
                    CustomMessageBox.Show(this,
                        "The Kyber.dll in the app folder is not the approved one, so it was not loaded into the game.\n\n" +
                        "Reinstall the app to restore the original file.", "File not approved");
                    break;
                default:
                    CustomMessageBox.Show(this,
                        "Auric could not be loaded into the game.\n\n" +
                        "Try closing the app and reopening it as administrator (right-click the icon, choose 'Run as administrator').\n\n" +
                        $"Technical details: {result.Detail}", "Could not inject Auric");
                    break;
            }
        }
    }

    /// <summary>One interchangeable version of a mod, shown as a swatch on its row.</summary>
    public class ModVariant : INotifyPropertyChanged
    {
        private bool isSelected;
        private bool isAvailable = true;

        public string Label { get; set; }
        public string ImagePath { get; set; }

        /// <summary>Null for the "off" swatch, which applies nothing.</summary>
        public string FileName { get; set; }

        public bool HasImage => ImagePath != null;

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        /// <summary>
        /// Judged per variant, not per row: one bad crosshair file should not take the
        /// working colours down with it.
        /// </summary>
        public bool IsAvailable
        {
            get => isAvailable;
            set
            {
                isAvailable = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAvailable)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class ModItem : INotifyPropertyChanged
    {
        private bool isAvailable;
        private bool isDownloading;
        private bool isEnabled;
        private string statusText = "";
        private string imagePath;

        /// <summary>Stable id, used to remember what was selected.</summary>
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The files this mod applies, in the order they must be applied. Usually one, but
        /// a mod that was built in several parts is still a single choice to the user.
        /// </summary>
        public string[] FileNames { get; set; }

        /// <summary>
        /// The interchangeable versions of this mod, if it has any. A variant row is picked
        /// from swatches instead of switched on and off, and its "off" swatch is what makes
        /// <see cref="IsEnabled"/> false.
        /// </summary>
        public ModVariant[] Variants { get; set; }

        public bool HasVariants => Variants != null;

        /// <summary>
        /// What a launch would actually apply. For a variant row that is whichever version
        /// is picked, and nothing at all when the "off" swatch is.
        /// </summary>
        public string[] ActiveFileNames
        {
            get
            {
                if (!HasVariants)
                    return FileNames;

                ModVariant chosen = Variants.FirstOrDefault(v => v.IsSelected);
                return chosen?.FileName == null ? new string[0] : new[] { chosen.FileName };
            }
        }

        public string ImagePath
        {
            get => imagePath;
            set { imagePath = value; Raise(nameof(ImagePath)); Raise(nameof(HasCustomIcon)); }
        }

        /// <summary>Whether the mod is selected for the next launch.</summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set { isEnabled = value; Raise(nameof(IsEnabled)); }
        }

        public string Description { get; set; }

        /// <summary>False for mods with no artwork of their own, which fall back to the app icon.</summary>
        public bool HasCustomIcon => ImagePath != null;

        /// <summary>True when the file is present and has passed the catalog check.</summary>
        public bool IsAvailable
        {
            get => isAvailable;
            set { isAvailable = value; Raise(nameof(IsAvailable)); }
        }

        /// <summary>True while this mod is being fetched, so the button can step aside.</summary>
        public bool IsDownloading
        {
            get => isDownloading;
            set { isDownloading = value; Raise(nameof(IsDownloading)); }
        }

        /// <summary>Size when missing, progress while downloading, reason when it failed.</summary>
        public string StatusText
        {
            get => statusText;
            set { statusText = value; Raise(nameof(StatusText)); }
        }

        /// <summary>True for mods the app knows how to fetch, so a missing file is offerable.</summary>
        public bool CanDownload { get; set; }

        /// <summary>Nothing to offer while it is already here or already on its way.</summary>
        public bool ShowDownloadButton => CanDownload && !IsAvailable && !IsDownloading;

        /// <summary>The row's control, whichever kind it is, replaces the download offer.</summary>
        public bool ShowSwitch => !ShowDownloadButton && !HasVariants;

        public bool ShowVariants => !ShowDownloadButton && HasVariants;

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            if (name == nameof(IsAvailable) || name == nameof(IsDownloading))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDownloadButton)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowSwitch)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowVariants)));
            }
        }
    }
}
