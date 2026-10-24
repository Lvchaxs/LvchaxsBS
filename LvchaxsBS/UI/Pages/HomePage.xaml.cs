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
using System.Windows.Threading;
using LvchaxsBS.Config;
using LvchaxsBS.Core;
using LvchaxsBS.Services;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages
{
    public partial class HomePage : Page
    {
        // 防止"加载时"触发 Toggle_Changed
        // （容器生成/绑定初始化会经过布局阶段，故延后到 ContextIdle 再放开）
        private bool _isLoading = false;

        /// <summary>
        /// 主程序「功能总开关」是否开启 —— **卡片上胶囊开关的灰度绑它**。
        /// <para>
        /// ★★ 必须走绑定，不能像原来那样"遍历卡片把 CheckBox 的 Opacity 改成 0.4"：
        ///   拖动排序结束是 <c>ObservableCollection.Move</c>，WPF 会把那一行的容器移除重建，
        ///   重建出来的 CheckBox 默认 Opacity=1 ⇒ 遍历设过的灰度全丢
        ///   （用户看到「总开关是关的，排一下序就变亮了」就是这个）。
        /// </para>
        /// <para>★ 用页面级 DP 而不是给每张卡片加属性：改一个值，所有卡片（含之后重建的）自动正确。</para>
        /// </summary>
        public static readonly DependencyProperty MasterSwitchOnProperty =
            DependencyProperty.Register(nameof(MasterSwitchOn), typeof(bool), typeof(HomePage),
                new PropertyMetadata(true));

        /// <summary>「功能总开关」是否开启（见 <see cref="MasterSwitchOnProperty"/>）。</summary>
        public bool MasterSwitchOn
        {
            get => (bool)GetValue(MasterSwitchOnProperty);
            set => SetValue(MasterSwitchOnProperty, value);
        }

        public HomePage()
        {
            InitializeComponent();
            Loaded += HomePage_Loaded;

            // ★ 卡片数据**在这里就建好**（原来放在 HomePage_Loaded 里）：
            //   Loaded 之后才跑第一次布局、才生成容器 —— 数据在 Loaded 里才给的话，
            //   第一次布局面对的是空列表，启动首屏就是「先空白、卡片随后补上」（2026-10-16）。
            //   放构造函数里，容器一生成就带数据，首帧即有内容。
            var settings = ConfigManager.Get<HomePageSettings>();
            // ★★ ShowOnHome=false 的模块（如自定义宏）**不在首页生成卡片** ——
            //   它的入口只保留顶部「自定义宏」按钮，进配置页就是宏列表。
            //   ⚠️ 但模块本身仍在 ModuleRegistry.All 里：触发键占用检测、按 KeyTag 查找都还要用它。
            _modules = new ObservableCollection<ModuleCardItem>(
                ModuleRegistry.Ordered(settings)
                    .Where(m => m.ShowOnHome)
                    .Select(m => new ModuleCardItem(m, settings)));
            ModuleList.ItemsSource = _modules;

            // 拖动途中被导航走 → 必须收尾，否则 Window 上的订阅一直挂着
            // 页面被卸载 → 立刻收尾（此时**不能**走吸附动画：元素都要没了，Completed 不一定回来）。
            Unloaded += (_, _) => { if (_sortDragging) CommitSortDrag(); };
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;

            // 卡片数据已在构造函数里建好（首帧就要有内容），这里只取一份设置用
            var s = ConfigManager.Get<HomePageSettings>();

            // 手柄拾取是常驻监听（不走触发键通道），需要在进入主页时同步启停
            ControllerPickupLogic.SyncWithSettings();

            // 等容器生成 + 绑定初始化全部跑完，再应用总开关视觉并放开事件
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyMasterSwitchVisual(s.MasterSwitch);
                _isLoading = false;
                ScheduleModuleSpacingRefresh();
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>
        /// 首页功能卡「最后一张贴视口底」：卡片间距已从卡片自身 Margin 挪到
        /// <c>ItemContainerStyle</c>（ContentPresenter）上，这里把**最后一项容器**的下边距刷成 0
        /// ⇒ 滚到底时最后一张卡底边正好贴视口底（与 UID 账号列表 / 自定义宏列表同一标准）。
        /// <para>★ 首页列表是普通 StackPanel（没开虚拟化）⇒ 容器不回收，刷一次就稳定；
        /// 但首次加载时容器要等布局才生成，所以外面走 <see cref="ScheduleModuleSpacingRefresh"/>
        /// 重试等它。</para>
        /// </summary>
        private void RefreshModuleCardSpacing()
        {
            if (ModuleList == null || _modules.Count == 0) return;

            // 页面级滚动容器（ModuleList 的祖先，拖动排序那里也是找的它）
            var sv = _sortScroller ?? VisualTreeEx.FindAncestor<ScrollViewer>(ModuleList);

            // ★★ 2026-10-21：margin 已由 ItemContainerStyle 上的 LastItemSpacingConverter **绑定**自动算
            //   （见 HomePage.xaml）—— 容器一生成就带对的值，排序 Move / 换集合都自动重算。
            //   ⛔ 这里**绝不能再写 fe.Margin**：本地值优先级高于样式绑定，
            //      一写就永久覆盖，之后就不再更新了。
            //   只剩一件兜底：内容高度变化时若正停在底部，把 Offset 重钉到新底部。
            if (sv != null && sv.VerticalOffset >= sv.ScrollableHeight - 0.5)
                sv.ScrollToVerticalOffset(sv.ScrollableHeight);
        }

        /// <summary>
        /// 排一次首页卡片间距刷新，**看见容器才罢手**。
        /// ★★ 2026-10-21：以前这类补刷直接 <c>BeginInvoke(Loaded)</c> —— 那个优先级跑在布局之前，
        ///   容器还没生成 ⇒ 空转（UID 账号列表「首次不进、进过一次分组才好」就是这个原因）。
        ///   现在按 Render 优先级重排，直到 ContainerFromIndex(0) 拿得到东西（或试满 12 次）。
        /// </summary>
        private void ScheduleModuleSpacingRefresh()
        {
            if (ModuleList == null || _modules.Count == 0) return;

            int tries = 0;
            void Attempt()
            {
                if (ModuleList == null || _modules.Count == 0) return;
                bool ready = ModuleList.ItemContainerGenerator.ContainerFromIndex(0) != null;
                if (ready || ++tries >= 12)
                {
                    RefreshModuleCardSpacing();
                    return;
                }
                Dispatcher.BeginInvoke(new Action(Attempt), DispatcherPriority.Render);
            }
            Attempt();
        }

        // ============ 拖动排序（按住卡片图标即拖，与 UID 账号列表一致）============
        // 按下功能图标 → 卡片立刻被「抓起来」，跟着光标走、邻卡让位。
        // 松手 → 被拖卡 150ms **吸附**滑到目标槽（不是瞬间归位），滑完才落数据。
        // 没有长按延迟：图标本身也没有别的点击语义，不需要跟谁抢。
        //
        // ★★★ 三条铁律（写错任何一条都会「卡片瞬间飞到顶 / 底」）：
        //   ① 拖动全程**一行数据都不动**，只改容器的 TranslateTransform。
        //      ⛔ 绝对不能在拖动中重建 ItemsSource —— 容器连同被按的那个图标一起被回收，
        //         鼠标捕获丢失，抓在手里的那张卡立刻跳回原槽位，看上去就是「瞬间飞走」。
        //   ② 事件挂在 Window 上（Preview 隧道 + Deactivated）：
        //      ⛔ 不要挂图标自己的 LostMouseCapture —— 容器一回收就误判成松手，Commit 被反复触发。
        //   ③ 卡片位置每帧**按实际布局去量**再补差，不要靠算 —— 没有反馈回路可言。

        /// <summary>当前的功能模块顺序（ItemsSource，元素是卡片数据项）。</summary>
        /// <summary>
        /// 卡片数据（**ObservableCollection**：排序用 Move，只让被移动那一行的容器重建）。
        /// <para>★★ 原来这里是 List + 排序后 `ItemsSource = null; ItemsSource = _modules`，
        ///   等于**整张表重建** —— 每张卡片的胶囊开关都会被重建，于是所有胶囊的滑块动画一起重播，
        ///   观感就是「拖动排序一下，全部卡片重新加载」。改成 Move 后只有移动的那一行会重建。</para>
        /// </summary>
        private ObservableCollection<ModuleCardItem> _modules = new();

        /// <summary>承载列表的滚动容器（拖动时靠它的视口做自动滚动）。</summary>
        private ScrollViewer? _sortScroller;

        private bool _sortDragging;

        /// <summary>
        /// 松手之后的「吸附」阶段（被拖卡正在滑向目标槽）。
        /// <para>★ 这段时间里不能再起新的拖动、也不能再被鼠标移动驱动 —— 否则会跟吸附动画打架。</para>
        /// </summary>
        private bool _sortDropping;

        /// <summary>拖动序号：给"吸附兜底"用，防止上一轮的兜底把新的一次拖动掐掉。</summary>
        private int _sortDragToken;

        /// <summary>吸附动画时长（与 UID 账号列表一致：150ms，CubicEase/EaseOut）。</summary>
        private const int SortSettleMs = 150;

        /// <summary>吸附兜底：动画的 <c>Completed</c> 万一没来，这么久之后也要收尾落数据。</summary>
        private const int SortSettleFallbackMs = 400;

        private ModuleCardItem? _sortItem;

        /// <summary>起手序号（拖动全程不变 —— 数据没动，布局也就没动）。</summary>
        private int _sortFrom;

        /// <summary>当前算出来的目标序号。</summary>
        private int _sortTarget;

        private double _sortStartMouseY;
        private double _sortGrabOffset;   // 光标相对被拖卡片顶部的偏移
        private double _sortPitch;        // 实测行距（卡高 + 间距）
        private double _sortSlotTop;      // 列表第一格的顶部（相对 ModuleList）
        private double _sortSlotBottom;   // 列表最后一格的顶部

        /// <summary>拖动期间每个槽位的容器（索引 = 当前 UI 顺序）。</summary>
        private readonly List<FrameworkElement> _sortCards = new();

        /// <summary>与 <see cref="_sortCards"/> 一一对应的位移变换。</summary>
        private readonly List<TranslateTransform> _sortTransforms = new();

        /// <summary>每个变换当前的让位目标 —— 只在目标真变时才重设动画，否则每帧重启动画会卡。</summary>
        private readonly Dictionary<TranslateTransform, double> _sortShiftTargets = new();

        private Window? _sortWindow;
        private DateTime _sortLastMove = DateTime.MinValue;

        /// <summary>拖动时的鼠标捕获者 —— 收尾要显式释放。</summary>
        private FrameworkElement? _sortCapture;

        /// <summary>列表原本的光标，拖动结束后还原。</summary>
        private Cursor? _sortPrevCursor;

        /// <summary>
        /// 滚轮：一格 = **一张功能卡片**（UID 账号列表、自定义宏列表现在是同一个手感）。
        /// <para>本页原来是物理滚动（ScrollViewer 默认 48px/格），比一张卡片矮一截，
        ///      滚起来半张半张的。一步 78px = 卡片 70 + 卡片间距 8。</para>
        /// </summary>
        private void CardList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            e.Handled = true;

            sv.ScrollToVerticalOffset(sv.VerticalOffset + (e.Delta > 0 ? -CardRowPitch : CardRowPitch));
        }

        /// <summary>一张功能卡片占的高度：卡片 Height=70 + 卡片间距 8（与 UID / 宏列表一致）。</summary>
        private const double CardRowPitch = 78;

        /// <summary>
        /// 按下功能图标 → **立刻**进入拖动（和 UID 账号列表拖手柄一个手感，不设长按延迟）。
        /// <para>图标本身没有 Click 语义，所以不用跟什么抢：按下 = 抓起来，松手 = 落下。</para>
        /// <para>★ 排序手柄按钮也走这个 handler（DataContext 同为 ModuleCardItem）。</para>
        /// </summary>
        private void Icon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_sortDragging || _sortDropping) return;   // 吸附动画还没走完，别抢
            if (sender is not FrameworkElement grab) return;
            if (grab.DataContext is not ModuleCardItem item) return;

            var cards = CollectCardContainers();
            int from = cards.FindIndex(c => ReferenceEquals(c.DataContext, item));
            if (from < 0) return;

            _sortItem = item;
            _sortFrom = from;
            _sortTarget = from;
            _sortScroller = VisualTreeEx.FindAncestor<ScrollViewer>(ModuleList);
            _sortCards.Clear();
            _sortTransforms.Clear();
            _sortShiftTargets.Clear();

            foreach (var card in cards)
            {
                // 已经有自己的 TranslateTransform 就复用（先清掉残留动画/位移）；
                // XAML 里声明的变换可能是冻结的（IsFrozen），那种只能换新的。
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

            // 全部实测：不写死「一张卡 90px」这种假设 —— 行距一变这种常量就能把卡片推飞
            var tops = new List<double>(_sortCards.Count);
            foreach (var card in _sortCards)
                tops.Add(card.TransformToAncestor(ModuleList).Transform(new Point(0, 0)).Y);

            _sortPitch = tops.Count >= 2 ? tops[1] - tops[0] : 0;
            if (_sortPitch <= 0) _sortPitch = _sortCards[0].ActualHeight;
            if (_sortPitch <= 0) return;

            _sortSlotTop = tops[0];
            _sortSlotBottom = tops[tops.Count - 1];

            double mouseY = e.GetPosition(ModuleList).Y;
            _sortStartMouseY = mouseY;
            _sortGrabOffset = mouseY - tops[from];

            _sortDragging = true;
            _sortDropping = false;
            _sortDragToken++;                          // 让上一次拖动遗留的"吸附兜底"作废
            Panel.SetZIndex(_sortCards[from], 1);      // 拖动中的这张压在最上层
            _sortCards[from].Opacity = 0.85;           // 「抓起来了」的视觉反馈

            _sortPrevCursor = ModuleList.Cursor;
            ModuleList.Cursor = Cursors.SizeAll;

            grab.CaptureMouse();
            _sortCapture = grab;
            _sortLastMove = DateTime.Now;
            SubscribeDragEvents();
        }

        private void OnSortWindowMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // ★ 吸附阶段（松手后那 150ms）不再被鼠标驱动 —— 否则会和吸附动画抢同一个 Y
            if (_sortDragging && !_sortDropping) MoveDraggedCard(e);
        }

        private void MoveDraggedCard(System.Windows.Input.MouseEventArgs e)
        {
            if (!_sortDragging || _sortDropping || _sortItem == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { FinishSortDrag(); return; }

            MaybeAutoScroll(e);

            double mouseY = e.GetPosition(ModuleList).Y;
            double dy = mouseY - _sortStartMouseY;

            // ① 目标序号：起手位移 ÷ 行距，再钳到列表首尾（单调，不会跳）
            _sortTarget = Math.Clamp(_sortFrom + (int)Math.Round(dy / _sortPitch), 0, _sortCards.Count - 1);

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
                if (i == _sortFrom) continue;   // 这张自己跟光标，别掺合

                double want = 0;
                if (target > _sortFrom && i > _sortFrom && i <= target) want = -_sortPitch;
                else if (target < _sortFrom && i >= target && i < _sortFrom) want = _sortPitch;

                var tf = _sortTransforms[i];
                if (_sortShiftTargets.TryGetValue(tf, out double cur) && Math.Abs(cur - want) < 0.5) continue;
                _sortShiftTargets[tf] = want;

                var anim = new DoubleAnimation
                {
                    To = want,                          // ★ 不写 From：从当前值续着走
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                tf.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        /// <summary>
        /// 把被拖卡片的顶部钉到 <paramref name="desiredTop"/>（已扣掉抓取偏移，
        /// 也就是「卡片跟着手指走」而不是「跳到光标正上方」）。
        /// </summary>
        private void PinDraggedCard(double desiredTop)
        {
            desiredTop = Math.Clamp(desiredTop, _sortSlotTop, _sortSlotBottom);

            var card = _sortCards[_sortFrom];
            var tf = _sortTransforms[_sortFrom];

            // 每帧量真实位置（含已生效的动画值），再补上差额 —— 一步到位，没有反馈回路
            double currentTop = card.TransformToAncestor(ModuleList).Transform(new Point(0, 0)).Y;

            tf.BeginAnimation(TranslateTransform.YProperty, null);   // 动画会让直接赋值失效
            tf.Y += desiredTop - currentTop;
        }

        /// <summary>鼠标贴近列表上下边缘时自动滚动（7 张卡可能撑出滚动条）。</summary>
        private void MaybeAutoScroll(System.Windows.Input.MouseEventArgs e)
        {
            var sc = _sortScroller;
            if (sc == null || sc.ScrollableHeight <= 0) return;

            var now = DateTime.Now;
            double dtMs = (now - _sortLastMove).TotalMilliseconds;
            _sortLastMove = now;
            if (dtMs <= 0 || dtMs > 200) return;    // 鼠标停住就不滚，别自作主张

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

        /// <summary>
        /// 松手：被拖卡片用 <see cref="SortSettleMs"/> 滑到目标槽（和 UID 账号列表**同一套手感**，
        /// 不是瞬间归位），滑完再 <see cref="CommitSortDrag"/> 落数据。
        /// <para>
        /// ★ 动画**不写 From** —— 从卡片当前所在的位置接着跑（用户可能是在半路松的手）。
        /// ★ 目标位移 = <c>(目标序号 − 起手序号) × 实测行距</c>：卡片被 <see cref="PinDraggedCard"/>
        ///   钳过边界，当前位置未必正好在目标槽上，所以这里必须给绝对目标值。
        /// </para>
        /// </summary>
        private void FinishSortDrag()
        {
            if (!_sortDragging || _sortDropping) return;
            _sortDropping = true;

            // 位置没变 / 数据不完整 → 没什么可滑的，直接收尾
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

            // 兜底：Completed 万一没触发（动画被顶掉 / 元素被卸载）也要收尾。
            // ★ 必须带**拖动序号**校验：这 400ms 内用户要是已经开始了下一次拖动，
            //   这个迟到的兜底会把新的一次拖动当场掐掉。
            int token = _sortDragToken;
            _ = Task.Run(async () =>
            {
                await Task.Delay(SortSettleFallbackMs);
                try { await Dispatcher.InvokeAsync(() => { if (token == _sortDragToken) CommitSortDrag(); }); } catch { }
            });
        }

        /// <summary>收尾：清位移 → 改一次数据源 → 落盘 → 同步顶部功能图标。</summary>
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

            ModuleList.Cursor = _sortPrevCursor;
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

            if (from < 0 || from >= _modules.Count) return;
            if (to == from) return;

            to = Math.Clamp(to, 0, _modules.Count - 1);
            var moved = _modules[from];

            // ★★ 用 Move（而不是 RemoveAt+Insert + 重设 ItemsSource）：
            //   后者会让**整张表**的容器重建 ⇒ 每个胶囊开关都重播一次 0.4s 滑块动画，
            //   用户看到的就是「拖一个，所有卡片重新播放」。Move 只影响被移动的那一行。
            _modules.Move(from, to);

            // ★★ Move 之后那一行的容器会被重建，CheckBox 的 IsChecked 重新求值 ⇒
            //   胶囊样式会把 0.4s 的滑块动画重播一次。这里在**布局完成之后**把滑块直接摆到终点。
            //   ★ 连压 3 轮：容器重建有时跨一轮布局，只压一次可能扑空。
            //   ★ 用 LayoutUpdated（布局末尾、渲染之前）而不是 DispatcherPriority.Loaded ——
            //     后者可能早于容器生成。
            SnapToggleThumbnailsAfterLayout();

            // ★ 2026-10-21：排序换了「最后一张卡」的人选 ⇒ 重新算底部贴齐
            //   （Move 之后被移动那行的容器会重建，新容器带的是 ItemContainerStyle 的 8）
            ScheduleModuleSpacingRefresh();

            var s = ConfigManager.Get<HomePageSettings>();
            ModuleRegistry.SaveOrder(s, _modules.Select(m => m.Module));
            ConfigManager.Save(s);

            if (Application.Current.MainWindow is UI.MainWindow mw)
                mw.SyncFunctionButtonOrder();
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

        /// <summary>按当前 UI 顺序收集所有卡片容器（ContentPresenter）。</summary>
        private List<ContentPresenter> CollectCardContainers()
        {
            var list = new List<ContentPresenter>();

            // ★ 必须走 ItemContainerGenerator，不能用 FindVisualChildren<ContentPresenter>：
            //   Button / CheckBox 的模板内部也有 ContentPresenter，且 DataContext 是同一个卡片数据项，
            //   遍历会连它们一起收进来 → 顺序乱、数量翻倍，拖动当场错位。
            var generator = ModuleList.ItemContainerGenerator;
            for (int i = 0; i < ModuleList.Items.Count; i++)
                if (generator.ContainerFromIndex(i) is ContentPresenter cp)
                    list.Add(cp);

            return list;
        }

        // ============ 总开关视觉 ============

        /// <summary>
        /// 总开关关时，所有功能胶囊开关视觉置灰（配置值不变）
        /// </summary>
        /// <summary>
        /// 胶囊开关**第一次进入可视树**时，把滑块直接摆到当前状态该在的位置。
        ///
        /// <para>
        /// ★★ 为什么必须挂在这里：胶囊样式里「选中」状态带一段 0.4s 的滑块动画，
        ///   而新建容器时 <c>IsChecked</c> 是「从默认 false 变成 true」⇒ 动画必然播一次。
        ///   用户看到的就是「开关本来是开的，切个页面回来先显示成关、然后自己滑到开」。
        /// </para>
        /// <para>
        /// ★ <c>Loaded</c> 在**首次渲染之前**触发（此时绑定已经算完），在这里压掉动画，
        ///   第一帧画出来就是终态，用户看不到那一下滑动。
        /// </para>
        /// </summary>
        private void ModuleToggle_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;

            // ★ 立刻试一次；再排到布局后重试一次 ——
            //   少数情况下 Loaded 早于模板应用，那时还拿不到滑块。
            ToggleSwitchHelper.Snap(cb);
            Dispatcher.BeginInvoke(new Action(() => ToggleSwitchHelper.Snap(cb)),
                DispatcherPriority.Loaded);
        }

        private void ApplyMasterSwitchVisual(bool masterOn)
        {
            // ★ 只改这一个值：卡片上的胶囊靠 DataTrigger 绑它，容器怎么重建都跟着走
            //   （原来遍历 CheckBox 设 Opacity 的做法在拖动排序后会失效）。
            MasterSwitchOn = masterOn;
        }

        /// <summary>
        /// 连续几轮布局结束后，把胶囊开关的滑块直接摆到终点（跳过容器重建带来的动画回放）。
        /// <para>
        /// ★ 为什么连压几轮：<c>ObservableCollection.Move</c> 之后容器重建可能不是同一轮布局完成的，
        ///   只压一次有可能遍历到还没换掉的旧容器。
        /// </para>
        /// ★ <c>LayoutUpdated</c> 在**布局末尾、渲染之前**触发 —— 压制后第一帧就是终态，用户看不到滑动。
        /// </summary>
        private void SnapToggleThumbnailsAfterLayout()
        {
            int rounds = 3;

            EventHandler? handler = null;
            handler = (_, _) =>
            {
                ToggleSwitchHelper.SnapThumbnails(ModuleList);

                if (--rounds <= 0) ModuleList.LayoutUpdated -= handler;
            };

            ModuleList.LayoutUpdated += handler;
        }

        /// <summary>供 MainWindow 在总开关切换后调用，刷新本页开关视觉</summary>
        public void RefreshMasterSwitchVisual()
        {
            var s = ConfigManager.Get<HomePageSettings>();
            ApplyMasterSwitchVisual(s.MasterSwitch);
        }

        // ============ 开关切换 → 保存 ============

        private void Toggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox cb || cb.DataContext is not ModuleCardItem item) return;

            bool isChecked = cb.IsChecked == true;
            var settings = ConfigManager.Get<HomePageSettings>();
            item.Module.SetEnabled(settings, isChecked);
            ConfigManager.Save(settings);

            // 手柄拾取需要跟随开关即时启停监听
            ControllerPickupLogic.SyncWithSettings();

            if (Application.Current.MainWindow is UI.MainWindow mw)
                mw.ShowToast($"{item.Module.Name}: {(isChecked ? "已启用" : "已关闭")}", isChecked);
        }

        // ============ 按钮点击 ============

        private void TriggerKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not ModuleCardItem item) return;
            NavigateToModule(item.Module.Name, showTriggerKey: true);
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not ModuleCardItem item) return;
            NavigateToModule(item.Module.Name, showTriggerKey: false);
        }

        /// <summary>
        /// 导航到指定功能的配置容器页（可直跳触发键页）。
        /// 供各处复用：首页卡片、触发键页的"点击占用键跳转"。
        /// </summary>
        public static void NavigateToModule(string moduleName, bool showTriggerKey)
        {
            // ★ 自定义宏不走「配置容器页」（那是带「快速传送」顶栏的通用工具页），
            //   而是和 UID 一样直接进内容区（固定头行 = 左侧导航栏，下面是文件夹/宏列表），
            //   并且带页面记忆。所以这里特判一下，按「触发键 / 配置」分流。
            if (string.Equals(moduleName, MacroModuleService.ModuleName, StringComparison.Ordinal))
            {
                var mw = Application.Current.MainWindow as UI.MainWindow;
                if (mw == null) return;

                if (showTriggerKey)
                {
                    // ★ 触发键按钮：打开宏的触发键设置页（用宿主配置容器页承载，
                    //   这样能复用「被其它功能占用」检测、占用后跳转等通用能力）。
                    var macroContainer = CreateContainerFor(moduleName);
                    if (macroContainer != null)
                    {
                        macroContainer.ShowTriggerKeyPage();
                        mw.MainFrame.Navigate(macroContainer);
                    }
                }
                else
                {
                    // ★ 「打开」：直接进宏的内容区视图（复用同一个实例 ⇒ 回到上次的位置）。
                    mw.ShowMacroView();
                }
                return;
            }

            var container = CreateContainerFor(moduleName);
            if (container == null) return;

            if (showTriggerKey) container.ShowTriggerKeyPage();
            else container.ShowConfigPage();

            (Application.Current.MainWindow as UI.MainWindow)?.MainFrame.Navigate(container);
        }

        // ============ 创建容器 ============

        public static ConfigContainerPage? CreateContainerFor(string moduleName)
        {
            var module = ModuleRegistry.ByName(moduleName);
            if (module == null) return null;

            var container = new ConfigContainerPage();
            container.SetSourceModule(module.Name,
                module.Description,
                module.IconPath,
                module.CreateTags?.Invoke());
            container.SetTriggerKeyPage(
                module.UseGamepad ? new GamepadTriggerKeyPage() : new TriggerKeyPage(),
                module.KeyTag);
            container.SetConfigPage(module.CreateConfigPage());

            // ★ 插件模块可以在底部按钮条最前面挂自己的按钮（「添加宏」）——
            //   位置就是「添加宏 / 触发键 / 配置 / 返回 / 开关」里的第一个。
            if (module.CreateToolbarElement != null)
            {
                try
                {
                    container.SetToolbarElement(module.CreateToolbarElement());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HomePage] 创建 {module.Name} 的工具栏按钮失败：{ex.Message}");
                }
            }

            return container;
        }

        // ============ 工具 ============
    }

    /// <summary>
    /// 主页模块卡片的数据项：模块 + 开关状态 + 触发键显示。
    /// 卡片外观由 HomePage.xaml 的 DataTemplate 呈现。
    /// </summary>
    public class ModuleCardItem
    {
        public ModuleCardItem(ModuleInfo module, HomePageSettings s)
        {
            Module = module;
            IsChecked = module.IsEnabled(s);

            ConfigButtonText = module.Name == MacroModuleService.ModuleName ? "打开" : "配置";

            string key = module.GetKey(s);
            HasKey = !string.IsNullOrEmpty(key) && key != "未设置";
            TriggerKeyText = HasKey ? key : "触发键";
            TriggerKeyTooltip = $"当前设置：{(HasKey ? key : "未设置")}";

            ShowTags = module.CreateTags != null;

            // ★ 插件模块的标题行控件（「自定义宏」的 宏文件 N 个 + 编辑 按钮）。
            //   给了它就用它替代分辨率标签；没给（或插件抛异常）就照旧什么都不显示。
            try
            {
                TagExtension = module.CreateTagExtension?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomePage] 创建 {module.Name} 的标题行扩展失败：{ex.Message}");
                TagExtension = null;
            }

            // ★ 插件模块的第二行控件（「自定义宏」的两个下拉）。构造函数本来就跑在 UI 线程，
            //   这里建控件是安全的；插件内部抛异常也不能让首页崩 —— 只当它没有扩展行。
            try
            {
                CardExtension = module.CreateCardExtension?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HomePage] 创建 {module.Name} 的卡片扩展失败：{ex.Message}");
                CardExtension = null;
            }
        }

        public ModuleInfo Module { get; }
        public bool IsChecked { get; set; }
        public bool HasKey { get; }
        public string TriggerKeyText { get; }
        public string TriggerKeyTooltip { get; }
        public bool ShowTags { get; }

        /// <summary>
        /// 卡片右侧「配置」按钮的文案：自定义宏要叫「打开」（点它进内容区视图，
        /// 且复用同一实例 ⇒ 回到上次浏览到的文件夹 / 滚动位置，即页面记忆）；
        /// 其它模块保持「配置」。
        /// </summary>
        public string ConfigButtonText { get; }

        /// <summary>卡片标题行右侧的插件控件（给了就替代分辨率标签；普通模块为 null）。</summary>
        public FrameworkElement? TagExtension { get; }

        public bool HasTagExtension => TagExtension != null;

        /// <summary>卡片第二行的扩展控件（插件模块用；普通模块为 null）。</summary>
        public FrameworkElement? CardExtension { get; }

        public bool HasCardExtension => CardExtension != null;

        /// <summary>没有扩展控件时才显示描述（插件模块的描述为空，本来也不显示）。</summary>
        public bool HasDescription => CardExtension == null && !string.IsNullOrEmpty(Module.Description);
    }
}
