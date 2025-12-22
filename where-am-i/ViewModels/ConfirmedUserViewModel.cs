using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using where_am_i.Commands;

namespace where_am_i.ViewModels
{
    public class ConfirmedUserViewModel : INotifyPropertyChanged
    {
        private readonly string email;
        private readonly HttpClient httpClient;

        // 여러 watcher 관리
        private readonly List<FileSystemWatcher> _watchers = new();

        public RelayCommand OpenWebCommand { get; }
        public RelayCommand ExitCommand { get; }

        public ConfirmedUserViewModel(string userEmail)
        {
            email = string.IsNullOrWhiteSpace(userEmail) ? "" : userEmail;

            httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://back.eftlibrary.com")
            };

            OpenWebCommand = new RelayCommand(OpenWeb);
            ExitCommand = new RelayCommand(ExitApp);

            try
            {
                StartWatchingScreenshots();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FileSystemWatcher 초기화 실패: {ex.Message}");
            }
        }

        private void OpenWeb()
        {
            string url = "https://eftlibrary.com/map-of-tarkov/CUSTOMS";

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
                MessageBox.Show($"웹 열기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitApp()
        {
            Application.Current.Shutdown();
        }

        // 스크린샷 감시 시작
        private void StartWatchingScreenshots()
        {
            string userName = Environment.UserName;

            var screenshotPaths = new List<string>
            {
                // Escape from Tarkov
                $@"C:\Users\{userName}\Documents\Escape from Tarkov\Screenshots",

                // Windows 기본 스크린샷
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Screenshots"
                ),

                // Steam (Pictures)
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Steam"
                )
            };

            // 일반 경로 감시
            foreach (var path in screenshotPaths)
            {
                AddWatcher(path, includeSubdirectories: false);
            }

            // Steam userdata 전체 감시 (자동)
            AddWatcher(
                @"C:\Program Files (x86)\Steam\userdata",
                includeSubdirectories: true
            );
        }

        // FileSystemWatcher 생성 공통 메서드
        private void AddWatcher(string path, bool includeSubdirectories)
        {
            if (!Directory.Exists(path))
                return;

            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            watcher.Created += OnScreenshotCreated;
            _watchers.Add(watcher);

            Console.WriteLine($"📂 스크린샷 감시 시작: {path}");
        }

        // 스크린샷 생성 이벤트
        private async void OnScreenshotCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(e.Name))
                    return;

                // 이미지 확장자 필터
                string ext = Path.GetExtension(e.Name).ToLower();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                    return;

                // Steam userdata 노이즈 필터
                if (e.FullPath.Contains(@"\Steam\userdata\", StringComparison.OrdinalIgnoreCase) &&
                    !e.FullPath.Contains(@"\screenshots\", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // 파일 저장 완료 대기
                await WaitUntilFileReady(e.FullPath);

                await SendScreenshotLocationAsync(e.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"스크린샷 처리 오류: {ex.Message}");
            }
        }

        // 파일 저장 완료 대기
        private async Task WaitUntilFileReady(string path, int retry = 10)
        {
            for (int i = 0; i < retry; i++)
            {
                try
                {
                    using var stream = File.Open(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.None
                    );
                    return;
                }
                catch
                {
                    await Task.Delay(200);
                }
            }
        }

        // API 전송
        private async Task SendScreenshotLocationAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fileName))
                return;

            var payload = new
            {
                email = email,
                location = fileName
            };

            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("/api/where-am-i/send-location", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"서버 전송 실패: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"서버 전송 중 오류: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
