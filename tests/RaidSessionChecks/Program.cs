using System.Text.Json;
using where_am_i.Services;

var zone = TimeZoneInfo.CreateCustomTimeZone("Fixture-KST", TimeSpan.FromHours(9), "KST", "KST");
var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
var expectations = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "expected.json")));
var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
string[] Lines(string scenario, string kind) => File.ReadAllLines(Path.Combine(root, scenario, kind + "_000.log"));
TarkovRaidSessionTracker Tracker() => new("execution-a", zone);

foreach (var scenario in expectations.RootElement.EnumerateObject())
{
    var app = Lines(scenario.Name, "application");
    var output = Lines(scenario.Name, "output");
    var chronological = app.Select(l => (Line: l, Output: false))
        .Concat(output.Select(l => (Line: l, Output: true)))
        .OrderBy(e => e.Line[..23], StringComparer.Ordinal).ToArray();
    var tracker = Tracker();
    var index = 0;
    foreach (var entry in chronological)
    {
        if (entry.Output) tracker.AppendOutputLine(entry.Line);
        else tracker.AppendApplicationLine(entry.Line);
        if (entry.Line.Contains("scene preset path:"))
            Check(tracker.Current.Timer is null && tracker.Current.StartedAt is null, "Loading must clear prior raid");
        if (entry.Line.Contains("|GameStarted:"))
        {
            var expected = scenario.Value[index++];
            Check(tracker.Current.Map == expected.GetProperty("raw_map").GetString(), scenario.Name + " map");
            var remaining = expected.GetProperty("remaining_seconds");
            Check(tracker.Current.Timer?.RemainingAtObservation.TotalSeconds ==
                (remaining.ValueKind == JsonValueKind.Null ? (double?)null : remaining.GetDouble()), scenario.Name + " timer");
        }
    }
    Check(index == scenario.Value.GetArrayLength(), scenario.Name + " starts");
    var final = tracker.Current;
    // Either file may be read first, including recovering both files mid-raid.
    foreach (bool outputFirst in new[] { true, false })
    {
        var recovered = Tracker();
        if (outputFirst) foreach (var line in output) recovered.AppendOutputLine(line);
        foreach (var line in app) recovered.AppendApplicationLine(line);
        if (!outputFirst) foreach (var line in output) recovered.AppendOutputLine(line);
        Check(recovered.Current == final, scenario.Name + " poll order independence");
        foreach (var line in output) Check(!recovered.AppendOutputLine(line), "Duplicate output");
        foreach (var line in app) Check(!recovered.AppendApplicationLine(line), "Duplicate application");
        Check(recovered.Current == final, "Duplicates preserve state");
    }
}
var customs = Lines("customs-woods", "application");
var timers = Lines("customs-woods", "output");
var missingWoods = Tracker();
foreach (var line in customs) missingWoods.AppendApplicationLine(line);
missingWoods.AppendOutputLine(timers[0]); // Old timer arriving after the Woods app events.
Check(missingWoods.Current.Map == "woods" && missingWoods.Current.Timer is null, "Old Customs timer must not attach to Woods");
missingWoods.AppendOutputLine(timers[1]);
Check(missingWoods.Current.Timer?.ObservedAt.Offset == TimeSpan.Zero, "Timer UTC");
Check(missingWoods.Current.StartedAt?.Offset == TimeSpan.FromHours(9), "Log local timezone");
var before = missingWoods.Current;
Check(!missingWoods.AppendOutputLine(timers[1].Replace("10/19/2026 00:15:32", "10/18/2026 23:00:00")), "Reject expired timer");
Check(!missingWoods.AppendOutputLine(timers[1].Replace("10/19/2026 00:15:32", "bad")), "Reject malformed timer");
Check(!missingWoods.AppendApplicationLine("incomplete"), "Reject incomplete line");
Check(missingWoods.Current == before, "Malformed input preserves state");
var fresh = new TarkovRaidSessionTracker("execution-b", zone);
Check(fresh.Current.Timer is null && fresh.Current.Map is null, "Execution isolation");
var unknown = customs[2].Replace("woods", "unrecognized");
missingWoods.AppendApplicationLine(unknown.Replace("08:38:58.000", "09:00:00.000"));
Check(missingWoods.Current.Map == "unrecognized" && missingWoods.Current.Timer is null, "Unknown raw map replaces previous map and clears old timer");
var streets = Lines("streets-city-repeat", "application");
var rawLoadingTracker = Tracker();
rawLoadingTracker.AppendApplicationLine(streets[0]);
var rawLoading = where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", rawLoadingTracker.Current);
Check(rawLoading.Map == "city" && rawLoading.StartedAt is null && !rawLoading.IsActive && rawLoading.RaidEndsAt is null,
    "City raw alias is sent at loading instead of canonical slug");
rawLoadingTracker.AppendOutputLine(Lines("streets-city-repeat", "output")[0]);
rawLoadingTracker.AppendApplicationLine(streets[1]);
var cityStarted = where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", rawLoadingTracker.Current);
Check(cityStarted.Map == "city" && cityStarted.IsActive && cityStarted.RaidEndsAt is not null,
    "City raw alias and timer are sent together for server mapping");
var reserveScene = Lines("reserve-underscore", "application")[1];
Check(TarkovLogParser.FindMap(reserveScene) == "Rezerv_Base", "Reserve source casing and underscore are preserved");
var reserveLoading = Tracker();
reserveLoading.AppendApplicationLine(reserveScene);
Check(where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", reserveLoading.Current).Map == "Rezerv_Base",
    "Reserve request carries original alias instead of mapped slug");
var unknownRaw = Tracker();
unknownRaw.AppendApplicationLine(streets[0].Replace("city", "FutureCity"));
unknownRaw.AppendOutputLine(Lines("streets-city-repeat", "output")[0]);
unknownRaw.AppendApplicationLine(streets[1]);
var unknownRequest = where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", unknownRaw.Current);
Check(unknownRequest.Map == "FutureCity" && unknownRequest.RaidEndsAt is not null, "No local whitelist blocks a new raw map or its timer");
Check(TarkovLogParser.FindRawMap("unrelated text") is null, "No observed map remains null");
Check(TarkovLogParser.FindRawMap("maps/rezerv_base_preset.bundle") == "rezerv_base", "Bundle fallback extracts map name without resolving it");
ActivityChecks.Run(Check);
await FileReadingChecks.Run(root, zone, Check);
await TransmissionChecks.Run(root, zone, Check);
await ScreenshotChecks.Run(Check);
await PathReportChecks.Run(Check);
await VersionChecks.Run(Check);
await LogTargetChecks.Run(root, Check);
await IntegrationChecks.Run(root, Check);
Console.WriteLine($"PASS: {checks} checks against production C# sources");
