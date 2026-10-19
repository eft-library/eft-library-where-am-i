using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Data;
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

        private static readonly HttpClient VersionHttpClient = new()
        {
            BaseAddress = HttpClient.BaseAddress,
            Timeout = TimeSpan.FromSeconds(10)
        };
        private readonly MapAliasService mapAliases = new(VersionHttpClient,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EFTLibrary", "where-am-i", "map-aliases.json"));
        private readonly VersionCheckService versionChecks = new(VersionHttpClient);
        private bool isCheckingVersion;
        private Uri? updateDownloadUrl;
        private string versionCheckStatus = "버전 확인 대기";
        private string latestVersionText = "최신 버전 미확인";
        private string lastVersionCheckText = "아직 확인하지 않았습니다";
        private string releaseNotes = "";
        public string CurrentAppVersion { get; } = AppVersionInfo.GetCurrentVersion(typeof(App).Assembly);
        public string CurrentVersionText => $"현재 버전 {CurrentAppVersion} · Windows x64";
        public string VersionCheckStatus { get => versionCheckStatus; private set { versionCheckStatus = value; OnPropertyChanged(); } }
        public string LatestVersionText { get => latestVersionText; private set { latestVersionText = value; OnPropertyChanged(); } }
        public string LastVersionCheckText { get => lastVersionCheckText; private set { lastVersionCheckText = value; OnPropertyChanged(); } }
        public string ReleaseNotes { get => releaseNotes; private set { releaseNotes = value; OnPropertyChanged(); } }
        private readonly string email;
        private readonly Dictionary<string, FileSystemWatcher> watchers =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object watcherLock = new();
        private readonly DispatcherTimer pathRefreshTimer;
        private readonly UserPreferencesService preferencesService = new();
        private readonly ActivityHistory activityHistory = new();
        private RaidSession? displayedSession;
        private bool showOnlyErrors;
        private string? currentMap;
        private string currentLogDirectory = "로그 실행 폴더 미확인";
        private string screenshotTransmissionStatus = "스크린샷 위치 전송 대기";
        private readonly TarkovLogMonitor logMonitor = new();
        private readonly RaidSessionSender raidSessionSender;
        private readonly ScreenshotLocationSender screenshotSender;
        private readonly PathReportService pathReports;
        private readonly CancellationTokenSource lifetimeCancellation = new();
        private readonly long[] pathReportRevisions = new long[2];
        private string screenshotPathReportStatus = "스크린샷 지정 경로 수집 대기";
        private string logPathReportStatus = "로그 지정 경로 수집 대기";
        private readonly object monitoringLock = new();
        private CancellationTokenSource monitoringCancellation = new();
        private bool isDisposed;
        private bool isMonitoringEnabled = true;
        private string watcherStatus = "스크린샷 폴더를 찾는 중입니다.";
        private string logStatus = "EFT 로그 폴더를 찾는 중입니다.";
        private string raidStatus = "레이드 대기 중";
        private string raidTimerStatus = "레이드 타이머 미확보";
        private string raidTransmissionStatus = "레이드 정보 전송 대기";
        private long raidTransmissionRevision;
        private string lastActivity = "아직 기록된 활동이 없습니다.";

        public ObservableCollection<string> WatchingPaths { get; } = new();
        public ReadOnlyObservableCollection<ActivityEntry> Activities => activityHistory.Entries;
        public ICollectionView ActivityView { get; }
        public bool HasVisibleActivities => !ActivityView.IsEmpty;
        public bool ShowOnlyErrors
        {
            get => showOnlyErrors;
            set
            {
                if (showOnlyErrors == value) return;
                showOnlyErrors = value;
                OnPropertyChanged();
                ActivityView.Refresh();
                OnPropertyChanged(nameof(HasVisibleActivities));
            }
        }
        public string? CurrentMap
        {
            get => currentMap;
            private set { currentMap = value; OnPropertyChanged(); }
        }
        public string CurrentLogDirectory
        {
            get => currentLogDirectory;
            private set { currentLogDirectory = value; OnPropertyChanged(); }
        }
        public string ScreenshotTransmissionStatus
        {
            get => screenshotTransmissionStatus;
            private set { screenshotTransmissionStatus = value; OnPropertyChanged(); }
        }

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
                OnPropertyChanged(nameof(MonitoringStatusText));
                ApplyMonitoringState();
            }
        }

        public string MonitoringStatusText => IsMonitoringEnabled ? "감지 켜짐" : "감지 중지";

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

        public string RaidTimerStatus
        {
            get => raidTimerStatus;
            private set { raidTimerStatus = value; OnPropertyChanged(); }
        }

        public string RaidTransmissionStatus
        {
            get => raidTransmissionStatus;
            private set { raidTransmissionStatus = value; OnPropertyChanged(); }
        }

        public string ScreenshotPathReportStatus
        {
            get => screenshotPathReportStatus;
            private set { screenshotPathReportStatus = value; OnPropertyChanged(); }
        }

        public string LogPathReportStatus
        {
            get => logPathReportStatus;
            private set { logPathReportStatus = value; OnPropertyChanged(); }
        }

        private bool isVersionInfoVisible;
        public bool IsVersionInfoVisible
        {
            get => isVersionInfoVisible;
            private set { isVersionInfoVisible = value; OnPropertyChanged(); }
        }
        public RelayCommand ToggleVersionInfoCommand { get; }
        public RelayCommand CheckVersionCommand { get; }
        public RelayCommand OpenUpdateDownloadCommand { get; }
        public RelayCommand OpenWebCommand { get; }
        public RelayCommand SelectFolderCommand { get; }
        public RelayCommand SelectLogFolderCommand { get; }
        public RelayCommand ToggleMonitoringCommand { get; }
        public RelayCommand ExitCommand { get; }

        public ConfirmedUserViewModel(string userEmail)
        {
            email = userEmail?.Trim() ?? "";
            raidSessionSender = new RaidSessionSender(HttpClient, email);
            screenshotSender = new ScreenshotLocationSender(HttpClient, email);
            pathReports = new PathReportService(HttpClient, CurrentAppVersion);
            screenshotSender.ProgressChanged += OnScreenshotProgress;
            ActivityView = CollectionViewSource.GetDefaultView(Activities);
            ActivityView.Filter = entry => !ShowOnlyErrors || entry is ActivityEntry { IsError: true };

            ToggleVersionInfoCommand = new RelayCommand(() => IsVersionInfoVisible = !IsVersionInfoVisible);
            CheckVersionCommand = new RelayCommand(() => _ = CheckVersionAsync(), () => !isCheckingVersion && !isDisposed);
            OpenUpdateDownloadCommand = new RelayCommand(OpenUpdateDownload, () => updateDownloadUrl is not null && !isCheckingVersion && !isDisposed);
            OpenWebCommand = new RelayCommand(OpenWeb);
            SelectFolderCommand = new RelayCommand(SelectScreenshotFolder);
            SelectLogFolderCommand = new RelayCommand(SelectLogFolder);
            ToggleMonitoringCommand = new RelayCommand(() => IsMonitoringEnabled = !IsMonitoringEnabled);
            ExitCommand = new RelayCommand(ExitApp);

            var preferences = preferencesService.Load();
            isMonitoringEnabled = preferences.MonitoringEnabled;

            logMonitor.StatusChanged += OnLogStatusChanged;
            logMonitor.SessionChanged += OnRaidSessionChanged;

            pathRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            pathRefreshTimer.Tick += OnPathRefreshTimerTick;

            mapAliases.LoadCache();
            ApplyMonitoringState(savePreference: false);
            _ = RefreshMapAliasesAsync();
            _ = ReportSavedCustomPathsAsync();
            _ = CheckVersionAsync();

            if (!preferences.HasShownWelcome)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (isDisposed) return;
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

        private async Task RefreshMapAliasesAsync()
        {
            var token = lifetimeCancellation.Token;
            var result = await mapAliases.RefreshAsync(token);
            if (isDisposed || token.IsCancellationRequested) return;
            if (result.Updated) logMonitor.Refresh();
            AddActivity("맵 별칭", result.Updated
                ? (result.CacheSaved ? "맵 별칭 갱신 완료" : "맵 별칭 적용 완료 · 캐시 저장 실패")
                : "맵 별칭 조회 실패 · 기존 캐시 또는 내장 별칭 사용", !result.Updated || !result.CacheSaved);
        }

        private async Task CheckVersionAsync()
        {
            if (isDisposed || isCheckingVersion) return;
            isCheckingVersion = true;
            updateDownloadUrl = null;
            VersionCheckStatus = "버전 확인 중";
            LatestVersionText = "최신 버전 미확인";
            ReleaseNotes = "";
            CheckVersionCommand.RaiseCanExecuteChanged();
            OpenUpdateDownloadCommand.RaiseCanExecuteChanged();
            var token = lifetimeCancellation.Token;
            try
            {
                var result = await versionChecks.CheckAsync(new(CurrentAppVersion), token);
                if (isDisposed || token.IsCancellationRequested || result.Outcome == VersionCheckOutcome.Canceled) return;
                LastVersionCheckText = $"마지막 확인 시도 {result.CheckedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
                VersionCheckStatus = result.Outcome switch
                {
                    VersionCheckOutcome.UpToDate => "사용 가능한 업데이트가 없습니다",
                    VersionCheckOutcome.UpdateAvailable => "새 버전이 있습니다 · 다운로드 페이지를 확인하세요",
                    VersionCheckOutcome.UpdateRequired => "업데이트가 필요합니다 · 지원 최소 버전을 확인하세요",
                    _ => "버전 확인 실패 · 다시 확인할 수 있습니다"
                };
                if (result.Release is not null)
                {
                    LatestVersionText = $"최신 {result.Release.LatestVersion} · 지원 최소 {result.Release.MinimumVersion}";
                    ReleaseNotes = result.Release.ReleaseNotes;
                    updateDownloadUrl = result.Outcome is VersionCheckOutcome.UpdateAvailable or VersionCheckOutcome.UpdateRequired
                        ? result.Release.DownloadUrl : null;
                }
                AddActivity("버전 확인", VersionCheckStatus, result.Outcome == VersionCheckOutcome.Failed);
            }
            finally
            {
                isCheckingVersion = false;
                if (!isDisposed)
                {
                    CheckVersionCommand.RaiseCanExecuteChanged();
                    OpenUpdateDownloadCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private void OpenUpdateDownload()
        {
            var uri = updateDownloadUrl;
            if (uri is null || !VersionCheckService.TryOfficialDownload(uri.AbsoluteUri, out _)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri, UseShellExecute = true
                });
            }
            catch (Exception)
            {
                AddActivity("버전 확인", "다운로드 페이지를 열지 못했습니다.", true);
            }
        }

        private void OpenWeb()
        {
            var url = LiveMapNavigation.GetUrl(CurrentMap);

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
                _ = ReportPathAsync(PathReportKind.Screenshot, selectedPath);
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
                _ = ReportPathAsync(PathReportKind.Log, selectedPath);
                logMonitor.Refresh();
                LogStatus = $"로그 폴더를 등록했습니다: {selectedPath}";
                AddActivity("설정", "로그 감시 폴더를 변경했습니다.");
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

        private async Task ReportSavedCustomPathsAsync()
        {
            // These read only user selections (including the legacy screenshot setting), never discovery candidates.
            await Task.WhenAll(
                ReportPathAsync(PathReportKind.Screenshot, ScreenshotPathService.GetSavedCustomPath()),
                ReportPathAsync(PathReportKind.Log, TarkovLogPathService.GetSavedCustomPath()));
        }

        private async Task ReportPathAsync(PathReportKind kind, string? path)
        {
            CancellationToken token;
            lock (monitoringLock)
            {
                if (isDisposed) return;
                token = lifetimeCancellation.Token;
            }
            var index = (int)kind;
            var revision = Interlocked.Increment(ref pathReportRevisions[index]);
            var label = kind == PathReportKind.Screenshot ? "스크린샷" : "로그";
            void Show(string message)
            {
                if (kind == PathReportKind.Screenshot) ScreenshotPathReportStatus = message;
                else LogPathReportStatus = message;
            }
            RunOnUiThread(() =>
            {
                if (!isDisposed && revision == Volatile.Read(ref pathReportRevisions[index]))
                    Show($"{label} 지정 경로 수집 중");
            });
            try
            {
                var result = await pathReports.ReportAsync(kind, path, token);
                RunOnUiThread(() =>
                {
                    if (isDisposed || token.IsCancellationRequested || revision != Volatile.Read(ref pathReportRevisions[index])) return;
                    var message = result.Outcome switch
                    {
                        PathReportOutcome.Skipped => $"{label} 사용자 지정 경로 없음",
                        PathReportOutcome.AlreadyReported => $"{label} 지정 경로 수집 완료",
                        PathReportOutcome.Success => $"{label} 지정 경로 수집 완료",
                        PathReportOutcome.StateSaveFailed => $"{label} 경로 전송 완료 · 완료 상태 저장 실패",
                        PathReportOutcome.Invalid => $"{label} 경로 수집 불가 · 로컬 감지는 유지됩니다",
                        _ => $"{label} 경로 수집 실패 · 다음 실행 시 재시도"
                    };
                    Show(message);
                    if (result.Outcome is not (PathReportOutcome.Skipped or PathReportOutcome.AlreadyReported or PathReportOutcome.Canceled))
                        AddActivity("경로 수집", message, result.Outcome != PathReportOutcome.Success);
                });
            }
            catch (Exception)
            {
                // Keep reporting failures separate from local save/watch outcomes; never display raw report exceptions.
                RunOnUiThread(() =>
                {
                    if (isDisposed || token.IsCancellationRequested || revision != Volatile.Read(ref pathReportRevisions[index])) return;
                    Show($"{label} 경로 수집 실패 · 로컬 감지는 유지됩니다");
                    AddActivity("경로 수집", $"{label} 지정 경로 수집 중 오류가 발생했습니다.", true);
                });
            }
        }

        private void ApplyMonitoringState(bool savePreference = true)
        {
            if (savePreference)
            {
                try { preferencesService.SaveMonitoringEnabled(IsMonitoringEnabled); }
                catch (Exception ex) { SetLastActivity($"감지 설정 저장 오류: {ex.Message}", "설정", true); }
            }

            if (IsMonitoringEnabled)
            {
                lock (monitoringLock)
                {
                    if (monitoringCancellation.IsCancellationRequested)
                    {
                        monitoringCancellation.Dispose();
                        monitoringCancellation = new CancellationTokenSource();
                    }
                }
                RaidStatus = "레이드 정보를 확인하는 중";
                RefreshWatchers();
                pathRefreshTimer.Start();
                logMonitor.Start();
                AddActivity("감지", "스크린샷·로그 감지를 시작했습니다.");
                return;
            }

            pathRefreshTimer.Stop();
            Interlocked.Increment(ref raidTransmissionRevision);
            lock (monitoringLock) monitoringCancellation.Cancel();
            screenshotSender.InvalidatePending();
            ScreenshotTransmissionStatus = "스크린샷 위치 전송 중지";
            RaidTransmissionStatus = "레이드 정보 전송 중지";
            logMonitor.Stop();
            string[] paths;
            lock (watcherLock)
                paths = watchers.Keys.ToArray();
            foreach (var path in paths)
                RemoveWatcher(path);
            WatcherStatus = "스크린샷 감지 중지";
            RaidStatus = "감지 중지 · 마지막 레이드 정보 유지";
            AddActivity("감지", "스크린샷·로그 감지를 중지했습니다.");
        }

        private void OnLogStatusChanged(object? sender, string status) =>
            RunOnUiThread(() =>
            {
                if (isDisposed || LogStatus == status) return;
                LogStatus = status;
                if (status.StartsWith("로그를 읽지 못", StringComparison.Ordinal) ||
                    status.StartsWith("로그 폴더 접근 권한", StringComparison.Ordinal) ||
                    status.StartsWith("EFT application 로그를 찾지 못", StringComparison.Ordinal))
                    AddActivity("로그", status, true);
            });

        private async void OnRaidSessionChanged(object? sender, RaidSession session)
        {
            var revision = Interlocked.Increment(ref raidTransmissionRevision);
            // Show the observed timer as a diagnostic value, not a live countdown.
            var timer = session.Timer;
            RunOnUiThread(() =>
            {
                if (isDisposed) return;
                var previous = displayedSession;
                displayedSession = session;
                CurrentMap = session.Map;
                RaidStatus = !IsMonitoringEnabled ? "감지 중지 · 마지막 레이드 정보 유지"
                    : session.StartedAt.HasValue ? $"레이드 시작 감지 · {session.Map ?? "맵 확인 중"}"
                    : session.Map is null ? "레이드 대기 중" : $"맵 로딩 중 · {session.Map}";
                CurrentLogDirectory = string.IsNullOrWhiteSpace(session.SourceSession)
                    ? "로그 실행 폴더 미확인" : session.SourceSession;
                if (session.Map is not null && (previous is null || previous.SourceSession != session.SourceSession ||
                    previous.LoadingAt != session.LoadingAt || previous.Map != session.Map))
                    AddActivity("레이드", $"맵 로딩 감지 · {session.Map}");
                if (session.StartedAt.HasValue && (previous is null || previous.SourceSession != session.SourceSession ||
                    previous.StartedAt != session.StartedAt))
                    AddActivity("레이드", $"레이드 시작 감지 · {session.Map ?? "맵 확인 중"}");
                if (session.Timer is not null && (previous is null || previous.Timer != session.Timer ||
                    previous.SourceSession != session.SourceSession || previous.LoadingAt != session.LoadingAt))
                    AddActivity("타이머", $"타이머 기준 확보 · {session.Map ?? "맵 확인 중"}");
                RaidTimerStatus = timer is null
                    ? "레이드 타이머 미확보 · 웹에서 수동 입력 가능"
                    : $"레이드 타이머 확보 · 기록 당시 {(int)timer.RemainingAtObservation.TotalMinutes:00}:{timer.RemainingAtObservation.Seconds:00} · " +
                  $"기준 {timer.ObservedAt.ToLocalTime():HH:mm:ss}";
            });

            CancellationToken cancellationToken;
            lock (monitoringLock)
            {
                if (isDisposed || !IsMonitoringEnabled) return;
                cancellationToken = monitoringCancellation.Token;
            }
            RunOnUiThread(() =>
            {
                if (revision == Volatile.Read(ref raidTransmissionRevision) && !cancellationToken.IsCancellationRequested)
                    RaidTransmissionStatus = "레이드 정보 전송 중";
            });
            var result = await raidSessionSender.SendLatestAsync(session, cancellationToken);
            if (isDisposed || cancellationToken.IsCancellationRequested ||
                result.Outcome is SendOutcome.Superseded or SendOutcome.Canceled) return;
            RunOnUiThread(() =>
            {
                if (isDisposed || cancellationToken.IsCancellationRequested ||
                    revision != Volatile.Read(ref raidTransmissionRevision)) return;
                RaidTransmissionStatus = result.Outcome == SendOutcome.Success
                    ? result.IncludesTimer ? "레이드 정보 전송 요청 완료 · 타이머 포함" : "레이드 정보 전송 요청 완료 · 타이머 없음"
                    : result.Outcome == SendOutcome.Rejected
                        ? "서버가 레이드 정보 수신을 확인하지 못했습니다"
                        : result.StatusCode.HasValue
                            ? $"레이드 정보 전송 실패: {(int)result.StatusCode.Value}"
                            : "레이드 정보 전송 실패 · 서버 연결을 확인하세요";
                AddActivity("레이드 전송", RaidTransmissionStatus, result.Outcome != SendOutcome.Success);
            });
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
            CancellationToken token;
            lock (monitoringLock)
            {
                if (isDisposed || !IsMonitoringEnabled) return;
                token = monitoringCancellation.Token;
            }
            try
            {
                var result = await screenshotSender.ProcessFileAsync(e.FullPath, token);
                if (result.Outcome is ScreenshotOutcome.Ignored or ScreenshotOutcome.Canceled or ScreenshotOutcome.Superseded)
                    return;
                RunOnUiThread(() =>
                {
                    if (isDisposed || token.IsCancellationRequested || !screenshotSender.IsCurrent(result.Revision)) return;
                    ScreenshotTransmissionStatus = result.Outcome switch
                    {
                        ScreenshotOutcome.Success => $"스크린샷 전송 완료 {DateTime.Now:HH:mm:ss} · {result.FileName}",
                        ScreenshotOutcome.FileNotReady => $"파일 준비 실패: {result.FileName}",
                        ScreenshotOutcome.Rejected => "스크린샷 전송 실패: 서버가 수신을 확인하지 못했습니다.",
                        _ => result.StatusCode.HasValue
                            ? $"스크린샷 전송 실패: {(int)result.StatusCode.Value}"
                            : "스크린샷 전송 실패 · 서버 연결을 확인하세요"
                    };
                    AddActivity("스크린샷 전송", ScreenshotTransmissionStatus, result.Outcome != ScreenshotOutcome.Success);
                });
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    SetLastActivity($"스크린샷 처리 오류: {ex.Message}", "스크린샷", true);
            }
        }

        private void OnScreenshotProgress(object? sender, ScreenshotProgress progress)
        {
            RunOnUiThread(() =>
            {
                if (isDisposed || !IsMonitoringEnabled || !screenshotSender.IsCurrent(progress.Revision)) return;
                ScreenshotTransmissionStatus = progress.Stage switch
                {
                    ScreenshotStage.WaitingForFile => "스크린샷 저장 완료 대기",
                    ScreenshotStage.WaitingToSend => "스크린샷 위치 전송 대기",
                    ScreenshotStage.Retrying => $"스크린샷 위치 재전송 중 · {progress.Attempt}/3",
                    _ => "스크린샷 위치 전송 중"
                };
            });
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
                AddActivity("스크린샷 감시", activity, true);
            });
        }

        private void SetLastActivity(string message, string category = "앱", bool isError = false)
        {
            RunOnUiThread(() => AddActivity(category, message, isError));
        }

        private void AddActivity(string category, string message, bool isError = false)
        {
            if (isDisposed) return;
            activityHistory.Add(category, message, isError);
            LastActivity = message;
            OnPropertyChanged(nameof(HasVisibleActivities));
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

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            lock (monitoringLock)
            {
                monitoringCancellation.Cancel();
                monitoringCancellation.Dispose();
                lifetimeCancellation.Cancel();
                lifetimeCancellation.Dispose();
            }
            screenshotSender.InvalidatePending();
            screenshotSender.ProgressChanged -= OnScreenshotProgress;
            pathRefreshTimer.Stop();
            pathRefreshTimer.Tick -= OnPathRefreshTimerTick;
            logMonitor.StatusChanged -= OnLogStatusChanged;
            logMonitor.SessionChanged -= OnRaidSessionChanged;
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
