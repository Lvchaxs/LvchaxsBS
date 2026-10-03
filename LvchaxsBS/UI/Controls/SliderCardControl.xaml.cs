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

        // ===== 事件（转发内部控件） =====

        public event EventHandler<RoutedPropertyChangedEventArgs<double>>? ValueChanged;
        public event EventHandler<RoutedPropertyChangedEventArgs<double>>? SpinValueChanged;

        public SliderCardControl()
        {
            InitializeComponent();

            PART_Slider.ValueChanged += (s, e) => ValueChanged?.Invoke(this, e);
            PART_SpinBox.ValueChanged += (s, e) => SpinValueChanged?.Invoke(this, e);

            UpdateSpinVisibility();
        }

        private static void OnSpinLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SliderCardControl)d).UpdateSpinVisibility();

        private void UpdateSpinVisibility()
        {
            var visibility = string.IsNullOrEmpty(SpinLabel) ? Visibility.Collapsed : Visibility.Visible;
            SpinLabelText.Visibility = visibility;
            PART_SpinBox.Visibility = visibility;
        }

        // ===== CLR 包装 =====

        public ImageSource? Icon { get => (ImageSource?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

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
    }
}
