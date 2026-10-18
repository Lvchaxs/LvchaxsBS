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

        /// <summary>卡片第二行的控件（宏名称 / 触发方式两个下拉）。★ 只能在 UI 线程调。</summary>
        public static FrameworkElement CreateCardExtension() => new MacroCardView();

        /// <summary>配置页（★ 现在开始有内容了：宏卡片列表）。</summary>
        public static Page CreateConfigPage() => new MacroConfigPage();

        /// <summary>
        /// 配置容器页底部按钮条最前面的按钮 —— 「添加宏 / 触发键 / 配置 / 返回 / 开关」里的第一个。
        /// </summary>
        public static FrameworkElement CreateToolbarButton() => new Controls.AddMacroButton();

        /// <summary>胶囊开关当前值。</summary>
        public static bool GetEnabled() => MacroSettingsStore.Current.Enabled;

        /// <summary>写胶囊开关（立刻落盘到 <c>Config\Macro\macro.json</c>）。</summary>
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

        /// <summary>
        /// 触发键**按下**（宿主 <c>MacroModuleService</c> 在键盘钩子里调）。
        /// <para>执行的是首页卡片下拉选中的那条宏 —— 三条触发方式的起停规则各不相同。</para>
        /// </summary>
        public static void OnTriggerDown()
        {
            // ★「客户区内生效」：游戏窗口不在前台 ⇒ 触发键完全不响应（也不停止正在跑的）
            if (MacroSettingsStore.IsClientOnly && !Utils.HostBridge.IsGameFocused()) return;

            var macro = MacroSettingsStore.GetActiveMacro();
            if (macro == null) return;

            // ★ 按住期间的自动重复：忽略（见 _triggerDownHandled 注释）
            if (_triggerDownHandled) return;
            _triggerDownHandled = true;

            switch (macro.Mode)
            {
                case MacroTriggerMode.Once:
                    // 按下后整段跑一遍就结束（不用等抬手，抬手也不会打断）
                    Core.MacroRunner.Start(macro, loop: false);
                    break;

                case MacroTriggerMode.HoldRepeat:
                    // 按住期间整段循环执行，松开时停
                    Core.MacroRunner.Start(macro, loop: true);
                    break;

                case MacroTriggerMode.Toggle:
                    // 按一下开始（整段循环执行），再按一下停
                    Core.MacroRunner.Toggle(macro);
                    break;
            }
        }

        /// <summary>
        /// 触发键**抬起**（宿主调）。
        /// <para>只有「长按循环」在抬手时停；另外两种抬起都不管。</para>
        /// </summary>
        public static void OnTriggerUp()
        {
            _triggerDownHandled = false;      // 抬手了 ⇒ 下一次按下重新算

            var macro = MacroSettingsStore.GetActiveMacro();
            if (macro == null) return;

            if (macro.Mode == MacroTriggerMode.HoldRepeat)
                Core.MacroRunner.Stop();
        }

        /// <summary>
        /// 游戏窗口**失去焦点**（宿主从 <c>WindowFocusService.FocusChanged</c> 转过来）。
        /// <para>★ 只有「客户区内生效」才停：宏不该跑到别的窗口里去。</para>
        /// </summary>
        public static void OnGameFocusLost()
        {
            if (!MacroSettingsStore.IsClientOnly) return;
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

        /// <summary>当前生效范围下标（0=全局 / 1=客户区内） —— 宿主卡片下拉回显用。</summary>
        public static int GetScopeIndex() => MacroSettingsStore.GetScopeIndex();

        /// <summary>写生效范围（宿主卡片下拉选完调）。</summary>
        public static void SetScopeIndex(int index) => MacroSettingsStore.SetScopeIndex(index);

        // ============ 旧版宿主兼容 ============

        /// <summary>
        /// 旧版宿主会调这个把卡片挂到首页末尾。新版宿主改走「卡片第二行控件」，已不再调用它；
        /// 留着是为了「旧宿主 + 新 dll」时不报"模块版本不匹配"。
        /// </summary>
        public static FrameworkElement GetCardView() => new MacroCardView();
    }
}
