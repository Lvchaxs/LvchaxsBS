using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LvchaxsBS.Core
{
    public static class StoryDialogueLogic
    {
        private static CancellationTokenSource? _cts = null;
        private static readonly object _lockObj = new object();
        private static bool _isRunning = false;
        private static bool _isPaused = false;
        private static bool _isDetected = false;
        private static CancellationTokenSource? _pauseCts = null;

        // UI 更新事件
        public static event Action<double, long, double, string>? DetectionResultUpdated;
        public static event Action<bool>? DetectionStateChanged;

        public static bool IsRunning => _isRunning;
        public static bool IsPaused => _isPaused;
        public static bool IsDetected => _isDetected;

        /// <summary>
        /// 启动前检测：是否检测到隐藏按钮。由 IconService 调用。
        /// </summary>
        public static bool CanStart()
        {
            return DetectHiddenButtonSync().detected;
        }

        /// <summary>
        /// 启动剧情对话。启动决策由 IconService 负责。
        /// </summary>
        public static void Start()
        {
            lock (_lockObj)
            {
                if (_isRunning) return;

                var initialResult = DetectHiddenButtonSync();
                if (!initialResult.detected)
                {
                    Debug.WriteLine("【剧情对话】启动检测未检测到隐藏按钮，不启动");
                    return;
                }

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _isPaused = false;
                _isDetected = true;
            }

            DetectionStateChanged?.Invoke(true);
            Task.Run(() => ExecuteStoryLoop(_cts!.Token));
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
                    _isDetected = false;
                    Debug.WriteLine("【剧情对话】已停止");
                }
            }
            DetectionStateChanged?.Invoke(false);
        }

        public static void Pause()
        {
            lock (_lockObj)
            {
                if (!_isRunning || _isPaused) return;

                _isPaused = true;
                _pauseCts = new CancellationTokenSource();
                Debug.WriteLine("【剧情对话】已暂停");
            }
            DetectionStateChanged?.Invoke(_isDetected);
        }

        public static void Resume()
        {
            lock (_lockObj)
            {
                if (!_isRunning || !_isPaused) return;

                _isPaused = false;
                _pauseCts?.Dispose();
                _pauseCts = null;
                Debug.WriteLine("【剧情对话】已恢复");
            }
            DetectionStateChanged?.Invoke(_isDetected);
        }

        private static async Task ExecuteStoryLoop(CancellationToken token)
        {
            var settings = ConfigManager.Get<StoryDialogueSettings>();

            try
            {
                Debug.WriteLine("【剧情对话】开始持续检测");

                while (!token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();

                    if (_isPaused)
                    {
                        await Task.Delay(100, token);
                        continue;
                    }

                    // 检测隐藏按钮
                    var result = await DetectHiddenButton();

                    if (result.detected != _isDetected)
                    {
                        _isDetected = result.detected;
                        DetectionStateChanged?.Invoke(_isDetected);
                    }

                    // 检测结果发送到 UI（配置页用）
                    DetectionResultUpdated?.Invoke(
                        result.similarity,
                        result.elapsedMs,
                        settings.DetectThreshold,
                        result.detected ? "隐藏按钮" : "");

                    token.ThrowIfCancellationRequested();

                    // 如果检测到隐藏按钮，在等待下次检测期间持续按 F 键
                    if (_isDetected)
                    {
                        int nextInterval = settings.StoryDialogueInterval;
                        int fKeyInterval = settings.FKeyInterval;

                        if (fKeyInterval > 0 && nextInterval > 0)
                        {
                            int elapsed = 0;
                            while (elapsed + fKeyInterval <= nextInterval)
                            {
                                Debug.WriteLine("【剧情对话】持续按F键");
                                SimulationService.PressF();
                                await Task.Delay(fKeyInterval, token);
                                elapsed += fKeyInterval;
                                token.ThrowIfCancellationRequested();

                                if (_isPaused || token.IsCancellationRequested)
                                    break;
                            }
                        }
                        else
                        {
                            Debug.WriteLine("【剧情对话】检测到隐藏按钮，按下F键");
                            SimulationService.PressF();
                        }
                    }
                    else
                    {
                        int interval = settings.StoryDialogueInterval;
                        if (interval > 0)
                        {
                            await Task.Delay(interval, token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("【剧情对话】任务被取消");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【剧情对话】异常: {ex.Message}");
            }
            finally
            {
                lock (_lockObj)
                {
                    _isRunning = false;
                    _isPaused = false;
                    _isDetected = false;
                    if (_cts != null)
                    {
                        _cts.Dispose();
                        _cts = null;
                    }
                }
                DetectionStateChanged?.Invoke(false);
            }
        }

        private static (bool detected, double similarity, long elapsedMs) DetectHiddenButtonSync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return (false, 0, sw.ElapsedMilliseconds);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("剧情对话_隐藏按钮区域");
                if (matchRect == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                var template = TemplateManager.GetCachedTemplate("剧情对话_隐藏按钮");
                if (template == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                double threshold = ConfigManager.Get<StoryDialogueSettings>().DetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                return (result != null && result.Similarity >= threshold, result?.Similarity ?? 0, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【剧情对话】检测异常: {ex.Message}");
                return (false, 0, sw.ElapsedMilliseconds);
            }
        }

        private static async Task<(bool detected, double similarity, long elapsedMs)> DetectHiddenButton()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!CoordinateFormats.CacheReady)
                    return (false, 0, sw.ElapsedMilliseconds);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("剧情对话_隐藏按钮区域");
                if (matchRect == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                var template = TemplateManager.GetCachedTemplate("剧情对话_隐藏按钮");
                if (template == null)
                    return (false, 0, sw.ElapsedMilliseconds);

                double threshold = ConfigManager.Get<StoryDialogueSettings>().DetectThreshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);

                bool detected = result != null && result.Similarity >= threshold;

                if (detected)
                {
                    Debug.WriteLine($"【剧情对话】检测到隐藏按钮 匹配度: {result.Similarity * 100:F2}%");
                }

                return (detected, result?.Similarity ?? 0, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【剧情对话】检测异常: {ex.Message}");
                return (false, 0, sw.ElapsedMilliseconds);
            }
        }
    }
}