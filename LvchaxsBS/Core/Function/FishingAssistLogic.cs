using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LvchaxsBS.Core
{
    public static class FishingAssistLogic
    {
        // 张力区目标颜色
        private static readonly CoordinateFormats.RgbColor TENSION_TARGET_COLOR = new CoordinateFormats.RgbColor(255, 255, 192);

        private static CancellationTokenSource? _cts = null;
        private static readonly object _lockObj = new object();
        private static bool _isRunning = false;
        private static bool _isPaused = false;
        private static string _currentStatus = "";
        private static CancellationTokenSource? _pauseCts = null;
        private static bool _isInTensionMode = false;
        private static DateTime _tensionStartTime = DateTime.MinValue;
        private static bool _tensionTimerStarted = false;
        private static int _tensionLostCounter = 0;
        private const int TENSION_LOST_THRESHOLD = 5;
        private const int TENSION_TIMEOUT_SECONDS = 3;

        /// <summary>最近一次检测到"鱼竿状态/张力区"的时刻（用于判断是否还在钓鱼流程中）。</summary>
        private static DateTime _lastFishingSignalAt = DateTime.MinValue;

        /// <summary>钓鱼流程宽限期（毫秒）：这么久没检测到任何钓鱼信号才算离开钓鱼流程。</summary>
        private const int FishingFlowGraceMs = 3000;

        // UI 更新事件（仅用于配置页显示，不参与状态管理）
        public static event Action<double, long, double, string>? DetectionResultUpdated;
        public static event Action<double, long, double, string>? TensionResultUpdated;

        public static bool IsRunning => _isRunning;
        public static bool IsPaused => _isPaused;
        public static string CurrentStatus => _currentStatus;

        /// <summary>是否处于张力阶段（上钩后正在等/读张力区结果）。</summary>
        public static bool IsInTensionMode => _isInTensionMode;

        /// <summary>
        /// 是否处于"钓鱼流程中"：张力阶段，或刚刚（宽限期内）检测到鱼竿状态/张力区。
        /// 用于上层判断"功能是不是正在为钓鱼工作"——因为钓鱼时游戏会隐藏常规 HUD，
        /// 上层原本依赖的"是否在主界面"会失效，不能拿它来决定暂停或图标状态。
        /// </summary>
        public static bool IsInFishingFlow
            => _isInTensionMode
            || (DateTime.Now - _lastFishingSignalAt).TotalMilliseconds < FishingFlowGraceMs;

        /// <summary>
        /// 启动前检测：是否满足启动条件（检测到鱼竿状态）。
        /// 由 IconService 调用，用于决定是否真正启动。
        /// </summary>
        public static bool CanStart()
        {
            var result = DetectFishingRodStatusSync();
            return !string.IsNullOrEmpty(result);
        }

        /// <summary>
        /// 启动钓鱼辅助。调用方（IconService）需先通过 CanStart() 检测。
        /// </summary>
        public static void Start()
        {
            lock (_lockObj)
            {
                if (_isRunning) return;

                // 启动前再检测一次，保证状态一致
                var initialResult = DetectFishingRodStatusSync();
                if (string.IsNullOrEmpty(initialResult))
                {
                    return;
                }

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _isPaused = false;
                _currentStatus = initialResult;
                _isInTensionMode = false;
                _tensionTimerStarted = false;
                _tensionLostCounter = 0;
                _lastFishingSignalAt = DateTime.Now;

                if (initialResult == "上钩了")
                {
                    SimulationService.LeftClick();
                    _isInTensionMode = true;
                    _tensionTimerStarted = true;
                    _tensionStartTime = DateTime.Now;
                    _tensionLostCounter = 0;
                    TensionResultUpdated?.Invoke(-1, -1, -1, "未发现");
                }
            }

            Task.Run(() => ExecuteFishingLoop(_cts!.Token));
        }

        public static void Stop()
        {
            lock (_lockObj)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                    _isRunning = false;
                    _isPaused = false;
                    _currentStatus = "";
                    _isInTensionMode = false;
                    _tensionTimerStarted = false;
                    _tensionLostCounter = 0;
                    _lastFishingSignalAt = DateTime.MinValue;
                }
            }
        }

        public static void Pause()
        {
            lock (_lockObj)
            {
                if (!_isRunning || _isPaused) return;

                _isPaused = true;
                _pauseCts = new CancellationTokenSource();
            }
        }

        public static void Resume()
        {
            lock (_lockObj)
            {
                if (!_isRunning || !_isPaused) return;

                _isPaused = false;
                _pauseCts?.Dispose();
                _pauseCts = null;
            }
        }

        private static async Task ExecuteFishingLoop(CancellationToken token)
        {
            var settings = ConfigManager.Get<FishingAssistSettings>();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();

                    if (_isPaused)
                    {
                        await Task.Delay(100, token);
                        continue;
                    }

                    if (_isInTensionMode)
                    {
                        if (_tensionTimerStarted)
                        {
                            var elapsed = (DateTime.Now - _tensionStartTime).TotalSeconds;
                            if (elapsed >= TENSION_TIMEOUT_SECONDS)
                            {
                                _isInTensionMode = false;
                                _tensionTimerStarted = false;
                                _tensionLostCounter = 0;
                                _currentStatus = "";
                                // 刚结束张力阶段，立刻给"钓鱼流程"续期，让鱼竿状态检测能正常接回
                                _lastFishingSignalAt = DateTime.Now;
                                TensionResultUpdated?.Invoke(-1, -1, -1, "未发现");
                                await Task.Delay(settings.TensionInterval, token);
                                continue;
                            }
                        }

                        var tensionResult = await DetectTensionArea();

                        if (tensionResult.detected)
                        {
                            _tensionLostCounter = 0;
                            _tensionTimerStarted = false;
                            _lastFishingSignalAt = DateTime.Now;
                            TensionResultUpdated?.Invoke(1, tensionResult.elapsedMs, 1, tensionResult.direction);
                        }
                        else
                        {
                            _tensionLostCounter++;

                            if (_tensionLostCounter >= TENSION_LOST_THRESHOLD)
                            {
                                _isInTensionMode = false;
                                _tensionTimerStarted = false;
                                _tensionLostCounter = 0;
                                _currentStatus = "";
                                // 同上：张力区连续丢失后退出张力阶段，给钓鱼流程续期
                                _lastFishingSignalAt = DateTime.Now;
                                TensionResultUpdated?.Invoke(-1, -1, -1, "未发现");
                            }
                            else
                            {
                                TensionResultUpdated?.Invoke(-1, tensionResult.elapsedMs, -1, "未发现");
                            }
                        }

                        await Task.Delay(settings.TensionInterval, token);
                        continue;
                    }

                    var rodResult = await DetectFishingRodStatusWithTime();

                    // 检测到鱼竿状态（未抛钩/已抛钩/上钩了）就算一次钓鱼信号
                    if (!string.IsNullOrEmpty(rodResult.status))
                        _lastFishingSignalAt = DateTime.Now;

                    if (rodResult.status != _currentStatus)
                    {
                        _currentStatus = rodResult.status;

                        if (rodResult.status == "上钩了")
                        {
                            SimulationService.LeftClick();

                            _isInTensionMode = true;
                            _tensionTimerStarted = true;
                            _tensionStartTime = DateTime.Now;
                            _tensionLostCounter = 0;
                            TensionResultUpdated?.Invoke(-1, -1, -1, "未发现");
                        }
                    }

                    DetectionResultUpdated?.Invoke(rodResult.similarity, rodResult.elapsedMs, settings.DetectThreshold, rodResult.status);

                    token.ThrowIfCancellationRequested();

                    int interval = settings.FishingAssistInterval;
                    if (interval > 0)
                    {
                        await Task.Delay(interval, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
            finally
            {
                lock (_lockObj)
                {
                    _isRunning = false;
                    _isPaused = false;
                    _currentStatus = "";
                    _isInTensionMode = false;
                    _tensionTimerStarted = false;
                    _tensionLostCounter = 0;
                    _lastFishingSignalAt = DateTime.MinValue;
                    if (_cts != null)
                    {
                        _cts.Dispose();
                        _cts = null;
                    }
                }
                TensionResultUpdated?.Invoke(-1, -1, -1, "未发现");
            }
        }

        private static async Task<(string status, double similarity, long elapsedMs)> DetectFishingRodStatusWithTime()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return ("", 0, sw.ElapsedMilliseconds);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("钓鱼辅助_鱼竿状态区域");
                if (matchRect == null)
                    return ("", 0, sw.ElapsedMilliseconds);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null)
                    return ("", 0, sw.ElapsedMilliseconds);

                var templateNames = new (string Name, string DisplayName)[]
                {
                    ("钓鱼辅助_未抛钩", "未抛钩"),
                    ("钓鱼辅助_已抛钩", "已抛钩"),
                    ("钓鱼辅助_上钩了", "上钩了")
                };

                double threshold = ConfigManager.Get<FishingAssistSettings>().DetectThreshold;
                double bestSimilarity = 0;
                string bestStatus = "";

                foreach (var templateInfo in templateNames)
                {
                    var template = TemplateManager.GetCachedTemplate(templateInfo.Name);
                    if (template == null) continue;

                    var result = ImageRecognition.MatchTemplate(source, template, threshold);
                    if (result != null && result.Similarity > bestSimilarity)
                    {
                        bestSimilarity = result.Similarity;
                        bestStatus = result.Similarity >= threshold ? templateInfo.DisplayName : "";
                    }
                }

                return (bestStatus, bestSimilarity, sw.ElapsedMilliseconds);
            }
            catch (Exception)
            {
                return ("", 0, sw.ElapsedMilliseconds);
            }
        }

        private static string DetectFishingRodStatusSync()
        {
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return "";

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("钓鱼辅助_鱼竿状态区域");
                if (matchRect == null)
                    return "";

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null)
                    return "";

                var templateNames = new (string Name, string DisplayName)[]
                {
                    ("钓鱼辅助_未抛钩", "未抛钩"),
                    ("钓鱼辅助_已抛钩", "已抛钩"),
                    ("钓鱼辅助_上钩了", "上钩了")
                };

                double threshold = ConfigManager.Get<FishingAssistSettings>().DetectThreshold;

                foreach (var templateInfo in templateNames)
                {
                    var template = TemplateManager.GetCachedTemplate(templateInfo.Name);
                    if (template == null) continue;

                    var result = ImageRecognition.MatchTemplate(source, template, threshold);
                    if (result != null && result.Similarity >= threshold)
                    {
                        return templateInfo.DisplayName;
                    }
                }

                return "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static async Task<(bool detected, long elapsedMs, string direction)> DetectTensionArea()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return (false, sw.ElapsedMilliseconds, "未发现");

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("钓鱼辅助_张力区状态区域");
                if (matchRect == null)
                    return (false, sw.ElapsedMilliseconds, "未发现");

                var settings = ConfigManager.Get<FishingAssistSettings>();

                var rect = matchRect.Rect;
                int x1 = rect.X1;
                int y1 = rect.Y1;
                int width = rect.Width;
                int height = rect.Height;

                var pixels = ImageRecognition.CaptureRegionGDI(x1, y1, width, height);
                if (pixels == null)
                    return (false, sw.ElapsedMilliseconds, "未发现");

                using var source = ImageRecognition.PixelsToBitmap(pixels, width, height);
                using var binarized = ImageRecognition.BinarizeImage(source, TENSION_TARGET_COLOR, settings.TensionTolerance);

                var detection = ImageRecognition.DetectSliderAndTarget(binarized, settings.JudgmentLinePosition);
                if (!detection.Detected)
                    return (false, sw.ElapsedMilliseconds, "未发现");

                int sliderCenterX = x1 + detection.SliderCenterX;
                int judgeLineX = x1 + detection.JudgeLineX;

                string direction = sliderCenterX < judgeLineX ? "向右滑动" : "向左滑动";

                if (sliderCenterX < judgeLineX)
                {
                    SimulationService.LeftClick();
                }

                return (true, sw.ElapsedMilliseconds, direction);
            }
            catch (Exception)
            {
                return (false, sw.ElapsedMilliseconds, "未发现");
            }
        }
    }
}