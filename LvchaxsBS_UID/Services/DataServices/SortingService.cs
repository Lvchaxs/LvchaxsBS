using LvchaxsBS_UID.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace LvchaxsBS_UID.Services.DataServices
{
    /// <summary>
    /// 排序服务，负责所有账号和分组的排序逻辑
    /// </summary>
    public class SortingService
    {
        #region 账号排序

        /// <summary>
        /// 对账号列表进行排序
        /// </summary>
        public List<Account> SortAccounts(List<Account> accounts, string sortBy,
            ListSortDirection direction, bool isGroupMode, AccountGroup group = null)
        {
            return isGroupMode
                ? SortGroupAccountsWithMarking(accounts, sortBy, direction, group)
                : SortMainAccountsWithPinning(accounts, sortBy, direction);
        }

        /// <summary>
        /// 主账号列表排序（包含置顶逻辑）
        /// </summary>
        private List<Account> SortMainAccountsWithPinning(List<Account> accounts, string sortBy, ListSortDirection direction)
        {
            var pinnedAccounts = accounts.Where(a => a.IsPinned).ToList();
            var unpinnedAccounts = accounts.Where(a => !a.IsPinned).ToList();

            // 如果按置顶排序，直接返回
            if (sortBy == "IsPinned")
                return direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.IsPinned).ToList()
                    : accounts.OrderByDescending(a => a.IsPinned).ToList();

            // 分别对置顶和未置顶账号排序
            var sortedPinned = SortAccountList(pinnedAccounts, sortBy, direction);
            var sortedUnpinned = SortAccountList(unpinnedAccounts, sortBy, direction);

            // 合并结果：置顶账号在前
            var result = new List<Account>();
            result.AddRange(sortedPinned);
            result.AddRange(sortedUnpinned);
            return result;
        }

        /// <summary>
        /// 分组内账号排序（包含标记逻辑）
        /// </summary>
        private List<Account> SortGroupAccountsWithMarking(List<Account> accounts, string sortBy,
            ListSortDirection direction, AccountGroup group)
        {
            var markedAccounts = accounts.Where(a => a.IsMarked).ToList();
            var unmarkedAccounts = accounts.Where(a => !a.IsMarked).ToList();

            // 如果按标记排序，直接返回
            if (sortBy == "IsMarked")
                return direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.IsMarked).ToList()
                    : accounts.OrderByDescending(a => a.IsMarked).ToList();

            // 分别对标记和未标记账号排序
            var sortedMarked = SortAccountList(markedAccounts, sortBy, direction);
            var sortedUnmarked = SortAccountList(unmarkedAccounts, sortBy, direction);

            // 合并结果：未标记在前，标记在后（默认显示顺序）
            var result = new List<Account>();
            result.AddRange(sortedUnmarked);
            result.AddRange(sortedMarked);
            return result;
        }

        /// <summary>
        /// 基础账号列表排序
        /// </summary>
        private List<Account> SortAccountList(List<Account> accounts, string sortBy, ListSortDirection direction)
        {
            return sortBy switch
            {
                "Uid" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Uid).ToList()
                    : accounts.OrderByDescending(a => a.Uid).ToList(),
                "Level" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Level).ThenBy(a => a.WorldLevel).ToList()
                    : accounts.OrderByDescending(a => a.Level).ThenByDescending(a => a.WorldLevel).ToList(),
                "Nickname" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Nickname).ToList()
                    : accounts.OrderByDescending(a => a.Nickname).ToList(),
                "Username" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Username).ToList()
                    : accounts.OrderByDescending(a => a.Username).ToList(),
                "IsMarked" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.IsMarked).ToList()
                    : accounts.OrderByDescending(a => a.IsMarked).ToList(),
                "IsPinned" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.IsPinned).ToList()
                    : accounts.OrderByDescending(a => a.IsPinned).ToList(),
                "Id" => direction == ListSortDirection.Ascending
                    ? accounts.OrderBy(a => a.Id).ToList()
                    : accounts.OrderByDescending(a => a.Id).ToList(),
                _ => accounts
            };
        }

        #endregion

        #region 分组排序

        /// <summary>
        /// 对分组列表进行排序（包含置顶逻辑）
        /// </summary>
        public List<AccountGroup> SortGroups(List<AccountGroup> groups)
        {
            if (groups != null && groups.Count > 0)
            {
                return groups
                    .OrderByDescending(g => g.IsPinned)
                    .ThenBy(g => g.IsPinned ? g.PinnedTime : g.CreatedTime)
                    .ToList();
            }
            return groups;
        }

        #endregion

        #region 显示排序（用于界面显示）

        /// <summary>
        /// 获取分组显示顺序的账号列表（按添加时间和标记时间）
        /// </summary>
        public List<Account> GetGroupDisplayOrder(List<Account> accounts, AccountGroup group)
        {
            var markedAccounts = accounts.Where(a => group.IsAccountMarked(a.Id)).ToList();
            var unmarkedAccounts = accounts.Where(a => !group.IsAccountMarked(a.Id)).ToList();

            // 未标记账号按添加时间排序
            var sortedUnmarked = unmarkedAccounts
                .OrderBy(a => group.AccountAddTimes?.ContainsKey(a.Id) == true
                    ? group.AccountAddTimes[a.Id]
                    : a.CreatedTime)
                .ToList();

            // 已标记账号按标记时间排序
            var sortedMarked = markedAccounts
                .OrderBy(a => group.MarkedTimes?.ContainsKey(a.Id) == true
                    ? group.MarkedTimes[a.Id]
                    : a.CreatedTime)
                .ToList();

            // 合并：未标记在前，标记在后
            var result = new List<Account>();
            result.AddRange(sortedUnmarked);
            result.AddRange(sortedMarked);
            return result;
        }

        /// <summary>
        /// 获取主账号列表显示顺序（按置顶时间）
        /// </summary>
        public List<Account> GetMainDisplayOrder(List<Account> accounts)
        {
            var pinnedAccounts = accounts.Where(a => a.IsPinned)
                .OrderBy(a => a.PinnedTime)
                .ToList();

            var unpinnedAccounts = accounts.Where(a => !a.IsPinned)
                .OrderBy(a => a.CreatedTime)
                .ToList();

            // 合并：置顶账号在前
            var result = new List<Account>();
            result.AddRange(pinnedAccounts);
            result.AddRange(unpinnedAccounts);
            return result;
        }

        #endregion

        #region 过滤和搜索

        /// <summary>
        /// 过滤账号列表
        /// </summary>
        public List<Account> FilterAccounts(List<Account> accounts, string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return accounts;

            return accounts.Where(a =>
                a.Uid.StartsWith(searchTerm) ||
                (a.Nickname != null && a.Nickname.StartsWith(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (a.Username != null && a.Username.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        #endregion

        #region 分组信息处理

        /// <summary>
        /// 创建分组显示对象
        /// </summary>
        public object CreateGroupDisplayObject(AccountGroup group, List<Account> allAccounts)
        {
            var groupAccounts = allAccounts.Where(a => group.AccountIds.Contains(a.Id)).ToList();
            int totalAccountCount = groupAccounts.Count;
            int markedCount = group.MarkedTimes?.Count ?? 0;

            string displayText = $"包含 {totalAccountCount} 个账号";
            if (markedCount > 0)
                displayText += $" • 已置底 {markedCount} • 未置底 {totalAccountCount - markedCount}";

            string displayTagNote = string.IsNullOrWhiteSpace(group.TagNote) ? string.Empty : $"• {group.TagNote}";

            return new
            {
                Id = group.Id,
                Name = group.Name,
                TagNote = displayTagNote,
                AccountIds = group.AccountIds,
                AccountCount = totalAccountCount,
                DisplayText = displayText,
                HasAccounts = totalAccountCount > 0,
                HasMarks = markedCount > 0,
                MarkedCount = markedCount,
                IsPinned = group.IsPinned,
                CreatedTime = group.CreatedTime,
                PinnedTime = group.PinnedTime
            };
        }

        #endregion
    }
}