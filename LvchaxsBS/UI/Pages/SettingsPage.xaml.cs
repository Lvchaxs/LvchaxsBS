using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
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

            _isLoading = false;

            // 入场动画
            SliderEntryAnimator.PlayAll(this);
        }

        // ============ 截图方式 ============
        private void CaptureModeSelect_Changed(object? sender, int idx)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AppSettings>();
            s.CaptureMode = idx;
            ConfigManager.Save(s);
        }

        // ============ 界面检测间隔 ============
        private void MainWindowCheckIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AppSettings>();
            s.MainWindowCheckInterval_1 = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        // ============ DPI（只保存，重启生效） ============
        private void DpiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AppSettings>();
            s.DpiScalePercent_1 = (int)e.NewValue;
            ConfigManager.Save(s);
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