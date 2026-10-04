using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LvchaxsBS.Helpers
{
    public static class ScrollViewerHelper
    {
        // ========== 附加属性：启用平滑滚动 ==========
        public static readonly DependencyProperty EnableSmoothScrollingProperty =
            DependencyProperty.RegisterAttached("EnableSmoothScrolling", typeof(bool), typeof(ScrollViewerHelper),
                new PropertyMetadata(false, OnEnableSmoothScrollingChanged));

        public static bool GetEnableSmoothScrolling(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableSmoothScrollingProperty);
        }

        public static void SetEnableSmoothScrolling(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableSmoothScrollingProperty, value);
        }

        // ========== 配置参数 ==========
        public static double ScrollStep { get; set; } = 60;
        public static double DecelerationFactor { get; set; } = 0.33;  // 每次移动剩余距离的比例

        // ========== 存储每个 ScrollViewer 的滚动状态 ==========
        private static readonly Dictionary<ScrollViewer, ScrollState> _states = new();

        private static void OnEnableSmoothScrollingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer scrollViewer) return;

            if ((bool)e.NewValue)
            {
                scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
                scrollViewer.Unloaded += OnScrollViewerUnloaded;
            }
            else
            {
                scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
                scrollViewer.Unloaded -= OnScrollViewerUnloaded;

                if (_states.TryGetValue(scrollViewer, out var state))
                {
                    state.Stop();
                    _states.Remove(scrollViewer);
                }
            }
        }

        private static void OnScrollViewerUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
                scrollViewer.Unloaded -= OnScrollViewerUnloaded;

                if (_states.TryGetValue(scrollViewer, out var state))
                {
                    state.Stop();
                    _states.Remove(scrollViewer);
                }
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            // 检查鼠标是否在 Slider 上
            var hitResult = VisualTreeHelper.HitTest(scrollViewer, e.GetPosition(scrollViewer));
            if (hitResult != null)
            {
                var element = hitResult.VisualHit as DependencyObject;
                while (element != null)
                {
                    if (element is Slider)
                    {
                        return;
                    }
                    element = VisualTreeHelper.GetParent(element);
                }
            }

            e.Handled = true;

            double delta = e.Delta > 0 ? -ScrollStep : ScrollStep;
            double targetOffset = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + delta));

            // 获取或创建滚动状态
            if (!_states.TryGetValue(scrollViewer, out var state))
            {
                state = new ScrollState(scrollViewer);
                _states[scrollViewer] = state;
            }

            // 更新目标位置（不中断动画）
            state.UpdateTarget(targetOffset);
        }

        /// <summary>
        /// 滚动状态 - 使用指数衰减平滑滚动
        /// </summary>
        private class ScrollState
        {
            private readonly ScrollViewer _scrollViewer;
            private double _targetOffset;
            private bool _isRunning;

            public ScrollState(ScrollViewer scrollViewer)
            {
                _scrollViewer = scrollViewer;
            }

            public void UpdateTarget(double targetOffset)
            {
                _targetOffset = targetOffset;

                if (!_isRunning)
                {
                    _isRunning = true;
                    CompositionTarget.Rendering += OnRendering;
                }
            }

            private void OnRendering(object? sender, EventArgs e)
            {
                if (_scrollViewer == null)
                {
                    Stop();
                    return;
                }

                double currentOffset = _scrollViewer.VerticalOffset;
                double diff = _targetOffset - currentOffset;

                // 距离足够近时直接跳到目标
                if (Math.Abs(diff) < 0.5)
                {
                    _scrollViewer.ScrollToVerticalOffset(_targetOffset);
                    Stop();
                    return;
                }

                // ⭐ 指数衰减：每次移动剩余距离的 33%
                double newOffset = currentOffset + diff * ScrollViewerHelper.DecelerationFactor;
                _scrollViewer.ScrollToVerticalOffset(newOffset);
            }

            public void Stop()
            {
                _isRunning = false;
                CompositionTarget.Rendering -= OnRendering;
            }
        }
    }
}