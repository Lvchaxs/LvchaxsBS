using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class StoryDialogueConfig : Page
    {
        private bool _isLoading = true;

        public StoryDialogueConfig()
        {
            InitializeComponent();
            Loaded += StoryDialogueConfig_Loaded;
        }

        private void StoryDialogueConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<StoryDialogueSettings>();
            StoryDialogueIntervalSlider.Value = s.StoryDialogueInterval;
            DetectThresholdSpinBox.Value = s.DetectThreshold;
            FKeyIntervalSlider.Value = s.FKeyInterval;
            _isLoading = false;

            SliderEntryAnimator.PlayAll(this);
        }

        private void StoryDialogueIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<StoryDialogueSettings>();
            s.StoryDialogueInterval = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<StoryDialogueSettings>();
            s.DetectThreshold = e.NewValue;
            ConfigManager.Save(s);
        }

        private void FKeyIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<StoryDialogueSettings>();
            s.FKeyInterval = (int)e.NewValue;
            ConfigManager.Save(s);
        }
    }
}