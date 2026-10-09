using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace LvchaxsBS.UI.Dialogs
{
    /// <summary>
    /// 开源许可与免责声明弹窗。
    ///
    /// 用途：GPL-3.0 第 0 条要求程序在交互界面上展示「Appropriate Legal Notices」
    /// （版权声明 / 无担保声明 / 可以依据本许可证传播 / 如何查看许可证副本），
    /// 这个弹窗就是承载这四项信息的地方，入口在「设置 → 关于项目」。
    ///
    /// 用法：
    ///   var dlg = new LicenseDialog { Owner = Window.GetWindow(this) };
    ///   dlg.ShowDialog();
    ///
    /// 注意：一旦程序展示了这些声明，依据 GPL-3.0 第 5(d) 条，
    /// 修改后再分发的衍生版本也必须保留这套声明。
    /// </summary>
    public partial class LicenseDialog : Window
    {
        /// <summary>完整许可证正文所在地址（仓库根目录的 LICENSE）。</summary>
        private const string LicenseUrl =
            "https://github.com/Lvchaxs/LvchaxsBS/blob/master/LICENSE";

        private const string CopyrightHolder = "Lvchaxs";
        private const int CopyrightYear = 2026;

        /// <summary>GPL-3.0 推介的版权与无担保声明（中文为主，附英文原文那一句）。</summary>
        private static readonly string LicenseNotice = string.Join("\n\n", new[]
        {
            $"版权所有 (C) {CopyrightYear} {CopyrightHolder}",

            "本程序是自由软件：你可以依据自由软件基金会发布的 GNU 通用公共许可证第 3 版的条款，" +
            "重新分发和／或修改它。",

            "本程序的分发是希望它有用，但不提供任何担保，甚至不提供适销性或特定用途适用性的默示担保。" +
            "详见 GNU 通用公共许可证。",

            "This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; " +
            "without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. " +
            "See the GNU General Public License for more details.",

            "你应当已随本程序收到一份 GNU 通用公共许可证的副本；如果没有，请访问 www.gnu.org/licenses/ 获取。",

            "对外分发本程序或其修改版本时，你必须一并提供完整的对应源代码，并按 GPL-3.0 授权，" +
            "同时在改动过的文件中标注「已修改」及修改日期。",

            "依据 GPL-3.0 第 15、16 条，作者不对本程序、以及任何人基于本程序所做的修改与分发" +
            "（含收费分发）承担任何担保或责任。",

            "本程序与米哈游 / COGNOSPHERE（HoYoverse）无任何隶属或合作关系；" +
            "仓库中取自游戏的图片、模板等第三方素材不在本授权范围内，其权利归原权利人所有。",

            "【账号风险】本程序是非官方的第三方辅助工具，米哈游《原神》的用户协议及相关规则" +
            "不允许使用此类工具。使用本程序可能被游戏方判定为违规，导致账号被警告、限制功能或封禁等后果；" +
            "此类风险全部由使用者自行承担，作者不承担任何责任，也不提供申诉协助或补救。"
        });

        public LicenseDialog()
        {
            InitializeComponent();

            LicenseText.Text = LicenseNotice;

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

        /// <summary>用系统默认浏览器打开仓库里的完整许可证正文。</summary>
        private void ViewFullBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = LicenseUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开许可证链接失败：{ex.Message}");
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
