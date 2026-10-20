using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace where_am_i.Services;

public sealed record ScreenshotCleanupResult(int Recycled, int Skipped, int Failed, bool Canceled, string? Error = null);

public sealed partial class ScreenshotCleanupService
{
    private readonly Action<string> recycle;
    private readonly ScreenshotFileActivity activity;
    private readonly TimeProvider clock;
    public ScreenshotCleanupService(Action<string> recycle, ScreenshotFileActivity activity, TimeProvider? clock = null)
    { this.recycle = recycle; this.activity = activity; this.clock = clock ?? TimeProvider.System; }

    public static bool MatchesFileName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 240) return false;
        var match = CleanupNameRegex().Match(name);
        return match.Success && DateTime.TryParseExact(match.Groups["stamp"].Value,
            "yyyy-MM-dd'['HH-mm']'", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    public static bool TryFolder(string? folder, out string normalized)
    {
        normalized = "";
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return false;
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full)) return false;
            // Do not follow a selected junction/symlink, including one in an ancestor directory.
            for (var directory = new DirectoryInfo(full); directory is not null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed) return false;
            normalized = full;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    public Task<ScreenshotCleanupResult> RunAsync(string? folder, int retentionDays, CancellationToken token = default)
    {
        var completion = new TaskCompletionSource<ScreenshotCleanupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Clean(folder, retentionDays, token)); }
            catch (Exception e) { completion.SetException(e); }
        }) { IsBackground = true, Name = "Screenshot recycle" };
        // The Windows Shell IFileOperation adapter runs on this dedicated STA, never the UI thread.
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private ScreenshotCleanupResult Clean(string? folder, int days, CancellationToken token)
    {
        var moved = 0; var skipped = 0; var failed = 0;
        if (token.IsCancellationRequested) return new(0, 0, 0, true);
        if (days is < 1 or > 365 || !TryFolder(folder, out var root))
            return new(0, 0, 0, false, "정리 폴더와 보관 기간(1~365일)을 확인해 주세요. 로컬 폴더만 지원합니다.");
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-days);
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                if (token.IsCancellationRequested) break;
                if (!MatchesFileName(Path.GetFileName(path))) continue;
                try
                {
                    using var lease = activity.BeginCleanup(path);
                    if (lease is null) { skipped++; continue; }
                    if (!TryFolder(root, out var currentRoot) || currentRoot != root)
                        return new(moved, skipped, failed, false, "정리 폴더가 변경되어 작업을 중단했습니다.");
                    var file = new FileInfo(path);
                    if ((file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly)) != 0 ||
                        file.Length == 0 || file.LastWriteTimeUtc >= cutoff) { skipped++; continue; }
                    // Deny writers while the Shell recycles; allow deletion/rename required by recycling.
                    using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    file.Refresh();
                    if (!file.Exists || file.LastWriteTimeUtc >= cutoff || file.Length != handle.Length ||
                        (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0) { skipped++; continue; }
                    if (token.IsCancellationRequested) break;
                    recycle(path); // Never fall back to File.Delete if the recycle adapter fails.
                    moved++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or OperationCanceledException)
                { failed++; }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return new(moved, skipped, failed, token.IsCancellationRequested, "정리 폴더를 읽지 못했습니다. 경로와 접근 권한을 확인해 주세요."); }
        return new(moved, skipped, failed, token.IsCancellationRequested);
    }

    private const string Number = @"-?[0-9]+(?:\.[0-9]+)?";
    [GeneratedRegex(@"\A(?<stamp>[0-9]{4}-[0-9]{2}-[0-9]{2}\[[0-9]{2}-[0-9]{2}\])_" +
        Number + @", *" + Number + @", *" + Number + "_" +
        Number + @", *" + Number + @", *" + Number + @", *" + Number + "_" +
        Number + @" \([0-9]+\)\.png\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CleanupNameRegex();
}
