using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace where_am_i.Services;

public sealed record MapAliasEntry(string Alias, string Map);
public sealed record MapAliasSnapshot(string Version, MapAliasEntry[] Aliases);
public sealed record MapAliasResult(bool Updated, bool CacheSaved, string Message);

/// <summary>One validated snapshot replaces the remote list; built-ins remain an offline fallback.</summary>
public sealed class MapAliasService
{
    public const string Endpoint = "/api/where-am-i/v3/map-aliases";
    private const int MaxBytes = 262144;
    private readonly HttpClient client;
    private readonly string cachePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    public MapAliasService(HttpClient client, string cachePath) { this.client = client; this.cachePath = cachePath; }

    public bool LoadCache()
    {
        try
        {
            if (!File.Exists(cachePath) || new FileInfo(cachePath).Length > MaxBytes) return false;
            var snapshot = JsonSerializer.Deserialize<MapAliasSnapshot>(File.ReadAllText(cachePath));
            if (!Validate(snapshot, out var aliases)) return false;
            TarkovLogParser.SetRemoteAliases(aliases);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public async Task<MapAliasResult> RefreshAsync(CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return new(false, false, "Map alias refresh canceled");
        if (!await gate.WaitAsync(0)) return new(false, false, "Map alias refresh already running");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var requestToken = timeout.Token;
        try
        {
            using var response = await client.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (!response.IsSuccessStatusCode) return new(false, false, "Map alias request failed; previous aliases retained");
            using var input = await response.Content.ReadAsStreamAsync(requestToken);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(bytes, requestToken)) > 0)
            {
                if (buffer.Length + count > MaxBytes) return new(false, false, "Map alias response too large");
                buffer.Write(bytes, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data))
                return new(false, false, "Invalid map alias response");
            var snapshot = data.Deserialize<MapAliasSnapshot>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (!Validate(snapshot, out var aliases)) return new(false, false, "Invalid map alias list; previous aliases retained");
            requestToken.ThrowIfCancellationRequested();
            var saved = Save(snapshot!);
            TarkovLogParser.SetRemoteAliases(aliases);
            return new(true, saved, saved ? "Map aliases refreshed" : "Map aliases refreshed; cache save failed");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return new(false, false, "Map alias refresh canceled"); }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or OperationCanceledException)
        { return new(false, false, "Map alias refresh failed; previous aliases retained"); }
        finally { gate.Release(); }
    }

    private bool Save(MapAliasSnapshot snapshot)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath))!);
            temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, cachePath, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool Validate(MapAliasSnapshot? snapshot, out IReadOnlyDictionary<string, string> aliases)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        aliases = result;
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Version) || snapshot.Version.Length > 128 ||
            snapshot.Aliases is not { Length: > 0 and <= 1024 }) return false;
        foreach (var entry in snapshot.Aliases)
        {
            if (entry is null || entry.Alias is null || entry.Map is null) return false;
            var key = TarkovLogParser.NormalizeAlias(entry.Alias);
            if (key.Length is 0 or > 128 || !Regex.IsMatch(key, @"\A[a-z0-9_ -]+\z", RegexOptions.CultureInvariant) ||
                !LiveMapNavigation.IsKnownMap(entry.Map) || !result.TryAdd(key, entry.Map)) return false;
        }
        return true;
    }
}
