using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LvchaxsBS.UI.Controls
{
    public partial class SpinSliderControl : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(SpinSliderControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SpinSliderControl),
                new FrameworkPropertyMetadata(0.0, OnMinMaxChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SpinSliderControl),
                new FrameworkPropertyMetadata(100.0, OnMinMaxChanged));

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(double), typeof(SpinSliderControl),
                new FrameworkPropertyMetadata(1.0));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SpinSliderControl),
                new FrameworkPropertyMetadata("", OnFormatChanged));

        public static readonly DependencyProperty NumericFormatProperty =
            DependencyProperty.Register(nameof(NumericFormat), typeof(string), typeof(SpinSliderControl),
                new FrameworkPropertyMetadata("F0", OnFormatChanged));

        // 显示名称（用于 Toast）
        public static readonly DependencyProperty DisplayNameProperty =
            DependencyProperty.Register(nameof(DisplayName), typeof(string), typeof(SpinSliderControl),
                new PropertyMetadata(""));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, Math.Max(Minimum, Math.Min(Maximum, value)));
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double Step
        {
            get => (double)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public string NumericFormat
        {
            get => (string)GetValue(NumericFormatProperty);
            set => SetValue(NumericFormatProperty, value);
        }

        public string DisplayName
        {
            get => (string)GetValue(DisplayNameProperty);
            set => SetValue(DisplayNameProperty, value);
        }

        /// <summary>值变化事件（拖动中/按钮点击时都会触发，用于保存配置）</summary>
        public event EventHandler<RoutedPropertyChangedEventArgs<double>>? ValueChanged;

        /// <summary>值"提交"事件（拖动结束、按钮点击结束时触发一次，用于弹 Toast）</summary>
        public event EventHandler<double>? ValueCommitted;

        /// <summary>拖动过程中是否真的改过值（避免只是点击滑轨也弹 Toast）</summary>
        private bool _isDragging;

        /// <summary>
        /// 入场动画进行中标志。为 true 时吞掉 Slider 的写回，
        /// 保证 Value 依赖属性、ValueChanged / ValueCommitted 事件都不被触发。
        /// </summary>
        private bool _isEntryAnimating;

        public SpinSliderControl()
        {
            InitializeComponent();
            UpdateDisplay();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpinSliderControl c)
            {
                double v = (double)e.NewValue;
                if (v < c.Minimum) c.SetCurrentValue(ValueProperty, c.Minimum);
                else if (v > c.Maximum) c.SetCurrentValue(ValueProperty, c.Maximum);
                else
                {
                    c.UpdateDisplay();
                    c.ValueChanged?.Invoke(c, new RoutedPropertyChangedEventArgs<double>(
                        (double)e.OldValue, (double)e.NewValue));
                }
            }
        }

        private static void OnMinMaxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpinSliderControl c)
            {
                double v = c.Value;
                if (v < c.Minimum) c.SetCurrentValue(ValueProperty, c.Minimum);
                else if (v > c.Maximum) c.SetCurrentValue(ValueProperty, c.Maximum);
                c.UpdateDisplay();
            }
        }

        private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as SpinSliderControl)?.UpdateDisplay();
        }

        /// <summary>用依赖属性 Value 刷新文本</summary>
        private void UpdateDisplay()
        {
            if (PART_ValueText == null) return;
            string val = Value.ToString(NumericFormat);
            PART_ValueText.Text = string.IsNullOrEmpty(Unit) ? val : $"{val} {Unit}";
        }

        /// <summary>用传入的任意数值刷新文本（入场动画期间用）</summary>
        private void UpdateDisplayFromValue(double v)
        {
            if (PART_ValueText == null) return;
            string val = v.ToString(NumericFormat);
            PART_ValueText.Text = string.IsNullOrEmpty(Unit) ? val : $"{val} {Unit}";
        }

        private void PART_Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isEntryAnimating)
            {
                // 入场动画期间：只刷新显示，不写回 Value，不触发任何事件
                UpdateDisplayFromValue(e.NewValue);
                return;
            }

            if (Math.Abs(e.NewValue - Value) > 0.0001)
                Value = e.NewValue;
        }

        private void PART_Slider_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isDragging = true;
        }

        private void PART_Slider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isDragging = false;
            ValueCommitted?.Invoke(this, Value);
        }

        /// <summary>
        /// 处理"点击滑轨跳转"的情况：
        /// IsMoveToPointEnabled=True 时点击滑轨不会触发 DragCompleted，只会触发 ValueChanged。
        /// 这里用 MouseUp 兜底。拖动结束路径已经被 DragCompleted 处理，用 _isDragging 去重。
        /// </summary>
        private void PART_Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ValueCommitted?.Invoke(this, Value);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            double v = Value + Step;
            if (v <= Maximum)
            {
                Value = v;
                ValueCommitted?.Invoke(this, Value);
            }
        }

        private void DownButton_Click(object sender, RoutedEventArgs e)
        {
            double v = Value - Step;
            if (v >= Minimum)
            {
                Value = v;
                ValueCommitted?.Invoke(this, Value);
            }
        }

        /// <summary>
        /// 播放入场动画：滑块从 Minimum 快速滑到当前 Value，右侧数值同步滚动。
        /// 纯视觉，不影响 Value 依赖属性，不触发 ValueChanged / ValueCommitted。
        /// 由外部（如页面 Loaded）主动调用。
        /// </summary>
        public void PlayEntryAnimation(int durationMs = 400)
        {
            double target = Value;

            // 已经等于下限则没有视觉意义，直接跳过
            if (Math.Abs(target - Minimum) < 0.0001) return;

            _isEntryAnimating = true;

            // 从最小值起步（会触发 ValueChanged → 刷新文本为 min 值）
            PART_Slider.Value = Minimum;
            UpdateDisplayFromValue(Minimum);

            var anim = new DoubleAnimation
            {
                From = Minimum,
                To = target,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };

            anim.Completed += (s, e) =>
            {
                _isEntryAnimating = false;

                // 先清除动画，再落 CLR 值
                PART_Slider.BeginAnimation(Slider.ValueProperty, null);
                PART_Slider.Value = target;

                // 最终以真实 Value 刷新一次（保证格式一致）
                UpdateDisplay();
            };

            PART_Slider.BeginAnimation(Slider.ValueProperty, anim);
        }
    }
}