using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LvchaxsBS.Config;
using LvchaxsBS.Core;
using LvchaxsBS.Services;
using LvchaxsBS.Services.Hooks;
using LvchaxsBS.Toolbox;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI
{
    /// <summary>应用壁纸时的过渡方式。</summary>
    public enum WallpaperTransition
    {
        /// <summary>即时生效（拖透明度 / 模糊滑块）</summary>
        Instant,

        /// <summary>缩放 / 旋转 / 位移带 ~200ms 过渡（拖这几个滑块）</summary>
        Smooth,

        /// <summary>淡入（启用壁纸 / 更换壁纸）</summary>
        FadeIn,

        /// <summary>启动：更慢、更柔和的淡入</summary>
        Startup,
    }

    public partial class MainWindow : Window
    {
        /// <summary>窗口圆角上限（个性化页滑块范围 0~20）</summary>
        private const double MaxWindowCornerRadius = 20;

        /// <summary>当前窗口圆角半径，由个性化页「窗口圆角」滑块决定</summary>
        private double _windowCornerRadius = 8;

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

                WindowFocusService.FocusChanged -= OnTargetWindowChanged;
                WindowFocusService.BoundsChanged -= OnTargetWindowBoundsChanged;
                WindowFocusService.WindowExistenceChanged -= OnTargetWindowExistenceChanged;
            };

            MainFrame.Navigated += MainFrame_Navigated;

            // 窗口检测：游戏是否启动 / 分辨率 / 焦点
            WindowFocusService.FocusChanged += OnTargetWindowChanged;
            WindowFocusService.BoundsChanged += OnTargetWindowBoundsChanged;
            WindowFocusService.WindowExistenceChanged += OnTargetWindowExistenceChanged;
            ApplyTargetWindowState();

            // 在窗口显示前应用 DPI，避免瞬移
            var app = ConfigManager.Get<AppSettings>();
            ApplyDpiScale(app.DpiScalePercent_1, center: true);

            // 窗口圆角（个性化页「窗口圆角」可调 0~20）
            _windowCornerRadius = Math.Clamp(
                ConfigManager.Get<PersonalizationSettings>().TitleOpacity, 0, MaxWindowCornerRadius);
            ApplyWindowCornerRadius();

            // 签名（标题行那行文字）字号
            SetSignatureFontSize(ConfigManager.Get<PersonalizationSettings>().TitleFontSize);

            Loaded += (s, e) =>
            {
                TopRight.UpdateThemeIcon(ThemeService.Current == AppTheme.Dark);
                ApplySavedWindowTitle();
                MainFrame.Navigate(new Pages.HomePage());
            };

            // 壁纸的启动淡入要等窗口第一帧渲染完再开始，
            // 否则动画可能在窗口真正显示出来之前就跑完了，看起来像"没有过渡"。
            ContentRendered += (s, e) =>
            {
                if (_initialWallpaperFadeDone) return;
                _initialWallpaperFadeDone = true;
                ApplyWallpaper(WallpaperTransition.Startup);
            };
        }

        // ============ 壁纸 ============

        /// <summary>模糊度 100% 对应的最大模糊半径</summary>
        private const double MaxWallpaperBlurRadius = 20;

        /// <summary>壁纸淡入 / 淡出时长（ms）</summary>
        private const int WallpaperFadeInMs = 350;          // 启用壁纸 / 更换壁纸
        private const int WallpaperStartupFadeInMs = 1200;  // 启动时：慢一些，营造"逐渐显现"
        private const int WallpaperFadeOutMs = 260;

        /// <summary>当前已解码壁纸的缓存标识（路径 + 最后写入时间），避免拖滑块时反复读盘</summary>
        private string _wallpaperKey = "";

        /// <summary>淡入淡出的代次号：让过期的动画 Completed 回调失效（快速开关时不误隐藏）</summary>
        private int _wallpaperAnimToken;

        /// <summary>是否正在淡出（避免重复触发）</summary>
        private bool _wallpaperFadingOut;

        /// <summary>启动时的壁纸淡入是否已经播过（ContentRendered 可能多次触发）</summary>
        private bool _initialWallpaperFadeDone;

        /// <summary>
        /// 按配置应用/清除窗口壁纸（个性化页修改后也调用它）。
        /// 壁纸画在主题底色之上的独立图层里，因此透明度可与主题底色叠加，
        /// 也不影响标题栏、卡片等任何内容。
        ///
        /// 透明度 / 模糊 / 缩放 / 旋转 / 位移全部是对壁纸图层本身的实时属性，
        /// 直接赋值即生效（GPU 渲染），拖动滑块和透明度一样顺滑、无延迟。
        /// </summary>
        /// <param name="transition">
        /// <see cref="WallpaperTransition.Instant"/> = 全部即时（拖透明度 / 模糊）；
        /// <see cref="WallpaperTransition.Smooth"/> = 缩放/旋转/位移带 200ms 过渡（拖这几个滑块）；
        /// <see cref="WallpaperTransition.FadeIn"/> / <see cref="WallpaperTransition.Startup"/> = 淡入。
        /// 关闭壁纸始终是淡出后再隐藏。
        /// </param>
        public void ApplyWallpaper(WallpaperTransition transition = WallpaperTransition.Instant)
        {
            var p = ConfigManager.Get<PersonalizationSettings>();

            if (!p.EnableWallpaper || string.IsNullOrEmpty(p.WallpaperPath) || !File.Exists(p.WallpaperPath))
            {
                _wallpaperKey = "";

                // 关闭壁纸：先淡出再隐藏；本来就隐藏的直接复位
                if (WallpaperImage.Visibility == Visibility.Visible)
                    FadeOutWallpaper();
                else
                    WallpaperImage.Source = null;

                return;
            }

            // 只有路径或文件内容变了才重新解码
            string key = $"{File.GetLastWriteTimeUtc(p.WallpaperPath).Ticks}|{p.WallpaperPath}";
            if (key != _wallpaperKey)
            {
                var bmp = LoadWallpaperBitmap(p.WallpaperPath);
                if (bmp == null)
                {
                    // 图片打不开：当作没有壁纸处理
                    _wallpaperKey = "";
                    if (WallpaperImage.Visibility == Visibility.Visible)
                        FadeOutWallpaper();
                    else
                        WallpaperImage.Source = null;

                    return;
                }

                WallpaperImage.Source = bmp;
                _wallpaperKey = key;
            }

            WallpaperImage.Visibility = Visibility.Visible;

            double opacity = Math.Clamp(p.WallpaperOpacity, 0, 100) / 100.0;

            // 模糊：实时 Effect，直接改半径即生效
            WallpaperBlurEffect.Radius = Math.Clamp(p.WallpaperBlur, 0, 100) / 100.0 * MaxWallpaperBlurRadius;

            // 缩放（50 = 1.0x）、旋转、位移：拖这些滑块时带过渡，其它情况直接落值
            bool smooth = transition == WallpaperTransition.Smooth;

            double factor = Math.Pow(10, (p.WallpaperScale - 50) / 50.0);
            double sx = p.EnableMirror ? -factor : factor;

            // 可移动范围随缩放变化（与旧版一致）
            double range = p.WallpaperScale <= 60 ? 300 : factor * 500;
            double tx = p.XPosition / 50.0 * range;
            double ty = p.YPosition / 50.0 * range;

            AnimateOrSet(WallpaperScale, ScaleTransform.ScaleXProperty, sx, smooth);
            AnimateOrSet(WallpaperScale, ScaleTransform.ScaleYProperty, factor, smooth);
            AnimateOrSet(WallpaperRotate, RotateTransform.AngleProperty, p.WallpaperRotation, smooth);
            AnimateOrSet(WallpaperTranslate, TranslateTransform.XProperty, tx, smooth);
            AnimateOrSet(WallpaperTranslate, TranslateTransform.YProperty, ty, smooth);

            bool fade = transition is WallpaperTransition.FadeIn or WallpaperTransition.Startup;

            if (fade)
            {
                bool startup = transition == WallpaperTransition.Startup;
                FadeInWallpaper(opacity,
                    startup ? WallpaperStartupFadeInMs : WallpaperFadeInMs,
                    gentleEase: startup);
            }
            else
            {
                // 即时：取消可能在跑的淡入淡出
                _wallpaperAnimToken++;
                _wallpaperFadingOut = false;
                WallpaperImage.Opacity = opacity;
                WallpaperImage.BeginAnimation(OpacityProperty, null);
            }
        }

        /// <summary>变换参数过渡时长（拖缩放 / 旋转 / 位移滑块时用）</summary>
        private const int WallpaperTransformMs = 200;

        /// <summary>
        /// animate = true：用 <see cref="WallpaperTransformMs"/> 缓动过渡到目标值
        /// （不写 From，从当前值起步，所以拖动时是平滑跟随而不是瞬移）；
        /// animate = false：先清掉可能残留的 HoldEnd 动画再直接落值。
        /// </summary>
        private static void AnimateOrSet(Animatable target, DependencyProperty prop, double to, bool animate)
        {
            if (animate)
            {
                target.BeginAnimation(prop, new DoubleAnimation
                {
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(WallpaperTransformMs),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
                return;
            }

            target.BeginAnimation(prop, null);
            target.SetValue(prop, to);
        }

        /// <summary>淡入到指定透明度（启用壁纸 / 更换壁纸 / 启动时用）。</summary>
        private void FadeInWallpaper(double targetOpacity, int durationMs, bool gentleEase = false)
        {
            int token = ++_wallpaperAnimToken;   // 使可能还在跑的淡出回调失效
            _wallpaperFadingOut = false;

            WallpaperImage.Visibility = Visibility.Visible;

            // 关键：先清掉可能残留的动画。
            // DoubleAnimation 默认 FillBehavior.HoldEnd，动画跑完仍然挂在属性上并压住基值，
            // 不清掉的话下面 Opacity=0 是无效的，动画就会从"当前值"动到同一个值 = 看不出变化。
            WallpaperImage.BeginAnimation(OpacityProperty, null);
            WallpaperImage.Opacity = 0;

            var anim = new DoubleAnimation
            {
                From = 0,                                // 显式起点，不依赖当前值
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(Math.Max(1, durationMs)),
                EasingFunction = new QuadraticEase
                {
                    EasingMode = gentleEase ? EasingMode.EaseInOut : EasingMode.EaseOut
                }
            };

            anim.Completed += (s, e) =>
            {
                if (token != _wallpaperAnimToken) return;

                // 先落基值，再清动画（顺序反了会闪一下 0）
                WallpaperImage.Opacity = targetOpacity;
                WallpaperImage.BeginAnimation(OpacityProperty, null);
            };

            WallpaperImage.BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>淡出后再真正隐藏（关闭壁纸时用）。</summary>
        private void FadeOutWallpaper()
        {
            if (_wallpaperFadingOut) return;
            _wallpaperFadingOut = true;

            int token = ++_wallpaperAnimToken;

            var anim = new DoubleAnimation
            {
                From = WallpaperImage.Opacity,           // 显式起点 = 当前显示的不透明度
                To = 0,
                Duration = TimeSpan.FromMilliseconds(WallpaperFadeOutMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (s, e) =>
            {
                // 期间被重新启用/替换过就丢弃这次回调
                if (token != _wallpaperAnimToken) return;

                _wallpaperFadingOut = false;
                WallpaperImage.Opacity = 0;
                WallpaperImage.BeginAnimation(OpacityProperty, null);
                WallpaperImage.Source = null;
                WallpaperImage.Visibility = Visibility.Collapsed;
            };

            WallpaperImage.BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>解码图片（忽略 WPF 的 Uri 图片缓存，同名文件被替换也能读到新图）。</summary>
        private static BitmapImage? LoadWallpaperBitmap(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        // ============ 签名 ============

        /// <summary>签名（标题行文字）字号范围，与个性化页滑块一致</summary>
        private const double MinSignatureFontSize = 9;
        private const double MaxSignatureFontSize = 20;

        /// <summary>
        /// 设置签名（标题栏下面那行文字）的字号。
        /// 由个性化页「签名字体大小」滑块调用。
        /// </summary>
        public void SetSignatureFontSize(double size)
        {
            if (TitleTextBlock == null) return;
            TitleTextBlock.FontSize = Math.Clamp(size, MinSignatureFontSize, MaxSignatureFontSize);
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

            // 浮层（悬停提示、下拉弹窗）是独立的视觉树，不吃上面的 LayoutTransform，
            // 需要它们自己按这个系数缩放，才能和界面里的元素保持同样比例。
            UiScale.Current = scale;

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

        private void TopLeft_OfficialServerClicked(object? sender, EventArgs e)
            => LaunchGameAsync(true);

        private void TopLeft_InternationalServerClicked(object? sender, EventArgs e)
            => LaunchGameAsync(false);

        // ============ 窗口检测 ============

        private void OnTargetWindowChanged(object? sender, bool focused)
            => Dispatcher.Invoke(ApplyTargetWindowState);

        private void OnTargetWindowBoundsChanged(object? sender, WindowFocusService.WindowBounds bounds)
            => Dispatcher.Invoke(ApplyTargetWindowState);

        private void OnTargetWindowExistenceChanged(object? sender, bool exists)
            => Dispatcher.Invoke(ApplyTargetWindowState);

        /// <summary>
        /// 根据"游戏是否启动"决定启动按钮显隐（只看 IsWindowPresent，与焦点无关）；
        /// 有坐标才显示分辨率。
        /// </summary>
        private void ApplyTargetWindowState()
        {
            bool present = WindowFocusService.IsWindowPresent;
            var bounds = WindowFocusService.LastBounds;

            TopLeft.SetGameLaunchVisible(!present);

            TopLeft.SetResolution(!bounds.IsEmpty
                ? $"分辨率: {bounds.Width}×{bounds.Height}"
                : "分辨率: --");
        }

        /// <summary>启动官服 / 国际服（路径来自设置页保存的路径）。</summary>
        private async void LaunchGameAsync(bool official)
        {
            string label = official ? "官服" : "国际服";

            try
            {
                var settings = ConfigManager.Get<AppSettings>();
                string path = official ? settings.OfficialServerPath : settings.InternationalServerPath;

                if (string.IsNullOrEmpty(path))
                {
                    ShowToast($"{label}: 请先在[设置]中配置路径", false);
                    return;
                }

                if (!File.Exists(path))
                {
                    ShowToast($"{label}: 文件不存在", false);
                    return;
                }

                ShowToast($"{label}: 尝试启动...", true);

                await Task.Run(() => Process.Start(path));

                ShowToast($"{label} 已启动", true);
            }
            catch (Exception ex)
            {
                ShowToast($"{label} 启动失败: {ex.Message}", false);
            }
        }

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
            if (ThemeFog == null)
            {
                ThemeService.Toggle();
                return;
            }

            // 迷雾从主题按钮处扩散（两个方向共用同一套雾色）
            var origin = TopRight.GetThemeButtonCenter(ThemeFog);

            if (!ThemeFog.Play(origin, ThemeService.Toggle))
                return;   // 动画播放中，忽略本次点击
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

            // 功能总开关关闭 → 立即停止所有正在运行的功能
            if (!TopRight.IsToggleOn)
            {
                IconService.StopAllModules();
            }

            ShowToast($"功能总开关: {(TopRight.IsToggleOn ? "已开启" : "已关闭")}", TopRight.IsToggleOn);
        }

        private void TopRight_ScreenshotClicked(object? sender, EventArgs e)
        {
            try
            {
                new ScreenshotToolWindow().ShowDialog();
            }
            catch (Exception ex)
            {
                ShowToast($"打开截图工具失败: {ex.Message}", false);
            }
        }

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
            ApplyWindowCornerRadius();
        }

        /// <summary>应用当前窗口圆角（最大化时强制为 0）。</summary>
        private void ApplyWindowCornerRadius()
        {
            if (RootBorder == null) return;

            double r = WindowState == WindowState.Maximized ? 0 : _windowCornerRadius;

            RootBorder.CornerRadius = new CornerRadius(r);
            if (TitleBarBorder != null)
                TitleBarBorder.CornerRadius = new CornerRadius(r, r, 0, 0);

            UpdateRootBorderClip();
        }

        /// <summary>
        /// 设置窗口四角圆角（0~20）。个性化页「窗口圆角」滑块调用。
        /// </summary>
        public void SetWindowCornerRadius(double radius)
        {
            _windowCornerRadius = Math.Clamp(radius, 0, MaxWindowCornerRadius);
            ApplyWindowCornerRadius();
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