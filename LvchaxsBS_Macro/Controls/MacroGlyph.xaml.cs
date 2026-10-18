using System.Windows.Controls;

namespace LvchaxsBS_Macro.Controls
{
    /// <summary>
    /// 矢量 `&lt;/&gt;` 图标（三条 Path 画的，**不用 png**）。
    /// <para>
    /// ★ 用在三处：「添加宏」按钮、每张宏卡片左侧、新建/编辑宏窗口的标题。
    /// 尺寸由外部 <c>Width</c>/<c>Height</c> 给（内部是 Viewbox，等比缩放）。
    /// ★ 颜色固定主题主色，**不随置顶变色**（UID 分组卡片的图标也没有这种反馈）。
    /// </para>
    /// </summary>
    public partial class MacroGlyph : UserControl
    {
        public MacroGlyph()
        {
            InitializeComponent();
        }
    }
}
