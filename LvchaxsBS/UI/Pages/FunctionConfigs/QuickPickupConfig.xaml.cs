using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class QuickPickupConfig : Page
    {
        private bool _isLoading = true;

        public QuickPickupConfig()
        {
            InitializeComponent();
            Loaded += QuickPickupConfig_Loaded;
        }

        private void QuickPickupConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<QuickPickupSettings>();
            PickupIntervalCard.Value = s.PickupInterval;
            FAndScrollDelayCard.Value = s.FAndScrollDelay;
            _isLoading = false;

            SliderEntryAnimator.PlayAll(this);
        }

        private void PickupIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickPickupSettings>(s => s.PickupInterval = (int)e.NewValue);
        }

        private void FAndScrollDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<QuickPickupSettings>(s => s.FAndScrollDelay = (int)e.NewValue);
        }
    }
}