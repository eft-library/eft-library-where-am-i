using System.Net;
using System.Text.Json;
using where_am_i.Models;
using where_am_i.Services;

static class IntegrationChecks
{
    sealed class Handler : HttpMessageHandler
    {
        public List<(string Path, string Body)> Sent { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == MapAliasService.Endpoint)
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"version":"integration-1","aliases":[{"alias":"futurewoods","map":"woods"}]}}""") };
            Sent.Add((path, await request.Content!.ReadAsStringAsync(token)));
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":true}") };
        }
    }

    public static async Task Run(string fixtures, Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "eft-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var application = Path.Combine(root, "application_000.log");
            var app = File.ReadAllLines(Path.Combine(fixtures, "customs-woods/application_000.log"));
            var timers = File.ReadAllLines(Path.Combine(fixtures, "customs-woods/output_000.log"));
            File.WriteAllText(application, string.Join('\n', app[2..]).Replace("woods", "futurewoods") + "\n");
            File.WriteAllText(Path.Combine(root, "output_000.log"), timers[1] + "\n");
            var handler = new Handler();
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
            var sender = new RaidSessionSender(client, "integration@example.invalid");
            using var monitor = new TarkovLogMonitor(() => new FileInfo(application));
            var first = new TaskCompletionSource<RaidSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resolved = new TaskCompletionSource<RaidSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.SessionChanged += (_, session) =>
            {
                first.TrySetResult(session);
                if (session.Map == "woods" && session.Timer is not null) resolved.TrySetResult(session);
            };
            monitor.Start();
            var initial = await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(initial.Map is null, "Integration: unknown map remains unknown before alias lookup");
            var alias = new MapAliasService(client, Path.Combine(root, "aliases.json"));
            check((await alias.RefreshAsync()).Updated, "Integration: server snapshot supplies new map alias");
            monitor.Refresh();
            var session = await resolved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(session.StartedAt is not null && session.Timer is not null, "Integration: alias refresh reparses existing log and correlates timer");
            var sent = await sender.SendLatestAsync(session);
            check(sent.Outcome == SendOutcome.Success && sent.IncludesTimer, "Integration: reparsed raid and timer reach fake API");
            using (var json = JsonDocument.Parse(handler.Sent.Single().Body))
            {
                var data = json.RootElement;
                check(data.GetProperty("map").GetString() == "woods" && data.GetProperty("raid_ends_at").GetDateTimeOffset() == session.Timer!.EndsAt,
                    "Integration: map and timer payload agree with monitor state");
                check(!handler.Sent.Single().Body.Contains(root) && data.GetProperty("session_id").GetString()?.Length == 64,
                    "Integration: local execution path is hashed before transmission");
            }
            var screenshot = Path.Combine(root, "2026-10-19_1.0,2.0,3.0_0.png");
            File.WriteAllBytes(screenshot, new byte[] { 1 });
            check((await new ScreenshotLocationSender(client, "integration@example.invalid").ProcessFileAsync(screenshot)).Outcome == ScreenshotOutcome.Success,
                "Integration: screenshot coordinates flow alongside log timer");
            using (var json = JsonDocument.Parse(handler.Sent.Last().Body))
                check(json.RootElement.EnumerateObject().Count() == 2 && json.RootElement.GetProperty("location").GetString() == Path.GetFileName(screenshot),
                    "Integration: screenshot API retains filename-only contract");
            monitor.Stop();
        }
        finally
        {
            TarkovLogParser.SetRemoteAliases(new Dictionary<string, string>());
            Directory.Delete(root, true);
        }
    }
}
