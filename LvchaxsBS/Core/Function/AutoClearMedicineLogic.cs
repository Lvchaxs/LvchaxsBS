using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using LvchaxsBS.Services.Hooks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LvchaxsBS.Core.Function
{
    public static class AutoClearMedicineLogic
    {
        private enum ClearCapacityState
        {
            None,
            Normal,
            Locked
        }

        private static CancellationTokenSource? _cts = null;
        private static readonly object _lockObj = new object();
        private static bool _isRunning = false;

        private const string STORAGE_DEVICE_REGION_KEY = "自动清药_寄物装置区域";
        private const string STORAGE_DEVICE_TEMPLATE_KEY = "自动清药_寄物装置";
        private const string GRID_REGION_KEY = "自动清药_格子区域";

        private const string SHORT_SCROLL_BAR_POINT_KEY = "自动清药_短滚动条坐标";
        private const string LONG_SCROLL_BAR_POINT_KEY = "自动清药_长滚动条坐标";

        private const string DELICIOUS_CONFIRM_REGION_KEY = "自动清药_美味确认区域";
        private const string DELICIOUS_CONFIRM_TEMPLATE_KEY = "自动清药_美味确认";

        private const string DELICIOUS_FAIL_POINT_KEY = "自动清药_美味失败坐标";

        private const string FUEL_POINT_KEY = "自动清药_燃料坐标";

        private const string OPEN_STORAGE_REGION_KEY = "自动清药_打开寄物装置区域";
        private const string OPEN_STORAGE_TEMPLATE_KEY = "自动清药_打开寄物装置";

        private const string CLEAR_CAPACITY_POINT_KEY = "自动清药_清空容量坐标";

        private const string CONFIRM_STORAGE_REGION_KEY = "自动清药_确认储存区域";
        private const string CONFIRM_STORAGE_TEMPLATE_KEY = "自动烹饪_确认";

        private const string AUTO_MAIN_REGION_KEY = "自动化_主界面区域";
        private const string AUTO_MAIN_TEMPLATE_KEY = "自动化_主界面";

        private const string DISMANTLE_REGION_KEY = "自动清药_拆除区域";
        private const string DISMANTLE_TEMPLATE_KEY = "自动清药_拆除";

        private const string SCREENSHOT_ROOT = "截图日志";
        private const string SCREENSHOT_SUB_DIR = "自动清药-截图日志";

        /// <summary>
        /// 粗匹配档位的取值下限/上限（和 UI 数字框保持一致）。
        /// </summary>
        private const int GRID_SCALE_FACTOR_MIN = 1;
        private const int GRID_SCALE_FACTOR_MAX = 5;

        private const int SCROLL_DOWN_TIMES = 3;
        private const int CONFIRM_SETTLE_MS = 100;
        private const int REPEAT_MATCH_TIMES = 29;
        private const int REPEAT_SETTLE_MS = 50;
        private const int REPEAT_END_WAIT_MS = 500;
        private const int EXPAND_PIXELS = 15;
        private const double SCALE_PERCENT = 4;
        private const int CONFIRM_MATCH_TIMES = 0;

        private const int POLL_TIMEOUT_MS = 15000;

        private const int STORAGE_MAX_COUNT = 30;

        private const int CLEAR_CAPACITY_CLICK_TIMES = 30;
        private const int CLEAR_CAPACITY_CLICK_INTERVAL_MS = 50;

        private const int GRID_MOUSE_ENTER_WAIT_MS = 100;

        private const int TOTAL_TIMER_INTERVAL_MS = 200;

        private static bool _scrolledToBottom = false;
        private static int _scrollDownCount = 0;
        private static bool _scrollBarTypeDetected = false;
        private static bool _useShortScrollBar = false;

        private static bool _isFirstLoop = true;
        private static int _lastScrollCount = 0;

        private static bool _gridMouseEntered = false;

        private static string _itemName = "";
        private static int _totalStoredCount = 0;
        private static int _totalLimit = 1;
        private static string _endReason = "";
        private static bool _reachedTotalLimit = false;

        private static DateTime _runStartTime = DateTime.MinValue;
        private static DispatcherTimer? _totalTimer = null;

        public static event Action<double, long, double, string>? DetectionResultUpdated;
        public static event Action? Stopped;

        /// <summary>
        /// 当前的粗匹配档位（来自配置，夹在 1-5）。
        /// 1 = 不缩放（关闭粗匹配，原图直跑，最准最慢）；越大越快，但误差越大。
        /// </summary>
        private static int GridMatchScaleFactor =>
            Math.Clamp(ConfigManager.Get<AutoCookSettings>().MedicineScaleFactor,
                       GRID_SCALE_FACTOR_MIN, GRID_SCALE_FACTOR_MAX);

        public static bool IsRunning => _isRunning;

        public static bool TryDetectStorageDevice()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(STORAGE_DEVICE_REGION_KEY);
                if (matchRect == null) return false;

                var rect = matchRect.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return false;

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return false;

                var template = TemplateManager.GetCachedTemplate(STORAGE_DEVICE_TEMPLATE_KEY);
                if (template == null) return false;

                double threshold = ConfigManager.Get<AutoCookSettings>().MedicineDetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                return result != null && result.Similarity >= threshold;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDetectOpenStorage()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(OPEN_STORAGE_REGION_KEY);
                if (matchRect == null) return false;

                var rect = matchRect.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return false;

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return false;

                var template = TemplateManager.GetCachedTemplate(OPEN_STORAGE_TEMPLATE_KEY);
                if (template == null) return false;

                double threshold = ConfigManager.Get<AutoCookSettings>().MedicineDetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                return result != null && result.Similarity >= threshold;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDetectAutoMainWindow()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(AUTO_MAIN_REGION_KEY);
                if (matchRect == null) return false;

                var rect = matchRect.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return false;

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return false;

                var template = TemplateManager.GetCachedTemplate(AUTO_MAIN_TEMPLATE_KEY);
                if (template == null)
                {
                    Debug.WriteLine($"【自动清药】主界面模板不存在: {AUTO_MAIN_TEMPLATE_KEY}");
                    return false;
                }

                double threshold = ConfigManager.Get<AutoCookSettings>().MedicineDetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                return result != null && result.Similarity >= threshold;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDetectDismantle()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(DISMANTLE_REGION_KEY);
                if (matchRect == null) return false;

                var rect = matchRect.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return false;

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return false;

                var template = TemplateManager.GetCachedTemplate(DISMANTLE_TEMPLATE_KEY);
                if (template == null)
                {
                    Debug.WriteLine($"【自动清药】拆除模板不存在: {DISMANTLE_TEMPLATE_KEY}");
                    return false;
                }

                double threshold = ConfigManager.Get<AutoCookSettings>().MedicineDetectThreshold;

                var result = ImageRecognition.MatchTemplateScaledBest(
                    source, template, threshold, GridMatchScaleFactor, "拆除区域");

                return result != null && result.Matched;
            }
            catch
            {
                return false;
            }
        }

        private static ClearCapacityState CheckClearCapacityState()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return ClearCapacityState.None;

                var colorRect = CoordinateFormats.GetCached<CoordinateFormats.ColorRect>(CLEAR_CAPACITY_POINT_KEY);
                if (colorRect == null) return ClearCapacityState.None;

                var r = colorRect.Rect;

                int x1 = r.X1;
                int y1 = r.Y1;
                int w = r.X2 - r.X1;
                int h = r.Y2 - r.Y1;
                if (w < 1) w = 1;
                if (h < 1) h = 1;

                var pixels = ImageRecognition.CaptureRegionGDI(x1, y1, w, h);
                if (pixels == null) return ClearCapacityState.None;

                int width = w;
                int height = h;

                bool foundNormal = false;
                bool foundLocked = false;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (y * width + x) * 4;

                        byte b = pixels[idx];
                        byte g = pixels[idx + 1];
                        byte rch = pixels[idx + 2];

                        foreach (var target in colorRect.Colors)
                        {
                            if (Math.Abs(rch - target.R) <= colorRect.Tolerance &&
                                Math.Abs(g - target.G) <= colorRect.Tolerance &&
                                Math.Abs(b - target.B) <= colorRect.Tolerance)
                            {
                                if (target.R == 230 && target.G == 242 && target.B == 246)
                                    foundNormal = true;
                                else if (target.R == 233 && target.G == 229 && target.B == 220)
                                    foundLocked = true;
                            }
                        }
                    }
                }

                if (foundLocked) return ClearCapacityState.Locked;
                if (foundNormal) return ClearCapacityState.Normal;
                return ClearCapacityState.None;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】清空容量检测异常: {ex.Message}");
                return ClearCapacityState.None;
            }
        }

        private static async Task ClearStorageCapacityAsync(CancellationToken token)
        {
            SetSubtitleSafe(SubtitleTexts.AutoCook.ClearMedicineClearingCapacity);

            var failPoint = CoordinateFormats.GetCached<CoordinateFormats.DetectPoint>(DELICIOUS_FAIL_POINT_KEY);
            if (failPoint == null) return;

            for (int i = 0; i < CLEAR_CAPACITY_CLICK_TIMES; i++)
            {
                token.ThrowIfCancellationRequested();

                SimulationService.LeftClickAt(failPoint.X, failPoint.Y);
                await Task.Delay(CLEAR_CAPACITY_CLICK_INTERVAL_MS, token);
            }
        }

        private static string BuildElapsedString()
        {
            if (_runStartTime == DateTime.MinValue) return "";

            var ts = DateTime.Now - _runStartTime;
            return $" 运行时长 {(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private static void UpdateTotalSubtitle()
        {
            SubtitleOverlayService.SetSubtitle2(
                $"已储存 {_itemName} {_totalStoredCount}/{_totalLimit}{BuildElapsedString()}");
        }

        private static void StartTotalTimer()
        {
            StopTotalTimer();

            _totalTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TOTAL_TIMER_INTERVAL_MS)
            };
            _totalTimer.Tick += (s, e) => UpdateTotalSubtitle();
            _totalTimer.Start();
        }

        private static void StopTotalTimer()
        {
            if (_totalTimer != null)
            {
                _totalTimer.Stop();
                _totalTimer = null;
            }
        }

        public static void Start()
        {
            lock (_lockObj)
            {
                if (_isRunning) return;

                _cts = new CancellationTokenSource();
                _isRunning = true;

                _scrolledToBottom = false;
                _scrollDownCount = 0;

                _scrollBarTypeDetected = false;
                _useShortScrollBar = false;

                _isFirstLoop = true;
                _lastScrollCount = 0;

                _gridMouseEntered = false;

                _totalStoredCount = 0;
                _endReason = "";
                _reachedTotalLimit = false;

                _runStartTime = DateTime.Now;
            }

            IconService.ShowSubtitle(SubtitleOverlayService.SubtitleOwner.AutoCook);

            var settings = ConfigManager.Get<AutoCookSettings>();
            _itemName = ParseItemName(settings.AutoClearMedicineSelection);

            _totalLimit = settings.MedicineTotalLimit;
            if (_totalLimit < 1) _totalLimit = 1;

            SubtitleOverlayService.SetSubtitle("自动清药 匹配度： --  耗时： --");

            IconService.ShowSubtitle2(SubtitleOverlayService.SubtitleOwner.AutoCook);
            UpdateTotalSubtitle();
            StartTotalTimer();

            Task.Run(() => ExecuteLoop(_cts!.Token));
        }

        public static void Stop()
        {
            lock (_lockObj)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                    _cts.Cancel();
            }
        }

        private static void SetSubtitleSafe(string text)
        {
            SubtitleOverlayService.SetSubtitle(text);
        }

        private static async Task ExecuteLoop(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            string itemName = _itemName;
            if (string.IsNullOrEmpty(itemName) || itemName == "未选择")
            {
                Stop();
                return;
            }

            string templateKey = $"自动清药_{itemName}";

            try
            {
                while (!token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();

                    SetSubtitleSafe(SubtitleTexts.AutoCook.ClearMedicineOpeningStorage);

                    bool storageFound = await WaitStorageDeviceAsync(token, settings.MedicineInterval_1);
                    if (!storageFound)
                    {
                        _endReason = SubtitleTexts.AutoCook.ClearMedicineOpeningStorage;
                        Debug.WriteLine("【自动清药】未检测到寄物装置区域，结束");
                        Stop();
                        return;
                    }

                    if (_reachedTotalLimit)
                    {
                        Stop();
                        return;
                    }

                    var clearState = CheckClearCapacityState();
                    Debug.WriteLine($"[自动清药] 清空容量状态：{clearState}");

                    if (clearState == ClearCapacityState.Locked)
                    {
                        Debug.WriteLine("[自动清药] 检测到锁定物品，跳过本轮格子匹配");

                        SimulationService.PressEscape();

                        bool inMainLocked = await WaitMainWindowAsync(token, settings.MedicineInterval_1);
                        if (!inMainLocked)
                        {
                            _endReason = SubtitleTexts.AutoCook.ClearMedicineWaitingMain;
                            Debug.WriteLine("【自动清药】未回到主界面，结束");
                            Stop();
                            return;
                        }

                        SimulationService.PressT();

                        bool dismantleFoundLocked = await WaitDismantleAsync(token, settings.MedicineInterval_1);
                        if (!dismantleFoundLocked)
                        {
                            _endReason = SubtitleTexts.AutoCook.ClearMedicineOpeningStorage;
                            Debug.WriteLine("【自动清药】未检测到拆除区域，结束");
                            Stop();
                            return;
                        }

                        SimulationService.LeftClick();

                        bool fuelFoundLocked = await WaitFuelPointAsync(token, settings.MedicineInterval_1);
                        if (!fuelFoundLocked)
                        {
                            _endReason = SubtitleTexts.AutoCook.ClearMedicineFuelInsufficient;
                            Debug.WriteLine("【自动清药】未检测到燃料坐标，结束");
                            Stop();
                            return;
                        }

                        SimulationService.LeftClick();

                        await Task.Delay(1000, token);

                        bool openStorageFoundLocked = await WaitOpenStorageAsync(token, settings.MedicineInterval_1);
                        if (!openStorageFoundLocked)
                        {
                            _endReason = SubtitleTexts.AutoCook.ClearMedicineOpeningStorage;
                            Debug.WriteLine("【自动清药】未检测到打开寄物装置区域，结束");
                            Stop();
                            return;
                        }

                        SimulationService.PressF();

                        continue;
                    }
                    else if (clearState == ClearCapacityState.Normal)
                    {
                        await ClearStorageCapacityAsync(token);
                    }

                    _scrolledToBottom = false;
                    _scrollDownCount = 0;
                    _scrollBarTypeDetected = false;
                    _useShortScrollBar = false;
                    _gridMouseEntered = false;

                    bool roundCompleted = false;
                    int foundScrollCount = -1;
                    bool recordInvalidated = false;

                    bool jumped = !_isFirstLoop && _lastScrollCount > 0;

                    if (jumped)
                    {
                        await ScrollGridDownAsync(token, _lastScrollCount);
                        _scrollDownCount += _lastScrollCount;

                        await Task.Delay(settings.MedicineInterval_1, token);
                    }

                    while (!token.IsCancellationRequested)
                    {
                        token.ThrowIfCancellationRequested();

                        bool matched = await TryMatch(itemName, templateKey, settings, token, settings.MedicineInterval_1);

                        if (matched)
                        {
                            roundCompleted = true;

                            if (jumped)
                            {
                                if (_scrollDownCount == _lastScrollCount)
                                {
                                    foundScrollCount = _lastScrollCount;
                                }
                                else
                                {
                                    recordInvalidated = true;
                                }
                            }
                            else
                            {
                                foundScrollCount = _scrollDownCount;
                            }

                            break;
                        }

                        if (!_scrollBarTypeDetected)
                        {
                            DetectScrollBarType();
                        }

                        bool atBottom = CheckScrollBarColor();

                        if (!atBottom)
                        {
                            await ScrollGridDownAsync(token);
                            _scrollDownCount++;

                            await Task.Delay(settings.MedicineInterval_1, token);
                        }
                        else
                        {
                            if (!_scrolledToBottom && _scrollDownCount > 0)
                            {
                                _scrolledToBottom = true;

                                for (int i = 0; i < _scrollDownCount; i++)
                                {
                                    token.ThrowIfCancellationRequested();

                                    ScrollGridUp();
                                    await Task.Delay(settings.MedicineInterval_1, token);

                                    bool matchedOnWayBack = await TryMatch(itemName, templateKey, settings, token, settings.MedicineInterval_1);
                                    if (matchedOnWayBack)
                                    {
                                        roundCompleted = true;

                                        if (jumped)
                                        {
                                            recordInvalidated = true;
                                        }

                                        break;
                                    }
                                }

                                if (roundCompleted) break;
                            }

                            _endReason = SubtitleTexts.AutoCook.ClearMedicineOpeningStorage;
                            Debug.WriteLine("【自动清药】滚到底且原路返回后仍未匹配到，结束");
                            Stop();
                            return;
                        }
                    }

                    if (roundCompleted)
                    {
                        if (_isFirstLoop)
                        {
                            if (foundScrollCount >= 0)
                            {
                                _lastScrollCount = foundScrollCount;
                            }
                            _isFirstLoop = false;
                        }

                        if (recordInvalidated)
                        {
                            _lastScrollCount = 0;
                            _isFirstLoop = true;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】循环异常: {ex.Message}");
            }
            finally
            {
                lock (_lockObj)
                {
                    _isRunning = false;
                    if (_cts != null) { _cts.Dispose(); _cts = null; }
                }

                StopTotalTimer();

                string elapsed = BuildElapsedString();

                if (_reachedTotalLimit)
                {
                    SubtitleOverlayService.SetSubtitle2(
                        $"已储存 {_itemName} {_totalStoredCount}/{_totalLimit}{elapsed} 已结束-按下TAB关闭");
                }
                else if (!string.IsNullOrEmpty(_endReason))
                {
                    SubtitleOverlayService.SetSubtitle2(
                        $"已储存 {_itemName} {_totalStoredCount}/{_totalLimit} 错误：{_endReason}{elapsed} 超时自动结束-按下TAB关闭");
                }
                else
                {
                    SubtitleOverlayService.SetSubtitle2(
                        $"已储存 {_itemName} {_totalStoredCount}/{_totalLimit}{elapsed} 已结束-按下TAB关闭");
                }

                Stopped?.Invoke();
            }
        }

        private static async Task<bool> WaitStorageDeviceAsync(CancellationToken token, int intervalMs)
        {
            var sw = Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();

                if (TryDetectStorageDevice())
                    return true;

                if (sw.ElapsedMilliseconds >= POLL_TIMEOUT_MS)
                    return false;

                await Task.Delay(intervalMs, token);
            }

            return false;
        }

        private static async Task<bool> WaitMainWindowAsync(CancellationToken token, int intervalMs)
        {
            var sw = Stopwatch.StartNew();

            SetSubtitleSafe(SubtitleTexts.AutoCook.ClearMedicineWaitingMain);

            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();

                if (TryDetectAutoMainWindow())
                {
                    SetSubtitleSafe(SubtitleTexts.AutoCook.ClearMedicineDestroyStorage);
                    return true;
                }

                if (sw.ElapsedMilliseconds >= POLL_TIMEOUT_MS)
                    return false;

                await Task.Delay(intervalMs, token);
            }

            return false;
        }

        private static async Task<bool> WaitFuelPointAsync(CancellationToken token, int intervalMs)
        {
            var sw = Stopwatch.StartNew();

            SetSubtitleSafe(SubtitleTexts.AutoCook.ClearMedicineFuelInsufficient);

            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();

                if (CheckFuelPointHasColor())
                {
                    SetSubtitleSafe(SubtitleTexts.AutoCook.ClearMedicineRecreateStorage);
                    return true;
                }

                if (sw.ElapsedMilliseconds >= POLL_TIMEOUT_MS)
                    return false;

                await Task.Delay(intervalMs, token);
            }

            return false;
        }

        private static async Task<bool> WaitOpenStorageAsync(CancellationToken token, int intervalMs)
        {
            var sw = Stopwatch.StartNew();

            SetSubtitleSafe(SubtitleTexts.AutoCook.ClearMedicineOpeningStorage);

            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();

                if (TryDetectOpenStorage())
                    return true;

                if (sw.ElapsedMilliseconds >= POLL_TIMEOUT_MS)
                    return false;

                await Task.Delay(intervalMs, token);
            }

            return false;
        }

        private static async Task<bool> WaitDismantleAsync(CancellationToken token, int intervalMs)
        {
            var sw = Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();

                if (TryDetectDismantle())
                    return true;

                if (sw.ElapsedMilliseconds >= POLL_TIMEOUT_MS)
                    return false;

                await Task.Delay(intervalMs, token);
            }

            return false;
        }

        private static async Task<bool> WaitConfirmStorageAsync(CancellationToken token, int intervalMs)
        {
            var sw = Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();

                if (TryDetectConfirmStorage(out int clickX, out int clickY))
                {
                    SimulationService.LeftClickAt(clickX, clickY);
                    await Task.Delay(500, token);
                    return true;
                }

                if (sw.ElapsedMilliseconds >= POLL_TIMEOUT_MS)
                    return false;

                await Task.Delay(intervalMs, token);
            }

            return false;
        }

        private static bool TryDetectConfirmStorage(out int clickX, out int clickY)
        {
            clickX = 0;
            clickY = 0;

            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(CONFIRM_STORAGE_REGION_KEY);
                if (matchRect == null) return false;

                var rect = matchRect.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return false;

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return false;

                var template = TemplateManager.GetCachedTemplate(CONFIRM_STORAGE_TEMPLATE_KEY);
                if (template == null) return false;

                double threshold = ConfigManager.Get<AutoCookSettings>().MedicineDetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                if (result != null && result.Similarity >= threshold)
                {
                    double capScale = ImageRecognition.GetCaptureScale();
                    clickX = rect.X1 + (int)((result.X + template.Width / 2.0) / capScale);
                    clickY = rect.Y1 + (int)((result.Y + template.Height / 2.0) / capScale);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckFuelPointHasColor()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var fuelRect = CoordinateFormats.GetCached<CoordinateFormats.ColorRect>(FUEL_POINT_KEY);
                if (fuelRect == null) return false;

                var r = fuelRect.Rect;

                int x1 = r.X1;
                int y1 = r.Y1;
                int w = r.X2 - r.X1;
                int h = r.Y2 - r.Y1;
                if (w < 1) w = 1;
                if (h < 1) h = 1;

                var pixels = ImageRecognition.CaptureRegionGDI(x1, y1, w, h);
                if (pixels == null) return false;

                int width = w;
                int height = h;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (y * width + x) * 4;

                        byte b = pixels[idx];
                        byte g = pixels[idx + 1];
                        byte rch = pixels[idx + 2];

                        foreach (var target in fuelRect.Colors)
                        {
                            if (Math.Abs(rch - target.R) <= fuelRect.Tolerance &&
                                Math.Abs(g - target.G) <= fuelRect.Tolerance &&
                                Math.Abs(b - target.B) <= fuelRect.Tolerance)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】燃料区域检测异常: {ex.Message}");
                return false;
            }
        }

        private static void DetectScrollBarType()
        {
            try
            {
                _scrollBarTypeDetected = true;

                if (!CoordinateFormats.CacheReady) return;

                var longPoint = CoordinateFormats.GetCached<CoordinateFormats.DetectPoint>(LONG_SCROLL_BAR_POINT_KEY);
                if (longPoint == null)
                {
                    _useShortScrollBar = false;
                    return;
                }

                var probeColor = ImageRecognition.GetPixelColor(longPoint.X, longPoint.Y);

                bool isShort =
                    Math.Abs(probeColor.R - 233) <= longPoint.Tolerance &&
                    Math.Abs(probeColor.G - 233) <= longPoint.Tolerance &&
                    Math.Abs(probeColor.B - 233) <= longPoint.Tolerance;

                _useShortScrollBar = isShort;
            }
            catch
            {
                _useShortScrollBar = false;
            }
        }

        private static bool CheckScrollBarColor()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                string key = _useShortScrollBar ? SHORT_SCROLL_BAR_POINT_KEY : LONG_SCROLL_BAR_POINT_KEY;

                var detectPoint = CoordinateFormats.GetCached<CoordinateFormats.DetectPoint>(key);
                if (detectPoint == null) return false;

                var actual = ImageRecognition.GetPixelColor(detectPoint.X, detectPoint.Y);

                foreach (var target in detectPoint.Colors)
                {
                    if (Math.Abs(actual.R - target.R) <= detectPoint.Tolerance &&
                        Math.Abs(actual.G - target.G) <= detectPoint.Tolerance &&
                        Math.Abs(actual.B - target.B) <= detectPoint.Tolerance)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static async Task ScrollGridDownAsync(CancellationToken token, int times = 1)
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return;
                if (times < 1) times = 1;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(GRID_REGION_KEY);
                if (matchRect == null) return;

                var rect = matchRect.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return;

                int x = rect.CenterX;
                int y = rect.Y1 + 5;

                if (!_gridMouseEntered)
                {
                    SimulationService.MoveTo(x, y);
                    _gridMouseEntered = true;
                    await Task.Delay(GRID_MOUSE_ENTER_WAIT_MS, token);
                }

                SimulationService.ScrollDownOnly(SCROLL_DOWN_TIMES * times);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】向下滚动异常: {ex.Message}");
            }
        }

        private static void ScrollGridUp()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return;
                if (!_gridMouseEntered) return;

                SimulationService.ScrollUpOnly(SCROLL_DOWN_TIMES);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】向上滚动异常: {ex.Message}");
            }
        }

        private static ImageRecognition.MatchResult? MatchWithScaleFallback(
            Bitmap source, Bitmap template, double threshold, out double bestScore)
        {
            bestScore = 0;

            var normal = ImageRecognition.MatchTemplate(source, template, threshold);
            if (normal != null)
            {
                if (normal.Similarity > bestScore) bestScore = normal.Similarity;
                if (normal.Matched) return normal;
            }

            if (Math.Abs(SCALE_PERCENT) > 0.001)
            {
                double factor = 1.0 + SCALE_PERCENT / 100.0;
                if (factor <= 0.05) factor = 0.05;

                int newW = Math.Max(1, (int)Math.Round(template.Width * factor));
                int newH = Math.Max(1, (int)Math.Round(template.Height * factor));

                using var scaledTemplate = ImageRecognition.ScaleBitmap(template, newW, newH);

                var scaled = ImageRecognition.MatchTemplate(source, scaledTemplate, threshold);
                if (scaled != null)
                {
                    if (scaled.Similarity > bestScore) bestScore = scaled.Similarity;
                    if (scaled.Matched) return scaled;
                }
            }

            return null;
        }

        private static ImageRecognition.MatchResult? MatchScaledOnly(
            Bitmap source, Bitmap template, double threshold, out double bestScore)
        {
            bestScore = 0;

            if (Math.Abs(SCALE_PERCENT) <= 0.001)
            {
                var normal = ImageRecognition.MatchTemplate(source, template, threshold);
                if (normal != null)
                {
                    bestScore = normal.Similarity;
                    if (normal.Matched) return normal;
                }
                return null;
            }

            double factor = 1.0 + SCALE_PERCENT / 100.0;
            if (factor <= 0.05) factor = 0.05;

            int newW = Math.Max(1, (int)Math.Round(template.Width * factor));
            int newH = Math.Max(1, (int)Math.Round(template.Height * factor));

            using var scaledTemplate = ImageRecognition.ScaleBitmap(template, newW, newH);

            var scaled = ImageRecognition.MatchTemplate(source, scaledTemplate, threshold);
            if (scaled != null)
            {
                bestScore = scaled.Similarity;
                if (scaled.Matched) return scaled;
            }

            return null;
        }

        private static async Task<bool> TryMatch(string itemName, string templateKey, AutoCookSettings settings, CancellationToken token, int intervalMs)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(GRID_REGION_KEY);
                if (matchRect == null) return false;

                var rect = matchRect.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return false;

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return false;

                var template = TemplateManager.GetCachedTemplate(templateKey);
                if (template == null)
                {
                    Debug.WriteLine($"【自动清药】模板不存在: {templateKey}");
                    return false;
                }

                double threshold = settings.MedicineDetectThreshold;

                var best = ImageRecognition.MatchTemplateScaledBest(
                    source, template, threshold, GridMatchScaleFactor, "格子区");

                long elapsedMs = sw.ElapsedMilliseconds;

                double similarity = best?.Similarity ?? 0;

                DetectionResultUpdated?.Invoke(similarity, elapsedMs, threshold, "自动清药");
                SetSubtitleSafe($"自动清药 匹配度： {similarity * 100:F0}%  耗时： {elapsedMs}ms");

                if (best != null && best.Matched)
                {
                    SaveMatchedScreenshot(source, itemName, similarity, elapsedMs,
                        new List<ImageRecognition.MatchResult> { best }, template.Width, template.Height);

                    int confirmX = best.X;
                    int confirmY = best.Y;
                    bool confirmStable = true;

                    for (int c = 0; c < CONFIRM_MATCH_TIMES; c++)
                    {
                        await Task.Delay(REPEAT_SETTLE_MS, token);

                        using var confirmSource = ImageRecognition.CaptureRegionScaled(matchRect);
                        if (confirmSource == null)
                        {
                            Debug.WriteLine($"【自动清药-确认】第 {c + 1}/{CONFIRM_MATCH_TIMES} 次 截图失败");
                            confirmStable = false;
                            break;
                        }

                        var confirmResult = ImageRecognition.MatchTemplateScaledBest(
                            confirmSource, template, threshold, GridMatchScaleFactor);

                        double confirmScore = confirmResult?.Similarity ?? 0;

                        if (confirmResult == null || !confirmResult.Matched)
                        {
                            Debug.WriteLine($"【自动清药-确认】第 {c + 1}/{CONFIRM_MATCH_TIMES} 次 未命中 匹配度={confirmScore * 100:F2}%");
                            confirmStable = false;
                            break;
                        }

                        confirmX = confirmResult.X;
                        confirmY = confirmResult.Y;

                        Debug.WriteLine($"【自动清药-确认】第 {c + 1}/{CONFIRM_MATCH_TIMES} 次 匹配度={confirmScore * 100:F2}% 位置=({confirmX},{confirmY})");
                    }

                    if (!confirmStable)
                    {
                        Debug.WriteLine("【自动清药-确认】位置未稳定，回到滚动循环继续");
                        return false;
                    }

                    double capScale = ImageRecognition.GetCaptureScale();

                    // ===== 首次目标：截图内部坐标 → 客户区左上角 =====
                    int targetClientX = rect.X1 + (int)(confirmX / capScale);
                    int targetClientY = rect.Y1 + (int)(confirmY / capScale);

                    // 模板在客户区的尺寸
                    int templateClientW = Math.Max(1, (int)Math.Round(template.Width / capScale));
                    int templateClientH = Math.Max(1, (int)Math.Round(template.Height / capScale));

                    int clickX = targetClientX + templateClientW / 2;
                    int clickY = targetClientY + templateClientH / 2;

                    Debug.WriteLine($"【自动清药-确认】位置已稳定，点击最终位置=({clickX},{clickY})  目标客户区左上=({targetClientX},{targetClientY})  模板客户区尺寸={templateClientW}x{templateClientH}");

                    SimulationService.LeftClickAt(clickX, clickY);
                    Thread.Sleep(CONFIRM_SETTLE_MS);

                    bool confirmed = TryConfirmDelicious(settings);

                    if (!confirmed)
                    {
                        TryClickDeliciousFailPoint();

                        _endReason = SubtitleTexts.AutoCook.ClearMedicineClosingFormat;
                        Debug.WriteLine("【自动清药】美味确认失败，点失败坐标后结束");
                        Stop();
                        return true;
                    }

                    int storedCount = 1;
                    SetSubtitleSafe(string.Format(
                        SubtitleTexts.AutoCook.ClearMedicineStorageFormat,
                        storedCount, STORAGE_MAX_COUNT));

                    _totalStoredCount++;
                    UpdateTotalSubtitle();

                    if (_totalStoredCount >= _totalLimit)
                    {
                        _reachedTotalLimit = true;
                    }

                    // ===== 用客户区坐标记录"上次检测到的目标左上角" =====
                    int lastClientX = targetClientX;
                    int lastClientY = targetClientY;

                    for (int i = 0; i < REPEAT_MATCH_TIMES && !_reachedTotalLimit; i++)
                    {
                        await Task.Delay(REPEAT_SETTLE_MS, token);

                        // 以"上次检测到的目标客户区左上角"为中心，四周扩展 EXPAND_PIXELS（客户区像素）
                        int exX1 = Math.Max(rect.X1, lastClientX - EXPAND_PIXELS);
                        int exY1 = Math.Max(rect.Y1, lastClientY - EXPAND_PIXELS);
                        int exX2 = Math.Min(rect.X1 + rect.Width, lastClientX + templateClientW + EXPAND_PIXELS);
                        int exY2 = Math.Min(rect.Y1 + rect.Height, lastClientY + templateClientH + EXPAND_PIXELS);

                        int screenX1 = exX1;
                        int screenY1 = exY1;
                        int screenW = exX2 - exX1;
                        int screenH = exY2 - exY1;

                        if (screenW < templateClientW || screenH < templateClientH)
                        {
                            Debug.WriteLine($"【自动清药-重复】第 {i + 1}/{REPEAT_MATCH_TIMES} 次 扩展区域过小，跳过");
                            break;
                        }

                        using var expandRegion = ImageRecognition.CaptureRegionScaled(screenX1, screenY1, screenW, screenH);
                        if (expandRegion == null)
                        {
                            Debug.WriteLine($"【自动清药-重复】第 {i + 1}/{REPEAT_MATCH_TIMES} 次 截图失败");
                            break;
                        }

                        var expandResult = MatchScaledOnly(expandRegion, template, threshold, out double bestScore);

                        if (expandResult == null)
                        {
                            Debug.WriteLine($"【自动清药-重复】第 {i + 1}/{REPEAT_MATCH_TIMES} 次 匹配失败  匹配度={bestScore * 100:F2}%  阈值={threshold * 100:F0}%  客户区=({screenX1}, {screenY1}, {screenX1 + screenW}, {screenY1 + screenH})");
                            break;
                        }

                        // ===== 匹配结果转客户区坐标 =====
                        int matchClientX = screenX1 + (int)(expandResult.X / capScale);
                        int matchClientY = screenY1 + (int)(expandResult.Y / capScale);

                        Debug.WriteLine($"【自动清药-重复】第 {i + 1}/{REPEAT_MATCH_TIMES} 次 匹配度={expandResult.Similarity * 100:F2}%  客户区=({screenX1}, {screenY1}, {screenX1 + screenW}, {screenY1 + screenH})  目标客户区左上=({matchClientX},{matchClientY})");

                        int repClickX = matchClientX + templateClientW / 2;
                        int repClickY = matchClientY + templateClientH / 2;

                        SimulationService.LeftClickAt(repClickX, repClickY);

                        storedCount++;
                        if (storedCount > STORAGE_MAX_COUNT) storedCount = STORAGE_MAX_COUNT;

                        SetSubtitleSafe(string.Format(
                            SubtitleTexts.AutoCook.ClearMedicineStorageFormat,
                            storedCount, STORAGE_MAX_COUNT));

                        _totalStoredCount++;
                        UpdateTotalSubtitle();

                        if (_totalStoredCount >= _totalLimit)
                        {
                            _reachedTotalLimit = true;
                            break;
                        }

                        // 更新"上次检测到的目标客户区左上角"
                        lastClientX = matchClientX;
                        lastClientY = matchClientY;

                        if (storedCount >= STORAGE_MAX_COUNT)
                        {
                            break;
                        }
                    }

                    SetSubtitleSafe(string.Format(
                        SubtitleTexts.AutoCook.ClearMedicineClosingFormat,
                        storedCount, STORAGE_MAX_COUNT));

                    await Task.Delay(REPEAT_END_WAIT_MS, token);
                    SimulationService.PressF();

                    bool confirmStorageFound = await WaitConfirmStorageAsync(token, intervalMs);
                    if (!confirmStorageFound)
                    {
                        _endReason = SubtitleTexts.AutoCook.ClearMedicineClosingFormat;
                        Debug.WriteLine("【自动清药】未检测到确认储存区域，结束");
                        Stop();
                        return true;
                    }

                    SimulationService.PressEscape();

                    bool inMain = await WaitMainWindowAsync(token, intervalMs);
                    if (!inMain)
                    {
                        _endReason = SubtitleTexts.AutoCook.ClearMedicineWaitingMain;
                        Debug.WriteLine("【自动清药】未回到主界面，结束");
                        Stop();
                        return true;
                    }

                    SimulationService.PressT();

                    bool dismantleFound = await WaitDismantleAsync(token, intervalMs);
                    if (!dismantleFound)
                    {
                        _endReason = SubtitleTexts.AutoCook.ClearMedicineOpeningStorage;
                        Debug.WriteLine("【自动清药】未检测到拆除区域，结束");
                        Stop();
                        return true;
                    }

                    SimulationService.LeftClick();

                    bool fuelFound = await WaitFuelPointAsync(token, intervalMs);
                    if (!fuelFound)
                    {
                        _endReason = SubtitleTexts.AutoCook.ClearMedicineFuelInsufficient;
                        Debug.WriteLine("【自动清药】未检测到燃料坐标，结束");
                        Stop();
                        return true;
                    }

                    SimulationService.LeftClick();

                    await Task.Delay(1000, token);

                    bool openStorageFound = await WaitOpenStorageAsync(token, intervalMs);
                    if (!openStorageFound)
                    {
                        _endReason = SubtitleTexts.AutoCook.ClearMedicineOpeningStorage;
                        Debug.WriteLine("【自动清药】未检测到打开寄物装置区域，结束");
                        Stop();
                        return true;
                    }

                    SimulationService.PressF();

                    return true;
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】匹配异常: {ex.Message}");
                return false;
            }
        }

        private static void TryClickDeliciousFailPoint()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return;

                var failPoint = CoordinateFormats.GetCached<CoordinateFormats.DetectPoint>(DELICIOUS_FAIL_POINT_KEY);
                if (failPoint == null) return;

                SimulationService.LeftClickAt(failPoint.X, failPoint.Y);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】美味失败点击异常: {ex.Message}");
            }
        }

        private static bool TryConfirmDelicious(AutoCookSettings settings)
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(DELICIOUS_CONFIRM_REGION_KEY);
                if (matchRect == null) return false;

                var rect = matchRect.Rect;
                if (rect.Width <= 0 || rect.Height <= 0) return false;

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return false;

                var template = TemplateManager.GetCachedTemplate(DELICIOUS_CONFIRM_TEMPLATE_KEY);
                if (template == null)
                {
                    return false;
                }

                double threshold = settings.MedicineDetectThreshold;

                var result = MatchWithScaleFallback(source, template, threshold, out _);

                return result != null;
            }
            catch
            {
                return false;
            }
        }

        private static void SaveMatchedScreenshot(Bitmap source, string itemName,
            double similarity, long elapsedMs,
            List<ImageRecognition.MatchResult> results, int matchW, int matchH)
        {
            if (!ConfigManager.Get<AutoCookSettings>().MedicineSaveScreenshotLog)
                return;

            try
            {
                using var annotated = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(annotated))
                {
                    g.DrawImage(source, 0, 0);

                    int n = results.Count;

                    using var redPen = new Pen(Color.Red, 2);

                    float fontSize = matchH * 0.22f;
                    if (fontSize > 12f) fontSize = 12f;
                    if (fontSize < 8f) fontSize = 8f;

                    using var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var redBrush = new SolidBrush(Color.Red);

                    for (int i = 0; i < n; i++)
                    {
                        var r = results[i];
                        g.DrawRectangle(redPen, r.X, r.Y, matchW, matchH);

                        string text = $"{r.Similarity * 100:F2}%";
                        var textSize = g.MeasureString(text, font);

                        float textX = r.X + (matchW - textSize.Width) / 2f;
                        if (textX < r.X + 1) textX = r.X + 1;

                        float textY = r.Y + matchH - textSize.Height - 2;
                        if (textY < r.Y + 1) textY = r.Y + 1;

                        g.DrawString(text, font, redBrush, textX, textY);
                    }
                }

                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SCREENSHOT_ROOT, SCREENSHOT_SUB_DIR);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                int nextIndex = GetNextScreenshotIndex(dir);

                string fileName = $"{nextIndex}_{itemName}-匹配度{similarity * 100:F0}%-耗时{elapsedMs}ms.png";
                annotated.Save(Path.Combine(dir, fileName), ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动清药】保存截图失败: {ex.Message}");
            }
        }

        private static int GetNextScreenshotIndex(string dir)
        {
            try
            {
                int maxIndex = 0;

                var files = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);

                    int underscorePos = name.IndexOf('_');
                    if (underscorePos <= 0) continue;

                    string prefix = name.Substring(0, underscorePos);

                    if (int.TryParse(prefix, out int idx))
                    {
                        if (idx > maxIndex) maxIndex = idx;
                    }
                }

                return maxIndex + 1;
            }
            catch
            {
                return 1;
            }
        }

        private static string ParseItemName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var parts = raw.Split(',');
            return parts.Length == 3 ? parts[2] : "";
        }
    }
}