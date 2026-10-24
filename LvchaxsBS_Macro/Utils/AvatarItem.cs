namespace LvchaxsBS_Macro.Utils
{
    /// <summary>
    /// 头像网格里的一项（照 UID 的 <c>AvatarItem</c>）。
    /// <para>
    /// <see cref="IsOther"/> 为 true 表示它来自「其他头像」（NPC / 任务类），
    /// 这些名字不在角色元素表里，筛选「其他」时才显示。
    /// </para>
    /// </summary>
    public sealed class AvatarItem
    {
        /// <summary>头像名（= 资源包里的文件名，不含扩展名）。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>是否属于「其他头像」目录。</summary>
        public bool IsOther { get; init; }

        /// <summary>资源包里的相对路径（延迟解码时才用得上）。</summary>
        public string Path { get; init; } = string.Empty;

        private System.Windows.Media.Imaging.BitmapImage? _image;
        private bool _imageLoaded;

        /// <summary>
        /// 头像图片 —— **第一次被取用才解码**。
        /// <para>
        /// ★ 不能在建清单时就把 152 张全解码完：宏列表只用得到其中几张，
        ///   那样每次打开列表都要白等 ~160ms（用户反馈"点自定义宏会卡一下"）。
        /// </para>
        /// <para>★ 资源包缺失时为 null（界面显示空白，不抛异常）。</para>
        /// </summary>
        public System.Windows.Media.Imaging.BitmapImage? Image
        {
            get
            {
                if (!_imageLoaded)
                {
                    _imageLoaded = true;
                    _image = AvatarCatalog.Decode(Path, AvatarCatalog.AvatarThumbWidth);
                }

                return _image;
            }
        }
    }
}
