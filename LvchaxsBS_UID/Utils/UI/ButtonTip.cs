using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LvchaxsBS_UID.Utils.UI
{
    /// <summary>
    /// 按钮悬停小提示的附加属性（气泡居中在按钮上方）。
    /// 用法：<c>ui:ButtonTip.Text="置顶"</c>，不占用 <c>Tag</c>。
    ///
    /// <para>
    /// 这是主程序 <c>LvchaxsBS.UI.Controls.ButtonTip</c> 在本模块内的<b>独立副本</b>：
    /// 模块与宿主之间没有编译期引用，不能直接复用宿主的类型。
    /// 外观和行为与主程序保持一致 —— 背景取主题里的 <c>TipBackgroundBrush</c>、
    /// 白字 12 号、圆角 6、内边距 8,4，鼠标一离开立即消失。
    /// </para>
    /// <para>
    /// 与主程序的唯一差别：宿主那版直接读它的 <c>UiScale.Current</c>；
    /// 这里改成从目标元素<b>实测</b>缩放系数，因此不需要宿主配合，单独运行也正常。
    /// </para>
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

            // 跟随界面缩放：Popup 是独立视觉树，不会继承宿主窗口那层的 LayoutTransform，
            // 不处理的话界面放大后提示会显得越来越小。每次打开时重新实测，缩放改了也能生效。
            if (popup.Child is FrameworkElement content)
                ApplyScale(content, MeasureScale(fe));

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

            // 背景优先用宿主的主题画刷（宿主会把主题字典并进 Application.Resources，
            // 所以这里能解析到 —— 深浅色主题各一份）；拿不到就退回同色常量。
            if (Application.Current?.TryFindResource("TipBackgroundBrush") is Brush bg)
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
                double s = MeasureScale(target);

                // 按钮在屏幕上的实际宽度 = 逻辑宽 × 界面缩放，居中要按这个算
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

            // 气泡只是提示：不接鼠标事件，免得挡住下方按钮的命中。
            border.IsHitTestVisible = false;

            return popup;
        }

        /// <summary>
        /// 实测目标元素的"界面缩放系数"（窗口整体 LayoutTransform 的 ScaleX）。
        /// 用屏幕实测宽 ÷ 逻辑宽得到，不依赖宿主的缩放配置，单独运行也准。
        /// </summary>
        private static double MeasureScale(FrameworkElement target)
        {
            try
            {
                if (target.ActualWidth <= 0) return 1.0;

                double dpi = VisualTreeHelper.GetDpi(target).DpiScaleX;
                if (dpi <= 0) dpi = 1.0;

                double left = target.PointToScreen(new Point(0, 0)).X / dpi;
                double right = target.PointToScreen(new Point(target.ActualWidth, 0)).X / dpi;

                double screenWidth = right - left;
                if (screenWidth <= 0) return 1.0;

                double scale = screenWidth / target.ActualWidth;
                return scale > 0 ? scale : 1.0;
            }
            catch
            {
                return 1.0;
            }
        }

        /// <summary>按缩放系数等比放大提示内容（100% 时移除变换，省掉多余开销）。</summary>
        private static void ApplyScale(FrameworkElement element, double scale)
        {
            if (element == null) return;

            if (Math.Abs(scale - 1.0) < 0.001)
            {
                element.LayoutTransform = null;
                return;
            }

            if (element.LayoutTransform is ScaleTransform st)
            {
                st.ScaleX = scale;
                st.ScaleY = scale;
            }
            else
            {
                element.LayoutTransform = new ScaleTransform(scale, scale);
            }
        }

        /// <summary>
        /// 气泡打开后的水平校正：先对准按钮中心，再保证不超出窗口内容区。
        /// 只在真的越界时才动，允许紧贴左右边缘。
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
                else if (desired + tipWidth > winRight) desired = winRight - tipWidth - 1;
                if (desired < winLeft) desired = winLeft;   // 窗口比气泡还窄时至少贴左

                // 上面量的是"屏幕 /96"，而 Popup 的偏移在窗口内容（带界面缩放）坐标系里，
                // 两者差一个缩放系数：用"按钮屏幕宽 ÷ 按钮逻辑宽"反推最可靠。
                double scale = target.ActualWidth > 0 ? btnWidth / target.ActualWidth : 1.0;
                if (scale <= 0) scale = 1.0;

                double delta = (desired - tipLeft) / scale;
                if (Math.Abs(delta) >= 0.5)
                    popup.HorizontalOffset += delta;

                // 竖直方向：默认弹在按钮上方；上方放不下就翻到下面。
                // 踩过的两个坑：① 不能拿"气泡当前顶边 < 屏幕顶边"当条件（WPF 会先把气泡夹到
                // 屏幕顶边，那个条件永远不成立）；② Popup 改变 Offset 会重新走一次定位回调，
                // 偏移量与屏幕位移的换算不能想当然，所以先用 10 个单位试探出真实比例再折算。
                double btnTop = target.PointToScreen(new Point(0, 0)).Y / dpi;
                double spaceAbove = btnTop - SystemParameters.VirtualScreenTop;
                double needAbove = (tip.ActualHeight + 8) * scale;      // 气泡高 + 间隙，都随缩放

                if (spaceAbove < needAbove)
                {
                    double desiredTop = target.PointToScreen(new Point(0, target.ActualHeight)).Y / dpi
                                        + 8 * scale;
                    double y0 = tip.PointToScreen(new Point(0, 0)).Y / dpi;

                    popup.VerticalOffset += 10;      // 试探步长

                    popup.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                    {
                        try
                        {
                            double y1 = tip.PointToScreen(new Point(0, 0)).Y / dpi;
                            double gain = (y1 - y0) / 10.0;          // 偏移单位 → 屏幕位移
                            if (Math.Abs(gain) < 0.01) gain = 1.0;
                            popup.VerticalOffset += (desiredTop - y1) / gain;
                        }
                        catch
                        {
                            // 量不到就算了，保持现状
                        }
                    }));
                }
            }
            catch
            {
                // 拿不到窗口/屏幕信息就保持原样，不影响显示
            }
        }
    }
}
