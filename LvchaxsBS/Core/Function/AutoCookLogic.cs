using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;

namespace LvchaxsBS.Core
{
    public static class AutoCookLogic
    {
        private static CancellationTokenSource? _cts = null;
        private static readonly object _lockObj = new object();
        private static bool _isRunning = false;
        private static bool _isPaused = false;
        private static CancellationTokenSource? _pauseCts = null;

        private static bool _isSelectingQuality = false;
        private static DateTime _selectQualityDeadline = DateTime.MinValue;
        private static bool _qualitySelected = false;

        private enum StageF123
        {
            None,
            WaitForAutoManual,
            CookingMonitor,
            ConfirmEnd,
        }

        private enum StageF4
        {
            None,
            WaitForAuto,
            WaitForX99Confirm,
            ClickX99Confirm,
            ConfirmEnd,
        }

        private static StageF123 _stageF123 = StageF123.None;
        private static StageF4 _stageF4 = StageF4.None;
        private static DateTime _stageStartTime = DateTime.MinValue;

        private static DateTime _lastSpaceTime = DateTime.MinValue;
        private const int SPACE_COOLDOWN_MS = 1500;

        private const int STAGE_TIMEOUT_MS = 5000;

        private static int _confirmFailCount = 0;
        private const int CONFIRM_FAIL_MAX = 3;

        private const int QUALITY_SELECT_TIMEOUT_MS = 10000;

        private const int MONITOR_LOOP_INTERVAL_MS = 1;

        private static bool _isCorrectionMode = false;
        private static DateTime _correctionStartTime = DateTime.MinValue;
        private const int CORRECTION_TIMEOUT_MS = 15000;

        private const int X99_CLICK_DELAY_MS = 100;

        private static bool _perfectPixelLocked = false;
        private static int _perfectPixelScreenX = 0;
        private static int _perfectPixelScreenY = 0;
        private static DateTime _perfectPixelDeadline = DateTime.MinValue;
        private const int PERFECT_PIXEL_TIMEOUT_MS = 5000;

        private static bool _normalPixelLocked = false;
        private static int _normalPixelScreenX = 0;
        private static int _normalPixelScreenY = 0;
        private static DateTime _normalPixelDeadline = DateTime.MinValue;
        private const int NORMAL_PIXEL_TIMEOUT_MS = 5000;
        private const int NORMAL_SAMPLE_OFFSET_PERCENT = -10;

        private static readonly CoordinateFormats.RgbColor PERFECT_TARGET_COLOR =
            new CoordinateFormats.RgbColor(255, 192, 64);

        private static double _overlayLastScore = 0;
        private static long _overlayLastElapsed = 0;
        private static string _overlayLastType = "";

        private static int _overlaySuccessCount = 0;
        private static string _overlayQualityName = "";
        private static DispatcherTimer? _overlayCountdownTimer;

        private static int _lastCountdownSec = -1;

        public static event Action<double, long, double, string>? DetectionResultUpdated;
        public static event Action<int, int>? ConfirmTimeoutUpdated;
        public static event Action? QualitySelectWindowOpened;
        public static event Action? Stopped;

        public enum QualityMode
        {
            None,
            Strange,
            Normal,
            Perfect,
            AutoX99,
        }

        private static QualityMode _selectedMode = QualityMode.None;

        public static QualityMode SelectedMode => _selectedMode;
        public static bool IsSelectingQuality => _isSelectingQuality;
        public static int ConfirmTimeoutCount => _confirmFailCount;
        public static int ConfirmFailCount => _confirmFailCount;

        public static bool IsRunning => _isRunning;
        public static bool IsPaused => _isPaused;

        public static bool CanStart()
        {
            return DetectCookInterfaceSync().detected;
        }

        public static void Start()
        {
            lock (_lockObj)
            {
                if (_isRunning) return;

                var initialResult = DetectCookInterfaceSync();
                if (!initialResult.detected)
                {
                    Debug.WriteLine("【自动烹饪】启动检测未检测到烹饪界面，不启动");
                    return;
                }

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _isPaused = false;
                _isSelectingQuality = true;
                _qualitySelected = false;
                _selectedMode = QualityMode.None;
                _confirmFailCount = 0;
                _stageF123 = StageF123.None;
                _stageF4 = StageF4.None;
                _isCorrectionMode = false;
                _selectQualityDeadline = DateTime.Now.AddMilliseconds(QUALITY_SELECT_TIMEOUT_MS);
                _lastCountdownSec = -1;
                ClearPixelLock();
            }

            _overlaySuccessCount = 0;
            _overlayQualityName = "";

            ConfirmTimeoutUpdated?.Invoke(_confirmFailCount, CONFIRM_FAIL_MAX);
            QualitySelectWindowOpened?.Invoke();

            IconService.ShowSubtitle(SubtitleOverlayService.SubtitleOwner.AutoCook);
            SubtitleOverlayService.SetSubtitle(SubtitleTexts.AutoCook.Preparing);

            var autoR = DetectAutoCook().GetAwaiter().GetResult();
            var manualR = DetectManualCook().GetAwaiter().GetResult();
            if (autoR.detected)
                _overlayLastType = "自动烹饪+手动烹饪";
            else if (manualR.detected)
                _overlayLastType = "手动烹饪";
            else
                _overlayLastType = "";

            StartCountdownTimer();

            Task.Run(() => ExecuteCookLoop(_cts!.Token));
        }

        public static bool SelectQuality(QualityMode mode)
        {
            lock (_lockObj)
            {
                if (!_isRunning || !_isSelectingQuality) return false;
                if (_qualitySelected) return false;

                if (mode == QualityMode.AutoX99)
                {
                    var autoResult = DetectAutoCook().GetAwaiter().GetResult();
                    if (!autoResult.detected)
                    {
                        return false;
                    }
                }

                _qualitySelected = true;
                _selectedMode = mode;
                _isSelectingQuality = false;

                _overlayQualityName = GetQualityName(mode);

                return true;
            }
        }

        public static void Stop()
        {
            lock (_lockObj)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
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

        private static void ClearPixelLock()
        {
            _lastSpaceTime = DateTime.MinValue;

            _perfectPixelLocked = false;
            _perfectPixelScreenX = 0;
            _perfectPixelScreenY = 0;
            _perfectPixelDeadline = DateTime.MinValue;

            _normalPixelLocked = false;
            _normalPixelScreenX = 0;
            _normalPixelScreenY = 0;
            _normalPixelDeadline = DateTime.MinValue;
        }

        private static void OnConfirmSuccess()
        {
            if (_confirmFailCount > 0)
            {
                _confirmFailCount--;
            }

            _overlaySuccessCount++;

            ConfirmTimeoutUpdated?.Invoke(_confirmFailCount, CONFIRM_FAIL_MAX);
            UpdateRunStatusDisplay();
        }

        private static bool OnConfirmFail()
        {
            _confirmFailCount++;
            ConfirmTimeoutUpdated?.Invoke(_confirmFailCount, CONFIRM_FAIL_MAX);

            if (_confirmFailCount >= CONFIRM_FAIL_MAX)
            {
                _confirmFailCount = 0;
                ConfirmTimeoutUpdated?.Invoke(_confirmFailCount, CONFIRM_FAIL_MAX);
                Stop();
                return true;
            }

            UpdateRunStatusDisplay();
            return false;
        }

        private static void NotifyDetection(double score, long elapsed, double threshold, string type)
        {
            DetectionResultUpdated?.Invoke(score, elapsed, threshold, type);
            SyncToSubtitle(score, elapsed, type);
        }

        private static void SyncToSubtitle(double matchScore, long elapsedMs, string type)
        {
            _overlayLastScore = matchScore;
            _overlayLastElapsed = elapsedMs;
            _overlayLastType = type;

            if (_isSelectingQuality)
            {
                UpdateCountdownDisplay();
                return;
            }

            if (!string.IsNullOrEmpty(_overlayQualityName))
            {
                UpdateRunStatusDisplay();
            }
        }

        private static string GetQualityName(QualityMode mode)
        {
            return mode switch
            {
                QualityMode.Strange => SubtitleTexts.AutoCook.QualityStrange,
                QualityMode.Normal => SubtitleTexts.AutoCook.QualityNormal,
                QualityMode.Perfect => SubtitleTexts.AutoCook.QualityPerfect,
                QualityMode.AutoX99 => SubtitleTexts.AutoCook.QualityAutoX99,
                _ => SubtitleTexts.AutoCook.QualityUnknown
            };
        }

        private static void StartCountdownTimer()
        {
            StopCountdownTimer();

            _overlayCountdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _overlayCountdownTimer.Tick += (s, e) => UpdateCountdownDisplay();
            _overlayCountdownTimer.Start();

            UpdateCountdownDisplay();
        }

        private static void StopCountdownTimer()
        {
            if (_overlayCountdownTimer != null)
            {
                _overlayCountdownTimer.Stop();
                _overlayCountdownTimer = null;
            }
            _lastCountdownSec = -1;
        }

        private static void UpdateCountdownDisplay()
        {
            if (!_isSelectingQuality)
            {
                StopCountdownTimer();
                return;
            }

            int remainingSec = (int)Math.Ceiling((_selectQualityDeadline - DateTime.Now).TotalSeconds);
            if (remainingSec < 0) remainingSec = 0;

            if (remainingSec == _lastCountdownSec) return;
            _lastCountdownSec = remainingSec;

            bool isAuto = _overlayLastType.Contains("自动");

            string optionText = isAuto
                ? SubtitleTexts.AutoCook.SelectOptionsWithAuto
                : SubtitleTexts.AutoCook.SelectOptionsManual;

            SubtitleOverlayService.SetSubtitle($"{optionText} {remainingSec}{SubtitleTexts.AutoCook.SelectCountdownSuffix}");
        }

        private static void UpdateRunStatusDisplay()
        {
            string qualityText = string.IsNullOrEmpty(_overlayQualityName) ? SubtitleTexts.AutoCook.QualityUnknown : _overlayQualityName;
            SubtitleOverlayService.SetSubtitle(string.Format(
                SubtitleTexts.AutoCook.RunStatusFormat,
                qualityText,
                _overlaySuccessCount,
                _confirmFailCount,
                CONFIRM_FAIL_MAX));
        }

        private static async Task ExecuteCookLoop(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

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

                    if (_isSelectingQuality)
                    {
                        if (DateTime.Now >= _selectQualityDeadline)
                        {
                            Stop();
                            return;
                        }

                        var autoR = await DetectAutoCook();
                        var manualR = await DetectManualCook();

                        string type;
                        double bestScore;
                        if (autoR.detected)
                        {
                            type = "自动烹饪+手动烹饪";
                            bestScore = autoR.similarity;
                        }
                        else if (manualR.detected)
                        {
                            type = "手动烹饪";
                            bestScore = manualR.similarity;
                        }
                        else
                        {
                            type = "未检测到烹饪";
                            bestScore = 0;
                        }

                        NotifyDetection(bestScore, 0, settings.DetectThreshold, type);
                        await Task.Delay(settings.AutoCookInterval, token);
                        continue;
                    }

                    if (_stageF123 == StageF123.None && _stageF4 == StageF4.None)
                    {
                        if (_selectedMode == QualityMode.AutoX99)
                        {
                            _stageF4 = StageF4.WaitForAuto;
                            _stageStartTime = DateTime.MinValue;
                        }
                        else
                        {
                            _stageF123 = StageF123.WaitForAutoManual;
                            _stageStartTime = DateTime.MinValue;
                        }
                    }

                    if (_isCorrectionMode)
                    {
                        if ((DateTime.Now - _correctionStartTime).TotalMilliseconds >= CORRECTION_TIMEOUT_MS)
                        {
                            _isCorrectionMode = false;
                            Stop();
                            return;
                        }

                        bool found = await RunCorrectionDetect(token);
                        if (found)
                        {
                            _isCorrectionMode = false;
                        }

                        await Task.Delay(MONITOR_LOOP_INTERVAL_MS, token);
                        continue;
                    }

                    if (_selectedMode == QualityMode.AutoX99)
                    {
                        switch (_stageF4)
                        {
                            case StageF4.WaitForAuto:
                                await RunF4WaitForAuto(token);
                                break;
                            case StageF4.WaitForX99Confirm:
                                await RunF4WaitForX99Confirm(token);
                                break;
                            case StageF4.ClickX99Confirm:
                                await RunF4ClickX99Confirm(token);
                                break;
                            case StageF4.ConfirmEnd:
                                await RunF4ConfirmEnd(token);
                                break;
                        }
                    }
                    else
                    {
                        switch (_stageF123)
                        {
                            case StageF123.WaitForAutoManual:
                                await RunF123WaitForAutoManual(token);
                                break;
                            case StageF123.CookingMonitor:
                                await RunF123CookingMonitor(token);
                                break;
                            case StageF123.ConfirmEnd:
                                await RunF123ConfirmEnd(token);
                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
            finally
            {
                lock (_lockObj)
                {
                    _isRunning = false;
                    _isPaused = false;
                    _isSelectingQuality = false;
                    _qualitySelected = false;
                    _selectedMode = QualityMode.None;
                    _confirmFailCount = 0;
                    _stageF123 = StageF123.None;
                    _stageF4 = StageF4.None;
                    _isCorrectionMode = false;
                    if (_cts != null)
                    {
                        _cts.Dispose();
                        _cts = null;
                    }
                }
                ClearPixelLock();

                DetectionManager.Resume();

                ConfirmTimeoutUpdated?.Invoke(_confirmFailCount, CONFIRM_FAIL_MAX);

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    StopCountdownTimer();
                    SubtitleOverlayService.HideSubtitle();
                }), DispatcherPriority.Normal);

                Stopped?.Invoke();
            }
        }

        private static async Task RunF123WaitForAutoManual(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            if (_stageStartTime == DateTime.MinValue)
            {
                _stageStartTime = DateTime.Now;
            }

            var autoR = await DetectAutoCook();
            var manualR = await DetectManualCook();

            if (autoR.detected || manualR.detected)
            {
                string type = autoR.detected ? "自动烹饪+手动烹饪" : "手动烹饪";
                double bestScore = autoR.detected ? autoR.similarity : manualR.similarity;

                try { SimulationService.PressF(); }
                catch { }

                _stageF123 = StageF123.CookingMonitor;
                _stageStartTime = DateTime.MinValue;
                NotifyDetection(bestScore, 0, settings.DetectThreshold, type);
                return;
            }

            NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");

            if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
            {
                _stageStartTime = DateTime.MinValue;
                _isCorrectionMode = true;
                _correctionStartTime = DateTime.Now;
                return;
            }

            await Task.Delay(settings.AutoCookInterval, token);
        }

        private static async Task RunF123CookingMonitor(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            if (_stageStartTime == DateTime.MinValue)
            {
                _stageStartTime = DateTime.Now;
            }

            if (_selectedMode == QualityMode.Perfect)
            {
                if (_perfectPixelLocked)
                {
                    DetectionManager.Suspend();

                    bool triggered = CheckPerfectJudgePixelOnly();
                    if (triggered)
                    {
                        DetectionManager.Resume();

                        _stageF123 = StageF123.ConfirmEnd;
                        _stageStartTime = DateTime.MinValue;
                    }
                    await Task.Delay(1, token);
                    return;
                }

                bool locked = TryLockPerfectJudgePixel();
                if (locked)
                {
                    await Task.Yield();
                    return;
                }

                if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
                {
                    DetectionManager.Resume();

                    _stageStartTime = DateTime.MinValue;
                    _isCorrectionMode = true;
                    _correctionStartTime = DateTime.Now;
                    return;
                }

                await Task.Delay(MONITOR_LOOP_INTERVAL_MS, token);
                return;
            }

            if (_selectedMode == QualityMode.Normal)
            {
                if (_normalPixelLocked)
                {
                    DetectionManager.Suspend();

                    bool triggered = CheckNormalJudgePixelOnly();
                    if (triggered)
                    {
                        DetectionManager.Resume();

                        _stageF123 = StageF123.ConfirmEnd;
                        _stageStartTime = DateTime.MinValue;
                    }
                    await Task.Delay(1, token);
                    return;
                }

                bool locked = TryLockNormalJudgePixel();
                if (locked)
                {
                    await Task.Yield();
                    return;
                }

                if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
                {
                    DetectionManager.Resume();

                    _stageStartTime = DateTime.MinValue;
                    _isCorrectionMode = true;
                    _correctionStartTime = DateTime.Now;
                    return;
                }

                await Task.Delay(MONITOR_LOOP_INTERVAL_MS, token);
                return;
            }

            var cookingResult = await DetectCooking();
            bool cookingDetected = cookingResult.similarity >= settings.DetectThreshold;

            if (cookingDetected)
            {
                if (_selectedMode == QualityMode.Strange)
                {
                    try { SimulationService.PressSpace(); }
                    catch { }

                    _stageF123 = StageF123.ConfirmEnd;
                    _stageStartTime = DateTime.MinValue;
                    NotifyDetection(cookingResult.similarity, 0, settings.DetectThreshold, "正在烹饪");
                    return;
                }
            }
            else
            {
                NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");
            }

            if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
            {
                _stageStartTime = DateTime.MinValue;
                _isCorrectionMode = true;
                _correctionStartTime = DateTime.Now;
                return;
            }

            await Task.Delay(MONITOR_LOOP_INTERVAL_MS, token);
        }

        private static async Task RunF123ConfirmEnd(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            if (_stageStartTime == DateTime.MinValue)
            {
                _stageStartTime = DateTime.Now;
            }

            var (detected, similarity, clickX, clickY) = await DetectConfirm("自动烹饪_确认区域");

            if (detected)
            {
                try { SimulationService.LeftClickAt(clickX, clickY); }
                catch { }

                OnConfirmSuccess();

                _stageF123 = StageF123.WaitForAutoManual;
                _stageStartTime = DateTime.MinValue;
                ClearPixelLock();
                NotifyDetection(similarity, 0, settings.DetectThreshold, "确认结束");
                return;
            }

            NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");

            if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
            {
                _stageStartTime = DateTime.MinValue;

                bool ended = OnConfirmFail();
                if (ended) return;

                _isCorrectionMode = true;
                _correctionStartTime = DateTime.Now;
                return;
            }

            await Task.Delay(settings.AutoCookInterval, token);
        }

        private static bool TryLockPerfectJudgePixel()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("自动烹饪_品质控制区域");
                if (matchRect == null) return false;

                var rect = matchRect.Rect;

                var pixels = ImageRecognition.CaptureRegionGDI(rect.X1, rect.Y1, rect.Width, rect.Height);
                if (pixels == null) return false;

                using var source = ImageRecognition.PixelsToBitmap(pixels, rect.Width, rect.Height);

                int perfectTolerance = ConfigManager.Get<AutoCookSettings>().PerfectBinarizeTolerance;
                if (perfectTolerance < 0) perfectTolerance = 0;
                if (perfectTolerance > 255) perfectTolerance = 255;

                using var binarized = ImageRecognition.BinarizeImage(source, PERFECT_TARGET_COLOR, perfectTolerance);

                var (found, leftXLocal, leftYLocal, rightXLocal, rightYLocal, topLocal, bottomLocal) =
                    ImageRecognition.GetWhiteHorizontalBounds(binarized);

                if (!found || rightXLocal <= leftXLocal) return false;

                int percent = ConfigManager.Get<AutoCookSettings>().PerfectJudgePercent;
                if (percent < -100) percent = -100;
                if (percent > 100) percent = 100;

                int judgeXLocal = leftXLocal + (int)Math.Round((rightXLocal - leftXLocal) * (percent / 100.0));
                int judgeYLocal = (topLocal + bottomLocal) / 2;

                SavePerfectRegionDebugImage(source, binarized, leftXLocal, rightXLocal, topLocal, bottomLocal, judgeXLocal);

                int judgeScreenX = rect.X1 + judgeXLocal;
                int judgeScreenY = rect.Y1 + judgeYLocal;

                _perfectPixelScreenX = judgeScreenX;
                _perfectPixelScreenY = judgeScreenY;
                _perfectPixelLocked = true;
                _perfectPixelDeadline = DateTime.Now.AddMilliseconds(PERFECT_PIXEL_TIMEOUT_MS);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryLockNormalJudgePixel()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("自动烹饪_品质控制区域");
                if (matchRect == null) return false;

                var rect = matchRect.Rect;

                var pixels = ImageRecognition.CaptureRegionGDI(rect.X1, rect.Y1, rect.Width, rect.Height);
                if (pixels == null) return false;

                using var source = ImageRecognition.PixelsToBitmap(pixels, rect.Width, rect.Height);

                int perfectTolerance = ConfigManager.Get<AutoCookSettings>().PerfectBinarizeTolerance;
                if (perfectTolerance < 0) perfectTolerance = 0;
                if (perfectTolerance > 255) perfectTolerance = 255;

                using var binarizedPerfect = ImageRecognition.BinarizeImage(source, PERFECT_TARGET_COLOR, perfectTolerance);

                var (foundPerfect, pLeftX, pLeftY, pRightX, pRightY, pTop, pBottom) =
                    ImageRecognition.GetWhiteHorizontalBounds(binarizedPerfect);

                if (!foundPerfect || pRightX <= pLeftX) return false;

                int sampleX = pLeftX + (int)Math.Round((pRightX - pLeftX) * (NORMAL_SAMPLE_OFFSET_PERCENT / 100.0));
                int sampleY = (pTop + pBottom) / 2;

                if (sampleX < 0 || sampleX >= rect.Width) return false;
                if (sampleY < 0 || sampleY >= rect.Height) return false;

                int sampleIdx = (sampleY * rect.Width + sampleX) * 4;
                byte sb = pixels[sampleIdx];
                byte sg = pixels[sampleIdx + 1];
                byte sr = pixels[sampleIdx + 2];

                var sampleColor = new CoordinateFormats.RgbColor(sr, sg, sb);

                int normalTolerance = ConfigManager.Get<AutoCookSettings>().NormalBinarizeTolerance;
                if (normalTolerance < 0) normalTolerance = 0;
                if (normalTolerance > 255) normalTolerance = 255;

                using var binarizedNormal = ImageRecognition.BinarizeImage(source, sampleColor, normalTolerance);

                var leftBlob = ImageRecognition.FindWhiteBlobAtPoint(binarizedNormal, sampleX, sampleY);
                if (leftBlob == null || leftBlob.PixelCount < 20) return false;

                int percent = ConfigManager.Get<AutoCookSettings>().NormalJudgePercent;
                if (percent < -100) percent = -100;
                if (percent > 100) percent = 100;

                int judgeXLocal = leftBlob.MinX + (int)Math.Round((leftBlob.MaxX - leftBlob.MinX) * (percent / 100.0));
                int judgeYLocal = (leftBlob.MinY + leftBlob.MaxY) / 2;

                SaveNormalRegionDebugImage(source, binarizedNormal, leftBlob.MinX, leftBlob.MaxX, leftBlob.MinY, leftBlob.MaxY, judgeXLocal);

                int judgeScreenX = rect.X1 + judgeXLocal;
                int judgeScreenY = rect.Y1 + judgeYLocal;

                _normalPixelScreenX = judgeScreenX;
                _normalPixelScreenY = judgeScreenY;
                _normalPixelLocked = true;
                _normalPixelDeadline = DateTime.Now.AddMilliseconds(NORMAL_PIXEL_TIMEOUT_MS);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckPerfectJudgePixelOnly()
        {
            try
            {
                if ((DateTime.Now - _perfectPixelDeadline).TotalMilliseconds >= 0)
                {
                    _perfectPixelLocked = false;
                    DetectionManager.Resume();
                    return false;
                }

                var pixels = ImageRecognition.CaptureRegionGDI(
                    _perfectPixelScreenX, _perfectPixelScreenY, 1, 1);

                if (pixels == null)
                {
                    return false;
                }

                byte b = pixels[0];
                byte g = pixels[1];
                byte r = pixels[2];

                bool isWhite = (r >= 250 && g >= 250 && b >= 250);

                if (isWhite)
                {
                    var now = DateTime.Now;
                    if ((now - _lastSpaceTime).TotalMilliseconds >= SPACE_COOLDOWN_MS)
                    {
                        _lastSpaceTime = now;
                        try { SimulationService.PressSpace(); }
                        catch { }
                        _perfectPixelLocked = false;
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

        private static bool CheckNormalJudgePixelOnly()
        {
            try
            {
                if ((DateTime.Now - _normalPixelDeadline).TotalMilliseconds >= 0)
                {
                    _normalPixelLocked = false;
                    DetectionManager.Resume();
                    return false;
                }

                var pixels = ImageRecognition.CaptureRegionGDI(
                    _normalPixelScreenX, _normalPixelScreenY, 1, 1);

                if (pixels == null)
                {
                    return false;
                }

                byte b = pixels[0];
                byte g = pixels[1];
                byte r = pixels[2];

                bool isWhite = (r >= 250 && g >= 250 && b >= 250);

                if (isWhite)
                {
                    var now = DateTime.Now;
                    if ((now - _lastSpaceTime).TotalMilliseconds >= SPACE_COOLDOWN_MS)
                    {
                        _lastSpaceTime = now;
                        try { SimulationService.PressSpace(); }
                        catch { }
                        _normalPixelLocked = false;
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

        private static string? SavePerfectRegionDebugImage(Bitmap source, Bitmap binarized, int leftX, int rightX, int top, int bottom, int judgeX)
        {
            try
            {
                int width = binarized.Width;
                int height = binarized.Height;
                int totalHeight = height * 2 + 1;

                using var annotated = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(annotated))
                {
                    g.DrawImage(source, 0, 0, width, height);
                    g.DrawImage(binarized, 0, height + 1, width, height);

                    using var pen = new Pen(Color.Red, 1);

                    int rectW = rightX - leftX + 1;
                    int rectH = bottom - top + 1;
                    if (rectW < 1) rectW = 1;
                    if (rectH < 1) rectH = 1;

                    int offsetY = height + 1;
                    g.DrawRectangle(pen, leftX, offsetY + top, rectW, rectH);

                    int lineX = judgeX;
                    if (lineX < 0) lineX = 0;
                    if (lineX >= width) lineX = width - 1;
                    g.DrawLine(pen, lineX, offsetY, lineX, offsetY + height - 1);
                }

                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "截图日志", "自动烹饪-截图日志");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string fileName = "自动烹饪-完美品质.png";
                string path = Path.Combine(dir, fileName);

                annotated.Save(path, ImageFormat.Png);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string? SaveNormalRegionDebugImage(Bitmap source, Bitmap binarized, int leftX, int rightX, int top, int bottom, int judgeX)
        {
            try
            {
                int width = binarized.Width;
                int height = binarized.Height;
                int totalHeight = height * 2 + 1;

                using var annotated = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(annotated))
                {
                    g.DrawImage(source, 0, 0, width, height);
                    g.DrawImage(binarized, 0, height + 1, width, height);

                    using var pen = new Pen(Color.Red, 1);

                    int rectW = rightX - leftX + 1;
                    int rectH = bottom - top + 1;
                    if (rectW < 1) rectW = 1;
                    if (rectH < 1) rectH = 1;

                    int offsetY = height + 1;
                    g.DrawRectangle(pen, leftX, offsetY + top, rectW, rectH);

                    int lineX = judgeX;
                    if (lineX < 0) lineX = 0;
                    if (lineX >= width) lineX = width - 1;
                    g.DrawLine(pen, lineX, offsetY, lineX, offsetY + height - 1);
                }

                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "截图日志", "自动烹饪-截图日志");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string fileName = "自动烹饪-一般品质.png";
                string path = Path.Combine(dir, fileName);

                annotated.Save(path, ImageFormat.Png);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static async Task RunF4WaitForAuto(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            if (_stageStartTime == DateTime.MinValue)
            {
                _stageStartTime = DateTime.Now;
            }

            var autoR = await DetectAutoCook();

            if (autoR.detected)
            {
                try { SimulationService.PressR(); }
                catch { }

                _stageF4 = StageF4.WaitForX99Confirm;
                _stageStartTime = DateTime.MinValue;
                NotifyDetection(autoR.similarity, 0, settings.DetectThreshold, "自动烹饪+手动烹饪");
                return;
            }

            NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");

            if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
            {
                _stageStartTime = DateTime.MinValue;
                _isCorrectionMode = true;
                _correctionStartTime = DateTime.Now;
                return;
            }

            await Task.Delay(settings.AutoCookInterval, token);
        }

        private static async Task RunF4WaitForX99Confirm(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            if (_stageStartTime == DateTime.MinValue)
            {
                _stageStartTime = DateTime.Now;
            }

            var (detected, similarity, _, _) = await DetectConfirm("自动烹饪_自动烹饪X99确认区域");

            if (detected)
            {
                NotifyDetection(similarity, 0, settings.DetectThreshold, "确认结束");

                await Task.Delay(X99_CLICK_DELAY_MS, token);

                try
                {
                    var clickPosObj = CoordinateFormats.GetCached<object>("自动烹饪_X99数量点击坐标");
                    if (clickPosObj != null)
                    {
                        var clickPos = (CoordinateFormats.Point)clickPosObj;
                        SimulationService.LeftClickAt(clickPos.X, clickPos.Y);
                    }
                }
                catch { }

                _stageF4 = StageF4.ClickX99Confirm;
                _stageStartTime = DateTime.MinValue;
                return;
            }

            NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");

            if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
            {
                _stageStartTime = DateTime.MinValue;
                _isCorrectionMode = true;
                _correctionStartTime = DateTime.Now;
                return;
            }

            await Task.Delay(MONITOR_LOOP_INTERVAL_MS, token);
        }

        private static async Task RunF4ClickX99Confirm(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            if (_stageStartTime == DateTime.MinValue)
            {
                _stageStartTime = DateTime.Now;
            }

            var (detected, similarity, clickX, clickY) = await DetectConfirm("自动烹饪_自动烹饪X99确认区域");

            if (detected)
            {
                NotifyDetection(similarity, 0, settings.DetectThreshold, "确认结束");

                await Task.Delay(X99_CLICK_DELAY_MS, token);

                try
                {
                    SimulationService.LeftClickAt(clickX, clickY);
                }
                catch { }

                _stageF4 = StageF4.ConfirmEnd;
                _stageStartTime = DateTime.MinValue;
                return;
            }

            NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");

            if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
            {
                _stageStartTime = DateTime.MinValue;
                _isCorrectionMode = true;
                _correctionStartTime = DateTime.Now;
                return;
            }

            await Task.Delay(MONITOR_LOOP_INTERVAL_MS, token);
        }

        private static async Task RunF4ConfirmEnd(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            if (_stageStartTime == DateTime.MinValue)
            {
                _stageStartTime = DateTime.Now;
            }

            var (detected, similarity, clickX, clickY) = await DetectConfirm("自动烹饪_确认区域");

            if (detected)
            {
                try { SimulationService.LeftClickAt(clickX, clickY); }
                catch { }

                OnConfirmSuccess();

                _stageF4 = StageF4.WaitForAuto;
                _stageStartTime = DateTime.MinValue;
                ClearPixelLock();
                NotifyDetection(similarity, 0, settings.DetectThreshold, "确认结束");
                return;
            }

            NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");

            if ((DateTime.Now - _stageStartTime).TotalMilliseconds >= STAGE_TIMEOUT_MS)
            {
                _stageStartTime = DateTime.MinValue;

                bool ended = OnConfirmFail();
                if (ended) return;

                _isCorrectionMode = true;
                _correctionStartTime = DateTime.Now;
                return;
            }

            await Task.Delay(settings.AutoCookInterval, token);
        }

        private static async Task<bool> RunCorrectionDetect(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoCookSettings>();

            if (_selectedMode == QualityMode.AutoX99)
            {
                var autoR = await DetectAutoCook();
                if (autoR.detected)
                {
                    _stageF4 = StageF4.WaitForAuto;
                    _stageStartTime = DateTime.MinValue;
                    NotifyDetection(autoR.similarity, 0, settings.DetectThreshold, "自动烹饪+手动烹饪");
                    return true;
                }

                var (x99Detected, x99Sim, _, _) = await DetectConfirm("自动烹饪_自动烹饪X99确认区域");
                if (x99Detected)
                {
                    _stageF4 = StageF4.ClickX99Confirm;
                    _stageStartTime = DateTime.MinValue;
                    NotifyDetection(x99Sim, 0, settings.DetectThreshold, "确认结束");
                    return true;
                }

                var (confirmDetected, confirmSim, clickX, clickY) = await DetectConfirm("自动烹饪_确认区域");
                if (confirmDetected)
                {
                    try { SimulationService.LeftClickAt(clickX, clickY); }
                    catch { }

                    OnConfirmSuccess();

                    _stageF4 = StageF4.WaitForAuto;
                    _stageStartTime = DateTime.MinValue;
                    NotifyDetection(confirmSim, 0, settings.DetectThreshold, "确认结束");
                    return true;
                }

                NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");
                return false;
            }
            else
            {
                var autoR = await DetectAutoCook();
                if (autoR.detected)
                {
                    _stageF123 = StageF123.WaitForAutoManual;
                    _stageStartTime = DateTime.MinValue;
                    NotifyDetection(autoR.similarity, 0, settings.DetectThreshold, "自动烹饪+手动烹饪");
                    return true;
                }

                var manualR = await DetectManualCook();
                if (manualR.detected)
                {
                    _stageF123 = StageF123.WaitForAutoManual;
                    _stageStartTime = DateTime.MinValue;
                    NotifyDetection(manualR.similarity, 0, settings.DetectThreshold, "手动烹饪");
                    return true;
                }

                var cookingR = await DetectCooking();
                if (cookingR.detected)
                {
                    _stageF123 = StageF123.CookingMonitor;
                    _stageStartTime = DateTime.MinValue;
                    NotifyDetection(cookingR.similarity, 0, settings.DetectThreshold, "正在烹饪");
                    return true;
                }

                var (confirmDetected, confirmSim, clickX, clickY) = await DetectConfirm("自动烹饪_确认区域");
                if (confirmDetected)
                {
                    try { SimulationService.LeftClickAt(clickX, clickY); }
                    catch { }

                    OnConfirmSuccess();

                    _stageF123 = StageF123.WaitForAutoManual;
                    _stageStartTime = DateTime.MinValue;
                    NotifyDetection(confirmSim, 0, settings.DetectThreshold, "确认结束");
                    return true;
                }

                NotifyDetection(0, 0, settings.DetectThreshold, "未检测到烹饪");
                return false;
            }
        }

        private static async Task<(bool detected, double similarity, int x, int y)> DetectConfirm(string regionKey)
        {
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return (false, 0, 0, 0);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(regionKey);
                if (matchRect == null) return (false, 0, 0, 0);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return (false, 0, 0, 0);

                var template = TemplateManager.GetCachedTemplate("自动烹饪_确认");
                if (template == null) return (false, 0, 0, 0);

                double threshold = ConfigManager.Get<AutoCookSettings>().DetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                if (result != null && result.Similarity >= threshold)
                {
                    double capScale = ImageRecognition.GetCaptureScale();
                    int clickX = matchRect.Rect.X1 + (int)((result.X + template.Width / 2.0) / capScale);
                    int clickY = matchRect.Rect.Y1 + (int)((result.Y + template.Height / 2.0) / capScale);
                    return (true, result.Similarity, clickX, clickY);
                }

                return (false, 0, 0, 0);
            }
            catch
            {
                return (false, 0, 0, 0);
            }
        }

        private static (bool detected, double similarity, long elapsedMs) DetectCookInterfaceSync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return (false, 0, sw.ElapsedMilliseconds);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("自动烹饪_烹饪界面区域");
                if (matchRect == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                var template = TemplateManager.GetCachedTemplate("自动烹饪_烹饪界面");
                if (template == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                double threshold = ConfigManager.Get<AutoCookSettings>().DetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                return (result != null && result.Similarity >= threshold, result?.Similarity ?? 0, sw.ElapsedMilliseconds);
            }
            catch
            {
                return (false, 0, sw.ElapsedMilliseconds);
            }
        }

        private static async Task<(bool detected, double similarity)> DetectAutoCook()
        {
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return (false, 0);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("自动烹饪_自动烹饪区域");
                if (matchRect == null) return (false, 0);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return (false, 0);

                var template = TemplateManager.GetCachedTemplate("自动烹饪_自动烹饪");
                if (template == null) return (false, 0);

                double threshold = ConfigManager.Get<AutoCookSettings>().DetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                bool detected = result != null && result.Similarity >= threshold;
                return (detected, result?.Similarity ?? 0);
            }
            catch
            {
                return (false, 0);
            }
        }

        private static async Task<(bool detected, double similarity)> DetectManualCook()
        {
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return (false, 0);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("自动烹饪_手动烹饪区域");
                if (matchRect == null) return (false, 0);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return (false, 0);

                var template = TemplateManager.GetCachedTemplate("自动烹饪_手动烹饪");
                if (template == null) return (false, 0);

                double threshold = ConfigManager.Get<AutoCookSettings>().DetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                bool detected = result != null && result.Similarity >= threshold;
                return (detected, result?.Similarity ?? 0);
            }
            catch
            {
                return (false, 0);
            }
        }

        private static async Task<(bool detected, double similarity)> DetectCooking()
        {
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return (false, 0);

                var colorRect = CoordinateFormats.GetCached<CoordinateFormats.ColorRect>("自动烹饪_正在烹饪区域");
                if (colorRect == null) return (false, 0);

                int x1 = colorRect.Rect.X1;
                int y1 = colorRect.Rect.Y1;
                int width = colorRect.Rect.Width;
                int height = colorRect.Rect.Height;

                if (width <= 0 || height <= 0) return (false, 0);

                var pixels = ImageRecognition.CaptureRegionGDI(x1, y1, width, height);
                if (pixels == null) return (false, 0);

                int totalPixels = width * height;
                int matchedPixels = 0;

                var targetColors = colorRect.Colors;
                int tolerance = colorRect.Tolerance;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (y * width + x) * 4;
                        byte b = pixels[idx];
                        byte g = pixels[idx + 1];
                        byte r = pixels[idx + 2];

                        foreach (var target in targetColors)
                        {
                            if (Math.Abs(r - target.R) <= tolerance &&
                                Math.Abs(g - target.G) <= tolerance &&
                                Math.Abs(b - target.B) <= tolerance)
                            {
                                matchedPixels++;
                                break;
                            }
                        }
                    }
                }

                double similarity = totalPixels > 0 ? (double)matchedPixels / totalPixels : 0;
                double threshold = ConfigManager.Get<AutoCookSettings>().DetectThreshold;
                bool detected = similarity >= threshold;

                return (detected, similarity);
            }
            catch
            {
                return (false, 0);
            }
        }
    }
}