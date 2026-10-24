using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LvchaxsBS.Core;       // SimulationService（按 ESC / Enter / F2、点击）、TemplateManager
using LvchaxsBS.Services;   // CoordinateFormats、ImageRecognition
using LvchaxsBS_UID.Utils.UI;

namespace LvchaxsBS_UID.Services.LoginServices
{
    /// <summary>
    /// 联机大厅里「世界权限」的三种状态 —— 对应登录窗口第 4 列的三个复选框。
    /// <para>三种共用一个区域「世界权限」，靠三张模板区分当前是哪一个。</para>
    /// </summary>
    public enum WorldPermission
    {
        /// <summary>没选：**检测到任意一个**就算进了联机大厅，不做任何切换。</summary>
        None = 0,

        /// <summary>不允许加入（复选框 10）。</summary>
        NotAllowJoin = 1,

        /// <summary>直接加入（复选框 11）。</summary>
        DirectJoin = 2,

        /// <summary>确认后可加入（复选框 12）。</summary>
        ConfirmJoin = 3,
    }

    /// <summary>
    /// 登录成功后要做的「附加动作」—— 由登录窗口左边的三个复选框决定。
    /// <para>
    /// ★ 三个动作是**互斥**的（窗口里勾一个会自动取消另外两个），所以这里最多只执行一个；
    ///   三个都没勾（<see cref="HasAny"/> == false）就什么都不做 —— 登录成功即结束。
    /// </para>
    /// </summary>
    public sealed class PostLoginOptions
    {
        /// <summary>登录后打开派蒙界面（主界面按 ESC）。</summary>
        public bool OpenPaimonMenu { get; set; }

        /// <summary>登录后打开聊天界面（主界面按 Enter）。</summary>
        public bool OpenChat { get; set; }

        /// <summary>登录后打开联机大厅（主界面按 F2）。</summary>
        public bool OpenMultiplayer { get; set; }

        /// <summary>
        /// 勾了「双人 / 三人 / 四人联机」里的哪一个 —— 联机人数的**目标**（2 / 3 / 4）。
        /// <para>
        /// ★ <b>0 = 一个都没勾</b>（第三行只显示「当前人数： n/4」，不判断达标）；
        ///   非 0 时第三行显示「双人联机： 1/4 未达标」这种，达到目标后变成「已达标」。
        /// </para>
        /// <para>★ 仅当 <see cref="OpenChat"/> 为真时才有意义（窗口里也只在那时才解锁这一列）。</para>
        /// </summary>
        public int PlayerCountTarget { get; set; }

        /// <summary>
        /// 联机大厅里要把「世界权限」切到哪一个。**仅当** <see cref="OpenMultiplayer"/> 为真时才有意义。
        /// <see cref="WorldPermission.None"/> = 不管它，能进大厅就行。
        /// </summary>
        public WorldPermission Permission { get; set; } = WorldPermission.None;

        /// <summary>
        /// ★★★ 「指令权限」（2026-10-18 用户要求）：哪些 P 位**有权让程序真的动起来**。
        /// <para>
        /// 只管**会执行操作**的那两条指令 —— 「换号」和「改世界权限」（含不允许/直接/确认后可加入）。
        /// 纯回答的（你好 / 下一个 / 世界权限 / 谢谢…）**不受限制**，谁发都回。
        /// </para>
        /// <para>三个都勾 = 谁都能指挥（老行为）；窗口里保证至少勾一个，不会出现"谁都没资格"。</para>
        /// </summary>
        public bool Allow2P { get; set; } = true;

        /// <inheritdoc cref="Allow2P"/>
        public bool Allow3P { get; set; } = true;

        /// <inheritdoc cref="Allow2P"/>
        public bool Allow4P { get; set; } = true;

        /// <summary>三个动作里有没有**至少一个**被勾选 —— 都没勾就直接结束，什么事都不做。</summary>
        public bool HasAny => OpenPaimonMenu || OpenChat || OpenMultiplayer;

        /// <summary>
        /// 这次附加动作的一句话描述 —— 登录期间会直接写进第三行字幕（「附加动作：…」）。
        /// ★ 没勾人数目标时别把「（人数目标=0）」也拼出来，那就是噪音。
        /// </summary>
        public override string ToString()
            => OpenPaimonMenu ? "打开派蒙界面"
             : OpenChat ? (PlayerCountTarget > 0 ? $"打开聊天界面（人数目标={PlayerCountTarget}）" : "打开聊天界面")
             : OpenMultiplayer ? $"打开联机界面（世界权限={Permission}）"
             : "无附加动作";
    }

    /// <summary>
    /// 主界面左上角那个「多人模式」图标所表达的联机状态。
    /// <para>
    /// ★★ 这个状态**只能**在主界面被更新（图标只在那儿）；在别处一律保留上一次的值。
    /// 这也是它为什么用 <see cref="Unknown"/> 做初值：监控刚开始、还没在主界面看过时，
    /// 文案里不该出现任何联机状态的说法。
    /// </para>
    /// </summary>
    public enum WorldMode
    {
        /// <summary>还没在主界面看过（初始值）：文案里不带联机状态。</summary>
        Unknown = 0,

        /// <summary>单人世界 —— 主界面上四张模板**一个都检测不到**。</summary>
        Single,

        /// <summary>自己在的世界开着多人模式（命中「多人模式」图标）。</summary>
        Multiplayer,

        /// <summary>别人的世界 —— 在联机中，但站在别人的世界里（命中「他人世界2P/3P/4P」）。</summary>
        OtherWorld,
    }

    /// <summary>
    /// ★★ **登录成功之后**才跑的附加动作流程，与 <see cref="AccountLoginFlow"/> 完全分离。
    /// <para>
    /// 为什么要分开：登录的判定是「输过账密 + 看到点击进入」，点完那一瞬间游戏**还在加载**，
    /// 真进到主界面要再等几秒。如果把这两件事塞进同一个状态机，登录的成功判定、重试配额、
    /// 字幕状态都会被后面的动作污染（反过来也一样）。所以这里是一个**独立的小流程**：
    /// 只在登录成功之后启动，自己等主界面、自己按键、自己检测、自己收尾。
    /// </para>
    /// <para>
    /// ★ 三条共同原则：
    /// <list type="bullet">
    ///   <item><b>先等主界面</b> —— 不确认在主界面就按键，ESC / Enter / F2 全都白发。</item>
    ///   <item><b>键只按一次</b> —— ESC / F2 都是「开关」，按第二下会把刚打开的界面关掉，
    ///         所以宁可检测不到就放弃，也不要反复按（来回开关的结果一定还是检测不到）。</item>
    ///   <item><b>只检测、不改登录状态</b> —— 不碰账号标记、不碰重试配额，字幕沿用第二行。</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class PostLoginActions
    {
        #region 常量（区域 key / 模板 key / 点击坐标 key，全部取自共享库 CoordinateFormats）

        /// <summary>主界面：左上角派蒙头像。用来确认「已经真正进到主界面了」。</summary>
        private const string MainRegionKey = AccountLoginFlow.MainRegionKey;
        private const string MainTemplateKey = AccountLoginFlow.MainTemplateKey;

        /// <summary>派蒙菜单：主界面按 ESC 打开（左上角派蒙头像附近那一块）。</summary>
        private const string PaimonRegionKey = AccountLoginFlow.PaimonRegionKey;
        private const string PaimonTemplateKey = AccountLoginFlow.PaimonTemplateKey;

        /// <summary>聊天界面：主界面按 Enter 打开，右下角「消息发送」按钮出现即算打开。</summary>
        private const string ChatRegionKey = "消息发送";
        private const string ChatTemplateKey = "其他模版_聊天界面_消息发送";

        /// <summary>联机大厅：主界面按 F2 打开，里面「世界权限」那一块。</summary>
        private const string WorldPermRegionKey = "世界权限";

        /// <summary>
        /// 联机队友：主界面右侧那一竖条。三张模板各代表一个队友槽位 ——
        /// <b>命中几张 = 当前有几个队友</b>（人数 = 队友数 + 自己）。
        /// </summary>
        private const string TeammateRegionKey = "联机队友";

        private static readonly string[] TeammateTemplateKeys =
        {
            "其他模版_联机状态_2P队友",
            "其他模版_联机状态_3P队友",
            "其他模版_联机状态_4P队友",
        };

        /// <summary>
        /// 「多人模式」区域：主界面左上角那个联机状态图标。
        /// <para>
        /// ★★ <b>只在主界面时检测</b> —— 别的地方根本没有这个图标，
        /// 拿到任何结果都没有意义（这也是状态只能"在主界面被更新"的原因）。
        /// </para>
        /// </summary>
        private const string WorldModeRegionKey = "多人模式";

        /// <summary>自己世界开着多人模式时的那个图标。</summary>
        private const string MultiplayerTemplateKey = "其他模版_联机状态_多人模式";

        /// <summary>别人世界（联机中但不在自己世界）—— 三张各对应 2P / 3P / 4P。</summary>
        private static readonly string[] OtherWorldTemplateKeys =
        {
            "其他模版_联机状态_他人世界2P",
            "其他模版_联机状态_他人世界3P",
            "其他模版_联机状态_他人世界4P",
        };

        /// <summary>
        /// 聊天界面里的「当前队伍」频道按钮（**只有联机时才有**）—— 区域 key。
        /// <para>它排在整个频道列表的最上面，所以检测到它就说明列表顶部就是"当前队伍"。</para>
        /// </summary>
        private const string TeamChannelRegionKey = "当前队伍";

        /// <summary>「当前队伍」频道的模板 key。</summary>
        private const string TeamChannelTemplateKey = "其他模版_联机状态_当前队伍";

        /// <summary>
        /// 「选择当前队伍」—— 就是**列表第一项**那个点（<c>DetectPoint</c>，颜色 <c>(65,72,86)</c>）。
        /// <para>该点变成这个颜色 = 第一项被选中了；配合 <see cref="TeamChannelTemplateKey"/> 就能判断
        /// 「现在选中的是不是『当前队伍』频道」。也是"往上滚回顶部"时鼠标要移到的位置。</para>
        /// </summary>
        private const string TeamChannelSelectPointKey = "选择当前队伍";

        /// <summary>「当前队伍」没找到（停在私人聊天）时，一次向上滚这么多格 —— 用户指定 500。</summary>
        private const int TeamChannelScrollTimes = 500;

        /// <summary>滚动一次之后等这么久再点击（用户指定 500ms —— 列表要滚到位才点得准）。</summary>
        private const int TeamChannelScrollWaitMs = 500;

        /// <summary>点击之后等这么久再检测（选中背景色要一点时间才刷出来）。</summary>
        private const int TeamChannelClickWaitMs = 200;

        /// <summary>
        /// 「聊天输入框」那个点（<c>DetectPoint</c>，颜色 <c>(255,255,255)</c>）。
        /// <para>输入框**有焦点**时是白色；不是白色 = 没焦点、打不了字 → 按一次 Enter 让它获得焦点。</para>
        /// </summary>
        private const string ChatInputPointKey = "聊天输入框";

        /// <summary>
        /// ★ 聊天界面里**队友发言**那一竖条（消息前面那个 P 标记）。
        /// </summary>
        private const string TeammateMsgRegionKey = "队友消息";

        /// <summary>
        /// ★ 聊天界面里**自己发言**那一竖条（消息前面那个 P 标记）。
        /// <para>
        /// ★★ 区域比「队友消息」宽（56 vs 32）是有意的：自己那张是「1P」**两个字符**，
        /// 比 2P/3P/4P 那种也是两个字符、但更窄一点 —— 留宽点省得把左边缘扫丢。
        /// </para>
        /// </summary>
        private const string OwnMsgRegionKey = "自己消息";

        /// <summary>
        /// ★★★ **聊天界面专用**的一套 P 标记（2026-10-18 用户重做，4 张都在
        /// <c>Template/其他模版/聊天界面/</c> 下）：自己 = <c>1P</c>，队友 = <c>2p</c> / <c>3P</c> / <c>4P</c>。
        /// <para>
        /// ⛔ 聊天里**不再借用**「联机状态」那几张（<c>1P地主</c> / <c>2P队友</c>…）—— 那几张只留给「数人」用
        /// （见 <see cref="TeammateTemplateKeys"/>），两套互不影响。
        /// </para>
        /// <para>
        /// ⚠️ <c>2p.png</c> 文件名是**小写 p**，所以 key 也是小写的 <c>其他模版_聊天界面_2p</c>。
        /// </para>
        /// </summary>
        private static readonly string[] OwnMsgTemplateKeys =
        {
            "其他模版_聊天界面_1P",
        };

        /// <summary>
        /// ★★ 聊天里**队友发言**要找的模板 —— 同样是聊天专用的那三张（<c>2p</c> / <c>3P</c> / <c>4P</c>）。
        /// </summary>
        private static readonly string[] ChatTeammateTemplateKeys =
        {
            "其他模版_聊天界面_2p",
            "其他模版_聊天界面_3P",
            "其他模版_聊天界面_4P",
        };

        /// <summary>
        /// ★★ 重新扫一次「队友消息」并把结果画到屏幕上的间隔（毫秒）。
        /// <para>聊天界面的轮询是 100ms 一轮，没必要每次都全区域匹配一遍。</para>
        /// </summary>
        private const int ChatMsgMarkIntervalMs = 500;

        /// <summary>
        /// ★★ 自适应上边界往上留的余量（像素）：起点 = 最下面那条消息的 y − 这个值。
        /// <para>留一点是别让模板正好卡在截图最顶上被切掉（聊天里的 P 标记高约 16px）。</para>
        /// </summary>
        private const int ChatScanMarginY = 40;

        /// <summary>
        /// ★ 自动回复的称呼（2026-10-17 用户定）：<b>「你好」+ 最下面那条队友消息是哪位</b>。
        /// <para>
        /// 例：最下面那条是 <c>2P队友</c> ⇒ 回「<b>你好2P</b>」；是 3P / 4P 就回「你好3P」「你好4P」。
        /// 队友区域里三张模板分别是 2P队友 / 3P队友 / 4P队友，取到的就是这几位里的某一个。
        /// ★ 只在认出的内容是「你好！」时才用它当**前缀**（谁发的就喊谁）。
        /// </para>
        /// </summary>
        private const string AutoReplyText = "你好";

        // ---------------- 队友消息「内容」模板（认它说了什么，按内容回） ----------------
        //
        // 目录：Template/其他模版/聊天界面/队友消息/
        // ★ key 是按路径**现推**的（Template/ 之后 / → _、去掉 .png），文件名里的感叹号有全角也有半角
        //   （「真是精彩!」是**半角** !），手写 key 时别改错。
        /// <summary>队友说「你好！」⇒ 回「你好」+ 2P/3P/4P。</summary>
        private const string MsgContentHelloKey = "其他模版_聊天界面_队友消息_你好！";

        /// <summary>队友说「救命！」⇒ 回「菜就多练！」。</summary>
        private const string MsgContentHelpKey = "其他模版_聊天界面_队友消息_救命！";

        /// <summary>队友说「开始挑战吧！」⇒ 回「加油！」。</summary>
        private const string MsgContentChallengeKey = "其他模版_聊天界面_队友消息_开始挑战吧！";

        /// <summary>队友说「谢谢！」⇒ 回「不用谢！」。</summary>
        private const string MsgContentThanksKey = "其他模版_聊天界面_队友消息_谢谢！";

        /// <summary>队友说「真是精彩!」⇒ 回「真是精彩！」。</summary>
        private const string MsgContentGreatKey = "其他模版_聊天界面_队友消息_真是精彩!";

        /// <summary>
        /// 队友问「世界权限」⇒ 回「当前是-直接加入 / 确认后加入 / 不允许加入」。
        /// <para>答案取 <see cref="_knownPermission"/>（程序自己改过的那次算准），不现场去开 F2 大厅确认。</para>
        /// </summary>
        private const string MsgContentWorldPermKey = "其他模版_聊天界面_队友消息_世界权限";

        // ---- 队友「点名要某个世界权限」的三张模板：认到就**直接去改**（2026-10-17 用户定） ----
        /// <summary>队友说「不允许加入」⇒ 把世界权限改成不允许加入。</summary>
        private const string MsgContentPermNotAllowKey = "其他模版_聊天界面_队友消息_不允许加入";

        /// <summary>队友说「直接加入」⇒ 改成直接加入（当前已经是它 → 只回一句「当前已经是直接加入」）。</summary>
        private const string MsgContentPermDirectKey = "其他模版_聊天界面_队友消息_直接加入";

        /// <summary>队友说「确认后可加入」⇒ 改成确认后可加入。</summary>
        private const string MsgContentPermConfirmKey = "其他模版_聊天界面_队友消息_确认后可加入";

        /// <summary>
        /// 队友说「换号」⇒ 播报下一个号是谁 →（必要时）把权限改成确认后可加入 → 交给上层换号。
        /// </summary>
        private const string MsgContentSwitchKey = "其他模版_聊天界面_队友消息_换号";

        /// <summary>
        /// 队友说「下一个」⇒ **只回答**下一个号是谁（<c>下一个「名字：UID」</c>），**不换号**。
        /// <para>和「换号」的区别就这一点：一个只是问一句，一个真的要把号切走。</para>
        /// </summary>
        private const string MsgContentNextAccountKey = "其他模版_聊天界面_队友消息_下一个";

        /// <summary>
        /// 队友说「申请权限」⇒ 分三路：全员都有权限 / 自己没权限 / 自己有权限（开 10 秒窗口招人）。
        /// </summary>
        private const string MsgContentApplyPermissionKey = "其他模版_聊天界面_队友消息_申请权限";

        /// <summary>
        /// 队友发**表情包**「指指点点」⇒ 回「反弹！」。
        /// <para>★ 表情包不是白字，白字几何校验必须把它排除（见 <see cref="EmojiContentKeys"/>）。</para>
        /// </summary>
        private const string MsgContentPointingKey = "其他模版_聊天界面_队友消息_指指点点";

        // ---------------- 「申请权限」用的话术（2026-10-18 用户定，别改字） ----------------

        /// <summary>没权限的人来申请 ⇒ 直接拒绝。</summary>
        private const string ApplyNoRightText = "你还没有权限不能申请";

        /// <summary>有权限的人来申请 ⇒ 开始招人，10 秒内谁回就给谁权限。</summary>
        private const string ApplyOpenText = "谁没权限发送任意消息即可获得权限";

        /// <summary>超时且**一个人都没回** ⇒ 损一句发起人（{0} = 发起人的 P 号）。</summary>
        private const string ApplyNoReplyFormat = "都过去10秒了没人回复啊{0}滥用权限小心我撤你权限";

        /// <summary>
        /// 超时、但期间**只有本来就有权限的人在瞎回** ⇒ 两个一起损
        /// （{0} = 发起人，{1} = 第一个来捣乱的）。
        /// </summary>
        private const string ApplyNuisanceFormat = "{0}滥用权限{1}捣乱小心我撤你们权限";

        /// <summary>来回复的人本来就有权限 ⇒ 白回一句。</summary>
        private const string ApplyAlreadyHasRightText = "你有权限回复个锤子";

        /// <summary>给没权限的人批了权限（{0} = 拿到的人，{1} = 批准人）。</summary>
        private const string ApplyGrantedFormat = "恭喜{0}获得权限，快谢谢{1}";

        /// <summary>三个 P 位全都有权限了还来申请。</summary>
        private const string ApplyAllHaveRightText = "都有权限你申请个锤子";

        /// <summary>「申请权限」的等待窗口（毫秒）：10 秒内有人回复就当场批，没人回复就损一句。</summary>
        private const int ApplyWaitMs = 10000;

        /// <summary>要在「最新消息」那一行里找的全部内容模板 —— 认到哪一句就按哪一句回。</summary>
        private static readonly string[] TeammateMsgContentKeys =
        {
            MsgContentHelloKey,
            MsgContentHelpKey,
            MsgContentChallengeKey,
            MsgContentThanksKey,
            MsgContentGreatKey,
            MsgContentWorldPermKey,
            MsgContentPermNotAllowKey,
            MsgContentPermDirectKey,
            MsgContentPermConfirmKey,
            MsgContentSwitchKey,
            MsgContentNextAccountKey,
            MsgContentApplyPermissionKey,
            MsgContentPointingKey,
        };

        /// <summary>
        /// ★★ 这几张是**表情包**，不是白字 —— 白字几何校验必须跳过它们，
        /// 否则「量白字块」那一步会把表情包判成"宽度不对"而永远匹配不上。
        /// </summary>
        private static readonly string[] EmojiContentKeys =
        {
            MsgContentPointingKey,
        };

        /// <summary>
        /// ★★ 程序**最近一次确认过**的世界权限 —— 队友问「世界权限」时靠它回答「当前是-XXX」。
        /// <para>
        /// 为什么不到现场检测：确认权限得按 F2 开联机大厅，会打断聊天、还要再退回来，代价太大。
        /// 而这两个时刻都是**程序自己动手改的**（登录后必设「直接加入」、人数达标后改「确认后可加入」），
        /// 所以记下来就是准的。★ 初值取「直接加入」：登录流程第一件事就是确保它是直接加入。
        /// </para>
        /// </summary>
        private static WorldPermission _knownPermission = WorldPermission.DirectJoin;

        /// <summary>
        /// ★★ 「最新消息内容」用的**固定区域** = 「队友消息」的左上角 → 「自己消息」的右下角。
        /// <para>
        /// 即 x 从 <c>队友消息.X1</c> 到 <c>自己消息.X2</c>、y 从 <c>队友消息.Y1</c> 到 <c>自己消息.Y2</c>
        /// （3840×2160 基准下就是 <c>(828, 208, 1976, 1874)</c>）。
        /// 两个角都**从区域表现取屏幕坐标**，所以不用在这里再维护一份数值，改那两个区域会自动跟着变。
        /// </para>
        /// <para>
        /// ★ 真正截图时再把<b>上边界抬到最新那条队友消息的 y</b> —— 上面那些旧消息不用截进去，
        /// 少截一段就少一片误匹配，也更省时间。
        /// </para>
        /// </summary>
        /// <summary>内容匹配的阈值（和两个消息区域一致）。</summary>
        private const double MsgContentThreshold = 0.8;

        /// <summary>
        /// ★★ 内容匹配用**粗匹配**的缩放倍数（2026-10-17 用户定 = <b>3</b>）。
        /// <para>
        /// 这块区域从「队友消息」左边一直横到「自己消息」右边、下到聊天列表底，面积很大，
        /// 逐像素全扫太慢（实测就是"识别太慢"的原因）。缩 3 倍先粗扫、再回原图局部精匹配，
        /// 快很多而且坐标仍是精匹配的结果。
        /// </para>
        /// </summary>
        private const int MsgContentRoughScale = 3;

        /// <summary>
        /// ★★★ 「自己消息**检测得到**」时，两次自动回复之间的间隔 —— **0，即判定成立就立刻发**。
        /// <para>
        /// 2026-10-17 用户定死：**只要最新的是队友就立马发送，不要任何等待**。
        /// 这里根本不需要间隔闸：两个区域的 y 不会重叠，谁在下面就谁是最新；回完自己那条
        /// 就变成最下面了，下一轮判定"自己才是最新"自然就不再发 —— 判据本身就不会刷屏。
        /// ⛔ 以前这里用的是 5000ms，实测就是"回复要等两三秒"的真凶（日志：等待 2765 / 4484 / 5687ms，
        /// 而扫描只要 15~32ms、节拍稳定 562~594ms，跟性能毫无关系）。
        /// </para>
        /// </summary>
        private const int AutoReplyMinGapMs = 0;

        /// <summary>
        /// ★ 唯一的例外：「自己消息**检测不到**（<c>ownY = -1</c>）」时的最小间隔。
        /// 那时候没法靠"自己变成最新"自然停下，若再不设闸就会**每 500ms 发一条**（2026-10-17 实测过的刷屏）。
        /// 取 1200ms ≈ 等一条消息渲染出来的时间，而不是一个长冷却。
        /// </summary>
        private const int AutoReplyCooldownMs = 1200;

        /// <summary>
        /// ★★ 让聊天输入框拿到焦点时**最多点几次**（点一下没白就再点，间隔 <c>AfterPressDelayMs</c>）。
        /// <para>
        /// 只点一次不够：聊天界面刚刚打开时那一击常常被界面吞掉，于是"输入框始终没有焦点，
        /// 通知根本没发出去"（2026-10-17 实测）。宁可多点两下，也不要静悄悄放弃发送。
        /// </para>
        /// </summary>
        private const int MaxChatFocusAttempts = 3;

        /// <summary>
        /// ★★ 按完回车、等这么久再看输入框有没有变白。
        /// <para>
        /// 这里**故意不用** <see cref="AfterPressDelayMs"/>（600ms）：那个是"按键之后等界面切换"用的，
        /// 而输入框变白是**瞬时**的，等 600ms 纯属白等 —— 最多按 3 次就是 1.8 秒，
        /// 正是"回复要等两三秒"的另一半原因。200ms 足够看到它变色。
        /// </para>
        /// </summary>
        private const int ChatFocusRecheckMs = 150;

        /// <summary>发送前「全选 → 退格」这两下之间的间隔（清空输入框里残留的旧文字用）。</summary>
        private const int UidClearDelayMs = 80;

        /// <summary>
        /// 粘贴之后、按回车发送之前等这么久（等文字真正落到输入框里）。
        /// <para>
        /// ★ 2026-10-17：300 → <b>100</b>。<see cref="SimulationService.PasteText"/> 内部已经有
        /// 一段等待（设置剪贴板后 50ms + Ctrl+V 键间 10ms + 粘完 50ms ≈ 110ms），
        /// 加上这里的 100ms，从投递 Ctrl+V 到按回车合计 200ms 以上（游戏 60fps ≈ 12 帧），足够生效。
        /// ⚠️ 若实测出现"发了空消息"或"一条消息回两次"，说明粘贴没赶上，把它调回 200。
        /// </para>
        /// </summary>
        private const int UidPasteToSendMs = 100;

        /// <summary>回车发送之后等这么久（让消息发出去、界面稳定下来），随后就算这一项做完了。</summary>
        private const int UidAfterSendMs = 150;

        /// <summary>队伍最多 4 人（含自己）。</summary>
        private const int MaxPlayers = 4;

        /// <summary>「世界权限」三张模板，与 <see cref="WorldPermission"/> 一一对应（下标 = 枚举值 - 1）。</summary>
        private static readonly string[] WorldPermTemplateKeys =
        {
            "其他模版_联机状态_世界权限不允许加入",   // NotAllowJoin
            "其他模版_联机状态_世界权限直接加入",     // DirectJoin
            "其他模版_联机状态_世界权限确认后可加入", // ConfirmJoin
        };

        /// <summary>从「世界权限」下拉里选某一项时要点的坐标，下标同 <see cref="WorldPermTemplateKeys"/>。</summary>
        private static readonly string[] WorldPermClickKeys =
        {
            "不允许加入",
            "直接加入",
            "确认后可加入",
        };

        #endregion

        #region 节奏参数

        /// <summary>登录后等主界面最久等多久（加载慢的时候给足时间）。</summary>
        private const int WaitMainTimeoutMs = 60_000;

        /// <summary>等主界面 / 等目标界面时，两轮检测之间的间隔。</summary>
        private const int PollIntervalMs = 400;

        /// <summary>按下按键之后，等界面响应一下再开始检测。</summary>
        private const int AfterPressDelayMs = 600;

        /// <summary>单项动作的检测总时长上限（到点还没检测到就放弃，不无限等）。</summary>
        private const int DetectTimeoutMs = 10_000;

        /// <summary>切换「世界权限」失败时最多重开几次联机大厅（每次先 ESC 退一层、再按 F2）。</summary>
        private const int MaxLobbyOpenAttempts = 3;

        /// <summary>退出聊天界面时最多按几次 ESC（第一次多半只是取消输入框焦点）。</summary>
        private const int MaxLeaveChatAttempts = 4;

        /// <summary>「世界权限」点开下拉 → 选一项 → 复查，最多切几次。</summary>
        private const int MaxPermissionSwitch = 3;

        /// <summary>点开下拉之后等列表展开的时长。</summary>
        private const int AfterOpenDropdownMs = 400;

        /// <summary>
        /// ★ 切换「世界权限」之后**等这么久就复查一次**（用户指定 500ms）。
        /// <para>对上了 = 切换成功；没对上 = **立刻再切一轮**，不要傻等到超时 ——
        /// 切换失败时早一秒重切，比原地干等有用得多。</para>
        /// </summary>
        private const int AfterSwitchDelayMs = 500;

        /// <summary>复查时若游戏不在焦点，最多再等这么久（焦点不在这，识别出来的结果没有意义）。</summary>
        private const int RecheckFocusWaitMs = 1500;

        /// <summary>
        /// **定位检测**的间隔（和登录流程一致：1 秒一轮）。
        /// <para>什么都不确定的时候用：每轮把「主界面」「聊天界面」两张模板都测一遍，
        /// 再按结果决定按哪个键 —— 慢一点没关系，这里本来就是在"找自己在哪"。</para>
        /// </summary>
        private const int LocatePollMs = 1000;

        /// <summary>
        /// **指定检测**的间隔（100ms，和登录流程里"盯住下一步"同一档）。
        /// <para>已经按过键了，就只盯着「聊天界面」等它出现 —— 这段要快，
        /// 因为它决定"聊天界面一出来就马上开始数人"的跟手感。</para>
        /// </summary>
        private const int TrackPollMs = 100;

        /// <summary>
        /// **指定检测**的最长等待：按过 Enter 之后，等「聊天界面」出现最多等这么久。
        /// <para>
        /// ★★ 这个值**必须短**（原来 10 秒）：按一次 Enter 之后聊天界面通常几百毫秒就出来了，
        /// 要是等了 2 秒还没出来，多半是**那一下按键没生效**（画面刚好在切换 / 黑屏，游戏把键吃了）
        /// 或者聊天界面刚被关掉。这时**回定位检测重新看一眼、必要时再按一次**，
        /// 远比原地傻等 10 秒强 —— 用户看到的就是"字幕写着要开聊天，却半天不动"。
        /// </para>
        /// <para>
        /// ★ 重按是安全的：定位检测里只有**确认站在主界面**（<c>mainVisible</c>）才会按 Enter，
        /// 真要是聊天界面已经开着，那儿检测到的是聊天界面，不会去按。
        /// </para>
        /// </summary>
        private const int TrackTimeoutMs = 2000;

        /// <summary>
        /// 「什么都检测不到」的**宽容期**（用户指定 5 秒）。
        /// <para>
        /// 在主界面下，只要用户没干预，那张「主界面」模板是能一直检测到的 —— 所以**突然**检测不到、
        /// 而用户又没操作，基本就是**正在进入多人模式 / 加载过渡**（屏幕会黑一下）。
        /// 这种时候<b>什么都不做</b>，等它自己再出现就行，千万别按 ESC
        /// （那会把派蒙菜单按开，界面全乱）。
        /// </para>
        /// <para>★ 超过这么久还没出现，才当成"真的跑到别的界面了"→ 交给定位检测按 ESC 往回退。</para>
        /// </summary>
        private const int MissGraceMs = 5000;

        /// <summary>
        /// ★ 同一个按键的**冷却时间**：按过之后这么久内不再按第二下。
        /// <para>
        /// 为什么必须按「无条件冷却」而不是「看检测结果再决定」：按键之后界面要过一小会儿才切过去，
        /// 这段空档里检测到的仍是<strong>旧界面</strong> —— 拿它当判断依据就会连按：
        /// ESC 连按 = 把主界面的派蒙菜单按开、再按回来；**Enter 连按 = 在聊天界面里连发消息**（更糟）。
        /// </para>
        /// <para>★ 冷却只挡「按键」，检测与数人照常跑（间隔不变）。</para>
        /// </summary>
        private const int KeyPressCooldownMs = 1000;

        /// <summary>
        /// ★ <b>用户操作后的让路时间</b>：用户刚动过键鼠的这一段时间里，监控只检测、<b>不按任何模拟键</b>。
        /// <para>
        /// 为什么需要：监控为了"自动回到聊天界面"会按 ESC / Enter，用户如果正在自己操作
        /// （翻背包、看地图、打字…），按键就会和他打架，甚至把界面关掉。
        /// </para>
        /// <para>
        /// ★ 这是**连续**静默判定：从最后一次用户操作算起满 <b>5 秒</b>才恢复按键；
        /// 中途又操作了就重新从那一刻算（<see cref="Utils.UI.UserInputProvider.IdleMs"/> 天然如此）。
        /// </para>
        /// <para>★ 只挡按键，检测与数人照常跑（看清"现在在哪个界面"不耽误）。</para>
        /// </summary>
        private const int UserQuietMs = 5_000;

        /// <summary>
        /// 常驻监控中的第二行文案 —— **带上"当前队伍"频道状态**：
        /// <c>聊天界面已打开-已在当前队伍-人数监控中</c> / <c>…-未在当前队伍-…</c>。
        /// <para>⛔ **不要写「-TAB隐藏」** —— 监控是常驻的，按 TAB 收掉之后循环还在跑、
        /// 却再也显示不出来（宿主的 SetSubtitle3 在非激活态会丢弃刷新），观感就是「结束了」。</para>
        /// </summary>
        private static string Line2Watching(bool inTeamChannel)
            => inTeamChannel
                ? "聊天界面已打开-已在当前队伍-人数监控中"
                : "聊天界面已打开-未在当前队伍-人数监控中";

        /// <summary>监控停止后的第二行文案（这时才轮到 TAB 收字幕）。</summary>
        private const string Line2WatchStopped = "人数监控已停止-TAB隐藏";

        /// <summary>
        /// **单人世界**停在主界面等待时的第二行文案。
        /// <para>★ 用它替掉「聊天界面已打开-人数监控中」：单人世界并没有打开聊天界面，
        /// 而且是**故意停在这儿等**进入多人模式，写清楚才不会让人以为卡住了。</para>
        /// </summary>
        private const string Line2SingleWaiting = "当前处于单人模式-等待进入多人模式";

        /// <summary>第二行字幕：连接服务器失败（彻底收场，不再换号）。</summary>
        private const string Line2ConnectFailed = "连接服务器失败，已停止";

        /// <summary>
        /// 缀在人数后面的「还没动静」提示 —— 登录中、暂停检测时都用这一份写法。
        /// </summary>
        private const string WaitingSuffix = " 等待中...";

        // ---- 第三行（联机人数）的几段固定话术 ----
        /// <summary>达标 / 未达标（「已达标」会显示成黄色）。</summary>
        private const string ReachedText = "已达标";

        private const string UnreachedText = "未达标";

        /// <summary>刚达标、人还没走：等所有人退出世界。</summary>
        private const string WaitPlayersLeaveText = " 等待所有玩家退出世界";

        /// <summary>达标过、人数已经掉下来了（队友在撤）：等他们全退了就换号。</summary>
        private const string WaitLeaveThenSwitchText = " 等待所有玩家退出世界后换号";

        /// <summary>还没达标 + 单人世界（这种世界里压根数不到队友）。</summary>
        private const string SingleWorldText = " 当前处于单人模式";

        /// <summary>
        /// ★ 人数第一次达标时**发到聊天里**的那句话（写在人数达标、改权限之前）——
        /// 让队友知道接下来要把世界权限改成「确认后可加入」。
        /// ★ 只在**确实要去改**的时候用；记录里已经是「确认后可加入」时发
        /// <see cref="ReachedNoticeAlreadyText"/>（不然就是喊了"我这就去改"却一步都没去）。
        /// </summary>
        private const string ReachedNoticeText = "我这就去把世界权限改成确认后可加入";

        /// <summary>
        /// 权限**已经是**「确认后可加入」时的达标通知（2026-10-18 用户定）：
        /// 不用改了就别再喊"我这就去改"，直接如实说权限现在的状态。
        /// </summary>
        private const string ReachedNoticeAlreadyText = "世界权限已是确认后可加入";

        #endregion

        #region 入口

        /// <summary>
        /// ★「实时」版本的选项 —— UI 窗口在**监控还跑着的时候**改了勾选，就往这里灌，下一轮立刻生效。
        /// <para>
        /// 典型场景：这次勾的是「四人联机」，进游戏后觉得等太久，直接在窗口里改成「双人联机」或干脆取消勾选
        /// → 第三行的格式 / 达标判断跟着变，不用重新登录一次。
        /// </para>
        /// <para>★ 只在本次流程期间有效：<see cref="RunAsync"/> 进入时替换、退出时清空。</para>
        /// </summary>
        private static PostLoginOptions? _liveOptions;

        /// <summary>
        /// ★ 换号出口（由 <see cref="RunAsync"/> 的 <c>onSwitchRequested</c> 参数在开局时赋值）。
        /// <para>人数监控的循环写在一个独立方法里，够不到 RunAsync 的局部变量 —— 和
        /// <see cref="_liveOptions"/> 一样借用静态字段把它递过去。</para>
        /// <para>什么时候叫它：① 人数达标过、又掉回 1（队友撤了）② 账号在别处被顶了。</para>
        /// </summary>
        private static Func<string, Task>? _switchRequested;

        /// <summary>
        /// ★★ 接下来要换到的那个账号的「<b>名字：UID</b>」（由 <see cref="RunAsync"/> 的
        /// <c>nextAccountText</c> 参数在开局时赋值；空串 = 不知道下一个是谁）。
        /// <para>队友在聊天里发「换号」时，就拿它拼「我去换号了下一个是「名字：UID」」播报给他们。</para>
        /// </summary>
        private static string _nextAccountText = "";

        /// <summary>
        /// ★ 换号已经移交给上层（<see cref="_switchRequested"/> 已踃过一次）。
        /// <para>收尾时据此**跳过**「人数监控已停止」那句第二行 —— 新一轮登录马上就要写自己的第二行，
        /// 这时候抢着写会把新字幕盖掉。</para>
        /// </summary>
        private static bool _handedOff;

        /// <summary>把 UI 窗口里最新的勾选灌进来（见 <see cref="_liveOptions"/>）。</summary>
        public static void UpdateLiveOptions(PostLoginOptions? options)
        {
            if (options != null) _liveOptions = options;
        }

        /// <summary>
        /// 取本轮应该用的人数目标 —— 以 <see cref="_liveOptions"/> 为准，没有再用启动时那一份。
        /// <para>★ UI 那边的勾选是「聊天」时才认它的人数目标：换成派蒙 / 联机的话，
        /// 第二列那几个复选框在窗口里本来也是禁用的，这里跟着不认。</para>
        /// </summary>
        private static int ResolveTarget(int fallback)
        {
            var live = _liveOptions;
            return (live != null && live.OpenChat) ? live.PlayerCountTarget : fallback;
        }

        /// <summary>
        /// 登录成功后执行附加动作。
        /// </summary>
        /// <param name="options">三个复选框的状态。</param>
        /// <param name="uid">
        /// 账号 UID（**2026-10-17 起不再使用**：原来登录后要把它粘到聊天里发出去，那条已经删掉了）。
        /// 参数留着是为了不动上层调用，随时可以再捡起来用。
        /// </param>
        /// <param name="token">取消令牌（用户手动停 / 窗口关闭时传进来）。</param>
        /// <param name="onSwitchRequested">
        /// ★★ 请求上层「**切换到下一个账号**」的回调（参数是原因，写日志用）。
        /// 两种情况会叫它：
        /// <list type="bullet">
        ///   <item>人数曾经达标、之后掉回 1（队友全走了）→ 这个号的活干完了；</item>
        ///   <item>弹出「账号在另一处重复登录」→ 这个号被顶了。</item>
        /// </list>
        /// 上层负责：停掉本次监控 → 置底当前账号 → 取下一个**未置底**的账号 → 重新走登录流程。
        /// ★ 本方法在回调返回后就**结束自己**（不继续监控了）。
        /// </param>
        public static async Task RunAsync(
            PostLoginOptions options, string uid, CancellationToken token,
            Func<string, Task>? onSwitchRequested = null, string nextAccountText = "")
        {
            // ★ 三个都没勾选 → 登录成功就结束了，什么都不用做
            if (options == null || !options.HasAny)
            {
                Debug.WriteLine("[UID/登录后] 没有勾选任何附加动作，登录成功即结束");
                return;
            }

            Debug.WriteLine($"[UID/登录后] 开始执行附加动作：{options}");

            // 实时生效的起点：以本次启动时的勾选为准，之后由 UpdateLiveOptions 往里灌新的
            _liveOptions = options;

            // ★ 换号出口：人数监控循环在另一个方法里，够不到这里的参数 ——
            //   和 _liveOptions 一样用静态字段传下去（每次 RunAsync 都会重新赋值）
            _switchRequested = onSwitchRequested;
            _nextAccountText = nextAccountText ?? "";
            _handedOff = false;

            // ★ 第二行的去重缓存是 static，跨会话保留。不重置的话，上一轮以某句收尾、
            //   这一轮又是同一句开头时会被「去重」吞掉 → 第二行一直空白。
            ResetLine2Cache();
            try
            {
                // ① 先等真正进到主界面 —— 登录成功只是「点了点击进入」，后面还有一段加载
                if (!await WaitForMainScreenAsync(token))
                {
                    SetLine2("未检测到主界面，已跳过附加动作");
                    Debug.WriteLine("[UID/登录后] 超时仍未检测到主界面，放弃附加动作");
                    return;
                }

                // ② 三个互斥，按顺序接第一个勾上的
                if (options.OpenPaimonMenu)
                {
                    await OpenPaimonMenuAsync(token);
                    // 第三行是「人数行」，派蒙这路用不上 → 动作一结束就收掉，
                    // 否则它会一直停在登录时那句「…等待中...」
                    AccountLoginFlow.HideThirdLine();
                }
                else if (options.OpenChat) await OpenChatAsync(options.PlayerCountTarget, token);
                else if (options.OpenMultiplayer)
                {
                    await OpenMultiplayerAsync(options.Permission, token);
                    AccountLoginFlow.HideThirdLine();   // 同上：联机这路也用不上人数行
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[UID/登录后] 附加动作已取消");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录后] 附加动作异常：{ex}");
            }
            finally
            {
                _liveOptions = null;   // 本次流程结束，别把勾选状态带到下一次
            }
        }

        #endregion

        #region 三个动作

        /// <summary>动作一：主界面按 ESC 打开派蒙菜单，检测到「派蒙菜单区域」即结束。</summary>
        private static async Task OpenPaimonMenuAsync(CancellationToken token)
        {
            SetLine2("正在打开派蒙界面");

            SimulationService.PressEscape();
            await Task.Delay(AfterPressDelayMs, token);

            bool ok = await WaitDetectAsync(PaimonRegionKey, PaimonTemplateKey, token, "派蒙菜单区域");
            SetLine2(ok ? "派蒙界面已打开-TAB隐藏" : "打开派蒙界面失败，请手动按 ESC");
            Debug.WriteLine($"[UID/登录后] 打开派蒙界面：{(ok ? "成功" : "未检测到")}");
        }

        /// <summary>
        /// 动作二：进入「打开聊天界面 + 常驻显示联机人数」这条流程。
        /// <para>
        /// ★★ <b>这里绝对不能自己按 Enter 开聊天</b>：打开聊天界面之前**必须先确认当前联机状态**，
        /// 而联机状态只有主界面能读。整套判断（没拿到状态就先回主界面 → 单人世界停在主界面等 →
        /// 多人 / 他人世界才按 Enter 开聊天）都在 <see cref="WatchPlayerCountAsync"/> 的定位检测里，
        /// 所以这里只把第二行交代一句，就把控制权交给监控循环。
        /// </para>
        /// <para>
        /// ★ 之前这里是「按 Enter → 等消息发送出现 → 进监控」，等于**跳过**了联机状态确认：
        /// 单人世界也会被硬开一个聊天界面。现在这条路径没了。
        /// </para>
        /// </summary>
        private static async Task OpenChatAsync(int playerCountTarget, CancellationToken token)
        {
            // ★ 不写「-TAB隐藏」：流程还在继续（后面是常驻的人数监控）。
            SetLine2("正在确认当前联机状态");
            Debug.WriteLine("[UID/登录后] 开始聊天流程：先确认联机状态，再决定是否打开聊天界面");

            // 交给监控循环：它会先确认状态、该回主界面就回主界面、该等就等，确认是联机状态才按 Enter
            await WatchPlayerCountAsync(playerCountTarget, token);
        }

        /// <summary>
        /// ★ 常驻的联机人数监控（第三行字幕）—— 用**和登录流程同一套两步检测**：
        /// <list type="bullet">
        ///   <item><b>定位检测</b>（<see cref="LocatePollMs"/>，1 秒/轮）：什么都不知道的时候，
        ///     每轮把「主界面」「聊天界面」两张模板**都测一遍**，再按结果决定动作 ——
        ///     命中聊天界面 → 转去指定检测；命中主界面 → 按一次 <b>Enter</b> 打开聊天 → 转去指定检测；
        ///     两个都没命中 → 按一次 <b>ESC</b> 退回上一层（**只有定位检测会按 ESC**，和登录一致）。</item>
        ///   <item><b>指定检测</b>（<see cref="TrackPollMs"/>，100ms/轮）：已经按过键了，
        ///     就只盯着「聊天界面」等它出现；一出现 = 正常数人；连续 <see cref="TrackTimeoutMs"/>
        ///     还没出现 → 退回定位检测重新确认现在在哪。</item>
        /// </list>
        /// <para>
        /// ★ 在聊天界面里的每轮固定做两件事（联机中）：<b>数人</b> → 确认停在多人公共频道
        /// <b>「当前队伍」</b>（<see cref="EnsureTeamChannelAsync"/>）。
        /// （2026-10-17 起**不再往聊天里发 UID** —— 那条已经按用户要求删掉了。）
        /// </para>
        /// <para>
        /// ★★ <b>单人世界（<see cref="WorldMode.Single"/>）是特例：必须待在主界面等</b>——
        /// 联机状态只能在主界面读取，所以单人时不管当前在聊天界面还是别的界面，
        /// 都按 ESC 往回退，直到回到主界面停住（不按 Enter、也不数人）；等图标变成
        /// 多人模式 / 他人世界，才继续打开聊天界面数人。
        /// </para>
        /// <para>
        /// ★ 两个按键各自有 <see cref="KeyPressCooldownMs"/>（1 秒）冷却 —— **冷却只挡按键**，
        /// 检测与数人照常跑。另外用户刚动过键鼠时（<see cref="UserQuietMs"/>，连续 5 秒静默才算过）
        /// 同样只检测不按键，把操作空间让给用户 —— 这段时间会挂出**第四行**字幕
        /// （「发现用户进行了操作，等待用户 N 秒内未进行操作时恢复...」，N 是倒计时），
        /// 倒计时走完自动收起（只是归零不透明度，窗口常驻不销毁，下次直接显示）。
        /// </para>
        /// <para>
        /// ★★ <b>切屏出去只是暂停，不是结束</b>：焦点不在游戏上时跳过检测继续等，
        /// 回到游戏就接着跑。<b>唯一</b>能让它结束的是 <paramref name="token"/> 被取消
        /// （用户在程序 UI 上停止 / 关掉登录窗）。
        /// </para>
        /// </summary>
        private static async Task WatchPlayerCountAsync(int target, CancellationToken token)
        {
            // ★★ 监控期间**锁住 TAB 隐藏**：这是常驻流程，按 TAB 收掉之后循环还在跑、
            //    但宿主的第三行已经变成非激活态、后续刷新全被丢弃 —— 用户看到的就是「结束了」。
            //    所以这里和登录流程一样上锁，只允许通过程序 UI（关登录窗 / 停止）来结束。
            SubtitlePresenter.LockTabHide();
            bool locked = true;

            // ★★ 第四行（「用户正在操作」提示）先**预热**：把窗口建好、保持**纯透明常驻**。
            //    这样用户一操作就能直接把它显示出来 —— 不用在那一刻现建窗（会慢半拍、还可能闪）。
            //    它平时看不见也点不到（覆盖层是鼠标穿透的），只在让路期间露脸。
            SubtitlePresenter.Prewarm4();

            string? lastText = null;        // 第三行当前真正显示的内容（去重用，含可能的「等待中」后缀）
            string? lastCountText = null;   // 最近一次**真的测出来**的人数文案（不带后缀）
            int lastPlayers = 0;            // 最近一次真的测出来的人数（0 = 还没测出过）
            bool areasPaused = false;       // 第三行当前是不是「等待中」形态

            // ★ 两个按键**各自**记上次按下的时刻（TickCount64，单调时钟）—— 冷却互不影响
            long lastEscTicks = 0;
            long lastEnterTicks = 0;

            // 冷却是否已过（从没按过 = 0，直接算过）
            bool EscReady() => Environment.TickCount64 - lastEscTicks >= KeyPressCooldownMs;
            bool EnterReady() => Environment.TickCount64 - lastEnterTicks >= KeyPressCooldownMs;

            // ★ 用户是不是"安静"了足够久（连续 10 秒没操作键鼠）—— 只有安静时才允许发模拟动作。
            //   用户刚操作过就先只检测、把操作空间留给他（中途又操作会重新计时）。
            bool UserQuiet() => IsUserQuiet();

            // ★★ 两步检测的状态（和登录流程一个意思）：
            //    true  = 定位检测（1 秒/轮，两张模板都测，**只有这层会按键**）
            //    false = 指定检测（100ms/轮，只盯「聊天界面」，等它出现）
            bool locating = true;
            long trackStartTicks = 0;   // 指定检测的起点（超时判定用）
            bool notedUserBusy = false; // 「用户正在操作、已打过日志」——避免每轮刷屏
            int lastWarnRemainSec = 0;  // 第四行当前显示的倒计时秒数（0 = 第四行没显示）

            // 「当前队伍」聊天频道是否已经确认选好（联机时聊天界面要停在多人公共大厅上）
            bool teamChannelReady = false;


            // ★ 当前联机状态（单人 / 自己世界多人 / 别人世界）。
            //   ★★ **只能**在主界面被更新（图标只在那儿）；别处一律沿用上一次的值，
            //      所以"跑到别的界面看不见图标"不会把状态误判成单人。
            WorldMode worldMode = WorldMode.Unknown;
            bool notedSingleWorld = false;   // 单人世界的日志只打一次（避免每秒刷屏）

            // ★★ 「现在必须待在主界面吗」——两种情况都要：
            //    ① 单人世界：主界面等多人模式（那儿数不到人，也没别的事可做）；
            //    ② 还没拿到联机状态（Unknown）：**首要目标就是回主界面把状态读出来**，
            //       因为状态只有主界面能读，"没确认状态"就绝不能去开聊天界面。
            bool MustStayAtMain() => worldMode == WorldMode.Single || worldMode == WorldMode.Unknown;

            // 上一轮是不是"该待主界面"——用来认出「刚解锁、可以去开聊天」这个转折点：
            // 那一刻要**立刻**按 Enter（不等让路 / 冷却），否则会出现"字幕都说要开聊天了，却等好几秒"。
            bool wasMustStayAtMain = true;

            // 连续"两个界面都没命中"的起点（0 = 当前没处在未命中状态）——见 MissGraceMs
            long missingSinceTicks = 0;
            bool notedMissing = false;   // 这一轮"未命中期"的日志只打一次（100ms 一轮会刷屏）

            // ★★ 本次监控有没有**达标过**（人数到过目标 2/3/4）。
            //    它是自动换号的前置条件：只有"曾经满过人、现在又掉回 1 个"才算队友撤了。
            bool reachedOnce = false;

            // ★ 登录进游戏后「确认世界权限」这一步有没有做过（只做一次，见 EnsureDirectJoinAsync）。
            bool permEnsured = false;

            // ★★ 达标那一刻要做的那套动作（发通知 + 改权限 + 回聊天）有没有做过 —— 一个人一辈子一次。
            bool reachedHandled = false;

            // ★ 上次「扫一遍聊天里的两组消息（画标记 + 自动回复判定）」的时刻
            long lastChatMsgTicks = 0;
            _lastTeammateMsgHitCount = -1;
            _lastOwnMsgHitCount = -1;

            // ★ 聊天区域的"自适应上边界"也是 static → 新一轮归零，第一轮先整条扫一遍定位置
            _chatScanTopY = 0;

            // ★ 「申请权限」的等待窗口也不该跨轮次留着
            ResetApplyPending();
            _lastTeammateMarkTicks = 0;
            _lastTeammateHitCount = -1;

            // ★ 自动回复的冷却 / 计时起点都是 static → 新一轮必须清零，
            //   不然会被上一轮的发过时刻挡住、或把上一轮的等待时间算到这一轮头上
            _lastAutoReplySentTicks = 0;
            _replyPendingSinceTicks = 0;

            // ★ 世界权限的"已知值"也跟着重来：登录流程第一件事就是把它设成「直接加入」
            //   （聊天里有人问"世界权限"时按这个回答）
            _knownPermission = WorldPermission.DirectJoin;

            // 往第三行写字。★ 用**三段式**是因为「已达标」要显示成黄色（Rich = 前缀 / 高亮 / 后缀），
            //   highlight 传空串就退化成一行普通白字。
            //   waiting=true 时在末尾再补「 等待中...」。文本完全相同就不重复发（跨 dll 发送，能省则省）。
            void ApplyThirdLine(string prefix, string highlight, string suffix, bool waiting)
            {
                string tail = waiting ? suffix + WaitingSuffix : suffix;
                string text = prefix + highlight + tail;
                if (text == lastText) return;
                lastText = text;
                SubtitlePresenter.SetRichText3(prefix, highlight, tail);
            }

            // 按当前人数 / 目标 / 达标状态拼一次第三行。★ 传的是 **reachedOnce**（不是"现在够不够人"）：
            //   达标是粘住的 —— 曾经满过人就算达标，之后人数往下掉文案里的「已达标」也不变。
            void ApplyCountLine(int players, int targetNow, bool waiting)
            {
                var parts = BuildPlayerCountParts(players, targetNow, reachedOnce, worldMode);
                ApplyThirdLine(parts.Prefix, parts.Highlight, parts.Suffix, waiting);
            }

            // 真的数一次人并刷新第三行（只在确认处在主界面 / 聊天界面后调用）
            void UpdatePlayerCount(int targetNow)
            {
                // ★ 单人世界里右侧**没有**队友竖条（也就数不出人）：直接按"只有自己"算 1/4，
                //   省掉三次检测。等进入多人模式 / 他人世界后才会真的去数。
                int teammates = worldMode == WorldMode.Single ? 0 : CountTeammates();
                int players = Math.Min(teammates + 1, MaxPlayers);
                lastPlayers = players;

                // ★ 把「联机队友」这一轮检测到的目标也画到屏幕上（序号 + 模板 + 坐标 + 匹配度）。
                //   单人世界里那一列本来就没有队友（也就没什么可画的），跳过省一次匹配。
                if (worldMode != WorldMode.Single)
                    MarkTeammateCountTargets();

                // ★ 本次监控里曾经达到过目标人数 —— 「达标过」是换号的**前置条件**
                //   （规则：先满 2/3/4 人，之后人数掉回 1（队友全走了）才换下一个账号）
                if (targetNow > 0 && players >= targetNow) reachedOnce = true;

                // ★★ 达标状态是**粘住**的：只要本次监控里曾经到过目标人数（reachedOnce），
                //    之后人数掉回去也一直算「已达标」—— 记的是"来过"，不是"现在够不够"。
                var core = BuildPlayerCountParts(players, targetNow, reachedOnce, worldMode);
                string coreText = core.Prefix + core.Highlight + core.Suffix;
                if (coreText == lastCountText) return;

                lastCountText = coreText;
                ApplyThirdLine(core.Prefix, core.Highlight, core.Suffix, false);
                Debug.WriteLine($"[UID/登录后] 联机人数：{coreText}（队友模板命中 {teammates} 个）");
            }

            try
            {
                // ★★ 第三行**不用在这里挂** —— 它早在登录开始时就挂好了（分组内的完整流程才挂，
                //    写着「附加动作：…」）。这里再 StartRun3 一次等于把它淡出再淡入，白白闪一下。
                //    本方法只负责往那一行上填内容（人数 / 进度）。

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    // ★ 切出去了：只暂停，不结束 —— 回到游戏继续跑
                    if (!GameFocusProvider.IsGameFocused())
                    {
                        await Task.Delay(PollIntervalMs, token);
                        continue;
                    }

                    // ★ 单轮异常只跳过这一轮，绝不让整个监控挂掉（它是常驻的）
                    try
                    {
                        // ⓪★ 人数目标每轮重取 —— UI 窗口里改了「双人/三人/四人/不选」，
                        //    下一轮就用新的格式和达标线（不用重新登录一次）。
                        int targetNow = ResolveTarget(target);

                        // ★★ 用户刚动过键鼠 → 这一轮**一个模拟键都不按**（检测照常）。
                        //   ① 让路是"连续"的：从最后一次操作算起满 10 秒才恢复，中途又操作就重新计时；
                        //   ② 只挡按键，不影响下面两张模板的检测与数人 —— 看清"在哪个界面"不耽误。
                        //   ③ 第四行字幕把这件事说出来，并把剩余秒数当倒计时显示。
                        bool userQuiet = UserQuiet();
                        if (!userQuiet)
                        {
                            // 剩余时间向上取整到整秒（10 → 1）；文本变了才发命令（每轮都发会白跨一次 dll）
                            int remainSec = Math.Max(1, (UserQuietMs - UserInputProvider.IdleMs + 999) / 1000);
                            if (remainSec != lastWarnRemainSec)
                            {
                                lastWarnRemainSec = remainSec;
                                SubtitlePresenter.ShowWarn4(
                                    $"发现用户进行了操作，等待用户 {remainSec} 秒内未进行操作时恢复...");
                            }

                            if (!notedUserBusy)
                            {
                                notedUserBusy = true;
                                _userWaitSinceTicks = Environment.TickCount64;
                                ReplyLog($"[让路] 开始：用户刚操作过（空闲 {UserInputProvider.IdleMs}ms）→ 暂停模拟按键，等空闲满 {UserQuietMs}ms");
                            }
                            else if (Environment.TickCount64 - _lastUserWaitLogTicks >= 1000)
                            {
                                // ★ 诊断：让路期间每秒记一笔空闲时间。
                                //   若「空闲」一直停在很小的值（几百毫秒）却不增长，就说明
                                //   **程序自己灌的模拟按键被当成了用户操作** ⇒ 永远让路、看起来像卡死。
                                _lastUserWaitLogTicks = Environment.TickCount64;
                                ReplyLog($"[让路] 进行中：空闲 {UserInputProvider.IdleMs}ms / 需要 {UserQuietMs}ms，" +
                                         $"已让路 {(Environment.TickCount64 - _userWaitSinceTicks) / 1000} 秒，" +
                                         // ★ 对照用：若「空闲」总是跟「本程序刚按完键」对得上，
                                         //   就说明模拟按键被误记成了用户操作（让路永远结束不了）
                                         $"距本程序上次按键 {Environment.TickCount64 - SimulationService.LastSimTicks}ms");
                            }
                        }
                        else if (notedUserBusy)
                        {
                            notedUserBusy = false;
                            lastWarnRemainSec = 0;
                            // 倒计时走完 → 第四行**收掉**（归零不透明度，窗口留着，下次直接显示）
                            SubtitlePresenter.Hide4();
                            ReplyLog($"[让路] 结束：用户已停手（空闲 {UserInputProvider.IdleMs}ms）→ 恢复模拟按键");
                        }

                        // ⓪★【连接已断开】每轮顺带看一眼 —— 它可能在游戏里**任何时刻突然弹出**
                        //   （和登录流程同一个规矩：一轮至少测「当前目标 + 这张」两个区域）。
                        //   ① 「连接服务器失败」= 网络故障 → 彻底收场，不再换号（换号也连不上）；
                        //   ② 「账号在另一处重复登录」= 这个号被顶了 → 换下一个账号。
                        var lost = AccountLoginFlow.DetectConnectionLost();
                        if (lost.detected)
                        {
                            if (lost.hitKey == AccountLoginFlow.ConnectFailedTemplateKey)
                            {
                                Debug.WriteLine(
                                    $"[UID/登录后] 检测到「连接服务器失败」{lost.similarity * 100:F1}% → 网络故障，停止监控");
                                SetLine2(Line2ConnectFailed);
                                return;
                            }

                            Debug.WriteLine(
                                $"[UID/登录后] 检测到「账号在另一处重复登录」{lost.similarity * 100:F1}% → 当前账号被顶号");
                            if (_switchRequested != null)
                            {
                                await _switchRequested("账号在另一处重复登录");
                                _handedOff = true;   // 别再写「人数监控已停止」盖掉新一轮的字幕
                            }
                            return;
                        }

                        // ①★ 两张模板**都测**（互斥，最多命中一个）——不短路、不猜，
                        //    拿到的就是「此刻到底在哪个界面」的完整信息，再据此决定按哪个键。
                        bool chatOpen = AccountLoginFlow.Detect(ChatRegionKey, ChatTemplateKey).detected;
                        bool mainVisible = AccountLoginFlow.Detect(MainRegionKey, MainTemplateKey).detected;

                        // ★★★ 绑定规则：**只要这一轮检测到主界面，就先读一次联机状态** ——
                        //    在读之前不做任何别的事（不数人、不按键、不判断状态机）。
                        //    理由：「多人模式」图标只有主界面存在，状态也只能在这里被更新；
                        //    把它放在所有分支之前，就不会出现"某个分支自己判断完才发现状态是旧的"。
                        //    （两张模板互斥，所以主界面命中就一定是"没在聊天界面"，直接读就行。）
                        if (mainVisible)
                        {
                            worldMode = DetectWorldMode();

                            // ⓪-B ★ 登录进游戏后（第一次站到主界面）**顺手确认世界权限是「直接加入」** ——
                            //    不是的话别人压根进不来，后面凑人数这套就白搭。
                            //    ★ 只做**一次**：之后用户自己改了权限，程序不该再抢回去。
                            //    ★ 它会按 F2 / 点鼠标 / 按 ESC —— 必须等用户停手（userQuiet）才能动。
                            if (!permEnsured && userQuiet)
                            {
                                permEnsured = true;
                                try
                                {
                                    // ★★ 先数人（主界面右侧竖条此刻就能数；一进联机大厅就数不到了）——
                                    //    一进来就满员的世界不需要「直接加入」（谁也进不来了），
                                    //    要的是「确认后可加入」；而且已经是它的话连改都不改，看一眼就退。
                                    int matesNow = worldMode == WorldMode.Single ? 0 : CountTeammates();
                                    int playersNow = Math.Min(matesNow + 1, MaxPlayers);
                                    int permTarget = ResolveTarget(0);
                                    bool alreadyFull = permTarget > 0 && playersNow >= permTarget;

                                    await EnsureWorldPermissionAsync(alreadyFull, token);
                                    // 这套动作里按过 ESC / Enter（关大厅、回主界面），冷却跟着刷新，
                                    // 免得循环下一轮紧接着又按一次。
                                    long nowTicks = Environment.TickCount64;
                                    lastEscTicks = nowTicks;
                                    lastEnterTicks = nowTicks;
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[UID/登录后] 确认世界权限时出错：{ex.Message}");
                                }
                            }
                        }

                        // ★ 记录"连续两个界面都没命中"的起点（命中任意一个就清零）——
                        //   黑屏 / 加载这种过渡期不该按 ESC，见 MissGraceMs。
                        if (chatOpen || mainVisible)
                        {
                            missingSinceTicks = 0;
                            notedMissing = false;
                        }
                        else if (missingSinceTicks == 0)
                        {
                            missingSinceTicks = Environment.TickCount64;
                        }

                        if (chatOpen)
                        {
                            if (MustStayAtMain())
                            {
                                // ★★ 这时**不该待在聊天界面**：
                                //    · 单人世界 → 那儿根本数不到人（右侧没有队友竖条），规矩是待在主界面等；
                                //    · 还没拿到状态 → 必须回主界面确认联机状态（状态只有主界面能读）。
                                //    → 按一次 ESC 退出聊天界面。
                                //    （用户手动打开聊天时，上面那道"让路 10 秒"会先护住他的操作，不会立刻抢键。）
                                //    ★ 这里**保持 locating = true**：我们并没有"打开聊天界面"这个目标状态。
                                UpdatePlayerCount(targetNow);

                                if (EscReady() && userQuiet)
                                {
                                    lastEscTicks = Environment.TickCount64;
                                    Debug.WriteLine(worldMode == WorldMode.Single
                                        ? "[UID/登录后] 单人世界却停在聊天界面 → 按 ESC 退回主界面等待多人模式"
                                        : "[UID/登录后] 联机状态还没确认 → 按 ESC 退回主界面读状态");
                                    SetLine2("正在返回主界面");
                                    SimulationService.PressEscape();
                                }
                            }
                            else
                            {
                                // ② 聊天界面在 = 目标界面，正常数人（两种检测状态都收在这里）
                                //    ★ 注意这里**不更新联机状态** —— 状态只能靠主界面上那张图标读，
                                //      不在主界面时沿用上一次的值（文案里的「单人模式」后缀也不会乱闪）。
                                if (locating || areasPaused)
                                {
                                    locating = false;
                                    if (areasPaused)
                                    {
                                        areasPaused = false;
                                        // 去掉「等待中」后缀：先原地重发一次（同一串要注重发，去重才认），
                                        // 紧接着的 UpdatePlayerCount 会把真正的新数字填上去。
                                        if (lastPlayers > 0)
                                            ApplyCountLine(lastPlayers, targetNow, false);
                                    }
                                    Debug.WriteLine("[UID/登录后] 已在聊天界面 → 转指定检测（100ms）并数人");
                                }

                                UpdatePlayerCount(targetNow);

                                // ★★ 聊天界面打开之后，**每一轮**都要确认它是不是停在
                                //    多人模式的公共聊天频道「当前队伍」上（不是确认一次就完事）：
                                //    没选中就点它，看不到这个频道就往上滚到顶。
                                //    ★ 这里要挪鼠标 / 滚轮 / 点击，所以**让路期间一律不碰**（userQuiet），
                                //      否则就成了"嘴上说着让用户操作、手上还在抢他的鼠标"。
                                if (userQuiet &&
                                    worldMode != WorldMode.Single && worldMode != WorldMode.Unknown)
                                {
                                    try
                                    {
                                        teamChannelReady = await EnsureTeamChannelAsync(token);
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"[UID/登录后] 确认「当前队伍」频道时出错：{ex.Message}");
                                    }
                                }

                                // 第二行放在频道确认**之后**写：这样每轮都反映"此刻在不在当前队伍"。
                                SetLine2(Line2Watching(teamChannelReady));

                                // ★★ 聊天消息这一拍（每 500ms 一次）：**一次扫描**，结果同时给
                                //     ① 两组屏幕标记（队友消息 / 自己消息，带序号 + 模板 + 坐标 + 匹配度）
                                //     ② 自动回复判定（比两个区域最下面那条的 y，队友更低 = 更新就回一句）
                                //     —— 分开各扫一遍会让回复慢一倍，这是"要等两三秒"的主因。
                                lastChatMsgTicks = await ChatMessagesTickAsync(lastChatMsgTicks, userQuiet, token);
                            }
                        }
                        else if (locating)
                        {
                            // ③★ 定位检测：**只有这一层会按键**（和登录流程一致）
                            if (mainVisible)
                            {
                                // ★ 联机状态已经在上面那段"绑定规则"里读过了（检测到主界面就先读状态），
                                //   这里直接用 —— 不再重复检测一遍（那是白跑 4 次截屏）。

                                // 站在主界面：右侧队友竖条一样在，顺便把人数量出来
                                //（第三行文案会带上「 当前处于单人模式」）
                                UpdatePlayerCount(targetNow);

                                // 是不是"刚刚解锁"（上一轮还要待主界面，这一轮已经可以开聊天了）——
                                // 比如单人世界→进入多人模式 / 别人世界的**那一刻**。
                                bool mustStayNow = MustStayAtMain();
                                bool justUnlocked = wasMustStayAtMain && !mustStayNow;
                                wasMustStayAtMain = mustStayNow;

                                if (worldMode == WorldMode.Single)
                                {
                                    // ★★ 单人世界：**停在主界面等，不按 Enter** ——
                                    //    要等进入多人模式 / 他人世界（联机中）才去打开聊天界面。
                                    if (!notedSingleWorld)
                                    {
                                        notedSingleWorld = true;
                                        Debug.WriteLine("[UID/登录后] 当前是单人世界 → 留在主界面等待进入多人模式 / 他人世界");
                                    }
                                    // 第二行点明"在等什么"：停在这儿不是卡住，是在等进入多人模式
                                    SetLine2(Line2SingleWaiting);
                                }
                                else
                                {
                                    if (notedSingleWorld)
                                    {
                                        notedSingleWorld = false;
                                        Debug.WriteLine($"[UID/登录后] 联机状态 = {worldMode} → 可以打开聊天界面了");
                                    }

                                    // 按一次 Enter 打开聊天界面（★ Enter 只在确认站在主界面时才按：
                                    //   聊天界面里 Enter 是「发送」，按错地方会把输入框里的内容发出去）。
                                    //
                                    // ★★ 两种放行条件：
                                    //   ① justUnlocked：刚从"该待主界面"变成"可以开聊天" —— 这是状态**刚翻过来**
                                    //      的那一瞬（单人→多人 / 别人世界），**立刻按**，不再等让路和冷却，
                                    //      否则字幕都写着要开聊天了却要干等好几秒；
                                    //   ② 常规：没在让路期内（用户没在操作）且按键冷却已过。
                                    if (justUnlocked || (EnterReady() && userQuiet))
                                    {
                                        lastEnterTicks = Environment.TickCount64;
                                        Debug.WriteLine(justUnlocked
                                            ? $"[UID/登录后] 联机状态刚变为 {worldMode} → 立即按 Enter 打开聊天界面"
                                            : "[UID/登录后] 定位检测命中主界面 → 按 Enter 打开聊天界面");
                                        SetLine2("正在返回聊天界面");
                                        SimulationService.PressEnter();

                                        // 按完就转去**指定检测**（100ms）盯着聊天界面出现；
                                        // 一直没出现的话，超时后自己退回定位检测重新确认位置。
                                        locating = false;
                                        trackStartTicks = Environment.TickCount64;
                                    }
                                }
                            }
                            else
                            {
                                // ★★ 两个都没命中：先看是不是"进多人模式 / 加载过渡"的那一下黑屏。
                                //    主界面下只要没人为干预，模板是能一直检测到的 —— 突然检测不到 + 用户没操作
                                //    ⇒ 大概率是游戏在切状态。**宽容期内什么都不做**（不按键、也不改第三行），
                                //    等它自己再出现就正常了；超过 MissGraceMs 才当成真跑到别的界面了。
                                bool graceOver = Environment.TickCount64 - missingSinceTicks >= MissGraceMs;

                                if (!graceOver)
                                {
                                    if (!notedMissing)
                                    {
                                        notedMissing = true;
                                        Debug.WriteLine($"[UID/登录后] 突然两个界面都检测不到（用户没操作）→ 大概率在进多人模式 / 加载，"
                                            + $"先不做任何处理，最多等 {MissGraceMs}ms");
                                    }
                                }
                                else
                                {
                                    // 真的不在主界面 / 聊天界面了（派蒙菜单 / 设置 / 联机大厅 / 长时间加载…）：
                                    // 人数保持上一次的 + 补一句「等待中...」，再按一次 ESC 退回上一层。
                                    if (!areasPaused)
                                    {
                                        areasPaused = true;
                                        Debug.WriteLine("[UID/登录后] 定位检测：超过宽容期仍未命中 → 按定位处理（人数暂停更新）");
                                    }
                                    if (lastPlayers > 0)
                                        ApplyCountLine(lastPlayers, targetNow, true);

                                    // ESC 也是「开关」，退一层之后界面还没切过来时不能接着按，
                                    // 否则会把刚退到的那个界面又按开（主界面 → 派蒙菜单）。
                                    if (EscReady() && userQuiet)
                                    {
                                        lastEscTicks = Environment.TickCount64;
                                        Debug.WriteLine("[UID/登录后] 定位检测：两个界面都没命中 → 按 ESC 退回上一层");
                                        SetLine2(MustStayAtMain() ? "正在返回主界面" : "正在返回聊天界面");
                                        SimulationService.PressEscape();
                                    }
                                }
                            }
                        }
                        else
                        {
                            // ④ 指定检测：已经按过键了 → 只盯「聊天界面」等它出现，**不按键**。
                            // ★★ 例外：要待在主界面的两种情形（单人世界 / 状态还没确认）——
                            //    这时不等聊天界面了，立刻退回定位检测去按 ESC 回主界面。
                            if (MustStayAtMain())
                            {
                                locating = true;
                                Debug.WriteLine("[UID/登录后] 指定检测中判定为「该待主界面」（单人 / 状态未确认）→ 退回定位检测");
                            }
                            else
                            {
                                if (!areasPaused)
                                {
                                    areasPaused = true;
                                    Debug.WriteLine("[UID/登录后] 指定检测：盯着聊天界面出现（100ms）");
                                }
                                if (lastPlayers > 0)
                                    ApplyCountLine(lastPlayers, targetNow, true);
                            }

                            if (Environment.TickCount64 - trackStartTicks >= TrackTimeoutMs)
                            {
                                locating = true;
                                Debug.WriteLine($"[UID/登录后] 指定检测连续 {TrackTimeoutMs}ms 没等到聊天界面 → 退回定位检测");
                            }
                        }

                        // ④-B★★ 人数**刚刚达标**（本次监控里的第一次）→ 一整套：
                        //    聊天发一句「人数已达标修改世界权限-确认后可加入」→ 改权限为「确认后可加入」→ 回聊天界面。
                        //    ★ 同样要等用户停手（整套动作里全是按键和鼠标）。
                        if (reachedOnce && targetNow > 0 && !reachedHandled && userQuiet)
                        {
                            reachedHandled = true;
                            try
                            {
                                await HandleReachedAsync(token);

                                // 这套动作里按过 Enter / ESC，冷却跟着刷新
                                long nowTicks = Environment.TickCount64;
                                lastEnterTicks = nowTicks;
                                lastEscTicks = nowTicks;

                                // 一通操作之后现在在哪个界面不确定了 → 回到定位检测重新认画面
                                locating = true;
                                areasPaused = false;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[UID/登录后] 达标后改世界权限时出错：{ex.Message}");
                            }
                        }

                        // ⑤★ 自动换号：人数**曾经达标**、现在又掉回 1（只剩自己 = 队友全走了）
                        //    → 这个号的活干完了 → 交给上层去登录下一个账号。
                        //    （上层会重新走登录流程，那边的定位检测会先把当前账号退掉。）
                        //    ⛔ 必须是"达标过"才换 —— 否则一进单人世界（人数恒为 1）就换了。
                        if (targetNow > 0 && reachedOnce && lastPlayers <= 1)
                        {
                            Debug.WriteLine(
                                $"[UID/登录后] 已达标过且人数掉到 {lastPlayers}/{MaxPlayers}（队友都离开了）→ 切换到下一个账号");
                            SetLine2("队友已离开，正在切换下一个账号");
                            if (_switchRequested != null)
                            {
                                await _switchRequested("队友都已离开");
                                _handedOff = true;   // 同上：交给上层了，收尾别再抢字幕
                            }
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[UID/登录后] 人数检测单轮异常：{ex.Message}");
                    }

                    // ★ 间隔按当前状态走：定位检测 1 秒/轮（在"找自己在哪"，快没意义）；
                    //   指定检测 100ms/轮（在等聊天界面出现，要跟得紧）
                    await Task.Delay(locating ? LocatePollMs : TrackPollMs, token);
                }
            }
            finally
            {
                // ★ 监控结束（停止 / 换号 / 异常）→ 把屏幕上的检测标记收掉，别留在那儿。
                DebugMarkerOverlay.Clear();
                _lastTeammateMsgHitCount = -1;
                _lastOwnMsgHitCount = -1;

                // ★★ 已经把任务交给下一个账号了（自动换号）→ 这里**一律不碰字幕**。
                //    时序是：本方法 `await` 上层回调 = 在UI线程里同步跑完「置底 + 起新一轮登录」，
                //    新一轮的 `ShowRunningSubtitles` 早就写好三行了；回调返回后走到这里，
                //    若再来一遍收尾就会把**别人的**第三行 / 第四行收掉、把 TAB 锁解开
                //    （2026-10-17 的 bug：一触发换号字幕就全没了）。
                //    ⇒ 收尾责任整个移交给新一轮（RunLoginFlowAsync 的 finally 有兜底解锁）。
                if (_handedOff)
                {
                    Debug.WriteLine("[UID/登录后] 人数监控已移交给下一个账号（本轮不碰字幕）");
                }
                else
                {
                    // 走到这里 = 用户停止（或异常）：第三行收掉，别留一个不会再更新的数字在那儿。
                    // ★ 走 AccountLoginFlow.HideThirdLine()（而不是直接 SubtitlePresenter.Hide3()）——
                    //   那边记着「第三行是不是本次挂的」，顺手把标志清掉，下次登录不会误收/漏收。
                    AccountLoginFlow.HideThirdLine();

                    // ★ 第四行也一并收掉（只归零不透明度，窗口留着 —— 它本来就是常驻透明的）
                    SubtitlePresenter.Hide4();

                    if (locked) SubtitlePresenter.UnlockTabHide();   // 停了才让 TAB 恢复收字幕的能力

                    // ★ 到这里才轮到 TAB 提示 —— 上面那句「监控中」是故意不写它的
                    SetLine2(Line2WatchStopped);
                    Debug.WriteLine("[UID/登录后] 联机人数监控已停止");
                }
            }
        }

        /// <summary>
        /// ★ 登录过程中第三行（人数行）的文案：写明这次登录后要做什么 + 「等待中...」。
        /// <list type="bullet">
        ///   <item>勾选了**聊天** → 直接按人数行的格式写：<c>三人联机： 1/4 等待中...</c>
        ///         （登录还没结束，进不去游戏也就数不了人 —— 这时只有自己，所以是 <c>1/4</c>）；
        ///         没勾人数目标时是 <c>当前人数： 1/4 等待中...</c>。</item>
        ///   <item>派蒙 / 联机那两路没有人数的概念 → <c>打开派蒙界面 等待中...</c> 这种。</item>
        ///   <item>一个都没勾 → <c>无附加动作</c>（没有东西在等，就不写「等待中」）。</item>
        /// </list>
        /// </summary>
        public static string BuildWaitingText(PostLoginOptions? options)
        {
            if (options == null || !options.HasAny) return "无附加动作";

            if (options.OpenChat)
                return BuildPlayerCountText(1, options.PlayerCountTarget) + WaitingSuffix;

            return options.ToString() + WaitingSuffix;
        }

        /// <summary>
        /// ★ 读一次当前的联机状态 —— <b>只能在主界面调用</b>（那张图标只在主界面存在）。
        /// <list type="bullet">
        ///   <item>命中「他人世界2P / 3P / 4P」任意一个 → 在联机中，但站在<b>别人的世界</b>；</item>
        ///   <item>命中「多人模式」 → <b>自己的世界</b>开着多人模式；</item>
        ///   <item>四张都检测不到 → <b>单人世界</b>。</item>
        /// </list>
        /// </summary>
        private static WorldMode DetectWorldMode()
        {
            foreach (var key in OtherWorldTemplateKeys)
            {
                if (AccountLoginFlow.Detect(WorldModeRegionKey, key).detected)
                    return WorldMode.OtherWorld;
            }

            if (AccountLoginFlow.Detect(WorldModeRegionKey, MultiplayerTemplateKey).detected)
                return WorldMode.Multiplayer;

            return WorldMode.Single;
        }

        /// <summary>
        /// ★ 确认聊天界面是否停在**多人模式的公共聊天频道「当前队伍」**上 ——
        /// <b>只要在聊天界面，每轮都会走一遍</b>（不是确认一次就完事）。
        /// <para>
        /// 顺序是用户定死的（先看选中点、再看频道，不要调换）：
        /// <list type="number">
        ///   <item><b>先检测「选择当前队伍」那个点</b>（列表第一项是不是选中色）：
        ///         没选中 → <b>点一下它</b>；已选中 → <b>不点</b>；</item>
        ///   <item><b>再检测「当前队伍」频道</b>在不在：
        ///         在 → <b>什么都不做</b>；不在 → <b>往上滚动</b>（滚到顶）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// ★ 这里只做**一轮**（不做内部循环）：外层监控每轮都会来问一次，
        /// 所以「只要还在聊天界面就一直确认着」；点击/滚动各自的等待就是天然的节流。
        /// </para>
        /// </summary>
        /// <returns>这一轮结束时**是否已在「当前队伍」频道上**（第二行文案用它显示 已/未在当前队伍）。</returns>
        private static async Task<bool> EnsureTeamChannelAsync(CancellationToken token)
        {
            var firstItemPoint = CoordinateFormats.GetCached<CoordinateFormats.DetectPoint>(TeamChannelSelectPointKey);
            if (firstItemPoint == null) return false;

            // ★★ 让路期间**一个模拟动作都不做**（连鼠标都不许挪）：
            //    这里会移动鼠标 + 滚轮 + 点击，那都是会打断用户操作的 —— 必须等用户停手满
            //    UserQuietMs 再来。
            if (!IsUserQuiet()) return false;

            // ★ 也不要在"已经不在聊天界面"时乱动 —— 用户可能自己按 ESC 关掉了聊天，
            //   这时应该立刻停手、把控制权交回监控循环（它会重新判断在哪个界面）。
            if (!AccountLoginFlow.Detect(ChatRegionKey, ChatTemplateKey).detected)
            {
                Debug.WriteLine("[UID/登录后] 已不在聊天界面 → 停止「当前队伍」频道确认，交回监控循环");
                return false;
            }

            // ① 先看「选择当前队伍」那个点：**没选中才点它**，已经选中就别再点
            if (!IsTeamChannelSelected())
            {
                Debug.WriteLine("[UID/登录后] 「选择当前队伍」未选中 → 点击列表第一项"
                    + $"（该点实际颜色 {DescribeFirstItemColor()}，期望 (65,72,86)）");

                SimulationService.LeftClickAt(firstItemPoint.X, firstItemPoint.Y);
                await Task.Delay(TeamChannelClickWaitMs, token);
            }

            // ② 再看「当前队伍」频道在不在：**在 = 什么都不做**（本轮就到此为止）
            if (AccountLoginFlow.Detect(TeamChannelRegionKey, TeamChannelTemplateKey).detected)
                return true;

            // ③ 不在 → 往上滚（滚到顶），下一轮从头再来（那时第 ① 步会把它点上）
            if (!IsUserQuiet()) return false;
            Debug.WriteLine($"[UID/登录后] 没检测到「当前队伍」频道 → 向上滚 {TeamChannelScrollTimes} 格");

            SimulationService.MoveTo(firstItemPoint.X, firstItemPoint.Y);
            await Task.Delay(50, token);
            SimulationService.ScrollUp(TeamChannelScrollTimes);
            await Task.Delay(TeamChannelScrollWaitMs, token);

            return false;
        }

        /// <summary>
        /// 「列表第一项」那个点的**实际像素颜色**（诊断用）。
        /// <para>★ 打印的是**真正被取样的那个像素**（缓存坐标已经是屏幕坐标，直接按 x/y 读）。</para>
        /// </summary>
        private static string DescribeFirstItemColor()
        {
            var point = CoordinateFormats.GetCached<CoordinateFormats.DetectPoint>(TeamChannelSelectPointKey);
            if (point == null) return "坐标取不到";

            var rgb = ImageRecognition.GetPixelColor(point.X, point.Y);
            return $"({rgb.R},{rgb.G},{rgb.B})";
        }

        /// <summary>
        /// 用户是不是"安静"了足够久（连续 <see cref="UserQuietMs"/> 没操作键鼠）——
        /// 只有安静时才允许发模拟动作（按键 / 移鼠标 / 滚轮 / 点击）。
        /// <para>监控循环里的局部函数用的是同一判据，这里给静态方法用（跨 dll 拿宿主的空闲时间）。</para>
        /// </summary>
        private static bool IsUserQuiet() => UserInputProvider.IdleMs >= UserQuietMs;

        /// <summary>
        /// 「列表第一项」那个点此刻是不是"已选中"的颜色（<c>(65,72,86)</c>）。
        /// 坐标取不到 / 像素读取失败时返回 false（= 当作没选中，比乱点安全）。
        /// <para>
        /// ★★ <b>必须按 x / y 直接取色</b>：<see cref="CoordinateFormats.GetCached{T}"/> 拿到的点
        /// 已经是**屏幕坐标**（`UpdateCache` 里统一做过 <c>ToScreen()</c>，和区域截图 / 点击用的是同一套坐标）。
        /// ⛔ 不能走 <c>CheckPixelColor(DetectPoint)</c> 那个重载 —— 它内部还会再 <c>ToScreen()</c> 一次，
        /// 等于把坐标又缩放 + 偏移了一遍，读到的是**完全另一个位置**的像素 ⇒ 永远判"未选中"，
        /// 表现就是鼠标每一轮都被拖去点一下（用户报过）。
        /// </para>
        /// </summary>
        private static bool IsTeamChannelSelected()
        {
            var point = CoordinateFormats.GetCached<CoordinateFormats.DetectPoint>(TeamChannelSelectPointKey);
            if (point == null) return false;

            return ImageRecognition.CheckPixelColor(point.X, point.Y, point.Colors, point.Tolerance);
        }

        /// <summary>
        /// 聊天界面里「聊天输入框」此刻是不是**白色**（= 有焦点、可以打字）。
        /// 坐标取不到 / 像素读取失败时返回 false。
        /// <para>★ 取色方式和 <see cref="IsTeamChannelSelected"/> 一样：缓存里已是屏幕坐标，按 x / y 直接读。</para>
        /// </summary>
        private static bool IsChatInputFocused()
        {
            var point = CoordinateFormats.GetCached<CoordinateFormats.DetectPoint>(ChatInputPointKey);
            if (point == null) return false;

            return ImageRecognition.CheckPixelColor(point.X, point.Y, point.Colors, point.Tolerance);
        }

        /// <summary>
        /// ★★ 让聊天输入框拿到焦点 —— **用点击，不用回车**。
        /// <para>
        /// 为什么不用 Enter：输入框里**可能还留着上一次没清掉的文字**，那一下回车就等于
        /// 把残留内容直接发出去了（发的还不是我们想发的那句）。点一下是最直接的：
        /// 点哪儿就聚焦哪儿，不会顺带触发"发送"。
        /// </para>
        /// <para>★ 点击坐标用 <see cref="ChatInputPointKey"/>（缓存里已是屏幕坐标）；
        /// ⛔ 别用 <c>AccountLoginFlow.ClickPoint</c>（强转 Point 会抛异常被吞，表现为"点了没反应"）。</para>
        /// </summary>
        /// <returns>点完之后输入框确实变白（有焦点）返回 true。</returns>
        private static async Task<bool> FocusChatInputAsync(CancellationToken token)
        {
            if (IsChatInputFocused()) return true;

            // ★★ 2026-10-17 改回**按回车**聚焦：
            //    之前用"点击输入框那个点"，实测**点了也不一定会变白**（聊天界面上的点击有时被别的层吃掉），
            //    于是"始终没焦点 → 放弃发送"。回车是游戏自己打开输入框的方式，稳。
            //    ⚠️ 副作用：输入框里若本来有字，那一下回车会把它发出去 —— 所以调用方
            //       （<see cref="SendNoticeToChatAsync"/>）在回车之后**无论如何都清空一次**。
            for (int attempt = 1; attempt <= MaxChatFocusAttempts; attempt++)
            {
                ReplyLog($"输入框没有焦点 → 第 {attempt}/{MaxChatFocusAttempts} 次按回车聚焦");
                SimulationService.PressEnter();
                await Task.Delay(ChatFocusRecheckMs, token);

                if (IsChatInputFocused())
                {
                    ReplyLog($"第 {attempt} 次回车后输入框已有焦点");
                    return true;
                }
            }

            ReplyLog($"按了 {MaxChatFocusAttempts} 次回车，输入框仍然没有变成白色");
            return false;
        }

        /// <summary>★ 「队友消息」标记的名字前缀（清空时只清自己这组）。</summary>
        private const string TeammateMsgMarkPrefix = "队友消息#";

        /// <summary>★ 「联机队友」（人数检测）标记的名字前缀 —— 和上面那组各清各的。</summary>
        private const string TeammateCountMarkPrefix = "联机队友#";

        /// <summary>★ 「自己消息」标记的名字前缀（同样只清自己这组）。</summary>
        private const string OwnMsgMarkPrefix = "自己消息#";

        /// <summary>
        /// ★ 「最新消息内容」标记的名字前缀 —— 认出那条队友消息说的是哪一句，就把它画在屏幕上
        /// （和队友/自己两组各清各的）。
        /// </summary>
        private const string MsgContentMarkPrefix = "最新消息内容#";

        /// <summary>★ 上一轮画出来的「队友消息」命中数（-1 = 还没画过）。只用来让日志别每 500ms 刷一次。</summary>
        private static int _lastTeammateMsgHitCount = -1;

        /// <summary>★ 「自己消息」那一组的上一轮命中数（同上，只为日志不刷屏）。</summary>
        private static int _lastOwnMsgHitCount = -1;

        /// <summary>★ 「最新消息内容」那一组的上一轮命中数（同上）。</summary>
        private static int _lastMsgContentHitCount = -1;

        /// <summary>
        /// ★ 上一次判过「认不出内容」的那条消息的 y（-1 = 没有）。
        /// <para>只用来让"认不出来"的日志别每 500ms 刷一行 —— 换了一条新的（y 变了）才再记一次。</para>
        /// </summary>
        private static int _lastUnknownMsgY = -1;

        /// <summary>★ 人数检测那一组的节流时刻 / 上一轮命中数（同上，只为日志不刷屏）。</summary>
        private static long _lastTeammateMarkTicks;
        private static int _lastTeammateHitCount = -1;

        /// <summary>
        /// ★★ 聊天界面里**每 <see cref="ChatMsgMarkIntervalMs"/> 一次**的统一节拍：
        /// <b>只扫一遍</b>两个区域（队友消息 / 自己消息），这一次的结果同时喂给
        /// <list type="number">
        ///   <item>两组屏幕标记（<see cref="DrawHits"/>）；</item>
        ///   <item>自动回复的判定（<see cref="AutoReplyIfNeededAsync"/>）。</item>
        /// </list>
        /// <para>
        /// ★★ 为什么合成一次：分开各扫一遍的话，一轮要做 **4 次**全区域匹配（两个区域各两遍），
        /// 单次匹配含一次截屏 + NCC 滑窗，累起来就是"回复要等两三秒"的一大半。
        /// 合并之后一轮只有 2 次，**而且屏幕上画的和判定用的严格是同一帧的数据**。
        /// </para>
        /// </summary>
        /// <returns>新的"上次扫描时刻"（局部变量不能按引用传，回传给调用方自己存）。</returns>
        private static async Task<long> ChatMessagesTickAsync(
            long lastTicks, bool userQuiet, CancellationToken token)
        {
            long now = Environment.TickCount64;
            if (now - lastTicks < ChatMsgMarkIntervalMs)
                return lastTicks;

            // ★ 计时日志：这一拍花了多久（含两次区域匹配）
            long scanStart = Environment.TickCount64;

            // ★★★ 两个区域**共用同一个自适应上边界**（2026-10-18 用户要求）：
            //   上面那些旧消息不用截进去 —— 从"最新那条消息"往下截就够了。
            //   谁最新都无所谓，起点取两个区域里最下面那条的 y。
            var teammateHits = DetectChatStrip(TeammateMsgRegionKey, ChatTeammateTemplateKeys);
            var ownHits = DetectChatStrip(OwnMsgRegionKey, OwnMsgTemplateKeys);

            int quickTY = BottomY(teammateHits);
            int quickOY = BottomY(ownHits);

            // 下一轮从哪儿开始截（往上留一条消息的余量，免得模板正好卡在边界上被切掉）。
            // ★ 安全前提：聊天列表"旧的在上、新的在下"，所以最新那条若跑到起点以上，
            //   区域里就不可能再有别的消息 ⇒ 必然 0 命中 ⇒ 归零、下一轮全区域重扫。
            int newestY = Math.Max(quickTY, quickOY);
            _chatScanTopY = newestY >= 0 ? Math.Max(0, newestY - ChatScanMarginY) : 0;

            // ★★ 只有「队友才是最下面那条」时才去认它**说了什么**
            //    （要多做一次截图 + 5 次模板匹配；自己本来就是最新的时候纯属浪费）。
            var contentHits = quickTY >= 0 && (quickOY < 0 || quickTY > quickOY)
                ? DetectLatestMessageContent(teammateHits)
                : new List<(int X, int Y, double Similarity, string Key)>();
            long scanCost = Environment.TickCount64 - scanStart;

            DrawHits(teammateHits, TeammateMsgMarkPrefix, "队友消息", ref _lastTeammateMsgHitCount);
            DrawHits(ownHits, OwnMsgMarkPrefix, "自己消息", ref _lastOwnMsgHitCount);
            DrawHits(contentHits, MsgContentMarkPrefix, "最新消息内容", ref _lastMsgContentHitCount);

            // ★ 计时日志：**y 一变就打**（那是"队友冒出新消息"的时刻）；没变化时每 2 秒打一行心跳，
            //   免得 500ms 一行刷屏。
            int logTY = BottomY(teammateHits), logOY = BottomY(ownHits);
            if (logTY != _lastLogTeammateY || logOY != _lastLogOwnY || now - _lastChatScanLogTicks >= 2000)
            {
                _lastChatScanLogTicks = now;
                _lastLogTeammateY = logTY;
                _lastLogOwnY = logOY;
                ReplyLog($"[计时] 距上次 {now - lastTicks}ms｜扫描 {scanCost}ms｜队{logTY}/己{logOY}｜" +
                         $"条数 {teammateHits.Count}/{ownHits.Count}｜y列表 队[{YList(teammateHits)}] 己[{YList(ownHits)}]｜" +
                         $"内容[{ContentList(contentHits)}]｜几何[{AccountLoginFlow.LastWordSizeNote}]");
            }

            await AutoReplyIfNeededAsync(teammateHits, ownHits, contentHits, userQuiet, token);

            return now;
        }

        /// <summary>
        /// ★★ 把一组命中画到屏幕上（三组标记共用）—— 每个标记写清
        /// <b>序号</b>、<b>命中的模板</b>（<c>2P队友</c> 这种短名）、<b>屏幕坐标</b>、<b>匹配度</b>。
        /// <para>
        /// ⛔ 一条都没检测到就**什么都不画** —— 覆盖层没有标记时本来就是隐藏的，不会在屏幕上留东西。
        /// ★ 这一步**纯绘制**：不检测、不按键、不发消息（命中由调用方扫好传进来）。
        /// </para>
        /// </summary>
        private static void DrawHits(
            List<(int X, int Y, double Similarity, string Key)> hits,
            string prefix, string logName, ref int lastCount)
        {
            // ★ 每轮先清掉上一轮**这一组**的标记：命中数变少时屏幕上立刻是对的，不会留着旧的。
            //   ⛔ 只清自己这组（按前缀）—— 全清会把另外几组刚画上去的一起抹掉。
            DebugMarkerOverlay.Clear(prefix);

            if (hits.Count != lastCount)
            {
                lastCount = hits.Count;
                Debug.WriteLine(hits.Count == 0
                    ? $"[UID/登录后] {logName}：一条都没检测到（屏幕上不画标记）"
                    : $"[UID/登录后] {logName}：检测到 {hits.Count} 个目标 → 已画到屏幕上");
            }

            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                DebugMarkerOverlay.Report(
                    $"{prefix}{i}", h.X, h.Y,
                    $"#{i + 1} {ShortTemplateName(h.Key)} ({h.X},{h.Y}) {h.Similarity * 100:F0}%");
            }
        }

        /// <summary>
        /// ★★ 自动回复（2026-10-17 用户定死的判定方式）：
        /// **只看两个区域各自最下面（y 最大）的那一条**，比它们的 y。
        /// <list type="bullet">
        ///   <item>队友最下面 y = 150、自己 = 180 ⇒ 自己更低 = **自己才是最新** ⇒ 不回；</item>
        ///   <item>自己 = 100、队友 = 150 ⇒ 队友更低 = **队友是最新** ⇒ 回一句；</item>
        ///   <item>回完之后自己那条变成最下面 ⇒ 下一轮自然是"自己最新" ⇒ 不会再发。</item>
        /// </list>
        /// 上面那些旧消息一律无视（不管各自区域里检测到几个）。
        /// <para>
        /// ★ 命中是 <see cref="ChatMessagesTickAsync"/> 刚扫好传进来的 —— **不再重复扫一遍**
        /// （重复扫是"回复慢"的主因）。
        /// ★ 用户正在操作（<c>userQuiet=false</c>）时一律不发 —— 发送要走模拟按键，不能抢他的输入。
        /// ★ 发送走 <see cref="SendNoticeToChatAsync"/>（三项前置确认 + 清空 + 粘贴 + 回车）。
        /// </para>
        /// </summary>
        private static async Task AutoReplyIfNeededAsync(
            List<(int X, int Y, double Similarity, string Key)> teammateHits,
            List<(int X, int Y, double Similarity, string Key)> ownHits,
            List<(int X, int Y, double Similarity, string Key)> contentHits,
            bool userQuiet, CancellationToken token)
        {
            // 用户在操作 → 让路，本轮不判定（更不会按键）
            if (!userQuiet) return;

            // ★★★ 「申请权限」的 10 秒窗口优先结算 —— 它**不看"自己是不是最新"**：
            //     程序刚把那句招募话发出去，自己就已经是最新的了，按普通判定会被直接跳过。
            if (await ResolveApplyPendingAsync(teammateHits, token)) return;

            int teammateY = BottomY(teammateHits);
            int ownY = BottomY(ownHits);

            // 队友那边一条都没有 → 没什么可回的（顺手把计时起点清掉）
            if (teammateY < 0) { _replyPendingSinceTicks = 0; return; }

            // ★ 自己才是最后发言的人（或两者同高）→ 不用回。
            //   ownY = -1（自己消息检测不到）当作"自己还没有消息"，那就按"队友是最新"处理。
            if (ownY >= 0 && teammateY <= ownY) { _replyPendingSinceTicks = 0; return; }

            // ★★ 认「最新那条队友消息**说了什么**」，认到哪一句就回对应的话。
            //    ⛔ 旧那套"只要自己不是最新就发你好"只是测试用的，2026-10-17 已按用户要求删除。
            //    一条都没认出来 ⇒ **不回**（不认识就别乱答）。
            string contentKey = "";
            int contentY = -1;
            foreach (var h in contentHits)
            {
                if (h.Y > contentY) { contentY = h.Y; contentKey = h.Key; }
            }

            // ★★★ 三张「权限」模板 = 队友点名要某个世界权限 ⇒ 不是回一句话就完事，**要真的去改**：
            //   ① 当前已经是它 → 只回「当前已经是XXX」，不动权限；
            //   ② 不是 → 先发「我这就去把世界权限改成XXX」，再执行切换，改完回聊天界面。
            WorldPermission? wantPermission =
                contentKey == MsgContentPermNotAllowKey ? WorldPermission.NotAllowJoin :
                contentKey == MsgContentPermDirectKey ? WorldPermission.DirectJoin :
                contentKey == MsgContentPermConfirmKey ? WorldPermission.ConfirmJoin :
                (WorldPermission?)null;

            // ★★★ 发这条指令的是哪位（2P / 3P / 4P）—— 「指令权限」靠它判断谁能真的指挥。
            string senderTag = BottomTeammateTag(teammateHits);

            // ★★★ 队友发「申请权限」⇒ 全员都有 / 自己没权限 / 自己有权限（开 10 秒窗口招人）
            if (contentKey == MsgContentApplyPermissionKey)
            {
                await HandleApplyPermissionAsync(senderTag, contentKey, teammateY, ownY, token);
                return;
            }

            if (wantPermission.HasValue)
            {
                await HandlePermissionRequestAsync(
                    wantPermission.Value, contentKey, senderTag, teammateY, ownY, token);
                return;
            }

            // ★ 队友发「换号」⇒ 播报下一个号是谁 →（必要时）先把权限改成确认后可加入 → 换号。
            if (contentKey == MsgContentSwitchKey)
            {
                await HandleSwitchRequestAsync(contentKey, senderTag, teammateY, ownY, token);
                return;
            }

            string reply = ReplyTextFor(contentKey, teammateHits);
            if (string.IsNullOrEmpty(reply))
            {
                // 只在"换了一条新消息"时记一次，别每 500ms 刷一行
                if (_lastUnknownMsgY != teammateY)
                {
                    _lastUnknownMsgY = teammateY;
                    ReplyLog($"[内容] 最新队友消息（y={teammateY}）没有可回的话" +
                             $"（命中 {contentHits.Count} 条，认出「{ShortTemplateName(contentKey)}」）→ 不回");
                }
                _replyPendingSinceTicks = 0;
                return;
            }
            _lastUnknownMsgY = -1;

            long now = Environment.TickCount64;

            // ★ 计时起点：只在**第一次**判定"需要回复"时记下（冷却等待期间不重置，
            //   那样最后打印的才是"从发现到真的发出"的完整耗时）
            if (_replyPendingSinceTicks == 0) _replyPendingSinceTicks = now;

            // ★★ 2026-10-17 用户定死：最新的是队友 ⇒ **立刻发，任何等待都不要**。
            //   唯一保留的闸：自己消息检测不到（ownY < 0）时给一个最小间隔，
            //   否则因为"自己永远不是最新"会每 500ms 发一条（实测过的刷屏）。
            int gap = ownY >= 0 ? AutoReplyMinGapMs : AutoReplyCooldownMs;
            if (gap > 0 && now - _lastAutoReplySentTicks < gap) return;

            ReplyLog($"判定要回复：队友 y={teammateY}「{ShortTemplateName(contentKey)}」" +
                     $"> 自己 y={(ownY < 0 ? "无" : ownY.ToString())} " +
                     $"→ 回「{reply}」（首次判定于 {now - _replyPendingSinceTicks}ms 前，" +
                     $"本次间隔闸 {gap}ms）");

            long sendStart = Environment.TickCount64;
            bool sent = await SendNoticeToChatAsync(reply, token);
            long now2 = Environment.TickCount64;

            if (sent)
            {
                _lastAutoReplySentTicks = now2;
                // ★★ 计时日志：动作本身多久 + 从"发现需要回复"到"真的发出"总共多久
                ReplyLog($"[计时] 自动回复已发送｜发送动作 {now2 - sendStart}ms｜" +
                         $"从判定需要回复到发出 {now2 - _replyPendingSinceTicks}ms");
                _replyPendingSinceTicks = 0;
            }
            else
            {
                // 发送失败**不计次数、不记时刻** —— 冷却过后自动再试，绝不静默放弃
                ReplyLog($"自动回复本次没发出去（耗时 {now2 - sendStart}ms，等待 {now2 - _replyPendingSinceTicks}ms）→ 稍后重试");
            }
        }

        /// <summary>
        /// 一组命中里**最下面（y 最大）**那个的屏幕 y —— 检测到几个都无所谓，只取这一个。
        /// </summary>
        /// <returns>屏幕 y；一条都没检测到返回 -1。</returns>
        private static int BottomY(List<(int X, int Y, double Similarity, string Key)> hits)
        {
            int bottom = -1;
            foreach (var h in hits)
            {
                if (h.Y > bottom) bottom = h.Y;
            }
            return bottom;
        }

        /// <summary>
        /// ★「最下面那条队友消息」是**哪位** —— 拿它的模板短名拼出称呼。
        /// <para>
        /// 例：最下面那条命中的是 <c>其他模版_聊天界面_2p</c> ⇒ 短名 <c>2p</c> ⇒ 统一成 <b>2P</b>，
        /// 于是自动回复的话是「<b>你好2P</b>」（3P / 4P 同理）。取不到就返回空串（那就只说「你好」）。
        /// </para>
        /// </summary>
        private static string BottomTeammateTag(List<(int X, int Y, double Similarity, string Key)> hits)
        {
            string name = "";
            int bottom = -1;
            foreach (var h in hits)
            {
                if (h.Y > bottom) { bottom = h.Y; name = ShortTemplateName(h.Key); }
            }

            // "2P队友" → "2P"（老的「联机队友」模板，聊天里已不用，留着兼容）
            if (name.EndsWith("队友")) name = name.Substring(0, name.Length - 2);

            // "2p" → "2P"：聊天专用模板的文件名里 p 是**小写**，说话时统一成大写
            if (name.Length == 2 && (name[1] == 'p' || name[1] == 'P'))
                name = name.Substring(0, 1) + "P";

            return name;
        }

        /// <summary>
        /// ★★ 扫聊天里的一个竖条区域（队友消息 / 自己消息）—— **上边界自适应**：
        /// 从 <see cref="_chatScanTopY"/> 往下截，上面的旧消息一概不进画面。
        /// <para>
        /// ★ 起点还没算出来（=0）或不在区域里时，退回区域表里的原始区域（等价于一整条都扫）。
        /// ★ 返回的坐标仍是**屏幕坐标**（<c>DetectAllInRect</c> 内部按 <c>rect.Rect.X1/Y1</c> 加回去）。
        /// </para>
        /// </summary>
        private static List<(int X, int Y, double Similarity, string Key)> DetectChatStrip(
            string regionKey, string[] templateKeys)
        {
            var empty = new List<(int X, int Y, double Similarity, string Key)>();

            var baseRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(regionKey);
            if (baseRect == null) return empty;

            int y1 = baseRect.Rect.Y1;

            // 起点落在区域内部才用它；否则（0 / 越界）就按原区域扫
            if (_chatScanTopY > y1 && _chatScanTopY < baseRect.Rect.Y2)
                y1 = _chatScanTopY;

            if (y1 == baseRect.Rect.Y1)
                return AccountLoginFlow.DetectAll(regionKey, templateKeys);

            var rect = new CoordinateFormats.MatchRect(
                baseRect.Rect.X1, y1, baseRect.Rect.X2, baseRect.Rect.Y2, baseRect.Threshold);

            return AccountLoginFlow.DetectAllInRect(rect, templateKeys);
        }

        /// <summary>
        /// ★★ 认「最新那条队友消息**说了什么**」—— 固定区域 + 从最新那条往下裁：
        /// <list type="bullet">
        ///   <item><b>固定区域</b>的左上角 = 「队友消息」的左上角，右下角 = 「自己消息」的右下角
        ///         （基准下就是 <c>(828, 208, 1976, 1874)</c>）；</item>
        ///   <item>真正截图时把<b>上边界抬到最新那条队友消息的 y</b> —— 上面那些旧消息不截进去，
        ///         只截它往下剩下的那一段，少一片内容就少一片误匹配。</item>
        /// </list>
        /// <para>
        /// ★ 两个角都从区域表取 <c>GetCached</c> 的**屏幕坐标**（基准值不能直接当像素用），
        /// 所以改那两个区域这里会自动跟着变，不用再维护一份数值。
        /// ★ 命中坐标也是屏幕坐标，传给 <see cref="AccountLoginFlow.DetectAllInRect"/> 时**不要再 ToScreen**。
        /// </para>
        /// </summary>
        /// <returns>五张内容模板的命中（0 条 = 认不出这条说了什么）。</returns>
        private static List<(int X, int Y, double Similarity, string Key)> DetectLatestMessageContent(
            List<(int X, int Y, double Similarity, string Key)> teammateHits)
        {
            var empty = new List<(int X, int Y, double Similarity, string Key)>();
            if (teammateHits.Count == 0) return empty;

            // 最下面（y 最大）的那条 = 最新的那条
            var latest = teammateHits[0];
            foreach (var h in teammateHits)
            {
                if (h.Y > latest.Y) latest = h;
            }

            var tRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(TeammateMsgRegionKey);
            var oRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(OwnMsgRegionKey);
            if (tRect == null || oRect == null) return empty;

            int x1 = tRect.Rect.X1;   // 左 = 「队友消息」的左边
            int x2 = oRect.Rect.X2;   // 右 = 「自己消息」的右边
            int y1 = latest.Y;        // ★ 上 = 最新那条的位置（再往上都是旧消息，不截）
            int y2 = oRect.Rect.Y2;   // 下 = 「自己消息」的下边（聊天列表的底）

            if (x2 <= x1 || y2 <= y1) return empty;

            var rect = new CoordinateFormats.MatchRect(x1, y1, x2, y2, MsgContentThreshold);

            // ★★★ 带**几何校验**（2026-10-18 用户方案）：白字框出来的"词"必须和模板差不多宽高，
            //     否则就是比模板更长的句子（如「你可以发送申请权限」），不算命中。
            //     ⚠️ 表情包不是白字 ⇒ 用 skipWordSizeKeys 排除，别让它被白字校验判死。
            return AccountLoginFlow.DetectAllInRect(
                rect, TeammateMsgContentKeys, MsgContentRoughScale,
                requireWordSize: true, skipWordSizeKeys: EmojiContentKeys);
        }

        /// <summary>
        /// ★★ 按「队友最新一条说了什么」决定回什么（2026-10-17 用户定死的话术表）：
        /// <list type="bullet">
        ///   <item>救命！ → <c>菜就多练！</c></item>
        ///   <item>开始挑战吧！ → <c>加油！</c></item>
        ///   <item>你好！ → <c>你好</c> + 2P/3P/4P（**谁发的就叫谁**）</item>
        ///   <item>谢谢！ → <c>不用谢！</c></item>
        ///   <item>真是精彩! → <c>真是精彩！</c></item>
        ///   <item>世界权限 → <c>当前是-直接加入 / 当前是-确认后加入 / 当前是-不允许加入</c>
        ///         （取 <see cref="_knownPermission"/>）</item>
        /// </list>
        /// <para>⛔ 一条都没认出来就返回空串 ⇒ 调用方**不回**（不认识就别乱答）。</para>
        /// </summary>
        private static string ReplyTextFor(
            string contentKey,
            List<(int X, int Y, double Similarity, string Key)> teammateHits)
        {
            switch (contentKey)
            {
                case MsgContentHelloKey: return AutoReplyText + BottomTeammateTag(teammateHits);
                case MsgContentHelpKey: return "菜就多练！";
                case MsgContentChallengeKey: return "加油！";
                case MsgContentThanksKey: return "不用谢！";
                case MsgContentGreatKey: return "真是精彩！";
                case MsgContentPointingKey: return "反弹！";
                case MsgContentWorldPermKey: return "当前是-" + WorldPermissionText(_knownPermission);

                // 「下一个」⇒ 报下一个号是谁（不换号）。不知道下一个是谁（后面没账号了）就不回，
                // 免得只说半句「下一个」让人更困惑。
                case MsgContentNextAccountKey:
                    return string.IsNullOrEmpty(_nextAccountText) ? "" : "下一个「" + _nextAccountText + "」";

                default: return "";
            }
        }

        /// <summary>世界权限 → 聊天里回的那几个字（和「世界权限」下拉里的一致）。</summary>
        private static string WorldPermissionText(WorldPermission permission)
        {
            switch (permission)
            {
                case WorldPermission.NotAllowJoin: return "不允许加入";
                case WorldPermission.ConfirmJoin: return "确认后加入";
                default: return "直接加入";   // DirectJoin / None（未知时按流程默认值）
            }
        }

        /// <summary>没勾「指令权限」的队友发「换号」时回的话（2026-10-18 用户定）。</summary>
        private const string NoRightToSwitchText = "你还没有资格让我换号";

        /// <summary>没勾「指令权限」的队友要改世界权限时回的话。</summary>
        private const string NoRightToChangePermissionText = "你还没有资格让我更改世界权限";

        /// <summary>
        /// ★★★ 这位队友**有没有指挥权**？—— 只有「换号」「改世界权限」这两条会问它。
        /// <para>
        /// 看窗口第 3 列「2P / 3P / 4P 指令权限」勾了哪几个。纯回答的指令
        /// （你好 / 下一个 / 世界权限 / 谢谢…）不走这里，谁发都回。
        /// </para>
        /// <para>★ 认不出是谁（P 标记没命中）时**不拦** —— 宁可执行也别把正常指令卡死。</para>
        /// </summary>
        private static bool CanOrder(string senderTag)
        {
            var options = _liveOptions;
            if (options == null) return true;   // 拿不到选项 → 保持老行为（谁都能指挥）

            switch (senderTag)
            {
                case "2P": return options.Allow2P;
                case "3P": return options.Allow3P;
                case "4P": return options.Allow4P;
                default: return true;
            }
        }

        /// <summary>
        /// ★ 没资格却下了指令 —— 只回一句拒绝，**什么操作都不执行**（2026-10-18 用户定）。
        /// </summary>
        private static async Task RejectCommandAsync(
            string rejectText, string contentKey, string senderTag, int teammateY, CancellationToken token)
        {
            // 拒绝也是一次发送，给个最小间隔兜底（正常发出去后自己就成了最新，本来也不会重复）
            long now = Environment.TickCount64;
            if (now - _lastAutoReplySentTicks < AutoReplyCooldownMs) return;

            ReplyLog($"判定要回复：队友 y={teammateY}「{ShortTemplateName(contentKey)}」" +
                     $"（{senderTag} 没勾指令权限）→ 回「{rejectText}」");

            bool sent = await SendNoticeToChatAsync(rejectText, token);
            if (sent) _lastAutoReplySentTicks = Environment.TickCount64;
        }

        /// <summary>
        /// ★ 三个 P 位的指令权限是不是**全勾了**（全勾 = 谁都能指挥，再申请就没意义）。
        /// <para>拿不到选项时返回 false —— 宁可当"还没全有"，也别误判成"都有权限"而不让人申请。</para>
        /// </summary>
        private static bool AllTeammatesHaveRight()
        {
            var options = _liveOptions;
            return options != null && options.Allow2P && options.Allow3P && options.Allow4P;
        }

        /// <summary>
        /// ★★★ 给某个 P 位**补上指令权限**（2026-10-18 用户要求"UI 复选框同步更改"）：
        /// <list type="number">
        ///   <item>先改运行时的选项 —— 立刻生效（下一轮判定就认他有权限）；</item>
        ///   <item>再通知窗口把对应复选框勾上 —— UI 那边勾上会自己走
        ///         <c>ExtraOptionsChanged</c> → <c>UpdateLiveOptions</c>，两边最终一致。</item>
        /// </list>
        /// </summary>
        private static void GrantCommandPermission(string tag)
        {
            var live = _liveOptions;
            if (live != null)
            {
                switch (tag)
                {
                    case "2P": live.Allow2P = true; break;
                    case "3P": live.Allow3P = true; break;
                    case "4P": live.Allow4P = true; break;
                    default: return;   // 认不出是谁（P 标记没命中）⇒ 不知道该给谁，什么都不动
                }
            }
            else if (tag != "2P" && tag != "3P" && tag != "4P")
            {
                return;
            }

            try
            {
                _grantPermissionTo?.Invoke(tag);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录后] 同步指令权限复选框失败（{tag}）：{ex.Message}");
            }
        }

        /// <summary>把「申请权限」的等待窗口关掉（结算完 / 新一轮开始 都要调）。</summary>
        private static void ResetApplyPending()
        {
            _applyPending = false;
            _applyFromTag = "";
            _applyNuisanceTag = "";
            _applyStartY = -1;
            _applyStartTicks = 0;
        }

        /// <summary>
        /// ★★★ 队友发「申请权限」（2026-10-18 用户定）：
        /// <list type="number">
        ///   <item>三个 P 位**全都有权限** ⇒ 只回「都有权限你申请个锤子」；</item>
        ///   <item>发的人**自己没权限** ⇒ 回「你还没有权限不能申请」；</item>
        ///   <item>发的人有权限 ⇒ 回「谁没权限发送任意消息即可获得权限」，随后开 <see cref="ApplyWaitMs"/> 的窗口等人。</item>
        /// </list>
        /// <para>★ 窗口只在消息**真的发出去**之后才开 —— 发不出去就别等了，没人看得到那句话。</para>
        /// </summary>
        private static async Task HandleApplyPermissionAsync(
            string senderTag, string contentKey, int teammateY, int ownY, CancellationToken token)
        {
            long now = Environment.TickCount64;
            int gap = ownY >= 0 ? AutoReplyMinGapMs : AutoReplyCooldownMs;
            if (gap > 0 && now - _lastAutoReplySentTicks < gap) return;

            string text;
            bool openWindow = false;

            if (AllTeammatesHaveRight())
            {
                text = ApplyAllHaveRightText;
            }
            else if (!CanOrder(senderTag))
            {
                text = ApplyNoRightText;
            }
            else
            {
                text = ApplyOpenText;
                openWindow = true;
            }

            ReplyLog($"判定要回复：队友 y={teammateY}「{ShortTemplateName(contentKey)}」（{senderTag}）" +
                     $"→ 回「{text}」{(openWindow ? $"，随后等 {ApplyWaitMs}ms 看谁要权限" : "")}");

            bool sent = await SendNoticeToChatAsync(text, token);
            if (sent) _lastAutoReplySentTicks = Environment.TickCount64;

            if (openWindow && sent)
            {
                _applyPending = true;
                _applyFromTag = senderTag;
                _applyStartTicks = Environment.TickCount64;
                _applyStartY = teammateY;
            }
        }

        /// <summary>
        /// ★★★ 结算「申请权限」的等待窗口（2026-10-18 用户定，第二版）：
        /// <list type="bullet">
        ///   <item>出现**比上次结算点更靠下**的队友消息 ⇒ 有人回复了：
        ///     <b>本来就有权限的</b> ⇒ 只回「你有权限回复个锤子」，**窗口继续、倒计时不重置**（记为捣乱者，只回一次）；
        ///     <b>没权限的</b> ⇒ **当场补权限** +「恭喜3P获得权限，快谢谢2P」，窗口到此结束；</item>
        ///   <item>满 10 秒（倒计时从发招募话那一刻算，中间不重置）：
        ///     期间**只有有权限的人在捣乱** ⇒「2P滥用权限3P捣乱小心我撤你们权限」；
        ///     一个人都没回 ⇒「都过去10秒了没人回复啊2P滥用权限小心我撤你权限」。</item>
        /// </list>
        /// <para>
        /// ★ 一次申请只批**一个人**（真的批了才清状态）。
        /// ★ 窗口期间返回 true ⇒ 调用方这一轮**什么都不做**，免得程序自己的话被卷进"谁回复了"的判断。
        /// </para>
        /// <para>
        /// ★ "更靠下"这个判据是安全的：聊天列表旧的在上、新的在下，旧消息只会往上走，
        /// 不可能凭空跑到发起位置的下方去。每次结算完把基准推进到最新那条，同一条不会被反复算。
        /// </para>
        /// </summary>
        /// <returns>true = 正在等 / 刚结算完，这一轮别再处理别的指令。</returns>
        private static async Task<bool> ResolveApplyPendingAsync(
            List<(int X, int Y, double Similarity, string Key)> teammateHits, CancellationToken token)
        {
            if (!_applyPending) return false;

            long now = Environment.TickCount64;
            int bottomY = BottomY(teammateHits);
            bool hasNewMessage = bottomY >= 0 && (_applyStartY < 0 || bottomY > _applyStartY);

            if (hasNewMessage)
            {
                string replierTag = BottomTeammateTag(teammateHits);

                // ★★ 本来就有权限的人来瞎回 ⇒ 只回一句「你有权限回复个锤子」，
                //    **窗口不结束、倒计时也不重置** —— 只有"有人真的拿到权限"或"10 秒走完"才结束。
                if (CanOrder(replierTag))
                {
                    bool firstNuisance = string.IsNullOrEmpty(_applyNuisanceTag);
                    if (firstNuisance) _applyNuisanceTag = replierTag;

                    // ★ 把基准推进到这条消息 —— 否则同一条会被反复当成"新回复"、一直刷「回复个锤子」
                    _applyStartY = bottomY;

                    if (firstNuisance)
                    {
                        long remain = Math.Max(0, ApplyWaitMs - (now - _applyStartTicks));
                        ReplyLog($"[申请权限] {replierTag} 本来就有权限 → 回「{ApplyAlreadyHasRightText}」，" +
                                 $"窗口继续（还剩 {remain}ms）");
                        await SendNoticeToChatAsync(ApplyAlreadyHasRightText, token);
                    }
                    else
                    {
                        ReplyLog($"[申请权限] {replierTag} 也有权限但不重复回话了（捣乱者已记为 {_applyNuisanceTag}）");
                    }

                    return true;   // 继续等
                }

                // ★ 没权限的人回复 ⇒ 当场补权限，窗口到此结束（一次申请只批一个人）
                string fromTag = _applyFromTag;
                ResetApplyPending();

                GrantCommandPermission(replierTag);
                string grantedText = string.Format(ApplyGrantedFormat, replierTag, fromTag);
                ReplyLog($"[申请权限] 给 {replierTag} 补权限（批准人 {fromTag}）→ 回「{grantedText}」");
                await SendNoticeToChatAsync(grantedText, token);

                return true;
            }

            if (now - _applyStartTicks >= ApplyWaitMs)
            {
                string fromTag = _applyFromTag;
                string nuisanceTag = _applyNuisanceTag;
                ResetApplyPending();

                bool onlyNuisance = !string.IsNullOrEmpty(nuisanceTag);
                string text = onlyNuisance
                    ? string.Format(ApplyNuisanceFormat, fromTag, nuisanceTag)   // 「2P滥用权限3P捣乱小心我撤你们权限」
                    : string.Format(ApplyNoReplyFormat, fromTag);                // 「都过去10秒了没人回复啊2P…」

                ReplyLog($"[申请权限] 10 秒到（{(onlyNuisance ? $"{nuisanceTag} 只捣乱、没人真的来要" : "一个人都没回")}）" +
                         $"→ 回「{text}」");
                await SendNoticeToChatAsync(text, token);
                return true;
            }

            // 还在等：这一轮什么都不做
            return true;
        }

        /// <summary>
        /// ★★★ 队友在聊天里**点名要某个世界权限**（三张模板：不允许加入 / 直接加入 / 确认后可加入）：
        /// <list type="number">
        ///   <item>当前**已经是**它 ⇒ 只回一句「当前已经是XXX」，不动权限；</item>
        ///   <item>不是 ⇒ 先发「我这就去把世界权限改成XXX」，然后**真的去改**，改完回到聊天界面。</item>
        /// </list>
        /// <para>
        /// ★ 通知发不出去也照改 —— 把权限改对才是目的（和人数达标那套通知的处理一致）。
        /// ★ 改完 <see cref="_knownPermission"/> 会更新，队友之后再问「世界权限」就是新值。
        /// </para>
        /// </summary>
        private static async Task HandlePermissionRequestAsync(
            WorldPermission target, string contentKey, string senderTag, int teammateY, int ownY,
            CancellationToken token)
        {
            // ★★★ 没勾「指令权限」的队友：只能得到一句拒绝，**权限一个字都不动**
            //      （连"当前已经是XXX"都不答 —— 那也算是答应了这条指令）。
            if (!CanOrder(senderTag))
            {
                await RejectCommandAsync(NoRightToChangePermissionText, contentKey, senderTag, teammateY, token);
                return;
            }

            string permText = WorldPermissionText(target);
            bool already = _knownPermission == target;

            string text = already ? "当前已经是" + permText : "我这就去把世界权限改成" + permText;

            // 和其它自动回复共用一个间隔闸（只有"自己消息检测不到"时才有值，防每 500ms 连发）
            long now = Environment.TickCount64;
            int gap = ownY >= 0 ? AutoReplyMinGapMs : AutoReplyCooldownMs;
            if (gap > 0 && now - _lastAutoReplySentTicks < gap) return;

            ReplyLog($"判定要回复：队友 y={teammateY}「{ShortTemplateName(contentKey)}」" +
                     $"> 自己 y={(ownY < 0 ? "无" : ownY.ToString())} → 回「{text}」" +
                     $"（已知权限 {WorldPermissionText(_knownPermission)}，" +
                     $"{(already ? "不用改" : "改完再回聊天")}）");

            bool sent = await SendNoticeToChatAsync(text, token);
            if (sent)
            {
                _lastAutoReplySentTicks = Environment.TickCount64;
                _replyPendingSinceTicks = 0;
            }
            else
            {
                ReplyLog("权限请求的通知没发出去（输入框没焦点）→ 仍然继续改权限");
            }

            if (already) return;   // 已经是它了，不用动权限

            ReplyLog($"[权限] 开始把世界权限改成 {permText}");
            bool ok = await ChangeWorldPermissionAsync(target, token);
            ReplyLog(ok
                ? $"[权限] 世界权限已改成 {permText}，已回到聊天界面"
                : $"[权限] 世界权限没改成 {permText}（当前已知 {WorldPermissionText(_knownPermission)}）");
        }

        /// <summary>
        /// ★★★ 队友在聊天里发「换号」（2026-10-17 用户定）：
        /// <list type="number">
        ///   <item>先播报「<b>我去换号了下一个是「名字：UID」</b>」（下一个号由上层开局时传进来）；</item>
        ///   <item>换号前**把世界权限弄成「确认后可加入」** —— `_knownPermission` 已经是它就跳过，不去白跑大厅；</item>
        ///   <item>交给上层 <see cref="_switchRequested"/> 真正换号（退游戏 → 下一个账号 → 重新登录）。</item>
        /// </list>
        /// <para>★ 播报发不出去也照换 —— 换号是目的，队友知不知道是次要的（和达标通知一致）。</para>
        /// </summary>
        private static async Task HandleSwitchRequestAsync(
            string contentKey, string senderTag, int teammateY, int ownY, CancellationToken token)
        {
            // ★★★ 没勾「指令权限」的队友：只回一句拒绝，**不换号、也不顺手改权限**。
            if (!CanOrder(senderTag))
            {
                await RejectCommandAsync(NoRightToSwitchText, contentKey, senderTag, teammateY, token);
                return;
            }

            string text = string.IsNullOrEmpty(_nextAccountText)
                ? "我去换号了"
                : "我去换号了下一个是「" + _nextAccountText + "」";

            // 和其它自动回复共用一个间隔闸（只有"自己消息检测不到"时才有值，防每 500ms 连发）
            long now = Environment.TickCount64;
            int gap = ownY >= 0 ? AutoReplyMinGapMs : AutoReplyCooldownMs;
            if (gap > 0 && now - _lastAutoReplySentTicks < gap) return;

            ReplyLog($"判定要回复：队友 y={teammateY}「{ShortTemplateName(contentKey)}」" +
                     $"> 自己 y={(ownY < 0 ? "无" : ownY.ToString())} → 回「{text}」（随后换号）");

            bool sent = await SendNoticeToChatAsync(text, token);
            if (sent)
            {
                _lastAutoReplySentTicks = Environment.TickCount64;
                _replyPendingSinceTicks = 0;
            }
            else
            {
                ReplyLog("换号播报没发出去（输入框没焦点）→ 仍然继续换号");
            }

            // ★★ 换号之前必须让世界权限停在「确认后可加入」
            //    （记录里已经是它就不用去开大厅，省十几秒）。
            if (_knownPermission != WorldPermission.ConfirmJoin)
            {
                ReplyLog($"[换号] 换号前先把世界权限改成确认后加入（当前 {WorldPermissionText(_knownPermission)}）");
                bool ok = await ChangeWorldPermissionAsync(WorldPermission.ConfirmJoin, token);
                ReplyLog(ok
                    ? "[换号] 世界权限已改成确认后加入"
                    : $"[换号] 世界权限没改成确认后加入（当前已知 {WorldPermissionText(_knownPermission)}）→ 继续换号");
            }
            else
            {
                ReplyLog("[换号] 世界权限已经是确认后加入 → 不用去改");
            }

            // ★ 交给上层真正换号（退当前号 → 挑下一个未置底的账号 → 重新走登录流程）
            if (_switchRequested != null)
            {
                ReplyLog("[换号] 移交给上层：开始换下一个账号");
                await _switchRequested("队友在聊天里要求换号");
                _handedOff = true;
            }
            else
            {
                ReplyLog("[换号] 没有换号出口（不在分组登录流程里）→ 无法换号");
            }
        }

        /// <summary>
        /// ★★ 真的去把「世界权限」改成 <paramref name="target"/>，做完再回聊天界面：
        /// 退出聊天（否则 F2 会被输入框吃掉）→ F2 开联机大厅（失败按 <see cref="MaxLobbyOpenAttempts"/> 重试）
        /// → 切权限 → 关大厅 → 回主界面 → Enter 回聊天。
        /// <para>★ 只有真的切成功了才更新 <see cref="_knownPermission"/>（失败保持原值，别撒谎）。</para>
        /// </summary>
        private static async Task<bool> ChangeWorldPermissionAsync(WorldPermission target, CancellationToken token)
        {
            // ★★ 输入框还占着焦点时，F2 会被聊天当成"正在输入"吃掉 —— 必须先退出聊天界面
            await ReturnToMainAsync(token);

            bool opened = false;
            int current = -1;
            for (int attempt = 1; attempt <= MaxLobbyOpenAttempts && !opened; attempt++)
            {
                (opened, current) = await OpenLobbyAsync(token);
                if (opened) break;

                Debug.WriteLine($"[UID/登录后] 第 {attempt}/{MaxLobbyOpenAttempts} 次打开联机大厅失败 → ESC 退一层后重试");
                SimulationService.PressEscape();
                await Task.Delay(AfterPressDelayMs, token);
            }

            if (!opened)
            {
                SetLine2("打开联机界面失败，未修改世界权限");
                return false;
            }

            bool ok = current >= 0 && await SwitchPermissionAsync(target, current, token);
            SetLine2(ok ? $"世界权限已改为{WorldPermissionText(target)}" : "世界权限切换未完成");
            if (ok) _knownPermission = target;

            await CloseLobbyAsync(token);

            // 回主界面 → 按 Enter 回聊天，之后的监控一切照旧
            bool atMain = AccountLoginFlow.Detect(MainRegionKey, MainTemplateKey).detected
                          || await ReturnToMainAsync(token);

            if (atMain)
            {
                SimulationService.PressEnter();
                await Task.Delay(AfterPressDelayMs, token);

                bool back = await WaitDetectAsync(ChatRegionKey, ChatTemplateKey, token, "聊天界面");
                Debug.WriteLine(back
                    ? "[UID/登录后] 已回到聊天界面"
                    : "[UID/登录后] 没能回到聊天界面 → 交给监控循环按 Enter 处理");
            }

            return ok;
        }

        /// <summary>把「最新消息内容」的命中拼成 <c>救命！(0.93)</c> 这种串，给日志看。</summary>
        private static string ContentList(List<(int X, int Y, double Similarity, string Key)> hits)
        {
            if (hits.Count == 0) return "无";
            var sb = new System.Text.StringBuilder();
            foreach (var h in hits)
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(ShortTemplateName(h.Key)).Append('(').Append(h.Similarity.ToString("F2")).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>
        /// ★★ 自动回复的**计时日志**：既打 <c>Debug.WriteLine</c>，也**追加**到
        /// exe 同目录的 <c>聊天自动回复.txt</c>（用 txt —— 双击就能用记事本打开，不用选程序）。
        /// <para>为什么落盘：<c>Debug.WriteLine</c> 直接运行 exe 时<b>看不见</b>（要挂 DebugView），
        /// 而这个耗时问题必须能翻记录慢慢看。</para>
        /// </summary>
        private static void ReplyLog(string text)
        {
            Debug.WriteLine("[UID/登录后] " + text);
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "聊天自动回复.txt"),
                    $"{DateTime.Now:HH:mm:ss.fff}  {text}{System.Environment.NewLine}");
            }
            catch
            {
                // 连日志都写不了就算了 —— 不能因为记日志再出问题
            }
        }

        /// <summary>把一组命中的 y 排好序拼成字符串（最多留最下面 6 个），给计时日志看"消息堆在哪"。</summary>
        private static string YList(List<(int X, int Y, double Similarity, string Key)> hits)
        {
            var ys = new List<int>();
            foreach (var h in hits) ys.Add(h.Y);
            ys.Sort();
            if (ys.Count > 6) ys = ys.GetRange(ys.Count - 6, 6);
            return string.Join(",", ys);
        }

        /// <summary>★ 上一次真的把自动回复发出去的时刻（冷却用；★ 新一轮监控开始时清零）。</summary>
        private static long _lastAutoReplySentTicks;

        /// <summary>★ 让路开始的时刻（诊断日志用：看让路持续了多久）。</summary>
        private static long _userWaitSinceTicks;

        /// <summary>★ 上一次打「让路中」诊断日志的时刻（每秒一条，别刷屏）。</summary>
        private static long _lastUserWaitLogTicks;

        /// <summary>
        /// ★★ 计时日志用：**第一次判定"需要回复"**的时刻（0 = 现在不需要回复）。
        /// <para>
        /// 从它到"真的发出去"之间的差值，就是用户体感到的"等了多久"。
        /// 判定不再成立（自己变成最新 / 队友没消息了）时清零；**冷却等待期间不清零**
        /// —— 那样记到的才是完整的等待时间。
        /// </para>
        /// </summary>
        private static long _replyPendingSinceTicks;

        /// <summary>★ 计时日志的两个节流字段：上次打日志的时刻 / 上次打出来的两个 y。</summary>
        private static long _lastChatScanLogTicks;
        private static int _lastLogTeammateY = -1;
        private static int _lastLogOwnY = -1;

        /// <summary>
        /// ★★ 聊天两个区域**这一轮从哪个 y 开始截**（屏幕坐标；0 = 用区域表里的原始上边界）。
        /// <para>
        /// 由上一轮「最下面那条消息」的 y 决定 —— 上面的旧消息不截进去，截图更小、匹配更快。
        /// 两个区域**共用**它，所以不管最新的是自己还是队友，两边都对齐到同一个起点。
        /// </para>
        /// <para>★ 某轮两个区域都 0 命中（聊天滚动 / 清空 / 位置整体变了）就归零，下一轮全区域重扫。</para>
        /// </summary>
        private static int _chatScanTopY;

        // ==================== 「申请权限」的 10 秒窗口 ====================

        /// <summary>正在等「谁没权限」的回复（false = 当前没有进行中的申请）。</summary>
        private static bool _applyPending;

        /// <summary>发起这次申请的**有权限那位**（2P / 3P / 4P）——「快谢谢他」要用。</summary>
        private static string _applyFromTag = "";

        /// <summary>开始等待的时刻。</summary>
        private static long _applyStartTicks;

        /// <summary>开始等待那一刻「队友消息」最下面那条的 y（0/-1 = 当时一条都没有）。</summary>
        private static int _applyStartY = -1;

        /// <summary>
        /// 窗口内**第一个"本来就有权限还来回复"**的人（捣乱者，2P/3P/4P；空 = 没有）。
        /// <para>★ 这种人只回一句「你有权限回复个锤子」，**窗口不结束、倒计时也不重置** ——
        /// 只有"有人真的拿到权限"或"10 秒走完"才能结束。</para>
        /// </summary>
        private static string _applyNuisanceTag = "";

        /// <summary>
        /// ★★ 给某个 P 位补上「指令权限」时，通知窗口把对应的复选框勾上（参数 <c>"2P" / "3P" / "4P"</c>）。
        /// <para>实现在 UI 侧（<c>LoginConfirmControl</c>），内部自己切回 UI 线程 —— 这边只管喊一声。</para>
        /// </summary>
        private static Action<string>? _grantPermissionTo;

        /// <summary>
        /// 注册「把某个 P 位的指令权限勾上」的回调（窗口创建时调一次）。
        /// 传 <c>null</c> 可解除（窗口销毁时用，免得留着已死的控件引用）。
        /// </summary>
        public static void SetGrantPermissionHandler(Action<string>? handler)
            => _grantPermissionTo = handler;

        /// <summary>
        /// ★★ 人数检测的可视化：把「联机队友」竖条（主界面右侧那一列）里**这一轮检测到的所有目标**
        /// 画到屏幕上 —— 每个标记带序号、模板名（<c>2P队友</c> 这类）、屏幕坐标、匹配度。
        /// <para>★ 和数人是同一时刻做的（<c>UpdatePlayerCount</c>），所以屏幕上画的跟"数出来几个人"是一回事。</para>
        /// </summary>
        private static void MarkTeammateCountTargets()
        {
            long now = Environment.TickCount64;
            if (now - _lastTeammateMarkTicks < ChatMsgMarkIntervalMs) return;
            _lastTeammateMarkTicks = now;

            DrawHits(AccountLoginFlow.DetectAll(TeammateRegionKey, TeammateTemplateKeys),
                TeammateCountMarkPrefix, "联机队友", ref _lastTeammateHitCount);
        }

        /// <summary>模板 key（<c>其他模版_联机状态_2P队友</c>）→ 短名（<c>2P队友</c>）：取最后一个下划线之后的部分。</summary>
        private static string ShortTemplateName(string templateKey)
        {
            if (string.IsNullOrEmpty(templateKey)) return "";
            int i = templateKey.LastIndexOf('_');
            return i >= 0 && i < templateKey.Length - 1 ? templateKey.Substring(i + 1) : templateKey;
        }

        /// <summary>
        /// 数「联机队友」区域里三张模板命中了几张 —— 命中数就是队友数。
        /// </summary>
        private static int CountTeammates()
        {
            int count = 0;
            foreach (var key in TeammateTemplateKeys)
            {
                if (AccountLoginFlow.Detect(TeammateRegionKey, key).detected)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 第三行的文案（拆成三段，方便把「已达标」显示成黄色）：
        /// <list type="bullet">
        ///   <item>没勾人数目标 → <c>当前人数： 1/4</c>（高亮段为空）</item>
        ///   <item>勾了双人 / 三人 / 四人 → <c>目标双人联机：1/4 未达标</c> / <c>… 已达标 …</c></item>
        ///   <item>曾达标过（<paramref name="reached"/>）→ <c>目标四人联机：4/4 <b>已达标</b> 等待所有玩家退出世界</c>；
        ///        人数掉下去之后仍是 <c>已达标</c>，后半句换成 <c>等待所有玩家退出世界后换号</c>。</item>
        ///   <item>没达标 + 单人世界 → 末尾补 <c> 当前处于单人模式</c></item>
        /// </list>
        /// <para>
        /// ★★ <paramref name="reached"/> 是**粘住**的标志：它记的是"本次监控里到过目标人数"，
        /// 不是"此刻够不够人"。所以队友陆续离开、人数往下掉的时候，「已达标」不会被撤销
        /// （用户要的就是这个：满过 4 人 = 已经达标了，剩下的是等所有人退出世界）。
        /// </para>
        /// <para>
        /// ★ <paramref name="world"/> 只在**主界面**更新过（见 <see cref="DetectWorldMode"/>），
        /// 别处沿用上一次的值 —— 状态不会因为"跑到别的界面看不见图标了"就被误判成单人。
        /// </para>
        /// </summary>
        private static (string Prefix, string Highlight, string Suffix) BuildPlayerCountParts(
            int players, int target, bool reached, WorldMode world = WorldMode.Unknown)
        {
            string ratio = $"{players}/{MaxPlayers}";

            // 没勾目标：只有人数，没有达标概念（高亮段空着）
            if (target <= 0)
                return ($"当前人数： {ratio}", "", world == WorldMode.Single ? SingleWorldText : "");

            string name = target switch { 2 => "双人联机", 3 => "三人联机", _ => "四人联机" };

            // 「目标双人联机：1/4 」—— 前缀到人数为止，后面接达标与否
            string prefix = $"目标{name}：{ratio} ";
            string highlight = reached ? ReachedText : UnreachedText;

            // ★ 达标之后就不再显示「当前处于单人模式」了 —— 那时重点是"等所有人退出世界后换号"
            string suffix = reached
                ? (players >= target ? WaitPlayersLeaveText : WaitLeaveThenSwitchText)
                : (world == WorldMode.Single ? SingleWorldText : "");

            return (prefix, highlight, suffix);
        }

        /// <summary>
        /// <see cref="BuildPlayerCountParts"/> 的**纯文本**版（三段直接拼起来）：
        /// 给不需要黄色的场合用（登录过程中的初始第三行 <see cref="BuildWaitingText"/>）。
        /// </summary>
        private static string BuildPlayerCountText(int players, int target, WorldMode world = WorldMode.Unknown)
        {
            var parts = BuildPlayerCountParts(players, target, false, world);
            return parts.Prefix + parts.Highlight + parts.Suffix;
        }

        /// <summary>
        /// 主界面按 <b>F2</b> 打开联机大厅，读出当前「世界权限」是哪一档。
        /// <para>三张模板里<b>任意一个</b>命中 = 进了大厅；<c>Current</c> 是
        /// <see cref="WorldPermTemplateKeys"/> 的下标（<c>-1</c> = 一张都没命中）。</para>
        /// </summary>
        private static async Task<(bool Opened, int Current)> OpenLobbyAsync(CancellationToken token)
        {
            SetLine2("正在打开联机界面");

            SimulationService.PressF2();
            await Task.Delay(AfterPressDelayMs, token);

            // 先确认进了联机大厅（三张模板任意一个命中）
            var hit = await WaitDetectAnyAsync(WorldPermRegionKey, WorldPermTemplateKeys, token);
            if (!hit.detected)
            {
                Debug.WriteLine("[UID/登录后] 打开联机界面：未检测到「世界权限」");
                return (false, -1);
            }

            Debug.WriteLine($"[UID/登录后] 已进入联机大厅，当前世界权限 = {hit.hitKey}");
            return (true, IndexOfTemplateKey(hit.hitKey));
        }

        /// <summary>
        /// 把「世界权限」切到 <paramref name="target"/> ——
        /// 「点开下拉 → 选一项 → 等 <see cref="AfterSwitchDelayMs"/> → 复查」，不对就立刻重切，
        /// 最多 <see cref="MaxPermissionSwitch"/> 轮。
        /// </summary>
        private static async Task<bool> SwitchPermissionAsync(
            WorldPermission target, int current, CancellationToken token)
        {
            int want = (int)target - 1;   // 枚举值从 1 开始，数组下标从 0 开始
            if (want < 0 || want >= WorldPermClickKeys.Length) return false;

            // 已经是目标 → 什么都不用做
            if (current == want)
            {
                Debug.WriteLine($"[UID/登录后] 世界权限已是「{WorldPermClickKeys[want]}」，无需切换");
                return true;
            }

            string wantName = WorldPermClickKeys[want];
            string currentName = WorldPermClickKeys[current >= 0 ? current : 0];

            for (int attempt = 1; attempt <= MaxPermissionSwitch; attempt++)
            {
                token.ThrowIfCancellationRequested();

                Debug.WriteLine($"[UID/登录后] 第 {attempt}/{MaxPermissionSwitch} 次切换世界权限 → {wantName}（当前 {currentName}）");
                SetLine2($"正在切换世界权限-{wantName}");

                // 点「世界权限」区域把下拉展开
                AccountLoginFlow.ClickRegionCenter(WorldPermRegionKey);
                await Task.Delay(AfterOpenDropdownMs, token);

                // 点对应那一项
                AccountLoginFlow.ClickPoint(wantName);

                // ★ 切换完等 500ms 就复查一次：对上了就成功，没对上立刻进入下一轮重切
                var recheck = await RecheckPermissionAsync(token);
                if (!recheck.detected)
                {
                    Debug.WriteLine($"[UID/登录后] 第 {attempt} 次切换后复查：连「世界权限」都检测不到了，准备重切");
                    continue;
                }

                current = IndexOfTemplateKey(recheck.hitKey);
                currentName = WorldPermClickKeys[current];
                Debug.WriteLine($"[UID/登录后] 第 {attempt} 次切换后复查：当前 = {currentName}（{recheck.similarity * 100:F1}%）");

                if (current == want)
                {
                    Debug.WriteLine($"[UID/登录后] 世界权限已切换为 {wantName}，切换成功");
                    return true;
                }
            }

            Debug.WriteLine($"[UID/登录后] 世界权限切换 {MaxPermissionSwitch} 次仍未到目标（{wantName}），放弃");
            return false;
        }

        /// <summary>按 <b>ESC</b> 关掉联机大厅回到主界面（成功与否都往下走，界面不对的话监控循环会重新认）。</summary>
        private static async Task CloseLobbyAsync(CancellationToken token)
        {
            SetLine2("正在返回主界面");
            SimulationService.PressEscape();
            await Task.Delay(AfterPressDelayMs, token);
        }

        /// <summary>
        /// ★★ 把界面退回到**主界面**（不在聊天界面就一直按 ESC —— 联机聊天里第一次 ESC
        /// 通常只是取消输入框焦点，要多按一下才真的把聊天关掉）。
        /// <para>
        /// 为什么必须走这一步：**开联机大厅（F2）只能在主界面按**。聊天输入框占着焦点时
        /// F2 会被它当成"正在输入"吃掉 —— 表现就是"按了 F2，大厅纹丝不动"。
        /// 所以凡是"从聊天里去 P2 / 联机大厅"的动作，都得先回到主界面。
        /// </para>
        /// </summary>
        /// <returns>结束时是不是真的站在主界面上。</returns>
        private static async Task<bool> ReturnToMainAsync(CancellationToken token)
        {
            for (int attempt = 1; attempt <= MaxLeaveChatAttempts; attempt++)
            {
                if (AccountLoginFlow.Detect(MainRegionKey, MainTemplateKey).detected)
                    return true;

                // 已经不在聊天界面了（也还没识别出主界面，比如停在过渡画面）→ 别再按了，交给循环
                if (!AccountLoginFlow.Detect(ChatRegionKey, ChatTemplateKey).detected)
                    return false;

                Debug.WriteLine($"[UID/登录后] 第 {attempt} 次按 ESC 退出聊天界面（要回主界面才能按 F2）");
                SimulationService.PressEscape();
                await Task.Delay(AfterPressDelayMs, token);
            }

            return AccountLoginFlow.Detect(MainRegionKey, MainTemplateKey).detected;
        }

        /// <summary>
        /// 动作三：主界面按 F2 打开联机大厅，检测到「世界权限」三张模板的**任意一个**即算进了大厅；
        /// 如果勾选了目标权限且当前不是它 → 点开「世界权限」下拉 → 点对应项 → 复查。
        /// </summary>
        private static async Task OpenMultiplayerAsync(WorldPermission target, CancellationToken token)
        {
            var (opened, current) = await OpenLobbyAsync(token);
            if (!opened)
            {
                SetLine2("打开联机界面失败，请手动按 F2");
                return;
            }

            // 没指定目标 → 进大厅就够了
            if (target == WorldPermission.None)
            {
                SetLine2("联机界面已打开-TAB隐藏");
                return;
            }

            bool ok = current >= 0 && await SwitchPermissionAsync(target, current, token);
            SetLine2(ok
                ? "联机界面已打开-TAB隐藏"
                : $"世界权限切换未完成（当前 {WorldPermClickKeys[current >= 0 ? current : 0]}）");
        }

        /// <summary>
        /// ★★ 登录进游戏之后（第一次站到主界面时）**看一眼世界权限要不要动**（2026-10-18 用户改）：
        /// <list type="bullet">
        ///   <item><b>人数已经满了</b>（队友数 + 自己 ≥ 目标）⇒ 谁也进不来了，「直接加入」没有意义，
        ///         该是「确认后可加入」；**如果已经是它，F2 打开看一眼就直接退出，一个字都不改**；</item>
        ///   <item>人数还没满 ⇒ 确认是「直接加入」（不是就改过来），让别人能进来。</item>
        /// </list>
        /// <para>
        /// ★ 为什么要这么改：以前是"一律先改成直接加入 → 人数够了再改成确认后可加入"。
        /// 登录进一个**本来就满员**的世界时，等于白跑两趟大厅、来回切两次。
        /// </para>
        /// <para>
        /// ★ 它和用户勾选的「打开联机大厅 + 指定权限」是<b>两码事</b>：这一项是流程自带的，
        /// 无条件做一次（只做一次 —— 之后用户自己改了权限，程序不该再抢回去）。
        /// </para>
        /// </summary>
        /// <param name="alreadyFull">
        /// 进游戏那一刻**人数就已经达标**（由调用方在主界面先数好 —— 进了大厅就数不到人了）。
        /// </param>
        private static async Task<bool> EnsureWorldPermissionAsync(bool alreadyFull, CancellationToken token)
        {
            var (opened, current) = await OpenLobbyAsync(token);
            if (!opened || current < 0)
            {
                SetLine2("打开联机界面失败，未能确认世界权限");
                return false;
            }

            // current 是 WorldPermTemplateKeys 的下标（= 枚举值 - 1）：
            // 0 不允许加入 / 1 直接加入 / 2 确认后可加入
            const int confirmJoinIdx = (int)WorldPermission.ConfirmJoin - 1;

            // ★★ 人已满 + 已经是「确认后可加入」⇒ 什么都不用改，直接退出去开聊天
            if (alreadyFull && current == confirmJoinIdx)
            {
                await CloseLobbyAsync(token);
                _knownPermission = WorldPermission.ConfirmJoin;
                SetLine2("人数已满，世界权限保持确认后可加入");
                Debug.WriteLine("[UID/登录后] 人数已满且权限已是「确认后可加入」→ 不做任何修改");
                return true;
            }

            // 人满了却没停在「确认后可加入」⇒ 改成它；人没满 ⇒ 确认是「直接加入」
            WorldPermission target = alreadyFull ? WorldPermission.ConfirmJoin : WorldPermission.DirectJoin;

            bool ok = current >= 0 && await SwitchPermissionAsync(target, current, token);
            await CloseLobbyAsync(token);

            // ★ 改成功了才记进"已知值"（聊天里有人问「世界权限」时按它回答）
            if (ok) _knownPermission = target;

            string name = WorldPermissionText(target);
            SetLine2(ok ? $"世界权限已确认为{name}" : $"世界权限未改成{name}");
            Debug.WriteLine(ok
                ? $"[UID/登录后] 世界权限已确认为「{name}」"
                : $"[UID/登录后] 世界权限改为「{name}」失败");
            return ok;
        }

        /// <summary>
        /// ★ 人数**第一次达标**时的一整套动作（用户定死的顺序，别调换）：
        /// <list type="number">
        ///   <item>在聊天里发一句「<see cref="ReachedNoticeText"/>」—— 让队友知道下面要改权限；</item>
        ///   <item>把「世界权限」改成 <see cref="WorldPermission.ConfirmJoin"/>（确认后可加入）；</item>
        ///   <item>再**回到聊天界面**，之后的监控一切照旧。</item>
        /// </list>
        /// </summary>
        private static async Task HandleReachedAsync(CancellationToken token)
        {
            SetLine2("人数已达标，正在通知队友");

            // ① 聊天界面没开就先打开（走到这儿时多半已经在聊天界面 —— 人是靠那里的队友竖条数出来的）
            bool chatOpen = AccountLoginFlow.Detect(ChatRegionKey, ChatTemplateKey).detected;
            if (!chatOpen)
            {
                // ★ 按 Enter 打开聊天，最多试 2 次：主界面上第一次 Enter 有时只被当成"关掉别的面板"，
                //   一次失败就放弃的话通知就永远发不出去。
                for (int attempt = 1; attempt <= 2 && !chatOpen; attempt++)
                {
                    SimulationService.PressEnter();
                    await Task.Delay(AfterPressDelayMs, token);
                    chatOpen = await WaitDetectAsync(ChatRegionKey, ChatTemplateKey, token, "聊天界面");
                    Debug.WriteLine($"[UID/登录后] 第 {attempt} 次尝试打开聊天界面：{(chatOpen ? "成功" : "没打开")}");
                }

                if (!chatOpen)
                    Debug.WriteLine("[UID/登录后] 人数已达标，但聊天界面没能打开 → 跳过发消息，直接去改权限");
            }

            // ② 停在「当前队伍」频道 + 输入框有焦点，把通知发出去
            if (chatOpen)
            {
                try
                {
                    await EnsureTeamChannelAsync(token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UID/登录后] 发送前确认「当前队伍」频道出错：{ex.Message}");
                }

                // ★★★ 话术看"到底改不改"（2026-10-18 用户定）：权限记录**已经是**「确认后可加入」时
                //     再喊"我这就去把世界权限改成确认后可加入"就是瞎说 —— 根本没去改。
                //     那种情况只告诉队友"人已经满了"。
                bool willChangePermission = _knownPermission != WorldPermission.ConfirmJoin;
                string reachedNotice = willChangePermission ? ReachedNoticeText : ReachedNoticeAlreadyText;

                // ★ 发送结果要**看得见**：之前的版本发不出去静悄悄就过去了
                //   （表现为"人数达标的通知压根没出现在聊天里"）。
                bool sent = await SendNoticeToChatAsync(reachedNotice, token);
                if (sent)
                {
                    SetLine2(willChangePermission
                        ? "已通知队友：人数已达标修改世界权限-确认后可加入"
                        : "已通知队友：世界权限已是确认后可加入");
                    Debug.WriteLine($"[UID/登录后] 达标通知已发送到聊天：{reachedNotice}");
                }
                else
                {
                    SetLine2("达标通知发送失败：聊天输入框没有焦点");
                    Debug.WriteLine("[UID/登录后] 达标通知发送失败（输入框没焦点）→ 继续去改世界权限");
                }
            }

            // ③ 改世界权限 → **确认后可加入**
            //    ★★★ 2026-10-18 用户要求：**记录里已经是「确认后可加入」就一步都不用动** ——
            //       别再去开联机大厅看一眼。进游戏那一步已经把权限调好了（`EnsureWorldPermissionAsync`），
            //       满员时本来就没必要再跑一趟；而且此刻还在聊天界面，连"退出去再回来"都省了。
            if (_knownPermission == WorldPermission.ConfirmJoin)
            {
                SetLine2("世界权限已是确认后可加入，无需修改");
                Debug.WriteLine("[UID/登录后] 人数达标：世界权限记录已是「确认后可加入」→ 跳过开联机大厅");
                return;
            }

            //    ★★ 先把聊天界面退掉：输入框还占着焦点时，F2 会被它当成"正在输入"吃掉，
            //       联机大厅压根开不出来（2026-10-17 实测的表现：消息发出去了，之后毫无动静）。
            //    ★ 逻辑上这也是必须的 —— 达标之后改权限，为的是让**路人**再也进不来：
            //      新来的人要发申请，放着不点就进不来，队伍里的人不受影响。
            await ReturnToMainAsync(token);

            bool opened = false;
            int current = -1;
            for (int attempt = 1; attempt <= MaxLobbyOpenAttempts && !opened; attempt++)
            {
                (opened, current) = await OpenLobbyAsync(token);
                if (opened) break;

                Debug.WriteLine($"[UID/登录后] 第 {attempt}/{MaxLobbyOpenAttempts} 次打开联机大厅失败 → ESC 退一层后重试");
                SimulationService.PressEscape();
                await Task.Delay(AfterPressDelayMs, token);
            }

            if (!opened)
            {
                SetLine2("打开联机界面失败，未修改世界权限");
                return;
            }

            bool ok = current >= 0 && await SwitchPermissionAsync(WorldPermission.ConfirmJoin, current, token);
            SetLine2(ok ? "世界权限已改为确认后可加入" : "世界权限切换未完成");

            // ★ 改成功了才记进"已知值"（聊天里有人问「世界权限」时按它回答）
            if (ok) _knownPermission = WorldPermission.ConfirmJoin;

            // ④ 关掉大厅回到主界面，确认真的站在主界面之后再按 Enter 回聊天
            await CloseLobbyAsync(token);
            bool atMain = AccountLoginFlow.Detect(MainRegionKey, MainTemplateKey).detected
                          || await ReturnToMainAsync(token);

            if (atMain)
            {
                SimulationService.PressEnter();
                await Task.Delay(AfterPressDelayMs, token);

                bool back = await WaitDetectAsync(ChatRegionKey, ChatTemplateKey, token, "聊天界面");
                Debug.WriteLine(back
                    ? "[UID/登录后] 已回到聊天界面，继续人数监控"
                    : "[UID/登录后] 没能回到聊天界面 → 交给监控循环按 Enter 处理");
            }
        }

        /// <summary>
        /// ★★ 把一句话发到聊天里（达标题通知）—— **三项前置确认一个都不能少**
        /// （用户 2026-10-17 17:07 定死，顺序也不能反）：
        /// <list type="number">
        ///   <item>**在聊天界面**里（<c>消息发送</c> 区域命中）—— 不在就别点输入框，
        ///        那会点到别的界面上去；</item>
        ///   <item>**频道是「当前队伍」**（<c>当前队伍</c> 区域命中）—— 发错频道等于没发；</item>
        ///   <item>**输入框是纯白**（<see cref="IsChatInputFocused"/>，容差 0）—— 只有纯白才是真的
        ///        拿到焦点、能打字；不是白就 <see cref="FocusChatInputAsync"/> 点它
        ///        （**点击**，最多 <see cref="MaxChatFocusAttempts"/> 次）。</item>
        /// </list>
        /// 三项都过 → **全选 + 退格**清掉输入框里残留的旧文字 → 粘贴 + 回车发送。
        /// <para>★ 聚焦那一步绝不能用 Enter：输入框里有残留文字时，那一下回车会把它直接发出去。</para>
        /// </summary>
        /// <returns>确实发出去了返回 true（三项有任一不满足 / 出异常都返回 false，调用方可以重试）。</returns>
        private static async Task<bool> SendNoticeToChatAsync(string text, CancellationToken token)
        {
            try
            {
                // ① ★ 必须在聊天界面（不在就别乱点 —— 那个坐标在别的界面上是别的东西）
                if (!AccountLoginFlow.Detect(ChatRegionKey, ChatTemplateKey).detected)
                {
                    Debug.WriteLine("[UID/登录后] 不在聊天界面 → 本次不发送（避免在别的界面上乱点输入框）");
                    return false;
                }

                // ② ★ 频道必须是「当前队伍」（发到别的频道等于白发）
                if (!AccountLoginFlow.Detect(TeamChannelRegionKey, TeamChannelTemplateKey).detected)
                {
                    Debug.WriteLine("[UID/登录后] 频道不在「当前队伍」 → 本次不发送");
                    return false;
                }

                // ③ ★★ 聚焦用**回车**（<see cref="FocusChatInputAsync"/>）。这里只"尽力"：
                //    即使按完还是没变白也**继续往下走** —— 以前"始终不白就放弃发送"，
                //    表现就是通知静悄悄发不出去；现在照发，反正紧接着会清空。
                bool focused = await FocusChatInputAsync(token);
                if (!focused)
                    Debug.WriteLine("[UID/登录后] 按了回车输入框仍未变白 → 照常清空并发送（不再因此放弃）");

                // ★★ 不管刚才有没有焦点、输入框里到底有没有东西，都**清空一次**防万一：
                //    回车聚焦那一下有可能把残留文字直接发出去，全选 + 退格能保证发出去的只有这一句话。
                SimulationService.SelectAllText();
                await Task.Delay(UidClearDelayMs, token);
                SimulationService.PressBackspace();
                await Task.Delay(UidClearDelayMs, token);

                Debug.WriteLine($"[UID/登录后] 聊天输入框已聚焦 → 粘贴并发送：{text}");
                SimulationService.PasteText(text);
                await Task.Delay(UidPasteToSendMs, token);

                SimulationService.PressEnter();     // 发送也是回车
                await Task.Delay(UidAfterSendMs, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录后] 发送聊天通知失败：{ex.Message}");
                return false;
            }
        }

        #endregion

        #region 内部辅助

        /// <summary>
        /// 等游戏真正进到主界面（左上角派蒙头像出现）。
        /// <para>★ 不等的话后面的 ESC / Enter / F2 都按在加载中的画面上，白费一次。</para>
        /// </summary>
        private static async Task<bool> WaitForMainScreenAsync(CancellationToken token)
        {
            SetLine2("等待进入主界面");

            long start = Environment.TickCount64;
            while (Environment.TickCount64 - start < WaitMainTimeoutMs)
            {
                token.ThrowIfCancellationRequested();

                // 游戏不在焦点就等着（和登录流程一致：焦点不在这里，按键和识别都没意义）
                if (GameFocusProvider.IsGameFocused())
                {
                    var r = AccountLoginFlow.Detect(MainRegionKey, MainTemplateKey);
                    if (r.detected)
                    {
                        Debug.WriteLine($"[UID/登录后] 已检测到主界面（{r.similarity * 100:F1}%）");
                        return true;
                    }
                }

                await Task.Delay(PollIntervalMs, token);
            }
            return false;
        }

        /// <summary>在超时前反复检测某个区域的单个模板。</summary>
        private static async Task<bool> WaitDetectAsync(
            string regionKey, string templateKey, CancellationToken token, string logName)
        {
            long start = Environment.TickCount64;
            while (Environment.TickCount64 - start < DetectTimeoutMs)
            {
                token.ThrowIfCancellationRequested();

                if (GameFocusProvider.IsGameFocused())
                {
                    var r = AccountLoginFlow.Detect(regionKey, templateKey);
                    if (r.detected)
                    {
                        Debug.WriteLine($"[UID/登录后] 检测到 {logName}（{r.similarity * 100:F1}%）");
                        return true;
                    }
                }

                await Task.Delay(PollIntervalMs, token);
            }
            return false;
        }

        /// <summary>在超时前反复检测某个区域的一组模板，返回命中的那一个。</summary>
        private static async Task<(bool detected, double similarity, string hitKey)> WaitDetectAnyAsync(
            string regionKey, string[] templateKeys, CancellationToken token)
        {
            long start = Environment.TickCount64;
            while (Environment.TickCount64 - start < DetectTimeoutMs)
            {
                token.ThrowIfCancellationRequested();

                if (GameFocusProvider.IsGameFocused())
                {
                    var r = AccountLoginFlow.DetectAnyOf(regionKey, templateKeys);
                    if (r.detected)
                    {
                        Debug.WriteLine($"[UID/登录后] 检测到 {r.hitKey}（{r.similarity * 100:F1}%）");
                        return r;
                    }
                }

                await Task.Delay(PollIntervalMs, token);
            }
            return (false, 0, string.Empty);
        }

        /// <summary>
        /// ★ 切换「世界权限」之后的复查：等 <see cref="AfterSwitchDelayMs"/> 再做**一次**检测。
        /// <para>
        /// ⛔ 这里**故意不用** <see cref="WaitDetectAnyAsync"/> —— 那个是要等到目标出现或超时为止，
        ///    用在「切换**之后**」就变成了：切换失败时原地干等 10 秒才走下一轮，白白浪费时间。
        ///    切换这种事必须**先切、再看、不对就再切**，靠轮次推进而不是靠等待。
        /// </para>
        /// <para>★ 只做单次检测，所以前置一个焦点等待 —— 焦点不在游戏上时识别结果没有意义。</para>
        /// </summary>
        private static async Task<(bool detected, double similarity, string hitKey)> RecheckPermissionAsync(
            CancellationToken token)
        {
            await Task.Delay(AfterSwitchDelayMs, token);

            // 焦点不在这 → 最多再等 RecheckFocusWaitMs（切到游戏上再检测）
            long start = Environment.TickCount64;
            while (!GameFocusProvider.IsGameFocused() &&
                   Environment.TickCount64 - start < RecheckFocusWaitMs)
            {
                await Task.Delay(PollIntervalMs, token);
            }

            return AccountLoginFlow.DetectAnyOf(WorldPermRegionKey, WorldPermTemplateKeys);
        }

        /// <summary>把命中的模板 key 换算成 <see cref="WorldPermTemplateKeys"/> 的下标；不在其中返回 -1。</summary>
        private static int IndexOfTemplateKey(string templateKey)
            => Array.FindIndex(WorldPermTemplateKeys, k => string.Equals(k, templateKey, StringComparison.Ordinal));

        /// <summary>第二行字幕沿用登录流程的用法：显示当前状态（相同就不重复发）。</summary>
        private static string _lastLine2 = "";

        private static void SetLine2(string text)
        {
            if (text == _lastLine2) return;
            _lastLine2 = text;
            SubtitlePresenter.SetText2(text);
        }

        /// <summary>
        /// 清掉第二行的去重缓存。
        /// <para>★ <see cref="_lastLine2"/> 是 static，跨登录会话保留。新一轮开始时不清，
        /// 这一轮的第一句若和上一轮的最后一句相同，就会被去重逻辑吞掉。</para>
        /// </summary>
        private static void ResetLine2Cache() => _lastLine2 = "";

        #endregion
    }
}
