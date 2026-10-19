using System.Net;
using System.Text.Json;
using where_am_i.Models;
using where_am_i.Services;

internal static class TransmissionChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static HttpResponseMessage Accepted() => new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":200,\"msg\":\"OK\",\"data\":true}") };
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://example.invalid") };
    private static Task NoDelay(TimeSpan _, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public static async Task Run(string fixtures, TimeZoneInfo zone, Action<bool, string> check)
    {
        var tracker = new TarkovRaidSessionTracker(@"C:\private-user\EFT\Logs\execution-a", zone);
        var app = File.ReadAllLines(Path.Combine(fixtures, "customs-woods/application_000.log"));
        var output = File.ReadAllLines(Path.Combine(fixtures, "customs-woods/output_000.log"));
        foreach (var line in app[..2]) tracker.AppendApplicationLine(line);
        tracker.AppendOutputLine(output[0]);
        var customs = tracker.Current;
        foreach (var line in app[2..]) tracker.AppendApplicationLine(line);
        var woodsNoTimer = tracker.Current;
        tracker.AppendOutputLine(output[1]);
        var woods = tracker.Current;
        var dto = SendRaidStateRequest.FromSession("player@example.com", woods);
        var noTimerDto = SendRaidStateRequest.FromSession("player@example.com", woodsNoTimer);
        check(dto.SessionId == noTimerDto.SessionId, "Late timer preserves session identity");
        check(dto.SessionId != SendRaidStateRequest.FromSession("player@example.com", customs).SessionId, "Map transitions use new identity");
        check(dto.SessionId?.Length == 64, "Opaque identity is hashed");
        check(dto.TimerObservedAt?.Offset == TimeSpan.Zero && dto.RaidEndsAt?.Offset == TimeSpan.Zero, "Wire timer uses UTC");
        check((dto.RaidEndsAt - dto.TimerObservedAt)?.TotalMinutes == 35, "Wire timer preserves actual Woods limit");
        var anotherExecution = woods with { SourceSession = "execution-b" };
        check(SendRaidStateRequest.FromSession("player@example.com", anotherExecution).SessionId != dto.SessionId, "Execution identity isolated");
        var repeated = woods with { LoadingAt = woods.LoadingAt!.Value.AddMinutes(40), StartedAt = woods.StartedAt!.Value.AddMinutes(40) };
        check(SendRaidStateRequest.FromSession("player@example.com", repeated).SessionId != dto.SessionId, "Same-map reentry has new identity");
        var loading = woods with { StartedAt = null };
        check(SendRaidStateRequest.FromSession("player@example.com", loading).TimerObservedAt is null, "No timer transmission before game start");
        check(SendRaidStateRequest.FromSession("player@example.com", woods with { Map = null }).RaidEndsAt is null, "Unknown map cannot carry timer");
        var body = JsonSerializer.Serialize(dto);
        check(!body.Contains("private-user") && !body.Contains("SourceSession"), "Request contains no local path");
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(noTimerDto)))
        {
            check(document.RootElement.GetProperty("timer_observed_at").ValueKind == JsonValueKind.Null, "Missing observation explicitly null");
            check(document.RootElement.GetProperty("raid_ends_at").ValueKind == JsonValueKind.Null, "Missing end explicitly null");
        }

        var calls = new List<string>();
        using (var client = Client(new Handler(async (request, _) =>
        {
            check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == RaidSessionSender.Endpoint, "Uses existing raid endpoint");
            check(request.Content!.Headers.ContentType?.MediaType == "application/json", "JSON content type");
            calls.Add(await request.Content.ReadAsStringAsync());
            return Accepted();
        })))
        {
            var sender = new RaidSessionSender(client, "player@example.com", NoDelay);
            foreach (var session in new[] { customs, woodsNoTimer, woods })
                check((await sender.SendLatestAsync(session)).Outcome == SendOutcome.Success, "Snapshot POST completed");
            using var sent = JsonDocument.Parse(calls[2]);
            check(sent.RootElement.GetProperty("map").GetString() == "woods", "Woods sent");
            check(sent.RootElement.GetProperty("email").GetString() == "player@example.com", "Existing email field preserved");
            check(sent.RootElement.GetProperty("is_active").GetBoolean(), "Existing active field preserved");
            check(sent.RootElement.GetProperty("transit_count").GetInt32() == 0, "Existing transit compatibility preserved");
            check(sent.RootElement.GetProperty("started_at").GetDateTimeOffset() == woods.StartedAt, "Existing start preserved");
            check(sent.RootElement.GetProperty("timer_observed_at").GetDateTimeOffset() == woods.Timer!.ObservedAt, "Observation sent");
            check(sent.RootElement.GetProperty("raid_ends_at").GetDateTimeOffset() == woods.Timer!.EndsAt, "Escape time sent");
        }

        // One HTTP request at a time: intermediate pending snapshots are discarded.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<string>();
        using (var client = Client(new Handler(async (request, token) =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync(token));
            if (requests.Count == 1)
            {
                started.SetResult();
                await release.Task.WaitAsync(token);
            }
            return Accepted();
        })))
        {
            var sender = new RaidSessionSender(client, "player@example.com", NoDelay);
            var first = sender.SendLatestAsync(customs);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var intermediate = sender.SendLatestAsync(woodsNoTimer);
            var last = sender.SendLatestAsync(woods);
            check(requests.Count == 1, "New snapshots wait for in-flight HTTP");
            release.SetResult();
            check((await first).Outcome == SendOutcome.Superseded, "Old success cannot overwrite current UI state");
            check((await intermediate).Outcome == SendOutcome.Superseded, "Intermediate pending snapshot dropped");
            check((await last).Outcome == SendOutcome.Success && requests.Count == 2, "Only latest pending snapshot sent");
        }

        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity,
                                      HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests, HttpStatusCode.RequestTimeout })
        {
            var count = 0;
            using var client = Client(new Handler((_, _) =>
            {
                count++;
                return Task.FromResult(new HttpResponseMessage(status));
            }));
            var result = await new RaidSessionSender(client, "player@example.com", NoDelay).SendLatestAsync(woods);
            var expected = (int)status >= 500 || status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout ? 3 : 1;
            check(result.Outcome == SendOutcome.Failed && result.StatusCode == status && result.Attempts == expected && count == expected,
                $"Bounded retry policy {status}");
        }
        var attempt = 0;
        using (var client = Client(new Handler((_, _) =>
        {
            attempt++;
            if (attempt == 1) throw new HttpRequestException("offline");
            if (attempt == 2) throw new TaskCanceledException("timeout");
            return Task.FromResult(Accepted());
        })))
        {
            var result = await new RaidSessionSender(client, "player@example.com", NoDelay).SendLatestAsync(woods);
            check(result.Outcome == SendOutcome.Success && result.Attempts == 3, "Network failure and timeout recover within retry bound");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            using var client = Client(new Handler(async (_, token) =>
            {
                if (++count == 1)
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return Accepted();
            }));
            var sender = new RaidSessionSender(client, "player@example.com", NoDelay);
            var sending = sender.SendLatestAsync(customs, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            check((await sending).Outcome == SendOutcome.Canceled, "Stop cancels in-flight HTTP");
            check((await sender.SendLatestAsync(woods)).Outcome == SendOutcome.Success && count == 2,
                "Sender lock is released after cancellation for resumed monitoring");
        }
        foreach (var responseBody in new[] { "{\"data\":false}", "{\"data\":null}", "{}", "true", "not-json", "" })
        {
            var count = 0;
            using var client = Client(new Handler((_, _) =>
            {
                count++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody) });
            }));
            var result = await new RaidSessionSender(client, "player@example.com", NoDelay).SendLatestAsync(woods);
            check(result.Outcome == SendOutcome.Rejected && count == 1, "HTTP 200 without data:true is not success");
        }
        using (var canceled = new CancellationTokenSource())
        using (var client = Client(new Handler((_, _) => throw new Exception("Canceled work must never reach HTTP"))))
        {
            canceled.Cancel();
            var result = await new RaidSessionSender(client, "player@example.com", NoDelay).SendLatestAsync(woods, canceled.Token);
            check(result.Outcome == SendOutcome.Canceled && result.Attempts == 0, "Canceled queued send does not POST");
        }
        using (var canceled = new CancellationTokenSource())
        using (var client = Client(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))))
        {
            var sender = new RaidSessionSender(client, "player@example.com", (_, token) =>
            {
                canceled.Cancel();
                return Task.Delay(1, token);
            });
            var result = await sender.SendLatestAsync(woods, canceled.Token);
            check(result.Outcome == SendOutcome.Canceled && result.Attempts == 1, "Stop cancels retry delay");
        }
    }
}
