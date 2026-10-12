using LvchaxsBS_UID.Models;
using System;
using System.Windows;
using System.Windows.Controls;

namespace LvchaxsBS_UID.Services.UI
{
    /// <summary>
    /// UI状态管理器，负责管理主界面按钮的显示状态
    /// </summary>
    public class UIStateManager
    {
        private DisplayMode _currentDisplayMode;
        private bool _isInGroupMode;
        private bool _isAddToGroupMode;
        private bool _hasSearchText;
        private bool _hasAccountsInTotal;

        // 界面控件引用
        public Button AddAccountButton { get; set; }
        public Button BackToAccountListButton { get; set; }
        public Button AddGroupButton { get; set; }
        public Button GroupManagementButton { get; set; }

        // 按钮事件处理器（需要从外部传入）
        public RoutedEventHandler GroupManagementButtonClickHandler { get; set; }
        public RoutedEventHandler ReturnToGroupListClickHandler { get; set; }
        public RoutedEventHandler CreateGroupButtonClickHandler { get; set; }

        // 当前的自定义点击事件处理器
        private RoutedEventHandler _currentAddGroupButtonHandler;

        public enum DisplayMode { AccountList, NoData, GroupManagement }

        public void UpdateState(DisplayMode displayMode, bool isInGroupMode, bool isAddToGroupMode,
            bool hasSearchText, bool hasAccountsInTotal)
        {
            _currentDisplayMode = displayMode;
            _isInGroupMode = isInGroupMode;
            _isAddToGroupMode = isAddToGroupMode;
            _hasSearchText = hasSearchText;
            _hasAccountsInTotal = hasAccountsInTotal;

            UpdateButtonVisibility();
        }

        private void UpdateButtonVisibility()
        {
            bool isMainAccountList = (_currentDisplayMode == DisplayMode.AccountList && !_isInGroupMode);
            bool isGroupManagement = (_currentDisplayMode == DisplayMode.GroupManagement);
            bool isInGroup = (_isInGroupMode && (_currentDisplayMode == DisplayMode.AccountList ||
                             (_currentDisplayMode == DisplayMode.NoData && !_hasSearchText)));
            bool isSearchNoData = (_currentDisplayMode == DisplayMode.NoData && _hasSearchText);

            if (isMainAccountList)
                ShowMainAccountListButtons();
            else if (isGroupManagement)
                ShowGroupManagementButtons();
            else if (isInGroup || _isAddToGroupMode)
                ShowInGroupButtons();
            else if (isSearchNoData)
                HideAllButtons();
            else if (_currentDisplayMode == DisplayMode.NoData && !_hasAccountsInTotal)
                ShowMainAccountListButtons();
            else
                ShowDefaultButtons();
        }

        private void ShowMainAccountListButtons()
        {
            SetButtonVisibility(AddAccountButton, Visibility.Visible);
            SetButtonVisibility(BackToAccountListButton, Visibility.Collapsed);
            SetButtonVisibility(AddGroupButton, Visibility.Collapsed);
            SetButtonVisibility(GroupManagementButton, Visibility.Visible);

            UpdateGroupManagementButtonHandler(GroupManagementButtonClickHandler);
        }

        private void ShowGroupManagementButtons()
        {
            SetButtonVisibility(AddGroupButton, _isAddToGroupMode ? Visibility.Collapsed : Visibility.Visible);
            SetButtonVisibility(BackToAccountListButton, Visibility.Visible);
            SetButtonVisibility(AddAccountButton, Visibility.Collapsed);
            SetButtonVisibility(GroupManagementButton, Visibility.Collapsed);
        }

        private void ShowInGroupButtons()
        {
            SetButtonVisibility(AddAccountButton, Visibility.Collapsed);
            SetButtonVisibility(BackToAccountListButton, Visibility.Visible);
            SetButtonVisibility(GroupManagementButton, Visibility.Visible);

            UpdateGroupManagementButtonHandler(ReturnToGroupListClickHandler);
        }

        private void HideAllButtons()
        {
            SetButtonVisibility(AddAccountButton, Visibility.Collapsed);
            SetButtonVisibility(BackToAccountListButton, Visibility.Collapsed);
            SetButtonVisibility(AddGroupButton, Visibility.Collapsed);
            SetButtonVisibility(GroupManagementButton, Visibility.Collapsed);
        }

        private void ShowDefaultButtons()
        {
            SetButtonVisibility(AddAccountButton, Visibility.Collapsed);
            SetButtonVisibility(BackToAccountListButton, Visibility.Collapsed);
            SetButtonVisibility(AddGroupButton, Visibility.Collapsed);
            SetButtonVisibility(GroupManagementButton, Visibility.Collapsed);
        }

        private void SetButtonVisibility(Button button, Visibility visibility)
        {
            if (button != null)
                button.Visibility = visibility;
        }

        private void UpdateGroupManagementButtonHandler(RoutedEventHandler handler)
        {
            if (GroupManagementButton != null)
            {
                // 先移除所有现有的事件处理器
                if (GroupManagementButtonClickHandler != null)
                    GroupManagementButton.Click -= GroupManagementButtonClickHandler;
                if (ReturnToGroupListClickHandler != null)
                    GroupManagementButton.Click -= ReturnToGroupListClickHandler;

                // 添加新的事件处理器
                if (handler != null)
                    GroupManagementButton.Click += handler;
            }
        }

        /// <summary>
        /// 显示添加账号到分组按钮
        /// </summary>
        public void ShowAddToGroupButton(AccountGroup group, Action<object, RoutedEventArgs> onAddGroupButtonClick)
        {
            if (AddGroupButton != null)
            {
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stackPanel.Children.Add(new TextBlock { Text = "+", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) });
                stackPanel.Children.Add(new TextBlock { Text = "移入账号", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });

                AddGroupButton.Content = stackPanel;
                AddGroupButton.Tag = group;

                // 移除之前的事件处理器
                if (_currentAddGroupButtonHandler != null)
                    AddGroupButton.Click -= _currentAddGroupButtonHandler;

                if (CreateGroupButtonClickHandler != null)
                    AddGroupButton.Click -= CreateGroupButtonClickHandler;

                // 创建新的处理器并保存引用
                _currentAddGroupButtonHandler = (sender, e) => onAddGroupButtonClick?.Invoke(sender, e);
                AddGroupButton.Click += _currentAddGroupButtonHandler;

                SetButtonVisibility(AddGroupButton, Visibility.Visible);
            }
        }

        /// <summary>
        /// 隐藏添加账号到分组按钮
        /// </summary>
        public void HideAddToGroupButton()
        {
            if (AddGroupButton != null)
            {
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stackPanel.Children.Add(new TextBlock { Text = "+", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0) });
                stackPanel.Children.Add(new TextBlock { Text = "添加分组", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });

                AddGroupButton.Content = stackPanel;
                AddGroupButton.Tag = null;

                // 移除自定义的事件处理器
                if (_currentAddGroupButtonHandler != null)
                {
                    AddGroupButton.Click -= _currentAddGroupButtonHandler;
                    _currentAddGroupButtonHandler = null;
                }

                // 恢复原来的点击事件
                if (CreateGroupButtonClickHandler != null)
                {
                    // 先移除可能重复的事件
                    AddGroupButton.Click -= CreateGroupButtonClickHandler;
                    // 再添加
                    AddGroupButton.Click += CreateGroupButtonClickHandler;
                }

                UpdateButtonVisibility();
            }
        }

        /// <summary>
        /// 清理事件订阅（在窗口关闭时调用）
        /// </summary>
        public void Cleanup()
        {
            if (GroupManagementButton != null)
            {
                if (GroupManagementButtonClickHandler != null)
                    GroupManagementButton.Click -= GroupManagementButtonClickHandler;
                if (ReturnToGroupListClickHandler != null)
                    GroupManagementButton.Click -= ReturnToGroupListClickHandler;
            }

            if (AddGroupButton != null)
            {
                if (_currentAddGroupButtonHandler != null)
                {
                    AddGroupButton.Click -= _currentAddGroupButtonHandler;
                    _currentAddGroupButtonHandler = null;
                }

                if (CreateGroupButtonClickHandler != null)
                    AddGroupButton.Click -= CreateGroupButtonClickHandler;
            }
        }
    }
}