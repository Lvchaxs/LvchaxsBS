using System;
using System.Globalization;
using System.Windows.Data;
using LvchaxsBS_Macro.Utils;

namespace LvchaxsBS_Macro.Converters
{
    /// <summary>
    /// 把 <c>MacroItem.Avatar</c>（头像名，例如「空」「荧」）转成头像图片。
    /// <para>取不到时返回 null —— Image 显示空白，不会抛异常（资源包缺失也不影响列表）。</para>
    /// </summary>
    public class AvatarImageConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => AvatarCatalog.Load(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
