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
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Add(paths, GetSavedCustomPath());
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
            File.WriteAllText(CustomPathFile, normalized);
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

        private static string? GetSavedCustomPath()
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

        private static string Normalize(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
