using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Views
{
    /// <summary>
    /// 「新建宏 / 编辑宏」对话框（无边框、圆角、淡入）。
    /// <para>
    /// ★ 和 UID 的「新建分组」窗口同一套交互：标题条可拖动、ESC 取消、名称必填、
    /// 描述可空、点保存才落盘（<c>Config\Macro\macro.json</c>）。
    /// </para>
    /// <para>
    /// ★ 用法：<c>new MacroEditWindow(null)</c> = 新建，<c>new MacroEditWindow(item)</c> = 编辑；
    /// <c>ShowDialog() == true</c> 表示保存成功（调用方刷新列表）。
    /// </para>
    /// </summary>
    public partial class MacroEditWindow : Window
    {
        /// <summary>null = 新建；非 null = 编辑这一条。</summary>
        private readonly MacroItem? _editing;

        public MacroEditWindow() : this(null)
        {
        }

        public MacroEditWindow(MacroItem? editing)
        {
            InitializeComponent();

            _editing = editing;

            if (editing == null)
            {
                WindowTitle.Text = "新建宏";
            }
            else
            {
                WindowTitle.Text = "编辑宏";
                NameTextBox.Text = editing.Name;
                DescTextBox.Text = editing.Description;

                // 编辑时名称已有内容，光标放到末尾
                NameTextBox.CaretIndex = NameTextBox.Text.Length;
            }

            UpdateWatermarks();

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            NameTextBox.Focus();
            NameTextBox.CaretIndex = NameTextBox.Text.Length;

            // 淡入（和宿主的弹窗手感一致）
            Opacity = 0;
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }

        private void Input_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateWatermarks();
            ErrorText.Visibility = Visibility.Collapsed;
        }

        /// <summary>水印：输入框为空时显示提示文字。</summary>
        private void UpdateWatermarks()
        {
            if (NameWatermark != null)
                NameWatermark.Visibility = string.IsNullOrEmpty(NameTextBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;

            if (DescWatermark != null)
                DescWatermark.Visibility = string.IsNullOrEmpty(DescTextBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NameTextBox.Text.Trim();
            if (name.Length == 0)
            {
                ShowError("请输入宏名称");
                NameTextBox.Focus();
                return;
            }

            // 名称是区分宏的唯一线索，重名直接拦下
            string exceptId = _editing?.Id ?? "";
            if (MacroSettingsStore.NameExists(name, exceptId))
            {
                ShowError("已经有同名宏了，换个名字吧");
                NameTextBox.Focus();
                return;
            }

            if (_editing == null)
                MacroSettingsStore.Add(name, DescTextBox.Text);
            else
                MacroSettingsStore.Update(_editing.Id, name, DescTextBox.Text);

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
