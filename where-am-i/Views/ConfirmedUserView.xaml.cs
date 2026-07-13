using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using where_am_i.ViewModels;

namespace where_am_i.Views
{
    /// <summary>
    /// ConfirmedUserView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class ConfirmedUserView : Window
    {
        private readonly ScaleTransform mapScale = new(1, 1);
        private readonly TranslateTransform mapTranslation = new();
        private Point? mapDragStart;

        public ConfirmedUserView(string email)
        {
            InitializeComponent();
            var mapTransform = new TransformGroup();
            mapTransform.Children.Add(mapScale);
            mapTransform.Children.Add(mapTranslation);
            MapViewport.RenderTransform = mapTransform;

            DataContext = new ConfirmedUserViewModel(email);
            Closed += OnClosed;
        }

        private void OnMapMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            var nextScale = Math.Clamp(mapScale.ScaleX * factor, 0.75, 6);
            mapScale.ScaleX = nextScale;
            mapScale.ScaleY = nextScale;
            e.Handled = true;
        }

        private void OnMapMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ResetMapTransform();
                e.Handled = true;
                return;
            }

            mapDragStart = e.GetPosition(this);
            MapViewport.CaptureMouse();
            Mouse.OverrideCursor = Cursors.Hand;
            e.Handled = true;
        }

        private void OnMapMouseMove(object sender, MouseEventArgs e)
        {
            if (mapDragStart is not Point previous || e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(this);
            mapTranslation.X += current.X - previous.X;
            mapTranslation.Y += current.Y - previous.Y;
            mapDragStart = current;
        }

        private void OnMapMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            mapDragStart = null;
            MapViewport.ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
            e.Handled = true;
        }

        private void ResetMapTransform()
        {
            mapScale.ScaleX = 1;
            mapScale.ScaleY = 1;
            mapTranslation.X = 0;
            mapTranslation.Y = 0;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (DataContext is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
