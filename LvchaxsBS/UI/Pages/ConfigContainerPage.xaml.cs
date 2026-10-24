using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Core;
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

        /// <summary>插件插入到按钮条最前面的额外按钮（宏的「添加宏 / 添加文件夹」）。</summary>
        private FrameworkElement? _toolbarElement;

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

            ApplyAuxButtonVisibility();

            LoadToggleState(moduleName);
        }

        /// <summary>
        /// 按当前模块决定「开图键 / 暂停键」两个辅助按钮谁显示（其余一律收起）。
        /// <para>★ 从「只留触发键 + 返回」模式退回来时要靠它把按钮恢复原样。</para>
        /// </summary>
        private void ApplyAuxButtonVisibility()
        {
            if (CurrentModuleName == "快速传送")
            {
                PART_OpenMapKeyBtn.Visibility = Visibility.Visible;
                PART_PauseKeyBtn.Visibility = Visibility.Collapsed;
            }
            else if (CurrentModuleName == "快速拾取" || CurrentModuleName == "剧情对话")
            {
                PART_PauseKeyBtn.Visibility = Visibility.Visible;
                PART_OpenMapKeyBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                PART_OpenMapKeyBtn.Visibility = Visibility.Collapsed;
                PART_PauseKeyBtn.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 「只留触发键 + 返回」模式：给单个地区绑触发键时，顶部不该出现配置 / 开关 / 开图键，
        /// 和自定义宏进触发键页时一样干净。
        /// </summary>
        private void SetTriggerKeyOnlyMode(bool on)
        {
            if (on)
            {
                PART_ConfigBtn.Visibility = Visibility.Collapsed;
                PART_Toggle.Visibility = Visibility.Collapsed;
                PART_OpenMapKeyBtn.Visibility = Visibility.Collapsed;
                PART_PauseKeyBtn.Visibility = Visibility.Collapsed;
                if (_toolbarElement != null) _toolbarElement.Visibility = Visibility.Collapsed;
            }
            else
            {
                PART_ConfigBtn.Visibility = Visibility.Visible;
                PART_Toggle.Visibility = Visibility.Visible;
                ApplyAuxButtonVisibility();
                if (_toolbarElement != null) _toolbarElement.Visibility = Visibility.Visible;
            }
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

                // ★ 点组合键（Ctrl / Alt）只是微调：保存，页面留着继续调
                keyboardPage.ComboAdjusted += (s, keyName) =>
                {
                    _setKey?.Invoke(keyName);
                    ShowKeyToast("触发键", keyName);
                };

                // ★ 点了已选中的键 = 取消：清配置，但**不关页面**（让用户接着选）
                keyboardPage.KeyCleared += (s, e) =>
                {
                    _setKey?.Invoke("未设置");
                    ShowKeyToast("触发键", "未设置");
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

        /// <summary>
        /// 在底部按钮条的**最前面**插一个额外按钮（插件模块用，如「自定义宏」的「添加宏」）。
        /// <para>
        /// ★ 插完这一排就是「添加宏 / 触发键 / 配置 / 返回 / 开关」，和普通功能完全一样的布局。
        ///   null 时什么都不加（普通模块不受影响）。
        /// </para>
        /// </summary>
        public void SetToolbarElement(FrameworkElement? element)
        {
            if (element == null) return;

            element.Margin = new Thickness(0, 0, 10, 0);
            element.VerticalAlignment = VerticalAlignment.Center;

            PART_ButtonBar.Children.Insert(0, element);
            _toolbarElement = element;
        }

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
        /// 把组合键里的修饰键统一成**左侧**：Ctrl → 左Ctrl，Alt → 左Alt，右Ctrl / 右Alt 一并归到左侧。
        /// <para>★ 组合键只认左 Ctrl / 左 Alt（右边是独立键，界面上永远禁用）。</para>
        /// <para>不含 '+' 的普通键、以及认不出的修饰键（如旧 Shift 组合）原样返回。</para>
        /// </summary>
        private static string NormalizeComboKeyLeftOnly(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            int plus = key.LastIndexOf('+');
            if (plus <= 0 || plus >= key.Length - 1) return key;

            bool ctrl = false, alt = false;
            foreach (var raw in key[..plus].Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                var m = raw.Trim();
                if (m.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                    || m.Equals("左Ctrl", StringComparison.OrdinalIgnoreCase)
                    || m.Equals("右Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    ctrl = true;
                }
                else if (m.Equals("Alt", StringComparison.OrdinalIgnoreCase)
                    || m.Equals("左Alt", StringComparison.OrdinalIgnoreCase)
                    || m.Equals("右Alt", StringComparison.OrdinalIgnoreCase))
                {
                    alt = true;
                }
            }

            if (!ctrl && !alt) return key;

            string baseKey = key[(plus + 1)..].Trim();
            var parts = new List<string>(3);
            if (ctrl) parts.Add("左Ctrl");
            if (alt) parts.Add("左Alt");
            parts.Add(baseKey);
            return string.Join("+", parts);
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
        private string ResetTriggerKeyIfDisabled(string currentKey, bool extraDisabled = false)
        {
            if (string.IsNullOrEmpty(currentKey) || currentKey == "未设置") return currentKey;
            if (!extraDisabled && !IsDisabledTriggerKey(currentKey)) return currentKey;

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
                // ★ 功能触发键支持组合键：Ctrl / Alt 不在禁用之列，改成"点一下当修饰键"。
                //   必须在 SetDisabledKeys 之前打开，否则它们会先被禁掉。
                keyboardPage.EnableComboModifierMode();

                // ★ 所有功能的触发键统一禁用：Win / Alt / Ctrl（含左右，页面里红色不可选）+ 右键。
                //   右键是全局禁选键——它是"取消/交互"键，任何功能都不允许拿它当触发键，
                //   与"右键取消传送"开关是否勾选无关（否则同一个键会时禁时可选，
                //   被别的功能占用时还会出现"禁用按钮却显示成可跳转的占用样式"的矛盾）。
                var disabledKeys = new List<string>(DisabledTriggerKeys);

                // ★ 快速传送：快速切图里已经绑给各地区的键也不能再选 ——
                //   否则按一下会同时"切图 + 触发快速传送"。这些键同样底部标地区名。
                var regionKeyOwners = CurrentModuleName == "快速传送"
                    ? BuildAllQuickMapRegionKeys()
                    : new Dictionary<string, string>();

                disabledKeys.AddRange(regionKeyOwners.Keys);

                keyboardPage.SetDisabledKeys(disabledKeys.ToArray());

                // 历史配置里万一残留了禁用键（以前设成右键 / 后来被绑成了地区键），
                // 自动改回默认值，免得出现"配置是右键、页面却显示未设置"的错觉
                // 地区键现在可能是组合键（"左Alt+2"），比对时两边都取基础键
                savedKey = ResetTriggerKeyIfDisabled(savedKey,
                    regionKeyOwners.ContainsKey(BaseKeyOf(savedKey)));

                // ★ 历史配置里可能是 "Ctrl+Alt+Space"（通用写法，左右都算）或带右 Ctrl/右 Alt，
                //   统一规范成左侧并写回配置 —— 组合键只认左 Ctrl / 左 Alt。
                string normalizedKey = NormalizeComboKeyLeftOnly(savedKey);
                if (!string.Equals(normalizedKey, savedKey, StringComparison.Ordinal))
                {
                    savedKey = normalizedKey;
                    _setKey?.Invoke(savedKey);
                }

                keyboardPage.LoadKey(savedKey);
                // ★ 其他功能已经用掉的触发键不可再选，按钮背景显示对应功能图标
                //   （必须在 LoadKey 之后：当前功能自己已选的键要保持选中样式）
                keyboardPage.SetOccupiedKeys(CurrentModuleName);

                // 被地区占用的键：按钮内底部写地区名（和快速切图页里那套一致）
                keyboardPage.SetDisabledKeyOwners(regionKeyOwners);
            }
            else if (_triggerKeyPage is GamepadTriggerKeyPage gamepadPage)
            {
                gamepadPage.LoadKey(savedKey);
            }

            PART_ContentFrame.Navigate(_triggerKeyPage);
            CurrentPageType = "TriggerKey";

            // ★ 自定义宏：这个容器只用来设触发键，顶部不该有「添加宏 / 添加文件夹」「配置」和胶囊开关，
            //   否则点触发键进来却看到一堆无关按钮。用户明确：触发键页只留「触发键 返回」。
            //   （宏的容器只用于触发键页——「打开」走的是 MacroConfigPage，不经过这里。）
            if (CurrentModuleName == MacroModuleService.ModuleName)
            {
                PART_ConfigBtn.Visibility = Visibility.Collapsed;
                PART_Toggle.Visibility = Visibility.Collapsed;
                if (_toolbarElement != null) _toolbarElement.Visibility = Visibility.Collapsed;
            }

            UpdateButtons("TriggerKey");
        }

        public void ShowConfigPage()
        {
            if (_configPage == null) return;

            // 从「只留触发键 + 返回」退回来：把配置 / 开图键 / 开关恢复出来
            SetTriggerKeyOnlyMode(false);

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
                // ★ 暂停键不做任何限制：它的用途就是随时暂停，任何按键（含 Win/Alt/Ctrl/右键）都能设
                tp.SetDisabledKeys();   // 清空禁用
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

        // ============ 快速切图：给单个地区绑触发键 ============

        private TriggerKeyPage? _quickMapKeyPage;
        private string? _quickMapRegionId;

        /// <summary>从快速切图页进来时，底部「返回」要回配置页而不是首页。</summary>
        private bool _quickMapReturnToConfig;

        /// <summary>
        /// 给某个地区绑触发键。
        /// <para>
        /// ★ 走的是和首页「触发键」按钮**同一套**触发键页（TriggerKeyPage），
        ///   不是自己监听按键 —— 用户点的是页面里那张键盘。
        /// </para>
        /// </summary>
        public void ShowQuickMapKeyPage(string regionId)
        {
            if (string.IsNullOrEmpty(regionId)) return;

            if (_quickMapKeyPage == null)
            {
                var page = new TriggerKeyPage();
                page.KeySelected += OnQuickMapRegionKeySelected;

                // ★ 点组合键（Ctrl / Alt）只是微调：保存但不关页面
                page.ComboAdjusted += OnQuickMapRegionComboAdjusted;

                // ★ 点了已选中的键 = 取消：清掉这个地区的键，但**不关页面**
                //   （取消不等于改好了，用户还要接着选；选完新键才走 KeySelected → 自动关闭）
                page.KeyCleared += (s, e) =>
                {
                    string? rid = _quickMapRegionId;
                    if (string.IsNullOrEmpty(rid)) return;

                    var st = ConfigManager.Get<QuickTeleportSettings>();
                    st.QuickMapKeys?.Remove(rid);
                    ConfigManager.Save(st);

                    ShowKeyToast($"{QuickMapSwitchLogic.RegionDisplayName(rid)} 触发键", "未设置");
                };

                _quickMapKeyPage = page;
            }

            _quickMapRegionId = regionId;
            _quickMapReturnToConfig = true;

            // ★ 快速切图也支持组合键：必须在 SetDisabledKeys 之前打开
            _quickMapKeyPage.EnableComboModifierMode();

            // ★ 禁选集合 = 全局禁用键 + 已被占用的键（其它地区 / 快速传送的触发键）。
            //   TriggerKeyPage 对 IsEnabled=false 的按钮是「红字 + 灰底 + 禁止光标」，正是要的效果。
            _quickMapKeyPage.SetDisabledKeys(BuildQuickMapDisabledKeys(regionId));

            // 组合键冲突没法按单键标红，单独给一份"完整组合键黑名单"
            _quickMapKeyPage.SetForbiddenCombos(BuildQuickMapForbiddenCombos(regionId));

            var settings = ConfigManager.Get<QuickTeleportSettings>();
            var keys = settings.QuickMapKeys;
            string saved = "";
            keys?.TryGetValue(regionId, out saved);

            // 历史配置里的 "Ctrl+1" / 右 Ctrl 统一规范成左侧并写回（组合键只认左 Ctrl / 左 Alt）
            string normalized = NormalizeComboKeyLeftOnly(saved);
            if (!string.Equals(normalized, saved, StringComparison.Ordinal) && keys != null)
            {
                saved = normalized;
                keys[regionId] = saved;
                ConfigManager.Save(settings);
            }

            _quickMapKeyPage.LoadKey(string.IsNullOrEmpty(saved) ? "未设置" : saved);

            // 被禁用的键在按钮内底部写一行小字：谁占用了它（"蒙德" / "传送"）
            _quickMapKeyPage.SetDisabledKeyOwners(BuildQuickMapOwnerLabels(regionId));

            // 从"A 地区"点跳转键到"B 地区"时页面实例不变，重复 Navigate 没必要
            if (!ReferenceEquals(PART_ContentFrame.Content, _quickMapKeyPage))
                PART_ContentFrame.Navigate(_quickMapKeyPage);

            // 顶部只留「触发键 / 返回」（与自定义宏进触发键页一致）
            SetTriggerKeyOnlyMode(true);

            CurrentPageType = "TriggerKey";
            UpdateButtons("TriggerKey");
        }

        /// <summary>
        /// 收集"这个地区不能选"的键（按钮红字、不可点，底部会写占用者名字）：
        /// 全局禁用键（Win / Alt / Ctrl / 右键）+ 其它地区已经绑过的切图键 +
        /// 快速传送自己的触发键。
        /// <para>本地区自己已绑的键要排除，否则进来就是红字、不能再点一次取消。</para>
        /// </summary>
        private static string[] BuildQuickMapDisabledKeys(string regionId)
        {
            var taken = new HashSet<string>(DisabledTriggerKeys, StringComparer.OrdinalIgnoreCase);

            // 其它地区
            // ★ 组合键要按"基础键"来禁：璃月设了 左Alt+2，蒙德页面上的 2 就该是红字，
            //   否则用户完全看不出这个键已经被占了（只有点了才提示，太晚）。
            var keys = ConfigManager.Get<QuickTeleportSettings>().QuickMapKeys;
            if (keys != null)
            {
                foreach (var kv in keys)
                {
                    if (kv.Key == regionId) continue;
                    if (!string.IsNullOrWhiteSpace(kv.Value)) taken.Add(BaseKeyOf(kv.Value));
                }
            }

            // 快速传送的触发键
            var teleportKey = ConfigManager.Get<HomePageSettings>().QuickTeleportKey;
            if (!string.IsNullOrWhiteSpace(teleportKey) && teleportKey != "未设置")
                taken.Add(BaseKeyOf(teleportKey));

            return taken.ToArray();
        }

        /// <summary>
        /// 取组合键的**基础键**："左Alt+2" → "2"。不含 '+' 就原样返回。
        /// <para>页面上标红 / 写占用者都是按单个按钮做的，所以要用基础键去匹配。</para>
        /// </summary>
        private static string BaseKeyOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            int plus = key.LastIndexOf('+');
            return plus < 0 ? key : key[(plus + 1)..].Trim();
        }

        /// <summary>
        /// 其它地区（以及快速传送）已绑的**完整键名**，组合键也算（如 "左Ctrl+1"）。
        /// <para>单键冲突由 <see cref="BuildQuickMapDisabledKeys"/> 标红处理；组合键没法按单键标红，
        ///   只能交给 TriggerKeyPage 在"选中基础键 / 激活修饰键"时按拼接结果拦下来。</para>
        /// </summary>
        private static Dictionary<string, string> BuildQuickMapForbiddenCombos(string regionId)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var keys = ConfigManager.Get<QuickTeleportSettings>().QuickMapKeys;
            if (keys != null)
            {
                foreach (var kv in keys)
                {
                    if (kv.Key == regionId) continue;
                    if (!string.IsNullOrWhiteSpace(kv.Value))
                        map[kv.Value.Trim()] = QuickMapSwitchLogic.RegionDisplayName(kv.Key);
                }
            }

            var teleportKey = ConfigManager.Get<HomePageSettings>().QuickTeleportKey;
            if (!string.IsNullOrWhiteSpace(teleportKey) && teleportKey != "未设置")
                map[teleportKey.Trim()] = "传送";

            return map;
        }

        /// <summary>所有快速切图地区已绑的键：键名 → 地区名（给"传送触发键页"用）。</summary>
        private static Dictionary<string, string> BuildAllQuickMapRegionKeys()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var keys = ConfigManager.Get<QuickTeleportSettings>().QuickMapKeys;
            if (keys == null) return map;

            foreach (var kv in keys)
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    map[BaseKeyOf(kv.Value)] = QuickMapSwitchLogic.RegionDisplayName(kv.Key);
            }

            return map;
        }

        /// <summary>键名 → 按钮底部小字（"蒙德" / "传送"）。</summary>
        private static Dictionary<string, string> BuildQuickMapOwnerLabels(string regionId)
        {
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var quick = ConfigManager.Get<QuickTeleportSettings>();

            if (quick.QuickMapKeys != null)
            {
                foreach (var kv in quick.QuickMapKeys)
                {
                    if (kv.Key == regionId) continue;
                    if (!string.IsNullOrWhiteSpace(kv.Value))
                        owners[BaseKeyOf(kv.Value)] = QuickMapSwitchLogic.RegionDisplayName(kv.Key);
                }
            }

            var teleportKey = ConfigManager.Get<HomePageSettings>().QuickTeleportKey;
            if (!string.IsNullOrWhiteSpace(teleportKey) && teleportKey != "未设置")
                owners[BaseKeyOf(teleportKey)] = "传送";

            return owners;
        }

        /// <summary>选完基础键 = "选好了"：保存并自动关闭。</summary>
        private void OnQuickMapRegionKeySelected(object? sender, string keyName)
            => SaveQuickMapRegionKey(keyName, close: true);

        /// <summary>点组合键（Ctrl / Alt）只是微调：保存但**不关**，好让用户接着调。</summary>
        private void OnQuickMapRegionComboAdjusted(object? sender, string keyName)
            => SaveQuickMapRegionKey(keyName, close: false);

        private void SaveQuickMapRegionKey(string keyName, bool close)
        {
            string? regionId = _quickMapRegionId;
            if (string.IsNullOrEmpty(regionId)) return;

            var settings = ConfigManager.Get<QuickTeleportSettings>();
            settings.QuickMapKeys ??= new Dictionary<string, string>();

            string regionName = QuickMapSwitchLogic.RegionDisplayName(regionId);

            if (string.IsNullOrEmpty(keyName) || keyName == "未设置")
            {
                settings.QuickMapKeys.Remove(regionId);
                ConfigManager.Save(settings);
                ShowKeyToast($"{regionName} 触发键", "未设置");
                if (close) CloseQuickMapKeyPage();
                return;
            }

            // 同一个键不能绑给两个地区（按钮已经禁选，这里只是兜底）
            foreach (var kv in settings.QuickMapKeys)
            {
                if (kv.Key != regionId
                    && string.Equals(kv.Value, keyName, StringComparison.OrdinalIgnoreCase))
                {
                    string owner = QuickMapSwitchLogic.RegionDisplayName(kv.Key);
                    ToastService.Show("快速切图", $"{keyName} 已经绑给「{owner}」，先把它清掉再绑");
                    return;
                }
            }

            settings.QuickMapKeys[regionId] = keyName;
            ConfigManager.Save(settings);
            ShowKeyToast($"{regionName} 触发键", keyName);

            if (close) CloseQuickMapKeyPage();
        }

        /// <summary>
        /// 选完键直接关掉触发键页、回配置页（用户要求"选完自动关闭"）。
        /// <para>★ 延后一帧再换页：这个回调是从触发键页按钮的 Click 里出来的，
        ///   当场把页面从 Frame 里换掉容易踩到"正在处理输入的控件被卸载"。</para>
        /// </summary>
        private void CloseQuickMapKeyPage()
        {
            _quickMapReturnToConfig = false;
            Dispatcher.BeginInvoke(new Action(ShowConfigPage),
                                   System.Windows.Threading.DispatcherPriority.Background);
        }

        public void ShowPageByCategory(string category)        {
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

        /// <summary>
        /// 悬浮到「返回」上时，按当前内容页的层级刷新提示文案。
        /// <para>
        /// ★ 这是**兜底**：WPF 没有 <c>PreviewMouseEnter</c>（鼠标进入没有隧道版本），
        ///   而 ButtonTip 也是在 MouseEnter 里读 Text，谁先谁后不保证。
        ///   所以主路径是内容页切视图时自己刷（<c>MacroConfigPage.UpdateHostBackTip</c>），这里只是再校一次。
        /// </para>
        /// </summary>
        private void BackBtn_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
            => RefreshBackTip();

        /// <summary>
        /// 问内容页「这一层的返回该叫什么」（约定方法 <c>public string GetBackTip()</c>）；
        /// 没有就退回默认的「返回主页」。
        /// </summary>
        private void RefreshBackTip()
        {
            string tip = "返回主页";

            try
            {
                if (PART_ContentFrame.Content is object content &&
                    content.GetType().GetMethod("GetBackTip") is System.Reflection.MethodInfo getTip &&
                    getTip.ReturnType == typeof(string) &&
                    getTip.GetParameters().Length == 0 &&
                    getTip.Invoke(content, null) is string custom &&
                    !string.IsNullOrWhiteSpace(custom))
                {
                    tip = custom;
                }
            }
            catch
            {
                // 内容页的 GetBackTip 抛了也不能影响返回按钮本身
            }

            UI.Controls.ButtonTip.SetText(PART_BackBtn, tip);
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            // 快速切图改地区键：这里返回到配置页（否则直接跳首页，用户得重新点进来）
            if (_quickMapReturnToConfig && ReferenceEquals(PART_ContentFrame.Content, _quickMapKeyPage))
            {
                _quickMapReturnToConfig = false;
                ShowConfigPage();
                return;
            }
            // ★ 先问内容页「你自己还能退回上一层吗」——比如自定义宏的分组内要退回文件夹列表。
            //   用**反射 + 约定方法**（`public bool TryGoBack()`）而不是类型判断/接口：
            //   插件不能被宿主引用（插件缺失也要能启动），这里也不能反向依赖插件类型。
            //   没这个方法、或返回 false ⇒ 照旧回首页。
            if (PART_ContentFrame.Content is object content &&
                content.GetType().GetMethod("TryGoBack") is System.Reflection.MethodInfo tryGoBack &&
                tryGoBack.ReturnType == typeof(bool) &&
                tryGoBack.GetParameters().Length == 0 &&
                tryGoBack.Invoke(content, null) is bool handled && handled)
            {
                return;
            }

            var mainWindow = Application.Current.MainWindow as UI.MainWindow;
            mainWindow?.MainFrame.Navigate(new Pages.HomePage());
        }

        // ============ 内容页「全屏」信号 ============

        /// <summary>
        /// 内容页只要写 <c>Tag="FullScreen"</c>（宏指令编辑页就这么干），
        /// 本容器就把顶部那张 70 高的功能信息卡收掉、内容区边距归零
        /// ⇒ 那个页面直接顶到窗口标题栏下面、占满整个内容区。
        /// <para>
        /// ★ 用**字符串约定**而不是类型判断：插件页不能被宿主引用（插件缺失也要能启动），
        ///   更不能被宿主反向依赖。离开全屏页时自动恢复。
        /// </para>
        /// </summary>
        private void PART_ContentFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            bool full = e.Content is Page page && (page.Tag as string) == "FullScreen";

            HeaderCard.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            ContentArea.Margin = full ? new Thickness(0) : new Thickness(-4, 0, -4, 0);
            ContentAreaInner.Margin = full ? new Thickness(0) : new Thickness(4);

            // 再上抛给主窗口：收掉文字标题行 + 顶部按钮栏，让内容区顶到标题栏下面
            (Application.Current.MainWindow as UI.MainWindow)?.SetContentFullScreen(full);
        }
    }

    public class TagInfo
    {
        public string Text { get; set; } = "";
        public string BgKey { get; set; } = "";
        public string FgKey { get; set; } = "";
    }
}