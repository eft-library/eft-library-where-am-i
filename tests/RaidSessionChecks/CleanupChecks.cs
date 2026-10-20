using System.Net;
using where_am_i.Services;

static class CleanupChecks
{
    sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero); }
    sealed class Handler(TaskCompletionSource started, TaskCompletionSource release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":true}") };
        }
    }
    const string Sample = "2026-10-20[13-00]_65.75, -1.12, -28.62_-0.09186, -0.59812, 0.06933, -0.79310_15.47 (0).png";

    public static async Task Run(Action<bool, string> check)
    {
        foreach (var name in new[] {
            Sample,
            "2026-10-20[12-59]_-269.17, -2.54, -352.26_-0.05365, 0.77485, -0.06642, -0.62635_6.92 (0).png",
            "2026-10-20[12-59]_254.85, 0.26, 368.25_-0.03520, 0.85865, -0.05951, -0.50788_18.94 (0).png",
            "2026-10-20[12-59]_16.78, -1.11, -10.20_0.03218, 0.72374, -0.03232, 0.68856_15.47 (0).png",
            "2026-10-20[12-59]_-212.19, 2.42, -89.45_-0.02306, -0.47533, 0.01085, -0.87944_18.92 (0).png",
            "2026-10-20[12-59]_-3.11, -2.37, -31.23_0.04149, -0.83102, 0.06529, 0.55084_15.47 (0).png",
            "2026-10-20[12-58]_-201.17, 2.60, -98.74_0.01957, -0.52721, 0.01214, 0.84943_18.85 (0).png",
            "2026-10-20[12-58]_-13.31, -2.28, -24.76_0.05240, -0.50653, 0.03077, 0.86008_15.47 (0).png",
            "2026-10-20[12-57]_-0.11, 0.92, -21.87_-0.11196, 0.43602, -0.05477, -0.89127_15.47 (0).png",
            "2026-10-20[12-57]_-1.91, -0.93, 15.65_-0.04040, -0.73792, 0.04500, -0.67217_15.47 (0).png",
            "2026-10-20[12-57]_232.35, -62.91, 428.03_-0.05536, 0.79631, -0.07298, -0.59792_6.67 (0).png",
            "2026-10-20[12-56]_-209.24, 2.57, -108.42_0.11016, 0.45575, -0.05647, 0.88146_18.64 (0).png",
            "2026-10-20[12-56]_-204.64, 2.58, -95.52_0.05734, -0.88400, 0.11503, 0.44947_18.59 (0).png",
            "2026-10-20[12-56]_-211.08, 2.63, -109.71_0.00212, 0.99561, -0.07860, 0.05078_18.57 (0).png",
            "2026-10-20[12-56]_-211.13, -5.50, -97.15_-0.02857, -0.42730, 0.01030, -0.90360_18.55 (0).png",
            "2026-10-20[12-56]_-145.26, 16.56, -632.37_-0.00558, -0.99387, 0.07642, -0.07964_6.55 (0).png" })
            check(ScreenshotCleanupService.MatchesFileName(name), "Every user-provided screenshot filename is accepted");
        foreach (var name in new[] { "photo.png", "2026-10-20_1,2,3_test.png", Sample + ".exe", "prefix" + Sample, Sample + "\n",
            Sample.Replace(".png", ".jpg"), Sample.Replace("[13-00]", "[24-00]"), Sample.Replace("2026-10-20", "2026-02-30"),
            Sample.Replace("2026-10-20", "2026-02-29"), Sample.Replace(" (0)", ""), Sample.Replace("65.75, -1.12, -28.62", "65.75, -1.12"),
            Sample.Replace("65.75", "NaN"), Sample.Replace("-0.09186, -0.59812", "-0.09186"), Sample.Replace("15.47 (0)", "15.47 (x)"), "../" + Sample })
            check(!ScreenshotCleanupService.MatchesFileName(name), "Similar, malformed, non-PNG and invalid-date files are excluded");
        check(ScreenshotCleanupService.MatchesFileName(Sample.Replace("2026-10-20", "2024-02-29")), "Valid leap day accepted");

        var root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "eft-cleanup-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "screenshots");
        var trash = Path.Combine(root, "fake-recycle-bin");
        Directory.CreateDirectory(folder); Directory.CreateDirectory(trash);
        var clock = new Clock();
        var activity = new ScreenshotFileActivity();
        var old = clock.GetUtcNow().UtcDateTime.AddDays(-4);
        var calls = new List<string>();
        var service = new ScreenshotCleanupService(path => { calls.Add(path); File.Move(path, Path.Combine(trash, Path.GetFileName(path))); }, activity, clock);
        string Create(string name, DateTime? at = null, byte[]? contents = null, string? directory = null)
        {
            var path = Path.Combine(directory ?? folder, name);
            File.WriteAllBytes(path, contents ?? new byte[] { 1 }); File.SetLastWriteTimeUtc(path, at ?? old); return path;
        }
        string Name(int i) => Sample.Replace("(0)", $"({i})");
        try
        {
            var eligible = Create(Name(0));
            var fresh = Create(Name(1), clock.GetUtcNow().UtcDateTime.AddHours(-1));
            var boundary = Create(Name(2), clock.GetUtcNow().UtcDateTime.AddDays(-3));
            var ordinary = Create("holiday.png");
            var busy = Create(Name(3));
            var empty = Create(Name(4), contents: Array.Empty<byte>());
            var readOnly = Create(Name(5)); File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
            var nestedDirectory = Path.Combine(folder, "nested"); Directory.CreateDirectory(nestedDirectory);
            var nested = Create(Name(6), directory: nestedDirectory);
            using (activity.BeginTransfer(busy))
            {
                var result = await service.RunAsync(folder, 3);
                check(result.Recycled == 1 && calls.SequenceEqual(new[] { eligible }), "Only expired exact-format top-level screenshot is recycled");
                check(new[] { fresh, boundary, ordinary, busy, empty, readOnly, nested }.All(File.Exists), "Recent, boundary, unrelated, active, empty, read-only and nested files remain");
            }
            File.SetAttributes(readOnly, FileAttributes.Normal);
            File.SetLastWriteTimeUtc(readOnly, clock.GetUtcNow().UtcDateTime);
            var result2 = await service.RunAsync(folder, 3);
            check(result2.Recycled == 1 && !File.Exists(busy), "Transfer lease release makes old file eligible on next pass");
            using (var lease = activity.BeginCleanup(fresh))
            { check(lease is not null && activity.BeginTransfer(fresh) is null, "Recycling excludes new app transfer until complete"); }
            using (var lease = activity.BeginTransfer(fresh)) check(lease is not null, "Cleanup lease is released for later transfers");
            var before = calls.Count;
            check((await service.RunAsync(folder, 0)).Error is not null && (await service.RunAsync(folder, 366)).Error is not null && calls.Count == before,
                "Out-of-range settings cannot recycle anything");
            check((await service.RunAsync(Path.GetPathRoot(folder), 3)).Error is not null, "Drive roots cannot be cleaned");
            check((await service.RunAsync(Path.Combine(root, "missing"), 3)).Error is not null, "Missing folder cannot be cleaned");
            check(!ScreenshotCleanupService.TryFolder("relative/path", out _), "Relative cleanup roots rejected");
            var failed = Create(Name(7));
            var failure = await new ScreenshotCleanupService(_ => throw new IOException("No bin"), activity, clock).RunAsync(folder, 3);
            check(failure.Failed == 1 && File.Exists(failed), "Recycle failure never falls back to permanent deletion");
            using (var canceled = new CancellationTokenSource())
            { canceled.Cancel(); check((await service.RunAsync(folder, 3, canceled.Token)).Canceled && File.Exists(failed), "Canceled pass leaves old files untouched"); }
            var second = Create(Name(8));
            using (var stop = new CancellationTokenSource())
            {
                var partial = await new ScreenshotCleanupService(path => { File.Move(path, Path.Combine(trash, Path.GetFileName(path))); stop.Cancel(); }, activity, clock)
                    .RunAsync(folder, 3, stop.Token);
                check(partial.Recycled == 1 && partial.Canceled && (File.Exists(second) || File.Exists(failed)), "Disabling during a pass stops before the next file");
            }
            // Real sender owns a transfer lease through asynchronous HTTP completion.
            var sending = Create(Name(9));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var client = new HttpClient(new Handler(started, release)) { BaseAddress = new Uri("https://test.invalid") })
            {
                var sender = new ScreenshotLocationSender(client, "test@example.invalid", activity);
                var pending = sender.ProcessFileAsync(sending);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await service.RunAsync(folder, 3);
                check(File.Exists(sending), "Real sender HTTP-in-flight file is protected from cleanup");
                release.SetResult(); await pending;
                await service.RunAsync(folder, 3);
                check(!File.Exists(sending), "File becomes eligible after real sender completes");
            }
            var writing = Create(Name(10));
            using (var writer = new FileStream(writing, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                await service.RunAsync(folder, 3);
                check(File.Exists(writing), "An externally locked screenshot is not recycled while being written");
            }
            await service.RunAsync(folder, 3);
            check(!File.Exists(writing), "Closed writer becomes eligible on a later pass");
            var external = Create(Name(11), directory: root);
            var linkedFile = Path.Combine(folder, Name(12));
            File.CreateSymbolicLink(linkedFile, external);
            await service.RunAsync(folder, 3);
            check(File.Exists(external) && File.Exists(linkedFile), "Matching symlink and its external target are left untouched");
            var linkedFolder = Path.Combine(root, "linked-folder");
            Directory.CreateSymbolicLink(linkedFolder, folder);
            check((await service.RunAsync(linkedFolder, 3)).Error is not null, "A cleanup root that is a link is rejected");
            var settings = Path.Combine(root, "settings.json");
            var preferences = new UserPreferencesService(settings);
            check(!preferences.Load().ScreenshotCleanupEnabled && preferences.Load().ScreenshotRetentionDays == 3, "First use defaults off with three-day retention");
            File.WriteAllText(settings, """{"Version":2,"RememberEmail":true,"Email":"test@example.invalid","HasShownWelcome":true} """);
            check(!preferences.Load().ScreenshotCleanupEnabled, "Legacy settings never opt a user into deletion");
            preferences.SaveScreenshotCleanup(true, 3, folder);
            var saved = preferences.Load();
            check(saved.ScreenshotCleanupEnabled && saved.ScreenshotRetentionDays == 3 && saved.ScreenshotCleanupFolder == folder && saved.RememberEmail && saved.HasShownWelcome,
                "Cleanup settings persist without losing existing account/welcome settings");
            preferences.ClearEmail();
            check(preferences.Load().ScreenshotCleanupEnabled, "Email preference changes preserve cleanup settings");
            File.WriteAllText(settings, """{"ScreenshotCleanupEnabled":true,"ScreenshotRetentionDays":0} """);
            check(!preferences.Load().ScreenshotCleanupEnabled && preferences.Load().ScreenshotRetentionDays == 3, "Invalid saved retention fails closed");
            File.WriteAllText(settings, "broken");
            check(!preferences.Load().ScreenshotCleanupEnabled, "Corrupt preferences fail closed");
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
