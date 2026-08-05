namespace where_am_i.Models
{
    public sealed record RaidState(
        string? Map,
        DateTimeOffset? StartedAt,
        bool IsActive,
        int TransitCount
    );
}
