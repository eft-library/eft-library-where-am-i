using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using where_am_i.Models;

namespace where_am_i.Services
{
    public class UserService
    {
        private static readonly HttpClient httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://back.eftlibrary.com")
        };

        public async Task<bool> CheckUserAsync(string email)
        {
            var request = new CheckUserRequest
            {
                email = email
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("/api/where-am-i/check-wpf-user", content);

            if (!response.IsSuccessStatusCode)
                return false; // 서버 상태 코드가 200이 아닌 경우 실패 처리

            // 응답 JSON 파싱
            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);

            // "data" 속성 확인
            if (doc.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.ValueKind == JsonValueKind.True)
            {
                return true; // data가 true면 확인 성공
            }

            return false; // data가 false거나 없으면 실패
        }

    }
}
