using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;   // VisualTreeHelper
using System.Windows.Media.Imaging;
using LvchaxsBS.Config;
using LvchaxsBS.Services;
using LvchaxsBS.UI.Controls;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages
{
    public partial class TriggerKeyPage : Page
    {
        public event EventHandler<string>? KeySelected;
        public event EventHandler<string>? PauseKeysChanged;

        /// <summary>
        /// 点击了被其他功能占用的键，参数 = 占用它的功能名（如 "自动烹饪"）。
        /// 容器页订阅它来跳转到那个功能的触发键页。
        /// </summary>
        public event EventHandler<string>? OccupiedKeyClicked;

        private readonly Dictionary<string, Button> _keyButtonMap = new();
        private readonly Dictionary<Button, string> _buttonToName = new();

        private Button? _selectedButton;
        private string _currentKeyName = "";

        private bool _isMultiSelectMode = false;
        private readonly HashSet<string> _selectedKeys = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>被禁用的键名（SetDisabledKeys 设置）。禁用键不会被回显为选中。</summary>
        private readonly HashSet<string> _disabledKeys = new(StringComparer.OrdinalIgnoreCase);

        // 待加载（Loaded 前调用）
        private string? _pendingLoadKey;
        private string? _pendingLoadPauseKeys;
        private string[]? _pendingDisabledKeys;

        /// <summary>
        /// 选中标记：写在按钮 Tag 上，由 KeyButtonStyle 里的 Trigger(Tag=Selected) 消费。
        /// 颜色走 DynamicResource（PrimaryLightBrush/PrimaryDarkBrush/PrimaryBrush），
        /// 与容器页"触发键/开图键/暂停键/配置"标签页的选中态是同一组资源，切主题会一起变。
        /// </summary>
        private const string SelectedTag = "Selected";

        /// <summary>
        /// 被其他功能占用标记（与 SelectedTag 互斥，当前功能自己选中的键不会被标成占用）。
        /// 由 KeyButtonStyle 的 Trigger(Tag=Occupied) 消费。
        /// </summary>
        private const string OccupiedTag = "Occupied";

        /// <summary>当前页面已标记占用的按钮，便于整页清理。</summary>
        private readonly List<Button> _occupiedButtons = new();

        /// <summary>占用按钮 → 占用它的功能名（点击跳转用）。</summary>
        private readonly Dictionary<Button, string> _occupiedOwner = new();

        // 待加载（Loaded 前调用）
        private string? _pendingOccupiedModule;

        public TriggerKeyPage()
        {
            InitializeComponent();
            Loaded += TriggerKeyPage_Loaded;
        }

        private void TriggerKeyPage_Loaded(object sender, RoutedEventArgs e)
        {
            BuildKeyMap();
            BindClickEvents();

            // 处理暂存
            if (!string.IsNullOrEmpty(_pendingLoadKey))
            {
                var k = _pendingLoadKey;
                _pendingLoadKey = null;
                LoadKey(k);
            }
            if (!string.IsNullOrEmpty(_pendingLoadPauseKeys))
            {
                var k = _pendingLoadPauseKeys;
                _pendingLoadPauseKeys = null;
                LoadPauseKeys(k);
            }
            // 应用暂存的禁用
            if (_pendingDisabledKeys != null)
            {
                var keys = _pendingDisabledKeys;
                _pendingDisabledKeys = null;
                SetDisabledKeys(keys);
            }
            // 应用暂存的占用
            if (!string.IsNullOrEmpty(_pendingOccupiedModule))
            {
                var m = _pendingOccupiedModule;
                _pendingOccupiedModule = null;
                SetOccupiedKeys(m);
            }
        }

        // ============ 建映射 ============

        /// <summary>
        /// 键名需要纠正的按钮（x:Name -> 引擎实际使用的键名）。
        /// 引擎侧键名来自 <c>GlobalKeyboardHookService.GetKeyName</c> / <c>GlobalMouseHookService</c>，
        /// 与按钮上显示的文本并不总是一致（例如按钮显示 "Back" 而引擎键名是 "Backspace"），
        /// 这里统一映射，保证存进配置的键名能被引擎正确匹配。
        /// </summary>
        private static readonly Dictionary<string, string> NameOverrides = new()
        {
            // 小键盘符号键（Content 与主键区重复，必须纠正）
            ["KeyNumPlus"] = "Num+",
            ["KeyNumMinus"] = "Num-",
            ["KeyNumMultiply"] = "Num*",
            ["KeyNumDivide"] = "Num/",
            ["KeyNumDecimal"] = "Num.",
            ["KeyNumEnter"] = "NumEnter",

            // 导航 / 编辑键
            ["KeyBackspace"] = "Backspace",
            ["KeyDelete"] = "Delete",
            ["KeyInsert"] = "Insert",
            ["KeyPageUp"] = "PageUp",
            ["KeyPageDown"] = "PageDown",
            ["KeyCapsLock"] = "CapsLock",
            ["KeyNumLock"] = "NumLock",
            ["KeyForward"] = "前进键",
            ["KeyBack"] = "后退键",

            // 符号键：引擎返回键帽上的双字符
            ["KeyMinus"] = "_-",
            ["KeyEquals"] = "+=",
            ["KeyLeftBracket"] = "[{",
            ["KeyRightBracket"] = "]}",
            ["KeySemicolon"] = ":;",
            ["KeyApostrophe"] = "'\"",
            ["KeyComma"] = "<,",
            ["KeyPeriod"] = ">.",
            ["KeySlash"] = "?/",

            // 左右修饰键
            ["KeyLeftShift"] = "左Shift",
            ["KeyRightShift"] = "右Shift",
            ["KeyLeftCtrl"] = "左Ctrl",
            ["KeyRightCtrl"] = "右Ctrl",
            ["KeyLeftAlt"] = "左Alt",
            ["KeyRightAlt"] = "右Alt",
            ["KeyLeftWin"] = "Win",
            ["KeyRightWin"] = "Win",
        };

        /// <summary>不参与映射的按钮（无需映射的装饰性按钮）。</summary>
        private static readonly HashSet<string> UnmappedNames = new();

        /// <summary>
        /// 遍历页面所有按钮自动建立"键名 -> 按钮"映射：
        /// 键名默认取按钮 Content；小键盘数字键由 x:Name 推导（KeyNum0 → Num0），
        /// 小键盘符号键由 NameOverrides 纠正（Content 与主键区重复）。
        /// 以后在 XAML 里加新按键按钮即可自动生效，无需改本文件。
        /// </summary>
        private void BuildKeyMap()
        {
            if (_keyButtonMap.Count > 0) return;

            foreach (var btn in FindVisualChildren<Button>(this))
            {
                if (string.IsNullOrEmpty(btn.Name) || UnmappedNames.Contains(btn.Name)) continue;

                string keyName;
                if (btn.Name.Length == 7 && btn.Name.StartsWith("KeyNum") && char.IsDigit(btn.Name[6]))
                    keyName = "Num" + btn.Name[6];                       // KeyNum0-9 → Num0-9
                else if (!NameOverrides.TryGetValue(btn.Name, out keyName))
                    keyName = btn.Content as string ?? "";

                if (string.IsNullOrEmpty(keyName)) continue;

                _keyButtonMap[keyName] = btn;
                _buttonToName[btn] = keyName;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var c in FindVisualChildren<T>(child))
                    yield return c;
            }
        }

        private void BindClickEvents()
        {
            foreach (var kvp in _keyButtonMap)
            {
                kvp.Value.Click -= KeyButton_Click;
                kvp.Value.Click += KeyButton_Click;
            }
        }

        // ============ 点击 ============

        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!_buttonToName.TryGetValue(btn, out var keyName)) return;

            // 被其他功能占用的键：点击 = 跳转到占用它的功能的触发键页
            if ((string?)btn.Tag == OccupiedTag)
            {
                if (_occupiedOwner.TryGetValue(btn, out var owner))
                    OccupiedKeyClicked?.Invoke(this, owner);
                return;
            }

            if (_isMultiSelectMode)
            {
                if (_selectedKeys.Contains(keyName))
                {
                    _selectedKeys.Remove(keyName);
                    Deselect(btn);
                }
                else
                {
                    _selectedKeys.Add(keyName);
                    Select(btn);
                }
                PauseKeysChanged?.Invoke(this, string.Join(",", _selectedKeys));
                return;
            }

            if (_selectedButton == btn)
            {
                Deselect(btn);
                _selectedButton = null;
                _currentKeyName = "";
                KeySelected?.Invoke(this, "未设置");
                return;
            }

            if (_selectedButton != null) Deselect(_selectedButton);

            Select(btn);
            _selectedButton = btn;
            _currentKeyName = keyName;
            KeySelected?.Invoke(this, keyName);
        }

        // ============ 视觉 ============

        private void Select(Button btn)
        {
            // 清掉历史遗留的本地值，否则会盖掉样式 Trigger 里的选中色
            btn.ClearValue(Button.BackgroundProperty);
            btn.ClearValue(Button.ForegroundProperty);
            btn.ClearValue(Button.BorderBrushProperty);

            btn.Tag = SelectedTag;
        }

        private void Deselect(Button btn)
        {
            btn.ClearValue(Button.BackgroundProperty);
            btn.ClearValue(Button.ForegroundProperty);
            btn.ClearValue(Button.BorderBrushProperty);

            btn.ClearValue(FrameworkElement.TagProperty);
        }

        // ============ 单选 ============

        public void LoadKey(string keyName)
        {
            if (string.IsNullOrEmpty(keyName) || keyName == "未设置") return;

            if (_keyButtonMap.Count == 0)
            {
                _pendingLoadKey = keyName;
                return;
            }

            // 已禁用的键（如快速传送的"右键"）不再回显为选中，避免"灰按钮却是选中态"
            if (_disabledKeys.Contains(keyName))
            {
                if (_selectedButton != null)
                {
                    Deselect(_selectedButton);
                    _selectedButton = null;
                }
                _currentKeyName = "";
                return;
            }

            if (_keyButtonMap.TryGetValue(keyName, out var btn))
            {
                if (_selectedButton != null) Deselect(_selectedButton);
                Select(btn);
                _selectedButton = btn;
                _currentKeyName = keyName;
            }
        }

        public string GetSelectedKeyName()
            => string.IsNullOrEmpty(_currentKeyName) ? "未设置" : _currentKeyName;

        // ============ 多选 ============

        public void SetMultiSelectMode(bool multi)
        {
            _isMultiSelectMode = multi;
            if (multi) _selectedKeys.Clear();
        }

        public void LoadPauseKeys(string keys)
        {
            _isMultiSelectMode = true;
            _selectedKeys.Clear();

            if (_keyButtonMap.Count == 0)
            {
                _pendingLoadPauseKeys = keys;
                return;
            }

            foreach (var kvp in _keyButtonMap)
                Deselect(kvp.Value);

            _selectedButton = null;

            if (string.IsNullOrEmpty(keys)) return;

            foreach (var raw in keys.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var key = raw.Trim();
                if (string.IsNullOrEmpty(key)) continue;

                // 已禁用的键（如快速传送的"右键"）不回显为选中
                if (_disabledKeys.Contains(key)) continue;

                _selectedKeys.Add(key);

                if (_keyButtonMap.TryGetValue(key, out var btn))
                    Select(btn);
            }
        }

        // ============ 禁用某些键 ============

        /// <summary>
        /// 逻辑键 → 页面上实际的键名集合。
        /// 同一个物理键在页面上可能有"左/右"两个按钮，引擎侧键名也不同
        /// （Ctrl / 左Ctrl / 右Ctrl、Alt / 左Alt / 右Alt），
        /// 只传 "Ctrl" 这类总称时要展开成具体键名才能全部禁掉。
        /// </summary>
        private static readonly Dictionary<string, string[]> KeyNameGroups = new()
        {
            ["Ctrl"] = new[] { "Ctrl", "左Ctrl", "右Ctrl" },
            ["Alt"] = new[] { "Alt", "左Alt", "右Alt" },
            ["Shift"] = new[] { "Shift", "左Shift", "右Shift" },
            ["Win"] = new[] { "Win" },   // 左右 Win 在页面上同名，靠遍历按钮一并禁用
        };

        /// <summary>
        /// 禁用指定按键（不可点击，字体显示为红色）。
        /// 传入的键名可以是总称（"Win" / "Alt" / "Ctrl"）或具体键名（"左Ctrl"、"右键"）。
        /// 每次调用会先清除之前的禁用状态，再应用新的。
        /// 如果在 Loaded 前调用，会暂存到 _pendingDisabledKeys，等 Loaded 后再应用。
        /// </summary>
        public void SetDisabledKeys(params string[] disabledKeys)
        {
            // 若映射未建，暂存（等 Loaded 后应用）
            if (_keyButtonMap.Count == 0)
            {
                _pendingDisabledKeys = disabledKeys;
                return;
            }

            _disabledKeys.Clear();

            // 先全部启用（按按钮遍历，避免同名按钮漏掉）
            foreach (var kvp in _buttonToName)
            {
                kvp.Key.IsEnabled = true;
            }

            if (disabledKeys == null || disabledKeys.Length == 0) return;

            // 展开键组
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in disabledKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;

                if (KeyNameGroups.TryGetValue(key, out var group))
                {
                    foreach (var g in group) names.Add(g);
                }
                else
                {
                    names.Add(key);
                }
            }

            // 同名按钮（左右 Win 都叫 "Win"）也要一起禁用，所以遍历"按钮 -> 键名"
            foreach (var kvp in _buttonToName)
            {
                var btn = kvp.Key;
                string keyName = kvp.Value;

                if (!names.Contains(keyName)) continue;

                _disabledKeys.Add(keyName);

                btn.IsEnabled = false;

                if (_selectedButton == btn)
                {
                    Deselect(btn);
                    _selectedButton = null;
                    _currentKeyName = "";
                }
                _selectedKeys.Remove(keyName);
            }
        }

        // ============ 被其他功能占用 ============

        /// <summary>
        /// 把「其他功能已经设置过的触发键」标记为不可选，并在按钮背景显示那个功能的图标，
        /// 让用户一眼看出这个键被谁用了（例如剧情对话用了「←」，其他功能打开触发键页时
        /// 「←」按钮的背景就是 剧情对话.png）。
        ///
        /// 排除项：
        /// - 当前功能自己（自己已选的键保持选中样式，不做任何标记）；
        /// - 手柄拾取：它用的是手柄按键页，键位体系和键盘页完全不同，不参与冲突。
        /// </summary>
        /// <param name="currentModuleName">当前正在设置的功能名（ModuleRegistry 的 Name）。</param>
        public void SetOccupiedKeys(string currentModuleName)
        {
            if (_keyButtonMap.Count == 0)
            {
                _pendingOccupiedModule = currentModuleName;
                return;
            }

            ClearOccupiedKeys();

            if (string.IsNullOrEmpty(currentModuleName)) return;

            var settings = ConfigManager.Get<HomePageSettings>();

            // 键名 -> 占用它的功能
            var occupied = new Dictionary<string, ModuleInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in ModuleRegistry.All)
            {
                if (module.Name == currentModuleName) continue;
                if (module.UseGamepad) continue;          // 手柄拾取不参与

                var key = module.GetKey(settings);
                if (string.IsNullOrEmpty(key) || key == "未设置") continue;

                // 旧配置里万一有重复，后注册的功能覆盖前面的（只是显示，不影响引擎）
                occupied[key] = module;
            }

            if (occupied.Count == 0) return;

            foreach (var kvp in _buttonToName)
            {
                var btn = kvp.Key;
                string keyName = kvp.Value;

                if (!occupied.TryGetValue(keyName, out var module)) continue;

                // 全局禁选键（Win/Alt/Ctrl/右键）优先保持"禁用"样式：
                // 它已经不可点了，再标成"被 X 占用、可点击跳转"会互相矛盾（点了没反应）
                if (_disabledKeys.Contains(keyName)) continue;

                // 当前功能自己已选的键不标占用（保持选中样式）
                if (btn == _selectedButton) continue;

                MarkOccupied(btn, module);
            }
        }

        private void MarkOccupied(Button btn, ModuleInfo module)
        {
            if (!string.IsNullOrEmpty(module.IconPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(module.IconPath, UriKind.RelativeOrAbsolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;   // 立刻加载，避免文件句柄被占用
                    bmp.EndInit();
                    KeyButtonState.SetOccupiedIcon(btn, bmp);
                }
                catch
                {
                    // 图标加载失败不影响可用性：退化为纯"不可选"样式
                }
            }

            // 不设 IsEnabled=false：禁用元素不触发 MouseEnter，ButtonTip 会弹不出来。
            // 改为保持可命中的 Tag=Occupied 样式（Cursor=No），点击在 KeyButton_Click 里拦截。
            btn.Tag = OccupiedTag;
            _occupiedOwner[btn] = module.Name;                 // 点击跳转要知道占用者是谁
            ButtonTip.SetText(btn, $"已被「{module.Name}」占用，点击跳转");

            _occupiedButtons.Add(btn);

            // 多选模式下（暂停键/开图键不会走到这里，保险起见）从选中集合里剔除
            _selectedKeys.Remove(_buttonToName.TryGetValue(btn, out var n) ? n : "");
        }

        /// <summary>清除本页所有占用标记（切页/重设时调用）。</summary>
        private void ClearOccupiedKeys()
        {
            foreach (var btn in _occupiedButtons)
            {
                btn.ClearValue(FrameworkElement.TagProperty);
                ButtonTip.SetText(btn, null);
                KeyButtonState.SetOccupiedIcon(btn, null);
            }
            _occupiedButtons.Clear();
            _occupiedOwner.Clear();
        }
    }
}