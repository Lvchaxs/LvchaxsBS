using System.Windows;
using System.Windows.Controls;
using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Models;
using LvchaxsBS_Macro.Views;

namespace LvchaxsBS_Macro
{
    /// <summary>
    /// 宿主（LvchaxsBS.exe）的「自定义宏」插件入口。
    ///
    /// <para>
    /// 宿主按**约定类型名 + 约定方法名**反射调用，双方没有编译期引用：
    /// 把 <c>LvchaxsBS_macro.dll</c> 放/删都不影响主程序启动 ——
    /// 有就在首页多一张卡片 + 顶部多一个图标，没有就少这两样。
    /// </para>
    ///
    /// <para>
    /// ★★ 本插件**不再自己画整张卡片**：宿主把它当成一条普通模块
    /// （<c>ModuleRegistry</c> 里的一条），所以卡片外框、图标、标题、触发键按钮、配置按钮、
    /// 胶囊开关、拖动排序、顶部导航、触发键占用检测，全部用宿主那一套。
    /// 插件只提供：<b>图标 URI / 卡片第二行的控件 / 配置页 / 开关与触发键的读写</b>。
    /// </para>
    ///
    /// <para>
    /// 约定（全部 <c>public static</c>）：
    /// <list type="table">
    ///   <item><term>GetIconUri()</term><description>string：图标 pack URI</description></item>
    ///   <item><term>CreateCardExtension()</term><description>FrameworkElement：卡片第二行控件</description></item>
    ///   <item><term>CreateConfigPage()</term><description>Page：配置页（现在空白）</description></item>
    ///   <item><term>GetEnabled() / SetEnabled(bool)</term><description>胶囊开关</description></item>
    ///   <item><term>GetTriggerKey() / SetTriggerKey(string)</term><description>触发键</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class MacroPlugin
    {
        /// <summary>卡片图标：图就在本 dll 里（★ 程序集名必须写全，省略会去宿主 exe 里找）。</summary>
        public const string IconUri =
            "pack://application:,,,/LvchaxsBS_macro;component/Assets/Images/自定义宏.png";

        // ============ 宿主约定入口 ============

        /// <summary>模块图标。</summary>
        public static string GetIconUri() => IconUri;

        /// <summary>
        /// 【v2 起不再使用】首页卡片已下线（用户要求：首页不要这张卡，入口只留顶部按钮）。
        /// <para>★ 方法保留是为了「旧宿主 + 新 dll」时不报"模块版本不匹配"；返回 null 即可。</para>
        /// </summary>
        public static FrameworkElement? CreateCardExtension() => null;

        /// <summary>
        /// 卡片**第一行标题右侧**的控件（2026-10-19 新增）：
        /// <c>宏文件 N 个</c> 标签 + <c>编辑</c> 按钮（点了直接编辑当前选中的宏）。
        /// <para>
        /// ★ 别的模块那里是四个分辨率标签，宏这里换成这两样（用户指定）。
        /// ★ 返回 null / 抛异常时宿主退回显示原来的分辨率标签 —— 旧宿主不认识这个方法，也不受影响。
        /// </para>
        /// </summary>
        public static FrameworkElement? CreateTagExtension() => null;

        /// <summary>配置页（★ 现在开始有内容了：宏卡片列表）。</summary>
        public static Page CreateConfigPage() => new MacroConfigPage();

        /// <summary>
        /// 配置容器页底部按钮条最前面的按钮 —— 「添加宏 / 触发键 / 配置 / 返回 / 开关」里的第一个。
        /// </summary>
        public static FrameworkElement CreateToolbarButton() => new Controls.AddMacroButton();

        /// <summary>胶囊开关当前值。</summary>
        public static bool GetEnabled() => MacroSettingsStore.Current.Enabled;

        /// <summary>写胶囊开关（立刻落盘到 <c>Data\Macro\macro.json</c>）。</summary>
        public static void SetEnabled(bool value) => MacroSettingsStore.Mutate(s => s.Enabled = value);

        /// <summary>触发键当前值（"未设置" = 还没设置过）。</summary>
        public static string GetTriggerKey()
        {
            string key = MacroSettingsStore.Current.TriggerKey;
            return string.IsNullOrWhiteSpace(key) ? "未设置" : key;
        }

        /// <summary>写触发键（同样落盘到插件自己的配置里）。</summary>
        public static void SetTriggerKey(string key)
            => MacroSettingsStore.Mutate(s =>
                s.TriggerKey = string.IsNullOrWhiteSpace(key) ? "未设置" : key);

        // ============ 触发（宿主全局键盘钩子 → 这里） ============

        /// <summary>
        /// ★ 触发键"这一次按下"是不是已经处理过了。
        /// <para>
        /// 按住一个键不放时，Windows 会以 ~30 次/秒的频率补发 KeyDown（系统自动重复）。
        /// 如果每条都当成一次新的按下，「长按时重复循环」就会被 <c>Stop + Start</c> 无限重启 ——
        /// 脚本刚执行到第 2、3 行就被掐掉重来，看起来就像"执行顺序不对 / 只执行了开头"。
        /// </para>
        /// <para>所以：同一次按下只处理第一条 KeyDown，直到收到 KeyUp 才复位。</para>
        /// </summary>
        private static bool _triggerDownHandled;

        // ============ v2：每条宏各自一个触发键 ============

        /// <summary>
        /// 当前需要宿主监听的**全部**触发键（启用 + 已设置的）。
        /// <para>★ 宿主每来一个按键都会问一次这个列表，所以改键/开关后不用主动通知宿主。</para>
        /// </summary>
        public static List<string> GetTriggerKeys()
            => Config.MacroSettingsStore.GetTriggerKeys();

        /// <summary>
        /// 某个触发键**按下**（新宿主会带键名叫这个；旧宿主仍调 <see cref="OnTriggerDown"/>）。
        ///
        /// <para>
        /// ★★ 触发方式 / 生效范围都按**这条宏自己**的设置来（2026-10-22 起每条宏各自可选）：
        ///   · 执行一次（Once）      —— 按一下把整段脚本跑一遍就结束；
        ///   · 长按循环（HoldRepeat）—— 按住一直重复，抬手结束；
        ///   · 循环执行（Toggle）    —— 按下开始循环，再按一下结束；
        ///   · 生效范围 = 客户区内   —— 不在游戏窗口里（含游戏没开）就不响应；
        ///     生效范围 = 全局       —— 任何窗口聚焦时都响应；
        ///   · 触发条件 = 主界面     —— 还必须**此刻是游戏主界面**（不在地图等其它界面）才响应；
        ///     触发条件 = 无条件     —— 不看画面状态。
        /// </para>
        /// <para>
        /// ★★ 触发键按下时会在 <c>Config\Macro\trigger.log</c> 记一行（命中 / 拦截及原因），排查用。
        /// </para>
        /// <para>
        /// ★ 也不需要自己防"按住不放的自动重复"：宿主钩子已经过滤了 <c>IsRepeat</c> 和 <c>IsInjected</c>。
        /// </para>
        /// </summary>
        public static void OnTriggerDownForKey(string key)
        {
            var macro = MacroSettingsStore.FindByTriggerKey(key);

            if (macro == null) return;

            // ① 生效范围：选了「客户区内生效」就必须**正在游戏里**（游戏窗口在前台）。
            //   ★★ 只要不在游戏里就拦截 —— 包括「游戏压根没开」：
            //     不在客户区还照样执行，等于这一项没起作用（用户实测反馈）。
            if (macro.ScopeIndex == (int)MacroScope.ClientOnly && !Utils.HostBridge.IsGameFocused())
            {
                TriggerLog($"key={key} macro={macro.Name} 拦截：生效范围=客户区内，但当前不在游戏窗口内");
                return;
            }

            // ② 触发条件：**只在「客户区内生效」时才判断** —— 全局生效就是任何地方都能触发，
            //   那时再看画面状态等于自相矛盾。选了「主界面」就必须**此刻是游戏主界面**（不在地图等其它界面）。
            if (macro.ScopeIndex == (int)MacroScope.ClientOnly &&
                macro.ConditionIndex == (int)MacroCondition.MainWindowOnly &&
                !Utils.HostBridge.IsInGameMainWindow())
            {
                TriggerLog($"key={key} macro={macro.Name} 拦截：触发条件=主界面，但当前不是主界面");
                return;
            }

            TriggerLog($"key={key} macro={macro.Name} " +
                       $"mode={macro.Mode} scope={(MacroScope)macro.ScopeIndex} " +
                       $"condition={(MacroCondition)macro.ConditionIndex} → 执行");

            switch (macro.Mode)
            {
                case MacroTriggerMode.HoldRepeat:
                    Core.MacroRunner.Start(macro, loop: true);
                    break;

                case MacroTriggerMode.Toggle:
                    Core.MacroRunner.Toggle(macro);
                    break;

                default:
                    Core.MacroRunner.Start(macro, loop: false);
                    break;
            }
        }

        /// <summary>trigger.log 最多保留多少行。</summary>
        private const int TriggerLogKeep = 200;

        /// <summary>每追加多少行才去裁剪一次（裁剪要读文件，不能每次都做）。</summary>
        private const int TriggerLogTrimBatch = 50;

        /// <summary>已追加的行数（够一批了才裁剪）。</summary>
        private static int _triggerLogLines;

        private static readonly object _triggerLogGate = new();

        /// <summary>
        /// 触发诊断日志（`Config\Macro\trigger.log`，只保留最近 200 行）。
        /// <para>
        /// ★★ 这里只做**追加一行**：不读旧内容、不整文件重写。
        ///   触发路径跑在键盘钩子的回调线程上，读文件 + 全量重写是毫秒级的磁盘 I/O，
        ///   既拖慢"按下 → 启动宏"，极端情况还可能撞上 Windows 的低级钩子超时被摘钩。
        /// </para>
        /// <para>★ 裁剪（只留最后 200 行）攒够一批才做，而且放到后台线程，不挡这次触发。</para>
        /// </summary>
        private static void TriggerLog(string text)
        {
            string? path = null;
            try
            {
                path = System.IO.Path.Combine(MacroSettingsStore.FolderPath, "trigger.log");
                string line = $"{DateTime.Now:HH:mm:ss.fff} {text}";

                bool trim;
                lock (_triggerLogGate)
                {
                    System.IO.File.AppendAllText(path, line + Environment.NewLine);
                    _triggerLogLines++;
                    trim = _triggerLogLines >= TriggerLogTrimBatch;
                    if (trim) _triggerLogLines = 0;
                }

                if (trim)
                    System.Threading.Tasks.Task.Run(() => TrimTriggerLog(path!, TriggerLogKeep));
            }
            catch
            {
                // 日志失败绝不影响触发
            }
        }

        /// <summary>只保留 trigger.log 的最后 <paramref name="keep"/> 行（后台跑，和追加共用同一把锁）。</summary>
        private static void TrimTriggerLog(string path, int keep)
        {
            try
            {
                lock (_triggerLogGate)
                {
                    if (!System.IO.File.Exists(path)) return;

                    string[] lines = System.IO.File.ReadAllLines(path);
                    if (lines.Length <= keep) return;

                    System.IO.File.WriteAllLines(path, lines[^keep..]);
                }
            }
            catch
            {
                // 裁剪失败无所谓，下次再来
            }
        }

        /// <summary>
        /// 停掉正在运行的宏。
        /// <para>
        /// ★★ 宿主**关闭「功能总开关」**时调（<c>IconService.StopAllModules</c>）。
        ///   宏是插件自己开的循环，不在宿主的运行状态表里，不单独通知就会「总开关关了宏还在跑」。
        /// </para>
        /// </summary>
        public static void StopRunning() => Core.MacroRunner.Stop();

        /// <summary>
        /// 某个触发键**抬起**。
        /// <para>★ 只有「长按循环」在抬手时停；另外两种（执行一次 / 循环执行）抬起都不管。</para>
        /// </summary>
        public static void OnTriggerUpForKey(string key)
        {
            var macro = MacroSettingsStore.FindByTriggerKey(key);
            if (macro?.Mode == MacroTriggerMode.HoldRepeat)
                Core.MacroRunner.Stop();
        }

        /// <summary>
        /// 触发键**按下**（旧宿主调用的无参版本）。
        /// <para>执行的是当前激活的那条宏，起停规则同样按它自己的触发方式 / 生效范围来。</para>
        /// </summary>
        public static void OnTriggerDown()
        {
            var macro = MacroSettingsStore.GetActiveMacro();
            if (macro == null) return;

            // 生效范围：只有「客户区内生效」才看游戏窗口在不在前台
            if (macro.ScopeIndex == (int)MacroScope.ClientOnly)
            {
                if (!Utils.HostBridge.IsGameFocused()) return;
            }

            // 触发条件：只在「客户区内生效」时才判断；选了「主界面」就必须此刻是游戏主界面
            if (macro.ScopeIndex == (int)MacroScope.ClientOnly &&
                macro.ConditionIndex == (int)MacroCondition.MainWindowOnly &&
                !Utils.HostBridge.IsInGameMainWindow())
                return;

            // ★ 按住期间的自动重复：忽略（见 _triggerDownHandled 注释）
            if (_triggerDownHandled) return;
            _triggerDownHandled = true;

            switch (macro.Mode)
            {
                case MacroTriggerMode.HoldRepeat:
                    Core.MacroRunner.Start(macro, loop: true);
                    break;

                case MacroTriggerMode.Toggle:
                    Core.MacroRunner.Toggle(macro);
                    break;

                default:
                    Core.MacroRunner.Start(macro, loop: false);
                    break;
            }
        }

        /// <summary>
        /// 触发键**抬起**（旧宿主调用）。
        /// <para>只有「长按循环」在抬手时停；另外两种抬起都不管。</para>
        /// </summary>
        public static void OnTriggerUp()
        {
            _triggerDownHandled = false;      // 抬手了 ⇒ 下一次按下重新算

            var macro = MacroSettingsStore.GetActiveMacro();
            if (macro?.Mode == MacroTriggerMode.HoldRepeat)
                Core.MacroRunner.Stop();
        }

        /// <summary>
        /// 游戏窗口**失去焦点**（宿主从 <c>WindowFocusService.FocusChanged</c> 转过来）。
        /// <para>★ 只看**正在跑的那条宏**自己的生效范围：客户区内生效的才停，全局生效的继续跑。</para>
        /// </summary>
        public static void OnGameFocusLost()
        {
            if (Core.MacroRunner.CurrentMacro?.ScopeIndex == (int)MacroScope.ClientOnly)
                Core.MacroRunner.Stop();
        }

        // ============ 宿主注入（可选；没注入就降级，不影响功能） ============

        /// <summary>
        /// 宿主注入「全局模态遮罩」的入口（命令, 参数）。命令：<c>mask:show</c> / <c>mask:hide</c>。
        /// <para>
        /// 有了它，插件弹的窗口（新建/编辑宏）打开时主窗口会被压暗，
        /// 和主程序自己的弹窗一个效果；没有就只是不压暗。
        /// </para>
        /// </summary>
        public static void SetHostOverlay(Action<string, object?> handler)
            => Utils.HostBridge.SetOverlay(handler);

        /// <summary>
        /// 宿主注入「确认弹窗」（标题, 正文, 确认文案, 取消文案）→ 是否点确认。
        /// <para>删除宏走它，样式/遮罩与主程序其它确认框完全一致；没注入时退回系统 MessageBox。</para>
        /// </summary>
        public static void SetHostConfirmDialog(Func<string, string, string, string, bool> handler)
            => Utils.HostBridge.SetConfirmDialog(handler);

        /// <summary>
        /// 宿主注入「游戏窗口焦点查询」→ true = 游戏在前台。
        /// <para>给「客户区内生效」用；没注入时按有焦点处理（等价于全局生效）。</para>
        /// </summary>
        public static void SetHostFocusQuery(Func<bool> handler)
            => Utils.HostBridge.SetFocusQuery(handler);

        /// <summary>当前生效范围下标（0=全局 / 1=客户区内）—— 读的是**当前激活宏自己选的那个值**。</summary>
        public static int GetScopeIndex()
        {
            var macro = MacroSettingsStore.GetActiveMacro();
            return macro?.ScopeIndex ?? (int)MacroScope.ClientOnly;
        }

        /// <summary>写生效范围 —— 写进**当前激活宏**（用户选哪个就存哪个，落盘）。</summary>
        public static void SetScopeIndex(int index)
            => MacroSettingsStore.SetActiveScopeIndex(index);

        // ============ 旧版宿主兼容 ============

        /// <summary>
        /// 旧版宿主会调这个把卡片挂到首页末尾。新版宿主改走「卡片第二行控件」，已不再调用它；
        /// 留着是为了「旧宿主 + 新 dll」时不报"模块版本不匹配"。
        /// </summary>
        public static FrameworkElement? GetCardView() => null;
    }
}
