using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class ControllerPickupConfig : Page
    {
        private bool _isLoading = true;

        public ControllerPickupConfig()
        {
            InitializeComponent();
            Loaded += ControllerPickupConfig_Loaded;
        }

        private void ControllerPickupConfig_Loaded(object sender, RoutedEventArgs e)
        {
            TriggerDelaySlider.Value = ConfigManager.Get<ControllerPickupSettings>().TriggerDelay;
            _isLoading = false;

            SliderEntryAnimator.PlayAll(this);
        }

        private void TriggerDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<ControllerPickupSettings>();
            s.TriggerDelay = (int)e.NewValue;
            ConfigManager.Save(s);
        }
    }
}