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
            Check(tracker.Current.Map == expected.GetProperty("map").GetString(), scenario.Name + " map");
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
Check(missingWoods.Current.Map is null && missingWoods.Current.Timer is null, "Unknown map clears old state");
var streets = Lines("streets-city-repeat", "application");
var loadingCity = Tracker();
loadingCity.AppendApplicationLine(streets[0]);
var cityRequest = where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", loadingCity.Current);
Check(cityRequest.Map == "streets-of-tarkov" && cityRequest.StartedAt is null && !cityRequest.IsActive,
    "Actual city scene sends Streets map at loading without waiting for GameStarted");
Check(cityRequest.TimerObservedAt is null && cityRequest.RaidEndsAt is null,
    "City loading clears prior timer in API snapshot");
var unmapped = Tracker();
var rawScene = streets[0].Replace("city", "newcity");
unmapped.AppendApplicationLine(rawScene);
var rawLoading = where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", unmapped.Current);
Check(unmapped.Current.Map is null && unmapped.Current.RawMap == "newcity" && rawLoading.Map == "newcity",
    "Unregistered scene preserves raw map and sends it at loading instead of null");
Check(rawLoading.StartedAt is null && rawLoading.RaidEndsAt is null, "Unknown loading has no start or wire timer");
unmapped.AppendOutputLine(Lines("streets-city-repeat", "output")[0]);
unmapped.AppendApplicationLine(streets[1]);
var rawStarted = where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", unmapped.Current);
Check(rawStarted.Map == "newcity" && rawStarted.IsActive && unmapped.Current.Timer is not null && rawStarted.RaidEndsAt is null,
    "Unknown map retains timer locally but sends raw name without unsupported timer");
Check(where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", new("empty", null, null, null, null)).Map is null,
    "No map observed remains null rather than inventing an alias");
Check(TarkovLogParser.FindRawMap("rcid:NewCity.scenespreset maps/woods.bundle") == "NewCity",
    "Raw scene preserves source case and scene priority");
Check(TarkovLogParser.FindRawMap("maps/newcity_preset.bundle") == "newcity", "Bundle-only source also preserves raw map");
Check(TarkovLogParser.FindRawMap("unrelated text") is null, "Unrelated log text cannot become raw map");
Check(where_am_i.Models.SendRaidStateRequest.FromSession("test@example.invalid", loadingCity.Current).Map == "streets-of-tarkov",
    "Mapped slug still takes precedence over raw alias");
unmapped.AppendApplicationLine(streets[2]);
Check(unmapped.Current.Map == "streets-of-tarkov" && unmapped.Current.RawMap == "city" && unmapped.Current.Timer is null,
    "Next loading replaces raw alias and clears previous timer");
ActivityChecks.Run(Check);
await FileReadingChecks.Run(root, zone, Check);
await TransmissionChecks.Run(root, zone, Check);
await ScreenshotChecks.Run(Check);
await PathReportChecks.Run(Check);
await VersionChecks.Run(Check);
await LogTargetChecks.Run(root, Check);
await MapAliasChecks.Run(Check);
await IntegrationChecks.Run(root, Check);
Console.WriteLine($"PASS: {checks} checks against production C# sources");
