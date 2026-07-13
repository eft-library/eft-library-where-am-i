using System.Text.Json.Serialization;

namespace where_am_i.Models
{
    public sealed class ApiResponse<T>
    {
        [JsonPropertyName("data")]
        public T? Data { get; init; }
    }
}
