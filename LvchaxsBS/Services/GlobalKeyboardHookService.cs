using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace LvchaxsBS.Services
{
    public enum KeyboardEventType
    {
        KeyDown,
        KeyUp
    }

    public class KeyboardEventArgs : EventArgs
    {
        public KeyboardEventType EventType { get; set; }
        public int VirtualKeyCode { get; set; }
        public string KeyName { get; set; } = string.Empty;
    }

    public static class GlobalKeyboardHookService
    {
        // Win32 API 常量
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelKeyboardProc _proc;
        private static bool _isStarted = false;

        // ==================== 监听开关（0=不监听，1=监听） ====================
        public static int ListenKeyDown = 1;   // 按下
        public static int ListenKeyUp = 1;     // 抬起

        public static event EventHandler<KeyboardEventArgs>? KeyboardEvent;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern int GetKeyNameText(int lParam, System.Text.StringBuilder lpString, int nSize);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void Start()
        {
            if (_isStarted) return;

            _proc = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }

            _isStarted = _hookId != IntPtr.Zero;
            if (!_isStarted)
            {
                throw new InvalidOperationException("键盘钩子注册失败！");
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
                    var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int wParamInt = wParam.ToInt32();
                    int vkCode = (int)hookStruct.vkCode;

                    var args = new KeyboardEventArgs
                    {
                        VirtualKeyCode = vkCode,
                        KeyName = GetKeyName(vkCode, hookStruct.scanCode)
                    };

                    switch (wParamInt)
                    {
                        case WM_KEYDOWN:
                        case WM_SYSKEYDOWN:
                            if (ListenKeyDown == 0) break;
                            args.EventType = KeyboardEventType.KeyDown;
                            KeyboardEvent?.Invoke(null, args);
                            break;

                        case WM_KEYUP:
                        case WM_SYSKEYUP:
                            if (ListenKeyUp == 0) break;
                            args.EventType = KeyboardEventType.KeyUp;
                            KeyboardEvent?.Invoke(null, args);
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

        public static string GetKeyName(int vkCode, uint scanCode = 0)
        {
            try
            {
                // 特殊按键处理
                switch (vkCode)
                {
                    case 0x08: return "Backspace";   // 退格键
                    case 0x09: return "Tab";         // 制表键
                    case 0x0D: return "Enter";       // 回车键
                    case 0x10: return "Shift";       // Shift键
                    case 0x11: return "Ctrl";        // Ctrl键
                    case 0x12: return "Alt";         // Alt键
                    case 0x14: return "CapsLock";    // 大写锁定键
                    case 0x1B: return "Esc";         // 退出键
                    case 0x20: return "Space";       // 空格键
                    case 0x21: return "PageUp";      // 上翻页键
                    case 0x22: return "PageDown";    // 下翻页键
                    case 0x23: return "End";         // 结束键
                    case 0x24: return "Home";        // 起始键
                    case 0x25: return "←";           // 左箭头键
                    case 0x26: return "↑";           // 上箭头键
                    case 0x27: return "→";           // 右箭头键
                    case 0x28: return "↓";           // 下箭头键
                    case 0x2D: return "Insert";      // 插入键
                    case 0x2E: return "Delete";      // 删除键
                    case 0x5B: return "Win";         // Windows键
                    case 0x5C: return "Win";         // 右Win键（和左Win共用显示）
                    case 0x90: return "NumLock";     // 数字锁定键
                    case 0x91: return "ScrollLock";  // 滚动锁定键
                    case 0xA0: return "左Shift";     // 左Shift键
                    case 0xA1: return "右Shift";     // 右Shift键
                    case 0xA2: return "左Ctrl";      // 左Ctrl键
                    case 0xA3: return "右Ctrl";      // 右Ctrl键
                    case 0xA4: return "左Alt";       // 左Alt键
                    case 0xA5: return "右Alt";       // 右Alt键

                    // 标点符号键
                    case 0xC0: return "`~";    // 反引号
                    case 0xBD: return "_-";    // 减号
                    case 0xBB: return "+=";    // 等号
                    case 0xDB: return "[{";    // 左方括号
                    case 0xDD: return "]}";    // 右方括号
                    case 0xDC: return "\\|";   // 反斜杠
                    case 0xBA: return ":;";    // 分号
                    case 0xDE: return "'\"";   // 单引号
                    case 0xBC: return "<,";    // 逗号
                    case 0xBE: return ">.";    // 句号
                    case 0xBF: return "?/";    // 斜杠

                    // 小键盘符号
                    case 0x6A: return "Num*";        // 小键盘乘号
                    case 0x6F: return "Num/";        // 小键盘除号
                    case 0x6D: return "Num-";        // 小键盘减号
                    case 0x6B: return "Num+";        // 小键盘加号
                    case 0x6E: return "Num.";        // 小键盘小数点

                    // 媒体键（可选）
                    case 0xAD: return "音量-";        // 音量减
                    case 0xAE: return "音量+";        // 音量加
                    case 0xB1: return "静音";         // 静音

                    default:
                        // F1-F12
                        if (vkCode >= 0x70 && vkCode <= 0x7B)
                        {
                            return $"F{vkCode - 0x6F}";
                        }
                        // 数字键 0-9
                        if (vkCode >= 0x30 && vkCode <= 0x39)
                        {
                            return ((char)vkCode).ToString();
                        }
                        // 字母键 A-Z
                        if (vkCode >= 0x41 && vkCode <= 0x5A)
                        {
                            return ((char)vkCode).ToString();
                        }
                        // 小键盘数字
                        if (vkCode >= 0x60 && vkCode <= 0x69)
                        {
                            return $"Num{vkCode - 0x60}";
                        }
                        return $"VK_{vkCode:X2}";
                }
            }
            catch
            {
                return $"VK_{vkCode:X2}";
            }
        }
    }
}