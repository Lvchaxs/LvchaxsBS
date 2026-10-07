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

        // ===== 逻辑 =====

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;

            fe.MouseEnter -= OnMouseEnter;
            fe.MouseLeave -= OnMouseLeave;

            if (string.IsNullOrEmpty(e.NewValue as string))
            {
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

            // 跟随界面缩放：Popup 是独立视觉树，不会继承窗口 RootBorder 的 LayoutTransform，
            // 不处理的话 DPI 缩放下就会"字体和间距永远不变"。每次打开时更新，缩放改了也能生效。
            if (popup.Child is FrameworkElement content)
                UiScale.ApplyTo(content);

            popup.IsOpen = true;
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var popup = GetCache(fe);
            if (popup == null) return;

            // 气泡可点击时，鼠标要从按钮移到气泡上才能点中，所以延迟关闭：
            // 期间如果鼠标已经落在气泡上就保持打开，否则关闭。
            if (GetClickAction(fe) != null)
            {
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                timer.Tick += (s, _) =>
                {
                    timer.Stop();
                    if (!popup.IsMouseOver) popup.IsOpen = false;
                };
                timer.Start();
                return;
            }

            popup.IsOpen = false;
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