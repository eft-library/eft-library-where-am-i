using System.Net;
using System.Text.Json;
using where_am_i.Services;

internal static class VersionChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    }
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://example.invalid") };
    private static Dictionary<string, object?> Release(string current = "1.0.0.0", string latest = "1.1.0", string minimum = "1.0.0", bool available = true, bool required = false) => new()
    {
        ["current_version"] = current, ["latest_version"] = latest, ["min_supported_version"] = minimum,
        ["update_available"] = available, ["update_required"] = required,
        ["download_url"] = "https://eftlibrary.com/downloads/where-am-i", ["release_notes"] = "타이머 개선"
    };
    private static HttpResponseMessage Response(object? data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { status = 200, msg = "OK", data }))
    };

    public static async Task Run(Action<bool, string> check)
    {
        var clock = new Clock();
        var count = 0;
        using (var client = Client(new Handler(async (request, token) =>
        {
            count++;
            check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == VersionCheckService.Endpoint,
                "Version check uses documented proposed endpoint");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            check(body.RootElement.EnumerateObject().Count() == 5 && !body.RootElement.TryGetProperty("email", out _),
                "Version request has only product/version/platform/architecture/channel");
            check(body.RootElement.GetProperty("current_version").GetString() == "1.0.0.0" &&
                body.RootElement.GetProperty("platform").GetString() == "windows" &&
                body.RootElement.GetProperty("architecture").GetString() == "x64" &&
                body.RootElement.GetProperty("channel").GetString() == "stable" &&
                body.RootElement.GetProperty("product").GetString() == "where-am-i", "Windows deployment identity preserved");
            return Response(Release());
        })))
        {
            var result = await new VersionCheckService(client, clock).CheckAsync(new("1.0.0.0"));
            check(result.Outcome == VersionCheckOutcome.UpdateAvailable && count == 1, "Optional update detected without retries");
            check(result.Release?.LatestVersion == "1.1.0" && result.Release.ReleaseNotes == "타이머 개선", "Release information retained");
            check(result.CheckedAt == clock.GetUtcNow() && result.Release?.DownloadUrl?.Host == "eftlibrary.com", "Check time and approved download available");
        }
        foreach (var (current, payload, expected) in new[]
        {
            ("1.0.0.0", Release(latest:"1.0.0", minimum:"1.0", available:false), VersionCheckOutcome.UpToDate),
            ("1.9.0.0", Release(current:"1.9.0.0", latest:"1.10.0"), VersionCheckOutcome.UpdateAvailable),
            ("1.0.0.0", Release(latest:"2.0.0", minimum:"1.5.0", required:true), VersionCheckOutcome.UpdateRequired),
            ("2.0.0.0", Release(current:"2.0.0.0", latest:"1.5.0", available:false), VersionCheckOutcome.UpToDate)
        })
        {
            using var client = Client(new Handler((_, _) => Task.FromResult(Response(payload))));
            check((await new VersionCheckService(client).CheckAsync(new(current))).Outcome == expected,
                "Numeric version comparison and current/optional/required states");
        }
        var invalid = new List<object?> { null, true, new object[] { }, new { }, "not a release" };
        void Changed(string key, object? value) { var payload = Release(); payload[key] = value; invalid.Add(payload); }
        Changed("current_version", "9.0.0");
        Changed("latest_version", "1.1.0-beta");
        Changed("latest_version", "1.1.0+build");
        Changed("latest_version", "abc");
        Changed("latest_version", "999999999999999999.0");
        Changed("min_supported_version", "3.0.0");
        Changed("update_available", false);
        Changed("update_available", "true");
        Changed("update_required", true);
        Changed("release_notes", new { html = "bad shape" });
        Changed("release_notes", new string('x', 4001));
        Changed("download_url", 123);
        Changed("download_url", null);
        foreach (var url in new[] { "http://eftlibrary.com/download", "https://example.invalid/download", "https://eftlibrary.com.evil.invalid/",
                                    "https://evil.invalid/eftlibrary.com", "https://user:password@eftlibrary.com/download", "https://eftlibrary.com:8443/",
                                    "//eftlibrary.com/download", "file:///tmp/app.exe", "javascript:alert(1)" })
            Changed("download_url", url);
        var inconsistent = Release(minimum:"1.0.1", required:false);
        invalid.Add(inconsistent);
        foreach (var payload in invalid)
        {
            using var client = Client(new Handler((_, _) => Task.FromResult(Response(payload))));
            var result = await new VersionCheckService(client).CheckAsync(new("1.0.0.0"));
            check(result.Outcome == VersionCheckOutcome.Failed && result.Release is null,
                "Invalid or contradictory response cannot claim latest/enable download");
        }
        foreach (var body in new[] { "not-json", "[]", "{\"data\":false}", "{}" })
        {
            using var client = Client(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(body) })));
            check((await new VersionCheckService(client).CheckAsync(new("1.0.0.0"))).Outcome == VersionCheckOutcome.Failed,
                "Malformed envelope is a failure, not up-to-date");
        }
        foreach (var code in new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable, HttpStatusCode.TooManyRequests })
        {
            var calls = 0;
            using var client = Client(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(code)); }));
            var result = await new VersionCheckService(client).CheckAsync(new("1.0.0.0"));
            check(result.Outcome == VersionCheckOutcome.Failed && result.StatusCode == code && calls == 1,
                "Missing/unavailable API reports failure with no blocking retry loop");
        }
        foreach (var exception in new Exception[] { new HttpRequestException("offline"), new TaskCanceledException("timeout") })
        {
            using var client = Client(new Handler((_, _) => throw exception));
            check((await new VersionCheckService(client).CheckAsync(new("1.0.0.0"))).Outcome == VersionCheckOutcome.Failed,
                "Network and timeout failures remain unknown");
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callsAfterCancel = 0;
        using (var cancellation = new CancellationTokenSource())
        using (var client = Client(new Handler(async (_, token) =>
        {
            if (++callsAfterCancel == 1) { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            return Response(Release());
        })))
        {
            var service = new VersionCheckService(client);
            var pending = service.CheckAsync(new("1.0.0.0"), cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            check((await pending).Outcome == VersionCheckOutcome.Canceled, "App exit cancels version check");
            check((await service.CheckAsync(new("1.0.0.0"))).Outcome == VersionCheckOutcome.UpdateAvailable,
                "Later recheck works after cancellation");
        }
        using (var client = Client(new Handler((_, _) => throw new Exception("Invalid request must not reach HTTP"))))
            check((await new VersionCheckService(client).CheckAsync(new("not a version"))).Outcome == VersionCheckOutcome.Failed,
                "Unsupported current version never sent");
        check(VersionCheckService.TryOfficialDownload("https://www.eftlibrary.com/download", out _), "Official www download allowed");
    }
}
