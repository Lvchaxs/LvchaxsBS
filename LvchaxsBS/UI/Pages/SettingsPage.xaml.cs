using System;
using System.Diagnostics;
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

        private void RefreshOfficialPathBtn_Click(object sender, RoutedEventArgs e)
            => RefreshGamePath(official: true);

        private void RefreshInternationalPathBtn_Click(object sender, RoutedEventArgs e)
            => RefreshGamePath(official: false);

        /// <summary>
        /// 刷新游戏启动路径：按进程名找到"当前正在运行"的客户端，取其可执行文件路径并保存。
        /// 官服 = YuanShen.exe，国际服 = GenshinImpact.exe（与 WindowFocusService 的目标进程名保持一致）。
        /// 没启动 / 取不到路径 → 提示未启动；路径与已保存的相同 → 提示已是最新。
        /// </summary>
        private void RefreshGamePath(bool official)
        {
            string label = official ? "官服" : "国际服";
            string processName = official
                ? Services.Hooks.WindowFocusService.TargetProcessName2   // YuanShen
                : Services.Hooks.WindowFocusService.TargetProcessName1;   // GenshinImpact

            string? exePath = null;

            try
            {
                exePath = FindRunningProcessPath(processName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【{label}】读取进程路径失败：{ex}");
            }

            if (string.IsNullOrEmpty(exePath))
            {
                ToastService.Show($"{label} 未启动无法更新启动路径", false);
                return;
            }

            string saved = official
                ? ConfigManager.Get<AppSettings>().OfficialServerPath
                : ConfigManager.Get<AppSettings>().InternationalServerPath;

            if (string.Equals(saved, exePath, StringComparison.OrdinalIgnoreCase))
            {
                ToastService.Show($"{label} 启动路径已是最新无需更新", true);
                return;
            }

            ConfigSync.Mutate<AppSettings>(s =>
            {
                if (official) s.OfficialServerPath = exePath;
                else s.InternationalServerPath = exePath;
            });

            LoadGamePaths();
            ToastService.Show($"{label} 启动路径已更新为：{exePath}", true);
        }

        /// <summary>
        /// 取指定进程名（不含 .exe）当前运行实例的可执行文件全路径；没在跑或取不到则返回 null。
        /// 优先用 Process.MainModule；失败（权限不足 / 跨位数）时退回 OpenProcess + QueryFullProcessImageName，
        /// 后者只需要 PROCESS_QUERY_LIMITED_INFORMATION，对管理员启动的客户端也能拿到路径。
        /// </summary>
        private static string? FindRunningProcessPath(string processName)
        {
            var processes = Process.GetProcessesByName(processName);

            try
            {
                foreach (var p in processes)
                {
                    try
                    {
                        string? path = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                    catch
                    {
                        // 落到下面的 Win32 兜底
                    }

                    string? fallback = QueryProcessImagePath(p.Id);
                    if (!string.IsNullOrEmpty(fallback)) return fallback;
                }

                return null;
            }
            finally
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags,
            System.Text.StringBuilder exeName, ref int size);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static string? QueryProcessImagePath(int pid)
        {
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return null;

            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
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
        private void ResetConfigBtn_Click(object sender, RoutedEventArgs e)
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
                ToastService.Show("重置配置", "正在重置并重启...");
                // 内部：删 Config → 拉起新实例 → 结束当前进程（正常路径下不会返回）
                ResetService.ResetAndRestart();
            }
            catch (Exception ex)
            {
                // 删除/重启失败：恢复写盘，程序继续可用
                ConfigManager.SuppressPersist = false;
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

        // ============ 开源许可 ============

        /// <summary>
        /// 「关于项目」卡片里的「开源许可」入口：弹出 GPL-3.0 的版权 / 无担保 / 许可证说明。
        /// 对应 GPL-3.0 第 0 条要求的界面「Appropriate Legal Notices」。
        /// </summary>
        private void LicenseLink_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LicenseDialog
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
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