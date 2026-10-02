using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LvchaxsBS.Config;
using LvchaxsBS.Services;

namespace LvchaxsBS.UI
{
    public partial class MainWindow : Window
    {
        private const double CornerRadiusValue = 8;

        private const double BaseWidth = 1000;
        private const double BaseHeight = 580;
        private const double BaseMinWidth = 800;
        private const double BaseMinHeight = 300;

        public MainWindow()
        {
            InitializeComponent();

            TopLeft.HomeClicked += TopLeft_HomeClicked;
            TopLeft.SettingsClicked += TopLeft_SettingsClicked;
            TopLeft.PersonalizationClicked += TopLeft_PersonalizationClicked;
            TopLeft.VoiceClicked += TopLeft_VoiceClicked;
            TopLeft.OfficialServerClicked += TopLeft_OfficialServerClicked;
            TopLeft.InternationalServerClicked += TopLeft_InternationalServerClicked;

            TopCenter.FunctionClicked += TopCenter_FunctionClicked;

            TopRight.ThemeToggleClicked += TopRight_ThemeToggleClicked;
            TopRight.ToggleClicked += TopRight_ToggleClicked;
            TopRight.ScreenshotClicked += TopRight_ScreenshotClicked;
            TopRight.RestartClicked += TopRight_RestartClicked;

            ThemeService.ThemeChanged += ThemeService_ThemeChanged;

            AppearanceService.WindowTitleChanged += AppearanceService_WindowTitleChanged;
            Closed += (s, e) =>
            {
                AppearanceService.WindowTitleChanged -= AppearanceService_WindowTitleChanged;
            };

            MainFrame.Navigated += MainFrame_Navigated;

            // 在窗口显示前应用 DPI，避免瞬移
            var app = ConfigManager.Get<AppSettings>();
            ApplyDpiScale(app.DpiScalePercent_1, center: true);

            Loaded += (s, e) =>
            {
                TopRight.UpdateThemeIcon(ThemeService.Current == AppTheme.Dark);
                ApplySavedWindowTitle();
                MainFrame.Navigate(new Pages.HomePage());
            };
        }

        // ============ DPI 缩放 ============

        /// <summary>
        /// 应用 DPI 缩放（百分比 50~175）。
        /// </summary>
        /// <param name="percent">缩放百分比（50~175）</param>
        /// <param name="center">是否重新居中窗口（默认 false）</param>
        public void ApplyDpiScale(double percent, bool center = false)
        {
            double scale = Math.Clamp(percent / 100.0, 0.5, 1.75);

            double newWidth = BaseWidth * scale;
            double newHeight = BaseHeight * scale;

            this.Width = newWidth;
            this.Height = newHeight;
            this.MinWidth = BaseMinWidth * scale;
            this.MinHeight = BaseMinHeight * scale;

            if (RootScaleTransform != null)
            {
                RootScaleTransform.ScaleX = scale;
                RootScaleTransform.ScaleY = scale;
            }

            if (center && WindowState == WindowState.Normal)
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                this.Left = (screenWidth - newWidth) / 2;
                this.Top = (screenHeight - newHeight) / 2;
            }
        }

        // ============ 窗口标题 ============

        private void ApplySavedWindowTitle()
        {
            var s = ConfigManager.Get<PersonalizationSettings>();
            string title = string.IsNullOrEmpty(s.WindowTitle)
                ? "才识是年岁的冠冕，正如思念是我们共度的时间。"
                : s.WindowTitle;

            if (TitleTextBlock != null)
                TitleTextBlock.Text = title;
        }

        private void AppearanceService_WindowTitleChanged(object? sender, string newTitle)
        {
            if (TitleTextBlock == null) return;

            Dispatcher.Invoke(() =>
            {
                TitleTextBlock.Text = string.IsNullOrEmpty(newTitle)
                    ? "才识是年岁的冠冕，正如思念是我们共度的时间。"
                    : newTitle;
            });
        }

        // ============ Toast ============

        public void ShowToast(string message, bool isSuccess = true)
        {
            TitleBarToast?.Show(message, isSuccess);
        }

        // ============ 页面导航 ============

        private void MainFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            bool alignRight = e.Content is Pages.ConfigContainerPage;
            TopCenter.SetAlignment(alignRight, animate: true);

            TopLeft.SetHomeActive(e.Content is Pages.HomePage);
            TopLeft.SetSettingsActive(e.Content is Pages.SettingsPage);
            TopLeft.SetPersonalizationActive(e.Content is Pages.PersonalizationPage);
            TopLeft.SetVoiceActive(e.Content is Pages.VoicePage);
        }

        // ============ 顶部栏事件 ============

        private void TopLeft_HomeClicked(object? sender, EventArgs e)
        {
            MainFrame.Navigate(new Pages.HomePage());
        }

        private void TopLeft_SettingsClicked(object? sender, EventArgs e)
        {
            MainFrame.Navigate(new Pages.SettingsPage());
        }

        private void TopLeft_PersonalizationClicked(object? sender, EventArgs e)
        {
            MainFrame.Navigate(new Pages.PersonalizationPage());
        }

        private void TopLeft_VoiceClicked(object? sender, EventArgs e)
        {
            MainFrame.Navigate(new Pages.VoicePage());
        }

        private void TopLeft_OfficialServerClicked(object? sender, EventArgs e) { }
        private void TopLeft_InternationalServerClicked(object? sender, EventArgs e) { }

        private void TopCenter_FunctionClicked(object? sender, string functionName)
        {
            string keepCategory = "";
            if (MainFrame.Content is Pages.ConfigContainerPage current)
                keepCategory = current.CurrentCategory;

            var container = Pages.HomePage.CreateContainerFor(functionName);
            if (container == null) return;

            if (!string.IsNullOrEmpty(keepCategory))
                container.ShowPageByCategory(keepCategory);
            else
                container.ShowConfigPage();

            MainFrame.Navigate(container);
        }

        // ============ 右侧栏事件 ============

        private void TopRight_ThemeToggleClicked(object? sender, EventArgs e)
        {
            ThemeService.Toggle();
        }

        private void TopRight_ToggleClicked(object? sender, EventArgs e)
        {
            if (MainFrame.Content is Pages.HomePage home)
            {
                home.RefreshMasterSwitchVisual();
            }
            else if (MainFrame.Content is Pages.ConfigContainerPage container)
            {
                container.RefreshMasterSwitchVisual();
            }

            ShowToast($"功能总开关: {(TopRight.IsToggleOn ? "已开启" : "已关闭")}", TopRight.IsToggleOn);
        }

        private void TopRight_ScreenshotClicked(object? sender, EventArgs e) { }

        private void TopRight_RestartClicked(object? sender, EventArgs e)
        {
            RestartApplication();
        }

        private void RestartApplication()
        {
            try
            {
                string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;
                System.Diagnostics.Process.Start(exePath);
                Application.Current.Shutdown();
            }
            catch { }
        }

        // ============ 主题图标刷新 ============

        private void ThemeService_ThemeChanged(object? sender, AppTheme theme)
        {
            TopRight.UpdateThemeIcon(theme == AppTheme.Dark);
        }

        // ============ 窗口 ============

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                RootBorder.CornerRadius = new CornerRadius(0);
                RootBorder.Clip = null;
                if (TitleBarBorder != null)
                    TitleBarBorder.CornerRadius = new CornerRadius(0);
            }
            else
            {
                RootBorder.CornerRadius = new CornerRadius(CornerRadiusValue);
                if (TitleBarBorder != null)
                    TitleBarBorder.CornerRadius = new CornerRadius(CornerRadiusValue, CornerRadiusValue, 0, 0);
                UpdateRootBorderClip();
            }
        }

        private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateRootBorderClip();
        }

        private void UpdateRootBorderClip()
        {
            if (RootBorder == null) return;
            double radius = RootBorder.CornerRadius.TopLeft;
            if (radius > 0 && RootBorder.ActualWidth > 0 && RootBorder.ActualHeight > 0)
            {
                RootBorder.Clip = new RectangleGeometry(
                    new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight),
                    radius, radius);
            }
            else
            {
                RootBorder.Clip = null;
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, null);
        }
    }
}