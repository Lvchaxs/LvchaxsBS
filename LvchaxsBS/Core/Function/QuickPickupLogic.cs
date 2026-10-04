using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LvchaxsBS.Core
{
    public static class QuickPickupLogic
    {
        private static CancellationTokenSource? _cts = null;
        private static readonly object _lockObj = new object();
        private static bool _isRunning = false;
        private static bool _isPaused = false;
        private static CancellationTokenSource? _pauseCts = null;

        // 保留给 UI 用的暂停/恢复通知（如果外部需要感知）
        public static event Action? OnPaused;
        public static event Action? OnResumed;

        public static bool IsRunning => _isRunning;
        public static bool IsPaused => _isPaused;

        // ===== 物理 Win 键检测 =====
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        /// <summary>
        /// 检测物理 Win 键是否按下（最高位为 1 表示当前按下）
        /// </summary>
        private static bool IsWinKeyDown()
        {
            return (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0
                || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        }

        /// <summary>
        /// 启动快速拾取。启动决策由 IconService 负责。
        /// </summary>
        public static void Start()
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
                }

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _isPaused = false;
            }

            Task.Run(() => ExecutePickupLoop(_cts!.Token));
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

                OnPaused?.Invoke();
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
                OnResumed?.Invoke();
            }
        }

        private static async Task ExecutePickupLoop(CancellationToken token)
        {
            var settings = ConfigManager.Get<QuickPickupSettings>();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();

                    while (_isPaused)
                    {
                        await Task.Delay(50, token);
                        token.ThrowIfCancellationRequested();
                    }

                    // 物理 Win 键按下时，跳过本轮模拟，避免打断系统对"单独按 Win"的识别
                    if (IsWinKeyDown())
                    {
                        await Task.Delay(30, token);
                        continue;
                    }

                    SimulationService.PressF();

                    token.ThrowIfCancellationRequested();

                    // 发完 F 后再查一次，Win 按下就别继续滚轮
                    if (IsWinKeyDown())
                    {
                        await Task.Delay(30, token);
                        continue;
                    }

                    int fAndScrollDelay = settings.FAndScrollDelay;
                    if (fAndScrollDelay > 0)
                    {
                        await Task.Delay(fAndScrollDelay, token);
                    }

                    token.ThrowIfCancellationRequested();

                    while (_isPaused)
                    {
                        await Task.Delay(50, token);
                        token.ThrowIfCancellationRequested();
                    }

                    // 滚轮前再查
                    if (IsWinKeyDown())
                    {
                        await Task.Delay(30, token);
                        continue;
                    }

                    SimulationService.ScrollDown(1);

                    token.ThrowIfCancellationRequested();

                    while (_isPaused)
                    {
                        await Task.Delay(50, token);
                        token.ThrowIfCancellationRequested();
                    }

                    int pickupInterval = settings.PickupInterval;
                    if (pickupInterval > 0)
                    {
                        await Task.Delay(pickupInterval, token);
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
                    if (_cts != null)
                    {
                        _cts.Dispose();
                        _cts = null;
                    }
                }
            }
        }
    }
}