using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// ★ 屏幕实时标记：在桌面最上层铺一个整屏的透明窗口（点击穿透、不抢焦点），
    /// 把「程序识别到了什么、识别在哪里」直接画在屏幕上 —— 每个命中一个十字 + 一行说明
    /// （绿 = 命中 / 红 = 没命中；文字内容由调用方给，例如「2P队友 (1234,567) 93%」）。
    /// <para>
    /// ★★ 绘制走的是**逐像素 alpha 的 <c>UpdateLayeredWindow</c>**（和宿主字幕覆盖层同一条合成路径），
    /// 不是颜色键 —— 颜色键那种在 DWM 硬件叠加面上容易被吃掉、看不见。
    /// </para>
    /// <para>
    /// ★ 纯 Win32，不依赖 WPF（本工程是共享库，宿主与 UID 都引它，不能反过来依赖宿主）。
    /// 窗口建在**自己的后台线程**上（带消息循环），绘制与宿主 UI 线程无关。
    /// ★ 标记有 TTL（默认 1.5 秒）：调用方不再上报就自动消失，窗口也会隐藏。
    /// </para>
    /// </summary>
    public static class DebugMarkerOverlay
    {
        /// <summary>总开关（默认开）。</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>标记在屏幕上保留多久（程序每轮都会重报，过期自动消失）。</summary>
        private const int MarkTtlMs = 1500;

        /// <summary>重绘间隔（同时负责"保顶"）。</summary>
        private const int TimerMs = 100;

        /// <summary>★ 十字臂长 / 圈半径（像素）—— 整体画小一点，别挡住底下的内容。</summary>
        private const int CrossArm = 22;
        private const int CrossRing = 11;

        /// <summary>★ 字号（GDI 逻辑高度，负数值）。小一点，标记只占一丁点地方。</summary>
        private const int FontHeight = -16;

        /// <summary>建窗后显示多久的启动横幅（让用户确认"覆盖层到底出来没有"）。</summary>
        private const int BannerMs = 8000;

        private sealed class MarkInfo
        {
            public string Name = "";
            public int X, Y;          // 屏幕坐标
            public int R, G, B;
            public bool Ok;
            public int OffX, OffY;    // 位置校验查到的偏移（0,0 = 没查到）
            /// <summary>★ 自定义文字（非空时直接用它，不再拼默认的 "名字 (x,y)=r,g,b"）。</summary>
            public string Label = "";
            public long Tick;
        }

        private static readonly object _gate = new();
        private static readonly List<MarkInfo> _marks = new();

        private static Thread? _thread;
        private static IntPtr _hwnd;
        private static int _vx, _vy, _vw, _vh;      // 虚拟屏幕（多显示器整体）范围
        private static WndProcDelegate? _wndProc;   // 必须保活，否则回调被 GC 回收

        // 离屏画面（32bpp 顶朝下 DIB，逐像素 alpha）
        private static IntPtr _memDC, _dib, _oldDib, _dibBits;
        private static byte[]? _zero;               // 清屏用（全 0 = 全透明）
        private static int _surfaceW, _surfaceH;

        /// <summary>本帧"画过东西"的区域（这些区域要补 alpha）。</summary>
        private static readonly List<RECT> _dirty = new();

        private static int _presentFailures, _renderErrors, _fontFailLogged;
        private static bool _presentOkLogged;

        private static long _bannerUntil;
        private static string _bannerText = "";

        #region 对外接口

        /// <summary>报告一个"正在检查的点"（同一个名字会覆盖上一次的）。</summary>
        public static void Report(string name, int x, int y, int r, int g, int b, bool ok,
            int offX = 0, int offY = 0)
        {
            if (!Enabled) return;

            try
            {
                lock (_gate)
                {
                    var m = _marks.Find(t => t.Name == name);
                    if (m == null)
                    {
                        m = new MarkInfo { Name = name };
                        _marks.Add(m);
                    }
                    m.X = x; m.Y = y;
                    m.R = r; m.G = g; m.B = b;
                    m.Ok = ok;
                    m.OffX = offX; m.OffY = offY;
                    m.Label = "";
                    m.Tick = Environment.TickCount64;
                }

                Start();     // 第一次用到时才建窗、起线程
            }
            catch { }
        }

        /// <summary>
        /// ★ 报告一个「识别命中」：位置 + 自己写的说明文字（绿十字；没命中就别调，或者 <paramref name="ok"/> 传 false）。
        /// <para>文字里一般放「模板名 + 屏幕坐标 + 匹配度」，例如 <c>2P队友 (1234,567) 93%</c>。</para>
        /// </summary>
        public static void Report(string name, int x, int y, string label, bool ok = true)
        {
            if (!Enabled) return;

            try
            {
                lock (_gate)
                {
                    var m = _marks.Find(t => t.Name == name);
                    if (m == null)
                    {
                        m = new MarkInfo { Name = name };
                        _marks.Add(m);
                    }
                    m.X = x; m.Y = y;
                    m.Ok = ok;
                    m.Label = label ?? "";
                    m.OffX = 0; m.OffY = 0;
                    m.Tick = Environment.TickCount64;
                }

                Start();
            }
            catch { }
        }

        /// <summary>撤掉所有标记（窗口会被隐藏）。</summary>
        public static void Clear()
        {
            lock (_gate) _marks.Clear();
        }

        /// <summary>
        /// ★ 只撤掉**名字以 <paramref name="namePrefix"/> 开头**的那组标记。
        /// <para>几处检测同时在画标记时（例如「队友消息」和人数用的「联机队友」），
        /// 各清各的，互不干扰 —— 全清会把别人刚画上去的一起抹掉。</para>
        /// </summary>
        public static void Clear(string namePrefix)
        {
            if (string.IsNullOrEmpty(namePrefix)) { Clear(); return; }

            lock (_gate)
                _marks.RemoveAll(m => m.Name != null && m.Name.StartsWith(namePrefix, StringComparison.Ordinal));
        }

        #endregion

        #region 建窗 + 消息循环（自己的后台线程）

        private static void Start()
        {
            if (_thread != null) return;
            lock (_gate)
            {
                if (_thread != null) return;
                _thread = new Thread(Loop) { IsBackground = true, Name = "LvchaxsBS-诊断标记" };
                _thread.Start();
            }
        }

        private static void Loop()
        {
            try
            {
                _vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
                _vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
                _vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                _vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
                if (_vw <= 0) { _vw = GetSystemMetrics(SM_CXSCREEN); _vx = 0; }
                if (_vh <= 0) { _vh = GetSystemMetrics(SM_CYSCREEN); _vy = 0; }
                if (_vw <= 0 || _vh <= 0) { Note("★诊断标记：屏幕尺寸取不到，放弃覆盖层"); return; }

                _wndProc = WndProc;

                var wc = new WNDCLASSW
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = GetModuleHandleW(null),
                    lpszClassName = "LvchaxsBS_DiagMarkerOverlay"
                };
                ushort atom = RegisterClassW(ref wc);

                _hwnd = CreateWindowExW(
                    WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                    "LvchaxsBS_DiagMarkerOverlay", "LvchaxsBS 诊断标记",
                    WS_POPUP,
                    _vx, _vy, _vw, _vh,
                    IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

                if (_hwnd == IntPtr.Zero)
                {
                    Note($"★诊断标记：窗口创建失败（atom={atom}，GetLastError={Marshal.GetLastWin32Error()}）→ 看不到十字");
                    return;
                }

                if (!CreateSurface(_vw, _vh))
                {
                    Note($"★诊断标记：离屏画面创建失败（{_vw}x{_vh}）→ 看不到十字");
                    return;
                }

                // 先亮一个横幅：让用户能立刻确认"覆盖层到底出来没有"
                _bannerText = $"LvchaxsBS 检测标记已启动   {_vw}x{_vh}@{_vx},{_vy}   DPI={GetDpiForSystem()}";
                _bannerUntil = Environment.TickCount64 + BannerMs;

                ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
                SetTimer(_hwnd, 1, TimerMs, IntPtr.Zero);
                Render();      // 立刻画一次（别等 100ms）

                Note($"诊断标记窗口已创建 hwnd=0x{_hwnd.ToInt64():X}  覆盖 {_vw}x{_vh}@{_vx},{_vy}"
                     + $"  DPI={GetDpiForSystem()}  （屏幕上的十字=程序正在检查的点：绿=匹配 红=不匹配 黄=实际读到位置）");

                while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }
            catch (Exception ex)
            {
                try { Note($"★诊断标记：线程异常 {ex.Message}"); } catch { }
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (msg)
                {
                    case WM_TIMER:
                        if (HasFreshMarks() || Environment.TickCount64 < _bannerUntil)
                        {
                            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                            Render();
                        }
                        else if (IsWindowVisible(hwnd))
                        {
                            ShowWindow(hwnd, SW_HIDE);
                        }
                        return IntPtr.Zero;

                    case WM_DESTROY:
                    case WM_CLOSE:
                        PostQuitMessage(0);
                        return IntPtr.Zero;
                }
            }
            catch { }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        private static bool HasFreshMarks()
        {
            long now = Environment.TickCount64;
            lock (_gate)
            {
                _marks.RemoveAll(m => now - m.Tick > MarkTtlMs);
                return _marks.Count > 0;
            }
        }

        private static MarkInfo[] SnapshotMarks()
        {
            long now = Environment.TickCount64;
            lock (_gate)
            {
                _marks.RemoveAll(m => now - m.Tick > MarkTtlMs);
                return _marks.ToArray();
            }
        }

        private static void Note(string text)
        {
            try { Debug.WriteLine($"[标记] {DateTime.Now:HH:mm:ss}  {text}"); } catch { }
        }

        #endregion

        #region 离屏画面（32bpp DIB + 逐像素 alpha）

        private static bool CreateSurface(int w, int h)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return false;

            try
            {
                _memDC = CreateCompatibleDC(screenDc);
                if (_memDC == IntPtr.Zero) return false;

                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                        biWidth = w,
                        biHeight = -h,          // 负数 = 顶朝下
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0
                    }
                };

                _dib = CreateDIBSection(_memDC, ref bmi, DIB_RGB_COLORS, out _dibBits, IntPtr.Zero, 0);
                if (_dib == IntPtr.Zero || _dibBits == IntPtr.Zero) return false;

                _oldDib = SelectObject(_memDC, _dib);
                _surfaceW = w; _surfaceH = h;
                _zero = new byte[w * h * 4];    // 全 0 = 全透明
                return true;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>把一块区域内"被画过（非纯黑）"的像素补上 alpha=255 —— GDI 画出来的像素 alpha 是 0，不补就看不见。</summary>
        private static void MakeOpaque(int left, int top, int right, int bottom)
        {
            if (_dibBits == IntPtr.Zero || _zero == null) return;

            int x0 = Math.Max(0, left), y0 = Math.Max(0, top);
            int x1 = Math.Min(_surfaceW, right), y1 = Math.Min(_surfaceH, bottom);

            for (int y = y0; y < y1; y++)
            {
                IntPtr row = IntPtr.Add(_dibBits, y * _surfaceW * 4);
                for (int x = x0; x < x1; x++)
                {
                    IntPtr p = IntPtr.Add(row, x * 4);
                    byte b = Marshal.ReadByte(p);
                    byte g = Marshal.ReadByte(p, 1);
                    byte r = Marshal.ReadByte(p, 2);
                    if (r != 0 || g != 0 || b != 0) Marshal.WriteByte(p, 3, 255);
                }
            }
        }

        private static void Render()
        {
            try
            {
                if (_memDC == IntPtr.Zero || _zero == null || _hwnd == IntPtr.Zero) return;

                // 清屏（全透明）
                Marshal.Copy(_zero, 0, _dibBits, _zero.Length);
                _dirty.Clear();

                IntPtr hFont = CreateFontW(FontHeight, 0, 0, 0, FW_BOLD, 0, 0, 0, DEFAULT_CHARSET,
                    0, 0, 0, 0, "Microsoft YaHei");
                if (hFont == IntPtr.Zero && _fontFailLogged++ == 0)
                    Note("★诊断标记：字体创建失败（文字画不出来，但还是能看到十字）");
                IntPtr oldFont = SelectObject(_memDC, hFont);
                IntPtr nullBrush = GetStockObject(NULL_BRUSH);
                IntPtr oldBrush = SelectObject(_memDC, nullBrush);
                SetBkMode(_memDC, TRANSPARENT);

                try
                {
                    // ① 启动横幅（左上角大字，8 秒）——用来确认"覆盖层到底出来没有"
                    if (Environment.TickCount64 < _bannerUntil && _bannerText.Length > 0)
                    {
                        var bsz = MeasureText(_memDC, _bannerText);
                        if (DrawLabel(_memDC, 20, 20, _bannerText, 0x0000FFFFu))
                            _dirty.Add(new RECT { Left = 14, Top = 16, Right = 20 + bsz.cx + 10, Bottom = 20 + bsz.cy + 6 });
                    }

                    // ② 每个检查点：十字 + 圈 + 文字（绿=匹配、红=不匹配）
                    var marks = SnapshotMarks();
                    int labelDy = 0;
                    foreach (var m in marks)
                    {
                        int x = m.X - _vx;
                        int y = m.Y - _vy;
                        uint color = m.Ok ? 0x0000E000u : 0x000000FFu;    // COLORREF = 0x00BBGGRR

                        DrawCross(_memDC, x, y, color);
                        _dirty.Add(new RECT { Left = x - 30, Top = y - 30, Right = x + 30, Bottom = y + 30 });

                        // 黄十字 = 位置校验查出的"程序实际读到的内容所在处"
                        if (m.OffX != 0 || m.OffY != 0)
                        {
                            int ox = x + m.OffX, oy = y + m.OffY;
                            DrawCross(_memDC, ox, oy, 0x0000FFFFu);
                            _dirty.Add(new RECT { Left = ox - 30, Top = oy - 30, Right = ox + 30, Bottom = oy + 30 });
                        }

                        string text = m.Label.Length > 0
                            ? m.Label
                            : $"{m.Name} ({m.X},{m.Y})={m.R},{m.G},{m.B} {(m.Ok ? "✓匹配" : "✗不匹配")}";
                        var sz = MeasureText(_memDC, text);
                        int tx = x + 18;
                        int ty = y - sz.cy - 10;
                        if (tx + sz.cx > _surfaceW - 4) tx = Math.Max(4, _surfaceW - sz.cx - 4);
                        if (ty < 4) { ty = Math.Min(_surfaceH - sz.cy - 4, y + 18 + labelDy * (sz.cy + 6)); labelDy++; }

                        DrawLabel(_memDC, tx, ty, text, color);
                        _dirty.Add(new RECT { Left = tx - 6, Top = ty - 5, Right = tx + sz.cx + 6, Bottom = ty + sz.cy + 5 });
                    }
                }
                finally
                {
                    SelectObject(_memDC, oldFont);
                    DeleteObject(hFont);
                    SelectObject(_memDC, oldBrush);
                }

                // ★★★ 必须 flush：GDI 是**批量提交**的，不 flush 就直接读 DIB 像素会读不到刚画的内容，
                //     于是"补 alpha"那一步看到的全是 0 → 整帧全透明 → 屏幕上什么都看不到（2026-10-16 踩过）。
                GdiFlush();

                // 给"被画过"的像素补 alpha=255（GDI 画的像素 alpha 是 0，不补就完全透明）
                foreach (var rc in _dirty) MakeOpaque(rc.Left, rc.Top, rc.Right, rc.Bottom);

                // ③ 一次性合成到屏幕（逐像素 alpha）
                var dst = new POINT { X = _vx, Y = _vy };
                var size = new SIZE { cx = _surfaceW, cy = _surfaceH };
                var src = new POINT { X = 0, Y = 0 };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                bool ok = UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref dst, ref size, _memDC, ref src, 0, ref blend, ULW_ALPHA);
                if (!ok && _presentFailures++ < 3)
                    Note($"★诊断标记：UpdateLayeredWindow 失败 GetLastError={Marshal.GetLastWin32Error()} → 看不到十字");
                else if (ok && !_presentOkLogged)
                {
                    _presentOkLogged = true;
                    Note($"诊断标记：首帧已合成到屏幕（{_surfaceW}x{_surfaceH}@{_vx},{_vy}，标记 {SnapshotMarks().Length} 个）");
                }
            }
            catch (Exception ex)
            {
                if (_renderErrors++ < 3) Note($"★诊断标记：绘制异常 {ex.Message}");
            }
        }

        private static bool DrawCross(IntPtr hdc, int x, int y, uint color)
        {
            // ★ 尺寸经过两轮调整：4K 下 46px/线宽 6 是"大到挡视线"（2026-10-17 用户嫌太大），
            //   现在收到 22px、线宽 2 —— 看得见、又不挡住底下的聊天内容。
            IntPtr pen = CreatePen(PS_SOLID, 2, color);
            IntPtr old = SelectObject(hdc, pen);
            MoveToEx(hdc, x - CrossArm, y, IntPtr.Zero); LineTo(hdc, x + CrossArm, y);
            MoveToEx(hdc, x, y - CrossArm, IntPtr.Zero); LineTo(hdc, x, y + CrossArm);
            Ellipse(hdc, x - CrossRing, y - CrossRing, x + CrossRing, y + CrossRing);
            SelectObject(hdc, old);
            DeleteObject(pen);

            // 中心实心点（更容易被看到）
            IntPtr brush = CreateSolidBrush(color);
            IntPtr oldB = SelectObject(hdc, brush);
            Ellipse(hdc, x - 3, y - 3, x + 3, y + 3);
            SelectObject(hdc, oldB);
            DeleteObject(brush);
            return true;
        }

        private static bool DrawLabel(IntPtr hdc, int x, int y, string text, uint color)
        {
            if (text.Length == 0) return false;

            var sz = MeasureText(hdc, text);
            var rc = new RECT { Left = x - 2, Top = y - 1, Right = x + sz.cx + 2, Bottom = y + sz.cy + 1 };

            // 底衬用**深灰**（不是纯黑）：MakeOpaque 靠"非纯黑"识别被画过的像素
            IntPtr bg = CreateSolidBrush(0x00202020);
            FillRect(hdc, ref rc, bg);
            DeleteObject(bg);

            SetTextColor(hdc, color);
            TextOutW(hdc, x, y, text, text.Length);
            return true;
        }

        private static SIZE MeasureText(IntPtr hdc, string text)
        {
            var sz = new SIZE();
            try { GetTextExtentPoint32W(hdc, text, text.Length, ref sz); } catch { }
            return sz;
        }

        #endregion

        #region Win32

        private const uint WS_POPUP = 0x80000000;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_HIDE = 0;
        private const uint WM_DESTROY = 0x0002;
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_TIMER = 0x0113;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int PS_SOLID = 0;
        private const int FW_BOLD = 700;
        private const uint DEFAULT_CHARSET = 1;
        private const int NULL_BRUSH = 5;
        private const int TRANSPARENT = 1;
        private const uint DIB_RGB_COLORS = 0;
        private const uint ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        private static readonly IntPtr HWND_TOPMOST = new(-1);

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSW
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX, ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public uint[] bmiColors;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(
            int dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG lpMsg);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int nExitCode);
        [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT lprc, IntPtr hbr);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        // ⚠️⚠️ P/Invoke 的 dll 名写错 = **只有真正调用时才抛** `Unable to find an entry point`，
        //    被 try/catch 一吞就是"窗口建了却什么都看不到"。2026-10-16 连踩两次：
        //    ① SetBkMode/SetTextColor/GetTextExtentPoint32W 错写成 user32（实为 gdi32）；
        //    ② FillRect 错写成 gdi32（实为 **user32**）。
        //    ⇒ 以后加任何 Win32 声明，**先用脚本验证归属**，别靠记忆。已核对过的分布：
        //      user32：窗口/消息/DC(GetDC·ReleaseDC)/绘制原语(FillRect·DrawTextW)；
        //      gdi32 ：画笔·画刷·字体·DIB·选入对象·BitBlt·GdiFlush·SetBkMode·SetTextColor·TextOutW。
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint crColor);
        [DllImport("gdi32.dll")] private static extern bool GetTextExtentPoint32W(IntPtr hdc, string lpString, int c, ref SIZE psizl);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool TextOutW(IntPtr hdc, int x, int y, string lpString, int c);

        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint crColor);
        [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr lppt);
        [DllImport("gdi32.dll")] private static extern bool LineTo(IntPtr hdc, int x, int y);
        [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int i);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFontW(
            int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
            uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
            uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

        #endregion
    }
}
