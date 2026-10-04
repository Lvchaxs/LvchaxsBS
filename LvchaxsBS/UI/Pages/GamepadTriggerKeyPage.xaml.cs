using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LvchaxsBS.Core;
using LvchaxsBS.Services;

namespace LvchaxsBS.UI.Pages
{
    public partial class GamepadTriggerKeyPage : Page
    {
        public event EventHandler<string>? KeySelected;

        private readonly Dictionary<string, Button> _keyButtonMap = new();
        private readonly Dictionary<Button, string> _buttonToName = new();

        /// <summary>
        /// ABXY 按键内部字母的原始颜色（Xbox 配色：A 绿 / B 红 / X 蓝 / Y 黄）。
        /// 选中时底色变浅红，彩色字母会看不清，所以临时改白；取消选中再还原。
        /// </summary>
        private readonly Dictionary<Button, (TextBlock Text, Brush Color)> _letterColors = new();

        private Button? _selectedButton;
        private string _currentKeyName = "";

        /// <summary>Loaded 之前就被调 LoadKey 时暂存，等按钮映射建好后再应用。</summary>
        private string? _pendingLoadKey;

        /// <summary>XInput 是否由本页开启（离开时按需回收，避免影响"手柄拾取"）。</summary>
        private bool _xinputStartedByPage;

        private static readonly Brush SelectedBg = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x99));
        private static readonly Brush SelectedFg = Brushes.White;
        private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0xE0, 0x66, 0x66));

        public GamepadTriggerKeyPage()
        {
            InitializeComponent();
            Loaded += GamepadTriggerKeyPage_Loaded;
            Unloaded += GamepadTriggerKeyPage_Unloaded;
        }

        private void GamepadTriggerKeyPage_Loaded(object sender, RoutedEventArgs e)
        {
            BuildKeyMap();
            BindClickEvents();

            // 打开本页期间要保证手柄输入可用：
            // Xbox 手柄依赖 XInput 轮询，而它平时只在"手柄拾取"开启时才启动。
            if (!GlobalGamepadHookService.XInputEnabled)
            {
                GlobalGamepadHookService.StartXInput();
                _xinputStartedByPage = true;
            }

            GlobalGamepadHookService.GamepadEvent -= OnGamepadEvent;
            GlobalGamepadHookService.GamepadEvent += OnGamepadEvent;

            // 应用"容器页在 Navigate 之前"传入的当前键
            if (!string.IsNullOrEmpty(_pendingLoadKey))
            {
                var k = _pendingLoadKey;
                _pendingLoadKey = null;
                LoadKey(k);
            }
        }

        private void GamepadTriggerKeyPage_Unloaded(object sender, RoutedEventArgs e)
        {
            GlobalGamepadHookService.GamepadEvent -= OnGamepadEvent;

            // 只回收"本页开启的"XInput；若手柄拾取正在监听则必须保持开启
            if (_xinputStartedByPage && !ControllerPickupLogic.IsRunning)
            {
                GlobalGamepadHookService.StopXInput();
            }
            _xinputStartedByPage = false;
        }

        // ============ 真实手柄按键 ============

        private void OnGamepadEvent(object? sender, GamepadEventArgs args)
        {
            if (!args.IsPressed) return;

            string keyName = args.Button.ToString();

            // 手柄事件来自轮询/钩子线程，切回 UI 线程再改界面
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded) return;
                SelectKey(keyName, raiseEvent: true);
            }));
        }

        // ============ 建映射 / 绑事件 ============

        /// <summary>
        /// 遍历页面所有按钮自动建映射：键名 = x:Name 去掉 "Gamepad" 前缀，
        /// 与 <see cref="GamepadButton"/> 枚举名一致（GamepadB → "B"）。
        /// 在 XAML 里加新按键即可自动生效，无需改本文件。
        /// </summary>
        private void BuildKeyMap()
        {
            foreach (var btn in FindVisualChildren<Button>(this))
            {
                if (string.IsNullOrEmpty(btn.Name) || !btn.Name.StartsWith("Gamepad")) continue;

                string keyName = btn.Name["Gamepad".Length..];
                if (keyName.Length == 0) continue;

                _keyButtonMap[keyName] = btn;
                _buttonToName[btn] = keyName;

                // ABXY 的字母是 TextBlock（带 Xbox 配色），记下原色以便选中/还原
                if (btn.Content is TextBlock tb && tb.Foreground is not null)
                    _letterColors[btn] = (tb, tb.Foreground);
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

        // ============ 选择 ============

        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!_buttonToName.TryGetValue(btn, out var keyName)) return;

            // 点击已选中的键 = 取消
            if (_selectedButton == btn)
            {
                Deselect(btn);
                _selectedButton = null;
                _currentKeyName = "";
                UpdateCurrentKeyText();
                KeySelected?.Invoke(this, "未设置");
                return;
            }

            SelectKey(keyName, raiseEvent: true);
        }

        /// <summary>选中某个键。<paramref name="raiseEvent"/>=false 用于初始化，只高亮不触发保存。</summary>
        private bool SelectKey(string keyName, bool raiseEvent)
        {
            if (!_keyButtonMap.TryGetValue(keyName, out var btn)) return false;
            if (_selectedButton == btn) return true;

            if (_selectedButton != null) Deselect(_selectedButton);

            Select(btn);
            _selectedButton = btn;
            _currentKeyName = keyName;
            UpdateCurrentKeyText();

            if (raiseEvent) KeySelected?.Invoke(this, keyName);
            return true;
        }

        private void Select(Button btn)
        {
            btn.ClearValue(Button.BackgroundProperty);
            btn.ClearValue(Button.ForegroundProperty);
            btn.ClearValue(Button.BorderBrushProperty);
            btn.Background = SelectedBg;
            btn.Foreground = SelectedFg;
            btn.BorderBrush = SelectedBorder;

            // ABXY：字母改白，避免浅红底上看不清彩色字母
            if (_letterColors.TryGetValue(btn, out var letter))
                letter.Text.Foreground = SelectedFg;
        }

        private void Deselect(Button btn)
        {
            btn.ClearValue(Button.BackgroundProperty);
            btn.ClearValue(Button.ForegroundProperty);
            btn.ClearValue(Button.BorderBrushProperty);

            // 还原字母的 Xbox 配色
            if (_letterColors.TryGetValue(btn, out var letter))
                letter.Text.Foreground = letter.Color;
        }

        private void UpdateCurrentKeyText()
        {
            if (CurrentKeyText == null) return;

            CurrentKeyText.Text = string.IsNullOrEmpty(_currentKeyName)
                ? "当前设置：未设置"
                : $"当前设置：{_currentKeyName}";
        }

        // ============ 对外接口 ============

        public void LoadKey(string keyName)
        {
            if (string.IsNullOrEmpty(keyName) || keyName == "未设置") return;

            // 映射还没建好（容器页在 Navigate 之前就调用了）→ 暂存，等 Loaded 后应用
            if (_keyButtonMap.Count == 0)
            {
                _pendingLoadKey = keyName;
                return;
            }

            SelectKey(keyName, raiseEvent: false);
        }

        public string GetSelectedKeyName()
            => string.IsNullOrEmpty(_currentKeyName) ? "未设置" : _currentKeyName;
    }
}
