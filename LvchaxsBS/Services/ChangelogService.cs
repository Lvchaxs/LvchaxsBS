using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 「版本改动」的内容解析与本地缓存。
    ///
    /// <para>
    /// 缓存策略：**按版本号各存一份** —— <c>Cache/changelog_&lt;版本号&gt;.json</c>，
    /// 内容为 Gitee 上 modules.json 的原始文本。
    /// </para>
    ///
    /// 这样做的目的：
    /// <list type="bullet">
    /// <item>「当前程序版本」永远读自己那一份，显示的就是"这个版本带来了什么改动"；</item>
    /// <item>远端出现新版本时写的是**另一个文件名**，不会覆盖当前版本的缓存；</item>
    /// <item>只有当程序真正更新到新版本之后（更新确认时预写 + 重启后按新版本号读取），
    ///       新内容才会成为"当前版本改动"。</item>
    /// </list>
    /// </summary>
    public static class ChangelogService
    {
        /// <summary>远端 changelog 字段的候选名（不区分大小写，取第一个命中的）</summary>
        private static readonly string[] ChangelogFieldNames =
            { "changelog", "changelogs", "changes", "updates", "更新内容" };

        /// <summary>
        /// 本地缓存目录。放在 <c>Cache</c> 下 —— 「重置配置」只删 <c>Config</c>，缓存不会被带走。
        /// </summary>
        public static string CacheDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache");

        /// <summary>指定版本的缓存文件路径</summary>
        public static string CachePathFor(string? version) =>
            Path.Combine(CacheDir, $"changelog_{SanitizeVersion(version)}.json");

        /// <summary>指定版本是否已有本地缓存</summary>
        public static bool HasCacheFor(string? version) => File.Exists(CachePathFor(version));

        // ==================== 读 ====================

        /// <summary>读取指定版本的缓存原始文本；没有或读失败返回空串。</summary>
        public static string ReadRawFor(string? version)
        {
            try
            {
                string path = CachePathFor(version);
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【版本改动】读缓存失败（v{version}）：{ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>取指定版本已缓存的改动文案；没有可用内容时返回 null。</summary>
        public static string? LoadTextFor(string? version)
        {
            string raw = ReadRawFor(version);
            return string.IsNullOrWhiteSpace(raw) ? null : ParseChangelog(raw);
        }

        // ==================== 写 ====================

        /// <summary>
        /// 把 modules.json 原始文本按版本号存成缓存（UTF-8 无 BOM）。
        /// 失败不影响调用方，只记调试日志。
        /// </summary>
        public static void SaveCacheFor(string? version, string? rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson) || string.IsNullOrWhiteSpace(version))
                return;

            // 内容里没有 changelog 就不必存了
            if (string.IsNullOrWhiteSpace(ParseChangelog(rawJson)))
            {
                Debug.WriteLine($"【版本改动】v{version} 的内容里没有 changelog 字段，跳过缓存");
                return;
            }

            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(CachePathFor(version), rawJson, new UTF8Encoding(false));
                Debug.WriteLine($"【版本改动】已缓存 v{version} 的改动内容");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【版本改动】写缓存失败（v{version}）：{ex.Message}");
            }
        }

        // ==================== 解析 ====================

        /// <summary>
        /// 从 modules.json 里解析改动文案。容错处理，支持三种写法：
        /// <list type="number">
        /// <item><c>"changelog": ["第一行", "第二行", ...]</c> —— 推荐，一行一个元素</item>
        /// <item><c>"changelog": "第一行\n第二行"</c></item>
        /// <item><c>"changelog": { "新增": ["条目", ...], "调整": [...] }</c> —— 自动拼成「小标题 + · 条目」</item>
        /// </list>
        /// </summary>
        public static string? ParseChangelog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                foreach (var prop in root.EnumerateObject())
                {
                    if (!IsChangelogField(prop.Name))
                        continue;

                    switch (prop.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                        {
                            string? s = prop.Value.GetString();
                            return string.IsNullOrWhiteSpace(s) ? null : s;
                        }

                        case JsonValueKind.Array:
                        {
                            var lines = new List<string>();
                            foreach (var item in prop.Value.EnumerateArray())
                                lines.Add(item.ValueKind == JsonValueKind.String
                                    ? item.GetString() ?? string.Empty
                                    : item.GetRawText());

                            return lines.Count == 0 ? null : string.Join("\n", lines);
                        }

                        case JsonValueKind.Object:
                        {
                            var lines = new List<string>();
                            foreach (var section in prop.Value.EnumerateObject())
                            {
                                if (lines.Count > 0) lines.Add(string.Empty);
                                lines.Add(section.Name);

                                if (section.Value.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var item in section.Value.EnumerateArray())
                                        lines.Add("· " + (item.ValueKind == JsonValueKind.String
                                            ? item.GetString()
                                            : item.GetRawText()));
                                }
                                else if (section.Value.ValueKind == JsonValueKind.String)
                                {
                                    lines.Add("· " + section.Value.GetString());
                                }
                            }

                            return lines.Count == 0 ? null : string.Join("\n", lines);
                        }
                    }
                }

                return null;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"【版本改动】modules.json 解析失败：{ex.Message}");
                return null;
            }
        }

        private static bool IsChangelogField(string name)
        {
            foreach (var candidate in ChangelogFieldNames)
            {
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>把版本号里的非法文件名字符合法化（点号保留，其余非字母数字换成下划线）。</summary>
        private static string SanitizeVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "unknown";

            var sb = new StringBuilder(version.Length);
            foreach (char c in version)
                sb.Append(char.IsLetterOrDigit(c) || c == '.' ? c : '_');

            return sb.ToString();
        }
    }
}
