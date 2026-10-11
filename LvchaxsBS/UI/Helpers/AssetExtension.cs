using System;
using System.Windows.Markup;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 在 XAML 里直接引用共用资源包（<c>LvchaxsBS_Resources.dll</c>）中的图片：
    ///
    /// <code>
    /// &lt;Image Source="{ui:Asset Images/Pictures/角色头像/空.png}" /&gt;
    /// </code>
    ///
    /// <para>
    /// 路径相对资源包的 <c>Resources/</c> 目录，前导斜杠可省。
    /// 资源包缺失（或图不存在）时返回 <c>null</c> —— 控件显示空白，
    /// <b>不会在 XAML 解析期抛异常</b>。
    /// </para>
    ///
    /// <para>
    /// 对比：直接写 <c>Source="pack://application:,,,/LvchaxsBS_Resources;component/…"</c>
    /// 这种静态 URI，在资源包缺失时会于解析期抛 <c>IOException</c>，
    /// 导致整个页面/窗口加载失败 —— 所以引资源包的图一律走这个标记扩展。
    /// </para>
    /// </summary>
    public class AssetExtension : MarkupExtension
    {
        /// <summary>相对资源包 <c>Resources/</c> 的路径，例如 <c>Images/Pictures/角色头像/空.png</c>。</summary>
        public string Path { get; set; } = string.Empty;

        public AssetExtension()
        {
        }

        public AssetExtension(string path)
        {
            Path = path;
        }

        public override object? ProvideValue(IServiceProvider serviceProvider)
            => Core.AssetLocator.Load(Path);
    }
}
