using System.Net;
using System.Text.Json;
using where_am_i.Services;

internal static class ScreenshotChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://example.invalid") };
    private static HttpResponseMessage Accepted() => new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":true}") };
    private static Task NoDelay(TimeSpan _, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }

    public static async Task Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "eft-screenshot-check-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string Image(string id, bool ready = true)
        {
            var path = Path.Combine(directory, $"shot_-123.4,5.6,78.9_{id}.png");
            File.WriteAllBytes(path, ready ? [1, 2, 3] : []);
            return path;
        }
        try
        {
            var a = Image("a");
            var b = Image("b");
            var c = Image("c");
            foreach (var valid in new[] { "shot_-1, 2.5, -3_0.PNG", "shot_1,2,3_0.jpg", "shot_1,2,3_0.jpeg" })
                check(ScreenshotLocationSender.IsScreenshot(valid), "Game coordinate filename accepted");
            foreach (var invalid in new[] { "", "screenshot.png", "shot_1,2_0.png", "shot_1,2,3_0.exe" })
                check(!ScreenshotLocationSender.IsScreenshot(invalid), "Unrelated filename excluded");

            var calls = 0;
            var clock = new Clock();
            using (var client = Client(new Handler(async (request, token) =>
            {
                calls++;
                check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == ScreenshotLocationSender.Endpoint,
                    "Existing screenshot endpoint preserved");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                check(body.RootElement.EnumerateObject().Count() == 2 &&
                    body.RootElement.GetProperty("email").GetString() == "player@example.com" &&
                    body.RootElement.GetProperty("location").GetString() == Path.GetFileName(a),
                    "Only email and filename sent; no image binary or full path");
                return Accepted();
            })))
            {
                var service = new ScreenshotLocationSender(client, "player@example.com", NoDelay, clock);
                check((await service.ProcessFileAsync(a)).Outcome == ScreenshotOutcome.Success, "Screenshot acknowledged");
                check((await service.ProcessFileAsync(a)).Outcome == ScreenshotOutcome.Ignored && calls == 1, "Duplicate filesystem event suppressed");
                clock.Now = clock.Now.AddSeconds(4);
                check((await service.ProcessFileAsync(a)).Outcome == ScreenshotOutcome.Success && calls == 2, "Dedup window expires");
                check((await service.ProcessFileAsync(Path.Combine(directory, "unrelated.png"))).Outcome == ScreenshotOutcome.Ignored,
                    "Unrelated files do not reserve or send");
            }

            // Empty files can become ready later; empty does not mean an immediate permanent failure.
            var empty = Image("empty", false);
            var probes = 0;
            calls = 0;
            using (var client = Client(new Handler((_, _) => { calls++; return Task.FromResult(Accepted()); })))
            {
                var service = new ScreenshotLocationSender(client, "player@example.com", (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (++probes == 2) File.WriteAllBytes(empty, [1]);
                    return Task.CompletedTask;
                });
                check((await service.ProcessFileAsync(empty)).Outcome == ScreenshotOutcome.Success && probes == 2 && calls == 1,
                    "Zero-length file waits until a later readiness attempt");
                var unreadable = Image("never-ready", false);
                var exhausted = await new ScreenshotLocationSender(client, "player@example.com", NoDelay).ProcessFileAsync(unreadable);
                check(exhausted.Outcome == ScreenshotOutcome.FileNotReady && calls == 1, "Readiness exhaustion never sends location");
            }

            // Newer image finishes before the older image is ready.
            var slow = Image("slow", false);
            var releaseFile = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var transmitted = new List<string>();
            using (var client = Client(new Handler(async (request, token) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                transmitted.Add(body.RootElement.GetProperty("location").GetString()!);
                return Accepted();
            })))
            {
                var service = new ScreenshotLocationSender(client, "player@example.com", (_, token) => releaseFile.Task.WaitAsync(token));
                var pending = service.ProcessFileAsync(slow);
                check((await service.ProcessFileAsync(b)).Outcome == ScreenshotOutcome.Success, "Newer ready image sends first");
                File.WriteAllBytes(slow, [1]);
                releaseFile.SetResult();
                check((await pending).Outcome == ScreenshotOutcome.Superseded && transmitted.SequenceEqual(new[] { Path.GetFileName(b) }),
                    "Older slow image cannot overtake newer detected location");
            }

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseHttp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            transmitted.Clear();
            using (var client = Client(new Handler(async (request, token) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                transmitted.Add(body.RootElement.GetProperty("location").GetString()!);
                if (transmitted.Count == 1) { entered.SetResult(); await releaseHttp.Task.WaitAsync(token); }
                return Accepted();
            })))
            {
                var service = new ScreenshotLocationSender(client, "player@example.com", NoDelay);
                var first = service.ProcessFileAsync(a);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var middle = service.ProcessFileAsync(b);
                var latest = service.ProcessFileAsync(c);
                check(transmitted.Count == 1, "Only one screenshot HTTP request in flight");
                releaseHttp.SetResult();
                check((await first).Outcome == ScreenshotOutcome.Superseded, "Earlier response cannot mark current location successful");
                check((await middle).Outcome == ScreenshotOutcome.Superseded, "Intermediate pending location discarded");
                check((await latest).Outcome == ScreenshotOutcome.Success &&
                    transmitted.SequenceEqual(new[] { Path.GetFileName(a), Path.GetFileName(c) }), "Latest pending location follows first HTTP completion");
            }

            // A newly detected file invalidates an older retry even while the new file is still being written.
            var replacementFile = Image("replacement", false);
            var allowReplacement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ScreenshotSendResult>? replacement = null;
            ScreenshotLocationSender? replacementService = null;
            calls = 0;
            using (var client = Client(new Handler((_, _) => Task.FromResult(++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Accepted()))))
            {
                replacementService = new ScreenshotLocationSender(client, "player@example.com", (delay, token) =>
                {
                    if (delay == TimeSpan.FromSeconds(1))
                    {
                        replacement = replacementService!.ProcessFileAsync(replacementFile);
                        return Task.CompletedTask;
                    }
                    return allowReplacement.Task.WaitAsync(token);
                });
                var old = await replacementService.ProcessFileAsync(a);
                check(old.Outcome == ScreenshotOutcome.Superseded && calls == 1, "New detection prevents old HTTP retry before new file readiness");
                File.WriteAllBytes(replacementFile, [1]);
                allowReplacement.SetResult();
                check((await replacement!).Outcome == ScreenshotOutcome.Success && calls == 2, "Replacement sends once ready");
            }

            calls = 0;
            var stages = new List<ScreenshotProgress>();
            using (var client = Client(new Handler((_, _) => Task.FromResult(++calls < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Accepted()))))
            {
                var service = new ScreenshotLocationSender(client, "player@example.com", NoDelay);
                service.ProgressChanged += (_, progress) => stages.Add(progress);
                var result = await service.ProcessFileAsync(a);
                check(result.Outcome == ScreenshotOutcome.Success && result.Attempts == 3 && calls == 3, "Transient failure retries same screenshot within bound");
                check(stages.Where(p => p.Stage == ScreenshotStage.Retrying).Select(p => p.Attempt).SequenceEqual(new[] { 2, 3 }), "Retry progress visible");
            }
            foreach (var code in new[] { HttpStatusCode.BadRequest, HttpStatusCode.ServiceUnavailable })
            {
                calls = 0;
                using var client = Client(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(code)); }));
                var result = await new ScreenshotLocationSender(client, "player@example.com", NoDelay).ProcessFileAsync(a);
                check(result.Outcome == ScreenshotOutcome.Failed && calls == (code == HttpStatusCode.BadRequest ? 1 : 3), "Permanent and exhausted failures respect retry policy");
            }
            foreach (var body in new[] { "{\"data\":false}", "{\"data\":null}", "{}", "[]", "not json" })
            {
                using var client = Client(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(body) })));
                check((await new ScreenshotLocationSender(client, "player@example.com", NoDelay).ProcessFileAsync(a)).Outcome == ScreenshotOutcome.Rejected,
                    "HTTP success without acknowledgement is not screenshot success");
            }

            var cancelFile = Image("cancel-file", false);
            using (var cancellation = new CancellationTokenSource())
            using (var client = Client(new Handler((_, _) => throw new Exception("Canceled file must not reach HTTP"))))
            {
                var service = new ScreenshotLocationSender(client, "player@example.com", (_, token) =>
                { cancellation.Cancel(); return Task.Delay(1, token); });
                check((await service.ProcessFileAsync(cancelFile, cancellation.Token)).Outcome == ScreenshotOutcome.Canceled, "Stop cancels readiness wait");
            }
            calls = 0;
            using (var cancellation = new CancellationTokenSource())
            using (var client = Client(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); })))
            {
                var service = new ScreenshotLocationSender(client, "player@example.com", (_, token) =>
                { cancellation.Cancel(); return Task.Delay(1, token); });
                check((await service.ProcessFileAsync(a, cancellation.Token)).Outcome == ScreenshotOutcome.Canceled && calls == 1, "Stop cancels screenshot retry wait");
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
                var service = new ScreenshotLocationSender(client, "player@example.com", NoDelay);
                var active = service.ProcessFileAsync(a, cancellation.Token);
                await httpStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                service.InvalidatePending();
                check((await active).Outcome == ScreenshotOutcome.Canceled, "Stop cancels in-flight screenshot HTTP");
                check((await service.ProcessFileAsync(b)).Outcome == ScreenshotOutcome.Success && calls == 2, "New screenshot sends after monitoring resumes");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
