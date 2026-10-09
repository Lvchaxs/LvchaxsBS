using LvchaxsBS.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LvchaxsBS.UI.Dialogs
{
    /// <summary>
    /// 「当前版本改动」弹窗。入口在「设置 → 程序版本」卡片里的「版本改动」按钮。
    ///
    /// <para>显示的是**当前运行版本**带来的改动，来源按优先级：</para>
    /// <list type="number">
    /// <item>本地缓存 <c>Cache/changelog_&lt;当前版本&gt;.json</c> —— 有就**直接显示，不联网**；</item>
    /// <item>没有缓存时才联网拉一次 modules.json：**只有远端版本与当前版本一致**才采纳并缓存
    ///       （远端已经出了更新版本时，那份改动属于新版本，留给更新之后再看）；</item>
    /// <item>联网失败 / 版本对不上 → 显示本文件里的 <see cref="FallbackChangelog"/> 兜底。</item>
    /// </list>
    ///
    /// <para>缓存的读写与解析见 <see cref="ChangelogService"/>。</para>
    /// </summary>
    public partial class ChangelogDialog : Window
    {
        /// <summary>
        /// ⬇⬇⬇ 最后兜底文案（当前版本第一次运行 + 断网时才会看到）⬇⬇⬇
        /// 正常情况下内容来自 Gitee 的 modules.json，并按版本缓存到 Cache/changelog_&lt;版本&gt;.json。
        /// 这里请填写**当前编译版本**的改动内容，发版时跟着一起改。
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

        /// <summary>没有本地缓存时，才需要联网拉一次</summary>
        private readonly bool _needFetch;

        public ChangelogDialog()
        {
            InitializeComponent();

            string localVersion = UpdateCheckService.GetLocalVersion();

            // 优先本地缓存：有就完全不走网络
            string? cached = ChangelogService.LoadTextFor(localVersion);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                ChangelogText.Text = cached;
                _needFetch = false;
                Debug.WriteLine($"【版本改动】直接使用本地缓存（v{localVersion}），不联网");
            }
            else
            {
                ChangelogText.Text = FallbackChangelog;
                _needFetch = true;
                Debug.WriteLine($"【版本改动】本地没有 v{localVersion} 的缓存，将联网尝试获取一次");
            }

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

            // 已有当前版本的缓存 → 不再联网
            if (!_needFetch) return;

            await FetchOnceAsync();
        }

        /// <summary>
        /// 仅在没有本地缓存时调用一次：
        /// 拉到远端内容后，**必须远端版本 == 本地版本**才采纳并缓存；
        /// 远端已经更新（版本不一致）时保持兜底文案不动 —— 那份改动属于新版本，
        /// 等用户真正更新到新版本后才会成为"当前版本改动"。
        /// </summary>
        private async Task FetchOnceAsync()
        {
            string localVersion = UpdateCheckService.GetLocalVersion();

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
                Debug.WriteLine("【版本改动】拉不到 modules.json，沿用内置兜底");
                ChangelogText.Text = FallbackChangelog + "\n\n（未能连接远程仓库，以上为程序内置内容）";
                return;
            }

            string? remoteVersion = UpdateCheckService.ParseVersionFromJson(json);
            if (!UpdateCheckService.IsSameVersion(remoteVersion, localVersion))
            {
                Debug.WriteLine($"【版本改动】远端 v{remoteVersion} 与本地 v{localVersion} 不一致，" +
                                "远端内容属于新版本，本次不采纳");
                ChangelogText.Text =
                    FallbackChangelog + "\n\n（远程仓库已有更新版本，以上为当前版本的内置说明）";
                return;
            }

            string? text = ChangelogService.ParseChangelog(json);
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.WriteLine("【版本改动】远端没有可用的 changelog 字段，沿用内置兜底");
                return;
            }

            ChangelogText.Text = text;
            ChangelogService.SaveCacheFor(localVersion, json);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
