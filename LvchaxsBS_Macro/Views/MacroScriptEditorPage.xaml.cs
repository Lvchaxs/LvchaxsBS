using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Core;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Views
{
    /// <summary>
    /// 宏指令编辑页（照 <c>D:\C#\LvchaxsBS_MacroEditor_WebSim\index.html</c> 的「行号代码页」做的）。
    ///
    /// <para>
    /// ★ 由 <see cref="MacroConfigPage"/> 的「编辑指令」按钮通过 <c>NavigationService.Navigate</c>
    /// 打进宿主配置容器的内容 Frame —— **不是弹窗**；「返回列表」再导航回宏列表页（同一个实例）。
    /// </para>
    ///
    /// <para>
    /// ★ 行号列与代码区**同字体同字号**（Consolas 13）⇒ 自然行高必然一致：
    /// 行号列铺 1..N，编辑区的 <c>PART_ContentHost</c>（ScrollViewer）滚动时
    /// 用 TranslateTransform 平移行号列，两者永远对齐。
    /// </para>
    ///
    /// <para>
    /// ★ 语法高亮这轮先不做（纯文本）；「保存」/ Ctrl+S 把文本写进
    /// <see cref="MacroSettingsStore.SetScript"/>。执行引擎后面再补。
    /// </para>
    /// </summary>
    public partial class MacroScriptEditorPage : Page
    {
        /// <summary>正在编辑的宏 Id（编辑期间宏列表也可能被改名，只记 Id 不记引用）。</summary>
        private readonly string _macroId;

        /// <summary>内容有没有改动（决定「保存」是否真写盘）。</summary>
        private bool _dirty;

        /// <summary>编辑区内部那个 ScrollViewer（第一次拿到后缓存，LayoutUpdated 兜底要用）。</summary>
        private ScrollViewer? _scrollViewer;

        /// <summary>当前脚本解析出来的可执行指令条数（状态条显示）。</summary>
        private int _commandCount;

        public MacroScriptEditorPage(MacroItem macro)
        {
            InitializeComponent();

            _macroId = macro.Id;
            TitleText.Text = $"指令编辑 · {macro.Name}";
            Editor.Text = macro.Script ?? "";

            UpdateLineNumbers();
            UpdateHighlight();
            UpdateStatus();
            _dirty = false;

            // ★★ 滚动联动**不靠 FindName 找 PART_ContentHost**（模板换名字就整个失效）：
            //    ① ScrollChanged 是**冒泡**路由事件，直接挂 TextBox 就能收到内部 ScrollViewer 抛上来的；
            //    ② 再把 LayoutUpdated 当兜底 —— 只要布局动过就把行号列对到当前滚动位置。
            Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnEditorScroll));
            Editor.LayoutUpdated += (_, _) =>
            {
                var sv = _scrollViewer ??= FindChild<ScrollViewer>(Editor);
                if (sv != null) LineNumbersShift.Y = -sv.VerticalOffset;
            };
        }

        // ============ 语法着色 ============

        /// <summary>着色用的画刷缓存（主题资源取一次就够）。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Brush> BrushCache = new();

        /// <summary>
        /// 取主题画刷；取不到（比如宿主换了主题、或不在宿主里运行）就用固定色兜底
        /// ★ 固定色都选**中等亮度**，深底浅底上都读得清。
        /// </summary>
        private static Brush Themed(string key, Color fallback)
        {
            if (BrushCache.TryGetValue(key, out Brush? cached)) return cached;

            Brush? brush = null;
            try
            {
                brush = Application.Current?.TryFindResource(key) as Brush;
            }
            catch
            {
                // 拿主题资源失败就用兜底色，绝不让编辑器崩掉
            }

            brush ??= new SolidColorBrush(fallback);
            BrushCache[key] = brush;
            return brush;
        }

        /// <summary>把整篇文本重新着色（高亮层）。</summary>
        private void UpdateHighlight()
        {
            string text = Editor.Text ?? "";

            if (text.Length > 20000)          // 超大脚本就只当纯文本，不做着色（不然每次按键都卡）
            {
                Highlight.Text = text;
                return;
            }

            Highlight.Inlines.Clear();

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) Highlight.Inlines.Add(new LineBreak());
                AppendTokens(Highlight.Inlines, lines[i]);
            }
        }

        /// <summary>
        /// 把一行切成「指令名 + 参数 + 注释」三段着色。
        /// <para>★ 空白也要原样输出（不然行内位置会错位，光标对不上字）。</para>
        /// </summary>
        private static void AppendTokens(InlineCollection inlines, string line)
        {
            if (line.Length == 0) return;

            int cmtStart = CommentStart(line);
            string code = cmtStart < 0 ? line : line.Substring(0, cmtStart);
            string comment = cmtStart < 0 ? "" : line.Substring(cmtStart);

            string op = LeadingWord(code, out string rest);
            if (op.Length > 0)
                Add(inlines, op, MacroScript.IsOpKeyword(op) ? OpBrush() : PlainBrush());

            if (string.Equals(op, "text", StringComparison.OrdinalIgnoreCase) && rest.Length > 0)
            {
                // Text 后面整段都是要敲进去的内容 → 当字符串处理
                Add(inlines, rest, StringBrush());
            }
            else
            {
                AppendParams(inlines, rest);
            }

            if (comment.Length > 0)
                Add(inlines, comment, CommentBrush());
        }

        /// <summary>参数段：空白原样，词按「数字 / 鼠标键 / 键盘键 / 其它」着色。</summary>
        private static void AppendParams(InlineCollection inlines, string rest)
        {
            int i = 0;
            while (i < rest.Length)
            {
                bool space = char.IsWhiteSpace(rest[i]);
                int start = i;
                while (i < rest.Length && char.IsWhiteSpace(rest[i]) == space) i++;

                string chunk = rest.Substring(start, i - start);
                if (space) Add(inlines, chunk, PlainBrush());
                else Add(inlines, chunk, ColorOfParam(chunk));
            }
        }

        private static Brush ColorOfParam(string word)
        {
            if (int.TryParse(word, out _)) return NumberBrush();                    // 负数也算
            if (int.TryParse(word.TrimStart('-'), out _)) return NumberBrush();
            if (MacroScript.IsButtonName(word)) return ButtonBrush();               // Left / Middle / Right
            if (SimulationService.TryGetKeyCode(word, out _)) return PlainBrush();    // 认识的键名：普通色
            return PlainBrush();
        }

        private static void Add(InlineCollection inlines, string text, Brush brush)
            => inlines.Add(new Run(text) { Foreground = brush });

        /// <summary>行里注释的起点（<c>#</c> 或 <c>//</c>），没有就是 -1。</summary>
        private static int CommentStart(string line)
        {
            int hash = line.IndexOf('#');
            int slash = line.IndexOf("//", StringComparison.Ordinal);

            if (hash >= 0 && slash >= 0) return Math.Min(hash, slash);
            return hash >= 0 ? hash : slash;
        }

        /// <summary>切出行首的第一个词（剩下的放进 rest，保留前导空白）。</summary>
        private static string LeadingWord(string s, out string rest)
        {
            int i = 0;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;

            rest = i >= s.Length ? "" : s.Substring(i);
            return s.Substring(0, i);
        }

        private static Brush OpBrush() => Themed("TagBlueFgBrush", Color.FromRgb(0x4A, 0x9E, 0xDA));
        private static Brush NumberBrush() => Themed("TagPurpleFgBrush", Color.FromRgb(0xB0, 0x6A, 0xC0));
        private static Brush StringBrush() => Themed("TagAmberFgBrush", Color.FromRgb(0xC0, 0x80, 0x3A));
        private static Brush ButtonBrush() => Themed("TagGreenFgBrush", Color.FromRgb(0x3A, 0x9A, 0x6A));
        private static Brush CommentBrush() => Themed("TextDisabledBrush", Color.FromRgb(0x7A, 0x7A, 0x7A));
        private static Brush PlainBrush() => Themed("TextPrimaryBrush", Color.FromRgb(0x20, 0x20, 0x20));

        // ============ 行号 ============

        /// <summary>
        /// 编辑区滚动 → 行号列跟着平移（行号列宽 1 列、代码区在这条线右边）。
        /// <para>⛔ 别用 <c>sv.ScrollChanged +=</c> 去找模板内的 ScrollViewer：模板换了名字就彻底不联动。</para>
        /// </summary>
        private void OnEditorScroll(object sender, ScrollChangedEventArgs e)
        {
            if (e.OriginalSource is ScrollViewer sv)
            {
                _scrollViewer = sv;
                LineNumbersShift.Y = -sv.VerticalOffset;
                // 高亮层要跟着编辑区一起动（纵向滚动 + 横向滚动），不然字会错位
                HighlightShift.Y = -sv.VerticalOffset;
                HighlightShift.X = -sv.HorizontalOffset;
            }
            else
            {
                LineNumbersShift.Y = -e.VerticalOffset;
                HighlightShift.Y = -e.VerticalOffset;
                HighlightShift.X = -e.HorizontalOffset;
            }
        }

        /// <summary>在视觉树里找第一个指定类型的子元素（只在这里用于取初始滚动位置）。</summary>
        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;

                var deeper = FindChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void UpdateLineNumbers()
        {
            // ⛔ 别用 TextBox.LineCount：它只数**已经过布局**的行，
            //    内容比可视区高时（要滚动的那种）会停在可视行数（比如 21）。
            //    改数换行符 —— 和布局无关，永远准。
            string text = Editor.Text ?? "";
            int lines = 1;
            foreach (char c in text)
                if (c == '\n') lines++;

            var sb = new StringBuilder(lines * 4);
            for (int i = 1; i <= lines; i++)
            {
                if (i > 1) sb.Append('\n');
                sb.Append(i);
            }
            LineNumbers.Text = sb.ToString();
        }

        // ============ 编辑 / 保存 ============

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateLineNumbers();
            UpdateHighlight();
            UpdateStatus();

            if (!_dirty)
            {
                _dirty = true;
                SaveButton.Content = "保存";
            }
        }

        /// <summary>光标移动 → 状态条上的行列跟着变。</summary>
        private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => UpdateCaretInfo();

        /// <summary>
        /// 状态条：左边第一条语法错误（没有就不显示），右边「行 / 列 · 指令数」。
        /// <para>★ 解析出的错误行**不会被执行**，所以这里等于实时语法检查。</para>
        /// </summary>
        private void UpdateStatus()
        {
            string text = Editor.Text ?? "";

            var cmds = MacroScript.ParseWithErrors(text, out var errors);
            _commandCount = cmds.Count;

            if (errors.Count > 0)
            {
                // 红字报错（有错的行不会被执行，等于实时语法检查）
                ErrorText.Foreground = Themed("DangerBrush", Color.FromRgb(0xD0, 0x50, 0x50));
                ErrorText.Text = errors[0];
            }
            else if (text.Trim().Length == 0)
            {
                // 空脚本 → 给一行语法提示，省得对着空白页发呆
                ErrorText.Foreground = CommentBrush();
                ErrorText.Text = MacroScript.HelpText.Split('\r')[0];
            }
            else
            {
                ErrorText.Text = "";
            }

            UpdateCaretInfo();
        }

        /// <summary>右侧的「第几行，第几列 · N 条指令」。</summary>
        private void UpdateCaretInfo()
        {
            string text = Editor.Text ?? "";
            int caret = Math.Clamp(Editor.CaretIndex, 0, text.Length);

            int line = 1, col = 1;
            for (int i = 0; i < caret; i++)
            {
                if (text[i] == '\n') { line++; col = 1; }
                else col++;
            }

            StatusText.Text = $"第 {line} 行，第 {col} 列 · {_commandCount} 条指令";
        }

        private void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+S = 保存（编辑器惯例）
            if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SaveButton_Click(sender, e);
                e.Handled = true;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            MacroSettingsStore.SetScript(_macroId, Editor.Text);
            _dirty = false;

            // 给个看得见的反馈：按钮短暂变成「已保存」再弹回
            SaveButton.Content = "已保存";
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (!_dirty) SaveButton.Content = "保存";
            };
            t.Start();
        }

        // ============ 返回宏列表 ============

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // 有改动先自动存一份再走，避免「改了忘存」
            if (_dirty) SaveButton_Click(sender, e);

            // 回到**同一个**宏列表页实例（列表状态不丢）；万一它没了就新建一个
            // ⛔ 别 Navigate(null) —— 那会让 Frame 内容变成空白
            var listPage = (Page?)MacroConfigPage.CurrentInstance ?? new MacroConfigPage();
            NavigationService?.Navigate(listPage);
        }
    }
}
