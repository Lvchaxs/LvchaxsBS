using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LvchaxsBS_Macro.Core
{
    public static class SimulationService
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr lstrcpy(IntPtr dest, string src);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        // 键盘按键
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_F = 0x46;          // F键
        private const byte VK_SPACE = 0x20;      // 空格键
        private const byte VK_ESCAPE = 0x1B;     // Esc键
        private const byte VK_Z = 0x5A;          // Z键
        private const byte VK_R = 0x52;          // R键
        private const byte VK_T = 0x54;          // T键
        private const byte VK_TAB = 0x09;        // Tab键
        private const byte VK_RETURN = 0x0D;     // Enter（回车）键
        private const byte VK_BACK = 0x08;       // Backspace（退格）键
        private const byte VK_F2 = 0x71;         // F2 键（主界面按下打开联机大厅）
        private const byte VK_CONTROL = 0x11;    // Ctrl键
        private const byte VK_V = 0x56;          // V键
        private const uint CF_UNICODETEXT = 13;  // 剪贴板 Unicode 文本格式
        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;

        // 鼠标按键
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;    // 鼠标左键按下
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;      // 鼠标左键释放
        private const uint MOUSEEVENTF_WHEEL = 0x0800;       // 鼠标滚轮
        private const uint MOUSEEVENTF_MOVE = 0x0001;        // 鼠标移动（相对）
        private const uint WHEEL_DELTA = 120;                // 滚轮滚动单位

        #endregion

        #region 模拟标志

        private static int _isSimulating = 0;

        public static bool IsSimulating => _isSimulating > 0;

        /// <summary>
        /// ★ 最后一次**模拟动作**（按键 / 点击 / 滚轮）的时刻（<see cref="Environment.TickCount64"/>）。
        /// <para>
        /// 诊断用：拿它和宿主报的「用户空闲」时间对照 —— 如果空闲时间每次都在本程序刚按完键后归零，
        /// 就说明模拟按键被宿主的 <c>UserInputMonitor</c> 误当成了用户真实操作
        /// （表现：让路永远结束不了，看着像卡死）。
        /// </para>
        /// </summary>
        public static long LastSimTicks { get; private set; }

        private static void BeginSimulation()
        {
            Interlocked.Increment(ref _isSimulating);
            LastSimTicks = Environment.TickCount64;
        }

        private static void EndSimulation()
        {
            Interlocked.Decrement(ref _isSimulating);
        }

        #endregion

        #region 键盘模拟

        // 按下F键
        public static void PressF()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_F, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_F, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>
        /// 按下 F2 键 —— 主界面里打开「联机大厅」（多人游戏）界面的快捷键。
        /// <para>★ 注意用 <b>虚拟键码</b> 0x71，不是 '2' 的字符码。</para>
        /// </summary>
        public static void PressF2()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_F2, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_F2, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        // 按下空格键
        public static void PressSpace()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_SPACE, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_SPACE, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        // 按下Esc键
        public static void PressEscape()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        // 按下Z键
        public static void PressZ()        {
            BeginSimulation();
            try
            {
                keybd_event(VK_Z, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_Z, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        // 按下R键
        public static void PressR()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_R, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_R, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        // 按下T键
        public static void PressT()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_T, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_T, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        // 按下Tab键
        public static void PressTab()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_TAB, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>
        /// Ctrl+A 全选当前输入框里的内容。
        /// ★ 给「重新输入账密」用：现在**回到输入页就无条件重输**（见 AccountLoginFlow），
        ///   重输次数比以前多得多。粘贴前不清框的话，上一次若真粘进去了就会**首尾相接变成两份**
        ///   （账号变成「账号账号」），所以必须先全选再删掉。
        /// </summary>
        public static void SelectAllText()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event((byte)'A', 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event((byte)'A', 0, KEYEVENTF_KEYUP, IntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>退格删除一格（配合 <see cref="SelectAllText"/> 清空输入框）。</summary>
        public static void PressBackspace()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_BACK, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_BACK, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>清空当前输入框：Ctrl+A → Backspace。</summary>
        public static void ClearInputField()
        {
            SelectAllText();
            PressBackspace();
        }

        // 按下Enter（回车）键
        public static void PressEnter()
        {
            BeginSimulation();
            try
            {
                keybd_event(VK_RETURN, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>
        /// 把文本设进剪贴板，再发一次 Ctrl+V 粘贴到当前焦点控件里。
        /// 登录流程输账号 / 密码用（逐键打字太慢，且中文输入法会插足）。
        /// </summary>
        public static void PasteText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (!SetClipboardText(text))
            {
                // 剪贴板偶尔会被别的进程占着（刚复制过东西），缓一下再试一次
                Thread.Sleep(50);
                if (!SetClipboardText(text)) return;
            }

            Thread.Sleep(50);

            BeginSimulation();
            try
            {
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                keybd_event(VK_V, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                Thread.Sleep(10);
                keybd_event(VK_V, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }

            Thread.Sleep(50);
        }

        /// <summary>
        /// Win32 方式设置剪贴板文本（不依赖 STA 线程，调用方在后台线程也能用）。
        /// 打开剪贴板失败会重试几次 —— 那是剪贴板正被其他进程占用。
        /// </summary>
        /// <remarks>
        /// 公开出来是为了「粘完密码后立刻覆盖掉」：密码不该在剪贴板里过夜，
        /// 用账号 UID（玩家公开 ID，非秘密）顶替即可。
        /// </remarks>
        public static bool SetClipboardText(string text)
        {
            IntPtr hGlobal = IntPtr.Zero;

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    if (!OpenClipboard(IntPtr.Zero))
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    try
                    {
                        EmptyClipboard();
                        uint sizeInBytes = (uint)((text.Length + 1) * 2);
                        hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)sizeInBytes);
                        if (hGlobal == IntPtr.Zero) return false;

                        IntPtr lockedPtr = GlobalLock(hGlobal);
                        if (lockedPtr == IntPtr.Zero) return false;

                        lstrcpy(lockedPtr, text);
                        GlobalUnlock(lockedPtr);

                        if (SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero)
                        {
                            // 系统接管了这块内存的所有权，不能再释放
                            hGlobal = IntPtr.Zero;
                            return true;
                        }

                        return false;
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (hGlobal != IntPtr.Zero) GlobalFree(hGlobal);
            }
        }

        #endregion

        #region 鼠标点击

        // 鼠标左键点击指定坐标
        public static void LeftClickAt(int x, int y)
        {
            BeginSimulation();
            try
            {
                SetCursorPos(x, y);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        // 鼠标左键点击当前位置
        public static void LeftClick()
        {
            BeginSimulation();
            try
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                Thread.Sleep(1);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        #endregion

        #region 鼠标移动

        // 只移动鼠标到指定坐标（不滚动、不点击）
        public static void MoveTo(int x, int y)
        {
            BeginSimulation();
            try
            {
                SetCursorPos(x, y);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>
        /// 相对移动鼠标（用于转 3D 游戏视角）。
        /// dx 正=右，负=左；dy 正=下，负=上。
        /// 游戏需处于锁定鼠标状态，移动量才会驱动视角旋转。
        /// </summary>
        public static void MoveRelative(int dx, int dy)
        {
            BeginSimulation();
            try
            {
                mouse_event(MOUSEEVENTF_MOVE, unchecked((uint)dx), unchecked((uint)dy), 0, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>
        /// 分步平滑相对移动鼠标（用于转 3D 游戏视角）。
        /// 把总位移 (dx, dy) 拆成 steps 步，每步之间等 stepDelayMs。
        /// </summary>
        public static void MoveRelativeSmooth(int dx, int dy, int steps = 10, int stepDelayMs = 10)
        {
            if (steps < 1) steps = 1;

            BeginSimulation();
            try
            {
                for (int i = 0; i < steps; i++)
                {
                    int stepX = dx / steps;
                    int stepY = dy / steps;

                    // 最后一步补足余数，避免累计误差
                    if (i == steps - 1)
                    {
                        stepX = dx - (dx / steps) * (steps - 1);
                        stepY = dy - (dy / steps) * (steps - 1);
                    }

                    mouse_event(MOUSEEVENTF_MOVE, unchecked((uint)stepX), unchecked((uint)stepY), 0, IntPtr.Zero);

                    if (stepDelayMs > 0)
                        Thread.Sleep(stepDelayMs);
                }
            }
            finally
            {
                EndSimulation();
            }
        }

        #endregion

        #region 鼠标滚轮

        // 鼠标滚轮向下滚动，amount为滚动次数
        public static void ScrollDown(int amount = 1)
        {
            BeginSimulation();
            try
            {
                for (int i = 0; i < amount; i++)
                {
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)-WHEEL_DELTA), IntPtr.Zero);
                }
            }
            finally
            {
                EndSimulation();
            }
        }

        // 鼠标滚轮向上滚动，amount为滚动次数
        public static void ScrollUp(int amount = 1)
        {
            BeginSimulation();
            try
            {
                for (int i = 0; i < amount; i++)
                {
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, WHEEL_DELTA, IntPtr.Zero);
                }
            }
            finally
            {
                EndSimulation();
            }
        }

        // 只在当前位置向下滚动（不移动鼠标）
        public static void ScrollDownOnly(int amount = 1)
        {
            BeginSimulation();
            try
            {
                for (int i = 0; i < amount; i++)
                {
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)-WHEEL_DELTA), IntPtr.Zero);
                }
            }
            finally
            {
                EndSimulation();
            }
        }

        // 只在当前位置向上滚动（不移动鼠标）
        public static void ScrollUpOnly(int amount = 1)
        {
            BeginSimulation();
            try
            {
                for (int i = 0; i < amount; i++)
                {
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, WHEEL_DELTA, IntPtr.Zero);
                }
            }
            finally
            {
                EndSimulation();
            }
        }

        #endregion

        #region 通用模拟（键鼠宏脚本用）

        /// <summary>
        /// 鼠标按键 —— 宏脚本与通用模拟统一用它（下标 = 宏引擎里的参数值）。
        /// </summary>
        public enum MouseButtonKind
        {
            Left = 0,
            Middle = 1,
            Right = 2,
        }

        // 鼠标按键常量（左键那两个在上面「鼠标点击」区里）
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const byte VK_SHIFT = 0x10;

        /// <summary>把一个字符翻译成「虚拟键码 + 要不要按 Shift」（-1 = 这个字符敲不出来）。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern short VkKeyScan(char ch);

        /// <summary>按住一个键（不抬起）。<paramref name="vk"/> = 虚拟键码（0x57 = W）。</summary>
        public static void KeyDown(int vk)
        {
            BeginSimulation();
            try
            {
                keybd_event((byte)vk, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>抬起一个键。</summary>
        public static void KeyUp(int vk)
        {
            BeginSimulation();
            try
            {
                keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>按一下：按下 →（按住 holdMs 毫秒）→ 抬起。holdMs=0 时按下即抬起。</summary>
        public static void KeyPress(int vk, int holdMs = 0)
        {
            BeginSimulation();
            try
            {
                keybd_event((byte)vk, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                if (holdMs > 0) Thread.Sleep(holdMs);
                keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>按名字按住一个键（名字不认识就什么都不做，不抛异常）。</summary>
        public static void KeyDownName(string? name)
        {
            if (TryGetKeyCode(name, out int vk)) KeyDown(vk);
        }

        /// <summary>按名字抬起一个键。</summary>
        public static void KeyUpName(string? name)
        {
            if (TryGetKeyCode(name, out int vk)) KeyUp(vk);
        }

        /// <summary>按名字按一下（可指定按住毫秒数）。</summary>
        public static void KeyPressName(string? name, int holdMs = 0)
        {
            if (TryGetKeyCode(name, out int vk)) KeyPress(vk, holdMs);
        }

        /// <summary>键名 → 虚拟键码（大小写不敏感）。认识返回 true。</summary>
        public static bool TryGetKeyCode(string? name, out int vk)
        {
            vk = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;
            return KeyMap.TryGetValue(name.Trim(), out vk);
        }

        private static System.Collections.Generic.Dictionary<string, int>? _keyMap;

        private static System.Collections.Generic.Dictionary<string, int> KeyMap
            => _keyMap ??= BuildKeyMap();

        /// <summary>
        /// 键名表（宏脚本里写什么名字都能认）。
        /// <para>★ 用 <c>OrdinalIgnoreCase</c>：脚本里写 <c>w</c> 和 <c>W</c> 都算 W 键。</para>
        /// </summary>
        private static System.Collections.Generic.Dictionary<string, int> BuildKeyMap()
        {
            var map = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (char c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;
            for (int i = 0; i <= 9; i++)
            {
                map[i.ToString()] = 0x30 + i;          // 主键盘数字
                map["Num" + i] = 0x60 + i;             // 小键盘
                map["Numpad" + i] = 0x60 + i;
            }
            for (int i = 1; i <= 24; i++) map["F" + i] = 0x6F + i;   // F1=0x70 … F24

            map["Space"] = 0x20;
            map["Enter"] = 0x0D;
            map["Return"] = 0x0D;
            map["NumpadEnter"] = 0x0D;
            map["Esc"] = 0x1B;
            map["Escape"] = 0x1B;
            map["Tab"] = 0x09;
            map["Back"] = 0x08;
            map["Backspace"] = 0x08;
            map["Delete"] = 0x2E;
            map["Del"] = 0x2E;
            map["Insert"] = 0x2D;
            map["Ins"] = 0x2D;

            map["Shift"] = 0x10;
            map["LShift"] = 0xA0;
            map["RShift"] = 0xA1;
            map["Ctrl"] = 0x11;
            map["Control"] = 0x11;
            map["LCtrl"] = 0xA2;
            map["RCtrl"] = 0xA3;
            map["Alt"] = 0x12;
            map["LAlt"] = 0xA4;
            map["RAlt"] = 0xA5;
            map["Win"] = 0x5B;
            map["LWin"] = 0x5B;
            map["RWin"] = 0x5C;

            map["Up"] = 0x26;
            map["Down"] = 0x28;
            map["Left"] = 0x25;
            map["Right"] = 0x27;
            map["Home"] = 0x24;
            map["End"] = 0x23;
            map["PageUp"] = 0x21;
            map["PageDown"] = 0x22;
            map["CapsLock"] = 0x14;
            map["NumLock"] = 0x90;
            map["PrintScreen"] = 0x2C;
            map["Pause"] = 0x13;

            map["Minus"] = 0xBD;
            map["Hyphen"] = 0xBD;
            map["Plus"] = 0xBB;
            map["Equal"] = 0xBB;
            map["Comma"] = 0xBC;
            map["Period"] = 0xBE;
            map["Dot"] = 0xBE;
            map["Slash"] = 0xBF;
            map["Semicolon"] = 0xBA;
            map["Quote"] = 0xDE;
            map["LBracket"] = 0xDB;
            map["RBracket"] = 0xDD;
            map["Backslash"] = 0xDC;
            map["Backtick"] = 0xC0;
            map["Grave"] = 0xC0;

            return map;
        }

        /// <summary>按住鼠标某个键（不松开）。</summary>
        public static void MouseDown(MouseButtonKind button)
        {
            BeginSimulation();
            try
            {
                mouse_event(DownFlag(button), 0, 0, 0, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>松开鼠标某个键。</summary>
        public static void MouseUp(MouseButtonKind button)
        {
            BeginSimulation();
            try
            {
                mouse_event(UpFlag(button), 0, 0, 0, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>点一下鼠标：按下 →（按住 holdMs）→ 松开。</summary>
        public static void MouseClick(MouseButtonKind button, int holdMs = 0)
        {
            BeginSimulation();
            try
            {
                mouse_event(DownFlag(button), 0, 0, 0, IntPtr.Zero);
                if (holdMs > 0) Thread.Sleep(holdMs);
                mouse_event(UpFlag(button), 0, 0, 0, IntPtr.Zero);
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>
        /// 滚轮滚动（在当前位置，不移动鼠标）。
        /// <para>ticks 正 = 向上滚，负 = 向下滚（一格 = WHEEL_DELTA）。</para>
        /// </summary>
        public static void MouseScroll(int ticks)
        {
            if (ticks == 0) return;

            BeginSimulation();
            try
            {
                for (int i = 0; i < Math.Abs(ticks); i++)
                {
                    uint delta = ticks > 0 ? WHEEL_DELTA : unchecked((uint)-WHEEL_DELTA);
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, delta, IntPtr.Zero);
                }
            }
            finally
            {
                EndSimulation();
            }
        }

        /// <summary>
        /// 逐字符敲入文本（用 <c>VkKeyScan</c> 翻译，需要 Shift 的自动带 Shift）。
        /// <para>敲不出来的字符（比如中文）会跳过 —— 那种场景用现成的 <see cref="PasteText"/>（剪贴板粘贴）。</para>
        /// </summary>
        public static void TypeText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;

            BeginSimulation();
            try
            {
                foreach (char c in text)
                {
                    short v = VkKeyScan(c);
                    if (v == -1) continue;              // 这个字符没有对应按键 → 跳过

                    int vk = v & 0xFF;
                    bool needShift = ((v >> 8) & 1) != 0;

                    if (needShift) keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                    keybd_event((byte)vk, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
                    keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
                    if (needShift) keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
                }
            }
            finally
            {
                EndSimulation();
            }
        }

        private static uint DownFlag(MouseButtonKind button) => button switch
        {
            MouseButtonKind.Left => MOUSEEVENTF_LEFTDOWN,
            MouseButtonKind.Middle => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_RIGHTDOWN,
        };

        private static uint UpFlag(MouseButtonKind button) => button switch
        {
            MouseButtonKind.Left => MOUSEEVENTF_LEFTUP,
            MouseButtonKind.Middle => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_RIGHTUP,
        };

        #endregion
    }
}