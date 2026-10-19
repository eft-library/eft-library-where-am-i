using System.Collections.ObjectModel;
using where_am_i.Models;

namespace where_am_i.Services
{
    /// <summary>In-memory history. All mutation belongs to the UI dispatcher.</summary>
    public sealed class ActivityHistory
    {
        private readonly ObservableCollection<ActivityEntry> entries = new();
        private readonly ObservableCollection<ActivityEntry> visibleEntries = new();
        private bool errorsOnly;
        private readonly int capacity;
        private readonly TimeProvider clock;
        public ReadOnlyObservableCollection<ActivityEntry> Entries { get; }
        public ReadOnlyObservableCollection<ActivityEntry> VisibleEntries { get; }

        public void SetErrorsOnly(bool value)
        {
            if (errorsOnly == value) return;
            errorsOnly = value;
            UpdateVisibleEntries();
        }

        public ActivityHistory(int capacity = 100, TimeProvider? clock = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            this.capacity = capacity;
            this.clock = clock ?? TimeProvider.System;
            Entries = new(entries);
            VisibleEntries = new(visibleEntries);
        }

        public void Add(string category, string message, bool isError = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            entries.Insert(0, new(clock.GetUtcNow(), category, message, isError));
            while (entries.Count > capacity) entries.RemoveAt(entries.Count - 1);
            UpdateVisibleEntries();
        }

        private void UpdateVisibleEntries()
        {
            var desired = entries.Where(entry => !errorsOnly || entry.IsError).ToArray();
            // Keep the same bound collection. Never clear/reset it during live updates or filter changes.
            for (var index = 0; index < desired.Length; index++)
            {
                if (index < visibleEntries.Count && ReferenceEquals(visibleEntries[index], desired[index])) continue;
                var existing = -1;
                for (var candidate = index + 1; candidate < visibleEntries.Count; candidate++)
                    if (ReferenceEquals(visibleEntries[candidate], desired[index])) { existing = candidate; break; }
                if (existing >= 0) visibleEntries.Move(existing, index);
                else visibleEntries.Insert(index, desired[index]);
            }
            while (visibleEntries.Count > desired.Length) visibleEntries.RemoveAt(visibleEntries.Count - 1);
        }
    }
}
