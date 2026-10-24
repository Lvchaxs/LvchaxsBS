using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace LvchaxsBS_Macro.Models
{
    /// <summary>
    /// 触发方式（2026-10-19 用户定的三种）。
    /// <para>
    /// ⚠️ 0 和 2 的语义在 2026-10-19 **改过**：旧 0=ToggleOnPress、旧 2=OnRelease，
    /// 新 0=Once、新 2=Toggle —— 旧配置靠 <c>MacroSettings.SchemaVersion</c> 一次性迁移（0↔2 互换）。
    /// </para>
    /// </summary>
    public enum MacroTriggerMode
    {
        /// <summary>按下仅执行一次：按下后把整段脚本跑一遍就结束（不用等抬手）。</summary>
        Once = 0,

        /// <summary>长按时重复循环：执行完一次后继续重复，抬起后结束。</summary>
        HoldRepeat = 1,

        /// <summary>按下触发循环、再次按下结束（和「拾取」一个手感）。</summary>
        Toggle = 2,
    }

    /// <summary>
    /// 触发方式的显示名 —— <b>顺序必须和 <see cref="MacroTriggerMode"/> 的枚举值一致</b>。
    /// </summary>
    public static class MacroTriggerModeNames
    {
        public static readonly string[] All =
        {
            "执行一次",
            "长按循环",
            "循环执行",
        };
    }

    /// <summary>
    /// 生效范围 —— 决定「触发键按下时，游戏窗口必须是什么状态才响应」。
    /// </summary>
    public enum MacroScope
    {
        /// <summary>全局生效：任何窗口聚焦时按下触发键都生效。</summary>
        Global = 0,

        /// <summary>客户区内生效：只有游戏窗口处于前台时才响应。</summary>
        ClientOnly = 1,
    }

    /// <summary>
    /// 生效范围的显示名 —— <b>顺序必须和 <see cref="MacroScope"/> 的枚举值一致</b>。
    /// </summary>
    public static class MacroScopeNames
    {
        public static readonly string[] All =
        {
            "全局生效",
            "客户区内生效",
        };
    }

    /// <summary>
    /// 触发条件 —— 决定「按下触发键的那一刻，游戏画面必须处于什么状态才执行」。
    /// <para>★ 和 <see cref="MacroScope"/> 是两回事：那个看的是**窗口焦点**（在不在游戏窗口里），
    ///   这个看的是**游戏画面**（是不是主界面）。两个条件都满足才执行。</para>
    /// </summary>
    public enum MacroCondition
    {
        /// <summary>无条件：不看画面状态，只要生效范围满足就执行。</summary>
        None = 0,

        /// <summary>主界面：只有游戏处在主界面（不在地图等其它界面）时才执行。</summary>
        MainWindowOnly = 1,
    }

    /// <summary>
    /// 触发条件的显示名 —— <b>顺序必须和 <see cref="MacroCondition"/> 的枚举值一致</b>。
    /// </summary>
    public static class MacroConditionNames
    {
        public static readonly string[] All =
        {
            "无条件",
            "主界面",
        };
    }

    /// <summary>
    /// 一条自定义宏（配置页里的一张卡片）。
    /// <para>★ 目前只驱动界面，还没有真正执行按键 —— 执行引擎后面再补。</para>
    /// </summary>
    public class MacroItem : INotifyPropertyChanged
    {
        private string _id = "";
        private string _name = "";
        private string _description = "";
        private int _modeIndex;
        private bool _isPinned;
        private bool _showStar;

        /// <summary>唯一标识（改名也能定位到同一条）。加载旧配置时若为空会自动补一个。</summary>
        public string Id
        {
            get => _id;
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        /// <summary>宏名称（用户自己起的名字）。</summary>
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        /// <summary>宏描述（可空）。</summary>
        public string Description
        {
            get => _description;
            set
            {
                if (_description == value) return;
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        private string _avatar = Utils.AvatarCatalog.DefaultName;

        /// <summary>
        /// 卡片图标用的头像名（资源包 <c>Images/Pictures/角色头像/</c> 里的文件名，不含扩展名）。
        /// <para>★ 为空一律回落到默认头像「空」，界面不会出现空白圆。</para>
        /// </summary>
        public string Avatar
        {
            get => string.IsNullOrWhiteSpace(_avatar) ? Utils.AvatarCatalog.DefaultName : _avatar;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value)
                    ? Utils.AvatarCatalog.DefaultName : value;

                if (_avatar == normalized) return;
                _avatar = normalized;
                OnPropertyChanged(nameof(Avatar));
            }
        }

        private string _folderId = "";

        /// <summary>
        /// 所属文件夹 Id（<c>""</c> = 未分组，直接显示在最外层列表）。
        /// <para>★ 归属按 Id 记，不按文件夹名 —— 重命名文件夹不会把宏甩出去。</para>
        /// </summary>
        public string FolderId
        {
            get => _folderId;
            set
            {
                if (_folderId == value) return;
                _folderId = value;
                OnPropertyChanged(nameof(FolderId));
            }
        }

        /// <summary>触发方式下标（对应 <see cref="MacroTriggerMode"/> 的枚举值）。</summary>
        public int ModeIndex
        {
            get => _modeIndex;
            set
            {
                if (_modeIndex == value) return;
                _modeIndex = value;
                OnPropertyChanged(nameof(ModeIndex));
                OnPropertyChanged(nameof(ModeName));
            }
        }

        /// <summary>触发方式的显示名（卡片标题行那个标签）。</summary>
        [JsonIgnore]
        public string ModeName
            => MacroTriggerModeNames.All[Math.Clamp(ModeIndex, 0, MacroTriggerModeNames.All.Length - 1)];

        private int _scopeIndex = (int)MacroScope.ClientOnly;

        /// <summary>
        /// 生效范围下标（对应 <see cref="MacroScope"/> 的枚举值）：0=全局生效 / 1=客户区内生效。
        /// <para>★ 每条宏各管各的 —— 有的宏要在任何窗口都能按，有的只想在游戏里生效。</para>
        /// </summary>
        public int ScopeIndex
        {
            get => _scopeIndex;
            set
            {
                if (_scopeIndex == value) return;
                _scopeIndex = value;
                OnPropertyChanged(nameof(ScopeIndex));
                OnPropertyChanged(nameof(ScopeName));
            }
        }

        /// <summary>生效范围的显示名（卡片标题行那个标签）。</summary>
        [JsonIgnore]
        public string ScopeName
            => MacroScopeNames.All[Math.Clamp(ScopeIndex, 0, MacroScopeNames.All.Length - 1)];

        private int _conditionIndex = (int)MacroCondition.None;

        /// <summary>
        /// 触发条件下标（对应 <see cref="MacroCondition"/> 的枚举值）：0=无条件 / 1=主界面。
        /// <para>★ 和 <see cref="ScopeIndex"/> 各管一段：先看过没过生效范围，再看画面满不满足触发条件。</para>
        /// </summary>
        public int ConditionIndex
        {
            get => _conditionIndex;
            set
            {
                if (_conditionIndex == value) return;
                _conditionIndex = value;
                OnPropertyChanged(nameof(ConditionIndex));
                OnPropertyChanged(nameof(ConditionName));
            }
        }

        /// <summary>触发条件的显示名。</summary>
        [JsonIgnore]
        public string ConditionName
            => MacroConditionNames.All[Math.Clamp(ConditionIndex, 0, MacroConditionNames.All.Length - 1)];

        private string _script = "";

        /// <summary>
        /// 宏指令脚本（指令编辑页里编辑的原始文本，一行一条指令）。
        /// <para>★ 执行引擎还没做，先只是存取；解析/格式校验后面再补。</para>
        /// </summary>
        public string Script
        {
            get => _script;
            set
            {
                if (_script == value) return;
                _script = value;
                OnPropertyChanged(nameof(Script));
            }
        }

        private bool _enabled = true;

        /// <summary>
        /// 这条宏自己的胶囊开关（2026-10-21 起：**每个宏独立**，不再用全局开关）。
        /// <para>★ 关掉之后，即使按了它的触发键也不执行。</para>
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                OnPropertyChanged(nameof(Enabled));
            }
        }

        private string _triggerKey = "未设置";

        /// <summary>
        /// 这条宏自己的触发键（<c>"未设置"</c> = 还没设过）。
        /// <para>★ 每条宏可以设不同的键，宿主会把这些键一起注册给键盘钩子。</para>
        /// </summary>
        public string TriggerKey
        {
            get => _triggerKey;
            set
            {
                string v = string.IsNullOrWhiteSpace(value) ? "未设置" : value;
                if (_triggerKey == v) return;
                _triggerKey = v;
                OnPropertyChanged(nameof(TriggerKey));
                // ★ 触发键变灰/变主题色靠数据触发器绑 HasTriggerKey，这里必须一起通知
                OnPropertyChanged(nameof(HasTriggerKey));
            }
        }

        /// <summary>触发键是否已设置（界面用来把「未设置」显示成灰色提示）。</summary>
        [JsonIgnore]
        public bool HasTriggerKey
            => !string.IsNullOrWhiteSpace(TriggerKey) && TriggerKey != "未设置";

        /// <summary>是否置顶（点卡片图标切换；置顶的排在列表最前）。</summary>
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
        /// 触发方式（枚举形式）。★ 只读，且不参与 JSON 序列化
        /// （否则配置文件里会多出一个重复的 Mode 字段）。
        /// </summary>
        [JsonIgnore]
        public MacroTriggerMode Mode => (MacroTriggerMode)ModeIndex;

        /// <summary>
        /// 【纯界面标志】卡片左缘的置顶竖条是否显示（照 UID 的 <c>ShowStar</c>）。
        /// <para>
        /// ★ 不落盘、不参与排序：置顶/取消时先翻它让高亮层展开/收回，
        /// 动画**播完**才改真正的 <see cref="IsPinned"/> 并重排 —— 直接绑 IsPinned 会变成"瞬间出现/消失"。
        /// </para>
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

        /// <summary>有没有描述（界面用来隐藏描述那一行）。</summary>
        [JsonIgnore]
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        /// <summary>
        /// 【纯界面标志】主程序「功能总开关」是否开启 —— 关闭时胶囊开关显示成灰的（Opacity 0.4）。
        /// <para>
        /// ★★ 必须做成**数据绑定**，不能靠"遍历可视树设 Opacity"：
        ///   拖动排序结束时是 `ObservableCollection.Move`，ItemsControl 会把那一行的容器
        ///   移除重建 ⇒ 重建出来的 CheckBox 默认 Opacity=1，遍历设过的灰度全丢（用户看到
        ///   「总开关是关的，排一下序胶囊就变亮了」就是这个）。
        ///   绑到数据上则容器怎么重建都自动正确。
        /// </para>
        /// </summary>
        [JsonIgnore]
        public bool MasterSwitchOn
        {
            get => _masterSwitchOn;
            set
            {
                if (_masterSwitchOn == value) return;
                _masterSwitchOn = value;
                OnPropertyChanged(nameof(MasterSwitchOn));
            }
        }

        private bool _masterSwitchOn = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
