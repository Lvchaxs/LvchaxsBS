using System;
using System.Windows;

namespace LvchaxsBS_Macro.Utils
{
    /// <summary>
    /// 宿主（LvchaxsBS.exe）注入过来的能力收口：
    /// **全局模态遮罩 + 确认弹窗 + 游戏窗口焦点查询**。
    ///
    /// <para>
    /// 插件没有宿主的编译期引用，全部走「委托 + 命令」：
    /// 宿主在加载 dll 时反射调用 <c>MacroPlugin.SetHostOverlay</c> /
    /// <c>MacroPlugin.SetHostConfirmDialog</c> / <c>MacroPlugin.SetHostFocusQuery</c>
    /// 把实现交过来。
    /// </para>
    ///
    /// <para>
    /// ★★ 宿主没注入（旧版宿主 / 单独调试）时**一律降级而不是报错**：
    /// 遮罩 = 不显示；确认弹窗 = 退回系统 MessageBox；
    /// 焦点查询 = 当作「有焦点」（于是「客户区内生效」退化成全局，功能不受影响）。
    /// </para>
    /// </summary>
    public static class HostBridge
    {
        /// <summary>
        /// 宿主全局遮罩的命令入口。命令：<c>mask:show</c> / <c>mask:hide</c>。
        /// </summary>
        public static Action<string, object?>? Overlay { get; private set; }

        /// <summary>
        /// 宿主的确认弹窗（标题, 正文, 确认按钮文案, 取消按钮文案）→ 是否点了确认。
        /// 弹窗本身和遮罩都由宿主负责，样式与主程序其它弹窗完全一致。
        /// </summary>
        public static Func<string, string, string, string, bool>? ConfirmDialog { get; private set; }

        /// <summary>
        /// 宿主的焦点查询：游戏窗口现在是不是处于前台（返回 true = 有焦点）。
        /// </summary>
        public static Func<bool>? FocusQuery { get; private set; }

        /// <summary>宿主注入遮罩入口。</summary>
        public static void SetOverlay(Action<string, object?> handler) => Overlay = handler;

        /// <summary>宿主注入确认弹窗入口。</summary>
        public static void SetConfirmDialog(Func<string, string, string, string, bool> handler)
            => ConfirmDialog = handler;

        /// <summary>宿主注入焦点查询入口（给「客户区内生效」用）。</summary>
        public static void SetFocusQuery(Func<bool> handler) => FocusQuery = handler;

        /// <summary>
        /// 游戏窗口现在有焦点吗？
        /// <para>★ 宿主没注入焦点查询 ⇒ 一律当作有焦点（降级为全局生效，不会把功能弄残）。</para>
        /// </summary>
        public static bool IsGameFocused()
        {
            var handler = FocusQuery;
            if (handler == null) return true;

            try
            {
                return handler();
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 带<b>宿主全局遮罩</b>地以模态方式显示一个窗口（直接替换原来的 <c>ShowDialog()</c>）。
        /// <para>遮罩没注入时只是不压暗，弹窗照常显示。</para>
        /// </summary>
        public static bool? ShowModal(Window dialog)
        {
            Invoke("mask:show");
            try
            {
                return dialog.ShowDialog();
            }
            finally
            {
                Invoke("mask:hide");
            }
        }

        /// <summary>
        /// 弹确认框。宿主注入了就用宿主的（带遮罩、样式一致）；没有就退回 MessageBox。
        /// </summary>
        public static bool Confirm(string title, string message,
                                   string okText = "确定", string cancelText = "取消")
        {
            var handler = ConfirmDialog;
            if (handler != null)
            {
                try
                {
                    return handler(title, message, okText, cancelText);
                }
                catch
                {
                    // 宿主弹窗出问题也不能把删除动作卡死 → 往下退回 MessageBox
                }
            }

            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes;
        }

        private static void Invoke(string command)
        {
            try
            {
                Overlay?.Invoke(command, null);
            }
            catch
            {
                // 遮罩只是装饰，失败无所谓
            }
        }
    }
}
