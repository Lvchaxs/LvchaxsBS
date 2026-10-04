using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Services;
using LvchaxsBS.UI.Dialogs;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages
{
    public partial class SettingsPage : Page
    {
        private bool _isLoading = true;

        private static readonly string[] CaptureModes = { "BitBlt(hwnd)", "BitBlt(NULL)" };

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
            Unloaded += SettingsPage_Unloaded;
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingsUIService.UnregisterAll();
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<AppSettings>();

            // 截图方式
            CaptureModeSelect.ItemsSource = CaptureModes;
            CaptureModeSelect.SelectedIndex = (s.CaptureMode >= 0 && s.CaptureMode < CaptureModes.Length)
                ? s.CaptureMode : 0;
            CaptureModeSelect.SelectionChanged += CaptureModeSelect_Changed;

            // 界面检测间隔
            MainWindowCheckIntervalSlider.Value = s.MainWindowCheckInterval_1;
            MainWindowCheckIntervalSlider.ValueChanged += MainWindowCheckIntervalSlider_ValueChanged;

            // DPI
            DpiScaleSlider.Value = s.DpiScalePercent_1;
            DpiScaleSlider.ValueChanged += DpiScaleSlider_ValueChanged;

            // 版本
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"v{version}";

            // 游戏路径
            LoadGamePaths();

            // 卡片1：窗口检测（检测/游戏/焦点/坐标/标题）
            SettingsUIService.RegisterWindowInfoUI(
                ServiceTagText, ServiceTagBorder,
                GameTagText, GameTagBorder,
                FocusTagText, FocusTagBorder,
                TopLeftTagText, TopLeftTagBorder,
                TitleTagText, TitleTagBorder);

            // 卡片2：界面检测（检测/耗时/类型）
            SettingsUIService.RegisterMainWindowUI(
                DetectionTagText, DetectionTagBorder,
                ResultTagText, ResultTagBorder,
                ElapsedTagText, ElapsedTagBorder);

            _isLoading = false;

            // 入场动画
            SliderEntryAnimator.PlayAll(this);
        }

        // ============ 截图方式 ============
        private void CaptureModeSelect_Changed(object? sender, int idx)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AppSettings>(s => s.CaptureMode = idx);
        }

        // ============ 界面检测间隔 ============
        private void MainWindowCheckIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AppSettings>(s => s.MainWindowCheckInterval_1 = (int)e.NewValue);
        }

        // ============ DPI（只保存，重启生效） ============
        private void DpiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AppSettings>(s => s.DpiScalePercent_1 = (int)e.NewValue);
        }

        // ============ 游戏路径 ============

        private void LoadGamePaths()
        {
            var s = ConfigManager.Get<AppSettings>();

            OfficialServerPathText.Text = string.IsNullOrEmpty(s.OfficialServerPath)
                ? "未设置路径"
                : s.OfficialServerPath;

            InternationalServerPathText.Text = string.IsNullOrEmpty(s.InternationalServerPath)
                ? "未设置路径"
                : s.InternationalServerPath;
        }

        // ============ 版本与更新 ============

        private async void LatestVersionBtn_Click(object sender, RoutedEventArgs e)
            => await DownloadAndInstallAsync("正式版本", UpdateService.RELEASE_BASE_URL, showOptionCheckBox: true);

        private async void TestVersionBtn_Click(object sender, RoutedEventArgs e)
            => await DownloadAndInstallAsync("测试版本", UpdateService.BETA_BASE_URL, showOptionCheckBox: false);

        /// <summary>
        /// 重置配置：把 Config 目录整体删除并重启，程序会按默认值重新生成配置文件。
        /// 只影响配置，语音/壁纸/截图日志等数据不受影响。
        /// </summary>
        private async void ResetConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ConfirmDialog(
                "确认重置配置",
                "确定要重置全部配置吗？\n\n将删除 Config 目录下的配置文件并重启程序，所有设置会恢复为默认值。\n\n（语音、壁纸、截图日志等数据不受影响）",
                "确定", "取消")
            {
                Owner = Window.GetWindow(this)
            };
            dialog.SetOptionCheckBox(visible: false);

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                ToastService.Show("重置配置", "即将重置并重启...");
                await ResetService.PrepareAndLaunchAsync();
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                ToastService.Show("重置失败", ex.Message, false);
                System.Diagnostics.Debug.WriteLine($"重置配置失败：{ex}");
            }
        }

        /// <summary>通用下载安装流程。</summary>
        private async Task DownloadAndInstallAsync(string channelName, string baseUrl, bool showOptionCheckBox)
        {
            var dialog = new ConfirmDialog(
                "确认更新",
                $"确定要下载并安装【{channelName}】吗？\n\n程序将自动关闭，稍等几秒会自动重启，请勿手动结束进程。",
                "确定", "取消")
            {
                Owner = Window.GetWindow(this)
            };

            if (showOptionCheckBox)
            {
                dialog.SetOptionCheckBox(
                    visible: true,
                    content: "新版本自动更新",
                    isChecked: ConfigManager.Get<AppSettings>().StartUpdateAfterDownload);

                dialog.OptionCheckedChanged += isChecked =>
                    ConfigSync.Mutate<AppSettings>(s => s.StartUpdateAfterDownload = isChecked);
            }
            else
            {
                dialog.SetOptionCheckBox(visible: false);
            }

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                ToastService.Show("更新", $"即将下载{channelName}并重启...");
                await UpdateService.PrepareAndLaunchAsync(baseUrl);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                ToastService.Show("更新失败", ex.Message, false);
                System.Diagnostics.Debug.WriteLine($"更新失败：{ex}");
            }
        }

        // ============ 超链接跳转到浏览器 ============
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"打开链接失败：{ex.Message}");
            }
        }
    }
}