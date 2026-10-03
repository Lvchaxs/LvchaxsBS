using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LvchaxsBS.UI.Pages
{
    public partial class GamepadTriggerKeyPage : Page
    {
        public event EventHandler<string>? KeySelected;

        private readonly Dictionary<string, Button> _keyButtonMap = new();
        private readonly Dictionary<Button, string> _buttonToName = new();

        private Button? _selectedButton;
        private string _currentKeyName = "";

        private static readonly Brush SelectedBg = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x99));
        private static readonly Brush SelectedFg = Brushes.White;
        private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0xE0, 0x66, 0x66));

        public GamepadTriggerKeyPage()
        {
            InitializeComponent();
            Loaded += GamepadTriggerKeyPage_Loaded;
        }

        private void GamepadTriggerKeyPage_Loaded(object sender, RoutedEventArgs e)
        {
            BuildKeyMap();
            BindClickEvents();
        }

        /// <summary>
        /// 遍历页面所有按钮自动建映射：键名 = x:Name 去掉 "Gamepad" 前缀。
        /// 在 XAML 里加新手柄按键按钮即可自动生效，无需改本文件。
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

        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!_buttonToName.TryGetValue(btn, out var keyName)) return;

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

        public void LoadKey(string keyName)
        {
            if (string.IsNullOrEmpty(keyName) || keyName == "未设置") return;

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
    }
}