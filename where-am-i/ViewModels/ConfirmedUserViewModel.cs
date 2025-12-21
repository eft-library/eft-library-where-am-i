using System;
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
        private FileSystemWatcher? watcher;
        private readonly HttpClient httpClient;


        public RelayCommand OpenWebCommand { get; }
        public RelayCommand ExitCommand { get; }

        public ConfirmedUserViewModel(string userEmail)
        {
            // 이메일 null 처리
            email = string.IsNullOrWhiteSpace(userEmail) ? "" : userEmail;

            httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://back.eftlibrary.com")
            };

            OpenWebCommand = new RelayCommand(OpenWeb);
            ExitCommand = new RelayCommand(ExitApp);

            // 스크린샷 감시 시작
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

        private void StartWatchingScreenshots()
        {
            // 현재 로그인한 사용자 이름 가져오기
            string userName = Environment.UserName;

            // Tarkov 스크린샷 경로
            string screenshotsPath = $@"C:\Users\{userName}\Documents\Escape from Tarkov\Screenshots";

            // 폴더가 없으면 생성
            if (!Directory.Exists(screenshotsPath))
                Directory.CreateDirectory(screenshotsPath);

            watcher = new FileSystemWatcher(screenshotsPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
            };

            watcher.Created += async (s, e) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(e.Name))
                        await SendScreenshotLocationAsync(e.Name);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SendScreenshotLocationAsync 오류: {ex.Message}");
                }
            };

            watcher.EnableRaisingEvents = true;

            Console.WriteLine($"스크린샷 감시 시작: {screenshotsPath}");
        }


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
