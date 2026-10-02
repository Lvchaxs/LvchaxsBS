using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class FishingAssistConfig : Page
    {
        private bool _isLoading = true;

        public FishingAssistConfig()
        {
            InitializeComponent();
            Loaded += FishingAssistConfig_Loaded;
        }

        private void FishingAssistConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<FishingAssistSettings>();
            FishingAssistIntervalSlider.Value = s.FishingAssistInterval;
            DetectThresholdSpinBox.Value = s.DetectThreshold;
            TensionIntervalSlider.Value = s.TensionInterval;
            TensionToleranceSpinBox.Value = s.TensionTolerance;
            JudgmentLineSlider.Value = s.JudgmentLinePosition;
            _isLoading = false;

            SliderEntryAnimator.PlayAll(this);
        }

        private void FishingAssistIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<FishingAssistSettings>();
            s.FishingAssistInterval = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<FishingAssistSettings>();
            s.DetectThreshold = e.NewValue;
            ConfigManager.Save(s);
        }

        private void TensionIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<FishingAssistSettings>();
            s.TensionInterval = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void TensionToleranceSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<FishingAssistSettings>();
            s.TensionTolerance = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void JudgmentLineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<FishingAssistSettings>();
            s.JudgmentLinePosition = (int)e.NewValue;
            ConfigManager.Save(s);
        }
    }
}