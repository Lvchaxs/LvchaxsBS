using LvchaxsBS_UID.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LvchaxsBS_UID.Views
{
    public partial class AddEditGroupWindow : Window
    {
        public AccountGroup Group { get; private set; }
        public bool IsEditMode { get; private set; }
        private bool _isClosing = false;

        public delegate void ShowToastDelegate(string title, string message, bool isSuccess = true);
        public event ShowToastDelegate ShowToastRequested;

        public AddEditGroupWindow(AccountGroup group = null)
        {
            InitializeComponent();
            // 独立窗口不会继承主界面的「窗口缩放」，自己跟一下（宿主注入的系数）
            Utils.UI.UiScale.ApplyTo(this);

            if (group == null)
            {
                // 创建新分组
                Group = new AccountGroup();
            }
            else
            {
                // 编辑现有分组
                Group = group;
            }

            IsEditMode = group != null;

            windowTitle.Text = IsEditMode ? "编辑分组" : "添加分组";
            if (IsEditMode)
            {
                groupNameTextBox.Text = group.Name;
                tagNoteTextBox.Text = group.TagNote;
            }

            UpdateWatermark();
            this.Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PlayAnimation("FadeInAnimation", () =>
            {
                mainBorder.Opacity = 1;
                var transform = mainBorder.RenderTransform as ScaleTransform;
                transform.ScaleX = 1;
                transform.ScaleY = 1;
            });
        }

        private void PlayAnimation(string animationKey, Action onCompleted = null)
        {
            if (FindResource(animationKey) is Storyboard animation)
            {
                if (onCompleted != null)
                    animation.Completed += (s, args) => onCompleted();
                animation.Begin(mainBorder);
            }
        }

        private void UpdateWatermark()
        {
            groupNameWatermark.Visibility = string.IsNullOrWhiteSpace(groupNameTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            tagNoteWatermark.Visibility = string.IsNullOrWhiteSpace(tagNoteTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void CloseWithAnimation(bool dialogResult = false)
        {
            if (_isClosing) return;
            _isClosing = true;

            PlayAnimation("FadeOutAnimation", () =>
            {
                DialogResult = dialogResult;
                Close();
            });
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(groupNameTextBox.Text))
            {
                ShowToast("请输入分组名称", false);
                return false;
            }

            if (groupNameTextBox.Text.Length > 20)
            {
                ShowToast("分组名称不能超过20个字符", false);
                return false;
            }

            if (tagNoteTextBox.Text.Length > 50)
            {
                ShowToast("标签备注不能超过50个字符", false);
                return false;
            }

            return true;
        }

        private void ShowToast(string message, bool isSuccess = true)
        {
            ShowToastRequested?.Invoke("输入验证", message, isSuccess);
        }

        // 事件处理方法
        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);
        private void CancelButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);
        private void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateWatermark();
        private void TagNoteTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateWatermark();

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;

            Group.Name = groupNameTextBox.Text.Trim();
            Group.TagNote = tagNoteTextBox.Text.Trim();

            CloseWithAnimation(true);
        }
    }
}