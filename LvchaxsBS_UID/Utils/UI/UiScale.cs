using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace LvchaxsBS_UID.Utils.UI
{
    /// <summary>
    /// 跟随主程序「窗口缩放」的界面缩放系数。
    ///
    /// <para>
    /// 主界面是靠 <c>RootBorder.LayoutTransform</c> 整体缩放的。主程序里的<b>页内浮层</b>
    /// （例如 UID 账号列表里那个「确认删除」模态框）天然继承那层变换，所以自动跟着缩放；
    /// 但模块弹出的<b>独立窗口</b>（编辑账号 / 分组管理 / 移入账号 / 使用说明 / 确认登录）
    /// 都是各自的顶层窗口，<b>不会继承</b> —— 于是它们永远是 100% 大小，和主界面比例对不上。
    /// </para>
    ///
    /// <para>
    /// 系数由宿主注入（<c>UidPlugin.SetHostUiScaleProvider</c> ← 宿主的 <c>UidModuleService</c>）；
    /// 宿主没注入时按 1.0 处理，模块单独运行也不会出错。
    /// </para>
    /// </summary>
    public static class UiScale
    {
        private static Func<double>? _provider;

        /// <summary>每个窗口类型的基准尺寸（XAML 里写的 Width/Height），只记一次，保证可重复调用。</summary>
        private static readonly Dictionary<Type, (double Width, double Height)> BaseSizes = new();

        /// <summary>宿主注入缩放系数来源（读的是宿主的 UiScale.Current）。</summary>
        public static void SetProvider(Func<double>? provider) => _provider = provider;

        /// <summary>当前缩放系数（1.0 = 100%）。</summary>
        public static double Current
        {
            get
            {
                try
                {
                    double scale = _provider?.Invoke() ?? 1.0;
                    return scale > 0 ? scale : 1.0;
                }
                catch
                {
                    return 1.0;
                }
            }
        }

        /// <summary>
        /// 让一个独立窗口跟随界面缩放：窗口宽高按系数放大，内容整体做 LayoutTransform。
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
                Apply(root, scale);
        }

        /// <summary>按系数给元素套 LayoutTransform（100% 时移除，省掉多余开销）。</summary>
        public static void Apply(FrameworkElement element, double scale)
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
    }
}
