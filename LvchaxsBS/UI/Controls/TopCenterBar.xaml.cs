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

        // ============ 导航页标识 ============
        // 唯一来源：SetXxxActive 写入、RefreshActiveVisual 读取，两边必须用同一套常量，
        // 否则就会出现"记的是 A、刷的是 B"这类对不上的高亮残留。

        private const string PageHome = "Home";
        private const string PagePersonalization = "Personalization";
        private const string PageVoice = "Voice";
        private const string PageSettings = "Settings";
        private const string PageUid = "Uid";

        /// <summary>当前高亮的导航页标识；空串表示哪个导航项都不该亮。</summary>
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
            SetHomeActive(_activePage == PageHome);
            SetSettingsActive(_activePage == PageSettings);
            SetPersonalizationActive(_activePage == PagePersonalization);
            SetVoiceActive(_activePage == PageVoice);

            // ★ UID 也要在这里刷：它是 TopLeftBar 里的第 5 个导航项，
            //   以前漏了这一行，导致"UID 页切主题后 UID 还亮着上个主题的旧色、
            //   而 _activePage 里记着的上一个页面反倒被重新点亮"。
            SetUidActive(_activePage == PageUid);
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
            RememberActivePage(active, PageUid);

            if (UidText != null)
                UidText.Foreground = active ? ThemeBrushProvider.PrimaryBrush()
                                            : ThemeBrushProvider.TextSecondaryBrush();

            ApplyNavFrame(ButtonUid, active);
        }

        // ============ 当前页高亮 ============

        public void SetHomeActive(bool active) => SetNavActive(active, PageHome, ButtonHome, IconHome);

        public void SetSettingsActive(bool active)
            => SetNavActive(active, PageSettings, ButtonSettings, IconSettingsOuter, IconSettingsInner);

        public void SetPersonalizationActive(bool active)
            => SetNavActive(active, PagePersonalization, ButtonPersonalization,
                IconSliderLine1, IconSliderLine2, IconSliderLine3,
                IconSliderKnob1, IconSliderKnob2, IconSliderKnob3);

        public void SetVoiceActive(bool active)
            => SetNavActive(active, PageVoice, ButtonVoice,
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
            RememberActivePage(active, page);

            var iconBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                   : ThemeBrushProvider.TextSecondaryBrush();

            foreach (var icon in icons)
                if (icon != null) icon.Stroke = iconBrush;

            ApplyNavFrame(button, active);
        }

        /// <summary>
        /// 记录 / 清除"当前页"。
        ///
        /// <para>
        /// ★ 取消高亮时必须把记录一并清掉。旧代码只在 <c>active == true</c> 时写 <c>_activePage</c>：
        /// 进 UID 页时四个导航项全部传 <c>false</c>，记录却仍停在上一个导航页，于是
        /// 一旦切主题触发 <see cref="RefreshActiveVisual"/>，那一项就被重新点亮 ——
        /// 这就是「在 UID 页却看到设置 / 字幕设置自己变蓝」的直接原因。
        /// </para>
        /// </summary>
        private void RememberActivePage(bool active, string page)
        {
            if (active) _activePage = page;
            else if (_activePage == page) _activePage = "";
        }

        /// <summary>导航按钮外框：当前页用主色描边，其余用中性描边。</summary>
        private static void ApplyNavFrame(Button? button, bool active)
        {
            if (button == null) return;

            button.BorderBrush = active ? ThemeBrushProvider.PrimaryBrush()
                                        : ThemeBrushProvider.BorderStrongBrush();
            button.BorderThickness = new Thickness(1);
        }
    }
}