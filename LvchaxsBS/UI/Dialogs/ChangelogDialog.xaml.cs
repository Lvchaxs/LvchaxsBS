using System;
using System.Windows;
using System.Windows.Input;

namespace LvchaxsBS.UI.Dialogs
{
    /// <summary>
    /// 「当前版本改动」弹窗。入口在「设置 → 程序版本」卡片里的「版本改动」按钮。
    ///
    /// 文案是**硬编码**的，就在本文件下面的 <see cref="Changelog"/> 常量里改。
    /// 这里只写**当前版本**的改动；发布新版本时把内容整体换成新版本的即可
    /// （旧版本改动可另行保留在 git 历史里）。
    /// </summary>
    public partial class ChangelogDialog : Window
    {
        /// <summary>
        /// ⬇⬇⬇ 当前版本改动正文：直接在这里改 ⬇⬇⬇
        /// 建议格式：第一行「版本号  日期」，然后用小标题（新增 / 调整 / 修复）+ 「· 」开头的条目。
        /// 记得改版本号时与 LvchaxsBS.csproj 里的 &lt;Version&gt; 保持一致。
        /// </summary>
        private const string Changelog =
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

        public ChangelogDialog()
        {
            InitializeComponent();

            ChangelogText.Text = Changelog;

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
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
