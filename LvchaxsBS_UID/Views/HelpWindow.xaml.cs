using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.IO;

namespace LvchaxsBS_UID.Views
{
    public partial class HelpWindow : Window
    {
        private bool _isClosing = false;
        private static readonly string SETTINGS_FILE = "help_window_settings.json";

        /// <summary>
        /// 用户在帮助窗口里点了『关闭工具』。
        ///
        /// <para>
        /// 这里只记录意图，实际关闭由 Owner（账号管理主窗口）在
        /// <see cref="Window.ShowDialog"/> 返回之后处理。
        /// <b>不能在这里直接关掉 Owner</b>：本窗口是模态显示的，这段代码正跑在模态嵌套的
        /// DispatcherFrame 内部，此时关闭 Owner 会让模态循环无法正常退出，
        /// 整个 UI 线程会卡在消息泵里（表现为窗口点击无响应、只能结束进程）。
        /// </para>
        /// </summary>
        public bool CloseToolRequested { get; private set; }

        public HelpWindow()
        {
            InitializeComponent();
            // 独立窗口不会继承主界面的「窗口缩放」，自己跟一下（宿主注入的系数）
            Utils.UI.UiScale.ApplyTo(this);
            Loaded += HelpWindow_Loaded;

            // 初始化复选框状态
            InitializeCheckboxState();
        }

        private void InitializeCheckboxState()
        {
            try
            {
                // 读取保存的设置
                string settingsPath = GetSettingsFilePath();
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    if (bool.TryParse(json, out bool dontShow))
                    {
                        dontShowAgainCheckBox.IsChecked = dontShow;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取设置失败: {ex.Message}");
            }

            // 默认不勾选
            dontShowAgainCheckBox.IsChecked = false;
        }

        private void HelpWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 播放淡入动画
            var storyboard = (Storyboard)Resources["FadeInAnimation"];
            storyboard.Begin(mainBorder);
        }

        #region 按钮事件处理
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 右上角 X = 放弃：不提交『下次启动不再显示此窗口』的勾选
            CloseSelf();
        }

        private void CloseToolButton_Click(object sender, RoutedEventArgs e)
        {
            // 『关闭工具』= 退出账号管理，同样不算"我知道了"，因此不保存勾选设置。
            // 只登记意图并关掉自己，由 Owner 在 ShowDialog 返回后收尾（见 CloseToolRequested 注释）。
            CloseToolRequested = true;
            CloseSelf();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 只有『我知道了』才真正提交『下次启动不再显示此窗口』
            SaveUserPreference();
            CloseSelf();
        }
        #endregion

        #region 用户偏好管理
        /// <summary>
        /// 把『下次启动不再显示此窗口』写入磁盘。
        /// 只在用户点击『我知道了』时调用 —— 勾选框本身不再即时落盘，
        /// 避免用户只是勾了一下（随后用 X 或『关闭工具』离开、并未确认）也会被永久记住。
        /// </summary>
        private void SaveUserPreference()
        {
            try
            {
                // 保存到JSON文件
                bool dontShow = dontShowAgainCheckBox.IsChecked ?? false;
                string json = dontShow.ToString();
                string settingsPath = GetSettingsFilePath();
                File.WriteAllText(settingsPath, json);

                // 立即应用状态
                System.Diagnostics.Debug.WriteLine($"帮助窗口设置已保存: {dontShow}");
            }
            catch (Exception ex)
            {
                // 如果保存失败，静默处理，不打扰用户
                System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
            }
        }

        private string GetSettingsFilePath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appDataPath, "LvchaxsBS_UID");

            // 确保目录存在
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            return Path.Combine(appFolder, SETTINGS_FILE);
        }
        #endregion

        #region 超链接事件处理
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开链接
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region 窗口关闭
        /// <summary>
        /// 关闭帮助窗口。
        ///
        /// <para>
        /// <b>必须同步调用 <see cref="Window.Close"/>。</b>
        /// 实测（在宿主 LvchaxsBS 进程内）：如果把关闭动作交给异步回调执行 —— 不论是
        /// <c>Storyboard.Completed</c>、<c>DispatcherTimer</c> 还是 <c>Dispatcher.BeginInvoke</c> ——
        /// 窗口本身会正常关掉（<c>Closed</c> 也会触发），
        /// 但 <see cref="Window.ShowDialog"/> 内部的 DispatcherFrame 不会退出，
        /// UI 线程从此一直卡在模态消息循环里：表现为 LvchaxsBS 整个界面点击无响应、
        /// 系统不停发出提示音，只能用任务管理器结束进程。
        /// </para>
        /// <para>
        /// 因此这里放弃了原来的淡出动画（动画要等播完才能关，天然属于"异步关闭"），
        /// 改用最直接、已验证可靠的同步关闭。淡入动画不受影响，仍然保留。
        /// </para>
        /// </summary>
        private void CloseSelf()
        {
            if (_isClosing) return;
            _isClosing = true;

            Close();
        }
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}