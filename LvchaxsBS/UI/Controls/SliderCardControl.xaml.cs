using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LvchaxsBS.UI.Controls
{
    /// <summary>
    /// 配置页通用"滑条卡片"：图标 + 标题 + 滑条 + 提示文字，
    /// 可选在标题行右侧带一个数字框（设置 SpinLabel 即显示）。
    /// 滑条值变化通过 ValueChanged 转发，数字框通过 SpinValueChanged 转发，
    /// 事件签名与 SpinSliderControl / SpinTextBoxControl 一致，
    /// 页面处理器无需改动。
    /// </summary>
    public partial class SliderCardControl : UserControl
    {
        // ===== 外观 =====

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(SliderCardControl),
                new PropertyMetadata(null));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register(nameof(Hint), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        /// <summary>标题后紧跟的自定义内容（如运行状态标签），与标题在同一行显示。</summary>
        public static readonly DependencyProperty TitleContentProperty =
            DependencyProperty.Register(nameof(TitleContent), typeof(object), typeof(SliderCardControl),
                new PropertyMetadata(null));

        // ===== 滑条 =====

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(SliderCardControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(100.0));

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(1.0));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        public static readonly DependencyProperty NumericFormatProperty =
            DependencyProperty.Register(nameof(NumericFormat), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata("F0"));

        /// <summary>滑条提交时 Toast 提示的文字（转发给内部 ToastNotifier）</summary>
        public static readonly DependencyProperty SliderToastProperty =
            DependencyProperty.Register(nameof(SliderToast), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        // ===== 侧边数字框（SpinLabel 为空则隐藏） =====

        public static readonly DependencyProperty SpinLabelProperty =
            DependencyProperty.Register(nameof(SpinLabel), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata("", OnSpinLabelChanged));

        public static readonly DependencyProperty SpinValueProperty =
            DependencyProperty.Register(nameof(SpinValue), typeof(double), typeof(SliderCardControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty SpinMinimumProperty =
            DependencyProperty.Register(nameof(SpinMinimum), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty SpinMaximumProperty =
            DependencyProperty.Register(nameof(SpinMaximum), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(100.0));

        public static readonly DependencyProperty SpinStepProperty =
            DependencyProperty.Register(nameof(SpinStep), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(1.0));

        public static readonly DependencyProperty SpinUnitProperty =
            DependencyProperty.Register(nameof(SpinUnit), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        public static readonly DependencyProperty SpinNumericFormatProperty =
            DependencyProperty.Register(nameof(SpinNumericFormat), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata("F0"));

        public static readonly DependencyProperty SpinTextBoxWidthProperty =
            DependencyProperty.Register(nameof(SpinTextBoxWidth), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(45.0));

        /// <summary>数字框提交时 Toast 提示的文字</summary>
        public static readonly DependencyProperty SpinToastProperty =
            DependencyProperty.Register(nameof(SpinToast), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        // ===== 第二个数字框（显示在 Spin 左侧，Spin2Label 为空则隐藏） =====

        public static readonly DependencyProperty Spin2LabelProperty =
            DependencyProperty.Register(nameof(Spin2Label), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata("", OnSpinLabelChanged));

        public static readonly DependencyProperty Spin2ValueProperty =
            DependencyProperty.Register(nameof(Spin2Value), typeof(double), typeof(SliderCardControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    null, CoerceSpin2Value));

        public static readonly DependencyProperty Spin2MinimumProperty =
            DependencyProperty.Register(nameof(Spin2Minimum), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(0.0, OnSpin2RangeChanged));

        public static readonly DependencyProperty Spin2MaximumProperty =
            DependencyProperty.Register(nameof(Spin2Maximum), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(100.0, OnSpin2RangeChanged));

        public static readonly DependencyProperty Spin2StepProperty =
            DependencyProperty.Register(nameof(Spin2Step), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(1.0));

        public static readonly DependencyProperty Spin2UnitProperty =
            DependencyProperty.Register(nameof(Spin2Unit), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        public static readonly DependencyProperty Spin2NumericFormatProperty =
            DependencyProperty.Register(nameof(Spin2NumericFormat), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata("F0"));

        public static readonly DependencyProperty Spin2TextBoxWidthProperty =
            DependencyProperty.Register(nameof(Spin2TextBoxWidth), typeof(double), typeof(SliderCardControl),
                new PropertyMetadata(45.0));

        public static readonly DependencyProperty Spin2ToastProperty =
            DependencyProperty.Register(nameof(Spin2Toast), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        /// <summary>第二个数字框的悬停说明（走 ButtonTip，留空则不弹）</summary>
        public static readonly DependencyProperty Spin2TipProperty =
            DependencyProperty.Register(nameof(Spin2Tip), typeof(string), typeof(SliderCardControl),
                new PropertyMetadata(""));

        // ===== 事件（转发内部控件） =====

        public event EventHandler<RoutedPropertyChangedEventArgs<double>>? ValueChanged;
        public event EventHandler<RoutedPropertyChangedEventArgs<double>>? SpinValueChanged;
        public event EventHandler<RoutedPropertyChangedEventArgs<double>>? Spin2ValueChanged;

        public SliderCardControl()
        {
            InitializeComponent();

            PART_Slider.ValueChanged += (s, e) => ValueChanged?.Invoke(this, e);
            PART_SpinBox.ValueChanged += (s, e) => SpinValueChanged?.Invoke(this, e);
            PART_SpinBox2.ValueChanged += (s, e) => Spin2ValueChanged?.Invoke(this, e);

            UpdateSpinVisibility();
        }

        private static void OnSpinLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SliderCardControl)d).UpdateSpinVisibility();

        /// <summary>把 Spin2Value 强制夹在 [Spin2Minimum, Spin2Maximum] 内。</summary>
        private static object CoerceSpin2Value(DependencyObject d, object baseValue)
        {
            var card = (SliderCardControl)d;
            double v = (double)baseValue;

            if (v < card.Spin2Minimum) return card.Spin2Minimum;
            if (v > card.Spin2Maximum) return card.Spin2Maximum;
            return v;
        }

        /// <summary>范围变了要重新夹一次当前值。</summary>
        private static void OnSpin2RangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => d.CoerceValue(Spin2ValueProperty);

        private void UpdateSpinVisibility()
        {
            var visibility = string.IsNullOrEmpty(SpinLabel) ? Visibility.Collapsed : Visibility.Visible;
            SpinLabelText.Visibility = visibility;
            PART_SpinBox.Visibility = visibility;

            var visibility2 = string.IsNullOrEmpty(Spin2Label) ? Visibility.Collapsed : Visibility.Visible;
            Spin2LabelText.Visibility = visibility2;
            PART_SpinBox2.Visibility = visibility2;
        }

        // ===== CLR 包装 =====

        public ImageSource? Icon { get => (ImageSource?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
        public object? TitleContent { get => GetValue(TitleContentProperty); set => SetValue(TitleContentProperty, value); }

        /// <summary>
        /// 取标题行 <see cref="TitleContent"/> 里第 <paramref name="index"/> 个状态标签。
        /// <para>
        /// 页面 XAML 里放进 TitleContent 的元素**不能再写 x:Name** ——
        /// SliderCardControl 是 UserControl，其内部是独立名称作用域，
        /// 外部命名会触发 MC3093（"已注册了名称"）。所以只能按声明顺序取。
        /// </para>
        /// </summary>
        public StatusTagPanel? GetTitleTag(int index)
        {
            if (TitleContent is not Panel panel) return null;

            int n = 0;
            foreach (var child in panel.Children)
            {
                if (child is StatusTagPanel tag)
                {
                    if (n == index) return tag;
                    n++;
                }
            }

            return null;
        }

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        public string NumericFormat { get => (string)GetValue(NumericFormatProperty); set => SetValue(NumericFormatProperty, value); }
        public string SliderToast { get => (string)GetValue(SliderToastProperty); set => SetValue(SliderToastProperty, value); }

        public string SpinLabel { get => (string)GetValue(SpinLabelProperty); set => SetValue(SpinLabelProperty, value); }
        public double SpinValue { get => (double)GetValue(SpinValueProperty); set => SetValue(SpinValueProperty, value); }
        public double SpinMinimum { get => (double)GetValue(SpinMinimumProperty); set => SetValue(SpinMinimumProperty, value); }
        public double SpinMaximum { get => (double)GetValue(SpinMaximumProperty); set => SetValue(SpinMaximumProperty, value); }
        public double SpinStep { get => (double)GetValue(SpinStepProperty); set => SetValue(SpinStepProperty, value); }
        public string SpinUnit { get => (string)GetValue(SpinUnitProperty); set => SetValue(SpinUnitProperty, value); }
        public string SpinNumericFormat { get => (string)GetValue(SpinNumericFormatProperty); set => SetValue(SpinNumericFormatProperty, value); }
        public double SpinTextBoxWidth { get => (double)GetValue(SpinTextBoxWidthProperty); set => SetValue(SpinTextBoxWidthProperty, value); }
        public string SpinToast { get => (string)GetValue(SpinToastProperty); set => SetValue(SpinToastProperty, value); }

        public string Spin2Label { get => (string)GetValue(Spin2LabelProperty); set => SetValue(Spin2LabelProperty, value); }
        public double Spin2Value { get => (double)GetValue(Spin2ValueProperty); set => SetValue(Spin2ValueProperty, value); }
        public double Spin2Minimum { get => (double)GetValue(Spin2MinimumProperty); set => SetValue(Spin2MinimumProperty, value); }
        public double Spin2Maximum { get => (double)GetValue(Spin2MaximumProperty); set => SetValue(Spin2MaximumProperty, value); }
        public double Spin2Step { get => (double)GetValue(Spin2StepProperty); set => SetValue(Spin2StepProperty, value); }
        public string Spin2Unit { get => (string)GetValue(Spin2UnitProperty); set => SetValue(Spin2UnitProperty, value); }
        public string Spin2NumericFormat { get => (string)GetValue(Spin2NumericFormatProperty); set => SetValue(Spin2NumericFormatProperty, value); }
        public double Spin2TextBoxWidth { get => (double)GetValue(Spin2TextBoxWidthProperty); set => SetValue(Spin2TextBoxWidthProperty, value); }
        public string Spin2Toast { get => (string)GetValue(Spin2ToastProperty); set => SetValue(Spin2ToastProperty, value); }
        public string Spin2Tip { get => (string)GetValue(Spin2TipProperty); set => SetValue(Spin2TipProperty, value); }
    }
}
