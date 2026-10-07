using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LvchaxsBS.UI.Pages
{
    public partial class ConfigContainerPage : Page
    {
        private Page? _triggerKeyPage;
        private Page? _configPage;
        private Page? _pauseKeyPage;
        private Page? _openMapKeyPage;

        private Func<string>? _getKey;
        private Action<string>? _setKey;

        private IEnumerable<TagInfo>? _tags;

        /// <summary>防止加载时触发 PART_Toggle 的 Checked/Unchecked 回调</summary>
        private bool _isLoadingToggle = false;

        public string CurrentModuleName { get; private set; } = "";
        public string CurrentPageType { get; private set; } = "";

        public string CurrentCategory
        {
            get
            {
                return CurrentPageType switch
                {
                    "TriggerKey" => "TriggerKey",
                    "PauseKey" => "AuxKey",
                    "OpenMapKey" => "AuxKey",
                    "Config" => "Config",
                    _ => "Config"
                };
            }
        }

        public ConfigContainerPage()
        {
            InitializeComponent();
            Loaded += ConfigContainerPage_Loaded;
            Unloaded += ConfigContainerPage_Unloaded;
        }

        private void ConfigContainerPage_Loaded(object sender, RoutedEventArgs e)
        {
            ThemeService.ThemeChanged += ThemeService_ThemeChanged;
        }

        private void ConfigContainerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
        }

        private void ThemeService_ThemeChanged(object? sender, AppTheme theme)
        {
            Dispatcher.Invoke(() =>
            {
                ApplyTags(_tags);
                UpdateButtons(CurrentPageType);
            });
        }

        // ============ 模块信息 ============

        public void SetSourceModule(string moduleName,
                                    string description,
                                    string iconPath,
                                    IEnumerable<TagInfo>? tags = null)
        {
            CurrentModuleName = moduleName;
            _tags = tags;

            PART_Title.Text = moduleName;
            PART_Description.Text = description;

            if (!string.IsNullOrEmpty(iconPath))
            {
                try
                {
                    PART_Icon.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(iconPath, UriKind.RelativeOrAbsolute));
                }
                catch { }
            }

            ApplyTags(tags);

            if (moduleName == "快速传送")
            {
                PART_OpenMapKeyBtn.Visibility = Visibility.Visible;
                PART_PauseKeyBtn.Visibility = Visibility.Collapsed;
            }
            else if (moduleName == "快速拾取" || moduleName == "剧情对话")
            {
                PART_PauseKeyBtn.Visibility = Visibility.Visible;
                PART_OpenMapKeyBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                PART_OpenMapKeyBtn.Visibility = Visibility.Collapsed;
                PART_PauseKeyBtn.Visibility = Visibility.Collapsed;
            }

            LoadToggleState(moduleName);
        }

        // ============ 胶囊开关 ============

        private void LoadToggleState(string moduleName)
        {
            var s = ConfigManager.Get<HomePageSettings>();
            var module = ModuleRegistry.ByName(moduleName);

            _isLoadingToggle = true;
            PART_Toggle.IsChecked = module?.IsEnabled(s) ?? false;
            _isLoadingToggle = false;

            PART_Toggle.Opacity = s.MasterSwitch ? 1.0 : 0.4;
        }

        public void RefreshMasterSwitchVisual()
        {
            var s = ConfigManager.Get<HomePageSettings>();
            if (PART_Toggle != null)
                PART_Toggle.Opacity = s.MasterSwitch ? 1.0 : 0.4;
        }

        private void PART_Toggle_Checked(object sender, RoutedEventArgs e)
        {
            OnToggleChanged(true);
        }

        private void PART_Toggle_Unchecked(object sender, RoutedEventArgs e)
        {
            OnToggleChanged(false);
        }

        private void OnToggleChanged(bool isChecked)
        {
            if (_isLoadingToggle) return;
            if (string.IsNullOrEmpty(CurrentModuleName)) return;

            var module = ModuleRegistry.ByName(CurrentModuleName);
            if (module == null) return;

            var s = ConfigManager.Get<HomePageSettings>();
            module.SetEnabled(s, isChecked);
            ConfigManager.Save(s);

            if (Application.Current?.MainWindow is UI.MainWindow mw)
                mw.ShowToast($"{module.Name}: {(isChecked ? "已启用" : "已关闭")}", isChecked);
        }

        // ============ Tags ============

        private void ApplyTags(IEnumerable<TagInfo>? tags)
        {
            if (tags == null)
            {
                PART_TagsControl.ItemsSource = null;
                return;
            }

            var list = new List<Border>();
            foreach (var t in tags)
            {
                var border = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = (Brush)Application.Current.Resources[t.BgKey]
                };
                var tb = new TextBlock
                {
                    Text = t.Text,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources[t.FgKey]
                };
                border.Child = tb;
                list.Add(border);
            }
            PART_TagsControl.ItemsSource = list;
        }

        // ============ 触发键页 ============

        public void SetTriggerKeyPage(Page page, string keyConfigTag)
        {
            _triggerKeyPage = page;

            if (page is TriggerKeyPage keyboardPage)
                keyboardPage.KeySelected += (s, keyName) =>
                {
                    _setKey?.Invoke(keyName);
                    ShowKeyToast("触发键", keyName);
                };
            else if (page is GamepadTriggerKeyPage gamepadPage)
                gamepadPage.KeySelected += (s, keyName) =>
                {
                    _setKey?.Invoke(keyName);
                    ShowKeyToast("触发键", keyName);
                };

            var module = ModuleRegistry.ByKeyTag(keyConfigTag);

            _getKey = () =>
            {
                var s = ConfigManager.Get<HomePageSettings>();
                return module?.GetKey(s) ?? "未设置";
            };

            _setKey = (keyName) =>
            {
                if (module == null) return;
                var settings = ConfigManager.Get<HomePageSettings>();
                module.SetKey(settings, keyName);
                ConfigManager.Save(settings);
            };
        }

        public void SetConfigPage(Page page) => _configPage = page;

        // ============ 切换 ============

        public void ShowTriggerKeyPage()
        {
            if (_triggerKeyPage == null) return;

            var savedKey = _getKey?.Invoke() ?? "";

            if (_triggerKeyPage is TriggerKeyPage keyboardPage)
            {
                // ★ 禁用（所有功能的触发键都禁）：Win / Alt / Ctrl（含左右，页面里显示为红色不可选）；
                //   快速传送再加"右键"——地图界面里右键被用作"取消本次传送"的开关，不能当触发键
                keyboardPage.SetDisabledKeys(
                    CurrentModuleName == "快速传送"
                        ? new[] { "Win", "Alt", "Ctrl", "右键" }
                        : new[] { "Win", "Alt", "Ctrl" });
                keyboardPage.LoadKey(savedKey);
            }
            else if (_triggerKeyPage is GamepadTriggerKeyPage gamepadPage)
            {
                gamepadPage.LoadKey(savedKey);
            }

            PART_ContentFrame.Navigate(_triggerKeyPage);
            CurrentPageType = "TriggerKey";
            UpdateButtons("TriggerKey");
        }

        public void ShowConfigPage()
        {
            if (_configPage == null) return;
            PART_ContentFrame.Navigate(_configPage);
            CurrentPageType = "Config";
            UpdateButtons("Config");
        }

        public void ShowPauseKeyPage()
        {
            if (_pauseKeyPage == null)
            {
                var page = new TriggerKeyPage();
                page.SetMultiSelectMode(true);
                page.PauseKeysChanged += OnPauseKeysChanged;
                _pauseKeyPage = page;
            }

            var savedKeys = ConfigManager.Get<QuickPickupSettings>().PauseKeys;
            if (_pauseKeyPage is TriggerKeyPage tp)
            {
                tp.SetDisabledKeys();   // ★ 清空禁用（暂停键不禁用）
                tp.LoadPauseKeys(savedKeys);
            }

            PART_ContentFrame.Navigate(_pauseKeyPage);
            CurrentPageType = "PauseKey";
            UpdateButtons("PauseKey");
        }

        public void ShowOpenMapKeyPage()
        {
            if (_openMapKeyPage == null)
            {
                var page = new TriggerKeyPage();
                page.SetMultiSelectMode(true);
                page.PauseKeysChanged += OnOpenMapKeysChanged;
                _openMapKeyPage = page;
            }

            var savedKeys = ConfigManager.Get<QuickTeleportSettings>().OpenMapKey_1;
            if (_openMapKeyPage is TriggerKeyPage tp)
            {
                // ★ 开图键只禁"右键"（地图界面右键 = 取消本次传送）；
                //   Win/Alt/Ctrl 在这里是允许选的，只有各功能的"触发键"才禁这三个
                tp.SetDisabledKeys("右键");
                tp.LoadPauseKeys(savedKeys);
            }

            PART_ContentFrame.Navigate(_openMapKeyPage);
            CurrentPageType = "OpenMapKey";
            UpdateButtons("OpenMapKey");
        }

        public void ShowPageByCategory(string category)
        {
            switch (category)
            {
                case "TriggerKey":
                    if (_triggerKeyPage != null)
                    {
                        ShowTriggerKeyPage();
                        return;
                    }
                    break;

                case "AuxKey":
                    if (PART_OpenMapKeyBtn.Visibility == Visibility.Visible)
                    {
                        ShowOpenMapKeyPage();
                        return;
                    }
                    if (PART_PauseKeyBtn.Visibility == Visibility.Visible)
                    {
                        ShowPauseKeyPage();
                        return;
                    }
                    if (_triggerKeyPage != null)
                    {
                        ShowTriggerKeyPage();
                        return;
                    }
                    break;

                case "Config":
                default:
                    break;
            }

            ShowConfigPage();
        }

        // ============ 暂停键 / 开图键保存 ============

        private void OnPauseKeysChanged(object? sender, string keys)
        {
            var settings = ConfigManager.Get<QuickPickupSettings>();
            settings.PauseKeys = keys;
            ConfigManager.Save(settings);

            ShowKeyToast("暂停键", keys);
        }

        private void OnOpenMapKeysChanged(object? sender, string keys)
        {
            var settings = ConfigManager.Get<QuickTeleportSettings>();
            settings.OpenMapKey_1 = keys;
            ConfigManager.Save(settings);

            ShowKeyToast("开图键", keys);
        }

        // ============ Toast ============

        private void ShowKeyToast(string label, string keyName)
        {
            string display;
            if (string.IsNullOrEmpty(keyName) || keyName == "未设置")
            {
                display = "未设置";
            }
            else
            {
                display = keyName.Replace(",", ", ");
            }

            string prefix = string.IsNullOrEmpty(CurrentModuleName)
                ? label
                : $"{CurrentModuleName} {label}";

            if (Application.Current?.MainWindow is UI.MainWindow mw)
                mw.ShowToast($"{prefix}: {display}", true);
        }

        // ============ 按钮状态 ============

        private void UpdateButtons(string active)
        {
            SetButtonActive(PART_TriggerKeyBtn, active == "TriggerKey");
            SetButtonActive(PART_ConfigBtn, active == "Config");
            SetButtonActive(PART_PauseKeyBtn, active == "PauseKey");
            SetButtonActive(PART_OpenMapKeyBtn, active == "OpenMapKey");
        }

        private void SetButtonActive(Button btn, bool active)
        {
            if (btn == null || btn.Visibility != Visibility.Visible) return;

            if (active)
            {
                btn.Background = (Brush)Application.Current.Resources["PrimaryLightBrush"];
                btn.Foreground = (Brush)Application.Current.Resources["PrimaryDarkBrush"];
                btn.BorderBrush = (Brush)Application.Current.Resources["PrimaryBrush"];
                btn.BorderThickness = new Thickness(1);
            }
            else
            {
                btn.Background = (Brush)Application.Current.Resources["CardBackgroundBrush"];
                btn.Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"];
                btn.BorderBrush = (Brush)Application.Current.Resources["BorderStrongBrush"];
                btn.BorderThickness = new Thickness(1);
            }
        }

        // ============ 按钮事件 ============

        private void TriggerKeyBtn_Click(object sender, RoutedEventArgs e) => ShowTriggerKeyPage();
        private void ConfigBtn_Click(object sender, RoutedEventArgs e) => ShowConfigPage();
        private void PauseKeyBtn_Click(object sender, RoutedEventArgs e) => ShowPauseKeyPage();
        private void OpenMapKeyBtn_Click(object sender, RoutedEventArgs e) => ShowOpenMapKeyPage();

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow as UI.MainWindow;
            mainWindow?.MainFrame.Navigate(new Pages.HomePage());
        }
    }

    public class TagInfo
    {
        public string Text { get; set; } = "";
        public string BgKey { get; set; } = "";
        public string FgKey { get; set; } = "";
    }
}