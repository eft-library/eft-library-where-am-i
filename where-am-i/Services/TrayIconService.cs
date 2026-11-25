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

            StopTrackingHiddenWindow();
            hiddenWindow = window;
            window.Closed += OnHiddenWindowClosed;
            window.Hide();
            notifyIcon.Visible = true;
        }

        public void RestoreWindow(Window? fallbackWindow = null)
        {
            var window = hiddenWindow ?? fallbackWindow;
            if (isDisposed || window is null)
                return;

            StopTrackingHiddenWindow();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            if (!window.IsVisible)
                window.Show();
            window.Activate();
        }

        private void OnHiddenWindowClosed(object? sender, EventArgs e)
        {
            StopTrackingHiddenWindow();
        }

        private void StopTrackingHiddenWindow()
        {
            if (hiddenWindow is not null)
                hiddenWindow.Closed -= OnHiddenWindowClosed;
            hiddenWindow = null;
            notifyIcon.Visible = false;
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
            StopTrackingHiddenWindow();
            notifyIcon.Dispose();
        }
    }
}
