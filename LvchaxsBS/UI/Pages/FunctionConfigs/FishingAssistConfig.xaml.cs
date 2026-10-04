using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Core;
using LvchaxsBS.UI.Controls;
using LvchaxsBS.UI.Helpers;
using TagState = LvchaxsBS.UI.Controls.TagState;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class FishingAssistConfig : Page
    {
        private bool _isLoading = true;
        private StatusTagPanel? _match;
        private StatusTagPanel? _tension;

        public FishingAssistConfig()
        {
            InitializeComponent();
            Loaded += FishingAssistConfig_Loaded;
            Unloaded += FishingAssistConfig_Unloaded;
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

            _match = FishingAssistCard.GetTitleTag(0);
            _tension = TensionCard.GetTitleTag(0);

            FishingAssistLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
            FishingAssistLogic.DetectionResultUpdated += OnDetectionResultUpdated;
            FishingAssistLogic.TensionResultUpdated -= OnTensionResultUpdated;
            FishingAssistLogic.TensionResultUpdated += OnTensionResultUpdated;
            _match?.SetEmpty();

            SliderEntryAnimator.PlayAll(this);
        }

        private void FishingAssistConfig_Unloaded(object sender, RoutedEventArgs e)
        {
            FishingAssistLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
            FishingAssistLogic.TensionResultUpdated -= OnTensionResultUpdated;
        }

        private void OnDetectionResultUpdated(double matchScore, long elapsedMs, double threshold, string status)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                _match?.SetResult(matchScore, elapsedMs, threshold, status));
        }

        private void OnTensionResultUpdated(double matchScore, long elapsedMs, double threshold, string direction)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_tension == null) return;

                bool isDetected = matchScore > 0;

                _tension.SetScoreRaw(
                    isDetected ? "张力区: 已发现" : "张力区: 未发现",
                    isDetected ? TagState.Good : TagState.Bad);

                _tension.SetTimeRaw(
                    elapsedMs >= 0 ? $"耗时: {elapsedMs}ms" : "耗时: --",
                    TagState.Good);

                _tension.SetSourceRaw(
                    isDetected && !string.IsNullOrEmpty(direction) ? $"类型: {direction}" : "类型: --",
                    isDetected ? TagState.Good : TagState.Bad);
            });
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
