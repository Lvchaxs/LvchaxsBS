using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LvchaxsBS_Macro.Core;
using LvchaxsBS_Macro.Utils;

namespace LvchaxsBS_Macro.Controls
{
    /// <summary>
    /// 宏指令「工具面板」：右侧列出**所有支持的指令**，每条带参数输入 + 复制 / 插入。
    /// <para>
    /// ★ 键盘类指令：键名那一格是**按钮**（不是输入框）—— 点它进入**监听模式**，
    ///   按任意键就把键名填进去（例：按 W → 显示 "W"），不用手打。
    ///   ★★ 监听走的是**全局低级键盘钩子**（<see cref="KeyWatcher"/>），不是 WPF 的键盘事件：
    ///   插件是反射加载进宿主的独立 dll，宿主窗口又是透明置顶窗，
    ///   WPF 键盘事件经常送不到这个控件上（"点了没反应"就是栽在这里）。
    ///   全局钩子与焦点无关，键按下去一定收得到。
    /// </para>
    /// <para>
    /// ★ 「复制」= 整条指令复制到剪贴板；「插入」= 贴到编辑器光标处。
    /// </para>
    /// <para>
    /// ★★ 颜色**全部走动态资源**（<c>SetResourceReference</c>）—— 主题切换时资源字典整个换掉，
    ///   静态引用会留在旧主题的颜色上（深色下黑字黑底看不见）。动态绑定才会跟着换。
    /// </para>
    /// </summary>
    public partial class MacroCommandToolPanel : UserControl
    {
        /// <summary>要往编辑器里插入的整条指令（宿主收到后插到光标处）。</summary>
        public event EventHandler<string>? InsertRequested;

        /// <summary>
        /// 面板生成的指令用什么语言：<c>true</c> = 中文别名（按键键 E / 延迟 100），
        /// <c>false</c> = 英文原码（KeyDown E / Delay 100）。
        /// <para>★ 由宿主页面的「中/英」按钮同步，**复制**和**插入**都按这个语言输出，
        ///   免得中文显示时插进去一条英文的。用户看到的要和编辑器里的一致。</para>
        /// </summary>
        public bool ChineseFormat { get; set; }

        public MacroCommandToolPanel()
        {
            InitializeComponent();

            // ★ 主题切换后要重刷一批「写死了具体颜色」的控件（这里是图标按钮的悬停背景）。
            //   订阅宿主 ThemeService.ThemeChanged 做不到（泛型 EventHandler<AppTheme> 在宿主 dll 里，
            //   插件没法引用）→ 沿用编辑页的老办法：定时轮询主题名，一变就刷。
            _themeWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _themeWatcher.Tick += (_, _) =>
            {
                bool dark = IsDarkTheme();
                if (dark == _lastThemeDark) return;
                _lastThemeDark = dark;
                foreach (var r in _themeRefreshers)
                {
                    try { r(); } catch { /* 单个控件刷失败不影响其它 */ }
                }
            };
            _lastThemeDark = IsDarkTheme();
            _themeWatcher.Start();

            BuildItems();

            // ★ 面板被换掉 / 关掉时把全局钩子摘掉（否则钩子一直挂着，白白收键）
            Unloaded += (_, _) => StopAllWatching();

            // ★ 吞键的「延迟解除」定时器：撑过 Space 的 KeyDown→KeyUp 整个周期，
            //   否则 KeyUp 漏出去会触发 Button.Click（表现为键名被清空）。
            _swallowReleaseTimer.Tick -= (_, _) => StopSwallowKeysNow();
            _swallowReleaseTimer.Tick += (_, _) => StopSwallowKeysNow();
        }

        private readonly DispatcherTimer _themeWatcher;
        private bool _lastThemeDark;

        /// <summary>主题切换后需要重刷外观的控件回调（由 <see cref="_themeWatcher"/> 统一触发）。</summary>
        private readonly List<Action> _themeRefreshers = new();

        // =====================================================================
        // 面板构建
        // =====================================================================

        private void BuildItems()
        {
            ItemsHost.Children.Clear();

            // ---------- 键盘 ----------
            AddGroup("键盘");
            AddHint("点击按钮后按下任意键盘按键 → 自动填入");
            AddKeyRow("KeyDown", "按住某个键", "A");
            AddKeyRow("KeyUp", "松开某个键", "A");
            AddKeyRow("KeyPress", "按一下某个键", "A");

            // ---------- 延迟 ----------
            AddGroup("延迟");
            AddNumberRow("Delay", "等待多少毫秒", "100");

            // ---------- 鼠标 ----------
            AddGroup("鼠标");
            AddHint("点击按钮后按下鼠标左/右/中键 → 自动填入");
            AddMouseRow("MouseDown", "按住鼠标某个键", "Left");
            AddMouseRow("MouseUp", "松开鼠标某个键", "Left");
            AddMouseRow("MouseClick", "点击鼠标某个键", "Left");
            // ★ 两个数字之间写**逗号**更好读（一眼看出是两个坐标而不是一个值）
            AddNumberRow("Move", "移动鼠标（相对：X Y）", "10, -20");
            AddNumberRow("MoveTo", "移动鼠标到（绝对：X Y）", "800, 600");
            AddNumberRow("Wheel", "滚轮格数（正上/负下）", "1");

            // ---------- 输入 ----------
            AddGroup("输入");
            AddTextRow("Text", "粘贴一段文本（后面跟原文，中文可直接写）", "你好");

            // ---------- 代码块 ----------
            AddGroup("代码块");
            AddBlockRow();
        }

        private void AddGroup(string title)
        {
            var tb = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(2, 10, 0, 5),
            };
            SetRes(tb, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            ItemsHost.Children.Add(tb);
        }

        /// <summary>一行灰色小提示（代替 ToolTip 气泡）。</summary>
        private void AddHint(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 0, 4),
            };
            SetRes(tb, TextBlock.ForegroundProperty, "TextDisabledBrush");
            ItemsHost.Children.Add(tb);
        }

        // ---------- 键盘行 ----------

        /// <summary>
        /// 键盘类指令行：<b>键名是按钮</b>（不是输入框），点它进入监听。
        /// <para>★ 只有 <c>Delay</c> / 坐标 / 滚轮 / 按住毫秒 这些**数值项**才用输入框。</para>
        /// </summary>
        /// <param name="op">
        /// 指令名。<c>KeyDown</c>=按住某键、<c>KeyUp</c>=松开某键、<c>KeyPress</c>=按一下某键。
        /// </param>
        /// <param name="defaultKey">默认键名。</param>
        private void AddKeyRow(string op, string desc, string defaultKey)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            row.Children.Add(MakeLabel(op, desc));

            // 中间：就是键名按钮（★ 是按钮不是输入框）
            // ★ 2026-10-20：原来的「按住毫秒」输入框已按用户要求删除 ——
            //   KeyPress 默认按住 0ms（= 瞬间点一下），引擎 `KeyPress(vk, holdMs = 0)` 本来就默认 0。
            var mid = new Grid();
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string keyName = defaultKey;
            var keyBtn = MakeKeyButton(defaultKey);
            Grid.SetColumn(keyBtn, 0);
            mid.Children.Add(keyBtn);

            bool listening = false;

            void StopListen()
            {
                if (!listening) return;
                listening = false;
                _activeKeyHandler = null;
                _modifierTimer.Stop();
                _pendingModifierVk = 0;
                KeyWatcher.KeyPressed -= OnGlobalKeyPressed;
                KeyWatcher.Stop();
                StopSwallowKeys();
                StopCancelOnOutsideClick();
                _activeKeyButton = null;
                _cancelActiveListen = null;
                SetKeyButtonListening(keyBtn, false);
            }

            void Accept(string name)
            {
                keyName = name;
                StopListen();
                keyBtn.Content = KeyButtonContent(name);
            }

            void StartListen()
            {
                listening = true;
                _pendingModifierVk = 0;

                // ★ 与鼠标监听**互斥**：开始监听键盘前先把鼠标那边收干净
                StopMouseWatching();

                _activeKeyHandler = vk =>
                {
                    // ★★ Esc **不再当取消键** —— 用户要能录 Esc 本身。
                    //   取消监听统一走「点按钮以外的地方」（见 OnClickOutsideKeyButton）。

                    // 引擎不认识的键（打印屏 / 一些 OEM 键）→ 继续等，不填
                    string? name = MacroKeyNames.FromVirtualKey(vk);
                    if (name == null) return;

                    // ★★ 修饰键（Shift / Ctrl / Alt / Win）**先按住不放**：
                    //   按 Ctrl+W 想要的是 W；只有**单独**按 Ctrl 才算要录 Ctrl。
                    //   所以修饰键先挂 400ms —— 这段时间里来了别的键就用那个，
                    //   没人跟就认定用户要录这个修饰键。
                    if (MacroKeyNames.IsModifier(vk))
                    {
                        _pendingModifierVk = vk;
                        _modifierTimer.Stop();
                        _modifierTimer.Start();
                        return;
                    }

                    _pendingModifierVk = 0;
                    _modifierTimer.Stop();
                    Accept(name);
                };

                _modifierTimer.Tick -= OnModifierTimeout;
                _modifierTimer.Tick += OnModifierTimeout;
                void OnModifierTimeout(object? s, EventArgs e)
                {
                    _modifierTimer.Stop();
                    int vk = _pendingModifierVk;
                    _pendingModifierVk = 0;
                    if (vk == 0 || !listening) return;

                    string? name = MacroKeyNames.FromVirtualKey(vk);
                    if (name == null) return;

                    Accept(name);
                }

                KeyWatcher.KeyPressed -= OnGlobalKeyPressed;
                KeyWatcher.KeyPressed += OnGlobalKeyPressed;
                KeyWatcher.Start();

                SetKeyButtonListening(keyBtn, true);
                keyBtn.Content = "按任意键…";

                // ★ 焦点挪到按钮上 + 窗口级吞掉按键：
                //   否则按下的键会落进 AvalonEdit 编辑器里，把脚本内容改了。
                keyBtn.Focus();
                StartSwallowKeys();

                // ★ 点按钮以外的地方 = 取消监听（用户要求，比「必须再点一次按钮」自然）
                _activeKeyButton = keyBtn;
                _cancelActiveListen = () =>
                {
                    StopListen();
                    keyBtn.Content = KeyButtonContent(keyName);   // 恢复原来的键名
                };
                StartCancelOnOutsideClick();
            }

            keyBtn.Click += (_, _) =>
            {
                if (listening)
                {
                    // 再点一次 = 取消监听
                    StopListen();
                    keyBtn.Content = KeyButtonContent(keyName);
                }
                else
                {
                    StartListen();
                }
            };

            var right = new Grid { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(mid);
            Grid.SetColumn(right, 1);
            row.Children.Add(right);

            AddActionButtons(row, op, () => BuildKeyCommand(op, keyName));
            ItemsHost.Children.Add(row);
        }

        /// <summary>当前正在监听的键名行（同一时刻只有一个）。</summary>
        private Action<int>? _activeKeyHandler;

        /// <summary>修饰键等待窗口：按下修饰键后，等这么久看有没有别的键跟上来。</summary>
        private readonly DispatcherTimer _modifierTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };

        /// <summary>当前挂着的修饰键（等待中）。</summary>
        private int _pendingModifierVk;

        /// <summary>全局钩子回调 → 转给当前正在监听的行。</summary>
        private void OnGlobalKeyPressed(int vk) => _activeKeyHandler?.Invoke(vk);

        /// <summary>
        /// 监听期间**吞掉**所有按键：不让它们落进 AvalonEdit 编辑器把脚本改了。
        /// <para>★ 挂在**窗口**上（而不是按钮上）：按钮拿到焦点也不代表键盘事件一定先到它，
        ///   挂窗口最稳。</para>
        /// </summary>
        private void StartSwallowKeys()
        {
            StopSwallowKeysNow();

            var win = Window.GetWindow(this);
            if (win == null) return;

            // handledEventsToo = true：即使别的控件已经处理过（e.Handled = true），我们也要拦
            win.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnSwallowKey), true);
            win.AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(OnSwallowKey), true);
            _swallowWindow = win;
            _swallowActive = true;
            _swallowReleaseTimer.Stop();
        }

        /// <summary>
        /// 停止吞键 —— 但**延后 <see cref="_swallowReleaseDelay"/> 毫秒**真正执行。
        /// <para>
        /// ★★ 这是修「按下空格录到 Space，**抬起后按钮被清空**」的关键：
        ///   WPF 的 Button 是**在 Space 的 KeyUp 时触发 Click**（不是 KeyDown）。
        ///   而我们在 KeyDown 录到键后就立刻 StopListen → `_activeKeyHandler` 置 null
        ///   → 若吞键标志也跟着判 `_activeKeyHandler`，KeyUp 就不再被拦
        ///   → Space KeyUp 落到按钮上 ⇒ 触发 Click ⇒ 又被当成「再点一次 = 取消」⇒ 键名被清空。
        ///   ⇒ 吞键标志必须**独立于** handler，并且要撑过整个 KeyDown→KeyUp 周期。
        /// </para>
        /// </summary>
        private void StopSwallowKeys()
        {
            if (!_swallowActive) return;
            _swallowReleaseTimer.Stop();
            _swallowReleaseTimer.Start();
        }

        private void StopSwallowKeysNow()
        {
            _swallowReleaseTimer.Stop();
            if (!_swallowActive) return;
            _swallowActive = false;

            if (_swallowWindow == null) return;
            try
            {
                _swallowWindow.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnSwallowKey));
                _swallowWindow.RemoveHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(OnSwallowKey));
            }
            catch
            {
                // 窗口已经没了：无所谓
            }
            _swallowWindow = null;
        }

        private Window? _swallowWindow;

        /// <summary>正在监听的键名按钮（窗口级鼠标事件据此判断「是不是点在自己身上」）。</summary>
        private Button? _activeKeyButton;

        /// <summary>取消当前监听并恢复原键名的回调（由 <see cref="StartListen"/> 登记）。</summary>
        private Action? _cancelActiveListen;

        private Window? _outsideClickWindow;

        /// <summary>监听期间挂「点别处取消」。</summary>
        private void StartCancelOnOutsideClick()
        {
            StopCancelOnOutsideClick();
            var win = Window.GetWindow(this);
            if (win == null) return;

            // ★ 只拦**按下**那一刻，不拦抬起，也不吞事件 ——
            //   点了「复制/插入」按钮时，取消监听后那个点击仍要正常生效。
            win.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnClickOutsideKeyButton), true);
            win.AddHandler(UIElement.PreviewMouseRightButtonDownEvent,
                new MouseButtonEventHandler(OnClickOutsideKeyButton), true);
            _outsideClickWindow = win;
        }

        private void StopCancelOnOutsideClick()
        {
            if (_outsideClickWindow == null) return;
            try
            {
                _outsideClickWindow.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                    new MouseButtonEventHandler(OnClickOutsideKeyButton));
                _outsideClickWindow.RemoveHandler(UIElement.PreviewMouseRightButtonDownEvent,
                    new MouseButtonEventHandler(OnClickOutsideKeyButton));
            }
            catch
            {
                // 窗口没了：无所谓
            }
            _outsideClickWindow = null;
        }

        private void OnClickOutsideKeyButton(object sender, MouseButtonEventArgs e)
        {
            var cancel = _cancelActiveListen;
            if (cancel == null) return;

            // 点在自己身上（按钮本体或它的模板子元素）→ 不取消
            var btn = _activeKeyButton;
            if (btn != null && e.OriginalSource is DependencyObject src && btn.IsAncestorOf(src))
                return;

            cancel();
        }

        /// <summary>是否正在吞键（独立于 <see cref="_activeKeyHandler"/>，见 <see cref="StopSwallowKeys"/> 注释）。</summary>
        private bool _swallowActive;

        /// <summary>松手后再过这么久才真正解除吞键（要撑过 KeyDown→KeyUp 这一整个周期）。</summary>
        private static readonly TimeSpan _swallowReleaseDelay = TimeSpan.FromMilliseconds(350);

        private readonly DispatcherTimer _swallowReleaseTimer = new DispatcherTimer
        {
            Interval = _swallowReleaseDelay,
        };

        private void OnSwallowKey(object sender, KeyEventArgs e)
        {
            // ★ 判据是「是否还在吞键」，**不是** `_activeKeyHandler != null`
            //   —— 否则 Space 的 KeyUp 会漏出去触发 Button.Click（清空键名）。
            if (!_swallowActive) return;
            e.Handled = true;
        }

        /// <summary>组装一条键盘指令的文本。</summary>
        private static string BuildKeyCommand(string op, string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return op;
            return $"{op} {key}";
        }

        // ---------- 数字行 ----------

        private void AddNumberRow(string op, string desc, string defaultValue)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            row.Children.Add(MakeLabel(op, desc));

            var box = MakeTextBox(defaultValue);
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            AddActionButtons(row, op, () => $"{op} {NormalizeNumberArgs(box.Text)}");
            ItemsHost.Children.Add(row);
        }

        /// <summary>
        /// 把参数里的逗号（<c>,</c> / <c>，</c>）当成分隔符换成空格，多余空白压成一个。
        /// <para>
        /// ★ 面板里坐标写成「800, 600」更好读（一眼看出是两个坐标），
        ///   但宏引擎只认**空格分隔**的写法 ⇒ 拼成指令文本之前在这里统一归一。
        /// </para>
        /// </summary>
        private static string NormalizeNumberArgs(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            return string.Join(' ', text.Replace('，', ' ').Replace(',', ' ')
                                       .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        // ---------- 鼠标按键行 ----------

        /// <summary>鼠标键的**显示文案**（中文）。★ 下拉里给用户看的是中文。</summary>
        private static readonly string[] MouseKeyLabels = { "左键", "右键", "中键" };

        /// <summary>鼠标键的**英文原码**（引擎只认这套）—— 与 <see cref="MouseKeyLabels"/> 一一对应。</summary>
        private static readonly string[] MouseKeyValues = { "Left", "Right", "Middle" };

        private void AddMouseRow(string op, string desc, string defaultValue)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            row.Children.Add(MakeLabel(op, desc));

            // ★★ 2026-10-20：鼠标键那一格也改成**监听按钮**（原来是指甲盖那么小的下拉，
            //   根本没法点、也没法显示清楚）。跟键盘那三行完全一致：
            //   点它 → 在**任何地方**（包括游戏窗口）按下鼠标左/右/中键 → 自动填入；点别处即取消。
            //   ★ 按钮上显示**中文**（左键/右键/中键），拼出来的指令用**英文原码**
            //     （Left/Right/Middle）—— 宏引擎只认英文原码。
            string mouseKey = defaultValue;   // 英文原码
            // ★ 宽度跟上面三行的键名格**完全一致**：不设 MinWidth、不设 HorizontalAlignment，
            //   直接让它在 star 列里拉伸（和 AddKeyRow 一样）。之前多写了 MinWidth=74 + Left，
            //   三行鼠标键就比三行键盘键窄一截，一眼能看出来。
            var keyBtn = MakeKeyButton(MouseLabelOf(mouseKey));
            var mid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(keyBtn);
            Grid.SetColumn(mid, 1);
            row.Children.Add(mid);

            bool listening = false;

            // ★ 本次「按下」有没有刚刚录成键（用来区分：这次点击是「录左键」还是「重新开始监听」）
            //   用**时间戳**而不是 bool：钩子通道录到右键（发生在窗口外）之后，
            //   bool 会一直挂着 true，导致用户下一次正常点按钮被误吞。
            long acceptedTicks = 0;

            void StopListen()
            {
                if (!listening) return;
                listening = false;
                _activeMouseHandler = null;
                MouseWatcher.Stop();
                StopWindowMouseCapture();
                StopSwallowKeys();
                StopCancelOnOutsideClick();
                _activeKeyButton = null;
                _cancelActiveListen = null;
                SetKeyButtonListening(keyBtn, false);
            }

            void Accept(string enValue)
            {
                mouseKey = enValue;
                StopListen();
                keyBtn.Content = MouseLabelOf(enValue);
                // ★ 告诉 Click 处理器：刚刚录了一次键，别再开一轮监听
                //   （时间戳要比「按下」到「Click」之间那几十毫秒长，别误吞后续的正常点击）
                acceptedTicks = Environment.TickCount64;
            }

            void StartListen()
            {
                listening = true;
                acceptedTicks = 0;      // 新一轮监听，清掉上一轮的「刚录过」标记

                // ★ 与键盘监听**互斥**：两个钩子同时挂着没意义，先把键盘那边停掉
                StopKeyListening();

                _activeMouseHandler = button =>
                {
                    string? en = MouseValueOf(button);
                    // 侧键等引擎不认的键：继续等，不填
                    if (en == null) return;
                    Accept(en);
                };

                // ★★ **双通道**监听（探针实测后的结论）：
                //   ① 窗口级 WPF 事件 —— 点在**宿主窗口内**（面板、编辑器、空白处）必定收得到，
                //      走的是 WPF 自己的输入管线，不依赖任何钩子，最可靠；
                //   ② 全级低级鼠标钩子 —— 点在**别的窗口**（比如游戏里）时兜底。
                //   两个通道都指向同一个处理函数，谁先到都行；
                //   Accept() 里第一件事就是 StopListen()，所以同一次点击不会填两遍。
                //   ⚠️ 光靠钩子曾经一直「没反应」；光靠窗口事件又覆盖不到窗口外 —— 都要。
                StartWindowMouseCapture();
                MouseWatcher.MouseButtonPressed -= OnGlobalMouseDispatch;
                MouseWatcher.MouseButtonPressed += OnGlobalMouseDispatch;
                MouseWatcher.Start();

                SetKeyButtonListening(keyBtn, true, "点任意鼠标键…");
                keyBtn.Content = "点任意鼠标键…";

                // 焦点挪到按钮 + 窗口级吞键：监听时按到的键不该落进编辑器把脚本改了
                keyBtn.Focus();
                StartSwallowKeys();

                // ★★ **不挂**「点别处取消」：鼠标监听时点哪儿都是「要录的那一下」，
                //   挂上去会把刚填好的键名又恢复成原来的 —— 表现就是「按右键却显示还是左键」。
                //   取消统一走「再点按钮一下」。
                _activeKeyButton = keyBtn;
                _cancelActiveListen = null;
            }

            keyBtn.Click += (_, _) =>
            {
                // ★★ 监听中再点按钮一下 = **录左键**（不是取消监听）。
                //   这正是 WPF 事件的自然顺序：PreviewMouseDown 先到 → 窗口通道已经把这次
                //   点击记成「左键」并 Accept()（顺带 StopListen 了）；
                //   紧接着 MouseUp 触发 Button.Click —— 此时 listening 已是 false，
                //   如果这里再走 else 分支就会**重新开始监听**，于是按钮一直显示
                //   「点任意鼠标键…」，怎么点都停不下来。
                //   ⇒ 判定「刚才那次 Accept 是不是就是本次点击引起的」：同一次点击里
                //   PreviewMouseDown → Click 间隔通常 <300ms，超过就认为是两次独立点击。
                if (Environment.TickCount64 - acceptedTicks < 300)
                {
                    acceptedTicks = 0;
                    return;   // 这次点击已经录成左键了，别再开一轮监听
                }

                if (listening)
                {
                    // 再点一次 = 取消监听
                    StopListen();
                    keyBtn.Content = MouseLabelOf(mouseKey);
                }
                else
                {
                    StartListen();
                }
            };

            AddActionButtons(row, op, () => $"{op} {mouseKey}");
            ItemsHost.Children.Add(row);
        }

        /// <summary>
        /// 把文本写进剪贴板。
        /// <para>★★ **不要用 WPF 的 <c>Clipboard.SetText</c>**：它在 UI 线程同步调用时会走
        ///   OLE 的 <c>OleSetClipboard</c> + 失败重试（每次带几十毫秒延迟），
        ///   连续点两下复制就会明显**卡顿**（用户反馈）。</para>
        /// <para>★ 优先反射调用宿主的 <c>LvchaxsBS.Services.FastClipboard.WriteText</c> ——
        ///   那是原生 <c>OpenClipboard / SetClipboardData</c> 实现的（5ms 短重试），几乎不卡。
        ///   宿主没有这个类时退回 WPF 的 clipboard（功能不坏，只是可能卡）。</para>
        /// </summary>
        private static void CopyToClipboard(string text)
        {
            try
            {
                var type = Type.GetType("LvchaxsBS.Services.FastClipboard, LvchaxsBS");
                var m = type?.GetMethod("WriteText",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);

                if (m?.Invoke(null, new object[] { text }) is true) return;

                // 宿主没有 / 调用失败 → 退回 WPF
                Clipboard.SetText(text);
            }
            catch
            {
                try { Clipboard.SetText(text); } catch { /* 剪贴板被占用：忽略 */ }
            }
        }

        /// <summary>鼠标键编号 → 英文原码（引擎认的）。返回 null = 引擎不认（侧键）。</summary>
        private static string? MouseValueOf(int button) => button switch
        {
            MouseWatcher.ButtonLeft => "Left",
            MouseWatcher.ButtonRight => "Right",
            MouseWatcher.ButtonMiddle => "Middle",
            _ => null,
        };

        /// <summary>英文原码 → 中文显示文案（面板上给用户看的）。</summary>
        private static string MouseLabelOf(string value)
        {
            int i = Array.IndexOf(MouseKeyValues, value);
            return i >= 0 && i < MouseKeyLabels.Length ? MouseKeyLabels[i] : value;
        }

        /// <summary>
        /// 停掉**键盘**监听（如果正有键盘行在监听）。
        /// <para>★ 键盘和鼠标的监听共用同一套「点别处取消 / 吞键 / 按钮高亮」的状态，
        ///   所以开始监听鼠标前先确保键盘那边已经收干净。</para>
        /// </summary>
        private void StopKeyListening()
        {
            _activeKeyHandler = null;
            _modifierTimer.Stop();
            _pendingModifierVk = 0;
            KeyWatcher.KeyPressed -= OnGlobalKeyPressed;
            KeyWatcher.Stop();
        }

        /// <summary>停掉**鼠标**监听（如果正有鼠标行在监听）。</summary>
        private void StopMouseWatching()
        {
            _activeMouseHandler = null;
            MouseWatcher.MouseButtonPressed -= OnGlobalMouseDispatch;
            MouseWatcher.Stop();
            StopWindowMouseCapture();
        }

        /// <summary>当前正在监听的鼠标行（同一时刻只有一个）。</summary>
        private Action<int>? _activeMouseHandler;

        /// <summary>全局钩子回调 → 转给当前正在监听的鼠标行。</summary>
        private void OnGlobalMouseDispatch(int button) => _activeMouseHandler?.Invoke(button);

        // ---------- 窗口级鼠标捕获（钩子之外的第二条通道）----------

        private Window? _mouseCaptureWindow;

        /// <summary>
        /// 在**宿主窗口**上挂鼠标按下事件当第二条监听通道。
        /// <para>★ 为什么必须要有这条：低级钩子在某些宿主环境（提权运行、宿主有自己的输入处理）
        ///   会莫名其妙收不到事件，纯钩子方案表现为「点了没反应」。而点在窗口内时，
        ///   WPF 自己的输入管线**一定**会把 PreviewMouseButtonDown 送到窗口上 —— 这条最稳。</para>
        /// <para>★ 用 Preview（隧道）阶段 + <c>handledEventsToo: true</c>：
        ///   这样子控件（按钮、编辑器）就算已经把事件标记为 Handled，我们也照样收得到。</para>
        /// </summary>
        private void StartWindowMouseCapture()
        {
            StopWindowMouseCapture();
            var win = Window.GetWindow(this);
            if (win == null) return;

            // ★ 用 PreviewMouseDown（三种键统一走这一个事件）+ args.ChangedButton 区分左右中，
            //   比分别挂 Left/Right/MiddleButtonDown 简洁（后者中键的事件挂在 MouseButtonBase 上，
            //   命名空间容易踩坑）。
            // ★ Preview（隧道）阶段 + handledEventsToo=true：
            //   子控件（按钮、编辑器）就算已经把事件标记 Handled，我们也照样收得到。
            win.AddHandler(UIElement.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnWindowMouseCaptured), true);
            _mouseCaptureWindow = win;
        }

        private void StopWindowMouseCapture()
        {
            if (_mouseCaptureWindow == null) return;
            try
            {
                _mouseCaptureWindow.RemoveHandler(UIElement.PreviewMouseDownEvent,
                    new MouseButtonEventHandler(OnWindowMouseCaptured));
            }
            catch
            {
                // 窗口没了：无所谓
            }
            _mouseCaptureWindow = null;
        }

        private void OnWindowMouseCaptured(object sender, MouseButtonEventArgs e)
        {
            // ★ 不吞事件：这次点击还得正常生效（点复制/插入按钮要真的复制/插入）
            _activeMouseHandler?.Invoke(e.ChangedButton switch
            {
                MouseButton.Left => MouseWatcher.ButtonLeft,
                MouseButton.Right => MouseWatcher.ButtonRight,
                MouseButton.Middle => MouseWatcher.ButtonMiddle,
                _ => 0,
            });
        }

        // ---------- 文本行 ----------

        private void AddTextRow(string op, string desc, string defaultValue)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            row.Children.Add(MakeLabel(op, desc));

            var box = MakeTextBox(defaultValue);
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            AddActionButtons(row, op, () => $"{op} {box.Text}");
            ItemsHost.Children.Add(row);
        }

        // ---------- 代码块行 ----------

        private void AddBlockRow()
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            row.Children.Add(MakeLabel("代码块", "调用一个代码块（定义写「名字 { … }」）"));

            var box = MakeTextBox("MyBlock");
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            AddActionButtons(row, "Block", () => box.Text.Trim() + "()");
            ItemsHost.Children.Add(row);
        }

        // =====================================================================
        // 控件工厂
        // =====================================================================

        /// <summary>左边那列（指令名 + 说明）。</summary>
        private static StackPanel MakeLabel(string op, string desc)
        {
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var name = new TextBlock
            {
                Text = op,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
            };
            SetRes(name, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            left.Children.Add(name);

            var d = new TextBlock
            {
                Text = desc,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
            };
            SetRes(d, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            left.Children.Add(d);

            return left;
        }

        /// <summary>
        /// 统一风格的输入框。
        /// <para>★ 样式直接套宿主主程序的 <c>PublicTextBoxStyle</c>（圆角边框 + 聚焦紫色描边 + 悬停反馈），
        ///   跟主程序里的输入框完全一样。宿主没这个样式时退回本地设置。</para>
        /// </summary>
        private static TextBox MakeTextBox(string text)
        {
            var box = new TextBox
            {
                Text = text,
                FontSize = 12,
                Height = 26,
                Padding = new Thickness(4, 1, 4, 1),
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            // ★ 复用宿主样式（取不到就保留默认样式，不至于崩）
            if (Application.Current?.TryFindResource("PublicTextBoxStyle") is Style hostStyle)
                box.Style = hostStyle;

            SetRes(box, Control.ForegroundProperty, "TextPrimaryBrush");
            SetRes(box, Control.BackgroundProperty, "AppBackgroundBrush");
            SetRes(box, Control.BorderBrushProperty, "BorderBrush");
            return box;
        }

        private static Button MakeSmallButton(string content)
        {
            var btn = new Button
            {
                Content = content,
                Width = 22,
                Height = 22,
                Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(0),
                FontSize = 11,
                Cursor = Cursors.Arrow,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetRes(btn, Control.ForegroundProperty, "TextSecondaryBrush");
            SetRes(btn, Control.BackgroundProperty, "AppBackgroundBrush");
            SetRes(btn, Control.BorderBrushProperty, "BorderBrush");
            return btn;
        }

        // =====================================================================
        // 键名按钮（★ 不是输入框）
        // =====================================================================

        /// <summary>
        /// 键名那一格的按钮。外观做成**跟输入框一样**（圆角边框 + 悬停描边），
        /// 这样整行看上去还是整齐的，只是它**点一下就进入监听**。
        /// <para>★ 不用宿主的 <c>BaseButtonStyle</c>：那个把 <c>Focusable</c> 设成了 false，
        ///   而监听时**必须**能把焦点挪到按钮上（否则按键落进编辑器把脚本改了）。</para>
        /// </summary>
        private static Button MakeKeyButton(string keyName)
        {
            var btn = new Button
            {
                // ★★★ 纯键名，**不加 ▾ 之类箭头**：加了会被当成下拉列表选择控件（用户明确反感）
                Content = keyName,
                FontSize = 12,
                Height = 26,
                MinWidth = 56,
                Padding = new Thickness(8, 0, 8, 0),
                Cursor = Cursors.Hand,
                // ★ 必须可聚焦：监听时焦点要挪到它身上
                Focusable = true,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };

            // ★★ 直接用**主程序现成的按钮样式** `SettingsButtonStyleRound6`
            //   （卡片底 + 灰字 + 细边框 + 悬停变亮）—— 跟面板里其它控件观感一致，
            //   也跟这一列原来的输入框差不多，**不要突兀的紫底**。
            //   —— 只覆盖 Focusable：宿主样式是给图标小按钮设的 Focusable="False"，
            //      而监听时按键必须能送到这个按钮上。
            //   —— 取不到就退到 WPF 默认外观（照样可见）。
            btn.Style = TryFindHostButtonStyle();
            btn.Focusable = true;

            // ★★ 关掉宿主样式带来的**悬停放大 / 按下缩小**（见 DisableInteractionScale 注释）。
            //   这个按钮是用来「点一下然后按一个键」的，不该有尺寸变化分散注意力。
            DisableInteractionScale(btn);

            // ★ 不设 ToolTip：用户要求面板内一律不要悬停气泡标签
            //   （2026-10-20：「悬停一会后鼠标会显示一个标签…都删除，后续我确定好样式之后再用自己的」）
            return btn;
        }

        /// <summary>
        /// 取主程序现成的按钮样式。优先 <c>SettingsButtonStyleRound6</c>（低调、有边框、跟输入箱观感一致），
        /// 再退 <c>FlatButtonStyleRound6</c>、<c>BaseButtonStyle</c>、全局隐式 Button，最后 null（走系统默认）。
        /// </summary>
        private static Style? TryFindHostButtonStyle()
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app == null) return null;

                foreach (string key in new[]
                {
                    "SettingsButtonStyleRound6",
                    "FlatButtonStyleRound6",
                    "BaseButtonStyle",
                })
                {
                    if (app.TryFindResource(key) is Style st) return st;
                }
                return app.TryFindResource(typeof(Button)) as Style;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>键名按钮的文字：就是键名本身（**不加任何箭头**，加了会被当成下拉控件）。</summary>
        private static string KeyButtonContent(string keyName)
            => keyName;

        /// <summary>
        /// 键名按钮的「监听中」外观。
        /// <para>
        /// ★ 平时**完全保持宿主样式**（不覆盖任何颜色，避免和旁边的复制/插入按钮、输入框显得不一样）；
        ///   只有「正在监听」时才临时改成紫色边框 + 紫字作为提示 —— 这样用户一眼知道在等按键，
        ///   又不会觉得到处都是色块。
        /// </para>
        /// </summary>
        /// <summary>
        /// 监听按钮的「监听中」外观 —— **整个 Style 换掉**。
        /// <para>★ 为什么不能只改 <c>btn.BorderBrush</c>：宿主按钮样式里有
        ///   <c>Trigger IsMouseOver</c> 同时改 Background / BorderBrush / Foreground，
        ///   而 <b>样式触发器的优先级高于本地赋值</b> —— 一鼠标移上去，
        ///   触发器就把本地设的紫色顶掉了（症状：怎么改都看不到变化）。</para>
        /// <para>★ 这里监听态**只改边框色和文字色**，背景不动（用户要求：
        ///   不要「变大点击效果，只要边框颜色变化」）。</para>
        /// </summary>
        private static void SetKeyButtonListening(Button btn, bool listening, string waitingText = "按任意键…")
        {
            // 记住常态样式，解除监听时换回去
            var normalStyle = (Style?)btn.Tag;
            if (normalStyle == null)
            {
                normalStyle = btn.Style;
                btn.Tag = normalStyle;
            }

            if (listening)
            {
                var st = new Style(typeof(Button));
                if (normalStyle != null) st.BasedOn = normalStyle;
                // ★ 悬停时保持常态背景/文字，只有边框变紫 —— 监听按钮不该有尺寸/背景变化
                var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hover.Setters.Add(new Setter(Control.BackgroundProperty,
                    TryBrush("AppBackgroundBrush") ?? Brushes.Transparent));
                hover.Setters.Add(new Setter(Control.BorderBrushProperty,
                    TryBrush("PrimaryBrush") ?? Brushes.Purple));
                hover.Setters.Add(new Setter(Control.ForegroundProperty,
                    TryBrush("PrimaryBrush") ?? Brushes.Purple));
                st.Triggers.Add(hover);

                st.Setters.Add(new Setter(Control.BorderBrushProperty,
                    TryBrush("PrimaryBrush") ?? Brushes.Purple));
                st.Setters.Add(new Setter(Control.ForegroundProperty,
                    TryBrush("PrimaryBrush") ?? Brushes.Purple));

                // Style 不是 Freezable，不能 Freeze
                btn.Style = st;
                btn.Content = waitingText;       // ★ 提示直接写在按钮上，不再用 ToolTip
            }
            else
            {
                // ★ 解除监听 → 整个 Style 换回常态（清掉本地颜色覆盖）
                btn.Style = normalStyle;
                btn.ClearValue(Control.BorderBrushProperty);
                btn.ClearValue(Control.ForegroundProperty);
                btn.ClearValue(Control.BorderThicknessProperty);
            }
        }

        /// <summary>面板卸载时把所有监听停掉（否则全局钩子一直挂着）。</summary>
        private void StopAllWatching()
        {
            _activeKeyHandler = null;
            _modifierTimer.Stop();
            _pendingModifierVk = 0;
            KeyWatcher.KeyPressed -= OnGlobalKeyPressed;
            KeyWatcher.Stop();
            _activeMouseHandler = null;                    // ★ 鼠标钩子也要摘
            MouseWatcher.Stop();
            StopSwallowKeysNow();      // ★ 卸载时立即解除，不用等延迟
            _themeWatcher.Stop();
            StopCancelOnOutsideClick();
            _activeKeyButton = null;
            _cancelActiveListen = null;
        }

        // =====================================================================
        // 复制 / 插入按钮
        // =====================================================================

        /// <summary>给一行加「复制」「插入」两个小按钮（插在第 2、3 列）。</summary>
        private void AddActionButtons(Grid row, string op, Func<string> build)
        {
            // ★ 统一出口：按 <see cref="ChineseFormat"/> 把英文原码转成中文别名再交出去。
            //   代码块行（MyBlock()）和 Text 行翻译器认不出来，会原样返回，不受影响。
            string Build()
            {
                string raw = build();
                if (string.IsNullOrWhiteSpace(raw)) return raw;
                return MacroScriptTranslator.Translate(raw, ChineseFormat);
            }

            // 「复制」：两张叠起来的纸
            var copyBtn = MakeIconButton();
            copyBtn.Content = MakeIconGrid(copyBtn, new (string Data, double W)[] {
                ("M 2.2,4.6 h 8 v 8 h -8 z", 1.2),
                ("M 5.8,4.6 v -2.4 h 8 v 8 h -2.4", 1.2),
            });
            Grid.SetColumn(copyBtn, 2);
            copyBtn.Click += (_, _) =>
            {
                string text = Build();
                if (string.IsNullOrWhiteSpace(text)) return;
                CopyToClipboard(text);
                // ★ 复制成功后弹一条宿主提示（窗口左上角标题栏旁）
                HostBridge.ShowToast($"已复制指令“{text}”");
            };
            row.Children.Add(copyBtn);

            // 「插入」：一个向下的箭头插进一条线
            var insertBtn = MakeIconButton();
            insertBtn.Content = MakeIconGrid(insertBtn, new (string Data, double W)[] {
                ("M 8,2.4 v 8.4", 1.4),
                ("M 5.1,8.2 L 8,11.1 L 10.9,8.2", 1.4),
                ("M 3,13.4 h 10", 1.4),
            });
            Grid.SetColumn(insertBtn, 3);
            insertBtn.Click += (_, _) =>
            {
                string text = Build();
                if (string.IsNullOrWhiteSpace(text)) return;
                InsertRequested?.Invoke(this, text);
            };
            row.Children.Add(insertBtn);
        }

        /// <summary>
        /// 小图标按钮（复制 / 插入）：与 UID 的复制按钮、编辑/删除按钮**同一套观感** ——
        /// 透明无背景、24×24、悬停放大、按下回弹、悬停时图标颜色加深。
        /// <para>
        /// ★ 缩放/悬停反馈全部由宿主的 <c>IconCopyButtonStyle</c> 提供（内部走
        ///   <c>helpers:InteractionScale</c>），插件只要引用这份样式，改宿主一处两边一起生效。
        /// </para>
        /// </summary>
        private Button MakeIconButton()
        {
            var btn = new Button
            {
                Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            // ★ 尺寸、光标、背景、图标颜色都由样式给，这里**不写本地值**
            //   （本地值优先级最高，写了就把样式里的设定钉死，悬停/缩放都会失效）
            //
            // ★ 悬停**只放大、不变色**：图标描边绑的是按钮的 Foreground，
            //   样式里常态是 TextDisabledBrush，悬停保持原色（跟 UID 的复制按钮一个样）。

            Style? hostStyle = null;
            try
            {
                hostStyle = Application.Current?.TryFindResource("IconCopyButtonStyle") as Style;
            }
            catch
            {
                // 宿主版本较老、没有这份样式 → 走回退分支
            }

            if (hostStyle != null)
            {
                btn.Style = hostStyle;
                return btn;
            }

            // 回退：自己造的那份（颜色是取好的固定值，所以主题切换后要重建）
            btn.Style = BuildIconHoverStyle(TryFindHostIconButtonStyle());
            _themeRefreshers.Add(() =>
                btn.Style = BuildIconHoverStyle(TryFindHostIconButtonStyle()));

            return btn;
        }

        /// <summary>
        /// 造「复制/插入」小图标按钮的样式：常态**完全透明**（不要背景也不要边框），
        /// 悬停时才有底色（<see cref="HoverIconBackground"/>）。
        /// <para>★ 悬停色只能写在**触发器**里才有效果（本地赋值会被宿主样式的
        ///   <c>IsMouseOver</c> 触发器盖掉，见调用处注释）。</para>
        /// <para>
        /// ★ 常态背景不取主题色：这里是 Style Setter，画刷会被固定住，
        ///   跟着主题渐变走会停在某一帧的中间色，不如干脆不要背景 —— 常态透明、只有悬停有底色。
        /// </para>
        /// </summary>
        private Style BuildIconHoverStyle(Style? baseStyle)
        {
            var st = new Style(typeof(Button));
            if (baseStyle != null) st.BasedOn = baseStyle;

            st.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            st.Setters.Add(new Setter(Control.ForegroundProperty,
                TryBrush("TextSecondaryBrush") ?? Brushes.Gray));
            st.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));

            var hover = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true,
            };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, HoverIconBackground()));
            hover.Setters.Add(new Setter(Control.ForegroundProperty,
                TryBrush("TextPrimaryBrush") ?? Brushes.Black));
            st.Triggers.Add(hover);

            // Style 不是 Freezable，不能 Freeze
            return st;
        }

        /// <summary>图标按钮基于哪个宿主样式（取不到就 null，造一个干净的默认样式）。</summary>
        private static Style? TryFindHostIconButtonStyle()
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app == null) return null;

                foreach (string key in new[] { "BaseButtonStyle", "FlatButtonStyleRound6" })
                {
                    if (app.TryFindResource(key) is Style st) return st;
                }
                return app.TryFindResource(typeof(Button)) as Style;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>用矢量 Path 拼一个图标（16×16 网格），描边跟随按钮前景色。</summary>
        private static Grid MakeIconGrid(Button owner, (string Data, double W)[] paths)
        {
            var grid = new Grid { Width = 16, Height = 16 };
            foreach (var (data, w) in paths)
            {
                var path = new Path
                {
                    Data = System.Windows.Media.Geometry.Parse(data),
                    StrokeThickness = w,
                    Fill = Brushes.Transparent,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                };
                // 描边跟着按钮的 Foreground 走（换主题自动跟着变）
                path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = owner });
                grid.Children.Add(path);
            }
            return grid;
        }

        // =====================================================================
        // 键名翻译（监听模式用）
        // =====================================================================

        /// <summary>
        /// WPF 按键 → 宏脚本里的键名。返回 null = 这个键不认识（或引擎不认）。
        /// <para>★ 监听走的是全局钩子（虚拟键码），这条路是给**代码调用方**留的兼容入口；
        ///   面板内部一律用 <see cref="MacroKeyNames"/>。</para>
        /// </summary>
        public static string? TranslateKey(Key key)
        {
            string? name = MapKeyName(key);
            if (name == null) return null;

            // 宏引擎必须认识这个键名，否则不给
            return SimulationService.TryGetKeyCode(name, out _) ? name : null;
        }

        private static string? MapKeyName(Key key)
        {
            // 字母
            if (key >= Key.A && key <= Key.Z) return key.ToString();

            // 主键盘数字
            if (key >= Key.D0 && key <= Key.D9) return ((int)key - (int)Key.D0).ToString();

            // 小键盘数字
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + ((int)key - (int)Key.NumPad0);

            // 功能键
            if (key >= Key.F1 && key <= Key.F24) return "F" + ((int)key - (int)Key.F1 + 1);

            switch (key)
            {
                case Key.Space: return "Space";
                case Key.Enter: return "Enter";
                case Key.Escape: return "Esc";
                case Key.Tab: return "Tab";
                case Key.Back: return "Back";
                case Key.Delete: return "Delete";
                case Key.Insert: return "Insert";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PageUp";
                case Key.PageDown: return "PageDown";
                case Key.Up: return "Up";
                case Key.Down: return "Down";
                case Key.Left: return "Left";
                case Key.Right: return "Right";
                case Key.CapsLock: return "CapsLock";
                case Key.NumLock: return "NumLock";
                case Key.PrintScreen: return "PrintScreen";
                case Key.Pause: return "Pause";

                case Key.LeftShift: return "LShift";
                case Key.RightShift: return "RShift";
                case Key.LeftCtrl: return "LCtrl";
                case Key.RightCtrl: return "RCtrl";
                case Key.LeftAlt: return "LAlt";
                case Key.RightAlt: return "RAlt";
                case Key.LWin: return "LWin";
                case Key.RWin: return "RWin";

                case Key.OemMinus: return "Minus";
                case Key.OemPlus: return "Equal";
                case Key.OemComma: return "Comma";
                case Key.OemPeriod: return "Period";
                case Key.OemQuestion: return "Slash";
                case Key.OemSemicolon: return "Semicolon";
                case Key.OemQuotes: return "Quote";
                case Key.OemOpenBrackets: return "LBracket";
                case Key.OemCloseBrackets: return "RBracket";
                case Key.OemPipe: return "Backslash";
                case Key.OemTilde: return "Backtick";
            }

            return null;
        }

        // =====================================================================
        // 资源
        // =====================================================================

        /// <summary>
        /// **动态资源**绑定（等价于 XAML 里的 <c>{DynamicResource 名字}</c>）。
        /// <para>★ 关键：不能 <c>TryFindResource</c> 拿到 Brush 直接赋值 —— 那样颜色被固定成
        /// 当前主题的值，主题切换后资源字典整个换掉，控件还拿着旧画刷（深色下黑字黑底）。</para>
        /// </summary>
        private static void SetRes(FrameworkElement target, DependencyProperty prop, string key)
        {
            try
            {
                target.SetResourceReference(prop, key);
            }
            catch
            {
                // 资源不存在：保留默认样式，不影响功能
            }
        }

        /// <summary>取主题画刷（取不到返回 null，调用方自己兜底）。</summary>
        /// <para>
        /// ★ 优先走宿主 <c>ThemeService.GetThemeBrush</c>（反射）：拿的是主题字典里的<b>终值</b>，
        ///   而且是新实例。切换主题时宿主会让颜色渐变 300ms，这段时间里
        ///   <c>TryFindResource</c> 给的是那只正在变的临时画刷 —— 把它塞进 Style 后颜色会被
        ///   固定成某一帧的中间值，表现就是「切完主题，按钮背景还是另一个主题的」。
        /// </para>
        private static Brush? TryBrush(string key)
        {
            try
            {
                var type = Type.GetType("LvchaxsBS.Services.ThemeService, LvchaxsBS");
                var method = type?.GetMethod("GetThemeBrush",
                    BindingFlags.Public | BindingFlags.Static);

                if (method?.Invoke(null, new object[] { key }) is Brush brush)
                    return brush;
            }
            catch
            {
                // 宿主版本较老、没有这个入口 → 退回原来的取法
            }

            return Application.Current?.TryFindResource(key) as Brush;
        }

        // =====================================================================
        // 主题判断（反射宿主 ThemeService，插件不引用宿主 dll）
        // =====================================================================

        /// <summary>当前是不是深色主题。读不到一律按浅色处理（浅色值更接近中性，不至于突兀）。</summary>
        private static bool IsDarkTheme()
        {
            try
            {
                var type = Type.GetType("LvchaxsBS.Services.ThemeService, LvchaxsBS");
                var cur = type?.GetProperty("Current",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                return cur?.ToString() == "Dark";
            }
            catch
            {
                return false;
            }
        }

        // =====================================================================
        // 宿主 InteractionScale（悬停放大 / 按下缩小）
        // =====================================================================

        /// <summary>
        /// 关掉某个元素上的**悬停放大 / 按下缩小**效果。
        /// <para>★ 宿主 <c>BaseButtonStyle</c> 里有附加属性
        ///   <c>helpers:InteractionScale.HoverScale=1.06 / PressScale=0.97</c>，
        ///   监听按钮是「填一个值」用的控件，变大/缩小反而干扰视线
        ///   （用户：「不需要按钮选题变大点击效果」）—— 把它设成 0 就是「不缩放」。</para>
        /// <para>★ 用**反射**找宿主的附加属性：宏插件是独立 dll，编译期不引用 LvchaxsBS。
        ///   拿不到（宿主换了命名空间）就跳过，只是保留放大效果，功能不受影响。</para>
        /// </summary>
        private static void DisableInteractionScale(FrameworkElement element)
        {
            try
            {
                var type = Type.GetType("LvchaxsBS.UI.Helpers.InteractionScale, LvchaxsBS");
                if (type == null) return;

                foreach (string prop in new[] { "HoverScale", "PressScale" })
                {
                    var dp = type.GetField(prop + "Property",
                        BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                        as DependencyProperty;
                    if (dp != null) element.SetValue(dp, 0.0);
                }
            }
            catch
            {
                // 宿主没有这个辅助类：保留默认观感，不影响功能
            }
        }

        /// <summary>复制/插入小图标按钮的悬停背景色。</summary>
        private static Brush HoverIconBackground() =>
            new SolidColorBrush(IsDarkTheme()
                ? Color.FromRgb(53, 53, 53)     // 深色主题
                : Color.FromRgb(240, 240, 240)); // 浅色主题
    }
}
