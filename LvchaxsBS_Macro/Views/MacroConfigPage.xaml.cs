using System;
using System.Collections;
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
using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Models;
using LvchaxsBS_Macro.Utils;

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

        /// <summary>
        /// 顶层文件夹列表（用 <see cref="ObservableCollection{T}"/>：拖动排序要**就地 Move**，
        /// 重设 ItemsSource 会让容器重建、手感断掉）。
        /// </summary>
        private ObservableCollection<MacroFolder> _folders = new();

        /// <summary>
        /// 正在查看的文件夹 Id（<c>""</c> = 顶层，显示文件夹列表）。
        /// </summary>
        private string _currentFolderId = "";

        /// <summary>
        /// 下一次打开本页时**直接进入这个文件夹**（首页卡片的「打开宏文件夹」按钮用）。
        /// <para>
        /// ★ 宿主每次都 new 一个 <see cref="MacroConfigPage"/>，实例之间没法传参 ⇒ 只能走静态字段；
        ///   <see cref="Reload"/> 一进来就消费并清空，不会影响后续正常打开。
        /// </para>
        /// <para>
        /// ★ <c>null</c> = 没指定（保留当前视图，用于页面记忆）；<c>""</c> = 顶层文件夹列表；
        ///   非空的文件夹 Id = 直接进那个文件夹（即使是空文件夹也进）。
        /// </para>
        /// </summary>
        public static string? PendingFolderId { get; set; } = null;

        /// <summary>当前是不是"已经进到某个文件夹里"（底部按钮据此切「添加文件夹 / 添加宏」）。</summary>
        public static bool IsInsideFolderView => false;   // v2：文件夹功能已下线

        /// <summary>当前文件夹 Id（新建宏时归到这里；顶层为空串）。</summary>
        public static string CurrentFolderId => "";       // v2：不再分组

        /// <summary>视图切换（顶层 ↔ 分组内）时通知外部（底部那个「添加宏 / 添加文件夹」按钮）。</summary>
        public static event Action? ViewChanged;

        /// <summary>
        /// 宿主底部「返回」按钮的**约定钩子**（按名字反射调用，见宿主的 <c>ConfigContainerPage.BackBtn_Click</c>）。
        /// <para>
        /// ★ 分组内 ⇒ 退回文件夹列表并返回 <c>true</c>（宿主就不再回首页）；
        ///   顶层 ⇒ 返回 <c>false</c>，让宿主照常回首页。
        /// </para>
        /// </summary>
        public bool TryGoBack()
        {
            if (_currentFolderId.Length == 0) return false;

            _currentFolderId = "";
            Reload();
            return true;
        }

        /// <summary>
        /// 宿主底部「返回」按钮的**悬浮提示文案**（同样按名字反射调用，
        /// 见宿主的 <c>ConfigContainerPage.RefreshBackTip</c>）。
        /// <para>分组内说清是退回文件夹列表，顶层才是回首页。</para>
        /// </summary>
        public string GetBackTip()
            => "返回首页";

        /// <summary>
        /// 把「返回」的悬浮提示同步给宿主那个按钮。
        /// <para>
        /// ★★ 为什么要插件自己推：WPF 没有 PreviewMouseEnter（鼠标进入没有隧道版本），
        /// 而宿主的 ButtonTip 是在 MouseEnter 里读 Text 的，两者先后不保证 ——
        /// 等宿主那侧去问就已经晚了。所以在**每次切视图后**直接把 Text 写好。
        /// </para>
        /// <para>
        /// ★ 插件不引用宿主，所以附加属性按**字符串类型名**反射拿：
        /// <c>LvchaxsBS.UI.Controls.ButtonTip.TextProperty</c>。取不到就静默跳过（不影响功能）。
        /// </para>
        /// </summary>
        private void UpdateHostBackTip()
        {
            try
            {
                var window = Window.GetWindow(this);
                if (window == null) return;

                if (FindVisualChild<Button>(window, "PART_BackBtn") is not Button backButton) return;

                var tipType = Type.GetType("LvchaxsBS.UI.Controls.ButtonTip, LvchaxsBS");
                if (tipType?.GetField("TextProperty")?.GetValue(null) is not DependencyProperty textProperty) return;

                backButton.SetValue(textProperty, GetBackTip());
            }
            catch
            {
                // 宿主版本对不上 / 按钮还没建出来 —— 提示不更新而已，绝不能因此崩掉
            }
        }

        public MacroConfigPage()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                _current = this;
                Reload();

                // 底栏「返回」按钮：进到某个文件夹里才显示（顶层隐藏）
                MacroConfigPage.ViewChanged -= OnViewChanged;
                MacroConfigPage.ViewChanged += OnViewChanged;
                UpdateBackButton();
            };

            // 拖动途中被导航走 → 必须收尾，否则 Window 上的订阅一直挂着
            Unloaded += (_, _) =>
            {
                // ⛔ 绝不能在这里清 _current！导航进指令编辑页时本页会 Unload，
                //    清了 ⇒ 编辑页「返回列表」拿到 null ⇒ Navigate(null) ⇒ 列表一片空白。
                //    （_current 会在下次 Loaded 时被新/旧实例覆盖，不会泄漏）
                MacroConfigPage.ViewChanged -= OnViewChanged;
                if (_sortDragging) CommitSortDrag();
            };
        }

        /// <summary>视图切换（顶层 ↔ 文件夹内）：刷新底栏「返回」按钮和顶部统计文案。</summary>
        private void OnViewChanged()
        {
            UpdateBackButton();
            UpdateHeader();
        }

        /// <summary>
        /// 顶部统计文案（v2：没有文件夹了）——「宏列表：共 N 个 · M 个已启用」。
        /// </summary>
        private void UpdateHeader()
        {
            if (HeaderStatsLeft == null || HeaderStatsRight == null) return;

            try
            {
                var macros = MacroSettingsStore.Current.Macros;
                int total = macros?.Count ?? 0;
                int on = macros?.Count(m => m.Enabled) ?? 0;

                // 和 UID「账号列表：共计N个账号 · M个分组」同一套分段（主文本 / 次要文本）
                HeaderStatsLeft.Text = $"宏列表: 共{total}个";
                HeaderStatsRight.Text = $"{on}个已启用";
            }
            catch
            {
                // 读配置失败不影响页面本身
            }
        }

        private void UpdateBackButton()
        {
            if (BackButton != null)
                BackButton.Visibility = _currentFolderId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>底栏「返回」：从文件夹内退回文件夹列表（顶层时按钮隐藏，不会点到）。</summary>
        private void BackButton_Click(object sender, RoutedEventArgs e) => TryGoBack();

        // ============ 刷新 ============

        /// <summary>重建列表期间屏蔽胶囊开关的 Checked 回调（否则初始化会写配置）。</summary>
        private bool _reloading;

        /// <summary>
        /// 处理一次胶囊开关期间屏蔽**连带**的回调。
        /// <para>
        /// ★ 同键互斥会自动把别的宏关掉 ⇒ 那张卡的 CheckBox 回一次 Unchecked，
        ///   不拦的话它会再跑一次写入（无害但多余，且可能把互斥结果再写回去）。
        /// </para>
        /// </summary>
        private bool _toggling;

        private void Reload()
        {
            _reloading = true;
            try
            {
                ReloadCore();
            }
            finally
            {
                _reloading = false;
            }
        }

        // ============ 主程序「功能总开关」的视觉联动 ============

        /// <summary>
        /// 供主程序在总开关切换后调用（宿主按**约定方法名**反射调用：公开、无参）。
        /// <para>★ 和首页功能卡片完全一致的表达：总开关关闭时胶囊开关 Opacity = 0.4。</para>
        /// <para>
        /// ★★ 只改**数据**（<see cref="MacroItem.MasterSwitchOn"/>），不碰控件 ——
        ///   胶囊的灰度由 DataTrigger 绑上去，这样拖动排序（容器会被 Move 重建）之后依然正确。
        /// </para>
        /// </summary>
        public void RefreshMasterSwitchVisual()
        {
            bool masterOn = HostBridge.IsMasterSwitchOn();
            foreach (MacroItem item in _items) item.MasterSwitchOn = masterOn;
        }

        private void ReloadCore()
        {
            // ★ 首页卡片「打开宏文件夹」预置的目标文件夹（只消费一次；null = 保留当前视图）
            if (PendingFolderId != null)
            {
                _currentFolderId = PendingFolderId;
                PendingFolderId = null;
            }

            var settings = MacroSettingsStore.Current;

            // v2：文件夹功能下线，直接是一整张宏列表
            ReloadTopLevel(settings);

            ViewChanged?.Invoke();

            // 每次切视图都把顶部统计文案和宿主「返回」的悬浮提示改对
            UpdateHeader();

            // 每次切视图都把宿主「返回」的悬浮提示改对（分组内 = 返回 宏文件夹）
            UpdateHostBackTip();

            // ★ 列表已经显示完 ⇒ 趁界面空闲把 152 张头像解好，
            //   等下点「编辑」开编辑窗口时就不用再等解码（只做一次）。
            if (!_avatarsWarmed)
            {
                _avatarsWarmed = true;
                Utils.AvatarCatalog.BeginWarmUp();
            }
        }

        /// <summary>头像预热只排一次（<see cref="Utils.AvatarCatalog.BeginWarmUp"/>）。</summary>
        private static bool _avatarsWarmed;

        /// <summary>
        /// 重新绑定宏列表（v2：不再有文件夹，直接是全部宏；置顶的排在前面）。
        /// </summary>
        private void ReloadTopLevel(Config.MacroSettings settings)
        {
            var all = settings.Macros
                .OrderByDescending(m => m.IsPinned)
                .ToList();

            // ★ 置顶高亮的显示状态跟着真实置顶状态走（重建后不会残留上一次的显示/隐藏）
            // ★ 胶囊灰度也在这里一次设好：容器重建（拖动 Move）后由 DataTrigger 自动沿用
            bool masterOn = HostBridge.IsMasterSwitchOn();
            foreach (var item in all)
            {
                item.ShowStar = item.IsPinned;
                item.MasterSwitchOn = masterOn;
            }

            _items = new ObservableCollection<MacroItem>(all);
            MacroList.ItemsSource = _items;
            MacroList.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            bool empty = _items.Count == 0;
            EmptyHint.Text = "还没有宏，点左上角「新建宏」创建一个";
            EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

            // 最后一张卡不留下边距（滚到底时贴视口底）。
            // ★ ItemsSource 刚重设，容器要等布局才生成 ⇒ 布局跑完再刷一次兜底。
            RefreshMacroCardSpacing();
            Dispatcher.BeginInvoke(new Action(RefreshMacroCardSpacing), DispatcherPriority.Loaded);
        }

        // ============ 文件夹：进入 / 返回 / 编辑 / 删除 ============

        private static Button? FindAncestorButton(DependencyObject? node)
        {
            while (node != null)
            {
                if (node is Button b) return b;
                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return null;
        }

        // ============ 卡片：置顶 / 编辑 / 删除 ============

        /// <summary>
        /// 点图标 = 置顶 / 取消置顶。
        /// <para>竖条从中心向上下展开，播完才改数据并重排到顶部。</para>
        /// </summary>
        private void MacroIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MacroItem item) return;

            e.Handled = true;

            bool showing = !item.IsPinned;   // 这次要变成「置顶」还是「取消置顶」

            ApplyStarToggle(showing, () =>
            {
                MacroSettingsStore.TogglePin(item.Id);
                ReorderInPlace(item);        // ★ 就地 Move + FLIP：卡片平滑滑到新位置，不瞬间跳
            }, MacroList, item, v => item.ShowStar = v);
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
            var before = CaptureCardTops(MacroList, _items, o => ((MacroItem)o).Id);

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
            RunFlip(MacroList, _items, o => ((MacroItem)o).Id, before);

            // 置顶重排后「最后一张」可能换了人 ⇒ 重算一遍底边距
            RefreshMacroCardSpacing();
        }

        /// <summary>记录每张卡片的**旧**位置（按 Id 索引，容器不在可视区就跳过）。</summary>
        private static Dictionary<string, (FrameworkElement card, double top)> CaptureCardTops(
            ItemsControl list, IEnumerable items, Func<object, string> idOf)
        {
            var map = new Dictionary<string, (FrameworkElement, double)>();

            foreach (object item in items)
            {
                if (list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement card) continue;

                double top = card.TransformToAncestor(list).Transform(new Point(0, 0)).Y;
                map[idOf(item)] = (card, top);
            }

            return map;
        }

        /// <summary>FLIP 主体：把每张卡从旧位置 300ms 缓出滑到当前位置（同 UID）。</summary>
        private void RunFlip(ItemsControl list, IEnumerable items, Func<object, string> idOf,
                             Dictionary<string, (FrameworkElement card, double top)> before)
        {
            if (before.Count == 0) return;

            var after = CaptureCardTops(list, items, idOf);
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
        /// <summary>
        /// 胶囊开关**第一次进入可视树**时，把滑块直接摆到当前状态该在的位置。
        /// <para>
        /// ★★ 胶囊样式里「选中」带 0.4s 滑块动画，新建容器时 IsChecked 是「默认 false → true」
        ///   ⇒ 动画必播一次。用户看到的就是「本来是开着的，切个页面回来先显示成关、然后自己滑到开」。
        /// </para>
        /// <para>★ Loaded 在首次渲染之前触发（绑定已算完），压掉后第一帧就是终态。</para>
        /// </summary>
        private void MacroToggle_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;

            // ★ 立刻试一次；再排到布局后重试一次（Loaded 偶尔早于模板应用，那时还拿不到滑块）
            SnapToggle(cb);
            Dispatcher.BeginInvoke(new Action(() => SnapToggle(cb)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>胶囊开关（每条宏自己的生效开关）。</summary>
        private void MacroToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_reloading) return;   // 重建列表时 CheckBox 初始化会触发，忽略
            if (_toggling) return;    // 互斥关掉别的宏时那张卡回的 Unchecked，忽略
            if (sender is not CheckBox cb || cb.Tag is not string id) return;

            bool on = cb.IsChecked == true;

            // ★★ 一律交给 Store 落盘并做「同键互斥」——
            //   不能先比对 current.Enabled 再决定写不写：IsChecked 是 TwoWay 绑定，
            //   事件触发时模型里的值**已经被绑定改过了**，比出来永远相等 ⇒ 互斥和落盘全被跳过。
            _toggling = true;
            try
            {
                MacroSettingsStore.SetMacroEnabled(id, on);
            }
            finally
            {
                _toggling = false;
            }

            // ★ 顶部「宏列表: 共N个 · M个已启用」当场跟着变（不然要切走再切回来才更新）
            UpdateHeader();
        }

        /// <summary>
        /// 点触发键 → 弹「请按键」小窗，捕获到这个宏自己的触发键。
        /// <para>★ 用插件自己的全局键盘钩子（KeyWatcher）捕获，不依赖宿主的按键设置页。</para>
        /// </summary>
        private void MacroTriggerKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string id) return;

            var macro = MacroSettingsStore.GetAllMacros().FirstOrDefault(m => m.Id == id);
            if (macro == null) return;

            // ★★ 走宿主的**键盘网格页**（和「快速切图给地区绑键」同一个控件）：
            //   点一下键就选好、自动返回；别的宏用掉的键在那里是灰底红字、点不动。
            //   （之前用的是插件自己那个「请按键」小窗，用户要求换成这张网格页。）
            NavigationService?.Navigate(new MacroKeyPickerPage(id));
        }

        private void EditMacroCommands_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id) return;

            var item = MacroSettingsStore.Current.Macros.FirstOrDefault(m => m.Id == id);
            if (item == null) return;

            NavigationService?.Navigate(new MacroScriptEditorPage(item));
        }

        // ============ 置顶 / 取消置顶：竖条的生长/收回动画（同 UID 分组列表） ============

        /// <summary>置顶标识层与竖条的 x:Name（写在卡片模板里，靠视觉树按名字找）。</summary>
        private const string PinHighlightLayerName = "PinHighlightLayer";
        private const string PinBarName = "PinBar";

        /// <summary>竖条动画还没落地时忽略再次点击，避免连点把置顶切错。</summary>
        private bool _starToggleAnimating;

        /// <summary>
        /// 置顶/取消置顶的「表演 + 落地」（只作用于用户点的那一张卡）。
        /// <para>
        /// ★★ 顺序：**动画先播完，卡片再挪位置**（生效与取消都一样）。
        ///   · 生效（showing=true）：先「预告」—— 把竖条收成 0 高，再翻 <see cref="MacroItem.ShowStar"/>
        ///     让它显出来（此刻是收起的，不会先在原位闪一下全高）；接着播生长；
        ///     **播完才 commit**（这时才真正改置顶数据并重排到最上）。
        ///   · 取消（showing=false）：先播缩回（此时数据还没变、竖条还看得见），播完再 commit。
        /// ★ 为什么不用 XAML 触发器：列表刷新会重建行容器，取消时新容器一建出来就是不显示，
        ///   触发器的 ExitActions 没有「true→false」这个过渡可依赖，缩回动画根本没机会播。
        /// </summary>
        private void ApplyStarToggle(bool showing, Action commit, ItemsControl list, object item, Action<bool> setStar)
        {
            if (_starToggleAnimating || _reordering) return;      // 重排/动画期间忽略新点击

            // ★ 先把布局催出来：上一次 commit 刚重排过时容器可能还没生成，
            //   ContainerFromItem 会返回 null → 动画静默不播（看着就是"没动画"）。
            try { list.UpdateLayout(); } catch { /* 布局失败不影响落地 */ }

            bool committed = false;
            Action commitOnce = () =>
            {
                if (committed) return;             // 幂等：兜底与 Completed 都调它也只会落地一次
                committed = true;
                _starToggleAnimating = false;
                setStar?.Invoke(showing);   // 兜底：动画没播成时也要把 ShowStar 归位
                commit?.Invoke();
            };

            try
            {
                var container = list.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;

                if (showing)
                {
                    PreviewPinHighlight(container, setStar);     // 预告：竖条先收成 0 高（还看不出变化）

                    if (TryPlayPinHighlight(container, true, 240, EasingMode.EaseOut, commitOnce, setStar))
                    {
                        _starToggleAnimating = true;          // 落地被延后到动画结束
                        _ = CommitAfterDelayAsync(commitOnce); // 兜底：Completed 没触发也要落地
                    }
                    else
                    {
                        commitOnce();                          // 没容器/播不了，直接落地
                    }
                }
                else if (TryPlayPinHighlight(container, false, 180, EasingMode.EaseIn, commitOnce, setStar))
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
        /// 「竖条要出现」的视觉预告 —— 让生长动画能在**数据切换之前**先播完。
        /// 步骤：① 把竖条收成 0 高（此刻整层还是折叠的，看不出变化）；
        ///       ② 再翻一下 ShowStar 让它显出来 —— 它一出现就是收起状态，不会先在原位闪一帧全高。
        /// ★ 只翻 ShowStar 这个**纯界面标志**（不落盘、不参与排序）→ 列表顺序不变，
        ///   真正的置顶数据由随后的 commit 在动画播完后才改。
        /// </summary>
        private void PreviewPinHighlight(DependencyObject? container, Action<bool> setStar)
        {
            try
            {
                if (container == null) return;

                var layer = FindVisualChild<FrameworkElement>(container, PinHighlightLayerName);
                if (layer != null)
                {
                    var scale = EnsureScaleTransform(FindVisualChild<FrameworkElement>(layer, PinBarName));
                    if (scale != null)
                    {
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);   // 清掉上一轮留下的终值
                        scale.ScaleY = 0;
                    }
                }

                // 收起状态已摆好，再让它出现（顺序不能反：先显出来就会在原地闪一帧）
                setStar(true);
            }
            catch
            {
                // 预告失败不影响落地
            }
        }

        /// <summary>
        /// 对某张卡片播「置顶竖条」动画：竖条从中心向上下展开 / 向中心缩回。
        /// <para>返回是否真的播了（没播就让调用方立刻落地数据）。</para>
        /// </summary>
        private bool TryPlayPinHighlight(DependencyObject? container, bool showing,
                                         int milliseconds, EasingMode easing, Action onCompleted,
                                         Action<bool>? setStar = null)
        {
            try
            {
                if (container == null) return false;

                var layer = FindVisualChild<FrameworkElement>(container, PinHighlightLayerName);
                if (layer == null) return false;

                var scale = EnsureScaleTransform(FindVisualChild<FrameworkElement>(layer, PinBarName));
                if (scale == null) return false;

                // 名称颜色跟着一起过渡：和蓝杠**同一个时长、同一个缓动**，
                // 所以看上去是"蓝杠往下长 + 名称慢慢染蓝"同时发生。
                TextBlock? title = null;
                try
                {
                    title = FindVisualChild<TextBlock>(container, MacroTitleName);
                    if (title != null)
                    {
                        var brush = EnsureOwnNameBrush(title, showing);
                        Color from = showing ? NameNormalColor : NamePinnedColor;
                        Color to = showing ? NamePinnedColor : NameNormalColor;

                        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);

                        var colorAnim = new ColorAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
                        {
                            EasingFunction = new CubicEase { EasingMode = easing },
                            FillBehavior = FillBehavior.Stop
                        };
                        // ★★ Stop 播完会把 Color 交还**本地值**（= 这个画刷自己的 Color 属性）。
                        //    画刷是复用的：置顶时建的、取消置顶时还拿着它，本地色仍是蓝色
                        //    ⇒ 不手动改的话，动画一结束名称就"立马又变蓝"（用户实测反馈）。
                        // ★★ 交还本地色之后还要 **ClearValue 把 Foreground 还给样式**：
                        //    私有画刷是本地值，优先级压过 Style 里的 DynamicResource
                        //    ⇒ 不还的话切主题时名称不跟着变色（用户实测反馈）。
                        //    终值和样式值一致，所以 ClearValue 那一帧不会有色变。
                        // ★★ 交还之前必须先把 ShowStar 归位（取消置顶 → false）：
                        //    名称颜色由 DataTrigger 绑 ShowStar 决定，ShowStar 不归位的话
                        //    ClearValue 一交还样式，触发器立刻又给蓝色
                        //    （用户实测：取消置顶后昵称不变回去 —— 取消路径原来只置 true、从不置 false）。
                        //    归位后再 ClearValue ⇒ 样式值就是目标色，那一帧无色变。
                        colorAnim.Completed += (_, __) =>
                        {
                            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                            brush.Color = to;
                            setStar?.Invoke(showing);
                            title.ClearValue(TextBlock.ForegroundProperty);
                        };

                        brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                    }
                }
                catch
                {
                    // 动画没播成：也把本地画刷交还样式（显示状态由 ShowStar 触发器保证是对的）
                    title?.ClearValue(TextBlock.ForegroundProperty);
                }

                return AnimateScaleY(scale, showing ? 0 : 1, showing ? 1 : 0,
                                     milliseconds, easing, onCompleted);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 名称 TextBlock 换了数据源（容器被回收复用）时，清掉上一轮动画留下的本地画梳。
        /// <para>
        /// ★★ 置顶/取消置顶会重排列表，行的容器会被回收再套到另一张卡上。
        ///    颜色动画用的是**逐实例画梳（本地值）**，它会跟着容器一起残留
        ///    ⇒ 不清的话，复用出来的卡片名称会顶着上一个卡片的颜色（用户实测：取消置顶后昵称不变回去）。
        /// </para>
        /// <para>清掉后由 Style 的 Setter / DataTrigger 按新的 ShowStar 给出正确颜色。</para>
        /// </summary>
        private void PinTitle_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBlock tb)
                tb.ClearValue(TextBlock.ForegroundProperty);
        }

        /// <summary>卡片名称那个 TextBlock 的 x:Name（颜色过渡要找到它）。</summary>
        private const string MacroTitleName = "MacroTitleText";

        /// <summary>名称的两种颜色（跟主题走；取不到时用浅色主题的兜底值）。</summary>
        private static Color NameNormalColor => ThemeColor("TextPrimaryBrush", Color.FromRgb(0x1F, 0x29, 0x37));
        private static Color NamePinnedColor => ThemeColor("PinNameBrush", Color.FromRgb(0x93, 0xC5, 0xFD));

        /// <summary>从主题字典取画刷颜色（找不到 / 不是纯色就返回兜底值）。</summary>
        private static Color ThemeColor(string key, Color fallback)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is SolidColorBrush sb)
                    return sb.Color;
            }
            catch
            {
                // 取不到就用兜底
            }
            return fallback;
        }

        /// <summary>
        /// 拿一个「属于这个 TextBlock 自己」的画刷来播颜色动画。
        /// <para>
        /// ★★ 主题字典里的画刷是**全程序共享的同一个实例** —— 直接动画它的 <see cref="SolidColorBrush.Color"/>
        ///    会把所有用该画刷的文字一起染蓝，所以必须先换成逐实例的新画刷。
        /// </para>
        /// <para>
        /// ★ 判据：现在这个 Foreground 只要还是主题共享实例（或冻结的），就换成新的，
        ///   新实例的初始色取"此刻应该显示的颜色"（<paramref name="showing"/> = 即将变成置顶）。
        ///   已经是自己实例的（上一轮动画换过）直接复用。
        /// </para>
        /// </summary>
        private static SolidColorBrush EnsureOwnNameBrush(TextBlock text, bool showing)
        {
            var sharedText = Application.Current?.TryFindResource("TextPrimaryBrush") as SolidColorBrush;
            var sharedPrimary = Application.Current?.TryFindResource("PinNameBrush") as SolidColorBrush;

            if (text.Foreground is SolidColorBrush own && !own.IsFrozen
                && !ReferenceEquals(own, sharedText) && !ReferenceEquals(own, sharedPrimary))
                return own;

            var fresh = new SolidColorBrush(showing ? NamePinnedColor : NameNormalColor);
            text.Foreground = fresh;
            return fresh;
        }

        /// <summary>
        /// 取出元素上「可以播动画」的缩放变换（没有就挂一个）。
        /// ★ 坑：XAML 里写的 &lt;ScaleTransform/&gt;（不管在 Style / ControlTemplate / DataTemplate 里）
        ///   会在模板密封时被 **Freeze**，冻结后既不能改它的值、也不能 BeginAnimation，
        ///   异常又被 try/catch 吞掉 → 表面上就是「没动画」。
        ///   所以这里一律检查 IsFrozen：不合适的就换一个逐实例、未冻结的新实例挂上去。
        /// </summary>
        private static ScaleTransform? EnsureScaleTransform(FrameworkElement? element)
        {
            if (element == null) return null;

            if (element.RenderTransform is ScaleTransform existing && !existing.IsFrozen)
                return existing;

            var fresh = new ScaleTransform(1, 1);
            element.RenderTransform = fresh;
            return fresh;
        }

        private static bool AnimateScaleY(ScaleTransform scale, double fromY, double toY,
                                          int milliseconds, EasingMode easing, Action? onCompleted)
        {
            try
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);   // 清掉上一次的终值

                var animation = new DoubleAnimation(fromY, toY, TimeSpan.FromMilliseconds(milliseconds))
                {
                    EasingFunction = new CubicEase { EasingMode = easing }
                };
                if (onCompleted != null) animation.Completed += (_, __) => onCompleted();

                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
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

        /// <summary>视觉树里**第一个**类型为 T 的后代（不限名字）。</summary>
        private static T? FirstVisualDescendant<T>(DependencyObject parent) where T : class
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;

                var found = FirstVisualDescendant<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 列表变化/排序后：**最后一张卡不留下边距**（滚到底时它的底边正好贴视口底）。
        /// <para>用户诉求：「滚到底后底部空白留太多了，应该是刚好最后一个卡片最下面」。
        /// 卡片间距 8 来自 <c>ModuleFunctionCardStyle</c> 的 <c>Margin="0,0,0,8"</c> ——
        /// 最后一张卡也带着这 8，滚到底时视口底与卡片底之间就空出一截。
        /// 这里把最后一张卡的下边距**本地覆盖成 0**（本地值优先于样式 setter），
        /// 内容总高随之少 8 ⇒ 滚到底时最后一张卡的底边刚好落在视口底上。</para>
        /// ★ 排序 <c>Move</c> 之后容器复用、本地值会残留 ⇒ 每次列表变化都全量重算一遍。
        /// </summary>
        private void RefreshMacroCardSpacing()
        {
            var panel = FirstVisualDescendant<Panel>(MacroList);
            if (panel == null) return;

            int last = panel.Children.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                if (panel.Children[i] is not FrameworkElement fe) continue;
                double bottom = i == last ? 0 : 8;
                if (Math.Abs(fe.Margin.Bottom - bottom) > 0.01)
                    fe.Margin = new Thickness(0, 0, 0, bottom);
            }
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

        /// <summary>正在拖的卡片数据（<see cref="MacroItem"/> 或 <see cref="MacroFolder"/>）。</summary>
        private object? _sortSource;

        /// <summary>这次拖的是文件夹列表（顶层）还是宏列表（分组内）。</summary>
        private bool _sortIsFolder;

        /// <summary>拖动时对应的列表控件（v2：只有宏列表可拖）。</summary>
        private ItemsControl SortHost => MacroList;

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

        /// <summary>
        /// 滚轮：**一格 = 一张宏卡片**（和 UID 账号列表同一个手感）。
        /// <para>
        /// 列表用的是宿主的 <c>CustomScrollViewer</c>（物理滚动），系统默认一格只走 48px
        /// （WheelScrollLines=3 行 × 16px），比一张卡片矮一截 —— 滚起来半张半张的，
        /// 和账号列表「一格一张」对不上。这里和 UID 那边一样接过来自己按卡片高度步进。
        /// </para>
        /// <para>★ 卡片实际高度直接量已实例化的第一张卡片兜不准（空白列表没有容器），
        ///       所以用卡片样式里的 70 + 间距 8 = 78，与 UID 的兜底值一致。</para>
        /// </summary>
        private void MacroScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            e.Handled = true;

            // 一格一张卡片：卡片 70 + 卡片间距 8（ModuleFunctionCardStyle 的 Margin 下 8）
            double step = 78;

            sv.ScrollToVerticalOffset(sv.VerticalOffset + (e.Delta > 0 ? -step : step));
        }

        private void SortHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_sortDragging || _sortDropping) return;      // 吸附动画还没走完，别抢
            if (sender is not FrameworkElement grab) return;

            // ★ 同一个手柄样式两个列表都在用：按 DataContext 判断拖的是文件夹还是宏
            if (grab.DataContext is MacroFolder folder)
            {
                _sortIsFolder = true;
                _sortSource = folder;
            }
            else if (grab.DataContext is MacroItem item)
            {
                _sortIsFolder = false;
                _sortSource = item;
            }
            else
            {
                return;
            }

            // ★ 拦在 Button 处理之前：否则 Button 会自己抢鼠标捕获 / 变成按下态
            e.Handled = true;

            var cards = CollectCardContainers();
            int from = cards.FindIndex(c => ReferenceEquals(c.DataContext, _sortSource));
            if (from < 0) return;

            _sortFrom = from;
            _sortTarget = from;

            // 分区：置顶的只能在置顶区里拖，非置顶的只能在非置顶区里拖（和 UID 一样不串位）
            // ★ 文件夹和宏一样有置顶 ⇒ 两边都按置顶数分区
            int pinnedCount = _sortIsFolder
                ? _folders.Count(x => x.IsPinned)
                : _items.Count(x => x.IsPinned);
            bool isPinned = _sortIsFolder
                ? ((MacroFolder)_sortSource!).IsPinned
                : ((MacroItem)_sortSource!).IsPinned;
            if (isPinned)
            {
                _sortMin = 0;
                _sortMax = Math.Max(0, pinnedCount - 1);
            }
            else
            {
                _sortMin = pinnedCount;
                _sortMax = cards.Count - 1;
            }

            _sortScroller = FindAncestor<ScrollViewer>(SortHost);
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
                tops.Add(card.TransformToAncestor(SortHost).Transform(new Point(0, 0)).Y);

            _sortPitch = tops.Count >= 2 ? tops[1] - tops[0] : 0;
            if (_sortPitch <= 0) _sortPitch = _sortCards[0].ActualHeight;
            if (_sortPitch <= 0) return;

            _sortSlotTop = tops[0];
            _sortSlotBottom = tops[tops.Count - 1];

            double mouseY = e.GetPosition(SortHost).Y;
            _sortStartMouseY = mouseY;
            _sortGrabOffset = mouseY - tops[from];

            _sortDragging = true;
            _sortDropping = false;
            _sortDragToken++;
            Panel.SetZIndex(_sortCards[from], 1);
            _sortCards[from].Opacity = 0.85;

            _sortPrevCursor = SortHost.Cursor;
            SortHost.Cursor = Cursors.SizeAll;

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
            if (!_sortDragging || _sortDropping || _sortSource == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { FinishSortDrag(); return; }

            MaybeAutoScroll(e);

            double mouseY = e.GetPosition(SortHost).Y;
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

            double currentTop = card.TransformToAncestor(SortHost).Transform(new Point(0, 0)).Y;

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

            SortHost.Cursor = _sortPrevCursor;
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
            _sortSource = null;

            // ---------- 文件夹（顶层）----------
            if (_sortIsFolder)
            {
                if (from < 0 || from >= _folders.Count) return;
                if (to == from) return;

                to = Math.Clamp(to, 0, _folders.Count - 1);

                var movedFolder = _folders[from];
                _folders.Move(from, to);           // ★ 就地 Move：容器不重建
                MacroSettingsStore.MoveFolder(movedFolder.Id, to);
                return;
            }

            // ---------- 宏 ----------
            if (from < 0 || from >= _items.Count) return;
            if (to == from) return;

            to = Math.Clamp(to, 0, _items.Count - 1);

            var moved = _items[from];
            _items.Move(from, to);                 // ★ 就地 Move：容器不重建
            MacroSettingsStore.Move(moved.Id, to);

            // 排序换了「最后一张」的人选 ⇒ 重算底边距（滚到底贴视口底）
            RefreshMacroCardSpacing();

            // ★★ WPF 对 ObservableCollection.Move 的处理是「移除 + 插入」⇒ 这一行的容器被重建，
            //   CheckBox 的 IsChecked 重新求值，宿主的胶囊样式会把 **0.4s 滑块动画重播一次**
            //   （观感像"整页重新加载"）。这里等布局建好容器后，直接把滑块摆到终点。
            SnapToggleThumbnails();

            // ★ 这里**不重建列表**：松手时卡片已经停在目标槽上（邻卡让位 + 位移已复位），
            //   数据 Move 之后布局正好对应，所以看不到任何跳动。重建反而会闪一下。
        }

        /// <summary>
        /// 把整个列表里所有胶囊开关的滑块直接摆到终点（跳过排序后的历史动画回放）。
        /// <para>★ 终点 X = 20 来自宿主 <c>CardStyles.xaml</c> 的 <c>ModuleToggleSwitchStyle</c>
        ///   关键帧末帧（0.2s → 22，0.4s → 20）。宿主改那个样式时这里要跟着改。</para>
        /// </summary>
        private void SnapToggleThumbnails()
        {
            // ★ 连压 3 轮：Move 引起的容器重建不一定在同一轮布局完成，只压一次可能遍历到旧容器。
            //   LayoutUpdated 在「布局末尾、渲染之前」触发 ⇒ 压制后第一帧就是终态。
            int rounds = 3;

            EventHandler? handler = null;
            handler = (_, _) =>
            {
                SnapTogglesIn(MacroList);

                if (--rounds <= 0) MacroList.LayoutUpdated -= handler;
            };

            MacroList.LayoutUpdated += handler;
        }

        private static void SnapTogglesIn(DependencyObject root)
        {
            if (root is CheckBox cb) SnapToggle(cb);

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                SnapTogglesIn(VisualTreeHelper.GetChild(root, i));
        }

        private static void SnapToggle(CheckBox checkBox)
        {
            try
            {
                TranslateTransform? thumb = FindThumbTransform(checkBox);
                if (thumb == null) return;

                thumb.BeginAnimation(TranslateTransform.XProperty, null);
                thumb.X = checkBox.IsChecked == true ? 20 : 0;
            }
            catch
            {
                // 模板结构变了 → 最多多播一次动画，不影响功能
            }
        }

        /// <summary>
        /// 在模板可视树里找胶囊滑块用的 <see cref="TranslateTransform"/>。
        /// <para>
        /// ★★ <b>不能用</b> <c>Template.FindName("ThumbTranslate", cb)</c>：那个名字挂在一个
        ///   <see cref="TranslateTransform"/>（Freezable）上，<c>FindName</c> 只认 FrameworkElement，
        ///   永远返回 null（上一版压制没生效就是这个原因）。
        /// </para>
        /// <para>★ 改为按结构找：<c>ThumbBorder</c> 的 <c>RenderTransform</c> 是 TransformGroup，
        ///   里面第二项就是滑块位移；名字对不上时退回「任意带 TransformGroup 的元素」。</para>
        /// </summary>
        private static TranslateTransform? FindThumbTransform(DependencyObject root)
        {
            TranslateTransform? fallback = null;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is FrameworkElement fe && fe.RenderTransform is TransformGroup group)
                {
                    TranslateTransform? tt = null;
                    foreach (object? item in group.Children)
                    {
                        if (item is TranslateTransform found) { tt = found; break; }
                    }

                    if (tt != null)
                    {
                        if (fe.Name == "ThumbBorder") return tt;
                        fallback ??= tt;
                    }
                }

                TranslateTransform? deep = FindThumbTransform(child);
                if (deep != null) return deep;
            }

            return fallback;
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

            var generator = SortHost.ItemContainerGenerator;
            for (int i = 0; i < SortHost.Items.Count; i++)
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
