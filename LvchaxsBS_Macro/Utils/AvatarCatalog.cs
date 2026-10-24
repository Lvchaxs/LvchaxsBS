using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace LvchaxsBS_Macro.Utils
{
    /// <summary>
    /// 头像清单 + 角色元素表 —— 图片与数据都来自宿主的共用资源包 <c>LvchaxsBS_Resources.dll</c>，
    /// 和 UID「添加/编辑账号」窗口用的是同一套图、同一份 <c>Data/CharacterElements.json</c>。
    ///
    /// <para>
    /// 顺序完全照 UID：**旅行者（空 / 荧）固定在最前**（按编号正序），
    /// 其余角色按编号 <b>倒序</b>（最新上线在前），没编号的排最后再按拼音；
    /// 「其他头像」统一排在所有角色之后，按它自己那套编号倒序。
    /// </para>
    /// <para>
    /// ★ 宏插件没有宿主 / 资源包的编译期引用，一律反射调
    ///   <c>LvchaxsBS.Core.AssetLocator.Pack</c> / <c>ListFileNames</c>。
    /// </para>
    /// <para>
    /// ★★ 宿主没加载 / 资源包缺失时**降级而不是报错**：清单只剩默认头像「空」，
    ///   图片返回 null（界面那边自然显示空白），元素筛选点了等于「全部」，功能照常。
    /// </para>
    /// </summary>
    public static class AvatarCatalog
    {
        /// <summary>没有选头像时用的名字（资源包里一定有这一张）。</summary>
        public const string DefaultName = "空";

        /// <summary>
        /// 头像解码的目标宽度（像素）。
        /// <para>
        /// ★ 头像在界面上最大只显示 48×48（宏卡片图标 / 头像网格格子），
        ///   按 96 解码（2 倍、够 200% 缩放）就够了 —— 原图是 164×164，
        ///   按缩略图解码 152 张能省掉大半的解码时间和内存。
        /// </para>
        /// </summary>
        public const int AvatarThumbWidth = 96;

        /// <summary>角色头像目录。</summary>
        private const string CharacterDir = "Images/Pictures/角色头像";

        /// <summary>「其他头像」（NPC / 任务类）目录。</summary>
        private const string OtherDir = "Images/Pictures/其他头像";

        /// <summary>「其他」筛选按钮用的伪元素名（Tag 值，与 UID 一致）。</summary>
        public const string OtherFilterKey = "其他";

        /// <summary>七个元素（按界面上的显示顺序）。</summary>
        public static readonly string[] Elements = { "火", "水", "风", "雷", "草", "冰", "岩" };

        /// <summary>按中文（zh-CN）排序的比较器，和 UID 头像网格一致。</summary>
        private static readonly StringComparer PinyinComparer =
            StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), true);

        private static List<AvatarItem>? _all;

        /// <summary>全部可选头像（未过滤）：角色（含旅行者）+ 其他头像。</summary>
        public static IReadOnlyList<AvatarItem> All => _all ??= LoadAll();

        /// <summary>是否为旅行者（空 / 荧）—— 她们不属于任何元素，元素筛选里不出现。</summary>
        public static bool IsTraveler(string? name) => Elements_IsTraveler(name);

        /// <summary>取角色元素；查不到返回 null（「其他头像」也返回 null）。</summary>
        public static string? ElementOf(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Elements_Table.Value.Element.TryGetValue(name, out string? element) ? element : null;
        }

        // ===== 清单构建（完全照 UID LoadAvatarItems） =====

        private static List<AvatarItem> LoadAll()
        {
            var list = new List<AvatarItem>();

            // ---- 角色头像 ----
            var names = ListNames(CharacterDir);

            int SortKey(string n)
            {
                int order = OrderOf(n);
                if (order == int.MaxValue) return 1_000_000;
                return Elements_IsTraveler(n) ? order : -order;
            }

            var ordered = names
                .OrderBy(n => Elements_IsTraveler(n) ? 0 : 1)
                .ThenBy(SortKey)
                .ThenBy(n => n, PinyinComparer)
                .ToList();

            // ★ 只记路径、先不解码 —— 图片等真正要显示时再解（见 AvatarItem.Image）
            foreach (string name in ordered)
                list.Add(new AvatarItem { Name = name, Path = $"{CharacterDir}/{name}.png" });

            // ---- 其他头像（NPC / 任务类）----
            int OtherKey(string n)
            {
                int order = OtherOrderOf(n);
                return order == 0 ? int.MinValue : -order;
            }

            var others = ListNames(OtherDir)
                .OrderBy(OtherKey)
                .ThenBy(n => n, PinyinComparer)
                .ToList();

            foreach (string name in others)
                list.Add(new AvatarItem { Name = name, IsOther = true, Path = $"{OtherDir}/{name}.png" });

            // 资源包缺失 → 至少留一个默认头像，免得网格全空、保存时取出空串
            if (list.Count == 0)
                list.Add(new AvatarItem { Name = DefaultName, Path = $"{CharacterDir}/{DefaultName}.png" });

            return list;
        }

        /// <summary>取某个头像名对应的那一项（找不到返回 null）。</summary>
        public static AvatarItem? Find(string? name)
            => string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(i => i.Name == name);

        /// <summary>解码某张头像（找不到就是 null，界面显示空白，不抛异常）。</summary>
        public static BitmapImage? Load(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = DefaultName;

            AvatarItem? hit = Find(name);
            if (hit != null) return hit.Image;

            // 不在「全部」里（比如配置里存了一个后来被删掉的名字）→ 直接按角色目录猜一次
            return Decode($"{CharacterDir}/{name}.png", AvatarThumbWidth);
        }

        // ===== 排序 / 元素表 =====

        /// <summary>角色编号（001 = 1）；查不到返回 <see cref="int.MaxValue"/>。</summary>
        private static int OrderOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return int.MaxValue;
            return Elements_Table.Value.Order.TryGetValue(name, out int order) ? order : int.MaxValue;
        }

        /// <summary>「其他头像」编号（独立一套）；查不到返回 0。</summary>
        private static int OtherOrderOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            return Elements_Table.Value.OtherOrder.TryGetValue(name, out int order) ? order : 0;
        }

        private static bool Elements_IsTraveler(string? name)
            => !string.IsNullOrEmpty(name)
               && (string.Equals(name, "空", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "荧", StringComparison.OrdinalIgnoreCase));

        private static readonly Lazy<ElementTable> Elements_Table = new(LoadElementTable, true);

        private static ElementTable LoadElementTable()
        {
            var table = new ElementTable();

            try
            {
                string? json = LoadText("Data/CharacterElements.json");
                if (string.IsNullOrEmpty(json)) return table;

                var file = JsonSerializer.Deserialize<CharactersFile>(json);
                if (file?.Characters != null)
                {
                    foreach (var item in file.Characters)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Name)) continue;

                        if (!string.IsNullOrEmpty(item.Element))
                            table.Element[item.Name] = item.Element;

                        if (int.TryParse(item.Id, out int order) && order > 0)
                            table.Order[item.Name] = order;
                    }
                }

                if (file?.Others != null)
                {
                    foreach (var item in file.Others)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Name)) continue;
                        if (int.TryParse(item.Id, out int order) && order > 0)
                            table.OtherOrder[item.Name] = order;
                    }
                }
            }
            catch
            {
                // 资源包缺失 / JSON 有问题 → 空表（排序按拼音，元素筛选只剩「全部」）
            }

            return table;
        }

        private sealed class ElementTable
        {
            public Dictionary<string, string> Element { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> Order { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> OtherOrder { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class CharactersFile
        {
            [JsonPropertyName("characters")]
            public List<CharacterEntry>? Characters { get; set; }

            [JsonPropertyName("others")]
            public List<CharacterEntry>? Others { get; set; }
        }

        private sealed class CharacterEntry
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("element")]
            public string? Element { get; set; }
        }

        // ===== 资源包访问（反射宿主 AssetLocator） =====

        /// <summary>已解码的图片缓存（同一张图会被卡片、网格、筛选按钮反复要）。</summary>
        private static readonly Dictionary<string, BitmapImage?> Cache = new();

        /// <summary>
        /// 按资源包相对路径解码一张图（<paramref name="relativePath"/> 如 <c>Images/元素/火.png</c>）。
        /// </summary>
        /// <param name="relativePath">资源包里的相对路径。</param>
        /// <param name="decodePixelWidth">
        /// 目标宽度（像素）：&gt; 0 时按这个宽度解码（缩略图，快很多）；
        /// 0 = 按原图尺寸解码。★ 同一个尺寸各缓存一份。
        /// </param>
        public static BitmapImage? Decode(string relativePath, int decodePixelWidth = 0)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;

            string key = decodePixelWidth > 0 ? $"{relativePath}#{decodePixelWidth}" : relativePath;

            lock (Cache)
            {
                if (Cache.TryGetValue(key, out BitmapImage? cached)) return cached;
            }

            BitmapImage? image = DecodeCore(relativePath, decodePixelWidth);

            lock (Cache)
            {
                Cache[key] = image;
            }
            return image;
        }

        /// <summary>
        /// 把全部头像**提前**解码好（在界面空闲时一小批一小批做）。
        /// <para>
        /// ★ 目的：宏列表只解自己用得到的几张（打开快），但用户随后点「编辑」要的 152 张
        ///   已经在列表显示完、界面空闲的那点时间里解完了 ⇒ 编辑窗口不用再等。
        /// </para>
        /// </summary>
        public static void BeginWarmUp()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            IReadOnlyList<AvatarItem> all = All;
            int index = 0;

            void Step()
            {
                // 一批 12 张：单批时间短到不会被看成卡顿，又能很快预热完
                for (int n = 0; n < 12 && index < all.Count; n++, index++)
                    _ = all[index].Image;

                if (index < all.Count)
                    dispatcher.BeginInvoke(new Action(Step),
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            dispatcher.BeginInvoke(new Action(Step),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>资源包里的一个文件路径 → pack URI（取不到返回空串）。</summary>
        public static string Pack(string relativePath)
        {
            try
            {
                object? uri = LocatorMethod("Pack", 1)?.Invoke(null, new object[] { relativePath });
                return uri as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>读资源包里的一个文本文件（如 <c>Data/CharacterElements.json</c>）。</summary>
        private static string? LoadText(string relativePath)
        {
            string uri = Pack(relativePath);
            if (uri.Length == 0) return null;

            try
            {
                var info = System.Windows.Application.GetResourceStream(new Uri(uri, UriKind.Absolute));
                if (info?.Stream == null) return null;

                using var reader = new System.IO.StreamReader(info.Stream, System.Text.Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>列出资源包某个目录下的文件名（只取本层，不含扩展名）。</summary>
        private static List<string> ListNames(string relativeDir)
        {
            var list = new List<string>();

            try
            {
                object? result = LocatorMethod("ListFileNames", 2)
                    ?.Invoke(null, new object[] { relativeDir, ".png" });

                if (result is IEnumerable sequence)
                {
                    foreach (object? item in sequence)
                    {
                        if (item is string name && name.Length > 0) list.Add(name);
                    }
                }
            }
            catch
            {
                // 宿主不在 / 反射失败 → 空清单（调用方会兜底默认头像）
            }

            return list;
        }

        private static BitmapImage? DecodeCore(string relativePath, int decodePixelWidth)
        {
            try
            {
                string pack = Pack(relativePath);
                if (pack.Length == 0) return null;

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(pack, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                // ★ 缩略图解码：只解到需要的宽度（头像最大显示 48px），省时间也省内存
                if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
                image.EndInit();
                image.Freeze();          // 冻结后可以跨实例复用，也省一次拷贝
                return image;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 取宿主 <c>LvchaxsBS.Core.AssetLocator</c> 上的静态方法（按参数个数挑重载）。
        /// </summary>
        private static System.Reflection.MethodInfo? LocatorMethod(string name, int parameterCount)
        {
            try
            {
                Type? locator = Type.GetType("LvchaxsBS.Core.AssetLocator, LvchaxsBS");
                if (locator == null) return null;

                return locator.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount);
            }
            catch
            {
                return null;
            }
        }
    }
}
