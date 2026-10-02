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
            AutoLumberIntervalSlider.Value = s.AutoLumberInterval;
            DetectThresholdSpinBox.Value = s.DetectThreshold;
            _isLoading = false;

            SliderEntryAnimator.PlayAll(this);
        }

        private void AutoLumberIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoLumberSettings>();
            s.AutoLumberInterval = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoLumberSettings>();
            s.DetectThreshold = e.NewValue;
            ConfigManager.Save(s);
        }
    }
}