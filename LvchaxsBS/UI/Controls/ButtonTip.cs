using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Controls
{
    /// <summary>
    /// 按钮 Popup 提示的附加属性（居中在按钮上方）。
    /// 用法：ui:ButtonTip.Text="提示文字"
    /// 不占用 Tag。
    /// </summary>
    public static class ButtonTip
    {
        // ===== 文字 =====
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(ButtonTip),
                new PropertyMetadata(null, OnTextChanged));

        public static string GetText(DependencyObject obj)
            => (string)obj.GetValue(TextProperty);

        public static void SetText(DependencyObject obj, string value)
            => obj.SetValue(TextProperty, value);

        // ===== 可选：气泡本身可点击（触发键页"点击跳转"用） =====
        public static readonly DependencyProperty ClickActionProperty =
            DependencyProperty.RegisterAttached(
                "ClickAction",
                typeof(Action),
                typeof(ButtonTip),
                new PropertyMetadata(null));

        public static Action? GetClickAction(DependencyObject obj)
            => (Action?)obj.GetValue(ClickActionProperty);

        public static void SetClickAction(DependencyObject obj, Action? value)
            => obj.SetValue(ClickActionProperty, value);

        // ===== 内部：Popup 缓存（不占 Tag） =====
        private static readonly DependencyProperty CacheProperty =
            DependencyProperty.RegisterAttached(
                "Cache",
                typeof(Popup),
                typeof(ButtonTip),
                new PropertyMetadata(null));

        private static Popup? GetCache(DependencyObject obj)
            => (Popup?)obj.GetValue(CacheProperty);

        private static void SetCache(DependencyObject obj, Popup value)
            => obj.SetValue(CacheProperty, value);

        // ===== 内部：关闭计时器（每个目标复用一个，避免堆积） =====
        private static readonly DependencyProperty CloseTimerProperty =
            DependencyProperty.RegisterAttached(
                "CloseTimer",
                typeof(System.Windows.Threading.DispatcherTimer),
                typeof(ButtonTip),
                new PropertyMetadata(null));

        private static System.Windows.Threading.DispatcherTimer? GetCloseTimer(DependencyObject obj)
            => (System.Windows.Threading.DispatcherTimer?)obj.GetValue(CloseTimerProperty);

        private static void SetCloseTimer(DependencyObject obj, System.Windows.Threading.DispatcherTimer? value)
            => obj.SetValue(CloseTimerProperty, value);

        /// <summary>
        /// 鼠标离开后延迟多久关闭气泡（毫秒）。
        /// 只有气泡可点击（有 ClickAction）时才需要这点缓冲，用来把鼠标从按钮移到气泡上；
        /// 不可点击的气泡鼠标一离开就立刻关掉。
        /// </summary>
        private const int CloseDelayMs = 120;

        // ===== 逻辑 =====

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;

            fe.MouseEnter -= OnMouseEnter;
            fe.MouseLeave -= OnMouseLeave;

            if (string.IsNullOrEmpty(e.NewValue as string))
            {
                StopCloseTimer(fe);

                var old = GetCache(fe);
                if (old != null)
                {
                    old.IsOpen = false;
                    SetCache(fe, null!);
                }
                return;
            }

            fe.MouseEnter += OnMouseEnter;
            fe.MouseLeave += OnMouseLeave;
        }

        private static void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var text = GetText(fe);
            if (string.IsNullOrEmpty(text)) return;

            var popup = GetCache(fe);
            if (popup == null)
            {
                popup = CreatePopup(fe);
                SetCache(fe, popup);
            }

            if (popup.Child is Border border && border.Child is TextBlock tb)
                tb.Text = text;

            // 手型光标：可点击时显示（每次打开都刷新，避免 SetText/SetClickAction 顺序影响）
            if (popup.Child is Border b2)
                b2.Cursor = GetClickAction(fe) != null ? Cursors.Hand : null;

            // 鼠标又回来了：撤销上一次离开排的关闭任务
            StopCloseTimer(fe);

            // 跟随界面缩放：Popup 是独立视觉树，不会继承窗口 RootBorder 的 LayoutTransform，
            // 不处理的话 DPI 缩放下就会"字体和间距永远不变"。每次打开时更新，缩放改了也能生效。
            if (popup.Child is FrameworkElement content)
                UiScale.ApplyTo(content);

            popup.IsOpen = true;
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            ScheduleClose(fe);
        }

        /// <summary>
        /// 安排关闭：气泡可点击时留一点缓冲让鼠标能移到气泡上，否则立刻关闭。
        /// 缓冲到期后如果鼠标已经回到按钮或落在气泡上，就不关（真正还在用）。
        /// </summary>
        private static void ScheduleClose(FrameworkElement fe)
        {
            var popup = GetCache(fe);
            if (popup == null) return;

            // 不可点击的普通提示：鼠标一离开就消失，不做任何延迟
            if (GetClickAction(fe) == null)
            {
                StopCloseTimer(fe);
                popup.IsOpen = false;
                return;
            }

            var timer = GetCloseTimer(fe);
            if (timer == null)
            {
                timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(CloseDelayMs)
                };
                timer.Tick += (s, _) =>
                {
                    timer.Stop();

                    var p = GetCache(fe);
                    if (p == null) return;

                    // 鼠标还在按钮上、或已经移到气泡上了 → 保持打开
                    if (fe.IsMouseOver || p.IsMouseOver ||
                        (p.Child is FrameworkElement child && child.IsMouseOver))
                        return;

                    p.IsOpen = false;
                };
                SetCloseTimer(fe, timer);
            }

            timer.Stop();
            timer.Start();
        }

        private static void StopCloseTimer(FrameworkElement fe)
        {
            var timer = GetCloseTimer(fe);
            if (timer == null) return;

            // 只停不销毁：计时器是每个目标复用一个，Tick 只在创建时订阅一次
            timer.Stop();
        }

        private static Popup CreatePopup(FrameworkElement target)
        {
            var tb = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 12
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Child = tb
            };

            if (Application.Current.Resources["TipBackgroundBrush"] is Brush bg)
                border.Background = bg;
            else
                border.Background = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));

            var popup = new Popup
            {
                Child = border,
                PlacementTarget = target,
                Placement = PlacementMode.Custom,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                StaysOpen = true
            };

            popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                double x = (targetSize.Width - popupSize.Width) / 2.0;

                // 与按钮的间隙同样跟着缩放，否则界面放大后气泡会贴到按钮上
                double gap = 8 * UiScale.Current;
                double y = -popupSize.Height - gap;

                return new[]
                {
                    new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)
                };
            };

            // 鼠标进入气泡：撤销按钮那边排的关闭任务，让"移到气泡上点击"来得及
            border.MouseEnter += (s, e) => StopCloseTimer(target);

            // 鼠标离开气泡：安排关闭（如果此时已经回到按钮上，就保持不关，避免闪烁）
            border.MouseLeave += (s, e) => ScheduleClose(target);

            // 气泡可点击：点击时取"当前的" ClickAction（不缓存闭包，
            // 这样先 SetText 后 SetClickAction 也能生效）
            border.MouseLeftButtonUp += (s, e) =>
            {
                var action = GetClickAction(target);
                if (action == null) return;

                e.Handled = true;
                popup.IsOpen = false;
                action();
            };

            return popup;
        }
    }
}