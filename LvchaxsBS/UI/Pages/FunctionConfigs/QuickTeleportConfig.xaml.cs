using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Core;
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

        private void OnDetectionResultUpdated(double matchScore, double elapsedMs, double threshold, string source, int count)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (matchScore >= 0)
                    RightCornerPanel.SetResult(matchScore, (long)elapsedMs, threshold, source);
                else
                    RightCornerPanel.SetEmpty();
            });
        }

        private void OnRightListDetectionResultUpdated(double matchScore, double elapsedMs, double threshold, string source, int count)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (matchScore >= 0)
                    RightListPanel.SetResult(matchScore, (long)elapsedMs, threshold, source);
                else
                    RightListPanel.SetEmpty();
            });
        }

        private void QuickTeleportConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<QuickTeleportSettings>();

            QuickTeleportDelayCard.Value = s.RightCornerDetectDelay_1;
            QuickTeleportDelayCard.SpinValue = s.DetectThreshold;
            RightListDelayCard.Value = s.RightListDetectDelay_1;
            RightListDelayCard.SpinValue = s.RightListThreshold;
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

            ConfigSync.Mutate<QuickTeleportSettings>(s => s.EnableRightClickCancel = EnableRightClickCancel.IsChecked == true);

            // 关掉开关时把可能残留的取消标志清掉，避免它继续挡着后续传送
            if (EnableRightClickCancel.IsChecked != true)
                QuickTeleportLogic.ClearTeleportCancelled();
        }
    }
}