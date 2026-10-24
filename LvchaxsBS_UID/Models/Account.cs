using LvchaxsBS_UID.Services.EncryptionServices;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LvchaxsBS_UID.Models
{
    public class Account : INotifyPropertyChanged
    {
        private int _id;
        private string _uid = "";
        private int _level;
        private string _nickname = "";
        private string _encryptedUsername = "";
        private string _encryptedPassword = "";
        private bool _isPasswordVisible = false;
        private bool _isUsernameVisible = false;
        private bool _isSelectedForMove = false;
        private bool _isMoveable = true;
        private bool _isMarked = false;
        private bool _isPinned = false;
        private bool _showStar = false;
        private bool _hasLoginError = false;
        private string _loginErrorText = "";
        private int _order;

        [JsonPropertyName("id")]
        public int Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyName("isMarked")]
        public bool IsMarked
        {
            get => _isMarked;
            set
            {
                if (_isMarked != value)
                {
                    _isMarked = value;
                    OnPropertyChanged();
                }
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
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 上次登录是不是「账号或密码错误」（登录流程重试满次数仍报错时打上）。
        /// ★ 会落盘 —— 卡片上的红色标签一直挂到这个账号的编辑窗口里点了「清除异常」才去掉。
        /// </summary>
        [JsonPropertyName("hasLoginError")]
        public bool HasLoginError
        {
            get => _hasLoginError;
            set
            {
                if (_hasLoginError != value)
                {
                    _hasLoginError = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// ★★ 上次登录失败的原因文案 —— 卡片上那个红标签显示的就是它。
        /// <list type="bullet">
        ///   <item><b>空串</b> = 没有标签（正常账号）；</item>
        ///   <item><c>账号或密码错误-账号编辑中清除异常</c>（<c>LoginConfirmControl.AccountErrorHintText</c>）
        ///         = 账密被服务器判错；</item>
        ///   <item><c>账号验证失效</c>（<c>LoginConfirmControl.VerifyInvalidHintText</c>）
        ///         = 设备验证过期，弹出过「请先验证身份后再登录」。</item>
        /// </list>
        /// ★ 是否显示由 <see cref="HasLoginError"/> 决定，显示什么由它决定 ——
        ///   两个字段要一起写，别只改一个（只改它会看不见，只改 HasLoginError 文案会串）。
        /// ★ 同样会落盘，清除方式与 <see cref="HasLoginError"/> 一致（编辑账号里点「清除异常」）。
        /// </summary>
        [JsonPropertyName("loginErrorText")]
        public string LoginErrorText
        {
            get => _loginErrorText;
            set
            {
                string v = value ?? string.Empty;
                if (_loginErrorText != v)
                {
                    _loginErrorText = v;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 卡片是否显示「置顶 / 置底」竖条（左缘那条蓝色竖条）。
        /// ★ 纯界面状态、不落盘：由列表构建逻辑按**当前模式**算好写进来 ——
        ///   主列表 = 该账号自己的置顶（<see cref="IsPinned"/>）；
        ///   分组内 = 该账号在**这个分组**里的置底（<c>AccountGroup.IsAccountMarked</c>）；
        ///   移入模式 = 不显示。
        /// ★ 不能直接绑 <see cref="IsPinned"/> —— 它是全局字段、分组内不会重置，
        ///   否则主列表里置顶过的账号一进分组也会亮高亮。
        /// </summary>
        [JsonIgnore]
        public bool ShowStar
        {
            get => _showStar;
            set
            {
                if (_showStar != value)
                {
                    _showStar = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 手动排序号（「自由排序」，照前端模拟 <c>LvchaxsBS_UID_WebSim/index.html</c>）：
        /// **取代了按添加时间 / 置顶时间排序**。数字越小越靠前。
        /// 置顶区与普通区（分组内是未置底区 / 置底区）各自独立按它排、两区互不跨界。
        /// <para>★ 0 是合法值（第一张卡就是 0）；判断老数据有没有排序号只能看**整批是不是全 0**，
        /// 见 <see cref="SortingService.EnsureAccountOrders"/>。</para>
        /// </summary>
        [JsonPropertyName("order")]
        public int Order
        {
            get => _order;
            set
            {
                _order = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyName("uid")]
        public string Uid
        {
            get => _uid;
            set
            {
                _uid = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(ServerTag));
                OnPropertyChanged(nameof(CanLogin));
            }
        }

        [JsonPropertyName("level")]
        public int Level
        {
            get => _level;
            set
            {
                _level = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorldLevel));
            }
        }

        [JsonPropertyName("nickname")]
        public string Nickname
        {
            get => _nickname;
            set
            {
                _nickname = value ?? "";
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 没有选择头像时使用的默认头像名。
        /// 对应资源包 <c>Resources/Images/Pictures/角色头像/空.png</c>。
        /// </summary>
        public const string DefaultAvatarName = "空";

        private string _avatar = DefaultAvatarName;

        /// <summary>
        /// 账号头像：资源包 <c>Resources/Images/Pictures/角色头像/</c> 下的文件名（不含扩展名），
        /// 也就是角色名。<b>必填</b>：为空一律回落到 <see cref="DefaultAvatarName"/>（空）。
        ///
        /// <para>旧数据文件里没有这个字段，反序列化后自动就是默认头像，不需要迁移。</para>
        /// </summary>
        [JsonPropertyName("avatar")]
        public string Avatar
        {
            get => string.IsNullOrEmpty(_avatar) ? DefaultAvatarName : _avatar;
            set
            {
                string v = string.IsNullOrEmpty(value) ? DefaultAvatarName : value;
                if (_avatar != v)
                {
                    _avatar = v;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("encryptedUsername")]
        public string EncryptedUsername
        {
            get => _encryptedUsername;
            set => _encryptedUsername = value ?? "";
        }

        [JsonPropertyName("encryptedPassword")]
        public string EncryptedPassword
        {
            get => _encryptedPassword;
            set => _encryptedPassword = value ?? "";
        }

        [JsonIgnore]
        public string Username
        {
            get => AccountEncryptionService.Decrypt(_encryptedUsername);
            set
            {
                _encryptedUsername = AccountEncryptionService.Encrypt(value ?? "");
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaskedUsername));
            }
        }

        [JsonIgnore]
        public string Password
        {
            get => AccountEncryptionService.Decrypt(_encryptedPassword);
            set
            {
                _encryptedPassword = AccountEncryptionService.Encrypt(value ?? "");
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaskedPassword));
            }
        }

        [JsonIgnore]
        public int WorldLevel => CalculateWorldLevel(_level);

        /// <summary>
        /// 服务器标签，按 UID **首位**判定（《原神》UID 统一 9 位，官服与国际服都是）。
        /// 官服（1/2/3 开头）返回**空串** —— 界面不显示标签，因为默认就是官服，没必要标。
        /// 其余：5=渠道服、6=美服、7=欧服、8=亚服、9=港澳台服。
        /// 首位不在上述范围（老数据、手工填错）时按官服处理，避免误伤登录按钮。
        /// </summary>
        [JsonIgnore]
        public string ServerTag => GetServerTag(_uid);

        /// <summary>
        /// 能不能用「一键登录」—— 只有官服可以。
        /// 国际服（亚/美/欧/港澳台）与渠道服的登录通道不同，这里不提供，
        /// 只能手动复制账号密码。见 <see cref="ServerTag"/>。
        /// </summary>
        [JsonIgnore]
        public bool CanLogin => GetServerTag(_uid).Length == 0;

        private static string GetServerTag(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "";

            return uid[0] switch
            {
                '1' or '2' or '3' => "",        // 官服（天空岛 / 世界树）
                '5' => "渠道服",                 // B服、小米服等
                '6' => "美服",                   // 国际服 UID 首位：6 美 / 7 欧 / 8 亚 / 9 港澳台
                '7' => "欧服",
                '8' => "亚服",
                '9' => "港澳台服",
                _ => ""                          // 认不出来就当官服，别把登录按钮弄没了
            };
        }

        [JsonIgnore]
        public string MaskedUsername => GetMaskedUsername();

        [JsonIgnore]
        public string MaskedPassword => GetMaskedPassword();

        /// <summary>
        /// 密码那一列是否显示明文。纯界面状态，不落盘。
        /// ★ 和 <see cref="IsUsernameVisible"/> 是**两个独立开关**，别合并 ——
        ///   账号行和密码行各有一个眼睛按钮，共用一个属性会导致点其中一个把两个都露出来。
        /// </summary>
        [JsonIgnore]
        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set
            {
                _isPasswordVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaskedPassword));
            }
        }

        /// <summary>
        /// 账号那一列是否显示明文。纯界面状态，不落盘。
        /// 见 <see cref="IsPasswordVisible"/> 的说明：两者独立。
        /// </summary>
        [JsonIgnore]
        public bool IsUsernameVisible
        {
            get => _isUsernameVisible;
            set
            {
                _isUsernameVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaskedUsername));
            }
        }

        /// <summary>
        /// 「移入账号」模式下这一条是否被勾选。
        /// 纯界面状态，不落盘（<see cref="JsonIgnoreAttribute"/>）。
        /// </summary>
        [JsonIgnore]
        public bool IsSelectedForMove
        {
            get => _isSelectedForMove;
            set
            {
                if (_isSelectedForMove != value)
                {
                    _isSelectedForMove = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 「移入账号」模式下这一条能不能被选中：已经在目标分组里的账号为 <c>false</c>
        /// —— 界面显示「已在分组」提示，点卡片也不响应。
        /// 非移入模式下恒为 <c>true</c>。
        /// </summary>
        [JsonIgnore]
        public bool IsMoveable
        {
            get => _isMoveable;
            set
            {
                if (_isMoveable != value)
                {
                    _isMoveable = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 构造函数
        public Account()
        {
        }

        public Account(int id, string uid, int level, string nickname, string username, string password)
        {
            _id = id;
            _uid = uid ?? "";
            _level = level;
            _nickname = nickname ?? "";
            _encryptedUsername = AccountEncryptionService.Encrypt(username ?? "");
            _encryptedPassword = AccountEncryptionService.Encrypt(password ?? "");
        }

        // 置顶 / 取消置顶（排序号由调用方按自由排序规则改，见 SortingService.AssignPinOrder）
        public void SetPinned(bool isPinned)
        {
            IsPinned = isPinned;
        }

        // 私有辅助方法
        private static int CalculateWorldLevel(int level)
        {
            if (level >= 58) return 9;
            if (level >= 55) return 8;
            if (level >= 50) return 7;
            if (level >= 45) return 6;
            if (level >= 40) return 5;
            if (level >= 35) return 4;
            if (level >= 30) return 3;
            if (level >= 25) return 2;
            if (level >= 20) return 1;
            return 0;
        }

        /// <summary>账号 / 密码隐藏时统一显示的点数。</summary>
        private const int MaskDotCount = 9;

        /// <summary>
        /// 账号的隐藏形式：一律 9 个黑点（不再保留首尾字符）。
        /// 完整账号只在用户点了「显示」按钮后才显示（由界面切换明文/掩码两个文本块）。
        /// </summary>
        private string GetMaskedUsername()
        {
            return string.IsNullOrEmpty(Username) ? string.Empty : new string('●', MaskDotCount);
        }

        /// <summary>密码的隐藏形式：同样 9 个黑点。</summary>
        private string GetMaskedPassword()
        {
            string password = Password;
            if (string.IsNullOrEmpty(password))
                return "";

            return new string('●', MaskDotCount);
        }
    }
}