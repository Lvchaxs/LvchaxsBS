using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 胶囊开关（<c>ModuleToggleSwitchStyle</c>）的小工具。
    ///
    /// <para>
    /// ★★ 为什么需要它：那个样式的模板里写着
    /// <c>Trigger Property="IsChecked" Value="True"</c> → <c>EnterActions</c> 播一段
    /// **0.4 秒的滑块位移动画**。而当列表用 <c>ObservableCollection.Move</c> 排序时，
    /// WPF 会把那一行的容器移除再插入（容器被重建）⇒ IsChecked 重新求值 ⇒ **动画重播一次**。
    /// 用户看到的现象就是「排一下序，胶囊开关自己又滑了一遍，像重新加载」。
    /// </para>
    ///
    /// <para>
    /// ★ 这里不清除样式、也不改模板 —— 只是把当前这次**历史回放**压掉：
    /// 清掉滑块上的动画时钟、直接把 X 设成它该在的终点。用户之后自己拨开关时，
    /// Trigger 会重新启动 Storyboard，动画照常。
    /// </para>
    /// </summary>
    public static class ToggleSwitchHelper
    {
        /// <summary>模板里「选中」状态滑块的终点 X（见 CardStyles.xaml 的 0.4s 关键帧，末帧 Value=20）。</summary>
        private const double OnThumbX = 20;

        /// <summary>滑块所在 Border 的名字（CardStyles.xaml 的 <c>x:Name="ThumbBorder"</c>）。</summary>
        private const string ThumbBorderName = "ThumbBorder";

        /// <summary>把整棵可视树里所有胶囊开关的滑块直接摆到终点（不播动画）。</summary>
        public static void SnapThumbnails(DependencyObject? root)
        {
            if (root == null) return;

            if (root is CheckBox cb) Snap(cb);

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                SnapThumbnails(VisualTreeHelper.GetChild(root, i));
        }

        /// <summary>把单个胶囊开关的滑块直接摆到终点。</summary>
        public static void Snap(CheckBox? checkBox)
        {
            if (checkBox == null) return;

            try
            {
                TranslateTransform? thumb = FindThumb(checkBox);
                if (thumb == null) return;

                // 清掉 Storyboard 挂在属性上的动画时钟（否则动画会继续把它推着走）
                thumb.BeginAnimation(TranslateTransform.XProperty, null);
                thumb.X = checkBox.IsChecked == true ? OnThumbX : 0;
            }
            catch
            {
                // 模板结构变了 → 什么都不做，最多是多播一次动画，不影响功能
            }
        }

        /// <summary>
        /// 在模板可视树里找滑块用的 <see cref="TranslateTransform"/>。
        ///
        /// <para>
        /// ★★ <b>不能用</b> <c>Template.FindName("ThumbTranslate", cb)</c>：那个名字挂在一个
        ///   <see cref="TranslateTransform"/>（Freezable）上，<c>FindName</c> 只认 FrameworkElement，
        ///   所以永远返回 null —— 上一版压制"没生效"就是这个原因（异常被吞了，看不出报错）。
        /// </para>
        /// <para>
        /// ★ 这里改为按模板结构找：<c>ThumbBorder</c> 的 <c>RenderTransform</c> 是一个
        ///   <see cref="TransformGroup"/>，里面第二项就是滑块位移。优先按 Border 名字匹配，
        ///   名字对不上时退回「任意带 TransformGroup 的元素」。
        /// </para>
        /// </summary>
        private static TranslateTransform? FindThumb(DependencyObject root)
        {
            TranslateTransform? fallback = null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is FrameworkElement fe && fe.RenderTransform is TransformGroup group)
                {
                    TranslateTransform? tt = null;
                    foreach (object? item in group.Children)
                    {
                        if (item is TranslateTransform found) { tt = found; break; }
                    }

                    if (tt != null)
                    {
                        if (fe.Name == ThumbBorderName) return tt;   // 精确命中
                        fallback ??= tt;                              // 结构变了时的兜底
                    }
                }

                TranslateTransform? deep = FindThumb(child);
                if (deep != null) return deep;
            }

            return fallback;
        }
    }
}
