using System;
using System.Globalization;
using System.Windows.Data;

namespace LvchaxsBS.Converters
{
    /// <summary>
    /// 将百分比值转换为透明度值
    /// 100% → 0（完全透明），0% → 1（完全不透明）
    /// </summary>
    public class PercentageToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percentage)
            {
                // 限制范围 0-100
                percentage = Math.Max(0, Math.Min(100, percentage));
                // 100%透明 → 0，0%透明 → 1
                return 1.0 - (percentage / 100.0);
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double opacity)
            {
                // 反向转换：透明度 → 百分比
                return (1.0 - opacity) * 100.0;
            }
            return 0.0;
        }
    }
}