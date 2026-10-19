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
            Sent.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(token)));
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
            var app = File.ReadAllLines(Path.Combine(fixtures, "reserve-underscore/application_000.log"));
            var timers = File.ReadAllLines(Path.Combine(fixtures, "reserve-underscore/output_000.log"));
            File.WriteAllText(application, app[1] + "\n");
            var handler = new Handler();
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
            var sender = new RaidSessionSender(client, "integration@example.invalid");
            using var monitor = new TarkovLogMonitor(() => new FileInfo(application));
            var first = new TaskCompletionSource<RaidSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<RaidSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.SessionChanged += (_, session) =>
            {
                first.TrySetResult(session);
                if (session.StartedAt is not null && session.Timer is not null) started.TrySetResult(session);
            };
            monitor.Start();
            var initial = await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(initial.Map == "Rezerv_Base" && initial.StartedAt is null, "Integration: raw Reserve alias detected during loading without dictionaries");
            check((await sender.SendLatestAsync(initial)).Outcome == SendOutcome.Success, "Integration: loading sends API before GameStarted");
            using (var json = JsonDocument.Parse(handler.Sent[0].Body))
                check(json.RootElement.GetProperty("map").GetString() == "Rezerv_Base" && !json.RootElement.GetProperty("is_active").GetBoolean(),
                    "Integration: loading payload preserves original case and underscore");
            File.WriteAllText(Path.Combine(root, "output_000.log"), timers[0] + "\n");
            File.AppendAllText(application, app[2] + "\n");
            var session = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var sent = await sender.SendLatestAsync(session);
            check(sent.Outcome == SendOutcome.Success && sent.IncludesTimer, "Integration: raw alias and timer reach fake API together");
            using (var json = JsonDocument.Parse(handler.Sent[1].Body))
            {
                var data = json.RootElement;
                check(data.GetProperty("map").GetString() == "Rezerv_Base" && data.GetProperty("raid_ends_at").GetDateTimeOffset() == session.Timer!.EndsAt,
                    "Integration: transmission preserves log map and timer values without client alias resolution");
                check(!handler.Sent[1].Body.Contains(root) && data.GetProperty("session_id").GetString()?.Length == 64,
                    "Integration: local execution path is hashed before transmission");
                using var loading = JsonDocument.Parse(handler.Sent[0].Body);
                check(loading.RootElement.GetProperty("session_id").GetString() == data.GetProperty("session_id").GetString(),
                    "Integration: loading and active state share the same identity");
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
        finally { Directory.Delete(root, true); }
    }
}
