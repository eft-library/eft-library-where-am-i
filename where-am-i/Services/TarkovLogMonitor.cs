using System.IO;
using where_am_i.Models;

namespace where_am_i.Services
{
    public sealed class TarkovLogMonitor : IDisposable
    {
        private readonly System.Threading.Timer timer;
        private readonly SemaphoreSlim pollLock = new(1, 1);
        private readonly Func<LogTargetSelection> selectTarget;
        private volatile bool isRunning;
        private volatile bool isDisposed;
        private int refreshRequested;
        private int runVersion;
        private string? sessionDirectory;
        private TarkovLogSessionReader? sessionReader;
        private RaidSession? lastSession;
        private RaidState? lastState;

        public event EventHandler<RaidState>? StateChanged;
        public event EventHandler<RaidSession>? SessionChanged;
        public event EventHandler<string>? StatusChanged;

        public TarkovLogMonitor() : this(new TarkovLogTargetSelector()) { }

        internal TarkovLogMonitor(TarkovLogTargetSelector selector) : this(selector.Select) { }

        internal TarkovLogMonitor(Func<FileInfo?> findApplicationLog)
            : this(() => new LogTargetSelection(findApplicationLog(), null, false)) { }

        private TarkovLogMonitor(Func<LogTargetSelection> selectTarget)
        {
            this.selectTarget = selectTarget;
            timer = new System.Threading.Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            if (isDisposed || isRunning)
                return;

            isRunning = true;
            Interlocked.Increment(ref runVersion);
            Interlocked.Exchange(ref refreshRequested, 1);
            timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }

        public void Stop()
        {
            isRunning = false;
            Interlocked.Increment(ref runVersion);
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            StatusChanged?.Invoke(this, "로그 감지 중지");
        }

        public void Refresh() => Interlocked.Exchange(ref refreshRequested, 1);

        private async Task PollAsync()
        {
            var version = Volatile.Read(ref runVersion);
            if (!isRunning || isDisposed || !await pollLock.WaitAsync(0))
                return;

            try
            {
                var target = selectTarget();
                var logFile = target.ApplicationLog;
                if (logFile?.DirectoryName is not string directory)
                {
                    sessionReader = null;
                    sessionDirectory = null;
                    if (!isRunning || isDisposed || version != Volatile.Read(ref runVersion)) return;
                    Publish(new RaidSession("", null, null, null, null));
                    StatusChanged?.Invoke(this, target.Error ?? "EFT application 로그를 찾지 못했습니다.");
                    return;
                }

                var refreshed = Interlocked.Exchange(ref refreshRequested, 0) == 1;
                if (refreshed ||
                    sessionReader is null || !string.Equals(directory, sessionDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    sessionReader = new TarkovLogSessionReader(directory);
                    sessionDirectory = directory;
                }
                var session = await sessionReader.ReadAsync();
                if (!isRunning || isDisposed || version != Volatile.Read(ref runVersion)) return;
                Publish(session, refreshed);
                var timerStatus = session.Timer is null ? "타이머 미확보 · 웹 수동 입력 가능" : "타이머 확보";
                StatusChanged?.Invoke(this, $"로그 감지 중 · {(target.IsCustom ? "사용자 지정 경로" : "자동 탐색")} · {timerStatus} · {directory}");
            }
            catch (UnauthorizedAccessException ex)
            {
                if (isRunning && !isDisposed && version == Volatile.Read(ref runVersion))
                    StatusChanged?.Invoke(this, $"로그 폴더 접근 권한이 없습니다: {ex.Message}");
            }
            catch (IOException ex)
            {
                if (isRunning && !isDisposed && version == Volatile.Read(ref runVersion))
                    StatusChanged?.Invoke(this, $"로그를 읽지 못했습니다: {ex.Message}");
            }
            finally
            {
                pollLock.Release();
            }
        }

        private void Publish(RaidSession session, bool force = false)
        {
            if (force || session != lastSession)
            {
                lastSession = session;
                SessionChanged?.Invoke(this, session);
            }
            // SessionChanged drives the UI and request snapshot; StateChanged remains a compatibility event.
            var state = new RaidState(session.Map, session.StartedAt, session.StartedAt.HasValue, 0);
            if (state != lastState)
            {
                lastState = state;
                StateChanged?.Invoke(this, state);
            }
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            isRunning = false;
            Interlocked.Increment(ref runVersion);
            timer.Dispose();
            // An in-flight poll still releases this managed semaphore in its finally block.
        }
    }
}
