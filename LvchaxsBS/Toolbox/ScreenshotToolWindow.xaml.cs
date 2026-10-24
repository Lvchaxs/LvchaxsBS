using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LvchaxsBS.Helpers;
using LvchaxsBS.Services.Hooks;
using WinColor = System.Windows.Media.Color;
using WinPoint = System.Windows.Point;
using WinRect = System.Windows.Shapes.Rectangle;

namespace LvchaxsBS.Toolbox
{
    public partial class ScreenshotToolWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hDC, int X, int Y);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, int rop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);

        private const int DESKTOPVERTRES = 117;
        private const int DESKTOPHORZRES = 118;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const int SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public uint[] bmiColors;
        }

        private enum ResizeHandle
        {
            None,
            TopLeft,
            Top,
            TopRight,
            Right,
            BottomRight,
            Bottom,
            BottomLeft,
            Left
        }

        private enum CopyMode
        {
            Coordinate,
            Region,
            RGB
        }

        private WriteableBitmap _screenCapture;
        private bool _isSelecting = false;
        private WinPoint _startPoint;
        private WinPoint _endPoint;
        private bool _hasSelection = false;
        private bool _isMouseDown = false;

        private CombinedGeometry _maskClipGeometry;

        private const int MAGNIFIER_COLS = 13;
        private const int MAGNIFIER_ROWS = 7;
        private const int PIXEL_SIZE = 16;
        private const int MAGNIFIER_WIDTH = MAGNIFIER_COLS * PIXEL_SIZE;
        private const int MAGNIFIER_HEIGHT = MAGNIFIER_ROWS * PIXEL_SIZE;

        private byte[] _pixelData;
        private int _stride;

        private readonly int _physicalScreenWidth;
        private readonly int _physicalScreenHeight;

        private readonly double _logicalScreenWidth;
        private readonly double _logicalScreenHeight;

        private readonly double _dpiScaleX;
        private readonly double _dpiScaleY;

        private readonly double _maskWidth;
        private readonly double _maskHeight;
        private readonly double _maskOffsetX;
        private readonly double _maskOffsetY;

        private WinPoint _currentMousePosition;
        private bool _isDraggingSelection = false;
        private WinPoint _dragStartPoint;
        private WinPoint _dragStartSelectionPos;

        private ResizeHandle _activeResizeHandle = ResizeHandle.None;
        private WinPoint _resizeStartPoint;
        private double _resizeStartX1;
        private double _resizeStartY1;
        private double _resizeStartX2;
        private double _resizeStartY2;
        private const double HANDLE_SIZE = 8;
        private const double HANDLE_HIT_SIZE = 12;

        private CopyMode _currentCopyMode = CopyMode.Coordinate;
        private WinColor _currentPixelColor = WinColor.FromRgb(0, 0, 0);

        /// <summary>
        /// 坐标显示模式：<c>false</c> = 屏幕坐标（默认），<c>true</c> = 游戏窗口客户区坐标。
        /// <para>★ 由 <c>SHIFT</c> 切换；坐标行、区域行、Z/X 复制的内容都跟着它走。</para>
        /// </summary>
        private bool _useClientCoords = false;

        private const string ScreenCoordLabel = "屏幕坐标";
        private const string ClientCoordLabel = "客户区坐标";

        /// <summary>SHIFT 正按着吗（配合 <see cref="_lastShiftDownTicks"/> 去重）。</summary>
        private bool _shiftDown;

        /// <summary>上一次 SHIFT 按下的时刻（毫秒）。</summary>
        private long _lastShiftDownTicks;

        private WinRect[,] _magnifierPixels;
        private bool _magnifierInitialized = false;

        private Dictionary<int, SolidColorBrush> _brushCache = new Dictionary<int, SolidColorBrush>();

        private bool _suppressMouseMoveMagnifier = false;
        private int _lastCursorX = -1;
        private int _lastCursorY = -1;

        private bool _isKeyboardResizing = false;

        /// <summary>
        /// ★ 同时只开一个截图窗口：全局热键是**右 Shift**，而窗口里的 SHIFT 又是「切换坐标模式」，
        ///   按住/连按会不断补发 KeyDown —— 没有这道闸口就会一叠一叠地弹截图窗口。
        /// </summary>
        private static int _openFlag;

        /// <summary>
        /// 占用「打开截图窗口」的名额。已经开着返回 false，调用方就不该再 new。
        /// <para>★ 原子操作：热键回调在钩子线程，和 UI 线程（按钮入口）会同时来抢。</para>
        /// </summary>
        public static bool TryEnter() => Interlocked.CompareExchange(ref _openFlag, 1, 0) == 0;

        /// <summary>释放名额（窗口关掉 / 打开失败时）。</summary>
        public static void Leave() => Interlocked.Exchange(ref _openFlag, 0);

        public WriteableBitmap CapturedImage { get; private set; }
        public Rect SelectionRectInfo { get; private set; }

        public event EventHandler<WriteableBitmap> ImageCaptured;
        public event EventHandler<Rect> SelectionConfirmed;

        public ScreenshotToolWindow()
        {
            InitializeComponent();

            _physicalScreenWidth = GetPhysicalScreenWidth();
            _physicalScreenHeight = GetPhysicalScreenHeight();

            _logicalScreenWidth = SystemParameters.PrimaryScreenWidth;
            _logicalScreenHeight = SystemParameters.PrimaryScreenHeight;

            _dpiScaleX = _physicalScreenWidth / _logicalScreenWidth;
            _dpiScaleY = _physicalScreenHeight / _logicalScreenHeight;

            _maskWidth = _logicalScreenWidth * 2;
            _maskHeight = _logicalScreenHeight * 2;
            _maskOffsetX = -_logicalScreenWidth / 2;
            _maskOffsetY = -_logicalScreenHeight / 2;

            MagnifierCanvas.Width = MAGNIFIER_WIDTH;
            MagnifierCanvas.Height = MAGNIFIER_HEIGHT;

            InitializeMask();

            this.Loaded += OnLoaded;
            this.PreviewKeyDown += OnKeyDown;
            this.PreviewKeyUp += OnKeyUp;
            this.MouseMove += OnMouseMove;
            this.MouseDown += OnMouseDown;
            this.MouseUp += OnMouseUp;
            this.SizeChanged += OnSizeChanged;
        }

        private int GetPhysicalScreenWidth()
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            int width = GetDeviceCaps(hdc, DESKTOPHORZRES);
            ReleaseDC(IntPtr.Zero, hdc);
            return width;
        }

        private int GetPhysicalScreenHeight()
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            int height = GetDeviceCaps(hdc, DESKTOPVERTRES);
            ReleaseDC(IntPtr.Zero, hdc);
            return height;
        }

        private int LogicalToPhysicalX(double logicalX)
        {
            return (int)Math.Round(logicalX * _dpiScaleX);
        }

        private int LogicalToPhysicalY(double logicalY)
        {
            return (int)Math.Round(logicalY * _dpiScaleY);
        }

        private double PhysicalToLogicalX(int physicalX)
        {
            return physicalX / _dpiScaleX;
        }

        private double PhysicalToLogicalY(int physicalY)
        {
            return physicalY / _dpiScaleY;
        }

        private void InitializeMask()
        {
            var fullScreenRect = new RectangleGeometry(new Rect(
                _maskOffsetX,
                _maskOffsetY,
                _maskWidth,
                _maskHeight
            ));
            MaskPath.Data = fullScreenRect;
            MaskPath.Fill = new SolidColorBrush(WinColor.FromArgb(120, 0, 0, 0));
        }

        private void UpdateMaskHole(double x, double y, double width, double height, bool showHole)
        {
            if (!showHole || width < 2 || height < 2)
            {
                var fullScreenRect = new RectangleGeometry(new Rect(
                    _maskOffsetX,
                    _maskOffsetY,
                    _maskWidth,
                    _maskHeight
                ));
                MaskPath.Data = fullScreenRect;
                return;
            }

            var outerRect = new RectangleGeometry(new Rect(
                _maskOffsetX,
                _maskOffsetY,
                _maskWidth,
                _maskHeight
            ));
            var innerRect = new RectangleGeometry(new Rect(x, y, width, height));

            _maskClipGeometry = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                outerRect,
                innerRect);
            MaskPath.Data = _maskClipGeometry;
        }

        private void InitializeMagnifierPixels()
        {
            if (_magnifierInitialized) return;

            _magnifierPixels = new WinRect[MAGNIFIER_ROWS, MAGNIFIER_COLS];

            for (int row = 0; row < MAGNIFIER_ROWS; row++)
            {
                for (int col = 0; col < MAGNIFIER_COLS; col++)
                {
                    var rect = new WinRect
                    {
                        Width = PIXEL_SIZE,
                        Height = PIXEL_SIZE,
                        Stroke = new SolidColorBrush(WinColor.FromArgb(30, 0, 0, 0)),
                        StrokeThickness = 0.5
                    };

                    Canvas.SetLeft(rect, col * PIXEL_SIZE);
                    Canvas.SetTop(rect, row * PIXEL_SIZE);
                    MagnifierCanvas.Children.Add(rect);
                    _magnifierPixels[row, col] = rect;
                }
            }

            _magnifierInitialized = true;
        }

        private SolidColorBrush GetCachedBrush(WinColor color)
        {
            int key = (color.R << 16) | (color.G << 8) | color.B;
            if (!_brushCache.TryGetValue(key, out var brush))
            {
                brush = new SolidColorBrush(color);
                brush.Freeze();
                _brushCache[key] = brush;
            }
            return brush;
        }

        private WriteableBitmap CaptureScreenByBitBlt(int x, int y, int width, int height, out byte[] pixelData, out int stride)
        {
            pixelData = null;
            stride = 0;

            IntPtr hdcSrc = GetDC(IntPtr.Zero);
            IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            IntPtr hOld = SelectObject(hdcDest, hBitmap);

            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, SRCCOPY);

            SelectObject(hdcDest, hOld);
            DeleteDC(hdcDest);
            ReleaseDC(IntPtr.Zero, hdcSrc);

            stride = (width * 32 + 7) / 8;
            pixelData = new byte[stride * height];

            BITMAPINFO bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            IntPtr hdc = GetDC(IntPtr.Zero);
            int result = GetDIBits(hdc, hBitmap, 0, (uint)height, pixelData, ref bmi, 0);
            ReleaseDC(IntPtr.Zero, hdc);

            DeleteObject(hBitmap);

            if (result == 0)
            {
                pixelData = null;
                return null;
            }

            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            wb.WritePixels(new Int32Rect(0, 0, width, height), pixelData, stride, 0);
            wb.Freeze();

            return wb;
        }

        private async Task CaptureScreenAndUpdateUI()
        {
            WriteableBitmap bitmap = null;
            byte[] pixelData = null;
            int stride = 0;

            await Task.Run(() =>
            {
                bitmap = CaptureScreenByBitBlt(0, 0, _physicalScreenWidth, _physicalScreenHeight, out pixelData, out stride);
            });

            if (bitmap == null) return;

            BackgroundImage.Source = bitmap;

            _screenCapture = bitmap;
            _pixelData = pixelData;
            _stride = stride;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            InputMethod.SetPreferredImeState(this, InputMethodState.Off);
            InputMethod.SetIsInputMethodEnabled(this, false);

            SetupWindow();

            this.Focus();

            GetCursorPos(out POINT pt);
            _currentMousePosition = new WinPoint(PhysicalToLogicalX(pt.X), PhysicalToLogicalY(pt.Y));

            UpdateSelectionCoord();

            MagnifierContainer.Visibility = Visibility.Collapsed;
            await CaptureScreenAndUpdateUI();

            InitializeMagnifierPixels();

            GetCursorPos(out POINT pt2);
            _currentMousePosition = new WinPoint(PhysicalToLogicalX(pt2.X), PhysicalToLogicalY(pt2.Y));
            _lastCursorX = pt2.X;
            _lastCursorY = pt2.Y;
            UpdateMagnifierFast(pt2.X, pt2.Y);
            MagnifierContainer.Visibility = Visibility.Visible;

            BackgroundImage.Width = _logicalScreenWidth;
            BackgroundImage.Height = _logicalScreenHeight;
            BackgroundImage.Stretch = Stretch.Fill;
        }

        private void SetupWindow()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
            }
            catch
            {
            }
        }

        private WinColor GetPixelFast(int x, int y)
        {
            if (_pixelData == null || x < 0 || x >= _physicalScreenWidth || y < 0 || y >= _physicalScreenHeight)
            {
                return WinColor.FromArgb(255, 0, 0, 0);
            }

            int index = y * _stride + x * 4;
            byte b = _pixelData[index];
            byte g = _pixelData[index + 1];
            byte r = _pixelData[index + 2];

            return WinColor.FromRgb(r, g, b);
        }

        private void UpdateSelectionCoord()
        {
            SelectionCoord.Text = $"({GetSelectionCoordText()})";
        }

        /// <summary>
        /// 选区四角的文本（<b>已按当前坐标模式折算</b>）：
        /// 屏幕模式给屏幕值；客户区模式下四角都减掉客户区左上角，
        /// 鼠标不在客户区内 / 没有游戏窗口 ⇒ <c>--, --, --, --</c>。
        /// <para>
        /// ★ 放大镜第二行和<b>选区上方那块黑标签</b>都读它，保证两处永远一致
        ///   （以前标签自己拼屏幕坐标，切到客户区模式也不跟着变）。
        /// </para>
        /// </summary>
        private string GetSelectionCoordText()
        {
            if (!_hasSelection) return "--, --, --, --";

            int x1 = LogicalToPhysicalX(Math.Min(_startPoint.X, _endPoint.X));
            int y1 = LogicalToPhysicalY(Math.Min(_startPoint.Y, _endPoint.Y));
            int x2 = LogicalToPhysicalX(Math.Max(_startPoint.X, _endPoint.X));
            int y2 = LogicalToPhysicalY(Math.Max(_startPoint.Y, _endPoint.Y));

            if (_useClientCoords)
            {
                if (!TryGetClientOrigin(out int left, out int top) || !MouseInClientNow())
                    return "--, --, --, --";

                return $"{x1 - left}, {y1 - top}, {x2 - left}, {y2 - top}";
            }

            return $"{x1}, {y1}, {x2}, {y2}";
        }

        /// <summary>
        /// 刷新坐标行与区域行（切模式时用：像素网格不用重画，只换文本）。
        /// </summary>
        private void UpdateCoordTexts()
        {
            SetCoordText(LogicalToPhysicalX(_currentMousePosition.X),
                         LogicalToPhysicalY(_currentMousePosition.Y));
            UpdateSelectionCoord();

            // ★ 选区上方那块黑标签（[x1, y1, x2, y2]  W × H）也走同一套取值，跟着模式一起换
            UpdateMask();
        }

        /// <summary>
        /// 写坐标行：前面带当前模式的名字（「屏幕坐标 (x, y)」/「客户区坐标 (x, y)」）。
        /// </summary>
        private void SetCoordText(int physX, int physY)
        {
            if (!_useClientCoords)
            {
                MagnifierCoord.Text = $"{ScreenCoordLabel} ({physX}, {physY})";
                return;
            }

            // 客户区模式：给不出坐标时**写明原因**，免得看起来像「按了没反应」
            if (!TryGetClientOrigin(out _, out _))
            {
                MagnifierCoord.Text = $"{ClientCoordLabel} (无游戏窗口)";
                return;
            }

            MagnifierCoord.Text = TryToClient(physX, physY, out int cx, out int cy)
                ? $"{ClientCoordLabel} ({cx}, {cy})"
                : $"{ClientCoordLabel} (--, --)";
        }

        /// <summary>
        /// 游戏窗口客户区左上角（屏幕坐标系）拿得到吗。
        /// <para>★ 用的就是截图/识别那套屏幕坐标（<c>WindowFocusService.LastBounds</c>），
        ///   与放大镜里的像素坐标同一坐标系 ⇒ 直接相减即可，不用再做 DPI 换算。</para>
        /// </summary>
        private static bool TryGetClientOrigin(out int left, out int top)
        {
            left = 0;
            top = 0;

            if (!WindowFocusService.IsWindowPresent) return false;

            var bounds = WindowFocusService.LastBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;

            left = bounds.Left;
            top = bounds.Top;
            return true;
        }

        /// <summary>
        /// 屏幕（物理）坐标 → 游戏窗口客户区内的坐标；没有游戏窗口或**不在客户区里** ⇒ false。
        /// </summary>
        private static bool TryToClient(int physX, int physY, out int clientX, out int clientY)
        {
            clientX = 0;
            clientY = 0;

            if (!TryGetClientOrigin(out int left, out int top)) return false;

            var bounds = WindowFocusService.LastBounds;
            clientX = physX - left;
            clientY = physY - top;

            return clientX >= 0 && clientY >= 0 && clientX < bounds.Width && clientY < bounds.Height;
        }

        /// <summary>当前鼠标在客户区内吗（客户区模式才有意义）。</summary>
        private bool MouseInClientNow()
            => TryToClient(LogicalToPhysicalX(_currentMousePosition.X),
                           LogicalToPhysicalY(_currentMousePosition.Y), out _, out _);

        private string GetCurrentCopyText()
        {
            switch (_currentCopyMode)
            {
                case CopyMode.Coordinate:
                    int physX = LogicalToPhysicalX(_currentMousePosition.X);
                    int physY = LogicalToPhysicalY(_currentMousePosition.Y);

                    // 客户区模式复制的就是客户区坐标（鼠标在客户区外 ⇒ 给不出值）
                    if (_useClientCoords)
                        return TryToClient(physX, physY, out int cx, out int cy) ? $"{cx}, {cy}" : "--, --";

                    return $"{physX}, {physY}";
                case CopyMode.Region:
                    if (_hasSelection)
                    {
                        int x1 = LogicalToPhysicalX(Math.Min(_startPoint.X, _endPoint.X));
                        int y1 = LogicalToPhysicalY(Math.Min(_startPoint.Y, _endPoint.Y));
                        int x2 = LogicalToPhysicalX(Math.Max(_startPoint.X, _endPoint.X));
                        int y2 = LogicalToPhysicalY(Math.Max(_startPoint.Y, _endPoint.Y));

                        if (_useClientCoords)
                        {
                            if (!TryGetClientOrigin(out int left, out int top) || !MouseInClientNow())
                                return "--, --, --, --";

                            return $"{x1 - left}, {y1 - top}, {x2 - left}, {y2 - top}";
                        }

                        return $"{x1}, {y1}, {x2}, {y2}";
                    }
                    return "--, --, --, --";
                case CopyMode.RGB:
                    return $"{_currentPixelColor.R}, {_currentPixelColor.G}, {_currentPixelColor.B}";
                default:
                    return "";
            }
        }

        private void CopyCurrentContent()
        {
            string text = GetCurrentCopyText();
            if (!string.IsNullOrEmpty(text))
            {
                var tempText = text;
                DialogResult = true;
                Close();

                NativeClipboardHelper.SafeSetText(tempText);
            }
        }

        private ResizeHandle GetResizeHandle(WinPoint pos)
        {
            if (!_hasSelection) return ResizeHandle.None;

            double x1 = Math.Min(_startPoint.X, _endPoint.X);
            double y1 = Math.Min(_startPoint.Y, _endPoint.Y);
            double x2 = Math.Max(_startPoint.X, _endPoint.X);
            double y2 = Math.Max(_startPoint.Y, _endPoint.Y);

            double half = HANDLE_HIT_SIZE / 2;

            bool nearLeft = pos.X >= x1 - half && pos.X <= x1 + half;
            bool nearRight = pos.X >= x2 - half && pos.X <= x2 + half;
            bool nearTop = pos.Y >= y1 - half && pos.Y <= y1 + half;
            bool nearBottom = pos.Y >= y2 - half && pos.Y <= y2 + half;

            if (nearTop && nearLeft) return ResizeHandle.TopLeft;
            if (nearTop && nearRight) return ResizeHandle.TopRight;
            if (nearBottom && nearLeft) return ResizeHandle.BottomLeft;
            if (nearBottom && nearRight) return ResizeHandle.BottomRight;
            if (nearTop) return ResizeHandle.Top;
            if (nearBottom) return ResizeHandle.Bottom;
            if (nearLeft) return ResizeHandle.Left;
            if (nearRight) return ResizeHandle.Right;

            return ResizeHandle.None;
        }

        private void UpdateCursorForHandle(ResizeHandle handle)
        {
            switch (handle)
            {
                case ResizeHandle.TopLeft:
                case ResizeHandle.BottomRight:
                    this.Cursor = Cursors.SizeNWSE;
                    break;
                case ResizeHandle.TopRight:
                case ResizeHandle.BottomLeft:
                    this.Cursor = Cursors.SizeNESW;
                    break;
                case ResizeHandle.Top:
                case ResizeHandle.Bottom:
                    this.Cursor = Cursors.SizeNS;
                    break;
                case ResizeHandle.Left:
                case ResizeHandle.Right:
                    this.Cursor = Cursors.SizeWE;
                    break;
                default:
                    this.Cursor = Cursors.Cross;
                    break;
            }
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                GetCursorPos(out POINT pt);
                int physX = (int)Math.Clamp(pt.X, 0, _physicalScreenWidth - 1);
                int physY = (int)Math.Clamp(pt.Y, 0, _physicalScreenHeight - 1);

                WinPoint logicalPos = new WinPoint(PhysicalToLogicalX(physX), PhysicalToLogicalY(physY));

                if (_hasSelection)
                {
                    ResizeHandle handle = GetResizeHandle(logicalPos);
                    if (handle != ResizeHandle.None)
                    {
                        _activeResizeHandle = handle;
                        _resizeStartPoint = logicalPos;
                        _resizeStartX1 = Math.Min(_startPoint.X, _endPoint.X);
                        _resizeStartY1 = Math.Min(_startPoint.Y, _endPoint.Y);
                        _resizeStartX2 = Math.Max(_startPoint.X, _endPoint.X);
                        _resizeStartY2 = Math.Max(_startPoint.Y, _endPoint.Y);
                        return;
                    }

                    double x1 = Math.Min(_startPoint.X, _endPoint.X);
                    double y1 = Math.Min(_startPoint.Y, _endPoint.Y);
                    double x2 = Math.Max(_startPoint.X, _endPoint.X);
                    double y2 = Math.Max(_startPoint.Y, _endPoint.Y);

                    if (logicalPos.X >= x1 && logicalPos.X <= x2 && logicalPos.Y >= y1 && logicalPos.Y <= y2)
                    {
                        _isDraggingSelection = true;
                        _dragStartPoint = logicalPos;
                        _dragStartSelectionPos = new WinPoint(x1, y1);
                        return;
                    }
                }

                _isMouseDown = true;
                _isSelecting = true;
                _startPoint = logicalPos;
                _hasSelection = false;

                UpdateMask();
                UpdateSelectionCoord();
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                Close();
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (_isDraggingSelection)
                {
                    _isDraggingSelection = false;
                    return;
                }

                if (_activeResizeHandle != ResizeHandle.None)
                {
                    _activeResizeHandle = ResizeHandle.None;
                    this.Cursor = Cursors.Cross;
                    return;
                }

                _isMouseDown = false;
                _isSelecting = false;

                UpdateMask();
                UpdateSelectionCoord();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_suppressMouseMoveMagnifier)
                return;

            GetCursorPos(out POINT pt);
            int physX = (int)Math.Clamp(pt.X, 0, _physicalScreenWidth - 1);
            int physY = (int)Math.Clamp(pt.Y, 0, _physicalScreenHeight - 1);

            WinPoint logicalPos = new WinPoint(PhysicalToLogicalX(physX), PhysicalToLogicalY(physY));
            _currentMousePosition = logicalPos;

            if ((_isDraggingSelection || _activeResizeHandle != ResizeHandle.None) && !_isKeyboardResizing)
            {
                MagnifierContainer.Visibility = Visibility.Collapsed;
            }
            else if (_pixelData != null)
            {
                if (physX != _lastCursorX || physY != _lastCursorY)
                {
                    _lastCursorX = physX;
                    _lastCursorY = physY;
                    UpdateMagnifierFast(physX, physY);
                }
                MagnifierContainer.Visibility = Visibility.Visible;
            }

            if (_activeResizeHandle != ResizeHandle.None)
            {
                double offsetX = logicalPos.X - _resizeStartPoint.X;
                double offsetY = logicalPos.Y - _resizeStartPoint.Y;

                double newX1 = _resizeStartX1;
                double newY1 = _resizeStartY1;
                double newX2 = _resizeStartX2;
                double newY2 = _resizeStartY2;

                switch (_activeResizeHandle)
                {
                    case ResizeHandle.TopLeft:
                        newX1 = Math.Clamp(_resizeStartX1 + offsetX, 0, _logicalScreenWidth);
                        newY1 = Math.Clamp(_resizeStartY1 + offsetY, 0, _logicalScreenHeight);
                        break;

                    case ResizeHandle.TopRight:
                        newX2 = Math.Clamp(_resizeStartX2 + offsetX, 0, _logicalScreenWidth);
                        newY1 = Math.Clamp(_resizeStartY1 + offsetY, 0, _logicalScreenHeight);
                        break;

                    case ResizeHandle.BottomRight:
                        newX2 = Math.Clamp(_resizeStartX2 + offsetX, 0, _logicalScreenWidth);
                        newY2 = Math.Clamp(_resizeStartY2 + offsetY, 0, _logicalScreenHeight);
                        break;

                    case ResizeHandle.BottomLeft:
                        newX1 = Math.Clamp(_resizeStartX1 + offsetX, 0, _logicalScreenWidth);
                        newY2 = Math.Clamp(_resizeStartY2 + offsetY, 0, _logicalScreenHeight);
                        break;

                    case ResizeHandle.Top:
                        newY1 = Math.Clamp(_resizeStartY1 + offsetY, 0, _logicalScreenHeight);
                        break;

                    case ResizeHandle.Bottom:
                        newY2 = Math.Clamp(_resizeStartY2 + offsetY, 0, _logicalScreenHeight);
                        break;

                    case ResizeHandle.Left:
                        newX1 = Math.Clamp(_resizeStartX1 + offsetX, 0, _logicalScreenWidth);
                        break;

                    case ResizeHandle.Right:
                        newX2 = Math.Clamp(_resizeStartX2 + offsetX, 0, _logicalScreenWidth);
                        break;
                }

                _startPoint = new WinPoint(Math.Min(newX1, newX2), Math.Min(newY1, newY2));
                _endPoint = new WinPoint(Math.Max(newX1, newX2), Math.Max(newY1, newY2));

                UpdateMask();
                UpdateSelectionCoord();
                return;
            }

            if (_isDraggingSelection)
            {
                double offsetX = logicalPos.X - _dragStartPoint.X;
                double offsetY = logicalPos.Y - _dragStartPoint.Y;

                double width = Math.Abs(_endPoint.X - _startPoint.X);
                double height = Math.Abs(_endPoint.Y - _startPoint.Y);

                double newX1 = Math.Clamp(_dragStartSelectionPos.X + offsetX, 0, _logicalScreenWidth - width);
                double newY1 = Math.Clamp(_dragStartSelectionPos.Y + offsetY, 0, _logicalScreenHeight - height);
                double newX2 = newX1 + width;
                double newY2 = newY1 + height;

                _startPoint = new WinPoint(newX1, newY1);
                _endPoint = new WinPoint(newX2, newY2);

                UpdateMask();
                UpdateSelectionCoord();
                return;
            }

            if (_hasSelection)
            {
                ResizeHandle handle = GetResizeHandle(logicalPos);
                UpdateCursorForHandle(handle);
            }
            else
            {
                this.Cursor = Cursors.Cross;
            }

            if (_isSelecting && _isMouseDown)
            {
                _endPoint = logicalPos;
                _hasSelection = true;

                double x1 = Math.Min(_startPoint.X, _endPoint.X);
                double y1 = Math.Min(_startPoint.Y, _endPoint.Y);
                double x2 = Math.Max(_startPoint.X, _endPoint.X);
                double y2 = Math.Max(_startPoint.Y, _endPoint.Y);

                double width = x2 - x1;
                double height = y2 - y1;

                _hasSelection = width > 2 && height > 2;

                UpdateMask();
                UpdateSelectionCoord();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConfirmSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Z)
            {
                _currentCopyMode = CopyMode.Coordinate;
                CopyCurrentContent();
                e.Handled = true;
            }
            else if (e.Key == Key.X)
            {
                _currentCopyMode = CopyMode.Region;
                CopyCurrentContent();
                e.Handled = true;
            }
            else if (e.Key == Key.C)
            {
                _currentCopyMode = CopyMode.RGB;
                CopyCurrentContent();
                e.Handled = true;
            }
            else if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                e.Handled = true;

                long now = Environment.TickCount64;

                // ★★ 去重**不看 e.IsRepeat**（按住 Shift 时窗口焦点一变就会丢 KeyUp，
                //   之后系统一直把 KeyDown 标成 IsRepeat ⇒ Shift 从此彻底失效），
                //   改用「上次按下的时间」自己判：
                //   · 按住不放 ⇒ 自动重复间隔只有几十毫秒 ⇒ 一直 < 400ms ⇒ 只切一次；
                //   · 松手后（KeyUp 把 _shiftDown 复位）再按 ⇒ 立刻生效；
                //   · KeyUp 丢了也没关系：隔 400ms 以上再按就当新的一次。
                if (_shiftDown)
                {
                    if (now - _lastShiftDownTicks < 400)
                    {
                        _lastShiftDownTicks = now;
                        return;
                    }
                }

                _shiftDown = true;
                _lastShiftDownTicks = now;

                ToggleCoordMode();
            }
            else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right ||
                     e.Key == Key.W || e.Key == Key.A || e.Key == Key.S || e.Key == Key.D)
            {
                HandleArrowKeyMove(e.Key);
                e.Handled = true;
            }
        }

        /// <summary>
        /// SHIFT 松开：把「正按着」的标志复位，下次按下才能再切一次。
        /// </summary>
        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
                _shiftDown = false;
        }

        /// <summary>
        /// SHIFT：屏幕坐标 ⇄ 客户区坐标。
        /// <para>
        /// ★ 客户区坐标 = 屏幕坐标 − 游戏窗口客户区左上角；**只有鼠标在客户区内才有值**
        ///   （在客户区外显示 <c>(--, --)</c>，区域同理），没有游戏窗口时也一样。
        /// </para>
        /// </summary>
        private void ToggleCoordMode()
        {
            _useClientCoords = !_useClientCoords;

            TipShift.Text = _useClientCoords ? "SHIFT：切换到屏幕坐标" : "SHIFT：切换到客户区坐标";

            try
            {
                // ★ 用**此刻真实的光标位置**刷新：模式一换就出新的数，不用等下一次鼠标移动
                if (GetCursorPos(out POINT pt))
                    _currentMousePosition = new WinPoint(PhysicalToLogicalX(pt.X), PhysicalToLogicalY(pt.Y));

                UpdateCoordTexts();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[截图] 切换坐标模式后刷新失败：{ex.Message}");
            }
        }

        private void HandleArrowKeyMove(Key key)
        {
            try
            {
                if (_activeResizeHandle != ResizeHandle.None)
                {
                    _isKeyboardResizing = true;

                    int step = 1;
                    int offsetX = 0, offsetY = 0;

                    switch (key)
                    {
                        case Key.Up:
                        case Key.W:
                            offsetY = -step;
                            break;
                        case Key.Down:
                        case Key.S:
                            offsetY = step;
                            break;
                        case Key.Left:
                        case Key.A:
                            offsetX = -step;
                            break;
                        case Key.Right:
                        case Key.D:
                            offsetX = step;
                            break;
                    }

                    if (offsetX == 0 && offsetY == 0)
                    {
                        _isKeyboardResizing = false;
                        return;
                    }

                    GetCursorPos(out POINT currentPos);
                    int newPhysX = Math.Clamp(currentPos.X + offsetX, 0, _physicalScreenWidth - 1);
                    int newPhysY = Math.Clamp(currentPos.Y + offsetY, 0, _physicalScreenHeight - 1);

                    _suppressMouseMoveMagnifier = true;
                    SetCursorPos(newPhysX, newPhysY);

                    _lastCursorX = newPhysX;
                    _lastCursorY = newPhysY;

                    WinPoint newLogicalPos = new WinPoint(PhysicalToLogicalX(newPhysX), PhysicalToLogicalY(newPhysY));
                    _currentMousePosition = newLogicalPos;

                    double offsetXLogical = newLogicalPos.X - _resizeStartPoint.X;
                    double offsetYLogical = newLogicalPos.Y - _resizeStartPoint.Y;

                    double newX1 = _resizeStartX1;
                    double newY1 = _resizeStartY1;
                    double newX2 = _resizeStartX2;
                    double newY2 = _resizeStartY2;

                    switch (_activeResizeHandle)
                    {
                        case ResizeHandle.TopLeft:
                            newX1 = Math.Clamp(_resizeStartX1 + offsetXLogical, 0, _logicalScreenWidth);
                            newY1 = Math.Clamp(_resizeStartY1 + offsetYLogical, 0, _logicalScreenHeight);
                            break;

                        case ResizeHandle.TopRight:
                            newX2 = Math.Clamp(_resizeStartX2 + offsetXLogical, 0, _logicalScreenWidth);
                            newY1 = Math.Clamp(_resizeStartY1 + offsetYLogical, 0, _logicalScreenHeight);
                            break;

                        case ResizeHandle.BottomRight:
                            newX2 = Math.Clamp(_resizeStartX2 + offsetXLogical, 0, _logicalScreenWidth);
                            newY2 = Math.Clamp(_resizeStartY2 + offsetYLogical, 0, _logicalScreenHeight);
                            break;

                        case ResizeHandle.BottomLeft:
                            newX1 = Math.Clamp(_resizeStartX1 + offsetXLogical, 0, _logicalScreenWidth);
                            newY2 = Math.Clamp(_resizeStartY2 + offsetYLogical, 0, _logicalScreenHeight);
                            break;

                        case ResizeHandle.Top:
                            newY1 = Math.Clamp(_resizeStartY1 + offsetYLogical, 0, _logicalScreenHeight);
                            break;

                        case ResizeHandle.Bottom:
                            newY2 = Math.Clamp(_resizeStartY2 + offsetYLogical, 0, _logicalScreenHeight);
                            break;

                        case ResizeHandle.Left:
                            newX1 = Math.Clamp(_resizeStartX1 + offsetXLogical, 0, _logicalScreenWidth);
                            break;

                        case ResizeHandle.Right:
                            newX2 = Math.Clamp(_resizeStartX2 + offsetXLogical, 0, _logicalScreenWidth);
                            break;
                    }

                    _startPoint = new WinPoint(Math.Min(newX1, newX2), Math.Min(newY1, newY2));
                    _endPoint = new WinPoint(Math.Max(newX1, newX2), Math.Max(newY1, newY2));

                    UpdateMask();
                    UpdateSelectionCoord();

                    UpdateMagnifierFast(newPhysX, newPhysY);
                    MagnifierContainer.Visibility = Visibility.Visible;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _suppressMouseMoveMagnifier = false;
                        _isKeyboardResizing = false;
                    }), System.Windows.Threading.DispatcherPriority.Input);

                    return;
                }

                if (_hasSelection)
                {
                    int physX1 = LogicalToPhysicalX(Math.Min(_startPoint.X, _endPoint.X));
                    int physY1 = LogicalToPhysicalY(Math.Min(_startPoint.Y, _endPoint.Y));
                    int physX2 = LogicalToPhysicalX(Math.Max(_startPoint.X, _endPoint.X));
                    int physY2 = LogicalToPhysicalY(Math.Max(_startPoint.Y, _endPoint.Y));

                    int width = physX2 - physX1;
                    int height = physY2 - physY1;

                    int newPhysX1 = physX1;
                    int newPhysY1 = physY1;

                    switch (key)
                    {
                        case Key.Up:
                        case Key.W:
                            newPhysY1 = Math.Max(0, physY1 - 1);
                            break;
                        case Key.Down:
                        case Key.S:
                            newPhysY1 = Math.Min(_physicalScreenHeight - height, physY1 + 1);
                            break;
                        case Key.Left:
                        case Key.A:
                            newPhysX1 = Math.Max(0, physX1 - 1);
                            break;
                        case Key.Right:
                        case Key.D:
                            newPhysX1 = Math.Min(_physicalScreenWidth - width, physX1 + 1);
                            break;
                    }

                    if (newPhysX1 == physX1 && newPhysY1 == physY1)
                        return;

                    double newX1 = PhysicalToLogicalX(newPhysX1);
                    double newY1 = PhysicalToLogicalY(newPhysY1);
                    double newX2 = PhysicalToLogicalX(newPhysX1 + width);
                    double newY2 = PhysicalToLogicalY(newPhysY1 + height);

                    _startPoint = new WinPoint(newX1, newY1);
                    _endPoint = new WinPoint(newX2, newY2);

                    UpdateMask();
                    UpdateSelectionCoord();

                    GetCursorPos(out POINT pt);
                    UpdateMagnifierFast(pt.X, pt.Y);

                    return;
                }

                GetCursorPos(out POINT cursorPos);

                int newX = cursorPos.X;
                int newY = cursorPos.Y;

                switch (key)
                {
                    case Key.Up:
                    case Key.W:
                        newY = Math.Max(0, cursorPos.Y - 1);
                        break;
                    case Key.Down:
                    case Key.S:
                        newY = Math.Min(_physicalScreenHeight - 1, cursorPos.Y + 1);
                        break;
                    case Key.Left:
                    case Key.A:
                        newX = Math.Max(0, cursorPos.X - 1);
                        break;
                    case Key.Right:
                    case Key.D:
                        newX = Math.Min(_physicalScreenWidth - 1, cursorPos.X + 1);
                        break;
                }

                if (newX == cursorPos.X && newY == cursorPos.Y)
                    return;

                _suppressMouseMoveMagnifier = true;

                SetCursorPos(newX, newY);

                _lastCursorX = newX;
                _lastCursorY = newY;
                _currentMousePosition = new WinPoint(PhysicalToLogicalX(newX), PhysicalToLogicalY(newY));

                if (_pixelData != null)
                {
                    UpdateMagnifierFast(newX, newY);
                    MagnifierContainer.Visibility = Visibility.Visible;
                }

                UpdateSelectionCoord();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _suppressMouseMoveMagnifier = false;
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            catch
            {
                _suppressMouseMoveMagnifier = false;
                _isKeyboardResizing = false;
            }
        }

        private void UpdateMagnifierFast(int x, int y)
        {
            if (_screenCapture == null || _pixelData == null)
            {
                SetCoordText(x, y);
                MagnifierColor.Text = "RGB: (加载中...)";
                return;
            }

            if (!_magnifierInitialized)
            {
                InitializeMagnifierPixels();
            }

            try
            {
                int halfCols = MAGNIFIER_COLS / 2;
                int halfRows = MAGNIFIER_ROWS / 2;

                int startX = Math.Clamp(x - halfCols, 0, _physicalScreenWidth - MAGNIFIER_COLS);
                int startY = Math.Clamp(y - halfRows, 0, _physicalScreenHeight - MAGNIFIER_ROWS);

                int centerX = x - startX;
                int centerY = y - startY;

                _currentPixelColor = GetPixelFast(x, y);

                SetCoordText(x, y);
                MagnifierColor.Text = $"RGB: ({_currentPixelColor.R}, {_currentPixelColor.G}, {_currentPixelColor.B})";

                double selX1 = 0, selY1 = 0, selX2 = 0, selY2 = 0;
                bool hasSelection = _hasSelection;
                if (hasSelection)
                {
                    selX1 = Math.Min(_startPoint.X, _endPoint.X);
                    selY1 = Math.Min(_startPoint.Y, _endPoint.Y);
                    selX2 = Math.Max(_startPoint.X, _endPoint.X);
                    selY2 = Math.Max(_startPoint.Y, _endPoint.Y);
                }

                for (int row = 0; row < MAGNIFIER_ROWS; row++)
                {
                    for (int col = 0; col < MAGNIFIER_COLS; col++)
                    {
                        int pixelX = startX + col;
                        int pixelY = startY + row;

                        if (pixelX < _physicalScreenWidth && pixelY < _physicalScreenHeight)
                        {
                            WinColor color = GetPixelFast(pixelX, pixelY);
                            bool isBorder = false;

                            if (hasSelection)
                            {
                                double logicalPixelX = PhysicalToLogicalX(pixelX);
                                double logicalPixelY = PhysicalToLogicalY(pixelY);
                                double borderWidth = 1.5 / _dpiScaleX;
                                double borderHeight = 1.5 / _dpiScaleY;

                                bool onLeft = Math.Abs(logicalPixelX - selX1) < borderWidth;
                                bool onRight = Math.Abs(logicalPixelX - selX2) < borderWidth;
                                bool onTop = Math.Abs(logicalPixelY - selY1) < borderHeight;
                                bool onBottom = Math.Abs(logicalPixelY - selY2) < borderHeight;

                                isBorder = (onLeft || onRight) && logicalPixelY >= selY1 && logicalPixelY <= selY2 ||
                                           (onTop || onBottom) && logicalPixelX >= selX1 && logicalPixelX <= selX2;
                            }

                            bool isCross = ((col == centerX) || (row == centerY)) && !(col == centerX && row == centerY);

                            if (isBorder)
                            {
                                _magnifierPixels[row, col].Fill = GetCachedBrush(WinColor.FromRgb(30, 140, 212));
                                _magnifierPixels[row, col].Stroke = new SolidColorBrush(WinColor.FromArgb(255, 30, 140, 212));
                                _magnifierPixels[row, col].StrokeThickness = 1;
                            }
                            else if (isCross)
                            {
                                byte r = (byte)(color.R * 0.5 + 100 * 0.5);
                                byte g = (byte)(color.G * 0.5 + 180 * 0.5);
                                byte b = (byte)(color.B * 0.5 + 255 * 0.5);
                                var mixedColor = WinColor.FromRgb(r, g, b);
                                _magnifierPixels[row, col].Fill = GetCachedBrush(mixedColor);
                                _magnifierPixels[row, col].Stroke = new SolidColorBrush(WinColor.FromArgb(180, 200, 200, 200));
                                _magnifierPixels[row, col].StrokeThickness = 0.5;
                            }
                            else if (col == centerX && row == centerY)
                            {
                                _magnifierPixels[row, col].Fill = GetCachedBrush(color);
                                _magnifierPixels[row, col].Stroke = new SolidColorBrush(WinColor.FromRgb(255, 0, 0));
                                _magnifierPixels[row, col].StrokeThickness = 1.5;
                            }
                            else
                            {
                                _magnifierPixels[row, col].Fill = GetCachedBrush(color);
                                _magnifierPixels[row, col].Stroke = new SolidColorBrush(WinColor.FromArgb(180, 200, 200, 200));
                                _magnifierPixels[row, col].StrokeThickness = 0.5;
                            }
                        }
                    }
                }

                double totalWidth = MAGNIFIER_WIDTH + 12;
                double totalHeight = MAGNIFIER_HEIGHT + 48;

                double logicalX = PhysicalToLogicalX(x);
                double logicalY = PhysicalToLogicalY(y);

                double magnifierX = logicalX + 15;
                double magnifierY = logicalY + 15;

                if (_hasSelection)
                {
                    double margin = 10;

                    if (magnifierX + totalWidth > selX1 - margin && magnifierX < selX2 + margin &&
                        magnifierY + totalHeight > selY1 - margin && magnifierY < selY2 + margin)
                    {
                        if (logicalX + totalWidth + 15 < _logicalScreenWidth - 5)
                        {
                            magnifierX = logicalX + 15;
                        }
                        else
                        {
                            magnifierX = logicalX - totalWidth - 15;
                        }

                        if (magnifierY + totalHeight > selY1 - margin && magnifierY < selY2 + margin)
                        {
                            if (logicalY + totalHeight + 15 < _logicalScreenHeight - 5)
                            {
                                magnifierY = logicalY + 15;
                            }
                            else
                            {
                                magnifierY = logicalY - totalHeight - 15;
                            }
                        }

                        if (magnifierX + totalWidth > selX1 - margin && magnifierX < selX2 + margin)
                        {
                            if (logicalX + totalWidth + 15 < _logicalScreenWidth - 5)
                            {
                                magnifierX = logicalX + 15;
                            }
                            else
                            {
                                magnifierX = logicalX - totalWidth - 15;
                            }
                        }
                    }
                }

                if (magnifierX + totalWidth > _logicalScreenWidth - 5)
                    magnifierX = logicalX - totalWidth - 15;

                if (magnifierY + totalHeight > _logicalScreenHeight - 5)
                    magnifierY = logicalY - totalHeight - 15;

                if (magnifierX < 5) magnifierX = 5;
                if (magnifierY < 5) magnifierY = 5;

                Canvas.SetLeft(MagnifierContainer, magnifierX);
                Canvas.SetTop(MagnifierContainer, magnifierY);

                MagnifierContainer.InvalidateVisual();
                MagnifierContainer.UpdateLayout();

                UpdateSelectionCoord();
            }
            catch
            {
            }
        }

        private void UpdateMask()
        {
            if (!_hasSelection)
            {
                UpdateMaskHole(0, 0, 0, 0, false);
                SelectionBorder.Visibility = Visibility.Collapsed;
                SizeLabel.Visibility = Visibility.Collapsed;
                return;
            }

            double x1 = Math.Min(_startPoint.X, _endPoint.X);
            double y1 = Math.Min(_startPoint.Y, _endPoint.Y);
            double x2 = Math.Max(_startPoint.X, _endPoint.X);
            double y2 = Math.Max(_startPoint.Y, _endPoint.Y);
            double width = x2 - x1;
            double height = y2 - y1;

            if (width > 2 && height > 2)
            {
                UpdateMaskHole(x1, y1, width, height, true);

                SelectionBorder.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelectionBorder, x1);
                Canvas.SetTop(SelectionBorder, y1);
                SelectionBorder.Width = width;
                SelectionBorder.Height = height;

                int physWidth = LogicalToPhysicalX((int)width);
                int physHeight = LogicalToPhysicalY((int)height);

                // ★ 坐标部分与放大镜第二行同源（跟随屏幕/客户区模式）；宽高是差值，两种模式下一样
                SizeText.Text = $"[{GetSelectionCoordText()}]  {physWidth} × {physHeight}";

                SizeLabel.Visibility = Visibility.Visible;
                SizeLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double labelWidth = SizeLabel.DesiredSize.Width;
                double labelHeight = SizeLabel.DesiredSize.Height;

                double margin = 8;
                double labelX, labelY;

                if (y1 - labelHeight - margin > 0)
                {
                    labelY = y1 - labelHeight - margin;
                }
                else
                {
                    labelY = y2 + margin;
                }

                labelX = x1 + margin;

                if (labelX + labelWidth > _logicalScreenWidth - margin)
                {
                    double centerX = x1 + (width - labelWidth) / 2;
                    if (centerX >= margin && centerX + labelWidth <= _logicalScreenWidth - margin)
                    {
                        labelX = centerX;
                    }
                    else
                    {
                        labelX = _logicalScreenWidth - labelWidth - margin;
                        if (labelX < margin) labelX = margin;
                    }
                }

                if (labelX + labelWidth > x1 && labelX < x2 && labelY < y2 && labelY + labelHeight > y1)
                {
                    if (x2 + labelWidth + margin < _logicalScreenWidth - margin)
                    {
                        labelX = x2 + margin;
                    }
                    else if (x1 - labelWidth - margin > 0)
                    {
                        labelX = x1 - labelWidth - margin;
                    }
                }

                if (labelX < 0) labelX = margin;
                if (labelY < 0) labelY = margin;
                if (labelX + labelWidth > _logicalScreenWidth - margin)
                    labelX = _logicalScreenWidth - labelWidth - margin;
                if (labelY + labelHeight > _logicalScreenHeight - margin)
                    labelY = _logicalScreenHeight - labelHeight - margin;

                Canvas.SetLeft(SizeLabel, labelX);
                Canvas.SetTop(SizeLabel, labelY);
            }
            else
            {
                SelectionBorder.Visibility = Visibility.Collapsed;
                SizeLabel.Visibility = Visibility.Collapsed;
            }
        }

        private void ConfirmSelection()
        {
            if (!_hasSelection || _screenCapture == null)
            {
                return;
            }

            try
            {
                int x1 = LogicalToPhysicalX(Math.Min(_startPoint.X, _endPoint.X));
                int y1 = LogicalToPhysicalY(Math.Min(_startPoint.Y, _endPoint.Y));
                int x2 = LogicalToPhysicalX(Math.Max(_startPoint.X, _endPoint.X));
                int y2 = LogicalToPhysicalY(Math.Max(_startPoint.Y, _endPoint.Y));

                int width = x2 - x1;
                int height = y2 - y1;

                if (width < 1 || height < 1)
                {
                    return;
                }

                SelectionRectInfo = new Rect(x1, y1, width, height);

                var cropped = CropWriteableBitmap(_screenCapture, x1, y1, width, height);
                CapturedImage = cropped;

                if (cropped != null)
                {
                    Clipboard.SetImage(cropped);
                }

                SelectionConfirmed?.Invoke(this, SelectionRectInfo);
                ImageCaptured?.Invoke(this, cropped);

                DialogResult = true;
                Close();
            }
            catch
            {
            }
        }

        private WriteableBitmap CropWriteableBitmap(WriteableBitmap source, int x, int y, int width, int height)
        {
            if (source == null || width <= 0 || height <= 0)
                return null;

            var cropped = new WriteableBitmap(width, height, source.DpiX, source.DpiY, source.Format, null);

            int sourceStride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
            int destStride = (width * source.Format.BitsPerPixel + 7) / 8;
            byte[] pixels = new byte[sourceStride * source.PixelHeight];

            source.CopyPixels(pixels, sourceStride, 0);

            byte[] croppedPixels = new byte[destStride * height];

            for (int row = 0; row < height; row++)
            {
                int sourceRowStart = (y + row) * sourceStride + x * 4;
                int destRowStart = row * destStride;
                Array.Copy(pixels, sourceRowStart, croppedPixels, destRowStart, destStride);
            }

            cropped.WritePixels(new Int32Rect(0, 0, width, height), croppedPixels, destStride, 0);
            cropped.Freeze();

            return cropped;
        }

        protected override void OnClosed(EventArgs e)
        {
            // ★ 双保险：窗口自己关掉时也把名额放掉（万一调用方没走 Leave）
            Leave();

            this.Loaded -= OnLoaded;
            this.PreviewKeyDown -= OnKeyDown;
            this.PreviewKeyUp -= OnKeyUp;
            this.MouseMove -= OnMouseMove;
            this.MouseDown -= OnMouseDown;
            this.MouseUp -= OnMouseUp;
            this.SizeChanged -= OnSizeChanged;

            _pixelData = null;
            _screenCapture = null;

            _brushCache.Clear();
            _brushCache = null;

            base.OnClosed(e);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMask();
        }
    }
}