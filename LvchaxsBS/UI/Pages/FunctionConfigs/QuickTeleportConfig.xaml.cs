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

            QuickTeleportDelaySlider.Value = s.RightCornerDetectDelay_1;
            DetectThresholdSpinBox.Value = s.DetectThreshold;
            RightListDelaySlider.Value = s.RightListDetectDelay_1;
            RightListThresholdSpinBox.Value = s.RightListThreshold;
            RightListClickDelaySlider.Value = s.RightListClickItemDelay_1;
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
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.RightCornerDetectDelay_1 = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.DetectThreshold = e.NewValue;
            ConfigManager.Save(s);
        }

        private void RightListDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.RightListDetectDelay_1 = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void RightListThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.RightListThreshold = e.NewValue;
            ConfigManager.Save(s);
        }

        private void RightListClickDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.RightListClickItemDelay_1 = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void RightListFKeyDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.RightListFKeyDelay = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void RightListAbyssFKeyDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.RightListAbyssFKeyDelay = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void DisableListRecognition_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.DisableListRecognition = DisableListRecognition.IsChecked == true;
            ConfigManager.Save(s);
        }

        private void DisableAbyssFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.DisableAbyssFilter = DisableAbyssFilter.IsChecked == true;
            ConfigManager.Save(s);
        }

        private void SaveScreenshotLog_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<QuickTeleportSettings>();
            s.SaveScreenshotLog = SaveScreenshotLog.IsChecked == true;
            ConfigManager.Save(s);
        }
    }
}