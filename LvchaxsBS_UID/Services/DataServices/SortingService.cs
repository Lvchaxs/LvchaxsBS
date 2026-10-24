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
        /// ★ 自由排序：置顶分组在前、普通分组在后，两区各自按 <see cref="AccountGroup.Order"/> 排、互不跨界。
        /// </summary>
        public List<AccountGroup> SortGroups(List<AccountGroup> groups)
        {
            if (groups != null && groups.Count > 0)
            {
                return groups
                    .OrderByDescending(g => g.IsPinned)
                    .ThenBy(g => g.Order)
                    .ToList();
            }
            return groups;
        }

        #endregion

        #region 显示排序（用于界面显示）

        /// <summary>
        /// 获取分组显示顺序的账号列表。
        /// ★ 自由排序：未置底区在前、已置底区沉底，两区各自按 <see cref="Account.Order"/> 排、互不跨界。
        /// </summary>
        public List<Account> GetGroupDisplayOrder(List<Account> accounts, AccountGroup group)
        {
            var markedAccounts = accounts.Where(a => group.IsAccountMarked(a.Id))
                .OrderBy(a => a.Order)
                .ToList();
            var unmarkedAccounts = accounts.Where(a => !group.IsAccountMarked(a.Id))
                .OrderBy(a => a.Order)
                .ToList();

            // 合并：未置底在前，已置底在后
            var result = new List<Account>();
            result.AddRange(unmarkedAccounts);
            result.AddRange(markedAccounts);
            return result;
        }

        /// <summary>
        /// 获取主账号列表显示顺序。
        /// ★ 自由排序：置顶区在前、普通区在后，两区各自按 <see cref="Account.Order"/> 排、互不跨界。
        /// </summary>
        public List<Account> GetMainDisplayOrder(List<Account> accounts)
        {
            var pinnedAccounts = accounts.Where(a => a.IsPinned)
                .OrderBy(a => a.Order)
                .ToList();

            var unpinnedAccounts = accounts.Where(a => !a.IsPinned)
                .OrderBy(a => a.Order)
                .ToList();

            // 合并：置顶账号在前
            var result = new List<Account>();
            result.AddRange(pinnedAccounts);
            result.AddRange(unpinnedAccounts);
            return result;
        }

        #endregion

        #region 自由排序（Order）的赋值规则

        /// <summary>
        /// 老数据迁移：账号文件里还没有 order 时，按**文件里的原有顺序**补一遍排序号，
        /// 让升级后第一眼看到的顺序和升级前完全一致（置顶区在前，同区内保持原顺序）。
        /// ★ 只在**整批全 0** 时执行：0 本身是合法值（第一张卡），不能拿单条是不是 0 判断。
        /// ★ 只改内存、不落盘；下一次任意保存（编辑账号、置顶、拖动排序…）自然会带出去。
        /// ★ 日期字段已删除（排序只看 order）→ 不再按时间排：`OrderByDescending` 是**稳定排序**，
        ///   同区内的相对顺序就是文件里的顺序（而文件顺序本身就是升级前的显示顺序）。
        /// </summary>
        public static void EnsureAccountOrders(List<Account> accounts)
        {
            if (accounts == null || accounts.Count == 0) return;
            if (accounts.Any(a => a.Order != 0)) return;

            int i = 0;
            foreach (var account in accounts.OrderByDescending(a => a.IsPinned))
                account.Order = i++;
        }

        /// <summary>分组的老数据迁移，规则同 <see cref="EnsureAccountOrders"/>（置顶在前，同区内保持文件顺序）。</summary>
        public static void EnsureGroupOrders(List<AccountGroup> groups)
        {
            if (groups == null || groups.Count == 0) return;
            if (groups.Any(g => g.Order != 0)) return;

            int i = 0;
            foreach (var group in groups.OrderByDescending(g => g.IsPinned))
                group.Order = i++;
        }

        /// <summary>置顶 / 取消置顶后重写排序号（照前端模拟的规则）。</summary>
        /// <param name="accounts">全部账号（含自己，且自己的 IsPinned 已经是新值）</param>
        /// <param name="account">被操作的那个账号</param>
        /// <remarks>
        /// · 置顶 → 排到**置顶区末尾**（Order = 置顶区最大值 + 1）；
        /// · 取消置顶 → 排到**普通区最上方**（Order = 普通区最小值 − 1，紧贴置顶区下方）。
        /// </remarks>
        public static void AssignPinOrder(List<Account> accounts, Account account)
        {
            if (account.IsPinned)
                account.Order = MaxOrder(accounts.Where(a => a.IsPinned)) + 1;
            else
                account.Order = MinOrder(accounts.Where(a => !a.IsPinned)) - 1;
        }

        /// <summary>分组置顶 / 取消置顶后重写排序号，规则同 <see cref="AssignPinOrder(List{Account}, Account)"/>。</summary>
        public static void AssignGroupPinOrder(List<AccountGroup> groups, AccountGroup group)
        {
            if (group.IsPinned)
                group.Order = MaxOrder(groups.Where(g => g.IsPinned)) + 1;
            else
                group.Order = MinOrder(groups.Where(g => !g.IsPinned)) - 1;
        }

        /// <summary>
        /// 置底 / 取消置底后重写排序号（照前端模拟的规则）。
        /// · 置底 → 排到**置底区末尾**；
        /// · 取消置底 → 排到**未置底区末尾**（不是最上方，和前端一致）。
        /// </summary>
        public static void AssignMarkOrder(List<Account> accounts, Account account, AccountGroup group)
        {
            bool marked = group.IsAccountMarked(account.Id);
            account.Order = MaxOrder(accounts.Where(a => group.IsAccountMarked(a.Id) == marked)) + 1;
        }

        /// <summary>新账号 / 新分组的排序号：排在普通区最末尾（Order = 全局最大值 + 1）。</summary>
        public static int NextOrder<T>(IEnumerable<T> items, Func<T, int> selector)
        {
            int max = -1;
            foreach (var item in items)
            {
                int value = selector(item);
                if (value > max) max = value;
            }
            return max + 1;
        }

        /// <summary>拖动排序落单：按当前显示顺序把 Order 写成 0,1,2…（只重排本视图内的条目）。</summary>
        public static void ApplyDisplayOrder(IEnumerable<Account> displayOrder)
        {
            int i = 0;
            foreach (var account in displayOrder)
                account.Order = i++;
        }

        /// <summary>拖动排序落单（分组版）。</summary>
        public static void ApplyDisplayOrder(IEnumerable<AccountGroup> displayOrder)
        {
            int i = 0;
            foreach (var group in displayOrder)
                group.Order = i++;
        }

        private static int MaxOrder(IEnumerable<Account> accounts)
        {
            int max = -1;
            foreach (var a in accounts) if (a.Order > max) max = a.Order;
            return max;
        }

        private static int MinOrder(IEnumerable<Account> accounts)
        {
            int min = int.MaxValue;
            foreach (var a in accounts) if (a.Order < min) min = a.Order;
            return min;
        }

        private static int MaxOrder(IEnumerable<AccountGroup> groups)
        {
            int max = -1;
            foreach (var g in groups) if (g.Order > max) max = g.Order;
            return max;
        }

        private static int MinOrder(IEnumerable<AccountGroup> groups)
        {
            int min = int.MaxValue;
            foreach (var g in groups) if (g.Order < min) min = g.Order;
            return min;
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
        /// 创建分组显示对象。
        /// ★ 由匿名类型改成 <see cref="GroupDisplayItem"/>（带 INotifyPropertyChanged）——
        ///   匿名类型改属性界面不刷新，做不了「先显示星、动画播完再改数据」的置顶动画预告。
        /// </summary>
        public GroupDisplayItem CreateGroupDisplayObject(AccountGroup group, List<Account> allAccounts)
        {
            var groupAccounts = allAccounts.Where(a => group.AccountIds.Contains(a.Id)).ToList();
            int totalAccountCount = groupAccounts.Count;
            int markedCount = group.MarkedAccountIds?.Count ?? 0;

            // 三个计数直接给三个标签用（不再拼成一整串文字）
            return new GroupDisplayItem
            {
                Id = group.Id,
                Name = group.Name,
                Remark = group.TagNote ?? "",
                AccountIds = group.AccountIds,
                AccountCount = totalAccountCount,
                MarkedCount = markedCount,
                UnmarkedCount = totalAccountCount - markedCount,
                HasMarks = markedCount > 0,
                IsPinned = group.IsPinned,
                // 分组的置顶标识：和账号卡片同一套（置顶了才显示左缘那条蓝色竖条）
                ShowStar = group.IsPinned
            };
        }

        #endregion
    }
}
