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

                return new[]
                {
                    new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)
                };
            };

            // 打开后按"实测屏幕坐标"再校一次：气泡居中到按钮正上方，
            // 并且只有真的伸到窗口外面才拉回来（可以贴着窗口边缘，不额外留边距）。
            popup.Opened += (_, _) => CenterAndClamp(popup, border, target);

            // 气泡只是提示：不接鼠标事件（IsHitTestVisible=False），
            // 这样它不会挡住下方按钮的命中，鼠标掠过时也不会触发多余事件。
            border.IsHitTestVisible = false;

            return popup;
        }

        /// <summary>
        /// 气泡打开后的水平校正：先对准按钮中心，再保证不超出窗口内容区。
        /// <para>
        /// 全部用 PointToScreen 实测（换算到"屏幕 / 96"单位，即除掉 DPI），
        /// 不依赖缩放系数/DPI 的推算，所以不会把"贴着边缘但没超出"的气泡误推回窗口内。
        /// 只在真的越界时才动，允许紧贴左右边缘。
        /// </para>
        /// </summary>
        private static void CenterAndClamp(Popup popup, FrameworkElement tip, FrameworkElement target)
        {
            try
            {
                var win = Window.GetWindow(target);
                if (win?.Content is not FrameworkElement content || content.ActualWidth <= 0) return;

                var src = PresentationSource.FromVisual(content);
                double dpi = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                if (dpi <= 0) dpi = 1.0;

                double winLeft = content.PointToScreen(new Point(0, 0)).X / dpi;
                double winRight = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight)).X / dpi;

                double tipLeft = tip.PointToScreen(new Point(0, 0)).X / dpi;
                double tipWidth = tip.PointToScreen(new Point(tip.ActualWidth, 0)).X / dpi - tipLeft;
                if (tipWidth <= 0) return;

                double btnLeft = target.PointToScreen(new Point(0, 0)).X / dpi;
                double btnWidth = target.PointToScreen(new Point(target.ActualWidth, 0)).X / dpi - btnLeft;

                // 1) 居中到按钮正上方
                double desired = btnLeft + (btnWidth - tipWidth) / 2.0;

                // 2) 越界才拉回；贴边是允许的（不留额外边距）
                if (desired < winLeft) desired = winLeft;
                // 留 1px 是给 Popup 定位取整用的兜底，肉眼仍等于贴边
                else if (desired + tipWidth > winRight) desired = winRight - tipWidth - 1;
                if (desired < winLeft) desired = winLeft;      // 窗口比气泡还窄时至少贴左

                // 上面量的是"屏幕 /96"，而 Popup 的偏移是在窗口内容（带界面缩放）的坐标系里，
                // 两者差一个缩放系数：直接用"按钮屏幕宽 ÷ 按钮逻辑宽"反推，比读缩放配置更可靠。
                double scale = target.ActualWidth > 0 ? btnWidth / target.ActualWidth : 1.0;
                if (scale <= 0) scale = 1.0;

                double delta = (desired - tipLeft) / scale;
                if (Math.Abs(delta) < 0.5) return;

                popup.HorizontalOffset += delta;
            }
            catch
            {
                // 拿不到窗口/屏幕信息就保持原样，不影响显示
            }
        }
    }
}