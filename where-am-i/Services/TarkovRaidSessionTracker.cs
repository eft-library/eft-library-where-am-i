using System.Globalization;
using System.Text.RegularExpressions;
using where_am_i.Models;

namespace where_am_i.Services
{
    /// <summary>
    /// One instance per game execution directory. Feed complete lines only.
    /// Replays relevant events by log timestamp so polling order across files is immaterial.
    /// Does not detect exits or infer transit links. Callers serialize access.
    /// </summary>
    public sealed partial class TarkovRaidSessionTracker
    {
        private readonly string sourceSession;
        private readonly TimeZoneInfo logTimeZone;
        private readonly HashSet<LogEvent> events = new();

        public RaidSession Current { get; private set; }

        public TarkovRaidSessionTracker(string sourceSession, TimeZoneInfo? logTimeZone = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceSession);
            this.sourceSession = sourceSession;
            this.logTimeZone = logTimeZone ?? TimeZoneInfo.Local;
            Current = Empty();
        }

        public bool AppendApplicationLine(string line) => Append(line, false);
        public bool AppendOutputLine(string line) => Append(line, true);

        private RaidSession Empty() => new(sourceSession, null, null, null, null);

        private bool Append(string line, bool output)
        {
            if (line.Length < 23 || !DateTime.TryParseExact(line[..23],
                "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var at) || logTimeZone.IsInvalidTime(at))
                return false;

            LogEvent? next = null;
            if (output)
            {
                var match = TimerRegex().Match(line);
                if (match.Success && TryUtc(match.Groups[1].Value, out var observed) &&
                    TryUtc(match.Groups[2].Value, out var started) &&
                    TryUtc(match.Groups[3].Value, out var ends) &&
                    started <= observed && observed < ends)
                    next = new(at, 1, null, new(observed, started, ends));
            }
            else if (line.Contains("|application|scene preset path:", StringComparison.Ordinal))
                next = new(at, 0, TarkovLogParser.FindMap(line), null, TarkovLogParser.FindRawMap(line));
            else if (line.Contains("|application|GameStarted:", StringComparison.Ordinal))
                next = new(at, 2, null, null);

            if (next is null || !events.Add(next))
                return false;

            var previous = Current;
            var state = Empty();
            foreach (var item in events.OrderBy(e => e.At).ThenBy(e => e.Kind))
            {
                var timestamp = new DateTimeOffset(item.At, logTimeZone.GetUtcOffset(item.At));
                if (item.Kind == 0)
                    state = new(sourceSession, item.Map, timestamp, null, null, item.RawMap);
                else if (item.Kind == 1 && state.LoadingAt.HasValue)
                    state = state with { Timer = item.Timer };
                else if (item.Kind == 2)
                    // Without a new loading event, never reuse a preceding raid's timer.
                    state = state with { StartedAt = timestamp,
                        Timer = state.StartedAt.HasValue ? null : state.Timer };
            }
            Current = state;
            return Current != previous;
        }

        private static bool TryUtc(string value, out DateTimeOffset timestamp) =>
            DateTimeOffset.TryParseExact(value, "MM/dd/yyyy HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);

        private sealed record LogEvent(DateTime At, int Kind, string? Map, RaidTimer? Timer, string? RawMap = null);

        [GeneratedRegex(@"\|output\|GameTimer utcNow:(\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}) start:(\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}) escape:(\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}) PastTime:")]
        private static partial Regex TimerRegex();
    }
}
