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

        /// <summary>
        /// 这个按键是不是**程序自己注入**的（<c>LLKHF_INJECTED</c>）——
        /// 也就是 <c>keybd_event</c> / <c>SendInput</c> 发出来的模拟按键，不是用户真按的。
        /// <para>★ 判断"是不是用户操作"要靠它，<b>不能只靠 <c>SimulationService.IsSimulating</c></b>：
        /// 低级钩子回调是在消息循环里跑的，模拟事件常常在 <c>EndSimulation()</c> 之后才被回调处理，
        /// 那时标志已经复位 —— 于是"自己按的键"被当成"用户按的键"。</para>
        /// </summary>
        public bool IsInjected { get; set; }

        /// <summary>
        /// 这次 KeyDown 是不是「按着不放时系统自动重复」补发出来的（★ 长按型宏必须挡掉它）。
        /// <para>
        /// 按住一个键大约 0.5 秒后，Windows 会以 ~30 次/秒的频率重复投递 WM_KEYDOWN ——
        /// 如果每条都当成"新按下一次"，`MacroRunner.Start()` 里的 `Stop()` 就会把刚跑了几行的脚本掐掉重来，
        /// 表现就是「脚本永远只执行开头那一小段」。
        /// </para>
        /// <para>
        /// ★ 低级钩子的 <c>lParam</c> 是 <c>KBDLLHOOKSTRUCT*</c>，<b>不是</b>键盘消息的 lParam，
        ///   所以 WM_KEYDOWN 那个 bit30（previous key state）在这里读不到 ——
        ///   只能自己维护一份"当前真实按下的键"来判重。
        /// </para>
        /// </summary>
        public bool IsRepeat { get; set; }
    }

    public static class GlobalKeyboardHookService
    {
        // Win32 API 常量
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        /// <summary>KBDLLHOOKSTRUCT.flags 里的「事件是注入的（模拟按键）」标志位。</summary>
        private const uint LLKHF_INJECTED = 0x10;

        /// <summary>
        /// 「由**更低完整性级别**的进程注入」标志位。
        /// <para>★ 也是模拟输入（只是来源进程权限更低），同样不能当用户的真实按键。</para>
        /// </summary>
        private const uint LLKHF_LOWER_IL_INJECTED = 0x02;

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

        /// <summary>现在**物理**按着的键（只记真实按键，用来判断自动重复）。</summary>
        private static readonly HashSet<int> _physicallyDown = new HashSet<int>();

        private static readonly object _downLock = new object();

        /// <summary>
        /// 某个虚拟键此刻是否被**真实**按住（程序注入的模拟按键不算）。
        /// <para>
        /// ★ 为什么不直接问 <c>GetAsyncKeyState</c>：那个 API 在某些环境（游戏独占输入、
        ///   钩子线程状态尚未同步）下会漏报修饰键 —— 表现为"Ctrl+V 死活不触发"。
        ///   这里的集合由钩子自己的 KeyDown/KeyUp 维护，只要钩子收得到键就一定准确。
        /// </para>
        /// <para>修饰键要用具体值才能区分左右：0xA2 左Ctrl / 0xA3 右Ctrl / 0xA4 左Alt / 0xA5 右Alt。</para>
        /// </summary>
        public static bool IsPhysicallyDown(int vkCode)
        {
            lock (_downLock) return _physicallyDown.Contains(vkCode);
        }

        /// <summary>
        /// 维护"物理按下的键"集合；返回这次 KeyDown 是不是自动重复。
        /// <para>
        /// ★ 注入（模拟）事件<b>完全不参与</b>统计：既不进集合、抬起时也不移除 ——
        ///   否则脚本里模拟按下的键会把真实按键状态带歪（比如脚本按了触发键本身）。
        /// </para>
        /// </summary>
        private static bool TrackRepeat(int vkCode, bool isDown)
        {
            lock (_downLock)
            {
                if (isDown) return !_physicallyDown.Add(vkCode);   // 已在集合里 ⇒ 这次是自动重复
                _physicallyDown.Remove(vkCode);
                return false;
            }
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

                    bool isDown = wParamInt == WM_KEYDOWN || wParamInt == WM_SYSKEYDOWN;
                    bool isUp = wParamInt == WM_KEYUP || wParamInt == WM_SYSKEYUP;

                    // ★ LLKHF_INJECTED = 0x10 / LLKHF_LOWER_IL_INJECTED = 0x02：
                    //   有其中任何一位，这个键就是程序灌进去的（模拟按键），不是用户按的。
                    bool injected = (hookStruct.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;

                    // ★★ 2026-10-23 起：模拟按键**一律不往外发**（只让它作用于游戏本身）——
                    //    模拟输入只能"执行"，不能反过来触发功能 / 宏。
                    //    之前只在各功能里靠 SimulationService.IsSimulating 挡宿主自己灌的键，
                    //    宏插件是独立 dll、另一份 SimulationService ⇒ 它模拟的键照样触发了功能。
                    if (injected) return CallNextHookEx(_hookId, nCode, wParam, lParam);

                    var args = new KeyboardEventArgs
                    {
                        VirtualKeyCode = vkCode,
                        KeyName = GetKeyName(vkCode, hookStruct.scanCode),
                        IsInjected = injected,
                        // ★ 自动重复只对"真实按键"统计（注入事件不进集合，见 TrackRepeat）
                        IsRepeat = !injected && (isDown || isUp) && TrackRepeat(vkCode, isDown),
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