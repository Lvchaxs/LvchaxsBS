using System;
using System.Windows;
using System.Windows.Input;

namespace LvchaxsBS.UI.Dialogs
{
    /// <summary>
    /// 通用确认弹窗（自旧项目移植，改用新项目主题资源）。
    /// 用法：
    ///   var dlg = new ConfirmDialog("标题", "内容");
    ///   dlg.Owner = Window.GetWindow(this);
    ///   if (dlg.ShowDialog() == true) { ... }
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        /// <summary>复选框勾选状态变化（true=勾上，false=取消勾）</summary>
        public event Action<bool>? OptionCheckedChanged;

        /// <summary>复选框是否勾选（未显示时恒为 false）</summary>
        public bool IsOptionChecked =>
            OptionCheckBox.Visibility == Visibility.Visible
            && OptionCheckBox.IsChecked == true;

        public ConfirmDialog(string title, string message,
                             string okText = "确定", string cancelText = "取消")
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;

            // 回车 = 确定，Esc = 取消
            OkBtn.IsDefault = true;
            CancelBtn.IsCancel = true;

            // 复选框变化 → 对外抛事件
            OptionCheckBox.Checked += (s, e) => OptionCheckedChanged?.Invoke(true);
            OptionCheckBox.Unchecked += (s, e) => OptionCheckedChanged?.Invoke(false);

            // 支持拖动窗口
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
        }

        /// <summary>
        /// 覆盖复选框的显示方式。由调用处在 ShowDialog 之前调用。
        /// 不调用则保持 XAML 默认：显示、文案为空、未勾选。
        /// </summary>
        public void SetOptionCheckBox(bool visible = true,
                                      string content = "",
                                      bool isChecked = false)
        {
            OptionCheckBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            OptionCheckBox.Content = content;
            OptionCheckBox.IsChecked = isChecked;
        }

        /// <summary>
        /// 在正文下方显示一块可滚动的附加内容（用于「新版本改动」等）。
        /// <paramref name="content"/> 为空则隐藏该区域。
        /// </summary>
        public void SetExtraContent(string? title, string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                ExtraContentBorder.Visibility = Visibility.Collapsed;
                return;
            }

            ExtraTitleText.Text = title ?? string.Empty;
            ExtraTitleText.Visibility = string.IsNullOrWhiteSpace(title)
                ? Visibility.Collapsed
                : Visibility.Visible;

            ExtraContentText.Text = content;
            ExtraContentBorder.Visibility = Visibility.Visible;

            // 带改动内容时略加宽，长条目不至于频繁折行
            Width = 420;
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
