using System.Net;
using System.Text.Json;
using where_am_i.Services;

internal static class PathReportChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://example.invalid") };
    private static HttpResponseMessage Accepted() => new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":true}") };

    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "eft-path-report-check-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string State(string name) => Path.Combine(root, name + ".json");
        const string original = @"C:\Users\홍길동\OneDrive - player@example.com\Documents\Escape from Tarkov\Screenshots";
        try
        {
            check(ReportedPathPrivacy.Mask(original, null) == @"C:\Users\<user>\OneDrive - <account>\Documents\Escape from Tarkov\Screenshots", "Profile directory and email masked");
            check(ReportedPathPrivacy.Mask(@"D:\Profiles\alice\EFT\Logs", @"D:\Profiles\alice") == @"D:\Profiles\<user>\EFT\Logs", "Moved user profile masked");
            check(ReportedPathPrivacy.Mask(@"C:\Documents and Settings\alice\EFT", null) == @"C:\Documents and Settings\<user>\EFT", "Legacy profile directory masked");
            check(ReportedPathPrivacy.Mask(@"\\server\share\타르코프\Logs", null) == @"\\server\share\타르코프\Logs", "UNC and Unicode path pattern preserved");
            check(ReportedPathPrivacy.Mask(@"C:\", null) == @"C:\", "Drive root preserved");
            check(ReportedPathPrivacy.Mask(@"D:\Profiles\alice-other\EFT", @"D:\Profiles\alice") == @"D:\Profiles\alice-other\EFT", "Profile masking respects segment boundary");
            var calls = 0;
            using (var client = Client(new Handler(async (request, token) =>
            {
                calls++;
                check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == PathReportService.Endpoint, "Existing path-report API used");
                var text = await request.Content!.ReadAsStringAsync(token);
                using var json = JsonDocument.Parse(text);
                var transmittedPath = json.RootElement.GetProperty("path").GetString()!;
                check(!transmittedPath.Contains("홍길동") && !transmittedPath.Contains("player@example.com"), "Request contains no profile name or email");
                check(json.RootElement.EnumerateObject().Count() == 3 && json.RootElement.GetProperty("path_type").GetString() == "screenshot" &&
                    json.RootElement.GetProperty("app_version").GetString() == "1.2.3.4", "Exactly three backend fields, no account identity");
                check(json.RootElement.GetProperty("path").GetString()!.Contains("<user>"), "Masked selected path transmitted");
                return Accepted();
            })))
            {
                var state = State("mask");
                var service = new PathReportService(client, "1.2.3.4", state, @"C:\Users\홍길동");
                check((await service.ReportAsync(PathReportKind.Screenshot, original)).Outcome == PathReportOutcome.Success, "Acknowledged report marked complete");
                check((await service.ReportAsync(PathReportKind.Screenshot, original)).Outcome == PathReportOutcome.AlreadyReported && calls == 1, "Same selected path not repeatedly reported");
                check((await new PathReportService(client, "9.0.0.0", state).ReportAsync(PathReportKind.Screenshot, original)).Outcome == PathReportOutcome.AlreadyReported,
                    "Completion survives new process and app version changes");
                var saved = File.ReadAllText(state);
                check(!saved.Contains("hong") && !saved.Contains("홍길동") && !saved.Contains("Users") && !saved.Contains("@"), "Completion state contains fingerprints only");
                check((await service.ReportAsync(PathReportKind.Log, null)).Outcome == PathReportOutcome.Skipped && calls == 1, "Missing saved selection does not report discovery fallback");
                check((await service.ReportAsync(PathReportKind.Log, " \t")).Outcome == PathReportOutcome.Skipped, "Blank saved selection skipped");
                check((await service.ReportAsync(PathReportKind.Log, "D:\\Game\nLogs")).Outcome == PathReportOutcome.Invalid && calls == 1, "Control characters rejected before HTTP");
                check((await service.ReportAsync(PathReportKind.Log, new string('x', 4097))).Outcome == PathReportOutcome.Invalid, "Oversized path rejected locally");
            }

            // Independent kinds: one failed report must not suppress the other kind or its completion.
            calls = 0;
            using (var client = Client(new Handler(async (request, token) =>
            {
                calls++;
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                return json.RootElement.GetProperty("path_type").GetString() == "screenshot"
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Accepted();
            })))
            {
                var service = new PathReportService(client, "1.0", State("independent"));
                var results = await Task.WhenAll(service.ReportAsync(PathReportKind.Screenshot, @"D:\Shots"),
                    service.ReportAsync(PathReportKind.Log, @"D:\Logs"));
                check(results[0].Outcome == PathReportOutcome.Failed && results[1].Outcome == PathReportOutcome.Success && calls == 2,
                    "Failure in one kind does not block the other or perform immediate retries");
                check((await service.ReportAsync(PathReportKind.Log, @"d:/logs/")).Outcome == PathReportOutcome.AlreadyReported && calls == 2,
                    "Path identity ignores Windows case and trailing separators");
                var restarted = new PathReportService(client, "1.0", State("independent"));
                check((await restarted.ReportAsync(PathReportKind.Screenshot, @"D:\Shots")).Outcome == PathReportOutcome.Failed && calls == 3,
                    "Only unfinished selection is retried after restart");
            }

            // A completion arriving after the user changes A to B can only mark A.
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            calls = 0;
            using (var client = Client(new Handler(async (_, token) =>
            {
                var count = ++calls;
                if (count == 1) { entered.SetResult(); await release.Task.WaitAsync(token); }
                return count == 2 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Accepted();
            })))
            {
                var service = new PathReportService(client, "1.0", State("changed"));
                var old = service.ReportAsync(PathReportKind.Log, @"D:\Old\Logs");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var current = service.ReportAsync(PathReportKind.Log, @"E:\New\Logs");
                release.SetResult();
                check((await old).Outcome == PathReportOutcome.Success && (await current).Outcome == PathReportOutcome.Failed,
                    "Older selection finishes independently from current failure");
                var restarted = new PathReportService(client, "1.0", State("changed"));
                check((await restarted.ReportAsync(PathReportKind.Log, @"D:\Old\Logs")).Outcome == PathReportOutcome.AlreadyReported,
                    "Old successful request marks its own fingerprint");
                check((await restarted.ReportAsync(PathReportKind.Log, @"E:\New\Logs")).Outcome == PathReportOutcome.Success && calls == 3,
                    "New failed path is not accidentally marked by old completion");
                check((await restarted.ReportAsync(PathReportKind.Screenshot, @"E:\New\Logs")).Outcome == PathReportOutcome.Success && calls == 4,
                    "Same folder has independent completion for each kind");
            }

            foreach (var body in new[] { "{}", "{\"data\":false}", "{\"data\":null}", "not-json" })
            {
                calls = 0;
                using var client = Client(new Handler((_, _) =>
                {
                    calls++;
                    return Task.FromResult(calls == 1 ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) } : Accepted());
                }));
                var state = State(Guid.NewGuid().ToString());
                check((await new PathReportService(client, "1.0", state).ReportAsync(PathReportKind.Log, @"D:\Logs")).Outcome == PathReportOutcome.Rejected,
                    "HTTP 200 without data:true is not a completed path report");
                check((await new PathReportService(client, "1.0", state).ReportAsync(PathReportKind.Log, @"D:\Logs")).Outcome == PathReportOutcome.Success && calls == 2,
                    "Logical failure remains pending for future login");
            }
            calls = 0;
            using (var client = Client(new Handler((_, _) => { calls++; return Task.FromResult(Accepted()); })))
            {
                var corrupt = State("corrupt");
                File.WriteAllText(corrupt, "broken json");
                check((await new PathReportService(client, "1.0", corrupt).ReportAsync(PathReportKind.Log, @"D:\Logs")).Outcome == PathReportOutcome.Success,
                    "Corrupt local completion state does not prevent reporting");
                var blocked = Path.Combine(root, "directory-not-file");
                Directory.CreateDirectory(blocked);
                check((await new PathReportService(client, "1.0", blocked).ReportAsync(PathReportKind.Log, @"D:\Logs")).Outcome == PathReportOutcome.StateSaveFailed,
                    "Accepted remote report with failed local marker is not silently marked complete");
                check((await new PathReportService(client, "1.0", blocked).ReportAsync(PathReportKind.Log, @"D:\Logs")).Outcome == PathReportOutcome.StateSaveFailed && calls == 3,
                    "Unpersisted completion safely retries instead of losing the path");
                check(!Directory.EnumerateFiles(root, ".path-report-*.tmp").Any(), "Atomic marker temporary files cleaned up");
            }
            calls = 0;
            var httpStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var cancellation = new CancellationTokenSource())
            using (var client = Client(new Handler(async (_, token) =>
            {
                if (++calls == 1) { httpStarted.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                return Accepted();
            })))
            {
                var state = State("canceled");
                var service = new PathReportService(client, "1.0", state);
                var pending = service.ReportAsync(PathReportKind.Log, @"D:\Logs", cancellation.Token);
                await httpStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                check((await pending).Outcome == PathReportOutcome.Canceled && !File.Exists(state), "Exit cancellation does not persist completion");
                check((await service.ReportAsync(PathReportKind.Log, @"D:\Logs")).Outcome == PathReportOutcome.Success && calls == 2,
                    "Cancellation releases reporter and preserves pending selection");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
