using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LvchaxsBS_Macro.Core
{
    /// <summary>
    /// 「监听下一个鼠标键」用的**低级鼠标钩子**（<c>WH_MOUSE_LL</c>）。
    ///
    /// <para>
    /// ★ 为什么不直接用 WPF 的 <c>PreviewMouseDown</c>：
    ///   和键盘同理 —— 本插件是反射加载进宿主的独立 dll，宿主窗口是透明置顶窗，
    ///   WPF 事件只在焦点确实落在那个控件上时才到得了；而且用户想去**游戏窗口**上点一下，
    ///   那种情况下宿主根本收不到事件。全局钩子与焦点无关，一定收得到。
    /// </para>
    /// <para>
    /// ★ 只报**左 / 右 / 中**三个键（以及两个侧键但不填进面板）：
    ///   宏引擎的 <c>MouseDown/MouseUp/MouseClick</c> 只认 <c>Left / Right / Middle</c>。
    ///   滚轮（Wheel）不算「鼠标键」，不在这里处理。
    /// </para>
    /// <para>
    /// ★ 过滤 <c>LLMHF_INJECTED</c>：宏执行时自己模拟出来的点击不算。
    /// </para>
    /// </summary>
    public static class MouseWatcher
    {
        // ---- 面板里用的「鼠标键」编号（不是 Win32 的 VK 值，省得混淆）----
        public const int ButtonLeft = 1;
        public const int ButtonRight = 2;
        public const int ButtonMiddle = 3;

        // ---- Win32 ----
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const uint LLMHF_INJECTED = 0x01;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            // ★★ 必须先把 POINT 放前面！MSLLHOOKSTRUCT 的第一个字段是 pt(x,y)，
            //   少了它后面所有字段全部错位 —— 读到的 mouseData 其实是鼠标的 y 坐标、
            //   flags 其实是 x 坐标，于是 `(flags & INJECTED) == 0` 这个判断基本靠运气，
            //   表现就是「点了监听按钮、按左/右/中键，压根没反应」。
            public int x;
            public int y;

            public uint mouseData;   // XBUTTON 时高 16 位是 1/2
            public uint flags;       // LLMHF_INJECTED 在这里
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>按下的鼠标键（<see cref="ButtonLeft"/> 等）。★ 侧键不会触发（引擎不支持）。</summary>
        public static event Action<int>? MouseButtonPressed;

        /// <summary>钩子是否已装上。</summary>
        public static bool IsWatching { get; private set; }

        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelMouseProc? _proc;   // ★ 必须持有引用，否则被 GC 回收 → 钩子失效

        /// <summary>
        /// 开始监听下一个鼠标键。重复调用安全。★ 必须在 UI 线程调用。
        /// <para>★ <c>hMod</c> 传 <see cref="IntPtr.Zero"/>。实测（探针验证）两种写法都能装上：
        ///   传 0 和传进程模块句柄都返回有效钩子句柄，事件也都能收到 ——
        ///   所以这里保持 0（全局钩子的标准写法），别再为此纠结。</para>
        /// </summary>
        public static bool Start()
        {
            if (IsWatching) return true;

            try
            {
                _proc = HookCallback;
                _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);

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
            IsWatching = false;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var hs = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    int msg = wParam.ToInt32();

                    // ★ 只处理「按下」，抬起不管（避免一次点击报两次）
                    int button = 0;
                    if (msg == WM_LBUTTONDOWN) button = ButtonLeft;
                    else if (msg == WM_RBUTTONDOWN) button = ButtonRight;
                    else if (msg == WM_MBUTTONDOWN) button = ButtonMiddle;
                    // ★ 侧键（XButton）**不报**：宏引擎不认，报上去也填不进去，白白打断监听

                    if (button != 0 && (hs.flags & LLMHF_INJECTED) == 0)
                    {
                        MouseButtonPressed?.Invoke(button);
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
