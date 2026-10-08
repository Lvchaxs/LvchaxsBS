using System;
using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Core;
using LvchaxsBS.Services;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class QuickTeleportConfig : Page
    {
        private bool _isLoading = true;

        public QuickTeleportConfig()
        {
            InitializeComponent();
            Loaded += QuickTeleportConfig_Loaded;
            Unloaded += QuickTeleportConfig_Unloaded;
        }

        private void QuickTeleportConfig_Unloaded(object sender, RoutedEventArgs e)
        {
            QuickTeleportLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
            QuickTeleportLogic.RightListDetectionResultUpdated -= OnRightListDetectionResultUpdated;
        }

        // ============ 运行状态标签 ============

        // SetResult 内部已按情况分流：
        //   分数 < 0（真没跑）→ 整行 "--"；没识别出类型 → 显示匹配度/耗时/次数，只把类型留 "--"
        private void OnDetectionResultUpdated(double matchScore, double elapsedMs, double threshold, string source, int count)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                RightCornerPanel.SetResult(matchScore, (long)elapsedMs, threshold, source, count));
        }

        private void OnRightListDetectionResultUpdated(double matchScore, double elapsedMs, double threshold, string source, int count)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                RightListPanel.SetResult(matchScore, (long)elapsedMs, threshold, source, count));
        }

        private void QuickTeleportConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<QuickTeleportSettings>();

            QuickTeleportDelayCard.Value = s.RightCornerDetectDelay_1;
            QuickTeleportDelayCard.SpinValue = s.DetectThreshold;
            RightListDelayCard.Value = s.RightListDetectDelay_1;
            RightListDelayCard.SpinValue = s.RightListThreshold;
            RightListDelayCard.Spin2Value = Math.Clamp(s.RightListScaleFactor, 1, 10);
            RightListClickDelayCard.Value = s.RightListClickItemDelay_1;
            RightListFKeyDelaySlider.Value = s.RightListFKeyDelay;
            RightListAbyssFKeyDelaySlider.Value = s.RightListAbyssFKeyDelay;

            EnableListRecognition.IsChecked = s.EnableListRecognition;
            DisableAbyssFilter.IsChecked = s.DisableAbyssFilter;
            EnableRightClickCancel.IsChecked = s.EnableRightClickCancel;

            _isLoading = false;

            QuickTeleportLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
            QuickTeleportLogic.DetectionResultUpdated += OnDetectionResultUpdated;
            QuickTeleportLogic.RightListDetectionResultUpdated -= OnRightListDetectionResultUpdated;
            QuickTeleportLogic.RightListDetectionResultUpdated += OnRightListDetectionResultUpdated;
            RightCornerPanel.SetEmpty();
            RightListPanel.SetEmpty();

            SliderEntryAnimator.PlayAll(this);
        }

        private void QuickTeleportDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.RightCornerDetectDelay_1 = (int)e.NewValue);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.DetectThreshold = e.NewValue);
        }

        private void RightListDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.RightListDetectDelay_1 = (int)e.NewValue);
        }

        private void RightListThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.RightListThreshold = e.NewValue);
        }

        private void RightListScaleFactorSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;

            // 只允许 1-10 的整数：1 = 关闭粗匹配
            int factor = (int)Math.Round(e.NewValue);
            if (factor < 1) factor = 1;
            if (factor > 10) factor = 10;

            ConfigSync.Mutate<QuickTeleportSettings>(s => s.RightListScaleFactor = factor);
        }

        private void RightListClickDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.RightListClickItemDelay_1 = (int)e.NewValue);
        }

        private void RightListFKeyDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.RightListFKeyDelay = (int)e.NewValue);
        }

        private void RightListAbyssFKeyDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.RightListAbyssFKeyDelay = (int)e.NewValue);
        }

        private void EnableListRecognition_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.EnableListRecognition = EnableListRecognition.IsChecked == true);
        }

        private void DisableAbyssFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.DisableAbyssFilter = DisableAbyssFilter.IsChecked == true);
        }

        private void EnableRightClickCancel_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            bool enabled = EnableRightClickCancel.IsChecked == true;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.EnableRightClickCancel = enabled);

            if (enabled)
            {
                // 打开开关后右键被独占为"取消本次传送"，不能再当触发键，需要纠正旧的右键配置
                ResetTriggerKeyIfRightButton();
            }
            else
            {
                // 关掉开关时把可能残留的取消标志清掉，避免它继续挡着后续传送
                QuickTeleportLogic.ClearTeleportCancelled();
            }
        }

        /// <summary>
        /// 勾选开关后右键被独占为"取消本次传送"，不能再当触发键。
        /// 如果之前（未勾选时）已经把触发键设成了右键，这里自动改回默认值（左键），
        /// 避免"设了却用不了"。
        /// </summary>
        private void ResetTriggerKeyIfRightButton()
        {
            if (!string.Equals(ConfigManager.Get<HomePageSettings>().QuickTeleportKey,
                               "右键", StringComparison.OrdinalIgnoreCase))
                return;

            string defaultKey = new HomePageSettings().QuickTeleportKey;   // 配置类里的默认值：左键
            ConfigSync.Mutate<HomePageSettings>(s => s.QuickTeleportKey = defaultKey);

            ToastService.Show("快速传送", $"右键已被取消传送功能占用，触发键已改回「{defaultKey}」");
        }
    }
}