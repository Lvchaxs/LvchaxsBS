using LvchaxsBS.UI.Helpers;
using LvchaxsBS.Services;
using LvchaxsBS.Services.Common;
using LvchaxsBS.Services.Hooks;
using LvchaxsBS.Services.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LvchaxsBS.Core
{
    public static class SubtitleOverlayService
    {
        public enum SubtitleOwner
        {
            None,
            AutoCook,
            Fishing,
            AutoLumber,
            /// <summary>UID 模块的登录流程（第一行「正在登录 {账号} {计时}」）。</summary>
            UidLogin,
        }

        /// <summary>
        /// ★ 当前占用**第一行字幕**的模块。四行里只有第一行是多个模块共用的
        /// （钓鱼 / 自动烹饪 / 自动伐木 / UID 登录都挂在这一行上），所以要认主：
        /// 见 <see cref="HideSubtitle(SubtitleOwner)"/>。
        /// </summary>
        private static SubtitleOwner _subtitleOwner = SubtitleOwner.None;

        #region 物理像素基准常量

        /// <summary>
        /// ★ 字幕整体缩放倍率（头像 + 气泡 + 文字 + 行距，一次性等比缩放）。
        /// 1.0 = 原尺寸；1.1 = 整体放大 1.1 倍；0.9 = 整体缩小到 0.9 倍。
        /// 走 <see cref="GetScaleByResolution"/> 乘算 ⇒ 泡高/圆角/左侧尖勾的硬编码坐标会一起等比变化，形状不失真。
        /// 想单独微调某一项（头像大小 / 字号）时，再改下面对应的常量。
        /// </summary>
        private const double SUBTITLE_OVERALL_SCALE = 1.2;

        private const double AVATAR_SIZE = 113;
        private const double AVATAR_MARGIN_RIGHT = 10;

        private const double BUBBLE_HEIGHT = 83;
        private const double BUBBLE_CORNER_RADIUS = 42;
        private const double BUBBLE_TAIL_RADIUS = 42;
        private const double BUBBLE_TAIL_RADIUS_Y = 25;
        private const double BUBBLE_MIN_WIDTH = 210;
        private const double BUBBLE_TEXT_EXTRA = 88;

        private const double TEXT_FONT_SIZE = 29;
        private const double TEXT_MARGIN_LEFT = 54;
        private const double TEXT_MARGIN_RIGHT = 34;

        private const int FADE_IN_MS = 100;
        private const int FADE_OUT_MS = 100;

        private const int SECOND_SUBTITLE_OFFSET_Y = 130;

        /// <summary>第三行相对第一行的下移量（= 第二行的两倍，三行等距）。</summary>
        private const int THIRD_SUBTITLE_OFFSET_Y = 260;

        /// <summary>第四行相对第一行的下移量（= 第二行的三倍，四行等距）。</summary>
        private const int FOURTH_SUBTITLE_OFFSET_Y = 390;

        #endregion

        #region 字段

        private static readonly string[] _avatarFiles =
        {
            "空.png",
            "荧.png",
        };

        private static Window? _overlayWindow;
        private static bool _isOverlayVisible = false;
        private static bool _isFadingOut = false;

        private static StackPanel? _container;
        private static Image? _avatarImage;
        private static Path? _subtitleBorder;
        private static TextBlock? _subtitleText;

        private static DispatcherTimer? _overlayTimer;
        private const int TOPMOST_REFRESH_MS = 50;

        private static bool _isFocused = false;
        private static bool _isActive = false;

        private static int _currentWidth = 0;
        private static int _currentHeight = 0;

        private static string _positionKey = "";
        private static string _currentAvatarPath = "";

        /// <summary>
        /// 待显示的正文。★ 这个字段是必需的：覆盖层只在<b>游戏窗口成为前台</b>时才建窗
        /// （<c>ShowOverlay()</c> 里有 <c>if (!_isFocused) return;</c>），而宿主判定前台是
        /// <b>1 秒轮询</b>（<c>WindowFocusService.PollIntervalMs</c>）。所以「先把游戏切到前台、
        /// 紧接着 SetSubtitle」这种调用，会发生在建窗之前 —— 那时 <c>_subtitleText</c> 还是 null，
        /// 文本会被丢掉，表现为「头像出来了、气泡里没字」。
        /// 这里先记下来，<see cref="CreateWindowIfNeeded"/> 建好元素时补上。
        /// </summary>
        private static string _pendingSubtitleText = "";

        private static Window? _overlayWindow2;
        private static bool _isOverlayVisible2 = false;
        private static bool _isFadingOut2 = false;

        private static StackPanel? _container2;
        private static Image? _avatarImage2;
        private static Path? _subtitleBorder2;
        private static TextBlock? _subtitleText2;

        private static bool _isActive2 = false;
        private static string _currentAvatarPath2 = "";

        /// <summary>第二行字幕的待显示正文，作用同 <see cref="_pendingSubtitleText"/>。</summary>
        private static string _pendingSubtitleText2 = "";

        /// <summary>
        /// 第二行字幕是否显示头像。默认显示（主程序自己的用法）。
        /// UID 的登录流程只要文字（第二行不带头像），会把它关掉。
        /// 窗口可能还没建，所以这个标志先存着，建窗时按它决定 Visibility。
        /// </summary>
        private static bool _avatarVisible2 = true;

        // -------- 第三行（联机人数那一行；结构与第二行完全对称） --------
        private static Window? _overlayWindow3;
        private static bool _isOverlayVisible3 = false;
        private static bool _isFadingOut3 = false;

        private static StackPanel? _container3;
        private static Image? _avatarImage3;
        private static Path? _subtitleBorder3;
        private static TextBlock? _subtitleText3;

        private static bool _isActive3 = false;
        private static string _currentAvatarPath3 = "";

        /// <summary>第三行的待显示正文，作用同 <see cref="_pendingSubtitleText"/>。</summary>
        private static string _pendingSubtitleText3 = "";

        /// <summary>第三行是否显示头像（默认显示，与人数提示一起出现的是空/荧随机头像）。</summary>
        private static bool _avatarVisible3 = true;

        private static bool _hasRich3;
        private static string _richPrefix3 = "";
        private static string _richHighlight3 = "";
        private static string _richSuffix3 = "";

        // -------- 第四行（UID 的「用户正在操作，暂缓按键」提示；结构与第三行对称） --------
        // ★ 这一行不走"淡入淡出 + Hide"，它**常驻不销毁**：窗口建好后一直是纯透明
        //   （Opacity 0），要显示时直接把不透明度打上去 —— 触发即出现，不重建、不闪。
        //   覆盖层带 WS_EX_TRANSPARENT（鼠标穿透）+ WS_EX_NOACTIVATE，透明时既不挡点击也不抢焦点。
        private static Window? _overlayWindow4;
        private static bool _isOverlayVisible4 = false;

        private static StackPanel? _container4;
        private static Image? _avatarImage4;
        private static Path? _subtitleBorder4;
        private static TextBlock? _subtitleText4;

        /// <summary>第四行此刻要不要显示（= 用户正在操作、倒计时还没走完）。</summary>
        private static bool _isActive4 = false;
        private static string _currentAvatarPath4 = "";

        /// <summary>第四行的待显示正文，作用同 <see cref="_pendingSubtitleText"/>。</summary>
        private static string _pendingSubtitleText4 = "";

        /// <summary>
        /// 第四行是否显示头像。
        /// ★ 默认 **true**（2026-10-17 修：之前默认 false，而 UID 侧根本没有对应的开关命令，
        ///   结果第四行永远 <c>Visibility.Hidden</c> —— 就是"第四行没有头像"）。
        ///   它照样不参与三行头像的协调分配（<see cref="AssignAvatars"/> 也不管它），
        ///   只是在建窗 / 每次显示时按这里的值决定显不显示。
        /// </summary>
        private static bool _avatarVisible4 = true;

        #endregion

        #region 初始化

        public static void Initialize()
        {
            DpiService.Initialize();

            WindowFocusService.FocusChanged += OnFocusChanged;
            WindowFocusService.BoundsChanged += OnBoundsChanged;

            _isFocused = WindowFocusService.IsTargetFocused;
            var initBounds = WindowFocusService.LastBounds;
            if (!initBounds.IsEmpty)
            {
                _currentWidth = initBounds.Width;
                _currentHeight = initBounds.Height;
            }

            StartOverlayTimer();
        }

        public static void Shutdown()
        {
            WindowFocusService.FocusChanged -= OnFocusChanged;
            WindowFocusService.BoundsChanged -= OnBoundsChanged;
            StopOverlayTimer();
            CloseOverlay();
            CloseOverlay2();
            CloseOverlay3();
            CloseOverlay4();
        }

        public static void ResetAvatarForNewRun()
        {
            _currentAvatarPath = "";
            _currentAvatarPath2 = "";
            _currentAvatarPath3 = "";
            _currentAvatarPath4 = "";
        }

        #endregion

        #region 窗口焦点

        private static void OnFocusChanged(object? sender, bool focused)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // ★ 这里**不用事件带过来的 focused**，而是读此刻的真值：
                //   本回调是把一堆前台事件排进 Dispatcher 队列后逐个执行的，UI 线程一忙（例如
                //   UID 激活游戏窗口时的 Thread.Sleep）就会积压，轮到它时真实焦点可能早变了；
                //   照着过期参数写会把缓存钉在 false —— 症状是"字幕内容在刷新、画面上全看不到，
                //   只有再切一次焦点才回来"（自动换号时最容易撞上）。
                _isFocused = WindowFocusService.IsTargetFocused;
                UpdateOverlayVisibility();
                UpdateOverlayVisibility2();
                UpdateOverlayVisibility3();
                UpdateOverlayVisibility4();
            });
        }

        private static void OnBoundsChanged(object? sender, WindowFocusService.WindowBounds info)
        {
            if (info.IsEmpty) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (info.Width > 0 && info.Height > 0)
                {
                    bool resolutionChanged = (_currentWidth != info.Width || _currentHeight != info.Height);
                    _currentWidth = info.Width;
                    _currentHeight = info.Height;

                    if (resolutionChanged)
                    {
                        ApplySizes();
                        ApplySizes2();
                        ApplySizes3();
                        ApplySizes4();

                        Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            UpdateWindowPosition();
                            UpdateWindowPosition2();
                            UpdateWindowPosition3();
                            UpdateWindowPosition4();
                        }, DispatcherPriority.Render);
                    }
                }

                UpdateOverlayVisibility();
                UpdateOverlayVisibility2();
                UpdateOverlayVisibility3();
                UpdateOverlayVisibility4();
            });
        }

        /// <summary>
        /// ★★ 用**真实的前台窗口**复核一次焦点，并把它同步到本服务（连带刷新四行的显示 / 隐藏）。
        ///
        /// <para>
        /// 为什么需要它：本服务的 <c>_isFocused</c> 是缓存值，只跟着焦点事件更新，而
        /// <c>WindowFocusService</c> 是"事件驱动 + **非焦点时**才轮询自愈"。一旦两者错位
        /// （事件丢失 / 积压后迟到 / 短暂抖动没配成对），缓存就会停在 false，而自愈轮询压根不会启动
        /// ——字幕从此永远不满足 <c>_isActive &amp;&amp; _isFocused</c>，画面上一行也看不到。
        /// </para>
        /// <para>
        /// 调用时机：① 挂出一条新的字幕行时（UID 每次挂行、自动换号重挂三行），
        /// 让"要不要显示"用**当下**的真值判断一次。
        /// </para>
        /// </summary>
        public static void RefreshFocusState()
        {
            _isFocused = WindowFocusService.Reevaluate();
            UpdateOverlayVisibility();
            UpdateOverlayVisibility2();
            UpdateOverlayVisibility3();
            UpdateOverlayVisibility4();
        }

        /// <summary>
        /// ★ 焦点自愈（轻量版）：只在"这一行正在显示、却因为 <c>_isFocused</c> 为 false 而没露出来"
        /// 时才去复核一次真值。
        /// <para>挂在写文本的那几个入口上：计时按秒刷新第一行、人数按秒刷新第三行，
        /// 所以就算挂行那一刻焦点还没稳，下一秒也会自己浮出来，不用用户手动切焦点。</para>
        /// </summary>
        private static void SelfHealFocus(bool lineActive)
        {
            if (!lineActive || _isFocused) return;
            RefreshFocusState();
        }

        #endregion

        #region 定时刷新

        private static void StartOverlayTimer()
        {
            StopOverlayTimer();

            _overlayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TOPMOST_REFRESH_MS)
            };
            _overlayTimer.Tick += (s, e) => OverlayTick();
            _overlayTimer.Start();
        }

        private static void StopOverlayTimer()
        {
            if (_overlayTimer != null)
            {
                _overlayTimer.Stop();
                _overlayTimer = null;
            }
        }

        private static void OverlayTick()
        {
            if (_isOverlayVisible && _overlayWindow != null)
                UpdateWindowPosition();

            if (_isOverlayVisible2 && _overlayWindow2 != null)
                UpdateWindowPosition2();

            if (_isOverlayVisible3 && _overlayWindow3 != null)
                UpdateWindowPosition3();

            if (_isOverlayVisible4 && _overlayWindow4 != null)
                UpdateWindowPosition4();
        }

        #endregion

        #region 显示/隐藏（第一个字幕）

        private static void UpdateOverlayVisibility()
        {
            bool shouldShow = _isActive && _isFocused;

            if (shouldShow)
                ShowOverlay();
            else
                HideOverlay();
        }

        public static void SetPositionKey(string key)
        {
            _positionKey = key ?? "";
        }

        /// <summary>
        /// 显示第一行。<paramref name="owner"/> 非空时同时**认主**（见 <see cref="_subtitleOwner"/>）。
        /// </summary>
        public static void ShowSubtitle(SubtitleOwner owner = SubtitleOwner.None)
        {
            if (owner != SubtitleOwner.None)
                _subtitleOwner = owner;

            _isActive = true;
            UpdateOverlayVisibility();
        }

        /// <summary>
        /// 收起第一行。
        /// <para>
        /// ★ <paramref name="owner"/> 非空时**只收自己挂的那一行**：第一行是钓鱼 / 自动烹饪 /
        /// 自动伐木 / UID 登录共用的，而钓鱼的图标刷新（鱼竿状态、张力区回调，高频）在
        /// 「自己没在跑 / 文本为空」时会一路调到 <c>HideSubtitle()</c> ——
        /// 不认主的话它会把 UID 登录正显示的第一行整行收掉（2026-10-16 的 bug）。
        /// </para>
        /// <paramref name="owner"/> 为 <see cref="SubtitleOwner.None"/> 表示**强制收起**
        /// （TAB 隐藏、UID 自己重开一行等），不看 owner。
        /// </param>
        /// </summary>
        public static void HideSubtitle(SubtitleOwner owner = SubtitleOwner.None)
        {
            // 别的模块正占着第一行 → 这次收起无效（不能收别人的字幕）
            if (owner != SubtitleOwner.None && _subtitleOwner != SubtitleOwner.None && owner != _subtitleOwner)
                return;

            if (owner == SubtitleOwner.None || owner == _subtitleOwner)
                _subtitleOwner = SubtitleOwner.None;

            _isActive = false;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 收起时把待显示正文一并清掉，避免下次显示又把旧内容带出来
                _pendingSubtitleText = "";
                if (_subtitleText != null) _subtitleText.Text = "";
            });

            UpdateOverlayVisibility();
        }

        public static void ShowOverlay()
        {
            if (!_isActive) return;
            if (!_isFocused) return;

            // ★ 记住"刚才是不是正在淡出"，再把它清掉。
            bool wasFadingOut = _isFadingOut;
            _isFadingOut = false;

            if (_isOverlayVisible && _overlayWindow != null && _overlayWindow.IsVisible)
            {
                // ★★ 一定要带上 wasFadingOut：上一次 Hide 的淡出动画可能才刚起步 ——
                //   WPF 动画的第一帧要等下一次渲染才生效，此刻读到的 Opacity 还是 1.0，
                //   于是下面这个判断不成立，那条淡出**不会被打断**，会一路把 Opacity 拉到 0 并 HoldEnd 停住；
                //   而它的 Completed 里 `if (!_isFadingOut) return;` 又被上面那句提前短路了
                //   （没人 Hide、也没人把不透明度拉回来）。
                //   ⇒ 窗口还在、文本照常刷新，画面上却完全透明 —— 就是"字幕全没了"
                //   （只有焦点变化让 ShowOverlay 重入一次，此时 Opacity 已降到 0 才终于淡入）。
                //   挂一行字幕的动作是「先收起再显示」（同一个 tick，必然撞上）⇒ 必须主动顶掉那条动画。
                if (wasFadingOut || _overlayWindow.Opacity < 1.0)
                {
                    StartFadeIn();
                }
                return;
            }

            CreateWindowIfNeeded();
            RefreshAvatarIfNeeded();
            ApplySizes();

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                UpdateWindowPosition();
            }, DispatcherPriority.Render);

            if (_overlayWindow != null && !_overlayWindow.IsVisible)
            {
                _overlayWindow.Opacity = 0;
                _overlayWindow.Show();
                _isOverlayVisible = true;
                StartFadeIn();
            }
        }

        public static void HideOverlay()
        {
            if (_overlayWindow == null || !_overlayWindow.IsVisible) return;
            if (_isFadingOut) return;

            _isFadingOut = true;

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(FADE_OUT_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            fadeOut.Completed += (s, e) =>
            {
                if (!_isFadingOut) return;

                _isFadingOut = false;

                if (_overlayWindow != null && _overlayWindow.IsVisible)
                {
                    _overlayWindow.Hide();
                    _overlayWindow.Opacity = 1;
                }
                _isOverlayVisible = false;
            };

            _overlayWindow.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private static void StartFadeIn()
        {
            if (_overlayWindow == null) return;

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(FADE_IN_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            _overlayWindow.BeginAnimation(Window.OpacityProperty, fadeIn);
            _overlayWindow.Opacity = 1;
        }

        #endregion

        #region 字幕文本（第一个）

        public static void SetSubtitle(string text)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // ★ 该显示却没显示时用真值复核一次焦点（详见 SelfHealFocus）
                SelfHealFocus(_isActive);

                // 纯文本会把富文本（账号名高亮）整个覆盖掉，状态一并清掉
                ClearRich();

                if (!_isActive)
                {
                    _pendingSubtitleText = "";
                    if (_subtitleText != null) _subtitleText.Text = "";
                    return;
                }

                // 先记下来：窗口可能还没建（游戏尚未被判定为前台），建窗时会补上
                _pendingSubtitleText = text ?? string.Empty;

                if (_subtitleText == null) return;

                _subtitleText.Text = _pendingSubtitleText;
                UpdateBubbleWidth();
            });
        }

        #region 富文本（账号名高亮）

        /// <summary>
        /// 高亮色（账号名）。字幕铺在游戏画面上、气泡底是深色半透明，
        /// 所以用固定的亮黄，不跟主题画刷走（主题色压在游戏画面上看不清）。
        /// </summary>
        private static readonly System.Windows.Media.SolidColorBrush HighlightBrush =
            new(System.Windows.Media.Color.FromRgb(0xFF, 0xD8, 0x3B));

        // 富文本三段的待生效值：窗口还没建时先记着，建窗时按它补（同 _pendingSubtitleText 的思路）
        private static bool _hasRich;
        private static string _richPrefix = "";
        private static string _richHighlight = "";
        private static string _richSuffix = "";

        /// <summary>
        /// 第一行设成三段富文本：<paramref name="highlight"/>（账号名）用高亮色，前后两段照常白色。
        /// ★ 之后若调用 <see cref="SetSubtitle"/>，富文本会被纯文本覆盖、状态一并清掉。
        /// ★ 气泡宽度不受影响：<see cref="TextBlock.Text"/> 在 Inlines 模式下就等于各段的拼接。
        /// </summary>
        public static void SetSubtitleRich(string prefix, string highlight, string suffix)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SelfHealFocus(_isActive);

                if (!_isActive)
                {
                    ClearRich();
                    if (_subtitleText != null) _subtitleText.Text = "";
                    return;
                }

                _hasRich = true;
                _richPrefix = prefix ?? "";
                _richHighlight = highlight ?? "";
                _richSuffix = suffix ?? "";
                _pendingSubtitleText = _richPrefix + _richHighlight + _richSuffix;

                ApplyRich(_subtitleText, _richPrefix, _richHighlight, _richSuffix);
                UpdateBubbleWidth();
            });
        }

        /// <summary>把三段写进 TextBlock 的 Inlines（窗口未建时什么都不做，建窗时会按记下的值补上）。</summary>
        private static void ApplyRich(TextBlock? textBlock, string prefix, string highlight, string suffix)
        {
            if (textBlock == null) return;

            textBlock.Inlines.Clear();
            if (!string.IsNullOrEmpty(prefix))
                textBlock.Inlines.Add(new Run(prefix));
            if (!string.IsNullOrEmpty(highlight))
                textBlock.Inlines.Add(new Run(highlight) { Foreground = HighlightBrush });
            if (!string.IsNullOrEmpty(suffix))
                textBlock.Inlines.Add(new Run(suffix));
        }

        private static void ClearRich()
        {
            _hasRich = false;
            _richPrefix = _richHighlight = _richSuffix = "";
        }

        #endregion

        public static void AppendSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                string cur = _subtitleText?.Text ?? _pendingSubtitleText ?? "";
                if (cur.EndsWith(suffix)) return;

                _pendingSubtitleText = cur + suffix;

                if (_subtitleText == null) return;

                _subtitleText.Text = _pendingSubtitleText;
                UpdateBubbleWidth();
            });
        }

        public static string GetSubtitle()
        {
            string result = "";
            Application.Current?.Dispatcher.Invoke(() =>
            {
                result = _subtitleText?.Text ?? "";
            });
            return result;
        }

        public static void SetBackgroundOpacity(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            byte alpha = (byte)Math.Round(percent * 255.0 / 100.0);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_subtitleBorder != null)
                {
                    _subtitleBorder.Fill = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                }
            });
        }

        #endregion

        #region 显示/隐藏（第二个字幕）

        private static void UpdateOverlayVisibility2()
        {
            bool shouldShow = _isActive2 && _isFocused;

            if (shouldShow)
                ShowOverlay2();
            else
                HideOverlay2();
        }

        public static void ShowSubtitle2()
        {
            _isActive2 = true;
            UpdateOverlayVisibility2();
        }

        public static void HideSubtitle2()
        {
            _isActive2 = false;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 同 HideSubtitle：清掉待显示正文
                _pendingSubtitleText2 = "";
                if (_subtitleText2 != null) _subtitleText2.Text = "";
            });

            UpdateOverlayVisibility2();
        }

        public static void ShowOverlay2()
        {
            if (!_isActive2) return;
            if (!_isFocused) return;

            // 同 ShowOverlay：正在淡出时也要重新淡入，把没跑完的那条淡出动画顶掉
            bool wasFadingOut2 = _isFadingOut2;
            _isFadingOut2 = false;

            if (_isOverlayVisible2 && _overlayWindow2 != null && _overlayWindow2.IsVisible)
            {
                if (wasFadingOut2 || _overlayWindow2.Opacity < 1.0)
                {
                    StartFadeIn2();
                }
                return;
            }

            CreateWindowIfNeeded2();
            RefreshAvatarIfNeeded2();
            ApplySizes2();

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                UpdateWindowPosition2();
            }, DispatcherPriority.Render);

            if (_overlayWindow2 != null && !_overlayWindow2.IsVisible)
            {
                _overlayWindow2.Opacity = 0;
                _overlayWindow2.Show();
                _isOverlayVisible2 = true;
                StartFadeIn2();
            }
        }

        public static void HideOverlay2()
        {
            if (_overlayWindow2 == null || !_overlayWindow2.IsVisible) return;
            if (_isFadingOut2) return;

            _isFadingOut2 = true;

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(FADE_OUT_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            fadeOut.Completed += (s, e) =>
            {
                if (!_isFadingOut2) return;

                _isFadingOut2 = false;

                if (_overlayWindow2 != null && _overlayWindow2.IsVisible)
                {
                    _overlayWindow2.Hide();
                    _overlayWindow2.Opacity = 1;
                }
                _isOverlayVisible2 = false;
            };

            _overlayWindow2.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private static void StartFadeIn2()
        {
            if (_overlayWindow2 == null) return;

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(FADE_IN_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            _overlayWindow2.BeginAnimation(Window.OpacityProperty, fadeIn);
            _overlayWindow2.Opacity = 1;
        }

        #endregion

        #region 字幕文本（第二个）

        public static void SetSubtitle2(string text)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SelfHealFocus(_isActive2);

                // 纯文本会把富文本整个覆盖掉，状态一并清掉
                ClearRich2();

                if (!_isActive2)
                {
                    _pendingSubtitleText2 = "";
                    if (_subtitleText2 != null) _subtitleText2.Text = "";
                    return;
                }

                // 同 SetSubtitle：窗口可能还没建，先记下来
                _pendingSubtitleText2 = text ?? string.Empty;

                if (_subtitleText2 == null) return;

                _subtitleText2.Text = _pendingSubtitleText2;
                UpdateBubbleWidth2();
            });
        }

        #region 富文本（第二个）

        // 同第一行的 _hasRich 一套：窗口还没建时先记着，建窗时按它补
        private static bool _hasRich2;
        private static string _richPrefix2 = "";
        private static string _richHighlight2 = "";
        private static string _richSuffix2 = "";

        /// <summary>
        /// 第二行设成三段富文本：<paramref name="highlight"/> 用高亮色，前后两段照常白色。
        /// ★ 之后若调用 <see cref="SetSubtitle2"/>，富文本会被纯文本覆盖、状态一并清掉。
        /// </summary>
        public static void SetSubtitleRich2(string prefix, string highlight, string suffix)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SelfHealFocus(_isActive2);

                if (!_isActive2)
                {
                    ClearRich2();
                    if (_subtitleText2 != null) _subtitleText2.Text = "";
                    return;
                }

                _hasRich2 = true;
                _richPrefix2 = prefix ?? "";
                _richHighlight2 = highlight ?? "";
                _richSuffix2 = suffix ?? "";
                _pendingSubtitleText2 = _richPrefix2 + _richHighlight2 + _richSuffix2;

                ApplyRich(_subtitleText2, _richPrefix2, _richHighlight2, _richSuffix2);
                UpdateBubbleWidth2();
            });
        }

        private static void ClearRich2()
        {
            _hasRich2 = false;
            _richPrefix2 = _richHighlight2 = _richSuffix2 = "";
        }

        #endregion

        public static void AppendSuffix2(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                string cur = _subtitleText2?.Text ?? _pendingSubtitleText2 ?? "";
                if (cur.EndsWith(suffix)) return;

                _pendingSubtitleText2 = cur + suffix;

                if (_subtitleText2 == null) return;

                _subtitleText2.Text = _pendingSubtitleText2;
                UpdateBubbleWidth2();
            });
        }

        public static string GetSubtitle2()
        {
            string result = "";
            Application.Current?.Dispatcher.Invoke(() =>
            {
                result = _subtitleText2?.Text ?? "";
            });
            return result;
        }

        #endregion

        #region 尺寸计算

        private static double GetScaleByResolution()
        {
            // ★ 乘整体倍率：所有字幕尺寸（含 BuildBubbleGeometry 里按泡高硬编码的尖勾坐标）都从这里取 scale
            return ResolutionScaleHelper.GetScale(_currentWidth, _currentHeight) * SUBTITLE_OVERALL_SCALE;
        }

        private static void ApplySizes()
        {
            if (_avatarImage == null || _subtitleText == null) return;

            double scale = GetScaleByResolution();

            double avatarPhys = AVATAR_SIZE * scale;
            double avatarMarginRightPhys = AVATAR_MARGIN_RIGHT * scale;
            double fontPhys = TEXT_FONT_SIZE * scale;
            double textMarginLeftPhys = TEXT_MARGIN_LEFT * scale;
            double textMarginRightPhys = TEXT_MARGIN_RIGHT * scale;

            _avatarImage.Width = DpiService.PhysicalToDipX(avatarPhys);
            _avatarImage.Height = DpiService.PhysicalToDipY(avatarPhys);
            _avatarImage.Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(avatarMarginRightPhys), 0);

            _subtitleText.FontSize = DpiService.PhysicalToDipY(fontPhys);
            _subtitleText.Margin = new Thickness(
                DpiService.PhysicalToDipX(textMarginLeftPhys), 0,
                DpiService.PhysicalToDipX(textMarginRightPhys), 0);

            UpdateBubbleWidth();
        }

        private static void ApplySizes2()
        {
            if (_avatarImage2 == null || _subtitleText2 == null) return;

            double scale = GetScaleByResolution();

            double avatarPhys = AVATAR_SIZE * scale;
            double avatarMarginRightPhys = AVATAR_MARGIN_RIGHT * scale;
            double fontPhys = TEXT_FONT_SIZE * scale;
            double textMarginLeftPhys = TEXT_MARGIN_LEFT * scale;
            double textMarginRightPhys = TEXT_MARGIN_RIGHT * scale;

            _avatarImage2.Width = DpiService.PhysicalToDipX(avatarPhys);
            _avatarImage2.Height = DpiService.PhysicalToDipY(avatarPhys);
            _avatarImage2.Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(avatarMarginRightPhys), 0);

            _subtitleText2.FontSize = DpiService.PhysicalToDipY(fontPhys);
            _subtitleText2.Margin = new Thickness(
                DpiService.PhysicalToDipX(textMarginLeftPhys), 0,
                DpiService.PhysicalToDipX(textMarginRightPhys), 0);

            UpdateBubbleWidth2();
        }

        private static void UpdateBubbleWidth()
        {
            if (_subtitleBorder == null || _subtitleText == null) return;
            _subtitleBorder.Data = BuildBubbleGeometry(_subtitleText);
        }

        private static void UpdateBubbleWidth2()
        {
            if (_subtitleBorder2 == null || _subtitleText2 == null) return;
            _subtitleBorder2.Data = BuildBubbleGeometry(_subtitleText2);
        }

        private static Geometry BuildBubbleGeometry(TextBlock textBlock)
        {
            double scale = GetScaleByResolution();

            string text = textBlock.Text ?? "";

            double textWidth;
            try
            {
                var formattedText = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                    textBlock.FontSize,
                    Brushes.White,
                    new NumberSubstitution(),
                    TextFormattingMode.Ideal);

                textWidth = formattedText.Width;
            }
            catch
            {
                textWidth = 0;
                foreach (char c in text)
                {
                    if (c > 127) textWidth += 29;
                    else textWidth += 16;
                }
            }

            double textWidthPhys = DpiService.DipToPhysicalX(textWidth);

            double targetWidthPhys = Math.Max(BUBBLE_MIN_WIDTH * scale,
                                              textWidthPhys + BUBBLE_TEXT_EXTRA * scale);
            double hPhys = BUBBLE_HEIGHT * scale;
            double rPhys = BUBBLE_CORNER_RADIUS * scale;

            double tw = DpiService.PhysicalToDipX(targetWidthPhys);
            double h = DpiService.PhysicalToDipY(hPhys);
            double r = DpiService.PhysicalToDipX(rPhys);

            double xTail = DpiService.PhysicalToDipX(62 * scale);
            double xNotch1 = DpiService.PhysicalToDipX(24 * scale);
            double xNotch2 = DpiService.PhysicalToDipX(17 * scale);
            double xNotch3 = DpiService.PhysicalToDipX(59 * scale);

            double yNotch1 = DpiService.PhysicalToDipY(67 * scale);
            double yNotch2 = DpiService.PhysicalToDipY(52 * scale);
            double yNotch3 = DpiService.PhysicalToDipY(45 * scale);

            double rxTail = DpiService.PhysicalToDipX(BUBBLE_TAIL_RADIUS * scale);
            double ryTail = DpiService.PhysicalToDipY(BUBBLE_TAIL_RADIUS * scale);
            double ryTail2 = DpiService.PhysicalToDipY(BUBBLE_TAIL_RADIUS_Y * scale);

            string newData =
                $"M {xTail},0 " +
                $"L {tw - r},0 " +
                $"A {r},{r} 0 0,1 {tw},{r} " +
                $"L {tw},{h - r} " +
                $"A {r},{r} 0 0,1 {tw - r},{h} " +
                $"L {xTail},{h} " +
                $"A {rxTail},{ryTail} 0 0,1 {xNotch1},{yNotch1} " +
                $"A {rxTail},{ryTail2} 0 0,1 0,{yNotch2} " +
                $"A {rxTail},{ryTail} 0 0,0 {xNotch2},{yNotch3} " +
                $"A {rxTail},{ryTail} 0 0,1 {xNotch3},0 Z";

            return Geometry.Parse(newData);
        }

        #endregion

        #region 头像

        private static string PickRandomAvatarPath()
        {
            var random = new Random();
            string file = _avatarFiles[random.Next(_avatarFiles.Length)];
            return AssetLocator.Pack($"Template/角色头像/150x150/{file}");
        }

        private static string PickOtherAvatarPath(string otherPath)
        {
            foreach (var file in _avatarFiles)
            {
                string path = AssetLocator.Pack($"Template/角色头像/150x150/{file}");
                if (path != otherPath)
                    return path;
            }

            return PickRandomAvatarPath();
        }

        private static void RefreshAvatarIfNeeded()
        {
            if (_avatarImage == null) return;

            if (string.IsNullOrEmpty(_currentAvatarPath))
            {
                _currentAvatarPath = PickRandomAvatarPath();
            }

            _avatarImage.Source = ResourceImageHelper.Load(_currentAvatarPath);
        }

        private static void RefreshAvatarIfNeeded2()
        {
            if (_avatarImage2 == null) return;

            if (string.IsNullOrEmpty(_currentAvatarPath2))
            {
                _currentAvatarPath2 = PickOtherAvatarPath(_currentAvatarPath);
            }

            _avatarImage2.Source = ResourceImageHelper.Load(_currentAvatarPath2);
        }

        /// <summary>
        /// 设置第二行字幕是否显示头像。UID 的登录流程只要文字，会传 false。
        /// 窗口可能还没建，标志先存着，<see cref="CreateWindowIfNeeded2"/> 建窗时按它设 Visibility。
        /// 隐藏时用 <see cref="Visibility.Hidden"/>（保留布局宽度占位），两行字幕才能左对齐。
        /// </summary>
        public static void SetAvatarVisible2(bool visible)
        {
            _avatarVisible2 = visible;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_avatarImage2 != null)
                    _avatarImage2.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            });
        }

        #endregion

        #region 创建窗口

        private static void CreateWindowIfNeeded()
        {
            if (_overlayWindow != null) return;

            _overlayWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false,
                Opacity = 0
            };

            var helper = new WindowInteropHelper(_overlayWindow);
            helper.EnsureHandle();
            OverlayWindowService.ApplyOverlayStyle(_overlayWindow);

            _container = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.Transparent
            };

            if (string.IsNullOrEmpty(_currentAvatarPath))
            {
                _currentAvatarPath = PickRandomAvatarPath();
            }

            _avatarImage = new Image
            {
                Width = DpiService.PhysicalToDipX(AVATAR_SIZE),
                Height = DpiService.PhysicalToDipY(AVATAR_SIZE),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(AVATAR_MARGIN_RIGHT), 0),
                Source = ResourceImageHelper.Load(_currentAvatarPath),
                Visibility = Visibility.Visible
            };
            RenderOptions.SetBitmapScalingMode(_avatarImage, BitmapScalingMode.HighQuality);
            _container.Children.Add(_avatarImage);

            var bubbleGrid = new Grid
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            _subtitleBorder = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)),
                Stretch = Stretch.None,
                Data = Geometry.Parse(
                    "M 62,0 L 168,0 A 42,42 0 0,1 210,42 L 210,45 A 42,42 0 0,1 168,87 L 62,87 " +
                    "A 42,42 0 0,1 24,67 A 42,25 0 0,1 0,52 A 42,42 0 0,0 17,45 A 42,42 0 0,1 59,0 Z")
            };
            bubbleGrid.Children.Add(_subtitleBorder);

            _subtitleText = new TextBlock
            {
                // 建窗可能晚于 SetSubtitle（见 _pendingSubtitleText 注释），这里把已记下的正文补上
                Text = _pendingSubtitleText ?? "",
                FontSize = DpiService.PhysicalToDipY(TEXT_FONT_SIZE),
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(
                    DpiService.PhysicalToDipX(TEXT_MARGIN_LEFT), 0,
                    DpiService.PhysicalToDipX(TEXT_MARGIN_RIGHT), 0)
            };
            bubbleGrid.Children.Add(_subtitleText);

            // 之前设置过富文本（账号名高亮）就按三段补上，否则只剩初始化时那句兜底纯文本
            if (_hasRich)
                ApplyRich(_subtitleText, _richPrefix, _richHighlight, _richSuffix);

            _container.Children.Add(bubbleGrid);

            _overlayWindow.Content = _container;
            _overlayWindow.MouseLeftButtonDown += (s, e) => HideOverlay();

            ApplySizes();
        }

        private static void CreateWindowIfNeeded2()
        {
            if (_overlayWindow2 != null) return;

            _overlayWindow2 = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false,
                Opacity = 0
            };

            var helper = new WindowInteropHelper(_overlayWindow2);
            helper.EnsureHandle();
            OverlayWindowService.ApplyOverlayStyle(_overlayWindow2);

            _container2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.Transparent
            };

            if (string.IsNullOrEmpty(_currentAvatarPath2))
            {
                _currentAvatarPath2 = PickOtherAvatarPath(_currentAvatarPath);
            }

            _avatarImage2 = new Image
            {
                Width = DpiService.PhysicalToDipX(AVATAR_SIZE),
                Height = DpiService.PhysicalToDipY(AVATAR_SIZE),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(AVATAR_MARGIN_RIGHT), 0),
                Source = ResourceImageHelper.Load(_currentAvatarPath2),
                // UID 的登录字幕不要头像（见 SetAvatarVisible2）。
                // 用 Hidden 而不是 Collapsed：保留布局宽度做占位，两行气泡才能左对齐。
                Visibility = _avatarVisible2 ? Visibility.Visible : Visibility.Hidden
            };
            RenderOptions.SetBitmapScalingMode(_avatarImage2, BitmapScalingMode.HighQuality);
            _container2.Children.Add(_avatarImage2);

            var bubbleGrid2 = new Grid
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            _subtitleBorder2 = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)),
                Stretch = Stretch.None,
                Data = Geometry.Parse(
                    "M 62,0 L 168,0 A 42,42 0 0,1 210,42 L 210,45 A 42,42 0 0,1 168,87 L 62,87 " +
                    "A 42,42 0 0,1 24,67 A 42,25 0 0,1 0,52 A 42,42 0 0,0 17,45 A 42,42 0 0,1 59,0 Z")
            };
            bubbleGrid2.Children.Add(_subtitleBorder2);

            _subtitleText2 = new TextBlock
            {
                // 同 _subtitleText：建窗晚于 SetSubtitle2 时补上已记下的正文
                Text = _pendingSubtitleText2 ?? "",
                FontSize = DpiService.PhysicalToDipY(TEXT_FONT_SIZE),
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(
                    DpiService.PhysicalToDipX(TEXT_MARGIN_LEFT), 0,
                    DpiService.PhysicalToDipX(TEXT_MARGIN_RIGHT), 0)
            };
            bubbleGrid2.Children.Add(_subtitleText2);

            // 同第一行：之前设置过富文本就按三段补上
            if (_hasRich2)
                ApplyRich(_subtitleText2, _richPrefix2, _richHighlight2, _richSuffix2);

            _container2.Children.Add(bubbleGrid2);

            _overlayWindow2.Content = _container2;
            _overlayWindow2.MouseLeftButtonDown += (s, e) => HideOverlay2();

            ApplySizes2();
        }

        #endregion

        #region 窗口定位

        private static void UpdateWindowPosition()
        {
            if (_overlayWindow == null) return;
            if (string.IsNullOrEmpty(_positionKey)) return;

            var cachedPosObj = CoordinateFormats.GetCached<object>(_positionKey);
            if (cachedPosObj == null) return;

            var cachedPos = (CoordinateFormats.Point)cachedPosObj;

            double wDip = _overlayWindow.ActualWidth > 0 ? _overlayWindow.ActualWidth : 300;
            double hDip = _overlayWindow.ActualHeight > 0 ? _overlayWindow.ActualHeight : 80;

            double wPhys = DpiService.DipToPhysicalX(wDip);
            double hPhys = DpiService.DipToPhysicalY(hDip);

            OverlayWindowService.SetBounds(_overlayWindow,
                (int)cachedPos.X, (int)cachedPos.Y,
                (int)wPhys, (int)hPhys);
        }

        private static void UpdateWindowPosition2()
        {
            if (_overlayWindow2 == null) return;
            if (string.IsNullOrEmpty(_positionKey)) return;

            var cachedPosObj = CoordinateFormats.GetCached<object>(_positionKey);
            if (cachedPosObj == null) return;

            var cachedPos = (CoordinateFormats.Point)cachedPosObj;

            double wDip = _overlayWindow2.ActualWidth > 0 ? _overlayWindow2.ActualWidth : 300;
            double hDip = _overlayWindow2.ActualHeight > 0 ? _overlayWindow2.ActualHeight : 80;

            double wPhys = DpiService.DipToPhysicalX(wDip);
            double hPhys = DpiService.DipToPhysicalY(hDip);

            double scale = GetScaleByResolution();
            int offsetY = (int)(SECOND_SUBTITLE_OFFSET_Y * scale);

            OverlayWindowService.SetBounds(_overlayWindow2,
                (int)cachedPos.X, (int)cachedPos.Y + offsetY,
                (int)wPhys, (int)hPhys);
        }

        #endregion

        #region 第三行字幕（联机人数提示）

        // ★ 这一行的用途：UID 在「登录后打开聊天界面」时**常驻**显示当前联机人数
        //   （当前人数： 2/4 / 双人联机： 1/4 未达标 …）。它是第三行，结构与前两行完全对称，
        //   只是窗口位置再多往下偏移一格（THIRD_SUBTITLE_OFFSET_Y）。

        private static void UpdateOverlayVisibility3()
        {
            bool shouldShow = _isActive3 && _isFocused;

            if (shouldShow)
                ShowOverlay3();
            else
                HideOverlay3();
        }

        public static void ShowSubtitle3()
        {
            _isActive3 = true;
            UpdateOverlayVisibility3();
        }

        public static void HideSubtitle3()
        {
            _isActive3 = false;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _pendingSubtitleText3 = "";
                if (_subtitleText3 != null) _subtitleText3.Text = "";
            });

            UpdateOverlayVisibility3();
        }

        public static void ShowOverlay3()
        {
            if (!_isActive3) return;
            if (!_isFocused) return;

            // 同 ShowOverlay：正在淡出时也要重新淡入，把没跑完的那条淡出动画顶掉
            bool wasFadingOut3 = _isFadingOut3;
            _isFadingOut3 = false;

            if (_isOverlayVisible3 && _overlayWindow3 != null && _overlayWindow3.IsVisible)
            {
                if (wasFadingOut3 || _overlayWindow3.Opacity < 1.0)
                {
                    StartFadeIn3();
                }
                return;
            }

            CreateWindowIfNeeded3();
            RefreshAvatarIfNeeded3();
            ApplySizes3();

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                UpdateWindowPosition3();
            }, DispatcherPriority.Render);

            if (_overlayWindow3 != null && !_overlayWindow3.IsVisible)
            {
                _overlayWindow3.Opacity = 0;
                _overlayWindow3.Show();
                _isOverlayVisible3 = true;
                StartFadeIn3();
            }
        }

        public static void HideOverlay3()
        {
            if (_overlayWindow3 == null || !_overlayWindow3.IsVisible) return;
            if (_isFadingOut3) return;

            _isFadingOut3 = true;

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(FADE_OUT_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            fadeOut.Completed += (s, e) =>
            {
                if (!_isFadingOut3) return;

                _isFadingOut3 = false;

                if (_overlayWindow3 != null && _overlayWindow3.IsVisible)
                {
                    _overlayWindow3.Hide();
                    _overlayWindow3.Opacity = 1;
                }
                _isOverlayVisible3 = false;
            };

            _overlayWindow3.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private static void StartFadeIn3()
        {
            if (_overlayWindow3 == null) return;

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(FADE_IN_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            _overlayWindow3.BeginAnimation(Window.OpacityProperty, fadeIn);
            _overlayWindow3.Opacity = 1;
        }

        public static void SetSubtitle3(string text)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SelfHealFocus(_isActive3);

                ClearRich3();

                if (!_isActive3)
                {
                    _pendingSubtitleText3 = "";
                    if (_subtitleText3 != null) _subtitleText3.Text = "";
                    return;
                }

                _pendingSubtitleText3 = text ?? string.Empty;

                if (_subtitleText3 == null) return;

                _subtitleText3.Text = _pendingSubtitleText3;
                UpdateBubbleWidth3();
            });
        }

        /// <summary>第三行设成三段富文本（高亮段用黄色）。纯文本 <see cref="SetSubtitle3"/> 会覆盖它。</summary>
        public static void SetSubtitleRich3(string prefix, string highlight, string suffix)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SelfHealFocus(_isActive3);

                if (!_isActive3)
                {
                    ClearRich3();
                    if (_subtitleText3 != null) _subtitleText3.Text = "";
                    return;
                }

                _hasRich3 = true;
                _richPrefix3 = prefix ?? "";
                _richHighlight3 = highlight ?? "";
                _richSuffix3 = suffix ?? "";
                _pendingSubtitleText3 = _richPrefix3 + _richHighlight3 + _richSuffix3;

                ApplyRich(_subtitleText3, _richPrefix3, _richHighlight3, _richSuffix3);
                UpdateBubbleWidth3();
            });
        }

        private static void ClearRich3()
        {
            _hasRich3 = false;
            _richPrefix3 = _richHighlight3 = _richSuffix3 = "";
        }

        public static string GetSubtitle3()
        {
            string result = "";
            Application.Current?.Dispatcher.Invoke(() =>
            {
                result = _subtitleText3?.Text ?? "";
            });
            return result;
        }

        private static void ApplySizes3()
        {
            if (_avatarImage3 == null || _subtitleText3 == null) return;

            double scale = GetScaleByResolution();

            double avatarPhys = AVATAR_SIZE * scale;
            double avatarMarginRightPhys = AVATAR_MARGIN_RIGHT * scale;
            double fontPhys = TEXT_FONT_SIZE * scale;
            double textMarginLeftPhys = TEXT_MARGIN_LEFT * scale;
            double textMarginRightPhys = TEXT_MARGIN_RIGHT * scale;

            _avatarImage3.Width = DpiService.PhysicalToDipX(avatarPhys);
            _avatarImage3.Height = DpiService.PhysicalToDipY(avatarPhys);
            _avatarImage3.Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(avatarMarginRightPhys), 0);

            _subtitleText3.FontSize = DpiService.PhysicalToDipY(fontPhys);
            _subtitleText3.Margin = new Thickness(
                DpiService.PhysicalToDipX(textMarginLeftPhys), 0,
                DpiService.PhysicalToDipX(textMarginRightPhys), 0);

            UpdateBubbleWidth3();
        }

        private static void ApplySizes4()
        {
            if (_avatarImage4 == null || _subtitleText4 == null) return;

            double scale = GetScaleByResolution();

            double avatarPhys = AVATAR_SIZE * scale;
            double avatarMarginRightPhys = AVATAR_MARGIN_RIGHT * scale;
            double fontPhys = TEXT_FONT_SIZE * scale;
            double textMarginLeftPhys = TEXT_MARGIN_LEFT * scale;
            double textMarginRightPhys = TEXT_MARGIN_RIGHT * scale;

            _avatarImage4.Width = DpiService.PhysicalToDipX(avatarPhys);
            _avatarImage4.Height = DpiService.PhysicalToDipY(avatarPhys);
            _avatarImage4.Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(avatarMarginRightPhys), 0);

            _subtitleText4.FontSize = DpiService.PhysicalToDipY(fontPhys);
            _subtitleText4.Margin = new Thickness(
                DpiService.PhysicalToDipX(textMarginLeftPhys), 0,
                DpiService.PhysicalToDipX(textMarginRightPhys), 0);

            UpdateBubbleWidth4();
        }

        private static void UpdateBubbleWidth3()
        {
            if (_subtitleBorder3 == null || _subtitleText3 == null) return;
            _subtitleBorder3.Data = BuildBubbleGeometry(_subtitleText3);
        }

        private static void UpdateBubbleWidth4()
        {
            if (_subtitleBorder4 == null || _subtitleText4 == null) return;
            _subtitleBorder4.Data = BuildBubbleGeometry(_subtitleText4);
        }

        /// <summary>设置第三行是否显示头像（默认显示）。</summary>
        public static void SetAvatarVisible3(bool visible)
        {
            _avatarVisible3 = visible;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_avatarImage3 != null)
                    _avatarImage3.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            });
        }

        /// <summary>设置第四行是否显示头像（默认显示）。</summary>
        public static void SetAvatarVisible4(bool visible)
        {
            _avatarVisible4 = visible;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_avatarImage4 != null)
                    _avatarImage4.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            });
        }

        private static void CreateWindowIfNeeded3()
        {
            if (_overlayWindow3 != null) return;

            _overlayWindow3 = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false,
                Opacity = 0
            };

            var helper = new WindowInteropHelper(_overlayWindow3);
            helper.EnsureHandle();
            OverlayWindowService.ApplyOverlayStyle(_overlayWindow3);

            _container3 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.Transparent
            };

            if (string.IsNullOrEmpty(_currentAvatarPath3))
            {
                _currentAvatarPath3 = PickRandomAvatarPath();
            }

            _avatarImage3 = new Image
            {
                Width = DpiService.PhysicalToDipX(AVATAR_SIZE),
                Height = DpiService.PhysicalToDipY(AVATAR_SIZE),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(AVATAR_MARGIN_RIGHT), 0),
                Source = ResourceImageHelper.Load(_currentAvatarPath3),
                Visibility = _avatarVisible3 ? Visibility.Visible : Visibility.Hidden
            };
            RenderOptions.SetBitmapScalingMode(_avatarImage3, BitmapScalingMode.HighQuality);
            _container3.Children.Add(_avatarImage3);

            var bubbleGrid3 = new Grid
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            _subtitleBorder3 = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)),
                Stretch = Stretch.None,
                Data = Geometry.Parse(
                    "M 62,0 L 168,0 A 42,42 0 0,1 210,42 L 210,45 A 42,42 0 0,1 168,87 L 62,87 " +
                    "A 42,42 0 0,1 24,67 A 42,25 0 0,1 0,52 A 42,42 0 0,0 17,45 A 42,42 0 0,1 59,0 Z")
            };
            bubbleGrid3.Children.Add(_subtitleBorder3);

            _subtitleText3 = new TextBlock
            {
                Text = _pendingSubtitleText3 ?? "",
                FontSize = DpiService.PhysicalToDipY(TEXT_FONT_SIZE),
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(
                    DpiService.PhysicalToDipX(TEXT_MARGIN_LEFT), 0,
                    DpiService.PhysicalToDipX(TEXT_MARGIN_RIGHT), 0)
            };
            bubbleGrid3.Children.Add(_subtitleText3);

            if (_hasRich3)
                ApplyRich(_subtitleText3, _richPrefix3, _richHighlight3, _richSuffix3);

            _container3.Children.Add(bubbleGrid3);

            _overlayWindow3.Content = _container3;
            _overlayWindow3.MouseLeftButtonDown += (s, e) => HideOverlay3();

            ApplySizes3();
        }

        private static void CreateWindowIfNeeded4()
        {
            if (_overlayWindow4 != null) return;

            _overlayWindow4 = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false,
                Opacity = 0
            };

            var helper = new WindowInteropHelper(_overlayWindow4);
            helper.EnsureHandle();
            OverlayWindowService.ApplyOverlayStyle(_overlayWindow4);

            _container4 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.Transparent
            };

            if (string.IsNullOrEmpty(_currentAvatarPath4))
            {
                _currentAvatarPath4 = PickRandomAvatarPath();
            }

            _avatarImage4 = new Image
            {
                Width = DpiService.PhysicalToDipX(AVATAR_SIZE),
                Height = DpiService.PhysicalToDipY(AVATAR_SIZE),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(AVATAR_MARGIN_RIGHT), 0),
                Source = ResourceImageHelper.Load(_currentAvatarPath4),
                Visibility = _avatarVisible4 ? Visibility.Visible : Visibility.Hidden
            };
            RenderOptions.SetBitmapScalingMode(_avatarImage4, BitmapScalingMode.HighQuality);
            _container4.Children.Add(_avatarImage4);

            var bubbleGrid4 = new Grid
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            _subtitleBorder4 = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)),
                Stretch = Stretch.None,
                Data = Geometry.Parse(
                    "M 62,0 L 168,0 A 42,42 0 0,1 210,42 L 210,45 A 42,42 0 0,1 168,87 L 62,87 " +
                    "A 42,42 0 0,1 24,67 A 42,25 0 0,1 0,52 A 42,42 0 0,0 17,45 A 42,42 0 0,1 59,0 Z")
            };
            bubbleGrid4.Children.Add(_subtitleBorder4);

            _subtitleText4 = new TextBlock
            {
                Text = _pendingSubtitleText4 ?? "",
                FontSize = DpiService.PhysicalToDipY(TEXT_FONT_SIZE),
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(
                    DpiService.PhysicalToDipX(TEXT_MARGIN_LEFT), 0,
                    DpiService.PhysicalToDipX(TEXT_MARGIN_RIGHT), 0)
            };
            bubbleGrid4.Children.Add(_subtitleText4);

            _container4.Children.Add(bubbleGrid4);

            _overlayWindow4.Content = _container4;
            _overlayWindow4.MouseLeftButtonDown += (s, e) => HideSubtitle4();

            ApplySizes4();
        }

        private static void UpdateWindowPosition3()
        {
            if (_overlayWindow3 == null) return;
            if (string.IsNullOrEmpty(_positionKey)) return;

            var cachedPosObj = CoordinateFormats.GetCached<object>(_positionKey);
            if (cachedPosObj == null) return;

            var cachedPos = (CoordinateFormats.Point)cachedPosObj;

            double wDip = _overlayWindow3.ActualWidth > 0 ? _overlayWindow3.ActualWidth : 300;
            double hDip = _overlayWindow3.ActualHeight > 0 ? _overlayWindow3.ActualHeight : 80;

            double wPhys = DpiService.DipToPhysicalX(wDip);
            double hPhys = DpiService.DipToPhysicalY(hDip);

            double scale = GetScaleByResolution();
            int offsetY = (int)(THIRD_SUBTITLE_OFFSET_Y * scale);

            OverlayWindowService.SetBounds(_overlayWindow3,
                (int)cachedPos.X, (int)cachedPos.Y + offsetY,
                (int)wPhys, (int)hPhys);
        }

        private static void UpdateWindowPosition4()
        {
            if (_overlayWindow4 == null) return;
            if (string.IsNullOrEmpty(_positionKey)) return;

            var cachedPosObj = CoordinateFormats.GetCached<object>(_positionKey);
            if (cachedPosObj == null) return;

            var cachedPos = (CoordinateFormats.Point)cachedPosObj;

            double wDip = _overlayWindow4.ActualWidth > 0 ? _overlayWindow4.ActualWidth : 300;
            double hDip = _overlayWindow4.ActualHeight > 0 ? _overlayWindow4.ActualHeight : 80;

            double wPhys = DpiService.DipToPhysicalX(wDip);
            double hPhys = DpiService.DipToPhysicalY(hDip);

            double scale = GetScaleByResolution();
            int offsetY = (int)(FOURTH_SUBTITLE_OFFSET_Y * scale);

            OverlayWindowService.SetBounds(_overlayWindow4,
                (int)cachedPos.X, (int)cachedPos.Y + offsetY,
                (int)wPhys, (int)hPhys);
        }

        private static void CloseOverlay3()
        {
            if (_overlayWindow3 != null)
            {
                OverlayWindowService.StopTopmostKeepAlive(_overlayWindow3);
                _overlayWindow3.Close();
                _overlayWindow3 = null;
                _isOverlayVisible3 = false;
                _isFadingOut3 = false;
            }
        }

        private static void CloseOverlay4()
        {
            if (_overlayWindow4 != null)
            {
                OverlayWindowService.StopTopmostKeepAlive(_overlayWindow4);
                _overlayWindow4.Close();
                _overlayWindow4 = null;
                _isOverlayVisible4 = false;
            }
        }

        private static void RefreshAvatarIfNeeded3()
        {
            if (_avatarImage3 == null) return;

            if (string.IsNullOrEmpty(_currentAvatarPath3))
            {
                _currentAvatarPath3 = PickRandomAvatarPath();
            }

            _avatarImage3.Source = ResourceImageHelper.Load(_currentAvatarPath3);
        }

        private static void RefreshAvatarIfNeeded4()
        {
            if (_avatarImage4 == null) return;

            if (string.IsNullOrEmpty(_currentAvatarPath4))
            {
                _currentAvatarPath4 = PickRandomAvatarPath();
            }

            _avatarImage4.Source = ResourceImageHelper.Load(_currentAvatarPath4);
        }

        #endregion

        #region 第四行：UID 的「用户正在操作」提示（常驻透明、触发即显示）

        /// <summary>
        /// ★ 第四行「预建窗」：把窗口先建好、定位好并 Show 出来，但保持**纯透明**（Opacity 0）。
        /// <para>
        /// 为什么要预热：这一行是"用户一操作就要立刻出现"的提示，若等到那一刻才建窗
        /// （新建 Window + 布局 + 定位）会慢半拍、还可能闪一下。预热之后
        /// <see cref="SetSubtitle4"/> 只是把不透明度打上去 —— <b>直接显示</b>。
        /// </para>
        /// <para>
        /// ★ 透明常驻不会挡操作：覆盖层带 <c>WS_EX_TRANSPARENT</c>（鼠标穿透）+
        /// <c>WS_EX_NOACTIVATE</c>（不抢焦点），看不见也点不到。
        /// </para>
        /// </summary>
        public static void PrewarmSubtitle4()
        {
            // 覆盖层只在游戏窗口是前台时才建（和 ShowOverlay 一样的规矩）
            if (!_isFocused) return;

            CreateWindowIfNeeded4();
            RefreshAvatarIfNeeded4();
            ApplySizes4();

            if (_overlayWindow4 == null) return;

            if (!_overlayWindow4.IsVisible)
            {
                _overlayWindow4.Opacity = 0;
                _overlayWindow4.Show();
                _isOverlayVisible4 = true;
            }

            _pendingSubtitleText4 = "";
            if (_subtitleText4 != null) _subtitleText4.Text = "";
            UpdateBubbleWidth4();
            UpdateWindowPosition4();
        }

        /// <summary>
        /// 显示第四行并设置正文 —— <b>直接出现，没有淡入</b>。
        /// 窗口还没预热过也不要紧：这里会顺手建出来。
        /// </summary>
        public static void SetSubtitle4(string text)
        {
            // ★ 顺序要紧：先预热（顺便清掉上一轮的旧文本），再把新文本写进去。
            //   反过来写会被预热那一步清空。
            PrewarmSubtitle4();

            _isActive4 = true;
            _pendingSubtitleText4 = text ?? string.Empty;

            // ★★ 每次显示都把头像**补一次**：建窗那一刻资源可能还没就绪（Source 会是 null，
            //    之后也没人再管它）—— 表现正是"第四行没有头像"。这里兜住。
            if (_overlayWindow4 != null && _avatarImage4 != null)
            {
                RefreshAvatarIfNeeded4();
                _avatarImage4.Visibility = _avatarVisible4 ? Visibility.Visible : Visibility.Hidden;
            }

            if (_subtitleText4 != null)
                _subtitleText4.Text = _pendingSubtitleText4;

            UpdateBubbleWidth4();
            UpdateWindowPosition4();
            UpdateOverlayVisibility4();
        }

        /// <summary>
        /// 收起第四行：**只把不透明度归零，不 Hide、不 Close** ——
        /// 窗口留着，下次 <see cref="SetSubtitle4"/> 直接显示，不用重新建窗。
        /// </summary>
        public static void HideSubtitle4()
        {
            _isActive4 = false;
            UpdateOverlayVisibility4();
        }

        /// <summary>
        /// 第四行的显示状态：要显示 = <c>_isActive4 &amp;&amp; _isFocused</c>。
        /// <para>
        /// ★ 这里**直接切 <c>Opacity</c>**（不淡入淡出）：提示要"立刻出现 / 立刻消失"，
        /// 而且窗口始终在，没有建窗开销。窗口还没建时只在此刻确实要显示才补建。
        /// </para>
        /// </summary>
        private static void UpdateOverlayVisibility4()
        {
            if (_overlayWindow4 == null)
            {
                if (!_isActive4 || !_isFocused) return;

                CreateWindowIfNeeded4();
                RefreshAvatarIfNeeded4();
                ApplySizes4();
                if (_overlayWindow4 == null) return;

                UpdateBubbleWidth4();
                UpdateWindowPosition4();
            }

            bool shouldShow = _isActive4 && _isFocused;

            if (shouldShow && !_overlayWindow4.IsVisible)
            {
                _overlayWindow4.Show();
                _isOverlayVisible4 = true;
            }

            _overlayWindow4.Opacity = shouldShow ? 1.0 : 0.0;
        }

        #endregion

        #region 三行头像的协调分配

        /// <summary>按 <paramref name="index"/> 取头像资源路径（0 = 空，1 = 荧）。</summary>
        private static string AvatarPathByIndex(int index)
        {
            if (index < 0 || index >= _avatarFiles.Length) index = 0;
            return AssetLocator.Pack($"Template/角色头像/150x150/{_avatarFiles[index]}");
        }

        /// <summary>
        /// ★ 一次把**三行**的头像分配好（空 / 荧），规则：
        /// <list type="bullet">
        ///   <item>只显示 1 行 → 随机一个。</item>
        ///   <item>显示 2 行 → 两行**必须不同**。</item>
        ///   <item>显示 3 行 → 允许有两个一样，但**不能三个都一样**。</item>
        /// </list>
        /// 实现上第 1、2 行永远取「一个 + 另一个」，第 3 行随机 —— 于是三行时必然是
        /// <c>[空,荧,空]</c> 或 <c>[空,荧,荧]</c>，天然满足「不能三个都一样」。
        /// </summary>
        /// <para>
        /// ★ 由 UID 在刷新字幕后显式调用（<c>subtitle:avatars</c>）。不放在
        /// <see cref="RefreshAvatarIfNeeded3"/> 里自动做，是为了不影响主程序原有的两行场景
        /// （自动清药 / 钓鱼那些自己有自己的节奏）。
        /// </para>
        public static void AssignAvatars()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var random = new Random();
                int first = random.Next(_avatarFiles.Length);
                int second = (first + 1) % _avatarFiles.Length;   // 与第一行**必然不同**
                int third = random.Next(_avatarFiles.Length);      // 三行时随机 → 不可能三个都一样

                _currentAvatarPath = AvatarPathByIndex(first);
                _currentAvatarPath2 = AvatarPathByIndex(second);
                _currentAvatarPath3 = AvatarPathByIndex(third);

                if (_avatarImage != null)
                    _avatarImage.Source = ResourceImageHelper.Load(_currentAvatarPath);
                if (_avatarImage2 != null)
                    _avatarImage2.Source = ResourceImageHelper.Load(_currentAvatarPath2);
                if (_avatarImage3 != null)
                    _avatarImage3.Source = ResourceImageHelper.Load(_currentAvatarPath3);
            });
        }

        #endregion

        #region 关闭

        private static void CloseOverlay()
        {
            if (_overlayWindow != null)
            {
                OverlayWindowService.StopTopmostKeepAlive(_overlayWindow);
                _overlayWindow.Close();
                _overlayWindow = null;
                _isOverlayVisible = false;
                _isFadingOut = false;
            }
        }

        private static void CloseOverlay2()
        {
            if (_overlayWindow2 != null)
            {
                OverlayWindowService.StopTopmostKeepAlive(_overlayWindow2);
                _overlayWindow2.Close();
                _overlayWindow2 = null;
                _isOverlayVisible2 = false;
                _isFadingOut2 = false;
            }
        }

        #endregion
    }
}