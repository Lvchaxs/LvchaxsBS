using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 当前界面缩放系数（对应设置里的「窗口缩放」）。
    /// <para>
    /// 主界面是通过 <c>RootBorder.LayoutTransform</c> 整体缩放的，
    /// 但 <b>Popup 是独立的视觉树</b>，不会继承这个变换 —— 悬停提示、下拉弹窗这类浮层
    /// 如果不自己跟着缩放，就会和界面里的其它元素比例对不上。用 <see cref="ApplyTo(FrameworkElement)"/> 解决。
    /// </para>
    /// <para>
    /// 同理，<b>独立打开的 Window</b>（ConfirmDialog / LicenseDialog / ChangelogDialog、
    /// 以及模块 LvchaxsBS_uid 弹的那些窗口）也不会继承 —— 用
    /// <see cref="ApplyTo(Window)"/> 让它们自己跟上。
    /// </para>
    /// </summary>
    public static class UiScale
    {
        /// <summary>当前缩放系数（1.0 = 100%）。由 MainWindow.ApplyDpiScale 写入。</summary>
        public static double Current { get; set; } = 1.0;

        /// <summary>每个窗口类型的基准尺寸（XAML 里写的 Width/Height），只记一次，保证可重复调用。</summary>
        private static readonly Dictionary<Type, (double Width, double Height)> BaseSizes = new();

        /// <summary>
        /// 让元素按当前缩放等比放大（字号、内边距、圆角一起，因为 LayoutTransform 作用于整个元素）。
        /// 缩放为 100% 时移除变换，避免多余的开销。
        /// </summary>
        public static void ApplyTo(FrameworkElement element)
        {
            if (element == null) return;

            double scale = Current;

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
        /// 让一个<b>独立窗口</b>跟随「窗口缩放」：窗口宽高 × 系数 + 内容整体 LayoutTransform。
        ///
        /// <para>
        /// 必须在 <c>InitializeComponent()</c> 之后、窗口显示之前调用
        /// （首次调用时它会记下窗口当时的 Width/Height 作为基准；重复调用不会叠加放大）。
        /// </para>
        /// </summary>
        public static void ApplyTo(Window window)
        {
            if (window == null) return;

            double scale = Current;
            if (Math.Abs(scale - 1.0) < 0.001) return;   // 100% 时什么都不做

            Type key = window.GetType();
            if (!BaseSizes.TryGetValue(key, out var basis))
            {
                basis = (window.Width, window.Height);
                BaseSizes[key] = basis;
            }

            if (!double.IsNaN(basis.Width)) window.Width = basis.Width * scale;
            if (!double.IsNaN(basis.Height)) window.Height = basis.Height * scale;

            if (window.Content is FrameworkElement root)
                ApplyTo(root);
        }
    }
}
