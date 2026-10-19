using System.Text.RegularExpressions;

namespace where_am_i.Services
{
    public static partial class ReportedPathPrivacy
    {
        public static string Mask(string path, string? userProfile)
        {
            var result = path.Replace('/', '\\');
            var profile = userProfile?.Replace('/', '\\').TrimEnd('\\');
            if (!string.IsNullOrEmpty(profile) && profile.LastIndexOf('\\') > 2 &&
                (result.Equals(profile, StringComparison.OrdinalIgnoreCase) ||
                 result.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                result = profile[..(profile.LastIndexOf('\\') + 1)] + "<user>" + result[profile.Length..];
            }
            result = UserDirectoryRegex().Replace(result, "${1}<user>");
            return EmailSegmentRegex().Replace(result, "<account>");
        }

        [GeneratedRegex(@"([A-Za-z]:\\(?:Users|Documents and Settings)\\)[^\\]+", RegexOptions.IgnoreCase)]
        private static partial Regex UserDirectoryRegex();
        [GeneratedRegex(@"[^\\\s]+@[^\\\s]+")]
        private static partial Regex EmailSegmentRegex();
    }
}
