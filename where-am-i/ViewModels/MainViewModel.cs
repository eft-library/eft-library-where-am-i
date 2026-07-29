using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using where_am_i.Commands;
using where_am_i.Services;
using where_am_i.Views;

namespace where_am_i.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly UserService userService = new UserService();
        private readonly UserPreferencesService preferencesService = new();

        private string email = "";
        public string Email
        {
            get => email;
            set
            {
                email = value;
                OnPropertyChanged();
                CheckUserCommand.RaiseCanExecuteChanged();
            }
        }

        private bool rememberEmail = true;
        public bool RememberEmail
        {
            get => rememberEmail;
            set
            {
                rememberEmail = value;
                OnPropertyChanged();
            }
        }

        private string message = "";
        public string Message
        {
            get => message;
            set
            {
                message = value;
                OnPropertyChanged();
            }
        }

        public RelayCommand CheckUserCommand { get; }

        public MainViewModel()
        {
            CheckUserCommand = new RelayCommand(
                execute: CheckUser,
                canExecute: () => !string.IsNullOrWhiteSpace(Email)
            );

            var preferences = preferencesService.Load();
            Email = preferences.RememberEmail ? preferences.Email : "";
            RememberEmail = preferences.RememberEmail || string.IsNullOrWhiteSpace(preferences.Email);
        }

        private async void CheckUser()
        {
            Message = "";
            var checkedEmail = Email.Trim();

            try
            {
                var isValid = await userService.CheckUserAsync(checkedEmail);

                if (!isValid)
                {
                    MessageBox.Show(
                        "존재하지 않는 사용자",
                        "확인 실패",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                try
                {
                    if (RememberEmail)
                        preferencesService.SaveEmail(checkedEmail);
                    else
                        preferencesService.ClearEmail();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"이메일 저장 설정을 적용하지 못했습니다.\n{ex.Message}",
                        "설정 저장 오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }

                // 성공 시 화면 전환
                var confirmedView = new ConfirmedUserView(email: checkedEmail);
                confirmedView.Show();

                // 현재 창 닫기
                Application.Current.Windows.OfType<MainView>().FirstOrDefault()?.Close();

            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"서버 통신 오류\n{ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
