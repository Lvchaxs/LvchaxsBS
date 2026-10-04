using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace LvchaxsBS.Services.Hooks
{
    /// <summary>
    /// 窗口焦点事件驱动服务 + 非焦点轮询兜底。
    /// </summary>
    public static class WindowFocusService
    {
        #region Win32 API

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int val, int size);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        #endregion

        #region 常量

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_OBJECT_DESTROY = 0x8001;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int OBJID_WINDOW = 0;

        private const int DebounceMs = 150;
        private const int PollIntervalMs = 2000;
        private const string TargetWindowClass = "UnityWndClass";
        private const int SW_RESTORE = 9;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const uint GW_OWNER = 4;
        private const int DWMWA_CLOAKED = 14;

        #endregion

        #region 目标进程

        public static string TargetProcessName1 = "GenshinImpact";
        public static string TargetProcessName2 = "YuanShen";

        #endregion

        #region 状态字段

        private static IntPtr _foregroundHook = IntPtr.Zero;
        private static IntPtr _locationHook = IntPtr.Zero;
        private static IntPtr _destroyHook = IntPtr.Zero;
        private static readonly WinEventDelegate _winEventProc = OnWinEvent;

        private static bool _isRunning;
        private static bool _lastFocused;
        private static bool _lastWindowExists;

        private static IntPtr _lastHwnd = IntPtr.Zero;
        private static int _lastLeft = int.MinValue;
        private static int _lastTop = int.MinValue;
        private static int _lastWidth = int.MinValue;
        private static int _lastHeight = int.MinValue;
        private static string _lastTitle = string.Empty;
        private static string _lastProcessName = string.Empty;

        private static DispatcherTimer? _debounceTimer;
        private static IntPtr _pendingHwnd = IntPtr.Zero;

        private static DispatcherTimer? _pollTimer;

        #endregion

        #region 对外接口

        public static bool IsRunning => _isRunning;

        public static bool IsTargetFocused => _lastFocused && _lastWindowExists;

        public static bool IsWindowPresent => _lastWindowExists;

        public static string CurrentTitle => _lastTitle;

        public static string CurrentProcessName => _lastProcessName;

        public static WindowBounds LastBounds { get; private set; } = WindowBounds.Empty;

        public static event EventHandler<bool>? FocusChanged;
        public static event EventHandler<WindowBounds>? BoundsChanged;
        public static event EventHandler<bool>? WindowExistenceChanged;

        /// <summary>
        /// 激活检测到的游戏窗口（最小化则先还原）。
        /// 返回是否成功。
        /// </summary>
        public static bool ActivateWindow()
        {
            if (_lastHwnd == IntPtr.Zero || !IsWindow(_lastHwnd)) return false;

            if (IsIconic(_lastHwnd))
            {
                ShowWindow(_lastHwnd, SW_RESTORE);
            }

            return SetForegroundWindow(_lastHwnd);
        }

        /// <summary>
        /// 枚举任务栏窗口，按进程名找目标，纯让系统去激活。
        /// 等价于用户点任务栏，不修改窗口尺寸。
        /// </summary>
        public static bool ActivateByProcessName()
        {
            IntPtr hwnd = FindTaskbarWindowByProcess(
                TargetProcessName1, TargetProcessName2);

            if (hwnd == IntPtr.Zero) return false;

            // 纯让系统激活，等价用户点任务栏
            SwitchToThisWindow(hwnd, true);

            return true;
        }

        /// <summary>在任务栏窗口里按进程名找目标 hwnd。</summary>
        private static IntPtr FindTaskbarWindowByProcess(params string[] processNames)
        {
            foreach (var info in GetTaskbarWindows())
            {
                foreach (var name in processNames)
                {
                    if (info.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return info.Hwnd;
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>任务栏里可激活的窗口信息。</summary>
        public sealed class TaskbarWindowInfo
        {
            public IntPtr Hwnd { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ClassName { get; set; } = string.Empty;
            public string ProcessName { get; set; } = string.Empty;
            public uint ProcessId { get; set; }
            public bool IsIconic { get; set; }

            public override string ToString()
                => $"句柄: 0x{Hwnd.ToInt64():X}, " +
                   $"进程: \"{ProcessName}\" (PID={ProcessId}), " +
                   $"类名: \"{ClassName}\", " +
                   $"标题: \"{(string.IsNullOrEmpty(Title) ? "(无)" : Title)}\", " +
                   $"最小化: {(IsIconic ? "是" : "否")}";
        }

        /// <summary>
        /// 枚举"任务栏里可激活的窗口"，过滤规则与任务栏显示逻辑基本一致。
        /// </summary>
        public static List<TaskbarWindowInfo> GetTaskbarWindows()
        {
            var list = new List<TaskbarWindowInfo>();

            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                if (GetWindowTextLength(hwnd) == 0) return true;

                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

                IntPtr owner = GetWindow(hwnd, GW_OWNER);
                if (owner != IntPtr.Zero && (exStyle & WS_EX_APPWINDOW) == 0) return true;

                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                    && cloaked != 0)
                    return true;

                var info = new TaskbarWindowInfo { Hwnd = hwnd };

                var sb = new StringBuilder(512);
                GetWindowText(hwnd, sb, sb.Capacity);
                info.Title = sb.ToString();

                sb.Clear();
                GetClassName(hwnd, sb, sb.Capacity);
                info.ClassName = sb.ToString();

                GetWindowThreadProcessId(hwnd, out uint pid);
                info.ProcessId = pid;
                if (pid != 0)
                {
                    try { info.ProcessName = Process.GetProcessById((int)pid).ProcessName; }
                    catch { info.ProcessName = "(已退出)"; }
                }

                info.IsIconic = IsIconic(hwnd);
                list.Add(info);
                return true;
            }, IntPtr.Zero);

            return list;
        }

        public readonly struct WindowBounds
        {
            public static readonly WindowBounds Empty =
                new WindowBounds(IntPtr.Zero, 0, 0, 0, 0, string.Empty, string.Empty);

            public IntPtr Hwnd { get; }
            public int Left { get; }
            public int Top { get; }
            public int Width { get; }
            public int Height { get; }
            public string Title { get; }
            public string ProcessName { get; }

            public WindowBounds(IntPtr hwnd, int left, int top, int width, int height, string title, string processName)
            {
                Hwnd = hwnd;
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                Title = title ?? string.Empty;
                ProcessName = processName ?? string.Empty;
            }

            public bool IsEmpty => Hwnd == IntPtr.Zero || Width <= 0 || Height <= 0;

            public override string ToString()
                => $"进程: \"{ProcessName}\", " +
                   $"标题: \"{(string.IsNullOrEmpty(Title) ? "(无)" : Title)}\", " +
                   $"分辨率: {Width}x{Height}, " +
                   $"左上角: ({Left}, {Top}), " +
                   $"句柄: 0x{Hwnd.ToInt64():X}";
        }

        #endregion

        #region 启动 / 停止

        public static void Start()
        {
            if (_isRunning) return;

            _foregroundHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

            if (_foregroundHook == IntPtr.Zero) return;

            _locationHook = SetWinEventHook(
                EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

            _destroyHook = SetWinEventHook(
                EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
                IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

            if (_debounceTimer == null)
            {
                _debounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(DebounceMs)
                };
                _debounceTimer.Tick += OnDebounceTick;
            }

            if (_pollTimer == null)
            {
                _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
                };
                _pollTimer.Tick += OnPollTick;
            }

            _isRunning = true;

            EvaluateForeground();
            CheckWindowExistence();
            UpdatePollingState();

            if (_lastFocused)
            {
                EvaluateBounds(GetForegroundWindow(), force: true);
            }
        }

        public static void Stop()
        {
            if (!_isRunning) return;

            if (_foregroundHook != IntPtr.Zero) { UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
            if (_locationHook != IntPtr.Zero) { UnhookWinEvent(_locationHook); _locationHook = IntPtr.Zero; }
            if (_destroyHook != IntPtr.Zero) { UnhookWinEvent(_destroyHook); _destroyHook = IntPtr.Zero; }

            _debounceTimer?.Stop();
            _pollTimer?.Stop();
            _pendingHwnd = IntPtr.Zero;

            _isRunning = false;
            _lastFocused = false;
            _lastWindowExists = false;
            _lastHwnd = IntPtr.Zero;
            _lastLeft = _lastTop = _lastWidth = _lastHeight = int.MinValue;
            _lastTitle = string.Empty;
            _lastProcessName = string.Empty;
            LastBounds = WindowBounds.Empty;
        }

        #endregion

        #region 事件回调

        private static void OnWinEvent(
            IntPtr hook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;

            switch (eventType)
            {
                case EVENT_SYSTEM_FOREGROUND:
                    EvaluateForeground();
                    break;

                case EVENT_OBJECT_LOCATIONCHANGE:
                    {
                        bool isTarget = _lastHwnd != IntPtr.Zero
                            ? hwnd == _lastHwnd
                            : IsTargetProcess(hwnd);
                        if (!isTarget) return;

                        _pendingHwnd = hwnd;
                        _debounceTimer?.Stop();
                        _debounceTimer?.Start();
                    }
                    break;

                case EVENT_OBJECT_DESTROY:
                    {
                        if (_lastHwnd == IntPtr.Zero || hwnd != _lastHwnd) return;
                        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
                            new Action(CheckWindowExistence),
                            DispatcherPriority.Background);
                    }
                    break;
            }
        }

        private static void OnDebounceTick(object? sender, EventArgs e)
        {
            _debounceTimer?.Stop();

            IntPtr hwnd = _pendingHwnd;
            _pendingHwnd = IntPtr.Zero;

            if (hwnd == IntPtr.Zero || !IsTargetProcess(hwnd)) return;

            EvaluateBounds(hwnd, force: false);
        }

        private static void OnPollTick(object? sender, EventArgs e)
        {
            if (_lastFocused) return;
            CheckWindowExistence();
        }

        #endregion

        #region 判定逻辑

        private static void EvaluateForeground()
        {
            bool focused = IsTargetProcess(GetForegroundWindow());

            if (focused == _lastFocused) return;

            _lastFocused = focused;
            FocusChanged?.Invoke(null, focused);

            UpdatePollingState();

            if (focused)
            {
                EvaluateBounds(GetForegroundWindow(), force: true);
            }
        }

        private static void UpdatePollingState()
        {
            if (_pollTimer == null) return;

            if (_lastFocused)
            {
                if (_pollTimer.IsEnabled) _pollTimer.Stop();
            }
            else
            {
                if (!_pollTimer.IsEnabled) _pollTimer.Start();
            }
        }

        private static void CheckWindowExistence()
        {
            if (!_isRunning) return;

            IntPtr found = IntPtr.Zero;

            if (_lastHwnd != IntPtr.Zero && IsWindow(_lastHwnd) && IsTargetProcess(_lastHwnd))
            {
                found = _lastHwnd;
            }
            else
            {
                found = FindTargetWindow();
            }

            bool exists = found != IntPtr.Zero;

            if (exists)
            {
                if (found != _lastHwnd)
                {
                    _lastHwnd = found;
                }

                var titleBuilder = new StringBuilder(256);
                GetWindowText(found, titleBuilder, titleBuilder.Capacity);
                _lastTitle = titleBuilder.ToString();

                try
                {
                    GetWindowThreadProcessId(found, out uint pid);
                    if (pid != 0)
                        _lastProcessName = Process.GetProcessById((int)pid).ProcessName;
                }
                catch { }

                // 非最小化时读坐标/分辨率；最小化跳过
                if (!IsIconic(found))
                {
                    EvaluateBounds(found, force: false);
                }

                if (!_lastWindowExists)
                {
                    _lastWindowExists = true;
                    WindowExistenceChanged?.Invoke(null, true);
                }
                return;
            }

            if (_lastWindowExists)
            {
                _lastWindowExists = false;
                _lastHwnd = IntPtr.Zero;
                _lastTitle = string.Empty;
                _lastProcessName = string.Empty;
                _lastLeft = _lastTop = _lastWidth = _lastHeight = int.MinValue;

                if (!LastBounds.IsEmpty)
                {
                    LastBounds = WindowBounds.Empty;
                    BoundsChanged?.Invoke(null, WindowBounds.Empty);
                }

                if (_lastFocused)
                {
                    _lastFocused = false;
                    FocusChanged?.Invoke(null, false);
                }

                WindowExistenceChanged?.Invoke(null, false);
            }
        }

        private static IntPtr FindTargetWindow()
        {
            IntPtr result = IntPtr.Zero;

            EnumWindows((hwnd, lParam) =>
            {
                var cls = new StringBuilder(256);
                GetClassName(hwnd, cls, cls.Capacity);
                if (!cls.ToString().Equals(TargetWindowClass, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!IsTargetProcess(hwnd)) return true;

                result = hwnd;
                return false;
            }, IntPtr.Zero);

            return result;
        }

        private static void EvaluateBounds(IntPtr hwnd, bool force)
        {
            if (hwnd == IntPtr.Zero || !IsTargetProcess(hwnd))
            {
                if (_lastHwnd != IntPtr.Zero && IsTargetProcess(_lastHwnd))
                    hwnd = _lastHwnd;
                else
                    return;
            }

            if (!GetClientRect(hwnd, out RECT client)) return;

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;

            // 0x0（最小化）直接无视
            if (width <= 0 || height <= 0) return;

            POINT pt = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref pt)) return;

            int left = pt.X;
            int top = pt.Y;

            var titleBuilder = new StringBuilder(256);
            GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString();

            string procName = string.Empty;
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                    procName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch { }

            if (!force &&
                hwnd == _lastHwnd &&
                left == _lastLeft && top == _lastTop &&
                width == _lastWidth && height == _lastHeight &&
                title == _lastTitle &&
                procName == _lastProcessName)
                return;

            _lastHwnd = hwnd;
            _lastLeft = left;
            _lastTop = top;
            _lastWidth = width;
            _lastHeight = height;
            _lastTitle = title;
            _lastProcessName = procName;

            var bounds = new WindowBounds(hwnd, left, top, width, height, title, procName);
            LastBounds = bounds;

            BoundsChanged?.Invoke(null, bounds);
        }

        private static bool IsTargetProcess(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return false;

                string name = Process.GetProcessById((int)pid).ProcessName;
                return name.Equals(TargetProcessName1, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(TargetProcessName2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}