using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LvchaxsBS_UID.Utils.GameWindow
{
    /// <summary>
    /// 把原神客户区调到前台（俗称「激活窗口」）。
    ///
    /// <para>
    /// 只做窗口激活，不含截图 / 图像识别 / 模拟输入 —— 那些能力在共享库
    /// <c>LvchaxsBS_Resources</c> 里。这里是插件侧的独立实现，
    /// 自带 P/Invoke，<b>不引用宿主任何类型</b>（插件是 Assembly.LoadFrom 载入的，
    /// 与宿主没有编译期引用）。
    /// </para>
    /// <para>
    /// <b>为什么不用一句 SetForegroundWindow 了事</b>：
    /// Windows 有「前台锁定」——只有当前前台进程、或刚收到过输入事件的进程，
    /// 才被允许调用 SetForegroundWindow。项目里试过直接调，出现「点了没反应」。
    /// 所以这里沿用旧版项目 GenshinWindowActivator.ActivateWindow 的那套序列：
    /// </para>
    /// <list type="number">
    ///   <item>窗口最小化时，先用 <c>SendMessage(WM_SYSCOMMAND, SC_RESTORE)</c> 还原 ——
    ///         走系统命令比直接 ShowWindow 更容易绕开前台锁定；</item>
    ///   <item>再 <c>ShowWindow</c> 确保窗口可见（非最小化时也补一次 SW_SHOW）；</item>
    ///   <item>最后 <c>SetForegroundWindow</c>，被挡下则退 <c>BringWindowToTop</c> 抬到 Z 序顶部。</item>
    /// </list>
    /// </summary>
    public static class GameWindowActivator
    {
        /// <summary>原神主窗口的窗口类名（GenshinImpact.exe / YuanShen.exe 都是它）。</summary>
        private const string GENSHIN_WINDOW_CLASS = "UnityWndClass";

        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        private const uint WM_SYSCOMMAND = 0x0112;
        private const uint SC_RESTORE = 0xF120;

        #region Native Methods
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        #endregion

        /// <summary>
        /// 找原神主窗口句柄。找不到（游戏没启动）返回 <see cref="IntPtr.Zero"/>。
        /// </summary>
        public static IntPtr FindGameWindow()
        {
            IntPtr hwnd = FindWindow(GENSHIN_WINDOW_CLASS, null);
            return (hwnd != IntPtr.Zero && IsWindow(hwnd)) ? hwnd : IntPtr.Zero;
        }

        /// <summary>
        /// 找到并激活原神窗口。返回是否成功。
        /// </summary>
        public static bool Activate()
        {
            IntPtr hwnd = FindGameWindow();
            if (hwnd == IntPtr.Zero)
            {
                Debug.WriteLine("[UID/激活] 未找到原神窗口（UnityWndClass），游戏可能没启动");
                return false;
            }

            return Activate(hwnd);
        }

        /// <summary>
        /// 激活指定窗口；最小化时先还原。
        ///
        /// <para>
        /// 注意：<b>调用方要在自己的窗口还处于前台时调用</b>（例如按钮点击处理里、
        /// 关闭模态窗口之前），否则前台锁定会把这次激活挡掉。
        /// </para>
        /// </summary>
        public static bool Activate(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                return false;

            try
            {
                if (IsIconic(hWnd))
                {
                    // 走系统命令还原，比直接 ShowWindow 更能绕开前台锁定
                    SendMessage(hWnd, WM_SYSCOMMAND, (IntPtr)SC_RESTORE, IntPtr.Zero);
                    Thread.Sleep(300);
                    ShowWindow(hWnd, SW_RESTORE);
                    ShowWindow(hWnd, SW_SHOWNORMAL);
                }
                else
                {
                    ShowWindow(hWnd, SW_SHOW);
                }

                // 等窗口状态落定再抢前台
                Thread.Sleep(200);

                bool ok = SetForegroundWindow(hWnd);
                if (!ok)
                {
                    // 被前台锁定挡下时，至少把它抬到 Z 序顶部
                    ok = BringWindowToTop(hWnd);
                }

                Debug.WriteLine($"[UID/激活] 激活窗口 0x{hWnd.ToInt64():X} → {(ok ? "成功" : "失败")}");
                return ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/激活] 激活窗口异常: {ex.Message}");
                return false;
            }
        }
    }
}
