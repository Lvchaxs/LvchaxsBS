using System.Windows;
using System.Windows.Controls;
using LvchaxsBS_Macro.Views;

namespace LvchaxsBS_Macro.Controls
{
    /// <summary>
    /// 配置容器页底部按钮条的「添加宏」按钮（宿主把它插在「触发键」前面）。
    /// <para>
    /// ★ 点一下弹「新建宏」窗口，保存成功后让当前打开的配置页刷新列表。
    /// </para>
    /// </summary>
    public partial class AddMacroButton : UserControl
    {
        public AddMacroButton()
        {
            InitializeComponent();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new MacroEditWindow(null) { Owner = Window.GetWindow(this) };
            // ★ 带宿主的全局遮罩（没注入时就是普通的 ShowDialog）
            if (Utils.HostBridge.ShowModal(window) == true)
                MacroConfigPage.RefreshIfActive();
        }
    }
}
