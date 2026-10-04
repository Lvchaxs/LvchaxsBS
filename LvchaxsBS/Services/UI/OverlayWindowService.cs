using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LvchaxsBS.Services.UI
{
    /// <summary>
    /// 透明、点击穿透、不抢焦点、置顶的覆盖窗口服务。
    /// 负责：创建 / 打扩展样式 / SetWindowPos 定位 / 定时保顶。
    /// 坐标与尺寸一律使用【物理像素】。
    /// </summary>
    public static class OverlayWindowService
    {
        #region Win32 API

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(nint hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static readonly nint HWND_TOPMOST = new nint(-1);

        #endregion

        #region 保顶定时器

        private const int DEFAULT_TOPMOST_INTERVAL_MS = 50;

        private static readonly Dictionary<Window, DispatcherTimer> _topmostTimers = new();

        #endregion

        #region 创建窗口

        /// <summary>
        /// 给一个 WPF 窗口打上透明、点击穿透、不抢焦点的扩展样式。
        /// 必须在窗口已创建 HWND 后调用（Show 或 EnsureHandle 之后）。
        /// </summary>
        public static void ApplyOverlayStyle(Window window)
        {
            if (window == null) return;

            var helper = new WindowInteropHelper(window);
            nint hwnd = helper.Handle;
            if (hwnd == nint.Zero) return;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        }

        #endregion

        #region 位置与尺寸（物理像素）

        /// <summary>
        /// 设置窗口位置与尺寸（物理像素）。
        /// 同时会置顶并显示窗口。
        /// </summary>
        public static void SetBounds(Window window, int x, int y, int width, int height)
        {
            if (window == null) return;

            var helper = new WindowInteropHelper(window);
            nint hwnd = helper.Handle;
            if (hwnd == nint.Zero) return;

            SetWindowPos(hwnd, HWND_TOPMOST,
                x, y, width, height,
                SWP_SHOWWINDOW | SWP_NOACTIVATE);
        }

        /// <summary>
        /// 把窗口钉到最顶层（不改变位置与尺寸）。
        /// </summary>
        public static void BringToTop(Window window)
        {
            if (window == null) return;

            var helper = new WindowInteropHelper(window);
            nint hwnd = helper.Handle;
            if (hwnd == nint.Zero) return;

            SetWindowPos(hwnd, HWND_TOPMOST,
                0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        #endregion

        #region 定时保顶

        /// <summary>
        /// 启动定时保顶：每隔 intervalMs 毫秒把窗口钉到最顶层。
        /// 对同一个窗口重复调用会替换旧定时器。
        /// </summary>
        public static void StartTopmostKeepAlive(Window window, int intervalMs = DEFAULT_TOPMOST_INTERVAL_MS)
        {
            if (window == null) return;
            if (intervalMs <= 0) intervalMs = DEFAULT_TOPMOST_INTERVAL_MS;

            StopTopmostKeepAlive(window);

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs)
            };
            timer.Tick += (s, e) =>
            {
                if (window.IsVisible)
                {
                    BringToTop(window);
                }
            };
            timer.Start();

            _topmostTimers[window] = timer;
        }

        /// <summary>
        /// 停止定时保顶。
        /// </summary>
        public static void StopTopmostKeepAlive(Window window)
        {
            if (window == null) return;

            if (_topmostTimers.TryGetValue(window, out var timer))
            {
                timer.Stop();
                _topmostTimers.Remove(window);
            }
        }

        /// <summary>
        /// 停止所有保顶定时器（关闭时调用）。
        /// </summary>
        public static void StopAllTopmostKeepAlive()
        {
            foreach (var timer in _topmostTimers.Values)
            {
                timer.Stop();
            }
            _topmostTimers.Clear();
        }

        #endregion
    }
}