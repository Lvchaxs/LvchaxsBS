using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using LvchaxsBS.Services.Hooks;

namespace LvchaxsBS.Core
{
    public static class ControllerPickupLogic
    {
        private static readonly object _lockObj = new object();
        private static bool _isRunning = false;
        private static bool _isFocused = false;
        private static bool _isListening = false;

        public static bool IsRunning => _isRunning;

        /// <summary>
        /// 启动手柄拾取监听。启动决策由 HomePage 开关控制。
        /// </summary>
        public static void Start()
        {
            lock (_lockObj)
            {
                if (_isRunning) return;

                _isRunning = true;
                _isFocused = false;
            }

            WindowFocusService.FocusChanged += OnFocusChanged;
            _isFocused = WindowFocusService.IsTargetFocused;
            GlobalGamepadHookService.GamepadEvent += OnGamepadEvent;

            // 保证 XInput 轮询开启（Xbox 手柄）
            GlobalGamepadHookService.StartXInput();

            _isListening = true;

            Debug.WriteLine("【手柄拾取】已启动");
        }

        /// <summary>
        /// 停止手柄拾取监听。
        /// </summary>
        public static void Stop()
        {
            lock (_lockObj)
            {
                if (!_isRunning) return;

                _isRunning = false;
                _isFocused = false;
            }

            if (_isListening)
            {
                WindowFocusService.FocusChanged -= OnFocusChanged;
                GlobalGamepadHookService.GamepadEvent -= OnGamepadEvent;
                _isListening = false;
            }

            GlobalGamepadHookService.StopXInput();

            Debug.WriteLine("【手柄拾取】已停止");
        }

        private static void OnFocusChanged(object? sender, bool focused)
        {
            _isFocused = focused;
        }

        private static void OnGamepadEvent(object? sender, GamepadEventArgs args)
        {
            if (!_isRunning) return;
            if (!args.IsPressed) return;
            if (!_isFocused) return;
            if (SimulationService.IsSimulating) return;

            var settings = ConfigManager.Get<HomePageSettings>();
            if (!settings.ControllerPickup) return;

            string buttonName = args.Button.ToString();
            if (!string.Equals(buttonName, settings.ControllerPickupKey, StringComparison.OrdinalIgnoreCase))
                return;

            int delay = ConfigManager.Get<ControllerPickupSettings>().TriggerDelay;

            Task.Run(async () =>
            {
                try
                {
                    if (delay > 0)
                    {
                        await Task.Delay(delay);
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        SimulationService.LeftClick();
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"【手柄拾取】执行异常: {ex.Message}");
                }
            });
        }
    }
}