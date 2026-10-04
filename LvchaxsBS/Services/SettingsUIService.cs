using System;
using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Core;
using LvchaxsBS.Services.Hooks;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.Services
{
    public static class SettingsUIService
    {
        #region 卡片1：窗口信息

        private static StatusTagItem? _serviceTag;
        private static StatusTagItem? _gameTag;
        private static StatusTagItem? _focusTag;
        private static StatusTagItem? _topLeftTag;
        private static StatusTagItem? _titleTag;
        private static bool _isWindowInfoUIRegistered = false;

        #endregion

        #region 卡片2：主界面检测

        private static StatusTagItem? _mainWindowDetectionTag;
        private static StatusTagItem? _mainWindowResultTag;
        private static StatusTagItem? _mainWindowElapsedTag;
        private static bool _isMainWindowUIRegistered = false;

        #endregion

        #region 注册/注销

        public static void RegisterWindowInfoUI(
            TextBlock serviceStatusText,
            Border serviceStatusBorder,
            TextBlock gameStatusText,
            Border gameStatusBorder,
            TextBlock focusStatusText,
            Border focusStatusBorder,
            TextBlock topLeftTextBox,
            Border topLeftStatusBorder,
            TextBlock titleTextBox,
            Border titleStatusBorder)
        {
            _serviceTag = new StatusTagItem { Text = serviceStatusText, Border = serviceStatusBorder };
            _gameTag = new StatusTagItem { Text = gameStatusText, Border = gameStatusBorder };
            _focusTag = new StatusTagItem { Text = focusStatusText, Border = focusStatusBorder };
            _topLeftTag = new StatusTagItem { Text = topLeftTextBox, Border = topLeftStatusBorder };
            _titleTag = new StatusTagItem { Text = titleTextBox, Border = titleStatusBorder };

            _isWindowInfoUIRegistered = true;

            WindowFocusService.FocusChanged += OnFocusChanged;
            WindowFocusService.BoundsChanged += OnBoundsChanged;
            WindowFocusService.WindowExistenceChanged += OnWindowExistenceChanged;

            UpdateWindowInfoUI();
        }

        public static void RegisterMainWindowUI(
            TextBlock detectionStatusText,
            Border detectionStatusBorder,
            TextBlock resultStatusText,
            Border resultStatusBorder,
            TextBlock elapsedStatusText,
            Border elapsedStatusBorder)
        {
            _mainWindowDetectionTag = new StatusTagItem { Text = detectionStatusText, Border = detectionStatusBorder };
            _mainWindowResultTag = new StatusTagItem { Text = resultStatusText, Border = resultStatusBorder };
            _mainWindowElapsedTag = new StatusTagItem { Text = elapsedStatusText, Border = elapsedStatusBorder };

            _isMainWindowUIRegistered = true;

            WindowFocusService.FocusChanged += OnMainWindowFocusChanged;
            DetectionManager.DetectionResultChanged += OnDetectionResultChanged;
            DetectionManager.MapDetectionResultChanged += OnMapDetectionResultChanged;
            DetectionManager.DetectionElapsed += OnDetectionElapsed;

            UpdateMainWindowUI();
        }

        public static void UnregisterAll()
        {
            if (_isWindowInfoUIRegistered)
            {
                WindowFocusService.FocusChanged -= OnFocusChanged;
                WindowFocusService.BoundsChanged -= OnBoundsChanged;
                WindowFocusService.WindowExistenceChanged -= OnWindowExistenceChanged;
                _isWindowInfoUIRegistered = false;
            }

            if (_isMainWindowUIRegistered)
            {
                WindowFocusService.FocusChanged -= OnMainWindowFocusChanged;
                DetectionManager.DetectionResultChanged -= OnDetectionResultChanged;
                DetectionManager.MapDetectionResultChanged -= OnMapDetectionResultChanged;
                DetectionManager.DetectionElapsed -= OnDetectionElapsed;
                _isMainWindowUIRegistered = false;
            }

            _serviceTag = null;
            _gameTag = null;
            _focusTag = null;
            _topLeftTag = null;
            _titleTag = null;
            _mainWindowDetectionTag = null;
            _mainWindowResultTag = null;
            _mainWindowElapsedTag = null;
        }

        #endregion

        #region 卡片1 更新

        private static void OnFocusChanged(object? sender, bool focused)
        {
            Application.Current?.Dispatcher.Invoke(UpdateWindowInfoUI);
        }

        private static void OnBoundsChanged(object? sender, WindowFocusService.WindowBounds bounds)
        {
            Application.Current?.Dispatcher.Invoke(UpdateWindowInfoUI);
        }

        private static void OnWindowExistenceChanged(object? sender, bool exists)
        {
            Application.Current?.Dispatcher.Invoke(UpdateWindowInfoUI);
        }

        private static void UpdateWindowInfoUI()
        {
            if (!_isWindowInfoUIRegistered) return;

            bool present = WindowFocusService.IsWindowPresent;
            bool hasBounds = !WindowFocusService.LastBounds.IsEmpty;
            bool isFocused = WindowFocusService.IsTargetFocused;
            var bounds = WindowFocusService.LastBounds;

            _serviceTag?.Set("检测：" + (WindowFocusService.IsRunning ? "运行中" : "已停止"),
                             WindowFocusService.IsRunning);

            _gameTag?.Set("游戏：" + (present ? "已启动" : "未启动"), present);

            if (!present)
            {
                _focusTag?.SetNeutral("焦点：-");
            }
            else
            {
                _focusTag?.Set("焦点：" + (isFocused ? "焦点中" : "焦点外"), isFocused);
            }

            // 坐标：归焦点事件驱动
            _topLeftTag?.Set(hasBounds ? $"坐标：({bounds.Left}, {bounds.Top})" : "坐标：-", hasBounds);

            // 标题：由轮询维护，只要进程在就显示
            string title = WindowFocusService.CurrentTitle;
            _titleTag?.Set(present
                ? $"标题：{(string.IsNullOrEmpty(title) ? "(无)" : title)}"
                : "标题：未找到",
                present);
        }

        #endregion

        #region 卡片2 更新

        private static void OnMainWindowFocusChanged(object? sender, bool focused)
        {
            Application.Current?.Dispatcher.Invoke(UpdateMainWindowUI);
        }

        private static void OnDetectionResultChanged(bool isInMainWindow)
        {
            Application.Current?.Dispatcher.Invoke(UpdateMainWindowUI);
        }

        private static void OnMapDetectionResultChanged(bool isInMap)
        {
            Application.Current?.Dispatcher.Invoke(UpdateMainWindowUI);
        }

        private static void OnDetectionElapsed(double elapsedMs)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!_isMainWindowUIRegistered) return;

                bool detectionRunning = WindowFocusService.IsTargetFocused && DetectionManager.IsRunning;

                if (!detectionRunning)
                {
                    _mainWindowElapsedTag?.SetNeutral("耗时：-");
                    return;
                }

                _mainWindowElapsedTag?.Set($"耗时：{elapsedMs:F2}ms", true);
            });
        }

        private static void UpdateMainWindowUI()
        {
            if (!_isMainWindowUIRegistered) return;

            bool isFocused = WindowFocusService.IsTargetFocused;
            bool isLoopRunning = DetectionManager.IsRunning;

            bool detectionRunning = isFocused && isLoopRunning;

            _mainWindowDetectionTag?.Set("检测：" + (detectionRunning ? "运行中" : "已停止"),
                                         detectionRunning);

            if (!detectionRunning)
            {
                _mainWindowResultTag?.SetNeutral("类型：未检测到");
                _mainWindowElapsedTag?.SetNeutral("耗时：-");
                return;
            }

            bool isInMainWindow = DetectionManager.IsInMainWindow;
            bool isInMap = DetectionManager.IsInMap;

            if (isInMainWindow)
            {
                _mainWindowResultTag?.Set("类型：主界面中", true);
            }
            else if (isInMap)
            {
                _mainWindowResultTag?.Set("类型：地图界面中", true);
            }
            else
            {
                _mainWindowResultTag?.SetNeutral("类型：未检测到");
            }
        }

        #endregion
    }
}