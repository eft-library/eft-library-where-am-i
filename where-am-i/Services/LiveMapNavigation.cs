namespace where_am_i.Services
{
    public static class LiveMapNavigation
    {
        private static readonly HashSet<string> Maps = new(StringComparer.Ordinal)
        {
            "customs", "woods", "shoreline", "factory", "interchange", "the-lab",
            "the-labyrinth", "lighthouse", "reserve", "streets-of-tarkov", "terminal", "ground-zero"
        };

        public static bool IsKnownMap(string map) => Maps.Contains(map);

        public static string GetUrl(string? map) =>
            map is not null && Maps.Contains(map)
                ? "https://eftlibrary.com/live-map/" + map
                : "https://eftlibrary.com/live-map";
    }
}
