using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace LvchaxsBS_UID.Models
{
    public class AccountGroup : INotifyPropertyChanged
    {
        private int _id;
        private string _name = "";
        private string _tagNote = "";
        private List<int> _accountIds = new List<int>();
        /// <summary>该分组里**已置底**的账号编号集合。★ 只记编号、不记时间 —— 排序由 <see cref="Order"/> 决定。</summary>
        private List<int> _markedAccountIds = new List<int>();
        private bool _isPinned = false;
        private int _order;

        [JsonPropertyName("id")]
        public int Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        [JsonPropertyName("name")]
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(AccountCount));
            }
        }

        // 新增：标签备注属性
        [JsonPropertyName("tagNote")]
        public string TagNote
        {
            get => _tagNote;
            set
            {
                _tagNote = value ?? "";
                OnPropertyChanged(nameof(TagNote));
            }
        }

        [JsonPropertyName("accountIds")]
        public List<int> AccountIds
        {
            get => _accountIds;
            set
            {
                _accountIds = value ?? new List<int>();
                OnPropertyChanged(nameof(AccountIds));
                OnPropertyChanged(nameof(AccountCount));
            }
        }

        /// <summary>
        /// 该分组里**已置底**的账号编号（「清除置底」按钮与卡片计数都读它）。
        /// ★ 以前这里是 <c>Dictionary&lt;int, DateTime&gt; MarkedTimes</c>（键=账号、值=置底时间），
        ///   但那个时间**从来没被读过** —— 界面只关心"在不在集合里"，排序也早就改用 order 了。
        ///   于是去掉时间值，退化成纯编号列表，json 里不再出现任何日期。
        ///   ★ 老文件里的 <c>markedTimes</c> 由 <see cref="Services.DataServices.GroupDataService"/> 加载时迁移过来。
        /// </summary>
        [JsonPropertyName("markedAccountIds")]
        public List<int> MarkedAccountIds
        {
            get => _markedAccountIds;
            set
            {
                _markedAccountIds = value ?? new List<int>();
                OnPropertyChanged(nameof(MarkedAccountIds));
            }
        }

        [JsonPropertyName("isPinned")]
        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned != value)
                {
                    _isPinned = value;
                    OnPropertyChanged(nameof(IsPinned));
                }
            }
        }

        /// <summary>
        /// 手动排序号（「自由排序」）：**取代了按创建时间 / 置顶时间排序**，数字越小越靠前。
        /// 置顶分组与普通分组各自独立按它排（和账号的 <see cref="Account.Order"/> 同一套规则）。
        /// ★ 0 是合法值；老数据没有排序号时按整批全 0 判断，见 <see cref="SortingService.EnsureGroupOrders"/>。
        /// </summary>
        [JsonPropertyName("order")]
        public int Order
        {
            get => _order;
            set
            {
                _order = value;
                OnPropertyChanged(nameof(Order));
            }
        }

        [JsonIgnore]
        public int AccountCount => _accountIds?.Count ?? 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 构造函数
        public AccountGroup()
        {
        }

        // 添加账号到分组
        public void AddAccount(int accountId)
        {
            if (!_accountIds.Contains(accountId))
            {
                _accountIds.Add(accountId);
                OnPropertyChanged(nameof(AccountIds));
                OnPropertyChanged(nameof(AccountCount));
            }
        }

        // 从分组移除账号
        public void RemoveAccount(int accountId)
        {
            if (_accountIds.Contains(accountId))
            {
                _accountIds.Remove(accountId);
                _markedAccountIds.Remove(accountId);
                OnPropertyChanged(nameof(AccountIds));
                OnPropertyChanged(nameof(AccountCount));
            }
        }

        // 批量添加账号
        public void AddAccounts(List<int> accountIds)
        {
            foreach (var accountId in accountIds)
            {
                if (!_accountIds.Contains(accountId))
                {
                    _accountIds.Add(accountId);
                }
            }
            OnPropertyChanged(nameof(AccountIds));
            OnPropertyChanged(nameof(AccountCount));
        }

        // 置底账号
        public void MarkAccount(int accountId)
        {
            if (!_markedAccountIds.Contains(accountId))
                _markedAccountIds.Add(accountId);
        }

        // 取消置底
        public void UnmarkAccount(int accountId)
        {
            _markedAccountIds.Remove(accountId);
        }

        // 检查账号是否已置底
        public bool IsAccountMarked(int accountId)
        {
            return _markedAccountIds.Contains(accountId);
        }

        // 置顶 / 取消置顶（排序号由调用方按自由排序规则改，见 SortingService.AssignGroupPinOrder）
        public void SetPinned(bool isPinned)
        {
            IsPinned = isPinned;
        }
    }
}