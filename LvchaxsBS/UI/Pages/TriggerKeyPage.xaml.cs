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
        /// 组合键被"微调"（点左 Ctrl / 左 Alt 加、减修饰键），参数 = 拼好的完整键名。
        /// <para>★ 与 <see cref="KeySelected"/> 的区别：这只是**调**，不是"选完了"，
        ///   订阅方应该保存但**不要关页面** —— 用户还要接着点 Alt 或换基础键。</para>
        /// </summary>
        public event EventHandler<string>? ComboAdjusted;

        /// <summary>
        /// 用户点了**已经选中**的那个键（= 取消当前设置）。
        /// <para>★ 与 <see cref="KeySelected"/> 的区别：取消不算"改好了"，
        ///   订阅方应该只清配置、**不要关页面** —— 用户还要接着选别的键。</para>
        /// </summary>
        public event EventHandler? KeyCleared;

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

        /// <summary>待应用的"禁用原因"（键名 → 占用者显示名），Loaded 后再套到按钮上。</summary>
        private Dictionary<string, string>? _pendingDisabledKeyOwners;

        // ===== 组合键（Ctrl / Alt 当修饰键）=====
        // 只有"功能触发键"页会打开它；开图键 / 暂停键 / 快速切图地区键都不开。

        private const string ComboTag = "Combo";
        private const string ComboActiveTag = "ComboActive";

        /// <summary>程序关闭快捷键（Ctrl+F12）的基础键。</summary>
        private const string CloseHotkeyKey = "F12";

        /// <summary>撞上关闭快捷键时的提示。</summary>
        private const string CloseHotkeyMessage = "CTRL+F12 是程序关闭快捷键，请使用其他按键组合";

        /// <summary>组合键模式是否开启。</summary>
        public bool EnableComboModifiers { get; private set; }

        /// <summary>Ctrl / Alt 的按钮 → 它代表的修饰键（"Ctrl" / "Alt"）。</summary>
        private readonly Dictionary<Button, string> _comboButtons = new();

        /// <summary>当前激活的修饰键（"Ctrl" / "Alt"）。</summary>
        private readonly HashSet<string> _activeModifiers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 禁止选中的**完整键名** → 占用者名字（如 "左Ctrl+1" → "蒙德"）。
        /// <para>既能拦住"点了才发现冲突"，也能在按钮上预先标红 + 写占用者。</para>
        /// </summary>
        private readonly Dictionary<string, string> _forbiddenCombos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>待应用的组合键黑名单（Loaded 前调用时暂存）。</summary>
    private Dictionary<string, string>? _pendingForbiddenCombos;

    /// <summary>
    /// 「可共用」的占用标注：键名 → 按钮底部显示名（如 <c>"F8" → "3个宏"</c>）。
    /// <para>★ 与 <see cref="_forbiddenCombos"/> 的区别：这些键**不禁用**，只是灰底红字 + 底部小字标注。</para>
    /// </summary>
    private readonly Dictionary<string, string> _sharedKeyOwners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>待应用的共用标注（Loaded 前调用时暂存）。</summary>
    private Dictionary<string, string>? _pendingSharedKeyOwners;


        /// <summary>开启组合键模式。★ 必须在 SetDisabledKeys 之前调用，否则 Ctrl/Alt 会先被禁掉。</summary>
        public void EnableComboModifierMode()
        {
            EnableComboModifiers = true;
            BuildComboButtons();
        }

        /// <summary>
        /// 设置"不许选"的完整键名 → 占用者（例如 "左Alt+2" → "璃月"）。
        /// <para>既用来拦"点了才发现冲突"，也用来在按钮上预先标红 + 写占用者名字。</para>
        /// <para>传入的应该是**别的**实体占用的键，本实体自己已选的键要由调用方排除。</para>
        /// </summary>
        public void SetForbiddenCombos(IDictionary<string, string>? combos)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (combos != null)
            {
                foreach (var kv in combos)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key)) map[kv.Key.Trim()] = kv.Value ?? "";
                }
            }

            if (_keyButtonMap.Count == 0)
            {
                _pendingForbiddenCombos = map;
                return;
            }

            _forbiddenCombos.Clear();
            foreach (var kv in map) _forbiddenCombos[kv.Key] = kv.Value;

        }


        /// <summary>
        /// 把「左Ctrl / 左Alt」两个按钮收进组合键表。
        /// <para>★ 只收左侧：右 Ctrl / 右 Alt 不参与组合键，它们和 Win / 右键一样保持禁用。</para>
        /// </summary>
        private void BuildComboButtons()
        {
            if (!EnableComboModifiers || _keyButtonMap.Count == 0) return;

            _comboButtons.Clear();
            foreach (var kvp in _buttonToName)
            {
                if (kvp.Value.Equals("左Ctrl", StringComparison.OrdinalIgnoreCase))
                    _comboButtons[kvp.Key] = "Ctrl";
                else if (kvp.Value.Equals("左Alt", StringComparison.OrdinalIgnoreCase))
                    _comboButtons[kvp.Key] = "Alt";
            }
        }

        /// <summary>
        /// 配置里的修饰键名 → 页面上能表示的修饰键（"Ctrl" / "Alt"）。
        /// <para>★ 只映射左侧：可点的组合键按钮只有「左Ctrl / 左Alt」，
        ///   右 Ctrl / 右 Alt 是禁用键（组合键不含右侧）。</para>
        /// <para>"Ctrl" / "Alt" 这种通用写法（老配置）按左侧处理 —— 那是页面上唯一能显示的状态。</para>
        /// </summary>
        private static string? NormalizeModifier(string keyName)
        {
            if (keyName.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || keyName.Equals("左Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                return "Ctrl";
            }

            if (keyName.Equals("Alt", StringComparison.OrdinalIgnoreCase)
                || keyName.Equals("左Alt", StringComparison.OrdinalIgnoreCase))
            {
                return "Alt";
            }

            return null;
        }

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

        /// <summary>
        /// 「共用」标记：这个键已被其他宏占用，但宏之间允许共用同一个触发键。
        /// <para>观感同禁用（灰底红字 + 按钮底部占用者小字），但可点；由 KeyButtonStyle 的 Trigger(Tag=Shared) 消费。</para>
        /// </summary>
        private const string SharedTag = "Shared";

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

            // 组合键表依赖按键映射，映射建好后再补一次
            BuildComboButtons();

            // 应用暂存的组合键黑名单
            if (_pendingForbiddenCombos != null)
            {
                var list = _pendingForbiddenCombos;
                _pendingForbiddenCombos = null;
                SetForbiddenCombos(list);
            }

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
            // 应用暂存的"禁用原因"（必须在 SetDisabledKeys 之后，只标真正被禁用的键）
            if (_pendingDisabledKeyOwners != null)
            {
                var owners = _pendingDisabledKeyOwners;
                _pendingDisabledKeyOwners = null;
                SetDisabledKeyOwners(owners);
            }
        // 应用暂存的占用
        if (!string.IsNullOrEmpty(_pendingOccupiedModule))
        {
            var m = _pendingOccupiedModule;
            _pendingOccupiedModule = null;
            SetOccupiedKeys(m);
        }
        // 应用暂存的共用标注（放在禁用/占用之后：只标可用的键）
        if (_pendingSharedKeyOwners != null)
        {
            var owners = _pendingSharedKeyOwners;
            _pendingSharedKeyOwners = null;
            SetSharedKeyOwners(owners);
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

            foreach (var btn in VisualTreeEx.FindChildren<Button>(this))
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

            // 组合键模式下的 Ctrl / Alt：点一下切换"是否参与组合"，不当作普通键
            if (EnableComboModifiers && _comboButtons.TryGetValue(btn, out var modifier))
            {
                ToggleComboModifier(modifier);
                return;
            }

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

                // ★ 只取消**基础键**，已激活的组合键（左Ctrl / 左Alt）要留着：
                //   用户只是想换个基础键，组合部分不用重新勾一遍。
                //   （这里别 Clear _activeModifiers，否则取消一次组合键就跟着没了。）
                ApplyComboVisuals();

                // ★ 取消 ≠ 改好了：走 KeyCleared（订阅方清配置但不关页面），
                //   不用 KeySelected("未设置")，否则页面会当成"选完了"直接关掉。
                KeyCleared?.Invoke(this, EventArgs.Empty);
                return;
            }

            // 组合键模式下要按"拼出来的完整键名"查冲突（单键冲突靠禁用集合，这里补组合键）
            if (EnableComboModifiers)
            {
                string preview = ComposeKey(keyName);
                if (_forbiddenCombos.ContainsKey(preview))
                {
                    ToastService.Show("触发键", $"「{preview}」已经被占用，换一个键");
                    return;
                }
            }

            if (_selectedButton != null) Deselect(_selectedButton);

            Select(btn);
            _selectedButton = btn;
            _currentKeyName = keyName;
            RaiseComposedKey();
        }

        // ============ 组合键（Ctrl / Alt 修饰）============

        /// <summary>
        /// 当前选中的组合是不是正好「Ctrl+F12」：Ctrl 激活、Alt 没激活、基础键是 F12。
        /// <para>★ Ctrl+Alt+F12 不算 —— 关闭快捷键要求 Alt 没按下（见 <c>App.IsCloseHotkey</c>）。</para>
        /// </summary>
        private bool IsCloseHotkeySelected()
            => EnableComboModifiers
               && _activeModifiers.Contains("Ctrl")
               && !_activeModifiers.Contains("Alt")
               && string.Equals(_currentKeyName, CloseHotkeyKey, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 组合正好是「Ctrl+F12」时给一条**红色**提示。
        /// <para>
        /// ★ 只提示、<b>不拦也不禁用</b>：设成这个组合后按下去只会关掉程序，
        ///   功能/宏根本没机会执行 —— 用户自己看见提示决定要不要换就行。
        /// </para>
        /// </summary>
        private void NotifyIfCloseHotkey()
        {
            if (IsCloseHotkeySelected())
                ToastService.Show("触发键", CloseHotkeyMessage, false);
        }

        private void ToggleComboModifier(string modifier)
        {
            bool adding = !_activeModifiers.Contains(modifier);

            // 拼出来的组合键已经被别人占了 → 这次激活不成立（否则两个地区抢同一个键）
            if (adding && !string.IsNullOrEmpty(_currentKeyName))
            {
                string preview = ComposeKey(_currentKeyName, extra: modifier);
                if (_forbiddenCombos.ContainsKey(preview))
                {
                    ToastService.Show("触发键", $"「{preview}」已经被占用，换一个组合");
                    return;
                }
            }

            if (adding) _activeModifiers.Add(modifier);
            else _activeModifiers.Remove(modifier);

            ApplyComboVisuals();

            // 还没选基础键 → 拼不出完整键名，只能先记住状态。
            // 这时给个明确提示，免得用户以为什么都没发生（页面自然也不会关）。
            if (string.IsNullOrEmpty(_currentKeyName))
            {
                // 组合键还亮着时说明刚取消的是基础键，提示要换成"再选一个基础键"
                ToastService.Show("触发键", _activeModifiers.Count > 0
                    ? "组合键已保留，再选一个基础键"
                    : "先选一个基础键，再点组合键");
                return;
            }

            // ★ 只是"调"修饰键：保存，但页面不关 —— 用户可能还要点 Alt 或换基础键。
            //   真正"选完了"是点基础键（RaiseComposedKey），那次才关。
            RaiseComboAdjusted();
        }

        /// <summary>把"修饰键 + 基础键"拼成完整键名上报给容器（= 选完了，订阅方会关页面）。</summary>
        private void RaiseComposedKey()
        {
            if (string.IsNullOrEmpty(_currentKeyName)) return;
            KeySelected?.Invoke(this, ComposeKey(_currentKeyName));

            // ★ 放最后：订阅方的"已设为 XXX"先弹，这条红色提示盖在上面
            NotifyIfCloseHotkey();
        }

        /// <summary>同上，但走 <see cref="ComboAdjusted"/>：只更新配置，页面留着继续调。</summary>
        private void RaiseComboAdjusted()
        {
            if (string.IsNullOrEmpty(_currentKeyName)) return;
            ComboAdjusted?.Invoke(this, ComposeKey(_currentKeyName));

            NotifyIfCloseHotkey();
        }

        /// <summary>
        /// 拼出配置里要存的完整键名（"左Ctrl+左Alt+V"）。
        /// <paramref name="extra"/> 用于预览：临时多算一个还没激活的修饰键（冲突校验时用）。
        /// </summary>
        private string ComposeKey(string baseKey, string? extra = null)
        {
            if (!EnableComboModifiers || (_activeModifiers.Count == 0 && string.IsNullOrEmpty(extra)))
                return baseKey;

            // ★ 存精确的"左"名：引擎按名字只认左侧修饰键（右 Ctrl / 右 Alt 无效）。
            //   "Ctrl" 这种通用写法虽然也能被引擎识别，但左右都算，不符合"组合键只用左侧"。
            var parts = new List<string>(3);
            if (_activeModifiers.Contains("Ctrl") || extra == "Ctrl") parts.Add("左Ctrl");
            if (_activeModifiers.Contains("Alt") || extra == "Alt") parts.Add("左Alt");
            parts.Add(baseKey);

            return string.Join("+", parts);
        }

        /// <summary>
        /// 刷新 Ctrl / Alt 的外观：
        /// 未激活 = 红字灰底（原来的"禁用"观感）+ 按钮内底部小字"组合键"；
        /// 激活   = 与"当前已选中键"同一套主题色（蓝底蓝字）。
        /// </summary>
        private void ApplyComboVisuals()
        {
            if (!EnableComboModifiers || _comboButtons.Count == 0) return;

            foreach (var kvp in _comboButtons)
            {
                var btn = kvp.Key;
                bool active = _activeModifiers.Contains(kvp.Value);

                // 清掉可能残留的本地值，否则会盖掉样式 Trigger 里的颜色
                btn.ClearValue(Button.BackgroundProperty);
                btn.ClearValue(Button.ForegroundProperty);
                btn.ClearValue(Button.BorderBrushProperty);

                btn.Tag = active ? ComboActiveTag : ComboTag;

                UI.Helpers.KeyButtonState.SetDisabledOwner(btn, "组合键");
                UI.Controls.ButtonTip.SetText(btn, active ? "点击取消组合键" : "点击作为组合键");
            }
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

            // 取消选中后把「共用」标注还回去（否则灰底红字和底部占用者小字会一起掉）
            if (_buttonToName.TryGetValue(btn, out var keyName)
                && _sharedKeyOwners.ContainsKey(keyName))
                btn.Tag = SharedTag;
        }

        // ============ 单选 ============

        public void LoadKey(string keyName)
        {
            if (_keyButtonMap.Count == 0)
            {
                _pendingLoadKey = keyName;
                return;
            }

            LoadKeyCore(keyName);
            ApplyComboVisuals();
        }

        private void LoadKeyCore(string keyName)
        {
            // 组合键：先把 "Ctrl+Alt+V" 拆成修饰键 + 基础键，再按基础键去选中
            _activeModifiers.Clear();

            if (EnableComboModifiers && !string.IsNullOrEmpty(keyName) && keyName.Contains('+'))
            {
                var parts = keyName.Split('+', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        var mod = NormalizeModifier(parts[i].Trim());
                        if (mod != null) _activeModifiers.Add(mod);
                    }

                    keyName = parts[parts.Length - 1].Trim();
                }
            }

            // ★ "未设置" 也要真的把选中清掉：
            //   同一个页面实例被复用、从"有键"切到"没键"时，旧选中不能留在界面上
            //   （快速切图同一页要依次给多个地区设键，就必须走这条）。
            if (string.IsNullOrEmpty(keyName) || keyName == "未设置")
            {
                if (_selectedButton != null)
                {
                    Deselect(_selectedButton);
                    _selectedButton = null;
                }
                _currentKeyName = "";
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
            _sharedKeyOwners.Clear();

            // 先全部启用（按按钮遍历，避免同名按钮漏掉）
            foreach (var kvp in _buttonToName)
            {
                kvp.Key.IsEnabled = true;

                // 共用标注一并擦掉（禁用与共用互斥，不能同时顶在一个键上）
                if (kvp.Key.Tag is string tag && tag == SharedTag)
                    kvp.Key.ClearValue(FrameworkElement.TagProperty);
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

            // ★ 组合键模式下只有「左Ctrl / 左Alt」是修饰键，不能被禁用（样式由 ApplyComboVisuals 负责）。
            //   右 Ctrl / 右 Alt 仍然照旧禁用：组合键只认左侧。
            if (EnableComboModifiers)
            {
                names.Remove("左Ctrl");
                names.Remove("左Alt");
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

        /// <summary>
        /// 给「被禁用」的键在按钮内底部标一行红色小字（谁占用了它，例如"蒙德"）。
        /// <para>
        /// 场景：快速切图一个页面要给十几个地区轮流设键，别的地区用掉的键虽然变红不可点，
        /// 但用户看不出是谁占的 —— 这里把名字写进按钮容器底部。
        /// </para>
        /// <para>
        /// ★ 只标**真正被禁用**的键：传进来的名字若对应一个可选键，会被忽略（否则误导）。
        /// ★ 每次调用都会把所有按钮的原因重写一遍，传 null 即全部清除。
        /// </para>
        /// </summary>
        public void SetDisabledKeyOwners(IDictionary<string, string>? owners)
        {
            if (_keyButtonMap.Count == 0)
            {
                _pendingDisabledKeyOwners = owners == null
                    ? null
                    : new Dictionary<string, string>(owners, StringComparer.OrdinalIgnoreCase);
                return;
            }

            foreach (var kvp in _buttonToName)
            {
                var btn = kvp.Key;
                string keyName = kvp.Value;

                // Ctrl / Alt 的底部小字固定是"组合键"，由 ApplyComboVisuals 管，这里不要覆盖
                if (_comboButtons.ContainsKey(btn)) continue;


                string owner = "";
                if (owners != null
                    && _disabledKeys.Contains(keyName)
                    && owners.TryGetValue(keyName, out var found))
                {
                    owner = found ?? "";
                }

                UI.Helpers.KeyButtonState.SetDisabledOwner(btn, owner);
            }
        }

        /// <summary>
        /// 标注「已被其他宏占用、但允许共用」的键：灰底红字 + 按钮底部红色小字，**不禁用、可点**。
        /// <para>
        /// 场景：自定义宏之间从 2026-10-22 起允许设同一个触发键（同时只能有一个启用），
        /// 选键页要把"这个键已经有主了"告诉用户，但不能挡住他选。
        /// </para>
        /// <para>
        /// ★ 与 <see cref="SetDisabledKeyOwners"/> 互补：那个只标**真正被禁用**的键，这个只标**可用**的键。
        /// ★ 键名用页面上显示的键名（基础键）；组合键由调用方取完基础键再传。
        /// ★ 每次调用都会把上一轮标注全部擦掉重写，传 null 即全部清除。
        /// </para>
        /// </summary>
        public void SetSharedKeyOwners(IDictionary<string, string>? owners)
        {
            if (_keyButtonMap.Count == 0)
            {
                _pendingSharedKeyOwners = owners == null
                    ? null
                    : new Dictionary<string, string>(owners, StringComparer.OrdinalIgnoreCase);
                return;
            }

            // 先清掉上一轮的共用标注（Tag 交还给样式，底部小字由下面统一重写）
            foreach (var btn in _buttonToName.Keys)
            {
                if (btn.Tag is string tag && tag == SharedTag)
                    btn.ClearValue(FrameworkElement.TagProperty);
            }

            _sharedKeyOwners.Clear();
            if (owners != null)
            {
                foreach (var kv in owners)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                        _sharedKeyOwners[kv.Key.Trim()] = kv.Value ?? "";
                }
            }

            foreach (var kvp in _buttonToName)
            {
                var btn = kvp.Key;
                string keyName = kvp.Value;

                // Ctrl / Alt 的底部小字固定是"组合键"，由 ApplyComboVisuals 管，这里不要覆盖
                if (_comboButtons.ContainsKey(btn)) continue;
                // 禁用键不标共用（它已经点不了，再套一层灰底红字只会和 IsEnabled=False 打架）
                if (!btn.IsEnabled) continue;
                // 被其他功能占用的键（图标态）不碰
                if (btn.Tag is string tag && tag == OccupiedTag) continue;

                bool shared = _sharedKeyOwners.TryGetValue(keyName, out var owner);
                UI.Helpers.KeyButtonState.SetDisabledOwner(btn, shared ? owner ?? "" : "");

                // 正被选中的键保持蓝色选中态（Tag=Selected），只留底部小字
                if (shared && !ReferenceEquals(btn, _selectedButton))
                    btn.Tag = SharedTag;
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
        /// <summary>
        /// 取组合键的**基础键**："左Ctrl+F" → "F"；不含 '+' 就原样返回。
        /// <para>页面上一个按钮就是一个触发键，占用标记只能按基础键去匹配。</para>
        /// </summary>
        private static string BaseKeyOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            int plus = key.LastIndexOf('+');
            return plus < 0 ? key : key[(plus + 1)..].Trim();
        }

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

                // ★ 组合键按**基础键**登记：别的模块设了 左Ctrl+F，本页面上的 F 照样要显示"被占用"。
                //   和快速切图那套规则一致 —— 占用只看触发键本身，不看修饰键。
                // 旧配置里万一有重复，后注册的功能覆盖前面的（只是显示，不影响引擎）
                occupied[BaseKeyOf(key)] = module;
            }

            if (occupied.Count == 0) return;

            foreach (var kvp in _buttonToName)
            {
                var btn = kvp.Key;
                string keyName = kvp.Value;

                if (!occupied.TryGetValue(keyName, out var module)) continue;

                // 组合键按钮（Ctrl/Alt）不参与"占用"标记：它有自己的红/蓝两态
                if (_comboButtons.ContainsKey(btn)) continue;

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