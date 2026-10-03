using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
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

            DisableListRecognition.IsChecked = s.DisableListRecognition;
            DisableAbyssFilter.IsChecked = s.DisableAbyssFilter;
            SaveScreenshotLog.IsChecked = s.SaveScreenshotLog;

            _isLoading = false;

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

        private void DisableListRecognition_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.DisableListRecognition = DisableListRecognition.IsChecked == true);
        }

        private void DisableAbyssFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.DisableAbyssFilter = DisableAbyssFilter.IsChecked == true);
        }

        private void SaveScreenshotLog_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickTeleportSettings>(s => s.SaveScreenshotLog = SaveScreenshotLog.IsChecked == true);
        }
    }
}