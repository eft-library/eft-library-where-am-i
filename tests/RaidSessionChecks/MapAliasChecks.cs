using System.Net;
using System.Text;
using where_am_i.Services;

static class MapAliasChecks
{
    sealed class Handler : HttpMessageHandler
    {
        public string Body = "";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public HttpRequestMessage? Request;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "eft-alias-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "map-aliases.json");
        var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        var service = new MapAliasService(client, cache);
        string Scene(string alias) => $"rcid:{alias}.scenespreset";
        try
        {
            check(!service.LoadCache() && TarkovLogParser.FindMap(Scene("bigmap")) == "customs", "Missing alias cache keeps built-ins");
            handler.Body = """{"data":{"version":"v1","aliases":[{"alias":" NEW_WOODS_preset ","map":"woods"}]}}""";
            var result = await service.RefreshAsync();
            check(result.Updated && result.CacheSaved, "Alias snapshot applied and persisted");
            check(handler.Request?.Method == HttpMethod.Get && handler.Request.RequestUri?.AbsolutePath == MapAliasService.Endpoint && handler.Request.Content is null, "Alias request has no user data");
            check(TarkovLogParser.FindMap(Scene("new_woods")) == "woods", "Remote aliases normalize case and preset");
            check(TarkovLogParser.FindMap(Scene("bigmap")) == "customs", "Partial remote list retains built-ins");
            TarkovLogParser.SetRemoteAliases(new Dictionary<string, string>());
            check(service.LoadCache() && TarkovLogParser.FindMap(Scene("new_woods")) == "woods", "Offline cache restores aliases");
            var goodCache = File.ReadAllText(cache);
            foreach (var body in new[] {
                "not json", "{}", "{\"data\":null}",
                """{"data":{"version":"v2","aliases":[]}}""",
                """{"data":{"version":"v2","aliases":[{"alias":"x","map":"../evil"}]}}""",
                """{"data":{"version":"v2","aliases":[{"alias":"x","map":"woods"},{"alias":"X_preset","map":"customs"}]}}""",
                """{"data":{"version":"v2","aliases":[null]}}""",
                """{"data":{"version":"v2","aliases":[{"alias":"bad/path","map":"woods"}]}}""",
                new string('x', 262145) })
            {
                handler.Body = body;
                result = await service.RefreshAsync();
                check(!result.Updated && TarkovLogParser.FindMap(Scene("new_woods")) == "woods" && File.ReadAllText(cache) == goodCache, "Invalid response preserves last good alias snapshot");
            }
            handler.Status = HttpStatusCode.NotFound;
            check(!(await service.RefreshAsync()).Updated && TarkovLogParser.FindMap(Scene("new_woods")) == "woods", "Missing backend preserves cache");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            check(!(await service.RefreshAsync(canceled.Token)).Updated, "Canceled aliases do not apply");
            handler.Status = HttpStatusCode.OK;
            handler.Body = """{"data":{"version":"v2","aliases":[{"alias":"bigmap","map":"woods"}]}}""";
            check((await service.RefreshAsync()).Updated && TarkovLogParser.FindMap(Scene("bigmap")) == "woods" && TarkovLogParser.FindMap(Scene("new_woods")) is null, "New snapshot overrides built-in and removes old remote rows");
            File.WriteAllText(cache, "broken");
            check(!service.LoadCache() && TarkovLogParser.FindMap(Scene("bigmap")) == "woods", "Corrupt cache leaves active snapshot intact");
            var blocked = Path.Combine(root, "blocked");
            File.WriteAllText(blocked, "file");
            var unsaved = await new MapAliasService(client, Path.Combine(blocked, "cache.json")).RefreshAsync();
            check(unsaved.Updated && !unsaved.CacheSaved, "Cache write failure still applies valid server aliases");
        }
        finally
        {
            TarkovLogParser.SetRemoteAliases(new Dictionary<string, string>());
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
