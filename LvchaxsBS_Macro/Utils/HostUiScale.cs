using System;
using System.Windows;

namespace LvchaxsBS_Macro.Utils
{
    /// <summary>
    /// 让本插件弹出的独立窗口跟随主程序的「窗口缩放」（100% / 110% / 125% …）。
    ///
    /// <para>
    /// 主界面是靠 <c>RootBorder.LayoutTransform</c> 整体缩放的，页面内的浮层天然继承；
    /// 但模块弹出的<b>独立 Window</b> 是各自的顶层窗口，<b>不会继承</b> ——
    /// 不管用户把窗口缩放调到多少，它们都还是 100% 大小，和主界面比例对不上。
    /// UID 的各个窗口就是这么处理的（<c>Utils/UI/UiScale.cs</c>），宏窗口同理。
    /// </para>
    /// <para>
    /// ★ 具体实现直接反射调宿主的 <c>LvchaxsBS.UI.Helpers.UiScale.ApplyTo(Window)</c>：
    ///   它自己会读当前的缩放系数、并把窗口宽高与内容 LayoutTransform 一起放大。
    ///   宿主不在 / 方法缺失时静默按 100% 处理（窗口照常显示，只是不缩放）。
    /// </para>
    /// </summary>
    public static class HostUiScale
    {
        /// <summary>
        /// 让窗口跟上缩放。必须在 <c>InitializeComponent()</c> 之后、窗口显示之前调用。
        /// </summary>
        public static void ApplyTo(Window window)
        {
            if (window == null) return;

            try
            {
                Type.GetType("LvchaxsBS.UI.Helpers.UiScale, LvchaxsBS")
                    ?.GetMethod("ApplyTo", new[] { typeof(Window) })
                    ?.Invoke(null, new object[] { window });
            }
            catch
            {
                // 缩不了就按 100% 显示，不影响功能
            }
        }
    }
}
