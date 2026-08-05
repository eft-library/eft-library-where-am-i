using System.IO;
using where_am_i.Models;

namespace where_am_i.Services
{
    public sealed class TarkovLogMonitor : IDisposable
    {
        private const int TailLength = 500_000;
        private readonly System.Threading.Timer timer;
        private readonly SemaphoreSlim pollLock = new(1, 1);
        private bool isRunning;
        private bool isDisposed;
        private string? lastSignature;
        private RaidState? lastState;

        public event EventHandler<RaidState>? StateChanged;
        public event EventHandler<string>? StatusChanged;

        public TarkovLogMonitor()
        {
            timer = new System.Threading.Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            if (isDisposed || isRunning)
                return;

            isRunning = true;
            timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }

        public void Stop()
        {
            isRunning = false;
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            StatusChanged?.Invoke(this, "로그 감지 중지");
        }

        public void Refresh() => lastSignature = null;

        private async Task PollAsync()
        {
            if (!isRunning || isDisposed || !await pollLock.WaitAsync(0))
                return;

            try
            {
                var logFile = FindLatestApplicationLog();
                if (logFile is null)
                {
                    StatusChanged?.Invoke(this, "EFT application 로그를 찾지 못했습니다.");
                    return;
                }

                var signature = $"{logFile.FullName}|{logFile.Length}|{logFile.LastWriteTimeUtc.Ticks}";
                if (signature == lastSignature)
                    return;

                lastSignature = signature;
                var text = await ReadTailAsync(logFile.FullName);
                var state = TarkovLogParser.Parse(text);
                StatusChanged?.Invoke(this, $"로그 감지 중 · {logFile.DirectoryName}");

                if (state != lastState)
                {
                    lastState = state;
                    StateChanged?.Invoke(this, state);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                StatusChanged?.Invoke(this, $"로그 폴더 접근 권한이 없습니다: {ex.Message}");
            }
            catch (IOException ex)
            {
                StatusChanged?.Invoke(this, $"로그를 읽지 못했습니다: {ex.Message}");
            }
            finally
            {
                pollLock.Release();
            }
        }

        private static FileInfo? FindLatestApplicationLog()
        {
            FileInfo? latest = null;
            foreach (var directory in TarkovLogPathService.FindCandidatePaths().Where(Directory.Exists))
            {
                try
                {
                    foreach (var path in Directory.EnumerateFiles(
                        directory,
                        "application_*.log",
                        SearchOption.AllDirectories
                    ))
                    {
                        var info = new FileInfo(path);
                        if (latest is null || info.LastWriteTimeUtc > latest.LastWriteTimeUtc)
                            latest = info;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            return latest;
        }

        private static async Task<string> ReadTailAsync(string path)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            if (stream.Length > TailLength)
                stream.Seek(-TailLength, SeekOrigin.End);

            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            isRunning = false;
            timer.Dispose();
            pollLock.Dispose();
        }
    }
}
