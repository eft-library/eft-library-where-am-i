using System.Text.Json.Serialization;

namespace where_am_i.Models
{
    public sealed class LiveMapDetail
    {
        [JsonPropertyName("map_selector")]
        public List<LiveMapSelectorEntry> MapSelector { get; init; } = [];

        [JsonPropertyName("floors")]
        public List<LiveMapFloor> Floors { get; init; } = [];
    }

    public sealed class LiveMapSelectorEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("normalized_name")]
        public string NormalizedName { get; init; } = "";

        [JsonPropertyName("name_ko")]
        public string NameKo { get; init; } = "";

        [JsonPropertyName("name_en")]
        public string NameEn { get; init; } = "";

        public string DisplayName => string.IsNullOrWhiteSpace(NameKo) ? NameEn : NameKo;
    }

    public sealed class LiveMapFloor
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("floor_no")]
        public int FloorNo { get; init; }

        [JsonPropertyName("name_ko")]
        public string? NameKo { get; init; }

        [JsonPropertyName("name_en")]
        public string? NameEn { get; init; }

        [JsonPropertyName("image")]
        public string? Image { get; init; }

        [JsonPropertyName("min_y")]
        public double? MinY { get; init; }

        [JsonPropertyName("max_y")]
        public double? MaxY { get; init; }

        [JsonPropertyName("map_bounds")]
        public double[][]? MapBounds { get; init; }

        [JsonPropertyName("zones")]
        public List<LiveMapFloorZone> Zones { get; init; } = [];

        public string DisplayName => NameKo ?? NameEn ?? $"{FloorNo}층";
    }

    public sealed class LiveMapFloorZone
    {
        [JsonPropertyName("area_x_min")]
        public double AreaXMin { get; init; }

        [JsonPropertyName("area_x_max")]
        public double AreaXMax { get; init; }

        [JsonPropertyName("area_z_min")]
        public double AreaZMin { get; init; }

        [JsonPropertyName("area_z_max")]
        public double AreaZMax { get; init; }

        [JsonPropertyName("override_min_y")]
        public double? OverrideMinY { get; init; }

        [JsonPropertyName("override_max_y")]
        public double? OverrideMaxY { get; init; }
    }

    public sealed record PlayerLocation(double X, double Y, double Z, double Yaw);
}
