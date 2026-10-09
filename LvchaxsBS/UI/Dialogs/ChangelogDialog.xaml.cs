using LvchaxsBS.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LvchaxsBS.UI.Dialogs
{
    /// <summary>
    /// 「当前版本改动」弹窗。入口在「设置 → 程序版本」卡片里的「版本改动」按钮。
    ///
    /// 内容优先从 Gitee 仓库的 modules.json 里读取（字段 <c>changelog</c>），
    /// 所以改文案只要改远端文件、不用重新发版；拉不到时用本文件里的
    /// <see cref="FallbackChangelog"/> 兜底。
    /// </summary>
    public partial class ChangelogDialog : Window
    {
        /// <summary>
        /// ⬇⬇⬇ 兜底文案（离线 / 远端还没配 changelog 时显示）⬇⬇⬇
        /// 正常情况下来自 modules.json，这里只保证"断网也有东西看"。
        /// </summary>
        private const string FallbackChangelog =
            """
            v1.0.3.4  2026-10-09

            新增
            · 「关于项目」新增「开源许可」入口，可查看 GPL-3.0 授权与免责声明
            · 设置页新增本「版本改动」弹窗
            · 快速传送：识别未命中时也会上报滑窗最高相似度，状态标签能看到具体分数
            · 自动清药新增「粗匹配」可配置项（默认 3）；粗匹配档位统一收紧为 1-5

            调整
            · 按钮悬停缩放 1.15 → 1.06、按下 0.95 → 0.97，动作更克制；顶部功能栏与启动按钮保留原放大效果
            · 复选框方块悬停 1.18 → 1.08、按下 0.88 → 0.95
            · 个性化「更换壁纸」图标与文字合并为一个按钮，点文字也能选壁纸
            · 手柄触发键的悬停 / 按下 / 选中表现，与键盘触发键完全统一
            · 快速传送：列表识别开关语义反转（禁用 → 启用）

            修复
            · 拖动壁纸滑块时每帧一次的文件系统调用与 5 条无谓动画重建（约 11ms/秒 UI 线程）已移除
            · 气泡提示只在实际超出窗口时才拉回，不再误推贴边的气泡
            """;

        /// <summary>远端 changelog 字段的候选名（不区分大小写，取第一个命中的）</summary>
        private static readonly string[] ChangelogFieldNames =
            { "changelog", "changelogs", "changes", "updates", "更新内容" };

        public ChangelogDialog()
        {
            InitializeComponent();

            // 先显示兜底内容，远端拿到后再替换（避免白屏）
            ChangelogText.Text = FallbackChangelog;

            // Esc 关闭
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };

            // 支持拖动窗口
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };

            Loaded += ChangelogDialog_Loaded;
        }

        private async void ChangelogDialog_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ChangelogDialog_Loaded;
            await LoadRemoteChangelogAsync();
        }

        /// <summary>
        /// 拉取 modules.json 并取出改动文案。失败就继续用兜底内容。
        /// </summary>
        private async Task LoadRemoteChangelogAsync()
        {
            string json;
            try
            {
                json = await UpdateCheckService.FetchModulesJsonAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【版本改动】拉取 modules.json 异常：{ex.Message}");
                json = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.WriteLine("【版本改动】拉不到 modules.json，显示内置内容");
                ChangelogText.Text = FallbackChangelog + "\n\n（未能连接远程仓库，以上为程序内置内容）";
                return;
            }

            string? text = ParseChangelog(json);

            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.WriteLine("【版本改动】modules.json 里没有可用的 changelog 字段，显示内置内容");
                ChangelogText.Text = FallbackChangelog;
                return;
            }

            ChangelogText.Text = text;
        }

        /// <summary>
        /// 从 modules.json 里解析改动文案。容错处理，支持三种写法：
        /// 1. "changelog": ["第一行", "第二行", ...]      ← 推荐，一行一个元素
        /// 2. "changelog": "第一行\n第二行"
        /// 3. "changelog": { "新增": ["条目", ...], "调整": [...] }  ← 自动拼成「小标题 + · 条目」
        /// </summary>
        private static string? ParseChangelog(string json)
        {
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

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
