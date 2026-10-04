using LvchaxsBS.Services;
using LvchaxsBS.Services.Hooks;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.Core
{
    public static class TemplateManager
    {
        private const int BASE_WIDTH = 1920;
        private const int BASE_HEIGHT = 1080;

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

        private static int _currentWidth = 0;
        private static int _currentHeight = 0;
        private static bool _hasWindowInfo = false;
        private static string _processName = "";

        // 模板字典
        private static readonly Dictionary<string, Bitmap?> _templates = new Dictionary<string, Bitmap?>();
        // 缓存字典
        private static readonly Dictionary<string, Bitmap?> _cache = new Dictionary<string, Bitmap?>();

        private static int _lastWidth = 0;
        private static int _lastHeight = 0;
        private static bool _hasLastInfo = false;

        public static bool HasWindowInfo => _hasWindowInfo;

        public static void UpdateWindowInfo(int width, int height, string processName)
        {
            _currentWidth = width;
            _currentHeight = height;
            _processName = processName;
            _hasWindowInfo = true;
            UpdateCache();
        }

        private static bool IsResolutionSupported(int width, int height)
        {
            return _supportedResolutions.Contains((width, height));
        }

        public static double GetScaleY()
        {
            if (_currentHeight == 0) return 1.0;
            if (_currentHeight >= BASE_HEIGHT) return 1.0;
            return (double)_currentHeight / BASE_HEIGHT;
        }

        private static void UpdateCache()
        {
            if (!_hasWindowInfo)
            {
                return;
            }

            if (!IsResolutionSupported(_currentWidth, _currentHeight))
            {
                return;
            }

            bool resolutionChanged = (_currentWidth != _lastWidth || _currentHeight != _lastHeight);

            if (!_hasLastInfo || resolutionChanged)
            {
                double scale = GetScaleY();

                foreach (var key in _cache.Keys)
                {
                    _cache[key]?.Dispose();
                }
                _cache.Clear();

                int loadedCount = 0;
                foreach (var kvp in _templates)
                {
                    var scaled = ScaleTemplate(kvp.Value, scale, scale);
                    _cache[kvp.Key] = scaled;
                    if (kvp.Value != null) loadedCount++;
                }

                _lastWidth = _currentWidth;
                _lastHeight = _currentHeight;
                _hasLastInfo = true;

                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("========== 模版缓存更新 ==========");
                System.Diagnostics.Debug.WriteLine($"进程: {_processName} 窗口分辨率: {_currentWidth}x{_currentHeight}  缩放比例: {scale:F3}  模版数量: {loadedCount}/{_templates.Count}");

                foreach (var kvp in _templates)
                {
                    LogCacheDetail(kvp.Key, kvp.Value, _cache.TryGetValue(kvp.Key, out var cached) ? cached : null);
                }

                System.Diagnostics.Debug.WriteLine("====================================");
            }
        }

        private static void LogCacheDetail(string name, Bitmap? source, Bitmap? cached)
        {
            if (source == null)
            {
                System.Diagnostics.Debug.WriteLine($"{name}: 基础模板为空");
                return;
            }

            if (cached == null)
            {
                System.Diagnostics.Debug.WriteLine($"{name}: 缓存生成失败  基础尺寸: ({source.Width}, {source.Height})");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"{name}_缓存尺寸: ({cached.Width}, {cached.Height})  基础尺寸: ({source.Width}, {source.Height})");
        }

        private static Bitmap? ScaleTemplate(Bitmap? source, double scaleX, double scaleY)
        {
            if (source == null) return null;

            int newWidth = (int)(source.Width * scaleX);
            int newHeight = (int)(source.Height * scaleY);

            if (newWidth <= 0 || newHeight <= 0) return null;

            var scaled = new Bitmap(newWidth, newHeight);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, newWidth, newHeight);
            }
            return scaled;
        }

        #region 模板加载

        /// <summary>
        /// 从路径添加模板，自动解析名称
        /// 例如: "pack://application:,,,/Resources/Template/快速传送/掩码1.png"
        /// 解析为: "快速传送_掩码1"
        /// </summary>
        private static void AddTemplateFromPath(string uriString)
        {
            string relativePath = uriString;

            // 优先匹配 /Resources/Template/
            int index = relativePath.IndexOf("/Resources/Template/");
            if (index >= 0)
            {
                relativePath = relativePath.Substring(index + "/Resources/Template/".Length);
            }
            else
            {
                // 兜底：匹配 /Resources/
                int idx2 = relativePath.IndexOf("/Resources/");
                if (idx2 >= 0)
                    relativePath = relativePath.Substring(idx2 + "/Resources/".Length);
            }

            // "快速传送/掩码1.png" -> "快速传送_掩码1"
            string name = relativePath.Replace("/", "_").Replace("\\", "_");
            name = name.EndsWith(".png") ? name.Substring(0, name.Length - 4) : name;

            if (_templates.ContainsKey(name))
            {
                _templates[name]?.Dispose();
                _templates.Remove(name);
            }

            var bitmap = LoadBitmapFromResource(uriString);
            _templates[name] = bitmap;
        }

        public static void LoadTemplates()
        {
            try
            {
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/传送.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/靶场.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/七天神像.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/新月神像.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/传送锚点.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/秘境1.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/秘境2.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/列车.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/宅邸.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/幽境危战.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/青玄乘阳华盖.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/快速传送/口袋锚点.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/剧情对话/隐藏按钮.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/钓鱼辅助/未抛钩.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/钓鱼辅助/已抛钩.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/钓鱼辅助/上钩了.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动伐木/王树瑞佑.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动伐木/获得.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动伐木/退出游戏.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动伐木/退出至登录界面.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动伐木/点击进入.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动烹饪/烹饪界面.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动烹饪/自动烹饪.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动烹饪/手动烹饪.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动烹饪/确认.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/寄物装置.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/打开寄物装置.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/美味确认.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/拆除.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/凝造.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/提瓦特煎蛋.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/烤肉排.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/蒙德烤鱼.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/摩拉肉.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/爆炒肉片.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/鸟蛋烧.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/庄园烤松饼.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/素鲍鱼.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/蟹黄豆腐.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/什锦炒面.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/乌冬面.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/绿汁脆球.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/脆饼珐提.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/多彩之森.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/蟹黄火腿焗时蔬.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/绯樱饼.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/椰炭饼.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/塔塔可.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/夏槲蛋糕.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/薄荷泡泡糖.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/北地烟熏鸡.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/山珍热卤面.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/阿如拌饭.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/桔香鸭胸肉.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/清心花饼.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/炸鱼薯条.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/巧克力.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/钱汤馒头.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/白灵果派.png");
                AddTemplateFromPath("pack://application:,,,/Resources/Template/自动清药/肉末酿豆腐.png");

                AddTemplateFromPath("pack://application:,,,/Resources/Template/通用模版/主界面.png");

                System.Diagnostics.Debug.WriteLine($"模板加载成功: {_templates.Count} 个");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载模板失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取模板（原始尺寸）
        /// </summary>
        public static Bitmap? GetTemplate(string name)
        {
            return _templates.TryGetValue(name, out var bitmap) ? bitmap : null;
        }

        /// <summary>
        /// 获取缓存的模板（缩放后）
        /// </summary>
        public static Bitmap? GetCachedTemplate(string name)
        {
            return _cache.TryGetValue(name, out var cached) ? cached : null;
        }

        private static Bitmap? LoadBitmapFromResource(string uriString)
        {
            try
            {
                var uri = new Uri(uriString, UriKind.RelativeOrAbsolute);
                var bitmapImage = new BitmapImage(uri);
                bitmapImage.Freeze();

                using (var ms = new MemoryStream())
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
                    encoder.Save(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                    return new Bitmap(ms);
                }
            }
            catch
            {
                return null;
            }
        }

        #endregion

        static TemplateManager()
        {
            WindowFocusService.BoundsChanged += (s, info) =>
            {
                if (!info.IsEmpty)
                {
                    UpdateWindowInfo(info.Width, info.Height, info.ProcessName);
                }
            };
        }
    }
}