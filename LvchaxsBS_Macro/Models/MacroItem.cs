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
            "按下仅执行一次",
            "长按时重复循环",
            "按下触发循环，再次按下结束",
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

        /// <summary>触发方式下标（对应 <see cref="MacroTriggerMode"/> 的枚举值）。</summary>
        public int ModeIndex
        {
            get => _modeIndex;
            set
            {
                if (_modeIndex == value) return;
                _modeIndex = value;
                OnPropertyChanged(nameof(ModeIndex));
            }
        }

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
        /// 【纯界面标志】星 + 灰区是否显示（照 UID 的 <c>ShowStar</c>）。
        /// <para>
        /// ★ 不落盘、不参与排序：置顶/取消时先翻它让「星 + 灰区」滑入/滑出，
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

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
