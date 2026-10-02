using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LvchaxsBS.UI.Controls
{
    public partial class DropDownSelect : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<string>), typeof(DropDownSelect),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty SelectedIndexProperty =
            DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(DropDownSelect),
                new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIndexChanged));

        public static readonly DependencyProperty DropDirectionProperty =
            DependencyProperty.Register(nameof(DropDirection), typeof(int), typeof(DropDownSelect),
                new PropertyMetadata(0));

        public IEnumerable<string>? ItemsSource
        {
            get => (IEnumerable<string>?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public int SelectedIndex
        {
            get => (int)GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public int DropDirection
        {
            get => (int)GetValue(DropDirectionProperty);
            set => SetValue(DropDirectionProperty, value);
        }

        public event EventHandler<int>? SelectionChanged;

        private Popup? _popup;

        public DropDownSelect()
        {
            InitializeComponent();
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DropDownSelect c) c.UpdateSelectedText();
        }

        private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DropDownSelect c) c.UpdateSelectedText();
        }

        private void UpdateSelectedText()
        {
            if (PART_SelectedText == null) return;
            var list = ItemsSource;
            if (list == null) { PART_SelectedText.Text = ""; return; }

            var arr = new List<string>(list);
            if (SelectedIndex >= 0 && SelectedIndex < arr.Count)
                PART_SelectedText.Text = arr[SelectedIndex];
            else
                PART_SelectedText.Text = arr.Count > 0 ? arr[0] : "";
        }

        private void MainButton_Click(object sender, RoutedEventArgs e)
        {
            if (_popup != null && _popup.IsOpen)
            {
                ClosePopup();
                return;
            }
            OpenPopup();
        }

        private void OpenPopup()
        {
            if (ItemsSource == null) return;

            var stack = new StackPanel();
            var list = new List<string>(ItemsSource);

            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var btn = new Button
                {
                    Content = list[i],
                    Style = (Style)Application.Current.Resources["SimpleDropDownItemStyle"]
                };

                if (i == SelectedIndex)
                    btn.Foreground = (Brush)Application.Current.Resources["PrimaryBrush"];

                btn.Click += (s, e2) =>
                {
                    SelectedIndex = idx;
                    SelectionChanged?.Invoke(this, idx);
                    ClosePopup();
                };
                stack.Children.Add(btn);
            }

            var border = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderStrongBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                MinWidth = ActualWidth
            };
            border.Child = stack;

            bool up = DropDirection == 1;

            _popup = new Popup
            {
                Child = border,
                PlacementTarget = PART_MainButton,
                Placement = up ? PlacementMode.Top : PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                VerticalOffset = up ? -2 : 2
            };

            // 箭头动画（找模板里的 ArrowRotate）
            AnimateArrow(up ? (up ? -180 : 180) : 180);

            _popup.Closed += (s, e2) =>
            {
                AnimateArrow(0);
            };

            _popup.IsOpen = true;
        }

        /// <summary>从 Button 模板里找 ArrowRotate 做动画</summary>
        private void AnimateArrow(double angle)
        {
            if (PART_MainButton == null) return;

            // 从模板里找 ArrowRotate
            var rotate = PART_MainButton.Template?.FindName("ArrowRotate", PART_MainButton) as RotateTransform;
            if (rotate == null) return;

            var anim = new DoubleAnimation
            {
                To = angle,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void ClosePopup()
        {
            if (_popup != null)
            {
                _popup.IsOpen = false;
            }
        }
    }
}