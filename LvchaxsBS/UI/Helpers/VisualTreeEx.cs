using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 可视树查找的公共实现。
    ///
    /// <para>
    /// 这两个方法是"到处都要用"的小工具，之前首页、两个触发键页面、滑块入场动画里
    /// 各抄了一份（内容完全一样，只是参数名不同）。统一到这里后：
    /// 行为完全不变（深度优先、跳过非目标类型的中间节点），但只需要维护一份。
    /// </para>
    ///
    /// <para>
    /// ★ 只用于**已经生成容器**的可视树（例如 Loaded 之后）。
    ///   数据驱动的列表在容器还没生成时查不到东西，那种场景要用
    ///   <see cref="System.Windows.Controls.ItemContainerGenerator"/>。
    /// </para>
    /// </summary>
    internal static class VisualTreeEx
    {
        /// <summary>深度优先遍历所有后代，返回类型为 <typeparamref name="T"/> 的节点。</summary>
        public static IEnumerable<T> FindChildren<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) yield break;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;

                foreach (var grand in FindChildren<T>(child))
                    yield return grand;
            }
        }

        /// <summary>沿父链向上找第一个类型为 <typeparamref name="T"/> 的祖先（找不到返回 null）。</summary>
        public static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
