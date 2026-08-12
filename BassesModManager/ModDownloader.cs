using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BassesModManager
{
    public sealed class DownloadResult
    {
        public bool Success { get; }
        public bool Cancelled { get; }

        /// <summary>Why it failed; empty when it didn't.</summary>
        public string Error { get; }

        private DownloadResult(bool success, bool cancelled, string error)
        {
            Success = success;
            Cancelled = cancelled;
            Error = error;
        }

        public static DownloadResult Ok() => new DownloadResult(true, false, "");
        public static DownloadResult Aborted() => new DownloadResult(false, true, "");
        public static DownloadResult Failed(string error) => new DownloadResult(false, false, error);
    }

    /// <summary>
    /// Fetches a mod file and only lets it into the mods folder if its contents hash to
    /// what the catalog expects. Downloading rather than bundling changes nothing about
    /// which mods are allowed: approval is the hash, so where the bytes came from does not
    /// matter, and a file that fails the check never reaches the mods folder at all.
    /// </summary>
    public static class ModDownloader
    {
        private const int BufferSize = 128 * 1024;

        public static async Task<DownloadResult> DownloadAsync(
            string url, string destinationPath, string expectedSha256,
            IProgress<double> progress, CancellationToken token)
        {
            // Downloaded under a temporary name and only renamed into place once verified,
            // so an interrupted or tampered download can never be mistaken for the real
            // mod - not even if the app is killed mid-transfer.
            string partPath = destinationPath + ".part";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                using (var client = new HttpClient())
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;  // the transfer can be long; cancellation is the token's job
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BassesModManager");

                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        long? total = response.Content.Headers.ContentLength;
                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
                        {
                            var buffer = new byte[BufferSize];
                            long copied = 0;
                            int read;
                            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                            {
                                await target.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                                copied += read;
                                if (total.HasValue && total.Value > 0)
                                    progress?.Report((double)copied / total.Value);
                            }
                        }
                    }
                }

                token.ThrowIfCancellationRequested();

                if (!string.Equals(FileHash.OfFile(partPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(partPath);
                    return DownloadResult.Failed("the downloaded file did not match the expected contents");
                }

                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                File.Move(partPath, destinationPath);

                return DownloadResult.Ok();
            }
            catch (OperationCanceledException)
            {
                TryDelete(partPath);
                return DownloadResult.Aborted();
            }
            catch (Exception ex)
            {
                TryDelete(partPath);
                return DownloadResult.Failed(ex.Message);
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
