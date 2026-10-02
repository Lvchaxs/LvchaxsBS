using System;
using System.Windows;
using System.Windows.Media;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 主题色读取服务。
    /// 统一从 Application 资源里读画刷，找不到时给一个兜底色。
    /// </summary>
    public static class ThemeBrushProvider
    {
        // 常用色名（避免各处拼字符串拼错）
        public const string Primary = "PrimaryBrush";
        public const string PrimaryLight = "PrimaryLightBrush";
        public const string PrimaryDark = "PrimaryDarkBrush";
        public const string TextPrimary = "TextPrimaryBrush";
        public const string TextSecondary = "TextSecondaryBrush";
        public const string TextDisabled = "TextDisabledBrush";
        public const string BorderNormal = "BorderBrush";
        public const string BorderStrong = "BorderStrongBrush";
        public const string Card = "CardBackgroundBrush";
        public const string Hover = "HoverBackgroundBrush";
        public const string ToggleOn = "ToggleOnBrush";
        public const string ToggleOff = "ToggleOffBrush";

        /// <summary>
        /// 按资源键取画刷，找不到时返回 fallback。
        /// </summary>
        public static Brush Get(string key, Color fallback)
        {
            if (Application.Current?.TryFindResource(key) is Brush b)
                return b;
            return new SolidColorBrush(fallback);
        }

        // ============ 常用快捷方法 ============

        public static Brush PrimaryBrush()
            => Get(Primary, Color.FromRgb(0x3B, 0x82, 0xF6));

        public static Brush PrimaryLightBrush()
            => Get(PrimaryLight, Color.FromRgb(0xDB, 0xEA, 0xFE));

        public static Brush PrimaryDarkBrush()
            => Get(PrimaryDark, Color.FromRgb(0x1D, 0x4E, 0xD8));

        public static Brush TextPrimaryBrush()
            => Get(TextPrimary, Color.FromRgb(0x00, 0x00, 0x00));

        public static Brush TextSecondaryBrush()
            => Get(TextSecondary, Color.FromRgb(0x6B, 0x72, 0x80));

        public static Brush TextDisabledBrush()
            => Get(TextDisabled, Color.FromRgb(0x9C, 0xA3, 0xAF));

        public static Brush BorderBrush()
            => Get(BorderNormal, Color.FromRgb(0xE5, 0xE7, 0xEB));

        public static Brush BorderStrongBrush()
            => Get(BorderStrong, Color.FromRgb(0xD1, 0xD5, 0xDB));

        public static Brush CardBackgroundBrush()
            => Get(Card, Color.FromRgb(0xFF, 0xFF, 0xFF));

        public static Brush HoverBackgroundBrush()
            => Get(Hover, Color.FromRgb(0xF9, 0xFA, 0xFB));

        public static Brush ToggleOnBrush()
            => Get(ToggleOn, Color.FromRgb(0x22, 0xC5, 0x5E));

        public static Brush ToggleOffBrush()
            => Get(ToggleOff, Color.FromRgb(0xE5, 0xE7, 0xEB));
    }
}