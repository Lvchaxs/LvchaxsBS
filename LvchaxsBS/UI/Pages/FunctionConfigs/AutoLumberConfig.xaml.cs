using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Core;
using LvchaxsBS.UI.Controls;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class AutoLumberConfig : Page
    {
        private bool _isLoading = true;
        private StatusTagPanel? _match;

        public AutoLumberConfig()
        {
            InitializeComponent();
            Loaded += AutoLumberConfig_Loaded;
            Unloaded += AutoLumberConfig_Unloaded;
        }

        private void AutoLumberConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<AutoLumberSettings>();
            AutoLumberCard.Value = s.AutoLumberInterval;
            AutoLumberCard.SpinValue = s.DetectThreshold;
            _isLoading = false;

            // 运行状态标签在 SlideCardControl 的独立名称作用域里，按顺序取
            _match = AutoLumberCard.GetTitleTag(0);

            AutoLumberLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
            AutoLumberLogic.DetectionResultUpdated += OnDetectionResultUpdated;
            _match?.SetEmpty();

            SliderEntryAnimator.PlayAll(this);
        }

        private void AutoLumberConfig_Unloaded(object sender, RoutedEventArgs e)
        {
            AutoLumberLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
        }

        private void OnDetectionResultUpdated(double matchScore, long elapsedMs, double threshold, string type)
        {
            Application.Current?.Dispatcher.Invoke(() =>
                _match?.SetResult(matchScore, elapsedMs, threshold, type));
        }

        private void AutoLumberIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoLumberSettings>(s => s.AutoLumberInterval = (int)e.NewValue);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoLumberSettings>(s => s.DetectThreshold = e.NewValue);
        }
    }
}
