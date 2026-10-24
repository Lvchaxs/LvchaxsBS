using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LvchaxsBS_UID.Converters
{
    /// <summary>
    /// 列表容器间距：「**最后一项**的下边距为 0，其余为 <see cref="Spacing"/>（默认 8）」。
    /// <para>用途：滚到底时最后一张卡片的底边正好贴视口底，不再多出一截空白。</para>
    /// ★★ 为什么用绑定而不是代码刷（2026-10-21 的血泪教训）：
    ///   代码刷永远比「容器生成」晚一步 —— 行容器要等布局才生成，Collapsed→Visible 的首进、
    ///   滚动中新实现的行（虚拟化）都会漏；用户实测现象「账号列表首次打开滚到底有空白，
    ///   非得打开一次分组再返回才正常」就是补刷总赶不上容器生成。
    ///   绑在容器/行自己身上 ⇒ **生成即正确**，回收复用、搜索过滤、整体换集合都自动重算。</para>
    /// </summary>
    public class LastItemSpacingConverter : IMultiValueConverter
    {
        /// <summary>非最后一项的下边距（DIP）。可用 ConverterParameter 覆盖。</summary>
        public double Spacing { get; set; } = 8;

        /// <summary>
        /// values[0] = 数据项；values[1] = 列表的 <c>Items</c>（内部视图，内容变化会通知）；
        /// values[2] = <c>ItemsSource</c>（整体换集合时通知，兜底）。
        /// </summary>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var item = values.Length > 0 ? values[0] : null;
            var source = (values.Length > 1 ? values[1] as System.Collections.IList : null)
                      ?? (values.Length > 2 ? values[2] as System.Collections.IList : null);

            double spacing = Spacing;
            if (parameter is string s &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                spacing = p;

            if (item == null || source == null || source.Count == 0)
                return new Thickness(0, 0, 0, spacing);

            bool isLast = ReferenceEquals(item, source[source.Count - 1]);
            return new Thickness(0, 0, 0, isLast ? 0 : spacing);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
