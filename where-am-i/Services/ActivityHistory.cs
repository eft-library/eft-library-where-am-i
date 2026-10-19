using System.Collections.ObjectModel;
using where_am_i.Models;

namespace where_am_i.Services
{
    /// <summary>In-memory history. All mutation belongs to the UI dispatcher.</summary>
    public sealed class ActivityHistory
    {
        private readonly ObservableCollection<ActivityEntry> entries = new();
        private readonly int capacity;
        private readonly TimeProvider clock;
        public ReadOnlyObservableCollection<ActivityEntry> Entries { get; }

        public ActivityHistory(int capacity = 100, TimeProvider? clock = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            this.capacity = capacity;
            this.clock = clock ?? TimeProvider.System;
            Entries = new(entries);
        }

        public void Add(string category, string message, bool isError = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            entries.Insert(0, new(clock.GetUtcNow(), category, message, isError));
            while (entries.Count > capacity) entries.RemoveAt(entries.Count - 1);
        }
    }
}
