using LvchaxsBS_UID.Models;
using System;
using System.Windows;
using System.Windows.Controls;

namespace LvchaxsBS_UID.Services.UI
{
    /// <summary>
    /// 界面状态管理器 - 基于明确的界面概念管理按钮状态
    /// </summary>
    public class ViewStateManager
    {
        // 界面枚举
        public enum AppView
        {
            Home,           // 主页（账号列表）
            GroupManagement, // 分组管理页面
            GroupDetail,    // 分组详情页面（分组内）
            MoveMode        // 「移入账号」模式（复用账号列表页面，只留「移入 / 取消」两个按钮）
        }

        private AppView _currentView = AppView.Home;

        // 界面控件引用（新增移入账号按钮）
        public Button AddAccountButton { get; set; }
        public Button BackToAccountListButton { get; set; }
        public Button AddGroupButton { get; set; }
        public Button AddAccountsToGroupButton { get; set; }  // 新增
        public Button GroupManagementButton { get; set; }

        // 「移入账号」模式下专用的两个按钮
        public Button MoveConfirmButton { get; set; }
        public Button MoveCancelButton { get; set; }

        // 按钮事件处理器
        public RoutedEventHandler HomeViewGroupManagementClick { get; set; }
        public RoutedEventHandler GroupDetailReturnToGroupListClick { get; set; }
        public RoutedEventHandler CreateGroupButtonClick { get; set; }
        public RoutedEventHandler AddAccountsToGroupClick { get; set; }

        /// <summary>
        /// 切换到指定界面并更新按钮状态
        /// </summary>
        public void SwitchToView(AppView view, AccountGroup groupForAdding = null)
        {
            _currentView = view;

            UpdateButtonVisibility(groupForAdding);
        }

        /// <summary>
        /// 获取当前界面
        /// </summary>
        public AppView GetCurrentView()
        {
            return _currentView;
        }

        /// <summary>
        /// 更新按钮可见性和功能
        /// </summary>
        private void UpdateButtonVisibility(AccountGroup groupForAdding = null)
        {
            // 先隐藏所有按钮
            HideAllButtons();

            // 根据当前界面显示相应按钮
            switch (_currentView)
            {
                case AppView.Home:
                    ShowHomeViewButtons();
                    break;

                case AppView.GroupManagement:
                    ShowGroupManagementViewButtons();
                    break;

                case AppView.GroupDetail:
                    ShowGroupDetailViewButtons(groupForAdding);
                    break;

                case AppView.MoveMode:
                    ShowMoveModeButtons();
                    break;
            }
        }

        /// <summary>
        /// 「移入账号」模式：只留「移入 / 取消」两个按钮，
        /// 分组管理 / 添加账号 / 返回账号列表 / 添加分组 / 移入账号 全部隐藏。
        /// </summary>
        private void ShowMoveModeButtons()
        {
            SetButtonVisibility(MoveConfirmButton, Visibility.Visible);
            SetButtonVisibility(MoveCancelButton, Visibility.Visible);
        }

        /// <summary>
        /// 主页（账号列表）按钮
        /// </summary>
        private void ShowHomeViewButtons()
        {
            // 主页显示：添加账号按钮、分组管理按钮
            SetButtonVisibility(AddAccountButton, Visibility.Visible);
            SetButtonVisibility(GroupManagementButton, Visibility.Visible);
        }

        /// <summary>
        /// 分组管理页面按钮
        /// </summary>
        private void ShowGroupManagementViewButtons()
        {
            // 分组管理页面显示：添加分组按钮、返回账号列表按钮
            SetButtonVisibility(AddGroupButton, Visibility.Visible);
            SetButtonVisibility(BackToAccountListButton, Visibility.Visible);
        }

        /// <summary>
        /// 分组详情页面（分组内）按钮
        /// </summary>
        private void ShowGroupDetailViewButtons(AccountGroup group)
        {
            // 分组详情页面显示：返回账号列表按钮、分组管理按钮、移入账号按钮
            SetButtonVisibility(BackToAccountListButton, Visibility.Visible);
            SetButtonVisibility(GroupManagementButton, Visibility.Visible);
            SetButtonVisibility(AddAccountsToGroupButton, Visibility.Visible);

            // 设置分组管理按钮功能为返回分组列表
            UpdateGroupManagementButtonHandler(GroupDetailReturnToGroupListClick);

            // 设置移入账号按钮的Tag为当前分组
            if (AddAccountsToGroupButton != null && group != null)
            {
                AddAccountsToGroupButton.Tag = group;
            }
        }

        /// <summary>
        /// 隐藏所有按钮
        /// </summary>
        private void HideAllButtons()
        {
            SetButtonVisibility(AddAccountButton, Visibility.Collapsed);
            SetButtonVisibility(BackToAccountListButton, Visibility.Collapsed);
            SetButtonVisibility(AddGroupButton, Visibility.Collapsed);
            SetButtonVisibility(AddAccountsToGroupButton, Visibility.Collapsed);
            SetButtonVisibility(GroupManagementButton, Visibility.Collapsed);
            SetButtonVisibility(MoveConfirmButton, Visibility.Collapsed);
            SetButtonVisibility(MoveCancelButton, Visibility.Collapsed);
        }

        /// <summary>
        /// 设置按钮可见性
        /// </summary>
        private void SetButtonVisibility(Button button, Visibility visibility)
        {
            if (button != null)
                button.Visibility = visibility;
        }

        /// <summary>
        /// 更新分组管理按钮的事件处理器
        /// </summary>
        private void UpdateGroupManagementButtonHandler(RoutedEventHandler handler)
        {
            if (GroupManagementButton != null)
            {
                // 先移除可能的事件处理器
                GroupManagementButton.Click -= HomeViewGroupManagementClick;
                GroupManagementButton.Click -= GroupDetailReturnToGroupListClick;

                // 添加新的事件处理器
                if (handler != null)
                    GroupManagementButton.Click += handler;
            }
        }

        /// <summary>
        /// 清理事件订阅
        /// </summary>
        public void Cleanup()
        {
            if (GroupManagementButton != null)
            {
                GroupManagementButton.Click -= HomeViewGroupManagementClick;
                GroupManagementButton.Click -= GroupDetailReturnToGroupListClick;
            }
        }
    }
}