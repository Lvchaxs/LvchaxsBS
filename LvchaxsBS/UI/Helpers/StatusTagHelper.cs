using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 状态标签三态：
    ///   Good    = 绿（运行中 / 已就位 / 匹配成功）
    ///   Bad     = 红（已停止 / 未就位 / 匹配失败）
    ///   Neutral = 灰（无数据 / 无意义状态）
    /// </summary>
    public enum TagState
    {
        Good,
        Bad,
        Neutral
    }

    /// <summary>
    /// "TextBlock + Border" 组成的状态标签。
    /// 把三态配色逻辑收拢到一处，避免每个页面重复写 RGB。
    /// </summary>
    public sealed class StatusTagItem
    {
        // 绿色
        private static readonly Brush GoodBg = new SolidColorBrush(Color.FromRgb(209, 250, 229));
        private static readonly Brush GoodBorder = new SolidColorBrush(Color.FromRgb(167, 243, 208));
        private static readonly Brush GoodFg = new SolidColorBrush(Color.FromRgb(6, 95, 70));

        // 红色
        private static readonly Brush BadBg = new SolidColorBrush(Color.FromRgb(254, 226, 226));
        private static readonly Brush BadBorder = new SolidColorBrush(Color.FromRgb(252, 165, 165));
        private static readonly Brush BadFg = new SolidColorBrush(Color.FromRgb(153, 27, 27));

        // 灰色
        private static readonly Brush NeutralBg = new SolidColorBrush(Color.FromRgb(243, 244, 246));
        private static readonly Brush NeutralBorder = new SolidColorBrush(Color.FromRgb(229, 231, 235));
        private static readonly Brush NeutralFg = new SolidColorBrush(Color.FromRgb(107, 114, 128));

        static StatusTagItem()
        {
            GoodBg.Freeze(); GoodBorder.Freeze(); GoodFg.Freeze();
            BadBg.Freeze(); BadBorder.Freeze(); BadFg.Freeze();
            NeutralBg.Freeze(); NeutralBorder.Freeze(); NeutralFg.Freeze();
        }

        public TextBlock? Text { get; init; }
        public Border? Border { get; init; }

        public bool IsValid => Text != null || Border != null;

        public void Set(string text, TagState state)
        {
            if (Text != null) Text.Text = text ?? string.Empty;
            ApplyColors(state);
        }

        public void Set(string text, bool isGood)
        {
            Set(text, isGood ? TagState.Good : TagState.Bad);
        }

        public void SetGood(string text) => Set(text, TagState.Good);
        public void SetBad(string text) => Set(text, TagState.Bad);
        public void SetNeutral(string text) => Set(text, TagState.Neutral);

        private void ApplyColors(TagState state)
        {
            Brush bg, border, fg;

            switch (state)
            {
                case TagState.Good: bg = GoodBg; border = GoodBorder; fg = GoodFg; break;
                case TagState.Bad: bg = BadBg; border = BadBorder; fg = BadFg; break;
                default: bg = NeutralBg; border = NeutralBorder; fg = NeutralFg; break;
            }

            if (Border != null)
            {
                Border.Background = bg;
                Border.BorderBrush = border;
            }
            if (Text != null)
            {
                Text.Foreground = fg;
            }
        }
    }

    /// <summary>
    /// 静态方法式入口（无对象场景时用）。
    /// </summary>
    public static class StatusTagHelper
    {
        private static readonly StatusTagItem _shared = new();

        public static void Update(TextBlock? text, Border? border, string textValue, TagState state)
        {
            new StatusTagItem { Text = text, Border = border }.Set(textValue, state);
        }

        public static void Update(TextBlock? text, Border? border, string textValue, bool isGood)
        {
            new StatusTagItem { Text = text, Border = border }.Set(textValue, isGood);
        }
    }
}