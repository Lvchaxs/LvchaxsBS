using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace LvchaxsBS.Services
{
    public enum GamepadButton
    {
        DPadUp, DPadDown, DPadLeft, DPadRight,
        A, B, X, Y,
        L1, R1, L2, R2, L3, R3,
        Create, Options, PS, TouchPad, Mic
    }

    public class GamepadEventArgs : EventArgs
    {
        public GamepadButton Button { get; set; }
        public bool IsPressed { get; set; }
        public string DeviceName { get; set; } = string.Empty;
    }

    /// <summary>
    /// 手柄全局钩子服务
    /// - PS5 手柄：Raw Input（事件驱动），Start(window) 时自动启动
    /// - Xbox 手柄：XInput（轮询），由 StartXInput() / StopXInput() 控制
    /// 对外统一暴露 GamepadEvent
    /// </summary>
    public static class GlobalGamepadHookService
    {
        #region Win32 / Raw Input

        private const int WM_INPUT = 0x00FF;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEHID = 2;

        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_GAMEPAD = 0x05;
        private const ushort HID_USAGE_JOYSTICK = 0x04;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        #endregion

        #region XInput

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        private const ushort XINPUT_DPAD_UP = 0x0001;
        private const ushort XINPUT_DPAD_DOWN = 0x0002;
        private const ushort XINPUT_DPAD_LEFT = 0x0004;
        private const ushort XINPUT_DPAD_RIGHT = 0x0008;
        private const ushort XINPUT_START = 0x0010;
        private const ushort XINPUT_BACK = 0x0020;
        private const ushort XINPUT_LEFT_THUMB = 0x0040;
        private const ushort XINPUT_RIGHT_THUMB = 0x0080;
        private const ushort XINPUT_LEFT_SHOULDER = 0x0100;
        private const ushort XINPUT_RIGHT_SHOULDER = 0x0200;
        private const ushort XINPUT_GUIDE = 0x0400;
        private const ushort XINPUT_A = 0x1000;
        private const ushort XINPUT_B = 0x2000;
        private const ushort XINPUT_X = 0x4000;
        private const ushort XINPUT_Y = 0x8000;

        private const byte XINPUT_TRIGGER_THRESHOLD = 128;
        private const uint XINPUT_ERROR_SUCCESS = 0;
        private const uint XINPUT_MAX_USER_INDEX = 4;
        private const int XINPUT_POLL_INTERVAL_MS = 4;

        #endregion

        #region 状态

        private static HwndSource? _hwndSource;
        private static IntPtr _hwnd = IntPtr.Zero;
        private static bool _isStarted = false;

        // PS5 按键状态
        private static readonly Dictionary<GamepadButton, bool> _lastState = new();

        // PS5 方向键消抖
        private static GamepadButton? _dpadCandidate = null;
        private static int _dpadStableCount = 0;
        private const int DPAD_STABLE_THRESHOLD = 3;

        // Xbox 轮询
        private static CancellationTokenSource? _xinputCts;
        private static Task? _xinputTask;
        private static readonly Dictionary<uint, HashSet<GamepadButton>> _xinputLastState = new();

        /// <summary>
        /// Xbox XInput 轮询当前是否启用
        /// </summary>
        public static bool XInputEnabled { get; private set; } = false;

        public static event EventHandler<GamepadEventArgs>? GamepadEvent;

        public static bool IsStarted => _isStarted;

        #endregion

        #region 启动/停止

        public static void Start(Window window)
        {
            if (_isStarted) return;

            if (window == null)
                throw new ArgumentNullException(nameof(window));

            // ===== PS5：注册 Raw Input =====
            var helper = new WindowInteropHelper(window);
            _hwnd = helper.Handle;

            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("窗口句柄未创建，无法注册手柄钩子。");

            _hwndSource = HwndSource.FromHwnd(_hwnd);
            if (_hwndSource == null)
                throw new InvalidOperationException("无法获取 HwndSource。");

            _hwndSource.AddHook(WndProc);

            var devices = new RAWINPUTDEVICE[]
            {
                new RAWINPUTDEVICE
                {
                    UsagePage = HID_USAGE_PAGE_GENERIC,
                    Usage = HID_USAGE_GAMEPAD,
                    Flags = RIDEV_INPUTSINK,
                    Target = _hwnd
                },
                new RAWINPUTDEVICE
                {
                    UsagePage = HID_USAGE_PAGE_GENERIC,
                    Usage = HID_USAGE_JOYSTICK,
                    Flags = RIDEV_INPUTSINK,
                    Target = _hwnd
                }
            };

            if (!RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                throw new InvalidOperationException("注册 Raw Input 手柄设备失败！");
            }

            _lastState.Clear();
            _dpadCandidate = null;
            _dpadStableCount = 0;

            // Xbox：不自动启动，由 StartXInput() 控制

            _isStarted = true;
        }

        public static void Stop()
        {
            if (!_isStarted) return;

            // 停 Xbox 轮询
            StopXInput();

            // 停 PS5 Raw Input
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            _hwnd = IntPtr.Zero;
            _isStarted = false;
            _lastState.Clear();
            _dpadCandidate = null;
            _dpadStableCount = 0;
        }

        #endregion

        #region XInput 开关

        /// <summary>
        /// 启动 Xbox XInput 轮询
        /// </summary>
        public static void StartXInput()
        {
            if (XInputEnabled) return;

            XInputEnabled = true;
            _xinputLastState.Clear();
            _xinputCts = new CancellationTokenSource();
            _xinputTask = Task.Run(() => XInputPollLoop(_xinputCts.Token));
        }

        /// <summary>
        /// 停止 Xbox XInput 轮询
        /// </summary>
        public static void StopXInput()
        {
            if (!XInputEnabled) return;

            XInputEnabled = false;
            _xinputCts?.Cancel();
            _xinputCts?.Dispose();
            _xinputCts = null;
            _xinputTask = null;
            _xinputLastState.Clear();
        }

        #endregion

        #region PS5：Raw Input 消息处理

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUT)
            {
                try
                {
                    ProcessRawInput(lParam);
                }
                catch
                {
                }
            }
            return IntPtr.Zero;
        }

        private static void ProcessRawInput(IntPtr lParam)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size)
                    return;

                var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
                if (header.Type != RIM_TYPEHID) return;

                int hidOffset = Marshal.SizeOf<RAWINPUTHEADER>();
                uint sizeHid = (uint)Marshal.ReadInt32(buffer, hidOffset);
                uint count = (uint)Marshal.ReadInt32(buffer, hidOffset + 4);

                if (sizeHid == 0 || count == 0) return;

                int dataOffset = hidOffset + 8;
                byte[] report = new byte[sizeHid];
                Marshal.Copy(IntPtr.Add(buffer, dataOffset), report, 0, (int)sizeHid);

                // 只处理 PS5（64 字节）
                if (report.Length != 64) return;

                var pressed = ParsePS5(report);

                foreach (GamepadButton btn in Enum.GetValues(typeof(GamepadButton)))
                {
                    bool now = pressed.Contains(btn);
                    bool last = _lastState.TryGetValue(btn, out var l) && l;

                    if (now != last)
                    {
                        _lastState[btn] = now;
                        GamepadEvent?.Invoke(null, new GamepadEventArgs
                        {
                            Button = btn,
                            IsPressed = now,
                            DeviceName = header.Device.ToString()
                        });
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        #endregion

        #region Xbox：XInput 轮询

        private static async Task XInputPollLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    for (uint i = 0; i < XINPUT_MAX_USER_INDEX; i++)
                    {
                        if (token.IsCancellationRequested) return;

                        uint result = XInputGetState(i, out XINPUT_STATE state);
                        if (result != XINPUT_ERROR_SUCCESS)
                        {
                            _xinputLastState.Remove(i);
                            continue;
                        }

                        var pressed = ParseXInput(state.Gamepad);

                        if (!_xinputLastState.TryGetValue(i, out var last))
                        {
                            last = new HashSet<GamepadButton>();
                            _xinputLastState[i] = last;
                        }

                        foreach (GamepadButton btn in Enum.GetValues(typeof(GamepadButton)))
                        {
                            bool now = pressed.Contains(btn);
                            bool was = last.Contains(btn);

                            if (now != was)
                            {
                                GamepadEvent?.Invoke(null, new GamepadEventArgs
                                {
                                    Button = btn,
                                    IsPressed = now,
                                    DeviceName = $"XInput#{i}"
                                });
                            }
                        }

                        last.Clear();
                        foreach (var b in pressed) last.Add(b);
                    }
                }
                catch
                {
                }

                try
                {
                    await Task.Delay(XINPUT_POLL_INTERVAL_MS, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private static HashSet<GamepadButton> ParseXInput(XINPUT_GAMEPAD gp)
        {
            var result = new HashSet<GamepadButton>();
            ushort b = gp.wButtons;

            if ((b & XINPUT_DPAD_UP) != 0) result.Add(GamepadButton.DPadUp);
            if ((b & XINPUT_DPAD_DOWN) != 0) result.Add(GamepadButton.DPadDown);
            if ((b & XINPUT_DPAD_LEFT) != 0) result.Add(GamepadButton.DPadLeft);
            if ((b & XINPUT_DPAD_RIGHT) != 0) result.Add(GamepadButton.DPadRight);

            if ((b & XINPUT_A) != 0) result.Add(GamepadButton.A);
            if ((b & XINPUT_B) != 0) result.Add(GamepadButton.B);
            if ((b & XINPUT_X) != 0) result.Add(GamepadButton.X);
            if ((b & XINPUT_Y) != 0) result.Add(GamepadButton.Y);

            if ((b & XINPUT_LEFT_SHOULDER) != 0) result.Add(GamepadButton.L1);
            if ((b & XINPUT_RIGHT_SHOULDER) != 0) result.Add(GamepadButton.R1);

            if ((b & XINPUT_LEFT_THUMB) != 0) result.Add(GamepadButton.L3);
            if ((b & XINPUT_RIGHT_THUMB) != 0) result.Add(GamepadButton.R3);

            if ((b & XINPUT_BACK) != 0) result.Add(GamepadButton.Create);
            if ((b & XINPUT_START) != 0) result.Add(GamepadButton.Options);
            if ((b & XINPUT_GUIDE) != 0) result.Add(GamepadButton.PS);

            if (gp.bLeftTrigger >= XINPUT_TRIGGER_THRESHOLD) result.Add(GamepadButton.L2);
            if (gp.bRightTrigger >= XINPUT_TRIGGER_THRESHOLD) result.Add(GamepadButton.R2);

            return result;
        }

        #endregion

        #region PS5：解析

        private static HashSet<GamepadButton> ParsePS5(byte[] report)
        {
            var result = new HashSet<GamepadButton>();

            byte b8 = report[8];
            byte b9 = report[9];
            byte b10 = report[10];

            // 方向键：低 4 位 + 消抖
            GamepadButton? currentDpad = null;
            switch ((byte)(b8 & 0x0F))
            {
                case 0x00: currentDpad = GamepadButton.DPadUp; break;
                case 0x02: currentDpad = GamepadButton.DPadRight; break;
                case 0x04: currentDpad = GamepadButton.DPadDown; break;
                case 0x06: currentDpad = GamepadButton.DPadLeft; break;
                case 0x08: currentDpad = null; break;
                default: currentDpad = null; break;
            }

            if (currentDpad == _dpadCandidate)
            {
                _dpadStableCount++;
            }
            else
            {
                _dpadCandidate = currentDpad;
                _dpadStableCount = 1;
            }

            if (_dpadStableCount >= DPAD_STABLE_THRESHOLD && currentDpad.HasValue)
            {
                result.Add(currentDpad.Value);
            }

            // ABXY
            if ((b8 & 0x10) != 0) result.Add(GamepadButton.X);
            if ((b8 & 0x20) != 0) result.Add(GamepadButton.A);
            if ((b8 & 0x40) != 0) result.Add(GamepadButton.B);
            if ((b8 & 0x80) != 0) result.Add(GamepadButton.Y);

            // 肩键 + 扳机 + 摇杆 + Create/Options
            if ((b9 & 0x01) != 0) result.Add(GamepadButton.L1);
            if ((b9 & 0x02) != 0) result.Add(GamepadButton.R1);
            if ((b9 & 0x04) != 0) result.Add(GamepadButton.L2);
            if ((b9 & 0x08) != 0) result.Add(GamepadButton.R2);
            if ((b9 & 0x10) != 0) result.Add(GamepadButton.Create);
            if ((b9 & 0x20) != 0) result.Add(GamepadButton.Options);
            if ((b9 & 0x40) != 0) result.Add(GamepadButton.L3);
            if ((b9 & 0x80) != 0) result.Add(GamepadButton.R3);

            // PS / 触摸板 / 麦克风
            if ((b10 & 0x01) != 0) result.Add(GamepadButton.PS);
            if ((b10 & 0x02) != 0) result.Add(GamepadButton.TouchPad);
            if ((b10 & 0x04) != 0) result.Add(GamepadButton.Mic);

            return result;
        }

        #endregion

        #region 按键名称

        public static string GetButtonName(GamepadButton button)
        {
            return button switch
            {
                GamepadButton.DPadUp => "方向键上",
                GamepadButton.DPadDown => "方向键下",
                GamepadButton.DPadLeft => "方向键左",
                GamepadButton.DPadRight => "方向键右",
                GamepadButton.A => "A/×",
                GamepadButton.B => "B/○",
                GamepadButton.X => "X/□",
                GamepadButton.Y => "Y/△",
                GamepadButton.L1 => "L1/LB",
                GamepadButton.R1 => "R1/RB",
                GamepadButton.L2 => "L2/LT",
                GamepadButton.R2 => "R2/RT",
                GamepadButton.L3 => "L3",
                GamepadButton.R3 => "R3",
                GamepadButton.Create => "Create/Back",
                GamepadButton.Options => "Options/Start",
                GamepadButton.PS => "PS/Guide",
                GamepadButton.TouchPad => "触摸板",
                GamepadButton.Mic => "麦克风",
                _ => button.ToString()
            };
        }

        #endregion
    }
}