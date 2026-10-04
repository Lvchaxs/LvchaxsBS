using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LvchaxsBS.Services
{
    public enum MouseEventType
    {
        LeftButtonDown,
        LeftButtonUp,
        RightButtonDown,
        RightButtonUp,
        MiddleButtonDown,
        MiddleButtonUp,
        MouseWheelUp,
        MouseWheelDown,
        XButton1Down,
        XButton1Up,
        XButton2Down,
        XButton2Up
    }

    public class MouseEventArgs : EventArgs
    {
        public MouseEventType EventType { get; set; }
        public int Delta { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int XButton { get; set; }
    }

    public static class GlobalMouseHookService
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;

        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelMouseProc _proc;
        private static bool _isStarted = false;

        public static int ListenLeftButton = 1;
        public static int ListenRightButton = 1;
        public static int ListenMiddleButton = 1;
        public static int ListenMouseWheel = 1;
        public static int ListenXButton1 = 1;
        public static int ListenXButton2 = 1;

        public static event EventHandler<MouseEventArgs>? MouseEvent;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>
        /// 获取鼠标事件的中文/自定义显示名称
        /// </summary>
        public static string GetMouseEventName(MouseEventType eventType)
        {
            switch (eventType)
            {
                case MouseEventType.LeftButtonDown: return "左键";
                case MouseEventType.LeftButtonUp: return "左键抬起";
                case MouseEventType.RightButtonDown: return "右键";
                case MouseEventType.RightButtonUp: return "右键抬起";
                case MouseEventType.MiddleButtonDown: return "中键";
                case MouseEventType.MiddleButtonUp: return "中键抬起";
                case MouseEventType.MouseWheelUp: return "滚轮↑";
                case MouseEventType.MouseWheelDown: return "滚轮↓";
                case MouseEventType.XButton1Down: return "前进键";
                case MouseEventType.XButton1Up: return "前进键抬起";
                case MouseEventType.XButton2Down: return "后退键";
                case MouseEventType.XButton2Up: return "后退键抬起";
                default: return eventType.ToString();
            }
        }

        public static void Start()
        {
            if (_isStarted) return;

            _proc = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }

            _isStarted = _hookId != IntPtr.Zero;
            if (!_isStarted)
            {
                throw new InvalidOperationException("鼠标钩子注册失败！");
            }
        }

        public static void Stop()
        {
            if (!_isStarted) return;

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _isStarted = false;
        }

        public static bool IsStarted => _isStarted;

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var mouseData = (int)hookStruct.mouseData;
                    int wParamInt = wParam.ToInt32();

                    var args = new MouseEventArgs
                    {
                        X = hookStruct.pt.x,
                        Y = hookStruct.pt.y
                    };

                    switch (wParamInt)
                    {
                        case WM_LBUTTONDOWN:
                            if (ListenLeftButton == 0) break;
                            args.EventType = MouseEventType.LeftButtonDown;
                            MouseEvent?.Invoke(null, args);
                            break;
                        case WM_LBUTTONUP:
                            if (ListenLeftButton == 0) break;
                            args.EventType = MouseEventType.LeftButtonUp;
                            MouseEvent?.Invoke(null, args);
                            break;

                        case WM_RBUTTONDOWN:
                            if (ListenRightButton == 0) break;
                            args.EventType = MouseEventType.RightButtonDown;
                            MouseEvent?.Invoke(null, args);
                            break;
                        case WM_RBUTTONUP:
                            if (ListenRightButton == 0) break;
                            args.EventType = MouseEventType.RightButtonUp;
                            MouseEvent?.Invoke(null, args);
                            break;

                        case WM_MBUTTONDOWN:
                            if (ListenMiddleButton == 0) break;
                            args.EventType = MouseEventType.MiddleButtonDown;
                            MouseEvent?.Invoke(null, args);
                            break;
                        case WM_MBUTTONUP:
                            if (ListenMiddleButton == 0) break;
                            args.EventType = MouseEventType.MiddleButtonUp;
                            MouseEvent?.Invoke(null, args);
                            break;

                        case WM_MOUSEWHEEL:
                            if (ListenMouseWheel == 0) break;
                            int delta = (short)((mouseData >> 16) & 0xFFFF);
                            args.Delta = delta;
                            args.EventType = delta > 0 ? MouseEventType.MouseWheelUp : MouseEventType.MouseWheelDown;
                            MouseEvent?.Invoke(null, args);
                            break;

                        case WM_XBUTTONDOWN:
                            int xButtonDown = (mouseData >> 16) & 0xFFFF;
                            if (xButtonDown == 1 && ListenXButton1 == 0) break;
                            if (xButtonDown == 2 && ListenXButton2 == 0) break;
                            args.XButton = xButtonDown;
                            if (xButtonDown == 1)
                                args.EventType = MouseEventType.XButton2Down;
                            else if (xButtonDown == 2)
                                args.EventType = MouseEventType.XButton1Down;
                            MouseEvent?.Invoke(null, args);
                            break;

                        case WM_XBUTTONUP:
                            int xButtonUp = (mouseData >> 16) & 0xFFFF;
                            if (xButtonUp == 1 && ListenXButton1 == 0) break;
                            if (xButtonUp == 2 && ListenXButton2 == 0) break;
                            args.XButton = xButtonUp;
                            if (xButtonUp == 1)
                                args.EventType = MouseEventType.XButton2Up;
                            else if (xButtonUp == 2)
                                args.EventType = MouseEventType.XButton1Up;
                            MouseEvent?.Invoke(null, args);
                            break;
                    }
                }
                catch
                {
                    // 忽略异常
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}