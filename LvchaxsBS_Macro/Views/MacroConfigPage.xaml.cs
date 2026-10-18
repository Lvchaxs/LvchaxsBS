using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Views
{
    /// <summary>
    /// 「自定义宏」的配置页：一叠 70 高的宏卡片。
    /// <para>
    /// ★ 底部那一排按钮（添加宏 / 触发键 / 配置 / 返回 / 开关）由**宿主**配置容器页提供，
    ///   本页只有列表本体 —— 所以看起来和普通功能的配置页完全一样。
    /// </para>
    /// <para>
    /// ★ 拖动排序是**主页那张卡片 / UID 账号列表同一套手感**：
    ///   按住手柄立刻抓起 → 卡片跟手 → 邻卡让位 → 松手 150ms 吸附到目标槽 → 才落数据落盘。
    ///   ⛔ 三条铁律（照抄 HomePage，改错任何一条都会"卡片瞬间飞到顶/底"）：
    ///   ① 拖动全程**一行数据都不动**，只改容器的 TranslateTransform；
    ///   ② 事件挂 Window（Preview 隧道），不挂手柄自己的 LostMouseCapture；
    ///   ③ 卡片位置每帧按实际布局去量再补差，不靠算。
    /// </para>
    /// </summary>
    public partial class MacroConfigPage : Page
    {
        /// <summary>当前打开的配置页（供「添加宏」按钮保存后刷新它）。</summary>
        private static MacroConfigPage? _current;

        /// <summary>
        /// 当前实例（供指令编辑页「返回列表」导航回**同一个**实例 —— 列表状态不丢）。
        /// </summary>
        public static MacroConfigPage? CurrentInstance => _current;

        /// <summary>「添加宏」按钮保存完调这个：页面开着就刷新列表。</summary>
        public static void RefreshIfActive()
        {
            var page = _current;
            if (page == null) return;

            if (page.Dispatcher.CheckAccess()) page.Reload();
            else page.Dispatcher.Invoke(page.Reload);
        }

        /// <summary>
        /// 当前列表（<see cref="ObservableCollection{T}"/>：置顶/拖动**就地 Move**，容器不重建
        /// ⇒ FLIP 位置准确、手感和 UID 一致）。
        /// </summary>
        private ObservableCollection<MacroItem> _items = new();

        public MacroConfigPage()
        {
            InitializeComponent();

            Loaded += (_, _) => { _current = this; Reload(); };

            // 拖动途中被导航走 → 必须收尾，否则 Window 上的订阅一直挂着
            Unloaded += (_, _) =>
            {
                // ⛔ 绝不能在这里清 _current！导航进指令编辑页时本页会 Unload，
                //    清了 ⇒ 编辑页「返回列表」拿到 null ⇒ Navigate(null) ⇒ 列表一片空白。
                //    （_current 会在下次 Loaded 时被新/旧实例覆盖，不会泄漏）
                if (_sortDragging) CommitSortDrag();
            };
        }

        // ============ 刷新 ============

        private void Reload()
        {
            var source = MacroSettingsStore.Current.Macros;

            // ★ 星 / 灰区的显示状态跟着真实置顶状态走（重建后不会残留上一次的显示/隐藏）
            foreach (var item in source) item.ShowStar = item.IsPinned;

            // 换一个集合实例：ItemsControl 重新生成卡片
            _items = new ObservableCollection<MacroItem>(source);
            MacroList.ItemsSource = _items;

            EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ============ 卡片：置顶 / 编辑 / 删除 ============

        /// <summary>
        /// 点图标 = 置顶 / 取消置顶。
        /// <para>
        /// ★★ 和 UID 一模一样：**先播「星 + 灰区」的滑入/滑出，动画播完才改数据并重排**
        /// （直接改数据会立刻重建容器 → 星是「突然出现 / 突然消失」）。
        /// </para>
        /// </summary>
        private void MacroIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MacroItem item) return;

            e.Handled = true;

            bool showing = !item.IsPinned;   // 这次要变成「置顶」还是「取消置顶」

            ApplyStarToggle(showing, () =>
            {
                MacroSettingsStore.TogglePin(item.Id);
                ReorderInPlace(item);        // ★ 就地 Move + FLIP：卡片平滑滑到新位置，不瞬间跳
            }, item);
        }

        // ============ 平滑重排（FLIP，照 UID 的 smoothReorder） ============

        /// <summary>FLIP 平滑重排进行中（照 UID）：期间忽略新的置顶点击。</summary>
        private bool _reordering;

        /// <summary>
        /// 置顶/取消置顶之后：**就地 Move**（不重建容器）+ FLIP，卡片平滑滑到新位置。
        /// <para>
        /// ★ 为什么要就地 Move 而不是重设 ItemsSource：重建容器会丢掉"旧位置"，
        ///   就只能看到卡片瞬间跳过去（这正是"瞬间移动"的根因）。
        /// </para>
        /// </summary>
        private void ReorderInPlace(MacroItem item)
        {
            var before = CaptureCardTops();

            // ★ 和数据层**完全同一个规则**：稳定重排（置顶在前、各自保持原有相对顺序），
            //   再逐个 Move 到目标下标 —— 容器不重建，FLIP 才有准确的"旧位置"。
            //   ⛔ 不能"置顶就插到 index 0"：那样原来在下边的那条会和上边那条互换（UID 不会）。
            var ordered = _items.OrderByDescending(x => x.IsPinned).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                int current = _items.IndexOf(ordered[i]);
                if (current >= 0 && current != i) _items.Move(current, i);
            }

            MacroList.UpdateLayout();    // 催布局（只是重新排列，不重建容器 → 很快）
            RunFlip(before);
        }

        /// <summary>记录每张卡片的**旧**位置（按宏 Id 索引，容器不在可视区就跳过）。</summary>
        private Dictionary<string, (FrameworkElement card, double top)> CaptureCardTops()
        {
            var map = new Dictionary<string, (FrameworkElement, double)>();

            foreach (var item in _items)
            {
                if (MacroList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement card) continue;

                double top = card.TransformToAncestor(MacroList).Transform(new Point(0, 0)).Y;
                map[item.Id] = (card, top);
            }

            return map;
        }

        /// <summary>FLIP 主体：把每张卡从旧位置 300ms 缓出滑到当前位置（同 UID）。</summary>
        private void RunFlip(Dictionary<string, (FrameworkElement card, double top)> before)
        {
            if (before.Count == 0) return;

            var after = CaptureCardTops();
            if (after.Count == 0) return;

            var cards = new List<(TranslateTransform tr, FrameworkElement card)>();
            foreach (var kv in after)
            {
                if (!before.TryGetValue(kv.Key, out var oldVal)) continue;

                double delta = oldVal.top - kv.Value.top;      // FLIP 的 Invert：先摆回旧位置
                if (Math.Abs(delta) < 0.5) continue;

                var tr = kv.Value.card.RenderTransform as TranslateTransform;
                if (tr == null || tr.IsFrozen)
                {
                    tr = new TranslateTransform();
                    kv.Value.card.RenderTransform = tr;
                }

                tr.BeginAnimation(TranslateTransform.YProperty, null);
                tr.Y = delta;
                cards.Add((tr, kv.Value.card));
            }

            if (cards.Count == 0) return;

            _reordering = true;

            int left = cards.Count;
            Action done = () =>
            {
                if (--left > 0) return;
                _reordering = false;

                foreach (var (tr, card) in cards)
                {
                    tr.BeginAnimation(TranslateTransform.YProperty, null);
                    tr.Y = 0;
                }
            };

            foreach (var (tr, card) in cards)
            {
                var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                anim.Completed += (_, __) => done();
                tr.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        private void EditMacro_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id) return;

            var item = MacroSettingsStore.Current.Macros.FirstOrDefault(m => m.Id == id);
            if (item == null) return;

            var window = new MacroEditWindow(item) { Owner = Window.GetWindow(this) };
            // ★ 带宿主的全局遮罩（没注入时就是普通的 ShowDialog）
            if (Utils.HostBridge.ShowModal(window) == true) Reload();
        }

        private void DeleteMacro_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id) return;

            var item = MacroSettingsStore.Current.Macros.FirstOrDefault(m => m.Id == id);
            if (item == null) return;

            // ★ 删除是不可撤销的 → 必须先确认（弹窗由宿主提供，与主程序其它确认框一致）
            bool ok = Utils.HostBridge.Confirm(
                "删除宏", $"确定要删除宏「{item.Name}」吗？此操作无法撤销。", "删除", "取消");
            if (!ok) return;

            MacroSettingsStore.Remove(id);
            Reload();
        }

        /// <summary>
        /// 「编辑指令」按钮 —— 导航到指令编辑页（<see cref="MacroScriptEditorPage"/>）。
        /// <para>
        /// ★ 不是弹窗：宿主配置容器的内容 Frame 直接切到编辑页，
        /// 编辑页里的「返回列表」再导航回**本页同一个实例**（<see cref="CurrentInstance"/>）。
        /// </para>
        /// </summary>
        private void EditMacroCommands_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id) return;

            var item = MacroSettingsStore.Current.Macros.FirstOrDefault(m => m.Id == id);
            if (item == null) return;

            NavigationService?.Navigate(new MacroScriptEditorPage(item));
        }

        // ============ 置顶 / 取消置顶：「星 + 灰区」的滑动动画（照 UID 分组列表逐条搬过来） ============

        /// <summary>「星 + 灰区」停在卡片外时的位移（比这块区域宽一点，配合卡片 ClipToBounds 就整套看不见）。</summary>
        private const double StarOffX = 260;

        private static readonly string[] StarElementNames = { "PinnedArcLayer", "PinnedStar" };

        /// <summary>上一次滑出动画还没落地时忽略再次点击，避免连点把置顶切错。</summary>
        private bool _starToggleAnimating;

        /// <summary>
        /// 置顶/取消置顶的「表演 + 落地」（只作用于用户点的那一张卡）。
        /// <para>
        /// ★★ 顺序：**动画先播完，卡片再挪位置**（生效与取消都一样）。
        ///   · 生效（showing=true）：先「预告」—— 把星/灰区摆到卡片外，再翻 <see cref="MacroItem.ShowStar"/>
        ///     让它显出来（此刻在卡片外，看不出变化、也不会先在原位闪一下）；接着播滑入；
        ///     **滑完才 commit**（这时才真正改置顶数据并重排到最上）。
        ///   · 取消（showing=false）：先播滑出（此时数据还没变、元素还看得见），滑完再 commit ——
        ///     全程不动 Visibility，所以不会「瞬失」。
        /// ★ 为什么不用 XAML 触发器：列表刷新会重建行容器，取消时新容器一建出来就是不显示，
        ///   触发器的 ExitActions 没有「true→false」这个过渡可依赖，滑出动画根本没机会播。
        /// </summary>
        private void ApplyStarToggle(bool showing, Action commit, MacroItem item)
        {
            if (_starToggleAnimating || _reordering) return;      // 重排/滑出动画期间忽略新点击

            // ★ 先把布局催出来：上一次 commit 刚重排过时容器可能还没生成，
            //   ContainerFromItem 会返回 null → 动画静默不播（看着就是"没动画"）。
            try { MacroList.UpdateLayout(); } catch { /* 布局失败不影响落地 */ }

            bool committed = false;
            Action commitOnce = () =>
            {
                if (committed) return;             // 幂等：兜底与 Completed 都调它也只会落地一次
                committed = true;
                _starToggleAnimating = false;
                commit?.Invoke();
            };

            try
            {
                var container = MacroList.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;

                if (showing)
                {
                    PreviewStarVisible(container, item);      // 预告：元素先出现在卡片外（还看不见）

                    if (TryPlayStarSlide(container, StarOffX, 0, 220, EasingMode.EaseOut, commitOnce))
                    {
                        _starToggleAnimating = true;          // 落地被延后到动画结束
                        _ = CommitAfterDelayAsync(commitOnce); // 兜底：Completed 没触发也要落地
                    }
                    else
                    {
                        commitOnce();                          // 没容器/播不了，直接落地
                    }
                }
                else if (TryPlayStarSlide(container, 0, StarOffX, 180, EasingMode.EaseIn, commitOnce))
                {
                    _starToggleAnimating = true;
                    _ = CommitAfterDelayAsync(commitOnce);
                }
                else
                {
                    commitOnce();
                }
            }
            catch
            {
                commitOnce();       // 动画怎么出问题都不能把数据落地拖着
            }
        }

        /// <summary>兜底：等一会儿再尝试落地（幂等，已落地就什么都不做）。</summary>
        private static async Task CommitAfterDelayAsync(Action commitOnce)
        {
            await Task.Delay(500);
            try { commitOnce(); } catch { }
        }

        /// <summary>
        /// 「星要出现」的视觉预告 —— 让滑入动画能在**数据切换之前**先播完。
        /// 步骤：① 把卡片里的「星 + 灰区」摆到卡片外（此刻它还是折叠的，看不出变化）；
        ///       ② 再翻一下 ShowStar 让它显出来 —— 它一出现就在卡片外，不会先在原位闪一下。
        /// ★ 只翻 ShowStar 这个**纯界面标志**（不落盘、不参与排序）→ 列表顺序不变，
        ///   真正的置顶数据由随后的 commit 在动画播完后才改。
        /// </summary>
        private void PreviewStarVisible(DependencyObject? container, MacroItem item)
        {
            try
            {
                if (container == null) return;

                foreach (var name in StarElementNames)
                {
                    var element = FindVisualChild<FrameworkElement>(container, name);
                    var translate = EnsureAnimatableTransform(element);
                    if (translate == null) continue;

                    translate.BeginAnimation(TranslateTransform.XProperty, null);   // 清掉上一轮留下的终值
                    translate.X = StarOffX;
                }

                // 位移已摆好，再让它出现（顺序不能反：先显出来就会在原位闪一帧）
                item.ShowStar = true;
            }
            catch
            {
                // 预告失败不影响落地
            }
        }

        /// <summary>对某张卡片播「星 + 灰区」位移；返回是否真的播了（没播就让调用方立刻落地数据）。</summary>
        private bool TryPlayStarSlide(DependencyObject? container, double fromX, double toX,
                                      int milliseconds, EasingMode easing, Action onCompleted)
        {
            try
            {
                if (container == null) return false;

                bool handled = false;
                foreach (var name in StarElementNames)
                {
                    var element = FindVisualChild<FrameworkElement>(container, name);
                    // 完成回调只挂给第一个真正播起来的元素，避免被调用多次
                    if (AnimateStarX(element, fromX, toX, milliseconds, easing, handled ? null : onCompleted))
                        handled = true;
                }

                return handled;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取出元素上「可以播动画」的平移变换。
        /// ★ 坑：XAML 里写的 &lt;TranslateTransform/&gt;（不管在 Style / ControlTemplate / DataTemplate 里）
        ///   会在模板密封时被 **Freeze**，冻结后既不能改 X、也不能 BeginAnimation，
        ///   异常又被 try/catch 吞掉 → 表面上就是「没动画」。
        ///   所以这里一律检查 IsFrozen：冻结的就换一个逐实例、未冻结的新实例挂上去。
        /// </summary>
        private static TranslateTransform? EnsureAnimatableTransform(FrameworkElement? element)
        {
            if (element == null) return null;

            if (element.RenderTransform is TranslateTransform existing && !existing.IsFrozen)
                return existing;

            var fresh = new TranslateTransform();
            if (element.RenderTransform is TranslateTransform frozen) fresh.X = frozen.X;

            element.RenderTransform = fresh;
            return fresh;
        }

        private static bool AnimateStarX(FrameworkElement? element, double fromX, double toX,
                                         int milliseconds, EasingMode easing, Action? onCompleted)
        {
            var translate = EnsureAnimatableTransform(element);
            if (translate == null) return false;

            try
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);   // 清掉上一次的终值

                var animation = new DoubleAnimation(fromX, toX, TimeSpan.FromMilliseconds(milliseconds))
                {
                    EasingFunction = new CubicEase { EasingMode = easing }
                };
                if (onCompleted != null) animation.Completed += (_, __) => onCompleted();

                translate.BeginAnimation(TranslateTransform.XProperty, animation);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>在容器（ContentPresenter）的视觉树里按 x:Name 找元素。</summary>
        private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name) return element;

                var found = FindVisualChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // ============ 拖动排序（与首页卡片 / UID 账号列表同一套） ============

        private ScrollViewer? _sortScroller;
        private bool _sortDragging;
        private bool _sortDropping;
        private int _sortDragToken;

        /// <summary>吸附动画时长（与 UID 账号列表 / 首页卡片一致：150ms）。</summary>
        private const int SortSettleMs = 150;

        /// <summary>吸附兜底：动画 Completed 万一没来，这么久之后也要收尾落数据。</summary>
        private const int SortSettleFallbackMs = 400;

        private MacroItem? _sortItem;
        private int _sortFrom;
        private int _sortTarget;

        /// <summary>允许落下的目标下标范围 —— 置顶区与非置顶区互相不串（同 UID 分区规则）。</summary>
        private int _sortMin;
        private int _sortMax;

        private double _sortStartMouseY;
        private double _sortGrabOffset;
        private double _sortPitch;
        private double _sortSlotTop;
        private double _sortSlotBottom;

        private readonly List<FrameworkElement> _sortCards = new();
        private readonly List<TranslateTransform> _sortTransforms = new();
        private readonly Dictionary<TranslateTransform, double> _sortShiftTargets = new();

        private Window? _sortWindow;
        private DateTime _sortLastMove = DateTime.MinValue;
        private FrameworkElement? _sortCapture;
        private Cursor? _sortPrevCursor;

        private void SortHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_sortDragging || _sortDropping) return;      // 吸附动画还没走完，别抢
            if (sender is not FrameworkElement grab) return;
            if (grab.DataContext is not MacroItem item) return;

            // ★ 拦在 Button 处理之前：否则 Button 会自己抢鼠标捕获 / 变成按下态
            e.Handled = true;

            var cards = CollectCardContainers();
            int from = cards.FindIndex(c => ReferenceEquals(c.DataContext, item));
            if (from < 0) return;

            _sortItem = item;
            _sortFrom = from;
            _sortTarget = from;

            // 分区：置顶的只能在置顶区里拖，非置顶的只能在非置顶区里拖（和 UID 一样不串位）
            int pinnedCount = _items.Count(x => x.IsPinned);
            if (item.IsPinned)
            {
                _sortMin = 0;
                _sortMax = Math.Max(0, pinnedCount - 1);
            }
            else
            {
                _sortMin = pinnedCount;
                _sortMax = cards.Count - 1;
            }

            _sortScroller = FindAncestor<ScrollViewer>(MacroList);
            _sortCards.Clear();
            _sortTransforms.Clear();
            _sortShiftTargets.Clear();

            foreach (var card in cards)
            {
                Transform tf;
                if (card.RenderTransform is TranslateTransform exist && !exist.IsFrozen)
                {
                    exist.BeginAnimation(TranslateTransform.YProperty, null);
                    exist.BeginAnimation(TranslateTransform.XProperty, null);
                    exist.X = 0;
                    exist.Y = 0;
                    tf = exist;
                }
                else
                {
                    tf = new TranslateTransform();
                    card.RenderTransform = tf;
                }

                _sortCards.Add(card);
                _sortTransforms.Add((TranslateTransform)tf);
            }

            // 实测行距 / 槽位（不写死"一张卡 78px"这种假设）
            var tops = new List<double>(_sortCards.Count);
            foreach (var card in _sortCards)
                tops.Add(card.TransformToAncestor(MacroList).Transform(new Point(0, 0)).Y);

            _sortPitch = tops.Count >= 2 ? tops[1] - tops[0] : 0;
            if (_sortPitch <= 0) _sortPitch = _sortCards[0].ActualHeight;
            if (_sortPitch <= 0) return;

            _sortSlotTop = tops[0];
            _sortSlotBottom = tops[tops.Count - 1];

            double mouseY = e.GetPosition(MacroList).Y;
            _sortStartMouseY = mouseY;
            _sortGrabOffset = mouseY - tops[from];

            _sortDragging = true;
            _sortDropping = false;
            _sortDragToken++;
            Panel.SetZIndex(_sortCards[from], 1);
            _sortCards[from].Opacity = 0.85;

            _sortPrevCursor = MacroList.Cursor;
            MacroList.Cursor = Cursors.SizeAll;

            grab.CaptureMouse();
            _sortCapture = grab;
            _sortLastMove = DateTime.Now;
            SubscribeDragEvents();
        }

        private void OnSortWindowMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_sortDragging && !_sortDropping) MoveDraggedCard(e);
        }

        private void MoveDraggedCard(System.Windows.Input.MouseEventArgs e)
        {
            if (!_sortDragging || _sortDropping || _sortItem == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { FinishSortDrag(); return; }

            MaybeAutoScroll(e);

            double mouseY = e.GetPosition(MacroList).Y;
            double dy = mouseY - _sortStartMouseY;

            // ① 目标序号：起手位移 ÷ 行距，再钳到**本分区**（单调，不会跳）
            _sortTarget = Math.Clamp(
                _sortFrom + (int)Math.Round(dy / _sortPitch), _sortMin, _sortMax);

            // ② 邻卡让位
            ShiftNeighbors(_sortTarget);

            // ③ 被拖卡片跟光标
            PinDraggedCard(mouseY - _sortGrabOffset);
        }

        private void OnSortWindowMouseUp(object sender, MouseButtonEventArgs e) => FinishSortDrag();

        private void OnSortWindowDeactivated(object? sender, EventArgs e) => FinishSortDrag();

        /// <summary>邻卡让位：夹在「起手位置」和「目标位置」之间的卡片整体挪一格。</summary>
        private void ShiftNeighbors(int target)
        {
            for (int i = 0; i < _sortCards.Count; i++)
            {
                if (i == _sortFrom) continue;

                double want = 0;
                if (target > _sortFrom && i > _sortFrom && i <= target) want = -_sortPitch;
                else if (target < _sortFrom && i >= target && i < _sortFrom) want = _sortPitch;

                var tf = _sortTransforms[i];
                if (_sortShiftTargets.TryGetValue(tf, out double cur) && Math.Abs(cur - want) < 0.5) continue;
                _sortShiftTargets[tf] = want;

                var anim = new DoubleAnimation
                {
                    To = want,                       // ★ 不写 From：从当前值续着走
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                tf.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        /// <summary>把被拖卡片的顶部钉到 desiredTop（扣掉抓取偏移 = 卡片跟着手指走）。</summary>
        private void PinDraggedCard(double desiredTop)
        {
            // ★ 视觉上也限制在本分区内：拖非置顶的卡不会跑到置顶区上面去
            double minTop = _sortSlotTop + _sortMin * _sortPitch;
            double maxTop = _sortSlotTop + _sortMax * _sortPitch;
            desiredTop = Math.Clamp(desiredTop, minTop, maxTop);

            var card = _sortCards[_sortFrom];
            var tf = _sortTransforms[_sortFrom];

            double currentTop = card.TransformToAncestor(MacroList).Transform(new Point(0, 0)).Y;

            tf.BeginAnimation(TranslateTransform.YProperty, null);   // 动画会让直接赋值失效
            tf.Y += desiredTop - currentTop;
        }

        /// <summary>鼠标贴近列表上下边缘时自动滚动。</summary>
        private void MaybeAutoScroll(System.Windows.Input.MouseEventArgs e)
        {
            var sc = _sortScroller;
            if (sc == null || sc.ScrollableHeight <= 0) return;

            var now = DateTime.Now;
            double dtMs = (now - _sortLastMove).TotalMilliseconds;
            _sortLastMove = now;
            if (dtMs <= 0 || dtMs > 200) return;    // 鼠标停住就不滚

            const double edge = 48;
            double vy = e.GetPosition(sc).Y;
            double speed = 0;

            if (vy < edge && sc.VerticalOffset > 0) speed = -(edge - vy) * 8;
            else if (vy > sc.ActualHeight - edge && sc.VerticalOffset < sc.ScrollableHeight)
                speed = (vy - (sc.ActualHeight - edge)) * 8;

            if (speed == 0) return;

            speed = Math.Clamp(speed, -600, 600);
            sc.ScrollToVerticalOffset(sc.VerticalOffset + speed * dtMs / 1000.0);
        }

        /// <summary>松手：被拖卡用 150ms 滑到目标槽，滑完才落数据（同 UID 手感）。</summary>
        private void FinishSortDrag()
        {
            if (!_sortDragging || _sortDropping) return;
            _sortDropping = true;

            if (_sortFrom < 0 || _sortFrom >= _sortTransforms.Count || _sortTarget == _sortFrom)
            {
                CommitSortDrag();
                return;
            }

            double finalY = (_sortTarget - _sortFrom) * _sortPitch;
            var anim = new DoubleAnimation(finalY, TimeSpan.FromMilliseconds(SortSettleMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (s, e) => CommitSortDrag();
            _sortTransforms[_sortFrom].BeginAnimation(TranslateTransform.YProperty, anim);

            // 兜底：Completed 万一没来（动画被顶掉 / 元素被卸载）也要收尾
            int token = _sortDragToken;
            _ = Task.Run(async () =>
            {
                await Task.Delay(SortSettleFallbackMs);
                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (token == _sortDragToken) CommitSortDrag();
                    });
                }
                catch { }
            });
        }

        /// <summary>收尾：清位移 → 改一次数据 → 落盘 → 刷新列表。</summary>
        private void CommitSortDrag()
        {
            if (!_sortDragging) return;
            _sortDragging = false;
            _sortDropping = false;
            UnsubscribeDragEvents();

            if (_sortCapture != null)
            {
                if (_sortCapture.IsMouseCaptured) _sortCapture.ReleaseMouseCapture();
                _sortCapture = null;
            }

            int from = _sortFrom;
            int to = _sortTarget;

            MacroList.Cursor = _sortPrevCursor;
            _sortPrevCursor = null;

            for (int i = 0; i < _sortCards.Count; i++)
            {
                var tf = _sortTransforms[i];
                tf.BeginAnimation(TranslateTransform.YProperty, null);
                tf.X = 0;
                tf.Y = 0;
                Panel.SetZIndex(_sortCards[i], 0);
                _sortCards[i].Opacity = 1.0;
            }

            _sortCards.Clear();
            _sortTransforms.Clear();
            _sortShiftTargets.Clear();
            _sortItem = null;

            if (from < 0 || from >= _items.Count) return;
            if (to == from) return;

            to = Math.Clamp(to, 0, _items.Count - 1);

            var moved = _items[from];
            _items.Move(from, to);                 // ★ 就地 Move：容器不重建
            MacroSettingsStore.Move(moved.Id, to);

            // ★ 这里**不重建列表**：松手时卡片已经停在目标槽上（邻卡让位 + 位移已复位），
            //   数据 Move 之后布局正好对应，所以看不到任何跳动。重建反而会闪一下。
        }

        private void SubscribeDragEvents()
        {
            _sortWindow = Window.GetWindow(this);
            if (_sortWindow == null) return;

            _sortWindow.PreviewMouseMove += OnSortWindowMouseMove;
            _sortWindow.PreviewMouseLeftButtonUp += OnSortWindowMouseUp;
            _sortWindow.Deactivated += OnSortWindowDeactivated;
        }

        private void UnsubscribeDragEvents()
        {
            if (_sortWindow == null) return;

            _sortWindow.PreviewMouseMove -= OnSortWindowMouseMove;
            _sortWindow.PreviewMouseLeftButtonUp -= OnSortWindowMouseUp;
            _sortWindow.Deactivated -= OnSortWindowDeactivated;
            _sortWindow = null;
        }

        /// <summary>按当前 UI 顺序收集卡片容器（必须走 ItemContainerGenerator）。</summary>
        private List<ContentPresenter> CollectCardContainers()
        {
            var list = new List<ContentPresenter>();

            var generator = MacroList.ItemContainerGenerator;
            for (int i = 0; i < MacroList.Items.Count; i++)
                if (generator.ContainerFromIndex(i) is ContentPresenter cp)
                    list.Add(cp);

            return list;
        }

        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
