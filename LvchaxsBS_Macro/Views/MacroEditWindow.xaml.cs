using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Views
{
    /// <summary>
    /// 「新建宏 / 编辑宏」对话框（无边框、圆角、淡入）。
    /// <para>
    /// ★ 布局照 UID 的「新建/编辑账号」窗口：**左边填信息、右边点头像**，
    ///   右侧连着元素筛选、5 列网格、容器外滚动条都一样。
    /// </para>
    /// <para>
    /// ★ 用法：<c>new MacroEditWindow(null)</c> = 新建，<c>new MacroEditWindow(item)</c> = 编辑；
    /// <c>ShowDialog() == true</c> 表示保存成功（调用方刷新列表）。
    /// </para>
    /// </summary>
    public partial class MacroEditWindow : Window
    {
        /// <summary>null = 新建；非 null = 编辑这一条。</summary>
        private readonly MacroItem? _editing;

        /// <summary>新建宏时放进哪个文件夹（<c>""</c> = 未分组）。</summary>
        private readonly string _targetFolderId;

        /// <summary>true = 这个窗口编辑的是**文件夹**（标题/校验/保存都走另一套）。</summary>
        private readonly bool _isFolder;

        private readonly MacroFolder? _editingFolder;

        /// <summary>当前选中的头像名（宏图标）。</summary>
        private string _avatar = Utils.AvatarCatalog.DefaultName;

        /// <summary>全部可选头像（未过滤），来源是资源包里的角色图 + 其他头像。</summary>
        private readonly List<Utils.AvatarItem> _allAvatarItems = new();

        /// <summary>当前选中的筛选；<c>null</c> = 「全部」；<see cref="Utils.AvatarCatalog.OtherFilterKey"/> = 只看其他头像。单选。</summary>
        private string? _selectedElement;

        /// <summary>八个筛选按钮（Tag = 元素名，或「其他」），「全部」按钮单独持有。</summary>
        private List<ToggleButton> _elementToggles = new();

        /// <summary>头像一行固定 5 个（同 UID 添加账号窗口）。</summary>
        private const int AvatarColumns = 5;

        /// <summary>头像列表内部那个 ScrollViewer（外侧滚动条跟着它同步）。</summary>
        private ScrollViewer? _avatarScrollViewer;

        public MacroEditWindow() : this(null)
        {
        }

        public MacroEditWindow(MacroItem? editing, string targetFolderId = "")
            : this(editing, targetFolderId, null, false)
        {
        }

        private MacroEditWindow(MacroItem? editing, string targetFolderId,
                                MacroFolder? editingFolder, bool isFolder)
        {
            InitializeComponent();

            // 独立窗口不会继承主界面的「窗口缩放」，自己跟一下（和 UID 的弹窗同一个处理）
            Utils.HostUiScale.ApplyTo(this);

            _editing = editing;
            _targetFolderId = targetFolderId ?? "";
            _editingFolder = editingFolder;
            _isFolder = isFolder;

            _elementToggles = new List<ToggleButton>
            {
                filterFireToggle, filterWaterToggle, filterWindToggle, filterThunderToggle,
                filterGrassToggle, filterIceToggle, filterRockToggle, filterOtherToggle
            };

            if (isFolder)
            {
                WindowTitle.Text = editingFolder == null ? "新建文件夹" : "编辑文件夹";

                // 文件夹模式：标签和输入框水印换成「文件夹」措辞
                NameLabel.Text = "文件夹名称";
                NameWatermark.Text = "请输入文件夹名称";
                DescLabel.Text = "文件夹描述";

                // 文件夹没有图标、触发方式、生效范围、触发条件
                AvatarSection.Visibility = Visibility.Collapsed;
                ModeSection.Visibility = Visibility.Collapsed;
                ScopeSection.Visibility = Visibility.Collapsed;
                ConditionSection.Visibility = Visibility.Collapsed;

                if (editingFolder != null)
                {
                    NameTextBox.Text = editingFolder.Name;
                    DescTextBox.Text = editingFolder.Description;
                    NameTextBox.CaretIndex = NameTextBox.Text.Length;
                }
            }
            else
            {
                // 头像：编辑时沿用原来的，新建时是默认头像「空」
                // ★ 网格不在这里填 —— 152 个格子一起建会把开窗拖到半秒以上，
                //   改成窗口先出来、网格紧接着补上（见 OnLoaded）。
                _avatar = editing?.Avatar ?? Utils.AvatarCatalog.DefaultName;

                if (editing == null)
                {
                    WindowTitle.Text = "新建宏";
                }
                else
                {
                    WindowTitle.Text = "编辑宏";
                    NameTextBox.Text = editing.Name;
                    DescTextBox.Text = editing.Description;
                    NameTextBox.CaretIndex = NameTextBox.Text.Length;
                }

                int mode = editing?.ModeIndex ?? (int)MacroTriggerMode.Once;
                ModeOnce.IsChecked = mode == (int)MacroTriggerMode.Once;
                ModeHold.IsChecked = mode == (int)MacroTriggerMode.HoldRepeat;
                ModeToggle.IsChecked = mode == (int)MacroTriggerMode.Toggle;

                int scope = editing?.ScopeIndex ?? (int)MacroScope.ClientOnly;
                ScopeGlobal.IsChecked = scope == (int)MacroScope.Global;
                ScopeClient.IsChecked = scope == (int)MacroScope.ClientOnly;

                int condition = editing?.ConditionIndex ?? (int)MacroCondition.None;
                ConditionMain.IsChecked = condition == (int)MacroCondition.MainWindowOnly;
                ConditionNone.IsChecked = condition == (int)MacroCondition.None;

                UpdateOptionDescs();
            }

            UpdateWatermarks();

            Loaded += OnLoaded;
        }

        /// <summary>文件夹版：<c>ForFolder(null)</c> = 新建文件夹，<c>ForFolder(f)</c> = 编辑文件夹。</summary>
        public static MacroEditWindow ForFolder(MacroFolder? editing)
            => new MacroEditWindow(null, "", editing, isFolder: true);

        /// <summary>触发方式换了选项 → 描述行跟着换成那一项的说明。</summary>
        private void ModeOption_Checked(object sender, RoutedEventArgs e)
            => UpdateOptionDescs();

        /// <summary>生效范围换了选项 → 描述行跟着换成那一项的说明。</summary>
        private void ScopeOption_Checked(object sender, RoutedEventArgs e)
            => UpdateOptionDescs();

        /// <summary>触发条件换了选项 → 描述行跟着换成那一项的说明。</summary>
        private void ConditionOption_Checked(object sender, RoutedEventArgs e)
            => UpdateOptionDescs();

        /// <summary>
        /// 按当前选中的选项刷新三行标题（触发方式 / 生效范围 / 触发条件）。
        /// <para>★ 标题行本身就是描述 —— 不额外加一行，选中哪个就显示哪个的说明。</para>
        /// </summary>
        private void UpdateOptionDescs()
        {
            if (ModeTitle == null || ScopeTitle == null || ConditionTitle == null) return;

            ModeTitle.Text = ModeToggle.IsChecked == true
                ? "触发方式：按下重复循环再次按下停止"
                : ModeHold.IsChecked == true
                    ? "触发方式：长按时重复循环松开后停止"
                    : "触发方式：按下时只执行一次";

            ScopeTitle.Text = ScopeGlobal.IsChecked == true
                ? "生效范围：游戏内或游戏外按下都执行"
                : "生效范围：仅在游戏内按下才执行";

            // ★ 触发条件只在「客户区内生效」时才有意义 —— 全局生效 = 任何地方都能触发，
            //   这时把这一栏**置灰且不可改**（配置值留着，切回客户区内就恢复）。
            bool globalScope = ScopeGlobal.IsChecked == true;
            ConditionSection.IsEnabled = !globalScope;
            ConditionSection.Opacity = globalScope ? 0.45 : 1.0;

            ConditionTitle.Text = globalScope
                ? "触发条件：全局生效时此选项不判断"
                : ConditionMain.IsChecked == true
                    ? "触发条件：仅在游戏主界面按下才执行"
                    : "触发条件：任何界面按下都执行";
        }

        /// <summary>
        /// 填充右侧头像列表：角色图 + 其他头像（顺序、默认选中都和 UID 添加账号窗口一致）。
        /// <para>资源包缺失时列表为空，保存时回落到默认头像，不会报错。</para>
        /// </summary>
        private void LoadAvatarItems()
        {
            _allAvatarItems.Clear();
            _allAvatarItems.AddRange(Utils.AvatarCatalog.All);

            // 默认「全部」：清空元素筛选再刷新
            _selectedElement = null;
            SyncFilterToggles();
            ApplyAvatarFilter();

            // 预选：当前已选的头像 → 否则默认头像「空」→ 否则第一个
            var target = _allAvatarItems.FirstOrDefault(i => i.Name == _avatar)
                         ?? _allAvatarItems.FirstOrDefault(i => i.Name == Utils.AvatarCatalog.DefaultName)
                         ?? _allAvatarItems.FirstOrDefault();

            if (target != null)
            {
                avatarListBox.SelectedItem = target;
                avatarListBox.ScrollIntoView(target);
            }
        }

        /// <summary>
        /// 元素筛选按钮点击。<b>单选</b>：
        /// 点某个元素 = 只显示它；再点同一个 = 取消，回到「全部」；点「全部」= 清空筛选。
        /// </summary>
        private void ElementFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton toggle) return;

            // ★ 网格是延迟填的：万一用户手快、网格还没填就点了筛选 → 先补填上再筛
            if (_allAvatarItems.Count == 0) LoadAvatarItems();

            if (toggle.Tag is not string element)
            {
                _selectedElement = null;          // 「全部」
            }
            else if (toggle.IsChecked == true)
            {
                _selectedElement = element;
            }
            else if (_selectedElement == element)
            {
                _selectedElement = null;          // 取消的就是当前选中项 → 回到「全部」
            }

            SyncFilterToggles();
            ApplyAvatarFilter();
        }

        /// <summary>让按钮的勾选状态与 <see cref="_selectedElement"/> 保持一致（互斥）。</summary>
        private void SyncFilterToggles()
        {
            filterAllToggle.IsChecked = _selectedElement == null;

            foreach (var toggle in _elementToggles)
                toggle.IsChecked = toggle.Tag is string el && el == _selectedElement;
        }

        /// <summary>
        /// 按当前筛选刷新头像列表。
        /// 「全部」= 旅行者 + 角色 + 其他头像；选元素 = 只看该元素的角色（旅行者不算元素）；
        /// 选「其他」= 只看其他头像。
        /// 换 <c>ItemsSource</c> 会清空 <c>SelectedItem</c>，所以这里把原来选中的头像恢复回来。
        /// </summary>
        private void ApplyAvatarFilter()
        {
            var previous = avatarListBox.SelectedItem as Utils.AvatarItem;

            List<Utils.AvatarItem> view;
            if (_selectedElement == null)
            {
                view = _allAvatarItems;
            }
            else if (_selectedElement == Utils.AvatarCatalog.OtherFilterKey)
            {
                view = _allAvatarItems.Where(i => i.IsOther).ToList();
            }
            else
            {
                // 旅行者（空 / 荧）没有元素归属：只出现在「全部」里
                view = _allAvatarItems
                    .Where(i => !i.IsOther
                                && !Utils.AvatarCatalog.IsTraveler(i.Name)
                                && Utils.AvatarCatalog.ElementOf(i.Name) == _selectedElement)
                    .ToList();
            }

            avatarListBox.ItemsSource = view;

            if (previous != null && view.Contains(previous))
                avatarListBox.SelectedItem = previous;

            // 换过 ItemsSource 后布局要重新走一遍：重算格子边长 + 重判要不要显示外侧滚动条
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateAvatarGrid();
                SyncAvatarScrollBar();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>找到列表内部的 ScrollViewer 并挂上同步（幂等）。</summary>
        private void HookAvatarScroll()
        {
            if (_avatarScrollViewer != null) return;

            _avatarScrollViewer = FindVisualChild<ScrollViewer>(avatarListBox);
            if (_avatarScrollViewer == null) return;

            _avatarScrollViewer.ScrollChanged += (s, e) => SyncAvatarScrollBar();
            avatarListBox.SizeChanged += (s, e) => UpdateAvatarGrid();
            SyncAvatarScrollBar();

            Dispatcher.BeginInvoke(new Action(UpdateAvatarGrid),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 按容器可用宽度算出格子边长（= 可用宽 ÷ 5）写进 WrapPanel 的 ItemWidth/ItemHeight。
        /// 这样不管容器多宽，都是稳定的「一行 5 个」，横竖间距也一致。
        /// </summary>
        private void UpdateAvatarGrid()
        {
            var panel = FindVisualChild<WrapPanel>(avatarListBox);
            if (panel == null || panel.ActualWidth <= 0) return;

            double cell = Math.Floor(panel.ActualWidth / AvatarColumns);
            if (cell <= 0) return;
            if (Math.Abs(panel.ItemWidth - cell) < 0.5) return;   // 没变化就不动，避免反复触发布局

            panel.ItemWidth = cell;
            panel.ItemHeight = cell;
        }

        /// <summary>
        /// 把内部滚动状态同步给容器外那根滚动条；内容不足一屏时把滚动条藏起来。
        /// 滚动条在容器外面，所以它的显隐/宽度**不会**影响 5 列头像的布局。
        /// </summary>
        private void SyncAvatarScrollBar()
        {
            if (_avatarScrollViewer == null) return;

            bool scrollable = _avatarScrollViewer.ScrollableHeight > 0.5;
            avatarScrollBar.Visibility = scrollable ? Visibility.Visible : Visibility.Collapsed;

            avatarScrollBar.Maximum = _avatarScrollViewer.ScrollableHeight;
            avatarScrollBar.ViewportSize = _avatarScrollViewer.ViewportHeight;
            avatarScrollBar.Value = _avatarScrollViewer.VerticalOffset;
        }

        /// <summary>拖动外部滚动条 → 滚列表。</summary>
        private void AvatarScrollBar_Scroll(object sender, ScrollEventArgs e)
            => _avatarScrollViewer?.ScrollToVerticalOffset(e.NewValue);

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;

                T? found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void AvatarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (avatarListBox.SelectedItem is Utils.AvatarItem item) _avatar = item.Name;
        }

        /// <summary>双击头像 = 直接保存（单击只是选中）。</summary>
        private void AvatarList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (avatarListBox.SelectedItem is Utils.AvatarItem) SaveButton_Click(sender, e);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            NameTextBox.Focus();
            NameTextBox.CaretIndex = NameTextBox.Text.Length;

            // 布局完成后再找列表内部的 ScrollViewer（容器外那根滚动条要跟它同步）
            if (!_isFolder)
            {
                avatarListBox.SelectionChanged += AvatarList_SelectionChanged;

                Dispatcher.BeginInvoke(new Action(HookAvatarScroll),
                    System.Windows.Threading.DispatcherPriority.Loaded);

                // ★ 窗口已经画出来了，再填头像网格（Background 优先级 = 界面空闲才做）
                Dispatcher.BeginInvoke(new Action(LoadAvatarItems),
                    System.Windows.Threading.DispatcherPriority.Background);
            }

            // 淡入（和宿主的弹窗手感一致）
            Opacity = 0;
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateWatermarks();
            ErrorText.Visibility = Visibility.Collapsed;
        }

        /// <summary>水印：输入框为空时显示提示文字。</summary>
        private void UpdateWatermarks()
        {
            if (NameWatermark != null)
                NameWatermark.Visibility = string.IsNullOrEmpty(NameTextBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;

            if (DescWatermark != null)
                DescWatermark.Visibility = string.IsNullOrEmpty(DescTextBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NameTextBox.Text.Trim();
            if (name.Length == 0)
            {
                ShowError(_isFolder ? "请输入文件夹名称" : "请输入宏名称");
                NameTextBox.Focus();
                return;
            }

            // ---------- 文件夹 ----------
            if (_isFolder)
            {
                string exceptFolderId = _editingFolder?.Id ?? "";
                if (MacroSettingsStore.FolderNameExists(name, exceptFolderId))
                {
                    ShowError("已经有同名文件夹了，换个名字吧");
                    NameTextBox.Focus();
                    return;
                }

                if (_editingFolder == null)
                    MacroSettingsStore.AddFolder(name, DescTextBox.Text);
                else
                    MacroSettingsStore.UpdateFolder(_editingFolder.Id, name, DescTextBox.Text);

                DialogResult = true;
                return;
            }

            // ---------- 宏 ----------
            // 名称是区分宏的唯一线索，重名直接拦下
            string exceptId = _editing?.Id ?? "";
            if (MacroSettingsStore.NameExists(name, exceptId))
            {
                ShowError("已经有同名宏了，换个名字吧");
                NameTextBox.Focus();
                return;
            }

            int modeIndex = ModeHold.IsChecked == true ? (int)MacroTriggerMode.HoldRepeat
                          : ModeToggle.IsChecked == true ? (int)MacroTriggerMode.Toggle
                          : (int)MacroTriggerMode.Once;

            int scopeIndex = ScopeGlobal.IsChecked == true
                ? (int)MacroScope.Global
                : (int)MacroScope.ClientOnly;

            int conditionIndex = ConditionMain.IsChecked == true
                ? (int)MacroCondition.MainWindowOnly
                : (int)MacroCondition.None;

            if (_editing == null)
                MacroSettingsStore.Add(name, DescTextBox.Text, _targetFolderId,
                                       _avatar, modeIndex, scopeIndex, conditionIndex);
            else
                MacroSettingsStore.Update(_editing.Id, name, DescTextBox.Text,
                                          _avatar, modeIndex, scopeIndex, conditionIndex);

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
