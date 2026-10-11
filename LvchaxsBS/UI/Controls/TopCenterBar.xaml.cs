using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
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

        /// <summary>UID 账号管理按钮点击（模块未加载时该按钮隐藏，不会触发）。</summary>
        public event EventHandler? UidClicked;

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

        private void ButtonUid_Click(object sender, RoutedEventArgs e)
            => UidClicked?.Invoke(this, EventArgs.Empty);

        private void CenterButton1_Click(object sender, RoutedEventArgs e)
            => OfficialServerClicked?.Invoke(this, EventArgs.Empty);

        private void CenterButton2_Click(object sender, RoutedEventArgs e)
            => InternationalServerClicked?.Invoke(this, EventArgs.Empty);

        public void SetResolution(string text)
        {
            ButtonResolution.Content = text;
        }

        /// <summary>
        /// 控制"官服 / 国际服"启动按钮显隐（游戏未启动时显示，已启动时隐藏）。
        /// </summary>
        public void SetGameLaunchVisible(bool visible)
        {
            var v = visible ? Visibility.Visible : Visibility.Collapsed;
            if (CenterButton1 != null) CenterButton1.Visibility = v;
            if (CenterButton2 != null) CenterButton2.Visibility = v;
        }

        /// <summary>
        /// 控制「UID」按钮显隐。只有 exe 同目录存在可用的 LvchaxsBS_uid.dll 时才显示。
        /// </summary>
        public void SetUidVisible(bool visible)
        {
            if (ButtonUid != null)
                ButtonUid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// UID 页面是否为当前页（和「首页 / 设置」等导航项一样高亮）。
        /// </summary>
        public void SetUidActive(bool active)
        {
            var brush = active ? ThemeBrushProvider.PrimaryBrush()
                               : ThemeBrushProvider.TextSecondaryBrush();

            if (UidText != null) UidText.Foreground = brush;

            if (ButtonUid != null)
            {
                ButtonUid.BorderBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                               : ThemeBrushProvider.BorderStrongBrush();
                ButtonUid.BorderThickness = new Thickness(1);
            }
        }

        // ============ 当前页高亮 ============

        public void SetHomeActive(bool active) => SetNavActive(active, "Home", ButtonHome, IconHome);

        public void SetSettingsActive(bool active)
            => SetNavActive(active, "Settings", ButtonSettings, IconSettingsOuter, IconSettingsInner);

        public void SetPersonalizationActive(bool active)
            => SetNavActive(active, "Personalization", ButtonPersonalization,
                IconSliderLine1, IconSliderLine2, IconSliderLine3,
                IconSliderKnob1, IconSliderKnob2, IconSliderKnob3);

        public void SetVoiceActive(bool active)
            => SetNavActive(active, "Voice", ButtonVoice,
                IconVoiceBubble, IconVoiceLine1, IconVoiceLine2);

        /// <summary>
        /// 导航项高亮的唯一实现：图标描边 + 按钮边框一起切到"当前页"配色。
        /// 四个入口以前各自抄了一遍同样的十来行，颜色/线宽一旦只改一处就会不一致。
        /// </summary>
        /// <param name="active">是否为当前页</param>
        /// <param name="page">页面标识（只在 active 时写回，供切主题后重刷用）</param>
        /// <param name="button">导航按钮</param>
        /// <param name="icons">该按钮里所有需要跟着变色的图标元素</param>
        private void SetNavActive(bool active, string page, Button button, params Shape[] icons)
        {
            if (active) _activePage = page;

            var iconBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                   : ThemeBrushProvider.TextSecondaryBrush();

            foreach (var icon in icons)
                if (icon != null) icon.Stroke = iconBrush;

            if (button != null)
            {
                button.BorderBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                            : ThemeBrushProvider.BorderStrongBrush();
                button.BorderThickness = new Thickness(1);
            }
        }
    }
}