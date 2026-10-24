using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LvchaxsBS.Core
{
    public static class AutoLumberLogic
    {
        private static CancellationTokenSource? _cts = null;
        private static readonly object _lockObj = new object();
        private static bool _isRunning = false;
        private static bool _isPaused = false;
        private static CancellationTokenSource? _pauseCts = null;

        private enum LumberStage
        {
            WangShuRuiYou,
            HuoDe,
            ExitGame,
            ExitToLogin,
            ClickEnter
        }

        private static LumberStage _currentStage = LumberStage.WangShuRuiYou;
        private static bool _isCorrectionMode = false;

        private static int _consecutiveTimeoutCount = 0;
        private static int _successCount = 0;

        private const int TIMEOUT_MAX = 3;

        public static event Action<double, long, double, string>? DetectionResultUpdated;
        public static event Action<int, int>? StatusCountUpdated;

        /// <summary>
        /// 功能内部停止（含自然结束、超时结束、异常结束）时触发。
        /// 由 IconService 订阅，用于同步图标与运行状态。
        /// </summary>
        public static event Action? Stopped;

        public static bool IsRunning => _isRunning;
        public static bool IsPaused => _isPaused;

        /// <summary>
        /// 启动前检测：是否检测到王树瑞佑。
        /// </summary>
        public static bool CanStart()
        {
            return DetectWangShuRuiYouSync().detected;
        }

        /// <summary>
        /// 启动自动伐木。启动决策由 IconService 负责。
        /// </summary>
        public static void Start()
        {
            lock (_lockObj)
            {
                if (_isRunning) return;

                var initialResult = DetectWangShuRuiYouSync();
                if (!initialResult.detected)
                {
                    Debug.WriteLine("【自动伐木】启动检测未检测到王树瑞佑，不启动");
                    return;
                }

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _isPaused = false;
                _isCorrectionMode = false;
                _currentStage = LumberStage.WangShuRuiYou;
                _consecutiveTimeoutCount = 0;
                _successCount = 0;
            }

            UpdateStatusUI();

            IconService.ShowSubtitle(SubtitleOverlayService.SubtitleOwner.AutoLumber);
            UpdateSubtitleDisplay();

            Task.Run(() => ExecuteLumberLoop(_cts!.Token));
        }

        /// <summary>
        /// 停止自动伐木。
        /// 只负责取消 token，所有状态清理与 Stopped 事件统一在 ExecuteLumberLoop 的 finally 中完成。
        /// </summary>
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
                Debug.WriteLine("【自动伐木】已暂停");
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
                Debug.WriteLine("【自动伐木】已恢复");
            }
        }

        private static void UpdateStatusUI()
        {
            StatusCountUpdated?.Invoke(_consecutiveTimeoutCount, TIMEOUT_MAX);
        }

        private static void UpdateSubtitleDisplay()
        {
            SubtitleOverlayService.SetSubtitle(string.Format(
                SubtitleTexts.AutoLumber.RunStatusFormat,
                _successCount,
                _consecutiveTimeoutCount,
                TIMEOUT_MAX));
        }

        private static async Task ExecuteLumberLoop(CancellationToken token)
        {
            var settings = ConfigManager.Get<AutoLumberSettings>();
            const int stageTimeoutMs = 5000;

            try
            {
                Debug.WriteLine("【自动伐木】开始持续检测");

                while (!token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();

                    if (_isPaused)
                    {
                        await Task.Delay(100, token);
                        continue;
                    }

                    // ========== 纠正模式 ==========
                    if (_isCorrectionMode)
                    {
                        Debug.WriteLine("【自动伐木】进入纠正模式");

                        bool found = false;
                        while (!found && !token.IsCancellationRequested)
                        {
                            token.ThrowIfCancellationRequested();

                            if (_isPaused)
                            {
                                await Task.Delay(100, token);
                                break;
                            }

                            var wangResult = await DetectWangShuRuiYou();
                            if (wangResult.detected)
                            {
                                Debug.WriteLine($"【自动伐木】纠正模式检测到王树瑞佑，匹配度: {wangResult.similarity * 100:F2}%");
                                _currentStage = LumberStage.WangShuRuiYou;
                                _isCorrectionMode = false;
                                found = true;
                                DetectionResultUpdated?.Invoke(wangResult.similarity, wangResult.elapsedMs, settings.DetectThreshold, "王树瑞佑");
                                break;
                            }

                            var huoDeResult = await DetectHuoDe();
                            if (huoDeResult.detected)
                            {
                                Debug.WriteLine($"【自动伐木】纠正模式检测到获得，匹配度: {huoDeResult.similarity * 100:F2}%");
                                _currentStage = LumberStage.HuoDe;
                                _isCorrectionMode = false;
                                found = true;
                                DetectionResultUpdated?.Invoke(huoDeResult.similarity, huoDeResult.elapsedMs, settings.DetectThreshold, "获得");
                                break;
                            }

                            var exitResult = await DetectExitGame();
                            if (exitResult.detected)
                            {
                                Debug.WriteLine($"【自动伐木】纠正模式检测到退出游戏，匹配度: {exitResult.similarity * 100:F2}%");
                                _currentStage = LumberStage.ExitGame;
                                _isCorrectionMode = false;
                                found = true;
                                DetectionResultUpdated?.Invoke(exitResult.similarity, exitResult.elapsedMs, settings.DetectThreshold, "退出游戏");
                                break;
                            }

                            var exitToLoginResult = await DetectExitToLogin();
                            if (exitToLoginResult.detected)
                            {
                                Debug.WriteLine($"【自动伐木】纠正模式检测到退出至登录界面，匹配度: {exitToLoginResult.similarity * 100:F2}%");
                                _currentStage = LumberStage.ExitToLogin;
                                _isCorrectionMode = false;
                                found = true;
                                DetectionResultUpdated?.Invoke(exitToLoginResult.similarity, exitToLoginResult.elapsedMs, settings.DetectThreshold, "退出至登录界面");
                                break;
                            }

                            var enterResult = await DetectClickEnter();
                            if (enterResult.detected)
                            {
                                Debug.WriteLine($"【自动伐木】纠正模式检测到点击进入，匹配度: {enterResult.similarity * 100:F2}%");
                                _currentStage = LumberStage.ClickEnter;
                                _isCorrectionMode = false;
                                found = true;
                                DetectionResultUpdated?.Invoke(enterResult.similarity, enterResult.elapsedMs, settings.DetectThreshold, "点击进入");
                                break;
                            }

                            int interval = settings.AutoLumberInterval;
                            if (interval > 0)
                            {
                                await Task.Delay(interval, token);
                            }
                        }

                        continue;
                    }

                    // ========== 正常流程（顺序执行） ==========

                    // 阶段1: 王树瑞佑
                    if (_currentStage == LumberStage.WangShuRuiYou)
                    {
                        var sw = Stopwatch.StartNew();
                        bool detected = false;

                        while (sw.ElapsedMilliseconds < stageTimeoutMs && !detected)
                        {
                            token.ThrowIfCancellationRequested();

                            if (_isPaused)
                            {
                                await Task.Delay(100, token);
                                break;
                            }

                            var result = await DetectWangShuRuiYou();
                            if (result.detected)
                            {
                                Debug.WriteLine("【自动伐木】检测到王树瑞佑，按下Z键");
                                SimulationService.PressZ();

                                _currentStage = LumberStage.HuoDe;
                                detected = true;
                                DetectionResultUpdated?.Invoke(result.similarity, result.elapsedMs, settings.DetectThreshold, "王树瑞佑");
                                break;
                            }

                            int interval = settings.AutoLumberInterval;
                            if (interval > 0)
                            {
                                await Task.Delay(interval, token);
                            }
                        }

                        if (!detected)
                        {
                            Debug.WriteLine("【自动伐木】王树瑞佑检测超时，进入纠正模式");
                            _isCorrectionMode = true;
                        }
                        continue;
                    }

                    // 阶段2: 获得
                    if (_currentStage == LumberStage.HuoDe)
                    {
                        var sw = Stopwatch.StartNew();
                        bool detected = false;

                        while (sw.ElapsedMilliseconds < stageTimeoutMs && !detected)
                        {
                            token.ThrowIfCancellationRequested();

                            if (_isPaused)
                            {
                                await Task.Delay(100, token);
                                break;
                            }

                            var result = await DetectHuoDe();
                            if (result.detected)
                            {
                                Debug.WriteLine("【自动伐木】检测到获得，按下Esc键");
                                SimulationService.PressEscape();

                                _successCount++;

                                // 每成功一次清除一次超时（最低 0）
                                if (_consecutiveTimeoutCount > 0)
                                {
                                    _consecutiveTimeoutCount--;
                                    Debug.WriteLine($"【自动伐木】成功一次，清除一次超时 (当前超时: {_consecutiveTimeoutCount}/{TIMEOUT_MAX})");
                                }

                                UpdateStatusUI();
                                UpdateSubtitleDisplay();
                                _currentStage = LumberStage.ExitGame;
                                detected = true;
                                DetectionResultUpdated?.Invoke(result.similarity, result.elapsedMs, settings.DetectThreshold, "获得");
                                break;
                            }

                            int interval = settings.AutoLumberInterval;
                            if (interval > 0)
                            {
                                await Task.Delay(interval, token);
                            }
                        }

                        if (!detected)
                        {
                            _consecutiveTimeoutCount++;
                            Debug.WriteLine($"【自动伐木】获得检测超时，超时计数: {_consecutiveTimeoutCount}/{TIMEOUT_MAX}");

                            // 无论超时多少次，都先按 ESC 走下一步
                            SimulationService.PressEscape();

                            UpdateStatusUI();
                            UpdateSubtitleDisplay();

                            if (_consecutiveTimeoutCount >= TIMEOUT_MAX)
                            {
                                Debug.WriteLine("【自动伐木】超时累计达到上限，自动结束伐木");
                                Stop();
                                return;
                            }

                            _currentStage = LumberStage.ExitGame;
                        }
                        continue;
                    }

                    // 阶段3: 退出游戏
                    if (_currentStage == LumberStage.ExitGame)
                    {
                        var sw = Stopwatch.StartNew();
                        bool detected = false;

                        while (sw.ElapsedMilliseconds < stageTimeoutMs && !detected)
                        {
                            token.ThrowIfCancellationRequested();

                            if (_isPaused)
                            {
                                await Task.Delay(100, token);
                                break;
                            }

                            var result = await DetectExitGame();
                            if (result.detected)
                            {
                                Debug.WriteLine("【自动伐木】检测到退出游戏，点击坐标");
                                ClickExitGamePosition();
                                _currentStage = LumberStage.ExitToLogin;
                                detected = true;
                                DetectionResultUpdated?.Invoke(result.similarity, result.elapsedMs, settings.DetectThreshold, "退出游戏");
                                break;
                            }

                            int interval = settings.AutoLumberInterval;
                            if (interval > 0)
                            {
                                await Task.Delay(interval, token);
                            }
                        }

                        if (!detected)
                        {
                            Debug.WriteLine("【自动伐木】退出游戏检测超时，进入纠正模式");
                            _isCorrectionMode = true;
                        }
                        continue;
                    }

                    // 阶段4: 退出至登录界面
                    if (_currentStage == LumberStage.ExitToLogin)
                    {
                        var sw = Stopwatch.StartNew();
                        bool detected = false;

                        while (sw.ElapsedMilliseconds < stageTimeoutMs && !detected)
                        {
                            token.ThrowIfCancellationRequested();

                            if (_isPaused)
                            {
                                await Task.Delay(100, token);
                                break;
                            }

                            var result = await DetectExitToLogin();
                            if (result.detected)
                            {
                                Debug.WriteLine($"【自动伐木】检测到退出至登录界面，点击位置: ({result.x}, {result.y})");
                                SimulationService.LeftClickAt(result.x, result.y);
                                _currentStage = LumberStage.ClickEnter;
                                detected = true;
                                DetectionResultUpdated?.Invoke(result.similarity, result.elapsedMs, settings.DetectThreshold, "退出至登录界面");
                                break;
                            }

                            int interval = settings.AutoLumberInterval;
                            if (interval > 0)
                            {
                                await Task.Delay(interval, token);
                            }
                        }

                        if (!detected)
                        {
                            Debug.WriteLine("【自动伐木】退出至登录界面检测超时，进入纠正模式");
                            _isCorrectionMode = true;
                        }
                        continue;
                    }

                    // 阶段5: 点击进入
                    if (_currentStage == LumberStage.ClickEnter)
                    {
                        var sw = Stopwatch.StartNew();
                        bool detected = false;

                        while (sw.ElapsedMilliseconds < stageTimeoutMs && !detected)
                        {
                            token.ThrowIfCancellationRequested();

                            if (_isPaused)
                            {
                                await Task.Delay(100, token);
                                break;
                            }

                            var result = await DetectClickEnter();
                            if (result.detected)
                            {
                                Debug.WriteLine($"【自动伐木】检测到点击进入，点击位置: ({result.x}, {result.y})");
                                SimulationService.LeftClickAt(result.x, result.y);
                                _currentStage = LumberStage.WangShuRuiYou;
                                detected = true;
                                DetectionResultUpdated?.Invoke(result.similarity, result.elapsedMs, settings.DetectThreshold, "点击进入");
                                break;
                            }

                            int interval = settings.AutoLumberInterval;
                            if (interval > 0)
                            {
                                await Task.Delay(interval, token);
                            }
                        }

                        if (!detected)
                        {
                            Debug.WriteLine("【自动伐木】点击进入检测超时，进入纠正模式");
                            _isCorrectionMode = true;
                        }
                        continue;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("【自动伐木】任务被取消");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动伐木】异常: {ex.Message}");
            }
            finally
            {
                lock (_lockObj)
                {
                    _isRunning = false;
                    _isPaused = false;
                    _isCorrectionMode = false;
                    _currentStage = LumberStage.WangShuRuiYou;
                    _consecutiveTimeoutCount = 0;
                    _successCount = 0;
                    if (_cts != null)
                    {
                        _cts.Dispose();
                        _cts = null;
                    }
                }
                UpdateStatusUI();

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    SubtitleOverlayService.HideSubtitle(SubtitleOverlayService.SubtitleOwner.AutoLumber);
                }));

                // 通知外部：功能已结束（含自然结束、超时结束、异常结束）
                Stopped?.Invoke();
            }
        }

        #region 检测方法

        // 这 6 个方法原来各自抄了一遍「判缓存 → 截图 → 取模板 → 匹配」的 40 行，
        // 只有区域键 / 模板键 / 日志文案不同。现在流水线只留一份（Core/TemplateProbe.cs），
        // 这里只做参数绑定 + 保持原来的返回值形状，上层调用与解构一行都不用改。

        private static (bool detected, double similarity, long elapsedMs) DetectWangShuRuiYouSync(bool logHit = false)
            => DetectPair("自动伐木_王树瑞佑区域", "自动伐木_王树瑞佑", "王树瑞佑", logHit);

        private static Task<(bool detected, double similarity, long elapsedMs)> DetectWangShuRuiYou()
            => Task.FromResult(DetectWangShuRuiYouSync(logHit: true));

        private static Task<(bool detected, double similarity, long elapsedMs)> DetectHuoDe()
            => Task.FromResult(DetectPair("自动伐木_获得区域", "自动伐木_获得", "获得"));

        private static Task<(bool detected, double similarity, long elapsedMs)> DetectExitGame()
            => Task.FromResult(DetectPair("派蒙菜单区域", "其他模版_ESC菜单", "退出游戏"));

        private static Task<(bool detected, double similarity, long elapsedMs, int x, int y)> DetectExitToLogin()
            => Task.FromResult(DetectWithPoint("退出至登录界面", "其他模版_退出游戏_退出至登录界面", "退出至登录界面"));

        private static Task<(bool detected, double similarity, long elapsedMs, int x, int y)> DetectClickEnter()
            => Task.FromResult(DetectWithPoint("点击进入区域", "其他模版_点击进入", "点击进入"));

        /// <summary>返回「命中 / 匹配度 / 耗时」三件套。</summary>
        private static (bool detected, double similarity, long elapsedMs) DetectPair(
            string rectKey, string templateKey, string label, bool logHit = true)
        {
            bool detected = TemplateProbe.TryRun(
                rectKey, templateKey, LumberThreshold, "自动伐木", label,
                out double similarity, out long elapsedMs, out _, out _, logHit);

            return (detected, similarity, elapsedMs);
        }

        /// <summary>
        /// 返回「命中 / 匹配度 / 耗时 / 命中点屏幕坐标」。
        /// ★ 未命中时匹配度按原实现返回 0（这三条是点击类检测，上层不拿分数做判断）。
        /// </summary>
        private static (bool detected, double similarity, long elapsedMs, int x, int y) DetectWithPoint(
            string rectKey, string templateKey, string label)
        {
            bool detected = TemplateProbe.TryRun(
                rectKey, templateKey, LumberThreshold, "自动伐木", label,
                out double similarity, out long elapsedMs, out int x, out int y,
                computeClickPoint: true);

            return detected
                ? (true, similarity, elapsedMs, x, y)
                : (false, 0, elapsedMs, 0, 0);
        }

        /// <summary>本功能的识别阈值（每次现取，用户改了配置立刻生效 —— 与原实现一致）。</summary>
        private static double LumberThreshold => ConfigManager.Get<AutoLumberSettings>().DetectThreshold;

        private static void ClickExitGamePosition()
        {
            try
            {
                var pointObj = CoordinateFormats.GetCached<object>("退出游戏点击坐标");
                if (pointObj == null)
                {
                    Debug.WriteLine("【自动伐木】退出游戏点击坐标未缓存");
                    return;
                }

                var point = (CoordinateFormats.Point)pointObj;
                Debug.WriteLine($"【自动伐木】点击退出游戏坐标: ({point.X}, {point.Y})");
                SimulationService.LeftClickAt(point.X, point.Y);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【自动伐木】点击退出游戏异常: {ex.Message}");
            }
        }

        #endregion
    }
}