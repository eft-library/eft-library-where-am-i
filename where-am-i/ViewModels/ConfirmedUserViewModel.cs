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
        private readonly LiveMapService liveMapService = new();
        private readonly Dictionary<string, FileSystemWatcher> watchers =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object watcherLock = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> recentlyProcessed =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer pathRefreshTimer;
        private bool isDisposed;
        private string watcherStatus = "스크린샷 폴더를 찾는 중입니다.";
        private string lastActivity = "아직 감지된 스크린샷이 없습니다.";
        private LiveMapSelectorEntry? selectedMap;
        private LiveMapFloor? selectedFloor;
        private PlayerLocation? playerLocation;
        private CancellationTokenSource? mapLoadCancellation;
        private bool isMapLoading;
        private string mapStatus = "지도 데이터를 불러오는 중입니다.";
        private double mapWidth = 1000;
        private double mapHeight = 700;
        private double playerMarkerLeft;
        private double playerMarkerTop;
        private double playerMarkerRotation;
        private bool isPlayerMarkerVisible;

        public ObservableCollection<string> WatchingPaths { get; } = new();
        public ObservableCollection<LiveMapSelectorEntry> Maps { get; } = new();
        public ObservableCollection<LiveMapFloor> Floors { get; } = new();

        public LiveMapSelectorEntry? SelectedMap
        {
            get => selectedMap;
            set
            {
                if (selectedMap?.NormalizedName == value?.NormalizedName)
                    return;

                selectedMap = value;
                OnPropertyChanged();
                if (value is not null)
                    _ = LoadMapAsync(value.NormalizedName);
            }
        }

        public LiveMapFloor? SelectedFloor
        {
            get => selectedFloor;
            set
            {
                if (selectedFloor?.Id == value?.Id)
                    return;

                selectedFloor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MapImage));
                UpdateMapProjection();
            }
        }

        public Uri? MapImage => Uri.TryCreate(SelectedFloor?.Image, UriKind.Absolute, out var uri)
            ? uri
            : null;

        public string MapStatus
        {
            get => mapStatus;
            private set
            {
                mapStatus = value;
                OnPropertyChanged();
            }
        }

        public bool IsMapLoading
        {
            get => isMapLoading;
            private set
            {
                isMapLoading = value;
                OnPropertyChanged();
            }
        }

        public double MapWidth
        {
            get => mapWidth;
            private set { mapWidth = value; OnPropertyChanged(); }
        }

        public double MapHeight
        {
            get => mapHeight;
            private set { mapHeight = value; OnPropertyChanged(); }
        }

        public double PlayerMarkerLeft
        {
            get => playerMarkerLeft;
            private set { playerMarkerLeft = value; OnPropertyChanged(); }
        }

        public double PlayerMarkerTop
        {
            get => playerMarkerTop;
            private set { playerMarkerTop = value; OnPropertyChanged(); }
        }

        public double PlayerMarkerRotation
        {
            get => playerMarkerRotation;
            private set { playerMarkerRotation = value; OnPropertyChanged(); }
        }

        public bool IsPlayerMarkerVisible
        {
            get => isPlayerMarkerVisible;
            private set { isPlayerMarkerVisible = value; OnPropertyChanged(); }
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

        public RelayCommand SelectFolderCommand { get; }
        public RelayCommand ExitCommand { get; }

        public ConfirmedUserViewModel(string userEmail)
        {
            email = userEmail?.Trim() ?? "";

            SelectFolderCommand = new RelayCommand(SelectScreenshotFolder);
            ExitCommand = new RelayCommand(ExitApp);

            pathRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            pathRefreshTimer.Tick += OnPathRefreshTimerTick;

            RefreshWatchers();
            pathRefreshTimer.Start();
            _ = LoadMapAsync("customs", initializeMapSelector: true);
        }

        private async Task LoadMapAsync(string normalizedName, bool initializeMapSelector = false)
        {
            mapLoadCancellation?.Cancel();
            mapLoadCancellation?.Dispose();
            mapLoadCancellation = new CancellationTokenSource();
            var cancellationToken = mapLoadCancellation.Token;

            IsMapLoading = true;
            MapStatus = "지도 데이터를 불러오는 중입니다.";

            try
            {
                var detail = await liveMapService.GetDetailAsync(normalizedName, cancellationToken);
                if (cancellationToken.IsCancellationRequested || isDisposed)
                    return;

                if (initializeMapSelector || Maps.Count == 0)
                {
                    Maps.Clear();
                    foreach (var map in detail.MapSelector)
                        Maps.Add(map);

                    selectedMap = Maps.FirstOrDefault(map =>
                        map.NormalizedName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
                    );
                    OnPropertyChanged(nameof(SelectedMap));
                }

                Floors.Clear();
                foreach (var floor in detail.Floors.OrderBy(floor => floor.FloorNo))
                    Floors.Add(floor);

                SelectedFloor = Floors.FirstOrDefault(floor => floor.FloorNo == 1)
                    ?? Floors.FirstOrDefault();
                MapStatus = SelectedFloor is null
                    ? "표시할 수 있는 지도 층이 없습니다."
                    : "마우스 휠로 확대하고 드래그로 이동할 수 있습니다.";

                if (playerLocation is not null)
                    ApplyPlayerLocation(playerLocation);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                MapStatus = $"지도 로드 실패: {ex.Message}";
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                    IsMapLoading = false;
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

                if (PlayerLocationService.TryParse(fileName, out var location) && location is not null)
                    RunOnUiThread(() => ApplyPlayerLocation(location));

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

        private void ApplyPlayerLocation(PlayerLocation location)
        {
            playerLocation = location;
            var matchedFloor = PlayerLocationService.FindFloor(Floors, location);
            if (matchedFloor is not null)
                SelectedFloor = matchedFloor;

            UpdateMapProjection();
            MapStatus = $"현재 좌표 X {location.X:F1} · Y {location.Y:F1} · Z {location.Z:F1}";
        }

        private void UpdateMapProjection()
        {
            var bounds = SelectedFloor?.MapBounds;
            if (
                bounds is null || bounds.Length < 2 ||
                bounds[0].Length < 2 || bounds[1].Length < 2 ||
                playerLocation is null || SelectedMap is null
            )
            {
                IsPlayerMarkerVisible = false;
                return;
            }

            var south = Math.Min(bounds[0][0], bounds[1][0]);
            var north = Math.Max(bounds[0][0], bounds[1][0]);
            var west = Math.Min(bounds[0][1], bounds[1][1]);
            var east = Math.Max(bounds[0][1], bounds[1][1]);
            MapWidth = Math.Max(1, east - west);
            MapHeight = Math.Max(1, north - south);

            var (latitude, longitude) = PlayerLocationService.ToMapPosition(
                SelectedMap.NormalizedName,
                playerLocation
            );
            const double markerSize = 24;
            PlayerMarkerLeft = longitude - west - markerSize / 2;
            PlayerMarkerTop = north - latitude - markerSize / 2;
            PlayerMarkerRotation = PlayerLocationService.ToMapYaw(
                SelectedMap.NormalizedName,
                playerLocation.Yaw
            );
            IsPlayerMarkerVisible = true;
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
            mapLoadCancellation?.Cancel();
            mapLoadCancellation?.Dispose();
            pathRefreshTimer.Stop();
            pathRefreshTimer.Tick -= OnPathRefreshTimerTick;

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
