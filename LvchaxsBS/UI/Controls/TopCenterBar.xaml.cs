using System;
using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Services;

namespace LvchaxsBS.UI.Controls
{
    public partial class TopLeftBar : UserControl
    {
        public event EventHandler? HomeClicked;
        public event EventHandler? SettingsClicked;
        public event EventHandler? PersonalizationClicked;
        public event EventHandler? VoiceClicked;
        public event EventHandler? OfficialServerClicked;
        public event EventHandler? InternationalServerClicked;

        private string _activePage = "";

        public TopLeftBar()
        {
            InitializeComponent();

            Loaded += (s, e) => ThemeService.ThemeChanged += ThemeService_ThemeChanged;
            Unloaded += (s, e) => ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
        }

        private void ThemeService_ThemeChanged(object? sender, AppTheme theme)
        {
            Dispatcher.Invoke(RefreshActiveVisual);
        }

        private void RefreshActiveVisual()
        {
            SetHomeActive(_activePage == "Home");
            SetSettingsActive(_activePage == "Settings");
            SetPersonalizationActive(_activePage == "Personalization");
            SetVoiceActive(_activePage == "Voice");
        }

        private void ButtonHome_Click(object sender, RoutedEventArgs e)
            => HomeClicked?.Invoke(this, EventArgs.Empty);

        private void ButtonSettings_Click(object sender, RoutedEventArgs e)
            => SettingsClicked?.Invoke(this, EventArgs.Empty);

        private void ButtonPersonalization_Click(object sender, RoutedEventArgs e)
            => PersonalizationClicked?.Invoke(this, EventArgs.Empty);

        private void ButtonVoice_Click(object sender, RoutedEventArgs e)
            => VoiceClicked?.Invoke(this, EventArgs.Empty);

        private void CenterButton1_Click(object sender, RoutedEventArgs e)
            => OfficialServerClicked?.Invoke(this, EventArgs.Empty);

        private void CenterButton2_Click(object sender, RoutedEventArgs e)
            => InternationalServerClicked?.Invoke(this, EventArgs.Empty);

        public void SetResolution(string text)
        {
            ButtonResolution.Content = text;
        }

        // ============ 当前页高亮 ============

        public void SetHomeActive(bool active)
        {
            if (active) _activePage = "Home";

            var iconBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                   : ThemeBrushProvider.TextSecondaryBrush();
            if (IconHome != null) IconHome.Stroke = iconBrush;

            if (ButtonHome != null)
            {
                ButtonHome.BorderBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                                : ThemeBrushProvider.BorderStrongBrush();
                ButtonHome.BorderThickness = new Thickness(1);
            }
        }

        public void SetSettingsActive(bool active)
        {
            if (active) _activePage = "Settings";

            var iconBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                   : ThemeBrushProvider.TextSecondaryBrush();
            if (IconSettingsOuter != null) IconSettingsOuter.Stroke = iconBrush;
            if (IconSettingsInner != null) IconSettingsInner.Stroke = iconBrush;

            if (ButtonSettings != null)
            {
                ButtonSettings.BorderBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                                    : ThemeBrushProvider.BorderStrongBrush();
                ButtonSettings.BorderThickness = new Thickness(1);
            }
        }

        public void SetPersonalizationActive(bool active)
        {
            if (active) _activePage = "Personalization";

            var iconBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                   : ThemeBrushProvider.TextSecondaryBrush();

            if (IconSliderLine1 != null) IconSliderLine1.Stroke = iconBrush;
            if (IconSliderLine2 != null) IconSliderLine2.Stroke = iconBrush;
            if (IconSliderLine3 != null) IconSliderLine3.Stroke = iconBrush;

            if (IconSliderKnob1 != null) IconSliderKnob1.Stroke = iconBrush;
            if (IconSliderKnob2 != null) IconSliderKnob2.Stroke = iconBrush;
            if (IconSliderKnob3 != null) IconSliderKnob3.Stroke = iconBrush;

            if (ButtonPersonalization != null)
            {
                ButtonPersonalization.BorderBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                                           : ThemeBrushProvider.BorderStrongBrush();
                ButtonPersonalization.BorderThickness = new Thickness(1);
            }
        }

        public void SetVoiceActive(bool active)
        {
            if (active) _activePage = "Voice";

            var iconBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                   : ThemeBrushProvider.TextSecondaryBrush();

            if (IconVoiceBubble != null) IconVoiceBubble.Stroke = iconBrush;
            if (IconVoiceLine1 != null) IconVoiceLine1.Stroke = iconBrush;
            if (IconVoiceLine2 != null) IconVoiceLine2.Stroke = iconBrush;

            if (ButtonVoice != null)
            {
                ButtonVoice.BorderBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                                 : ThemeBrushProvider.BorderStrongBrush();
                ButtonVoice.BorderThickness = new Thickness(1);
            }
        }
    }
}