using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LvchaxsBS_Macro.Controls
{
    /// <summary>
    /// ★ 和宿主 <c>LvchaxsBS.UI.Controls.DropDownSelect</c> 等价的下拉控件（按钮 + Popup 弹层）。
    /// <para>
    /// ⛔ 本 dll **不引用宿主**，所以不能直接 new 宿主那个控件；这里重写一份。
    /// 但**样式全部复用宿主的**（<c>SimpleDropDownButtonStyle</c> / <c>SimpleDropDownItemStyle</c> /
    /// <c>PopupBackgroundBrush</c> 等），靠 <c>DynamicResource</c> 与 <c>Application.Current.Resources</c>
    /// 在运行期取到 —— 这是本插件"不引用宿主但视觉完全一致"的关键。
    /// </para>
    /// </summary>
    public partial class MacroDropDown : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(string[]), typeof(MacroDropDown),
                new PropertyMetadata(null, OnVisualChanged));

        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(MacroDropDown),
                new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVisualChanged));

        /// <summary>下拉里要显示的选项（顺序即索引）。</summary>
        public string[]? ItemsSource
        {
            get => (string[]?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>当前选中项下标（-1 / 越界时显示第一项）。</summary>
        public int SelectedIndex
        {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        /// <summary>用户点了某一项（参数 = 新下标）。</summary>
        public event EventHandler<int>? SelectionChanged;

        /// <summary>
        /// 按钮模板里除文本以外的固定占位（DIP）：
        /// <c>ContentPresenter Margin="8,0,4,0"</c> = 12 + 箭头列 <c>20</c> + 左右边框 <c>1+1</c> = 34，
        /// 再加 1px 余量防字体 hinting 把最后一个字挤掉。
        /// <para>★ 数值来自宿主 <c>UI/Styles/ComboBoxStyles.xaml</c> 的 <c>SimpleDropDownButtonStyle</c>，
        /// 那边改了模板这里要跟着改。</para>
        /// </summary>
        private const double ChromeWidth = 35;

        private Popup? _popup;

        public MacroDropDown()
        {
            InitializeComponent();

            // ★ 构造时样式（DynamicResource）可能还没套上，字体会回落到默认值、量出来偏窄；
            //   Loaded 时再量一次 —— 那时 FontSize=13 与字体都已确定。
            Loaded += (s, e) => UpdateAutoWidth();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MacroDropDown c)
            {
                c.UpdateSelectedText();
                c.UpdateAutoWidth();
            }
        }

        private void UpdateSelectedText()
        {
            if (PART_SelectedText == null) return;

            var arr = ItemsSource;
            if (arr == null || arr.Length == 0) { PART_SelectedText.Text = ""; return; }

            int i = SelectedIndex;
            PART_SelectedText.Text = i >= 0 && i < arr.Length ? arr[i] : arr[0];
        }

        /// <summary>
        /// 宽度跟着**选项里最长的那一项**走（不是跟着当前选中项）——
        /// 否则选到长名字时会被裁掉，切回短名字又缩回去，宽度跳来跳去。
        /// <para>
        /// ★ 边界由 XAML 的 <c>MinWidth</c> / <c>MaxWidth</c> 控制（WPF 布局会自动钳制），
        ///   这里只管把"内容需要多宽"算出来。超长的宏名会被 <c>TextTrimming</c> 显示成省略号。
        /// </para>
        /// </summary>
        private void UpdateAutoWidth()
        {
            if (PART_SelectedText == null) return;

            var arr = ItemsSource;
            if (arr == null || arr.Length == 0) return;

            double widest = 0;
            foreach (string? item in arr)
            {
                if (string.IsNullOrEmpty(item)) continue;

                var probe = new TextBlock
                {
                    FontFamily = PART_SelectedText.FontFamily,
                    FontSize = PART_SelectedText.FontSize,
                    FontStyle = PART_SelectedText.FontStyle,
                    FontWeight = PART_SelectedText.FontWeight,
                    FontStretch = PART_SelectedText.FontStretch,
                    Text = item,
                };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (probe.DesiredSize.Width > widest) widest = probe.DesiredSize.Width;
            }

            if (widest <= 0) return;

            Width = Math.Ceiling(widest + ChromeWidth);
        }

        private void MainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_popup != null && _popup.IsOpen) { _popup.IsOpen = false; return; }
            OpenPopup();
        }

        private void OpenPopup()
        {
            var arr = ItemsSource;
            if (arr == null || arr.Length == 0) return;

            var res = Application.Current?.Resources;

            var stack = new StackPanel();
            for (int i = 0; i < arr.Length; i++)
            {
                int idx = i;
                var btn = new Button { Content = arr[i] };

                // ★ 复用宿主的下拉项样式（取不到就退回无样式，不至于崩）
                if (res?["SimpleDropDownItemStyle"] is Style itemStyle)
                    btn.Style = itemStyle;

                // 当前项用主题主色标出来（和宿主 DropDownSelect 一致）
                if (i == SelectedIndex && res?["PrimaryBrush"] is Brush accent)
                    btn.Foreground = accent;

                btn.Click += (s, e2) =>
                {
                    SelectedIndex = idx;
                    SelectionChanged?.Invoke(this, idx);
                    if (_popup != null) _popup.IsOpen = false;
                };

                stack.Children.Add(btn);
            }

            // 弹层是浮在内容之上的，必须用不透明背景
            var bg = TryBrush("PopupBackgroundBrush") ?? TryBrush("CardBackgroundBrush");
            var border = new Border
            {
                Background = bg,
                BorderBrush = TryBrush("BorderStrongBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                Child = stack,
            };

            // ★ Popup 是独立视觉树，不继承窗口上的 LayoutTransform 缩放 ——
            //   自己把祖先链上的缩放乘出来补上（等价宿主的 UiScale.ApplyTo）。
            double scale = AncestorScale();
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                border.LayoutTransform = new ScaleTransform(scale, scale);
                border.MinWidth = ActualWidth / scale;
            }
            else
            {
                border.MinWidth = ActualWidth;
            }

            bool up = ShouldOpenUpward();
            double gap = 2;

            _popup = new Popup
            {
                Child = border,
                PlacementTarget = PART_MainButton,
                Placement = up ? PlacementMode.Top : PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                VerticalOffset = up ? -gap : gap,
            };

            AnimateArrow(up ? -180 : 180);
            _popup.Closed += (s, e2) => AnimateArrow(0);
            _popup.IsOpen = true;
        }

        /// <summary>显存够不够在下面展开：不够就往上弹。</summary>
        private bool ShouldOpenUpward()
        {
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget == null) return false;

            double scaleY = AncestorScale();
            try
            {
                var origin = PART_MainButton.PointToScreen(new Point(0, 0));
                var bottom = PART_MainButton.PointToScreen(new Point(0, PART_MainButton.ActualHeight));
                double screenH = SystemParameters.PrimaryScreenHeight * src.CompositionTarget.TransformToDevice.M22;
                double need = (ItemsSource?.Length ?? 0) * 32 * scaleY + 16;
                return screenH - bottom.Y < need && origin.Y > need;
            }
            catch { return false; }
        }

        /// <summary>祖先链上所有 LayoutTransform 的缩放乘积（宿主用它在窗口上做整体缩放）。</summary>
        private double AncestorScale()
        {
            double scale = 1.0;
            try
            {
                DependencyObject? p = this;
                while (p != null)
                {
                    if (p is FrameworkElement fe && fe.LayoutTransform is ScaleTransform st && st.ScaleX > 0)
                        scale *= st.ScaleX;
                    p = p is Visual || p is System.Windows.Media.Media3D.Visual3D
                        ? VisualTreeHelper.GetParent(p)
                        : LogicalTreeHelper.GetParent(p);
                }
            }
            catch { /* 拿不到就当 1 倍 */ }
            return scale <= 0 ? 1.0 : scale;
        }

        private Brush? TryBrush(string key)
            => Application.Current?.TryFindResource(key) as Brush;

        /// <summary>箭头翻转：宿主按钮模板里那个 ArrowRotate。</summary>
        private void AnimateArrow(double angle)
        {
            if (PART_MainButton?.Template == null) return;

            if (PART_MainButton.Template.FindName("ArrowRotate", PART_MainButton) is not RotateTransform rotate)
                return;

            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation
            {
                To = angle,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }
}
