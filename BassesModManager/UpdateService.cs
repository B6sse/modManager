using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace BassesModManager
{
    /// <summary>
    /// Discord-style background updater (Spor A in Plans/AUTO_UPDATE_PLAN.md):
    /// quietly checks GitHub Releases at startup, downloads and checksum-verifies the
    /// installer without disturbing the user, then raises StateChanged so the
    /// UpdateNotificationBar can offer a one-click install whenever it suits the user.
    /// </summary>
    public static class UpdateService
    {
        private const string ReleaseApiUrl = "https://api.github.com/repos/TSL-Battlefront/modManager/releases/latest";
        private const string SilentInstallArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS /RESTARTAPPLICATIONS";

        public static bool IsDownloading { get; private set; }
        public static bool IsUpdateReady { get; private set; }
        public static string AvailableVersionText { get; private set; }
        public static bool IsBarVisible => (IsDownloading || IsUpdateReady) && !dismissed;

        /// <summary>Raised on the UI thread whenever bar visibility should be re-evaluated.</summary>
        public static event EventHandler StateChanged;

        private static string installerPath;
        private static bool dismissed;

        public static async Task CheckAndPrepareAsync()
        {
            // True once phase 1 (an update was found and download started) has been
            // announced to the UI - used in `finally` to know whether the bar needs to be
            // told to stop showing "downloading" if we never reach the ready state.
            bool announcedDownloading = false;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BassesModManager");

                    string json = await client.GetStringAsync(ReleaseApiUrl).ConfigureAwait(false);
                    JObject release = JObject.Parse(json);

                    Version latest = ParseVersion((string)release["tag_name"]);
                    Version current = Assembly.GetExecutingAssembly().GetName().Version;
                    if (latest == null || latest <= current)
                        return;

                    JArray assets = release["assets"] as JArray ?? new JArray();
                    JToken exeAsset = assets.FirstOrDefault(a =>
                    {
                        string n = (string)a["name"] ?? "";
                        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                               n.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (exeAsset == null)
                        return;

                    string exeName = Path.GetFileName((string)exeAsset["name"]);
                    JToken shaAsset = assets.FirstOrDefault(a =>
                        string.Equals((string)a["name"], exeName + ".sha256", StringComparison.OrdinalIgnoreCase));

                    // The installer runs elevated, so never accept a download we can't verify.
                    // Releases published without a .sha256 asset are simply ignored.
                    if (shaAsset == null)
                        return;

                    // Phase 1: a verifiable update exists - tell the UI now, before the
                    // (possibly slow) download even starts, instead of only once it's
                    // fully downloaded and verified.
                    AvailableVersionText = $"v{latest.Major}.{latest.Minor}";
                    announcedDownloading = true;
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        IsDownloading = true;
                        StateChanged?.Invoke(null, EventArgs.Empty);
                    });

                    string expectedHash = (await client.GetStringAsync((string)shaAsset["browser_download_url"]).ConfigureAwait(false))
                        .Trim()
                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];

                    string exePath = Path.Combine(GetUpdateFolder(), exeName);
                    using (var response = await client.GetAsync((string)exeAsset["browser_download_url"],
                               HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var target = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            await response.Content.CopyToAsync(target).ConfigureAwait(false);
                    }

                    if (!string.Equals(ComputeSha256(exePath), expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(exePath);
                        return;
                    }

                    installerPath = exePath;
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        IsDownloading = false;
                        IsUpdateReady = true;
                        StateChanged?.Invoke(null, EventArgs.Empty);
                    });
                }
            }
            catch
            {
                // Updates are best-effort; never disturb the user when the check fails
            }
            finally
            {
                // Reached here without ever getting to the ready state (checksum
                // mismatch, network failure, timeout...) - if phase 1 was already
                // announced, stop showing "downloading" instead of leaving the bar
                // stuck mid-progress forever.
                if (announcedDownloading && !IsUpdateReady)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        IsDownloading = false;
                        StateChanged?.Invoke(null, EventArgs.Empty);
                    });
                }
            }
        }

        public static void ApplyUpdateAndRestart()
        {
            if (installerPath == null || !File.Exists(installerPath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = SilentInstallArgs,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Most likely the user declined the UAC prompt - keep running on the current version
                return;
            }

            Application.Current.Shutdown();
        }

        public static void Dismiss()
        {
            dismissed = true;
            StateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            tag = tag.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(tag, out Version v))
                return null;

            // Normalize to four components so "1.3" compares correctly against "1.2.0.0"
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        }

        private static string GetUpdateFolder()
        {
            // Prefer the shared app folder; fall back to per-user temp if it isn't
            // writable (it may have been created by an elevated instance)
            string[] candidates =
            {
                Path.Combine(CachePathHelper.GetCacheBasePath(), "Updates"),
                Path.Combine(Path.GetTempPath(), "BassesModManager", "Updates")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    string probe = Path.Combine(candidate, ".probe");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);

                    foreach (string old in Directory.EnumerateFiles(candidate))
                        TryDelete(old);

                    return candidate;
                }
                catch
                {
                }
            }

            throw new IOException("No writable folder available for downloading updates");
        }

        private static string ComputeSha256(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
