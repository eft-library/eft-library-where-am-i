namespace where_am_i.Models
{
    public sealed record LogLocation(
        double X,
        double Y,
        double Z,
        DateTimeOffset ObservedAt
    );
}
