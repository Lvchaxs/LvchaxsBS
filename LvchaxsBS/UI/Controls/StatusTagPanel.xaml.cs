using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LvchaxsBS.UI.Controls
{
    /// <summary>状态标签的三种状态（决定配色）</summary>
    public enum TagState
    {
        /// <summary>达标 / 正常（绿）</summary>
        Good,
        /// <summary>未达标 / 异常（红）</summary>
        Bad,
        /// <summary>中性提示（灰）</summary>
        Neutral,
    }

    /// <summary>
    /// 运行状态标签组：匹配度 / 耗时 / 类型。
    /// <para>
    /// 目前只做 UI：构造后处于占位状态（"--"），由外部调用
    /// <see cref="SetResult"/> / <see cref="SetScoreRaw"/> 等方法更新。
    /// 配色统一走主题资源，深浅主题都正确。
    /// </para>
    /// </summary>
    public partial class StatusTagPanel : UserControl
    {
        // ===== 标签名（显示在 "--" 前面）=====

        public static readonly DependencyProperty ScoreLabelProperty =
            DependencyProperty.Register(nameof(ScoreLabel), typeof(string), typeof(StatusTagPanel),
                new PropertyMetadata("匹配度", OnLabelChanged));

        public static readonly DependencyProperty TimeLabelProperty =
            DependencyProperty.Register(nameof(TimeLabel), typeof(string), typeof(StatusTagPanel),
                new PropertyMetadata("耗时", OnLabelChanged));

        public static readonly DependencyProperty SourceLabelProperty =
            DependencyProperty.Register(nameof(SourceLabel), typeof(string), typeof(StatusTagPanel),
                new PropertyMetadata("类型", OnLabelChanged));

        public string ScoreLabel
        {
            get => (string)GetValue(ScoreLabelProperty);
            set => SetValue(ScoreLabelProperty, value);
        }

        public string TimeLabel
        {
            get => (string)GetValue(TimeLabelProperty);
            set => SetValue(TimeLabelProperty, value);
        }

        public string SourceLabel
        {
            get => (string)GetValue(SourceLabelProperty);
            set => SetValue(SourceLabelProperty, value);
        }

        // ===== 显隐 =====

        public static readonly DependencyProperty ShowTimeProperty =
            DependencyProperty.Register(nameof(ShowTime), typeof(bool), typeof(StatusTagPanel),
                new PropertyMetadata(true, OnVisibilityChanged));

        public static readonly DependencyProperty ShowSourceProperty =
            DependencyProperty.Register(nameof(ShowSource), typeof(bool), typeof(StatusTagPanel),
                new PropertyMetadata(true, OnVisibilityChanged));

        public bool ShowTime
        {
            get => (bool)GetValue(ShowTimeProperty);
            set => SetValue(ShowTimeProperty, value);
        }

        public bool ShowSource
        {
            get => (bool)GetValue(ShowSourceProperty);
            set => SetValue(ShowSourceProperty, value);
        }

        public StatusTagPanel()
        {
            InitializeComponent();
            ApplyVisibility();
            ApplyLabels();
            SetEmpty();
        }

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StatusTagPanel p) p.ApplyLabels();
        }

        private static void OnVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StatusTagPanel p) p.ApplyVisibility();
        }

        /// <summary>标签名变化后，若当前显示的还是占位符 "--" 就同步更新前缀。</summary>
        private void ApplyLabels()
        {
            if (ScoreText.Text.EndsWith(": --")) ScoreText.Text = $"{ScoreLabel}: --";
            if (TimeText.Text.EndsWith(": --")) TimeText.Text = $"{TimeLabel}: --";
            if (SourceText.Text.EndsWith(": --")) SourceText.Text = $"{SourceLabel}: --";
        }

        private void ApplyVisibility()
        {
            TimeBorder.Visibility = ShowTime ? Visibility.Visible : Visibility.Collapsed;
            SourceBorder.Visibility = ShowSource ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== 对外更新接口 =====

        /// <summary>恢复占位状态（"匹配度: --"）。</summary>
        public void SetEmpty()
        {
            SetScoreRaw($"{ScoreLabel}: --", TagState.Neutral);
            SetTimeRaw($"{TimeLabel}: --", TagState.Neutral);
            SetSourceRaw($"{SourceLabel}: --", TagState.Neutral);
        }

        /// <summary>
        /// 一次性设置"匹配度 / 耗时 / 类型"。
        /// 匹配度按百分比显示，达到阈值绿、未达到红。
        /// </summary>
        public void SetResult(double matchScore, long elapsedMs, double threshold, string type)
        {
            // 没有有效结果（未检测到 / 分数无效）→ 回到占位状态
            if (matchScore < 0 || string.IsNullOrEmpty(type))
            {
                SetEmpty();
                return;
            }

            SetScoreRaw($"{ScoreLabel}: {matchScore * 100:F2}%",
                        matchScore >= threshold ? TagState.Good : TagState.Bad);
            SetTimeRaw($"{TimeLabel}: {(elapsedMs >= 0 ? elapsedMs + "ms" : "--")}", TagState.Good);
            SetSourceRaw($"{SourceLabel}: {type}", TagState.Good);
        }

        /// <summary>直接改匹配度段。</summary>
        public void SetScoreRaw(string text, TagState state)
        {
            ScoreText.Text = text;
            ApplyState(ScoreBorder, ScoreText, state);
        }

        /// <summary>直接改耗时段。</summary>
        public void SetTimeRaw(string text, TagState state)
        {
            if (!ShowTime) return;
            TimeText.Text = text;
            ApplyState(TimeBorder, TimeText, state);
        }

        /// <summary>直接改类型段。</summary>
        public void SetSourceRaw(string text, TagState state)
        {
            if (!ShowSource) return;
            SourceText.Text = text;
            ApplyState(SourceBorder, SourceText, state);
        }

        /// <summary>按状态套主题色（SetResourceReference：切主题时自动跟随）。</summary>
        private static void ApplyState(Border border, TextBlock text, TagState state)
        {
            string bg, fg;

            switch (state)
            {
                case TagState.Good:
                    bg = "TagGreenBgBrush";
                    fg = "TagGreenFgBrush";
                    break;
                case TagState.Bad:
                    bg = "TagRedBgBrush";
                    fg = "TagRedFgBrush";
                    break;
                default:
                    bg = "Gray100Brush";
                    fg = "TextSecondaryBrush";
                    break;
            }

            border.SetResourceReference(Border.BackgroundProperty, bg);
            text.SetResourceReference(TextBlock.ForegroundProperty, fg);
        }

        // ===== 兼容旧代码：暴露内部元素 =====
        public TextBlock ScoreTextBlock => ScoreText;
        public Border ScoreBorderElement => ScoreBorder;
        public TextBlock TimeTextBlock => TimeText;
        public Border TimeBorderElement => TimeBorder;
        public TextBlock SourceTextBlock => SourceText;
        public Border SourceBorderElement => SourceBorder;
    }
}
