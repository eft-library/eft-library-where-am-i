using where_am_i.Models;
using where_am_i.Services;

internal static class LogTargetChecks
{
    public static async Task Run(string fixtures, Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "eft-log-target-check-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        string Session(string logs, string name, string? contents = "")
        {
            var directory = Path.Combine(logs, name);
            Directory.CreateDirectory(directory);
            var app = Path.Combine(directory, "application_000.log");
            if (contents is not null) File.WriteAllText(app, contents);
            return app;
        }
        static string Dir(string file) => Path.GetDirectoryName(file)!;
        try
        {
            var customRoot = Path.Combine(root, "custom", "Logs");
            var autoRoot = Path.Combine(root, "auto", "Logs");
            var auto = Session(autoRoot, "log_2026.10.20_12-00-00_1.2.5");
            var old = Session(customRoot, "log_2026.10.18_1-00-00_1.2.5");
            var current = Session(customRoot, "log_2026.10.19_8-22-41_1.2.5");
            File.SetLastWriteTimeUtc(old, new DateTime(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            string? configured = customRoot;
            var selector = new TarkovLogTargetSelector(() => configured, () => new[] { autoRoot });
            var target = selector.Select();
            check(target.IsCustom && target.ApplicationLog?.FullName == current, "Configured root wins over newer automatic installation and old file mtime");
            File.AppendAllText(old, "touched old log\n");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddYears(50));
            check(selector.Select().ApplicationLog?.FullName == current, "Touching old application cannot switch backwards");
            var newer = Session(customRoot, "log_2026.10.19_9-0-0_1.2.5", null);
            check(selector.Select().ApplicationLog?.FullName == current, "New empty execution directory waits for application file");
            File.WriteAllText(newer, "");
            check(selector.Select().ApplicationLog?.FullName == newer, "New execution application switches even before mtime exceeds copied logs");
            var copiedOld = Session(customRoot, "log_2026.10.17_23-00-00_1.2.5");
            File.SetLastWriteTimeUtc(copiedOld, DateTime.UtcNow.AddYears(60));
            check(selector.Select().ApplicationLog?.FullName == newer, "Newly copied historical execution cannot displace current execution");
            File.Delete(newer);
            check(selector.Select().ApplicationLog is null, "Deleting current execution waits instead of reverting to an older raid");
            configured = customRoot + Path.DirectorySeparatorChar;
            check(selector.Select().ApplicationLog is null, "Trailing root separator does not reset forward-only selection");
            File.WriteAllText(newer, "");
            check(selector.Select().ApplicationLog?.FullName == newer, "Same execution can recover after temporary disappearance");
            check(new TarkovLogTargetSelector(() => customRoot, () => new[] { autoRoot }).Select().ApplicationLog?.FullName == newer,
                "Cold startup ranks execution timestamps rather than modified files");
            var rotated = Path.Combine(Dir(newer), "application_001.log");
            File.WriteAllText(rotated, "rotation\n");
            File.SetLastWriteTimeUtc(rotated, DateTime.UtcNow.AddYears(70));
            check(selector.Select().ApplicationLog?.FullName == rotated, "Application rotation stays inside the same execution");

            configured = Path.Combine(root, "not-created", "Logs");
            check(selector.Select() is { ApplicationLog: null, IsCustom: true }, "Missing configured root never falls back to another install");
            Directory.CreateDirectory(configured);
            check(selector.Select().ApplicationLog is null, "Empty configured root waits rather than mixing automatic logs");
            configured = "invalid\0path";
            check(selector.Select() is { ApplicationLog: null, IsCustom: true, Error: not null }, "Invalid configured path is reported without automatic fallback");
            var unreadable = new TarkovLogTargetSelector(() => throw new UnauthorizedAccessException(), () => new[] { autoRoot });
            check(unreadable.Select() is { ApplicationLog: null, Error: not null }, "Unreadable saved setting does not silently switch installations");
            configured = null;
            check(selector.Select() is { IsCustom: false } && selector.Select().ApplicationLog?.FullName == auto, "No configured root allows automatic discovery");
            check(new TarkovLogTargetSelector(() => null, () => new[] { "bad\0root", autoRoot }).Select().ApplicationLog?.FullName == auto,
                "Malformed automatic candidate does not hide healthy installations");

            var otherRoot = Path.Combine(root, "other", "Logs");
            var other = Session(otherRoot, "log_2020.01.01_1-2-3_1.0.0");
            configured = otherRoot;
            check(selector.Select().ApplicationLog?.FullName == other, "Explicit root change can intentionally select an older execution");
            configured = Dir(other);
            check(selector.Select().RootDirectory == otherRoot, "Execution child under Logs resolves to parent scope");
            var next = Session(otherRoot, "log_2020.01.01_2-2-3_1.0.0");
            check(selector.Select().ApplicationLog?.FullName == next, "Selecting execution child can still discover the next sibling");
            var outsideRoot = Path.Combine(root, "outside", "Logs");
            Session(outsideRoot, "log_2099.01.01_1-00-00_1.0.0");
            Directory.CreateSymbolicLink(Path.Combine(otherRoot, "linked-outside"), outsideRoot);
            check(selector.Select().ApplicationLog?.FullName == next, "Recursive discovery does not follow directory links outside selected root");
            var generic = Path.Combine(root, "replay");
            Directory.CreateDirectory(generic);
            File.WriteAllText(Path.Combine(generic, "application_000.log"), "");
            check(new TarkovLogTargetSelector(() => generic, () => Array.Empty<string>()).Select().ApplicationLog is not null,
                "Nonstandard replay directory still supports fallback selection");

            // Actual monitor follows filesystem discovery, clears old map on new empty execution, then recovers new contents.
            var liveRoot = Path.Combine(root, "live", "Logs");
            var appLines = File.ReadAllLines(Path.Combine(fixtures, "customs-woods/application_000.log"));
            var timerLines = File.ReadAllLines(Path.Combine(fixtures, "customs-woods/output_000.log"));
            var live = Session(liveRoot, "log_2026.10.19_8-22-41_1.2.5", string.Join('\n', appLines[..2]) + "\n");
            File.WriteAllText(Path.Combine(Dir(live), "output_000.log"), timerLines[0] + "\n");
            using var monitor = new TarkovLogMonitor(new TarkovLogTargetSelector(() => liveRoot, () => Array.Empty<string>()));
            var received = new TaskCompletionSource<RaidSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<RaidSession, bool> accept = _ => true;
            monitor.SessionChanged += (_, session) => { if (accept(session)) received.TrySetResult(session); };
            monitor.Start();
            var state = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(state.Map == "customs" && state.Timer is not null, "Real target selection restores initial raid");
            received = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var liveNext = Session(liveRoot, "log_2026.10.19_9-00-00_1.2.5");
            state = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(state.Map is null && state.Timer is null && state.SourceSession == Dir(liveNext), "Automatic execution switch clears old map and timer");
            received = new(TaskCreationOptions.RunContinuationsAsynchronously);
            accept = session => session.Map == "woods" && session.Timer is not null;
            File.WriteAllText(liveNext, string.Join('\n', appLines[2..]) + "\n");
            File.WriteAllText(Path.Combine(Dir(liveNext), "output_000.log"), timerLines[1] + "\n");
            state = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(state.Map == "woods" && state.Timer is not null, "Next execution receives only its own map and timer");
            monitor.Stop();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
