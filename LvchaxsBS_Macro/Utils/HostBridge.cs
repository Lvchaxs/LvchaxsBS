using System;
using System.Windows;
using System.Windows.Media;

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
        /// 在宿主窗口左上角（标题栏「Lvchaxs伴生」旁边）弹一条提示。
        /// <para>★ 走反射调用宿主 <c>LvchaxsBS.Services.ToastService.Show(string, bool)</c>：
        ///   插件没有宿主的编译期引用。宿主找不到就静默忽略（提示可有可无，不能影响功能）。</para>
        /// </summary>
        public static void ShowToast(string message, bool isSuccess = true)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            try
            {
                Type.GetType("LvchaxsBS.Services.ToastService, LvchaxsBS")
                    ?.GetMethod("Show", new[] { typeof(string), typeof(bool) })
                    ?.Invoke(null, new object[] { message, isSuccess });
            }
            catch
            {
                // 静默降级：没提示而已
            }
        }

        /// <summary>
        /// 告诉宿主「自定义宏正在执行」—— 宿主据此把游戏内那排的图标从
        /// <c>自定义宏1.png</c> 换成 <c>自定义宏2.png</c>（反之换回来）。
        /// <para>
        /// ★ 这是**反向**调用（插件 → 宿主），和上面那几个注入方向相反，所以走反射：
        /// <c>LvchaxsBS.Core.IconService.ReportMacroRunning(bool)</c>。
        /// 旧宿主 / 找不到就静默忽略 —— 图标不换而已，绝不能影响宏本身。
        /// </para>
        /// </summary>
        public static void ReportMacroRunning(bool running)
        {
            try
            {
                Type.GetType("LvchaxsBS.Core.IconService, LvchaxsBS")
                    ?.GetMethod("ReportMacroRunning", new[] { typeof(bool) })
                    ?.Invoke(null, new object[] { running });
            }
            catch
            {
                // 静默降级：图标不切，功能照常
            }
        }

        /// <summary>
        /// 主程序右上角那个**功能总开关**是否开启。
        ///
        /// <para>
        /// ★★ 总开关是**全局**的：关掉之后所有功能都不执行（宿主在键盘钩子入口已经拦了宏的触发键）。
        ///   这里读它是为了让**宏列表里的胶囊开关跟着变灰**（Opacity 0.4），
        ///   和首页功能卡片的行为完全一致 —— 用户一眼就能看出"现在整体不生效"。
        /// </para>
        /// <para>
        /// ★ 走反射调 <c>LvchaxsBS.Services.MacroModuleService.GetMasterSwitch()</c>；
        ///   旧宿主没有这个方法 / 调用失败 ⇒ 按**开启**处理（绝不因为读不到就把界面上全部弄灰）。
        /// </para>
        /// </summary>
        public static bool IsMasterSwitchOn()
        {
            try
            {
                Type? svc = Type.GetType("LvchaxsBS.Services.MacroModuleService, LvchaxsBS");
                object? value = svc?
                    .GetMethod("GetMasterSwitch", System.Reflection.BindingFlags.Public |
                                                    System.Reflection.BindingFlags.Static)
                    ?.Invoke(null, null);

                return value is not bool b || b;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 取宿主某个主题色的**终值**（返回一个新的、未冻结的画刷副本；取不到返回 null）。
        ///
        /// <para>
        /// ★★ 主题切换时宿主会让颜色**渐变 300ms**，这段时间里
        ///   <c>TryFindResource(键)</c> 拿到的是那只正在从旧色变到新色的临时画刷。
        ///   凡是「取走画刷自己留着 / 取 <c>.Color</c> 固定下来」的用法
        ///   （语法高亮的规则色、自己造的 Style Setter…）都必须走这里拿终值，
        ///   否则颜色会被钉死在某一帧的中间色上 ——
        ///   表现就是「切完主题颜色不对，重新打开页面才正常」。
        /// </para>
        /// </summary>
        public static Brush? GetThemeBrush(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try
            {
                Type? svc = Type.GetType("LvchaxsBS.Services.ThemeService, LvchaxsBS");
                return svc?
                    .GetMethod("GetThemeBrush", System.Reflection.BindingFlags.Public |
                                                 System.Reflection.BindingFlags.Static)
                    ?.Invoke(null, new object[] { key }) as Brush;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 游戏窗口**此刻存在**吗（不要求在前台）。
        /// <para>
        /// ★ 用于「客户区内生效」的判定：游戏没启动时不该拦 ——
        ///   否则在主程序里测宏会「按什么都没反应」。
        /// ★ 拿不到（旧宿主）按 **false** 处理（= 不拦），宁可多触发也不要不可用。
        /// </para>
        /// </summary>
        public static bool IsGameWindowPresent()
        {
            try
            {
                Type? svc = Type.GetType("LvchaxsBS.Services.MacroModuleService, LvchaxsBS");
                object? value = svc?
                    .GetMethod("GetGameWindowPresent", System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.Static)
                    ?.Invoke(null, null);

                return value is bool b && b;
            }
            catch
            {
                return false;
            }
        }

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
        /// 游戏画面**此刻是不是主界面**（宿主 <c>DetectionManager.IsInMainWindow</c>）。
        /// <para>
        /// ★ 给「触发条件 = 主界面」用：只有在游戏主界面（不在地图等其它界面）按下才执行。
        ///   宿主的检测循环只在「游戏有焦点」时跑，所以拿不到（游戏没开 / 没焦点 / 旧宿主）
        ///   一律按**不是主界面**处理 ⇒ 该条宏这次不执行（绝不越权放行）。
        /// </para>
        /// </summary>
        /// <summary>
        /// 宿主 <c>DetectionManager.IsInMainWindow</c> 的反射句柄。
        /// <para>★ 触发键每次按下都要问一次，解析一次就存着（取不到时留 null，下次再试）。</para>
        /// </summary>
        private static System.Reflection.PropertyInfo? _isInMainWindowProp;

        public static bool IsInGameMainWindow()
        {
            try
            {
                var prop = _isInMainWindowProp ??=
                    Type.GetType("LvchaxsBS.Core.DetectionManager, LvchaxsBS")
                        ?.GetProperty("IsInMainWindow", System.Reflection.BindingFlags.Public |
                                                        System.Reflection.BindingFlags.Static);

                object? value = prop?.GetValue(null);

                return value is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 宿主 <c>FastClipboard.WriteText</c> 的反射句柄（Win32 原生写剪贴板）。
        /// <para>★ 取不到时留 null，下次再试（旧宿主没有这个类）。</para>
        /// </summary>
        private static System.Reflection.MethodInfo? _clipboardWrite;

        /// <summary>
        /// 宿主 <c>FastClipboard.ReadText</c> 的反射句柄。取不到时留 null。
        /// </summary>
        private static System.Reflection.MethodInfo? _clipboardRead;

        /// <summary>
        /// 把文本写进剪贴板。成功返回 true；宿主没有 / 写不进去返回 false。
        /// <para>
        /// ★★ 不用 WPF 的 <c>Clipboard.SetText</c>：它走 OLE 的 <c>OleSetClipboard</c> +
        ///   失败重试（每次几十毫秒），连续复制会明显**卡顿**（用户反馈）。
        ///   宿主 <c>FastClipboard</c> 是 <c>OpenClipboard / SetClipboardData</c> 直写（5ms 短重试）。
        /// </para>
        /// </summary>
        public static bool TrySetClipboardText(string text)
        {
            if (text == null) return false;
            try
            {
                var m = _clipboardWrite ??=
                    Type.GetType("LvchaxsBS.Services.FastClipboard, LvchaxsBS")
                        ?.GetMethod("WriteText", new[] { typeof(string) });

                return m?.Invoke(null, new object[] { text }) is true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 读剪贴板里的纯文本；没有文本 / 宿主没有 ⇒ 返回 null。
        /// </summary>
        public static string? TryGetClipboardText()
        {
            try
            {
                var m = _clipboardRead ??=
                    Type.GetType("LvchaxsBS.Services.FastClipboard, LvchaxsBS")
                        ?.GetMethod("ReadText", System.Reflection.BindingFlags.Public |
                                                System.Reflection.BindingFlags.Static);

                return m?.Invoke(null, null) as string;
            }
            catch
            {
                return null;
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
