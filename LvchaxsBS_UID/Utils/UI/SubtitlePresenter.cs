using System;
using System.Diagnostics;

namespace LvchaxsBS_UID.Utils.UI
{
    /// <summary>
    /// 客户区字幕（铺在游戏画面上的那层「头像 + 文字」提示）的转发入口。
    ///
    /// <para>
    /// 真正的显示层在主程序里（<c>SubtitleOverlayService</c>）：一个铺在游戏客户区上的
    /// 分层窗口，带随机头像（空 / 荧）、圆角气泡和淡入淡出。<b>一个进程只能有一个</b>，
    /// 所以本模块不自己建窗口，而是复用宿主那一个 —— 显示风格与钓鱼 / 自动烹饪 / 自动伐木
    /// 完全一致。
    /// </para>
    /// <para>
    /// 模块与宿主之间没有编译期引用，宿主在加载本模块时通过
    /// <see cref="LvchaxsBS_UID.UidPlugin.SetHostSubtitle"/> 把入口注入进来；
    /// 没注入时（脱离宿主单独调试）这里的所有调用都是空操作，不会抛异常。
    /// </para>
    /// <para>
    /// ★ 覆盖层只在<b>游戏窗口处于前台</b>时可见，所以调用前通常要先把游戏窗口激活
    /// （见 <see cref="LvchaxsBS_UID.Utils.GameWindow.GameWindowActivator"/>）。
    /// </para>
    /// </summary>
    public static class SubtitlePresenter
    {
        // 命令表（宿主侧实现在 Services/SubtitleHostBridge.cs，两边靠字符串约定）：
        // 第一行（带头像）
        private const string CmdStart = "subtitle:start";      // 参数：位置坐标 key
        private const string CmdText = "subtitle:text";        // 参数：正文
        private const string CmdRich = "subtitle:rich";        // 参数：string[3] { 前缀, 高亮段, 后缀 }
        private const string CmdSuffix = "subtitle:suffix";    // 参数：后缀
        private const string CmdHide = "subtitle:hide";        // 无参数
        private const string CmdOpacity = "subtitle:opacity";  // 参数：0~100
        // 第二行（显示在第一行下面；默认也带头像，可用 SetAvatarVisible2 关掉）
        private const string CmdStart2 = "subtitle2:start";    // 参数：位置坐标 key
        private const string CmdText2 = "subtitle2:text";      // 参数：正文
        private const string CmdRich2 = "subtitle2:rich";      // 参数：string[3] { 前缀, 高亮段, 后缀 }
        private const string CmdTabLock = "subtitle:tablock";  // 无参数
        private const string CmdTabUnlock = "subtitle:tabunlock"; // 无参数
        private const string CmdHide2 = "subtitle2:hide";      // 无参数
        private const string CmdAvatar2 = "subtitle2:avatar";  // 参数：bool
        // 第三行（联机人数提示）
        private const string CmdStart3 = "subtitle3:start";    // 参数：位置坐标 key
        private const string CmdText3 = "subtitle3:text";      // 参数：正文
        private const string CmdRich3 = "subtitle3:rich";      // 参数：string[3]
        private const string CmdHide3 = "subtitle3:hide";      // 无参数
        private const string CmdAvatar3 = "subtitle3:avatar";  // 参数：bool
        private const string CmdAvatars = "subtitle:avatars";  // 无参数：三行头像重新分配
        // 第四行（用户操作提示：常驻透明，触发即显示）
        private const string CmdStopAllFuncs = "func:stopall";   // 无参数：停掉宿主里所有正在运行的功能（含自动清药）
        private const string CmdPrewarm4 = "subtitle4:prewarm"; // 无参数：预建窗 + 保持纯透明常驻
        private const string CmdText4 = "subtitle4:text";       // 参数：正文（显示出来）
        private const string CmdHide4 = "subtitle4:hide";       // 无参数：只归零不透明度（不销毁）

        private static Action<string, object?>? _host;

        /// <summary>宿主注入的入口（命令, 参数）。</summary>
        public static Action<string, object?>? Host
        {
            get => _host;
            set => _host = value;
        }

        /// <summary>宿主是否已注入。false 时下面所有方法都是空操作。</summary>
        public static bool IsAvailable => _host != null;

        private static void Send(string command, object? arg = null)
        {
            Action<string, object?>? host = _host;
            if (host == null) return;

            try
            {
                host(command, arg);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/字幕] 命令 {command} 执行失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始一次显示：重新掷一个随机头像（空 / 荧）、按 <paramref name="positionKey"/> 定位、
        /// 然后显示出来。等价于主程序里
        /// <c>ResetAvatarForNewRun + SetPositionKey + ShowSubtitle</c> 那三连。
        /// </summary>
        /// <param name="positionKey">
        /// 位置坐标 key（定义在共享库 <c>CoordinateFormats</c>，如 <c>登录_字幕显示坐标</c>）。
        /// 传空、或该 key 取不到坐标时，字幕窗口不会被定位 —— 表现是「字幕出现在奇怪的位置」，
        /// 排查时先看这里。
        /// </param>
        public static void StartRun(string positionKey) => Send(CmdStart, positionKey);

        /// <summary>设置字幕正文。须先 <see cref="StartRun"/>，否则会被宿主忽略（未激活状态写入无效）。</summary>
        public static void SetText(string? text) => Send(CmdText, text);

        /// <summary>
        /// 第一行设成三段富文本：<paramref name="highlight"/>（账号名）用高亮色（黄），
        /// 前后两段照常白色。须先 <see cref="StartRun"/>。
        /// </summary>
        public static void SetRichText(string prefix, string highlight, string suffix)
            => Send(CmdRich, new[] { prefix, highlight, suffix });

        /// <summary>在正文后追加后缀（末尾已有则不重复追加），用于「3 / 5」这类进度。</summary>
        public static void AppendSuffix(string? suffix) => Send(CmdSuffix, suffix);

        /// <summary>收起字幕（不关窗口，只是淡出）。</summary>
        public static void Hide() => Send(CmdHide);

        /// <summary>设置气泡底色不透明度（0~100）。</summary>
        public static void SetBackgroundOpacity(int percent) => Send(CmdOpacity, percent);

        // ==================== 第二行（显示在第一行下面） ====================

        /// <summary>
        /// 开始第二行：按 <paramref name="positionKey"/> 定位并显示。
        /// 与第一行<b>共用同一个位置 key</b>，覆盖层会把第二行自动往下偏移。
        /// </summary>
        public static void StartRun2(string positionKey) => Send(CmdStart2, positionKey);

        /// <summary>设置第二行正文。须先 <see cref="StartRun2"/>。</summary>
        public static void SetText2(string? text) => Send(CmdText2, text);

        /// <summary>
        /// 第二行设成三段富文本：<paramref name="highlight"/> 用高亮色（黄），前后两段照常白色。
        /// 须先 <see cref="StartRun2"/>。
        /// </summary>
        public static void SetRichText2(string prefix, string highlight, string suffix)
            => Send(CmdRich2, new[] { prefix, highlight, suffix });

        /// <summary>收起第二行。</summary>
        public static void Hide2() => Send(CmdHide2);

        /// <summary>
        /// 锁住「按 TAB 隐藏字幕」。登录流程运行期间调用 —— 那会儿字幕是进度，不能收
        /// （和自动清药运行时一个道理）。
        /// </summary>
        public static void LockTabHide() => Send(CmdTabLock);

        /// <summary>解开「按 TAB 隐藏字幕」。登录结束（成功 / 失败 / 取消）时调用。</summary>
        public static void UnlockTabHide() => Send(CmdTabUnlock);

        /// <summary>
        /// ★ **停掉宿主里所有正在运行的功能**（钓鱼 / 伐木 / 烹饪 / 清药 / 快速拾取 / 剧情对话）。
        /// <para>有字幕的功能同时只能跑一个：**新的中断旧的**。登录流程开始前调一次，
        /// 免得别的功能还在操作鼠标键盘、并且跟登录抢同一行字幕。</para>
        /// </summary>
        public static void StopAllFunctions() => Send(CmdStopAllFuncs);

        /// <summary>
        /// 设置第二行是否显示头像。登录字幕和自动清药一样两行都带头像，所以传 true。
        /// 覆盖层窗口可能还没建，标志会先存着，建窗时按它生效。
        /// </summary>
        public static void SetAvatarVisible2(bool visible) => Send(CmdAvatar2, visible);

        // ==================== 第三行（联机人数提示） ====================

        /// <summary>
        /// 开始第三行：按 <paramref name="positionKey"/> 定位并显示。与第一行共用同一个位置 key
        /// （覆盖层自己往下偏移两格）。
        /// </summary>
        public static void StartRun3(string positionKey) => Send(CmdStart3, positionKey);

        /// <summary>设置第三行正文。须先 <see cref="StartRun3"/>。</summary>
        public static void SetText3(string? text) => Send(CmdText3, text);

        /// <summary>第三行设成三段富文本：<paramref name="highlight"/> 用高亮色（黄）。</summary>
        public static void SetRichText3(string prefix, string highlight, string suffix)
            => Send(CmdRich3, new[] { prefix, highlight, suffix });

        /// <summary>收起第三行。</summary>
        public static void Hide3() => Send(CmdHide3);

        /// <summary>设置第三行是否显示头像（默认显示）。</summary>
        public static void SetAvatarVisible3(bool visible) => Send(CmdAvatar3, visible);

        /// <summary>
        /// ★ 一次把三行的头像重新分配（空 / 荧）：
        /// 只显示 1 行时随机；2 行时两行不同；3 行时允许两个一样但不能三个都一样。
        /// <para>每次刷新完三行文本后调一次即可（头像不会自己变）。</para>
        /// </summary>
        public static void AssignAvatars() => Send(CmdAvatars);

        // ==================== 第四行（用户操作提示） ====================

        /// <summary>
        /// ★ **预热**第四行：窗口先建好并保持**纯透明常驻**（看不见、点得到不到 —— 覆盖层是鼠标穿透的）。
        /// <para>
        /// 这一行是"用户一操作就要立刻出现"的提示，预热之后
        /// <see cref="ShowWarn4"/> 只是把不透明度打上去 —— 直接显示，不重建、不闪。
        /// 登录开始时（三行挂出那一刻）调一次即可。
        /// </para>
        /// </summary>
        public static void Prewarm4() => Send(CmdPrewarm4);

        /// <summary>
        /// 显示第四行并设置正文（<b>直接出现，没有淡入</b>）。
        /// 未预热时也会顺手建窗，所以第一次调用同样安全。
        /// </summary>
        public static void ShowWarn4(string? text) => Send(CmdText4, text);

        /// <summary>
        /// 收起第四行：**只把不透明度归零，不销毁窗口** ——
        /// 下次 <see cref="ShowWarn4"/> 直接显示，不用重新建窗。
        /// </summary>
        public static void Hide4() => Send(CmdHide4);
    }
}
