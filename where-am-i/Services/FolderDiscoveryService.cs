using System.IO;
using System.Text.RegularExpressions;

namespace where_am_i.Services;

public sealed record DiscoveredFolder(string Kind, string Path, DateTime LatestUtc)
{
    public string Display => $"{(Kind == "screenshot" ? "스크린샷" : "로그")} · {LatestUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {Path}";
}

public static class FolderDiscoveryService
{
    public static IEnumerable<string> LocalRoots() => DriveInfo.GetDrives()
        .Where(d => d.IsReady && d.DriveType == DriveType.Fixed).Select(d => d.RootDirectory.FullName);

    // One directory at a time keeps cancellation responsive and avoids buffering whole drives.
    public static IReadOnlyList<DiscoveredFolder> Search(IEnumerable<string> roots,
        IProgress<int>? progress, CancellationToken token)
    {
        var found = new Dictionary<string, DiscoveredFolder>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(roots);
        var options = new EnumerationOptions { IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 || !visited.Add(Path.GetFullPath(directory))) continue;
                if (visited.Count % 100 == 0) progress?.Report(visited.Count);
                foreach (var file in Directory.EnumerateFiles(directory, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(file);
                    string? kind = ScreenshotCleanupService.MatchesFileName(name) ? "screenshot" : null;
                    var folder = directory;
                    if (kind is null && name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("application", StringComparison.OrdinalIgnoreCase) &&
                        Regex.IsMatch(Path.GetFileName(directory), @"^log_\d{4}\.\d{2}\.\d{2}_\d{1,2}-\d{1,2}-\d{1,2}(?:_|$)", RegexOptions.IgnoreCase))
                    {
                        kind = "log";
                        folder = Directory.GetParent(directory)?.FullName ?? directory;
                    }
                    if (kind is null) continue;
                    var stamp = File.GetLastWriteTimeUtc(file);
                    var key = kind + "|" + folder;
                    if (!found.TryGetValue(key, out var old) || stamp > old.LatestUtc)
                        found[key] = new(kind, folder, stamp);
                }
                foreach (var child in Directory.EnumerateDirectories(directory, "*", options)) pending.Push(child);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        progress?.Report(visited.Count);
        return found.Values.OrderByDescending(f => f.LatestUtc).ToArray();
    }
}
