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

        private void BuildKeyMap()
        {
            Add("L1", GamepadL1);
            Add("L2", GamepadL2);
            Add("R1", GamepadR1);
            Add("R2", GamepadR2);

            Add("A", GamepadA);
            Add("B", GamepadB);
            Add("X", GamepadX);
            Add("Y", GamepadY);

            Add("DPadUp", GamepadDPadUp);
            Add("DPadDown", GamepadDPadDown);
            Add("DPadLeft", GamepadDPadLeft);
            Add("DPadRight", GamepadDPadRight);

            Add("L3", GamepadL3);
            Add("R3", GamepadR3);

            Add("Create", GamepadCreate);
            Add("Options", GamepadOptions);
            Add("PS", GamepadPS);
            Add("TouchPad", GamepadTouchPad);
            Add("Mic", GamepadMic);
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