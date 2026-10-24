using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LvchaxsBS_Macro.Core
{
    /// <summary>
    /// 「监听下一个按键」用的**低级键盘钩子**（<c>WH_KEYBOARD_LL</c>）。
    ///
    /// <para>
    /// ★ 为什么不用 WPF 的 <c>PreviewKeyDown</c>：
    ///   键名格是插件里的一个控件，WPF 的键盘事件只在**焦点确实落在该控件上**时才到得了。
    ///   而本插件是**反射加载进宿主 exe** 的独立 dll，宿主窗口又是 <c>AllowsTransparency</c> 的
    ///   透明置顶窗，点击/激活行为与普通窗口不同 —— 焦点经常不在那个控件上，
    ///   于是"点了监听按钮、按了键、什么反应都没有"。
    /// </para>
    /// <para>
    /// ★ 低级钩子是**全局**的：不管前台窗口是不是宿主、哪怕焦点在游戏里，
    ///   按下的键都会到这里 —— 这正好是"录键名"想要的行为。
    ///   回调跑在**安装它的线程**（= UI 线程）的消息循环里，所以可以直接改 UI。
    /// </para>
    /// <para>
    /// ★ 两个过滤：
    ///   ① <c>LLKHF_INJECTED</c> —— 程序自己模拟出来的按键不算（宏执行时正在按的键）。
    ///   ② 自动重复 —— 按住不放时系统会每 ~30ms 补发一次 KeyDown，只认第一次。
    /// </para>
    /// </summary>
    public static class KeyWatcher
    {
        // ---- Win32 ----
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint LLKHF_INJECTED = 0x10;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

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

        /// <summary>按下的键（虚拟键码）。★ 只报**第一次**按下，不报自动重复。</summary>
        public static event Action<int>? KeyPressed;

        /// <summary>钩子是否已装上。</summary>
        public static bool IsWatching { get; private set; }

        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelKeyboardProc? _proc;   // ★ 必须持有引用，否则被 GC 回收 → 钩子失效

        /// <summary>当前物理按着的键（用来判自动重复）。</summary>
        private static readonly HashSet<int> _down = new();

        /// <summary>
        /// 开始监听下一个按键。重复调用是安全的（已装就不再装）。
        /// <para>★ 必须在**有消息循环的线程**（UI 线程）调用。</para>
        /// </summary>
        public static bool Start()
        {
            if (IsWatching) return true;

            try
            {
                _proc = HookCallback;
                using (Process cur = Process.GetCurrentProcess())
                using (ProcessModule mod = cur.MainModule!)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
                }

                IsWatching = _hookId != IntPtr.Zero;
                return IsWatching;
            }
            catch
            {
                _hookId = IntPtr.Zero;
                IsWatching = false;
                return false;
            }
        }

        /// <summary>停止监听（幂等）。</summary>
        public static void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hookId); } catch { /* 忽略 */ }
                _hookId = IntPtr.Zero;
            }
            _proc = null;
            _down.Clear();
            IsWatching = false;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var hs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int msg = wParam.ToInt32();
                    int vk = (int)hs.vkCode;
                    bool injected = (hs.flags & LLKHF_INJECTED) != 0;

                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        // 按住不放 → 系统补发 KeyDown：只认第一次
                        bool repeat = !_down.Add(vk);
                        if (!repeat && !injected)
                        {
                            KeyPressed?.Invoke(vk);
                        }
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        _down.Remove(vk);
                    }
                }
                catch
                {
                    // 钩子回调里绝不能让异常跑出去（会导致整个进程崩）
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}
