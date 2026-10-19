using System.IO;
using where_am_i.Models;

namespace where_am_i.Services
{
    /// <summary>Owns all relevant files of exactly one execution directory. Not thread-safe.</summary>
    public sealed class TarkovLogSessionReader
    {
        private readonly string directory;
        private readonly TimeZoneInfo? logTimeZone;
        private Dictionary<string, IncrementalLogReader> readers = new(StringComparer.OrdinalIgnoreCase);
        private TarkovRaidSessionTracker tracker;
        private bool recover;

        public TarkovLogSessionReader(string directory, TimeZoneInfo? logTimeZone = null)
        {
            this.directory = Path.GetFullPath(directory);
            this.logTimeZone = logTimeZone;
            tracker = new(this.directory, logTimeZone);
        }

        public async Task<RaidSession> ReadAsync()
        {
            try
            {
                var files = Directory.EnumerateFiles(directory, "*.log")
                    .Where(p => IsApplication(p) || IsOutput(p))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
                if (recover || files.Length != readers.Count || files.Any(p => !readers.ContainsKey(p)))
                    Reset(files);

                var batches = new List<(string Path, IReadOnlyList<string> Lines)>();
                var reset = false;
                foreach (var path in files)
                {
                    var batch = await readers[path].ReadAsync(path);
                    reset |= batch.Reset;
                    batches.Add((path, batch.Lines));
                }
                if (reset)
                {
                    // A rewritten file invalidates correlations with its sibling files as well.
                    Reset(files);
                    batches.Clear();
                    foreach (var path in files)
                        batches.Add((path, (await readers[path].ReadAsync(path)).Lines));
                }
                foreach (var batch in batches)
                    foreach (var line in batch.Lines)
                        if (IsApplication(batch.Path)) tracker.AppendApplicationLine(line);
                        else tracker.AppendOutputLine(line);
                return tracker.Current;
            }
            catch
            {
                // Earlier readers may have advanced before a sibling failed: replay all next time.
                recover = true;
                throw;
            }
        }

        private void Reset(string[] files)
        {
            readers = files.ToDictionary(p => p, _ => new IncrementalLogReader(), StringComparer.OrdinalIgnoreCase);
            tracker = new(directory, logTimeZone);
            recover = false;
        }

        private static bool IsApplication(string path) => Path.GetFileName(path).Contains("application", StringComparison.OrdinalIgnoreCase);
        private static bool IsOutput(string path) => Path.GetFileName(path).Contains("output", StringComparison.OrdinalIgnoreCase);
    }
}
