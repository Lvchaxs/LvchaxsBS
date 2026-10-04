using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using LvchaxsBS.Services.Hooks;

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

                return new MatchRect(x1, y1, x2, y2, Threshold);
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

        public static Point 自动烹饪_字幕显示坐标 = new Point(155, 1370,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (97, 0),
                [(3440, 1440)] = (62, 0),
                [(2560, 1080)] = (47, 0),
            }
        );

        public static Point 钓鱼辅助_字幕显示坐标 = new Point(60, 520,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (128, 0),
                [(3440, 1440)] = (86, 0),
                [(2560, 1080)] = (64, 0),
            }
        );

        public static Point 自动伐木_字幕显示坐标 = new Point(60, 520,
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

        public static MatchRect 快速传送_右侧列表区域 = new MatchRect(2590, 900, 2710, 1870, 0.9,
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

        public static MatchRect 自动伐木_退出游戏区域 = new MatchRect(71, 70, 121, 120, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (114, 0, 110, 0),
                [(3440, 1440)] = (77, 0, 72, 0),
                [(2560, 1080)] = (56, 0, 56, 0),
            }
        );

        public static MatchRect 自动伐木_退出至登录界面 = new MatchRect(1378, 1055, 1425, 1105, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (180, -2, 165, 3),
                [(3440, 1440)] = (124, -1, 115, 2),
                [(2560, 1080)] = (91, 0, 82, 0),
            }
        );

        public static MatchRect 自动伐木_点击进入区域 = new MatchRect(1834, 2026, 2003, 2054, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (25, 0, -15, 0),
                [(3440, 1440)] = (14, 0, 0, 0),
                [(2560, 1080)] = (12, 0, 0, 0),
            }
        );

        public static Point 退出游戏点击坐标 = new Point(92, 2055,
            new Dictionary<(int, int), (int, int)>
            {
                [(5120, 2160)] = (114, 0),
                [(3440, 1440)] = (68, 0),
                [(2560, 1080)] = (49, 0),
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

        #region 自动化


        public static MatchRect 自动化_小地图区域 = new MatchRect(211, 124, 461, 374, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        public static MatchRect 自动化_主界面区域 = new MatchRect(72, 100, 106, 134, 0.9,
            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (122, 0, 112, 0),
                [(3440, 1440)] = (78, 0, 76, 0),
                [(2560, 1080)] = (60, 0, 65, 0),
            }
        );

        public static MatchRect 自动化_冒险之证区域 = new MatchRect(2440, 1289, 2512, 1361, 0.9,

            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        public static MatchRect 自动化_冒险之证讨伐区域 = new MatchRect(2440, 1289, 2512, 1361, 0.9,

            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
            }
        );

        public static MatchRect 自动化_冒险之证首领材料区域 = new MatchRect(2440, 1289, 2512, 1361, 0.9, 

            new Dictionary<(int, int), (int x1, int y1, int x2, int y2)>
            {
                [(5120, 2160)] = (0, 0, 0, 0),
                [(3440, 1440)] = (0, 0, 0, 0),
                [(2560, 1080)] = (0, 0, 0, 0),
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

            WindowFocusService.BoundsChanged += OnBoundsChanged;
        }

        private static void OnBoundsChanged(object? sender, WindowFocusService.WindowBounds info)
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

        private static void LogCoordDetail(string name, object source, object? cached, WindowFocusService.WindowBounds info)
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