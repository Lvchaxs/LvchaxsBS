using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LvchaxsBS.UI.Controls
{
    public partial class VoiceTagItem : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(VoiceTagItem),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty TagColorProperty =
            DependencyProperty.Register(nameof(TagColor), typeof(Brush), typeof(VoiceTagItem),
                new PropertyMetadata(null, OnTagColorChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public Brush? TagColor
        {
            get => (Brush?)GetValue(TagColorProperty);
            set => SetValue(TagColorProperty, value);
        }

        /// <summary>点击标签（显示模式）。</summary>
        public event EventHandler? TagClicked;

        /// <summary>编辑完成，提交新文本。</summary>
        public event EventHandler<string>? TextCommitted;

        /// <summary>点击编辑按钮进入编辑。</summary>
        public event EventHandler? EditStarted;

        private bool _isEditing;

        public VoiceTagItem()
        {
            InitializeComponent();
        }

        private static void OnTagColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VoiceTagItem c && c.PART_TagButton != null)
                c.PART_TagButton.Background = e.NewValue as Brush;
        }

        private void TagButton_Click(object sender, RoutedEventArgs e)
        {
            TagClicked?.Invoke(this, EventArgs.Empty);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            EnterEditMode();
            EditStarted?.Invoke(this, EventArgs.Empty);
        }

        private void EnterEditMode()
        {
            if (_isEditing) return;
            _isEditing = true;

            PART_TagButton.Visibility = Visibility.Collapsed;
            PART_EditButton.Visibility = Visibility.Collapsed;
            PART_EditBorder.Visibility = Visibility.Visible;

            // 编辑框绑定是 OneWay，进入编辑时把当前文本拷进去；
            // 这样中途按 Esc 取消不会污染 Text（标签仍显示原文本）
            PART_EditBox.Text = Text;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                PART_EditBox.Focus();
                PART_EditBox.CaretIndex = PART_EditBox.Text.Length;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void ExitEditMode()
        {
            if (!_isEditing) return;
            _isEditing = false;

            PART_EditBorder.Visibility = Visibility.Collapsed;
            PART_TagButton.Visibility = Visibility.Visible;
            PART_EditButton.Visibility = Visibility.Visible;
        }

        private void Commit()
        {
            string newText = (PART_EditBox.Text ?? "").Trim();

            if (string.IsNullOrEmpty(newText))
            {
                ExitEditMode();
                return;
            }

            Text = newText;
            TextCommitted?.Invoke(this, newText);
            ExitEditMode();
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ExitEditMode();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void EditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isEditing)
                Commit();
        }
    }
}