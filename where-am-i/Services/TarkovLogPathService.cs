using Microsoft.Win32;
using System.IO;

namespace where_am_i.Services
{
    public static class TarkovLogPathService
    {
        private static readonly string CustomPathFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EFTLibrary",
            "WhereAmI",
            "log-path.txt"
        );

        public static IReadOnlyCollection<string> FindCandidatePaths()
        {
            var paths = new HashSet<string>(FindAutomaticCandidatePaths(), StringComparer.OrdinalIgnoreCase);
            Add(paths, GetSavedCustomPath());
            return paths;
        }

        public static IReadOnlyCollection<string> FindAutomaticCandidatePaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Add(paths, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Battlestate Games", "EscapeFromTarkov", "Logs"
            ));

            foreach (var installPath in GetInstallPaths())
            {
                Add(paths, Path.Combine(installPath, "Logs"));
                Add(paths, Path.Combine(installPath, "build", "Logs"));
            }

            return paths;
        }

        public static string SaveCustomPath(string path)
        {
            var normalized = Normalize(path);
            var directory = Path.GetDirectoryName(CustomPathFile)
                ?? throw new InvalidOperationException("설정 폴더를 확인할 수 없습니다.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".log-path-" + Guid.NewGuid() + ".tmp");
            try
            {
                File.WriteAllText(temporary, normalized);
                File.Move(temporary, CustomPathFile, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            return normalized;
        }

        private static IEnumerable<string> GetInstallPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[]
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov"
            })
            {
                try
                {
                    if (Registry.GetValue(key, "InstallLocation", null) is string path &&
                        !string.IsNullOrWhiteSpace(path))
                        paths.Add(path);
                }
                catch
                {
                    // Other candidates remain available when registry access is denied.
                }
            }

            return paths;
        }

        internal static string? GetConfiguredCustomPath()
        {
            try { return File.ReadAllText(CustomPathFile).Trim(); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        public static string? GetSavedCustomPath()
        {
            try
            {
                return File.Exists(CustomPathFile) ? File.ReadAllText(CustomPathFile).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void Add(ISet<string> paths, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(Normalize(path));
        }

        private static string Normalize(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return string.Equals(fullPath, Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase)
                ? fullPath : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
