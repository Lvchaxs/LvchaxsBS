using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LvchaxsBS.UI.Controls
{
    public partial class TopCenterBar : UserControl
    {
        public event EventHandler<string>? FunctionClicked;

        // 当前是否右对齐
        private bool _alignRight = false;

        public TopCenterBar()
        {
            InitializeComponent();
            SizeChanged += (s, e) => UpdateOffset(_alignRight, animate: false);
        }

        private void FunctionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                FunctionClicked?.Invoke(this, name);
            }
        }

        /// <summary>
        /// 设置对齐（波浪滑动）
        /// </summary>
        public void SetAlignment(bool alignRight, bool animate = true)
        {
            _alignRight = alignRight;
            UpdateOffset(alignRight, animate);
        }

        /// <summary>
        /// 核心：根据容器宽度计算偏移并应用到 7 个按钮
        /// </summary>
        private void UpdateOffset(bool alignRight, bool animate)
        {
            var container = FunctionButtonsContainer;
            container.UpdateLayout();

            double containerWidth = container.ActualWidth;
            if (containerWidth <= 0) return;

            double totalButtonsWidth = 7 * 43;

            double totalOffset = alignRight ? Math.Max(0, containerWidth - totalButtonsWidth) : 0;

            var transforms = new TranslateTransform[]
            {
                TfQuickTeleport,
                TfQuickPickup,
                TfStoryDialogue,
                TfAutoCook,
                TfFishingAssist,
                TfAutoLumber,
                TfControllerPickup
            };

            int buttonCount = transforms.Length;

            for (int i = 0; i < buttonCount; i++)
            {
                var tf = transforms[i];

                if (!animate)
                {
                    tf.BeginAnimation(TranslateTransform.XProperty, null);
                    tf.X = totalOffset;
                    continue;
                }

                double from = tf.X;
                double to = totalOffset;

                int delay = alignRight ? (buttonCount - 1 - i) * 35 : i * 35;

                var anim = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    BeginTime = TimeSpan.FromMilliseconds(delay)
                };
                tf.BeginAnimation(TranslateTransform.XProperty, anim);
            }
        }
    }
}