using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LvchaxsBS.Config;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 外观相关的全局通知服务（窗口标题、圆角、透明度等）。
    /// </summary>
    public static class AppearanceService
    {
        /// <summary>窗口标题变化（参数为新标题）</summary>
        public static event EventHandler<string>? WindowTitleChanged;

        /// <summary>广播窗口标题变化。由个性化页调用。</summary>
        public static void NotifyWindowTitleChanged(string newTitle)
        {
            WindowTitleChanged?.Invoke(null, newTitle ?? "");
        }

        // ============ 透明度（控件 / 窗口） ============

        /// <summary>
        /// 受「控件透明度」影响的主题背景色键。
        /// 覆盖所有卡片、窗口标题栏，以及以这些色为背景的按钮
        /// （首页 / 设置 / 个性化 / 语音 / 截图 / 主题 …）。
        /// 连带悬停、按下态一起调，否则透明卡片一悬停就会冒出一块实心色。
        /// </summary>
        private static readonly string[] ControlOpacityKeys =
        {
            "CardBackgroundBrush",
            "TitleBarBackgroundBrush",
            "HoverBackgroundBrush",
            "PressedBackgroundBrush",
        };

        /// <summary>
        /// 受「窗口透明度」影响的主题背景色键。
        /// 只有窗口底色（RootBorder.Background），所以调它不会影响卡片和文字。
        /// </summary>
        private static readonly string[] WindowOpacityKeys =
        {
            "AppBackgroundBrush",
        };

        /// <summary>
        /// 各主题下这些键的原始颜色。首次读取后缓存，
        /// 避免透明度被反复叠加衰减（每次都拿原始色重新算 alpha）。
        /// </summary>
        private static readonly Dictionary<(AppTheme theme, string key), Color> BaseColors = new();

        /// <summary>
        /// 应用「控件透明度」：0 = 不透明，100 = 全透明。
        /// 作用于所有卡片、窗口标题栏和相关按钮的背景。
        /// </summary>
        public static void ApplyControlOpacity(double value) => ApplyOpacity(ControlOpacityKeys, value);

        /// <summary>
        /// 应用「窗口透明度」：0/1 = 不透明，100 = 全透明。
        /// 只作用于窗口底色（RootBorder.Background）。
        /// </summary>
        public static void ApplyWindowOpacity(double value) => ApplyOpacity(WindowOpacityKeys, value);

        /// <summary>按配置重新应用两种透明度（切主题后调用）。</summary>
        public static void ReapplyOpacities()
        {
            var p = ConfigManager.Get<PersonalizationSettings>();
            ApplyControlOpacity(p.CardOpacity);
            ApplyWindowOpacity(p.WindowOpacity);
        }

        /// <summary>
        /// 把指定主题背景键覆盖成带 alpha 的同色画刷。
        /// Application.Resources 的自身条目优先级高于 MergedDictionaries 里的主题，
        /// 所以无需改主题文件；但切主题后必须再调一次（颜色要跟着新主题走）。
        /// </summary>
        private static void ApplyOpacity(string[] keys, double value)
        {
            var app = Application.Current;
            if (app == null) return;

            double keep = 1.0 - Math.Clamp(value, 0, 100) / 100.0;   // 不透明度系数
            byte alpha = (byte)Math.Round(keep * 255);

            foreach (string key in keys)
            {
                if (GetBaseColor(app, ThemeService.Current, key) is not Color baseColor) continue;

                var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                brush.Freeze();
                app.Resources[key] = brush;
            }
        }

        /// <summary>
        /// 从当前主题字典里取某键的原始颜色。
        /// 刻意不走 Application 资源查找，否则会读到自己刚写入的带 alpha 画刷。
        /// </summary>
        private static Color? GetBaseColor(Application app, AppTheme theme, string key)
        {
            if (BaseColors.TryGetValue((theme, key), out Color cached)) return cached;

            var themeDict = app.Resources.MergedDictionaries.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.EndsWith("Light.xaml") ||
                 d.Source.OriginalString.EndsWith("Dark.xaml")));

            if (themeDict?[key] is SolidColorBrush sb)
            {
                // alpha 完全由滑块决定，这里只取 RGB
                Color c = Color.FromRgb(sb.Color.R, sb.Color.G, sb.Color.B);
                BaseColors[(theme, key)] = c;
                return c;
            }

            return null;
        }
    }
}
