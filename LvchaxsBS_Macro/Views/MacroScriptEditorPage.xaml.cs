using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;




using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Core;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Views
{
    /// <summary>
    /// 宏指令编辑页。
    /// <para>★ 由 <see cref="MacroConfigPage"/> 的「编辑指令」按钮导航进来（内容 Frame，不是弹窗）。</para>
    /// <para>
    /// ★ 编辑器用 AvalonEdit（自带行号 + 虚拟化 + 语法高亮）：行数再多全选/滚动都顺，
    /// 彻底根治原生 TextBox 大文本卡顿。
    /// </para>
    /// <para>
    /// ★ 中英显示：脚本磁盘/执行永远是**英文原码**，「中/英」按钮只在编辑器里切换显示语言
    /// （中文别名：按键键 E / 延迟 20 …），保存与执行仍写回英文原码。
    /// </para>
    /// </summary>
    public partial class MacroScriptEditorPage : Page
    {
        /// <summary>
        /// 当前编辑的宏 Id。
        /// <para>★ 不是 <c>readonly</c>：标题下拉切换宏时要改成新宏的 Id
        ///   （用户要求「点标题就能换一条宏接着编辑」，不用退回列表）。</para>
        /// </summary>
        private string _macroId;

        /// <summary>当前宏所属文件夹 Id（下拉里列的就是这个文件夹下的全部宏）。</summary>
        private string _folderId;

        /// <summary>「返回」要去的页；<c>null</c> = 回宏列表页。</summary>
        private readonly Page? _returnPage;

        /// <summary>这条宏当前的触发方式序号（只影响「跑一遍 / 循环」）。</summary>
        private int _modeIndex;

        /// <summary>内容有没有改动（决定「保存」是否真写盘）。</summary>
        private bool _dirty;

        /// <summary>当前脚本解析出来的可执行指令条数（状态条显示）。</summary>
        private int _commandCount;

        /// <summary>整个宏的预计时长（毫秒）：所有 Delay + KeyPress 的按住时长。</summary>
        private long _totalDurationMs;

        /// <summary>
        /// 撤销栈顶部有几「步」是语言切换产生的、还没被撤销掉的。
        /// <para>★ 见 <see cref="TranslateButton_Click"/> 的注释：撤销栈存绝对 offset，
        ///   没法把翻译那一步从栈里摘掉（会越界崩），只能用计数在撤销时先跳过去。</para>
        /// </summary>
        private int _pendingTranslateUndos;

        /// <summary>构造中置 true：初始赋值触发的 TextChanged 什么都不做。</summary>
        private bool _initializing = true;

        /// <summary>刷新节流定时器：120ms 内变化合并成一次。</summary>
        private DispatcherTimer? _refreshTimer;

        /// <summary>
        /// 当前编辑器显示的语言：true = 中文别名显示，false = 英文原码。
        /// <para>★ 只影响显示；脚本在内存/磁盘/执行里永远是英文原码。</para>
        /// </summary>
        private bool _chineseDisplay;

        /// <summary>上一次文本变化后的内容与光标位置（给自动缩进兜底判断「插入了什么」）。</summary>
        private string _prevText = "";
        private int _prevCaret;

        /// <summary>自己改文本时置 true，避免 TextChanged 里再补一次（递归）。</summary>
        private bool _suppressAutoIndent;

        /// <summary>
        /// 「已保存内容」的**英文归一化基准**（比较用）。
        /// <para>★ 这是解决「只切换中英就误判为已修改」的关键：脏判定不看**显示文本**，
        ///   而看**归一化到英文原码之后**的内容 —— 中英切换只改显示，英文原码不变 ⇒ 不算改动，
        ///   保存按钮不会亮、返回列表也不会提示「是否保存」。</para>
        /// </summary>
        private string _savedEnglishNorm = "";

        /// <summary>正在做「语言切换」那批替换（TextChanged 里据此不动待跳过计数）。</summary>
        private bool _inTranslateOp;

        /// <summary>正在做撤销/重做（TextChanged 里据此不动待跳过计数）。</summary>
        private bool _inUndoRedo;

        private const string IndentUnit = "    ";

        /// <summary>当前脚本的「按下→抬起」配对（给灰色连线渲染器用）。</summary>
        private List<MacroPair> _pairs = new();

        /// <summary>未配对的按下（缺抬起 / 同键连续按下被作废）：画红色虚线，不报错。</summary>
        private List<UnpairedDown> _unpairedDowns = new();

        /// <summary>未配对的抬起（这个键按下过，但抬起的次数多了）：画红色虚线，不报错。</summary>
        private List<UnpairedUp> _unpairedUps = new();

        /// <summary>每行文字末尾的 X（文档坐标），缓存给连线渲染器定位横线起点。</summary>
        private Dictionary<int, double> _lineEndX = new();

        /// <summary>
        /// 行宽缓存：行号 → (该行文本, 该行像素宽)。
        /// <para>★ 指令多的宏（上千行）每次刷新都要对每个配对端点构造 <see cref="FormattedText"/>，
        ///   不缓存的话每次按键都要几百次测量 ⇒ 输入明显发黏。</para>
        /// </summary>
        private readonly Dictionary<int, (string Text, double Width)> _lineWidthCache = new();

        /// <summary>等宽字符宽（文档坐标），算「文字后空 4 空格」用。</summary>
        private double _charW = 7.8;

        /// <summary>编辑区缩放百分比（默认 100%，改的是编辑器字号）。</summary>
        private int _zoomPercent = 100;
        private const double BaseFontSize = 13;

        // ============ AvalonEdit 适配层（业务代码用这些，不再直接摸控件 API） ============

        /// <summary>文档行数。</summary>
        private int EditorLineCount => Editor.Document.LineCount;

        /// <summary>取某行（1 基）文本，不含行尾换行符。</summary>
        private string GetLineText(int lineNumber1Based)
        {
            var doc = Editor.Document;
            if (lineNumber1Based < 1 || lineNumber1Based > doc.LineCount) return "";
            DocumentLine line = doc.GetLineByNumber(lineNumber1Based);
            return doc.GetText(line.Offset, line.Length);
        }

        /// <summary>取某行（1 基）的起始偏移。</summary>
        private int GetLineStartOffset(int lineNumber1Based)
        {
            var doc = Editor.Document;
            if (lineNumber1Based < 1 || lineNumber1Based > doc.LineCount) return 0;
            return doc.GetLineByNumber(lineNumber1Based).Offset;
        }

        /// <summary>替换一段区间。</summary>
        private void ReplaceRange(int offset, int length, string text)
            => Editor.Document.Replace(offset, length, text ?? "");

        /// <summary>选中一段区间。</summary>
        private void SelectRange(int start, int length) => Editor.Select(start, length);

        /// <summary>把光标滚进视野（最小滚动）。</summary>
        private void BringCaretToView()
        {
            try { Editor.TextArea.Caret.BringCaretToView(); }
            catch { /* 忽略滚动失败 */ }
        }

        /// <summary>主题变化监视器（换主题时重算高亮 + 换图标）。</summary>
        private DispatcherTimer? _themeWatcher;

        /// <summary>上一次看到的主题名（用来判断有没有变）。</summary>
        private string? _lastThemeName;

        public MacroScriptEditorPage(MacroItem macro) : this(macro, null)
        {
        }

        /// <param name="returnPage">
        /// 编辑完「返回」要去的那一页；<c>null</c> = 回到宏列表页（从配置页进来时的老行为）。
        /// 从首页卡片的「编辑」按钮进来时传首页那个 Page 实例，返回就回首页。
        /// </param>
        public MacroScriptEditorPage(MacroItem macro, Page? returnPage)
        {
            InitializeComponent();

            _returnPage = returnPage;
            _macroId = macro.Id;
            _folderId = macro.FolderId ?? "";
            _modeIndex = macro.ModeIndex;
            TitleText.Text = $"指令编辑 · {macro.Name}";

            // ★ 读上次保存的缩放（持久化），先设字号再填文本
            _zoomPercent = MacroSettingsStore.GetEditorZoom();
            Editor.FontSize = BaseFontSize * _zoomPercent / 100.0;
            ZoomLabel.Text = _zoomPercent + "%";

            // 打开就把缩进整理一遍，先在内存里排好再一次性赋值。
            // ★★ 载入时先**归一化到英文原码**：磁盘上的脚本可能是老版本在「中文显示」下
            //   存下来的中文（引擎只认英文原码），直接在中文状态下解析会报「不认识的指令」。
            //   归一化之后编辑器里永远是英文原码 ⇒ 默认英文显示、解析永远不报错。
            string loaded = MacroScriptTranslator.Translate(macro.Script ?? "", toChinese: false);
            Editor.Text = NormalizeIndentText(loaded);

            _initializing = false;
            _prevText = Editor.Text ?? "";
            _prevCaret = Editor.CaretOffset;

            // ★ 脏判定基准 = 「已保存内容的英文归一化版本」
            //   （只切中英不会改它 ⇒ 不算改动 ⇒ 保存按钮不亮、返回不提示保存）
            _savedEnglishNorm = NormalizeForCompare(Editor.Text);

            ConfigureHighlighting();
            UpdateStatus();
            _dirty = false;
            UpdateSaveButtonState();
            UpdateThemeIcon();

            // ★ 右侧指令工具面板：点「插入」就把整条指令贴到光标处
            ToolPanel.InsertRequested += ToolPanel_InsertRequested;
            // ★ 面板输出语言跟编辑器显示语言一致（默认英文原码）
            ToolPanel.ChineseFormat = _chineseDisplay;

            // ★ 主题一变就重算高亮颜色（深色下不清的问题全靠它）
            StartThemeWatcher();

            // ★ 灰色「按下→抬起」配对连线（背景渲染器，虚拟化只画可见部分）
            Editor.TextArea.TextView.BackgroundRenderers.Add(new PairLineRenderer(this));

            // ★ 光标/选区变化 → 状态条行列更新
            //   （自研控件统一用 SelectionChanged 冒泡，光标移动必然触发）
            // ★ Caret.PositionChanged：只移动光标不选字时也会触发（TextArea.SelectionChanged 不会）
            Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretInfo();
            Editor.TextArea.SelectionChanged += (_, _) => UpdateCaretInfo();


            if (returnPage != null)
            {
                BackButton.Content = "返回首页";
            }

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _refreshTimer.Tick += (_, _) =>
            {
                _refreshTimer.Stop();
                UpdateStatus();
            };

            Loaded += (_, __) =>
            {
                SetHostContentFullScreen(true);
                // ★ 页面加载完、TextView 已布局，重新量文字末尾 X（配对连线定位才准）
                UpdateStatus();
            };
            Unloaded += (_, __) =>
            {
                SetHostContentFullScreen(false);
                _refreshTimer.Stop();
                _themeWatcher?.Stop();
                _themeWatcher = null;
            };
        }

        private static void SetHostContentFullScreen(bool full)
        {
            try
            {
                Window? window = Application.Current?.MainWindow;
                window?.GetType()
                      .GetMethod("SetContentFullScreen", new[] { typeof(bool) })
                      ?.Invoke(window, new object[] { full });
            }
            catch
            {
                // 反射失败不影响编辑本身
            }
        }

        // ============ 标题下拉：切换到别的宏 ============

        /// <summary>点标题 → 列出**全部文件夹**下的所有宏。</summary>
        private void TitleButton_Click(object sender, RoutedEventArgs e)
        {
            BuildMacroMenu();
            MacroMenuPopup.IsOpen = !MacroMenuPopup.IsOpen;
        }

        /// <summary>
        /// 把**其它宏**填进下拉列表。
        /// <para>★★ 用户要求：**只显示宏名称**（不要 ★/☆ 图标、不要文件夹分组标题、不要文件夹标注），
        ///   而且**不显示当前这条**（当前这条已经在标题上了，列出来是多余的）。</para>
        /// </summary>
        private void BuildMacroMenu()
        {
            MacroMenuList.Children.Clear();

            List<MacroItem> all;
            try
            {
                // ★ 用户要的是**全部宏**，不是当前文件夹的
                all = MacroSettingsStore.GetAllMacros();
            }
            catch
            {
                return;
            }

            // ★ 只要**别的**宏（当前这条不列）
            var others = all
                .Where(m => !string.Equals(m.Id, _macroId, StringComparison.Ordinal))
                .ToList();

            if (others.Count == 0)
            {
                var tip = new TextBlock
                {
                    Text = "（没有其它宏）",
                    FontSize = 12,
                    Margin = new Thickness(10, 6, 10, 6),
                };
                tip.SetResourceReference(TextBlock.ForegroundProperty, "TextDisabledBrush");
                MacroMenuList.Children.Add(tip);
                return;
            }

            foreach (var m in others)
            {
                var item = new MenuRow
                {
                    Height = 28,
                    Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    // ★ 只显示宏名称
                    Content = m.Name,
                    Tag = m.Id,
                };
                // ★ 不要在这里本地赋 Background/Foreground —— 本地赋值会盖掉 Style 的悬停触发器

                var captured = m.Id;
                item.Click += (_, __) =>
                {
                    MacroMenuPopup.IsOpen = false;
                    SwitchToMacro(captured);
                };

                MacroMenuList.Children.Add(item);
            }
        }

        /// <summary>
        /// 下拉列表里的一行。
        /// <para>★★ **不能直接用 <see cref="Button"/>**：宿主按钮样式是给工具栏设计的，
        ///   套到下拉列表上会出现「直角长方形 + 默认蓝灰底 + 悬停变成系统色」，
        ///   跟面板里的观感完全对不上（用户两次吐槽过这个问题）。</para>
        /// <para>★ 所以自造一份 Style：<b>圆角 6</b>、常态透明、悬停才是背景色，
        ///   并且触发器写在 Style 里（本地赋值会被宿主触发器盖掉）。</para>
        /// </summary>
        private sealed class MenuRow : Button
        {
            public MenuRow()
            {
                var st = new Style(typeof(Button));

                var normal = Application.Current?.TryFindResource("AppBackgroundBrush") as Brush;
                var hoverBg = IsDarkForMenu() ? new SolidColorBrush(Color.FromRgb(53, 53, 53))
                                              : new SolidColorBrush(Color.FromRgb(240, 240, 240));
                var fg = Application.Current?.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.Black;

                // ★ 常态透明 ⇒ 靠圆角 Border 显形，不会出现「长方形色块」
                st.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                st.Setters.Add(new Setter(Control.ForegroundProperty, fg));
                st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                st.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
                st.Setters.Add(new Setter(Control.TemplateProperty, BuildTemplate()));

                var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
                hover.Setters.Add(new Setter(Control.BackgroundProperty, hoverBg));
                st.Triggers.Add(hover);

                Style = st;
            }

            /// <summary>下拉行自己带圆角 Border（宿主那个模板是直角 Grid）。</summary>
            private static ControlTemplate BuildTemplate()
            {
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetBinding(Border.BackgroundProperty,
                    new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                border.SetValue(Border.PaddingProperty, new Thickness(6, 0, 6, 0));

                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty,
                    new System.Windows.Data.Binding(nameof(Control.HorizontalContentAlignment)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty,
                    new System.Windows.Data.Binding(nameof(Control.VerticalContentAlignment)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0));

                border.AppendChild(presenter);

                var template = new ControlTemplate(typeof(Button));
                template.VisualTree = border;
                return template;
            }

            private static bool IsDarkForMenu()
            {
                try
                {
                    var t = Type.GetType("LvchaxsBS.Services.ThemeService, LvchaxsBS");
                    return t?.GetProperty("Current",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        ?.GetValue(null)?.ToString() == "Dark";
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 切换到另一条宏继续编辑。
        /// <para>★ 用户要求：**当前宏有未保存改动 ⇒ 先弹「是否保存」；没改动 ⇒ 直接切**。
        ///   确认保存就写盘（英文原码），选「不保存」就丢弃改动。</para>
        /// </summary>
        private void SwitchToMacro(string newId)
        {
            if (string.Equals(newId, _macroId, StringComparison.Ordinal)) return;

            // 先重算脏状态（节流刷新可能还没跑到，否则「刚改完立刻切」会被当成没改动 ⇒ 丢改动）
            RecomputeDirty();

            if (_dirty)
            {
                bool save = Utils.HostBridge.Confirm(
                    "有未保存的修改",
                    "有修改未被保存，是否保存？",
                    "保存",
                    "不保存");

                // ★ 落盘同样写英文原码（中文只是显示形态）
                if (save)
                    MacroSettingsStore.SetScript(_macroId,
                        MacroScriptTranslator.Translate(Editor.Text ?? "", toChinese: false));

                _dirty = false;
            }

            // 找目标宏（列表可能刚被改过，重新取一次；★ 是**全部宏**里的，不限当前文件夹）
            var all = MacroSettingsStore.GetAllMacros();
            var target = all.FirstOrDefault(m => string.Equals(m.Id, newId, StringComparison.Ordinal));
            if (target == null) return;

            LoadMacro(target);

            // ★ 同步「首页卡片当前选中的宏」—— 触发键按下执行的就是它。
            //   在这里编辑的是哪条，首页下拉就该显示哪条，否则「编辑的和触发键跑的不是一条」。
            try { MacroSettingsStore.SetActiveMacroId(_macroId); } catch { /* 配置写不了也不影响编辑 */ }
        }

        /// <summary>把某条宏的内容载入编辑器（切宏 / 首次打开共用这一套状态重置）。</summary>
        private void LoadMacro(MacroItem macro)
        {
            _macroId = macro.Id;
            _folderId = macro.FolderId ?? _folderId;
            _modeIndex = macro.ModeIndex;

            TitleText.Text = $"指令编辑 · {macro.Name}";

            // ★ 和构造时一样：先归一化到英文原码再显示（老版本可能在中文态下存过盘）
            string loaded = MacroScriptTranslator.Translate(macro.Script ?? "", toChinese: false);

            _initializing = true;
            Editor.Text = NormalizeIndentText(loaded);
            _initializing = false;

            _prevText = Editor.Text ?? "";
            _prevCaret = Editor.CaretOffset;
            _savedEnglishNorm = NormalizeForCompare(Editor.Text);

            _dirty = false;

            ConfigureHighlighting();
            UpdateStatus();
            UpdateSaveButtonState();
            Editor.Focus();
            Editor.CaretOffset = 0;
        }

        private static Brush? TryBrush(string key)
            => Utils.HostBridge.GetThemeBrush(key)
               ?? Application.Current?.TryFindResource(key) as Brush;

        // ============ 中英切换 ============

        /// <summary>
        /// 「中/英」按钮：在中文别名显示 / 英文原码之间切换。
        /// <para>
        /// ★★ 撤销栈的处理是这里的核心，实测（探针 <c>D:/C#/_tmp_refl</c>）结论：
        /// <list type="bullet">
        /// <item><c>Editor.Text = next</c> 整篇赋值 → <b>清空整个 UndoStack</b>（CanUndo 立刻 False），
        ///       用户之前的 Alt+Z 历史全丢 —— 所以绝不能用。</item>
        /// <item>逐行 <c>doc.Replace</c>（从后往前，包在 <c>RunUpdate()</c> 里）→ 撤销栈保住，
        ///       整次翻译合并成一步。但这一步会<b>占据栈顶</b>，
        ///       用户按 Alt+Z 撤掉的是「语言切换」而不是刚插入的指令。</item>
        /// <item>把这一步从栈里直接摘掉（反射 <c>undostack.PopBack</c>）→ <b>会崩</b>：
        ///       撤销记录存的是<b>绝对 offset</b>，翻译改变了文本长度，
        ///       下一条撤销的 offset 越界（实测 <c>offset(19)+length(10) &lt;= 19</c>）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// ★ 所以采取「<b>待跳过计数</b>」：翻译完记 <c>_pendingTranslateUndos = 1</c>，
        ///   用户下一次按 Alt+Z 时先自动撤掉这一步（无感），<b>再接着撤真正的编辑</b> ——
        ///   即一次 Alt+Z 就把刚插入的指令撤掉。撤完计数清零，语言状态也跟着回滚。
        /// </para>
        /// </summary>
        private void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            int caret = Editor.CaretOffset;
            _chineseDisplay = !_chineseDisplay;

            // ★ 面板的复制/插入也跟着切语言，中文显示时插进去的必须是中文格式
            ToolPanel.ChineseFormat = _chineseDisplay;

            ApplyTranslation(_chineseDisplay);

            // 逐行替换后行长度变了，光标偏移夹回合法范围即可
            Editor.CaretOffset = Math.Clamp(caret, 0, Editor.Document.TextLength);

            // ★ 记一笔「栈顶这一步是翻译」—— 下次 Alt+Z 先无感撤掉它，再撤真正的编辑。
            //   注意：这里只能在 RunUpdate 结束后设，RunUpdate 内部的合并还没落定。
            if (_lastTranslateChanged) _pendingTranslateUndos++;

            UpdateStatus();
        }

        /// <summary>
        /// 逐行把整个文档翻译到指定语言，**在内存里拼好整篇再一次性赋值**。
        /// 逐行把整个文档翻译到指定语言（从最后一行往前替换，前面的行偏移才不受影响），
        /// 整批包在 <c>Document.RunUpdate()</c> 里 ⇒ 撤销栈里合并成<b>一步</b>。
        /// <para>★ 返回<b>是否真的产生了替换</b>：已经是目标语言的行不会触发 <c>Replace</c>，
        ///   整批就不会进撤销栈。<see cref="Editor_Undo"/> 靠这个特性判断补回语言后要不要记账。</para>
        /// </summary>
        private bool ApplyTranslation(bool toChinese)
        {
            var doc = Editor.Document;
            bool changed = false;

            _suppressAutoIndent = true;
            _inTranslateOp = true;
            try
            {
                using (doc.RunUpdate())
                {
                    for (int ln = doc.LineCount; ln >= 1; ln--)
                    {
                        DocumentLine line = doc.GetLineByNumber(ln);
                        if (line.Length == 0) continue;

                        string old = doc.GetText(line.Offset, line.Length);
                        string translated = MacroScriptTranslator.TranslateLine(old, toChinese);
                        if (translated == old) continue;

                        doc.Replace(line.Offset, line.Length, translated);
                        changed = true;
                    }
                }
            }
            finally
            {
                _suppressAutoIndent = false;
                _inTranslateOp = false;
            }

            _lastTranslateChanged = changed;
            return changed;
        }

        /// <summary>上一次 <see cref="ApplyTranslation"/> 是否真的改了文本。</summary>
        private bool _lastTranslateChanged;

        /// <summary>
        /// 撤销：<b>先跳过顶部的语言切换，再撤用户真正的编辑</b>，
        /// 最后把显示语言<b>补回用户选的那种</b>（不产生新的撤销步）。
        /// <para>★ 用户在中文显示下插了「延迟 100」，切到英文后按 Alt+Z ——
        ///   期望撤掉的是那条「延迟」、且仍停在英文显示，而不是撤掉切换_language_。
        ///   靠 <see cref="_pendingTranslateUndos"/> 实现。</para>
        /// </summary>
        private void Editor_Undo()
        {
            // 连续点了几次中/英就有几步要跳
            int skips = _pendingTranslateUndos;
            bool wantChinese = _chineseDisplay;

            if (skips > 0)
            {
                _pendingTranslateUndos = 0;
                _inUndoRedo = true;

                _suppressAutoIndent = true;
                try
                {
                    for (int i = 0; i < skips && Editor.CanUndo; i++)
                        Editor.Undo();
                }
                catch
                {
                    // 跳过的过程中万一出错就放弃跳，直接落到下面撤真正的编辑
                }
                finally
                {
                    _suppressAutoIndent = false;
                }

                // 撤掉翻译 = 屏幕语言暂时回到了另一边，先把状态记下来，撤完真正的编辑再补回去
            }

            if (Editor.CanUndo) Editor.Undo();
            _inUndoRedo = false;

            // ★ 把显示语言补回用户选的那种：跳过后屏幕上是「切换前」的语言，
            //   这里重新翻回去。若它真的改了文本，就会占掉新的栈顶 ⇒ 记一笔待跳过，
            //   这样连按 Alt+Z 时每一步撤的都是**用户真正的编辑**，不会在两种语言间来回跳。
            if (skips > 0)
            {
                _chineseDisplay = wantChinese;
                if (ApplyTranslation(wantChinese)) _pendingTranslateUndos++;
            }

            // ★★ 兜底同步：撤销栈里可能还有更早的翻译步骤（计数没覆盖到的），
            //   撤掉它之后屏幕语言就变了，标志却没回滚 ⇒ 状态条解析和面板输出语言会跟屏幕不一致。
            //   所以撤销后一律按**屏幕上实际是什么语言**来对齐标志。
            _chineseDisplay = DetectChineseDisplay();

            ToolPanel.ChineseFormat = _chineseDisplay;

            UpdateStatus();
        }

        /// <summary>
        /// 按编辑器里**当前实际显示**的文本判断是不是中文显示：只要有任一行的行首关键字是中文别名就算。
        /// <para>★ 用于撤销/重做之后的语言标志兜底同步（撤销可能连带撤掉语言切换那一步）。</para>
        /// </summary>
        private bool DetectChineseDisplay()
        {
            for (int ln = 1; ln <= EditorLineCount; ln++)
            {
                string text = (GetLineText(ln) ?? "").TrimStart();
                if (text.Length == 0) continue;

                int end = 0;
                while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;

                if (MacroScriptTranslator.IsChineseAlias(text.Substring(0, end)))
                    return true;
            }
            return false;
        }

        /// <summary>重做：清掉待跳过计数，并按屏幕实际语言同步标志（重做同样可能翻回另一种语言）。</summary>
        private void Editor_Redo()
        {
            _inUndoRedo = true;
            try
            {
                if (Editor.CanRedo) Editor.Redo();
            }
            finally
            {
                _inUndoRedo = false;
            }
            _pendingTranslateUndos = 0;
            _chineseDisplay = DetectChineseDisplay();
            ToolPanel.ChineseFormat = _chineseDisplay;
            UpdateStatus();
        }

        // ============ 编辑区缩放 ============

        private void ApplyZoom()
        {
            _zoomPercent = Math.Clamp(_zoomPercent, 50, 200);
            Editor.FontSize = BaseFontSize * _zoomPercent / 100.0;
            ZoomLabel.Text = _zoomPercent + "%";
            // ★ 持久化，下次打开直接沿用
            MacroSettingsStore.SetEditorZoom(_zoomPercent);
            // ★ 字号变了，文字末尾 X / 字符宽都变 ⇒ 行宽缓存整批作废，再重新量一遍
            _lineWidthCache.Clear();
            UpdateStatus();
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _zoomPercent += 10;
            ApplyZoom();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _zoomPercent -= 10;
            ApplyZoom();
        }

        // ============ 语法着色（AvalonEdit 高亮规则） ============

        /// <summary>
        /// 画刷缓存。★ **换主题时必须清空** —— 缓存里是旧主题的画刷对象，
        /// 主题字典整个换掉之后缓存不会自动更新（深色下会拿浅色的蓝当深色用，看不清）。
        /// </summary>
        private static readonly Dictionary<string, Brush> BrushCache = new();

        private static Brush Themed(string key, Color fallback)
        {
            if (BrushCache.TryGetValue(key, out Brush? cached)) return cached;

            // ★ 走宿主 ThemeService.GetThemeBrush 拿**终值**：
            //   主题渐变那 300ms 里 TryFindResource 给的是正在变色的临时画刷，
            //   取到的会是某一帧的中间色，而高亮规则一旦建好就把颜色固定住了
            //   ⇒ 表现就是「切完主题指令区颜色不对，重开页面才正常」。
            Brush? brush = Utils.HostBridge.GetThemeBrush(key);

            brush ??= new SolidColorBrush(fallback);
            BrushCache[key] = brush;
            return brush;
        }

        private static Color OpColor() => ThemedColor("TagBlueFgBrush", Color.FromRgb(0x4A, 0x9E, 0xDA));
        private static Color NumberColor() => ThemedColor("TagPurpleFgBrush", Color.FromRgb(0xB0, 0x6A, 0xC0));
        private static Color StringColor() => ThemedColor("TagAmberFgBrush", Color.FromRgb(0xC0, 0x80, 0x3A));
        private static Color CommentColor() => ThemedColor("TextDisabledBrush", Color.FromRgb(0x7A, 0x7A, 0x7A));
        private static Color ButtonColor() => ThemedColor("TagGreenFgBrush", Color.FromRgb(0x3A, 0x9A, 0x6A));
        private static Color KeyColor() => ThemedColor("TagCyanFgBrush", Color.FromRgb(0x3A, 0xB7, 0xC7));

        private static Color ThemedColor(string key, Color fallback)
        {
            var brush = Themed(key, fallback);
            return brush is SolidColorBrush scb ? scb.Color : fallback;
        }

        /// <summary>配置 AvalonEdit 的宏脚本语法高亮（打开编辑页时跑一次）。</summary>
        private void ConfigureHighlighting()
        {
            var rules = new HighlightingRuleSet();

            // 注释（# 或 // 到行尾）
            rules.Spans.Add(new HighlightingSpan
            {
                StartExpression = new Regex(@"#|//"),
                EndExpression = new Regex("$"),
                SpanColor = new HighlightingColor { Foreground = new SimpleHighlightingBrush(CommentColor()) },
            });

            // 数字（含负数）
            rules.Rules.Add(new HighlightingRule
            {
                Regex = new Regex(@"\b-?\d+\b"),
                Color = new HighlightingColor { Foreground = new SimpleHighlightingBrush(NumberColor()) },
            });

            // 指令关键字（英文 + 中文别名，不分大小写，整词匹配）
            var opNames = string.Join("|", MacroScript.OpNames.Select(Regex.Escape));
            var aliases = string.Join("|", MacroScriptTranslator.ChineseAliases.Select(Regex.Escape));
            var allOp = $"{opNames}|{aliases}";
            rules.Rules.Add(new HighlightingRule
            {
                Regex = new Regex($@"\b(?:{allOp})\b", RegexOptions.IgnoreCase),
                Color = new HighlightingColor { Foreground = new SimpleHighlightingBrush(OpColor()) },
            });

            // 鼠标键名（Left/Middle/Right + 中文 左键/中键/右键）→ 绿色
            rules.Rules.Add(new HighlightingRule
            {
                Regex = new Regex(@"\b(?:Left|Middle|Right|左键|中键|右键)\b", RegexOptions.IgnoreCase),
                Color = new HighlightingColor { Foreground = new SimpleHighlightingBrush(ButtonColor()) },
            });

            // 键盘键名（单个字母 A-Z，如 KeyDown E 的 E）→ 青色
            rules.Rules.Add(new HighlightingRule
            {
                Regex = new Regex(@"\b[A-Za-z]\b"),
                Color = new HighlightingColor { Foreground = new SimpleHighlightingBrush(KeyColor()) },
            });

            // Text 关键字后面整段当字符串着色
            rules.Spans.Add(new HighlightingSpan
            {
                StartExpression = new Regex(@"\b(?:Text|文本)\b", RegexOptions.IgnoreCase),
                EndExpression = new Regex("$"),
                StartColor = new HighlightingColor { Foreground = new SimpleHighlightingBrush(OpColor()) },
                SpanColor = new HighlightingColor { Foreground = new SimpleHighlightingBrush(StringColor()) },
            });

            Editor.SyntaxHighlighting = new MacroHighlightingDefinition(rules);
        }

        /// <summary>把一组高亮规则包成 AvalonEdit 的 IHighlightingDefinition。</summary>
        private sealed class MacroHighlightingDefinition : IHighlightingDefinition
        {
            private readonly HighlightingRuleSet _main;
            private readonly Dictionary<string, HighlightingColor> _colors = new();

            public MacroHighlightingDefinition(HighlightingRuleSet main)
            {
                _main = main;
            }

            public string Name => "MacroScript";

            public HighlightingRuleSet MainRuleSet => _main;

            public HighlightingRuleSet GetNamedRuleSet(string name) => _main;

            public HighlightingColor? GetNamedColor(string name)
                => _colors.TryGetValue(name, out var c) ? c : null;

            public IEnumerable<HighlightingColor> NamedHighlightingColors => _colors.Values;

            public IDictionary<string, string> Properties => new Dictionary<string, string>();
        }

        // ============ 编辑 / 保存 ============

        private void Editor_TextChanged(object sender, EventArgs e)
        {
            string now = Editor.Text ?? "";
            if (_initializing) return;

            // ★ 用户在语言切换之后又自己动手改了 ⇒ 撤销栈顶已经是「他这次的编辑」，
            //   不是翻译了 ⇒ 待跳过计数必须清零，否则 Alt+Z 会白撤一次。
            if (!_inTranslateOp && !_inUndoRedo) _pendingTranslateUndos = 0;

            if (!_suppressAutoIndent)
            {
                int added = now.Length - _prevText.Length;

                // ★ 只在「逐字输入」（一次新增 1~2 字符）时做自动缩进兜底；
                //   大段粘贴（一次新增 3 字符以上且有换行）**不做全文缩进重排** ——
                //   AvalonEdit 粘贴本身够快，全文重写反而清撤销栈、光标错位、甚至崩。
                if (added >= 1 && added <= 2) TryAutoIndentFallback();
            }

            ScheduleRefresh();

            _prevText = Editor.Text ?? "";
            _prevCaret = Editor.CaretOffset;
        }

        /// <summary>
        /// 重新算「有没有改动」。
        /// <para>★ 判定基准是**归一化到英文原码之后**的内容（<see cref="_savedEnglishNorm"/>），
        ///   所以「只切换中英」这种纯显示变化**不算改动** ⇒ 保存按钮不亮、返回列表不提示保存。
        ///   真正改了指令（哪怕改的是中文别名）⇒ 归一化后与基准不同 ⇒ 算改动。</para>
        /// <param name="englishText">已翻好的英文原码（复用 <see cref="UpdateStatus"/> 那次翻译，省一遍全文扫描）。</param>
        /// </summary>
        private void RecomputeDirty(string? englishText = null)
        {
            // 传入的已经是英文原码 ⇒ 只做换行/行尾空白归一化；
            // 没传才自己翻一遍（否则会重复翻译）。
            string norm = englishText != null
                ? NormalizeLines(englishText)
                : NormalizeForCompare(Editor.Text);

            bool nowDirty = !string.Equals(norm, _savedEnglishNorm, StringComparison.Ordinal);

            if (nowDirty != _dirty)
            {
                _dirty = nowDirty;
                UpdateSaveButtonState();
            }
        }

        /// <summary>
        /// 归一化：先把内容翻回**英文原码**，再统一换行 + 去掉每行行尾空白。
        /// <para>★ 这是「中英切换不算改动」的关键 —— 中英两种显示归一化后是同一份内容。</para>
        /// </summary>
        private static string NormalizeForCompare(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // ★ 归一化到英文原码：中文显示时先把中文别名翻回去
            //   （已是英文时是恒等变换，不会误伤）
            return NormalizeLines(MacroScriptTranslator.Translate(text, toChinese: false));
        }

        /// <summary>统一换行 + 去掉每行行尾空白（英文原码专用）。</summary>
        private static string NormalizeLines(string english)
        {
            string[] lines = english.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder(english.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(lines[i].TrimEnd());
            }
            return sb.ToString();
        }

        private void TryAutoIndentFallback()
        {
            string text = Editor.Text ?? "";
            int added = text.Length - _prevText.Length;
            if (added != 1 && added != 2) return;

            int at = _prevCaret;
            if (at < 0 || at >= text.Length) return;

            int newlineLen;
            if (at + 1 < text.Length && text[at] == '\r' && text[at + 1] == '\n') newlineLen = 2;
            else if (text[at] == '\n') newlineLen = 1;
            else return;

            if (added != newlineLen) return;

            int lineStart = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
            string beforeCaret = text.Substring(lineStart, at - lineStart);

            string indent = LeadingWhitespace(beforeCaret);
            string trimmed = beforeCaret.TrimEnd();
            if (trimmed.EndsWith("{") || trimmed.EndsWith("｛")) indent += IndentUnit;
            if (indent.Length == 0) return;

            int caretAfter = at + newlineLen;

            _suppressAutoIndent = true;
            try
            {
                ReplaceRange(caretAfter, 0, indent);
            }
            finally
            {
                _suppressAutoIndent = false;
            }
        }

        private void ScheduleRefresh()
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Start();
        }

        private static string NormalizeIndentText(string text)
        {
            if (text.Length == 0) return text;

            string eol = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');

            var sb = new StringBuilder(text.Length + 64);
            bool inBlock = false;
            bool changed = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string trimmed = raw.Trim();

                if (i > 0) sb.Append(eol);
                if (trimmed.Length == 0) continue;

                bool isClose = trimmed == "}" || trimmed == "｝";
                bool isOpen = trimmed == "{" || trimmed == "｛";
                bool isName = MacroScript.IsBlockHeaderLine(trimmed);
                bool isCall = trimmed.EndsWith(")") || trimmed.EndsWith("）");

                string want;
                if (isClose || isOpen) want = "";
                else if (isName && !isCall) want = "";
                else want = inBlock ? IndentUnit : "";
                string fixedLine = want + trimmed;

                if (fixedLine != raw) changed = true;
                sb.Append(fixedLine);

                if (isClose) inBlock = false;
                else if (isOpen) inBlock = true;
                else if (isName && (trimmed.EndsWith("{") || trimmed.EndsWith("｛"))) inBlock = true;
            }

            return changed ? sb.ToString() : text;
        }

        // ============ 状态条 ============

        private void UpdateStatus()
        {
            string text = Editor.Text ?? "";

            // ★ 语法检查/指令统计**永远解析英文原码**
            //   （中文显示时翻回英文；万一用户手写了中文别名也照样认得出来）
            string parseText = MacroScriptTranslator.Translate(text, toChinese: false);

            var cmds = MacroScript.ParseForWithErrors(parseText, _modeIndex, out var errors);
            _commandCount = cmds.Count;

            // ★ 状态条统计：整个宏的预计时长
            //   （引擎那边 Delay 就是 Sleep(Arg1)、KeyPress 是 Sleep(HoldMs)，
            //     别的指令瞬时完成 ⇒ 时长 = 所有 Delay + KeyPress 的按住时长）
            long totalMs = 0;
            foreach (var c in cmds)
            {
                switch (c.Op)
                {
                    case MacroOp.Delay: totalMs += Math.Max(0, c.Arg1); break;
                    case MacroOp.KeyPress: totalMs += Math.Max(0, c.HoldMs); break;
                }
            }
            _totalDurationMs = totalMs;

            // ★ 按下→抬起配对：缺抬起的按下不报错（画红线），只有缺按下报错
            var (pairs, unpaired, unpairedUps, pairErrors) = MacroPairAnalyzer.Analyze(parseText);
            _pairs = pairs;
            _unpairedDowns = unpaired;
            _unpairedUps = unpairedUps;
            if (pairErrors.Count > 0)
                errors.AddRange(pairErrors);

            // ★ 量每行文字末尾 X（供连线定位横线起点），缓存不随滚动变
            MeasureLineEnds(parseText, pairs, unpaired, unpairedUps);

            if (errors.Count > 0)
            {
                ErrorText.Foreground = Themed("DangerBrush", Color.FromRgb(0xD0, 0x50, 0x50));
                ErrorText.Text = errors[0];
            }
            else if (parseText.Trim().Length == 0)
            {
                ErrorText.Foreground = Themed("TextDisabledBrush", Color.FromRgb(0x7A, 0x7A, 0x7A));
                ErrorText.Text = MacroScript.HelpText.Split('\r')[0];
            }
            else
            {
                ErrorText.Text = "";
            }

            // 连线数据变了 → 重绘背景
            Editor.TextArea.TextView.InvalidateVisual();


            // ★ 顺便重算「有没有改动」（基准是英文归一化内容 ⇒ 切中英不算改动）
            RecomputeDirty(parseText);

            UpdateCaretInfo();
        }

        private void UpdateCaretInfo()
        {
            //行列（1-based）由自研控件算（正确处理 \r\n 与中文视觉列）
            CaretText.Text = $"第 {Editor.TextArea.Caret.Line} 行，第 {Editor.TextArea.Caret.Column} 列";

            // 右下角：X 条指令 · 整个宏的预计时长（时:分:秒，毫秒不足 1 秒时补毫秒）
            StatusText.Text = $"{_commandCount} 条指令 · {FormatDuration(_totalDurationMs)}";
        }

        /// <summary>
        /// 毫秒 → <c>HH:MM:SS</c>；不足 1 秒时补上毫秒（<c>00:00:00.410</c>），
        /// 否则几百毫秒的宏会被秒级截断成 00:00:00，看着像"没时长"。
        /// </summary>
        private static string FormatDuration(long ms)
        {
            if (ms < 0) ms = 0;
            var ts = TimeSpan.FromMilliseconds(ms);
            string hms = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
            return ts.Milliseconds > 0 ? $"{hms}.{ts.Milliseconds:000}" : hms;
        }

        /// <summary>
        /// 量配对端点行 + 未配对按下行的文字末尾 X（文档坐标），缓存到 <see cref="_lineEndX"/>。
        /// <para>★ 用自研控件暴露的度量（<c>CurrentCharWidth</c> / <c>TextOriginX</c>），
        ///   不再依赖 AvalonEdit 的 GetVisualPosition。</para>
        /// </summary>
        private void MeasureLineEnds(string text, List<MacroPair> pairs, List<UnpairedDown> unpaired,
                                    List<UnpairedUp> unpairedUps)
        {
            _lineEndX = new Dictionary<int, double>();
            if (pairs.Count == 0 && unpaired.Count == 0 && unpairedUps.Count == 0) return;

            var typeface = new Typeface("Consolas");
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // ★ 字符宽：用 FormattedText 量半角「0」（等宽，半角宽度固定；字号跟缩放走）
            var ft0 = new FormattedText("0",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, Editor.FontSize, Brushes.Black, dpi);
            _charW = ft0.WidthIncludingTrailingWhitespace;

            // ★ 行号列宽（文字起始 X）——控件自己算好：左内边距 + 位数×字符宽 + 间距
            // ★ 行号列宽 = 第 1 行第 1 列的 X（AvalonEdit 的视觉坐标，稳定不随内容变）
            double gutterX = 0;
            try
            {
                gutterX = Editor.TextArea.TextView
                    .GetVisualPosition(new ICSharpCode.AvalonEdit.TextViewPosition(1, 1),
                                       VisualYPosition.LineMiddle).X;
            }
            catch { return; }

            var lineNos = new HashSet<int>();
            foreach (var p in pairs) { lineNos.Add(p.DownLine); lineNos.Add(p.UpLine); }
            foreach (var u in unpaired) lineNos.Add(u.Line);
            foreach (var u in unpairedUps) lineNos.Add(u.Line);

            foreach (int ln in lineNos)
            {
                // 当前显示的行文字（中文显示时就是中文），去掉换行符
                string lineText = (GetLineText(ln) ?? "").TrimEnd('\r', '\n');

                // ★ 行宽缓存：文本没变就直接复用上次量好的宽度（大文档下省掉成百上千次 FormattedText）
                double width;
                if (_lineWidthCache.TryGetValue(ln, out var cached) &&
                    string.Equals(cached.Text, lineText, StringComparison.Ordinal))
                {
                    width = cached.Width;
                }
                else
                {
                    // 文字宽 = FormattedText 实测（正确量中文全角宽度）
                    var ft = new FormattedText(lineText,
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, typeface, Editor.FontSize, Brushes.Black, dpi);
                    width = ft.WidthIncludingTrailingWhitespace;
                    _lineWidthCache[ln] = (lineText, width);
                }

                _lineEndX[ln] = gutterX + width;
            }

            // 连线由 PairLineRenderer（IBackgroundRenderer）直接读 _pairs / _lineEndX 画，这里不用推数据
        }

        // ============ 键盘 ============

        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

            if (key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SaveButton_Click(sender, e);
                e.Handled = true;
                return;
            }

            // ★ 撤销/重做走自己的处理：要先跳过语言切换那一步（否则撤的是切换不是编辑）
            if (key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    Editor_Redo();
                else
                    Editor_Undo();
                e.Handled = true;
                return;
            }

            if (key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Editor_Redo();
                e.Handled = true;
                return;
            }

            // ★★ 复制 / 剪切 / 粘贴：自己走宿主的原生剪贴板，不用 AvalonEdit 默认那套
            //   （★ 必须在下面「有选区就 return」之前判 —— 复制/剪切本来就是选中后才按的）
            if (key is Key.C or Key.X or Key.V &&
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                HandleClipboardKey(key, e);
                return;
            }

            if (Editor.SelectionLength != 0) return;

            switch (key)
            {
                case Key.Enter:
                    AutoIndentOnEnter();
                    e.Handled = true;
                    break;

                case Key.Tab:
                    InsertAtCaret(IndentUnit);
                    e.Handled = true;
                    break;

                case Key.OemCloseBrackets:
                    if (Keyboard.Modifiers == ModifierKeys.Shift) TryOutdentBeforeCloseBrace();
                    break;
            }
        }

        /// <summary>
        /// Ctrl+C / Ctrl+X / Ctrl+V：自己调宿主的原生剪贴板读写。
        /// <para>
        /// ★★ 为什么要接管：AvalonEdit 内部用的是 <c>System.Windows.Clipboard</c>
        ///   （OLE + 失败重试，每次几十毫秒），选中一大段脚本再复制 / 剪切会明显顿一下。
        ///   宿主的 <c>FastClipboard</c> 是 <c>OpenClipboard / SetClipboardData</c> 直写，几乎无感
        ///   —— 主程序里所有 TextBox 早就走它了，只有 AvalonEdit 不是 <c>TextBoxBase</c>，
        ///   没被那个类处理器覆盖到，所以这里单独接一次。
        /// </para>
        /// <para>
        /// ★ 写不进去 / 剪贴板里不是纯文本 ⇒ **不接管**（不打 <c>e.Handled</c>），
        ///   交回 AvalonEdit 原路径：宁可慢一点，也不让功能坏掉。
        /// </para>
        /// </summary>
        private void HandleClipboardKey(Key key, KeyEventArgs e)
        {
            string selected = Editor.SelectedText ?? "";

            switch (key)
            {
                case Key.C:
                    if (selected.Length == 0) return;
                    if (Utils.HostBridge.TrySetClipboardText(selected)) e.Handled = true;
                    return;

                case Key.X:
                    if (selected.Length == 0) return;
                    if (!Utils.HostBridge.TrySetClipboardText(selected)) return;

                    // ★ 走「选区替换」而不是给 SelectedText 赋空串：这一步会完整进撤销栈，
                    //   Ctrl+Z 能把整段剪掉的内容一次性撤回来
                    Editor.TextArea.Selection.ReplaceSelectionWithText(string.Empty);
                    e.Handled = true;
                    return;

                case Key.V:
                    string? text = Utils.HostBridge.TryGetClipboardText();
                    if (string.IsNullOrEmpty(text)) return;     // 没有纯文本 ⇒ 交回 AvalonEdit

                    // 行结束符照原样插入（\r\n、\n 编辑器都认），不做改写
                    Editor.TextArea.Selection.ReplaceSelectionWithText(text);
                    Editor.TextArea.Caret.BringCaretToView();
                    e.Handled = true;
                    return;
            }
        }

        private void AutoIndentOnEnter()
        {
            string text = Editor.Text ?? "";
            int caret = Math.Clamp(Editor.CaretOffset, 0, text.Length);

            int lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
            string beforeCaret = text.Substring(lineStart, caret - lineStart);

            string indent = LeadingWhitespace(beforeCaret);
            string trimmed = beforeCaret.TrimEnd();
            if (trimmed.EndsWith("{") || trimmed.EndsWith("｛")) indent += IndentUnit;

            InsertAtCaret(Environment.NewLine + indent);
        }

        private void InsertAtCaret(string text)
        {
            // ★ clamp 光标到合法范围，避免空文档/删除后越界崩溃
            int at = Math.Clamp(Editor.CaretOffset, 0, (Editor.Text ?? "").Length);
            ReplaceRange(at, 0, text);
            // ★ 换行/插入后把光标滚进视野；BringCaretToView 在边界状态可能抛异常，包住
            BringCaretToView();   // 内部已 try/catch
        }

        private bool TryOutdentBeforeCloseBrace()
        {
            string text = Editor.Text ?? "";
            int caret = Math.Clamp(Editor.CaretOffset, 0, text.Length);
            if (caret == 0) return false;

            int lineStart = text.LastIndexOf('\n', caret - 1) + 1;
            string before = text.Substring(lineStart, caret - lineStart);

            if (before.Length < IndentUnit.Length) return false;
            if (before.Trim().Length != 0) return false;

            ReplaceRange(caret - IndentUnit.Length, IndentUnit.Length, "");
            return true;
        }

        private static string LeadingWhitespace(string s)
        {
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            return s.Substring(0, i);
        }

        // ============ 保存 / 返回 ============

        /// <summary>保存按钮状态：已保存（无改动）灰显禁用，有改动才可点。</summary>
        private void UpdateSaveButtonState()
        {
            SaveButton.IsEnabled = _dirty;
            SaveButton.Opacity = _dirty ? 1.0 : 0.4;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // ★★ 落盘**永远写英文原码** —— 磁盘上的脚本是引擎唯一认的东西，
            //   中文只是编辑器里的显示形态，绝不能存成中文（否则下次打开解析会报「不认识的指令」）。
            string english = MacroScriptTranslator.Translate(Editor.Text ?? "", toChinese: false);
            MacroSettingsStore.SetScript(_macroId, english);

            // 基准也跟着更新成「刚存下去的英文原码」⇒ 保存后立刻变成未修改状态
            _savedEnglishNorm = NormalizeForCompare(Editor.Text);

            _dirty = false;
            UpdateSaveButtonState();
        }

        // ============ 指令工具面板（右侧） ============

        /// <summary>工具面板点了「插入」→ 把整条指令贴到编辑器光标处。</summary>
        private void ToolPanel_InsertRequested(object? sender, string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            // ★ 一行只能一个指令 ⇒ 插进去时后面补换行，避免和下一行黏成一条
            string text = command.TrimEnd();
            if (!text.EndsWith("\n")) text += Environment.NewLine;

            InsertAtCaret(text);
            Editor.Focus();
        }

        // ============ 主题切换（全局，整个程序） ============

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTheme(sender as Visual);
        }

        /// <summary>
        /// 主题变化监视器（常驻）。
        /// <para>
        /// ★ 为什么要轮询而不是订阅事件：宿主的 <c>ThemeService.ThemeChanged</c> 是
        /// <c>EventHandler&lt;AppTheme&gt;</c>，泛型参数在宿主 dll 里，插件没法直接引用，
        /// 反射挂事件很别扭 ⇒ 改成轻量轮询（250ms 一次，只读一个枚举值，几乎不耗）。
        /// </para>
        /// <para>
        /// ★ 主题一变就要做三件事：
        ///   ① 清掉高亮的画刷缓存（否则还拿着旧主题的颜色，深色下看不清）；
        ///   ② 重新配置语法高亮（按新主题重算颜色）；
        ///   ③ 刷新太阳/月亮图标。
        /// </para>
        /// </summary>
        private void StartThemeWatcher()
        {
            _lastThemeName = ReadCurrentThemeName();

            _themeWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _themeWatcher.Tick += (_, _) =>
            {
                var now = ReadCurrentThemeName();
                if (now == null || now == _lastThemeName) return;

                _lastThemeName = now;

                // ① 缓存里是旧主题的画刷，必须清
                BrushCache.Clear();

                // ② 按新主题重算高亮颜色
                ConfigureHighlighting();
                Editor.TextArea.TextView.InvalidateVisual();

                // ③ 图标跟着换
                UpdateThemeIcon();
            };
            _themeWatcher.Start();
        }

        /// <summary>读当前主题名（"Light" / "Dark"），读不到返回 null。</summary>
        private static string? ReadCurrentThemeName()
        {
            try
            {
                var type = Type.GetType("LvchaxsBS.Services.ThemeService, LvchaxsBS");
                var cur = type?.GetProperty("Current",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                return cur?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 切换全局主题，**带迷雾扩散过渡**（和主窗口右上角那个主题按钮一样的观感）。
        /// <para>
        /// 优先反射调用宿主 <c>ThemeService.ToggleWithTransition(Visual?)</c>：
        /// 它会找到主窗口的迷雾层，从按钮中心扩散出去再换主题。
        /// 宿主是旧版（没这个方法）就退回 <c>ThemeService.Toggle()</c> 硬切，功能不坏。
        /// </para>
        /// </summary>
        private static void ToggleTheme(Visual? origin)
        {
            try
            {
                var type = Type.GetType("LvchaxsBS.Services.ThemeService, LvchaxsBS");
                if (type == null) return;

                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

                var withTransition = type.GetMethod("ToggleWithTransition", flags);
                if (withTransition != null &&
                    withTransition.GetParameters().Length == 1 &&
                    withTransition.GetParameters()[0].ParameterType == typeof(Visual))
                {
                    withTransition.Invoke(null, new object?[] { origin });
                    return;
                }

                type.GetMethod("Toggle", flags)?.Invoke(null, null);
            }
            catch
            {
                // 反射失败不影响编辑
            }
        }

        /// <summary>按当前主题切换太阳/月亮图标（Dark 显示月亮、Light 显示太阳）。</summary>
        private void UpdateThemeIcon()
        {
            try
            {
                var type = Type.GetType("LvchaxsBS.Services.ThemeService, LvchaxsBS");
                var cur = type?.GetProperty("Current",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                bool isDark = string.Equals(cur?.ToString(), "Dark", StringComparison.Ordinal);

                ThemeIconSun.Visibility = isDark ? Visibility.Collapsed : Visibility.Visible;
                ThemeIconMoon.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                // 拿不到主题就不切图标，太阳常显
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateBackToList();

        public bool TryGoBack()
        {
            NavigateBackToList();
            return true;
        }

        public string GetBackTip() => _returnPage != null ? "返回首页" : "返回 宏列表";

        private void NavigateBackToList()
        {
            // ★ 返回前先重算一次脏状态（节流刷新可能还没跑到），
            //   否则「刚改完立刻点返回」会被当成没改动 ⇒ 改动丢失。
            RecomputeDirty();

            if (_dirty)
            {
                bool save = Utils.HostBridge.Confirm(
                    "有未保存的修改",
                    "有修改未被保存，是否保存？",
                    "保存",
                    "不保存");

                // ★ 落盘同样写英文原码（中文只是显示形态）
                if (save)
                    MacroSettingsStore.SetScript(_macroId,
                        MacroScriptTranslator.Translate(Editor.Text ?? "", toChinese: false));
                _dirty = false;
            }

            Page target = _returnPage
                ?? (Page?)MacroConfigPage.CurrentInstance
                ?? new MacroConfigPage();

            NavigationService?.Navigate(target);
        }


        // ============ 按下→抬起 灰色配对连线（背景渲染器） ============

        /// <summary>
        /// 在编辑器里画「按下 → 抬起」的灰色「]」配对连线 + 缺抬起的红色虚线。
        /// <para>★ IBackgroundRenderer 的 DrawingContext 是**视口坐标**，GetVisualTopByDocumentLine 返回**文档坐标**，
        ///   必须自己减 VerticalOffset / HorizontalOffset 转视口坐标。</para>
        /// <para>★ 括号横线从该行**文字末尾 + 4 空格**处出发（不固定），竖线 X = 两端横线起点较大者 + 嵌套宽度。</para>
        /// </summary>
        private sealed class PairLineRenderer : IBackgroundRenderer
        {
            private static readonly Pen BracketPen = CreatePen(Color.FromArgb(0x48, 0x9A, 0x9A, 0x9A), 1.0);
            // ★ 虚线括号：概念上是一对（同键连续按下导致没法真正对应）→ 连起来但用虚线
            private static readonly Pen DashBracketPen = CreatePen(Color.FromArgb(0x48, 0x9A, 0x9A, 0x9A), 1.0, dash: true);
            private static readonly Pen RedDashPen = CreatePen(Color.FromArgb(0x66, 0xE0, 0x50, 0x50), 1.0, dash: true);

            private const double DepthW = 18;   // 每嵌套一层竖线往外移的宽度
            private const double R = 4;         // 圆角半径

            private readonly MacroScriptEditorPage _page;

            public PairLineRenderer(MacroScriptEditorPage page)
            {
                _page = page;
            }

            private static Pen CreatePen(Color c, double w, bool dash = false)
            {
                var p = new Pen(new SolidColorBrush(c), w);
                if (dash) p.DashStyle = new DashStyle(new double[] { 4, 4 }, 0);
                p.Freeze();
                return p;
            }

            public KnownLayer Layer => KnownLayer.Background;

            public void Draw(TextView textView, DrawingContext dc)
            {
                try
                {
                    double voff = textView.VerticalOffset;
                    double hoff = textView.HorizontalOffset;
                    double vh = textView.ActualHeight;
                    double lineH = textView.DefaultLineHeight;
                    int lineCount = textView.Document.LineCount;

                    // ★ 自内向外算每个配对的竖线 X：内层先定，外层 = max(自己文字末尾, 内层竖线) + DepthW。
                    var bracketX = ComputeBracketX(hoff);

                    // 红色虚线：缺抬起的按下 / 同键连续按下被作废的按下
                    foreach (var u in _page._unpairedDowns)
                        DrawRedLine(dc, textView, u.Line, voff, hoff, vh, lineH, lineCount);

                    // 红色虚线：多出来的抬起
                    foreach (var u in _page._unpairedUps)
                        DrawRedLine(dc, textView, u.Line, voff, hoff, vh, lineH, lineCount);

                    // 灰色括号：配对的（虚线 = 只是概念上的一对）
                    foreach (var p in _page._pairs)
                        DrawBracket(dc, textView, p, bracketX[p], voff, hoff, vh, lineH, lineCount, p.Dashed);
                }
                catch
                {
                    // ★ 文档刚变、_pairs/_unpairedDowns 还是旧行号时会越界 —— 忽略，等 UpdateStatus 刷新
                }
            }

            /// <summary>自内向外算每个配对的竖线 X（视口坐标）。</summary>
            private Dictionary<MacroPair, double> ComputeBracketX(double hoff)
            {
                var result = new Dictionary<MacroPair, double>();

                var sorted = _page._pairs.OrderByDescending(p => p.Depth).ToList();
                foreach (var p in sorted)
                {
                    double own = Math.Max(LineEndPlusGap(p.DownLine, hoff), LineEndPlusGap(p.UpLine, hoff));

                    double innerMax = 0;
                    foreach (var other in _page._pairs)
                    {
                        if (ReferenceEquals(other, p)) continue;
                        if (other.DownLine > p.DownLine && other.UpLine < p.UpLine && result.TryGetValue(other, out double x))
                            innerMax = Math.Max(innerMax, x);
                    }

                    result[p] = Math.Max(own, innerMax) + DepthW;
                }

                return result;
            }

            /// <summary>取某行文字末尾 + 4 空格的 X（文档坐标 → 视口坐标）。</summary>
            private double LineEndPlusGap(int lineNo, double hoff)
            {
                double end = _page._lineEndX.TryGetValue(lineNo, out var x) ? x : 0;
                return end - hoff + 4 * _page._charW;
            }

            private void DrawBracket(DrawingContext dc, TextView tv, MacroPair p, double endX,
                                     double voff, double hoff, double vh, double lineH, int lineCount, bool dashed = false)
            {
                if (p.DownLine < 1 || p.DownLine > lineCount || p.UpLine < 1 || p.UpLine > lineCount) return;

                double top = tv.GetVisualTopByDocumentLine(p.DownLine) + lineH * 0.5 - voff;
                double bottom = tv.GetVisualTopByDocumentLine(p.UpLine) + lineH * 0.5 - voff;
                if (bottom < 0 || top > vh) return;

                double downEnd = LineEndPlusGap(p.DownLine, hoff);
                double upEnd = LineEndPlusGap(p.UpLine, hoff);

                bool downVisible = top >= 0 && top <= vh;
                bool upVisible = bottom >= 0 && bottom <= vh;
                double y0 = Math.Max(0, top);
                double y1 = Math.Min(vh, bottom);

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    if (downVisible)
                    {
                        ctx.BeginFigure(new Point(downEnd, top), false, false);
                        ctx.LineTo(new Point(endX - R, top), true, false);
                        ctx.QuadraticBezierTo(new Point(endX, top), new Point(endX, top + R), true, false);
                    }
                    else
                    {
                        ctx.BeginFigure(new Point(endX, y0), false, false);
                    }

                    if (upVisible)
                    {
                        ctx.LineTo(new Point(endX, bottom - R), true, false);
                        ctx.QuadraticBezierTo(new Point(endX, bottom), new Point(endX - R, bottom), true, false);
                        ctx.LineTo(new Point(upEnd, bottom), true, false);
                    }
                    else
                    {
                        ctx.LineTo(new Point(endX, y1), true, false);
                    }
                }

                dc.DrawGeometry(null, dashed ? DashBracketPen : BracketPen, geo);
            }

            private void DrawRedLine(DrawingContext dc, TextView tv, int lineNo,
                                     double voff, double hoff, double vh, double lineH, int lineCount)
            {
                if (lineNo < 1 || lineNo > lineCount) return;

                double y = tv.GetVisualTopByDocumentLine(lineNo) + lineH * 0.5 - voff;
                if (y < 0 || y > vh) return;

                double startX = LineEndPlusGap(lineNo, hoff);
                dc.DrawLine(RedDashPen, new Point(startX, y), new Point(tv.ActualWidth, y));
            }
        }
    }
}
