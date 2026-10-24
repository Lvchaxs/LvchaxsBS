using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace LvchaxsBS.Services
{
    public static class CoordinateFormats
    {
        #region 坐标转换

        private const int BASE_WIDTH = 3840;
        private const int BASE_HEIGHT = 2160;

        private static readonly HashSet<(int width, int height)> _supportedResolutions = new HashSet<(int, int)>
        {
            (5120, 2880),
            (3840, 2160),
            (2560, 1440),
            (1920, 1080),
            (1600, 900),
            (5120, 2160),
            (3440, 1440),
            (2560, 1080)
        };

        private static int _clientLeft = 0;
        private static int _clientTop = 0;
        private static int _clientWidth = 0;
        private static int _clientHeight = 0;
        private static bool _hasWindowInfo = false;
        private static string _processName = "";
        private static bool _hasProcess = false;

        /// <summary>
        /// 日志输出开关：1=输出，0=不输出
        /// </summary>
        public static int EnableLog { get; set; } = 1;

        // 坐标字典
        private static readonly Dictionary<string, object> _coords = new Dictionary<string, object>();
        // 缓存字典
        private static readonly Dictionary<string, object> _cache = new Dictionary<string, object>();

        private static bool _cacheReady = false;
        private static string _lastProcessName = "";
        private static int _lastLeft = 0;
        private static int _lastTop = 0;
        private static int _lastWidth = 0;
        private static int _lastHeight = 0;
        private static bool _hasLastInfo = false;

        public static bool HasWindowInfo => _hasWindowInfo;
        public static int ClientWidth => _clientWidth;
        public static int ClientHeight => _clientHeight;
        public static string ProcessName => _processName;
        public static bool HasProcess => _hasProcess;
        public static bool CacheReady => _cacheReady;

        public static void UpdateWindowInfo(int left, int top, int width, int height)
        {
            _clientLeft = left;
            _clientTop = top;
            _clientWidth = width;
            _clientHeight = height;
            _hasWindowInfo = true;
        }

        public static void UpdateProcessInfo(string processName)
        {
            _processName = processName;
            _hasProcess = !string.IsNullOrEmpty(processName);
        }

        private static bool IsResolutionSupported(int width, int height)
        {
            return _supportedResolutions.Contains((width, height));
        }

        private static double GetScaleX()
        {
            if (_clientWidth == 0) return 1.0;
            return (double)_clientWidth / BASE_WIDTH;
        }

        private static double GetScaleY()
        {
            if (_clientHeight == 0) return 1.0;
            return (double)_clientHeight / BASE_HEIGHT;
        }

        #endregion

        #region 格式定义

        public struct RgbColor
        {
            public byte R { get; set; }
            public byte G { get; set; }
            public byte B { get; set; }

            public RgbColor(byte r, byte g, byte b)
            {
                R = r;
                G = g;
                B = b;
            }

            public override string ToString() => $"({R}, {G}, {B})";
        }

        public struct Point
        {
            public int X { get; set; }
            public int Y { get; set; }
            public Dictionary<(int width, int height), (int x, int y)>? CustomCompensation { get; set; }

            public Point(int x, int y)
            {
                X = x;
                Y = y;
                CustomCompensation = null;
            }

            public Point(int x, int y, Dictionary<(int, int), (int, int)>? customCompensation)
            {
                X = x;
                Y = y;
                CustomCompensation = customCompensation;
            }

            public Point ToScreen()
            {
                return ToScreen(CoordinateFormats.ClientWidth, CoordinateFormats.ClientHeight);
            }

            public Point ToScreen(int clientWidth, int clientHeight)
            {
                if (!_hasWindowInfo) return this;

                int compX = 0, compY = 0;
                if (CustomCompensation != null && CustomCompensation.TryGetValue((clientWidth, clientHeight), out var custom))
                {
                    compX = custom.x;
                    compY = custom.y;
                }

                return new Point(
                    _clientLeft + (int)(X * GetScaleX()) + compX,
                    _clientTop + (int)(Y * GetScaleY()) + compY
                );
            }

            public override string ToString() => $"({X}, {Y})";
        }

        public struct Rect
        {
            public int X1 { get; set; }
            public int Y1 { get; set; }
            public int X2 { get; set; }
            public int Y2 { get; set; }

            public Rect(int x1, int y1, int x2, int y2)
            {
                X1 = x1;
                Y1 = y1;
                X2 = x2;
                Y2 = y2;
            }

            public int CenterX => (X1 + X2) / 2;
            public int CenterY => (Y1 + Y2) / 2;
            public int Width => X2 - X1;
            public int Height => Y2 - Y1;

            public Rect ToScreen()
            {
                return ToScreen(CoordinateFormats.ClientWidth, CoordinateFormats.ClientHeight);
            }

            public Rect ToScreen(int clientWidth, int clientHeight)
            {
                if (!_hasWindowInfo) return new Rect(X1, Y1, X2, Y2);
                double scaleX = GetScaleX();
                double scaleY = GetScaleY();
                return new Rect(
                    _clientLeft + (int)(X1 * scaleX),
                    _clientTop + (int)(Y1 * scaleY),
                    _clientLeft + (int)(X2 * scaleX),
                    _clientTop + (int)(Y2 * scaleY)
                );
            }

            public override string ToString() => $"({X1}, {Y1}, {X2}, {Y2})";
        }

        public class DetectPoint
        {
            public int X { get; set; }
            public int Y { get; set; }
            public List<RgbColor> Colors { get; set; }
            public int Tolerance { get; set; }
            public Dictionary<(int width, int height), (int x, int y)>? CustomCompensation { get; set; }

            public DetectPoint(int x, int y, int tolerance = 5, params (byte r, byte g, byte b)[] colors)
            {
                X = x;
                Y = y;
                Colors = colors.Select(c => new RgbColor(c.r, c.g, c.b)).ToList();
                Tolerance = tolerance;
                CustomCompensation = null;
            }

            public DetectPoint(int x, int y, int tolerance, Dictionary<(int, int), (int, int)>? customCompensation, params (byte r, byte g, byte b)[] colors)
            {
                X = x;
                Y = y;
                Colors = colors.Select(c => new RgbColor(c.r, c.g, c.b)).ToList();
                Tolerance = tolerance;
                CustomCompensation = customCompensation;
            }

            public DetectPoint ToScreen()
            {
                return ToScreen(CoordinateFormats.ClientWidth, CoordinateFormats.ClientHeight);
            }

            public DetectPoint ToScreen(int clientWidth, int clientHeight)
            {
                if (!_hasWindowInfo) return this;

                int compX = 0, compY = 0;
                if (CustomCompensation != null && CustomCompensation.TryGetValue((clientWidth, clientHeight), out var custom))
                {
                    compX = custom.x;
                    compY = custom.y;
                }

                var screen = new DetectPoint(
                    _clientLeft + (int)(X * GetScaleX()) + compX,
                    _clientTop + (int)(Y * GetScaleY()) + compY,
                    Tolerance
                );
                screen.Colors = Colors;
                return screen;
            }
        }

        public class MatchRect
        {
            public Rect Rect { get; set; }
            public double Threshold { get; set; }

            /// <summary>区域内要匹配的目标颜色（可多个，命中任一即算通过）。</summary>
            public List<RgbColor> Colors { get; set; } = new();

            /// <summary>
            /// 颜色容差。★ 与 <c>ImageRecognition.CheckPixelColor</c> 的默认容差一致（=5）。
            /// 画面有轻微色彩抖动 / 亮度渐变，容差太小会导致明明在却判定失败。
            /// </summary>
            public int Tolerance { get; set; } = 5;

            // 四角独立偏移：分辨率 → (X1偏移, Y1偏移, X2偏移, Y2偏移)
            public Dictionary<(int width, int height), (int x1, int y1, int x2, int y2)>? CustomCompensation { get; set; }

            public MatchRect(int x1, int y1, int x2, int y2, double threshold)
            {
                Rect = new Rect(x1, y1, x2, y2);
                Threshold = threshold;
                CustomCompensation = null;
            }

            public MatchRect(int x1, int y1, int x2, int y2, double threshold, Dictionary<(int, int), (int x1, int y1, int x2, int y2)>? customCompensation)
            {
                Rect = new Rect(x1, y1, x2, y2);
                Threshold = threshold;
                CustomCompensation = customCompensation;
            }

            /// <summary>
            /// 带颜色与容差的模板匹配区域：区域 + 阈值 + 容差 + 分辨率补偿 + 目标颜色。
            /// <para>★ 与 <see cref="DetectPoint"/> 的签名风格保持一致（tolerance 在前、colors 收尾）。</para>
            /// </summary>
            public MatchRect(int x1, int y1, int x2, int y2, double threshold, int tolerance,
                Dictionary<(int width, int height), (int x1, int y1, int x2, int y2)>? customCompensation,
                params (byte r, byte g, byte b)[] colors)
            {
                Rect = new Rect(x1, y1, x2, y2);
                Threshold = threshold;
                Tolerance = tolerance;
                Colors = colors.Select(c => new RgbColor(c.r, c.g, c.b)).ToList();
                CustomCompensation = customCompensation;
            }

            public MatchRect ToScreen()
            {
                return ToScreen(CoordinateFormats.ClientWidth, CoordinateFormats.ClientHeight);
            }

            public MatchRect ToScreen(int clientWidth, int clientHeight)
            {
                if (!_hasWindowInfo) return this;

                double scaleX = GetScaleX();
                double scaleY = GetScaleY();

                int x1 = _clientLeft + (int)(Rect.X1 * scaleX);
                int y1 = _clientTop + (int)(Rect.Y1 * scaleY);
                int x2 = _clientLeft + (int)(Rect.X2 * scaleX);
                int y2 = _clientTop + (int)(Rect.Y2 * scaleY);

                if (CustomCompensation != null && CustomCompensation.TryGetValue((clientWidth, clientHeight), out var comp))
                {
                    x1 += comp.x1;
                    y1 += comp.y1;
                    x2 += comp.x2;
                    y2 += comp.y2;
                }

                var screen = new MatchRect(x1, y1, x2, y2, Threshold, Tolerance, null);
                screen.Colors = Colors;          // ★ 容差与颜色跟着一起换算，别只搬矩形
                return screen;
            }
        }

        public class ColorRect
        {
            public Rect Rect { get; set; }
            public List<RgbColor> Colors { get; set; }
            public int Tolerance { get; set; }
            public Dictionary<(int width, int height), (int x, int y)>? CustomCompensation { get; set; }

            public ColorRect(int x1, int y1, int x2, int y2, int tolerance = 5, params (byte r, byte g, byte b)[] colors)
            {
                Rect = new Rect(x1, y1, x2, y2);
                Colors = colors.Select(c => new RgbColor(c.r, c.g, c.b)).ToList();
                Tolerance = tolerance;
                CustomCompensation = null;
            }

            public ColorRect(int x1, int y1, int x2, int y2, int tolerance, Dictionary<(int, int), (int, int)>? customCompensation, params (byte r, byte g, byte b)[] colors)
            {
                Rect = new Rect(x1, y1, x2, y2);
                Colors = colors.Select(c => new RgbColor(c.r, c.g, c.b)).ToList();
                Tolerance = tolerance;
                CustomCompensation = customCompensation;
            }

            public ColorRect ToScreen()
            {
                return ToScreen(CoordinateFormats.ClientWidth, CoordinateFormats.ClientHeight);
            }

            public ColorRect ToScreen(int clientWidth, int clientHeight)
            {
                if (!_hasWindowInfo) return this;

                int compX = 0, compY = 0;
                if (CustomCompensation != null && CustomCompensation.TryGetValue((clientWidth, clientHeight), out var custom))
                {
                    compX = custom.x;
                    compY = custom.y;
                }

                double scaleX = GetScaleX();
                double scaleY = GetScaleY();

                var screen = new ColorRect(
                    _clientLeft + (int)(Rect.X1 * scaleX) + compX,
                    _clientTop + (int)(Rect.Y1 * scaleY) + compY,
                    _clientLeft + (int)(Rect.X2 * scaleX) + compX,
                    _clientTop + (int)(Rect.Y2 * scaleY) + compY,
                    Tolerance
                );
                screen.Colors = Colors;
                return screen;
            }
        }

        #endregion

        #region 坐标定义 主界面+地图

        public static DetectPoint 主界面 = new DetectPoint(1618, 2020, 5,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (101, 0),
                [(3440, 1440)] = (71, 1),
                [(2560, 1080)] = (52, 0),
            },
            (150, 215, 34),
            (255, 90, 90)
        );

        public static Point 主界面图标显示坐标 = new Point(1614, 2060,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (97, 0),
                [(3440, 1440)] = (68, 0),
                [(2560, 1080)] = (49, 0),
            }
        );

        #endregion

        #region 坐标定义 头像怎字幕

        public static Point 自动烹饪_字幕显示坐标 = new Point(77, 1370,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (97, 0),
                [(3440, 1440)] = (62, 0),
                [(2560, 1080)] = (47, 0),
            }
        );

        public static Point 钓鱼辅助_字幕显示坐标 = new Point(77, 520,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (128, 0),
                [(3440, 1440)] = (86, 0),
                [(2560, 1080)] = (64, 0),
            }
        );

        public static Point 自动伐木_字幕显示坐标 = new Point(77, 520,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (128, 0),
                [(3440, 1440)] = (86, 0),
                [(2560, 1080)] = (64, 0),
            }
        );
        #endregion

        #region 坐标定义 快速传送

        public static DetectPoint 快速传送_地图缩放按钮 = new DetectPoint(95, 887, 2,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (113, 0),
                [(3440, 1440)] = (74, 0),
                [(2560, 1080)] = (56, 0),
            },
            (237, 229, 218)
        );

        public static MatchRect 快速传送_右下角区域 = new MatchRect(2930, 1995, 3010, 2025, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (160, 0, 133, 0),
                [(3440, 1440)] = (113, 0, 113, 0),
                [(2560, 1080)] = (80, 0, 80, 0),
            }
        );

        public static MatchRect 快速传送_右侧列表区域 = new MatchRect(2590, 570, 2710, 1870, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (83, 0, 43, 0),
                [(3440, 1440)] = (59, 0, 32, 0),
                [(2560, 1080)] = (42, 0, 22, 0),
            }
        );

        public static MatchRect 快速传送_深渊过滤区域 = new MatchRect(2586, 15, 3621, 171, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        #endregion

        #region 坐标定义 快速切图

        public static DetectPoint 快速切图_地区探测点 = new DetectPoint(2603, 27, 5,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (189, 0),
                [(2560, 1080)] = (135, 0),
            },
            (61, 69, 85)
        );

        public static MatchRect 快速切图_导航条区域 = new MatchRect(3651, 2035, 3720, 2036, 0.9, 5,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(3440, 1440)] = (-56, 0, -66, 0),
                [(2560, 1080)] = (-44, 0, -52, -1),
            },
            (236, 229, 216)
        );

        public static Point 快速切图_蒙德 = new Point(2906, 360,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (118, 0),
                [(2560, 1080)] = (83, -1),
            }
        );

        public static Point 快速切图_璃月 = new Point(3501, 360,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (-18, 0),
                [(2560, 1080)] = (-16, -1),
            }
        );

        public static Point 快速切图_稻妻 = new Point(2906, 566,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (118, 0),
                [(2560, 1080)] = (83, -1),
            }
        );

        public static Point 快速切图_须弥 = new Point(3501, 566,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (-18, 0),
                [(2560, 1080)] = (-16, -1),
            }
        );

        public static Point 快速切图_枫丹 = new Point(2906, 773,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (118, 0),
                [(2560, 1080)] = (83, 0),
            }
        );

        public static Point 快速切图_纳塔 = new Point(3501, 773,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (-18, 0),
                [(2560, 1080)] = (-16, 0),
            }
        );

        public static Point 快速切图_挪德卡莱 = new Point(2906, 977,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (118, 0),
                [(2560, 1080)] = (83, 1),
            }
        );

        public static Point 快速切图_至冬 = new Point(3501, 977,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (-18, 0),
                [(2560, 1080)] = (-16, 1),
            }
        );

        public static Point 快速切图_渊下宫 = new Point(2906, 1218,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (118, 0),
                [(2560, 1080)] = (83, 0),
            }
        );

        public static Point 快速切图_层岩巨渊 = new Point(3501, 1218,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (-18, 0),
                [(2560, 1080)] = (-16, 0),
            }
        );

        public static Point 快速切图_旧日之海 = new Point(2906, 1424,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (118, 0),
                [(2560, 1080)] = (83, 0),
            }
        );

        public static Point 快速切图_远古圣山 = new Point(3501, 1424,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (-18, 0),
                [(2560, 1080)] = (-16, 0),
            }
        );

        public static Point 快速切图_空之神殿 = new Point(2906, 1629,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (118, 0),
                [(2560, 1080)] = (83, 1),
            }
        );

        public static Point 快速切图_霜月 = new Point(3501, 1629,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (-18, 0),
                [(2560, 1080)] = (-16, 1),
            }
        );

        public static Point 快速切图_尘歌壶 = new Point(2906, 1868,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (118, 0),
                [(2560, 1080)] = (83, 0),
            }
        );

        public static Point 快速切图_千星奇域 = new Point(3501, 1868,
            new Dictionary<(int, int), (int, int)>
            {
                [(3440, 1440)] = (-18, 0),
                [(2560, 1080)] = (-16, 0),
            }
        );

        #endregion

        #region 坐标定义 剧情对话

        public static MatchRect 剧情对话_隐藏按钮区域 = new MatchRect(530, 75, 636, 116, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (112, 1, 77, -1),
                [(3440, 1440)] = (71, 1, 46, -1),
                [(2560, 1080)] = (56, 1, 37, -1),
            }
        );

        #endregion

        #region 坐标定义 钓鱼辅助

        public static MatchRect 钓鱼辅助_鱼竿状态区域 = new MatchRect(3183, 1946, 3259, 2022, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (75, 0, 55, 0),
                [(3440, 1440)] = (53, -1, 40, 3),
                [(2560, 1080)] = (34, -3, 28, 3),
            }
        );

        public static MatchRect 钓鱼辅助_张力区状态区域 = new MatchRect(1435, 197, 2405, 253, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (164, 0, 160, 0),
                [(3440, 1440)] = (107, -1, -110, 5),
                [(2560, 1080)] = (80, -2, -78, 3),
            }
        );

        #endregion

        #region 坐标定义 自动伐木

        public static MatchRect 自动伐木_王树瑞佑区域 = new MatchRect(3631, 1632, 3661, 1662, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (-77, 0, -83, 0),
                [(3440, 1440)] = (-49, 0, -54, 0),
                [(2560, 1080)] = (-38, 0, -41, 0),
            }
        );

        public static MatchRect 自动伐木_获得区域 = new MatchRect(344, 1020, 424, 1060, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (24, 0, 8, 0),
                [(3440, 1440)] = (12, 0, 4, 0),
                [(2560, 1080)] = (11, 0, 5, 0),
            }
        );

        #endregion

        #region 坐标定义 自动烹饪

        public static MatchRect 自动烹饪_烹饪界面区域 = new MatchRect(3658, 331, 3765, 378, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (-84, 0, -178, 0),
                [(3440, 1440)] = (-55, 0, -117, 0),
                [(2560, 1080)] = (-42, 0, -89, 0),
            }
        );

        public static MatchRect 自动烹饪_自动烹饪区域 = new MatchRect(1254, 2005, 1300, 2046, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (221, 0, 207, 0),
                [(3440, 1440)] = (152, 0, 143, 0),
                [(2560, 1080)] = (110, 0, 104, 0),
            }
        );

        public static MatchRect 自动烹饪_手动烹饪区域 = new MatchRect(1624, 2005, 1668, 2046, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (98, 0, 84, 0),
                [(3440, 1440)] = (68, 0, 58, 0),
                [(2560, 1080)] = (49, 0, 42, 0),
            }
        );

        public static MatchRect 自动烹饪_自动烹饪X99确认区域 = new MatchRect(2020, 1492, 2065, 1537, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (-33, 0, -10, 0),
                [(3440, 1440)] = (-23, 0, -33, 0),
                [(2560, 1080)] = (-16, 0, -24, 0),
            }
        );

        public static Point 自动烹饪_X99数量点击坐标 = new Point(2398, 1181,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (-160, 0),
                [(3440, 1440)] = (-110, 0),
                [(2560, 1080)] = (-80, 0),
            }
        );

        public static MatchRect 自动烹饪_品质控制区域 = new MatchRect(1198, 1445, 2640, 1713, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (240, 0, -239, 0),
                [(3440, 1440)] = (164, 0, -166, 0),
                [(2560, 1080)] = (122, 0, -160, 0),
            }
        );

        public static ColorRect 自动烹饪_正在烹饪区域 = new ColorRect(1915, 1875, 1925, 1885, 0,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            },
            (255, 255, 192)
        );

        public static MatchRect 自动烹饪_确认区域 = new MatchRect(1625, 1788, 1668, 1831, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (77, 0, 84, 0),
                [(3440, 1440)] = (68, 0, 58, 0),
                [(2560, 1080)] = (49, 0, 42, 0),
            }
        );

        #endregion

        #region 坐标定义 自动清药

        public static MatchRect 自动清药_寄物装置区域 = new MatchRect(153, 40, 213, 150, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (95, 0, 73, 0),
                [(3440, 1440)] = (62, 0, 48, 0),
                [(2560, 1080)] = (47, 0, 37, 0),
            }
        );

        public static MatchRect 自动清药_打开寄物装置区域 = new MatchRect(2224, 1055, 2253, 1098, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (93, 0, 82, 0),
                [(3440, 1440)] = (65, 0, 58, 0),
                [(2560, 1080)] = (47, 0, 41, 0),
            }
        );

        public static MatchRect 自动清药_确认储存区域 = new MatchRect(1998, 1492, 2042, 1537, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (-25, 0, -40, 0),
                [(3440, 1440)] = (-17, 0, -28, 0),
                [(2560, 1080)] = (-13, 0, -20, 0),
            }
        );

        public static DetectPoint 自动清药_短滚动条坐标 = new DetectPoint(3739, 1712, 5,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (-111, 0),
                [(3440, 1440)] = (-73, 0),
                [(2560, 1080)] = (-55, 0),
            },
            (243, 243, 243)
        );

        public static DetectPoint 自动清药_长滚动条坐标 = new DetectPoint(3739, 1885, 5,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (-111, 0),
                [(3440, 1440)] = (-73, 0),
                [(2560, 1080)] = (-55, 0),
            },
            (243, 243, 243),
            (233, 233, 233)
        );

        public static MatchRect 自动清药_美味确认区域 = new MatchRect(2247, 1845, 2342, 1896, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (-109, 0, -140, 0),
                [(3440, 1440)] = (-75, 0, -97, 0),
                [(2560, 1080)] = (-55, 0, -71, 0),
            }
        );

        public static DetectPoint 自动清药_美味失败坐标 = new DetectPoint(250, 500, 5,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (68, 0),
                [(3440, 1440)] = (45, 0),
                [(2560, 1080)] = (35, 0),
            },
            (0, 0, 0)
        );

        public static MatchRect 自动清药_格子区域 = new MatchRect(2080, 700, 3675, 1380, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (-52, 0, -86, 0),
                [(3440, 1440)] = (-39, 0, -53, 0),
                [(2560, 1080)] = (-26, 0, -43, 0),
            }
        );

        public static MatchRect 自动清药_拆除区域 = new MatchRect(1893, 703, 2793, 1183, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        public static ColorRect 自动清药_清空容量坐标 = new ColorRect(170, 525, 171, 660, 5,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (91, 0),
                [(3440, 1440)] = (55, 0),
                [(2560, 1080)] = (42, 0),
            },
            (230, 242, 246),
            (233, 229, 220)
        );

        public static ColorRect 自动清药_燃料坐标 = new ColorRect(1750, 1985, 2200, 1986, 0,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            },
            (254, 246, 183)
        );

        #endregion

        #region 其他坐标

        //左上角派蒙头像存在为主界面
        public static MatchRect 主界面区域 = new MatchRect(72, 100, 106, 134, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (122, 0, 112, 0),
                [(3440, 1440)] = (78, 0, 76, 0),
                [(2560, 1080)] = (60, 0, 65, 0),
            }
        );

        //主界面按下ESC后会打开→派蒙菜单页面 而此处检测的是菜单中的相机功能图标代表处于菜单界面
        public static MatchRect 派蒙菜单区域 = new MatchRect(46, 730, 146, 830, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (114, 0, 110, 0),
                [(3440, 1440)] = (77, 0, 72, 0),
                [(2560, 1080)] = (56, 0, 56, 0),
            }
        );

        //登录界面操作结束后检测 点击进入 
        public static MatchRect 点击进入区域 = new MatchRect(1834, 2026, 2003, 2054, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (25, 0, -15, 0),
                [(3440, 1440)] = (14, 0, 0, 0),
                [(2560, 1080)] = (12, 0, 0, 0),
            }
        );

        //进入游戏后可能会显示月卡 空月祝福 领取界面 
        public static MatchRect 空月祝福 = new MatchRect(1868, 1308, 1968, 1408, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //检测账号是否在另一处设备重复登录/连接服务器失败的提示界面，检测到的是重复登录时按下esc后会返回登录界面，检测到连接服务器失败说明网络出现问题了直接结束不执行登录了
        //账号列表登录时 如果当前账号未输入过账号密码登录也检测到“检测账号是否在另一处设备重复登录”说明当前是处于其他账号中直接esc回到登录大厅就行了
        //分组账号列表登录时 如果检测到检测账号是否在另一处设备重复登录时注意是成功登录后的持续检测中检测到的说明当前账号被顶了所以直接按流程回到登录大厅执行登录下一个账号，如果检测到连接服务器失败直接结束不执行登录了
        public static MatchRect 连接已断开 = new MatchRect(1636, 986, 2206, 1056, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );
        #endregion

        #region 退出游戏-正常退出

        //派蒙菜单页面左下角 退出游戏 按钮的坐标
        public static Point 退出游戏点击坐标 = new Point(92, 2055,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (114, 0),
                [(3440, 1440)] = (68, 0),
                [(2560, 1080)] = (49, 0),
            }
        );

        //点击 退出游戏 按钮后显示→ 退出至登录界面选项
        public static MatchRect 退出至登录界面 = new MatchRect(1378, 1055, 1425, 1105, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (180, -2, 165, 3),
                [(3440, 1440)] = (124, -1, 115, 2),
                [(2560, 1080)] = (91, 0, 82, 0),
            }
        );

        //登录大厅点击{登录或退出区域}后的退出登录窗口
        public static MatchRect 是否保留登录记录 = new MatchRect(1374, 920, 1430, 1172, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //登录大厅点击{登录或退出区域}后的退出登录窗口 → 确认退出登录 的点击坐标
        public static Point 退出登录点击坐标 = new Point(2175, 1353,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (114, 0),
                [(3440, 1440)] = (68, 0),
                [(2560, 1080)] = (49, 0),
            }
        );

        #endregion

        #region 退出登录-用户中心退出

        //派蒙菜单页面左下角 设置 戏按钮的坐标
        public static Point 设置点击坐标 = new Point(95, 1646,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        //派蒙菜单页面左下角 设置 后→打开的设置界面 
        public static MatchRect 设置界面区域 = new MatchRect(286, 68, 390, 124, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //设置 界面内的 账户 点击坐标
        public static Point 账户点击坐标 = new Point(352, 1140,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        //设置 界面内点击 账户 后的→ 跳转 按钮区域
        public static MatchRect 账户跳转区域 = new MatchRect(3518, 394, 3576, 452, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //点击 点击跳转之后打开的 → 用户中心界面的刷新按钮区域
        public static MatchRect 用户中心刷新按钮区域 = new MatchRect(3616, 30, 3820, 97, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //用户中心界面 检测到 通用_切换账号区域 之后 确认切换账号 的点击坐标
        public static Point 切换账号击坐标 = new Point(3250, 420,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        //登录流程中客户区字幕（头像 + 文字）的显示位置，用法和 钓鱼辅助_字幕显示坐标 一致
        //★ 数值先照抄 钓鱼辅助 的，实际位置待实测调整
        public static Point 登录_字幕显示坐标 = new Point(60, 520,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (128, 0),
                [(3440, 1440)] = (86, 0),
                [(2560, 1080)] = (64, 0),
            }
        );

        #endregion

        #region 登录账号

        //处于登录大厅时有两个状态，一个是当前客户区没有账号在登录点击{登录或退出区域}后直接是输入账号密码界面，一个是当前客户区有账号还未退出点击{登录或退出区域}后弹出的是一些退出账号的步骤操作
        //{登录或退出区域}这里只有一个模版如果检测的到那就说明当前客户区没有账号点击后就是输入界面，如果检测不到但是{点击进入区域}检测的到就说明当前客户区已有账号了点击{点击进入区域}后直接就进入游戏了，如果刚执行时还未输入过账号密码时这里检测到{点击进入区域}不能点击进入游戏
        //所以 ui 点击确认登录激活窗口后检测到了{点击进入区域}时可以直接点击{登录或退出区域}执行退出账号操作，如果检测到了{登录或退出区域}可以直接点击此处然后就是输入账号密码阶段了
        public static MatchRect 登录或退出区域 = new MatchRect(3606, 1930, 3690, 2008, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //如果游戏有保存登录记录的话从用户中心退出游戏之后会处于无法输入账号密码的的账号登录界面，所以检测到 登录其他账号区域 就点击此处会打开使用账号密码登录其他账号
        public static MatchRect 登录其他账号区域 = new MatchRect(1772, 1380, 2068, 1436, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //检测到这里之后就处于 输入账号密码了 输入之后可以点击此处登录，进入游戏并非直接进入游戏就是登录账号的意思
        public static MatchRect 进入游戏区域 = new MatchRect(1816, 1392, 2022, 1452, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //账号输入框 点击后可粘贴账号 
        public static Point 输入账号点击坐标 = new Point(2000, 795,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        //密码输入框 点击后可粘贴密码 点击账号输入框粘贴后可以直接TAB切换至密码输入框内
        public static Point 输入密码点击坐标 = new Point(2000, 965,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        //检输入账号密码点击进入之后需要同意才能登录
        public static MatchRect 同意登录区域 = new MatchRect(2138, 1194, 2240, 1246, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //同意登录后如果账号有问题时此处区域能检测到，说明账号或密码有问题，账号密码正确时此处区域不会检测到
        public static MatchRect 账号密码出错区域 = new MatchRect(1738, 1054, 2100, 1108, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //同意登录后如果没有出错的话过一会会检测到 欢迎进入游戏 区域，说明账号登录成功了马上就会检测到 点击进入 了
        public static MatchRect 欢迎进入游戏区域 = new MatchRect(1738, 1054, 2100, 1108, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //检测到这里说明"您的账号存在安全风险，为确保是您本人操作，请先验证身份后再登录"说明验证失效了需要重新验证此设备
        public static MatchRect 验证身份 = new MatchRect(1718, 1010, 2206, 1068, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //"您的账号存在安全风险，为确保是您本人操作，请先验证身份后再登录"点击"取消验证"位置即可关闭此弹窗不进行验证
        public static Point 取消验证 = new Point(2426, 862,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        #endregion

        #region 聊天界面

        //聊天界面内的"消息发送"按钮
        public static MatchRect 消息发送 = new MatchRect(2096, 1988, 2218, 2052, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //聊天界面中的"当前队伍"频道按钮
        public static MatchRect 当前队伍 = new MatchRect(164, 252, 344, 312, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //聊天界面中的"当前队伍"是排在最上面的也就这个坐标位置选择后第一个聊天频道的背景会变成65, 72, 86
        //所以检测到这个颜色说明已经选择的了最上面的 频道，如果没有就点击
        //这个频道列表是可以滚动的所以如果当前队伍频道不在最上面就需要滚动到最顶部然后点击这个坐标，点击后如果还检测不到说明还不在联机中。
        //"当前队伍"只有在联机时才会有
        public static DetectPoint 选择当前队伍 = new DetectPoint(30, 290, 0,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            },
            (65, 72, 86)
        );

        //处于聊天界面时输入框处于焦点时这里会是白色的，说明可以输入文字了，如果不是白色说明没有焦点需要点击或回车输入框后才能输入文字 
        public static DetectPoint 聊天输入框 = new DetectPoint(760, 2020, 0,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            },
            (255, 255, 255)
        );

        //聊天界面中队友发送消息，个区域检测到说明有队友发送的消息，最底部的消息是最新的
        public static MatchRect 队友消息 = new MatchRect(828, 208, 860, 1872, 0.8,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //聊天界面中自己自动回复队友的消息，如果自己自动回复的消息比队友发送的最新消息的y要低时说明自己已经回复了队友的消息，如果对应的消息比自己的y要地说明没有恢复队友消息
        public static MatchRect 自己消息 = new MatchRect(1790, 208, 1976, 1874, 0.8,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        #endregion  

        #region 联机

        //主界面中可查看的当前是否处于多人模式的联机状态
        public static MatchRect 多人模式 = new MatchRect(692, 58, 752, 118, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //主界面中可查看的当前的联机队友
        public static MatchRect 联机队友 = new MatchRect(3630, 500, 3662, 870, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //联机大厅内的"世界权限"按钮
        public static MatchRect 世界权限 = new MatchRect(546, 2008, 848, 2060, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        //不允许加入
        public static Point 不允许加入 = new Point(670, 1740,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        //直接加入
        public static Point 直接加入 = new Point(670, 1840,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        //确认后可加入
        public static Point 确认后可加入 = new Point(670, 1940,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (0, 0),
                [(3440, 1440)] = (0, 0),
                [(2560, 1080)] = (0, 0),
            }
        );

        #endregion

        #region 初始化 - 自动注册所有坐标

        static CoordinateFormats()
        {
            // 自动注册所有坐标
            var fields = typeof(CoordinateFormats)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType != typeof(string) && !f.FieldType.IsPrimitive && !f.FieldType.IsEnum);

            foreach (var field in fields)
            {
                var value = field.GetValue(null);
                if (value != null)
                {
                    _coords[field.Name] = value;
                }
            }

            WindowBoundsBridge.Changed += OnBoundsChanged;
        }

        private static void OnBoundsChanged(WindowBoundsInfo info)
        {
            // 窗口无效 → 不清缓存，直接返回
            if (info.IsEmpty)
            {
                return;
            }

            // 分辨率不支持 → 不清缓存，直接返回
            if (!IsResolutionSupported(info.Width, info.Height))
            {
                return;
            }

            bool processChanged = (info.ProcessName != _lastProcessName);
            bool resolutionChanged = (info.Width != _lastWidth || info.Height != _lastHeight);
            bool positionChanged = (info.Left != _lastLeft || info.Top != _lastTop);

            if (_hasLastInfo && !processChanged && !resolutionChanged && !positionChanged)
            {
                return;
            }

            _lastProcessName = info.ProcessName;
            _lastLeft = info.Left;
            _lastTop = info.Top;
            _lastWidth = info.Width;
            _lastHeight = info.Height;
            _hasLastInfo = true;

            UpdateWindowInfo(info.Left, info.Top, info.Width, info.Height);
            UpdateProcessInfo(info.ProcessName);
            UpdateCache();

            if (EnableLog == 1)
            {
                Debug.WriteLine("");
                Debug.WriteLine("========== 坐标缓存更新 ==========");
                Debug.WriteLine($"进程: {info.ProcessName} 窗口分辨率: {info.Width}x{info.Height} 窗口左上角: ({info.Left}, {info.Top})");

                foreach (var kvp in _coords)
                {
                    var cached = _cache.TryGetValue(kvp.Key, out var c) ? c : null;
                    LogCoordDetail(kvp.Key, kvp.Value, cached, info);
                }

                Debug.WriteLine("====================================");
            }
        }

        #endregion

        #region 缓存更新

        private static void UpdateCache()
        {
            if (!_hasProcess || !_hasWindowInfo)
            {
                return;
            }

            _cache.Clear();

            foreach (var kvp in _coords)
            {
                object? cached = kvp.Value switch
                {
                    Point p => p.ToScreen(),
                    DetectPoint dp => dp.ToScreen(),
                    MatchRect mr => mr.ToScreen(),
                    ColorRect cr => cr.ToScreen(),
                    _ => null
                };
                if (cached != null)
                {
                    _cache[kvp.Key] = cached;
                }
            }

            _cacheReady = true;
        }

        #endregion

        #region 日志

        private static void LogCoordDetail(string name, object source, object? cached, WindowBoundsInfo info)
        {
            if (EnableLog != 1) return;
            if (cached == null) return;

            switch (cached)
            {
                case Point pointCache:
                    var pointBase = (Point)source;
                    Debug.WriteLine($"{name}_缓存坐标: ({pointCache.X}, {pointCache.Y})  客户区坐标: ({pointCache.X - info.Left}, {pointCache.Y - info.Top})  基础坐标: ({pointBase.X}, {pointBase.Y})");
                    break;

                case DetectPoint detectCache:
                    var detectBase = (DetectPoint)source;
                    Debug.WriteLine($"{name}_缓存坐标: ({detectCache.X}, {detectCache.Y})  客户区坐标: ({detectCache.X - info.Left}, {detectCache.Y - info.Top})  基础坐标: ({detectBase.X}, {detectBase.Y})  容差: {detectCache.Tolerance}");
                    break;

                case MatchRect matchCache:
                    var matchBase = (MatchRect)source;
                    var cr = matchCache.Rect;
                    var br = matchBase.Rect;
                    Debug.WriteLine($"{name}_缓存区域: ({cr.X1}, {cr.Y1}, {cr.X2}, {cr.Y2})  客户区区域: ({cr.X1 - info.Left}, {cr.Y1 - info.Top}, {cr.X2 - info.Left}, {cr.Y2 - info.Top})  基础区域: ({br.X1}, {br.Y1}, {br.X2}, {br.Y2})  阈值: {matchCache.Threshold}");
                    break;

                case ColorRect colorCache:
                    var colorBase = (ColorRect)source;
                    var ccr = colorCache.Rect;
                    var cbr = colorBase.Rect;
                    Debug.WriteLine($"{name}_缓存区域: ({ccr.X1}, {ccr.Y1}, {ccr.X2}, {ccr.Y2})  客户区区域: ({ccr.X1 - info.Left}, {ccr.Y1 - info.Top}, {ccr.X2 - info.Left}, {ccr.Y2 - info.Top})  基础区域: ({cbr.X1}, {cbr.Y1}, {cbr.X2}, {cbr.Y2})  容差: {colorCache.Tolerance}");
                    break;
            }
        }

        #endregion

        #region 获取坐标

        /// <summary>
        /// 获取缓存的坐标（屏幕坐标）
        /// </summary>
        public static T? GetCached<T>(string name) where T : class
        {
            return _cache.TryGetValue(name, out var cached) ? cached as T : null;
        }

        #endregion
    }
}