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
            {
                keyboardPage.KeySelected += (s, keyName) =>
                {
                    _setKey?.Invoke(keyName);
                    ShowKeyToast("触发键", keyName);
                };

                // ★ 点击"被其他功能占用"的键 → 跳转到那个功能的触发键页
                keyboardPage.OccupiedKeyClicked += (s, ownerModule) =>
                    HomePage.NavigateToModule(ownerModule, showTriggerKey: true);
            }
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

        // ============ 全局禁选键 ============

        /// <summary>
        /// 所有功能的触发键都禁用的键：Win / Alt / Ctrl（含左右）+ 右键。
        /// 右键是全局禁选，不再跟"右键取消传送"开关挂钩。
        /// </summary>
        private static readonly string[] DisabledTriggerKeys = { "Win", "Alt", "Ctrl", "右键" };

        /// <summary>判断是否属于禁选键（含左右分组的具体键名）。</summary>
        private static bool IsDisabledTriggerKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return key.Equals("右键", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Win", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Alt", StringComparison.OrdinalIgnoreCase)
                || key.Equals("左Alt", StringComparison.OrdinalIgnoreCase)
                || key.Equals("右Alt", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || key.Equals("左Ctrl", StringComparison.OrdinalIgnoreCase)
                || key.Equals("右Ctrl", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 多键列表（暂停键 / 开图键）里剔除全局禁选键。
        /// 右键现在不可选，历史配置里如果还留着它，打开页面时会一起清掉，
        /// 否则会出现"列表里没有、配置里还在，功能却还在响应"的错位。
        /// </summary>
        private static string StripDisabledKeys(string keys, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(keys)) return keys;

            var kept = new List<string>();
            foreach (var raw in keys.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var k = raw.Trim();
                if (string.IsNullOrEmpty(k)) continue;
                if (IsDisabledTriggerKey(k)) { changed = true; continue; }
                kept.Add(k);
            }
            return string.Join(",", kept);
        }

        /// <summary>
        /// 打开触发键页时，如果当前保存的触发键正好是禁选键（例如以前设成过右键），
        /// 自动改回该功能配置类里的默认触发键并提示，避免"设了却用不了 / 页面显示未设置"。
        /// </summary>
        private string ResetTriggerKeyIfDisabled(string currentKey)
        {
            if (string.IsNullOrEmpty(currentKey) || currentKey == "未设置") return currentKey;
            if (!IsDisabledTriggerKey(currentKey)) return currentKey;

            var module = ModuleRegistry.ByName(CurrentModuleName);
            if (module == null) return currentKey;

            string fallback = module.GetKey(new HomePageSettings());   // 新建配置 = 各字段的默认值
            if (string.IsNullOrEmpty(fallback) || IsDisabledTriggerKey(fallback)) return currentKey;

            var s = ConfigManager.Get<HomePageSettings>();
            module.SetKey(s, fallback);
            ConfigManager.Save(s);

            ToastService.Show(module.Name, $"触发键不能设为「{currentKey}」，已改回默认「{fallback}」");
            return fallback;
        }

        // ============ 切换 ============

        public void ShowTriggerKeyPage()
        {
            if (_triggerKeyPage == null) return;

            var savedKey = _getKey?.Invoke() ?? "";

            if (_triggerKeyPage is TriggerKeyPage keyboardPage)
            {
                // ★ 所有功能的触发键统一禁用：Win / Alt / Ctrl（含左右，页面里红色不可选）+ 右键。
                //   右键是全局禁选键——它是"取消/交互"键，任何功能都不允许拿它当触发键，
                //   与"右键取消传送"开关是否勾选无关（否则同一个键会时禁时可选，
                //   被别的功能占用时还会出现"禁用按钮却显示成可跳转的占用样式"的矛盾）。
                keyboardPage.SetDisabledKeys(DisabledTriggerKeys);

                // 历史配置里万一残留了禁用键（比如以前把触发键设成了右键），自动改回默认值，
                // 免得出现"配置是右键、页面却显示未设置"的错觉
                savedKey = ResetTriggerKeyIfDisabled(savedKey);

                keyboardPage.LoadKey(savedKey);
                // ★ 其他功能已经用掉的触发键不可再选，按钮背景显示对应功能图标
                //   （必须在 LoadKey 之后：当前功能自己已选的键要保持选中样式）
                keyboardPage.SetOccupiedKeys(CurrentModuleName);
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
                // ★ 暂停键不禁 Win/Alt/Ctrl，但右键全局禁选（它已经被用于取消/交互）
                tp.SetDisabledKeys("右键");

                savedKeys = StripDisabledKeys(savedKeys, out bool pauseChanged);
                if (pauseChanged)
                {
                    ConfigSync.Mutate<QuickPickupSettings>(s => s.PauseKeys = savedKeys);
                    ToastService.Show("暂停键", "右键不能作为暂停键，已从配置中移除");
                }
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
                // ★ 开图键只禁"右键"（地图界面右键 = 取消本次传送），无条件禁；
                //   Win/Alt/Ctrl 在这里是允许选的，只有各功能的"触发键"才禁这三个
                tp.SetDisabledKeys("右键");

                savedKeys = StripDisabledKeys(savedKeys, out bool mapChanged);
                if (mapChanged)
                {
                    ConfigSync.Mutate<QuickTeleportSettings>(s => s.OpenMapKey_1 = savedKeys);
                    ToastService.Show("开图键", "右键不能作为开图键，已从配置中移除");
                }
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