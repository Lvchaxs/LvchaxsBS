using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Core;
using LvchaxsBS.UI.Controls;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class StoryDialogueConfig : Page
    {
        private bool _isLoading = true;
        private StatusTagPanel? _match;

        public StoryDialogueConfig()
        {
            InitializeComponent();
            Loaded += StoryDialogueConfig_Loaded;
            Unloaded += StoryDialogueConfig_Unloaded;
        }

        private void StoryDialogueConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<StoryDialogueSettings>();
            StoryDialogueCard.Value = s.StoryDialogueInterval;
            StoryDialogueCard.SpinValue = s.DetectThreshold;
            FKeyIntervalCard.Value = s.FKeyInterval;
            _isLoading = false;

            _match = StoryDialogueCard.GetTitleTag(0);

            StoryDialogueLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
            StoryDialogueLogic.DetectionResultUpdated += OnDetectionResultUpdated;
            _match?.SetEmpty();

            SliderEntryAnimator.PlayAll(this);
        }

        private void StoryDialogueConfig_Unloaded(object sender, RoutedEventArgs e)
        {
            StoryDialogueLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
        }

        private void OnDetectionResultUpdated(double matchScore, long elapsedMs, double threshold, string type)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                _match?.SetResult(matchScore, elapsedMs, threshold, type));
        }

        private void StoryDialogueIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<StoryDialogueSettings>(s => s.StoryDialogueInterval = (int)e.NewValue);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<StoryDialogueSettings>(s => s.DetectThreshold = e.NewValue);
        }

        private void FKeyIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<StoryDialogueSettings>(s => s.FKeyInterval = (int)e.NewValue);
        }
    }
}
