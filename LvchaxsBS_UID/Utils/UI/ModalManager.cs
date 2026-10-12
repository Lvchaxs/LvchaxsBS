using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LvchaxsBS_UID.Utils.UI
{
    /// <summary>
    /// 页内模态框 + 遮罩的管理器。
    ///
    /// <para>
    /// 本视图自带一个 <c>modalOverlay</c> 遮罩，但它只盖得住内容区，
    /// 顶部栏亮着会很难看。所以现在优先走<b>宿主的全局遮罩</b>：
    /// 宿主通过 <see cref="HostOverlay"/> 注入一个命令入口，
    /// 由宿主在窗口根节点上铺遮罩（盖住整个窗口），并把模态框搬进宿主的模态层，
    /// 让它浮在遮罩之上。
    /// </para>
    /// <para>
    /// 宿主没注入时（例如脱离宿主单独调试）自动退回本视图内的遮罩，行为与以前一致。
    /// </para>
    /// </summary>
    public class ModalManager
    {
        /// <summary>
        /// 宿主注入的全局遮罩入口（命令, 参数）。命令含义：
        /// <c>mask:show</c> / <c>mask:hide</c> / <c>modal:attach</c> / <c>modal:detach</c>。
        /// </summary>
        public static Action<string, object?>? HostOverlay { get; set; }

        private readonly Border _modalOverlay;
        private readonly Dispatcher _dispatcher;

        public event Action<Border> ModalShown;
        public event Action<Border> ModalHidden;

        public ModalManager(Border modalOverlay)
        {
            _modalOverlay = modalOverlay ?? throw new ArgumentNullException(nameof(modalOverlay));
            _dispatcher = Application.Current.Dispatcher;
        }

        public void ShowModal(Border modal)
        {
            if (modal == null)
                throw new ArgumentNullException(nameof(modal));

            _dispatcher.Invoke(() =>
            {
                if (HostOverlay != null)
                {
                    // 交给宿主的模态层承载。
                    // 先脱离本视图的父级（第一次调用时它还挂在视图的根 Grid 上），
                    // 再让宿主把它加进「遮罩之上」的那一层。
                    if (modal.Parent is Panel localParent)
                        localParent.Children.Remove(modal);

                    HostOverlay("modal:attach", modal);
                }
                else
                {
                    // 没有宿主：退回本视图内的遮罩
                    _modalOverlay.Visibility = Visibility.Visible;
                }

                // 显示模态框
                modal.Visibility = Visibility.Visible;
                modal.InvalidateVisual();
                modal.UpdateLayout();

                // 执行显示动画
                var storyboard = modal.Resources["FadeInAnimation"] as Storyboard;
                if (storyboard != null)
                {
                    storyboard.Stop();
                    Storyboard.SetTarget(storyboard, modal);
                    storyboard.Begin();
                }
                else
                {
                    // 如果没有动画资源，直接设置可见
                    modal.Opacity = 1;
                    var transform = modal.RenderTransform as ScaleTransform;
                    if (transform != null)
                    {
                        transform.ScaleX = 1;
                        transform.ScaleY = 1;
                    }
                }

                // 触发事件
                ModalShown?.Invoke(modal);
            });
        }

        public async Task HideModalAsync(Border modal)
        {
            if (modal == null)
                throw new ArgumentNullException(nameof(modal));

            await _dispatcher.Invoke(async () =>
            {
                // 执行隐藏动画
                var storyboard = modal.Resources["FadeOutAnimation"] as Storyboard;
                if (storyboard != null)
                {
                    storyboard.Stop();
                    Storyboard.SetTarget(storyboard, modal);
                    storyboard.Begin();
                    await Task.Delay(150); // 等待动画完成
                }

                // 隐藏模态框
                modal.Visibility = Visibility.Collapsed;

                // 重置状态
                modal.Opacity = 0;
                var transform = modal.RenderTransform as ScaleTransform;
                if (transform != null)
                {
                    transform.ScaleX = 0.95;
                    transform.ScaleY = 0.95;
                }

                // 收遮罩：交给宿主的要还回去，否则关掉本视图自己的
                if (HostOverlay != null)
                    HostOverlay("modal:detach", modal);
                else
                    _modalOverlay.Visibility = Visibility.Collapsed;

                // 触发事件
                ModalHidden?.Invoke(modal);
            });
        }

        public void HideModal(Border modal)
        {
            _ = HideModalAsync(modal);
        }

        public void ShowOverlay()
        {
            _dispatcher.Invoke(() =>
            {
                if (HostOverlay != null)
                {
                    HostOverlay("mask:show", null);
                    return;
                }

                _modalOverlay.Visibility = Visibility.Visible;
            });
        }

        public void HideOverlay()
        {
            _dispatcher.Invoke(() =>
            {
                if (HostOverlay != null)
                {
                    HostOverlay("mask:hide", null);
                    return;
                }

                _modalOverlay.Visibility = Visibility.Collapsed;
            });
        }

        public bool IsModalVisible(Border modal)
        {
            return modal?.Visibility == Visibility.Visible;
        }

        public bool IsOverlayVisible =>
            HostOverlay != null || _modalOverlay.Visibility == Visibility.Visible;

        public void ResetModalState(Border modal)
        {
            if (modal == null) return;

            _dispatcher.Invoke(() =>
            {
                modal.Opacity = 0;
                var transform = modal.RenderTransform as ScaleTransform;
                if (transform != null)
                {
                    transform.ScaleX = 0.95;
                    transform.ScaleY = 0.95;
                }
                modal.Visibility = Visibility.Collapsed;
            });
        }
    }
}
