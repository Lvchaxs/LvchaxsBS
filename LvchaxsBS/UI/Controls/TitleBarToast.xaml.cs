using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LvchaxsBS.UI.Controls
{
    public partial class TitleBarToast : UserControl
    {
        private Storyboard? _storyboard;

        public TitleBarToast()
        {
            InitializeComponent();
        }

        public void Show(string message, bool isSuccess = true, int durationMs = 5000)
        {
            if (PART_Message == null || PART_Container == null) return;

            _storyboard?.Stop(PART_Container);

            var color = isSuccess
                ? Color.FromRgb(0x10, 0xAC, 0x84)   // 绿
                : Color.FromRgb(0xEF, 0x44, 0x44);  // 红

            var brush = new SolidColorBrush(color);

            PART_Message.Text = message;
            PART_Message.Foreground = brush;

            if (PART_IconInfo != null && PART_IconError != null)
            {
                PART_IconInfo.Visibility = isSuccess ? Visibility.Visible : Visibility.Collapsed;
                PART_IconError.Visibility = isSuccess ? Visibility.Collapsed : Visibility.Visible;
            }

            if (!(PART_Container.RenderTransform is TranslateTransform))
                PART_Container.RenderTransform = new TranslateTransform();

            var tt = (TranslateTransform)PART_Container.RenderTransform;
            tt.Y = -20;

            var slideIn = new DoubleAnimation
            {
                From = -20,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slideIn, PART_Container);
            Storyboard.SetTargetProperty(slideIn, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, PART_Container);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));

            var slideOut = new DoubleAnimation
            {
                From = 0,
                To = -20,
                BeginTime = TimeSpan.FromMilliseconds(durationMs),
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(slideOut, PART_Container);
            Storyboard.SetTargetProperty(slideOut, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                BeginTime = TimeSpan.FromMilliseconds(durationMs),
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeOut, PART_Container);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));

            _storyboard = new Storyboard();
            _storyboard.Children.Add(slideIn);
            _storyboard.Children.Add(fadeIn);
            _storyboard.Children.Add(slideOut);
            _storyboard.Children.Add(fadeOut);

            _storyboard.Begin();
        }
    }
}