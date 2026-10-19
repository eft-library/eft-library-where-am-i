using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace where_am_i.Services
{
    public enum PathReportKind { Screenshot, Log }
    public enum PathReportOutcome { Skipped, AlreadyReported, Success, Failed, Rejected, Canceled, StateSaveFailed, Invalid }
    public sealed record PathReportResult(PathReportKind Kind, PathReportOutcome Outcome, HttpStatusCode? StatusCode = null);

    /// <summary>Anonymous reports of saved user selections, never automatic discovery candidates.</summary>
    public sealed class PathReportService
    {
        public const string Endpoint = "/api/where-am-i/v3/path-reports";
        private readonly HttpClient client;
        private readonly string appVersion;
        private readonly string statePath;
        private readonly string? userProfile;
        private readonly SemaphoreSlim gate = new(1, 1);

        public PathReportService(HttpClient client, string appVersion, string? statePath = null, string? userProfile = null)
        {
            this.client = client;
            this.appVersion = appVersion;
            this.statePath = statePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EFTLibrary", "WhereAmI", "path-report-state.json");
            this.userProfile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public async Task<PathReportResult> ReportAsync(PathReportKind kind, string? savedPath, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(savedPath)) return new(kind, PathReportOutcome.Skipped);
            if (kind is not (PathReportKind.Screenshot or PathReportKind.Log)) return new(kind, PathReportOutcome.Invalid);
            var type = kind == PathReportKind.Screenshot ? "screenshot" : "log";
            var masked = ReportedPathPrivacy.Mask(savedPath, userProfile);
            if (!ValidText(masked, 4096) || !ValidText(appVersion, 64)) return new(kind, PathReportOutcome.Invalid);
            // Only the completion fingerprint is persisted here; local watcher settings remain unchanged.
            var identity = savedPath.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();
            var key = type + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
            var entered = false;
            try
            {
                await gate.WaitAsync(token);
                entered = true;
                var completed = await LoadCompletedAsync(token);
                if (completed.Contains(key)) return new(kind, PathReportOutcome.AlreadyReported);
                using var content = new StringContent(JsonSerializer.Serialize(new
                {
                    path_type = type, path = masked, app_version = appVersion
                }), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(Endpoint, content, token);
                if (!response.IsSuccessStatusCode) return new(kind, PathReportOutcome.Failed, response.StatusCode);
                if (!await LatestJsonSender.IsAcceptedAsync(response, token))
                    return new(kind, PathReportOutcome.Rejected, response.StatusCode);
                completed.Add(key); // Mark the request's path, even if the current selection changed meanwhile.
                try { await SaveCompletedAsync(completed, token); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    return new(kind, PathReportOutcome.StateSaveFailed, response.StatusCode);
                }
                return new(kind, PathReportOutcome.Success, response.StatusCode);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new(kind, PathReportOutcome.Canceled);
            }
            catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // Do not expose exception text: it may contain the reported or local state path.
                return new(kind, PathReportOutcome.Failed);
            }
            finally { if (entered) gate.Release(); }
        }

        private async Task<HashSet<string>> LoadCompletedAsync(CancellationToken token)
        {
            try
            {
                var json = await File.ReadAllTextAsync(statePath, token);
                return JsonSerializer.Deserialize<HashSet<string>>(json) ?? new();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return new(); // Missing/corrupt/unreadable markers may cause a safe duplicate report.
            }
        }

        private async Task SaveCompletedAsync(HashSet<string> completed, CancellationToken token)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(statePath))!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".path-report-" + Guid.NewGuid() + ".tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(completed), token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, statePath, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }

        private static bool ValidText(string value, int max) => !string.IsNullOrWhiteSpace(value) &&
            value.Length <= max && !value.Any(c => c < 32 || c == 127);
    }
}
