using Microsoft.Win32;
using System.IO;

namespace where_am_i.Services
{
    public static class ScreenshotPathService
    {
        private const string TarkovScreenshotRelativePath = @"Escape from Tarkov\Screenshots";
        private const string TarkovSteamAppId = "3932890";
        private static readonly string CustomPathFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EFT Library",
            "where-am-i",
            "screenshot-path.txt"
        );

        public static IReadOnlyCollection<string> FindCandidatePaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var customPath = GetSavedCustomPath();
            if (!string.IsNullOrWhiteSpace(customPath))
                paths.Add(customPath);

            AddCombinedPath(
                paths,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                TarkovScreenshotRelativePath
            );

            AddCombinedPath(
                paths,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @"Documents\Escape from Tarkov\Screenshots"
            );

            foreach (var oneDrivePath in GetOneDrivePaths())
            {
                AddCombinedPath(
                    paths,
                    oneDrivePath,
                    @"Documents\Escape from Tarkov\Screenshots"
                );
            }

            var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            AddCombinedPath(paths, picturesPath, "Screenshots");
            AddCombinedPath(paths, picturesPath, "Steam");

            foreach (var steamPath in GetSteamInstallPaths())
            {
                AddSteamScreenshotPaths(paths, steamPath);
            }

            return paths;
        }

        public static string SaveCustomPath(string path)
        {
            var normalizedPath = NormalizePath(path);
            var settingsDirectory = Path.GetDirectoryName(CustomPathFile)
                ?? throw new InvalidOperationException("설정 폴더 경로를 확인할 수 없습니다.");

            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(CustomPathFile, normalizedPath);
            return normalizedPath;
        }

        private static string? GetSavedCustomPath()
        {
            try
            {
                if (!File.Exists(CustomPathFile))
                    return null;

                var path = File.ReadAllText(CustomPathFile).Trim();
                return string.IsNullOrWhiteSpace(path) ? null : NormalizePath(path);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> GetOneDrivePaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var variableName in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                var path = Environment.GetEnvironmentVariable(variableName);
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
            }

            return paths;
        }

        private static IEnumerable<string> GetSteamInstallPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddRegistryPath(
                paths,
                @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam",
                "SteamPath"
            );
            AddRegistryPath(
                paths,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                "InstallPath"
            );
            AddRegistryPath(
                paths,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                "InstallPath"
            );

            AddCombinedPath(
                paths,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam"
            );

            return paths;
        }

        private static void AddRegistryPath(
            ISet<string> paths,
            string keyName,
            string valueName
        )
        {
            try
            {
                if (Registry.GetValue(keyName, valueName, null) is string path &&
                    !string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(NormalizePath(path));
                }
            }
            catch
            {
                // 레지스트리를 읽을 수 없는 환경에서는 다른 후보 경로를 사용한다.
            }
        }

        private static void AddSteamScreenshotPaths(ISet<string> paths, string steamPath)
        {
            var userDataPath = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userDataPath))
                return;

            try
            {
                foreach (var userPath in Directory.EnumerateDirectories(userDataPath))
                {
                    var remotePath = Path.Combine(userPath, "760", "remote");
                    if (!Directory.Exists(remotePath))
                        continue;

                    var tarkovPath = Path.Combine(remotePath, TarkovSteamAppId, "screenshots");
                    if (Directory.Exists(tarkovPath))
                        paths.Add(NormalizePath(tarkovPath));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void AddCombinedPath(
            ISet<string> paths,
            string basePath,
            string relativePath
        )
        {
            if (string.IsNullOrWhiteSpace(basePath))
                return;

            paths.Add(NormalizePath(Path.Combine(basePath, relativePath)));
        }

        private static string NormalizePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var rootPath = Path.GetPathRoot(fullPath);

            return string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                );
        }
    }
}
