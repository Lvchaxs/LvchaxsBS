using System;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 从 pack:// 资源路径加载 BitmapImage。
    /// </summary>
    public static class ResourceImageHelper
    {
        /// <summary>
        /// 加载图片资源。路径形如：
        /// pack://application:,,,/Resources/Images/图标/xxx.png
        /// 加载失败返回 null。
        /// </summary>
        public static BitmapImage? Load(string packUri)
        {
            if (string.IsNullOrEmpty(packUri)) return null;

            try
            {
                var uri = new Uri(packUri, UriKind.RelativeOrAbsolute);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}