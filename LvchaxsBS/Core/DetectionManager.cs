using LvchaxsBS.Config;
using LvchaxsBS.Services;
using LvchaxsBS.Services.Hooks;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static LvchaxsBS.Services.CoordinateFormats;

namespace LvchaxsBS.Core
{
    public static class DetectionManager
    {
        public static event Action<bool>? DetectionResultChanged;
        public static event Action<bool>? MapDetectionResultChanged;

        public static event Action<double>? DetectionElapsed;

        private static CancellationTokenSource? _loopCts;
        private static Task? _loopTask;
        private static readonly object _loopLock = new();

        private static bool _isRunning = false;
        private static bool _isLoopRunning = false;
        private static bool _isInMainWindow = false;
        private static bool _isFocused = false;
        private static bool _isInMap = false;
        private static bool _isSuspended = false;

        public static bool IsRunning => _isLoopRunning && _loopTask != null && !_loopTask.IsCompleted;
        public static bool IsInMainWindow => _isInMainWindow;
        public static bool IsFocused => _isFocused;
        public static bool IsInMap => _isInMap;
        public static bool IsSuspended => _isSuspended;

        public static void Initialize()
        {
            if (_isRunning) return;
            _isRunning = true;

            WindowFocusService.FocusChanged += OnFocusChanged;
            IconService.QuickTeleportTriggered += OnQuickTeleportTriggered;
        }

        public static void Shutdown()
        {
            if (!_isRunning) return;
            _isRunning = false;

            StopLoop();

            WindowFocusService.FocusChanged -= OnFocusChanged;
            IconService.QuickTeleportTriggered -= OnQuickTeleportTriggered;

            _isInMainWindow = false;
            _isInMap = false;
            _isFocused = false;
        }

        public static void StartLoop()
        {
            lock (_loopLock)
            {
                if (_loopTask != null && !_loopTask.IsCompleted) return;

                _loopCts = new CancellationTokenSource();

                // 立刻取出 token：Task.Run 的委托可能在 StopLoop 之后才真正执行，
                // 那时若还去读 _loopCts 字段就已经是 null 了（会抛 NRE 让循环静默死掉）。
                var token = _loopCts.Token;

                _isLoopRunning = true;
                _loopTask = Task.Run(() => DetectionLoop(token));
            }
        }

        public static void StopLoop()
        {
            lock (_loopLock)
            {
                if (!_isLoopRunning && _loopTask == null) return;

                _isLoopRunning = false;

                var cts = _loopCts;
                _loopCts = null;
                _loopTask = null;

                // 只取消不 Dispose：循环可能正停在 Task.Delay(_, token) 上，
                // 立刻释放会让它抛 ObjectDisposedException 打断收尾。让 GC 回收即可。
                cts?.Cancel();
            }

            if (_isInMainWindow)
            {
                _isInMainWindow = false;
                DetectionResultChanged?.Invoke(false);
            }
            if (_isInMap)
            {
                _isInMap = false;
                MapDetectionResultChanged?.Invoke(false);
            }
        }

        public static void Suspend()
        {
            _isSuspended = true;
        }

        public static void Resume()
        {
            _isSuspended = false;
        }

        private static void OnFocusChanged(object? sender, bool focused)
        {
            _isFocused = focused;
        }

        private static void OnQuickTeleportTriggered()
        {
            var settings = ConfigManager.Get<HomePageSettings>();

            // 功能总开关关闭 → 快速传送也不执行
            if (!settings.MasterSwitch) return;

            bool isLeftButton = settings.QuickTeleportKey == "左键";

            QuickTeleportLogic.Execute(_isFocused, _isInMainWindow, isLeftButton);
        }

        private static async Task DetectionLoop(CancellationToken token)
        {
            string? currentDetectedState = null;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_isSuspended)
                    {
                        await Task.Delay(200, token);
                        continue;
                    }

                    int interval = ConfigManager.Get<AppSettings>().MainWindowCheckInterval_1;

                    bool mainWindowResult = false;
                    bool mapResult = false;

                    double elapsedMs = 0;

                    if (currentDetectedState == null)
                    {
                        elapsedMs = MeasureDetectBoth(out mainWindowResult, out mapResult);
                    }
                    else if (currentDetectedState == "MainWindow")
                    {
                        elapsedMs = MeasureDetectMainWindow(out mainWindowResult);
                    }
                    else if (currentDetectedState == "Map")
                    {
                        elapsedMs = MeasureDetectMap(out mapResult);
                    }

                    // 主界面判定
                    if (currentDetectedState == null || currentDetectedState == "MainWindow")
                    {
                        if (mainWindowResult)
                        {
                            if (!_isInMainWindow)
                            {
                                _isInMainWindow = true;
                                DetectionResultChanged?.Invoke(true);
                            }
                            if (currentDetectedState != "MainWindow")
                            {
                                currentDetectedState = "MainWindow";
                                if (_isInMap)
                                {
                                    _isInMap = false;
                                    MapDetectionResultChanged?.Invoke(false);
                                }
                            }
                        }
                        else
                        {
                            if (_isInMainWindow)
                            {
                                _isInMainWindow = false;
                                DetectionResultChanged?.Invoke(false);
                            }
                            if (currentDetectedState == "MainWindow")
                            {
                                currentDetectedState = null;
                            }
                        }
                    }

                    // 地图判定
                    if (currentDetectedState == null || currentDetectedState == "Map")
                    {
                        if (mapResult)
                        {
                            if (!_isInMap)
                            {
                                _isInMap = true;
                                MapDetectionResultChanged?.Invoke(true);
                            }
                            if (currentDetectedState != "Map")
                            {
                                currentDetectedState = "Map";
                                if (_isInMainWindow)
                                {
                                    _isInMainWindow = false;
                                    DetectionResultChanged?.Invoke(false);
                                }
                            }
                        }
                        else
                        {
                            if (_isInMap)
                            {
                                _isInMap = false;
                                MapDetectionResultChanged?.Invoke(false);
                            }
                            if (currentDetectedState == "Map")
                            {
                                currentDetectedState = null;
                            }
                        }
                    }

                    if (elapsedMs > 0)
                        DetectionElapsed?.Invoke(elapsedMs);

                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // 出错延时用 None：token 对应的 CTS 可能已被取消/释放，
                    // 继续把它传给 Delay 会让循环直接崩掉（功能会整体失效）。
                    await Task.Delay(1000);
                }
            }
        }

        private static double MeasureDetectBoth(out bool mainWindowResult, out bool mapResult)
        {
            var sw = Stopwatch.StartNew();
            mainWindowResult = DetectMainWindow();
            mapResult = DetectMap();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        private static double MeasureDetectMainWindow(out bool mainWindowResult)
        {
            var sw = Stopwatch.StartNew();
            mainWindowResult = DetectMainWindow();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        private static double MeasureDetectMap(out bool mapResult)
        {
            var sw = Stopwatch.StartNew();
            mapResult = DetectMap();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        private static bool DetectMainWindow()
        {
            if (!CoordinateFormats.CacheReady) return false;

            var detectPoint = CoordinateFormats.GetCached<DetectPoint>("主界面");
            if (detectPoint == null) return false;

            try
            {
                var actualColor = ImageRecognition.GetPixelColor(detectPoint.X, detectPoint.Y);

                foreach (var target in detectPoint.Colors)
                {
                    if (Math.Abs(actualColor.R - target.R) <= detectPoint.Tolerance &&
                        Math.Abs(actualColor.G - target.G) <= detectPoint.Tolerance &&
                        Math.Abs(actualColor.B - target.B) <= detectPoint.Tolerance)
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

        private static bool DetectMap()
        {
            try
            {
                if (!CoordinateFormats.CacheReady) return false;

                var zoomButton = CoordinateFormats.GetCached<DetectPoint>("快速传送_地图缩放按钮");
                if (zoomButton == null) return false;

                var actualColor = ImageRecognition.GetPixelColor(zoomButton.X, zoomButton.Y);

                foreach (var target in zoomButton.Colors)
                {
                    if (Math.Abs(actualColor.R - target.R) <= zoomButton.Tolerance &&
                        Math.Abs(actualColor.G - target.G) <= zoomButton.Tolerance &&
                        Math.Abs(actualColor.B - target.B) <= zoomButton.Tolerance)
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
    }
}