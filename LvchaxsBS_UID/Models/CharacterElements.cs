using LvchaxsBS_UID.Utils.UI;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LvchaxsBS_UID.Models
{
    /// <summary>
    /// 角色 → 编号 / 元素 的对照表。
    ///
    /// <para>
    /// 数据放在共用资源包里：<c>Resources/Data/CharacterElements.json</c>，形如：
    /// <code>
    /// { "characters": [ { "id": "001", "name": "空", "element": "" }, ... ] }
    /// </code>
    /// <c>id</c> 是三位数编号，按角色首次进入卡池的上线先后排序
    /// （空 001 / 荧 002 / 安柏 003 / 凯亚 004 …）；
    /// 旅行者（空 / 荧）没有固定元素，<c>element</c> 留空。
    /// 想调整顺序、加新角色或改某个角色的元素，改那个 JSON 后重新编译资源包即可，不用改代码。
    /// </para>
    ///
    /// <para>
    /// 资源包缺失 / JSON 读不到 / 解析失败时，整张表是空的 ——
    /// 表现就是元素筛选点了没反应（只剩「全部」）、头像按名字排序，不会抛异常。
    /// </para>
    /// </summary>
    public static class CharacterElements
    {
        /// <summary>七个元素（按界面上的显示顺序）。</summary>
        public static readonly string[] All = { "火", "水", "风", "雷", "草", "冰", "岩" };

        /// <summary>取不到编号时用的兜底值（排在所有已编号角色之后）。</summary>
        private const int UnknownOrder = int.MaxValue;

        /// <summary>旅行者（空 / 荧）。头像列表里这两人固定排在最前，其余按编号倒序。</summary>
        private static readonly HashSet<string> Travelers =
            new(new[] { "空", "荧" }, StringComparer.OrdinalIgnoreCase);

        private static readonly Lazy<Table> Data = new(Load, true);

        /// <summary>是否为旅行者（空 / 荧）。</summary>
        public static bool IsTraveler(string? character)
            => !string.IsNullOrEmpty(character) && Travelers.Contains(character);

        /// <summary>取某个角色的元素；查不到返回 null。</summary>
        public static string? ElementOf(string? character)
        {
            if (string.IsNullOrEmpty(character)) return null;
            return Data.Value.Element.TryGetValue(character, out var element) ? element : null;
        }

        /// <summary>
        /// 取某个角色的排序编号（001 = 1）；查不到返回 <see cref="int.MaxValue"/>。
        /// 用于让头像列表按「游戏内顺序」排列。
        /// </summary>
        public static int OrderOf(string? character)
        {
            if (string.IsNullOrEmpty(character)) return UnknownOrder;
            return Data.Value.Order.TryGetValue(character, out var order) ? order : UnknownOrder;
        }

        /// <summary>取某个角色的三位数编号文本（如 "001"）；查不到返回 null。</summary>
        public static string? IdOf(string? character)
        {
            int order = OrderOf(character);
            return order == UnknownOrder ? null : order.ToString("000");
        }

        // ===== 「其他头像」（NPC / 任务类）—— 独立的一套编号，与角色编号互不相干 =====

        /// <summary>
        /// 取某个「其他头像」的编号（001 起，按实装版本先后）；查不到返回 0。
        /// </summary>
        public static int OtherOrderOf(string? name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            return Data.Value.OtherOrder.TryGetValue(name, out var order) ? order : 0;
        }

        /// <summary>取某个「其他头像」的实装版本（如 "4.6"）；查不到返回 null。</summary>
        public static string? VersionOf(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Data.Value.Version.TryGetValue(name, out var v) ? v : null;
        }

        private static Table Load()
        {
            var table = new Table();

            try
            {
                string? json = AssetLocator.LoadText("Data/CharacterElements.json");
                if (string.IsNullOrEmpty(json)) return table;

                var file = JsonSerializer.Deserialize<CharactersFile>(json);
                if (file == null) return table;

                if (file.Characters != null)
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

                if (file.Others != null)
                {
                    foreach (var item in file.Others)
                    {
                        if (item == null || string.IsNullOrEmpty(item.Name)) continue;

                        if (int.TryParse(item.Id, out int order) && order > 0)
                            table.OtherOrder[item.Name] = order;

                        if (!string.IsNullOrEmpty(item.Version))
                            table.Version[item.Name] = item.Version;
                    }
                }
            }
            catch
            {
                // 资源包缺失 / JSON 有问题 → 空表
            }

            return table;
        }

        /// <summary>查表结果。</summary>
        private sealed class Table
        {
            /// <summary>角色名 → 元素。</summary>
            public Dictionary<string, string> Element { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>角色名 → 编号。</summary>
            public Dictionary<string, int> Order { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>其他头像名 → 编号（独立一套）。</summary>
            public Dictionary<string, int> OtherOrder { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>其他头像名 → 实装版本。</summary>
            public Dictionary<string, string> Version { get; } =
                new(StringComparer.OrdinalIgnoreCase);
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

            [JsonPropertyName("version")]
            public string? Version { get; set; }
        }
    }
}
