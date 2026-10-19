using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace where_am_i.Services
{
    public sealed record LogTargetSelection(FileInfo? ApplicationLog, string? RootDirectory, bool IsCustom, string? Error = null);

    /// <summary>State belongs to one monitor. Folder timestamps outrank file modification times.</summary>
    public sealed partial class TarkovLogTargetSelector
    {
        private readonly Func<string?> customPath;
        private readonly Func<IReadOnlyCollection<string>> automaticPaths;
        private readonly Dictionary<string, DateTime> fallbackStarts = new(StringComparer.OrdinalIgnoreCase);
        private string? scope;
        private (bool Named, DateTime Start)? newestSelected;
        private string? selectedDirectory;

        public TarkovLogTargetSelector() : this(TarkovLogPathService.GetConfiguredCustomPath,
            TarkovLogPathService.FindAutomaticCandidatePaths) { }

        internal TarkovLogTargetSelector(Func<string?> customPath, Func<IReadOnlyCollection<string>> automaticPaths)
        {
            this.customPath = customPath;
            this.automaticPaths = automaticPaths;
        }

        public LogTargetSelection Select()
        {
            string? saved;
            try { saved = customPath(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new(null, null, true, "로그 폴더 접근 권한 또는 경로 설정 파일을 확인해 주세요.");
            }
            var isCustom = !string.IsNullOrWhiteSpace(saved);
            string[] roots;
            try
            {
                var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in isCustom ? new[] { saved! } : automaticPaths())
                {
                    try { normalized.Add(ResolveRoot(path)); }
                    catch (Exception error) when (!isCustom && error is (ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)) { }
                }
                roots = normalized.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return new(null, null, isCustom, "로그 폴더 접근 권한 또는 지정 경로를 확인해 주세요.");
            }
            var nextScope = (isCustom ? "custom:" : "auto:") + string.Join("\n", roots);
            if (!string.Equals(scope, nextScope, StringComparison.OrdinalIgnoreCase))
            {
                scope = nextScope;
                newestSelected = null;
                selectedDirectory = null;
                fallbackStarts.Clear();
            }

            Candidate? best = null;
            foreach (var root in roots.Where(Directory.Exists))
            {
                try
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    };
                    foreach (var path in Directory.EnumerateFiles(root, "*.log", options)
                        .Where(p => Path.GetFileName(p).Contains("application", StringComparison.OrdinalIgnoreCase)))
                    {
                        var file = new FileInfo(path);
                        var directory = file.Directory!;
                        var stamp = FindSessionTimestamp(directory, root);
                        if (!fallbackStarts.TryGetValue(directory.FullName, out var fallback))
                            fallbackStarts[directory.FullName] = fallback = directory.CreationTimeUtc;
                        var candidate = new Candidate(file, root, stamp.HasValue, stamp ?? fallback);
                        if (newestSelected.HasValue && Compare(candidate.Key, newestSelected.Value) < 0) continue;
                        if (best is null || Compare(candidate.Key, best.Key) > 0 ||
                            (Compare(candidate.Key, best.Key) == 0 && Prefer(candidate, best))) best = candidate;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Never escape a configured root just because it is unavailable.
                }
            }
            if (best is null)
                return new(null, isCustom ? roots[0] : null, isCustom,
                    "EFT application 로그를 찾지 못했습니다. " +
                    (isCustom ? "지정한 Logs 경로에서 새 로그를 기다립니다." : "자동 탐색 경로에서 새 로그를 기다립니다."));
            newestSelected = best.Key;
            selectedDirectory = best.File.DirectoryName;
            return new(best.File, best.Root, isCustom);
        }

        private bool Prefer(Candidate candidate, Candidate other)
        {
            var candidateCurrent = string.Equals(candidate.File.DirectoryName, selectedDirectory, StringComparison.OrdinalIgnoreCase);
            var otherCurrent = string.Equals(other.File.DirectoryName, selectedDirectory, StringComparison.OrdinalIgnoreCase);
            if (candidateCurrent != otherCurrent) return candidateCurrent;
            // Within a session, rotation can select another application file without changing the session reader.
            if (string.Equals(candidate.File.DirectoryName, other.File.DirectoryName, StringComparison.OrdinalIgnoreCase) &&
                candidate.File.LastWriteTimeUtc != other.File.LastWriteTimeUtc)
                return candidate.File.LastWriteTimeUtc > other.File.LastWriteTimeUtc;
            return string.Compare(candidate.File.FullName, other.File.FullName, StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static int Compare((bool Named, DateTime Start) left, (bool Named, DateTime Start) right)
        {
            var named = left.Named.CompareTo(right.Named);
            return named != 0 ? named : left.Start.CompareTo(right.Start);
        }

        private static string ResolveRoot(string path)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            // An execution folder selected directly can still discover its next sibling under Logs.
            if (SessionTimestamp(directory.Name).HasValue && directory.Parent?.Name.Equals("Logs", StringComparison.OrdinalIgnoreCase) == true)
                return Path.TrimEndingDirectorySeparator(directory.Parent.FullName);
            return Path.TrimEndingDirectorySeparator(directory.FullName);
        }

        private static DateTime? FindSessionTimestamp(DirectoryInfo directory, string root)
        {
            for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
            {
                var timestamp = SessionTimestamp(current.Name);
                if (timestamp.HasValue) return timestamp;
                if (string.Equals(current.FullName, root, StringComparison.OrdinalIgnoreCase)) break;
            }
            return null;
        }

        private static DateTime? SessionTimestamp(string name)
        {
            var match = SessionFolderRegex().Match(name);
            if (!match.Success) return null;
            try
            {
                var values = Enumerable.Range(1, 6).Select(i => int.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
                return new DateTime(values[0], values[1], values[2], values[3], values[4], values[5]);
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        private sealed record Candidate(FileInfo File, string Root, bool Named, DateTime Start)
        {
            public (bool Named, DateTime Start) Key => (Named, Start);
        }

        [GeneratedRegex(@"^log_([0-9]{4})\.([0-9]{2})\.([0-9]{2})_([0-9]{1,2})-([0-9]{1,2})-([0-9]{1,2})_", RegexOptions.IgnoreCase)]
        private static partial Regex SessionFolderRegex();
    }
}
