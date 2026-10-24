using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LvchaxsBS_Macro.Models
{
    /// <summary>
    /// 一个宏文件夹（分组）。
    /// <para>
    /// ★ 宏通过 <see cref="MacroItem.FolderId"/> 归属到文件夹；<c>FolderId = ""</c> 表示未分组。
    /// ★ 删文件夹时**不会删宏**，里面的宏会退回未分组（见 <c>MacroSettingsStore.RemoveFolder</c>）。
    /// </para>
    /// </summary>
    public class MacroFolder : INotifyPropertyChanged
    {
        private string _id = "";
        private string _name = "";
        private string _description = "";
        private bool _isPinned;
        private bool _showStar;
        private int _macroCount;

        /// <summary>唯一 Id（删改都按它认人，重命名不影响归属）。</summary>
        public string Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        /// <summary>文件夹名。</summary>
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        /// <summary>备注（可空）。</summary>
        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); } }
        }

        /// <summary>有没有备注（卡片第二行显隐用）。</summary>
        [JsonIgnore]
        public bool HasDescription => !string.IsNullOrWhiteSpace(_description);

        /// <summary>
        /// 置顶（点卡片上的文件夹图标切换）。
        /// <para>★ 和 <see cref="MacroItem.IsPinned"/> 完全同一套语义：只翻标志位，然后对整表做稳定排序。</para>
        /// </summary>
        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned == value) return;
                _isPinned = value;
                OnPropertyChanged(nameof(IsPinned));
            }
        }

        /// <summary>
        /// 卡片左缘的置顶竖条当前该不该显示。
        /// <para>
        /// ★ 它和 <see cref="IsPinned"/> **分开**：置顶时先把它打开播展开动画，动画播完才改 <see cref="IsPinned"/>
        /// （同 UID 分组卡片 / 账号卡片）。直接改 IsPinned 会立刻重建容器 → 高亮「突然出现」。
        /// </para>
        /// 不参与序列化 —— 纯界面状态，重开程序按 IsPinned 重新同步。
        /// </summary>
        [JsonIgnore]
        public bool ShowStar
        {
            get => _showStar;
            set
            {
                if (_showStar == value) return;
                _showStar = value;
                OnPropertyChanged(nameof(ShowStar));
            }
        }

        /// <summary>
        /// 里面有几个宏（卡片名称右边那个蓝标签用）。
        /// 纯界面数据，由 <c>MacroConfigPage.ReloadTopLevel</c> 每次刷新时填。
        /// </summary>
        [JsonIgnore]
        public int MacroCount
        {
            get => _macroCount;
            set
            {
                if (_macroCount == value) return;
                _macroCount = value;
                OnPropertyChanged(nameof(MacroCount));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
