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
            FishingAssistCard.Value = s.FishingAssistInterval;
            FishingAssistCard.SpinValue = s.DetectThreshold;
            TensionCard.Value = s.TensionInterval;
            TensionCard.SpinValue = s.TensionTolerance;
            JudgmentLineCard.Value = s.JudgmentLinePosition;
            _isLoading = false;

            SliderEntryAnimator.PlayAll(this);
        }

        private void FishingAssistIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<FishingAssistSettings>(s => s.FishingAssistInterval = (int)e.NewValue);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<FishingAssistSettings>(s => s.DetectThreshold = e.NewValue);
        }

        private void TensionIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<FishingAssistSettings>(s => s.TensionInterval = (int)e.NewValue);
        }

        private void TensionToleranceSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<FishingAssistSettings>(s => s.TensionTolerance = (int)e.NewValue);
        }

        private void JudgmentLineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<FishingAssistSettings>(s => s.JudgmentLinePosition = (int)e.NewValue);
        }
    }
}