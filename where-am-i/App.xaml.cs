using System.Configuration;
using System.Data;
using System.Windows;
using where_am_i.Services;

namespace where_am_i
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public TrayIconService TrayIcon { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            TrayIcon = new TrayIconService();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TrayIcon.Dispose();
            base.OnExit(e);
        }
    }

}
