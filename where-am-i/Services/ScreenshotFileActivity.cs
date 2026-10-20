namespace where_am_i.Services;

/// <summary>Coordinates this app's transfers and recycling for the same file.</summary>
public sealed class ScreenshotFileActivity
{
    private readonly object gate = new();
    private readonly Dictionary<string, int> leases = new(StringComparer.OrdinalIgnoreCase);
    public IDisposable? BeginTransfer(string path) => Acquire(path, cleanup: false);
    public IDisposable? BeginCleanup(string path) => Acquire(path, cleanup: true);

    private IDisposable? Acquire(string path, bool cleanup)
    {
        var key = System.IO.Path.GetFullPath(path);
        lock (gate)
        {
            leases.TryGetValue(key, out var count);
            if (count < 0 || (cleanup && count > 0)) return null;
            leases[key] = cleanup ? -1 : count + 1;
        }
        return new Lease(() =>
        {
            lock (gate)
            {
                var count = leases[key];
                if (count is -1 or 1) leases.Remove(key);
                else leases[key] = count - 1;
            }
        });
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
