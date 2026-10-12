using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace LvchaxsBS_UID.Utils.UI
{
    /// <summary>
    /// 共用图片资源包（<c>LvchaxsBS_Resources.dll</c>）的定位入口。
    ///
    /// <para>
    /// 这个资源包由独立工程 <c>LvchaxsBS_Resources</c> 编译产生，主程序与 UID 模块共用，
    /// 但<b>都是可选依赖</b>：dll 不在 exe 同目录时两边都照常运行，
    /// 只是图片取不到（Image 显示空白），不抛异常、不崩溃。
    /// </para>
    ///
    /// <para>
    /// 用法（相对路径以 <c>Resources/</c> 为根）：
    /// <code>
    /// using LvchaxsBS_UID.Utils.UI;
    ///
    /// image.Source = AssetLocator.Load("Template/角色头像/150x150/荧.png");   // 失败为 null
    /// string uri   = AssetLocator.Pack("Images/图标/钓鱼1.png");               // 失败为空串
    /// </code>
    /// 与宿主 <c>LvchaxsBS.Core.AssetLocator</c> 是同一套逻辑（模块与宿主无编译期引用，
    /// 所以各自持有一份实现，行为保持一致）。
    /// </para>
    /// </summary>
    public static class AssetLocator
    {
        /// <summary>资源程序集名（不含 .dll）。</summary>
        public const string ResourceAssemblyName = "LvchaxsBS_Resources";

        private const string ResourceRoot = "Resources/";

        /// <summary>资源包是否可用（进程内探测一次）。</summary>
        public static bool IsAvailable { get; } = Detect();

        /// <summary>
        /// pack URI 前缀，形如
        /// <c>pack://application:,,,/LvchaxsBS_Resources;component/Resources/</c>；
        /// 资源包缺失时是空字符串。
        /// </summary>
        public static string Root { get; } = IsAvailable
            ? $"pack://application:,,,/{ResourceAssemblyName};component/{ResourceRoot}"
            : string.Empty;

        /// <summary>
        /// 已解码图片缓存：<see cref="BitmapCacheOption.OnLoad"/> 不参与 WPF 内部的
        /// 「按 URI 共享解码结果」缓存，同一个 URI 反复取会各解一份像素；
        /// 这里按 URI 共享同一个 Freeze 过的实例（可安全跨线程/多处复用）。
        /// </summary>
        /// <remarks>
        /// ★ 必须声明在 <see cref="DefaultAvatar"/> 之前：静态字段是按声明顺序初始化的，
        /// 而 <see cref="DefaultAvatar"/> 的初始化会走 <see cref="Load"/> 用到这个字典。
        /// </remarks>
        private static readonly Dictionary<string, BitmapImage> Cache = new();

        /// <summary>缓存上限（账号列表一屏头像数远小于此值）。</summary>
        private const int CacheLimit = 256;

        private static bool Detect()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    AppContext.BaseDirectory, ResourceAssemblyName + ".dll");

                if (System.IO.File.Exists(path))
                {
                    Assembly.LoadFrom(path);
                    return true;
                }

                Assembly.Load(ResourceAssemblyName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 账号卡片的默认头像（<c>Resources/Template/角色头像/150x150/空.png</c>）。
        /// 资源包缺失时为 <c>null</c> —— XAML 里配合 <c>{x:Static}</c> 使用，
        /// 取不到就显示空白，不会抛异常。
        /// </summary>
        public static BitmapImage? DefaultAvatar { get; } = Load("Template/角色头像/150x150/空.png");

        /// <summary>把「相对 Resources/ 的路径」拼成完整 pack URI；资源包缺失时返回空串。</summary>
        public static string Pack(string relativePath)
        {
            if (!IsAvailable) return string.Empty;
            if (string.IsNullOrEmpty(relativePath)) return string.Empty;

            return Root + relativePath.Replace('\\', '/').TrimStart('/');
        }

        /// <summary>
        /// 加载图片（<see cref="BitmapCacheOption.OnLoad"/> + <see cref="Freezable.Freeze"/>，
        /// 解完立即释放文件句柄，且可跨线程/多处共享）。
        /// 资源包缺失或图片不存在时返回 null。
        /// </summary>
        /// <param name="decodePixelWidth">
        /// 大于 0 时按指定宽度解码（<see cref="BitmapImage.DecodePixelWidth"/>）。
        /// 列表里一次性铺开一百多张头像时用它把每张压到几十像素，内存和耗时都会明显下降。
        /// </param>
        public static BitmapImage? Load(string relativePath, int decodePixelWidth = 0)
        {
            string uri = Pack(relativePath);
            if (string.IsNullOrEmpty(uri)) return null;

            string cacheKey = decodePixelWidth > 0 ? $"{uri}#w{decodePixelWidth}" : uri;
            if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.UriSource = new Uri(uri, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                if (Cache.Count >= CacheLimit) Cache.Clear();
                Cache[cacheKey] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 资源包内的全部资源键（来自 g.resources，**已转小写**），懒加载只读一次。
        /// </summary>
        private static readonly Lazy<string[]> AllKeys = new(LoadAllKeys, true);

        private static string[] LoadAllKeys()
        {
            try
            {
                var asm = Assembly.Load(ResourceAssemblyName);
                using var stream = asm.GetManifestResourceStream(
                    ResourceAssemblyName + ".g.resources");
                if (stream == null) return Array.Empty<string>();

                var keys = new List<string>();
                using var reader = new System.Resources.ResourceReader(stream);
                var it = reader.GetEnumerator();
                while (it.MoveNext())
                {
                    if (it.Key is string k) keys.Add(k.ToLowerInvariant());
                }
                return keys.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 读取资源包里的文本资源（按 UTF-8 解码）。
        /// 资源包缺失或文件不存在时返回 <c>null</c>。
        /// </summary>
        public static string? LoadText(string relativePath)
        {
            string uri = Pack(relativePath);
            if (string.IsNullOrEmpty(uri)) return null;

            try
            {
                var info = System.Windows.Application.GetResourceStream(
                    new Uri(uri, UriKind.Absolute));
                if (info?.Stream == null) return null;

                using var reader = new System.IO.StreamReader(info.Stream, System.Text.Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 列出资源包里某个目录下的文件（只取本层，不含扩展名）。
        /// 例如 <c>ListFileNames("Images/Pictures/角色头像")</c> 返回全部角色名。
        /// 资源包缺失 / 目录为空时返回空列表。
        ///
        /// <para>
        /// ★ g.resources 的键对非 ASCII 做过百分号转义（<c>迪希雅.png</c> →
        /// <c>%e8%bf%aa…</c>），这里用 <see cref="Uri.UnescapeDataString(string)"/> 还原；
        /// 英文部分会被转成小写，所以该接口更适合中文名素材。
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> ListFileNames(
            string relativeDirectory, string extension = ".png")
        {
            if (!IsAvailable) return Array.Empty<string>();

            string prefix = (ResourceRoot + relativeDirectory)
                .Replace('\\', '/').TrimEnd('/') + "/";
            string suffix = extension;

            var result = new List<string>();
            foreach (string key in AllKeys.Value)
            {
                // ★ 键是把「整条路径」做过百分号转义的（不只是文件名）——
                //   目录名里一旦有中文（如 Images/Pictures/角色头像/），拿中文原文当前缀
                //   是匹配不上的，必须先把键还原成可读路径再比。
                string path = Uri.UnescapeDataString(key);

                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                string rest = path.Substring(prefix.Length);
                if (rest.Length == 0 || rest.Contains('/')) continue;      // 只取本层
                if (!rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                result.Add(rest.Substring(0, rest.Length - suffix.Length));
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }
    }
}
