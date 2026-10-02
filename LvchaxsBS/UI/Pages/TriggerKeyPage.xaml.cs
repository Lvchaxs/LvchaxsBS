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

        private void BuildKeyMap()
        {
            if (_keyButtonMap.Count > 0) return;

            Add("A", KeyA); Add("B", KeyB); Add("C", KeyC); Add("D", KeyD);
            Add("E", KeyE); Add("F", KeyF); Add("G", KeyG); Add("H", KeyH);
            Add("I", KeyI); Add("J", KeyJ); Add("K", KeyK); Add("L", KeyL);
            Add("M", KeyM); Add("N", KeyN); Add("O", KeyO); Add("P", KeyP);
            Add("Q", KeyQ); Add("R", KeyR); Add("S", KeyS); Add("T", KeyT);
            Add("U", KeyU); Add("V", KeyV); Add("W", KeyW); Add("X", KeyX);
            Add("Y", KeyY); Add("Z", KeyZ);

            Add("0", KeyD0); Add("1", KeyD1); Add("2", KeyD2); Add("3", KeyD3); Add("4", KeyD4);
            Add("5", KeyD5); Add("6", KeyD6); Add("7", KeyD7); Add("8", KeyD8); Add("9", KeyD9);

            Add("F1", KeyF1); Add("F2", KeyF2); Add("F3", KeyF3); Add("F4", KeyF4);
            Add("F5", KeyF5); Add("F6", KeyF6); Add("F7", KeyF7); Add("F8", KeyF8);
            Add("F9", KeyF9); Add("F10", KeyF10); Add("F11", KeyF11); Add("F12", KeyF12);

            Add("Esc", KeyEsc);
            Add("Tab", KeyTab);
            Add("Caps", KeyCapsLock);
            Add("Shift", KeyLeftShift);
            Add("Ctrl", KeyLeftCtrl);
            Add("Alt", KeyLeftAlt);
            Add("Win", KeyLeftWin);
            Add("Space", KeySpace);
            Add("Enter", KeyEnter);
            Add("Back", KeyBackspace);
            Add("Del", KeyDelete);
            Add("Ins", KeyInsert);
            Add("Pg↑", KeyPageUp);
            Add("Pg↓", KeyPageDown);

            Add("`~", KeyTilde);
            Add("-", KeyMinus);
            Add("=", KeyEquals);
            Add("[", KeyLeftBracket);
            Add("]", KeyRightBracket);
            Add("\\|", KeyBackslash);
            Add(";", KeySemicolon);
            Add("'", KeyApostrophe);
            Add(",", KeyComma);
            Add(".", KeyPeriod);
            Add("/", KeySlash);

            Add("↑", KeyUp); Add("↓", KeyDown); Add("←", KeyLeft); Add("→", KeyRight);

            Add("左键", MouseLeft);
            Add("中键", MouseMiddle);
            Add("右键", MouseRight);

            Add("Num0", KeyNum0); Add("Num1", KeyNum1); Add("Num2", KeyNum2);
            Add("Num3", KeyNum3); Add("Num4", KeyNum4); Add("Num5", KeyNum5);
            Add("Num6", KeyNum6); Add("Num7", KeyNum7); Add("Num8", KeyNum8);
            Add("Num9", KeyNum9);
            Add("Num+", KeyNumPlus);
            Add("Num-", KeyNumMinus);
            Add("Num*", KeyNumMultiply);
            Add("Num/", KeyNumDivide);
            Add("Num.", KeyNumDecimal);
            Add("NumEnter", KeyNumEnter);
            Add("Num", KeyNumLock);

            Add("前进", KeyForward);
            Add("后退", KeyBack);
        }

        private void Add(string name, Button btn)
        {
            if (btn == null) return;
            _keyButtonMap[name] = btn;
            _buttonToName[btn] = name;
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