using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LvchaxsBS.UI.Controls
{
    public partial class SpinTextBoxControl : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(SpinTextBoxControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SpinTextBoxControl),
                new FrameworkPropertyMetadata(0.0, OnMinMaxChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SpinTextBoxControl),
                new FrameworkPropertyMetadata(100.0, OnMinMaxChanged));

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(double), typeof(SpinTextBoxControl),
                new FrameworkPropertyMetadata(1.0));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SpinTextBoxControl),
                new FrameworkPropertyMetadata("", OnFormatChanged));

        public static readonly DependencyProperty NumericFormatProperty =
            DependencyProperty.Register(nameof(NumericFormat), typeof(string), typeof(SpinTextBoxControl),
                new FrameworkPropertyMetadata("F0", OnFormatChanged));

        public static readonly DependencyProperty TextBoxWidthProperty =
            DependencyProperty.Register(nameof(TextBoxWidth), typeof(double), typeof(SpinTextBoxControl),
                new FrameworkPropertyMetadata(60.0, OnWidthChanged));

        /// <summary>
        /// 紧凑高度：0（默认）= 用控件原本的高度（输入框 22 / 加减按钮 10）。
        /// 设成大于 0 的值时，输入框和加减按钮会按这个总高度等比压扁，
        /// 用在需要把多个数字框排成多行、又不能把卡片撑高的地方（例如快速传送卡片1里的三行参数）。
        /// </summary>
        public static readonly DependencyProperty CompactHeightProperty =
            DependencyProperty.Register(nameof(CompactHeight), typeof(double), typeof(SpinTextBoxControl),
                new FrameworkPropertyMetadata(0.0, OnCompactHeightChanged));

        // 显示名称（用于 Toast）
        public static readonly DependencyProperty DisplayNameProperty =
            DependencyProperty.Register(nameof(DisplayName), typeof(string), typeof(SpinTextBoxControl),
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

        public double CompactHeight
        {
            get => (double)GetValue(CompactHeightProperty);
            set => SetValue(CompactHeightProperty, value);
        }

        public string NumericFormat
        {
            get => (string)GetValue(NumericFormatProperty);
            set => SetValue(NumericFormatProperty, value);
        }

        public double TextBoxWidth
        {
            get => (double)GetValue(TextBoxWidthProperty);
            set => SetValue(TextBoxWidthProperty, value);
        }

        public string DisplayName
        {
            get => (string)GetValue(DisplayNameProperty);
            set => SetValue(DisplayNameProperty, value);
        }

        /// <summary>值变化事件（实时，用于保存配置）</summary>
        public event EventHandler<RoutedPropertyChangedEventArgs<double>>? ValueChanged;

        /// <summary>值"提交"事件（失焦/Enter/按钮点击结束时触发一次，用于弹 Toast）</summary>
        public event EventHandler<double>? ValueCommitted;

        public SpinTextBoxControl()
        {
            InitializeComponent();
            ApplyCompactHeight();
            UpdateDisplay();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpinTextBoxControl c)
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
            if (d is SpinTextBoxControl c)
            {
                double v = c.Value;
                if (v < c.Minimum) c.SetCurrentValue(ValueProperty, c.Minimum);
                else if (v > c.Maximum) c.SetCurrentValue(ValueProperty, c.Maximum);
                c.UpdateDisplay();
            }
        }

        private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            (d as SpinTextBoxControl)?.UpdateDisplay();
        }

        private static void OnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpinTextBoxControl c && c.PART_ValueTextBox != null)
                c.PART_ValueTextBox.Width = c.TextBoxWidth;
        }

        private static void OnCompactHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SpinTextBoxControl)d).ApplyCompactHeight();

        /// <summary>
        /// 按 CompactHeight 压扁输入框与加减按钮；为 0 时恢复默认高度。
        /// </summary>
        private void ApplyCompactHeight()
        {
            if (PART_ValueTextBox == null) return;

            double h = CompactHeight;

            if (h <= 0)
            {
                PART_ValueTextBox.Height = 22;
                PART_ValueTextBox.FontSize = 12;
                PART_ValueTextBox.Padding = new Thickness(2, 0, 2, 0);
                PART_UpButton.Height = 10;
                PART_DownButton.Height = 10;
                return;
            }

            PART_ValueTextBox.Height = h;
            PART_ValueTextBox.FontSize = h >= 18 ? 12 : 11;
            PART_ValueTextBox.Padding = new Thickness(2, 0, 2, 0);

            // 两个按钮 + 中间 1px 间距 = 总高度
            double btn = Math.Max(5, (h - 1) / 2.0);
            PART_UpButton.Height = btn;
            PART_DownButton.Height = btn;
        }

        private void UpdateDisplay()
        {
            if (PART_ValueTextBox == null) return;
            string val = Value.ToString(NumericFormat);
            PART_ValueTextBox.Text = string.IsNullOrEmpty(Unit) ? val : $"{val}{Unit}";
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

        private void PART_ValueTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PART_ValueTextBox.Text = Value.ToString(NumericFormat);
            PART_ValueTextBox.CaretIndex = PART_ValueTextBox.Text.Length;
        }

        private void PART_ValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string text = PART_ValueTextBox.Text;
            if (string.IsNullOrEmpty(text)) { UpdateDisplay(); return; }

            string numText = text;
            if (!string.IsNullOrEmpty(Unit) && text.EndsWith(Unit))
                numText = text.Substring(0, text.Length - Unit.Length);

            if (double.TryParse(numText, out double v))
            {
                v = Math.Max(Minimum, Math.Min(Maximum, v));
                if (Math.Abs(v - Value) > 0.0001)
                {
                    Value = v;
                    ValueCommitted?.Invoke(this, v);   // ★ 真实变化 → 提交
                }
                else UpdateDisplay();
            }
            else UpdateDisplay();
        }

        private void PART_ValueTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PART_ValueTextBox_LostFocus(sender, new RoutedEventArgs());
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                UpdateDisplay();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }
    }
}