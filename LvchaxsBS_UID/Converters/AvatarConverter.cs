using LvchaxsBS_UID.Utils.UI;
using System;
using System.Globalization;
using System.Windows.Data;

namespace LvchaxsBS_UID.Converters
{
    /// <summary>
    /// 把账号的 <c>Avatar</c>（头像名）转成头像图片。
    ///
    /// <para>
    /// 图片来自共用资源包 <c>LvchaxsBS_Resources.dll</c> 的
    /// <c>Resources/Images/Pictures/</c>：先在 <c>角色头像/</c> 里找，
    /// 找不到再到 <c>其他头像/</c>（NPC / 任务类）里找。
    /// 名字为空或两处都取不到时回落到默认头像「空」；
    /// 资源包整个缺失时返回 <c>null</c> —— 界面留白，不抛异常。
    /// </para>
    /// </summary>
    public class AvatarConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string name = value as string ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return AssetLocator.DefaultAvatar;

            // 卡片上只显示 48px，按 96 解码足够清晰，也省掉不必要的像素
            var image = AssetLocator.Load($"Images/Pictures/角色头像/{name}.png", decodePixelWidth: 96)
                        ?? AssetLocator.Load($"Images/Pictures/其他头像/{name}.png", decodePixelWidth: 96);
            return image ?? AssetLocator.DefaultAvatar;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
