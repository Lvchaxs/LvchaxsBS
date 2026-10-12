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
        private Dictionary<int, DateTime> _accountAddTimes = new Dictionary<int, DateTime>();
        private Dictionary<int, DateTime> _markedTimes = new Dictionary<int, DateTime>(); //标记时间
        private bool _isPinned = false;
        private DateTime _createdTime;
        private DateTime? _pinnedTime;

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

        [JsonPropertyName("accountAddTimes")]
        public Dictionary<int, DateTime> AccountAddTimes
        {
            get => _accountAddTimes;
            set
            {
                _accountAddTimes = value ?? new Dictionary<int, DateTime>();
                OnPropertyChanged(nameof(AccountAddTimes));
            }
        }

        [JsonPropertyName("markedTimes")]
        public Dictionary<int, DateTime> MarkedTimes
        {
            get => _markedTimes;
            set
            {
                _markedTimes = value ?? new Dictionary<int, DateTime>();
                OnPropertyChanged(nameof(MarkedTimes));
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

        [JsonPropertyName("createdTime")]
        public DateTime CreatedTime
        {
            get => _createdTime;
            set
            {
                _createdTime = value;
                OnPropertyChanged(nameof(CreatedTime));
            }
        }

        [JsonPropertyName("pinnedTime")]
        public DateTime? PinnedTime
        {
            get => _pinnedTime;
            set
            {
                _pinnedTime = value;
                OnPropertyChanged(nameof(PinnedTime));
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
            _createdTime = DateTime.Now;
            _accountAddTimes = new Dictionary<int, DateTime>();
            _markedTimes = new Dictionary<int, DateTime>();
        }

        // 添加账号到分组并记录时间
        public void AddAccount(int accountId)
        {
            if (!_accountIds.Contains(accountId))
            {
                _accountIds.Add(accountId);
                _accountAddTimes[accountId] = DateTime.Now;
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
                if (_accountAddTimes.ContainsKey(accountId))
                {
                    _accountAddTimes.Remove(accountId);
                }
                if (_markedTimes.ContainsKey(accountId))
                {
                    _markedTimes.Remove(accountId);
                }
                OnPropertyChanged(nameof(AccountIds));
                OnPropertyChanged(nameof(AccountCount));
            }
        }

        // 批量添加账号
        public void AddAccounts(List<int> accountIds)
        {
            var now = DateTime.Now;
            foreach (var accountId in accountIds)
            {
                if (!_accountIds.Contains(accountId))
                {
                    _accountIds.Add(accountId);
                    _accountAddTimes[accountId] = now;
                }
            }
            OnPropertyChanged(nameof(AccountIds));
            OnPropertyChanged(nameof(AccountCount));
        }

        // 获取账号移入时间
        public DateTime GetAccountAddTime(int accountId)
        {
            if (_accountAddTimes.ContainsKey(accountId))
            {
                return _accountAddTimes[accountId];
            }
            // 如果没有记录时间，返回当前时间
            return DateTime.Now;
        }

        // 标记账号（记录标记时间）
        public void MarkAccount(int accountId)
        {
            if (!_markedTimes.ContainsKey(accountId))
            {
                _markedTimes[accountId] = DateTime.Now;
            }
            else
            {
                // 更新标记时间
                _markedTimes[accountId] = DateTime.Now;
            }
        }

        // 取消标记账号
        public void UnmarkAccount(int accountId)
        {
            if (_markedTimes.ContainsKey(accountId))
            {
                _markedTimes.Remove(accountId);
            }
        }

        // 获取账号标记时间
        public DateTime? GetMarkedTime(int accountId)
        {
            if (_markedTimes.ContainsKey(accountId))
            {
                return _markedTimes[accountId];
            }
            return null;
        }

        // 检查账号是否被标记
        public bool IsAccountMarked(int accountId)
        {
            return _markedTimes.ContainsKey(accountId);
        }

        // 设置置顶时间
        public void SetPinned(bool isPinned)
        {
            IsPinned = isPinned;
            if (isPinned)
            {
                PinnedTime = DateTime.Now;
            }
            else
            {
                PinnedTime = null;
            }
        }
    }
}