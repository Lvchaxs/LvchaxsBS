using LvchaxsBS.Config;
using LvchaxsBS.Services;
using LvchaxsBS.UI.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;

namespace LvchaxsBS.UI.Pages
{
    public partial class PersonalizationPage : Page
    {
        private bool _isLoading = true;

        public PersonalizationPage()
        {
            InitializeComponent();
            Loaded += PersonalizationPage_Loaded;

            // 卡片1
            WallpaperOpacitySlider.ValueChanged += WallpaperOpacitySlider_ValueChanged;
            WallpaperBlurSlider.ValueChanged += WallpaperBlurSlider_ValueChanged;
            CardOpacitySlider.ValueChanged += CardOpacitySlider_ValueChanged;
            EnableCompressionCheckBox.Checked += EnableCompressionCheckBox_Changed;
            EnableCompressionCheckBox.Unchecked += EnableCompressionCheckBox_Changed;
            EnableMirrorCheckBox.Checked += EnableMirrorCheckBox_Changed;
            EnableMirrorCheckBox.Unchecked += EnableMirrorCheckBox_Changed;

            // 卡片2
            XPositionSlider.ValueChanged += XPositionSlider_ValueChanged;
            WallpaperScaleSlider.ValueChanged += WallpaperScaleSlider_ValueChanged;
            YPositionSlider.ValueChanged += YPositionSlider_ValueChanged;
            WallpaperRotationSlider.ValueChanged += WallpaperRotationSlider_ValueChanged;

            // 卡片3
            TitleFontSizeSlider.ValueChanged += TitleFontSizeSlider_ValueChanged;
            TitleOpacitySlider.ValueChanged += TitleOpacitySlider_ValueChanged;
            WindowTitleTextBox.LostFocus += WindowTitleTextBox_LostFocus;
            WindowOpacitySlider.ValueChanged += WindowOpacitySlider_ValueChanged;
        }

        private void PersonalizationPage_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<PersonalizationSettings>();

            WallpaperOpacitySlider.Value = s.WallpaperOpacity;
            WallpaperBlurSlider.Value = s.WallpaperBlur;
            CardOpacitySlider.Value = s.CardOpacity;
            EnableCompressionCheckBox.IsChecked = s.EnableCompression;
            EnableMirrorCheckBox.IsChecked = s.EnableMirror;

            XPositionSlider.Value = s.XPosition;
            WallpaperScaleSlider.Value = s.WallpaperScale;
            YPositionSlider.Value = s.YPosition;
            WallpaperRotationSlider.Value = s.WallpaperRotation;

            TitleFontSizeSlider.Value = s.TitleFontSize;
            TitleOpacitySlider.Value = s.TitleOpacity;
            WindowTitleTextBox.Text = s.WindowTitle;
            WindowOpacitySlider.Value = s.WindowOpacity;

            _isLoading = false;

            // 入场动画
            SliderEntryAnimator.PlayAll(this);
        }

        // ============ 卡片1 ============
        private void WallpaperOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperOpacity = e.NewValue);
        }

        private void WallpaperBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperBlur = e.NewValue);
        }

        private void CardOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.CardOpacity = e.NewValue);
        }

        private void EnableCompressionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.EnableCompression = EnableCompressionCheckBox.IsChecked == true);
        }

        private void EnableMirrorCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.EnableMirror = EnableMirrorCheckBox.IsChecked == true);
        }

        // ============ 卡片2 ============
        private void XPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.XPosition = e.NewValue);
        }

        private void WallpaperScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperScale = e.NewValue);
        }

        private void YPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.YPosition = e.NewValue);
        }

        private void WallpaperRotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperRotation = e.NewValue);
        }

        // ============ 卡片3 ============
        private void TitleFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.TitleFontSize = e.NewValue);
        }

        private void TitleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.TitleOpacity = e.NewValue);
        }

        private void WindowTitleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = ConfigSync.Mutate<PersonalizationSettings>(x => x.WindowTitle = WindowTitleTextBox.Text);

            AppearanceService.NotifyWindowTitleChanged(s.WindowTitle);
        }

        private void WindowOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WindowOpacity = e.NewValue);
        }
    }
}