using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class AutoLumberConfig : Page
    {
        private bool _isLoading = true;

        public AutoLumberConfig()
        {
            InitializeComponent();
            Loaded += AutoLumberConfig_Loaded;
        }

        private void AutoLumberConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<AutoLumberSettings>();
            AutoLumberCard.Value = s.AutoLumberInterval;
            AutoLumberCard.SpinValue = s.DetectThreshold;
            _isLoading = false;

            SliderEntryAnimator.PlayAll(this);
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