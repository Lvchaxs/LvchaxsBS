using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LvchaxsBS.Config;
using LvchaxsBS.Services;

namespace LvchaxsBS.UI.Controls
{
    public partial class TopRightBar : UserControl
    {
        public event EventHandler? ThemeToggleClicked;
        public event EventHandler? ScreenshotClicked;
        public event EventHandler? RestartClicked;
        public event EventHandler? ToggleClicked;

        public bool IsToggleOn { get; private set; }

        public TopRightBar()
        {
            InitializeComponent();

            // ★ 从配置读初始状态
            var s = ConfigManager.Get<HomePageSettings>();
            IsToggleOn = s.MasterSwitch;
            UpdateToggleVisual();

            Loaded += (s2, e) => ThemeService.ThemeChanged += ThemeService_ThemeChanged;
            Unloaded += (s2, e) => ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
        }

        private void ThemeService_ThemeChanged(object? sender, AppTheme theme)
        {
            Dispatcher.Invoke(() => UpdateToggleVisual());
        }

        private void ButtonTheme_Click(object sender, RoutedEventArgs e)
            => ThemeToggleClicked?.Invoke(this, EventArgs.Empty);

        private void ButtonScreenshot_Click(object sender, RoutedEventArgs e)
            => ScreenshotClicked?.Invoke(this, EventArgs.Empty);

        private void ButtonRestart_Click(object sender, RoutedEventArgs e)
            => RestartClicked?.Invoke(this, EventArgs.Empty);

        private void ButtonToggle_Click(object sender, RoutedEventArgs e)
        {
            IsToggleOn = !IsToggleOn;
            UpdateToggleVisual();

            // ★ 写回配置
            var s = ConfigManager.Get<HomePageSettings>();
            s.MasterSwitch = IsToggleOn;
            ConfigManager.Save(s);

            ToggleClicked?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateThemeIcon(bool isDark)
        {
            IconSun.Visibility = isDark ? Visibility.Collapsed : Visibility.Visible;
            IconMoon.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 主题按钮中心点（相对指定元素），用于让主题切换迷雾从按钮处扩散。
        /// 用 TransformToVisual 而不是 TransformToAncestor：两个元素只要求在同一棵可视化树里，
        /// 不要求是祖先/后代关系（迷雾层和按钮其实是兄弟节点）。
        /// 取不到时返回 NaN，由调用方回退到窗口中心。
        /// </summary>
        public Point GetThemeButtonCenter(Visual relativeTo)
        {
            try
            {
                var t = ButtonTheme.TransformToVisual(relativeTo);
                return t.Transform(new Point(ButtonTheme.ActualWidth / 2, ButtonTheme.ActualHeight / 2));
            }
            catch
            {
                return new Point(double.NaN, double.NaN);
            }
        }

        public void SetToggleState(bool isOn)
        {
            IsToggleOn = isOn;
            UpdateToggleVisual();
        }

        private void UpdateToggleVisual()
        {
            if (ToggleTrack == null || ToggleThumb == null) return;

            var normalBrush = ThemeBrushProvider.TextSecondaryBrush();
            var fadedBrush = ThemeBrushProvider.Get("Gray300Brush",
                                                    System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB));

            if (IsToggleOn)
            {
                ToggleTrack.BorderBrush = normalBrush;
                ToggleThumb.Stroke = normalBrush;
                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Right;
                ToggleThumb.Margin = new Thickness(0, 0, 4, 0);
            }
            else
            {
                ToggleTrack.BorderBrush = fadedBrush;
                ToggleThumb.Stroke = fadedBrush;
                ToggleThumb.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleThumb.Margin = new Thickness(4, 0, 0, 0);
            }

            ToggleTrack.BorderThickness = new Thickness(1.5);
            ToggleThumb.StrokeThickness = 1.5;
        }
    }
}