using System.Threading;
using System.Windows;
using System.Windows.Threading;
using where_am_i.Services;
using where_am_i.Views;
using Application = System.Windows.Application;

namespace where_am_i
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private Mutex? instanceMutex;
        private EventWaitHandle? restoreRequest;
        private RegisteredWaitHandle? restoreWait;
        private bool ownsInstanceMutex;

        public TrayIconService TrayIcon { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            restoreRequest = new EventWaitHandle(false, EventResetMode.AutoReset,
                @"Local\EftLibraryWhereAmI.Restore");
            instanceMutex = new Mutex(false, @"Local\EftLibraryWhereAmI.Instance");
            try
            {
                ownsInstanceMutex = instanceMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsInstanceMutex = true;
            }

            if (!ownsInstanceMutex)
            {
                restoreRequest.Set();
                Shutdown();
                return;
            }

            TrayIcon = new TrayIconService();
            base.OnStartup(e);
            MainWindow = new MainView();
            MainWindow.Show();

            restoreWait = ThreadPool.RegisterWaitForSingleObject(
                restoreRequest,
                (_, _) => Dispatcher.InvokeAsync(
                    () => TrayIcon.RestoreWindow(MainWindow), DispatcherPriority.Background),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            restoreWait?.Unregister(null);
            TrayIcon?.Dispose();
            restoreRequest?.Dispose();
            if (ownsInstanceMutex)
                instanceMutex!.ReleaseMutex();
            instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }

}
