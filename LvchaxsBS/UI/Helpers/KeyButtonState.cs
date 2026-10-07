using System.Windows;
using System.Windows.Media;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 触发键页按键的"被其他功能占用"附加状态。
    /// 用附加属性而不是 Button 自带属性，是为了不和 Tag（选中标记）打架，
    /// 同时让 KeyButtonStyle 的模板能直接绑定出功能图标。
    /// </summary>
    public static class KeyButtonState
    {
        /// <summary>
        /// 占用该键的功能图标（如"剧情对话.png"）。null = 未被占用。
        /// Image.Source 为 null 时本身不绘制，所以无需额外控制显隐。
        /// </summary>
        public static readonly DependencyProperty OccupiedIconProperty =
            DependencyProperty.RegisterAttached(
                "OccupiedIcon", typeof(ImageSource), typeof(KeyButtonState),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static ImageSource? GetOccupiedIcon(DependencyObject obj)
            => (ImageSource?)obj.GetValue(OccupiedIconProperty);

        public static void SetOccupiedIcon(DependencyObject obj, ImageSource? value)
            => obj.SetValue(OccupiedIconProperty, value);
    }
}
