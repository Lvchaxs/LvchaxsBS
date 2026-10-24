using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 「自定义宏」插件适配器（可选模块，2026-10-18 重做）。
    ///
    /// <para>
    /// ★★ 现在它**不是一个特殊卡片**，而是往 <see cref="ModuleRegistry"/> 里塞一条普通
    /// <see cref="ModuleInfo"/> —— 于是首页卡片（可拖动排序）、顶部导航图标、触发键页与占用检测、
    /// 配置容器页，全部自动和其它功能一样，一处特判都不需要。
    /// </para>
    ///
    /// <para>
    /// 约定入口类型 <c>LvchaxsBS_Macro.MacroPlugin</c>（exe 同目录 <c>LvchaxsBS_macro.dll</c>），
    /// 全是 <c>public static</c> 无参方法：
    /// <list type="table">
    ///   <item><term>GetIconUri()</term><description>string：模块图标 pack URI</description></item>
    ///   <item><term>CreateCardExtension()</term><description>FrameworkElement：卡片第二行的控件（两个下拉）</description></item>
    ///   <item><term>CreateConfigPage()</term><description>Page：配置页（目前是空白页）</description></item>
    ///   <item><term>GetEnabled() / SetEnabled(bool)</term><description>总开关（存在插件自己的配置里）</description></item>
    ///   <item><term>GetTriggerKey() / SetTriggerKey(string)</term><description>触发键（同上）</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// ⛔ 插件 dll 缺失 / 版本不匹配 / 抛异常，一律**静默降级**：宿主少一张卡片、少一个导航图标，
    /// 其余功能完全不受影响。
    /// </para>
    /// </summary>
    public static class MacroModuleService
    {
        /// <summary>模块 Id（写进 <c>HomePageSettings.ModuleOrder</c> 的就是它）。</summary>
        public const string ModuleId = "Macro";

        /// <summary>模块显示名（首页卡片标题 / 导航按钮 Tag / 配置容器页标题）。</summary>
        public const string ModuleName = "自定义宏";

        /// <summary>触发键配置标签（<c>ConfigContainerPage.SetTriggerKeyPage</c> 按它找回模块）。</summary>
        public const string KeyTag = "MacroKey";

        private const string ModuleFileName = "LvchaxsBS_macro.dll";
        private const string EntryTypeName = "LvchaxsBS_Macro.MacroPlugin";

        private static bool _probed;
        private static Type? _entry;

        /// <summary>键盘钩子是否已经挂过（<see cref="RegisterTrigger"/> 幂等保护）。</summary>
        private static bool _hooked;

        /// <summary>
        /// 已创建的宏配置界面实例（还没进过宏页面时为 null）。
        /// 宿主用它判断"当前内容区显示的是不是宏页面"，并实现页面记忆
        /// （切到别的页面再切回来，保留上次浏览到的文件夹 / 滚动位置）。
        /// </summary>
        private static FrameworkElement? _view;

        /// <summary>
        /// 已创建的宏配置界面实例（供宿主判断当前页 + 页面记忆）。还没进过宏页面时为 null。
        /// </summary>
        public static FrameworkElement? CurrentView => _view;

        /// <summary>
        /// 取宏配置界面（内容区直接承载的 Page）。模块未加载 / 版本不匹配时返回 null。
        /// <para>
        /// 模块内部是<b>单例</b>：多次调用拿到的是同一个实例（缓存到 <see cref="_view"/>），
        /// 因此宿主切到别的页面再切回来时，用户上次浏览到的文件夹 / 滚动位置会被保留。
        /// 必须在 UI 线程调用。
        /// </para>
        /// </summary>
        public static FrameworkElement? GetMacroConfigView()
        {
            Probe();
            if (!IsAvailable) return null;
            if (_view != null) return _view;

            _view = CreateConfigPage();
            return _view;
        }

        /// <summary>
        /// 重新探测一次（dll 补上后热加载用）。
        /// <para>
        /// ★ 只清探测缓存再跑一遍 <see cref="Probe"/>：不动已经建好的界面，
        ///   首页下次打开就会多出这张卡片、左侧主导航的「自定义宏」按钮也会显示
        ///   （见 <see cref="MainWindow.RefreshOptionalModules"/>）。
        /// ★ 已经装过并成功加载的情况下**不要**重复调 —— 同一个程序集 LoadFrom 两次是安全的，
        ///   但重复注入遮罩/焦点查询虽然幂等，都是无谓动作。
        /// </para>
        /// </summary>
        public static void Reload()
        {
            _probed = false;
            _entry = null;
            _view = null;        // 旧实例属于旧程序集，必须丢弃，下次进入重建
            Probe();
        }

        /// <summary>exe 同目录下是否存在可用的自定义宏模块。</summary>
        public static bool IsAvailable
        {
            get
            {
                Probe();
                return _entry != null;
            }
        }

        /// <summary>
        /// 探测并载入模块。可重复调用，只真正执行一次；失败一律静默。
        /// </summary>
        public static void Probe()
        {
            if (_probed) return;
            _probed = true;

            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModuleFileName);
                if (!File.Exists(path))
                {
                    Debug.WriteLine($"[Macro] 未找到模块：{path}");
                    return;
                }

                Assembly asm = Assembly.LoadFrom(path);

                var type = asm.GetType(EntryTypeName, throwOnError: false);
                if (type == null)
                {
                    Debug.WriteLine($"[Macro] 模块缺少入口类型：{EntryTypeName}");
                    return;
                }

                // 至少要能给出卡片第二行的控件和配置页，否则当版本不匹配处理
                if (!HasStaticMethod(type, "CreateCardExtension") || !HasStaticMethod(type, "CreateConfigPage"))
                {
                    Debug.WriteLine("[Macro] 模块版本不匹配：缺少 CreateCardExtension / CreateConfigPage，" +
                                    "请使用与本程序配套的 LvchaxsBS_macro.dll");
                    return;
                }

                _entry = type;
                Debug.WriteLine("[Macro] 模块加载成功");

                InjectOverlay(type);
                InjectConfirmDialog(type);
                InjectFocusQuery(type);
                RegisterTrigger();
            }
            catch (Exception ex)
            {
                _entry = null;
                Debug.WriteLine($"[Macro] 模块加载失败：{ex.Message}");
            }
        }

        private static bool HasStaticMethod(Type type, string name)
            => type.GetMethod(name, BindingFlags.Public | BindingFlags.Static) != null;

        // ============ 宿主能力注入（可选；模块没有这些方法就跳过） ============

        /// <summary>
        /// 把宿主的全局模态遮罩注入给模块：模块弹的窗口（新建/编辑宏）打开时整个窗口会被压暗。
        /// <para>模块没有宿主的编译期引用，所以用「命令 + 参数」的单一委托驱动，
        /// 命令含义见 <see cref="ModalOverlayService.HandleModuleCommand"/>（<c>mask:show</c> / <c>mask:hide</c>）。</para>
        /// </summary>
        private static void InjectOverlay(Type type)
        {
            var setOverlay = type.GetMethod("SetHostOverlay", BindingFlags.Public | BindingFlags.Static);
            if (setOverlay == null || setOverlay.GetParameters().Length != 1)
            {
                Debug.WriteLine("[Macro] 模块未提供 SetHostOverlay，弹窗不压暗");
                return;
            }

            try
            {
                setOverlay.Invoke(null, new object[]
                {
                    new Action<string, object?>(ModalOverlayService.HandleModuleCommand)
                });
                Debug.WriteLine("[Macro] 已注入宿主遮罩");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 注入遮罩失败：{ex.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// 把宿主的确认弹窗注入给模块（删除宏前的二次确认就用它）。
        /// <para>样式、遮罩与主程序其它确认框完全一致；模块不需要自己画一份。</para>
        /// </summary>
        private static void InjectConfirmDialog(Type type)
        {
            var setConfirm = type.GetMethod("SetHostConfirmDialog", BindingFlags.Public | BindingFlags.Static);
            if (setConfirm == null || setConfirm.GetParameters().Length != 1)
            {
                Debug.WriteLine("[Macro] 模块未提供 SetHostConfirmDialog，确认框将退回系统 MessageBox");
                return;
            }

            try
            {
                setConfirm.Invoke(null, new object[]
                {
                    new Func<string, string, string, string, bool>((title, message, okText, cancelText) =>
                    {
                        // ★ 全限定名：本文件在 LvchaxsBS.Services 下，写 UI.Dialogs 会被解析成 LvchaxsBS.Services.UI
                        var dialog = new LvchaxsBS.UI.Dialogs.ConfirmDialog(title, message, okText, cancelText)
                        {
                            Owner = Application.Current?.MainWindow
                        };
                        // 通用复选框是给「重置配置」那类场景用的，这里不需要
                        dialog.SetOptionCheckBox(visible: false);

                        return ModalOverlayService.ShowModalDialog(dialog) == true;
                    })
                });
                Debug.WriteLine("[Macro] 已注入宿主确认弹窗");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 注入确认弹窗失败：{ex.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// 把宿主的「游戏窗口焦点查询」注入给模块 —— 「客户区内生效」靠它判断。
        /// <para>
        /// ★ 顺带订阅 <c>WindowFocusService.FocusChanged</c>：游戏一失焦就通知模块，
        ///   由模块决定要不要停（选了「客户区内生效」才停）。
        /// </para>
        /// </summary>
        private static void InjectFocusQuery(Type type)
        {
            var setFocus = type.GetMethod("SetHostFocusQuery", BindingFlags.Public | BindingFlags.Static);
            if (setFocus == null || setFocus.GetParameters().Length != 1)
            {
                Debug.WriteLine("[Macro] 模块未提供 SetHostFocusQuery，「客户区内生效」将退化为全局生效");
                return;
            }

            try
            {
                setFocus.Invoke(null, new object[]
                {
                    // ★★ 用 IsTargetFocusedNow()（实时查 Win32，只读）而不是 IsTargetFocused（轮询缓存）：
                    //   缓存非焦点时一秒才刷新，「刚把游戏切到前台就按触发键」会被误判成"不在客户区"
                    //   而完全不响应 —— 用户报「宏都没有反应」很可能就是这个。
                    //   ⛔ 也不能用 Reevaluate()：它在钩子线程里会 raise 事件 + 操作 DispatcherTimer。
                    new Func<bool>(() => Hooks.WindowFocusService.IsTargetFocusedNow())
                });
                Debug.WriteLine("[Macro] 已注入宿主焦点查询");

                // 游戏失焦时通知模块（Probe 有 _probed 保护，重复调用不会重复订阅）
                Hooks.WindowFocusService.FocusChanged += (_, focused) =>
                {
                    if (!focused) Call("OnGameFocusLost");
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 注入焦点查询失败：{ex.GetBaseException().Message}");
            }
        }

        // ============ 触发键 → 执行宏 ============

        /// <summary>
        /// 挂上全局键盘钩子：触发键按下/抬起 → 调插件的 <c>OnTriggerDown</c> / <c>OnTriggerUp</c>。
        /// <para>
        /// ★ 钩子是宿主在 <c>App.InitializeEngine</c> 里常开的（所有功能共用这一个），
        ///   这里只是加一个订阅者，不用自己再去 Start/Stop。
        /// </para>
        /// </summary>
        private static void RegisterTrigger()
        {
            // ★ 幂等：热加载（Reload）会再走一遍 Probe，重复 += 会让触发键一次按下执行两遍
            if (_hooked) return;

            try
            {
                GlobalKeyboardHookService.KeyboardEvent += OnGlobalKeyboardEvent;
                _hooked = true;
                Debug.WriteLine("[Macro] 已挂上键盘钩子（触发键 → 执行宏）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 挂键盘钩子失败：{ex.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// 功能总开关是否开启（<see cref="Config.HomePageSettings.MasterSwitch"/>）。
        /// <para>★ 读配置异常按「开启」处理，避免因为读不到配置就把功能整个禁用掉。</para>
        /// </summary>
        private static bool IsMasterSwitchOn()
        {
            try
            {
                return Config.ConfigManager.Get<Config.HomePageSettings>().MasterSwitch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 读取功能总开关失败，按开启处理：{ex.GetBaseException().Message}");
                return true;
            }
        }

        /// <summary>
        /// 游戏窗口**此刻存在**吗（不要求前台）。
        /// <para>
        /// ★ 给插件判断「客户区内生效」用：游戏压根没开的时候不该拦 ——
        ///   否则用户在主程序里测宏（游戏未启动）会发现「按什么都没反应」。
        /// </para>
        /// </summary>
        public static bool GetGameWindowPresent() => Hooks.WindowFocusService.IsWindowPresent;

        /// <summary>
        /// 主程序「功能总开关」当前状态（给插件读，用来把胶囊开关跟着变灰）。
        /// <para>★ 读配置异常按「开启」处理，和 <see cref="IconService"/> 的策略一致。</para>
        /// </summary>
        public static bool GetMasterSwitch() => IsMasterSwitchOn();

        /// <summary>
        /// 停掉正在运行的宏（**功能总开关关闭**时由 <c>IconService.StopAllModules</c> 调用）。
        /// <para>★ 宏是插件自己开的循环，不在宿主的 <c>_runningStates</c> 里，所以必须单独通知。</para>
        /// </summary>
        public static void StopRunning() => Call("StopRunning");

        /// <summary>
        /// 钩子回调（**不是 UI 线程**）。★ 必须尽快返回：这里只做判断 + 转发，不阻塞。
        /// </summary>
        private static void OnGlobalKeyboardEvent(object? sender, KeyboardEventArgs e)
        {
            try
            {
                if (!GetEnabled()) return;

                // ★★ 功能总开关（首页那个「功能总开关」）也要管住自定义宏 ——
                //    关掉之后所有功能都不执行（快速传送/拾取等都在各自入口判断它），
                //    宏的触发键同样不该响应，否则"关了总开关宏还能跑"就是漏洞。
                if (!IsMasterSwitchOn()) return;

                // ★★ 必须挡掉"程序自己模拟出来的按键"：脚本里 KeyPress F 也会被这个钩子看到，
                //    不挡就会自己触发自己 → 无限套娃。
                if (e.IsInjected) return;

                // ★★ 按住不放时系统会每 ~30ms 补发一次 KeyDown（自动重复）——必须挡掉：
                //    否则「长按时重复循环」会被无限 Stop+Start，脚本永远跑不完一遍。
                if (e.IsRepeat) return;

                // ★★ v2：每条宏各自一个触发键 —— 先在「全部触发键」里找命中的那个
                string[] keys = GetTriggerKeys();
                for (int i = 0; i < keys.Length; i++)
                {
                    // ★★★ 必须用 IconService 的组合键匹配器，**不能**直接比对 e.KeyName：
                    //   e.KeyName 是单个键名（"W" / "左Ctrl"），而宏的触发键可能是
                    //   "左Ctrl+左Alt+W" 这种完整组合 ⇒ 用 == 永远匹配不上 ⇒ 宏"全都无法触发"。
                    //   这个匹配器同时处理"单键但此刻按着 Ctrl/Alt 不算命中"。
                    if (!Core.IconService.MatchesTriggerKey(keys[i], e.KeyName)) continue;

                    Call(e.EventType == KeyboardEventType.KeyDown ? "OnTriggerDownForKey" : "OnTriggerUpForKey",
                         keys[i]);
                    return;
                }
                
                // 旧插件（没有 GetTriggerKeys）或旧配置：退回原来的单键逻辑
                if (keys.Length > 0) return;

                string trigger = GetTriggerKey();
                if (trigger.Length == 0 || trigger == "未设置") return;
                if (!Core.IconService.MatchesTriggerKey(trigger, e.KeyName)) return;

                // 有些宏（比如按住型）本身就要按住触发键，抬起时才是"结束" —— 
                // 所以按下/抬起都要转发给插件，由它按触发方式决定起停。
                Call(e.EventType == KeyboardEventType.KeyDown ? "OnTriggerDown" : "OnTriggerUp");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 触发处理失败：{ex.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// 调插件里一个无参/单参的静态方法；没这个方法、返回类型不对、内部抛异常 —— 一律返回 null。
        /// </summary>
        private static object? Call(string methodName, params object?[] args)
        {
            Probe();
            if (_entry == null) return null;

            try
            {
                var mi = _entry.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (mi == null) return null;
                return mi.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 调用 {methodName} 失败：{ex.GetBaseException().Message}");
                return null;
            }
        }

        // ============ 供 ModuleInfo 用的取值/写值 ============

        /// <summary>模块图标（插件自己 dll 里的 pack URI）。</summary>
        public static string? GetIconUri() => Call("GetIconUri") as string;

        /// <summary>卡片第二行的控件（两个下拉）。★ 只能在 UI 线程调。</summary>
        public static FrameworkElement? CreateCardExtension()
            => Call("CreateCardExtension") as FrameworkElement;

        /// <summary>
        /// 卡片**第一行标题右侧**的控件（「宏文件 N 个」标签 + 编辑按钮）。★ 只能在 UI 线程调。
        /// <para>插件没有这个方法（旧版 dll）⇒ 返回 null，首页照旧显示分辨率标签（宏本来就 CreateTags=null，就是不显示）。</para>
        /// </summary>
        public static FrameworkElement? CreateTagExtension()
            => Call("CreateTagExtension") as FrameworkElement;

        /// <summary>配置页（目前是空白页）。</summary>
        public static Page? CreateConfigPage() => Call("CreateConfigPage") as Page;

        /// <summary>
        /// 底部按钮条最前面的那个按钮（「添加宏」）。★ 只能在 UI 线程调。
        /// </summary>
        public static FrameworkElement? CreateToolbarButton()
            => Call("CreateToolbarButton") as FrameworkElement;

        /// <summary>总开关当前值（插件自己的配置）。</summary>
        public static bool GetEnabled() => Call("GetEnabled") is bool b && b;

        /// <summary>写总开关（插件自己落盘）。</summary>
        public static void SetEnabled(bool value) => Call("SetEnabled", value);

        private static string[] _cachedKeys = Array.Empty<string>();

        /// <summary>
        /// 上次查询时间（<see cref="Environment.TickCount64"/>，毫秒）。
        /// <para>
        /// ★★★ 这里**绝不能**用 <c>int.MinValue</c> 当初值：
        ///   <c>Environment.TickCount</c> 是 int，`now - int.MinValue` 会**整数溢出成负数**，
        ///   于是 `now - 初值 &lt; 1000` 永远成立 ⇒ 永远直接返回空缓存、**一次都不去问插件**
        ///   ⇒ 钩子拿到的触发键列表恒为空 ⇒ 所有宏都无法触发
        ///   （这正是「原先单触发键能用、改成每条宏一个键后全都不行」的真凶：
        ///     原先走的是 <c>GetTriggerKey()</c>，压根没有这段缓存）。
        /// </para>
        /// <para>★ 用 <c>TickCount64</c> + 一个独立标志，彻底避开溢出。</para>
        /// </summary>
        private static long _keysCacheTick;
        private static bool _hasKeysCache;

        /// <summary>
        /// 插件当前要监听的全部触发键（v2：每条宏各一个）。
        /// <para>★ 插件改了键/开关后最多 1 秒生效 —— 钩子回调每键都会问一次，不能每次都反射。</para>
        /// <para>★ 旧插件没有 <c>GetTriggerKeys</c> ⇒ 返回空数组，调用方退回单键逻辑。</para>
        /// </summary>
        public static string[] GetTriggerKeys()
        {
            long now = Environment.TickCount64;
            if (_hasKeysCache && now - _keysCacheTick < 1000) return _cachedKeys;

            _keysCacheTick = now;
            _hasKeysCache = true;

            try
            {
                if (Call("GetTriggerKeys") is System.Collections.IEnumerable seq)
                {
                    var list = new List<string>();
                    foreach (var o in seq)
                    {
                        if (o is string str && str.Length > 0) list.Add(str);
                    }
                    _cachedKeys = list.ToArray();
                    return _cachedKeys;
                }
            }
            catch
            {
                // 插件内部异常 → 当作没有多键
            }

            _cachedKeys = Array.Empty<string>();
            return _cachedKeys;
        }

        // ============ 给插件用的「按键网格页」（和快速切图绑键同一套） ============

        /// <summary>
        /// 造一个**配好的**触发键页（宿主那张键盘网格），交给插件自己承载。
        ///
        /// <para>
        /// ★★ 与「快速切图给地区绑键」走的是**同一个控件**：
        ///   <c>EnableComboModifierMode()</c> → <c>SetSharedKeyOwners()</c> → <c>LoadKey()</c>。
        ///   观感/组合键支持与快速切图完全一致。
        /// </para>
        ///
        /// <para>
        /// 插件之所以不能自己 new，是因为它是反射加载的独立 dll，没有宿主的编译期引用；
        /// 而 TriggerKeyPage 里的 <c>KeyButtonStyle</c>、占用图标等都依赖宿主资源。
        /// </para>
        /// </summary>
        /// <param name="currentKey">这条宏当前的触发键（"未设置" = 没有）。</param>
        /// <param name="occupied">
        /// **别的宏**已经占用的键 → 按钮底部小字的显示名（如 <c>"F8" → "挖矿宏"</c>，
        /// 多个宏共用同一个键时是 <c>"F8" → "3个宏"</c>）。
        /// <para>★ 2026-10-22 起宏之间**允许共用同一个触发键**（同时只能有一个启用），
        ///   所以这些键不再禁选、也不拦组合键，只在按钮上标注占用者。</para>
        /// <para>★ 组合键请传完整键名（<c>"左Ctrl+F"</c>）：标注只取基础键。</para>
        /// </param>
        /// <param name="onSelected">点了新键 = 选好了。插件在这里保存并关闭页面。</param>
        /// <param name="onAdjusted">只调整了组合键（Ctrl/Alt）= 还没选完。插件保存但**不**关页面。</param>
        /// <param name="onCleared">点了已选中的键 = 取消。插件清配置但**不**关页面。</param>
        public static Page? CreateKeyPicker(
            string? currentKey,
            IDictionary<string, string>? occupied,
            Action<string>? onSelected,
            Action<string>? onAdjusted,
            Action? onCleared)
        {
            try
            {
                var page = new LvchaxsBS.UI.Pages.TriggerKeyPage();

                // ★ 组合键支持：必须在其他配置之前打开（页面要据此调整 Ctrl/Alt 的显示）
                page.EnableComboModifierMode();

                if (occupied != null && occupied.Count > 0)
                {
                    // 页面上一个按钮就是一个键 ⇒ 占用标注只能按**基础键**匹配
                    // （同一基础键只写一个显示名，插件那边已按「N个宏」规则合并好）
                    var shared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in occupied)
                    {
                        string baseKey = BaseKeyOf(kv.Key);
                        if (!string.IsNullOrEmpty(baseKey) && !shared.ContainsKey(baseKey))
                            shared[baseKey] = kv.Value;
                    }

                    page.SetSharedKeyOwners(shared);
                }

                page.LoadKey(string.IsNullOrWhiteSpace(currentKey) ? "未设置" : currentKey);

                page.KeySelected += (_, key) => onSelected?.Invoke(key);
                page.ComboAdjusted += (_, key) => onAdjusted?.Invoke(key);
                page.KeyCleared += (_, _) => onCleared?.Invoke();

                return page;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 创建触发键页失败：{ex.GetBaseException().Message}");
                return null;
            }
        }

        /// <summary>取组合键的基础键："左Ctrl+F" → "F"；不含 '+' 原样返回。</summary>
        private static string BaseKeyOf(string? key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            int plus = key.LastIndexOf('+');
            return plus < 0 ? key : key[(plus + 1)..].Trim();
        }

        public static string GetTriggerKey() => Call("GetTriggerKey") as string ?? "未设置";

        /// <summary>写触发键（插件自己落盘）。</summary>
        public static void SetTriggerKey(string key) => Call("SetTriggerKey", key);

        // ============ 组装成宿主的 ModuleInfo ============

        /// <summary>
        /// 把插件描述成一条普通模块。dll 不在 / 版本不对 → 返回 null（<see cref="ModuleRegistry.All"/> 里就没有它）。
        /// <para>
        /// ★ 开关与触发键委托给**插件自己的配置**（<c>Data\Macro\macro.json</c>），
        ///   所以 <c>HomePageSettings</c> 里不会多出这两个字段 —— 编辑器签名带的那个 settings 参数在这里被忽略。
        /// </para>
        /// </summary>
        public static ModuleInfo? CreateModuleInfo()
        {
            if (!IsAvailable) return null;

            string icon = GetIconUri() ?? "";

            return new ModuleInfo
            {
                Id = ModuleId,
                Name = ModuleName,

                // 用户要求：这张卡片**没有描述**（第二行放的是控件）
                Description = "",

                // ★★ 2026-10-21 用户要求：**首页不要这张卡片**，入口只保留顶部「自定义宏」按钮
                //    （点进去直接是宏列表，不再有文件夹层级）
                ShowOnHome = false,

                IconPath = icon,
                KeyTag = KeyTag,

                // 和其它功能一样走键盘触发键页（⇒ 参与「被其它功能占用」检测）；不是手柄拾取
                UseGamepad = false,

                // 不显示分辨率标签（宏跟分辨率无关）—— 标题行右边改放插件自己的
                // 「宏文件 N 个」标签 + 编辑按钮（CreateTagExtension，见 HomePage.xaml）
                CreateTags = null,

                CreateConfigPage = () => CreateConfigPage() ?? new Page(),
                CreateCardExtension = () => CreateCardExtension(),
                CreateTagExtension = () => CreateTagExtension(),

                // 底部按钮条最前面的「添加宏」（效果 = 添加宏 / 触发键 / 配置 / 返回 / 开关）
                CreateToolbarElement = () => CreateToolbarButton(),

                IsEnabled = _ => GetEnabled(),
                SetEnabled = (_, value) => SetEnabled(value),
                GetKey = _ => GetTriggerKey(),
                SetKey = (_, key) => SetTriggerKey(key),
            };
        }
    }
}
