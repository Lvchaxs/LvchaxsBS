using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.Core
{
    /// <summary>
    /// 图片资源包的统一入口。
    ///
    /// <para>
    /// 所有 UI 图标与图像识别模板都集中在<b>独立的资源程序集</b>
    /// <c>LvchaxsBS_Resources.dll</c> 里（源码工程见同级目录 LvchaxsBS_Resources）。
    /// 它是<b>可选组件</b>：dll 不在 exe 同目录时主程序照常启动，
    /// 只是所有图片取不到（Image 显示空白），不会抛异常、不会崩溃。
    /// </para>
    ///
    /// <para>
    /// 用法（相对路径一律以 <c>Resources/</c> 为根，不要写前导斜杠）：
    /// <code>
    /// using LvchaxsBS.Core;
    ///
    /// string uri = AssetLocator.Pack("Images/图标/钓鱼辅助.png");        // 拼 pack URI
    /// string uri = AssetLocator.Pack($"Images/图标/{iconName}");          // 插值同理
    /// BitmapImage? img = AssetLocator.Load("Images/图标/钓鱼辅助.png");   // 直接拿图，失败为 null
    /// </code>
    /// 资源包缺失时 <see cref="Pack"/> 返回空字符串 —— 下游的
    /// <see cref="UI.Helpers.ResourceImageHelper.Load"/> 与模板加载器都会把它当成
    /// “取不到”，于是安静地留空。
    /// </para>
    /// </summary>
    public static class AssetLocator
    {
        /// <summary>资源程序集名（不含 .dll）。</summary>
        public const string ResourceAssemblyName = "LvchaxsBS_Resources";

        /// <summary>资源程序集内部的根目录。</summary>
        private const string ResourceRoot = "Resources/";

        /// <summary>
        /// 资源包是否可用。进程内只探测一次（<see cref="Assembly.Load(string)"/>
        /// 会先在应用目录里按简单名找 LvchaxsBS_Resources.dll）。
        /// </summary>
        public static bool IsAvailable { get; } = Detect();

        /// <summary>
        /// pack URI 前缀，形如
        /// <c>pack://application:,,,/LvchaxsBS_Resources;component/Resources/</c>；
        /// 资源包缺失时是空字符串。
        /// </summary>
        public static string Root { get; } = IsAvailable
            ? $"pack://application:,,,/{ResourceAssemblyName};component/{ResourceRoot}"
            : string.Empty;

        private static bool Detect()
        {
            try
            {
                // 显式按文件路径加载：既完成"dll 在不在"的探测，又保证后续
                // pack URI（内部是按简单名 Assembly.Load）一定能命中这份已加载的程序集。
                // 只按简单名 Assembly.Load 在部分宿主形态下探测不到 AppBase 里的 dll。
                string path = System.IO.Path.Combine(
                    AppContext.BaseDirectory, ResourceAssemblyName + ".dll");

                if (System.IO.File.Exists(path))
                {
                    Assembly.LoadFrom(path);
                    return true;
                }

                // dll 不在程序目录（少见），再按简单名试一次
                Assembly.Load(ResourceAssemblyName);
                return true;
            }
            catch
            {
                // 没有资源包 → 全部图片功能降级为“空白”，但不影响主程序启动
                return false;
            }
        }

        /// <summary>
        /// 把「相对 Resources/ 的路径」拼成完整 pack URI。资源包缺失时返回空串。
        /// </summary>
        public static string Pack(string relativePath)
        {
            if (!IsAvailable) return string.Empty;
            if (string.IsNullOrEmpty(relativePath)) return string.Empty;

            return Root + relativePath.Replace('\\', '/').TrimStart('/');
        }

        /// <summary>
        /// 直接加载成 <see cref="BitmapImage"/>（经由统一缓存，同一个 URI 只解一份像素）。
        /// 资源包缺失或图片不存在时返回 null。
        /// </summary>
        public static BitmapImage? Load(string relativePath)
            => UI.Helpers.ResourceImageHelper.Load(Pack(relativePath));

        /// <summary>
        /// 资源包内的全部资源键（来自 g.resources，**已转小写**），
        /// 形如 <c>resources/images/voice/pictures/空.png</c>。懒加载，只读一次。
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
        /// 列出资源包里某个目录下的文件（只取本层，不含扩展名）。
        /// 例如 <c>ListFileNames("Images/Pictures/角色头像")</c> 返回全部角色名。
        ///
        /// <para>
        /// 用途：让"角色数量"这类清单以**资源包里的图片为准**（编译期固定），
        /// 而不是运行时扫磁盘目录。资源包缺失 / 目录为空时返回空列表。
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
