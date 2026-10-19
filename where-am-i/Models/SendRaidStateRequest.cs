using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace where_am_i.Models
{
    public sealed record SendRaidStateRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("map")] string? Map,
        [property: JsonPropertyName("started_at")] DateTimeOffset? StartedAt,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("transit_count")] int TransitCount,
        [property: JsonPropertyName("session_id")] string? SessionId,
        [property: JsonPropertyName("timer_observed_at")] DateTimeOffset? TimerObservedAt,
        [property: JsonPropertyName("raid_ends_at")] DateTimeOffset? RaidEndsAt
    )
    {
        public static SendRaidStateRequest FromSession(string email, RaidSession session)
        {
            var anchor = session.LoadingAt ?? session.StartedAt;
            string? id = null;
            if (!string.IsNullOrWhiteSpace(session.SourceSession) && anchor.HasValue)
            {
                // Stable across polls/restarts, without sending the user's local directory.
                var key = session.SourceSession.ToUpperInvariant() + "\n" +
                    anchor.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            }
            var timer = session.Map is not null && session.StartedAt.HasValue ? session.Timer : null;
            return new(email, session.Map, session.StartedAt, session.StartedAt.HasValue, 0, id,
                timer?.ObservedAt.ToUniversalTime(), timer?.EndsAt.ToUniversalTime());
        }
    }
}
