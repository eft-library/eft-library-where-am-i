using System.IO;
using System.Text.RegularExpressions;

namespace where_am_i.Services;

internal static class SteamLibraryPaths
{
    public static IEnumerable<string> Find()
    {
        var paths = new HashSet<string>(ScreenshotPathService.GetSteamInstallPaths(), StringComparer.OrdinalIgnoreCase);
        foreach (var steam in paths.ToArray())
        {
            try
            {
                var vdf = File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"));
                foreach (Match match in Regex.Matches(vdf, "\"path\"\\s*\"([^\"]+)\""))
                    paths.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return paths;
    }
}
