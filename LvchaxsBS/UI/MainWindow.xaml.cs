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
using System.Windows.Threading;
using System.Security.Principal;
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

        /// <summary>启动首屏用的首页实例（构造函数里就建好；Loaded 里若还没挂上就再挂一次）。</summary>
        private Pages.HomePage? _startupHome;

        private const double BaseWidth = 1000;
        private const double BaseHeight = 580;
        private const double BaseMinWidth = 800;
        private const double BaseMinHeight = 300;

        public MainWindow()
        {
            InitializeComponent();

            // ★★ 启动首屏（2026-10-16，日志实测）：
            //   `Frame` 的导航 —— 不管 `Navigate(...)` 还是直接赋 `Content` —— **都是异步的**。
            //   实测：赋值后 1ms 时 `MainFrame.Content` 仍是 null，要等约 125ms 才真正挂上，
            //   于是窗口第一帧只有外壳、内容区空白，首页随后才补上（用户看到的就是这个）。
            //
            //   两招一起用：
            //   ① 导航在**构造函数**里就排进去（窗口还没显示，等它排到时第一帧还没画）；
            //   ② 窗口先 `Opacity = 0` 藏住，等首页真正 Loaded（要显示的那一帧之前）再露脸 ——
            //      这样露出来的那一帧内容已经在了，中间的"空窗期"用户根本看不到。
            Opacity = 0;
            _startupHome = new Pages.HomePage();
            MainFrame.Navigate(_startupHome);

            TopLeft.HomeClicked += TopLeft_HomeClicked;
            TopLeft.SettingsClicked += TopLeft_SettingsClicked;
            TopLeft.PersonalizationClicked += TopLeft_PersonalizationClicked;
            TopLeft.VoiceClicked += TopLeft_VoiceClicked;
            TopLeft.OfficialServerClicked += TopLeft_OfficialServerClicked;
            TopLeft.InternationalServerClicked += TopLeft_InternationalServerClicked;

            // UID 账号管理模块（可选插件：exe 同目录有 LvchaxsBS_uid.dll 才显示按钮）
            TopLeft.UidClicked += TopLeft_UidClicked;
            UidModuleService.Probe();
            TopLeft.SetUidVisible(UidModuleService.IsAvailable);
            UidModuleService.CloseToolRequested += UidModuleService_CloseToolRequested;

            // 自定义宏模块（可选插件：exe 同目录有 LvchaxsBS_macro.dll 才显示左侧按钮）
            TopLeft.MacroClicked += TopLeft_MacroClicked;
            MacroModuleService.Probe();
            TopLeft.SetMacroVisible(MacroModuleService.IsAvailable);

            // 全局模态遮罩层：宿主的弹窗和 UID 模块的页内模态框都用它压暗整个窗口
            ModalOverlayService.Attach(ModalLayer, ModalMask, ModalHost);

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

                // 显式结束整个应用。
                // 不能依赖默认的 ShutdownMode=OnLastWindowClose：引擎会创建常驻的
                // 浮层窗口（底部图标 / 字幕），它们只是 Hide 并没有 Close，
                // 主窗口关掉后 Application.Windows 仍非空 → 不会自动退出，
                // 结果是"界面没了但 exe 还挂在后台"（钩子和检测线程都还在跑）。
                Application.Current?.Shutdown();
            };

            MainFrame.Navigated += MainFrame_Navigated;

            // 窗口检测：游戏是否启动 / 分辨率 / 焦点
            WindowFocusService.FocusChanged += OnTargetWindowChanged;
            WindowFocusService.BoundsChanged += OnTargetWindowBoundsChanged;
            WindowFocusService.WindowExistenceChanged += OnTargetWindowExistenceChanged;
            ApplyTargetWindowState();

            // 启动时按已保存的总开关状态应用一次（总开关为关时不应启动界面检测/图标窗口）
            IconService.ApplyMasterSwitchState(TopRight.IsToggleOn);

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

                // 顶部那一排功能图标按已保存的顺序排（和首页功能卡片同一个顺序）
                // ★ animate:false —— 启动时直接落到保存的顺序，别播一次重排动画
                SyncFunctionButtonOrder(animate: false);

                // 构造函数里排的那次导航可能还没轮到 → 这里再挂一次（同一个实例，不会重复建页面）
                if (_startupHome != null && !ReferenceEquals(MainFrame.Content, _startupHome))
                    MainFrame.Content = _startupHome;

                // 首页真正加载完（下一帧就带内容）→ 把窗口露出来
                if (_startupHome != null)
                {
                    if (_startupHome.IsLoaded) RevealWindow();
                    else _startupHome.Loaded += (_, _) => RevealWindow();
                }

                // 直接赋值触发的 Navigated 是异步的（见 MainFrame_Navigated 注释），
                // 这里先把顶部那排的高亮同步设好，免得启动后慢一帧才亮。
                TopLeft.SetHomeActive(true);
                TopLeft.SetSettingsActive(false);
                TopLeft.SetPersonalizationActive(false);
                TopLeft.SetVoiceActive(false);
                TopCenter.SetAlignment(false, animate: false);
            };

            // 壁纸的启动淡入要等窗口第一帧渲染完再开始，
            // 否则动画可能在窗口真正显示出来之前就跑完了，看起来像"没有过渡"。
            ContentRendered += (s, e) =>
            {
                // 兜底：万一首页的 Loaded 没来（异常/被切走），首帧渲染完也要把窗口露出来，
                // 免得程序起来了却看不见窗口。
                if (MainFrame.Content != null) RevealWindow();

                // ★ 非管理员启动 ⇒ 红色提示（模拟输入发不进以管理员运行的游戏）
                NotifyNonAdminStartup();

                if (_initialWallpaperFadeDone) return;
                _initialWallpaperFadeDone = true;
                ApplyWallpaper(WallpaperTransition.Startup);
            };
        }

        /// <summary>「非管理员」提示只在本次运行弹一次（ContentRendered 可能来多次）。</summary>
        private bool _nonAdminNotified;

        /// <summary>
        /// 启动时不是管理员 ⇒ 弹一条**红色**提示：模拟输入（SendInput 那套）在非管理员下
        /// 会被以管理员运行的游戏挡掉，功能看着都在、按下去却没反应，必须让用户知道。
        /// </summary>
        private void NotifyNonAdminStartup()
        {
            if (_nonAdminNotified) return;
            _nonAdminNotified = true;

            if (IsRunningAsAdmin()) return;

            ShowToast("非管理员启动，模拟输入部分将无法生效。", false);
        }

        /// <summary>本进程是不是以管理员身份运行（判断不了时按「是」处理，避免误报）。</summary>
        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 把窗口露出来（启动时先 Opacity=0 藏住，等首页真的挂上再调这里）。
        /// 只在还没露脸时生效，重复调用无害。
        /// </summary>
        private void RevealWindow()
        {
            if (Opacity >= 1) return;
            Opacity = 1;
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

        /// <summary>同一路径下，壁纸文件时间戳最多每这么多毫秒重查一次</summary>
        private const int WallpaperStampThrottleMs = 300;

        /// <summary>上次取到的壁纸文件最后写入时间（ticks）</summary>
        private long _wallpaperStampTicks;

        /// <summary>上次校验文件时间戳的时刻（Environment.TickCount64）。初始 0 保证首次一定校验。</summary>
        private long _wallpaperStampCheckedAt;

        /// <summary>
        /// 5 个壁纸变换各自"上次下发的目标值 + 是否走的是带动画那条路径"。
        /// 拖透明度 / 模糊滑块时缩放、旋转、平移的数值根本没变，
        /// 没必要每帧把这 5 条动画全部拆掉重建（那是纯粹的 UI 线程浪费）。
        /// </summary>
        private readonly double[] _transformTarget = { double.NaN, double.NaN, double.NaN, double.NaN, double.NaN };
        private readonly bool[] _transformAnimated = new bool[5];

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

                // 壁纸整个关掉时也把（半径为 0 的）模糊效果摘掉，彻底不占离屏通道
                ApplyWallpaperBlurEffect(false);

                return;
            }

            // 只有路径或文件内容变了才重新解码
            string key = BuildWallpaperKey(p.WallpaperPath);
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
            double blurRadius = Math.Clamp(p.WallpaperBlur, 0, 100) / 100.0 * MaxWallpaperBlurRadius;
            WallpaperBlurEffect.Radius = blurRadius;

            // ★ 半径为 0 时把 Effect 整个摘掉。BlurEffect 即使 Radius=0 也会让这张铺满窗口的图
            //   走一遍离屏渲染再合成（本窗口是 AllowsTransparency，纯软件渲染，代价尤其明显）；
            //   摘掉后画面与半径为 0 完全一致，但少一次全窗口离屏通道。
            ApplyWallpaperBlurEffect(blurRadius > 0);

            // 缩放（50 = 1.0x）、旋转、位移：拖这些滑块时带过渡，其它情况直接落值
            bool smooth = transition == WallpaperTransition.Smooth;

            double factor = Math.Pow(10, (p.WallpaperScale - 50) / 50.0);
            double sx = p.EnableMirror ? -factor : factor;

            // 可移动范围随缩放变化
            double range = p.WallpaperScale <= 60 ? 300 : factor * 500;
            double tx = p.XPosition / 50.0 * range;
            double ty = p.YPosition / 50.0 * range;

            ApplyTransform(0, WallpaperScale, ScaleTransform.ScaleXProperty, sx, smooth);
            ApplyTransform(1, WallpaperScale, ScaleTransform.ScaleYProperty, factor, smooth);
            ApplyTransform(2, WallpaperRotate, RotateTransform.AngleProperty, p.WallpaperRotation, smooth);
            ApplyTransform(3, WallpaperTranslate, TranslateTransform.XProperty, tx, smooth);
            ApplyTransform(4, WallpaperTranslate, TranslateTransform.YProperty, ty, smooth);

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

        /// <summary>
        /// 按需把模糊效果挂到壁纸图上 / 从壁纸图上摘掉（摘掉时视觉与 Radius=0 完全一致）。
        /// <para>壁纸被换掉（<see cref="WallpaperImage.Source"/> 变）时 Effect 跟着元素走，不用重挂。</para>
        /// </summary>
        private void ApplyWallpaperBlurEffect(bool withBlur)
        {
            bool attached = ReferenceEquals(WallpaperImage.Effect, WallpaperBlurEffect);

            if (withBlur)
            {
                if (!attached) WallpaperImage.Effect = WallpaperBlurEffect;
            }
            else if (attached)
            {
                WallpaperImage.Effect = null;
            }
        }

        /// <summary>
        /// 生成壁纸缓存键（文件最后写入时间 + 路径）。
        /// <para>
        /// 同一路径下对时间戳做节流：<see cref="ApplyWallpaper"/> 是"拖滑块每帧都会走到"的路径，
        /// 而 File.GetLastWriteTimeUtc 是一次文件系统调用，不能每帧都问一次。
        /// 路径变了（换了壁纸）缓存键本身就不同，会立刻重新解码，所以观感不变。
        /// </para>
        /// </summary>
        private string BuildWallpaperKey(string path)
        {
            long now = Environment.TickCount64;
            if (now - _wallpaperStampCheckedAt >= WallpaperStampThrottleMs)
            {
                _wallpaperStampCheckedAt = now;
                try { _wallpaperStampTicks = File.GetLastWriteTimeUtc(path).Ticks; }
                catch { _wallpaperStampTicks = 0; }
            }

            return $"{_wallpaperStampTicks}|{path}";
        }

        /// <summary>
        /// 下发一个壁纸变换。目标值和"是否为带动画那条路径"都没变时直接跳过：
        /// 拖透明度 / 模糊滑块会带着调用这里，但那几个变换本来就没动，
        /// 每帧拆掉重建动画纯属浪费 UI 线程。
        /// </summary>
        private void ApplyTransform(int slot, Animatable target, DependencyProperty prop, double to, bool animate)
        {
            if (to.Equals(_transformTarget[slot]) && _transformAnimated[slot] == animate)
                return;

            _transformTarget[slot] = to;
            _transformAnimated[slot] = animate;
            AnimateOrSet(target, prop, to, animate);
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

        /// <param name="durationMs">显示时长；<c>&lt;= 0</c> = 不限时（常驻，直到下一次提示把它顶掉）。</param>
        public void ShowToast(string message, bool isSuccess = true, int durationMs = 5000)
        {
            TitleBarToast?.Show(message, isSuccess, durationMs);
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

            // 内容区是不是 UID 页面？是就保持高亮 + 标题后缀「_UID」+ 标题栏「!」按钮。
            //
            // 注意：这里必须按"当前内容"判断，而不是只靠 TopLeft_UidClicked 里那次显式调用 ——
            // 给 Frame.Content 赋值会**异步**触发一次 Navigated，如果这里无脑写 false，
            // 就会把刚打开的后缀/按钮又关掉（实测踩过这个坑）。
            bool isUidPage = UidModuleService.IsAvailable
                             && e.Content != null
                             && ReferenceEquals(e.Content, UidModuleService.CurrentView);

            TopLeft.SetUidActive(isUidPage);
            SetUidTitleMode(isUidPage);

            // 内容区是不是自定义宏页面？是就保持高亮（与 UID 同逻辑：直接 Navigate 同一实例 → 页面记忆）
            bool isMacroPage = MacroModuleService.IsAvailable
                               && e.Content != null
                               && ReferenceEquals(e.Content, MacroModuleService.CurrentView);

            TopLeft.SetMacroActive(isMacroPage);

            // 兜底：直接 Navigate 到别处（不经过配置容器页）时，全屏状态必须还原 ——
            // 否则从编辑页跳走后，窗口会一直缺着文字标题行和顶部按钮栏。
            // ★ 只负责"还原"，不负责"开启"：开启由配置容器页在其内部导航时上抛。
            if (!(e.Content is Page page && (page.Tag as string) == "FullScreen"))
                SetContentFullScreen(false);
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

        // ============ 内容页「全屏」============

        /// <summary>
        /// 让内容区占满整个窗口 —— **只留 32 高的标题栏**。
        ///
        /// <para>
        /// 由内容页（经配置容器页 <c>ConfigContainerPage</c>）用 <c>Tag="FullScreen"</c> 触发，
        /// 例如宏指令编辑页。本方法负责主窗口这一层：
        /// 收掉「文字标题行」（才识是年岁的冠冕…）与「顶部按钮栏」，
        /// 再把 <see cref="MainFrame"/> 从 Row3 提升为 <c>Row=1, RowSpan=3</c> 盖过这两行。
        /// </para>
        ///
        /// <para>
        /// ⚠️ Row2 是固定 40 高，只 Collapse 内容**不会**让行消失 ⇒
        ///    必须靠 <c>RowSpan</c> 让 MainFrame 真正跨上去，否则底下空 40px。
        /// </para>
        /// </summary>
        public void SetContentFullScreen(bool full)
        {
            TitleRow.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            TopButtonBar.Visibility = full ? Visibility.Collapsed : Visibility.Visible;

            MainFrame.SetValue(Grid.RowProperty, full ? 1 : 3);
            MainFrame.SetValue(Grid.RowSpanProperty, full ? 3 : 1);
            MainFrame.Margin = full ? new Thickness(0) : new Thickness(5, 0, 5, 10);
        }

        /// <summary>
        /// 把 UID 账号管理界面显示到内容区（由外部插件 LvchaxsBS_uid.dll 提供）。
        ///
        /// <para>
        /// 这里刻意用 <see cref="System.Windows.Controls.ContentControl.Content"/> 直接赋值、
        /// 而不是 <c>MainFrame.Navigate(...)</c>：
        /// 模块内部是单例，复用同一个实例才能保住用户上次浏览到的分组、页面位置；
        /// 每次 new 一个新页面会把状态清空。副作用是不会触发 Navigated 事件，
        /// 所以导航高亮在这里手动处理。
        /// </para>
        /// </summary>
        private void TopLeft_UidClicked(object? sender, EventArgs e)
        {
            // 模块内部是单例：已存在就直接切过去（保住上次浏览到的分组 / 页面位置）
            var view = UidModuleService.CurrentView ?? UidModuleService.GetAccountManagerView();
            if (view == null) return;

            ApplyUidView(view);
        }

        /// <summary>
        /// 把 UID 界面放进内容区，并把顶部那排按钮的高亮切到「UID」。
        /// 与 <see cref="TopLeft_UidClicked"/> 分开，是因为切换到其它模块时也要复用这套高亮。
        /// </summary>
        private void ApplyUidView(FrameworkElement view)
        {
            MainFrame.Content = view;

            TopCenter.SetAlignment(false, animate: true);
            TopLeft.SetHomeActive(false);
            TopLeft.SetSettingsActive(false);
            TopLeft.SetPersonalizationActive(false);
            TopLeft.SetVoiceActive(false);
            TopLeft.SetUidActive(true);
            SetUidTitleMode(true);         // 标题变成「Lvchaxs伴生_UID」并显示「使用说明」入口
        }

        /// <summary>
        /// 把自定义宏配置界面显示到内容区（由插件 LvchaxsBS_macro.dll 提供）。
        /// <para>
        /// 和 UID 一样：用 <see cref="System.Windows.Controls.ContentControl.Content"/> 直接赋值 / Navigate
        /// 复用同一个实例（<see cref="MacroModuleService.CurrentView"/>），保住用户上次浏览到的文件夹 / 滚动位置；
        /// 导航高亮交给 <see cref="MainFrame_Navigated"/> 统一处理。
        /// </para>
        /// </summary>
        public void ShowMacroView()
        {
            var view = MacroModuleService.CurrentView ?? MacroModuleService.GetMacroConfigView();
            if (view == null) return;

            // 已经停在宏页面就不再重复 Navigate（避免 Loaded 反复重跑 Reload）
            if (!ReferenceEquals(MainFrame.Content, view))
                MainFrame.Navigate(view);
        }

        private void TopLeft_MacroClicked(object? sender, EventArgs e) => ShowMacroView();

        /// <summary>
        /// 标题栏的「UID 模式」外观：进入账号管理时把标题后缀显示成 <c>_UID</c>，
        /// 并在右边露出「使用说明」的「!」按钮；离开时全部收掉、回到原样。
        ///
        /// <para>
        /// 那个「!」按钮原先画在模块自己的页面里，挪到标题栏后整窗统一、也不再占内容区一行。
        /// 模块未加载（没有 dll）时按钮不出现。
        /// </para>
        /// </summary>
        private void SetUidTitleMode(bool on)
        {
            if (UidTitleSuffix != null)
                UidTitleSuffix.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

            if (UidHelpButton != null)
                UidHelpButton.Visibility = (on && UidModuleService.IsAvailable)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>标题栏「!」：打开账号管理的「使用说明」窗口（由模块提供）。</summary>
        private void UidHelpButton_Click(object sender, RoutedEventArgs e)
            => UidModuleService.ShowHelp();

        /// <summary>模块帮助窗里点了『关闭工具』：切回宿主自己的首页。</summary>
        private void UidModuleService_CloseToolRequested(object? sender, EventArgs e)
            => Dispatcher.Invoke(() => MainFrame.Navigate(new Pages.HomePage()));

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
            // ★ 自定义宏不走「配置容器页」（带「快速传送」顶栏的通用工具页），
            //   改走和 UID 一样的内容区视图（固定头行 + 内容区 + 页面记忆）。
            //   首页卡片的「打开宏文件夹」按钮也是走这个方法，所以这里统一拦截，
            //   让文件夹按钮和「配置」按钮落到同一个新宏视图（不再带那个顶栏）。
            if (string.Equals(functionName, MacroModuleService.ModuleName, StringComparison.Ordinal))
            {
                ShowMacroView();
                return;
            }

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

        /// <summary>
        /// 让顶部那一排功能图标按 <c>HomePageSettings.ModuleOrder</c> 排。
        /// 首页功能卡片拖动排序后也会调它 —— 两边永远是同一个顺序。
        /// </summary>
        /// <param name="animate">是否播滑动动画。启动时的首次同步传 <c>false</c>（见 <c>TopCenterBar.ApplyModuleOrder</c> 注释）。</param>
        public void SyncFunctionButtonOrder(bool animate = true)
        {
            try
            {
                var s = ConfigManager.Get<HomePageSettings>();
                TopCenter.ApplyModuleOrder(Services.ModuleRegistry.Ordered(s).Select(m => m.Id), animate);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"同步功能图标顺序失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 补上 <c>LvchaxsBS_Resources.dll</c> 之后调：重建当前页，让所有图片重新解析。
        /// <para>
        /// ★ 启动时缺资源包 ⇒ 页面上那些 `pack://…/LvchaxsBS_Resources;…` 的图全是空白的，
        ///   而且 **WPF 不会自己重试**。重建一次页面，图片就能重新加载出来。
        /// ★ 只重建有**无参构造**的页（首页 / 设置 / 个性化 / 语音都符合）；
        ///   配置容器页需要参数、指令编辑页是全屏页 —— 都跳过，用户切一次页面自然就好了。
        /// </para>
        /// </summary>
        public void ReloadCurrentPageForResource()
        {
            try
            {
                if (MainFrame.Content is not Page page) return;
                if ((page.Tag as string) == "FullScreen") return;

                var ctor = page.GetType().GetConstructor(Type.EmptyTypes);
                if (ctor == null) return;

                MainFrame.Navigate((Page)ctor.Invoke(null));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"重建当前页失败（资源热加载）：{ex.Message}");
            }
        }

        /// <summary>
        /// 可选组件补上后热加载：让顶部这一排立刻反映出新装的模块。
        /// <para>
        /// ★ 首页卡片不用管 —— <c>HomePage</c> 每次进入都会重建，自然会按新的
        ///   <c>ModuleRegistry.All</c> 生成卡片和标题行标签。
        /// </para>
        /// </summary>
        public void RefreshOptionalModules()
        {
            try
            {
                TopLeft.SetUidVisible(UidModuleService.IsAvailable);
                TopLeft.SetMacroVisible(MacroModuleService.IsAvailable);
                TopCenter.RefreshOptionalModules();
                SyncFunctionButtonOrder(animate: false);

                // ★★ 首页卡片是**构造 HomePage 时**按 ModuleRegistry.All 生成的 ——
                //    当前正显示首页的话必须重建，否则新装的模块卡片要等用户重新点一次「首页」
                //    才会出现（看起来就像"根本没热加载"）。
                if (MainFrame.Content is Pages.HomePage)
                    MainFrame.Navigate(new Pages.HomePage());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"刷新可选模块失败：{ex.Message}");
            }
        }

        // ============ 右侧栏事件 ============

        private void TopRight_ThemeToggleClicked(object? sender, EventArgs e)
        {
            // 主题切换的过渡就是「每个主题色自己渐变到新色」（见 ThemeService.Apply），整窗一起过渡。
            // 连点也不会打架：后一次会先把前一次落定，再从当前中间色接着渐变。
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
            else if (MainFrame.Content != null)
            {
                // ★★ 插件页面（如「自定义宏」的宏列表）也要跟随总开关变灰。
                //   约定：公开的**无参**方法 RefreshMasterSwitchVisual（和首页 / 配置容器页同名）。
                //   ★ 用反射而不是加接口：宿主不能引用插件类型（插件是反射加载的可选 dll）。
                MainFrame.Content.GetType()
                    .GetMethod("RefreshMasterSwitchVisual",
                               System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                               null, Type.EmptyTypes, null)
                    ?.Invoke(MainFrame.Content, null);
            }

            // 应用总开关状态：关闭时停止全部功能 + 暂停界面检测 + 隐藏图标窗口
            IconService.ApplyMasterSwitchState(TopRight.IsToggleOn);

            ShowToast($"功能总开关: {(TopRight.IsToggleOn ? "已开启" : "已关闭")}", TopRight.IsToggleOn);
        }

        private void TopRight_ScreenshotClicked(object? sender, EventArgs e)
        {
            // 已经开着（比如刚按了右 Shift 热键）就别再叠一个
            if (!ScreenshotToolWindow.TryEnter())
                return;

            try
            {
                ModalOverlayService.ShowModalDialog(new ScreenshotToolWindow());
            }
            catch (Exception ex)
            {
                ShowToast($"打开截图工具失败: {ex.Message}", false);
            }
            finally
            {
                ScreenshotToolWindow.Leave();
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

        /// <summary>
        /// 外层圆角裁剪用的几何。
        /// <para>
        /// ★ 只建一次、改 <see cref="RectangleGeometry.Rect"/>：<see cref="RootBorder_SizeChanged"/>
        ///   在拖窗口边缘时每帧都会来，原来每次都 new 一个 <see cref="RectangleGeometry"/> 换上去，
        ///   等于每帧一次分配 + 一次裁剪重建。复用同一个实例后视觉完全一致，但不再产生垃圾。
        /// </para>
        /// </summary>
        private RectangleGeometry? _rootClipGeometry;

        private void UpdateRootBorderClip()
        {
            if (RootBorder == null) return;

            double radius = RootBorder.CornerRadius.TopLeft;
            double w = RootBorder.ActualWidth;
            double h = RootBorder.ActualHeight;

            if (radius <= 0 || w <= 0 || h <= 0)
            {
                RootBorder.Clip = null;
                return;
            }

            _rootClipGeometry ??= new RectangleGeometry();
            _rootClipGeometry.RadiusX = radius;
            _rootClipGeometry.RadiusY = radius;
            _rootClipGeometry.Rect = new Rect(0, 0, w, h);

            // 只在还没挂上（或曾被摘掉）时赋值，避免每帧都触发一次 Clip 变更通知
            if (!ReferenceEquals(RootBorder.Clip, _rootClipGeometry))
                RootBorder.Clip = _rootClipGeometry;
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, null);
        }
    }
}