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

        private static readonly Dictionary<string, string[]> _iconMap = new()
        {
            ["快速拾取"] = new[] { "拾取1.png", "拾取2.png", "拾取3.png" },
            ["剧情对话"] = new[] { "剧情1.png", "剧情2.png", "剧情3.png" },
            ["钓鱼辅助"] = new[] { "钓鱼1.png", "钓鱼2.png", "钓鱼3.png" },
            ["快速传送"] = new[] { "锚点1.png", "锚点2.png" },
            ["自动伐木"] = new[] { "伐木1.png", "伐木2.png" },
            ["自动烹饪"] = new[] { "烹饪1.png", "烹饪2.png" },
        };

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
                    $"pack://application:,,,/Resources/Images/图标/{kvp.Value[0]}");
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
            SubtitleOverlayService.ShowSubtitle();
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
                SubtitleOverlayService.HideSubtitle();
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

        private static bool CanStartModule(string moduleName, out string reason)
        {
            reason = "";

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
                SubtitleOverlayService.HideSubtitle();
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
                SubtitleOverlayService.HideSubtitle();
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

                SyncQuickPickupState();
                SyncStoryDialogueState();
                SyncFishingAssistState();

                UpdateOverlayVisibility();
            });
        }

        private static void OnMapDetectionResultChanged(bool isInMap)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _isInMap = isInMap;

                SyncStoryDialogueState();

                int quickTeleportIndex = _moduleOrder.IndexOf("快速传送");
                if (quickTeleportIndex >= 0)
                {
                    string iconName = isInMap ? "锚点2.png" : "锚点1.png";
                    UpdateIconAtIndex(quickTeleportIndex, iconName);
                }

                UpdateOverlayVisibility();
            });
        }

        #endregion

        #region 图标更新方法

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
                if (!AutoClearMedicineLogic.IsRunning)
                {
                    SubtitleOverlayService.HideSubtitle();
                    SubtitleOverlayService.HideSubtitle2();
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

            if (keyName == settings.QuickPickupKey && settings.QuickPickup) matchedModule = "快速拾取";
            else if (keyName == settings.StoryDialogueKey && settings.StoryDialogue) matchedModule = "剧情对话";
            else if (keyName == settings.FishingAssistKey && settings.FishingAssist) matchedModule = "钓鱼辅助";
            else if (keyName == settings.QuickTeleportKey && settings.QuickTeleport) matchedModule = "快速传送";
            else if (keyName == settings.AutoLumberKey && settings.AutoLumber) matchedModule = "自动伐木";
            else if (keyName == settings.AutoCookKey && settings.AutoCook) matchedModule = "自动烹饪";
            else if (keyName == settings.ControllerPickupKey && settings.ControllerPickup) matchedModule = "手柄拾取";

            if (matchedModule != null)
            {
                if (matchedModule == "快速传送" && !InOpenMapWindow() && (_isInMainWindow || !_isInMap))
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
        }

        #endregion

        #region 鼠标事件

        private static void OnMouseEvent(object? sender, MouseEventArgs args)
        {
            // 同键盘：鼠标按键是离散事件，判定为焦点外时先按需复核一次，避免漏掉操作
            if (!_isFocused && !WindowFocusService.Reevaluate()) return;
            if (SimulationService.IsSimulating) return;

            // 功能总开关关闭 → 所有功能都不可执行
            if (!ConfigManager.Get<HomePageSettings>().MasterSwitch) return;

            string eventDisplayName = GlobalMouseHookService.GetMouseEventName(args.EventType);

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
            var settings = ConfigManager.Get<HomePageSettings>();

            if (settings.QuickPickup && settings.QuickPickupKey == eventDisplayName)
                matchedModule = "快速拾取";
            else if (settings.StoryDialogue && settings.StoryDialogueKey == eventDisplayName)
                matchedModule = "剧情对话";
            else if (settings.FishingAssist && settings.FishingAssistKey == eventDisplayName)
                matchedModule = "钓鱼辅助";
            else if (settings.QuickTeleport && settings.QuickTeleportKey == eventDisplayName)
                matchedModule = "快速传送";
            else if (settings.AutoLumber && settings.AutoLumberKey == eventDisplayName)
                matchedModule = "自动伐木";
            else if (settings.AutoCook && settings.AutoCookKey == eventDisplayName)
                matchedModule = "自动烹饪";
            else if (settings.ControllerPickup && settings.ControllerPickupKey == eventDisplayName)
                matchedModule = "手柄拾取";

            if (matchedModule == null) return;

            if (matchedModule == "快速传送" && !InOpenMapWindow() && (_isInMainWindow || !_isInMap))
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
                        $"pack://application:,,,/Resources/Images/图标/{iconName}");
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

            bool shouldShow = AnyRunning() || _isInMainWindow || _isInMap;

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
                    $"pack://application:,,,/Resources/Images/图标/{iconName}");
                if (bitmap == null) continue;

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
                            $"pack://application:,,,/Resources/Images/图标/{iconName}");
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
                "快速传送" => _isInMap ? "锚点2.png" : "锚点1.png",
                "自动伐木" => IsRunning("自动伐木") ? "伐木2.png" : "伐木1.png",
                "自动烹饪" => IsRunning("自动烹饪") ? "烹饪2.png" : "烹饪1.png",
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