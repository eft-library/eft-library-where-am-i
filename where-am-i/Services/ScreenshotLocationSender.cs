using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace where_am_i.Services
{
    public enum ScreenshotOutcome { Ignored, FileNotReady, Success, Failed, Rejected, Canceled, Superseded }
    public enum ScreenshotStage { WaitingForFile, WaitingToSend, Sending, Retrying }
    public sealed record ScreenshotProgress(long Revision, string FileName, ScreenshotStage Stage, int Attempt = 0);
    public sealed record ScreenshotSendResult(ScreenshotOutcome Outcome, long Revision, string FileName,
        int Attempts = 0, HttpStatusCode? StatusCode = null);

    public sealed class ScreenshotLocationSender
    {
        public const string Endpoint = "/api/where-am-i/send-location";
        private static readonly Regex FileNamePattern = new(
            @"_-?\d+(?:\.\d+)?,\s*-?\d+(?:\.\d+)?,\s*-?\d+(?:\.\d+)?_.*\.(?:png|jpe?g)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
        private readonly LatestJsonSender sender;
        private readonly string email;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly TimeProvider clock;
        private readonly ScreenshotFileActivity fileActivity;
        private readonly object dedupLock = new();
        private readonly Dictionary<string, DateTimeOffset> recentPaths = new(StringComparer.OrdinalIgnoreCase);
        public event EventHandler<ScreenshotProgress>? ProgressChanged;

        public ScreenshotLocationSender(HttpClient httpClient, string email, ScreenshotFileActivity? activity = null)
            : this(httpClient, email, Task.Delay, activity: activity) { }

        internal ScreenshotLocationSender(HttpClient httpClient, string email,
            Func<TimeSpan, CancellationToken, Task> delay, TimeProvider? clock = null, ScreenshotFileActivity? activity = null)
        {
            sender = new(httpClient, Endpoint, delay);
            this.email = email;
            this.delay = delay;
            this.clock = clock ?? TimeProvider.System;
            fileActivity = activity ?? new ScreenshotFileActivity();
        }

        public bool IsCurrent(long revision) => sender.IsCurrent(revision);
        public void InvalidatePending() => sender.ReserveLatest();

        public async Task<ScreenshotSendResult> ProcessFileAsync(string path, CancellationToken token = default)
        {
            var fileName = Path.GetFileName(path);
            long revision = 0;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!IsScreenshot(fileName)) return new(ScreenshotOutcome.Ignored, 0, fileName);
                if (string.IsNullOrWhiteSpace(email)) return new(ScreenshotOutcome.Rejected, 0, fileName);
                using var fileLease = fileActivity.BeginTransfer(path);
                if (fileLease is null) return new(ScreenshotOutcome.FileNotReady, 0, fileName);
                lock (dedupLock)
                {
                    var now = clock.GetUtcNow();
                    if (recentPaths.TryGetValue(path, out var last) && now - last < TimeSpan.FromSeconds(3))
                        return new(ScreenshotOutcome.Ignored, 0, fileName);
                    foreach (var old in recentPaths.Where(p => now - p.Value > TimeSpan.FromMinutes(1)).Select(p => p.Key).ToArray())
                        recentPaths.Remove(old);
                    recentPaths[path] = now;
                    // Reserve at detection, not after readiness: an older slow file cannot overtake a newer one.
                    revision = sender.ReserveLatest();
                }
                ProgressChanged?.Invoke(this, new(revision, fileName, ScreenshotStage.WaitingForFile));
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!sender.IsCurrent(revision)) return new(ScreenshotOutcome.Superseded, revision, fileName);
                    var ready = false;
                    try
                    {
                        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        ready = file.Length > 0;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    if (ready)
                    {
                        ProgressChanged?.Invoke(this, new(revision, fileName, ScreenshotStage.WaitingToSend));
                        var result = await sender.PostLatestAsync(new { email, location = fileName }, revision, token,
                            n => ProgressChanged?.Invoke(this, new(revision, fileName,
                                n == 1 ? ScreenshotStage.Sending : ScreenshotStage.Retrying, n)));
                        var outcome = result.Outcome switch
                        {
                            SendOutcome.Success => ScreenshotOutcome.Success,
                            SendOutcome.Rejected => ScreenshotOutcome.Rejected,
                            SendOutcome.Canceled => ScreenshotOutcome.Canceled,
                            SendOutcome.Superseded => ScreenshotOutcome.Superseded,
                            _ => ScreenshotOutcome.Failed
                        };
                        return new(outcome, revision, fileName, result.Attempts, result.StatusCode);
                    }
                    if (attempt < 19) await delay(TimeSpan.FromMilliseconds(200), token);
                }
                return new(ScreenshotOutcome.FileNotReady, revision, fileName);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new(ScreenshotOutcome.Canceled, revision, fileName);
            }
        }

        public static bool IsScreenshot(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            try { return FileNamePattern.IsMatch(fileName); }
            catch (RegexMatchTimeoutException) { return false; }
        }
    }
}
