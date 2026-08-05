using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using where_am_i.Commands;
using where_am_i.Models;
using where_am_i.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace where_am_i.ViewModels
{
    public class ConfirmedUserViewModel : INotifyPropertyChanged, IDisposable
    {
        private static readonly HttpClient HttpClient = new()
        {
            BaseAddress = new Uri("https://back.eftlibrary.com"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly Regex TarkovScreenshotFileNamePattern = new(
            @"_-?\d+(?:\.\d+)?,\s*-?\d+(?:\.\d+)?,\s*-?\d+(?:\.\d+)?_.*\.(?:png|jpe?g)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100)
        );

        private readonly string email;
        private readonly Dictionary<string, FileSystemWatcher> watchers =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object watcherLock = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> recentlyProcessed =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer pathRefreshTimer;
        private readonly UserPreferencesService preferencesService = new();
        private readonly TarkovLogMonitor logMonitor = new();
        private bool isDisposed;
        private bool isMonitoringEnabled = true;
        private string watcherStatus = "스크린샷 폴더를 찾는 중입니다.";
        private string logStatus = "EFT 로그 폴더를 찾는 중입니다.";
        private string raidStatus = "레이드 대기 중";
        private string lastActivity = "아직 감지된 스크린샷이 없습니다.";

        public ObservableCollection<string> WatchingPaths { get; } = new();

        public string WatcherStatus
        {
            get => watcherStatus;
            private set
            {
                if (watcherStatus == value)
                    return;

                watcherStatus = value;
                OnPropertyChanged();
            }
        }

        public string LastActivity
        {
            get => lastActivity;
            private set
            {
                if (lastActivity == value)
                    return;

                lastActivity = value;
                OnPropertyChanged();
            }
        }

        public bool IsMonitoringEnabled
        {
            get => isMonitoringEnabled;
            set
            {
                if (isMonitoringEnabled == value)
                    return;

                isMonitoringEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MonitoringButtonText));
                ApplyMonitoringState();
            }
        }

        public string MonitoringButtonText => IsMonitoringEnabled ? "감지 중지" : "감지 시작";

        public string LogStatus
        {
            get => logStatus;
            private set { logStatus = value; OnPropertyChanged(); }
        }

        public string RaidStatus
        {
            get => raidStatus;
            private set { raidStatus = value; OnPropertyChanged(); }
        }

        public RelayCommand OpenWebCommand { get; }
        public RelayCommand SelectFolderCommand { get; }
        public RelayCommand SelectLogFolderCommand { get; }
        public RelayCommand ToggleMonitoringCommand { get; }
        public RelayCommand ExitCommand { get; }

        public ConfirmedUserViewModel(string userEmail)
        {
            email = userEmail?.Trim() ?? "";

            OpenWebCommand = new RelayCommand(OpenWeb);
            SelectFolderCommand = new RelayCommand(SelectScreenshotFolder);
            SelectLogFolderCommand = new RelayCommand(SelectLogFolder);
            ToggleMonitoringCommand = new RelayCommand(() => IsMonitoringEnabled = !IsMonitoringEnabled);
            ExitCommand = new RelayCommand(ExitApp);

            var preferences = preferencesService.Load();
            isMonitoringEnabled = preferences.MonitoringEnabled;

            logMonitor.StatusChanged += OnLogStatusChanged;
            logMonitor.StateChanged += OnRaidStateChanged;

            pathRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            pathRefreshTimer.Tick += OnPathRefreshTimerTick;

            ApplyMonitoringState(savePreference: false);

            if (!preferences.HasShownWelcome)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show(
                        "스크린샷과 EFT 로그 폴더를 자동으로 찾습니다.\n\n자동으로 찾지 못한 경우 아래의 폴더 선택 버튼으로 직접 지정해 주세요. 프로그램은 게임 파일을 수정하지 않고 로그와 스크린샷 이름만 읽습니다.",
                        "내 위치 찾기 시작하기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    preferencesService.MarkWelcomeShown();
                });
            }
        }

        private void OpenWeb()
        {
            const string url = "https://eftlibrary.com/live-map/customs";

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"웹 열기 실패: {ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void ExitApp()
        {
            Application.Current.Shutdown();
        }

        private void SelectScreenshotFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Escape from Tarkov 스크린샷 폴더 선택",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var selectedPath = ScreenshotPathService.SaveCustomPath(dialog.FolderName);
                if (IsMonitoringEnabled)
                    AddWatcher(selectedPath);
                UpdateWatcherStatus();
                SetLastActivity($"감시 폴더를 등록했습니다: {selectedPath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"감시 폴더를 등록하지 못했습니다.\n{ex.Message}",
                    "경로 등록 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void SelectLogFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Escape from Tarkov Logs 폴더 선택",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var selectedPath = TarkovLogPathService.SaveCustomPath(dialog.FolderName);
                logMonitor.Refresh();
                LogStatus = $"로그 폴더를 등록했습니다: {selectedPath}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"로그 폴더를 등록하지 못했습니다.\n{ex.Message}",
                    "로그 경로 등록 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void ApplyMonitoringState(bool savePreference = true)
        {
            if (savePreference)
            {
                try { preferencesService.SaveMonitoringEnabled(IsMonitoringEnabled); }
                catch (Exception ex) { SetLastActivity($"감지 설정 저장 오류: {ex.Message}"); }
            }

            if (IsMonitoringEnabled)
            {
                RefreshWatchers();
                pathRefreshTimer.Start();
                logMonitor.Start();
                return;
            }

            pathRefreshTimer.Stop();
            logMonitor.Stop();
            string[] paths;
            lock (watcherLock)
                paths = watchers.Keys.ToArray();
            foreach (var path in paths)
                RemoveWatcher(path);
            WatcherStatus = "스크린샷 감지 중지";
        }

        private void OnLogStatusChanged(object? sender, string status) =>
            RunOnUiThread(() => LogStatus = status);

        private async void OnRaidStateChanged(object? sender, RaidState state)
        {
            RunOnUiThread(() =>
            {
                RaidStatus = state.IsActive
                    ? $"레이드 진행 중 · {state.Map ?? "맵 확인 중"}"
                    : "레이드 대기 중";
            });

            await SendRaidStateAsync(state);
        }

        private async Task SendRaidStateAsync(RaidState state)
        {
            var payload = new
            {
                email,
                map = state.Map,
                started_at = state.StartedAt,
                is_active = state.IsActive,
                transit_count = state.TransitCount
            };

            try
            {
                using var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );
                using var response = await HttpClient.PostAsync(
                    "/api/where-am-i/v3/send-raid-state",
                    content
                );
                if (!response.IsSuccessStatusCode)
                    SetLastActivity($"레이드 상태 전송 실패: {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                SetLastActivity($"레이드 상태 통신 오류: {ex.Message}");
            }
        }

        private void OnPathRefreshTimerTick(object? sender, EventArgs e)
        {
            RefreshWatchers();
        }

        private void RefreshWatchers()
        {
            if (isDisposed)
                return;

            IReadOnlyCollection<string> candidatePaths;
            try
            {
                candidatePaths = ScreenshotPathService.FindCandidatePaths();
            }
            catch (Exception ex)
            {
                SetStatus(
                    "스크린샷 경로를 확인하지 못했습니다.",
                    $"경로 검색 오류: {ex.Message}"
                );
                return;
            }

            foreach (var path in candidatePaths.Where(Directory.Exists))
            {
                AddWatcher(path);
            }

            string[] removedPaths;
            lock (watcherLock)
            {
                removedPaths = watchers.Keys
                    .Where(path => !Directory.Exists(path))
                    .ToArray();
            }

            foreach (var path in removedPaths)
            {
                RemoveWatcher(path);
            }

            UpdateWatcherStatus();
        }

        private void AddWatcher(string path)
        {
            lock (watcherLock)
            {
                if (isDisposed || watchers.ContainsKey(path))
                    return;

                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                        Filter = "*.*"
                    };

                    watcher.Created += OnScreenshotCreated;
                    watcher.Error += OnWatcherError;
                    watcher.EnableRaisingEvents = true;
                    watchers.Add(path, watcher);
                }
                catch (IOException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }

            RunOnUiThread(() =>
            {
                if (!WatchingPaths.Contains(path))
                    WatchingPaths.Add(path);
            });
        }

        private void RemoveWatcher(string path)
        {
            FileSystemWatcher? watcher;

            lock (watcherLock)
            {
                if (!watchers.Remove(path, out watcher))
                    return;
            }

            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnScreenshotCreated;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();

            RunOnUiThread(() => WatchingPaths.Remove(path));
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            if (sender is not FileSystemWatcher watcher)
                return;

            var path = watcher.Path;
            RemoveWatcher(path);

            SetStatus(
                $"감시 오류가 발생했습니다. 경로를 다시 연결합니다: {path}",
                $"감시 오류: {e.GetException().Message}"
            );
        }

        private async void OnScreenshotCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                var fileName = Path.GetFileName(e.FullPath);
                if (!IsTarkovScreenshot(fileName))
                    return;

                var now = DateTimeOffset.UtcNow;
                if (recentlyProcessed.TryGetValue(e.FullPath, out var processedAt) &&
                    now - processedAt < TimeSpan.FromSeconds(3))
                {
                    return;
                }

                recentlyProcessed[e.FullPath] = now;
                RemoveExpiredProcessedEntries(now);

                if (!await WaitUntilFileReadyAsync(e.FullPath))
                {
                    SetLastActivity($"파일 준비 실패: {fileName}");
                    return;
                }

                await SendScreenshotLocationAsync(fileName);
            }
            catch (Exception ex)
            {
                SetLastActivity($"스크린샷 처리 오류: {ex.Message}");
            }
        }

        private static bool IsTarkovScreenshot(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            return TarkovScreenshotFileNamePattern.IsMatch(fileName);
        }

        private static async Task<bool> WaitUntilFileReadyAsync(
            string path,
            int retryCount = 20
        )
        {
            for (var attempt = 0; attempt < retryCount; attempt++)
            {
                try
                {
                    using var stream = File.Open(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read
                    );
                    return stream.Length > 0;
                }
                catch (IOException)
                {
                    await Task.Delay(200);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(200);
                }
            }

            return false;
        }

        private async Task SendScreenshotLocationAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                SetLastActivity("이메일 정보가 없어 위치를 전송하지 못했습니다.");
                return;
            }

            var payload = new
            {
                email,
                location = fileName
            };

            try
            {
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await HttpClient.PostAsync(
                    "/api/where-am-i/send-location",
                    content
                );

                if (!response.IsSuccessStatusCode)
                {
                    SetLastActivity($"서버 전송 실패: {(int)response.StatusCode}");
                    return;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                using var responseDocument = JsonDocument.Parse(responseContent);
                if (!responseDocument.RootElement.TryGetProperty("data", out var dataElement) ||
                    dataElement.ValueKind != JsonValueKind.True)
                {
                    SetLastActivity("서버 전송 실패: 사용자 정보를 확인할 수 없습니다.");
                    return;
                }

                SetLastActivity($"최근 전송 {DateTime.Now:HH:mm:ss} · {fileName}");
            }
            catch (TaskCanceledException)
            {
                SetLastActivity("서버 전송 시간이 초과되었습니다.");
            }
            catch (HttpRequestException ex)
            {
                SetLastActivity($"서버 통신 오류: {ex.Message}");
            }
        }

        private void UpdateWatcherStatus()
        {
            int watcherCount;
            lock (watcherLock)
            {
                watcherCount = watchers.Count;
            }

            var message = watcherCount == 0
                ? "스크린샷 폴더를 찾지 못했습니다. 15초마다 다시 확인합니다."
                : $"스크린샷 감지 중 · {watcherCount}개 경로";

            RunOnUiThread(() => WatcherStatus = message);
        }

        private void SetStatus(string status, string activity)
        {
            RunOnUiThread(() =>
            {
                WatcherStatus = status;
                LastActivity = activity;
            });
        }

        private void SetLastActivity(string message)
        {
            RunOnUiThread(() => LastActivity = message);
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }

        private void RemoveExpiredProcessedEntries(DateTimeOffset now)
        {
            if (recentlyProcessed.Count < 100)
                return;

            foreach (var entry in recentlyProcessed)
            {
                if (now - entry.Value > TimeSpan.FromMinutes(1))
                    recentlyProcessed.TryRemove(entry.Key, out _);
            }
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            pathRefreshTimer.Stop();
            pathRefreshTimer.Tick -= OnPathRefreshTimerTick;
            logMonitor.StatusChanged -= OnLogStatusChanged;
            logMonitor.StateChanged -= OnRaidStateChanged;
            logMonitor.Dispose();

            string[] paths;
            lock (watcherLock)
            {
                paths = watchers.Keys.ToArray();
            }

            foreach (var path in paths)
            {
                RemoveWatcher(path);
            }

            GC.SuppressFinalize(this);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
