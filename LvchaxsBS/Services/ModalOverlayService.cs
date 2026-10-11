using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 全局模态遮罩服务。
    ///
    /// <para>
    /// 窗口根节点上有一层 <c>ModalLayer</c>（<c>MainWindow.xaml</c>）：
    /// 里面先铺一层半透明遮罩 <c>ModalMask</c>，再叠一个 <c>ModalHost</c>。
    /// 遮罩盖住<b>整个窗口</b>（含顶部栏），模态内容放进 ModalHost 就自然浮在遮罩之上。
    /// </para>
    /// <para>
    /// 两类使用者：
    /// <list type="bullet">
    /// <item>宿主自己的弹窗（版本改动 / 重置配置 / 许可证 / 截图工具等）——
    /// 用 <see cref="ShowModalDialog"/> 包一层即可；</item>
    /// <item>UID 插件模块 —— 它没有宿主的编译期引用，宿主通过
    /// <c>UidPlugin.SetHostOverlay(Action&lt;string, object?&gt;)</c> 把
    /// <see cref="HandleModuleCommand"/> 注入给它，模块用
    /// <c>mask:show/hide</c>、<c>modal:attach/detach</c> 四个命令驱动这里。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 遮罩显隐用「占用者集合」而不是简单计数：<b>某个来源重复 show 不会把引用数堆高，
    /// hide 一次就能干净地关掉</b>，避免出现遮罩关不掉的残留问题。
    /// </para>
    /// </summary>
    public static class ModalOverlayService
    {
        /// <summary>遮罩最大不透明度。</summary>
        private const double MaskOpacity = 0.45;

        /// <summary>遮罩淡入 / 淡出时长（ms）。</summary>
        private const int FadeMs = 140;

        private const string OwnerHostDialog = "host-dialog";
        private const string OwnerModuleMask = "uid-mask";
        private const string OwnerModuleModal = "uid-modal";

        private static Grid? _layer;
        private static Border? _mask;
        private static Panel? _host;

        private static readonly HashSet<string> Owners = new();
        private static bool _shown;

        /// <summary>由 MainWindow 在初始化时把自己的模态层注册进来。</summary>
        public static void Attach(Grid layer, Border mask, Panel host)
        {
            _layer = layer;
            _mask = mask;
            _host = host;
            Owners.Clear();
            _shown = false;
            Apply();
        }

        // ============ 宿主自己的弹窗 ============

        /// <summary>
        /// 带遮罩地以模态方式显示一个对话框，返回 <see cref="Window.ShowDialog"/> 的结果。
        /// 直接替换原来的 <c>dlg.ShowDialog()</c> 即可。
        /// </summary>
        public static bool? ShowModalDialog(Window dialog)
        {
            AddOwner(OwnerHostDialog);
            try
            {
                return dialog.ShowDialog();
            }
            finally
            {
                RemoveOwner(OwnerHostDialog);
            }
        }

        // ============ UID 模块的命令入口 ============

        /// <summary>
        /// 供外部模块（UID）调用。命令：
        /// <c>mask:show</c> / <c>mask:hide</c>（只控制遮罩）、
        /// <c>modal:attach</c> / <c>modal:detach</c>（把模块的模态框搬进宿主模态层）。
        /// </summary>
        public static void HandleModuleCommand(string command, object? arg)
        {
            switch (command)
            {
                case "mask:show":
                    AddOwner(OwnerModuleMask);
                    break;

                case "mask:hide":
                    RemoveOwner(OwnerModuleMask);
                    break;

                case "modal:attach":
                    AttachModuleModal(arg as UIElement);
                    break;

                case "modal:detach":
                    DetachModuleModal(arg as UIElement);
                    break;
            }
        }

        private static void AttachModuleModal(UIElement? modal)
        {
            if (modal == null || _host == null) return;

            if (!_host.Children.Contains(modal))
                _host.Children.Add(modal);

            AddOwner(OwnerModuleModal);
        }

        private static void DetachModuleModal(UIElement? modal)
        {
            if (modal == null || _host == null) return;

            if (_host.Children.Contains(modal))
                _host.Children.Remove(modal);

            RemoveOwner(OwnerModuleModal);
        }

        // ============ 内部 ============

        private static void AddOwner(string owner) { Owners.Add(owner); Apply(); }

        private static void RemoveOwner(string owner) { Owners.Remove(owner); Apply(); }

        private static void Apply()
        {
            if (_layer == null || _mask == null) return;

            bool shouldShow = Owners.Count > 0;
            if (shouldShow == _shown) return;
            _shown = shouldShow;

            if (shouldShow)
            {
                _layer.Visibility = Visibility.Visible;
                _mask.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                {
                    From = _mask.Opacity,
                    To = MaskOpacity,
                    Duration = TimeSpan.FromMilliseconds(FadeMs),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            }
            else
            {
                var anim = new DoubleAnimation
                {
                    From = _mask.Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(FadeMs),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                anim.Completed += (s, e) =>
                {
                    // 期间又被要求显示就丢弃这次回调
                    if (Owners.Count > 0) return;

                    _mask.BeginAnimation(UIElement.OpacityProperty, null);
                    _mask.Opacity = 0;
                    if (_layer != null) _layer.Visibility = Visibility.Collapsed;
                };

                _mask.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }
    }
}
