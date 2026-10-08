using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LvchaxsBS.Core
{
    public static class QuickTeleportLogic
    {
        private static CancellationTokenSource? _cts = null;
        private static readonly object _lockObj = new object();

        private static int _executeRefCount = 0;

        #region 取消标志（本次地图界面内禁止传送）

        /// <summary>
        /// 取消标志：这一次"处于地图界面"期间，用户主动禁止快速传送（想自己手动点）。
        ///
        /// 背景：地图界面本身会自动满足快速传送的触发条件（按下触发键就执行传送识别），
        /// 但有时用户虽然开着地图、却想手动传送，不希望被识别结果打断。
        /// 置位后即使条件仍符合（人还在地图界面）也不再响应触发键。
        ///
        /// 生效范围：一直有效，直到
        /// 1) 检测到主界面 → 自动清除（下次开图重新可用）；或
        /// 2) 用户在地图界面再次按下鼠标右键 → 手动解除。
        /// </summary>
        public static bool IsTeleportCancelled { get; private set; } = false;

        /// <summary>切换取消标志，返回切换后的状态（true = 本次地图已取消传送）。</summary>
        public static bool ToggleTeleportCancelled()
        {
            IsTeleportCancelled = !IsTeleportCancelled;
            Debug.WriteLine($"【快速传送】取消标志 = {IsTeleportCancelled}");
            return IsTeleportCancelled;
        }

        /// <summary>清除取消标志（检测到主界面时自动调用）。</summary>
        public static void ClearTeleportCancelled()
        {
            IsTeleportCancelled = false;
        }

        /// <summary>
        /// 是否启用"右键取消传送"（配置开关）。关闭后右键不再用于取消，恢复为普通右键。
        /// </summary>
        public static bool IsRightClickCancelEnabled
            => ConfigManager.Get<QuickTeleportSettings>().EnableRightClickCancel;

        /// <summary>
        /// 取消标志当前是否真的生效。
        /// 开关关闭时恒为 false —— 避免关掉开关后残留的旧标志继续挡着传送。
        /// </summary>
        public static bool IsCancelledEffective
            => IsRightClickCancelEnabled && IsTeleportCancelled;

        #endregion

        // ===== 右下角缩放匹配参数 =====
        private const int RIGHT_CORNER_SCALE_FACTOR = 1;        // 右下角模板匹配缩放因子
        private const double RIGHT_CORNER_SCALE_THRESHOLD_DELTA = 0;  // 缩放匹配阈值放宽容差

        // ===== 右侧列表缩放匹配参数 =====
        private const int RIGHT_LIST_SCALE_FACTOR = 6;          // 右侧列表模板匹配缩放因子
        private const double RIGHT_LIST_SCALE_THRESHOLD_DELTA = 0.1;    // 缩放匹配阈值放宽容差

        public static event Action<double, double, double, string, int>? DetectionResultUpdated;
        public static event Action<double, double, double, string, int>? RightListDetectionResultUpdated;

        #region 右下角缩放匹配（只沿 X 轴，缩放后直接判定命中）

        /// <summary>
        /// 右下角缩放匹配：缩放后直接匹配，命中就用缩放相似度作为结果。
        /// 缩放匹配的 X 会还原回原图坐标，供点击/日志使用。
        /// </summary>
        private static ImageRecognition.MatchResult MatchRightCornerScaled(
            Bitmap source, Bitmap template, double threshold)
        {
            if (source == null || template == null)
                return new ImageRecognition.MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            int scaleFactor = RIGHT_CORNER_SCALE_FACTOR;
            if (scaleFactor < 1) scaleFactor = 1;

            if (scaleFactor == 1)
                return ImageRecognition.MatchTemplateForRightCorner(source, template, threshold);

            int sourceWidth = source.Width;
            int sourceHeight = source.Height;
            int templateWidth = template.Width;
            int templateHeight = template.Height;

            if (templateWidth > sourceWidth || templateHeight > sourceHeight)
                return new ImageRecognition.MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            int scaledSourceWidth = Math.Max(1, sourceWidth / scaleFactor);
            int scaledSourceHeight = Math.Max(1, sourceHeight / scaleFactor);
            int scaledTemplateWidth = Math.Max(1, templateWidth / scaleFactor);
            int scaledTemplateHeight = Math.Max(1, templateHeight / scaleFactor);

            if (scaledTemplateWidth > scaledSourceWidth || scaledTemplateHeight > scaledSourceHeight)
                return new ImageRecognition.MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            using var scaledSource = ImageRecognition.ScaleBitmap(source, scaledSourceWidth, scaledSourceHeight);
            using var scaledTemplate = ImageRecognition.ScaleBitmap(template, scaledTemplateWidth, scaledTemplateHeight);

            double scaledThreshold = Math.Max(0, threshold - RIGHT_CORNER_SCALE_THRESHOLD_DELTA);

            var scaledResult = ImageRecognition.MatchTemplateForRightCorner(scaledSource, scaledTemplate, scaledThreshold);
            if (scaledResult == null)
                return new ImageRecognition.MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            return new ImageRecognition.MatchResult
            {
                Matched = scaledResult.Matched,
                X = scaledResult.X * scaleFactor,
                Y = 0,
                Similarity = scaledResult.Similarity
            };
        }

        #endregion

        #region 右侧列表缩放匹配（只沿 Y 轴，缩放后直接判定命中）

        /// <summary>
        /// 右侧列表缩放匹配：先缩放粗匹配，收集候选 Y；每个候选 Y 在原图上做只沿 Y 的局部精匹配，去重后返回全部达标点。
        /// <para>
        /// <paramref name="bestEffortScore"/> 是"没达标时的最高相似度"——它可能低于阈值，
        /// 不参与任何判定，只用来在 UI 上显示"没中，但最高到过多少"，避免未命中时匹配度只能显示 "--"。
        /// 优先取**原图精匹配**的最高分（和阈值同尺度，可以直接比较），
        /// 只有在连粗匹配候选都没有时才退回粗匹配（1/6 缩放图）的最高分。
        /// </para>
        /// </summary>
        private static List<ImageRecognition.MatchResult> MatchRightListScaled(
            Bitmap source, Bitmap template, double threshold, out double bestEffortScore)
        {
            bestEffortScore = 0;
            var results = new List<ImageRecognition.MatchResult>();

            if (source == null || template == null) return results;
            if (template.Width > source.Width || template.Height > source.Height) return results;

            int scaleFactor = RIGHT_LIST_SCALE_FACTOR;
            if (scaleFactor < 1) scaleFactor = 1;

            if (scaleFactor == 1)
                return ImageRecognition.MatchTemplateAllForRightListInternal(source, template, threshold, out bestEffortScore);

            int sourceWidth = source.Width;
            int sourceHeight = source.Height;
            int templateWidth = template.Width;
            int templateHeight = template.Height;

            int scaledSourceWidth = Math.Max(1, sourceWidth / scaleFactor);
            int scaledSourceHeight = Math.Max(1, sourceHeight / scaleFactor);
            int scaledTemplateWidth = Math.Max(1, templateWidth / scaleFactor);
            int scaledTemplateHeight = Math.Max(1, templateHeight / scaleFactor);

            if (scaledTemplateWidth > scaledSourceWidth || scaledTemplateHeight > scaledSourceHeight)
                return results;

            using var scaledSource = ImageRecognition.ScaleBitmap(source, scaledSourceWidth, scaledSourceHeight);
            using var scaledTemplate = ImageRecognition.ScaleBitmap(template, scaledTemplateWidth, scaledTemplateHeight);

            double scaledThreshold = Math.Max(0, threshold - RIGHT_LIST_SCALE_THRESHOLD_DELTA);

            var scaledResults = ImageRecognition.MatchTemplateAllForRightListInternal(
                scaledSource, scaledTemplate, scaledThreshold, out double coarseBestScore);
            if (scaledResults.Count == 0)
            {
                // 连粗匹配候选都没有：只能给一个缩放图上的参考分（比阈值宽松 0.1 都没中，说明差得远）
                bestEffortScore = coarseBestScore;
                return results;
            }

            // 精匹配阶段的最高分（原图尺度，和阈值可以直接比较）
            double preciseBestScore = 0;

            int searchMarginY = Math.Max(templateHeight / 6, 6);

            foreach (var scaledResult in scaledResults)
            {
                int roughY = scaledResult.Y * scaleFactor;

                int searchY = Math.Max(0, roughY - searchMarginY);
                int searchHeight = Math.Min(sourceHeight - searchY, templateHeight + searchMarginY * 2);

                using var searchRegion = ImageRecognition.CropBitmap(source, 0, searchY, sourceWidth, searchHeight);

                var preciseResults = ImageRecognition.MatchTemplateAllForRightListInternal(
                    searchRegion, template, threshold, out double regionBestScore);

                if (regionBestScore > preciseBestScore)
                    preciseBestScore = regionBestScore;

                foreach (var preciseResult in preciseResults)
                {
                    bool exists = false;
                    foreach (var existing in results)
                    {
                        if (Math.Abs(existing.Y - (searchY + preciseResult.Y)) < templateHeight / 2)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        results.Add(new ImageRecognition.MatchResult
                        {
                            Matched = true,
                            X = 0,
                            Y = searchY + preciseResult.Y,
                            Similarity = preciseResult.Similarity
                        });
                    }
                }
            }

            // 没达标：把原图精匹配的最高分给 UI（与阈值同尺度，不会出现"看着超阈值却没执行"的错觉）
            bestEffortScore = preciseBestScore > 0 ? preciseBestScore : coarseBestScore;

            return results;
        }

        #endregion

        #region 执行快速传送

        public static async void Execute(bool isFocused, bool isInMainWindow, bool isLeftButton = false)
        {
            if (SimulationService.IsSimulating)
            {
                return;
            }

            bool needSuspend = DetectionManager.IsInMap;

            if (needSuspend)
            {
                Interlocked.Increment(ref _executeRefCount);
                DetectionManager.Suspend();
            }

            CancellationToken token;

            lock (_lockObj)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                    Debug.WriteLine("【快速传送】用户触发新任务，取消旧任务");
                }

                _cts = new CancellationTokenSource();
                token = _cts.Token;
            }

            try
            {
                if (isLeftButton)
                {
                    SimulationService.GetCursorPos(out SimulationService.POINT pressPoint);

                    var tcs = new TaskCompletionSource<bool>();
                    SimulationService.POINT pressPointCopy = pressPoint;

                    void OnMouseEvent(object? sender, MouseEventArgs args)
                    {
                        if (args.EventType == MouseEventType.LeftButtonUp)
                        {
                            SimulationService.GetCursorPos(out SimulationService.POINT releasePoint);

                            double dx = pressPointCopy.X - releasePoint.X;
                            double dy = pressPointCopy.Y - releasePoint.Y;
                            double distance = Math.Sqrt(dx * dx + dy * dy);

                            tcs.TrySetResult(distance <= 15);
                        }
                    }

                    GlobalMouseHookService.MouseEvent += OnMouseEvent;

                    try
                    {
                        bool shouldExecute = await tcs.Task;
                        if (!shouldExecute)
                        {
                            return;
                        }
                    }
                    finally
                    {
                        GlobalMouseHookService.MouseEvent -= OnMouseEvent;
                    }

                    if (DetectionManager.IsInMap && ShouldBlockByAbyssFilter(pressPoint))
                    {
                        Debug.WriteLine("【快速传送】左键点击位于地图界面的深渊过滤区域，已拦截");
                        return;
                    }

                    await ExecuteTeleportLogic(isFocused, token);
                }
                else
                {
                    await ExecuteTeleportLogic(isFocused, token);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("【快速传送】被新触发中断");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【快速传送】异常: {ex.Message}");
            }
            finally
            {
                lock (_lockObj)
                {
                    if (_cts != null)
                    {
                        _cts.Dispose();
                        _cts = null;
                    }
                }

                if (needSuspend)
                {
                    if (Interlocked.Decrement(ref _executeRefCount) == 0)
                    {
                        DetectionManager.Resume();
                    }
                }
            }
        }

        #endregion

        #region 辅助方法

        private static async Task ExecuteTeleportLogic(bool isFocused, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!isFocused || !CoordinateFormats.CacheReady)
                        return;

                    DetectionResultUpdated?.Invoke(-1, -1, -1, "", 0);
                    RightListDetectionResultUpdated?.Invoke(-1, -1, -1, "", 0);

                    cancellationToken.ThrowIfCancellationRequested();

                    var settings = ConfigManager.Get<QuickTeleportSettings>();

                    int detectDelay = settings.RightCornerDetectDelay_1;
                    if (detectDelay > 0)
                    {
                        if (!SpinWait.SpinUntil(() => false, detectDelay))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (!CoordinateFormats.CacheReady)
                        return;

                    var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("快速传送_右下角区域");
                    if (matchRect == null)
                        return;

                    var rightListRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("快速传送_右侧列表区域");

                    double threshold = settings.DetectThreshold;
                    double rightListThreshold = settings.RightListThreshold;

                    string[] rightCornerTemplateNames = new string[]
                    {
                        "快速传送_传送",
                        "快速传送_靶场"
                    };

                    string[] rightListTemplateNames = new string[]
                    {
                        "快速传送_传送锚点",
                        "快速传送_七天神像",
                        "快速传送_新月神像",
                        "快速传送_秘境1",
                        "快速传送_秘境2",
                        "快速传送_列车",
                        "快速传送_宅邸",
                        "快速传送_幽境危战",
                        "快速传送_口袋锚点",
                        "快速传送_青玄乘阳华盖",
                    };

                    // 未启用列表识别：只循环右下角，不跑右侧列表
                    if (!settings.EnableListRecognition)
                    {
                        Debug.WriteLine("【快速传送】未启用列表识别，仅循环右下角检测");

                        int maxDetectTime = settings.RightListDetectDelay_1;

                        int detectCount = 0;
                        double cornerTotalMs = 0;

                        while (cornerTotalMs < maxDetectTime)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (!CoordinateFormats.CacheReady)
                                break;

                            detectCount++;

                            // bestScore：本轮所有模板的最高相似度（未达标也算，只用于 UI 展示）；
                            // bestSource：只有真正达标（Matched）才有类型。
                            // 判定仍然只看"有没有达标"——达标 ⟺ 相似度 ≥ 阈值，所以 bestScore 达标时必有类型
                            double bestScore = 0;
                            string bestSource = "";

                            var cornerSw = Stopwatch.StartNew();

                            using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                            if (source != null)
                            {
                                foreach (var templateName in rightCornerTemplateNames)
                                {
                                    var template = TemplateManager.GetCachedTemplate(templateName);
                                    if (template == null) continue;

                                    // 缩放匹配，命中即算命中
                                    var scaledResult = MatchRightCornerScaled(source, template, threshold);
                                    if (scaledResult == null) continue;

                                    // 没达标也记下相似度：UI 上就能看到"差多少"，而不是干巴巴一个 "--"。
                                    // （MatchTemplateForRightCorner 返回的 Similarity 是滑窗最高 NCC，与是否达标无关）
                                    bool isNewBest = scaledResult.Similarity > bestScore;
                                    if (isNewBest)
                                    {
                                        bestScore = scaledResult.Similarity;
                                        if (scaledResult.Matched)
                                            bestSource = templateName.Replace("快速传送_", "");
                                    }

                                    // 命中且刷新了最高分：再在原图匹配一次，只为拿到原图匹配度（不参与判定）
                                    if (isNewBest && scaledResult.Matched)
                                    {
                                        var precise = ImageRecognition.MatchTemplateForRightCorner(source, template, threshold);
                                        double preciseScore = precise?.Similarity ?? 0;
                                        Debug.WriteLine($"【快速传送-右下角】命中(缩放)={scaledResult.Similarity * 100:F2}%  原图匹配度={preciseScore * 100:F2}%  类型={bestSource}");
                                    }
                                }

                                cornerSw.Stop();
                                double cornerMs = cornerSw.Elapsed.TotalMilliseconds;
                                cornerTotalMs += cornerMs;


                                DetectionResultUpdated?.Invoke(bestScore, cornerTotalMs, threshold, bestSource, detectCount);

                                string sourceDisplay = string.IsNullOrEmpty(bestSource) ? "" : $" 类型: {bestSource}";
                                Debug.WriteLine($"【快速传送-右下角】匹配度: {bestScore * 100:F2}%{sourceDisplay} (阈值: {threshold * 100:F0}%) 累计耗时: {cornerTotalMs:F2}ms");

                                if (bestScore >= threshold)
                                {
                                    SimulationService.PressF();
                                    Debug.WriteLine("【快速传送】右下角检测到目标，已按F，结束");
                                    return;
                                }
                            }
                        }

                        Debug.WriteLine("【快速传送】右下角检测结束，未检测到目标");
                        return;
                    }

                    if (rightListRect == null)
                        return;

                    int maxDetectTimeFull = settings.RightListDetectDelay_1;

                    Debug.WriteLine($"【快速传送】开始检测，检测时长上限: {maxDetectTimeFull}ms");

                    int detectCountFull = 0;

                    double cornerTotalMsFull = 0;
                    double rightListTotalMsFull = 0;

                    while (cornerTotalMsFull < maxDetectTimeFull && rightListTotalMsFull < maxDetectTimeFull)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!CoordinateFormats.CacheReady)
                            break;

                        detectCountFull++;

                        // ===== 右下角检测 =====
                        double bestScore = 0;
                        string bestSource = "";

                        var cornerSw = Stopwatch.StartNew();

                        using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                        if (source != null)
                        {
                            foreach (var templateName in rightCornerTemplateNames)
                            {
                                var template = TemplateManager.GetCachedTemplate(templateName);
                                if (template == null) continue;

                                var scaledResult = MatchRightCornerScaled(source, template, threshold);
                                if (scaledResult == null) continue;

                                // 同样：没达标也记下相似度，UI 才能显示"差多少"
                                bool isNewBest = scaledResult.Similarity > bestScore;
                                if (isNewBest)
                                {
                                    bestScore = scaledResult.Similarity;
                                    if (scaledResult.Matched)
                                        bestSource = templateName.Replace("快速传送_", "");
                                }

                                if (isNewBest && scaledResult.Matched)
                                {
                                    var precise = ImageRecognition.MatchTemplateForRightCorner(source, template, threshold);
                                    double preciseScore = precise?.Similarity ?? 0;
                                    Debug.WriteLine($"【快速传送-右下角】命中(缩放)={scaledResult.Similarity * 100:F2}%  原图匹配度={preciseScore * 100:F2}%  类型={bestSource}");
                                }
                            }

                            cornerSw.Stop();
                            double cornerMs = cornerSw.Elapsed.TotalMilliseconds;
                            cornerTotalMsFull += cornerMs;


                            DetectionResultUpdated?.Invoke(bestScore, cornerTotalMsFull, threshold, bestSource, detectCountFull);

                            string sourceDisplay = string.IsNullOrEmpty(bestSource) ? "" : $" 类型: {bestSource}";
                            Debug.WriteLine($"【快速传送-右下角】匹配度: {bestScore * 100:F2}%{sourceDisplay} (阈值: {threshold * 100:F0}%) 累计耗时: {cornerTotalMsFull:F2}ms");

                            if (bestScore >= threshold)
                            {
                                SimulationService.PressF();
                                Debug.WriteLine("【快速传送】右下角检测到目标，已按F，结束");
                                return;
                            }

                            if (cornerTotalMsFull >= maxDetectTimeFull)
                            {
                                Debug.WriteLine($"【快速传送】右下角累计 {cornerTotalMsFull:F2}ms ≥ {maxDetectTimeFull}ms，结束");
                                return;
                            }
                        }

                        // ===== 右侧列表检测 =====
                        var rightListSw = Stopwatch.StartNew();

                        using var rightListSource = ImageRecognition.CaptureRegionScaled(rightListRect);
                        if (rightListSource != null)
                        {
                            int rightListBestY = int.MaxValue;
                            string rightListBestSource = "";
                            int rightListBestWidth = 0;
                            int rightListBestHeight = 0;
                            double rightListBestScore = 0;
                            double globalBestScore = 0;
                            string globalBestSource = "";

                            foreach (var templateName in rightListTemplateNames)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                var template = TemplateManager.GetCachedTemplate(templateName);
                                if (template == null) continue;

                                var results = MatchRightListScaled(rightListSource, template, rightListThreshold,
                                                                   out double bestEffortScore);

                                // 一个达标点都没有：把这个模板的最高相似度当作参考值报给 UI，
                                // 同时把模板名也报出去 —— 这样能看出"最接近的是哪个类型、差多少"
                                // （判定仍然只看 results，不受影响）
                                if (results.Count == 0 && bestEffortScore > globalBestScore)
                                {
                                    globalBestScore = bestEffortScore;
                                    globalBestSource = templateName.Replace("快速传送_", "");
                                }

                                foreach (var result in results)
                                {
                                    if (result.Similarity > globalBestScore)
                                    {
                                        globalBestScore = result.Similarity;
                                        globalBestSource = templateName.Replace("快速传送_", "");
                                    }

                                    if (result.Y < rightListBestY)
                                    {
                                        rightListBestY = result.Y;
                                        rightListBestSource = templateName.Replace("快速传送_", "");
                                        rightListBestWidth = template.Width;
                                        rightListBestHeight = template.Height;
                                        rightListBestScore = result.Similarity;
                                    }
                                }
                            }

                            rightListSw.Stop();
                            double rightListMs = rightListSw.Elapsed.TotalMilliseconds;
                            rightListTotalMsFull += rightListMs;

                            double rightListSaveScore = rightListBestY != int.MaxValue ? rightListBestScore : globalBestScore;

                            if (rightListBestY != int.MaxValue)
                            {
                                RightListDetectionResultUpdated?.Invoke(rightListBestScore, rightListTotalMsFull, rightListThreshold, rightListBestSource, detectCountFull);

                                string rightListSourceDisplay = string.IsNullOrEmpty(rightListBestSource) ? "" : $" 类型: {rightListBestSource}";
                                Debug.WriteLine($"【快速传送-右侧列表】匹配度: {rightListBestScore * 100:F2}%{rightListSourceDisplay} (阈值: {rightListThreshold * 100:F0}%) 累计耗时: {rightListTotalMsFull:F2}ms");

                                double capScale = ImageRecognition.GetCaptureScale();

                                int clickX = rightListRect.Rect.X1 + (int)(rightListBestWidth / 2.0 / capScale);
                                int clickY = rightListRect.Rect.Y1 + (int)((rightListBestY + rightListBestHeight / 2.0) / capScale);

                                Debug.WriteLine($"【快速传送-右侧列表】点击坐标: ({clickX}, {clickY}) 类型: {rightListBestSource}");

                                int clickDelay = settings.RightListClickItemDelay_1;
                                if (clickDelay > 0)
                                {
                                    if (!SpinWait.SpinUntil(() => false, clickDelay))
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                    }
                                }

                                SimulationService.LeftClickAt(clickX, clickY);

                                int fKeyDelay = settings.RightListFKeyDelay;
                                if (rightListBestSource == "秘境1" || rightListBestSource == "秘境2")
                                {
                                    fKeyDelay = settings.RightListAbyssFKeyDelay;
                                }
                                if (fKeyDelay > 0)
                                {
                                    if (!SpinWait.SpinUntil(() => false, fKeyDelay))
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                    }
                                }

                                SimulationService.PressF();
                                return;
                            }
                            else
                            {
                                RightListDetectionResultUpdated?.Invoke(globalBestScore, rightListTotalMsFull, rightListThreshold, globalBestSource, detectCountFull);
                                Debug.WriteLine($"【快速传送-右侧列表】未达阈值，最高匹配度: {globalBestScore * 100:F2}% 类型: {globalBestSource} 累计耗时: {rightListTotalMsFull:F2}ms");
                            }

                            if (rightListTotalMsFull >= maxDetectTimeFull)
                            {
                                Debug.WriteLine($"【快速传送】右侧列表累计 {rightListTotalMsFull:F2}ms ≥ {maxDetectTimeFull}ms，结束");
                                return;
                            }
                        }
                    }

                    Debug.WriteLine("【快速传送】检测结束，未检测到目标");
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("【快速传送】任务被中断");
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"【快速传送】异常: {ex.Message}");
                }
            }, cancellationToken);
        }

        private static bool ShouldBlockByAbyssFilter(SimulationService.POINT pressPoint)
        {
            var settings = ConfigManager.Get<QuickTeleportSettings>();

            if (!settings.DisableAbyssFilter)
                return false;

            if (!CoordinateFormats.CacheReady)
                return false;

            var abyssFilterRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>("快速传送_深渊过滤区域");
            if (abyssFilterRect == null)
                return false;

            var rect = abyssFilterRect.Rect;

            return pressPoint.X >= rect.X1 && pressPoint.X <= rect.X2
                && pressPoint.Y >= rect.Y1 && pressPoint.Y <= rect.Y2;
        }

        #endregion
    }
}