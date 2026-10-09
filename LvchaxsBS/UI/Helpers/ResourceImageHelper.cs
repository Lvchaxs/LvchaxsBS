using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 从 pack:// 资源路径加载 BitmapImage（同一个 URI 只解码一次，多处共用同一份像素）。
    /// </summary>
    public static class ResourceImageHelper
    {
        /// <summary>
        /// 已解码图片缓存。
        ///
        /// <para>
        /// 为什么需要它：<see cref="BitmapCacheOption.OnLoad"/> <b>不参与</b> WPF 内部那套
        /// 「按 URI 共享解码结果」的缓存，所以每次调用都会各自解一份像素。
        /// 探针实测（200 个 Image 引用同一张 131×112 的图，GC 后量保留增量）：
        /// </para>
        /// <list type="bullet">
        /// <item>每次 <c>Load</c> 建一个新实例（OnLoad）：<b>+15.0MB</b>（≈75KB/次，与次数成正比）</item>
        /// <item>共享同一个 Frozen 实例：<b>+0.04MB</b>（≈0）</item>
        /// <item>XAML 里直接写 <c>Source="pack://..."</c>（默认 OnDemand）：<b>+0.9MB</b>，
        ///       像素是共享的，只有每实例的一点包装开销</item>
        /// </list>
        ///
        /// <para>
        /// 结论：同一个 URI 重复取用时，共享实例几乎不花内存，各建一份则线性增长。
        /// 现在无论调用多少次，同一个 URI 都返回同一个实例 ——
        /// <see cref="BitmapImage.Freeze"/> 过的对象不可变、可安全跨多个 Image 共享。
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, BitmapImage> Cache = new();

        /// <summary>
        /// 缓存上限。正常只有模块图标 + 角色头像等十来个 pack URI；
        /// 留足余量的同时，防止有人拿动态路径反复调导致无限增长。
        /// </summary>
        private const int CacheLimit = 32;

        /// <summary>
        /// 加载图片资源。路径形如：
        /// pack://application:,,,/Resources/Images/图标/xxx.png
        /// 加载失败返回 null。
        /// </summary>
        public static BitmapImage? Load(string packUri)
        {
            if (string.IsNullOrEmpty(packUri)) return null;

            if (Cache.TryGetValue(packUri, out var cached))
                return cached;

            try
            {
                var uri = new Uri(packUri, UriKind.RelativeOrAbsolute);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                if (Cache.Count >= CacheLimit)
                    Cache.Clear();

                Cache[packUri] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
