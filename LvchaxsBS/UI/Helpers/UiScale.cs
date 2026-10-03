using System;
using System.Windows;
using System.Windows.Media;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 当前界面缩放系数（对应设置里的「DPI 缩放」）。
    /// <para>
    /// 主界面是通过 <c>RootBorder.LayoutTransform</c> 整体缩放的，
    /// 但 <b>Popup 是独立的视觉树</b>，不会继承这个变换 —— 悬停提示、下拉弹窗这类浮层
    /// 如果不自己跟着缩放，就会和界面里的其它元素比例对不上。用 <see cref="ApplyTo"/> 解决。
    /// </para>
    /// </summary>
    public static class UiScale
    {
        /// <summary>当前缩放系数（1.0 = 100%）。由 MainWindow.ApplyDpiScale 写入。</summary>
        public static double Current { get; set; } = 1.0;

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
    }
}
