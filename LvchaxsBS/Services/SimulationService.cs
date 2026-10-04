using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LvchaxsBS.Core
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

        private static void BeginSimulation()
        {
            Interlocked.Increment(ref _isSimulating);
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
        public static void PressZ()
        {
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
    }
}