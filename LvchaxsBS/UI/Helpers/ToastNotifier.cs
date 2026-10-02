using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LvchaxsBS.UI.Controls;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 附加属性：让 SpinSliderControl / SpinTextBoxControl / DropDownSelect / CheckBox
    /// 在"值真正提交"时自动弹 TitleBarToast。
    ///
    /// 用法（XAML）：
    ///   xmlns:ui="clr-namespace:LvchaxsBS.UI.Helpers"
    ///   <controls:SpinSliderControl ui:ToastNotifier.Message="壁纸透明度" ... />
    ///
    /// 内部机制：
    ///   为避免"打开页面加载配置"时误弹，附加属性挂载时不会立即监听事件，
    ///   而是在控件 Loaded 之后通过 Dispatcher.BeginInvoke(ContextIdle) 延后一拍再挂监听。
    ///   这样配置页 Loaded 里对 IsChecked / SelectedIndex 的初始化赋值就不会触发 Toast。
    /// </summary>
    public static class ToastNotifier
    {
        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.RegisterAttached(
                "Message", typeof(string), typeof(ToastNotifier),
                new PropertyMetadata(null, OnMessageChanged));

        public static string GetMessage(DependencyObject o) => (string)o.GetValue(MessageProperty);
        public static void SetMessage(DependencyObject o, string v) => o.SetValue(MessageProperty, v);

        // ============ 内部状态：是否已激活监听 ============
        private static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive", typeof(bool), typeof(ToastNotifier),
                new PropertyMetadata(false));

        private static bool GetIsActive(DependencyObject o) => (bool)o.GetValue(IsActiveProperty);
        private static void SetIsActive(DependencyObject o, bool v) => o.SetValue(IsActiveProperty, v);

        // ============ 附加属性变化 ============

        private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;

            string newMsg = e.NewValue as string;

            // 每次 Message 变化都先解绑，避免重复挂
            Detach(fe);

            if (string.IsNullOrEmpty(newMsg))
            {
                SetIsActive(fe, false);
                return;
            }

            // 若已 Loaded，直接延后一拍激活；
            // 否则等 Loaded 之后激活。
            if (fe.IsLoaded)
            {
                ActivateDeferred(fe);
            }
            else
            {
                fe.Loaded -= Fe_Loaded;
                fe.Loaded += Fe_Loaded;
            }
        }

        private static void Fe_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                fe.Loaded -= Fe_Loaded;
                ActivateDeferred(fe);
            }
        }

        /// <summary>
        /// 延后一拍激活监听。
        /// 配置页的 Loaded 赋值在 Dispatcher 的 Loaded 优先队列里，
        /// 我们用 ContextIdle 更低的优先级，保证初始化赋值全部跑完再挂监听。
        /// </summary>
        private static void ActivateDeferred(FrameworkElement fe)
        {
            fe.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (fe.GetValue(MessageProperty) is string msg && !string.IsNullOrEmpty(msg))
                {
                    SetIsActive(fe, true);
                    Attach(fe);
                }
            }), DispatcherPriority.ContextIdle);
        }

        // ============ 挂载 / 卸载 ============

        private static void Attach(FrameworkElement fe)
        {
            switch (fe)
            {
                case SpinSliderControl slider:
                    slider.ValueCommitted -= Slider_Committed;
                    slider.ValueCommitted += Slider_Committed;
                    break;

                case SpinTextBoxControl box:
                    box.ValueCommitted -= Box_Committed;
                    box.ValueCommitted += Box_Committed;
                    break;

                case DropDownSelect dd:
                    dd.SelectionChanged -= DropDown_Changed;
                    dd.SelectionChanged += DropDown_Changed;
                    break;

                case CheckBox cb:
                    cb.Checked -= CheckBox_Changed;
                    cb.Unchecked -= CheckBox_Changed;
                    cb.Checked += CheckBox_Changed;
                    cb.Unchecked += CheckBox_Changed;
                    break;
            }
        }

        private static void Detach(FrameworkElement fe)
        {
            switch (fe)
            {
                case SpinSliderControl slider:
                    slider.ValueCommitted -= Slider_Committed;
                    break;

                case SpinTextBoxControl box:
                    box.ValueCommitted -= Box_Committed;
                    break;

                case DropDownSelect dd:
                    dd.SelectionChanged -= DropDown_Changed;
                    break;

                case CheckBox cb:
                    cb.Checked -= CheckBox_Changed;
                    cb.Unchecked -= CheckBox_Changed;
                    break;
            }
        }

        // ============ 各控件事件处理 ============

        private static void Slider_Committed(object? sender, double value)
        {
            if (sender is not SpinSliderControl c) return;
            if (!GetIsActive(c)) return;   // ★ 静默期

            string val = value.ToString(c.NumericFormat);
            if (!string.IsNullOrEmpty(c.Unit)) val = $"{val} {c.Unit}";
            Show(GetMessage(c), val);
        }

        private static void Box_Committed(object? sender, double value)
        {
            if (sender is not SpinTextBoxControl c) return;
            if (!GetIsActive(c)) return;   // ★ 静默期

            string val = value.ToString(c.NumericFormat);
            if (!string.IsNullOrEmpty(c.Unit)) val = $"{val}{c.Unit}";
            Show(GetMessage(c), val);
        }

        private static void DropDown_Changed(object? sender, int index)
        {
            if (sender is not DropDownSelect c) return;
            if (!GetIsActive(c)) return;   // ★ 静默期

            string val = "";
            if (c.ItemsSource is IEnumerable<string> list)
            {
                int i = 0;
                foreach (var s in list)
                {
                    if (i == index) { val = s; break; }
                    i++;
                }
            }
            Show(GetMessage(c), val);
        }

        private static void CheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox c) return;
            if (!GetIsActive(c)) return;   // ★ 静默期

            Show(GetMessage(c), c.IsChecked == true ? "已启用" : "已关闭");
        }

        // ============ 显示 ============

        private static void Show(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (Application.Current?.MainWindow is LvchaxsBS.UI.MainWindow mw)
            {
                string msg = string.IsNullOrEmpty(value) ? name : $"{name}: {value}";
                mw.ShowToast(msg, true);
            }
        }
    }
}