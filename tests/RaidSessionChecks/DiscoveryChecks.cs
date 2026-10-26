using where_am_i.Services;

static class DiscoveryChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "eft-discovery-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var screenshots = Directory.CreateDirectory(Path.Combine(root, "arbitrary", "captures")).FullName;
            File.WriteAllText(Path.Combine(screenshots, "2026-10-20[13-00]_65.75, -1.12, -28.62_-0.09186, -0.59812, 0.06933, -0.79310_15.47 (0).png"), "image");
            File.WriteAllText(Path.Combine(root, "ordinary.png"), "image");
            var logs = Directory.CreateDirectory(Path.Combine(root, "game", "Logs", "log_2026.10.26_9-00-00_abc")).FullName;
            File.WriteAllText(Path.Combine(logs, "application_000.log"), "log");
            File.WriteAllText(Path.Combine(root, "application_000.log"), "not in a run folder");
            var results = FolderDiscoveryService.Search(new[] { root, root }, null, default);
            check(results.Count == 2, "Discovery deduplicates roots and ignores unrelated files");
            check(results.Any(r => r.Kind == "screenshot" && r.Path == screenshots), "Exact screenshot in arbitrary directory is discovered");
            check(results.Any(r => r.Kind == "log" && r.Path == Directory.GetParent(logs)!.FullName), "Discovery selects stable Logs root rather than individual run");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { FolderDiscoveryService.Search(new[] { root }, null, canceled.Token); check(false, "Discovery cancellation"); }
            catch (OperationCanceledException) { check(true, "Discovery cancellation"); }
            var link = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(link, screenshots);
            check(FolderDiscoveryService.Search(new[] { link }, null, default).Count == 0, "Discovery does not follow links");
            check(FolderDiscoveryService.Search(new[] { Path.Combine(root, "missing") }, null, default).Count == 0, "Missing roots do not fail discovery");
        }
        finally { Directory.Delete(root, true); }
    }
}
