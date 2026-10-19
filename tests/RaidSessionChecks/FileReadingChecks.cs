using System.Text;
using where_am_i.Services;
using where_am_i.Models;

internal static class FileReadingChecks
{
    public static async Task Run(string fixtures, TimeZoneInfo zone, Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "eft-reader-check-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var directory = Path.Combine(root, "session-a");
            Directory.CreateDirectory(directory);
            var app = Path.Combine(directory, "application_000.log");
            var output = Path.Combine(directory, "output_000.log");
            var apps = File.ReadAllLines(Path.Combine(fixtures, "customs-woods/application_000.log"));
            var timers = File.ReadAllLines(Path.Combine(fixtures, "customs-woods/output_000.log"));
            var reader = new TarkovLogSessionReader(directory, zone);
            File.WriteAllText(app, string.Join('\n', apps[..2]) + "\n");
            var session = await reader.ReadAsync();
            check(session.Map == "customs" && session.StartedAt.HasValue && session.Timer is null, "Missing output is allowed");
            File.WriteAllText(output, timers[0] + "\n");
            session = await reader.ReadAsync();
            check(session.Timer?.RemainingAtObservation.TotalMinutes == 35, "Late output creation recovered");
            check(await reader.ReadAsync() == session, "Unchanged polling preserves state");

            File.AppendAllText(app, apps[2] + "\n");
            session = await reader.ReadAsync();
            check(session.Map == "woods" && session.Timer is null && session.StartedAt is null, "New loading clears prior state");
            File.AppendAllText(app, apps[3] + "\n");
            var split = timers[1].Length / 2;
            File.AppendAllText(output, timers[1][..split]);
            session = await reader.ReadAsync();
            check(session.StartedAt.HasValue && session.Timer is null, "Partial timer is not parsed");
            File.AppendAllText(output, timers[1][split..]);
            check((await reader.ReadAsync()).Timer is null, "No newline means incomplete record");
            File.AppendAllText(output, "\r\n");
            session = await reader.ReadAsync();
            check(session.Timer?.RemainingAtObservation.TotalMinutes == 35, "Partial timer completed on next poll");

            // Do not lose initial events as the output grows past the previous 500KB tail.
            File.AppendAllText(output, new string('x', 700_000) + "\nnoise\n");
            check(await reader.ReadAsync() == session, "Huge irrelevant line keeps state");
            check(await new TarkovLogSessionReader(directory, zone).ReadAsync() == session, "Mid-raid recovery scans full existing files");

            File.WriteAllText(output, "");
            session = await reader.ReadAsync();
            check(session.Map == "woods" && session.Timer is null, "Output truncation drops stale timer");
            File.WriteAllText(output, timers[1] + "\n");
            session = await reader.ReadAsync();
            check(session.Timer is not null, "Timer recovered after truncation");
            var changed = timers[1].Replace("00:15:32", "00:20:32");
            File.WriteAllText(output, changed + "\n");
            session = await reader.ReadAsync();
            check(session.Timer?.RemainingAtObservation.TotalMinutes == 40, "Same-size file replacement detected");
            File.Delete(output);
            check((await reader.ReadAsync()).Timer is null, "Deleted output invalidates prior timer");
            File.WriteAllText(Path.Combine(directory, "output_001.log"), timers[1] + "\n");
            check((await reader.ReadAsync()).Timer is not null, "Rotated output recovered in same directory");

            // A transient directory failure must not consume lines or poison subsequent retries.
            Directory.Move(directory, directory + "-moved");
            try { await reader.ReadAsync(); check(false, "Expected directory read failure"); }
            catch (DirectoryNotFoundException) { check(true, "Read failure surfaced"); }
            Directory.Move(directory + "-moved", directory);
            check((await reader.ReadAsync()).Timer is not null, "Retry recovers all sibling files");

            // New execution must never use a sibling directory's output.
            var other = Path.Combine(root, "session-b");
            Directory.CreateDirectory(other);
            var otherApp = Path.Combine(other, "application_000.log");
            File.WriteAllText(otherApp, string.Join('\n', apps[..2]) + "\n");
            check((await new TarkovLogSessionReader(other, zone).ReadAsync()).Timer is null, "Execution directories isolated");

            // UTF-8 and long incomplete lines survive across byte boundaries without data corruption.
            var rawPath = Path.Combine(root, "raw.log");
            var raw = new IncrementalLogReader();
            var utf8 = Encoding.UTF8.GetBytes(apps[0] + " 한글\n");
            File.WriteAllBytes(rawPath, utf8[..^3]);
            check((await raw.ReadAsync(rawPath)).Lines.Count == 0, "Split UTF8 waits for newline");
            using (var stream = new FileStream(rawPath, FileMode.Append)) stream.Write(utf8[^3..]);
            check((await raw.ReadAsync(rawPath)).Lines.Single().EndsWith("한글"), "Split UTF8 reconstructed");

            string? selected = app;
            using var monitor = new TarkovLogMonitor(() => selected is null ? null : new FileInfo(selected));
            var received = new TaskCompletionSource<RaidSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.SessionChanged += (_, value) => received.TrySetResult(value);
            monitor.Start();
            var detected = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(detected.Map == "woods" && detected.Timer is not null, "Monitor publishes timer session");
            received = new(TaskCreationOptions.RunContinuationsAsynchronously);
            File.WriteAllText(Path.Combine(directory, "output_001.log"), changed + "\n");
            detected = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(detected.Timer?.RemainingAtObservation.TotalMinutes == 40,
                "Monitor detects output-only changes without application writes");
            monitor.Stop();
            received = new(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.Start();
            var resumed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(resumed == detected, "Resume republishes unchanged snapshot for transmission retry");
            received = new(TaskCreationOptions.RunContinuationsAsynchronously);
            selected = otherApp;
            monitor.Refresh();
            detected = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(detected.Map == "customs" && detected.Timer is null, "Monitor switches execution and clears timer");
            received = new(TaskCreationOptions.RunContinuationsAsynchronously);
            selected = null;
            detected = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(detected.Map is null && detected.Timer is null, "No application log clears monitor state");
            monitor.Stop();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
