using System;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 资源包访问的注入点。
    ///
    /// <para>
    /// 本工程是共享库，而"资源包在不在、怎么拼 pack URI"这套判断留在宿主/插件的
    /// <c>AssetLocator</c> 里（那份实现要能在资源 dll 缺失时安全降级成空白图片，
    /// 所以它不能依赖本工程）。这里只保留一套**规则相同**的默认实现，
    /// 宿主启动时把 <see cref="IsAvailable"/>（以及可选的 <see cref="PackProvider"/>）注入进来。
    /// </para>
    ///
    /// <para>
    /// 默认实现与宿主 <c>LvchaxsBS.Core.AssetLocator.Pack</c> 的规则完全一致：
    /// <c>pack://application:,,,/LvchaxsBS_Resources;component/Resources/ + 相对路径</c>。
    /// 宿主没注入时也能拼出正确的 URI，注入只是为了拿到"资源包是否存在"这个事实。
    /// </para>
    /// </summary>
    public static class ResourcePaths
    {
        /// <summary>资源程序集名（不含 .dll）。</summary>
        public const string ResourceAssemblyName = "LvchaxsBS_Resources";

        /// <summary>资源程序集内部的根目录。</summary>
        private const string ResourceRoot = "Resources/";

        /// <summary>资源包是否可用（宿主 / 插件启动时注入）。</summary>
        public static bool IsAvailable { get; set; }

        /// <summary>
        /// 可选的 Pack 实现（通常直接指到宿主的 <c>AssetLocator.Pack</c>）。
        /// 为 null 时用本类内置的同规则实现。
        /// </summary>
        public static Func<string, string>? PackProvider { get; set; }

        /// <summary>
        /// 可选的图片加载实现（通常指到宿主的 <c>AssetLocator.Load</c>，带统一缓存）。
        /// </summary>
        public static Func<string, BitmapImage?>? LoadProvider { get; set; }

        /// <summary>
        /// 把「相对 Resources/ 的路径」拼成完整 pack URI。资源包不可用时返回空串。
        /// </summary>
        public static string Pack(string relativePath)
        {
            if (!IsAvailable || string.IsNullOrEmpty(relativePath))
                return string.Empty;

            if (PackProvider != null)
                return PackProvider(relativePath);

            return $"pack://application:,,,/{ResourceAssemblyName};component/{ResourceRoot}"
                 + relativePath.Replace('\\', '/').TrimStart('/');
        }

        /// <summary>
        /// 直接加载成 <see cref="BitmapImage"/>；宿主没注入加载器或图片不存在时返回 null。
        /// </summary>
        public static BitmapImage? Load(string relativePath)
        {
            if (!IsAvailable || string.IsNullOrEmpty(relativePath))
                return null;

            return LoadProvider?.Invoke(relativePath);
        }
    }
}
