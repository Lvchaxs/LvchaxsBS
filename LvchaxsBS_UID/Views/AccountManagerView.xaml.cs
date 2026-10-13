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
        private List<int> _pinnedAccountOrder = new();
        private List<int> _pinnedGroupOrder = new();

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
        private bool _markAfterLoginPreference = true;

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

        /// <summary>
        /// 卡片里的「标记」方块是否显示：只在分组内的账号列表里出现。
        /// </summary>
        /// <remarks>
        /// 账号列表已经从「多列」合并成「一张卡片」，
        /// 原来靠隐藏整列来做这件事的做法不再适用，改成卡片模板绑定这两个属性。
        /// </remarks>
        public bool ShowMarkButton => !_isMoveMode && _selectedGroupId > 0 && _currentDisplayMode == DisplayMode.AccountList;

        /// <summary>卡片里的「置顶」方块是否显示：只在主列表（未选分组）里出现。</summary>
        public bool ShowPinButton => !_isMoveMode && _selectedGroupId == 0 && _currentDisplayMode == DisplayMode.AccountList;

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

            // 恢复登录窗口复选框偏好（仅界面状态）
            LoadMarkAfterLoginPreference();

            InitializeData();
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
            LoadPinnedOrders();
            LoadAllData();
        }

        private void LoadAllData()
        {
            _allAccounts = AccountDataService.LoadAccounts();
            _allGroups = GroupDataService.LoadGroups();
            UpdateAccountsList();
            UpdateWatermarkVisibility();
            UpdateGroupInfoDisplay();
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
                    UpdateMarkColumnVisibility();
                    UpdatePinColumnVisibility();
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

        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T))
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }
        #endregion

        #region 账号列表管理
        private void UpdateAccountsList(string searchTerm = "")
        {
            _accounts.Clear();

            // 已去掉表头与点列排序：顺序固定为
            //   主列表 —— 置顶账号在最前（按置顶时间），其余按「添加时间」升序，
            //             所以新添加的账号总是排在最下面；
            //   分组内 —— 未标记在前、已标记在后，各自按加入时间升序。
            // 这两条顺序由 SortingService 的 GetMainDisplayOrder / GetGroupDisplayOrder 给出。
            List<Account> filteredAccounts = GetFilteredAccounts(searchTerm);

            foreach (var account in filteredAccounts)
                _accounts.Add(account);

            accountsDataGrid.ItemsSource = _accounts;
            UpdateUI();
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
        /// 「移入账号」模式下的列表：全部账号，顺序和主账号列表一致
        /// （置顶在最前，其余按添加时间升序）。
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
            UpdateColumnVisibility();
        }

        private void UpdateColumnVisibility()
        {
            UpdateMarkColumnVisibility();
            UpdatePinColumnVisibility();
        }

        // 账号列表已合并成「一张卡片」，标记/置顶不再是独立列。
        // 它们的显示与否改由卡片模板绑定到 ShowMarkButton / ShowPinButton，
        // 这里只需要通知一次属性变化。
        private void UpdateMarkColumnVisibility()
            => OnPropertyChanged(nameof(ShowMarkButton));

        private void UpdatePinColumnVisibility()
            => OnPropertyChanged(nameof(ShowPinButton));

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
                    break;

                case DisplayMode.NoData:
                    ApplyViewState();

                    noDataMessage.Visibility = Visibility.Visible;
                    break;

                case DisplayMode.GroupManagement:
                    _viewStateManager.SwitchToView(ViewStateManager.AppView.GroupManagement);
                    groupManagementMessage.Visibility = Visibility.Visible;
                    SortGroups();
                    UpdateGroupsList();
                    break;
            }

            UpdateGroupInfoDisplay();
            UpdateColumnVisibility();
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
            int markedCount = group.MarkedTimes?.Count ?? 0;
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
            if (groupsItemsControl == null) return;

            if (_allGroups.Count > 0)
            {
                var groupsWithDisplayText = _allGroups.Select(g =>
                    _sortingService.CreateGroupDisplayObject(g, _allAccounts)).ToList();
                groupsItemsControl.ItemsSource = groupsWithDisplayText;
                UpdateGroupsListUI(groupsItemsControl);
                ShowGroupsListContainer(true);
            }
            else
            {
                groupsItemsControl.ItemsSource = null;
                ShowGroupsListContainer(false);
            }
        }

        private void UpdateGroupsListUI(ItemsControl groupsItemsControl)
        {
            groupsItemsControl.InvalidateVisual();
            groupsItemsControl.UpdateLayout();

            Dispatcher.BeginInvoke(() =>
            {
                foreach (var item in groupsItemsControl.Items)
                {
                    var container = groupsItemsControl.ItemContainerGenerator.ContainerFromItem(item);
                    if (container != null)
                    {
                        var clearMarksButton = FindVisualChild<Button>(container, "clearMarksButton");
                        if (clearMarksButton != null)
                        {
                            var propertyInfo = item.GetType().GetProperty("HasMarks");
                            if (propertyInfo != null)
                            {
                                bool hasMarks = (bool)propertyInfo.GetValue(item);
                                clearMarksButton.Visibility = hasMarks ? Visibility.Visible : Visibility.Collapsed;
                            }
                        }
                    }
                }
            }, DispatcherPriority.Render);
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
            if (group != null && group.MarkedTimes?.Count > 0)
            {
                group.MarkedTimes.Clear();
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
            var noDataText = FindVisualChild<TextBlock>(noDataMessage, "noDataText");
            var noDataSubText = FindVisualChild<TextBlock>(noDataMessage, "noDataSubText");

            if (noDataIcon == null || noDataText == null || noDataSubText == null) return;

            bool hasSearchText = !string.IsNullOrWhiteSpace(searchTextBox.Text);
            bool hasAccountsInCurrentView = _selectedGroupId > 0
                ? _allGroups.Any(g => g.Id == _selectedGroupId && g.AccountIds.Count > 0)
                : _allAccounts.Count > 0;

            noDataIcon.Text = "📋";
            noDataText.Text = "当前没有任何账号";
            noDataSubText.Text = "点击右上角'添加账号'按钮添加第一个账号";

            if (_selectedGroupId > 0 && !hasSearchText && !hasAccountsInCurrentView)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null)
                {
                    noDataIcon.Text = "📂";
                    noDataText.Text = $"「{group.Name}」分组中没有账号";
                    noDataSubText.Text = $"伴生Lvchaxs\n点击右上角 移入账号 按钮选择账号移入「{group.Name}」分组中";
                }
            }
            else if (hasSearchText && _allAccounts.Count > 0)
            {
                noDataIcon.Text = "🔍";
                noDataText.Text = "未找到相关账号";
                noDataSubText.Text = $"伴生Lvchaxs\n未找到与「{searchTextBox.Text}」相关的账号";
            }
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
                if (group != null)
                {
                    var account = _allAccounts.FirstOrDefault(a => a.Id == accountId);
                    if (account != null)
                    {
                        ToggleAccountMarkInGroup(accountId, _selectedGroupId);
                        bool isNowMarked = group.IsAccountMarked(accountId);
                        string action = isNowMarked ? "已置底" : "已取消置底";
                        _toastManager.Show($"{action}·账号「{account.Nickname}」在分组「{group.Name}」中", true);

                        UpdateAccountsList(searchTextBox.Text);
                        UpdateGroupInfoDisplay();
                    }
                }
            }
            else if (_selectedGroupId == 0)
            {
                _toastManager.Show("提示", "标记功能仅在分组内可用", false);
            }
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
                if (account != null)
                {
                    account.SetPinned(!account.IsPinned);

                    if (account.IsPinned)
                    {
                        if (!_pinnedAccountOrder.Contains(accountId))
                            _pinnedAccountOrder.Add(accountId);
                    }
                    else
                    {
                        _pinnedAccountOrder.Remove(accountId);
                    }

                    SavePinnedOrders();
                    bool success = AccountDataService.SaveAccount(account);

                    string message = account.IsPinned
                        ? $"账号「{account.Nickname}」已置顶"
                        : $"账号「{account.Nickname}」已取消置顶";

                    _toastManager.Show(account.IsPinned ? "已置顶" : "已取消置顶", message);

                    _allAccounts = AccountDataService.LoadAccounts();
                    UpdateAccountsList(searchTextBox.Text);
                }
            }
            else if (_selectedGroupId > 0)
            {
                _toastManager.Show("提示", "置顶功能仅在主账号列表可用", false);
            }
        }

        private void GroupPinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int groupId)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
                if (group != null)
                {
                    group.SetPinned(!group.IsPinned);

                    if (group.IsPinned)
                    {
                        if (!_pinnedGroupOrder.Contains(groupId))
                            _pinnedGroupOrder.Add(groupId);
                    }
                    else
                    {
                        _pinnedGroupOrder.Remove(groupId);
                    }

                    SavePinnedOrders();
                    bool success = GroupDataService.SaveGroup(group);

                    string action = group.IsPinned ? "已置顶" : "已取消置顶";
                    string message = group.IsPinned
                        ? $"分组「{group.Name}」已置顶"
                        : $"分组「{group.Name}」已取消置顶";

                    _toastManager.Show(action, message);

                    _allGroups = GroupDataService.LoadGroups();
                    SortGroups();

                    if (_currentDisplayMode == DisplayMode.GroupManagement)
                        UpdateGroupsList();
                }
            }
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
                    _selectedGroupId = id;
                    SelectedGroupId = id;
                    UpdateAccountsList();
                    SwitchToDisplayMode(DisplayMode.AccountList);
                    UpdateGroupInfoDisplay();
                    UpdateMarkColumnVisibility();
                    _viewStateManager.SwitchToView(ViewStateManager.AppView.GroupDetail, group);
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
            _allGroups = GroupDataService.LoadGroups();
            _allAccounts = AccountDataService.LoadAccounts();

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
            UpdateMarkColumnVisibility();
            UpdatePinColumnVisibility();

            // 按钮区只留「移入 / 取消」
            _viewStateManager.SwitchToView(ViewStateManager.AppView.MoveMode);

            UpdateAccountsList(string.Empty);
            SwitchToDisplayMode(DisplayMode.AccountList);
            UpdateGroupInfoDisplay();
        }

        /// <summary>退出移入模式，回到进入前的那个分组内。</summary>
        private void ExitMoveMode()
        {
            int groupId = _moveTargetGroupId;

            _isMoveMode = false;
            _moveTargetGroupId = 0;
            OnPropertyChanged(nameof(IsMoveMode));

            foreach (var account in _allAccounts)
            {
                account.IsSelectedForMove = false;
                account.IsMoveable = true;
            }

            // 回到之前的分组内
            _selectedGroupId = groupId;
            SelectedGroupId = groupId;
            _allGroups = GroupDataService.LoadGroups();
            UpdateAccountsList(searchTextBox.Text);
            SwitchToDisplayMode(DisplayMode.AccountList);
            UpdateGroupInfoDisplay();
            UpdateMarkColumnVisibility();
            UpdatePinColumnVisibility();
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

                var now = DateTime.Now;
                foreach (var accountId in accountIds)
                {
                    if (!group.AccountIds.Contains(accountId))
                    {
                        group.AccountIds.Add(accountId);
                        group.AccountAddTimes ??= new Dictionary<int, DateTime>();
                        group.AccountAddTimes[accountId] = now;
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
            UpdateMarkColumnVisibility();
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

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 处理选择改变的逻辑
        }

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (sender is DataGrid dataGrid)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(dataGrid);
                if (scrollViewer != null)
                {
                    double scrollOffset = e.Delta > 0 ? -1 : 1;
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollOffset);
                }
            }
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
                clearMarksConfirmText.Text = $"确定要清除分组「{group.Name}」中的所有标记吗？";
            if (clearMarksExtraText != null)
                clearMarksExtraText.Text = $"该分组中共有 {markedCount} 个已标记账号，清除后将全部恢复为未标记状态";

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

        private void ConfirmClearMarksButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroupId > 0)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
                if (group != null && group.MarkedTimes?.Count > 0)
                {
                    int markedCount = group.MarkedTimes.Count;
                    ClearAllMarksInGroup(_selectedGroupId);

                    UpdateGroupsList();
                    UpdateGroupInfoDisplay();

                    if (_currentDisplayMode == DisplayMode.AccountList)
                        UpdateAccountsList(searchTextBox.Text);

                    _toastManager.Show("操作成功", $"已清除分组「{group.Name}」中的 {markedCount} 个标记");
                }
                else
                {
                    _toastManager.Show("提示", "该分组中没有已标记的账号", false);
                }

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
                        _allGroups[index].AccountAddTimes = window.Group.AccountAddTimes;
                        _allGroups[index].MarkedTimes = window.Group.MarkedTimes;
                        _allGroups[index].IsPinned = window.Group.IsPinned;
                        _allGroups[index].PinnedTime = window.Group.PinnedTime;
                    }
                    SortGroups();
                }
            }
            else
            {
                window.Group.Id = GroupDataService.GetNextId();
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

        #region 置顶顺序管理
        private void SavePinnedOrders()
        {
            try
            {
                var orders = new { AccountOrder = _pinnedAccountOrder, GroupOrder = _pinnedGroupOrder };
                string json = System.Text.Json.JsonSerializer.Serialize(orders);
                string filePath = AppPaths.ConfigFile("pinned_orders.json");
                System.IO.File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存置顶顺序失败: {ex.Message}");
            }
        }

        private void LoadPinnedOrders()
        {
            try
            {
                string filePath = AppPaths.ConfigFile("pinned_orders.json");

                if (System.IO.File.Exists(filePath))
                {
                    string json = System.IO.File.ReadAllText(filePath);
                    using var document = System.Text.Json.JsonDocument.Parse(json);
                    var root = document.RootElement;

                    if (root.TryGetProperty("AccountOrder", out var accountOrderElement))
                        _pinnedAccountOrder = accountOrderElement.EnumerateArray().Select(x => x.GetInt32()).ToList();

                    if (root.TryGetProperty("GroupOrder", out var groupOrderElement))
                        _pinnedGroupOrder = groupOrderElement.EnumerateArray().Select(x => x.GetInt32()).ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载置顶顺序失败: {ex.Message}");
                _pinnedAccountOrder = new List<int>();
                _pinnedGroupOrder = new List<int>();
            }
        }
        #endregion

        #region 清除标记相关
        private void ClearMarksButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int groupId)
            {
                var group = _allGroups.FirstOrDefault(g => g.Id == groupId);
                if (group != null)
                {
                    int markedCount = group.MarkedTimes?.Count ?? 0;
                    if (markedCount > 0)
                        ShowClearMarksModal(group, markedCount);
                    else
                        _toastManager.Show("提示", "该分组中没有已标记的账号", false);
                }
            }
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
        private bool _isLoginFlowRunning;

        private CancellationTokenSource? _loginFlowCts;

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

            _loginConfirmWindow = new LoginConfirmWindow();

            _loginConfirmWindow.ConfirmLogin += LoginConfirmWindow_ConfirmLogin;
            _loginConfirmWindow.CloseRequested += LoginConfirmWindow_CloseRequested;
            _loginConfirmWindow.MarkPreferenceChanged += LoginConfirmWindow_MarkPreferenceChanged;
            _loginConfirmWindow.LoginNextRequested += OnLoginNextRequested;
            _loginConfirmWindow.LoginPrevRequested += OnLoginPrevRequested;
            _loginConfirmWindow.UidCopyRequested += OnUidCopyRequested;

            _loginConfirmWindow.CurrentAccountName = account.Nickname;
            _loginConfirmWindow.AccountUid = account.Uid ?? string.Empty;
            _loginConfirmWindow.TitleText = "确认登录";

            // 分组内已置底的账号不能登录（登录成功后会自动置底，所以这套规则只在分组内出现）
            bool marked = IsAccountMarkedInCurrentGroup(account.Id);
            _loginConfirmWindow.IsConfirmEnabled = !marked;
            _loginConfirmWindow.StatusHintText = marked ? "账号已置底，无法登录" : "测试阶段";

            _selectedAccountIds = new List<int> { account.Id };
            SetCurrentLoginAccountList(account);
            UpdateMarkCheckBoxState();

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

            // 上一个「可登录」的账号（分组内跳过已置底的）
            int prev = FindLoginableAccountIndex(_currentLoginAccountIndex - 1, -1);
            if (prev >= 0)
            {
                var account = _currentLoginAccountList[prev];
                _loginConfirmWindow.PrevAccountName = account.Nickname;
                _loginConfirmWindow.PrevAccountUid = account.Uid ?? string.Empty;
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

            // 下一个「可登录」的账号（分组内跳过已置底的）
            int next = FindLoginableAccountIndex(_currentLoginAccountIndex + 1, 1);
            if (next >= 0)
            {
                var account = _currentLoginAccountList[next];
                _loginConfirmWindow.NextAccountName = account.Nickname;
                _loginConfirmWindow.NextAccountUid = account.Uid ?? string.Empty;
            }
            else
            {
                _loginConfirmWindow.NextAccountName = string.Empty;
                _loginConfirmWindow.NextAccountUid = string.Empty;
            }
        }

        /// <summary>根据当前账号在该分组中的标记状态，刷新窗口里「标记」复选框的显示。</summary>
        private void UpdateMarkCheckBoxState()
        {
            if (_loginConfirmWindow == null) return;

            var currentAccount = (_currentLoginAccountIndex >= 0 && _currentLoginAccountIndex < _currentLoginAccountList.Count)
                ? _currentLoginAccountList[_currentLoginAccountIndex]
                : null;

            if (currentAccount == null || _selectedGroupId <= 0)
            {
                _loginConfirmWindow.ShowMarkCheckBox = false;
                _loginConfirmWindow.IsMarkChecked = false;
                return;
            }

            _loginConfirmWindow.ShowMarkCheckBox = true;

            var group = _allGroups.FirstOrDefault(g => g.Id == _selectedGroupId);
            bool alreadyMarked = group != null && group.IsAccountMarked(currentAccount.Id);

            _loginConfirmWindow.IsMarkCheckBoxEnabled = !alreadyMarked;
            _loginConfirmWindow.IsMarkChecked = !alreadyMarked && _markAfterLoginPreference;
        }

        /// <summary>在登录窗口里切换当前展示的账号（只切换显示，不触发任何登录）。</summary>
        private void SwitchLoginAccount(int direction)
        {
            if (_loginConfirmWindow == null) return;
            if (_currentLoginAccountList.Count == 0) return;

            // 分组内跳过已置底的账号（置底的不能登录）；主列表不过滤，一切如旧
            int target = FindLoginableAccountIndex(_currentLoginAccountIndex + direction, direction);
            if (target < 0)
            {
                _toastManager.Show("提示",
                    direction > 0 ? "没有下一个可登录的账号了" : "没有上一个可登录的账号了", false);
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

            // 切过来的账号一定是未置底的，确认按钮恢复可用、提示复位
            _loginConfirmWindow.IsConfirmEnabled = true;
            _loginConfirmWindow.StatusHintText = "测试阶段";

            SetPrevAccountInfo();
            SetNextAccountInfo();
            UpdateMarkCheckBoxState();
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
        /// 从 <paramref name="from"/> 沿 <paramref name="direction"/>（+1 / -1）找第一个「可登录」
        /// （分组内未置底）的账号，返回下标；找不到返回 -1。主列表不过滤置底，但仍做边界检查。
        /// </summary>
        private int FindLoginableAccountIndex(int from, int direction)
        {
            // direction 必须是 ±1：0 会让下面的循环原地打转
            if (direction != 1 && direction != -1) return -1;

            for (int i = from; i >= 0 && i < _currentLoginAccountList.Count; i += direction)
            {
                if (_selectedGroupId <= 0 || !IsAccountMarkedInCurrentGroup(_currentLoginAccountList[i].Id))
                    return i;
            }
            return -1;
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
                _loginConfirmWindow.IsConfirmEnabled = false;
                _loginConfirmWindow.StatusHintText = "账号已置底，无法登录";
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
            // 正在登录时，这个按钮已经是「停止登录」了
            if (_isLoginFlowRunning)
            {
                CancelLoginFlow();
                return;
            }

            StartLoginFlow();
        }

        /// <summary>
        /// 开始登录：先激活游戏窗口，再挂字幕、跑识别循环。
        /// ★ 激活必须在关闭 / 收起任何窗口之前做 —— 此刻本窗口还是前台窗口，
        ///   SetForegroundWindow 才被系统放行；先切走的话前台会落回宿主主窗口，
        ///   再去抢给游戏就容易被「前台锁定」挡下。
        /// </summary>
        private void StartLoginFlow()
        {
            if (!GameWindowActivator.Activate())
            {
                _toastManager.Show("未找到游戏窗口", "请先启动原神，再点确认登录", false);
                return;
            }

            _isLoginFlowRunning = true;
            _loginFlowCts = new CancellationTokenSource();

            // 按钮变「停止登录」、标题变「登录中...」，并禁用关闭
            _loginConfirmWindow?.SetLoggingInState();

            _ = RunLoginFlowAsync(CurrentLoginAccount(), _loginFlowCts.Token);
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
            bool succeeded = false;

            try
            {
                // 账密从 Account 现取（Username / Password 的 getter 会自动解密）
                succeeded = await AccountLoginFlow.RunAsync(
                    account.Nickname, account.Username, account.Password, account.Uid, token);
            }
            catch (OperationCanceledException)
            {
                // 用户点了「停止登录」，属于正常退出
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 流程异常：{ex}");
            }

            await Dispatcher.InvokeAsync(() =>
            {
                _isLoginFlowRunning = false;
                _loginFlowCts?.Dispose();
                _loginFlowCts = null;

                if (succeeded)
                {
                    // 第二行换成「登录成功-TAB关闭」，窗口停在「登录完成」
                    AccountLoginFlow.ShowSucceededSubtitle();
                    _loginConfirmWindow?.SetLoginCompletedState(true);
                    _toastManager.Show("登录成功", "已进入游戏");

                    // 登录完成 → 分组内自动置底。置底后这个账号就不能再登录了，
                    // 所以确认按钮随之置灰，用「上一个 / 下一个」切到还没置底的账号继续。
                    MarkCurrentAccountAfterLogin();
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
            _loginConfirmWindow?.Close();
        }

        private void LoginConfirmWindow_MarkPreferenceChanged(object sender, bool newValue)
        {
            _markAfterLoginPreference = newValue;
            SaveMarkAfterLoginPreference();
        }

        private void LoadMarkAfterLoginPreference()
        {
            try
            {
                string filePath = AppPaths.ConfigFile("mark_after_login_preference.json");
                if (File.Exists(filePath) && bool.TryParse(File.ReadAllText(filePath), out bool preference))
                    _markAfterLoginPreference = preference;
            }
            catch
            {
                // 读取失败就用默认值
            }
        }

        private void SaveMarkAfterLoginPreference()
        {
            try
            {
                string filePath = AppPaths.ConfigFile("mark_after_login_preference.json");
                File.WriteAllText(filePath, _markAfterLoginPreference.ToString());
            }
            catch
            {
                // 保存失败忽略
            }
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