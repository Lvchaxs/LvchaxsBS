using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LvchaxsBS.UI.Controls;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 页面打开时，让本页所有 SpinSliderControl 播放一次入场动画。
    /// 用法：在页面 Loaded 末尾调用 SliderEntryAnimator.PlayAll(this);
    /// </summary>
    public static class SliderEntryAnimator
    {
        /// <summary>播放本页所有 SpinSliderControl 的入场动画（延后一拍，等布局完成）</summary>
        public static void PlayAll(DependencyObject root)
        {
            if (root is not FrameworkElement fe) return;
            if (fe.Dispatcher == null) return;

            fe.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var slider in FindVisualChildren<SpinSliderControl>(root))
                {
                    slider.PlayEntryAnimation();
                }
            }), DispatcherPriority.Loaded);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null) yield break;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t)
                    yield return t;

                foreach (var grand in FindVisualChildren<T>(child))
                    yield return grand;
            }
        }
    }
}