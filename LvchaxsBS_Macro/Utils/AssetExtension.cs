using System;
using System.Windows.Markup;

namespace LvchaxsBS_Macro.Utils
{
    /// <summary>
    /// 在 XAML 里引用共用资源包（<c>LvchaxsBS_Resources.dll</c>）中的图片：
    ///
    /// <code>
    /// &lt;Image Source="{ui:Asset Images/元素/火.png}" /&gt;
    /// </code>
    ///
    /// <para>
    /// 路径相对资源包的 <c>Resources/</c> 目录，前导斜杠可省。
    /// 资源包缺失（或图不存在）时返回 <c>null</c> —— 控件显示空白，
    /// <b>不会在 XAML 解析期抛异常</b>，整个窗口照常打开。
    /// </para>
    /// </summary>
    public class AssetExtension : MarkupExtension
    {
        /// <summary>相对资源包 <c>Resources/</c> 的路径，例如 <c>Images/元素/火.png</c>。</summary>
        public string Path { get; set; } = string.Empty;

        public AssetExtension()
        {
        }

        public AssetExtension(string path)
        {
            Path = path;
        }

        public override object? ProvideValue(IServiceProvider serviceProvider)
            => AvatarCatalog.Decode(Path);
    }
}
