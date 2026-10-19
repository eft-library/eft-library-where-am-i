using where_am_i.Services;

internal static class ActivityChecks
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public static void Run(Action<bool, string> check)
    {
        var clock = new Clock();
        var history = new ActivityHistory(capacity: 3, clock: clock);
        history.Add("전송", "연결 실패", isError: true);
        clock.Now = clock.Now.AddSeconds(1);
        history.Add("전송", "연결 복구");
        check(history.Entries.Count == 2 && history.Entries[1].IsError,
            "Recovery does not overwrite the earlier failure");
        check(history.Entries[0].RecordedAt == clock.Now && history.Entries[0].Message == "연결 복구",
            "Newest entry uses reception time and appears first");
        var errors = history.Entries.Where(entry => entry.IsError).ToArray();
        check(errors.Length == 1 && errors[0].Message == "연결 실패", "Failures remain separately discoverable");
        history.Add("감지", "중지");
        history.Add("감지", "재개");
        check(history.Entries.Count == 3 && history.Entries.All(entry => !entry.IsError),
            "Only entries older than the capacity are evicted");
        check(history.Entries.Select(entry => entry.Message).SequenceEqual(new[] { "재개", "중지", "연결 복구" }),
            "Bounded history preserves remaining chronological order");
        check(new ActivityHistory().Entries.Count == 0, "New app session starts without persisted personal activity");
        check(LiveMapNavigation.GetUrl("woods") == "https://eftlibrary.com/live-map/woods", "Woods opens the detected map");
        check(LiveMapNavigation.GetUrl("shoreline") == "https://eftlibrary.com/live-map/shoreline", "Shoreline opens the detected map");
        foreach (var unknown in new[] { null, "", "unknown", "https://example.invalid", "../settings" })
            check(LiveMapNavigation.GetUrl(unknown) == "https://eftlibrary.com/live-map/customs", "Unknown map uses existing default");
    }
}
