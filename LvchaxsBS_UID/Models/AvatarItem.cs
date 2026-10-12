using System.Windows.Media.Imaging;

namespace LvchaxsBS_UID.Models
{
    /// <summary>
    /// 头像选择列表里的一项。
    ///
    /// <para>
    /// <see cref="Name"/> 就是资源包 <c>Resources/Images/Pictures/</c> 下的文件名
    /// （不含扩展名），保存账号时存进 <see cref="Account.Avatar"/>。
    /// 角色头像在 <c>角色头像/</c> 子目录，NPC / 任务类头像在 <c>其他头像/</c> 子目录，
    /// 后者没有元素归属，靠 <see cref="IsOther"/> 区分。
    /// </para>
    /// </summary>
    public class AvatarItem
    {
        /// <summary>头像名（= 资源包里的图片文件名，不含扩展名）。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>true = 来自「其他头像」目录（NPC / 任务类），没有元素归属。</summary>
        public bool IsOther { get; init; }

        /// <summary>已解码的头像；资源包缺失或图片损坏时为 null。</summary>
        public BitmapImage? Image { get; init; }
    }
}
