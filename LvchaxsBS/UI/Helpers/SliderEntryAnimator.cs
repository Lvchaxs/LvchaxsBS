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
        /// <summary>
        /// 同一类型页面在这么短时间内被重复打开时（快速连点导航按钮），
        /// 不再重播入场动画，直接显示最终值。避免动画堆叠、闪烁、卡顿。
        /// 设为 0 可关闭该节流（每次打开都播放）。
        /// </summary>
        private const int RepeatCooldownMs = 400;

        /// <summary>各页面类型上一次播放入场动画的时间戳（TickCount）</summary>
        private static readonly Dictionary<string, int> LastPlayTick = new();

        /// <summary>播放本页所有 SpinSliderControl 的入场动画（延后一拍，等布局完成）</summary>
        public static void PlayAll(DependencyObject root)
        {
            if (root is not FrameworkElement fe) return;
            if (fe.Dispatcher == null) return;

            string key = root.GetType().FullName ?? root.GetType().Name;

            fe.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 延后这一拍里页面可能已经被切走了，直接放弃，别再动画
                if (!fe.IsLoaded) return;

                // 快速重复进入同一页面：跳过动画，避免"又从最左侧滑一次"
                int now = Environment.TickCount;
                if (RepeatCooldownMs > 0
                    && LastPlayTick.TryGetValue(key, out int last)
                    && unchecked(now - last) < RepeatCooldownMs)
                    return;

                LastPlayTick[key] = now;

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
