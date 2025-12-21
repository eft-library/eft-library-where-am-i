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
        }

        private async void CheckUser()
        {
            Message = "";

            try
            {
                var isValid = await userService.CheckUserAsync(Email);

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

                // 성공 시 화면 전환
                var confirmedView = new ConfirmedUserView(email: Email);
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
