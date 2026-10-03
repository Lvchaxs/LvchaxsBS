using LvchaxsBS.Config;
using LvchaxsBS.Services;
using LvchaxsBS.UI.Helpers;
using Microsoft.Win32;
using System;
using System.IO;
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
            EnableWallpaperCheckBox.Checked += EnableWallpaperCheckBox_Changed;
            EnableWallpaperCheckBox.Unchecked += EnableWallpaperCheckBox_Changed;
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
            EnableWallpaperCheckBox.IsChecked = s.EnableWallpaper;
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

            // 按配置恢复壁纸
            ApplyWallpaperFromConfig();

            // 入场动画
            SliderEntryAnimator.PlayAll(this);
        }

        // ============ 壁纸应用 ============

        /// <summary>
        /// 把当前配置应用到窗口壁纸图层。
        /// 透明度 / 模糊是图层上的实时属性，直接生效（即时跟手）；
        /// 缩放 / 旋转 / 位移传 <see cref="WallpaperTransition.Smooth"/> 让它们带 200ms 过渡而不是瞬移；
        /// 启用 / 更换壁纸传 <see cref="WallpaperTransition.FadeIn"/> 走淡入。
        /// </summary>
        private void ApplyWallpaperFromConfig(WallpaperTransition transition = WallpaperTransition.Instant)
        {
            if (Application.Current?.MainWindow is UI.MainWindow mw)
                mw.ApplyWallpaper(transition);
        }

        // ============ 卡片1 ============
        private void WallpaperOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperOpacity = e.NewValue);
            ApplyWallpaperFromConfig();
        }

        private void WallpaperBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperBlur = e.NewValue);

            // 模糊是图层上的实时 Effect，直接改半径，和透明度一样即时跟手
            ApplyWallpaperFromConfig();
        }

        private void CardOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.CardOpacity = e.NewValue);

            // 覆盖主题背景画刷的 alpha，作用于所有卡片、窗口标题栏和相关按钮背景
            AppearanceService.ApplyControlOpacity(e.NewValue);
        }

        private void EnableWallpaperCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.EnableWallpaper = EnableWallpaperCheckBox.IsChecked == true);
            ApplyWallpaperFromConfig(WallpaperTransition.FadeIn);
        }

        private void EnableMirrorCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.EnableMirror = EnableMirrorCheckBox.IsChecked == true);

            // 镜像通过 ScaleX 取负实现，带过渡更自然
            ApplyWallpaperFromConfig(WallpaperTransition.Smooth);
        }

        private void SelectBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperPath = dlg.FileName);

            ApplyWallpaperFromConfig(WallpaperTransition.FadeIn);

            if (Application.Current?.MainWindow is UI.MainWindow mw)
                mw.ShowToast($"已设置背景：{Path.GetFileName(dlg.FileName)}", true);
        }

        // ============ 卡片2 ============
        private void XPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.XPosition = e.NewValue);
            ApplyWallpaperFromConfig(WallpaperTransition.Smooth);
        }

        private void WallpaperScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperScale = e.NewValue);
            ApplyWallpaperFromConfig(WallpaperTransition.Smooth);
        }

        private void YPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.YPosition = e.NewValue);
            ApplyWallpaperFromConfig(WallpaperTransition.Smooth);
        }

        private void WallpaperRotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.WallpaperRotation = e.NewValue);
            ApplyWallpaperFromConfig(WallpaperTransition.Smooth);
        }

        // ============ 卡片3 ============
        private void TitleFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.TitleFontSize = e.NewValue);

            // 作用于标题栏下面那行签名文字
            if (Application.Current?.MainWindow is UI.MainWindow mw)
                mw.SetSignatureFontSize(e.NewValue);
        }

        private void TitleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<PersonalizationSettings>(s => s.TitleOpacity = e.NewValue);

            // 这个滑块是窗口四角圆角（0~20）
            if (Application.Current?.MainWindow is UI.MainWindow mw)
                mw.SetWindowCornerRadius(e.NewValue);
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

            // 覆盖窗口底色画刷的 alpha（只影响 RootBorder 背景，不动卡片和文字）
            AppearanceService.ApplyWindowOpacity(e.NewValue);
        }
    }
}