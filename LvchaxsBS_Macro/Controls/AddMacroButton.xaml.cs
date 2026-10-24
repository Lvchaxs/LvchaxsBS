using System.Windows;
using System.Windows.Controls;
using LvchaxsBS_Macro.Views;

namespace LvchaxsBS_Macro.Controls
{
    /// <summary>
    /// 配置容器页底部按钮条的第一个按钮（宿主把它插在「触发键」前面）。
    /// <para>
    /// ★★ **一个按钮两种形态**，跟着配置页当前视图自动切：
    /// <list type="bullet">
    ///   <item>顶层（文件夹列表）→ 文件夹图标 + 「添加文件夹」</item>
    ///   <item>分组内（宏列表）  → <c>&lt;/&gt;</c> 图标 + 「添加宏」</item>
    /// </list>
    /// </para>
    /// <para>
    /// ★ 形态由 <see cref="MacroConfigPage.IsInsideFolderView"/> 决定；
    /// 页面切换视图时通过静态事件 <see cref="MacroConfigPage.ViewChanged"/> 通知这里刷新。
    /// </para>
    /// </summary>
    public partial class AddMacroButton : UserControl
    {
        public AddMacroButton()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                // 先退订再加，避免同一实例被重复订阅（宿主重建按钮条时会 new 一个新的）
                MacroConfigPage.ViewChanged -= OnViewChanged;
                MacroConfigPage.ViewChanged += OnViewChanged;
                UpdateAppearance();
            };

            Unloaded += (_, _) => MacroConfigPage.ViewChanged -= OnViewChanged;
        }

        private void OnViewChanged() => UpdateAppearance();

        /// <summary>固定成「新建宏」（v2 起没有文件夹了）。</summary>
        private void UpdateAppearance()
        {
            FolderIcon.Visibility = Visibility.Collapsed;
            MacroIcon.Visibility = Visibility.Visible;

            AddButton.MinWidth = 84;
            LabelText.Text = "新建宏";
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new MacroEditWindow(null, "")
            {
                Owner = Window.GetWindow(this),
            };

            // ★ 带宿主的全局遮罩（没注入时就是普通的 ShowDialog）
            if (Utils.HostBridge.ShowModal(window) == true)
                MacroConfigPage.RefreshIfActive();
        }    }
}
