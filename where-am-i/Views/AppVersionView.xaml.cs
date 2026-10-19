using System.Windows;
using where_am_i.Services;

namespace where_am_i.Views;

public partial class AppVersionView : Window
{
    public AppVersionView()
    {
        InitializeComponent();
        WindowThemeService.Apply(this);
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
