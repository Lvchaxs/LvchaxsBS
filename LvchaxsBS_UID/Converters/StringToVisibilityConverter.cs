using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LvchaxsBS_UID.Converters
{
    public class StringToVisibilityConverter : IValueConverter
    {
        public Visibility EmptyValue { get; set; } = Visibility.Collapsed;
        public Visibility NotEmptyValue { get; set; } = Visibility.Visible;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return string.IsNullOrWhiteSpace(str) ? EmptyValue : NotEmptyValue;
            }

            return EmptyValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}