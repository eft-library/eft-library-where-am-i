using System.Net.Http;
using System.Text.Json;
using where_am_i.Models;

namespace where_am_i.Services
{
    public sealed class LiveMapService
    {
        private static readonly HttpClient HttpClient = new()
        {
            BaseAddress = new Uri("https://back.eftlibrary.com"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<LiveMapDetail> GetDetailAsync(
            string normalizedName,
            CancellationToken cancellationToken = default
        )
        {
            var safeName = Uri.EscapeDataString(normalizedName);
            using var response = await HttpClient.GetAsync(
                $"/api/live-map/v3/detail/{safeName}",
                cancellationToken
            );
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var envelope = await JsonSerializer.DeserializeAsync<ApiResponse<LiveMapDetail>>(
                stream,
                JsonOptions,
                cancellationToken
            );

            return envelope?.Data
                ?? throw new InvalidDataException("지도 데이터가 비어 있습니다.");
        }
    }
}
