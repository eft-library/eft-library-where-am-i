using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace where_am_i.Services
{
    public sealed record VersionCheckRequest(
        [property: JsonPropertyName("current_version")] string CurrentVersion,
        [property: JsonPropertyName("product")] string Product = "where-am-i",
        [property: JsonPropertyName("platform")] string Platform = "windows",
        [property: JsonPropertyName("architecture")] string Architecture = "x64",
        [property: JsonPropertyName("channel")] string Channel = "stable");
    public enum VersionCheckOutcome { UpToDate, UpdateAvailable, UpdateRequired, Failed, Canceled }
    public sealed record VersionRelease(string LatestVersion, string MinimumVersion, Uri? DownloadUrl, string ReleaseNotes);
    public sealed record VersionCheckResult(VersionCheckOutcome Outcome, DateTimeOffset CheckedAt,
        VersionRelease? Release = null, HttpStatusCode? StatusCode = null);

    public sealed partial class VersionCheckService
    {
        // Proposed contract: backend implementation is tracked in BACKEND_API_REQUIREMENTS.md.
        public const string Endpoint = "/api/where-am-i/v3/check-version";
        private readonly HttpClient client;
        private readonly TimeProvider clock;
        public VersionCheckService(HttpClient client, TimeProvider? clock = null)
        {
            this.client = client;
            this.clock = clock ?? TimeProvider.System;
        }

        public async Task<VersionCheckResult> CheckAsync(VersionCheckRequest request, CancellationToken token = default)
        {
            VersionCheckResult Result(VersionCheckOutcome outcome, VersionRelease? release = null, HttpStatusCode? status = null) =>
                new(outcome, clock.GetUtcNow(), release, status);
            if (!TryVersion(request.CurrentVersion, out var current) || request.Product != "where-am-i" ||
                request.Platform != "windows" || request.Architecture != "x64" || request.Channel != "stable")
                return Result(VersionCheckOutcome.Failed);
            try
            {
                using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(Endpoint, content, token);
                if (!response.IsSuccessStatusCode) return Result(VersionCheckOutcome.Failed, status: response.StatusCode);
                using var stream = await response.Content.ReadAsStreamAsync(token);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    return Result(VersionCheckOutcome.Failed, status: response.StatusCode);
                var echoed = Text(data, "current_version");
                var latestText = Text(data, "latest_version");
                var minimumText = Text(data, "min_supported_version");
                if (!TryVersion(echoed, out var echo) || echo != current || !TryVersion(latestText, out var latest) ||
                    !TryVersion(minimumText, out var minimum) || minimum > latest ||
                    !Bool(data, "update_available", out var available) || !Bool(data, "update_required", out var required) ||
                    available != (current < latest) || required != (current < minimum))
                    return Result(VersionCheckOutcome.Failed, status: response.StatusCode);
                Uri? download = null;
                var url = Text(data, "download_url");
                if ((!string.IsNullOrEmpty(url) && !TryOfficialDownload(url, out download)) || (available && download is null))
                    return Result(VersionCheckOutcome.Failed, status: response.StatusCode);
                if (data.TryGetProperty("download_url", out var urlElement) && urlElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return Result(VersionCheckOutcome.Failed, status: response.StatusCode);
                if (data.TryGetProperty("release_notes", out var notesElement) && notesElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return Result(VersionCheckOutcome.Failed, status: response.StatusCode);
                var notes = Text(data, "release_notes") ?? "";
                if (notes.Length > 4000) return Result(VersionCheckOutcome.Failed, status: response.StatusCode);
                var release = new VersionRelease(latestText!, minimumText!, download, notes);
                return Result(required ? VersionCheckOutcome.UpdateRequired : available ? VersionCheckOutcome.UpdateAvailable : VersionCheckOutcome.UpToDate,
                    release, response.StatusCode);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return Result(VersionCheckOutcome.Canceled); }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException)
            {
                return Result(VersionCheckOutcome.Failed);
            }
        }

        public static bool TryOfficialDownload(string value, out Uri? uri)
        {
            uri = null;
            if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
                parsed.Scheme != Uri.UriSchemeHttps || !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 ||
                value.Contains('\\') || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;
            var path = Uri.UnescapeDataString(parsed.AbsolutePath);
            const string releasePath = "/eft-library/eft-library-where-am-i/releases";
            var githubRelease = parsed.Host == "github.com" &&
                (path.Equals(releasePath, StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith(releasePath + "/", StringComparison.OrdinalIgnoreCase)) &&
                !path.Split('/').Any(part => part is "." or "..") &&
                !path.Contains('\\') && !path.Any(char.IsControl);
            if (parsed.Host is not ("eftlibrary.com" or "www.eftlibrary.com") && !githubRelease) return false;
            uri = parsed;
            return true;
        }

        private static bool TryVersion(string? text, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            if (text is null || text.Length > 64 || !NumericVersionRegex().IsMatch(text) || !Version.TryParse(text, out var parsed)) return false;
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
            return true;
        }
        private static string? Text(JsonElement data, string key) => data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static bool Bool(JsonElement data, string key, out bool result)
        {
            result = false;
            if (!data.TryGetProperty(key, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            result = value.GetBoolean();
            return true;
        }
        [GeneratedRegex(@"^[0-9]+\.[0-9]+(?:\.[0-9]+){0,2}$")]
        private static partial Regex NumericVersionRegex();
    }
}
