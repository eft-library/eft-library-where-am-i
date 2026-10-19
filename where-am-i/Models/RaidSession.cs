namespace where_am_i.Models
{
    // Timer values are game-reported UTC; StartedAt is the application log's local time.
    public sealed record RaidTimer(
        DateTimeOffset ObservedAt,
        DateTimeOffset StartedAt,
        DateTimeOffset EndsAt
    )
    {
        public TimeSpan RemainingAtObservation => EndsAt - ObservedAt;
    }

    public sealed record RaidSession(
        string SourceSession,
        string? Map,
        DateTimeOffset? LoadingAt,
        DateTimeOffset? StartedAt,
        RaidTimer? Timer,
        string? RawMap = null
    );
}
