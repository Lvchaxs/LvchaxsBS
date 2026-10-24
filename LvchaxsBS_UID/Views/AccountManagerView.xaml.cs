using LvchaxsBS_UID.Models;
using LvchaxsBS_UID.Utils.File;
using LvchaxsBS_UID.Utils.GameWindow;
using LvchaxsBS_UID.Services.DataServices;
using LvchaxsBS_UID.Services.LoginServices;
using LvchaxsBS_UID.Services.UI;
using LvchaxsBS_UID.Utils.Clipboard;
using LvchaxsBS_UID.Utils.UI;
using LvchaxsBS_UID.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LvchaxsBS_UID.Views
{
    /// <summary>
    /// 账号管理界面。
    ///
    /// <para>
    /// 以前是一个独立的 <c>Window</c>，现在是嵌入宿主 LvchaxsBS 内容区显示的
    /// <see cref="UserControl"/>（宿主顶部「UID」按钮切换过来）。
    /// 因此这里不再有 Title / 尺寸 / 居中 / 拖拽 / 关闭 等窗口行为，
    /// 需要窗口时一律用 <see cref="Window.GetWindow"/> 取外层宿主窗口。
    /// </para>
    /// </summary>
    public partial class AccountManagerView : UserControl, INotifyPropertyChanged
    {
        #region 字段和属性
        private ObservableCollection<Account> _accounts = new();

        /// <summary>
        /// 分组列表的 ItemsSource。★ 始终复用**同一个实例** ——
        /// 置顶/取消置顶时才能像账号那样**就地 Move**（容器不重建 → FLIP 位置准确、手感一致）。
        /// </summary>
        private readonly ObservableCollection<GroupDisplayItem> _groupDisplayItems = new();
        private List<Account> _allAccounts = new();
        private List<AccountGroup> _allGroups = new();
        private List<int> _selectedAccountIds = new();
        private int _selectedGroupId = 0;

        /// <summary>
        /// 是否处于「移入账号」模式。
        /// 进入后直接复用账号列表页面：顶部信息改成移入统计，底部只留「移入 / 取消」，
        /// 每张卡片的登录按钮位置换成选择框，点卡片任意处即选中。
        /// </summary>
        private bool _isMoveMode = false;

        /// <summary>移入模式的目标分组（退出时回到这个分组内）。</summary>
        private int _moveTargetGroupId = 0;
        // ★ 旧的 _pinnedAccountOrder / _pinnedGroupOrder + pinned_orders.json 已废弃：
        //   排序统一走 Account.Order / AccountGroup.Order（自由排序），置顶顺序只是 Order 的一部分。

        // 服务类
        private SortingService _sortingService;
        private ViewStateManager _viewStateManager;

        // 管理器
        private ToastManager _toastManager;
        private ModalManager _modalManager;

        private DisplayMode _currentDisplayMode = DisplayMode.AccountList;
        private Window _currentModalWindow;
        private bool _isModalWindowOpen = false;

        // 登录窗口（仅保留界面，不执行登录）
        private LoginConfirmWindow _loginConfirmWindow;
        private List<Account> _currentLoginAccountList = new();
        private int _currentLoginAccountIndex = 0;

        /// <summary>是否已经做过首次初始化（视图是单例，切走再切回来不应重复初始化）。</summary>
        private bool _initialized;

        public static readonly DependencyProperty SelectedGroupIdProperty =
            DependencyProperty.Register("SelectedGroupId", typeof(int), typeof(AccountManagerView),
                new PropertyMetadata(0, OnSelectedGroupIdChanged));

        public int SelectedGroupId
        {
            get => (int)GetValue(SelectedGroupIdProperty);
            set => SetValue(SelectedGroupIdProperty, value);
        }


        public bool IsInGroupMode => _selectedGroupId > 0;

        /// <summary>是否处于「移入账号」模式（卡片模板据此切换按钮/选择框）。</summary>
        public bool IsMoveMode => _isMoveMode;

        public event PropertyChangedEventHandler PropertyChanged;
        #endregion

        #region 构造函数和初始化
        public AccountManagerView()
        {
            InitializeComponent();

            // 初始化服务
            _sortingService = new SortingService();
            // 提示条统一走宿主标题栏（见 ToastManager 注释），模块不再自绘
            _toastManager = new ToastManager();
            _modalManager = new ModalManager(modalOverlay);

            // 初始化界面状态管理器
            InitializeViewStateManager();

            // ★ 这里**不**加载数据：`Loaded` 里的 `ReloadData()` 会加载。
            //   原来构造函数和 Loaded 各加载一次 = 首屏读两遍盘 + 建两遍列表，
            //   是"首次进界面卡顿"的一半来源。
            this.Loaded += AccountManagerView_Loaded;
            // 注意：**不**在 Unloaded 时做清理。
            // 本视图是单例、切走只是从视觉树上摘下来，切回来还要继续用；
            // 若在这里调用 ViewStateManager.Cleanup() 会解绑「分组管理」按钮的事件，
            // 用户切走再切回来按钮就失效了。视图随宿主进程一起结束，无需手动清理。
        }

        /// <summary>
        /// 用户在帮助窗口里点了『关闭工具』。
        /// 本视图现在嵌在宿主内容区里，"关闭工具"没有窗口可关，
        /// 改为抛事件，由宿主决定切回它自己的页面（首页）。
        /// </summary>
        public event EventHandler? CloseToolRequested;

        private void InitializeViewStateManager()
        {
            _viewStateManager = new ViewStateManager
            {
                AddAccountButton = addAccountButton,
                BackToAccountListButton = backToAccountListButton,
                AddGroupButton = addGroupButton,
                AddAccountsToGroupButton = addAccountsToGroupButton,
                GroupManagementButton = groupManagementButton,
                MoveConfirmButton = moveConfirmButton,
                MoveCancelButton = moveCancelButton,
                HomeViewGroupManagementClick = GroupManagementButton_Click,
                GroupDetailReturnToGroupListClick = ReturnToGroupList_Click,
                CreateGroupButtonClick = CreateGroupButton_Click,
                AddAccountsToGroupClick = OnAddGroupButtonClick
            };

            // 初始化为主页
            _viewStateManager.SwitchToView(ViewStateManager.AppView.Home);
        }

        private void InitializeData()
        {
            LoadAllData();
        }

        private void LoadAllData()
        {
            var sw = Stopwatch.StartNew();

            // 优先用「预热缓存」：宿主打开 UID 界面前已在后台线程读好了一份
            // （读盘 + 解密是好几秒的大头），这里直接取用即可，界面几乎瞬间出来。
            // 没预热过时（例如直接创建视图、或预热失败）照旧自己读一遍。
            bool preheated = Services.DataServices.DataPreload.TryTake(out var preAccounts, out var preGroups);
            if (preheated)
            {
                _allAccounts = preAccounts ?? new List<Account>();
                _allGroups = preGroups ?? new List<AccountGroup>();
            }
            else
            {
                _allAccounts = AccountDataService.LoadAccounts();
                _allGroups = GroupDataService.LoadGroups();
            }
            long t1 = sw.ElapsedMilliseconds;
            UpdateAccountsList();
            long t2 = sw.ElapsedMilliseconds;
            UpdateWatermarkVisibility();
            UpdateGroupInfoDisplay();
            // 首屏排查用：取数据 / 建列表 各花了多久（取数据走预热时≈0）
            SortLog($"LoadAllData 取数据{t1}ms({(preheated ? "预热命中" : "读盘解密")}) " +
                    $"建列表{t2 - t1}ms 合计{sw.ElapsedMilliseconds}ms " +
                    $"账号数={_allAccounts.Count} 分组数={_allGroups.Count}");

            // 注：分组卡片的提前生成不在这里排 —— 构造阶段界面还没显示，`UpdateLayout()` 对
            // 未挂到 PresentationSource 的元素无效（造不出容器）。真正执行见
            // `AccountManagerView_Loaded`（界面显示之后）。
        }

        /// <summary>
        /// 视图被重新放进宿主内容区时触发。
        ///
        /// <para>
        /// ⚠️ 本视图是<b>单例</b>（宿主点「UID」按钮时复用的是同一个实例），
        /// 切到别的主程序页面再切回来会再次触发 Loaded。
        /// 所以数据与初始页面状态只在这里初始化一次，
        /// 否则会把用户浏览到一半的分组 / 列表位置重置掉（用户要求"记住上次的位置"）。
        /// </para>
        /// </summary>
        private async void AccountManagerView_Loaded(object sender, RoutedEventArgs e)
        {

            if (_initialized) return;
            _initialized = true;

            ReloadData();

            // ★ 分组卡片**提前造好**：界面已经显示出来了，这时 `UpdateLayout()` 才有效。
            //   （⛔ 别用 ContextIdle：宿主界面一直有渲染活动，那个优先级实测根本轮不到 ——
            //     日志里「已预热=False」就是这么来的，首次切分组仍是一个容器都没有。）
            Dispatcher.BeginInvoke(new Action(PrewarmGroupCards),
                System.Windows.Threading.DispatcherPriority.Background);

            // 等待UI完全加载
            await Task.Delay(300);

            // 检查是否需要显示帮助窗口
            if (ShouldShowHelpWindowOnStartup())
            {
                Dispatcher.Invoke(() =>
                {
                    ShowHelpWindow();
                });
            }
        }

        private bool ShouldShowHelpWindowOnStartup()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string settingsPath = System.IO.Path.Combine(appDataPath, "LvchaxsBS_UID", "help_window_settings.json");

                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    if (bool.TryParse(json, out bool dontShow))
                    {
                        return !dontShow;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检查帮助窗口设置失败: {ex.Message}");
            }

            return true;
        }

        private void ReloadData()
        {
            LoadAllData();

            if (_allAccounts.Count == 0)
            {
                SwitchToDisplayMode(DisplayMode.NoData);
            }
            else
            {
                SwitchToDisplayMode(DisplayMode.AccountList);
                Dispatcher.BeginInvoke(() =>
                {
                }, DispatcherPriority.Render);
            }
        }
        #endregion

        #region 通用辅助方法
        private void UpdateWatermarkVisibility()
        {
            watermarkText.Visibility = string.IsNullOrWhiteSpace(searchTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && (childName == null || (element is FrameworkElement fe && fe.Name == childName)))
                    return element;

                var result = FindVisualChild<T>(child, childName);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>视觉树里**所有**类型为 T 的后代（深度优先，按出现顺序）。</summary>
        private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : class
        {
            if (parent == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var x in VisualDescendants<T>(child)) yield return x;
            }
        }

        /// <summary>
        /// 账号列表变化/排序后：**最后一行不留下边距**（滚到底时最后一张卡底边贴视口底）。
        /// <para>用户诉求：「滚到底后底部空白留太多了，应该是刚好最后一个卡片最下面」。
        /// 卡片间距 8 来自 <c>DataGridRowStyle</c> 的 <c>Margin="0,0,0,8"</c>（在**行**上，
        /// 不在卡片上）—— 最后一行也带着这 8 ⇒ 滚到底时视口底与卡片底之间空出一截。
        /// 这里把最后一行的下边距本地覆盖成 0，行总高少 8 ⇒ 刚好贴平。</para>
        /// ★ 行容器会被虚拟化回收/复用，本地值会残留 ⇒ 每次列表变化都全量重算；
        ///   滚动时新实现的行也要重算（见 <see cref="HookAccountScrollSpacing"/>）。
        /// </summary>
        private void RefreshAccountRowSpacing()
        {
            if (accountsDataGrid == null || _accounts == null || _accounts.Count == 0) return;

            var sv = FindVisualChild<ScrollViewer>(accountsDataGrid);

            // ★★ 2026-10-21：margin 已改由 LastItemSpacingConverter **绑定**自动算
            //   （见 AccountManagerView.xaml 的 DataGridRowStyle）—— 行一生成就带对的值，
            //   首进 / 滚动中新建的行 / 排序 Move / 过滤全都能跟上。
            //   ⛔ 这里**绝不能再写 row.Margin**：本地值优先级高于样式绑定，
            //      一写就永久覆盖，之后集合变化再也不更新（那就是「打开一次分组才好」的根源）。
            //   只剩一件兜底：内容高度变化时若正停在底部，把 Offset 重钉到新底部。
            if (sv != null && sv.VerticalOffset >= sv.ScrollableHeight - 0.5)
                sv.ScrollToVerticalOffset(sv.ScrollableHeight);
        }

        /// <summary>
        /// 给账号列表内部的 ScrollViewer 挂一次 <see cref="ScrollViewer.ScrollChanged"/>：
        /// 滚到底时最后一行往往是**刚实现**的（行虚拟化），样式给的 8 还没被覆盖过。
        /// 滚动每帧重算一次已实现行（只有值真变时才写，通常 0 次写入）。
        /// </summary>
        private void HookAccountScrollSpacing()
        {
            var sv = FindVisualChild<ScrollViewer>(accountsDataGrid);
            if (sv == null) return;
            sv.ScrollChanged -= AccountGrid_ScrollChanged;
            sv.ScrollChanged += AccountGrid_ScrollChanged;
        }

        private void AccountGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) return;
            RefreshAccountRowSpacing();
        }

        /// <summary>
        /// 排一次「账号列表最后一行贴视口底」刷新，**看到行容器才罢手**。
        /// ★★ 2026-10-21：以前直接 <c>BeginInvoke(Loaded)</c> —— 那个优先级跑在布局之前，
        ///   DataGrid 刚 Visible / ItemsSource 刚重置时**一行都还没生成**（行容器要等
        ///   Measure/Arrange），于是 <c>VisualDescendants&lt;DataGridRow&gt;</c> 一条都找不到 ⇒ 空转。
        ///   用户实测现象「首次进账号列表不生效，非得进过一次分组后才好」就是这么来的：
        ///   进过分组后行容器已存在，再回来才刷得上。
        ///   现在改成按 Render 优先级重排，直到真的看到 DataGridRow（或试满 12 次放弃，
        ///   12 次足以跨过多轮布局；空列表在 RefreshAccountRowSpacing 里自己有 guard）。
        /// </summary>
        private void ScheduleAccountSpacingRefresh()
        {
            if (accountsDataGrid == null || _accounts == null || _accounts.Count == 0) return;

            int tries = 0;
            void Attempt()
            {
                if (accountsDataGrid == null || _accounts == null || _accounts.Count == 0) return;
                if (VisualDescendants<DataGridRow>(accountsDataGrid).Any() || ++tries >= 12)
                {
                    RefreshAccountRowSpacing();
                    return;
                }
                Dispatcher.BeginInvoke(new Action(Attempt), DispatcherPriority.Render);
            }
            Attempt();
        }

        /// <summary>分组列表同上：**最后一张分组卡**不留下边距（滚到底贴视口底）。</summary>
        private void RefreshGroupCardSpacing()
        {
            if (_groupDisplayItems.Count == 0) return;

            var list = FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl");
            var panel = list == null ? null : FindVisualChild<Panel>(list);
            if (panel == null) return;

            // 分组列表的滚动容器是 ItemsControl 的**祖先**（ThinScrollViewer）
            var sv = list == null ? null : FindVisualParent<ScrollViewer>(list);

            // ★★ 2026-10-21：margin 已由 ItemContainerStyle 上的 LastItemSpacingConverter 绑定自动算，
            //   这里不再写容器 Margin（本地值会覆盖绑定）。只剩 Offset 重钉兜底。
            if (sv != null && sv.VerticalOffset >= sv.ScrollableHeight - 0.5)
                sv.ScrollToVerticalOffset(sv.ScrollableHeight);
        }

        /// <summary>
        /// 分组列表版的「等面板建好再刷」调度（理由同 <see cref="ScheduleAccountSpacingRefresh"/>）：
        /// 分组卡容器同样要等布局才生成，同步调用时面板可能还是空的。
        /// </summary>
        private void ScheduleGroupCardSpacingRefresh()
        {
            if (_groupDisplayItems.Count == 0) return;

            int tries = 0;
            void Attempt()
            {
                var list = FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl");
                var panel = list == null ? null : FindVisualChild<Panel>(list);
                if ((panel != null && panel.Children.Count > 0) || ++tries >= 12)
                {
                    RefreshGroupCardSpacing();
                    return;
                }
                Dispatcher.BeginInvoke(new Action(Attempt), DispatcherPriority.Render);
            }
            Attempt();
        }

        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T))
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }
        #endregion

        #region 账号列表管理
        #region 置顶 / 置底
        /// <summary>置顶标识层与竖条的 x:Name（写在卡片模板里，靠视觉树按名字找）。</summary>
        private const string PinHighlightLayerName = "PinHighlightLayer";
        private const string PinBarName = "PinBar";

        /// <summary>竖条动画还没落地时忽略再次点击，避免连点把置顶/置底切错。</summary>
        private bool _starToggleAnimating;

        /// <summary>FLIP 平滑重排进行中（照前端 `reordering`）：期间忽略新的置顶/置底点击。</summary>
        private bool _reordering;

        /// <summary>
        /// 置顶/置底切换的「表演 + 落地」（只作用于用户点的那一张卡）。
        /// ★★ 顺序：**动画先播完，卡片再挪位置**（生效与取消都一样）。
        ///   · 生效（showing=true）：先「预告」—— 把竖条收成 0 高，再翻一下 <see cref="Account.ShowStar"/>
        ///     让它显出来（此刻是收起的，不会先在原位闪一帧全高）；接着播展开；
        ///     **播完才 commit**（这时才真正改置顶/置底数据并重排）。
        ///   · 取消（showing=false）：先播缩回（此时数据还没变、竖条还看得见），播完再 commit ——
        ///     全程不动 Visibility，也就不会被基础值提前收起（那正是「瞬失」的原因）。
        /// ★ 为什么不用 XAML 触发器：列表刷新会重建行容器，取消时新容器一建出来就是不显示，
        ///   触发器的 ExitActions 没有「true→false」这个过渡可依赖，缩回动画根本没机会播。
        /// ★ 所有动画路径都包了 try/catch：宿主没有全局异常处理，这里抛异常会直接崩掉整个程序。
        /// ★ 账号列表和分组列表**共用这一套** —— 两者只差「怎么拿到卡片容器」和「翻哪个 ShowStar」，
        ///   由调用方用小委托传进来（<paramref name="findContainer"/> / <paramref name="showStar"/>）。
        /// </summary>
        private void ApplyStarToggle(bool showing, Action commit,
            Func<DependencyObject> findContainer, Action forceLayout, Action<bool> showStar)
        {
            if (_starToggleAnimating || _reordering) return;   // 重排/动画期间忽略新点击（照前端）

            try { forceLayout?.Invoke(); }
            catch (Exception ex) { Debug.WriteLine("[UID/置顶] 催布局失败：" + ex.Message); }
            // ★ 先把布局催出来：上一次 commit 刚重排过时容器可能还没生成，
            //   `ContainerFromItem` 会返回 null → 动画静默不播（看着就是"没动画"）。

            bool committed = false;
            Action commitOnce = () =>
            {
                if (committed) return;      // 幂等：兜底与 Completed 都调它也只会落地一次
                committed = true;
                _starToggleAnimating = false;
                showStar?.Invoke(showing);   // 兜底：动画没播成时也要把 ShowStar 归位
                commit?.Invoke();
            };

            try
            {
                var container = findContainer?.Invoke();

                if (showing)
                {
                    PreviewPinHighlight(container, showStar);      // 预告：竖条先收成 0 高（还看不出变化）

                    if (TryPlayPinHighlight(container, true, 240, EasingMode.EaseOut, commitOnce, showStar))
                    {
                        _starToggleAnimating = true;                  // 落地被延后到动画结束
                        _ = CommitAfterDelayAsync(commitOnce);        // 兜底：Completed 没触发也要落地
                    }
                    else
                    {
                        commitOnce();                                 // 没容器/播不了，直接落地
                    }
                }
                else if (TryPlayPinHighlight(container, false, 180, EasingMode.EaseIn, commitOnce, showStar))
                {
                    _starToggleAnimating = true;                    // 落地被延后到动画结束
                    _ = CommitAfterDelayAsync(commitOnce);           // 兜底：Completed 没触发也要落地（否则点击会被永久忽略）
                }
                else
                {
                    commitOnce();                                    // 没容器/播不了，直接落地
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[UID/置顶] 切换失败：" + ex.Message);
                commitOnce();       // 动画怎么出问题都不能把数据落地拖着
            }
        }

        /// <summary>账号卡片：点头像 = 置顶/取消置顶（竖条在行模板里）。</summary>
        private void ApplyStarToggle(int accountId, bool showing, Action commit)
            => ApplyStarToggle(showing, commit,
                () => FindRowForAccount(accountId),
                () => accountsDataGrid?.UpdateLayout(),
                on =>
                {
                    var item = _accounts.FirstOrDefault(a => a.Id == accountId);
                    if (item != null) item.ShowStar = on;
                });

        /// <summary>分组卡片：点文件夹头像 = 置顶/取消置顶（竖条在分组卡片模板里，元素名和账号卡复用）。</summary>
        private void ApplyGroupStarToggle(int groupId, bool showing, Action commit)
            => ApplyStarToggle(showing, commit,
                () => FindGroupContainer(groupId),
                () => GetGroupsItemsControl()?.UpdateLayout(),
                on =>
                {
                    var item = GetGroupDisplayItems()?.FirstOrDefault(g => g.Id == groupId);
                    if (item != null) item.ShowStar = on;
                });

        /// <summary>兜底：等一会儿再尝试落地（幂等，已落地就什么都不做）。</summary>
        private static async Task CommitAfterDelayAsync(Action commitOnce)
        {
            await Task.Delay(500);
            commitOnce();
        }

        /// <summary>
        /// 「竖条要出现」的视觉预告 —— 让展开动画能在**数据切换之前**先播完。
        /// 步骤：① 把竖条收成 0 高（此刻整层还是折叠的，看不出变化）；
        ///       ② 再翻一下 ShowStar 让它显出来 —— 它一出现就是收起状态，不会先在原位闪一帧全高。
        /// ★ 只翻 ShowStar 这个**纯界面标志**（不落盘、不参与排序）→ 列表顺序不变。
        /// ★ 卡片不在可视区（容器为 null）就直接返回：不预告，交给调用方立刻 commit（最终状态一样正确）。
        /// </summary>
        private void PreviewPinHighlight(DependencyObject? container, Action<bool> showStar)
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
                showStar?.Invoke(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[UID/置顶] 预置竖条失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 对某张卡片播「置顶竖条」动画：竖条从中心向上下展开 / 向中心缩回。
        /// <para>返回是否真的播了（没播就让调用方立刻落地数据）。</para>
        /// </summary>
        private bool TryPlayPinHighlight(DependencyObject? container, bool showing, int milliseconds,
                                         EasingMode easing, Action onCompleted,
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
                // 看上去就是"蓝杠从中间往上下长 + 名称慢慢染蓝"同时发生。
                // （账号卡和分组卡各有一个名称 TextBlock，名字见 PinTitleNames。）
                TextBlock? title = null;
                try
                {
                    title = FindPinTitle(container);
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
                        //    画刷是复用的：置顶时建的、取消置底时还拿着它，本地色仍是蓝色
                        //    ⇒ 不手动改的话，动画一结束名称就"立马又变蓝"。
                        // ★★ 交还本地色之后还要 **ClearValue 把 Foreground 还给样式**：
                        //    私有画刷是本地值，优先级压过 Style 里的 DynamicResource
                        //    ⇒ 不还的话切主题时名称不跟着变色（用户实测反馈）。
                        //    终值和样式值一致，所以 ClearValue 那一帧不会有色变。
                        // ★★ 交还之前必须先把 ShowStar 归位（取消置顶/置底 → false）：
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
                catch (Exception ex)
                {
                    Debug.WriteLine("[UID/置顶] 播名称颜色失败：" + ex.Message);
                    // 动画没播成：也把本地画刷交还样式（显示状态由 ShowStar 触发器保证是对的）
                    title?.ClearValue(TextBlock.ForegroundProperty);
                }

                return AnimateScaleY(scale, showing ? 0 : 1, showing ? 1 : 0,
                                     milliseconds, easing, onCompleted);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[UID/置顶] 播竖条动画失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>账号卡 / 分组卡里名称 TextBlock 的 x:Name（颜色过渡要找它）。</summary>
        private static readonly string[] PinTitleNames = { "AccountNameText", "GroupNameText" };

        /// <summary>
        /// 名称 TextBlock 换了数据源（容器被回收复用）时，清掉上一轮动画留下的本地画梳。
        /// <para>
        /// ★★ 置顶/取消置顶、置底/取消置底都会重排列表，行的容器会被回收再套到另一张卡上。
        ///    颜色动画用的是**逐实例画梳（本地值）**，它会跟着容器一起残留
        ///    ⇒ 不清的话，复用出来的卡片名称会顶着上一个卡片的颜色。
        /// </para>
        /// <para>清掉后由 Style 的 Setter / DataTrigger 按新的 ShowStar 给出正确颜色。</para>
        /// </summary>
        private void PinTitle_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBlock tb)
                tb.ClearValue(TextBlock.ForegroundProperty);
        }

        /// <summary>在卡片容器里找名称 TextBlock（账号卡一个名、分组卡另一个名）。</summary>
        private TextBlock? FindPinTitle(DependencyObject container)
        {
            foreach (string name in PinTitleNames)
            {
                var hit = FindVisualChild<TextBlock>(container, name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>名称的两种颜色（跟主题走；取不到时用浅色主题的兜底值）。</summary>
        private static Color NameNormalColor
            => ThemeColor("TextPrimaryBrush", Color.FromRgb(0x00, 0x00, 0x00));

        private static Color NamePinnedColor
            => ThemeColor("PinNameBrush", Color.FromRgb(0x93, 0xC5, 0xFD));

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
        ///   会在模板密封时被 **Freeze**（Freezable 冻结），冻结后既不能改它的值、也不能 BeginAnimation，
        ///   而异常又被 try/catch 吞掉 → 表面上就是「没动画」。
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
                if (onCompleted != null)
                    animation.Completed += (_, __) => onCompleted();

                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[UID/置顶] 缩放动画失败：" + ex.Message);
                return false;
            }
        }

        private DataGridRow FindRowForAccount(int accountId)
        {
            var item = _accounts.FirstOrDefault(a => a.Id == accountId);
            return item == null ? null : accountsDataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        }

        /// <summary>分组卡片对应的容器（ItemsControl 的 ContentPresenter）；不在可视区时为 null。</summary>
        private DependencyObject FindGroupContainer(int groupId)
        {
            var itemsControl = GetGroupsItemsControl();
            if (itemsControl == null) return null;

            var item = itemsControl.Items.OfType<GroupDisplayItem>().FirstOrDefault(g => g.Id == groupId);
            return item == null ? null : itemsControl.ItemContainerGenerator.ContainerFromItem(item);
        }

        /// <summary>分组列表那个 ItemsControl（分组管理页里）。</summary>
        private ItemsControl GetGroupsItemsControl()
            => FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl");

        /// <summary>分组列表当前的显示对象（每个分组一张卡片）。</summary>
        private IEnumerable<GroupDisplayItem> GetGroupDisplayItems()
            => GetGroupsItemsControl()?.Items?.OfType<GroupDisplayItem>();
        #endregion

        private void UpdateAccountsList(string searchTerm = "")
        {
            // 已去掉表头与点列排序：顺序由 **Account.Order（自由排序）** 决定 ——
            //   主列表 —— 置顶账号在最前（按 Order），其余按 Order，两区互不跨界；
            //   分组内 —— 未置底在前、已置底沉底，各自按 Order。
            // 拖动卡片右侧的排序手柄即可自由调整（照前端模拟 D:/C#/LvchaxsBS_UID_WebSim/index.html）。
            // 这两条顺序由 SortingService 的 GetMainDisplayOrder / GetGroupDisplayOrder 给出。
            List<Account> filteredAccounts = GetFilteredAccounts(searchTerm);

            // ★★★ 列表内容和顺序都没变就**不要重建**（连 `ItemsSource` 都别碰）：
            //   原先是 `Clear()` + 逐条 `Add()` —— 那会给 DataGrid 发 N 次集合变更通知，
            //   逐个生成 / 失效行容器，51 条要卡几百毫秒（"切视图 / 返回账号列表"就卡在这）。
            //   而"返回账号列表""切回 UID"绝大多数时候列表根本没变 → 直接跳过，瞬间完成。
            if (_accounts.Count == filteredAccounts.Count)
            {
                bool same = true;
                for (int i = 0; i < filteredAccounts.Count; i++)
                {
                    if (!ReferenceEquals(_accounts[i], filteredAccounts[i])) { same = false; break; }
                }
                if (same) { UpdateUI(); return; }
            }

            // ★ 真的变了也**一次性换掉整个集合**：DataGrid 只收到一次 Reset，比 Clear+Add 快得多。
            //   （别的代码/拖动靠 `_accounts` 就地 Move 换位，所以这里重建实例是安全的。）
            // ★★ 重建之前先把「拖动预热」还回去（行虚拟化恢复开启）：否则这次重建会把
            //    **全部** 行容器重新生成一遍（51 个重容器）→ 切视图 / 打开分组 / 移入账号就卡、
            //    鼠标动不了（2026-10-14 用户报的就是它）。
            CoolDownSortCards();
            var sw = Stopwatch.StartNew();
            _accounts = new ObservableCollection<Account>(filteredAccounts);
            accountsDataGrid.ItemsSource = _accounts;
            UpdateUI();
            SortLog($"UpdateAccountsList 重建 {filteredAccounts.Count} 条，本次调用耗时 {sw.ElapsedMilliseconds}ms");

            // 最后一张卡贴视口底 + 滚动兜底（内部 ScrollViewer 现在才建出来）
            HookAccountScrollSpacing();
            ScheduleAccountSpacingRefresh();
        }

        private List<Account> GetFilteredAccounts(string searchTerm)
        {
            // 「移入账号」模式：直接复用账号列表页面，但展示的是**全部账号**
            // （不再按分组过滤），并给每条算出「能不能移入」。
            if (_isMoveMode)
                return ProcessMoveAccounts(searchTerm);

            if (_selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                return group != null ? ProcessGroupAccounts(group, searchTerm) : new List<Account>();
            }
            else
            {
                return ProcessMainAccounts(searchTerm);
            }
        }

        /// <summary>
        /// 「移入账号」模式下的列表：全部账号，顺序和主账号列表一致（按 Order：置顶区在前）。
        /// 已在目标分组里的账号 <see cref="Account.IsMoveable"/> = false —— 界面提示「已在分组」且不可选。
        /// </summary>
        private List<Account> ProcessMoveAccounts(string searchTerm)
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == _moveTargetGroupId);
            var inGroupIds = group?.AccountIds ?? new List<int>();

            var filteredAccounts = string.IsNullOrWhiteSpace(searchTerm)
                ? _allAccounts
                : _sortingService.FilterAccounts(_allAccounts, searchTerm);

            foreach (var account in filteredAccounts)
            {
                account.IsMarked = false;   // 移入模式下不显示分组标记
                account.ShowStar = false;   // 移入模式不显示置顶竖条
                account.IsMoveable = !inGroupIds.Contains(account.Id);
            }

            return _sortingService.GetMainDisplayOrder(filteredAccounts);
        }

        private List<Account> ProcessGroupAccounts(AccountGroup group, string searchTerm)
        {
            var filteredAccounts = _allAccounts
                .Where(a => group.AccountIds.Contains(a.Id))
                .ToList();

            foreach (var account in filteredAccounts)
            {
                account.IsMarked = group.IsAccountMarked(account.Id);
                account.IsMoveable = true;
                account.IsSelectedForMove = false;
                // 分组内的置顶高亮 = **本分组**的置底。
                // ★ 不能用 account.IsPinned：那是主列表的全局置顶、分组内不重置，
                //   会害得"主列表置顶过的账号在分组里也变蓝"。
                account.ShowStar = account.IsMarked;
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
                filteredAccounts = _sortingService.FilterAccounts(filteredAccounts, searchTerm);

            return _sortingService.GetGroupDisplayOrder(filteredAccounts, group);
        }

        private List<Account> ProcessMainAccounts(string searchTerm)
        {
            var filteredAccounts = string.IsNullOrWhiteSpace(searchTerm)
                ? _allAccounts
                : _sortingService.FilterAccounts(_allAccounts, searchTerm);

            foreach (var account in filteredAccounts)
            {
                account.IsMarked = false;
                account.IsMoveable = true;
                account.IsSelectedForMove = false;
                account.ShowStar = account.IsPinned;      // 主列表的置顶竖条 = 置顶
            }

            return _sortingService.GetMainDisplayOrder(filteredAccounts);
        }
        #endregion

        #region UI状态管理
        private void UpdateUI()
        {
            UpdateUIState();
            UpdateGroupInfoDisplay();
            UpdateWatermarkVisibility();
        }

        private void UpdateUIState()
        {
            if (_currentDisplayMode == DisplayMode.AccountList && _accounts.Count == 0)
            {
                bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
                bool hasAccountsInTotal = _selectedGroupId > 0
                    ? _allGroups.Any(g => g.Id == _selectedGroupId && g.AccountIds.Count > 0)
                    : _allAccounts.Count > 0;

                if ((hasSearchText && hasAccountsInTotal) || (!hasSearchText && !hasAccountsInTotal))
                    SwitchToDisplayMode(DisplayMode.NoData);
            }
            else if (_currentDisplayMode == DisplayMode.NoData && _accounts.Count > 0)
            {
                SwitchToDisplayMode(DisplayMode.AccountList);
            }
        }

        private void SwitchToDisplayMode(DisplayMode mode)
        {
            _currentDisplayMode = mode;

            noDataMessage.Visibility = Visibility.Collapsed;
            accountsDataGrid.Visibility = Visibility.Collapsed;
            groupManagementMessage.Visibility = Visibility.Collapsed;

            UpdateNoDataMessage();
            UpdateNoDataBackButton();

            switch (mode)
            {
                case DisplayMode.AccountList:
                    ApplyViewState();

                    noDataMessage.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    accountsDataGrid.Visibility = _accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                    // ★★ 2026-10-21：DataGrid 刚可见，行容器**还没生成**（以前只在 UpdateAccountsList
                    //   里刷「最后一张卡贴底」，那次跑在 Collapsed 期间 ⇒ VisualDescendants<DataGridRow>
                    //   一行都找不到 ⇒ 空转）。用户实测现象：「顶层账号列表首次进不生效，
                    //   但进过一次分组内账号列表后就正常了」（进分组那次 Visibility 已是 Visible）。
                    //   所以这里可见性切完之后必须补刷；行容器要等布局 ⇒ Loaded 优先级再兜底一次。
                    if (accountsDataGrid.Visibility == Visibility.Visible)
                    {
                        HookAccountScrollSpacing();
                        ScheduleAccountSpacingRefresh();
                    }
                    break;

                case DisplayMode.NoData:
                    ApplyViewState();

                    noDataMessage.Visibility = Visibility.Visible;
                    break;

                case DisplayMode.GroupManagement:
                    _viewStateManager.SwitchToView(ViewStateManager.AppView.GroupManagement);

                    // ★★ 三段的顺序是关键（2026-10-16「首次打开分组列表先空白，卡片随后才出来」）：
                    //   ① 先 Hidden（**参与布局** → ScrollViewer 模板展开、ItemsControl 进可视化树），
                    //      而不是直接 Visible —— Visible 之后第一帧屏幕上是「空容器」= 用户看到的空白；
                    //   ② 在 Hidden 状态下填数据 + UpdateLayout：卡片容器当场造好、布局当场算完；
                    //   ③ 最后才 Visible —— 切出去的这一帧就已经是带内容的。
                    //   全程同步，中途不会画帧，所以不存在「先空再填」的中间状态。
                    groupManagementMessage.Visibility = Visibility.Hidden;
                    groupManagementMessage.UpdateLayout();

                    SortGroups();
                    UpdateGroupsList();
                    groupManagementMessage.UpdateLayout();

                    groupManagementMessage.Visibility = Visibility.Visible;

                    // ★★ 2026-10-21：同上——分组列表「最后一张分组卡贴视口底」也要在
                    //   这里补刷（Hidden 期间面板 Children 可能还没建全）。
                    ScheduleGroupCardSpacingRefresh();
                    break;
            }

            UpdateGroupInfoDisplay();
        }

        /// <summary>
        /// 按当前所处的位置决定底部按钮区显示哪几个按钮。
        /// 「移入账号」模式优先：这时只显示「移入 / 取消」两个。
        /// </summary>
        private void ApplyViewState()
        {
            if (_isMoveMode)
            {
                _viewStateManager.SwitchToView(ViewStateManager.AppView.MoveMode);
            }
            else if (_selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                _viewStateManager.SwitchToView(ViewStateManager.AppView.GroupDetail, group);
            }
            else
            {
                _viewStateManager.SwitchToView(ViewStateManager.AppView.Home);
            }
        }
        #endregion

        #region 分组信息显示
        private void UpdateGroupInfoDisplay()
        {
            currentGroupLabel.Visibility = Visibility.Visible;

            if (_isMoveMode)
                UpdateMoveModeInfo();
            else if (_currentDisplayMode == DisplayMode.GroupManagement)
                UpdateGroupManagementInfo();
            else if (_selectedGroupId > 0)
                UpdateSelectedGroupInfo();
            else
                UpdateMainAccountListInfo();
        }

        /// <summary>
        /// 移入模式的顶部信息：
        /// 「移入账号：共计N个账号 • 可移入M个 • 已选择K个」。
        /// </summary>
        private void UpdateMoveModeInfo()
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == _moveTargetGroupId);
            int total = _allAccounts.Count;
            int inGroup = group == null ? 0 : _allAccounts.Count(a => group.AccountIds.Contains(a.Id));
            int moveable = total - inGroup;
            int selected = _allAccounts.Count(a => a.IsSelectedForMove);

            leftText.Text = $"移入账号：共计{total}个账号";
            groupSeparator.Visibility = Visibility.Visible;
            groupSeparator.Text = "•";
            rightText.Text = $"可移入{moveable}个 • 已选择{selected}个";
        }

        private void UpdateGroupManagementInfo()
        {
            leftText.Text = $"分组列表：共计{_allGroups.Count}个分组";
            groupSeparator.Visibility = Visibility.Collapsed;
            rightText.Text = "";
        }

        private void UpdateSelectedGroupInfo()
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
            if (group != null)
                UpdateGroupInfoForSelectedGroup(group);
        }

        private void UpdateMainAccountListInfo()
        {
            leftText.Text = $"账号列表：共计{_allAccounts.Count}个账号";
            groupSeparator.Visibility = Visibility.Visible;
            groupSeparator.Text = "•";
            rightText.Text = $"{_allGroups.Count}个分组";
        }

        private void UpdateGroupInfoForSelectedGroup(AccountGroup group)
        {
            var groupAccounts = _allAccounts.Where(a => group.AccountIds.Contains(a.Id)).ToList();
            int totalAccountCount = groupAccounts.Count;
            int markedCount = group.MarkedAccountIds?.Count ?? 0;
            int unmarkedCount = totalAccountCount - markedCount;

            leftText.Text = $"分组：{group.Name}";
            groupSeparator.Visibility = Visibility.Visible;
            groupSeparator.Text = "•";

            string displayText = $"{totalAccountCount}个账号";
            if (markedCount > 0) displayText += $" • 已置底{markedCount}";
            if (unmarkedCount > 0) displayText += $" • 未置底{unmarkedCount}";

            rightText.Text = displayText;
        }
        #endregion

        #region 分组管理
        private void SortGroups()
        {
            if (_allGroups != null && _allGroups.Count > 0)
            {
                _allGroups = _sortingService.SortGroups(_allGroups);
                UpdateGroupsList();
            }
        }

        private void UpdateGroupsList()
        {
            var groupsItemsControl = FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl");
            if (groupsItemsControl == null)
            {
                // ★ 找不到 = 数据一条都没填进去（ItemsSource 都设不上）→ 分组区显示出来就是空的。
                //   发生在分组区还没参与过布局时（ScrollViewer 模板未应用，ItemsControl 不在可视化树里）。
                SortLog($"× UpdateGroupsList：找不到 groupsItemsControl（可见性={groupManagementMessage?.Visibility}）");
                return;
            }

            if (_allGroups.Count > 0)
            {
                // ★ 始终用**同一个** ObservableCollection 当 ItemsSource：
                //   置顶/取消置顶时才能像账号那样"就地 Move"（容器不重建 → FLIP 位置准确、手感一致）。
                if (!ReferenceEquals(groupsItemsControl.ItemsSource, _groupDisplayItems))
                    groupsItemsControl.ItemsSource = _groupDisplayItems;

                // ★★ 内容没变就**不要 Clear + 重新 Add**（2026-10-16「首次打开分组列表闪一下」）：
                //    `Clear()` 会把所有卡片容器一次性撤掉，中间只要渲染一帧就是**空列表** → 看着就是闪一下。
                //    而切到分组管理时 `SortGroups()` 已经刷过一次、紧接着外面又刷一次 = 连闪两下。
                //    先按当前数据算出新的一批，逐字段比；一样就一个都不动。
                var newItems = _allGroups
                    .Select(g => _sortingService.CreateGroupDisplayObject(g, _allAccounts))
                    .ToList();

                if (!SameGroupDisplayItems(_groupDisplayItems, newItems))
                {
                    _groupDisplayItems.Clear();
                    foreach (var item in newItems) _groupDisplayItems.Add(item);
                }

                UpdateGroupsListUI(groupsItemsControl);
                ShowGroupsListContainer(true);
                // 分组卡片这时才参与布局（容器刚生成）⇒ 等布局跑完再重算底边距
                // （Render 优先级重排到看见面板 Children 为止，见 ScheduleGroupCardSpacingRefresh）
                ScheduleGroupCardSpacingRefresh();
            }
            else
            {
                groupsItemsControl.ItemsSource = null;
                _groupDisplayItems.Clear();
                ShowGroupsListContainer(false);
            }
        }

        private bool _groupsPrewarmed;

        /// <summary>
        /// 分组卡片**提前生成**（2026-10-16：「首次打开分组列表先是一块空白，过一下卡片才出来」）。
        ///
        /// <para>为什么空：分组区的 <c>ItemsControl</c> 平时是 <c>Collapsed</c>，
        /// **容器要等它真正参与布局那一刻才生成** —— 于是首次切到分组管理就是现造全部卡片
        /// （模板很重：一堆 Path / 按钮 / 宿主样式），屏幕上先空白、卡片随后才补上。</para>
        ///
        /// <para>★ 做法：临时把分组区设成 <c>Visibility.Hidden</c> —— **Hidden 仍然参与布局**
        /// （不像 Collapsed 直接跳过）→ 容器会真的造出来；但它**不绘制** → 屏幕上什么也看不到。
        /// 造完立刻还原，容器不会被回收（这个 ItemsControl 本来就是关虚拟化的）。</para>
        ///
        /// <para>★ 时机由 <see cref="LoadAllData"/> 排在 UI 空闲时（<c>ContextIdle</c>）：
        /// 首屏已经显示完再做，不和首屏抢时间。</para>
        /// </summary>
        private void PrewarmGroupCards()
        {
            if (_groupsPrewarmed) return;
            if (_allGroups == null || _allGroups.Count == 0) { SortLog("× 预热分组：没有分组数据"); return; }
            if (groupManagementMessage == null) { SortLog("× 预热分组：groupManagementMessage 为 null"); return; }

            var old = groupManagementMessage.Visibility;
            var sw = Stopwatch.StartNew();

            try
            {
                // ★ 顺序不能反：必须先 Hidden + UpdateLayout。
                //   `groupsItemsControl` 藏在 ScrollViewer 的模板里 —— 分组区一直 Collapsed 时
                //   ScrollViewer **模板没被应用**，ItemsControl 只在逻辑树上、不在可视化树里，
                //   FindVisualChild 直接找不到（2026-10-16 23:15 日志「× 找不到 groupsItemsControl」就是这个）。
                //   Hidden 会参与布局 → 模板展开 → ItemsControl 进可视化树 → 才能填数据/造容器。
                groupManagementMessage.Visibility = Visibility.Hidden;
                groupManagementMessage.UpdateLayout();

                var ic = FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl");
                if (ic == null) { SortLog("× 预热分组：仍找不到 groupsItemsControl"); return; }

                UpdateGroupsList();                 // 填数据（顺带把「暂无分组」空态收掉）
                groupManagementMessage.UpdateLayout();   // ★ 真正把卡片容器造出来

                // ★ 再离屏渲染一帧：把「第一次真正画到屏幕」的开销（阴影 shader 编译、字形/画刷缓存）
                //   提前付掉。用 Opacity=0.01 + 不接收命中测试，屏幕上什么都看不到；
                //   整个过程是同步的，中间不会有帧被画出去。
                try
                {
                    var size = groupManagementMessage.RenderSize;
                    int w = (int)Math.Max(1, Math.Min(size.Width, 4000));
                    int h = (int)Math.Max(1, Math.Min(size.Height, 4000));

                    groupManagementMessage.Opacity = 0.01;
                    groupManagementMessage.IsHitTestVisible = false;
                    groupManagementMessage.Visibility = Visibility.Visible;
                    groupManagementMessage.UpdateLayout();

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(groupManagementMessage);
                }
                catch (Exception ex)
                {
                    SortLog($"× 预热分组：离屏渲染失败 {ex.Message}");
                }

                sw.Stop();
                var panel = FindVisualChild<VirtualizingStackPanel>(
                    FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl"));
                SortLog($"→ 分组卡片预热：容器 {panel?.Children.Count ?? -1} 个，耗时 {sw.ElapsedMilliseconds}ms");

                _groupsPrewarmed = true;
            }
            finally
            {
                groupManagementMessage.Opacity = 1;
                groupManagementMessage.IsHitTestVisible = true;
                groupManagementMessage.Visibility = old;   // 还原（通常是 Collapsed）
            }
        }

        /// <summary>
        /// 两批分组卡片**在界面上看起来是否完全一样**（逐字段比，不用引用相等 ——
        /// `CreateGroupDisplayObject` 每次都是新对象）。一样 ⇒ 列表不需要重建。
        /// </summary>
        private static bool SameGroupDisplayItems(
            System.Collections.Generic.IList<Models.GroupDisplayItem> oldItems,
            System.Collections.Generic.List<Models.GroupDisplayItem> newItems)
        {
            if (oldItems.Count != newItems.Count) return false;

            for (int i = 0; i < newItems.Count; i++)
            {
                var a = oldItems[i];
                var b = newItems[i];

                if (a.Id != b.Id) return false;
                if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)) return false;
                if (!string.Equals(a.Remark, b.Remark, StringComparison.Ordinal)) return false;
                if (a.AccountCount != b.AccountCount) return false;
                if (a.MarkedCount != b.MarkedCount) return false;
                if (a.UnmarkedCount != b.UnmarkedCount) return false;
                if (a.IsPinned != b.IsPinned) return false;
                if (a.HasMarks != b.HasMarks) return false;
            }

            return true;
        }

        /// <summary>
        /// 分组卡片上「清除置底」按钮的显隐**不再在这里改**（2026-10-14）：
        /// 原来按名字找到按钮再赋 Visibility，而列表一刷新容器就重建 —— 新容器先按 XAML 默认值
        /// Collapsed 冒出来、再被这里改成 Visible，于是每次置底/取消置底那颗按钮都会闪一下。
        /// 现在 XAML 里直接 <c>Visibility="{Binding HasMarks, Converter=...}"</c>，
        /// 容器一建出来就是正确的，这里只负责把布局催出来。
        /// </summary>
        private void UpdateGroupsListUI(ItemsControl groupsItemsControl)
        {
            groupsItemsControl.InvalidateVisual();
            groupsItemsControl.UpdateLayout();
        }

        private void ShowGroupsListContainer(bool show)
        {
            var noGroupMessage = FindVisualChild<Border>(groupManagementMessage, "noGroupMessage");
            if (noGroupMessage != null)
                noGroupMessage.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }
        #endregion

        #region 标记管理
        private void ToggleAccountMarkInGroup(int accountId, int groupId)
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                if (group.IsAccountMarked(accountId))
                    group.UnmarkAccount(accountId);
                else
                    group.MarkAccount(accountId);

                GroupDataService.SaveGroup(group);

                var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                if (account != null)
                    account.IsMarked = group.IsAccountMarked(accountId);
            }
        }

        private void ClearAllMarksInGroup(int groupId)
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
            if (group != null && group.MarkedAccountIds?.Count > 0)
            {
                group.MarkedAccountIds.Clear();
                GroupDataService.SaveGroup(group);

                foreach (var account in _allAccounts.Where(a => group.AccountIds.Contains(a.Id)))
                    account.IsMarked = false;
            }
        }

        #endregion

        #region 无数据消息管理
        private void UpdateNoDataMessage()
        {
            var noDataIcon = FindVisualChild<TextBlock>(noDataMessage, "noDataIcon");
            var noDataVectorIcon = FindVisualChild<Viewbox>(noDataMessage, "noDataVectorIcon");
            var noDataText = FindVisualChild<TextBlock>(noDataMessage, "noDataText");
            var noDataSubText = FindVisualChild<TextBlock>(noDataMessage, "noDataSubText");

            if (noDataIcon == null || noDataText == null || noDataSubText == null) return;

            bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
            bool hasAccountsInCurrentView = _selectedGroupId > 0
                ? _allGroups.Any(g => g.Id == _selectedGroupId && g.AccountIds.Count > 0)
                : _allAccounts.Count > 0;

            // 默认 = 「一个账号都没有」：图标用**「添加账号」按钮那枚矢量人像**（原来是 📋 表情，像"复制"）
            ShowVectorIcon(noDataVectorIcon, noDataIcon, true);
            noDataText.Text = "当前没有任何账号";
            noDataSubText.Text = "点击右上角'添加账号'按钮添加第一个账号";

            if (_selectedGroupId > 0 && !hasSearchText && !hasAccountsInCurrentView)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                {
                    // 另外两种状态仍用文字表情（文件夹 / 放大镜），语义更贴切
                    ShowVectorIcon(noDataVectorIcon, noDataIcon, false);
                    noDataIcon.Text = "📂";
                    noDataText.Text = $"「{group.Name}」分组中没有账号";
                    noDataSubText.Text = $"点击右上角 移入账号 按钮选择账号移入「{group.Name}」分组中";
                }
            }
            else if (hasSearchText && _allAccounts.Count > 0)
            {
                ShowVectorIcon(noDataVectorIcon, noDataIcon, false);
                noDataIcon.Text = "🔍";
                noDataText.Text = "未找到相关账号";
                noDataSubText.Text = $"未找到与「{searchTextBox.Text}」相关的账号";
            }
        }

        /// <summary>
        /// 空状态图标二选一：矢量图标（「添加账号」的人像）或文字表情。
        /// 两者互斥显示，且共用同一个下边距（XAML 里各自写死 0,0,0,15）。
        /// </summary>
        private static void ShowVectorIcon(Viewbox? vectorIcon, TextBlock? textIcon, bool useVector)
        {
            if (vectorIcon != null) vectorIcon.Visibility = useVector ? Visibility.Visible : Visibility.Collapsed;
            if (textIcon != null) textIcon.Visibility = useVector ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateNoDataBackButton()
        {
            if (noDataBackButton != null)
            {
                bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
                bool hasAccountsInTotal = _allAccounts.Count > 0;

                noDataBackButton.Visibility = (hasSearchText && hasAccountsInTotal)
                    ? Visibility.Visible : Visibility.Collapsed;

                noDataBackButton.Content = _selectedGroupId > 0 ? "返回当前分组" : "返回账号列表";
            }
        }
        #endregion

        #region 搜索框事件
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool hasText = !string.IsNullOrEmpty(searchTextBox.Text);
            clearSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            watermarkText.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            UpdateAccountsList(searchTextBox.Text);

            UpdateWatermarkVisibility();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            bool hadFocus = searchTextBox.IsFocused;
            searchTextBox.Text = string.Empty;
            clearSearchButton.Visibility = Visibility.Collapsed;
            watermarkText.Visibility = Visibility.Visible;

            if (hadFocus)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    searchTextBox.Focus();
                    Keyboard.Focus(searchTextBox);
                }, DispatcherPriority.Render);
            }

            UpdateWatermarkVisibility();
        }

        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            searchBox.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x56, 0xDB));
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            searchBox.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xDD, 0xDD, 0xDD));
        }
        #endregion

        #region 标记按钮事件
        private void MarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int accountId && _selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                if (group == null || account == null) return;

                bool willMark = !group.IsAccountMarked(accountId);
                ApplyStarToggle(accountId, willMark, () => CommitMarkToggle(account, group, accountId));
            }
            else if (_selectedGroupId == 0)
            {
                _toastManager.Show("提示", "标记功能仅在分组内可用", false);
            }
        }

        /// <summary>真正落地一次置底切换（改数据 + 提示 + 刷新列表与分组信息）。</summary>
        private void CommitMarkToggle(Account account, AccountGroup group, int accountId)
        {
            ToggleAccountMarkInGroup(accountId, _selectedGroupId);
            bool isNowMarked = group.IsAccountMarked(accountId);
            string action = isNowMarked ? "已置底" : "已取消置底";

            // 自由排序：置底的排到置底区末尾，取消置底的排到未置底区末尾（照前端模拟）
            SortingService.AssignMarkOrder(_allAccounts, account, group);
            AccountDataService.SaveAccount(account);

            _toastManager.Show($"{action}·账号「{account.Nickname}」在分组「{group.Name}」中", true);

            // 照前端：改完数据用 FLIP 平滑滑到置底区/未置底区，不瞬间跳位；**就地 Move，不重建容器**
            SmoothReorderAccountInPlace(account);
            UpdateGroupInfoDisplay();
        }
        #endregion

        #region 自由排序（按住排序手柄上下拖动）

        //
        // ★★★ 第十四版（2026-10-14）—— 适配**行虚拟化**（列表里 51 个账号，不能再全部生成容器）。
        //
        //   为什么要改：账号从 5 个涨到 51 个之后，「进 UID 界面」时会一次性造出 51 个行容器
        //   （行模板很重：一堆 Path / 边框 / 头像）→ 首次进界面卡住、鼠标都动不了。
        //   所以重新打开 `EnableRowVirtualization`（只造可见的 ~8 行），代价是
        //   `panel.Children` 只剩**可见的那几张卡** → 一切**下标改用数据下标**：
        //     · 显示顺序 = 数据层（账号取 `_accounts`、分组取 itemsControl.Items）；
        //     · 让位只对**已经实例化的容器**做（在屏外的本来也看不到）；
        //     · 落单用 `_accounts.Move(数据下标, 数据下标)`。
        //
        //   ★ 仍然守住的铁律（前十三版的教训，全部保留）：
        //     ① 全程 DIP，绝不用 PointToScreen 的设备像素；
        //     ② 被拖卡每帧**量**它在哪、再摆到「按下时视口顶 + 鼠标位移」——
        //        不减任何滚动补偿 → 不依赖滚动怎么实现，滚动时绝不来回抽搐；
        //     ③ 目标槽位从位移直接推 `原槽位 + round(Y ÷ 行距)`（纯单调，不可能跳）；
        //     ④ 被拖卡的位置 = 「光标（钳在列表内）− 抓取偏移」，再**整张钳回列表容器**
        //        （永远完整显示，上不出顶、下不出底）；到边缘时由自动滚动接管；
        //        本区边界（置顶/未置顶不许互串）只在本区**不止一张**时才收窄；
        //     ⑤ 自动滚动：方向看"光标想把卡片推到哪"（未钳制）、许可看"卡片真的贴到容器边"，
        //        按「像素/秒 × dt」算（跨刷新率一致）；鼠标没真移动过就不滚；
        //     ⑥ 拖动中**一行数据都不动**，松手才写一次（批量落盘）。
        //

        private bool _sortDragging;
        private bool _sortDropping;
        private bool _sortIsGroup;
        private Panel? _sortPanel;
        private ScrollViewer? _sortScroller;
        private Button? _sortHandle;
        private Window? _sortWindow;
        private List<object> _sortItems = new();     // 当前视图的**数据层**显示顺序（快照）
        private readonly Dictionary<FrameworkElement, TranslateTransform> _sortTrs = new();
        private readonly Dictionary<FrameworkElement, double> _sortShiftTargets = new();  // 容器 → 当前动画目标
        private double _sortPitch = 70;              // 相邻槽距（DIP）
        private int _sortFrom, _sortTo;              // **数据下标**
        private int _sortSecStart, _sortSecEnd;      // 本区范围（数据下标，拖动中不变）
        private double _sortStartMouseY;             // 按下时鼠标 Y（DIP，相对 ScrollViewer 视口）
        private double _sortMouseY;                  // 最近一次鼠标 Y（同上）
        private double _sortStartCardViewport;       // 按下时被拖卡的视口顶（DIP）
        private double _sortPinTop;                  // 本帧"光标想让卡片到哪"（未受本区钳制）—— 自动滚动判方向
        private double _sortCardViewportTop;         // 本帧被拖卡实际的目标视口顶（受本区钳制后）—— 自动滚动判许可
        private int _sortDragToken;                  // 拖动序号：给"松手兜底"用，防止上一轮的兜底掐掉新的一次拖动
        private double _sortScrollLastDir;           // 自动滚动上一次的方向（只在翻转时写日志）
        private TimeSpan _sortLastRenderTime;        // 上一帧时间（自动滚动按像素/秒算）
        private TranslateTransform? _sortDragTr;
        private FrameworkElement? _sortDragCard;
        private bool _sortDirty;
        private bool _sortMovedLogged;
        private bool _sortCardsWarmed;               // 本次拖动已经把全部行实例化过了（见 EnsureSortCardsWarmed）
        private DispatcherTimer? _coolDownTimer;     // 预热后一直没拖动 → 延迟把虚拟化还回去

        /// <summary>拖动排序的排查日志（写到 exe 同目录 UID/Config/sort_drag.log）。</summary>
        private static void SortLog(string message)
        {
            try
            {
                string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UID", "Config");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "sort_drag.log"),
                    $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>当前视图的**数据层**显示顺序（账号取 `_accounts`，分组取 itemsControl.Items）。</summary>
        private List<object> CaptureSortItems()
        {
            var list = new List<object>();
            try
            {
                if (_currentDisplayMode == DisplayMode.GroupManagement)
                {
                    var ic = GetGroupsItemsControl();
                    if (ic != null) foreach (var it in ic.Items) list.Add(it);
                }
                else
                {
                    foreach (var a in _accounts) list.Add(a);
                }
            }
            catch { }
            return list;
        }

        private static TranslateTransform EnsureSortTransform(FrameworkElement fe)
        {
            if (fe.RenderTransform is TranslateTransform t) return t;
            t = new TranslateTransform();
            fe.RenderTransform = t;
            return t;
        }

        /// <summary>鼠标离开排序手柄（没按下去）→ 延迟把行虚拟化还回去。</summary>
        private void SortHandleButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_sortDragging && !_sortDropping) ScheduleCoolDown();
        }

        /// <summary>
        /// 「接下来这一拖要跨屏」时，把账号表的行全部实例化（关掉行虚拟化）。
        ///
        /// <para>只在**自动滚动真要开始**的那一刻调用（见 <see cref="AutoScrollStep"/>），
        /// 并按拖动去重（<see cref="_sortCardsWarmed"/>）：只在可见的几张卡之间挪位置的拖动
        /// 一次都不会触发，按下手柄也就没有任何延迟。</para>
        ///
        /// <para>★★ 为什么不能更早（悬停手柄 / 按下手柄就预热）：关虚拟化的那一刻要一次性
        /// 造出全部行容器，51 个账号实测 250~370ms，UI 线程整段卡死 —— 那正是「鼠标一卡一卡」
        /// 的来源；造完还会常驻 51 个重行容器参与命中测试和重绘。</para>
        /// </summary>
        private void EnsureSortCardsWarmed()
        {
            if (_sortCardsWarmed) return;
            _sortCardsWarmed = true;
            WarmUpSortCards();
        }

        /// <summary>
        /// 延迟把行虚拟化还回去（默认 1.2s）。
        /// 留这段缓冲是为了"鼠标刚移开又马上回来"不必重新预热（预热是最贵的一步）。
        /// </summary>
        private void ScheduleCoolDown(int delayMs = 1200)
        {
            if (_sortDragging || _sortDropping) return;

            _coolDownTimer ??= new DispatcherTimer();
            _coolDownTimer.Tick -= OnCoolDownTimerTick;
            _coolDownTimer.Tick += OnCoolDownTimerTick;
            _coolDownTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
            _coolDownTimer.Stop();
            _coolDownTimer.Start();
        }

        private void OnCoolDownTimerTick(object? sender, EventArgs e)
        {
            _coolDownTimer?.Stop();
            CoolDownSortCards();
        }

        /// <summary>鼠标离开账号列表 → 把行虚拟化还回去（预热只覆盖"可能要拖动"的这段时间）。</summary>
        private void AccountsDataGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            CoolDownSortCards();
        }

        /// <summary>
        /// 预热的**逆向操作**：把行虚拟化重新打开（只留可见的若干行 + 前后缓冲）。
        ///
        /// <para>★★★ 为什么必须还原（2026-10-14 用户报"打开分组 / 移入账号 / 切页面也卡、鼠标动不了"）：
        /// 虚拟化**关着**的时候，**每一次列表重建**（切视图、打开分组、进入移入模式、搜索、返回）
        /// 都要把 **全部** 行容器重新生成一遍（行模板很重：一堆 Path / 边框 / 头像）→ 几百毫秒、鼠标都动不了。
        /// 预热的收益只在"拖动的那几秒"，所以只在那段时间里关着，之后立刻还回去。</para>
        ///
        /// <para>★ 不在这里 `UpdateLayout()`：`InvalidateMeasure()` 已排好下一轮布局，
        /// 让它在随后的重建/渲染里自然完成，避免同步再付一笔。</para>
        /// </summary>
        private void CoolDownSortCards()
        {
            if (accountsDataGrid == null) return;
            if (_sortDragging || _sortDropping) return;      // 拖动中绝不还原：被拖那一行必须活着

            var panel = FindVisualChild<VirtualizingStackPanel>(accountsDataGrid);
            if (panel == null) return;
            if (VirtualizingPanel.GetIsVirtualizing(panel)) return;   // 本来就是开的

            accountsDataGrid.EnableRowVirtualization = true;
            VirtualizingPanel.SetIsVirtualizing(panel, true);
            panel.InvalidateMeasure();
        }

        /// <summary>
        /// 拖动前的**一次性预热**：把账号表的**行虚拟化关掉**，让所有行都实例化。
        ///
        /// <para>★ 为什么平时要开着虚拟化：这就是"按可视数量动态加载"—— 一屏只显示 5~6 张卡，
        /// 就只造那几张 + 前后各一屏的缓冲（WPF 默认 `CacheLength`），所以**首次进 UID 打开很快**；
        /// 账号涨到几十上百条也不会变慢。</para>
        ///
        /// <para>★ 为什么拖之前必须预热：拖动期间**被拖那一行必须一直活着**。虚拟化下只要它的
        /// **布局槽位**滚出可见区，行容器就被回收重建（长距离拖动必踩，日志里是每帧一条
        /// 「行容器被回收重建」→ 卡片抓不住、松手原位不动）。</para>
        ///
        /// <para>★ 什么时候做：由 <see cref="EnsureSortCardsWarmed"/> 在「这一次拖动要跨屏、
        /// 自动滚动即将开始」时调用，并按拖动去重。账号多时这笔开销是几百毫秒 —— 放在悬停或
        /// 按下时付，鼠标就会一卡一卡；放在滚动开始前付，短距离拖动则完全不付。</para>
        ///
        /// <para>★★ 必须同时做三件事，缺一个都**不会生效**（2026-10-14 日志实锤：只做第 1 步时
        /// 「行容器 6 个、耗时 1ms」= 一行都没多生成）：
        /// ① 把 `DataGrid.EnableRowVirtualization` 置 false（DataGrid 会把它同步给行面板）；
        /// ② 把**面板**上的 `VirtualizingPanel.IsVirtualizing` 置 false（运行时改 DataGrid 的属性
        ///    对已经建好的面板不生效 —— 面板只在创建时读一次）；
        /// ③ **显式 `InvalidateMeasure()`**：否则面板的布局缓存仍被视为有效，
        ///    `UpdateLayout()` 直接跳过 → 不会调用 Measure → 一行都不会多生成。</para>
        /// </summary>
        private void WarmUpSortCards()
        {
            if (accountsDataGrid == null) return;
            if (_currentDisplayMode == DisplayMode.GroupManagement) return;   // 分组列表本来就没虚拟化

            var panel = FindVisualChild<VirtualizingStackPanel>(accountsDataGrid);
            if (panel == null) { SortLog("× 预热：没找到行面板(VirtualizingStackPanel)"); return; }
            if (!VirtualizingPanel.GetIsVirtualizing(panel)) return;          // 已经预热过

            var sw = Stopwatch.StartNew();
            accountsDataGrid.EnableRowVirtualization = false;
            VirtualizingPanel.SetIsVirtualizing(panel, false);
            panel.InvalidateMeasure();                                        // ★ 破掉布局缓存，否则 UpdateLayout 空转
            panel.UpdateLayout();
            sw.Stop();

            SortLog($"→ 预热（关行虚拟化）：行容器 {panel.Children.Count} 个，耗时 {sw.ElapsedMilliseconds}ms");
        }

        private void SortHandleButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_sortDragging || _sortDropping) { SortLog("已在拖动中，忽略"); return; }
            if (_isMoveMode) return;
            if (!string.IsNullOrWhiteSpace(searchTextBox.Text))
            {
                _toastManager.Show("无法排序", "搜索状态下显示的是部分卡片，请先清空搜索再拖动排序", false);
                return;
            }

            if (sender is not Button handle) { SortLog("× sender 不是 Button"); return; }

            // ★ 按下时**不**实例化全部行：那样每次按下都要先卡 250~370ms 才开始跟手。
            //   真正的实例化推迟到「这一拖要跨屏、自动滚动即将开始」的那一刻
            //   （见 AutoScrollStep 里的 EnsureSortCardsWarmed）。
            _sortCardsWarmed = false;

            var card = _currentDisplayMode == DisplayMode.GroupManagement
                ? (FrameworkElement?)FindAncestor<ContentPresenter>(handle)
                : FindAncestor<DataGridRow>(handle);
            if (card == null) { SortLog("× 没找到卡片容器"); return; }
            if (card.DataContext is not (Account or GroupDisplayItem)) { SortLog("× DataContext 不是卡片"); return; }

            var parent = VisualTreeHelper.GetParent(card);
            _sortPanel = parent as Panel ?? FindAncestor<Panel>(parent);
            if (_sortPanel == null) { SortLog("× 找不到卡片面板(Panel)"); return; }

            var scroller = FindAncestor<ScrollViewer>(card);
            if (scroller == null) { SortLog("× 找不到 ScrollViewer"); return; }
            _sortScroller = scroller;

            _sortIsGroup = _currentDisplayMode == DisplayMode.GroupManagement;

            // ★ 显示顺序取**数据层**（虚拟化下 panel.Children 只有可见的那几张，不能当索引）
            _sortItems = CaptureSortItems();
            if (_sortItems.Count < 2)
            {
                SortLog($"× 卡片不足 2 张（{_sortItems.Count}）");
                _toastManager.Show("无法排序", "至少需要两张卡片才能拖动排序", false);
                return;
            }

            int from = _sortItems.IndexOf(card.DataContext!);
            if (from < 0) { SortLog("× 卡片不在数据列表里"); return; }

            // 槽距：优先量相邻两张已实例化卡片的布局差，量不到就用「卡片高 + 上下间距」
            double pitch = 0;
            {
                FrameworkElement? prev = null;
                foreach (var child in _sortPanel.Children)
                {
                    if (child is not FrameworkElement fe) continue;
                    if (fe is not (DataGridRow or ContentPresenter)) continue;
                    if (prev != null)
                    {
                        try
                        {
                            double d = VisualTreeHelper.GetOffset(fe).Y - VisualTreeHelper.GetOffset(prev).Y;
                            if (d > 1) { pitch = d; break; }
                        }
                        catch { }
                    }
                    prev = fe;
                }
            }
            if (pitch <= 1)
            {
                double h = card.ActualHeight;
                double m = card.Margin.Top + card.Margin.Bottom;
                pitch = h + m > 1 ? h + m : 70;
            }
            _sortPitch = pitch;

            // 被拖卡挂位移（并保证是干净的）
            _sortTrs.Clear();
            _sortShiftTargets.Clear();
            _sortDragTr = EnsureSortTransform(card);
            _sortDragTr.BeginAnimation(TranslateTransform.YProperty, null);
            _sortDragTr.Y = 0;

            double startViewport;
            try { startViewport = card.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y; }
            catch (Exception ex) { SortLog("× 量初始位置失败：" + ex.GetType().Name); return; }

            _sortFrom = _sortTo = from;
            _sortDragCard = card;
            _sortStartMouseY = _sortMouseY = e.GetPosition(scroller).Y;
            _sortStartCardViewport = startViewport;
            _sortPinTop = startViewport;
            _sortCardViewportTop = startViewport;
            _sortDragToken++;
            _sortScrollLastDir = 0;
            _sortLastRenderTime = TimeSpan.Zero;
            _sortDirty = false;
            _sortMovedLogged = false;

            // 本区范围（数据下标）：置顶区 / 普通区（分组内是未置底区 / 置底区）
            bool draggedStar = HasSortStar(_sortItems[from]);
            _sortSecStart = from; _sortSecEnd = from;
            while (_sortSecStart > 0 && HasSortStar(_sortItems[_sortSecStart - 1]) == draggedStar) _sortSecStart--;
            while (_sortSecEnd < _sortItems.Count - 1 && HasSortStar(_sortItems[_sortSecEnd + 1]) == draggedStar) _sortSecEnd++;

            _sortDragging = true;
            _sortDropping = false;

            Panel.SetZIndex(card, 100);
            _sortPanel.Cursor = Cursors.SizeAll;

            // 双保险接收鼠标事件：手柄捕获 + 宿主窗口级 Preview 订阅（位移是绝对量、幂等）
            _sortHandle = handle;
            _sortWindow = Window.GetWindow(handle);
            handle.CaptureMouse();
            handle.MouseMove += OnSortDragMouseMove;
            // ★ 不再订阅手柄的 LostMouseCapture：虚拟化下被拖卡所在的行容器会被回收/重建
            //   （滚动时必然发生），手柄一离开可视化树、捕获就丢 → 之前"有时候鼠标没松手
            //   就自己退出拖动"就是它触发了 FinishSortDrag。现在靠两条更可靠的路径收尾：
            //   ① 宿主窗口级 PreviewMouseLeftButtonUp（隧道路由，窗口里一定收得到）；
            //   ② 每帧查 Mouse.LeftButton == Released（连在窗口外松手也兜得住）。
            handle.MouseLeftButtonUp += OnSortDragMouseUp;
            if (_sortWindow != null)
            {
                _sortWindow.PreviewMouseMove += OnSortDragMouseMove;
                _sortWindow.PreviewMouseLeftButtonUp += OnSortDragMouseUp;
                _sortWindow.Deactivated += OnSortDragWindowDeactivated;
            }
            CompositionTarget.Rendering += OnSortDragFrame;
            e.Handled = true;
            SortLog($"→ 拖动开始 from={from}/{_sortItems.Count} pitch={_sortPitch:F1} 区[{_sortSecStart},{_sortSecEnd}] " +
                    $"panel={_sortPanel.GetType().Name} 可见容器={_sortPanel.Children.Count} " +
                    $"CanContentScroll={scroller.CanContentScroll} offset={scroller.VerticalOffset:F2}");
        }

        private void OnSortDragWindowDeactivated(object? sender, EventArgs e) => FinishSortDrag();

        /// <summary>只记录鼠标位置（相对视口，DIP）；真正的定位在每帧里做。</summary>
        private void OnSortDragMouseMove(object sender, MouseEventArgs e)
        {
            if (!_sortDragging || _sortDropping || _sortScroller == null) return;
            _sortMouseY = e.GetPosition(_sortScroller).Y;
            if (!_sortMovedLogged)
            {
                _sortMovedLogged = true;
                SortLog($"→ 收到首个移动事件（来源 {(sender as FrameworkElement)?.GetType().Name}）");
            }
            e.Handled = true;
        }

        /// <summary>每帧（等价前端 dragTick）：自动滚动 → 把被拖卡摆到鼠标位置 → 更新目标槽。</summary>
        private void OnSortDragFrame(object? sender, EventArgs e)
        {
            if (!_sortDragging || _sortDropping) return;
            try
            {
                double dtMs = 16.7;
                if (e is RenderingEventArgs rea)
                {
                    if (_sortLastRenderTime != TimeSpan.Zero)
                        dtMs = Math.Max(1, Math.Min(100, (rea.RenderingTime - _sortLastRenderTime).TotalMilliseconds));
                    _sortLastRenderTime = rea.RenderingTime;
                }

                // ★ 兜底：鼠标左键已经松开（比如在窗口外松的手，收不到 MouseUp）→ 收尾
                if (Mouse.LeftButton == MouseButtonState.Released)
                {
                    FinishSortDrag();
                    return;
                }

                AutoScrollStep(dtMs);
                PinDraggedCardToMouse();
            }
            catch (Exception ex)
            {
                SortLog("× 帧异常：" + ex.GetType().Name + " " + ex.Message);
                FinishSortDrag();
            }
        }

        /// <summary>
        /// 把被拖卡**摆到**「按下时的视口顶 + 鼠标位移」——每帧量它的真实位置再纠正，
        /// 所以完全不依赖滚动是怎么实现的（这是"滚动时抽搐"的根治办法）。
        /// </summary>
        private void PinDraggedCardToMouse()
        {
            if (_sortDragCard == null || _sortDragTr == null || _sortScroller == null || _sortPanel == null) return;

            // 行容器万一被回收重建（虚拟化下滚动时会）→ 找回新的，否则位移会丢、卡片会"归位"
            if (!_sortDragCard.IsDescendantOf(_sortPanel))
            {
                var again = FindCardByItem(_sortItems[_sortFrom]);
                if (again == null)
                {
                    SortLog("× 被拖卡的行容器被回收且找不回来（行虚拟化没关掉？）");
                    return;
                }
                _sortDragCard = again;
                _sortDragTr = EnsureSortTransform(again);
                Panel.SetZIndex(again, 100);
                SortLog("→ 行容器被回收重建，已重新绑定");
            }

            double viewH = _sortScroller.ActualHeight;
            double cardH = _sortDragCard.ActualHeight;
            if (viewH <= 1 || cardH <= 0) return;

            // ★★★ 位置 = 「光标（钳在列表内）− 抓取偏移」，然后再把**整张卡钳回列表容器里**：
            //     · 取「光标 − 抓取偏移」是为了让卡片**贴着光标走**（抓取偏移 = 按下时光标落在
            //       卡片里的高度，手柄画在卡片垂直中间 → ≈半张卡高），不会出现"卡片自己跑到别处"；
            //     · 再钳进容器是为了**卡片永远完整显示**（上不出顶、下不出底、不会被裁掉一截）。
            //   两者同时成立时：光标拖到上/下沿附近（约半张卡的距离内），
            //   卡片会先停到容器边缘 —— 而此时"抓取点贴到容器边"正好成立，
            //   AutoScrollStep 立刻开始滚动（见那里的两半判据），所以不会"卡片停住、光标一直走"。
            //   ★ 旧写法只做第 ① 步（把卡片整张钳在容器里 + 用鼠标位移算）：卡片到边缘就停，
            //     光标却还能继续走（最多差半张卡 ≈35px）→ 用户报的"自动滚动时卡片不跟手"。
            double grabOffset = _sortStartMouseY - _sortStartCardViewport;
            double cursor = Math.Max(0, Math.Min(viewH, _sortMouseY));
            _sortPinTop = cursor - grabOffset;                // 光标"想"让卡片到哪（**不受任何钳制**）→ 自动滚动判方向

            // ① 列表可视区：卡片**整张**不许越过容器最顶部 / 最底部（永远完整显示）
            double desiredViewport = Math.Max(0, Math.Min(viewH - cardH, _sortPinTop));

            // ★★ 本区边界（置顶 / 未置顶不许互串）：**只有本区不止一张卡时才收窄**。
            //    本区只有一张时（例如只有 1 个置顶账号）边界会退化成一个点 → 卡片被钉在
            //    原位、光标怎么拖都不动（用户报的"长按的卡片不跟鼠标"）。这种情况直接不收窄 ——
            //    落点本来也会被钳回本区（见下面的 best 钳制），拖了也是白拖，但手感是对的。
            //    ★ 边界取「本区第一张的布局顶」与「本区最后一张的布局底 − 卡高」，每帧实测
            //      （量到的位置减去已施加的让位位移），所以滚动时边界自己会跟着走。
            if (_sortSecStart != _sortSecEnd)
            {
                double lo = double.NegativeInfinity, hi = double.PositiveInfinity;

                if (_sortSecStart >= 0 && _sortSecStart < _sortItems.Count)
                {
                    var secFirst = FindCardByItem(_sortItems[_sortSecStart]);
                    if (secFirst != null)
                    {
                        double t = NaturalViewportTop(secFirst);
                        if (!double.IsNaN(t)) lo = t;
                    }
                }
                if (_sortSecEnd >= 0 && _sortSecEnd < _sortItems.Count)
                {
                    var secLast = FindCardByItem(_sortItems[_sortSecEnd]);
                    if (secLast != null)
                    {
                        double t = NaturalViewportTop(secLast);
                        if (!double.IsNaN(t)) hi = t + secLast.ActualHeight - cardH;
                    }
                }

                if (lo <= hi) desiredViewport = Math.Max(lo, Math.Min(hi, desiredViewport));
            }

            // ★ 记下**实际**的目标位置：自动滚动拿它判"卡片是否真的贴到容器上/下沿"。
            //   不用现量 `TransformToAncestor` —— 同一帧里**先滚、后摆卡**，
            //   现量会读到"还没重摆的旧位置"（卡顶在 0 和 78 之间来回跳，触发条件一帧开一帧关）。
            _sortCardViewportTop = desiredViewport;

            double measured;
            try { measured = _sortDragCard.TransformToAncestor(_sortScroller).Transform(new Point(0, 0)).Y; }
            catch { return; }

            // 扣掉上一帧我们自己加的那段位移 → 得到"纯布局位置"（无反馈、一步到位）
            double layoutViewport = measured - _sortDragTr.Y;
            double y = desiredViewport - layoutViewport;
            _sortDragTr.Y = y;

            // 目标槽位：直接从位移推（单调 → 不可能来回跳），再钳制在本区内
            int best = _sortFrom + (int)Math.Round(y / _sortPitch);
            best = Math.Max(_sortSecStart, Math.Min(_sortSecEnd, best));
            if (best == _sortTo) return;

            _sortTo = best;
            _sortDirty = best != _sortFrom;
            ApplyNeighborShifts();
        }

        /// <summary>
        /// 某张卡**不含我们加的位移**时的视口顶（DIP）—— 本区边界用。
        /// ★ 必须减掉已施加的 TranslateTransform（让位动画），否则边界会被算歪。
        /// 量不到时返回 <see cref="double.NaN"/>，调用方按"这一侧不设边界"处理。
        /// </summary>
        private double NaturalViewportTop(FrameworkElement card)
        {
            if (_sortScroller == null) return double.NaN;
            double measured;
            try { measured = card.TransformToAncestor(_sortScroller).Transform(new Point(0, 0)).Y; }
            catch { return double.NaN; }
            double applied = card.RenderTransform is TranslateTransform t ? t.Y : 0;
            return measured - applied;
        }

        /// <summary>在面板里按数据项找回卡片容器（虚拟化回收后会用）。</summary>
        private FrameworkElement? FindCardByItem(object? item)
        {
            if (_sortPanel == null || item == null) return null;
            foreach (var child in _sortPanel.Children)
            {
                if (child is FrameworkElement fe && ReferenceEquals(fe.DataContext, item)) return fe;
            }
            return null;
        }

        /// <summary>
        /// 自动滚动：**被拖卡一贴到列表容器的上/下沿就滚**，越往那一侧推越快。
        /// 速度按「鼠标往那一侧探得多深」加快（贴边 250 DIP/秒 起，封顶 1000 DIP/秒 × 本帧 dt）。
        /// ★★ 为什么不能只看鼠标（30px 带）：手柄画在卡片的**垂直中间**，所以卡片已经贴到容器底时，
        ///    鼠标还在上面约 35px 处 → 卡片到底了也不滚（用户报的"拖到容器底部没有自动向下滚"）。
        /// ★★ 为什么也不能只看卡片位置（早期版本）：那会把"光标推开一点、卡片还在原处"也算成滚动，
        ///    甚至抓在靠顶部的那张卡时"往下拖却一路往上滚"。
        /// ★ 正解 = **判据拆两半**（都在 `PinDraggedCardToMouse` 里算好，这里不现量 —— 同一帧是
        ///    **先滚、后摆卡**，现量会读到"还没重摆的旧位置"，触发一帧开一帧关）：
        ///      ① **方向**看 `_sortPinTop`（= 光标想让卡片到哪，**不受任何钳制**）→ 这才是用户意图，
        ///         上/下两支互斥，不会出现"往下拖却往上滚"；
        ///      ② **许可**看 `_sortCardViewportTop`（= 卡片实际到哪，受**容器 + 本区**双重钳制）→
        ///         必须真的贴在容器上/下沿（±1px）才算。
        ///    因为卡片位置 = 「光标 − 抓取偏移」再钳回容器，这两半在**卡片贴到容器边的瞬间同时成立**，
        ///    所以观感正好是「卡片一接触容器上/下沿就开始滚」；而卡片被**本区**钉住时两半不同时成立 →
        ///    不滚（避免"卡片早停了、列表还在滚"）。
        /// ★ 视口高用 `ActualHeight`（条目制下 `ViewportHeight` 的单位是"行"，不能直接比）。
        /// ★★ 前提：列表必须是**像素级滚动**（`VirtualizingPanel.ScrollUnit="Pixel"`，见 XAML）。
        ///    条目制下每帧推进的几 DIP 会被面板**取整到整行**：往下滚取整回原行 = 一动不动，
        ///    往上滚取整成 −1 行 = 一帧跳 78px —— 用户报的"拖到底部不往下滚 / 往上飞速到顶"就是这个。
        /// </summary>
        private void AutoScrollStep(double dtMs)
        {
            if (_sortScroller == null || _sortDragCard == null) return;

            double viewH = _sortScroller.ActualHeight;
            if (viewH <= 1) return;
            if (Math.Abs(_sortMouseY - _sortStartMouseY) < 4) return;   // 只是按了一下、没真拖过 → 不滚

            double cardH = _sortDragCard.ActualHeight;
            if (cardH <= 0) return;

            // ★★ 判据分两半（都在 PinDraggedCardToMouse 里算好，这里不现量 —— 同一帧是
            //    **先滚、后摆卡**，现量会读到"还没重摆的旧位置"，触发一帧开一帧关）：
            //      ① **方向**看 `_sortPinTop`（= 光标想让卡片到哪，**未受本区钳制**）→ 这才是用户意图，
            //         两支互斥，不会出现"往下拖却往上滚"；
            //      ② **许可**看 `_sortCardViewportTop`（= 卡片实际到哪，受本区钳制）→ 卡片真的贴到
            //         容器上/下沿才算（被本区钉住时不算，避免"卡片早停了列表还在滚"）。
            //    ★ 因为卡片位置 = 光标 − 抓取偏移，"卡片抓取点贴到容器边"就等价于
            //      "光标推到了那一侧" → 正好是用户要的「卡片一接触容器上/下沿就开始滚」。
            double pinTop = _sortPinTop;
            double cardTop = _sortCardViewportTop;
            double cardBottom = cardTop + cardH;

            const double touch = 1.0;        // 贴边容差（"接触到"就算）
            const double edge = 30.0;        // 只用来算"推得多深 → 多快"

            bool pushingUp = pinTop < 0;                // 光标把卡片往上推出容器
            bool pushingDown = pinTop + cardH > viewH;  // 往下推出容器
            bool atTop = cardTop <= touch;              // 卡片真的贴在容器上沿
            bool atBottom = cardBottom >= viewH - touch;// 卡片真的贴在容器下沿

            double dir;
            double mouseDepth;
            if (pushingUp && atTop)
            {
                dir = -1;
                mouseDepth = Math.Max(0, edge - _sortMouseY);            // 鼠标越往顶部探越快
            }
            else if (pushingDown && atBottom)
            {
                dir = 1;
                mouseDepth = Math.Max(0, _sortMouseY - (viewH - edge));  // 鼠标越往底部探越快
            }
            else
            {
                if (_sortScrollLastDir != 0)
                {
                    _sortScrollLastDir = 0;
                    SortLog($"→ 自动滚动 停 卡顶={cardTop:F0} 卡底={cardBottom:F0} 视口高={viewH:F0} 鼠标={_sortMouseY:F0}");
                }
                return;
            }

            // ★ 要开始跨屏滚动了：先把全部行实例化（被拖卡的行容器一旦滚出可见区就会被
            //   虚拟化回收，卡片会突然归位）。只在这一刻付这笔开销，短距离拖动完全不付。
            EnsureSortCardsWarmed();

            // 只在方向翻转时记一行（逐帧写会把日志刷爆）：万一还不对，这行能直接看出卡在哪个判断上
            if (dir != _sortScrollLastDir)
            {
                _sortScrollLastDir = dir;
                SortLog($"→ 自动滚动 dir={dir} 卡顶={cardTop:F0} 卡底={cardBottom:F0} 视口高={viewH:F0} " +
                        $"鼠标={_sortMouseY:F0} 深度={mouseDepth:F0} offset={_sortScroller.VerticalOffset:F1}");
            }

            // 速度：贴边 250 DIP/秒（≈3 行/秒）起，鼠标往那一侧推得越深越快，封顶 1000 DIP/秒（≈13 行/秒）。
            // （2026-10-14 曾为了"感觉慢"临时提到 800~2600，用户确认"自动滚动本来正常"→ 已改回。）
            double stepDips = Math.Min(1000.0, 250.0 + mouseDepth * 4.0) * dtMs / 1000.0;
            if (stepDips < 0.05) return;

            double target = _sortScroller.VerticalOffset + dir * stepDips / ScrollUnit();
            _sortScroller.ScrollToVerticalOffset(Math.Max(0, Math.Min(_sortScroller.ScrollableHeight, target)));
        }

        /// <summary>
        /// ScrollViewer 的 offset 单位折算成 DIP：像素制 = 1，条目制 = 一行（≈<see cref="_sortPitch"/>）。
        /// ★ 判据用「`ScrollableHeight` 是几」：条目制下它的单位是**行**、值必然 ≤ 条目数；
        ///   像素制下它是 DIP、值 ≈ 内容总高 − 视口高（远大于条目数）。
        ///   ⛔ 别用 `CanContentScroll` 判：`VirtualizingPanel.ScrollUnit=Pixel` 时它仍是 true，
        ///      但 offset 已经是像素了（判错 → 滚动慢 78 倍）。
        ///   ⛔ 也别用 `(ExtentHeight − ViewportHeight) / ScrollableHeight`：条目制下这三个量
        ///      **全都是"行"**，除出来恒等于 1（2026-10-14 踩过）。
        /// </summary>
        private double ScrollUnit()
        {
            if (_sortScroller == null) return 1;

            double units = _sortScroller.ScrollableHeight;
            if (units <= 0.5) return 1;

            int count = _sortItems.Count;
            if (count > 0 && units <= count + 2)
                return _sortPitch > 0.5 ? _sortPitch : 1;    // 条目制：1 单位 = 一行

            return 1;                                        // 像素制：1 单位 = 1 DIP
        }

        /// <summary>
        /// 邻卡让位：只按「原下标 / 目标下标」算（纯函数 → 确定性），且只作用于**已实例化的容器**。
        /// ★ 只在**目标真变化**时才重设动画；动画**不写 From**（WPF 从当前值接着跑，不跳）。
        /// </summary>
        private void ApplyNeighborShifts()
        {
            if (_sortPanel == null) return;

            foreach (var child in _sortPanel.Children)
            {
                if (child is not FrameworkElement fe) continue;
                if (ReferenceEquals(fe, _sortDragCard)) continue;

                int idx = _sortItems.IndexOf(fe.DataContext!);
                if (idx < 0) continue;

                double target = 0;
                if (_sortFrom < _sortTo && idx > _sortFrom && idx <= _sortTo) target = -_sortPitch;
                else if (_sortFrom > _sortTo && idx >= _sortTo && idx < _sortFrom) target = _sortPitch;

                double cur = _sortShiftTargets.TryGetValue(fe, out var t) ? t : 0;
                if (Math.Abs(cur - target) < 0.5) continue;    // 目标没变 → 别打断正在跑的动画
                _sortShiftTargets[fe] = target;

                var tr = EnsureSortTransform(fe);
                tr.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(target, TimeSpan.FromMilliseconds(170))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        private static bool HasSortStar(object item) => item switch
        {
            Account a => a.ShowStar,
            GroupDisplayItem g => g.ShowStar,
            _ => false
        };

        private void OnSortDragMouseUp(object sender, MouseButtonEventArgs e) => FinishSortDrag();

        private void FinishSortDrag()
        {
            if (!_sortDragging || _sortDropping) return;
            _sortDropping = true;

            CompositionTarget.Rendering -= OnSortDragFrame;

            if (_sortDragTr == null || _sortTo == _sortFrom)
            {
                CommitSortDrag();
                return;
            }

            // 松手：被拖卡 150ms 滑到目标槽（不写 From → 从当前值接着跑）
            double finalY = (_sortTo - _sortFrom) * _sortPitch;
            var anim = new DoubleAnimation(finalY, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            anim.Completed += (s, e) => CommitSortDrag();
            _sortDragTr.BeginAnimation(TranslateTransform.YProperty, anim);

            // 兜底：Completed 没触发也要收尾。
            // ★ 必须带**拖动序号**校验：这 400ms 内用户要是已经开始了下一次拖动，
            //   这个迟到的兜底会把新的一次拖动掐掉（表现就是"鼠标没松手却退出了拖动模式"）。
            int token = _sortDragToken;
            _ = Task.Run(async () =>
            {
                await Task.Delay(400);
                try { await Dispatcher.InvokeAsync(() => { if (token == _sortDragToken) CommitSortDrag(); }); } catch { }
            });
        }

        /// <summary>收尾：写数据（按新显示顺序编号 0,1,2…）+ 清痕迹 + 就地 Move 刷新。</summary>
        private void CommitSortDrag()
        {
            if (!_sortDragging) return;
            _sortDragging = false;
            _sortDropping = false;

            CompositionTarget.Rendering -= OnSortDragFrame;

            // 松手后鼠标还压在手柄上（还想接着拖）→ 保持预热状态，别急着还原
            bool handleStillHovered = _sortHandle?.IsMouseOver == true;

            if (_sortHandle != null)
            {
                _sortHandle.MouseMove -= OnSortDragMouseMove;
                _sortHandle.MouseLeftButtonUp -= OnSortDragMouseUp;
                if (_sortHandle.IsMouseCaptured) _sortHandle.ReleaseMouseCapture();
                _sortHandle = null;
            }
            if (_sortWindow != null)
            {
                _sortWindow.PreviewMouseMove -= OnSortDragMouseMove;
                _sortWindow.PreviewMouseLeftButtonUp -= OnSortDragMouseUp;
                _sortWindow.Deactivated -= OnSortDragWindowDeactivated;
                _sortWindow = null;
            }
            if (_sortPanel != null) _sortPanel.Cursor = Cursors.Arrow;

            if (_sortDirty && _sortTo != _sortFrom && _sortItems.Count > 0)
            {
                var order = new List<object>(_sortItems);
                var moved = order[_sortFrom];
                order.RemoveAt(_sortFrom);
                order.Insert(_sortTo, moved);

                if (_sortIsGroup)
                {
                    var groups = order.OfType<GroupDisplayItem>()
                        .Select(g => _allGroups.FirstOrDefault(x => x.Id == g.Id))
                        .Where(g => g != null).Select(g => g!).ToList();
                    SortingService.ApplyDisplayOrder(groups);
                    // ★★ 一次性批量落盘（逐个 SaveGroup 每次都会「读全表 → 改一条 → 写全表」）
                    GroupDataService.SaveGroups(_allGroups);
                }
                else
                {
                    var accounts = order.OfType<Account>()
                        .Select(a => _allAccounts.FirstOrDefault(x => x.Id == a.Id))
                        .Where(a => a != null).Select(a => a!).ToList();
                    SortingService.ApplyDisplayOrder(accounts);
                    // ★★ 一次性批量落盘（逐个 SaveAccount = 100+ 次磁盘读写 + 加解密 → 松手卡死）
                    AccountDataService.SaveAccounts(_allAccounts);
                }
            }

            bool reordered = _sortDirty && _sortTo != _sortFrom;

            // 清痕迹（拖动中碰过的容器都还原）
            if (_sortPanel != null)
            {
                foreach (var child in _sortPanel.Children)
                {
                    if (child is FrameworkElement fe)
                    {
                        fe.RenderTransform = null;
                        Panel.SetZIndex(fe, 0);
                    }
                }
            }

            _sortItems = new();
            _sortTrs.Clear();
            _sortShiftTargets.Clear();
            _sortDragTr = null;
            _sortDragCard = null;
            _sortPanel = null;
            _sortScroller = null;
            _sortDirty = false;
            SortLog($"→ 收尾 from={_sortFrom} to={_sortTo}");

            if (_sortIsGroup)
            {
                UpdateGroupsList();
            }
            else if (reordered && _sortFrom >= 0 && _sortTo >= 0
                     && _sortFrom < _accounts.Count && _sortTo < _accounts.Count)
            {
                // ★ 就地 Move 一次就够（其余卡片相对顺序不变）→ **不重建行容器**，也不跳位。
                _accounts.Move(_sortFrom, _sortTo);
                accountsDataGrid?.UpdateLayout();
                RefreshAccountRowSpacing();   // 排序换了「最后一张」的人选
            }
            else
            {
                UpdateAccountsList(searchTextBox.Text);
            }

            // 拖完了 → 把"关掉的行虚拟化"还回去（51 行常驻会拖慢平时的一切鼠标交互）
            if (!handleStillHovered) ScheduleCoolDown();
        }

        #endregion

        #region 头像点击（置顶 / 标记 / 移入模式下选中）
        /// <summary>
        /// 点卡片里的头像：
        /// 主账号列表 → 置顶/取消置顶；分组内 → 标记/取消标记；移入模式 → 选中/取消选中。
        /// 前两种直接转调原按钮的处理器，保证和点按钮的效果一字不差。
        /// </summary>
        private void AccountAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int accountId) return;

            var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return;

            // 移入模式：和「点卡片任意处」同一个动作
            if (_isMoveMode)
            {
                ToggleMoveSelection(account);
                return;
            }

            if (_selectedGroupId > 0) MarkButton_Click(button, e);   // 分组内 = 标记
            else PinButton_Click(button, e);                         // 主列表 = 置顶
        }
        #endregion

        #region 置顶功能
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int accountId && _selectedGroupId == 0)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                if (account == null) return;

                bool willPin = !account.IsPinned;
                ApplyStarToggle(accountId, willPin, () => CommitPinToggle(account, accountId));
            }
            else if (_selectedGroupId > 0)
            {
                _toastManager.Show("提示", "置顶功能仅在主账号列表可用", false);
            }
        }

        /// <summary>真正落地一次置顶切换（改数据 + 存盘 + 提示 + 重载并刷新列表）。</summary>
        private void CommitPinToggle(Account account, int accountId)
        {
            account.SetPinned(!account.IsPinned);

            // 自由排序：置顶的排到置顶区末尾，取消置顶的排到普通区最上方（紧贴置顶区下方）
            SortingService.AssignPinOrder(_allAccounts, account);

            AccountDataService.SaveAccount(account);

            string message = account.IsPinned
                ? $"账号「{account.Nickname}」已置顶"
                : $"账号「{account.Nickname}」已取消置顶";
            _toastManager.Show(account.IsPinned ? "已置顶" : "已取消置顶", message);

            // ★ 这里**不能**再 `_allAccounts = AccountDataService.LoadAccounts()`：
            //   那是把 51 个账号重新读盘 + 反序列化 + 解密，全在 UI 线程上跑，
            //   点一次置顶就卡住几百毫秒（鼠标都动不了）。改动的就是内存里这个对象，不需要重读。
            // 照前端：改完数据用 FLIP 平滑滑到置顶区/普通区，不瞬间跳位；**就地 Move，不重建容器**
            SmoothReorderAccountInPlace(account);
        }

        /// <summary>
        /// 分组的文件夹头像：和主列表点头像一样，= 置顶 / 取消置顶。
        /// （是否置顶看卡片左缘那条蓝色竖条。）
        /// </summary>
        private void GroupAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int groupId) return;
            if (_allGroups == null || _allGroups.Count == 0) return;
            ToggleGroupPin(groupId);
        }

        /// <summary>
        /// 分组置顶切换：**先播完动画，卡片再挪位置**（和账号卡片的置顶完全一致）。
        /// 置顶的分组会排到最前面、取消的回到原来的位置 —— 那次重排发生在 commit 里，
        /// 而 commit 被挂在动画的 Completed 上（见 <see cref="ApplyStarToggle(bool, Action, Func{DependencyObject}, Action, Action)"/>）。
        /// </summary>
        private void ToggleGroupPin(int groupId)
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return;

            bool willPin = !group.IsPinned;

            ApplyGroupStarToggle(groupId, willPin, () =>
            {
                group.SetPinned(!group.IsPinned);

                // 自由排序：置顶的排到置顶分组区末尾，取消置顶的排到普通分组区最上方
                SortingService.AssignGroupPinOrder(_allGroups, group);

                bool success = GroupDataService.SaveGroup(group);

                string action = group.IsPinned ? "已置顶" : "已取消置顶";
                string message = group.IsPinned
                    ? $"分组「{group.Name}」已置顶"
                    : $"分组「{group.Name}」已取消置顶";

                _toastManager.Show(action, message);

                // ★★ 这里**只排序、绝不能调 `SortGroups()`**：
                //   `SortGroups()` 内部还会再调一次 `UpdateGroupsList()` —— 那等于在 FLIP 记"旧位置"
                //   之前就把列表刷成新顺序了 → 旧位置 == 新位置 → 位移算出来是 0 → 不播动画，
                //   而那次"瞬移"就发生在 SortGroups() 里（分组置顶一直瞬移就是栽在这）。
                if (_allGroups != null && _allGroups.Count > 0)
                    _allGroups = _sortingService.SortGroups(_allGroups);

                if (_currentDisplayMode == DisplayMode.GroupManagement)
                    // 和账号**完全同一套**：就地 Move + FLIP（容器不重建 → 手感一致）
                    SmoothReorderGroupInPlace(group);
            });
        }
        #endregion

        #region 平滑重排（FLIP，照前端 smoothReorder）
        //
        // 照前端 `smoothReorder`：置顶/取消置顶（含分组、分组内置底）后**不再是瞬间跳位**，
        // 而是——记下每张卡的旧位置 → 按新排序刷新 → 把每张卡从「旧位置」用 300ms
        // `CubicEase(EaseOut)`（≈ 前端 `cubic-bezier(.215,.61,.355,1)`）滑到「新位置」。
        //
        // ★★ 两个版本，按代价选：
        //   · `SmoothReorderAccountInPlace`（**账号置顶/置底走这条**）：只把变动的那一项在
        //     `_accounts` 里 `Move` 到位 —— **不重建行容器**。整表刷新会重建 51 个行容器
        //     （行模板很重：一堆 Path/Border/头像），在 UI 线程上一卡就是几百毫秒
        //     （用户实测："星标播完后卡一下才移动、期间鼠标都动不了"），所以账号必须走这条。
        //   · `SmoothReorder`（分组列表用）：分组只有二十来个、卡片也轻，整表刷新可接受。
        // ★ 刷新会重建容器，所以新旧位置一律**按数据项 Id** 对应（前端能直接拿 DOM 元素当 key）。
        //

        /// <summary>账号（置顶 / 取消置顶 / 置底）：**就地 Move** + FLIP，不重建容器 → 不卡。</summary>
        private void SmoothReorderAccountInPlace(Account changed)
        {
            if (_reordering) return;

            var before = CaptureCardTops();
            if (before.Count > 1)
            {
                // 一次 Move 就够：置顶/置底只改被点那一个的 Order，其余相对顺序完全不变
                int oldIndex = _accounts.IndexOf(changed);
                int newIndex = GetFilteredAccounts(searchTextBox.Text).FindIndex(a => a.Id == changed.Id);
                if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                    _accounts.Move(oldIndex, newIndex);
            }

            accountsDataGrid?.UpdateLayout();     // 催布局（只是重新排列，不重建容器 → 很快）
            RunFlip(before);
            RefreshAccountRowSpacing();           // 置顶换了「最后一张」的人选
        }

        /// <summary>
        /// 分组（置顶 / 取消置顶）：**就地 Move** + FLIP —— 和账号**完全同一套做法**
        /// （容器不重建 → 位置准确、时序一致，不再有"整表重建"造成的手感差异）。
        /// </summary>
        private void SmoothReorderGroupInPlace(AccountGroup changed)
        {
            if (_reordering) return;

            var before = CaptureCardTops();
            if (before.Count > 1 && _groupDisplayItems.Count > 0)
            {
                // 那颗星要跟着置顶状态走（取消置顶时不走"预告"分支，必须在这里改）
                var item = _groupDisplayItems.FirstOrDefault(x => x.Id == changed.Id);
                if (item != null) { item.ShowStar = changed.IsPinned; item.IsPinned = changed.IsPinned; }

                // 数据层新顺序（`_allGroups` 已排好）里它排第几，就 Move 到第几
                int oldIndex = -1;
                for (int i = 0; i < _groupDisplayItems.Count; i++)
                    if (_groupDisplayItems[i].Id == changed.Id) { oldIndex = i; break; }
                int newIndex = _allGroups.FindIndex(g => g.Id == changed.Id);
                if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex && newIndex < _groupDisplayItems.Count)
                    _groupDisplayItems.Move(oldIndex, newIndex);
            }

            GetGroupsItemsControl()?.UpdateLayout();
            RunFlip(before);
            RefreshGroupCardSpacing();             // 置顶换了「最后一张」的人选
        }

        /// <summary>
        /// 整表刷新 + FLIP（兜底路径用）。
        /// ★★ 关键：刷新会**重建容器**，此时在同一帧里量的"新位置"往往还是**布局没跑完的旧值**
        ///    → 算出来位移是 0 → 不播动画 → 等真实布局一跑就"瞬移"。
        ///    所以挂一次 `LayoutUpdated`（布局真正结束、且在渲染之前触发）再量、再播 FLIP。
        /// </summary>
        private void SmoothReorder(Action refresh)
        {
            if (_reordering) { refresh(); return; }

            var before = CaptureCardTops();
            if (before.Count <= 1) { refresh(); return; }

            refresh();

            var target = (_currentDisplayMode == DisplayMode.GroupManagement
                ? (FrameworkElement?)GetGroupsItemsControl()
                : accountsDataGrid) ?? this;

            bool done = false;
            void Finish()
            {
                if (done) return;
                done = true;
                target.LayoutUpdated -= OnLayoutUpdated;
                RunFlip(before);
                // 整表刷新会重建容器 ⇒ 底边距回到样式默认值，重算一次
                if (_currentDisplayMode == DisplayMode.GroupManagement) RefreshGroupCardSpacing();
                else RefreshAccountRowSpacing();
            }
            void OnLayoutUpdated(object? s, EventArgs e) => Finish();

            target.LayoutUpdated += OnLayoutUpdated;
            target.UpdateLayout();                                        // 立刻催一次（多数情况这一下就够）
            Dispatcher.BeginInvoke(new Action(Finish), DispatcherPriority.Render);   // 兜底
        }

        /// <summary>FLIP 主体：把每张卡从 <paramref name="before"/> 里的旧位置 300ms 缓出滑到当前位置。</summary>
        private void RunFlip(Dictionary<int, (FrameworkElement card, double top)> before)
        {
            if (before.Count == 0) return;

            var after = CaptureCardTops();
            if (after.Count == 0) return;

            var cards = new List<(TranslateTransform tr, FrameworkElement card)>();
            foreach (var kv in after)
            {
                if (!before.TryGetValue(kv.Key, out var oldVal)) continue;
                double delta = oldVal.top - kv.Value.top;     // FLIP 的 Invert：先摆回旧位置
                if (Math.Abs(delta) < 0.5) continue;

                var tr = kv.Value.card.RenderTransform as TranslateTransform ?? new TranslateTransform();
                kv.Value.card.RenderTransform = tr;
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                tr.Y = delta;
                cards.Add((tr, kv.Value.card));
            }

            SortLog($"RunFlip: 旧位置{before.Count} 张 / 新位置{after.Count} 张 / 需要动画{cards.Count} 张");

            if (cards.Count == 0) { _reordering = false; return; }

            _reordering = true;                              // 期间忽略新的置顶/置底点击（照前端）

            int left = cards.Count;
            Action done = () =>
            {
                if (--left > 0) return;
                _reordering = false;
                foreach (var (tr, card) in cards)
                {
                    try
                    {
                        tr.BeginAnimation(TranslateTransform.YProperty, null);
                        tr.Y = 0;
                        card.RenderTransform = null;
                    }
                    catch { }
                }
            };

            foreach (var (tr, _) in cards)
            {
                var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                anim.Completed += (s, e) => done();
                tr.BeginAnimation(TranslateTransform.YProperty, anim);
            }

            // 兜底：Completed 万一没触发也要解锁（否则置顶/置底会被永久忽略）
            _ = Task.Run(async () =>
            {
                await Task.Delay(700);
                try { await Dispatcher.InvokeAsync(() => { _reordering = false; }); } catch { }
            });
        }

        /// <summary>量出当前列表里每张卡的视口顶（按数据项 Id 索引），供 FLIP 对应新旧位置。</summary>
        private Dictionary<int, (FrameworkElement card, double top)> CaptureCardTops()
        {
            var map = new Dictionary<int, (FrameworkElement, double)>();
            try
            {
                // ★ 虚拟化下 index 0 的容器可能没生成（滚到下面去了）→ 找**第一个已实例化**的容器
                FrameworkElement? first = null;
                if (_currentDisplayMode == DisplayMode.GroupManagement)
                {
                    var ic = GetGroupsItemsControl();
                    if (ic == null) return map;
                    foreach (var it in ic.Items)
                        if (ic.ItemContainerGenerator.ContainerFromItem(it) is FrameworkElement fe) { first = fe; break; }
                }
                else
                {
                    if (accountsDataGrid == null) return map;
                    for (int i = 0; i < accountsDataGrid.Items.Count; i++)
                        if (accountsDataGrid.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe) { first = fe; break; }
                }
                if (first == null) return map;

                var panel = VisualTreeHelper.GetParent(first) as Panel;
                var scroller = FindAncestor<ScrollViewer>(first);
                if (panel == null || scroller == null) return map;

                foreach (var child in panel.Children)
                {
                    if (child is not FrameworkElement fe) continue;
                    double top;
                    try { top = fe.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y; }
                    catch { continue; }

                    if (fe.DataContext is Account a) map[a.Id] = (fe, top);
                    else if (fe.DataContext is GroupDisplayItem g) map[g.Id] = (fe, top);
                }
            }
            catch { }
            return map;
        }
        #endregion

        #region 账号管理事件
        private void AddAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDisplayMode != DisplayMode.AccountList)
                SwitchToDisplayMode(DisplayMode.AccountList);
            ShowAccountModal();
        }

        private void EditAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    ShowAccountModal(account);
            }
        }

        private void DeleteAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    ShowDeleteModal(account);
            }
        }

        private void RemoveFromGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int accountId)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                {
                    var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                    if (account != null)
                    {
                        _selectedAccountIds = new List<int> { accountId };
                        ShowRemoveFromGroupModal(group, account);
                    }
                }
            }
        }

        #endregion

        #region 分组管理事件
        private void GroupManagementButton_Click(object sender, RoutedEventArgs e)
        {
            _allGroups = GroupDataService.LoadGroups();
            SortGroups();
            SwitchToDisplayMode(DisplayMode.GroupManagement);
            Dispatcher.BeginInvoke(() =>
            {
                UpdateGroupsList();
                UpdateGroupInfoDisplay();
            }, DispatcherPriority.Render);
        }

        private void CreateGroupButton_Click(object sender, RoutedEventArgs e)
        {
            ShowGroupModal();
        }

        private void EditGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == id);
                if (group != null)
                    ShowGroupModal(group);
            }
        }

        private void OpenGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == id);
                if (group != null)
                {
                    var sw = Stopwatch.StartNew();
                    _selectedGroupId = id;
                    SelectedGroupId = id;
                    UpdateAccountsList();
                    SwitchToDisplayMode(DisplayMode.AccountList);
                    UpdateGroupInfoDisplay();
                    _viewStateManager.SwitchToView(ViewStateManager.AppView.GroupDetail, group);
                    SortLog($"→ 打开分组「{group.Name}」耗时 {sw.ElapsedMilliseconds}ms（{group.AccountIds.Count} 个账号）");
                }
            }
        }

        private void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == id);
                if (group != null)
                    ShowDeleteGroupModal(group);
            }
        }

        private void OnAddGroupButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                AccountGroup group = button.Tag as AccountGroup;

                if (group == null && _selectedGroupId > 0)
                {
                    group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                }

                if (group != null)
                {
                    // 不再另开窗口：直接在本页进入「移入账号」模式
                    EnterMoveMode(group.Id);
                }
            }
        }
        #endregion

        #region 移入账号模式
        /// <summary>
        /// 进入「移入账号」模式：借用账号列表页面 ——
        /// 顶部信息换成移入统计，底部只留「移入 / 取消」，
        /// 卡片右侧的登录按钮换成选择框，点卡片任意处即选中。
        /// </summary>
        private void EnterMoveMode(int groupId)
        {
            // ★ 不再重新读盘：`_allAccounts/_allGroups` 就是权威内存副本（每次改动都已 SaveXxx + 同步内存），
            //   而 `LoadAccounts + LoadGroups` 是"读文件 + JSON + 逐个解密"，全在 UI 线程上（日志约 27ms）。
            var sw = Stopwatch.StartNew();

            _moveTargetGroupId = groupId;
            _isMoveMode = true;

            // 清掉搜索与上一次的勾选
            searchTextBox.Text = string.Empty;
            clearSearchButton.Visibility = Visibility.Collapsed;
            watermarkText.Visibility = Visibility.Visible;
            foreach (var account in _allAccounts)
            {
                account.IsSelectedForMove = false;
                account.IsMoveable = true;
            }

            // 通知卡片模板切换按钮/选择框
            OnPropertyChanged(nameof(IsMoveMode));

            // 按钮区只留「移入 / 取消」
            _viewStateManager.SwitchToView(ViewStateManager.AppView.MoveMode);

            UpdateAccountsList(string.Empty);
            SwitchToDisplayMode(DisplayMode.AccountList);
            UpdateGroupInfoDisplay();
            SortLog($"→ 进入移入模式 耗时 {sw.ElapsedMilliseconds}ms（账号 {_allAccounts.Count} 条）");
        }

        /// <summary>退出移入模式，回到进入前的那个分组内。</summary>
        private void ExitMoveMode()
        {
            int groupId = _moveTargetGroupId;
            var sw = Stopwatch.StartNew();

            _isMoveMode = false;
            _moveTargetGroupId = 0;
            OnPropertyChanged(nameof(IsMoveMode));

            foreach (var account in _allAccounts)
            {
                account.IsSelectedForMove = false;
                account.IsMoveable = true;
            }

            // 回到之前的分组内。
            // ★ 这里**必须**重读分组：移入账号（UpdateGroupAccounts）是"读全表 → 改那条分组 → 写全表"，
            //   没有回写 `_allGroups` 里那一份，不重读的话返回分组内看不到刚移入的账号。
            _selectedGroupId = groupId;
            SelectedGroupId = groupId;
            _allGroups = GroupDataService.LoadGroups();
            UpdateAccountsList(searchTextBox.Text);
            SwitchToDisplayMode(DisplayMode.AccountList);
            UpdateGroupInfoDisplay();
            SortLog($"→ 退出移入模式 耗时 {sw.ElapsedMilliseconds}ms");
        }

        private void MoveConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            var ids = _allAccounts.Where(a => a.IsSelectedForMove).Select(a => a.Id).ToList();

            if (ids.Count == 0)
            {
                _toastManager.Show("请选择要移入分组的账号", false);
                return;
            }

            if (UpdateGroupAccounts(_moveTargetGroupId, ids))
                _toastManager.Show("操作成功", $"已成功将 {ids.Count} 个账号移入分组");
            else
                _toastManager.Show("操作失败", "移入账号失败", false);

            ExitMoveMode();
        }

        private void MoveCancelButton_Click(object sender, RoutedEventArgs e) => ExitMoveMode();

        /// <summary>切换某条账号的勾选状态（已在分组里的不响应）。</summary>
        private void ToggleMoveSelection(Account account)
        {
            if (!_isMoveMode || account == null || !account.IsMoveable) return;

            account.IsSelectedForMove = !account.IsSelectedForMove;
            UpdateMoveModeInfo();
        }

        /// <summary>移入模式下点右侧的选择框（等效于点卡片任意处）。</summary>
        private void MoveSelectBoxButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Account account)
            {
                ToggleMoveSelection(account);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 移入模式下点卡片任意处 = 选中/取消选中。
        /// 在 Preview（隧道路由）阶段处理并标记 Handled，阻止 DataGrid 自己选中整行；
        /// 点在内层按钮（选择框/复制/眼睛…）上时直接放行，交给按钮自己处理。
        /// </summary>
        private void AccountsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isMoveMode) return;

            var source = e.OriginalSource as DependencyObject;

            if (FindAncestor<Button>(source) != null) return;

            var row = FindAncestor<DataGridRow>(source);
            if (row?.Item is Account account)
            {
                ToggleMoveSelection(account);
                e.Handled = true;
            }
        }

        /// <summary>把账号批量加入分组（保留原「移入账号」窗口的语义：不覆盖、只追加）。</summary>
        private bool UpdateGroupAccounts(int groupId, List<int> accountIds)
        {
            try
            {
                var groups = GroupDataService.LoadGroups();
                var group = groups.FirstOrDefault(g => g.Id == groupId);
                if (group == null) return false;

                foreach (var accountId in accountIds)
                {
                    if (!group.AccountIds.Contains(accountId))
                    {
                        group.AccountIds.Add(accountId);
                    }
                }

                return GroupDataService.SaveGroup(group);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"移入分组账号失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>沿视觉树往上找第一个指定类型的祖先（含自身）。</summary>
        private static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null)
            {
                if (node is T typed) return typed;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return null;
        }
        #endregion

        #region 返回按钮事件
        private void NoDataBackButton_Click(object sender, RoutedEventArgs e)
        {
            searchTextBox.Text = string.Empty;
            clearSearchButton.Visibility = Visibility.Collapsed;
            watermarkText.Visibility = Visibility.Visible;

            UpdateWatermarkVisibility();
        }

        private void BackToAccountListButton_Click(object sender, RoutedEventArgs e)
        {
            bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
            bool hasAccountsInTotal = _allAccounts.Count > 0;

            if (hasSearchText && hasAccountsInTotal)
            {
                searchTextBox.Text = string.Empty;
                clearSearchButton.Visibility = Visibility.Collapsed;
                watermarkText.Visibility = Visibility.Visible;
            }

            _selectedGroupId = 0;
            SelectedGroupId = 0;
            UpdateAccountsList(searchTextBox.Text);

            if (_currentDisplayMode == DisplayMode.GroupManagement)
                SortGroups();

            SwitchToDisplayMode(DisplayMode.AccountList);
            UpdateWatermarkVisibility();
        }

        private void ReturnToGroupList_Click(object sender, RoutedEventArgs e)
        {
            SwitchToDisplayMode(DisplayMode.GroupManagement);
        }
        #endregion

        #region DataGrid 事件
        private void DataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            e.Cancel = true;
        }

        /// <summary>
        /// 滚轮：一格 = **一张卡片**（所有「卡片列表」统一这一步进，见 <see cref="DataGrid_PreviewMouseWheel"/>）。
        /// <para>用于普通 ScrollViewer 包着的卡片列表（比如分组列表）：这类默认是物理滚动，一格只走 48px。</para>
        /// </summary>
        private void CardList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            e.Handled = true;

            sv.ScrollToVerticalOffset(sv.VerticalOffset + (e.Delta > 0 ? -CardRowPitch : CardRowPitch));
        }

        /// <summary>
        /// 一张卡片占的高度：**卡片 Height=70 + 卡片间距 8**。
        /// 本模块三处列表（账号卡、分组卡）和主程序首页的功能卡都是这个尺寸，所以共用一个值。
        /// </summary>
        private const double CardRowPitch = 78;

        /// <summary>
        /// 滚轮：一格滚动**一行**（原来的手感，别动）。
        /// ★ 2026-10-14 以前这里是写死的 `+1` 个单位 —— 那时列表是**条目制**，1 个单位正好是 1 行。
        ///   后来为了拖动自动滚动能平滑推进，列表改成了**像素制**
        ///   （`VirtualizingPanel.ScrollUnit="Pixel"`）→ 1 个单位变成 1 个 DIP，
        ///   滚动慢问题见下方 ★★：
        /// ★★ 2026-10-21：曾改成「48px/刻度」和首页统一，用户实测后说效果不好，**已回退**
        ///   （改动本身就在下面两行，注释留着提醒别再走这条路）。
        /// </summary>
        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (sender is not DataGrid dataGrid) return;

            var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
            if (scrollViewer == null) return;

            // 一行的实际高度 + 行间距（直接量已实例化的行容器；关掉虚拟化后第 0 行一定在）
            double oneRow = CardRowPitch;   // 兜底：卡片 70 + 卡片间 8
            if (dataGrid.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement row
                && row.ActualHeight > 1)
                oneRow = row.ActualHeight + row.Margin.Top + row.Margin.Bottom;

            /* 判单位：像素制下 `ScrollableHeight` 是 DIP（远大于条目数）；条目制下它是"行数" */
            int count = dataGrid.Items.Count;
            double units = scrollViewer.ScrollableHeight;
            bool pixelMode = count <= 0 || units > count + 2;

            /* ★ 2026-10-21 试过改成 48px/刻度（和首页物理滚动同手感），用户试后反馈「效果不好」，
                 已退回一格一行。⛔ 别再改成别的步长：真要做平滑请先把宿主首页那套也一起改一致。 */
            double step = pixelMode ? oneRow : 1;
            scrollViewer.ScrollToVerticalOffset(
                scrollViewer.VerticalOffset + (e.Delta > 0 ? -step : step));
        }
        #endregion

        #region 密码操作
        /// <summary>
        /// 账号那一行的「显示 / 隐藏」眼睛按钮。
        /// ★ 只管账号：它和密码是两个**独立开关**（各自有眼睛按钮），
        ///   以前两个按钮都去翻 IsPasswordVisible，点账号的眼睛会把密码一起露出来。
        /// </summary>
        private void ToggleUsernameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _accounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    account.IsUsernameVisible = !account.IsUsernameVisible;
            }
        }

        /// <summary>
        /// 密码那一行的「显示 / 隐藏」眼睛按钮。只管密码，见 <see cref="ToggleUsernameButton_Click"/>。
        /// </summary>
        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _accounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    account.IsPasswordVisible = !account.IsPasswordVisible;
            }
        }
        #endregion

        #region 复制操作
        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            try
            {
                var row = FindVisualParent<DataGridRow>(button);
                if (row?.DataContext is not Account account) return;

                // 账号列表已经从「多列」合并成「一张卡片」，
                // 不能再按 cell.Column.DisplayIndex 判断复制什么（那样永远是同一列、复制不到东西）。
                // 改成按按钮自己的名字判断，直接从这个账号的字段取值。
                (string textToCopy, string contentType) = button.Name switch
                {
                    "copyUidButton" => (account.Uid, "UID"),
                    "copyUsernameButton" => (account.Username, "账号"),
                    "copyPasswordButton" => (account.Password, "密码"),
                    _ => ("", "")
                };

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    NativeClipboardHelper.SafeSetText(textToCopy);
                    _toastManager.Show($"{contentType} 已复制", true);
                }
                else
                {
                    _toastManager.Show("没有可复制的内容", false);
                }
            }
            catch
            {
                _toastManager.Show("复制失败", false);
            }
        }
        #endregion

        #region 模态框特定操作
        private void ShowDeleteModal(Account account)
        {
            if (deleteConfirmText != null)
                deleteConfirmText.Text = $"确定要删除账号「{account.Nickname}」吗？此操作无法撤销。";

            _modalManager.ShowModal(deleteModal);
            _selectedAccountIds = new List<int> { account.Id };
        }

        private void ShowDeleteGroupModal(AccountGroup group)
        {
            if (deleteGroupConfirmText != null)
                deleteGroupConfirmText.Text = $"确定要删除分组「{group.Name}」吗？此操作无法撤销。";

            _modalManager.ShowModal(deleteGroupModal);
            _selectedGroupId = group.Id;
        }

        private void ShowRemoveFromGroupModal(AccountGroup group, Account account)
        {
            if (removeFromGroupConfirmText != null)
                removeFromGroupConfirmText.Text = $"确定要从分组「{group.Name}」中移出账号「{account.Nickname}」吗？";

            _modalManager.ShowModal(removeFromGroupModal);
        }

        private void HideDeleteModal() => _modalManager.HideModal(deleteModal);
        private void HideDeleteGroupModal() => _modalManager.HideModal(deleteGroupModal);
        private void HideRemoveFromGroupModal() => _modalManager.HideModal(removeFromGroupModal);

        private void ShowClearMarksModal(AccountGroup group, int markedCount)
        {
            _selectedGroupId = group.Id;

            if (clearMarksConfirmText != null)
                clearMarksConfirmText.Text = $"确定要清除分组「{group.Name}」中的所有置底标记吗？";
            if (clearMarksExtraText != null)
                clearMarksExtraText.Text = $"该分组中共有 {markedCount} 个已置底账号，清除后将全部恢复为未置底状态";

            _modalManager.ShowModal(clearMarksModal);
        }

        private void HideClearMarksModal()
        {
            _modalManager.HideModal(clearMarksModal);
            _selectedGroupId = 0;
        }
        #endregion

        #region 确认操作事件
        private void ConfirmDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccountIds.Count > 0)
            {
                bool success = AccountDataService.DeleteAccounts(_selectedAccountIds);

                if (success)
                {
                    _allAccounts.RemoveAll(a => _selectedAccountIds.Contains(a.Id));
                    UpdateAccountsList(searchTextBox.Text);

                    string message = _selectedAccountIds.Count == 1
                        ? "账号已成功删除"
                        : $"已成功删除 {_selectedAccountIds.Count} 个账号";

                    _toastManager.Show("操作成功", message);
                }
                else
                {
                    _toastManager.Show("操作失败", "删除账号失败", false);
                }

                HideDeleteModal();
            }
        }

        private void ConfirmDeleteGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroupId > 0)
            {
                bool success = GroupDataService.DeleteGroup(_selectedGroupId);

                if (success)
                {
                    _allGroups.RemoveAll(g => g.Id == _selectedGroupId);
                    SortGroups();
                    UpdateGroupInfoDisplay();

                    if (!_allGroups.Any())
                    {
                        _selectedGroupId = 0;
                        SelectedGroupId = 0;
                        if (_currentDisplayMode == DisplayMode.AccountList)
                        {
                            UpdateAccountsList(searchTextBox.Text);
                            SwitchToDisplayMode(DisplayMode.AccountList);
                        }
                    }
                    else if (_currentDisplayMode == DisplayMode.AccountList)
                    {
                        _selectedGroupId = 0;
                        SelectedGroupId = 0;
                        UpdateAccountsList(searchTextBox.Text);
                        SwitchToDisplayMode(DisplayMode.AccountList);
                    }

                    _toastManager.Show("操作成功", "分组已成功删除");
                }
                else
                {
                    _toastManager.Show("操作失败", "删除分组失败", false);
                }

                HideDeleteGroupModal();

                if (_currentDisplayMode == DisplayMode.GroupManagement)
                {
                    _allGroups = GroupDataService.LoadGroups();
                    SortGroups();
                    UpdateGroupInfoDisplay();
                    var groupsItemsControl = FindVisualChild<ItemsControl>(groupManagementMessage, "groupsItemsControl");
                    if (groupsItemsControl != null)
                    {
                        groupsItemsControl.ItemsSource = null;
                        UpdateGroupsList();
                    }
                    ShowGroupsListContainer(_allGroups.Count > 0);
                }
            }
        }

        private void ConfirmRemoveFromGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccountIds.Count > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                {
                    var account = _allAccounts.FirstOrDefault(a => a.Id == _selectedAccountIds[0]);
                    if (account != null && group.AccountIds.Contains(_selectedAccountIds[0]))
                    {
                        group.RemoveAccount(_selectedAccountIds[0]);
                        bool success = GroupDataService.SaveGroup(group);

                        if (success)
                        {
                            UpdateAccountListAfterRemove(group);
                            _toastManager.Show("操作成功", $"已从分组「{group.Name}」中移出账号「{account.Nickname}」");
                        }
                        else
                        {
                            _toastManager.Show("操作失败", "移出账号失败", false);
                        }
                    }
                    else
                    {
                        _toastManager.Show("操作失败", "账号不在该分组中", false);
                    }
                }

                HideRemoveFromGroupModal();
            }
        }

        private void UpdateAccountListAfterRemove(AccountGroup group)
        {
            var groupIndex = _allGroups.FindIndex(g => g.Id == group.Id);
            if (groupIndex >= 0)
                _allGroups[groupIndex] = group;

            UpdateAccountsList(searchTextBox.Text);
            UpdateGroupInfoDisplay();
        }

        /// <summary>
        /// 确认弹窗里的「确认清除」—— 2026-10-17 起这个弹窗**已经不再被触发**
        /// （按钮改成点一下直接清除，见 <see cref="ExecuteClearMarks"/>），这里保留只是为了兜底。
        /// </summary>
        private void ConfirmClearMarksButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroupId > 0)
            {
                ExecuteClearMarks(_selectedGroupId);
                HideClearMarksModal();
            }
        }
        #endregion

        #region 窗口模态框方法
        private void ShowModalWindow(Window window)
        {
            if (_isModalWindowOpen) return;

            _currentModalWindow = window;
            _isModalWindowOpen = true;

            _modalManager.ShowOverlay();

            window.Owner = Window.GetWindow(this);
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            window.Closed += ModalWindow_Closed;
            window.ShowDialog();
        }

        private void ModalWindow_Closed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= ModalWindow_Closed;
                _currentModalWindow = null;
                _isModalWindowOpen = false;
                _modalManager.HideOverlay();
            }
        }

        private void ShowAccountModal(Account account = null)
        {
            var window = new AddEditAccountWindow(account);

            window.ShowToastRequested += (title, message, isSuccess) => _toastManager.Show(title, message, isSuccess);

            // ★ 「清除异常」是**点了就落盘**的（不等点保存），所以这里同步刷新内存里那一份，
            //   否则卡片上的红标签要等到下次重新加载列表才会消失。
            window.AccountErrorCleared += clearedId =>
            {
                var inAll = _allAccounts.FirstOrDefault(a => a.Id == clearedId);
                if (inAll != null) { inAll.HasLoginError = false; inAll.LoginErrorText = string.Empty; }
                var inView = _accounts.FirstOrDefault(a => a.Id == clearedId);
                if (inView != null) { inView.HasLoginError = false; inView.LoginErrorText = string.Empty; }
                UpdateAccountsList(searchTextBox.Text);
            };

            window.Closed += (s, e) =>
            {
                if (window.DialogResult == true)
                    HandleAccountModalResult(window, account);
            };

            ShowModalWindow(window);
        }

        private void HandleAccountModalResult(AddEditAccountWindow window, Account originalAccount)
        {
            bool success;
            string message;

            if (originalAccount != null)
            {
                success = AccountDataService.SaveAccount(window.Account);
                message = success ? "账号已成功更新" : "更新账号失败";

                if (success)
                {
                    var index = _allAccounts.FindIndex(a => a.Id == originalAccount.Id);
                    if (index >= 0)
                        _allAccounts[index] = window.Account;
                    _allAccounts = AccountDataService.LoadAccounts();
                }
            }
            else
            {
                window.Account.Id = AccountDataService.GetNextId();
                // 新账号：排到普通区最末尾（Order = 全局最大值 + 1）
                window.Account.Order = SortingService.NextOrder(_allAccounts, a => a.Order);
                success = AccountDataService.SaveAccount(window.Account);
                message = success ? "账号已成功添加" : "添加账号失败";

                if (success)
                    _allAccounts = AccountDataService.LoadAccounts();
            }

            if (success)
            {
                UpdateAccountsList(searchTextBox.Text);
                SwitchToDisplayMode(_allAccounts.Count > 0 ? DisplayMode.AccountList : DisplayMode.NoData);
                _toastManager.Show("操作成功", message);
            }
            else
            {
                _toastManager.Show("操作失败", message, false);
            }
        }

        private void ShowGroupModal(AccountGroup group = null)
        {
            var window = new AddEditGroupWindow(group);

            window.ShowToastRequested += (title, message, isSuccess) => _toastManager.Show(title, message, isSuccess);

            window.Closed += (s, e) =>
            {
                if (window.DialogResult == true)
                    HandleGroupModalResult(window, group);
            };

            ShowModalWindow(window);
        }

        private void HandleGroupModalResult(AddEditGroupWindow window, AccountGroup originalGroup)
        {
            bool success;
            string message;

            if (originalGroup != null)
            {
                success = GroupDataService.SaveGroup(window.Group);
                message = success ? "分组已成功更新" : "更新分组失败";

                if (success)
                {
                    var index = _allGroups.FindIndex(g => g.Id == originalGroup.Id);
                    if (index >= 0)
                    {
                        _allGroups[index].Name = window.Group.Name;
                        _allGroups[index].TagNote = window.Group.TagNote;
                        _allGroups[index].AccountIds = window.Group.AccountIds;
                        _allGroups[index].MarkedAccountIds = window.Group.MarkedAccountIds;
                        _allGroups[index].IsPinned = window.Group.IsPinned;
                        // 排序号也要一起搬（自由排序；否则编辑分组会把顺序重置）
                        _allGroups[index].Order = window.Group.Order;
                    }
                    SortGroups();
                }
            }
            else
            {
                window.Group.Id = GroupDataService.GetNextId();
                // 新分组：排到普通分组区最末尾（Order = 全局最大值 + 1）
                window.Group.Order = SortingService.NextOrder(_allGroups, g => g.Order);
                success = GroupDataService.SaveGroup(window.Group);
                message = success ? "分组已成功添加" : "添加分组失败";

                if (success)
                {
                    _allGroups.Add(window.Group);
                    SortGroups();
                }
            }

            if (success)
            {
                _allGroups = GroupDataService.LoadGroups();
                SortGroups();
                UpdateGroupInfoDisplay();
                _toastManager.Show("操作成功", message);

                if (_currentDisplayMode == DisplayMode.GroupManagement)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        ShowGroupsListContainer(true);
                        UpdateGroupsList();
                    }, DispatcherPriority.Render);
                }
            }
            else
            {
                _toastManager.Show("操作失败", message, false);
            }
        }

        // 「移入账号」不再另开窗口：入口 OnAddGroupButtonClick 直接调 EnterMoveMode()，
        // 具体逻辑见上面「移入账号模式」region。
        #endregion

        #region 清除置底相关

        /// <summary>
        /// ★★ 分组卡片上的「清除置底」按钮 —— 2026-10-17：**点了直接清除，不再弹确认框**。
        /// <para>
        /// 以前要先弹一个「确认清除置底」的模态框再点一次确认，多此一举；
        /// 这个操作本来就只是把置底标记清掉（不删账号），没必要拦一道。
        /// </para>
        /// </summary>
        private void ClearMarksButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int groupId)
                ExecuteClearMarks(groupId);
        }

        /// <summary>真正执行「清除某个分组内所有置底」并刷新界面、给提示。</summary>
        private void ExecuteClearMarks(int groupId)
        {
            var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return;

            int markedCount = group.MarkedAccountIds?.Count ?? 0;
            if (markedCount <= 0)
            {
                _toastManager.Show("提示", "该分组中没有已置底的账号", false);
                return;
            }

            ClearAllMarksInGroup(groupId);

            UpdateGroupsList();
            UpdateGroupInfoDisplay();

            if (_currentDisplayMode == DisplayMode.AccountList)
                UpdateAccountsList(searchTextBox.Text);

            _toastManager.Show("操作成功", $"已清除分组「{group.Name}」中的 {markedCount} 个置底");
        }
        #endregion

        #region 登录流程（激活窗口 → 识别循环 → 两行字幕）
        // ============================================================================
        // 「确认登录」做三件事：
        //   ① 把游戏客户区调到前台（激活窗口）
        //   ② 挂出两行字幕：第一行「正在执行登录账号 xxx」带头像（随机 空/荧），
        //      第二行「登录中...」不带头像，显示在第一行下面
        //   ③ 跑识别循环：每轮识别「主界面区域 / 派蒙菜单区域」，
        //      检测到派蒙菜单即成功；否则按一次 ESC、等 1 秒再下一轮
        //      （细节见 Services/LoginServices/AccountLoginFlow.cs）
        // 登录期间按钮变「停止登录」、禁用关闭；★ 窗口保持打开，不再自动关。
        // ============================================================================

        /// <summary>是否正在跑登录流程（决定按钮是「确认登录」还是「停止登录」）。</summary>
        /// <summary>
        /// 「账号异常」的统一文案。卡片上的红标签、登录窗口的状态提示、失败 toast 都用这一句，
        /// 改一处就全跟着变（以前是各自写死，改文案容易漏）。
        /// </summary>
        private const string AccountErrorHintText = LoginConfirmWindow.AccountErrorHintText;

        /// <summary>登录时弹出「请先验证身份后再登录」→ 这个账号在本机的设备验证已失效。</summary>
        private const string VerifyInvalidHintText = LoginConfirmWindow.VerifyInvalidHintText;

        /// <summary>「连接服务器失败」的提示文案（网络故障，彻底结束登录）。</summary>
        private const string ConnectFailedHintText = "连接服务器失败，请检查网络";

        private bool _isLoginFlowRunning;

        private CancellationTokenSource? _loginFlowCts;

        /// <summary>本次登录流程的起始时刻（失败提示里那个「已耗时」用它）。</summary>
        private DateTime _loginFlowStartTime;

        /// <summary>
        /// ★★ 整轮运行的**总起点** —— 第一行字幕那个计时用它（<c>登录成功 {账号} {总时长}</c>）。
        /// <para>
        /// 与 <see cref="_loginFlowStartTime"/> 的区别：那个是**单个账号**的开始时刻，每登录一个账号就重置一次；
        /// 这个**跨账号** —— 分组内自动换号是**接续**，沿用同一个起点（2026-10-17 用户要求：
        /// 第一行是总计时，切换账号不许归零）。只有**新一轮运行**（用户重新点「确认登录」）才归零。
        /// </para>
        /// </summary>
        private DateTime _totalRunStartTime;

        /// <summary>
        /// ★ 本次登录是不是**分组内**发起的。
        /// <para>分组内 = 完整流程（登录 + 登录后的附加动作，字幕三行）；
        /// 账号列表里 = 单纯的登录（检测到主界面就结束，字幕两行、没有附加动作）。</para>
        /// <para>在 <see cref="StartLoginFlow"/> 那一刻定下来 —— 登录窗口是模态的，
        /// 期间切不了分组，所以后面（成功收尾时）读它依然准确。</para>
        /// </summary>
        private bool _loginFlowInGroupMode;

        /// <summary>
        /// ★ 登录成功之后的「附加动作」（打开派蒙 / 聊天 / 联机界面）用的取消令牌。
        /// 必须**独立于** <see cref="_loginFlowCts"/> —— 登录流程结束时那个已经被 Dispose 了，
        ///   而附加动作是在登录结束**之后**才开始的，共用一个会拿到已释放的对象。
        /// </summary>
        private CancellationTokenSource? _postLoginCts;

        /// <summary>
        /// 取「附加动作 + 第一行计时刷新」共用的那个 CTS —— 没有、或已经作废过，就新建一个。
        /// <para>★ 登录成功那一刻**先**建好：计时刷新要在附加动作启动之前就有令牌可用
        /// （哪怕最后发现一个复选框都没勾，也是靠取消它来给计时收尾的）。</para>
        /// </summary>
        private CancellationTokenSource EnsurePostLoginCts()
        {
            if (_postLoginCts == null || _postLoginCts.IsCancellationRequested)
            {
                _postLoginCts?.Dispose();
                _postLoginCts = new CancellationTokenSource();
            }
            return _postLoginCts;
        }

        /// <summary>
        /// ★ 附加选项**实时生效**：人数监控还在跑着的时候改了「双人 / 三人 / 四人 / 不选」，
        ///   立刻把最新的勾选灌给它 —— 第三行下一轮就换成新格式（含「当前人数： n/4」那种），
        ///   不用重新登录一次。没在跑的时候更新也无害（下一次启动会拿到最新的）。
        /// </summary>
        private void LoginConfirmWindow_ExtraOptionsChanged(object sender, EventArgs e)
        {
            PostLoginActions.UpdateLiveOptions(_loginConfirmWindow?.GetPostLoginOptions());
        }

        private void LoginAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int id)
            {
                var account = _allAccounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                    ShowLoginModal(account);
            }
        }

        private void ShowLoginModal(Account account)
        {
            // 每次打开登录窗口都先收起上一条可能还挂在游戏画面上的登录字幕（两行都要）
            AccountLoginFlow.HideSubtitles();

            // ★ 顺便让上一轮的「计时刷新」闭嘴：窗口已经关过一轮了，
            //   它若还在走会在停止时补一笔旧的「登录成功」，看着像字幕诈尸。
            AccountLoginFlow.SilenceElapsedTicker();

            _loginConfirmWindow = new LoginConfirmWindow();

            _loginConfirmWindow.ConfirmLogin += LoginConfirmWindow_ConfirmLogin;
            _loginConfirmWindow.CloseRequested += LoginConfirmWindow_CloseRequested;
            _loginConfirmWindow.LoginNextRequested += OnLoginNextRequested;
            _loginConfirmWindow.LoginPrevRequested += OnLoginPrevRequested;
            _loginConfirmWindow.UidCopyRequested += OnUidCopyRequested;
            _loginConfirmWindow.MarkToggleRequested += OnMarkToggleRequested;
            _loginConfirmWindow.ExtraOptionsChanged += LoginConfirmWindow_ExtraOptionsChanged;

            _loginConfirmWindow.CurrentAccountName = account.Nickname;
            _loginConfirmWindow.AccountUid = account.Uid ?? string.Empty;

            // 分组内已置底的账号不能登录（登录成功后会自动置底，所以这套规则只在分组内出现）
            bool marked = IsAccountMarkedInCurrentGroup(account.Id);

            // ★ 能不能登录、按钮写成什么、标题什么颜色 —— 全部交给窗口的 SetAccountState() 决定：
            //   打开窗口 / 上一个下一个切换 / 点置底 三处共用同一套，免得各写一半对不上。
            //   优先级：**账号异常（HasLoginError）> 分组内置底**。
            _loginConfirmWindow.SetAccountState(account.HasLoginError, marked, account.LoginErrorText);

            // 置底控件：仅分组内显示，初始状态跟着账号的置底状态
            _loginConfirmWindow.ShowMarkToggle = _selectedGroupId > 0;
            _loginConfirmWindow.IsMarked = marked;

            // ★★ 登录后的附加选项（打开派蒙 / 聊天 / 联机）也是**仅分组内**：
            //    分组内的登录是完整流程（登录 + 附加动作，字幕三行）；
            //    账号列表里的登录只是单纯的登录 —— 检测到主界面就结束，没有这一块。
            _loginConfirmWindow.ShowExtraOptions = _selectedGroupId > 0;

            _selectedAccountIds = new List<int> { account.Id };
            SetCurrentLoginAccountList(account);

            // 与「添加/编辑账号」等窗口一样走 ShowModalWindow：
            // 它会先铺上宿主的全局遮罩，窗口 Closed 时再收掉。
            // 以前这里直接 ShowDialog，所以登录窗口打开时背后是没有遮罩的。
            _loginConfirmWindow.Closed += (s, e) =>
            {
                _loginConfirmWindow = null;
                // 窗口万一被关掉（正常流程里登录期间是禁用关闭的），
                // 正在跑的识别循环也要停掉，免得后台一直往游戏里按 ESC
                if (_isLoginFlowRunning) CancelLoginFlow();
            };
            ShowModalWindow(_loginConfirmWindow);
            _loginConfirmWindow = null;
        }

        private void SetCurrentLoginAccountList(Account currentAccount)
        {
            _currentLoginAccountList = _accounts.ToList();
            _currentLoginAccountIndex = _currentLoginAccountList.FindIndex(a => a.Id == currentAccount.Id);
            SetPrevAccountInfo();
            SetNextAccountInfo();
        }

        private void SetPrevAccountInfo()
        {
            if (_loginConfirmWindow == null) return;

            if (_currentLoginAccountIndex > 0)
            {
                var prev = _currentLoginAccountList[_currentLoginAccountIndex - 1];
                _loginConfirmWindow.PrevAccountName = prev.Nickname;
                _loginConfirmWindow.PrevAccountUid = prev.Uid ?? string.Empty;
            }
            else
            {
                _loginConfirmWindow.PrevAccountName = string.Empty;
                _loginConfirmWindow.PrevAccountUid = string.Empty;
            }
        }

        private void SetNextAccountInfo()
        {
            if (_loginConfirmWindow == null) return;

            if (_currentLoginAccountIndex >= 0 && _currentLoginAccountIndex < _currentLoginAccountList.Count - 1)
            {
                var next = _currentLoginAccountList[_currentLoginAccountIndex + 1];
                _loginConfirmWindow.NextAccountName = next.Nickname;
                _loginConfirmWindow.NextAccountUid = next.Uid ?? string.Empty;
            }
            else
            {
                _loginConfirmWindow.NextAccountName = string.Empty;
                _loginConfirmWindow.NextAccountUid = string.Empty;
            }
        }

        /// <summary>在登录窗口里切换当前展示的账号（只切换显示，不触发任何登录）。</summary>
        private void SwitchLoginAccount(int direction)
        {
            if (_loginConfirmWindow == null) return;
            if (_currentLoginAccountList.Count == 0) return;

            // 上一个 / 下一个是**整个分组（或整个列表）内顺序切换**，不跳过置底账号 ——
            // 置底的账号切过去只是不能登录（按钮变灰「无法登录」），照样能查看。
            int target = _currentLoginAccountIndex + direction;
            if (target < 0 || target >= _currentLoginAccountList.Count)
            {
                _toastManager.Show("提示",
                    direction > 0 ? "没有下一个账号了" : "没有上一个账号了", false);
                return;
            }

            ApplyLoginAccount(target);
        }

        /// <summary>把登录窗口切到 <see cref="_currentLoginAccountList"/> 里的第 <paramref name="index"/> 个账号。</summary>
        private void ApplyLoginAccount(int index)
        {
            if (_loginConfirmWindow == null) return;
            if (index < 0 || index >= _currentLoginAccountList.Count) return;

            _currentLoginAccountIndex = index;
            var account = _currentLoginAccountList[index];

            _loginConfirmWindow.CurrentAccountName = account.Nickname;
            _loginConfirmWindow.AccountUid = account.Uid ?? string.Empty;
            _selectedAccountIds = new List<int> { account.Id };

            // ★★ 换账号 = 整段重算，不能只挑几个属性改。
            //    以前这里只赋了 ConfirmButtonText / IsConfirmEnabled / StatusHintText，
            //    漏了标题颜色（切走还残留红色）、ConfirmButtonDanger（正常账号的按钮也是红字）、
            //    以及 HasLoginError（切回异常账号时「账号异常」显示不出来）。
            bool marked = IsAccountMarkedInCurrentGroup(account.Id);
            _loginConfirmWindow.SetAccountState(account.HasLoginError, marked, account.LoginErrorText);
            _loginConfirmWindow.IsMarked = marked;

            SetPrevAccountInfo();
            SetNextAccountInfo();
        }

        /// <summary>
        /// 登录窗口里点了置底控件：在分组内给当前账号置底 / 取消置底。
        /// 置底后该账号不能登录（确认按钮变「无法登录」并置灰），取消置底即恢复。
        /// </summary>
        private void OnMarkToggleRequested(object sender, EventArgs e)
        {
            var account = CurrentLoginAccount();
            if (account == null || _selectedGroupId <= 0) return;

            var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
            if (group == null) return;

            bool nowMarked = !group.IsAccountMarked(account.Id);
            ToggleAccountMarkInGroup(account.Id, _selectedGroupId);
            UpdateAccountsList(searchTextBox.Text);
            UpdateGroupInfoDisplay();

            if (_loginConfirmWindow != null)
            {
                _loginConfirmWindow.IsMarked = nowMarked;
                // 置底状态一变，异常/置底两个禁用理由的优先级就要重算，所以走整段重算的办法
                _loginConfirmWindow.SetAccountState(account.HasLoginError, nowMarked);
            }

            SetPrevAccountInfo();
            SetNextAccountInfo();

            _toastManager.Show(nowMarked ? "已置底" : "已取消置底",
                nowMarked
                    ? $"账号「{account.Nickname}」已置底，置底后无法登录"
                    : $"账号「{account.Nickname}」已取消置底，可恢复登录");
        }

        /// <summary>
        /// 账号在当前分组里是否已置底。仅分组内有意义，主列表（未选分组）恒为 false。
        /// </summary>
        private bool IsAccountMarkedInCurrentGroup(int accountId)
        {
            if (_selectedGroupId <= 0) return false;

            var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
            return group != null && group.IsAccountMarked(accountId);
        }

        /// <summary>
        /// 登录完成后把当前账号在分组里置底（仅分组内、仅未置底时）。
        /// 顺带刷新列表与登录窗口：置底后该账号不能再登录，确认按钮置灰。
        /// </summary>
        private void MarkCurrentAccountAfterLogin()
        {
            var account = CurrentLoginAccount();
            if (account == null || _selectedGroupId <= 0) return;

            var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
            if (group == null || group.IsAccountMarked(account.Id)) return;

            ToggleAccountMarkInGroup(account.Id, _selectedGroupId);
            UpdateAccountsList(searchTextBox.Text);
            UpdateGroupInfoDisplay();

            if (_loginConfirmWindow != null)
            {
                _loginConfirmWindow.ConfirmButtonText = "无法登录";
                _loginConfirmWindow.IsConfirmEnabled = false;
                _loginConfirmWindow.StatusHintText = "账号已置底，无法登录";
                _loginConfirmWindow.IsMarked = true;
            }

            SetPrevAccountInfo();
            SetNextAccountInfo();
            _toastManager.Show("已置底", $"账号「{account.Nickname}」登录完成，已在分组「{group.Name}」中置底");
        }

        private void OnLoginPrevRequested(object sender, EventArgs e) => SwitchLoginAccount(-1);
        private void OnLoginNextRequested(object sender, EventArgs e) => SwitchLoginAccount(1);

        private void OnUidCopyRequested(object sender, string uid)
        {
            // 实际复制由登录窗口内部的 NativeClipboardHelper 完成，这里只提示
            _toastManager.Show("UID 已复制", $"UID {uid} 已复制到剪贴板");
        }

        private void LoginConfirmWindow_ConfirmLogin(object sender, LoginConfirmEventArgs e)
        {
            // ★ 账号带着失败标签（异常 / 验证失效）：这个按钮虽然写着「账号异常」「验证失效」，
            //   但**它是可点的** —— 点了不再跑登录，直接打开账号编辑让用户自己处理
            //   （改账密、或点里面那个红色的「清除异常」）。
            if (e.OpensAccountEditor)
            {
                OpenAccountEditorFromLoginWindow();
                return;
            }

            // 正在登录时，这个按钮已经是「停止登录」了
            if (_isLoginFlowRunning)
            {
                CancelLoginFlow();
                return;
            }

            StartLoginFlow();
        }

        /// <summary>
        /// 登录窗口上点「账号异常」/「验证失效」→ 关掉登录窗口，打开这个账号的编辑窗口。
        /// <para>
        /// ★ 必须**先关登录窗口**再开编辑窗口：登录窗口是独立的顶层 Window，两个都开着会互相抢焦点，
        ///   而且编辑里点「清除异常」后要看到列表刷新，被登录窗挡着就看不出来。
        /// </para>
        /// <para>
        /// ★ 这里不能走 <see cref="StartLoginFlow"/>：那种账号再跑一遍流程还是同一个结果
        ///   （服务器判错 / 设备验证失效，都不是重试能解决的）。
        /// </para>
        /// </summary>
        private void OpenAccountEditorFromLoginWindow()
        {
            var account = CurrentLoginAccount();
            if (account == null) return;

            // 登录流程若还在跑（异常窗口通常不会），先停掉
            if (_isLoginFlowRunning) CancelLoginFlow();

            var accountId = account.Id;

            // ★ 这里**不收起字幕**：失败后那两行（「登录失败 …」/「账号验证失效-TAB隐藏」）正是
            //   用户要看的信息，按 TAB 就能收。只有点叉（真正取消）才 HideSubtitles()。
            _loginConfirmWindow?.Close();

            // 编辑窗口里「清除异常」是点了就落盘的 → 清完之后立刻就能重新登录，不用再走别的地方
            ShowAccountModal(_allAccounts.FirstOrDefault(a => a.Id == accountId) ?? account);
        }

        /// <summary>
        /// 开始登录：先激活游戏窗口，再挂字幕、跑识别循环。
        /// ★ 激活必须在关闭 / 收起任何窗口之前做 —— 此刻本窗口还是前台窗口，
        ///   SetForegroundWindow 才被系统放行；先切走的话前台会落回宿主主窗口，
        ///   再去抢给游戏就容易被「前台锁定」挡下。
        /// </summary>
        /// <param name="continueRun">
        /// ★ 真 = 这次是**自动换号接上来的**（分组内换下一个账号）→ 总计时**不归零**；
        /// 假（默认）= 用户新发起的一轮运行 → 总计时从现在起算。
        /// </param>
        private void StartLoginFlow(bool continueRun = false)
        {
            // ★ 新一轮开始：上一轮那个「还在刷第一行」的计时刷新必须闭嘴 ——
            //   它还活着的话（比如上一个账号的人数监控一直常驻到此刻），
            //   会跟这一轮 ShowRunningSubtitles 写的第一行互相覆盖。
            AccountLoginFlow.SilenceElapsedTicker();
            CancelPostLoginActions();

            if (!GameWindowActivator.Activate())
            {
                _toastManager.Show("未找到游戏窗口", "请先启动原神，再点确认登录", false);
                return;
            }

            BeginLoginFlow(continueRun);
        }

        /// <summary>游戏窗口已经在 → 真正开始这一轮登录。</summary>
        private void BeginLoginFlow(bool continueRun)
        {
            _isLoginFlowRunning = true;
            _loginFlowCts = new CancellationTokenSource();
            _loginFlowStartTime = DateTime.Now;
            _loginFlowInGroupMode = _selectedGroupId > 0;   // 决定「附加动作跑不跑」+「字幕几行」

            // ★ 总计时只在**新一轮运行**开始时归零；自动换号是接续，沿用同一个起点
            if (!continueRun) _totalRunStartTime = DateTime.Now;

            // 按钮变「停止登录」、标题变「登录中...」，并禁用关闭
            _loginConfirmWindow?.SetLoggingInState();

            _ = RunLoginFlowAsync(CurrentLoginAccount(), _loginFlowCts.Token);
        }

        /// <summary>
        /// 第三行字幕的初始文案 —— 只有**分组内**的登录才有（写明这次登录后还要做什么）。
        /// <para>账号列表里的登录是单纯的登录，返回空串 = 不挂第三行（两行字幕）。</para>
        /// </summary>
        private string BuildThirdLineText()
        {
            if (!_loginFlowInGroupMode) return string.Empty;

            // 文案由 PostLoginActions 统一给（它才知道人数目标那套格式），
            // 例：勾了「聊天 + 三人联机」→ 「三人联机： 1/4 等待中...」
            return PostLoginActions.BuildWaitingText(_loginConfirmWindow?.GetPostLoginOptions());
        }

        /// <summary>当前登录窗口正在操作的那个账号（「确认登录」的目标）。</summary>
        private Account? CurrentLoginAccount()
        {
            if (_currentLoginAccountIndex >= 0 && _currentLoginAccountIndex < _currentLoginAccountList.Count)
                return _currentLoginAccountList[_currentLoginAccountIndex];

            return null;
        }

        /// <summary>后台跑识别循环，结束后回到 UI 线程收尾（刷新字幕与窗口状态）。</summary>
        private async Task RunLoginFlowAsync(Account account, CancellationToken token)
        {
            LoginResult result = LoginResult.Failed;

            try
            {
                // 账密从 Account 现取（Username / Password 的 getter 会自动解密）
                result = await AccountLoginFlow.RunAsync(
                    account.Nickname, account.Username, account.Password, account.Uid,
                    BuildThirdLineText(), token);
            }
            catch (OperationCanceledException)
            {
                // 用户点了「停止登录」，属于正常退出
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 流程异常：{ex}");
            }
            finally
            {
                // 无论成功 / 失败 / 取消，「TAB 隐藏字幕」的锁都要解开。
                // （成功分支靠 ShowSucceededSubtitle 解、失败分支靠 HideSubtitles 解，
                //   这里是兜底 —— 万一哪条路径漏了，标志卡死后用户按 TAB 再也收不起字幕。）
                AccountLoginFlow.UnlockTabHide();
            }

            await Dispatcher.InvokeAsync(() =>
            {
                _isLoginFlowRunning = false;
                _loginFlowCts?.Dispose();
                _loginFlowCts = null;

                if (result == LoginResult.Success)
                {
                    // ★★ 第一行的处理要分两种：
                    //    · 账号列表里的登录 = 单纯的登录，到这里就真的结束了 → 正常写「登录成功 {账号} {时长}」；
                    //    · 分组内的登录 = 完整流程，这里只是**登录这段**结束，后面还接着跑附加动作
                    //      （打开派蒙 / 聊天 / 联机、联机人数监控……）→ 计时不许停，
                    //      交给 ticker 继续按秒走，等整个流程跑完（或用户停止）才由它写下定格的那一笔。
                    string elapsedAccountName = account.Nickname;

                    // ★★ 第一行是**整轮运行的总时长**（跨账号连续），不是这一个账号的登录耗时。
                    //    自动换号时 _totalRunStartTime 保持不变 ⇒ 切换账号计时不会归零。
                    DateTime startedAt = _totalRunStartTime;

                    if (_loginFlowInGroupMode)
                    {
                        AccountLoginFlow.ResumeElapsedTicker(
                            elapsedAccountName, startedAt, EnsurePostLoginCts().Token,
                            () => AccountLoginFlow.ShowSucceededSubtitle(
                                elapsedAccountName, DateTime.Now - startedAt));
                    }
                    else
                    {
                        AccountLoginFlow.ShowSucceededSubtitle(elapsedAccountName, DateTime.Now - startedAt);
                    }

                    _loginConfirmWindow?.SetLoginCompletedState(true);
                    _toastManager.Show("登录成功", "已进入游戏");

                    // 这次登录成功了，把之前可能挂着的红标签（账密错误 / 验证失效）清掉
                    if (account.HasLoginError)
                    {
                        account.HasLoginError = false;
                        account.LoginErrorText = string.Empty;   // ★ 文案要一起清，不清会串到下一次失败
                        AccountDataService.SaveAccount(account);
                    }

                    // 登录完成 → 分组内**必定**置底（2026-10-17：原来的「登录完成并置底」复选框已删除，
                    //   登录成功就置底，没有开关）。置底后这个账号不能再登录，
                    //   确认按钮随之置灰，用「上一个 / 下一个」切到还没置底的账号继续。
                    MarkCurrentAccountAfterLogin();

                    // ★★ 到这里登录已经**彻底结束**了，接下来是「附加动作」的独立流程：
                    //   按复选框的勾选去打开派蒙 / 聊天 / 联机界面。
                    //   ① 不复用登录流程的任何状态（它已经跑完，配额/阶段/字幕都归位了）；
                    //   ② 用独立的 CTS，登录那个已经 Dispose；
                    //   ③ 三个都没勾 → 直接不启动（返回值 false），这时要把计时停在这一刻。
                    if (!StartPostLoginActions(account))
                        FinishPostLoginActions();   // 没有附加动作 → 第一行的计时就此定格成「登录成功」
                }
                else if (result == LoginResult.CredentialError)
                {
                    // 账密重试满次数仍报错：收起字幕、给账号打红标签，
                    // ★ 并且**把已经打开的这扇窗口切成「账号异常」**（禁点）——
                    //   否则按钮还写着「确认登录」，用户点下去会再白跑一遍完整流程。
                    AccountLoginFlow.HideSubtitles();
                    _loginConfirmWindow?.SetLoginCancelledState();   // 先把关闭/置底等开关复位
                    _loginConfirmWindow?.SetAccountErrorState();     // 再覆盖成「账号异常」
                    _toastManager.Show("登录失败", AccountErrorHintText, false);

                    account.HasLoginError = true;
                    AccountDataService.SaveAccount(account);
                    _allAccounts = AccountDataService.LoadAccounts();
                    UpdateAccountsList(searchTextBox.Text);
                }
                else if (result == LoginResult.VerifyInvalid)
                {
                    // ★ 账号验证失效：流程里已经点过「取消验证」把游戏里的弹窗关掉了，这里做界面收尾 ——
                    //   字幕 → 第一行「登录失败 {账号} {时长}」、第二行「账号验证失效-TAB隐藏」；
                    //   窗口 → 停在「验证失效」（按钮红字禁用，和账密错误一样，防止再白跑一遍）；
                    //   账号 → 打上红标签「账号验证失效」。
                    AccountLoginFlow.ShowFailedSubtitle(
                        account.Nickname, DateTime.Now - _loginFlowStartTime, VerifyInvalidHintText);
                    _loginConfirmWindow?.SetLoginCancelledState();                      // 先复位关闭/置底等开关
                    _loginConfirmWindow?.SetAccountErrorState(VerifyInvalidHintText);   // 再覆盖成「验证失效」
                    _toastManager.Show("登录失败", VerifyInvalidHintText, false);

                    account.HasLoginError = true;
                    account.LoginErrorText = VerifyInvalidHintText;
                    AccountDataService.SaveAccount(account);
                    _allAccounts = AccountDataService.LoadAccounts();
                    UpdateAccountsList(searchTextBox.Text);
                }
                else if (result == LoginResult.ConnectionFailed)
                {
                    // ★ 连接服务器失败：网络出问题了 —— 本次登录结束，**不再换号**（换了也连不上）。
                    //   第一行写正常的「登录失败 {账号} {时长}」并解锁 TAB（可以有始有终地收掉字幕）。
                    AccountLoginFlow.ShowFailedSubtitle(
                        account.Nickname, DateTime.Now - _loginFlowStartTime, ConnectFailedHintText);
                    _loginConfirmWindow?.SetLoginCancelledState();
                    _toastManager.Show("登录中断", ConnectFailedHintText, false);

                    // 彻底收尾：把人数监控 / 计时一并停掉
                    FinishPostLoginActions();
                }
                else
                {
                    AccountLoginFlow.HideSubtitles();
                    _loginConfirmWindow?.SetLoginCancelledState();
                }
            });
        }

        /// <summary>
        /// 「停止登录」：通知后台循环退出。
        /// <summary>
        /// 登录**成功之后**启动「附加动作」：按复选框去打开派蒙 / 聊天 / 联机界面。
        /// <para>
        /// ★★ 这是**独立流程**，不是登录流程的一部分 ——
        ///   登录在 <see cref="RunLoginFlowAsync"/> 返回 LoginResult.Success 那一刻就结束了，
        ///   这里不再碰账密、重试配额、成功判定那些东西，只是「进主界面 → 按一下键 → 检测」。
        /// </para>
        /// <para>三个复选框都没勾时不启动（返回 <c>false</c>），让调用方自己去收尾。</para>
        /// </summary>
        /// <param name="account">本次登录的账号 —— 进聊天界面后要把它的 UID 发到聊天公屏（只发一次）。</param>
        /// <returns>真启动了返回 true；账号列表里的登录 / 没勾任何一个都返回 false。</returns>
        private bool StartPostLoginActions(Account account)
        {
            // ★ 附加动作**只有分组内的登录才有**：账号列表里的登录是单纯的登录，
            //   检测到主界面就结束了（那里连复选框那一块都不显示）。
            if (!_loginFlowInGroupMode) return false;

            var options = _loginConfirmWindow?.GetPostLoginOptions();
            if (options == null || !options.HasAny) return false;   // 没勾就不启动

            // ★★ 必须用「登录成功那一刻建好的」那个 CTS —— 第一行的计时刷新已经挂在它上面了。
            //    ⛔ 这里千万不能 Cancel 再重建：那等于让计时刷新当场收尾（第一行提前定格）。
            //    它被取消过（上一轮遗留）时 EnsurePostLoginCts() 会自己换一个新的。
            var token = EnsurePostLoginCts().Token;

            // ★ 实时生效的起点：窗口里最新的勾选（PlayerCountTarget 那几个）
            PostLoginActions.UpdateLiveOptions(options);

            // ★ UID 在**UI 线程**上先取出来（`Account.Uid` 的 getter 会解密），别带进后台线程再读
            var uid = account?.Uid ?? string.Empty;

            // ★★ 下一个要换的号「名字：UID」—— 队友在聊天里发「换号」时要播报给他们。
            //    必须在 UI 线程算（`Account.Uid` 的 getter 要解密）；此刻登录到的正是当前号，
            //    所以 FindNextUnmarkedAccountIndex() 找到的就是换号时会去的那一个（跳过已置底的）。
            string nextAccountText = "";
            int nextIdx = FindNextUnmarkedAccountIndex();
            if (nextIdx >= 0 && nextIdx < _currentLoginAccountList.Count)
            {
                var nextAccount = _currentLoginAccountList[nextIdx];
                nextAccountText = $"{nextAccount.Nickname}：{nextAccount.Uid}";
            }

            // fire-and-forget：它自己会在里面收尾（写第二行字幕），不阻塞 UI
            _ = Task.Run(async () =>
            {
                try
                {
                    await PostLoginActions.RunAsync(options, uid, token, SwitchToNextAccountAsync, nextAccountText);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UID/登录后] 启动附加动作失败：{ex.Message}");
                }
                finally
                {
                    // ★ 附加动作跑完 == 整个流程跑完 → 取消令牌，
                    //   第一行那个还在按秒走的计时也随之定格成「登录成功 {账号} {总时长}」。
                    FinishPostLoginActions();
                }
            }, token);

            return true;
        }

        /// <summary>
        /// ★★ 自动切换到下一个账号（分组内自动换号）——人数监控那边判断"该换了"就来叫它。
        /// <para>触发的两种情况：
        /// <list type="bullet">
        ///   <item>人数**曾经达标**（2/3/4 人）、之后掉回 1 —— 队友全走了，这个号的活干完了；</item>
        ///   <item>弹出「账号在另一处重复登录」—— 这个号在别处被登了（被顶号）。</item>
        /// </list>
        /// </para>
        /// <para>★ 挑的是**未置底**的下一个账号 —— 置底表示今天已完成，跳过不登。</para>
        /// </summary>
        private async Task SwitchToNextAccountAsync(string reason)
        {
            Debug.WriteLine($"[UID/登录] 收到换号请求（{reason}）→ 准备切到下一个账号");

            // ★ 这个回调是从人数监控的后台循环过来的 —— 碰字段 / UI 一律回 UI 线程
            await Dispatcher.InvokeAsync(() =>
            {
                // 已经在跑登录了就别再叠一轮
                if (_isLoginFlowRunning) return;

                // ① 当前这个号置底（分组内的登录完成就得置底，已进入监控说明它登成功了）
                MarkCurrentAccountAfterLogin();

                // ② 往后找第一个**未置底**的账号（置底 = 已完成，跳过）
                int next = FindNextUnmarkedAccountIndex();
                if (next < 0)
                {
                    Debug.WriteLine("[UID/登录] 后面没有未置底的账号了 → 自动换号到此为止");
                    _toastManager.Show("自动切换结束", "后面的账号都已置底，没有要登录的了");
                    FinishPostLoginActions();   // 第一行的计时就此定格
                    return;
                }

                ApplyLoginAccount(next);
                Debug.WriteLine($"[UID/登录] 自动切换到账号「{_currentLoginAccountList[next].Nickname}」");

                // ★ 字幕**不收摊、直接把内容换成新账号的**（第一行换成新账号名、**计时沿用总计时**、
                //   第二行回到定位检测、第三行换成新账号的人数行）—— ShowRunningSubtitles 是纯换内容，
                //   加上旧监控那边已交接着不碰字幕（PostLoginActions 的 finally），
                //   所以屏幕上不会出现"换号瞬间字幕全没了"。
                AccountLoginFlow.ShowRunningSubtitles(
                    _currentLoginAccountList[next].Nickname, BuildThirdLineText(),
                    AccountLoginFlow.FormatElapsed(DateTime.Now - _totalRunStartTime));

                // ③ 对它重新走一遍登录流程（continueRun: true → **总计时不归零**）。
                //    StartLoginFlow 里面会：静默上一轮计时 →
                //    取消上一轮的人数监控 → 激活游戏窗口 → 新建 CTS → 跑流程；
                //    而流程开头的**定位检测**自然会先把当前还在线的这个号退掉。
                StartLoginFlow(continueRun: true);
            });
        }

        /// <summary>
        /// 从当前账号往后找第一个**没置底**的账号下标（自动换号用）。
        /// <para>和「下一个」按钮不同：那个是顺序挨个切（置底的也能切过去看），
        /// 这里是"跳过置底的"，因为置底 = 今天已经完成。</para>
        /// </summary>
        /// <returns>下标；<c>-1</c> = 后面没有可登录的了。</returns>
        private int FindNextUnmarkedAccountIndex()
        {
            if (_selectedGroupId <= 0) return -1;

            for (int i = _currentLoginAccountIndex + 1; i < _currentLoginAccountList.Count; i++)
            {
                if (!IsAccountMarkedInCurrentGroup(_currentLoginAccountList[i].Id))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// ★ 附加动作**正常收尾**：取消令牌（不等、"通知就行"）。
        /// <para>第一行的计时刷新用的是同一个令牌，它停止时会写下定格的那一笔；
        /// <see cref="CancelPostLoginActions"/> 则是用户放弃的那一条路径 —— 那边不许再写。</para>
        /// </summary>
        private void FinishPostLoginActions()
        {
            try
            {
                _postLoginCts?.Cancel();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录后] 结束附加动作失败：{ex.Message}");
            }
        }

        /// <summary>
        /// ★ 用户放弃：停掉正在跑的附加动作（关闭登录窗口 / 页面卸载时调用）。
        /// <para>与 <see cref="FinishPostLoginActions"/> 的区别是这里会先让「计时刷新」保持沉默 ——
        /// 用户主动关窗之后字幕是被收起来的，ticker 若再补一笔「登录成功」就成了诈尸。</para>
        /// </summary>
        private void CancelPostLoginActions()
        {
            try
            {
                AccountLoginFlow.SilenceElapsedTicker();
                _postLoginCts?.Cancel();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录后] 取消附加动作失败：{ex.Message}");
            }
        }

        /// 收尾（隐藏字幕、改窗口状态）统一在 <see cref="RunLoginFlowAsync"/> 里做，
        /// 这样两条退出路径（成功 / 取消）不会打架。
        /// </summary>
        private void CancelLoginFlow()
        {
            try
            {
                _loginFlowCts?.Cancel();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 取消失败：{ex.Message}");
            }
        }

        /// <summary>取当前登录窗口里展示的账号名，仅用于提示文案。</summary>
        private string CurrentLoginAccountNameForToast()
        {
            return CurrentLoginAccount()?.Nickname ?? string.Empty;
        }

        private void LoginConfirmWindow_CloseRequested(object sender, EventArgs e)
        {
            // 用户在登录窗口上点了叉（取消）：把可能已经挂出去的登录字幕一并收起
            AccountLoginFlow.HideSubtitles();

            // ★ 附加动作（打开派蒙 / 聊天 / 联机）还可能在等主界面 —— 用户点叉就是放弃，停掉它。
            //   已经打开了的界面不会因此关掉，只是不再继续检测 / 切换世界权限。
            CancelPostLoginActions();

            _loginConfirmWindow?.Close();
        }
        #endregion

        #region 帮助窗口相关
        /// <summary>
        /// 打开「使用说明」窗口。
        ///
        /// <para>
        /// 页内那个「!」按钮已经移到<b>主程序标题栏</b>（在标题右边），
        /// 所以这里做成公开方法，由插件入口 <c>UidPlugin.ShowHelp()</c> 反射转发给宿主调用。
        /// </para>
        /// </summary>
        public void ShowHelpDialog() => ShowHelpWindow();

        private void ShowHelpWindow()
        {
            try
            {
                _modalManager.ShowOverlay();

                var helpWindow = new HelpWindow();
                helpWindow.Owner = Window.GetWindow(this);
                helpWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                helpWindow.Closed += (s, e) =>
                {
                    _modalManager.HideOverlay();
                };

                helpWindow.ShowDialog();

                // 用户在帮助窗里点了『关闭工具』。
                // 必须等 ShowDialog（模态嵌套消息循环）完全退出后再处理：
                // 若在帮助窗内部直接关，模态 DispatcherFrame 无法正常弹出，
                // 整个 UI 线程会卡死（宿主 LvchaxsBS 窗口也会一并失去响应）。
                //
                // 本视图现在嵌在宿主内容区里，没有窗口可关，
                // 因此抛事件由宿主切回它自己的页面（首页）。
                if (helpWindow.CloseToolRequested)
                {
                    CloseToolRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                _modalManager.HideOverlay();
                System.Diagnostics.Debug.WriteLine($"打开帮助窗口失败: {ex.Message}");
                _toastManager?.Show("错误", "无法打开帮助窗口", false);
            }
        }
        #endregion

        #region 模态框按钮事件
        private void CloseDeleteModalButton_Click(object sender, RoutedEventArgs e) => HideDeleteModal();
        private void CancelDeleteButton_Click(object sender, RoutedEventArgs e) => HideDeleteModal();
        private void CloseDeleteGroupModalButton_Click(object sender, RoutedEventArgs e) => HideDeleteGroupModal();
        private void CancelDeleteGroupButton_Click(object sender, RoutedEventArgs e) => HideDeleteGroupModal();
        private void CloseRemoveFromGroupModalButton_Click(object sender, RoutedEventArgs e) => HideRemoveFromGroupModal();
        private void CancelRemoveFromGroupButton_Click(object sender, RoutedEventArgs e) => HideRemoveFromGroupModal();
        private void CloseClearMarksModalButton_Click(object sender, RoutedEventArgs e) => HideClearMarksModal();
        private void CancelClearMarksButton_Click(object sender, RoutedEventArgs e) => HideClearMarksModal();
        #endregion

        #region PropertyChanged 支持
        private static void OnSelectedGroupIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AccountManagerView view)
            {
                view._selectedGroupId = (int)e.NewValue;
                view.OnPropertyChanged(nameof(view.IsInGroupMode));
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region 枚举定义
        private enum DisplayMode { AccountList, NoData, GroupManagement }
        #endregion
    }
}
