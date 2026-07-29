using System.Diagnostics;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;

namespace where_am_i.Services
{
    public sealed class TrayIconService : IDisposable
    {
        private readonly Forms.NotifyIcon notifyIcon;
        private Window? hiddenWindow;
        private bool isDisposed;

        public TrayIconService()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("열기", null, (_, _) => RestoreWindow());
            menu.Items.Add("종료", null, (_, _) => Application.Current.Shutdown());

            notifyIcon = new Forms.NotifyIcon
            {
                Text = "EFT Library - 내 위치 찾기",
                ContextMenuStrip = menu,
                Icon = LoadApplicationIcon(),
                Visible = false
            };
            notifyIcon.DoubleClick += (_, _) => RestoreWindow();
        }

        public void HideToTray(Window window)
        {
            if (isDisposed)
                return;

            hiddenWindow = window;
            window.Hide();
            notifyIcon.Visible = true;
        }

        public void RestoreWindow()
        {
            if (isDisposed || hiddenWindow is null)
                return;

            notifyIcon.Visible = false;
            hiddenWindow.Show();
            hiddenWindow.WindowState = WindowState.Normal;
            hiddenWindow.Activate();
        }

        private static Icon LoadApplicationIcon()
        {
            var executablePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName;

            return executablePath is not null
                ? Icon.ExtractAssociatedIcon(executablePath) ?? SystemIcons.Application
                : SystemIcons.Application;
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }
    }
}
