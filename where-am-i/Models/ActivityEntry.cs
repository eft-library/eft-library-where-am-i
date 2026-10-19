namespace where_am_i.Models
{
    public sealed record ActivityEntry(
        DateTimeOffset RecordedAt, string Category, string Message, bool IsError)
    {
        public string LocalTimeText => RecordedAt.ToLocalTime().ToString("HH:mm:ss");
        public string ResultText => IsError ? "오류" : "안내";
    }
}
