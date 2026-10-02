using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LvchaxsBS.Config;
using LvchaxsBS.Services;
using LvchaxsBS.UI.Controls;

namespace LvchaxsBS.UI.Pages
{
    public partial class HomePage : Page
    {
        // 防止"加载时"触发 Toggle_Changed
        private bool _isLoading = false;

        public HomePage()
        {
            InitializeComponent();
            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;
            LoadToggleStates();
            LoadTriggerKeyDisplay();
            _isLoading = false;

            ThemeService.ThemeChanged += ThemeService_ThemeChanged;
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
        }

        private void ThemeService_ThemeChanged(object? sender, AppTheme theme)
        {
            Dispatcher.Invoke(LoadTriggerKeyDisplay);
        }

        // ============ 加载开关状态 ============

        private void LoadToggleStates()
        {
            var s = ConfigManager.Get<HomePageSettings>();

            QuickTeleportToggle.IsChecked = s.QuickTeleport;
            QuickPickupToggle.IsChecked = s.QuickPickup;
            StoryDialogueToggle.IsChecked = s.StoryDialogue;
            AutoCookToggle.IsChecked = s.AutoCook;
            FishingAssistToggle.IsChecked = s.FishingAssist;
            AutoLumberToggle.IsChecked = s.AutoLumber;
            ControllerPickupToggle.IsChecked = s.ControllerPickup;

            // 总开关关 → 所有功能开关灰化
            ApplyMasterSwitchVisual(s.MasterSwitch);
        }

        /// <summary>
        /// 总开关关时，所有功能胶囊开关视觉置灰（配置值不变）
        /// </summary>
        private void ApplyMasterSwitchVisual(bool masterOn)
        {
            var toggles = new CheckBox[]
            {
                QuickTeleportToggle,
                QuickPickupToggle,
                StoryDialogueToggle,
                AutoCookToggle,
                FishingAssistToggle,
                AutoLumberToggle,
                ControllerPickupToggle
            };

            foreach (var t in toggles)
            {
                if (t == null) continue;
                t.Opacity = masterOn ? 1.0 : 0.4;
            }
        }

        /// <summary>供 MainWindow 在总开关切换后调用，刷新本页开关视觉</summary>
        public void RefreshMasterSwitchVisual()
        {
            var s = ConfigManager.Get<HomePageSettings>();
            ApplyMasterSwitchVisual(s.MasterSwitch);
        }

        // ============ 开关切换 → 保存 ============

        private void Toggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox cb || cb.Tag is not string tag) return;

            bool isChecked = cb.IsChecked == true;
            var settings = ConfigManager.Get<HomePageSettings>();

            string moduleName = "";

            switch (tag)
            {
                case "QuickTeleport": settings.QuickTeleport = isChecked; moduleName = "快速传送"; break;
                case "QuickPickup": settings.QuickPickup = isChecked; moduleName = "快速拾取"; break;
                case "StoryDialogue": settings.StoryDialogue = isChecked; moduleName = "剧情对话"; break;
                case "AutoCook": settings.AutoCook = isChecked; moduleName = "自动烹饪"; break;
                case "FishingAssist": settings.FishingAssist = isChecked; moduleName = "钓鱼辅助"; break;
                case "AutoLumber": settings.AutoLumber = isChecked; moduleName = "自动伐木"; break;
                case "ControllerPickup": settings.ControllerPickup = isChecked; moduleName = "手柄拾取"; break;
            }

            ConfigManager.Save(settings);

            if (Application.Current.MainWindow is UI.MainWindow mw)
                mw.ShowToast($"{moduleName}: {(isChecked ? "已启用" : "已关闭")}", isChecked);
        }

        // ============ 触发键显示 ============

        private void LoadTriggerKeyDisplay()
        {
            var s = ConfigManager.Get<HomePageSettings>();

            UpdateTriggerKeyButton(QuickTeleportTriggerBtn, s.QuickTeleportKey);
            UpdateTriggerKeyButton(QuickPickupTriggerBtn, s.QuickPickupKey);
            UpdateTriggerKeyButton(StoryDialogueTriggerBtn, s.StoryDialogueKey);
            UpdateTriggerKeyButton(AutoCookTriggerBtn, s.AutoCookKey);
            UpdateTriggerKeyButton(FishingAssistTriggerBtn, s.FishingAssistKey);
            UpdateTriggerKeyButton(AutoLumberTriggerBtn, s.AutoLumberKey);
            UpdateTriggerKeyButton(ControllerPickupTriggerBtn, s.ControllerPickupKey);
        }

        private void UpdateTriggerKeyButton(Button btn, string keyName)
        {
            if (btn == null) return;

            if (string.IsNullOrEmpty(keyName) || keyName == "未设置")
            {
                btn.Content = "触发键";
                btn.Background = ThemeBrushProvider.CardBackgroundBrush();
                btn.Foreground = ThemeBrushProvider.TextSecondaryBrush();
                btn.BorderBrush = ThemeBrushProvider.BorderStrongBrush();

                // 未设置时提示文字
                ButtonTip.SetText(btn, "当前设置：未设置");
            }
            else
            {
                btn.Content = keyName;
                btn.Background = ThemeBrushProvider.Get("Gray100Brush",
                                                        Color.FromRgb(0xF3, 0xF4, 0xF6));
                btn.Foreground = ThemeBrushProvider.PrimaryDarkBrush();
                btn.BorderBrush = ThemeBrushProvider.PrimaryBrush();

                // 悬停时显示完整键名
                ButtonTip.SetText(btn, $"当前设置：{keyName}");
            }
        }

        // ============ 按钮点击 ============

        private void TriggerKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string moduleName) return;

            var container = CreateContainerFor(moduleName);
            if (container == null) return;

            container.ShowTriggerKeyPage();

            var mainWindow = Application.Current.MainWindow as UI.MainWindow;
            mainWindow?.MainFrame.Navigate(container);
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string moduleName) return;

            var container = CreateContainerFor(moduleName);
            if (container == null) return;

            container.ShowConfigPage();

            var mainWindow = Application.Current.MainWindow as UI.MainWindow;
            mainWindow?.MainFrame.Navigate(container);
        }

        // ============ 创建容器 ============

        public static ConfigContainerPage? CreateContainerFor(string moduleName)
        {
            var container = new ConfigContainerPage();

            switch (moduleName)
            {
                case "快速传送":
                    container.SetSourceModule("快速传送",
                        "地图界面中选择锚点后按下触发键识别并自动执行点击F传送操作",
                        "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/快速传送.png",
                        CommonTags());
                    container.SetTriggerKeyPage(new TriggerKeyPage(), "QuickTeleportKey");
                    container.SetConfigPage(new Pages.FunctionConfigs.QuickTeleportConfig());
                    break;

                case "快速拾取":
                    container.SetSourceModule("快速拾取",
                        "主界面中按下触发键开启/关闭 F滚轮快速循环，非主界面自动暂停",
                        "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/快速拾取.png",
                        CommonTags());
                    container.SetTriggerKeyPage(new TriggerKeyPage(), "QuickPickupKey");
                    container.SetConfigPage(new Pages.FunctionConfigs.QuickPickupConfig());
                    break;

                case "剧情对话":
                    container.SetSourceModule("剧情对话",
                        "对话界面中按下触发键开启/关闭 识别到左上角对话隐藏图标时自动F对话",
                        "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/剧情对话.png",
                        CommonTags());
                    container.SetTriggerKeyPage(new TriggerKeyPage(), "StoryDialogueKey");
                    container.SetConfigPage(new Pages.FunctionConfigs.StoryDialogueConfig());
                    break;

                case "自动烹饪":
                    container.SetSourceModule("自动烹饪",
                        "自动烹饪料理+自动清理完美品质的复活药体力药",
                        "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/自动烹饪.png",
                        CommonTags());
                    container.SetTriggerKeyPage(new TriggerKeyPage(), "AutoCookKey");
                    container.SetConfigPage(new Pages.FunctionConfigs.AutoCookConfig());
                    break;

                case "钓鱼辅助":
                    container.SetSourceModule("钓鱼辅助",
                        "钓鱼界面中按下触发键开启/关闭 只需手动抛竿，识别到上钩后自动收杆并控制滑块",
                        "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/钓鱼辅助.png",
                        CommonTags());
                    container.SetTriggerKeyPage(new TriggerKeyPage(), "FishingAssistKey");
                    container.SetConfigPage(new Pages.FunctionConfigs.FishingAssistConfig());
                    break;

                case "自动伐木":
                    container.SetSourceModule("自动伐木",
                        "主界面并装备王树瑞佑后按下触发键开启/关闭 自动识别循环使用小道具",
                        "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/自动伐木.png",
                        CommonTags());
                    container.SetTriggerKeyPage(new TriggerKeyPage(), "AutoLumberKey");
                    container.SetConfigPage(new Pages.FunctionConfigs.AutoLumberConfig());
                    break;

                case "手柄拾取":
                    container.SetSourceModule("手柄拾取",
                        "按下触发键执行一次鼠标左键一键拾取，需要关闭游戏内-主界面控制设备自动切换",
                        "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/手柄拾取.png",
                        null);
                    container.SetTriggerKeyPage(new GamepadTriggerKeyPage(), "ControllerPickupKey");
                    container.SetConfigPage(new Pages.FunctionConfigs.ControllerPickupConfig());
                    break;

                default:
                    return null;
            }

            return container;
        }

        private static TagInfo[] CommonTags()
        {
            return new[]
            {
                new TagInfo { Text = "16:9", BgKey = "TagBlueBgBrush", FgKey = "TagBlueFgBrush" },
                new TagInfo { Text = "5120x2160", BgKey = "TagPurpleBgBrush", FgKey = "TagPurpleFgBrush" },
                new TagInfo { Text = "3440x1440", BgKey = "TagGreenBgBrush", FgKey = "TagGreenFgBrush" },
                new TagInfo { Text = "2560x1080", BgKey = "TagRedBgBrush", FgKey = "TagRedFgBrush" },
            };
        }
    }
}