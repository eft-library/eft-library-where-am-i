using System.Net;
using System.Net.Http;
using where_am_i.Models;

namespace where_am_i.Services
{
    public sealed record RaidSendResult(
        SendOutcome Outcome, bool IncludesTimer, int Attempts, HttpStatusCode? StatusCode = null);

    public sealed class RaidSessionSender
    {
        public const string Endpoint = "/api/where-am-i/v3/send-raid-state";
        private readonly LatestJsonSender sender;
        private readonly string email;

        public RaidSessionSender(HttpClient httpClient, string email)
            : this(httpClient, email, Task.Delay) { }

        internal RaidSessionSender(HttpClient httpClient, string email,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            sender = new(httpClient, Endpoint, delay);
            this.email = email;
        }

        public async Task<RaidSendResult> SendLatestAsync(RaidSession session, CancellationToken cancellationToken = default)
        {
            var request = SendRaidStateRequest.FromSession(email, session);
            var version = sender.ReserveLatest();
            var result = await sender.PostLatestAsync(request, version, cancellationToken);
            return new(result.Outcome, request.TimerObservedAt.HasValue, result.Attempts, result.StatusCode);
        }
    }
}
