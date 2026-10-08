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

        // 说明：气泡不做任何延迟关闭 —— 鼠标一离开按钮就立即消失。
        // 之前为了让鼠标能移到气泡上点击（占用键"点击跳转"）加过缓冲，
        // 但跳转其实是点按钮本身触发的，气泡只是提示，所以那段缓冲不需要了。

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

            // 鼠标一离开就关，不做任何延迟（和其它按钮的提示表现一致）
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
                double s = UiScale.Current;

                // 按钮在屏幕上的实际宽度 = 逻辑宽 × 界面缩放，居中要按这个算，
                // 否则气泡比按钮宽多少都用同一个基准，缩放后就会偏。
                double x = (targetSize.Width * s - popupSize.Width) / 2.0;

                // 与按钮的间隙同样跟着缩放，否则界面放大后气泡会贴到按钮上
                double gap = 8 * s;
                double y = -popupSize.Height - gap;

                // 最左/最右一列的按钮（如 Esc 那一列）居中弹出时，气泡会有一部分伸到窗口外面，
                // 这里把它拉回窗口内（左右都夹），只动水平方向。
                x += ClampIntoWindow(target, popupSize.Width, x, s);

                return new[]
                {
                    new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)
                };
            };

            // 气泡只是提示：不接鼠标事件（IsHitTestVisible=False），
            // 这样它不会挡住下方按钮的命中，鼠标掠过时也不会触发多余事件。
            border.IsHitTestVisible = false;

            return popup;
        }

        /// <summary>
        /// 计算把气泡拉回窗口内容区所需的水平修正量（0 = 本来就放得下，不需要动）。
        /// <para>
        /// 坐标统一换算到"屏幕 / 96"单位：Popup 的偏移是按屏幕像素加的，
        /// 这里除掉 DPI 后与偏移用的是同一把尺子，界面缩放（UiScale）已经包含在屏幕坐标里。
        /// </para>
        /// </summary>
        private static double ClampIntoWindow(FrameworkElement target, double popupWidth, double x, double scale)
        {
            try
            {
                var win = Window.GetWindow(target);
                if (win?.Content is not FrameworkElement content || content.ActualWidth <= 0) return 0;

                var src = PresentationSource.FromVisual(content);
                double dpi = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                if (dpi <= 0) dpi = 1.0;

                // 窗口内容区的左右边界（PointToScreen 会带上 LayoutTransform，缩放后依然准确）
                double winLeft = content.PointToScreen(new Point(0, 0)).X / dpi;
                double winRight = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight)).X / dpi;

                double margin = 6 * scale;
                double tipLeft = target.PointToScreen(new Point(0, 0)).X / dpi + x;
                double tipRight = tipLeft + popupWidth;

                double minLeft = winLeft + margin;
                double maxRight = winRight - margin;

                // 气泡本身就比窗口还宽（极端缩放）时，干脆贴左放，别来回抖
                if (popupWidth >= maxRight - minLeft) return minLeft - tipLeft;

                if (tipLeft < minLeft) return minLeft - tipLeft;
                if (tipRight > maxRight) return maxRight - tipRight;
                return 0;
            }
            catch
            {
                // 拿不到窗口/屏幕信息（例如还没挂到视觉树上）时就按原样居中，不影响显示
                return 0;
            }
        }
    }
}