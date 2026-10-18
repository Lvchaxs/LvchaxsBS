using System;
using System.Diagnostics;
using System.IO;
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
        private static void Probe()
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
                    new Func<bool>(() => Hooks.WindowFocusService.IsTargetFocused)
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
            try
            {
                GlobalKeyboardHookService.KeyboardEvent += OnGlobalKeyboardEvent;
                Debug.WriteLine("[Macro] 已挂上键盘钩子（触发键 → 执行宏）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Macro] 挂键盘钩子失败：{ex.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// 钩子回调（**不是 UI 线程**）。★ 必须尽快返回：这里只做判断 + 转发，不阻塞。
        /// </summary>
        private static void OnGlobalKeyboardEvent(object? sender, KeyboardEventArgs e)
        {
            try
            {
                if (!GetEnabled()) return;

                // ★★ 必须挡掉"程序自己模拟出来的按键"：脚本里 KeyPress F 也会被这个钩子看到，
                //    不挡就会自己触发自己 → 无限套娃。
                if (e.IsInjected) return;

                // ★★ 按住不放时系统会每 ~30ms 补发一次 KeyDown（自动重复）——必须挡掉：
                //    否则「长按时重复循环」会被无限 Stop+Start，脚本永远跑不完一遍。
                if (e.IsRepeat) return;

                string trigger = GetTriggerKey();
                if (trigger.Length == 0 || trigger == "未设置") return;
                if (!string.Equals(e.KeyName, trigger, StringComparison.OrdinalIgnoreCase)) return;

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

        /// <summary>触发键当前值（"未设置" = 没设置过）。</summary>
        public static string GetTriggerKey() => Call("GetTriggerKey") as string ?? "未设置";

        /// <summary>写触发键（插件自己落盘）。</summary>
        public static void SetTriggerKey(string key) => Call("SetTriggerKey", key);

        // ============ 组装成宿主的 ModuleInfo ============

        /// <summary>
        /// 把插件描述成一条普通模块。dll 不在 / 版本不对 → 返回 null（<see cref="ModuleRegistry.All"/> 里就没有它）。
        /// <para>
        /// ★ 开关与触发键委托给**插件自己的配置**（<c>Config\Macro\config.json</c>），
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

                IconPath = icon,
                KeyTag = KeyTag,

                // 和其它功能一样走键盘触发键页（⇒ 参与「被其它功能占用」检测）；不是手柄拾取
                UseGamepad = false,

                // 不显示分辨率标签（宏跟分辨率无关）
                CreateTags = null,

                CreateConfigPage = () => CreateConfigPage() ?? new Page(),
                CreateCardExtension = () => CreateCardExtension(),

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
