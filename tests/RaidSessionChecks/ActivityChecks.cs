using where_am_i.Services;
using System.Collections.Specialized;

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
        var live = new ActivityHistory(clock: clock);
        var bound = live.VisibleEntries;
        var resets = 0;
        ((INotifyCollectionChanged)bound).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        for (var i = 0; i < 250; i++)
        {
            live.Add(i % 3 == 0 ? "맵 변경" : "전송", "동일한 메시지", isError: i % 7 == 0);
            check(live.VisibleEntries.Count == Math.Min(i + 1, 100) &&
                live.VisibleEntries.Zip(live.Entries).All(pair => ReferenceEquals(pair.First, pair.Second)),
                "Live history remains visible and ordered through repeated map/transmission updates and rollover");
        }
        live.SetErrorsOnly(true);
        check(live.VisibleEntries.SequenceEqual(live.Entries.Where(entry => entry.IsError)), "Errors filter selects retained errors");
        live.Add("전송", "성공");
        check(live.VisibleEntries.SequenceEqual(live.Entries.Where(entry => entry.IsError)), "Success does not hide retained filtered errors");
        live.SetErrorsOnly(false);
        check(live.VisibleEntries.Count == 100 && live.VisibleEntries.SequenceEqual(live.Entries), "Removing filter restores all retained activities");
        live.SetErrorsOnly(true);
        for (var i = 0; i < 100; i++) live.Add("전송", "성공");
        check(live.VisibleEntries.Count == 0 && live.Entries.Count == 100, "No errors in recent 100 means filtered empty, not data loss");
        live.SetErrorsOnly(false);
        check(live.VisibleEntries.Count == 100 && ReferenceEquals(bound, live.VisibleEntries) && resets == 0,
            "Bound collection survives all updates and filter changes without reset or replacement");
        check(new ActivityHistory().Entries.Count == 0, "New app session starts without persisted personal activity");
        check(LiveMapNavigation.GetUrl("woods") == "https://eftlibrary.com/live-map/woods", "Woods opens the detected map");
        check(LiveMapNavigation.GetUrl("shoreline") == "https://eftlibrary.com/live-map/shoreline", "Shoreline opens the detected map");
        foreach (var unknown in new[] { null, "", "unknown", "city", "Rezerv_Base", "https://example.invalid", "../settings" })
            check(LiveMapNavigation.GetUrl(unknown) == "https://eftlibrary.com/live-map", "Raw or unknown map opens live-map entry for server-driven navigation");
    }
}
