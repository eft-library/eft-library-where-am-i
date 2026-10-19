using System.Globalization;
using System.Text.RegularExpressions;
using where_am_i.Models;

namespace where_am_i.Services
{
    public static partial class TarkovLogParser
    {
        public static RaidState Parse(string text)
        {
            var map = FindMap(text);
            var startedAt = FindLastTimestamp(text, "GameStarted");
            var endedAt = FindLastTimestamp(text, "PrepareSelectedProfileLocally");
            var isActive = startedAt.HasValue && (!endedAt.HasValue || endedAt < startedAt);
            var transitCount = 0;

            foreach (Match match in TransitRegex().Matches(text))
                int.TryParse(match.Groups[1].Value, out transitCount);

            return new RaidState(map, startedAt, isActive, transitCount);
        }

        public static LogLocation? ParseLastLocation(string text, DateTimeOffset? raidStartedAt)
        {
            LogLocation? location = null;
            foreach (Match match in PositionRegex().Matches(text))
            {
                if (!DateTimeOffset.TryParseExact(
                    match.Groups[1].Value,
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var observedAt
                ) ||
                    !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                    !double.TryParse(match.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ||
                    (raidStartedAt.HasValue && observedAt < raidStartedAt.Value))
                {
                    continue;
                }

                location = new LogLocation(x, y, z, observedAt);
            }

            return location;
        }

        public static string? FindMap(string text) => FindRawMap(text);

        public static string? FindRawMap(string text)
        {
            foreach (var regex in new[] { SceneRegex(), BundleRegex(), LocationsRegex(), LocationRegex() })
            {
                var matches = regex.Matches(text);
                if (matches.Count == 0) continue;
                var raw = matches[^1].Groups[1].Value.Trim();
                if (raw.Length is > 0 and <= 128 && !raw.Any(char.IsControl)) return raw;
            }
            return null;
        }

        private static DateTimeOffset? FindLastTimestamp(string text, string marker)
        {
            var timestamp = (DateTimeOffset?)null;
            foreach (var line in text.Split('\n'))
            {
                if (!line.Contains(marker, StringComparison.OrdinalIgnoreCase) || line.Length < 23)
                    continue;

                if (DateTime.TryParseExact(
                    line[..23],
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var parsed
                ))
                    timestamp = new DateTimeOffset(parsed);
            }

            return timestamp;
        }

        [GeneratedRegex(@"rcid:([a-z0-9_]+)\.scenespreset", RegexOptions.IgnoreCase)]
        private static partial Regex SceneRegex();
        [GeneratedRegex(@"maps/([a-z0-9_]+?)(?:_preset)?\.bundle", RegexOptions.IgnoreCase)]
        private static partial Regex BundleRegex();
        [GeneratedRegex(@"Locations:\s*([A-Za-z0-9 _-]+?)\s*(?:->|[,\r\n])")]
        private static partial Regex LocationsRegex();
        [GeneratedRegex(@"\bLocation:\s*([A-Za-z0-9_]+)\s*,")]
        private static partial Regex LocationRegex();
        [GeneratedRegex(@"\[Transit\][^\r\n]*?Count:(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex TransitRegex();
        [GeneratedRegex(@"(?m)^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)[^\r\n]*Position:\((-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)")]
        private static partial Regex PositionRegex();
    }
}
