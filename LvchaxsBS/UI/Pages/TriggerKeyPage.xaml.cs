using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace LvchaxsBS.UI.Pages
{
    public partial class TriggerKeyPage : Page
    {
        public event EventHandler<string>? KeySelected;
        public event EventHandler<string>? PauseKeysChanged;

        private readonly Dictionary<string, Button> _keyButtonMap = new();
        private readonly Dictionary<Button, string> _buttonToName = new();

        private Button? _selectedButton;
        private string _currentKeyName = "";

        private bool _isMultiSelectMode = false;
        private readonly HashSet<string> _selectedKeys = new(StringComparer.OrdinalIgnoreCase);

        // 待加载（Loaded 前调用）
        private string? _pendingLoadKey;
        private string? _pendingLoadPauseKeys;
        private string[]? _pendingDisabledKeys;

        // 选中样式
        private static readonly Brush SelectedBg = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD));
        private static readonly Brush SelectedFg = Brushes.White;
        private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));

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
        }

        // ============ 建映射 ============

        /// <summary>Content 与键名不一致的按钮（x:Name -> 键名）</summary>
        private static readonly Dictionary<string, string> NameOverrides = new()
        {
            ["KeyNumPlus"] = "Num+",
            ["KeyNumMinus"] = "Num-",
            ["KeyNumMultiply"] = "Num*",
            ["KeyNumDivide"] = "Num/",
            ["KeyNumDecimal"] = "Num.",
            ["KeyNumEnter"] = "NumEnter",
        };

        /// <summary>不参与映射的按钮（右侧修饰键，与左侧同名重复）</summary>
        private static readonly HashSet<string> UnmappedNames = new()
        {
            "KeyRightShift", "KeyRightCtrl", "KeyRightAlt", "KeyRightWin"
        };

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
                if (btn.Name.Length == 6 && btn.Name.StartsWith("KeyNum") && char.IsDigit(btn.Name[5]))
                    keyName = "Num" + btn.Name[5];                       // KeyNum0-9 → Num0-9
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
            btn.ClearValue(Button.BackgroundProperty);
            btn.ClearValue(Button.ForegroundProperty);
            btn.ClearValue(Button.BorderBrushProperty);
            btn.Background = SelectedBg;
            btn.Foreground = SelectedFg;
            btn.BorderBrush = SelectedBorder;
        }

        private void Deselect(Button btn)
        {
            btn.ClearValue(Button.BackgroundProperty);
            btn.ClearValue(Button.ForegroundProperty);
            btn.ClearValue(Button.BorderBrushProperty);
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

                _selectedKeys.Add(key);

                if (_keyButtonMap.TryGetValue(key, out var btn))
                    Select(btn);
            }
        }

        // ============ 禁用某些键 ============

        /// <summary>
        /// 禁用指定按键（不可点击、灰化）。
        /// 传入的键名对应 _keyButtonMap 里的键名，例如 "Win"、"Alt"、"Ctrl"。
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

            // 先全部启用
            foreach (var kvp in _keyButtonMap)
            {
                kvp.Value.IsEnabled = true;
            }

            if (disabledKeys == null || disabledKeys.Length == 0) return;

            foreach (var name in disabledKeys)
            {
                if (string.IsNullOrEmpty(name)) continue;

                if (_keyButtonMap.TryGetValue(name, out var btn))
                {
                    btn.IsEnabled = false;

                    if (_selectedButton == btn)
                    {
                        Deselect(btn);
                        _selectedButton = null;
                        _currentKeyName = "";
                    }
                    _selectedKeys.Remove(name);
                }
            }
        }
    }
}