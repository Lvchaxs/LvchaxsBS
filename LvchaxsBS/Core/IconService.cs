using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Core.Function;
using LvchaxsBS.UI.Helpers;
using LvchaxsBS.Services;
using LvchaxsBS.Services.Common;
using LvchaxsBS.Services.Hooks;
using LvchaxsBS.Services.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfImage = System.Windows.Controls.Image;

namespace LvchaxsBS.Core
{
    public static class IconService
    {
        public static event Action? QuickTeleportTriggered;

        #region 位置配置

        private const double REF_SPACING = -3;

        #endregion

        #region 字段

        private static Window? _overlayWindow;
        private static bool _isInitialized = false;
        private static bool _isOverlayVisible = false;
        private static List<BitmapImage?>? _cachedImages = null;

        private static readonly Dictionary<string, string[]> _iconMap = BuildIconMap();

        /// <summary>
        /// 图标位定义：模块名 → 可用图标名（第一个是默认）。
        /// <para>
        /// ★ 「自定义宏」是**可选插件模块**：exe 同目录有 <c>LvchaxsBS_macro.dll</c> 才占一个图标位 ——
        /// 否则游戏内那排会多出一个加载不到图的空位。
        /// </para>
        /// </summary>
        private static Dictionary<string, string[]> BuildIconMap()
        {
            var map = new Dictionary<string, string[]>
            {
                ["快速拾取"] = new[] { "拾取1.png", "拾取2.png", "拾取3.png" },
                ["剧情对话"] = new[] { "剧情1.png", "剧情2.png", "剧情3.png" },
                ["钓鱼辅助"] = new[] { "钓鱼1.png", "钓鱼2.png", "钓鱼3.png" },
                ["快速传送"] = new[] { "锚点1.png", "锚点2.png", "锚点3.png" },
                ["自动伐木"] = new[] { "伐木1.png", "伐木2.png" },
                ["自动烹饪"] = new[] { "烹饪1.png", "烹饪2.png" },
            };

            if (MacroModuleService.IsAvailable)
                map[MacroModuleService.ModuleName] = new[] { "自定义宏1.png", "自定义宏2.png" };

            return map;
        }

        private static readonly List<string> _moduleOrder = new();

        /// <summary>各图标位当前显示的图片名，用于避免重复加载/重排。</summary>
        private static readonly Dictionary<int, string> _iconNameCache = new();

        private static readonly Dictionary<string, bool> _runningStates = new()
        {
            ["快速拾取"] = false,
            ["剧情对话"] = false,
            ["钓鱼辅助"] = false,
            ["自动伐木"] = false,
            ["自动烹饪"] = false,
        };

        private static readonly HashSet<string> _coexistGroup = new()
        {
            "快速拾取", "剧情对话"
        };

        private static readonly HashSet<string> _exclusiveGroup = new()
        {
            "钓鱼辅助", "自动伐木", "自动烹饪"
        };

        private static int _currentWidth = 0;
        private static int _currentHeight = 0;
        private static int _currentLeft = 0;
        private static int _currentTop = 0;
        private static bool _isFocused = false;
        private static bool _isInMainWindow = false;
        private static bool _isInMap = false;

        // 自动清药期间暂停 DetectionManager 的标记
        private static bool _detectionPausedByMedicine = false;

        private static readonly Dictionary<string, bool> _keyHandledStates = new();

        private static string _fishingRodStatus = "";
        private static string _fishingTensionDir = "";
        private static bool _fishingHookedLocked = false;

        private const int OPEN_MAP_WINDOW_MS = 1000;
        private static DateTime _openMapWindowUntil = DateTime.MinValue;
        private static bool _openMapKeyPressed = false;
        private static bool _openMapKeyValid = false;

        #endregion

        #region 初始化

        public static void Initialize()
        {
            if (_isInitialized) return;

            DpiService.Initialize();

            foreach (var kvp in _iconMap)
            {
                _moduleOrder.Add(kvp.Key);
            }

            _cachedImages = new List<BitmapImage?>();
            foreach (var kvp in _iconMap)
            {
                var bitmap = ResourceImageHelper.Load(
                    AssetLocator.Pack($"Images/图标/{kvp.Value[0]}"));
                _cachedImages.Add(bitmap);
            }

            WindowFocusService.FocusChanged += OnFocusChanged;
            WindowFocusService.BoundsChanged += OnBoundsChanged;

            _isFocused = WindowFocusService.IsTargetFocused;
            var initBounds = WindowFocusService.LastBounds;
            if (!initBounds.IsEmpty)
            {
                _currentWidth = initBounds.Width;
                _currentHeight = initBounds.Height;
                _currentLeft = initBounds.Left;
                _currentTop = initBounds.Top;
            }

            GlobalKeyboardHookService.KeyboardEvent += OnKeyboardEvent;
            GlobalMouseHookService.MouseEvent += OnMouseEvent;

            DetectionManager.Initialize();
            DetectionManager.DetectionResultChanged += OnDetectionResultChanged;
            DetectionManager.MapDetectionResultChanged += OnMapDetectionResultChanged;

            AutoCookLogic.QualitySelectWindowOpened += OnQualitySelectWindowOpened;
            StoryDialogueLogic.DetectionStateChanged += OnStoryDialogueDetectionStateChanged;
            FishingAssistLogic.DetectionResultUpdated += OnFishingRodStatusUpdated;
            FishingAssistLogic.TensionResultUpdated += OnFishingTensionUpdated;

            AutoCookLogic.Stopped += OnAutoCookStopped;
            AutoLumberLogic.Stopped += OnAutoLumberStopped;
            AutoClearMedicineLogic.Stopped += OnAutoClearMedicineStopped;

            if (WindowFocusService.IsTargetFocused && IsMasterSwitchOn())
            {
                DetectionManager.StartLoop();
            }

            // 宏图标状态看守（插件漏报时兜底纠正）
            StartMacroStateWatcher();

            _isInitialized = true;
        }

        public static void Shutdown()
        {
            if (!_isInitialized) return;

            OverlayWindowService.StopTopmostKeepAlive(_overlayWindow!);

            WindowFocusService.FocusChanged -= OnFocusChanged;
            WindowFocusService.BoundsChanged -= OnBoundsChanged;

            GlobalKeyboardHookService.KeyboardEvent -= OnKeyboardEvent;
            GlobalMouseHookService.MouseEvent -= OnMouseEvent;
            DetectionManager.DetectionResultChanged -= OnDetectionResultChanged;
            DetectionManager.MapDetectionResultChanged -= OnMapDetectionResultChanged;
            AutoCookLogic.QualitySelectWindowOpened -= OnQualitySelectWindowOpened;
            StoryDialogueLogic.DetectionStateChanged -= OnStoryDialogueDetectionStateChanged;
            FishingAssistLogic.DetectionResultUpdated -= OnFishingRodStatusUpdated;
            FishingAssistLogic.TensionResultUpdated -= OnFishingTensionUpdated;
            AutoCookLogic.Stopped -= OnAutoCookStopped;
            AutoLumberLogic.Stopped -= OnAutoLumberStopped;
            AutoClearMedicineLogic.Stopped -= OnAutoClearMedicineStopped;

            DetectionManager.Shutdown();
            CloseOverlay();
            _isInitialized = false;
        }

        #endregion

        #region 统一字幕入口

        public static void ShowSubtitle(SubtitleOverlayService.SubtitleOwner owner)
        {
            string key = owner switch
            {
                SubtitleOverlayService.SubtitleOwner.AutoCook => "自动烹饪_字幕显示坐标",
                SubtitleOverlayService.SubtitleOwner.Fishing => "钓鱼辅助_字幕显示坐标",
                SubtitleOverlayService.SubtitleOwner.AutoLumber => "自动伐木_字幕显示坐标",
                _ => ""
            };

            SubtitleOverlayService.ResetAvatarForNewRun();
            SubtitleOverlayService.SetPositionKey(key);
            SubtitleOverlayService.ShowSubtitle(owner);   // ★ 认主：之后只有同一模块能收起这一行
        }

        public static void ShowSubtitle2(SubtitleOverlayService.SubtitleOwner owner)
        {
            string key = owner switch
            {
                SubtitleOverlayService.SubtitleOwner.AutoCook => "自动烹饪_字幕显示坐标",
                SubtitleOverlayService.SubtitleOwner.Fishing => "钓鱼辅助_字幕显示坐标",
                SubtitleOverlayService.SubtitleOwner.AutoLumber => "自动伐木_字幕显示坐标",
                _ => ""
            };

            SubtitleOverlayService.SetPositionKey(key);
            SubtitleOverlayService.ShowSubtitle2();
        }

        #endregion

        #region 运行状态管理

        private static bool IsRunning(string module) =>
            _runningStates.TryGetValue(module, out var v) && v;

        private static void SetRunning(string module, bool running)
        {
            if (_runningStates.ContainsKey(module))
                _runningStates[module] = running;
        }

        private static bool AnyRunning() => _runningStates.Values.Any(v => v);

        /// <summary>
        /// 自定义宏（插件模块）当前是否在执行 —— 由插件通过反射上报，见 <see cref="ReportMacroRunning"/>。
        /// </summary>
        private static bool _macroRunning;

        /// <summary>
        /// 插件（自定义宏）上报执行状态：**没运行 = 自定义宏1.png，执行期间 = 自定义宏2.png**。
        /// <para>
        /// ★ 宿主不引用插件，所以这个方法由插件用反射调
        /// （<c>LvchaxsBS_Macro.Utils.HostBridge.ReportMacroRunning</c> → 这里）。
        /// 插件缺失或版本旧时不调，图标就一直是 1，不影响其它功能。
        /// </para>
        /// </summary>
        public static void ReportMacroRunning(bool running)
        {
            if (_macroRunning == running) return;
            _macroRunning = running;

            int index = _moduleOrder.IndexOf(MacroModuleService.ModuleName);
            if (index < 0) return;      // 没装插件 ⇒ 没有这个图标位

            UpdateIconAtIndex(index, running ? "自定义宏2.png" : "自定义宏1.png");

            // ★ 悬浮窗的显隐也跟着它走：
            //   开跑 ⇒ 哪怕不在主界面/地图也要把图标排显示出来；
            //   跑完 ⇒ 回到"只看在主界面/地图才显示"的常态（不满足就收起来）。
            UpdateOverlayVisibility();
        }

        /// <summary>
        /// 反射问插件「宏引擎**实际**在不在跑」（<c>MacroRunner.IsRunning</c>）。
        /// <para>
        /// ★★ 为什么不只信插件上报：插件收尾路径一旦漏报一次（异常 / 提前 return / 版本对不上），
        /// 图标就会永远卡在 2 号 —— 实测就撞上了。这里以**真实状态**为准，上报只算快速通道。
        /// </para>
        /// </summary>
        private static bool TryQueryMacroEngineRunning(out bool running)
        {
            running = false;

            try
            {
                Type? runner = Type.GetType("LvchaxsBS_Macro.Core.MacroRunner, LvchaxsBS_macro");
                if (runner?.GetProperty("IsRunning",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    is System.Reflection.PropertyInfo prop &&
                    prop.GetValue(null) is bool value)
                {
                    running = value;
                    return true;
                }
            }
            catch
            {
                // 插件版本旧 / 反射失败 ⇒ 当作查不到，退回上报值
            }

            return false;
        }

        /// <summary>图标与显隐的判断统一走它：查得到用真实值，查不到退回上报值。</summary>
        private static bool MacroEngineRunning()
            => TryQueryMacroEngineRunning(out bool actual) ? actual : _macroRunning;

        /// <summary>宏状态看守：插件漏报 / 异常时，这里把图标和显隐纠回来。</summary>
        private static System.Windows.Threading.DispatcherTimer? _macroWatcher;

        private static void StartMacroStateWatcher()
        {
            if (_macroWatcher != null) return;
            if (!MacroModuleService.IsAvailable) return;   // 没装插件，不用看（dll 都不在就没什么可同步的）

            _macroWatcher = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400),
            };

            _macroWatcher.Tick += (_, _) => SyncMacroRunningState();
            _macroWatcher.Start();

            AppendMacroLog("看守已启动（每 400ms 对齐一次自定义宏图标）");
        }

        private static void SyncMacroRunningState()
        {
            int index = _moduleOrder.IndexOf(MacroModuleService.ModuleName);
            if (index < 0) return;

            // 查不到（插件版本旧 / 反射失败）就退回上报值，绝不因此僵住
            bool actual = TryQueryMacroEngineRunning(out bool queried) ? queried : _macroRunning;

            // ★★ 每次 Tick 都**强制把图标对齐到真实状态**，而不是"状态变了才动"。
            //    UpdateIconAtIndex 内部有 _iconNameCache，目标一致就直接返回（不会重复载图），
            //    所以这里无条件调用是安全且便宜的 —— 关键是：不管是谁把图标刷成了 2，
            //    最多 400ms 内都会被纠回 1。
            UpdateIconAtIndex(index, actual ? "自定义宏2.png" : "自定义宏1.png");

            if (actual != _macroRunning)
            {
                AppendMacroLog($"状态翻转：实际={actual} 查得到={queried} 原缓存={!actual}");
                _macroRunning = actual;
            }

            // ★★ 显隐**每次都重新决定**，不能只在"状态翻转"时做。
            //    因为 _macroRunning 很可能从头到尾都是 false（插件上报失败，图标是靠实时查询刷出来的）——
            //    那种情况下"翻转"永远不会发生，窗口就永远收不回去。
            //    UpdateOverlayVisibility / ShowOverlay / HideOverlay 都是幂等的，每 400ms 调一次没有副作用。
            UpdateOverlayVisibility();
        }

        /// <summary>
        /// 宏图标状态日志（只在状态翻转时写一行，防刷屏；超过 200KB 就不再写）。
        /// <para>文件：<c>&lt;exe 目录&gt;\Config\Macro\icon_debug.log</c>。</para>
        /// </summary>
        private static void AppendMacroLog(string line)
        {
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "Config", "Macro");
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, "icon_debug.log");
                if (File.Exists(path) && new FileInfo(path).Length > 200_000) return;

                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {line}\r\n");
            }
            catch
            {
                // 日志失败无所谓
            }
        }

        #endregion

        #region 功能调度

        private static void StartModule(string moduleName)
        {
            StopModulesForStart(moduleName);

            switch (moduleName)
            {
                case "快速拾取":
                    QuickPickupLogic.Start();
                    break;
                case "剧情对话":
                    StoryDialogueLogic.Start();
                    break;
                case "钓鱼辅助":
                    FishingAssistLogic.Start();
                    break;
                case "自动伐木":
                    AutoLumberLogic.Start();
                    break;
                case "自动烹饪":
                    AutoCookLogic.Start();
                    break;
                default:
                    return;
            }

            SetRunning(moduleName, true);
            UpdateIconForModule(moduleName);
            UpdateOverlayVisibility();

            if (moduleName == "钓鱼辅助")
            {
                _fishingRodStatus = "";
                _fishingTensionDir = "";
                _fishingHookedLocked = false;
                UpdateFishingSubtitle();
            }
        }

        private static void StopModule(string moduleName)
        {
            switch (moduleName)
            {
                case "快速拾取":
                    QuickPickupLogic.Stop();
                    break;
                case "剧情对话":
                    StoryDialogueLogic.Stop();
                    break;
                case "钓鱼辅助":
                    FishingAssistLogic.Stop();
                    break;
                case "自动伐木":
                    AutoLumberLogic.Stop();
                    break;
                case "自动烹饪":
                    AutoCookLogic.Stop();
                    break;
                default:
                    return;
            }

            SetRunning(moduleName, false);
            UpdateIconForModule(moduleName);
            UpdateOverlayVisibility();

            if (moduleName == "钓鱼辅助")
            {
                _fishingRodStatus = "";
                _fishingTensionDir = "";
                _fishingHookedLocked = false;
                SubtitleOverlayService.HideSubtitle(SubtitleOverlayService.SubtitleOwner.Fishing);
            }
        }

        /// <summary>
        /// 结束所有正在运行的互斥功能：剧情对话、自动伐木、钓鱼辅助、自动烹饪。
        /// 供自动清药按钮启动前调用。
        /// </summary>
        public static void StopAllRunningForAutoClearMedicine()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (IsRunning("剧情对话")) StopModule("剧情对话");
                if (IsRunning("自动伐木")) StopModule("自动伐木");
                if (IsRunning("钓鱼辅助")) StopModule("钓鱼辅助");
                if (IsRunning("自动烹饪")) StopModule("自动烹饪");
            });
        }

        /// <summary>
        /// 停止所有正在运行的功能（含自动清药）。功能总开关关闭时调用。
        /// </summary>
        public static void StopAllModules()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 自动清药是独立循环，且在启动时暂停了检测循环，停止后要把检测恢复回来
                if (AutoClearMedicineLogic.IsRunning)
                {
                    AutoClearMedicineLogic.Stop();
                    ResumeDetectionFromAutoClearMedicine();
                }

                foreach (var module in _runningStates.Keys.ToList())
                {
                    if (IsRunning(module)) StopModule(module);
                }

                // ★ 自定义宏是**插件自己**开的循环（不在 _runningStates 里），
                //   总开关关闭必须单独通知它停 —— 否则"关了总开关宏还在跑"。
                Services.MacroModuleService.StopRunning();
            });
        }

        /// <summary>功能总开关是否开启（读配置；读取异常时按“开启”处理，避免误禁用功能）。</summary>
        private static bool IsMasterSwitchOn()
        {
            try
            {
                return ConfigManager.Get<HomePageSettings>().MasterSwitch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【图标服务】读取功能总开关失败，按开启处理: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// 应用功能总开关状态。
        /// <para>
        /// 关闭：停止所有功能 + 暂停主界面/地图检测 + 隐藏图标窗口（即使客户区窗口处于焦点）；
        /// 开启：窗口处于焦点时恢复检测并按需重新显示图标窗口。
        /// </para>
        /// </summary>
        public static void ApplyMasterSwitchState(bool enabled)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 手柄拾取是常驻监听、不走触发键通道，需单独跟随总开关同步
                ControllerPickupLogic.SyncWithSettings();

                if (!enabled)
                {
                    StopAllModules();
                    DetectionManager.StopLoop();
                    HideOverlay();
                }
                else if (_isFocused)
                {
                    if (!_detectionPausedByMedicine)
                    {
                        DetectionManager.StartLoop();
                    }

                    UpdateOverlayVisibility();
                }
            });
        }

        /// <summary>
        /// 自动清药结束：恢复被它暂停的检测循环。
        /// 挂在引擎里而不是页面上——旧实现只在 AutoCook 配置页订阅，页面没打开过就不会恢复。
        /// </summary>
        private static void OnAutoClearMedicineStopped()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ResumeDetectionFromAutoClearMedicine();
            });
        }



        /// <summary>
        /// 自动清药开始时调用：暂停主界面/地图检测。
        /// </summary>
        public static void PauseDetectionForAutoClearMedicine()
        {
            _detectionPausedByMedicine = true;
            DetectionManager.StopLoop();
        }

        /// <summary>
        /// 自动清药结束时调用：恢复主界面/地图检测（仅焦点时）。
        /// </summary>
        public static void ResumeDetectionFromAutoClearMedicine()
        {
            _detectionPausedByMedicine = false;
            if (_isFocused)
            {
                DetectionManager.StartLoop();
            }
        }

        private static void StopModulesForStart(string moduleName)
        {
            // ★ 自动清药也占第一行字幕（AutoClearMedicineLogic:386 走 ShowSubtitle(AutoCook)），
            //   且它是独立循环、不进 _runningStates ⇒ 下面两个分支的遍历都碰不到它，这里单独停。
            StopAutoClearMedicineIfRunning();

            if (_exclusiveGroup.Contains(moduleName))
            {
                foreach (var module in _runningStates.Keys.ToList())
                {
                    if (module == moduleName) continue;
                    if (IsRunning(module))
                    {
                        StopModule(module);
                    }
                }
            }
            else if (_coexistGroup.Contains(moduleName))
            {
                foreach (var module in _exclusiveGroup)
                {
                    if (IsRunning(module))
                    {
                        StopModule(module);
                    }
                }
            }
        }

        /// <summary>
        /// 自动清药在跑就停掉：它是独立循环（不在 <c>_runningStates</c> 里），
        /// 停止后必须把被它暂停的检测循环恢复回来（同 <see cref="StopAllModules"/>）。
        /// </summary>
        private static void StopAutoClearMedicineIfRunning()
        {
            if (!AutoClearMedicineLogic.IsRunning) return;

            AutoClearMedicineLogic.Stop();
            ResumeDetectionFromAutoClearMedicine();
        }

        private static bool CanStartModule(string moduleName, out string reason)
        {
            reason = "";

            // ★ UID 登录流程运行中：两边都要操作鼠标键盘、且共用第一行字幕 ⇒ 登录期间不启动别的功能
            if (SubtitleHostBridge.IsUidLoginRunning)
            {
                reason = "UID 登录流程运行中，请等登录结束后再启动功能";
                return false;
            }

            switch (moduleName)
            {
                case "快速拾取":
                    if (!_isFocused || !_isInMainWindow) { reason = "不在主界面或窗口未聚焦，无法启动快速拾取"; return false; }
                    return true;

                case "钓鱼辅助":
                    if (!_isFocused || !_isInMainWindow) { reason = "不在主界面或窗口未聚焦，无法启动钓鱼辅助"; return false; }
                    if (!FishingAssistLogic.CanStart()) { reason = "未检测到鱼竿状态，无法启动钓鱼辅助"; return false; }
                    return true;

                case "剧情对话":
                    if (!_isFocused) { reason = "窗口未聚焦，无法启动剧情对话"; return false; }
                    if (DetectionManager.IsInMainWindow || DetectionManager.IsInMap) { reason = "在主界面或地图界面，无法启动剧情对话"; return false; }
                    if (!StoryDialogueLogic.CanStart()) { reason = "未检测到隐藏按钮，无法启动剧情对话"; return false; }
                    return true;

                case "自动伐木":
                    if (!_isFocused || !_isInMainWindow) { reason = "不在主界面或窗口未聚焦，无法启动自动伐木"; return false; }
                    if (!AutoLumberLogic.CanStart()) { reason = "未检测到王树瑞佑，无法启动自动伐木"; return false; }
                    return true;

                case "自动烹饪":
                    if (!_isFocused) { reason = "窗口未聚焦，无法启动自动烹饪"; return false; }
                    if (DetectionManager.IsInMainWindow || DetectionManager.IsInMap) { reason = "在主界面或地图界面，无法启动自动烹饪"; return false; }
                    if (!AutoCookLogic.CanStart()) { reason = "未检测到烹饪界面，无法启动自动烹饪"; return false; }
                    return true;

                default:
                    return false;
            }
        }

        private static void UpdateIconForModule(string moduleName)
        {
            switch (moduleName)
            {
                case "快速拾取":
                    UpdateQuickPickupIconByDetection();
                    break;
                case "钓鱼辅助":
                    UpdateFishingAssistIcon();
                    break;
                case "剧情对话":
                    UpdateStoryDialogueIcon();
                    break;
                case "自动伐木":
                    UpdateAutoLumberIcon();
                    break;
                case "自动烹饪":
                    UpdateAutoCookIcon();
                    break;
                case "快速传送":
                    {
                        int index = _moduleOrder.IndexOf("快速传送");
                        if (index >= 0)
                        {
                            string iconName = _isInMap ? "锚点2.png" : "锚点1.png";
                            UpdateIconAtIndex(index, iconName);
                        }
                    }
                    break;
            }
        }

        #endregion

        #region 暂停/恢复统一同步

        private static void SyncQuickPickupState()
        {
            if (!IsRunning("快速拾取")) return;

            bool shouldRun = _isFocused && _isInMainWindow;

            if (shouldRun)
            {
                if (QuickPickupLogic.IsPaused)
                    QuickPickupLogic.Resume();
            }
            else
            {
                if (!QuickPickupLogic.IsPaused)
                    QuickPickupLogic.Pause();
            }

            UpdateQuickPickupIconByDetection();
        }

        private static void SyncStoryDialogueState()
        {
            if (!IsRunning("剧情对话")) return;

            bool shouldRun = _isFocused && !_isInMainWindow && !_isInMap;

            if (shouldRun)
            {
                if (StoryDialogueLogic.IsPaused)
                    StoryDialogueLogic.Resume();
            }
            else
            {
                if (!StoryDialogueLogic.IsPaused)
                    StoryDialogueLogic.Pause();
            }

            UpdateStoryDialogueIcon();
        }

        private static void SyncFishingAssistState()
        {
            if (!IsRunning("钓鱼辅助")) return;

            // 钓鱼时游戏会隐藏常规 HUD，"是否在主界面"会失效：
            // 此时只要还处于钓鱼流程（张力阶段 / 刚检测到鱼竿状态），就必须继续运行，
            // 否则会被判成"离开主界面"而暂停，张力区检测跟着停掉，鱼就钓不上来了。
            bool shouldRun = _isFocused && (_isInMainWindow || FishingAssistLogic.IsInFishingFlow);

            if (shouldRun)
            {
                if (FishingAssistLogic.IsPaused)
                    FishingAssistLogic.Resume();
            }
            else
            {
                if (!FishingAssistLogic.IsPaused)
                    FishingAssistLogic.Pause();
            }

            UpdateFishingAssistIcon();
            UpdateFishingSubtitle();
        }

        #endregion

        #region 开图键

        private static bool IsOpenMapKey(string keyName)
        {
            // 开图键不允许是右键（仅在该开关打开时）：地图界面里右键专用于"取消本次快速传送"
            if (keyName == "右键" && QuickTeleportLogic.IsRightClickCancelEnabled) return false;

            var openMapKey = ConfigManager.Get<QuickTeleportSettings>().OpenMapKey_1;
            if (string.IsNullOrEmpty(openMapKey)) return false;

            foreach (var k in openMapKey.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(keyName, k.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void OpenMapKeyDown()
        {
            _openMapKeyPressed = true;
            _openMapKeyValid = _isInMainWindow;
        }

        private static void OpenMapKeyUp()
        {
            if (!_openMapKeyPressed) return;
            _openMapKeyPressed = false;

            if (!_openMapKeyValid) return;

            _openMapWindowUntil = DateTime.Now.AddMilliseconds(OPEN_MAP_WINDOW_MS);
        }

        private static bool InOpenMapWindow()
        {
            return DateTime.Now < _openMapWindowUntil;
        }

        /// <summary>
        /// 本次是否允许触发快速传送。
        /// 地图界面会自动满足触发条件，但用户可以用右键取消"这一次"：
        /// 取消后即使人还在地图界面也不再触发，直到回到主界面自动清除，或再次按右键解除。
        /// </summary>
        private static bool CanTriggerQuickTeleport()
        {
            if (QuickTeleportLogic.IsCancelledEffective) return false;

            return InOpenMapWindow() || (!_isInMainWindow && _isInMap);
        }

        #endregion

        #region 自动烹饪品质选择窗口

        private static void OnQualitySelectWindowOpened()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                UpdateAutoCookIcon();
                UpdateOverlayVisibility();
            });
        }

        #endregion

        #region 剧情检测状态变化

        private static void OnStoryDialogueDetectionStateChanged(bool isDetected)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                UpdateStoryDialogueIcon();
            });
        }

        #endregion

        #region 钓鱼状态变化

        private static void OnFishingRodStatusUpdated(double score, long elapsedMs, double threshold, string status)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _fishingRodStatus = status;

                if (status == "上钩了")
                {
                    _fishingHookedLocked = true;
                }

                // 鱼竿状态是钓鱼信号的来源，图标要跟着实时变化（有信号→钓鱼2，长时间无信号→钓鱼3）
                UpdateFishingAssistIcon();
                UpdateFishingSubtitle();
            });
        }

        private static void OnFishingTensionUpdated(double score, long elapsedMs, double threshold, string direction)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _fishingTensionDir = direction;

                if (direction == "向右滑动" || direction == "向左滑动")
                {
                    _fishingHookedLocked = false;
                }

                // 张力区检测到结果 = 也是钓鱼信号，图标随刷新
                UpdateFishingAssistIcon();
                UpdateFishingSubtitle();
            });
        }

        private static void UpdateFishingSubtitle()
        {
            bool isFishing2 = IsRunning("钓鱼辅助") && !FishingAssistLogic.IsPaused;

            if (!isFishing2)
            {
                // ★ 只收钓鱼自己那一行：第一行是共用行，UID 登录正显示时不能被这里收掉
                SubtitleOverlayService.HideSubtitle(SubtitleOverlayService.SubtitleOwner.Fishing);
                return;
            }

            string text;
            if (_fishingHookedLocked)
            {
                text = SubtitleTexts.Fishing.Hooked;
            }
            else if (_fishingTensionDir == "向右滑动")
            {
                text = SubtitleTexts.Fishing.PullRight;
            }
            else if (_fishingTensionDir == "向左滑动")
            {
                text = SubtitleTexts.Fishing.PullLeft;
            }
            else if (_fishingRodStatus == "上钩了")
            {
                text = SubtitleTexts.Fishing.Hooked;
            }
            else if (_fishingRodStatus == "已抛钩")
            {
                text = SubtitleTexts.Fishing.WaitingBite;
            }
            else if (_fishingRodStatus == "未抛钩")
            {
                text = SubtitleTexts.Fishing.ReadyToCast;
            }
            else
            {
                text = "";
            }

            if (string.IsNullOrEmpty(text))
            {
                SubtitleOverlayService.HideSubtitle(SubtitleOverlayService.SubtitleOwner.Fishing);
                return;
            }

            ShowSubtitle(SubtitleOverlayService.SubtitleOwner.Fishing);
            SubtitleOverlayService.SetSubtitle(text);
        }

        #endregion

        #region 功能内部停止事件

        private static void OnAutoCookStopped()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!IsRunning("自动烹饪")) return;

                SetRunning("自动烹饪", false);
                UpdateAutoCookIcon();
                UpdateOverlayVisibility();
            });
        }

        private static void OnAutoLumberStopped()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!IsRunning("自动伐木")) return;

                SetRunning("自动伐木", false);
                UpdateAutoLumberIcon();
                UpdateOverlayVisibility();
            });
        }

        #endregion

        #region 窗口焦点 / 边界事件

        private static void OnFocusChanged(object? sender, bool focused)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _isFocused = focused;

                if (focused)
                {
                    // 清药期间 / 功能总开关关闭时，都不启动 DetectionManager
                    if (!_detectionPausedByMedicine && IsMasterSwitchOn())
                    {
                        DetectionManager.StartLoop();
                    }

                    var b = WindowFocusService.LastBounds;
                    if (!b.IsEmpty)
                    {
                        _currentWidth = b.Width;
                        _currentHeight = b.Height;
                        _currentLeft = b.Left;
                        _currentTop = b.Top;
                    }

                    SyncQuickPickupState();
                    SyncStoryDialogueState();
                    SyncFishingAssistState();

                    UpdateOverlayVisibility();
                    BringOverlayToTop();
                }
                else
                {
                    DetectionManager.StopLoop();

                    SyncQuickPickupState();
                    SyncStoryDialogueState();
                    SyncFishingAssistState();

                    if (IsRunning("自动伐木"))
                        StopModule("自动伐木");

                    if (IsRunning("自动烹饪"))
                        StopModule("自动烹饪");

                    HideOverlay();
                }
            });
        }

        private static void OnBoundsChanged(object? sender, WindowFocusService.WindowBounds b)
        {
            if (b.IsEmpty) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                bool resolutionChanged = (_currentWidth != b.Width || _currentHeight != b.Height);
                bool positionChanged = (_currentLeft != b.Left || _currentTop != b.Top);

                _currentWidth = b.Width;
                _currentHeight = b.Height;
                _currentLeft = b.Left;
                _currentTop = b.Top;

                if (!_isFocused) return;

                SyncQuickPickupState();
                SyncStoryDialogueState();
                SyncFishingAssistState();

                if (resolutionChanged)
                {
                    if (_overlayWindow != null && _isOverlayVisible)
                        UpdateOverlayContent();
                    else
                        UpdateOverlayVisibility();
                }
                else if (positionChanged && _isOverlayVisible && _overlayWindow != null)
                {
                    UpdateWindowPosition();
                }
                else
                {
                    UpdateOverlayVisibility();
                }

                BringOverlayToTop();
            });
        }

        #endregion

        #region 检测结果事件

        private static void OnDetectionResultChanged(bool isInMainWindow)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _isInMainWindow = isInMainWindow;

                // 回到主界面 → 清除"取消本次快速传送"标志，下次开图重新可用
                if (isInMainWindow && QuickTeleportLogic.IsTeleportCancelled)
                {
                    QuickTeleportLogic.ClearTeleportCancelled();
                }

                SyncQuickPickupState();
                SyncStoryDialogueState();
                SyncFishingAssistState();

                UpdateQuickTeleportIcon();
                UpdateOverlayVisibility();
            });
        }

        private static void OnMapDetectionResultChanged(bool isInMap)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _isInMap = isInMap;

                SyncStoryDialogueState();

                UpdateQuickTeleportIcon();

                UpdateOverlayVisibility();
            });
        }

        #endregion

        #region 图标更新方法

        /// <summary>
        /// 快速传送图标（三态）：
        /// - 不在地图界面              → 锚点1（未触发条件）
        /// - 地图界面、未取消          → 锚点2（条件符合，按触发键即可传送）
        /// - 地图界面、用户已右键取消  → 锚点3（条件符合但本次被禁用，直到回主界面或再按右键解除）
        /// 用户主要靠锚点2 / 锚点3 区分"右键取消是否生效"。
        /// </summary>
        private static string GetQuickTeleportIconName()
        {
            if (!_isInMap) return "锚点1.png";

            return QuickTeleportLogic.IsCancelledEffective ? "锚点3.png" : "锚点2.png";
        }

        private static void UpdateQuickTeleportIcon()
        {
            int index = _moduleOrder.IndexOf("快速传送");
            if (index < 0) return;

            UpdateIconAtIndex(index, GetQuickTeleportIconName());
        }

        /// <summary>
        /// 地图界面按下右键：切换取消标志并同步图标（锚点2 ⇄ 锚点3）。
        /// 必须在 UI 线程执行（改的是界面图标）。
        /// </summary>
        private static void UpdateQuickTeleportIconAfterCancelToggle()
        {
            QuickTeleportLogic.ToggleTeleportCancelled();
            UpdateQuickTeleportIcon();
        }

        private static void UpdateQuickPickupIconByDetection()
        {
            int index = _moduleOrder.IndexOf("快速拾取");
            if (index < 0) return;

            if (!IsRunning("快速拾取"))
            {
                UpdateIconAtIndex(index, "拾取1.png");
                return;
            }

            UpdateIconAtIndex(index, QuickPickupLogic.IsPaused ? "拾取3.png" : "拾取2.png");
        }

        private static void UpdateStoryDialogueIcon()
        {
            int index = _moduleOrder.IndexOf("剧情对话");
            if (index < 0) return;

            if (!IsRunning("剧情对话"))
            {
                UpdateIconAtIndex(index, "剧情1.png");
                return;
            }

            bool show2 = !StoryDialogueLogic.IsPaused && StoryDialogueLogic.IsDetected;
            UpdateIconAtIndex(index, show2 ? "剧情2.png" : "剧情3.png");
        }

        private static void UpdateFishingAssistIcon()
        {
            int index = _moduleOrder.IndexOf("钓鱼辅助");
            if (index < 0) return;

            UpdateIconAtIndex(index, GetFishingAssistIconName());
        }

        /// <summary>
        /// 钓鱼辅助当前应显示的图标（唯一判定入口，供图标刷新与悬浮窗构建共用）。
        /// - 钓鱼1：功能未运行（"在主界面"只是触发键启动时的准入条件，见 CanStartModule）
        /// - 钓鱼3：非主界面一律 3（在菜单/秘境/读条等场景）；在主界面但当前检测不到东西也是 3
        /// - 钓鱼2：在主界面 且 检测到东西 —— 鱼竿状态（未抛钩/已抛钩/上钩了 任意一个）
        ///          或张力区（检测到结果）
        /// 说明：启动之后"在主界面"就不参与判定了，只作为"非主界面一律 3"的护栏。
        ///       上钩后鱼竿状态检测会被设计性停掉（进入张力阶段），此时靠张力区结果保持 钓鱼2。
        /// </summary>
        private static string GetFishingAssistIconName()
        {
            if (!IsRunning("钓鱼辅助")) return "钓鱼1.png";

            if (!_isInMainWindow) return "钓鱼3.png";

            // 鱼竿状态：_fishingRodStatus 只可能是 未抛钩/已抛钩/上钩了 或 ""（检测不到）
            bool rodDetected = !string.IsNullOrEmpty(_fishingRodStatus);

            // 张力区：检测到时会给出滑动方向，检测不到是"未发现"
            bool tensionDetected = _fishingTensionDir == "向右滑动" || _fishingTensionDir == "向左滑动";

            return (rodDetected || tensionDetected) ? "钓鱼2.png" : "钓鱼3.png";
        }

        private static void UpdateAutoLumberIcon()
        {
            int index = _moduleOrder.IndexOf("自动伐木");
            if (index < 0) return;

            UpdateIconAtIndex(index, IsRunning("自动伐木") ? "伐木2.png" : "伐木1.png");
        }

        private static void UpdateAutoCookIcon()
        {
            int index = _moduleOrder.IndexOf("自动烹饪");
            if (index < 0) return;

            UpdateIconAtIndex(index, IsRunning("自动烹饪") ? "烹饪2.png" : "烹饪1.png");
        }

        #endregion

        #region 键盘事件

        private static void OnKeyboardEvent(object? sender, KeyboardEventArgs args)
        {
            // 焦点事件可能丢失或时序错位，造成"人在游戏里、程序却判定焦点外"。
            // 用户按键本身就是最可靠的"我正在用游戏"信号：判定为焦点外时先按需复核一次。
            if (!_isFocused && !WindowFocusService.Reevaluate())
                return;

            if (SimulationService.IsSimulating)
                return;

            string keyName = args.KeyName;

            if (args.EventType == KeyboardEventType.KeyDown && keyName == "Tab")
            {
                // ★ 自动清药运行中、以及 **UID 登录流程运行中**都不隐藏：
                //   那两会儿字幕是进度（自动清药显示储存数量、登录显示走到哪一步），收起来就看不见了。
                //   登录要等它成功（第二行出现「登录完成-TAB隐藏」）之后才允许按 TAB 收。
                if (!AutoClearMedicineLogic.IsRunning && !SubtitleHostBridge.IsTabHideLocked)
                {
                    SubtitleOverlayService.HideSubtitle();
                    SubtitleOverlayService.HideSubtitle2();
                    // 第三行（联机人数）一起收 —— 它和第二行一样带「-TAB隐藏」的提示
                    SubtitleOverlayService.HideSubtitle3();
                }
                return;
            }

            if (args.EventType == KeyboardEventType.KeyDown && AutoCookLogic.IsSelectingQuality)
            {
                AutoCookLogic.QualityMode? selected = keyName switch
                {
                    "F1" => AutoCookLogic.QualityMode.Strange,
                    "F2" => AutoCookLogic.QualityMode.Normal,
                    "F3" => AutoCookLogic.QualityMode.Perfect,
                    "F4" => AutoCookLogic.QualityMode.AutoX99,
                    _ => (AutoCookLogic.QualityMode?)null
                };

                if (selected.HasValue)
                {
                    bool ok = AutoCookLogic.SelectQuality(selected.Value);
                    if (ok)
                    {
                        UpdateAutoCookIcon();
                    }
                    return;
                }
            }

            if (IsOpenMapKey(keyName))
            {
                if (args.EventType == KeyboardEventType.KeyDown)
                    OpenMapKeyDown();
                else if (args.EventType == KeyboardEventType.KeyUp)
                    OpenMapKeyUp();
                return;
            }

            if (args.EventType == KeyboardEventType.KeyDown)
            {
                if (_keyHandledStates.TryGetValue(keyName, out bool handled) && handled)
                    return;

                _keyHandledStates[keyName] = true;

                ProcessKeyPress(keyName);
            }
            else if (args.EventType == KeyboardEventType.KeyUp)
            {
                _keyHandledStates[keyName] = false;
            }
        }

        // ===== 触发键匹配（支持 Ctrl+V 这种组合键）=====

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;

        // 左右分开的具体虚拟键：只有它们才能区分"左 Ctrl / 右 Ctrl"
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsVirtualKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>
        /// 修饰键此刻是否按着。两路取"或"：
        /// <list type="number">
        /// <item>全局键盘钩子维护的**物理按下**集合（主）——只要钩子收得到键就一定准；</item>
        /// <item><see cref="GetAsyncKeyState"/>（备）——钩子没装 / 键在钩子安装前就按下时兜底。</item>
        /// </list>
        /// <para>
        /// ★ 必须用**具体**的左右虚拟键（0xA2 左Ctrl / 0xA3 右Ctrl）去问系统：
        ///   通用值 0x11 在左右任一按下时都为真，用它就分不出左右。
        ///   <paramref name="genericVk"/> 只用来兜底物理集合 —— 个别环境的低层钩子只报通用值，
        ///   那种情况下集合里存的是 0x11，不认它组合键会整个失效。
        /// </para>
        /// </summary>
        private static bool IsModifierDown(int specificVk, int genericVk)
        {
            if (GlobalKeyboardHookService.IsPhysicallyDown(specificVk)) return true;
            if (IsVirtualKeyDown(specificVk)) return true;

            return genericVk != 0
                && specificVk != genericVk
                && GlobalKeyboardHookService.IsPhysicallyDown(genericVk);
        }

        private static bool IsCtrlHeld(bool left, bool right)
        {
            bool ok = false;
            if (left) ok |= IsModifierDown(VK_LCONTROL, VK_CONTROL);
            if (right) ok |= IsModifierDown(VK_RCONTROL, 0);
            return ok;
        }

        private static bool IsAltHeld(bool left, bool right)
        {
            bool ok = false;
            if (left) ok |= IsModifierDown(VK_LMENU, VK_MENU);
            if (right) ok |= IsModifierDown(VK_RMENU, 0);
            return ok;
        }

        private static bool IsShiftHeld(bool left, bool right)
        {
            bool ok = false;
            if (left) ok |= IsModifierDown(VK_LSHIFT, VK_SHIFT);
            if (right) ok |= IsModifierDown(VK_RSHIFT, 0);
            return ok;
        }

        /// <summary>
        /// 触发键匹配：支持 "左Ctrl+V" / "左Ctrl+左Alt+V" 这种组合键。
        /// <para>
        /// 规则：最后一个 '+' 后面是基础键，前面全是修饰键；基础键与当前按下的键一致、
        /// 且所有修饰键此刻都按着，才算命中。
        /// </para>
        /// <para>
        /// ★ 普通键（不含 '+'）多一条：**此刻不能按着 Ctrl / Alt** ——
        ///   否则"V"会被 Ctrl+V 顺带触发，组合键和单键就分不开了。
        /// </para>
        /// </summary>
        internal static bool MatchesTriggerKey(string? configured, string pressedKeyName)
        {
            if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(pressedKeyName)) return false;

            int plus = configured.LastIndexOf('+');
            if (plus < 0)
            {
                if (!string.Equals(configured, pressedKeyName, StringComparison.OrdinalIgnoreCase)) return false;

                // 按着 Ctrl / Alt 时，不带修饰键的配置不该被顺带触发
                return !IsCtrlHeld(left: true, right: true)
                    && !IsAltHeld(left: true, right: true);
            }

            string baseKey = configured[(plus + 1)..].Trim();
            if (!string.Equals(baseKey, pressedKeyName, StringComparison.OrdinalIgnoreCase)) return false;

            foreach (var raw in configured[..plus].Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                var mod = raw.Trim();
                if (mod.Length == 0) continue;

                // 注意：界面上只能选「左Ctrl / 左Alt」，但历史配置里可能存着右 Ctrl/右 Alt，
                // 这里两种都照旧解析，只是各自只认自己那一侧。
                if (mod.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsCtrlHeld(left: true, right: true)) return false;
                }
                else if (mod.Equals("左Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsCtrlHeld(left: true, right: false)) return false;
                }
                else if (mod.Equals("右Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsCtrlHeld(left: false, right: true)) return false;
                }
                else if (mod.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAltHeld(left: true, right: true)) return false;
                }
                else if (mod.Equals("左Alt", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAltHeld(left: true, right: false)) return false;
                }
                else if (mod.Equals("右Alt", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAltHeld(left: false, right: true)) return false;
                }
                else if (mod.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsShiftHeld(left: true, right: true)) return false;
                }
                else if (mod.Equals("左Shift", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsShiftHeld(left: true, right: false)) return false;
                }
                else if (mod.Equals("右Shift", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsShiftHeld(left: false, right: true)) return false;
                }
                else
                {
                    return false;   // 不认识的修饰键：宁可不触发，也不误触发
                }
            }

            return true;
        }

        private static void ProcessKeyPress(string keyName)
        {
            var settings = ConfigManager.Get<HomePageSettings>();

            // 功能总开关关闭 → 所有功能都不可执行（即使单个功能开关是开的）
            if (!settings.MasterSwitch) return;

            string? matchedModule = null;

            if (IsRunning("快速拾取") && !QuickPickupLogic.IsPaused)
            {
                var pickupSettings = ConfigManager.Get<QuickPickupSettings>();
                if (!string.IsNullOrEmpty(pickupSettings.PauseKeys))
                {
                    var pauseKeyList = pickupSettings.PauseKeys.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var pauseKey in pauseKeyList)
                    {
                        if (string.Equals(keyName, pauseKey.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            QuickPickupLogic.Pause();
                            return;
                        }
                    }
                }
            }

            if (MatchesTriggerKey(settings.QuickPickupKey, keyName) && settings.QuickPickup) matchedModule = "快速拾取";
            else if (MatchesTriggerKey(settings.StoryDialogueKey, keyName) && settings.StoryDialogue) matchedModule = "剧情对话";
            else if (MatchesTriggerKey(settings.FishingAssistKey, keyName) && settings.FishingAssist) matchedModule = "钓鱼辅助";
            // 快速传送不能用右键当触发键（仅在该开关打开时）：地图界面右键是"取消本次传送"的开关
            else if (MatchesTriggerKey(settings.QuickTeleportKey, keyName) && settings.QuickTeleport
                     && !(keyName == "右键" && QuickTeleportLogic.IsRightClickCancelEnabled)) matchedModule = "快速传送";
            else if (MatchesTriggerKey(settings.AutoLumberKey, keyName) && settings.AutoLumber) matchedModule = "自动伐木";
            else if (MatchesTriggerKey(settings.AutoCookKey, keyName) && settings.AutoCook) matchedModule = "自动烹饪";
            else if (MatchesTriggerKey(settings.ControllerPickupKey, keyName) && settings.ControllerPickup) matchedModule = "手柄拾取";

            if (matchedModule != null)
                ExecuteMatchedModule(matchedModule);
        }

        #endregion

        #region 鼠标事件

        /// <summary>
        /// 「组合键 + 鼠标键」按下时登记的待执行模块。
        /// <para>
        /// ★ 为什么按下不直接执行：组合键（如 左Ctrl+左键）成立时用户的手还按在鼠标上，
        ///   功能立刻开始模拟点击会和真实按键抢输入（拖拽被打断、点击状态错乱）。
        ///   所以按下只登记，等这个鼠标键**抬起**后再跑原来的执行逻辑。
        /// </para>
        /// </summary>
        private static string? _pendingMouseComboModule;

        /// <summary>待执行那一下对应的"抬起"事件（左键按下 → LeftButtonUp）。</summary>
        private static MouseEventType _pendingMouseComboUp;

        /// <summary>鼠标按下事件 → 对应的抬起事件（滚轮没有抬起，返回 false）。</summary>
        private static bool TryGetMouseUpEvent(MouseEventType down, out MouseEventType up)
        {
            switch (down)
            {
                case MouseEventType.LeftButtonDown: up = MouseEventType.LeftButtonUp; return true;
                case MouseEventType.RightButtonDown: up = MouseEventType.RightButtonUp; return true;
                case MouseEventType.MiddleButtonDown: up = MouseEventType.MiddleButtonUp; return true;
                case MouseEventType.XButton1Down: up = MouseEventType.XButton1Up; return true;
                case MouseEventType.XButton2Down: up = MouseEventType.XButton2Up; return true;
                default: up = default; return false;
            }
        }

        /// <summary>触发命中后的统一执行入口（键盘 / 鼠标共用）。</summary>
        private static void ExecuteMatchedModule(string matchedModule)
        {
            // 快速传送：地图界面会自动满足触发条件，但用户可能用右键取消了"这一次"
            if (matchedModule == "快速传送" && !CanTriggerQuickTeleport())
                return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (matchedModule == "快速传送")
                {
                    QuickTeleportTriggered?.Invoke();
                }
                else if (matchedModule == "手柄拾取")
                {
                }
                else
                {
                    ToggleIconState(matchedModule);
                }
            });
        }

        private static void OnMouseEvent(object? sender, MouseEventArgs args)
        {
            // 同键盘：鼠标按键是离散事件，判定为焦点外时先按需复核一次，避免漏掉操作
            if (!_isFocused && !WindowFocusService.Reevaluate()) return;
            if (SimulationService.IsSimulating) return;

            // 功能总开关关闭 → 所有功能都不可执行
            if (!ConfigManager.Get<HomePageSettings>().MasterSwitch) return;

            string eventDisplayName = GlobalMouseHookService.GetMouseEventName(args.EventType);

            // ★ 组合键 + 鼠标键：按下时只登记（见下方匹配段），这里等它抬起再执行。
            //   执行时不再看修饰键是否还按着 —— 用户的操作顺序本来就是"组合成立 → 松手"。
            if (_pendingMouseComboModule != null && args.EventType == _pendingMouseComboUp)
            {
                string pending = _pendingMouseComboModule;
                _pendingMouseComboModule = null;
                ExecuteMatchedModule(pending);
                return;
            }

            // 新的按下事件到来 ⇒ 上一次"待抬起"作废（松开事件丢失时不至于残留到下一次）
            if (TryGetMouseUpEvent(args.EventType, out _))
                _pendingMouseComboModule = null;

            // 地图界面 + 右键 = 切换"取消本次快速传送"标志。
            // 右键在快速传送的触发键/开图键里已被禁选，所以这里可以放心独占：
            // 条件符合但用户想手动传送时按右键，图标 锚点2 → 锚点3，本次地图内不再启用传送。
            if (args.EventType == MouseEventType.RightButtonDown
                && _isInMap
                && QuickTeleportLogic.IsRightClickCancelEnabled
                && ConfigManager.Get<HomePageSettings>().QuickTeleport)
            {
                Application.Current?.Dispatcher.Invoke(UpdateQuickTeleportIconAfterCancelToggle);
                return;
            }

            if (IsOpenMapKey(eventDisplayName))
            {
                switch (args.EventType)
                {
                    case MouseEventType.LeftButtonDown:
                    case MouseEventType.RightButtonDown:
                    case MouseEventType.MiddleButtonDown:
                    case MouseEventType.XButton1Down:
                    case MouseEventType.XButton2Down:
                        OpenMapKeyDown();
                        break;

                    case MouseEventType.LeftButtonUp:
                    case MouseEventType.RightButtonUp:
                    case MouseEventType.MiddleButtonUp:
                    case MouseEventType.XButton1Up:
                    case MouseEventType.XButton2Up:
                        OpenMapKeyUp();
                        break;
                }
                return;
            }

            if (IsRunning("快速拾取") && !QuickPickupLogic.IsPaused)
            {
                var pickupSettings = ConfigManager.Get<QuickPickupSettings>();
                if (!string.IsNullOrEmpty(pickupSettings.PauseKeys))
                {
                    var pauseKeyList = pickupSettings.PauseKeys.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var pauseKey in pauseKeyList)
                    {
                        if (string.Equals(eventDisplayName, pauseKey.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            QuickPickupLogic.Pause();
                            return;
                        }
                    }
                }
            }

            string? matchedModule = null;
            string matchedKeyConfig = "";
            var settings = ConfigManager.Get<HomePageSettings>();

            // 命中时连"是哪个配置字符串命中的"一起记下来 —— 后面要靠它判断是不是组合键
            void Match(string module, bool enabled, string key)
            {
                if (matchedModule != null || !enabled) return;
                if (!MatchesTriggerKey(key, eventDisplayName)) return;
                matchedModule = module;
                matchedKeyConfig = key ?? "";
            }

            Match("快速拾取", settings.QuickPickup, settings.QuickPickupKey);
            Match("剧情对话", settings.StoryDialogue, settings.StoryDialogueKey);
            Match("钓鱼辅助", settings.FishingAssist, settings.FishingAssistKey);
            // 同上：快速传送的触发键不认右键（右键在地图界面专用于取消本次传送，开关打开时才屏蔽）
            Match("快速传送",
                  settings.QuickTeleport && !(eventDisplayName == "右键" && QuickTeleportLogic.IsRightClickCancelEnabled),
                  settings.QuickTeleportKey);
            Match("自动伐木", settings.AutoLumber, settings.AutoLumberKey);
            Match("自动烹饪", settings.AutoCook, settings.AutoCookKey);
            Match("手柄拾取", settings.ControllerPickup, settings.ControllerPickupKey);

            if (matchedModule == null) return;

            // ★ 组合键 + 鼠标键（如 左Ctrl+左键）：组合成立时用户的手还在鼠标上，
            //   立刻执行会和真实按键抢输入 —— 先登记，等这个键抬起再跑（走它本来的触发逻辑）。
            if (matchedKeyConfig.IndexOf('+') >= 0 && TryGetMouseUpEvent(args.EventType, out var upEvent))
            {
                _pendingMouseComboModule = matchedModule;
                _pendingMouseComboUp = upEvent;
                return;
            }

            ExecuteMatchedModule(matchedModule);
        }

        #endregion

        #region 图标切换

        private static void ToggleIconState(string moduleName)
        {
            if (moduleName == "快速传送") return;

            if (IsRunning(moduleName))
            {
                StopModule(moduleName);
                return;
            }

            // 自动烹饪：先试自动烹饪，不行再试自动清药
            if (moduleName == "自动烹饪")
            {
                if (AutoClearMedicineLogic.IsRunning)
                {
                    AutoClearMedicineLogic.Stop();
                    return;
                }

                if (AutoCookLogic.CanStart())
                {
                    if (!_isFocused)
                    {
                        ToastService.Show("提示", "窗口未聚焦，无法启动自动烹饪", false);
                        return;
                    }
                    if (DetectionManager.IsInMainWindow || DetectionManager.IsInMap)
                    {
                        ToastService.Show("提示", "在主界面或地图界面，无法启动自动烹饪", false);
                        return;
                    }

                    StartModule(moduleName);
                    return;
                }

                // 自动烹饪不通过 → 只检测一次寄物装置区域
                if (AutoClearMedicineLogic.TryDetectStorageDevice())
                {
                    StartAutoClearMedicineFromTrigger();
                }
                else
                {
                    ToastService.Show("提示", "未检测到烹饪界面或寄物装置", false);
                }

                return;
            }

            if (!CanStartModule(moduleName, out string reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    ToastService.Show("提示", reason, false);
                return;
            }

            StartModule(moduleName);
        }

        /// <summary>
        /// 触发键启动自动清药：只检测一次「自动清药_寄物装置区域」，
        /// 由调用方（ToggleIconState）判断通过后再调用本方法。
        /// </summary>
        private static async void StartAutoClearMedicineFromTrigger()
        {
            try
            {
                // 同 CanStartModule：登录流程运行中不允许启动（这条路不经过 CanStartModule，单独挡一次）
                if (SubtitleHostBridge.IsUidLoginRunning)
                {
                    ToastService.Show("提示", "UID 登录流程运行中，请等登录结束后再启动功能", false);
                    return;
                }

                var settings = ConfigManager.Get<AutoCookSettings>();
                if (string.IsNullOrEmpty(settings.AutoClearMedicineSelection)
                    || settings.AutoClearMedicineSelection.EndsWith(",未选择"))
                {
                    ToastService.Show("提示", "请先选择要清理的物品", false);
                    return;
                }

                if (IsRunning("剧情对话")) StopModule("剧情对话");
                if (IsRunning("自动伐木")) StopModule("自动伐木");
                if (IsRunning("钓鱼辅助")) StopModule("钓鱼辅助");

                PauseDetectionForAutoClearMedicine();

                WindowFocusService.ActivateByProcessName();
                await Task.Delay(500);

                AutoClearMedicineLogic.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】触发键启动异常: {ex.Message}");
            }
        }

        private static void UpdateIconAtIndex(int index, string iconName)
        {
            // 图标没变化就直接返回：钓鱼这类高频刷新（约 10Hz）不该反复从 pack:// 载图并重排悬浮窗
            if (_iconNameCache.TryGetValue(index, out string? cached) && cached == iconName) return;

            if (_overlayWindow?.Content is Canvas canvas)
            {
                if (index >= canvas.Children.Count) return;
                if (canvas.Children[index] is WpfImage image)
                {
                    var bitmap = ResourceImageHelper.Load(
                        AssetLocator.Pack($"Images/图标/{iconName}"));
                    if (bitmap == null) return;

                    image.Source = bitmap;
                    image.Stretch = Stretch.Uniform;

                    _iconNameCache[index] = iconName;
                    UpdateWindowPosition();
                }
            }
        }

        #endregion

        #region 缩放

        private static double GetScaleFactor()
        {
            return ResolutionScaleHelper.GetScale(_currentWidth, _currentHeight, 0.3, 1.5);
        }

        #endregion

        #region 显示/隐藏

        private static void UpdateOverlayVisibility()
        {
            // 功能总开关关闭 → 不显示图标窗口（即使客户区窗口处于焦点）
            if (!IsMasterSwitchOn())
            {
                HideOverlay();
                return;
            }

            if (!_isFocused)
            {
                HideOverlay();
                return;
            }

            // ★ 自定义宏（插件）执行期间**不管在哪个界面都要显示**：
            //   AnyRunning() 只看内置 5 个模块，宏不在里面 ——
            //   不把 _macroRunning 算进来，在非主界面 / 非地图按触发键就完全看不到图标。
            bool shouldShow = AnyRunning() || _isInMainWindow || _isInMap || MacroEngineRunning();

            if (shouldShow)
                ShowOverlay();
            else
                HideOverlay();
        }

        private static void CreateWindowIfNeeded()
        {
            if (_overlayWindow != null) return;

            _overlayWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                ResizeMode = ResizeMode.NoResize,
                Width = 0,
                Height = 0,
                ShowActivated = false
            };

            var helper = new WindowInteropHelper(_overlayWindow);
            helper.EnsureHandle();
            OverlayWindowService.ApplyOverlayStyle(_overlayWindow);

            var canvas = new Canvas
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Width = 0,
                Height = 0,
                Background = System.Windows.Media.Brushes.Transparent
            };

            for (int i = 0; i < _moduleOrder.Count; i++)
            {
                string moduleName = _moduleOrder[i];
                string iconName = _iconMap[moduleName][0];

                var bitmap = ResourceImageHelper.Load(
                    AssetLocator.Pack($"Images/图标/{iconName}"));

                // ★★ 加载失败也**照样加一个占位 Image**，绝不能 continue 跳过 ——
                //    canvas.Children 的下标必须和 _moduleOrder 严格对齐，
                //    少一个的话后面所有 UpdateIconAtIndex(index) 都会换到别人的图标上。
                if (bitmap == null && _cachedImages != null && i < _cachedImages.Count)
                    bitmap = _cachedImages[i];

                var image = new WpfImage
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    Tag = moduleName
                };

                canvas.Children.Add(image);
            }

            _overlayWindow.Content = canvas;
            _overlayWindow.MouseLeftButtonDown += (s, e) => HideOverlay();

            OverlayWindowService.StartTopmostKeepAlive(_overlayWindow);
        }

        private static void UpdateOverlayContent()
        {
            if (_cachedImages == null || _cachedImages.Count == 0) return;
            if (_overlayWindow == null) return;

            if (_overlayWindow.Content is Canvas canvas)
            {
                for (int i = 0; i < canvas.Children.Count && i < _moduleOrder.Count; i++)
                {
                    if (canvas.Children[i] is WpfImage image)
                    {
                        string moduleName = _moduleOrder[i];
                        string iconName = GetCurrentIconName(moduleName);

                        var bitmap = ResourceImageHelper.Load(
                            AssetLocator.Pack($"Images/图标/{iconName}"));
                        if (bitmap == null) continue;

                        image.Source = bitmap;
                        image.Stretch = Stretch.Uniform;
                        image.Tag = moduleName;
                    }
                }
            }

            UpdateWindowPosition();
        }

        private static string GetCurrentIconName(string moduleName)
        {
            return moduleName switch
            {
                "快速拾取" => IsRunning("快速拾取")
                    ? (QuickPickupLogic.IsPaused ? "拾取3.png" : "拾取2.png")
                    : "拾取1.png",
                "剧情对话" => IsRunning("剧情对话")
                    ? ((!StoryDialogueLogic.IsPaused && StoryDialogueLogic.IsDetected) ? "剧情2.png" : "剧情3.png")
                    : "剧情1.png",
                "钓鱼辅助" => GetFishingAssistIconName(),
                "快速传送" => GetQuickTeleportIconName(),
                "自动伐木" => IsRunning("自动伐木") ? "伐木2.png" : "伐木1.png",
                "自动烹饪" => IsRunning("自动烹饪") ? "烹饪2.png" : "烹饪1.png",

                // ★ 自定义宏（插件）：执行中 2、没执行 1。
                //   这条必须有 —— 否则任何一次整体刷新（焦点/位置变化走 UpdateOverlayContent）
                //   都会把执行中的 2 刷回默认的 1。
                "自定义宏" => MacroEngineRunning() ? "自定义宏2.png" : "自定义宏1.png",

                _ => _iconMap[moduleName][0]
            };
        }

        private static void UpdateWindowPosition()
        {
            if (_overlayWindow == null) return;
            if (_currentWidth <= 0 || _currentHeight <= 0) return;

            var cachedPosObj = CoordinateFormats.GetCached<object>("主界面图标显示坐标");
            if (cachedPosObj == null) return;

            var cachedPos = (CoordinateFormats.Point)cachedPosObj;

            double scale = GetScaleFactor();

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            if (_overlayWindow.Content is Canvas canvas)
            {
                for (int i = 0; i < canvas.Children.Count; i++)
                {
                    if (canvas.Children[i] is WpfImage img && img.Source is BitmapImage bitmap)
                    {
                        double iconWidth = bitmap.PixelWidth * scale;
                        double iconHeight = bitmap.PixelHeight * scale;
                        double spacing = REF_SPACING * scale;

                        img.Width = DpiService.PhysicalToDipX(iconWidth);
                        img.Height = DpiService.PhysicalToDipY(iconHeight);

                        double x = i * (iconWidth + spacing);
                        Canvas.SetLeft(img, DpiService.PhysicalToDipX(x));
                        Canvas.SetTop(img, 0);

                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, 0);
                        maxX = Math.Max(maxX, x + iconWidth);
                        maxY = Math.Max(maxY, iconHeight);
                    }
                }
            }

            OverlayWindowService.SetBounds(_overlayWindow,
                (int)cachedPos.X, (int)cachedPos.Y,
                (int)(maxX - minX), (int)(maxY - minY));
        }

        private static void BringOverlayToTop()
        {
            if (_overlayWindow == null) return;
            if (!_isOverlayVisible) return;

            OverlayWindowService.BringToTop(_overlayWindow);
        }

        private static void ShowOverlay()
        {
            if (_cachedImages == null || _cachedImages.Count == 0) return;
            if (_isOverlayVisible) return;

            CreateWindowIfNeeded();
            UpdateOverlayContent();

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                UpdateWindowPosition();
            }, System.Windows.Threading.DispatcherPriority.Render);

            if (!_overlayWindow!.IsVisible)
            {
                _overlayWindow.Show();
                _isOverlayVisible = true;
            }
        }

        private static void HideOverlay()
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                _overlayWindow.Hide();
                _isOverlayVisible = false;
            }
        }

        private static void CloseOverlay()
        {
            if (_overlayWindow != null)
            {
                OverlayWindowService.StopTopmostKeepAlive(_overlayWindow);
                _overlayWindow.Close();
                _overlayWindow = null;
                _isOverlayVisible = false;

                // 窗口已销毁：清掉图标缓存，避免下次重建时因"名字相同"而被跳过设置
                _iconNameCache.Clear();
            }
        }

        #endregion
    }
}