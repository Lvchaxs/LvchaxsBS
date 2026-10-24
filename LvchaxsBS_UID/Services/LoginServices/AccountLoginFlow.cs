using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LvchaxsBS.Core;       // SimulationService（按 ESC / 点击）、TemplateManager（取模板）
using LvchaxsBS.Services;   // CoordinateFormats、ImageRecognition
using LvchaxsBS_UID.Utils.UI;

namespace LvchaxsBS_UID.Services.LoginServices
{
    /// <summary>
    /// 登录流程的结局。
    /// ★ 判定「成功」的依据是：**已经输入过账号密码之后**再检测到「点击进入区域」——
    ///   同一个「点击进入」按钮有两种含义，靠「本账号有没有输过账密」来区分：
    ///   · 还没输过 → 登录大厅里有旧账号在线（那是别人的账号）→ 走退出流程；
    ///   · 已经输过 → 这次登录成功了，那个按钮属于刚登录的这个账号 → 点进去，成功。
    ///   所以「成功」= 输过账密 且 看到「点击进入」。
    /// </summary>
    public enum LoginResult
    {
        /// <summary>登录成功（输入过账密之后检测到「点击进入」）。</summary>
        Success,

        /// <summary>账号或密码错误（B 类）：服务器错误重试满 <see cref="MaxCredentialAttempts"/> 次（含首次）仍报错。</summary>
        CredentialError,

        /// <summary>
        /// ★ 账号验证失效：账密提交后弹出「您的账号存在安全风险…请先验证身份后再登录」。
        /// 说明这个账号在本机的设备验证过期了 —— 本次立刻收场（顺手点「取消验证」关掉弹窗），
        /// 并给账号打「账号验证失效」红标签，之后这个账号不再尝试登录。
        /// </summary>
        VerifyInvalid,

        /// <summary>
        /// ★ 连接服务器失败：游戏里弹出「连接服务器失败」，说明网络出问题了。
        /// 再重试也没用 ⇒ **彻底结束登录**（不再尝试下一个账号）。
        /// </summary>
        ConnectionFailed,

        /// <summary>其他情况收场：用户点了停止、或点了「点击进入」但没证明账号密码是对的。</summary>
        Failed,
    }

    /// <summary>
    /// 账密输入/验证的三态（只在 <see cref="AccountLoginFlow.RunAsync"/> 内部用）。
    /// 解决「提交后画面仍停在输入界面 / 同意窗，导致每轮重复输入」的问题 ——
    /// 提交（回车或点同意）后服务器要一段时间才返回画面结果，这段过渡期里
    /// 输入界面和同意窗会持续命中，必须用状态把「已提交、等结果」锁住。
    /// </summary>
    internal enum LoginPhase
    {
        /// <summary>还没提交过账密（或在账密页上准备输入）。此时响应「进入游戏」输入。</summary>
        PendingInput,

        /// <summary>已提交账密，正在等服务器返回「账密出错」或「可以进游戏」。期间不碰输入框、不点同意。</summary>
        AwaitingResult,

        /// <summary>检测到「账号密码出错」，错误提示还挂在画面上，等它自己消失（期间不重复计数、不碰输入框）。</summary>
        ErrorShown,
    }

    /// <summary>
    /// 当前在盯哪个区域。
    /// <list type="bullet">
    ///   <item><see cref="Locating"/>：<b>定位检测</b> —— 不知道当前在哪个画面，
    ///     每 <c>LocatingIntervalMs</c> 全区域扫一遍；全都没命中就按一次 ESC。</item>
    ///   <item>其余：<b>指定区域检测</b> —— 只盯这一个（或这一对）区域，
    ///     每 <c>StepIntervalMs</c> 测一次；连续 <c>StepTimeoutMs</c> 测不到就退回 <see cref="Locating"/>。</item>
    /// </list>
    /// </summary>
    internal enum WatchTarget
    {
        /// <summary>定位检测（全区域）。</summary>
        Locating,

        /// <summary>节点1-2：ESC菜单（派蒙菜单）—— 命中后点「设置」。</summary>
        EscMenu,
        /// <summary>节点1-3：设置界面 —— 命中后点「账户」。</summary>
        Settings,
        /// <summary>节点1-4：账户跳转 —— 命中后点跳转。</summary>
        Jump,
        /// <summary>节点1-5：用户中心刷新 —— 命中后 TAB/ENTER/ENTER 退号。</summary>
        UserCenter,
        /// <summary>节点1-5 之后：登录其他账号 / 进入游戏 二选一。</summary>
        LoginOtherOrEnterGame,

        /// <summary>节点2-1：账密输入页（进入游戏）—— 命中后输入账密。</summary>
        EnterGame,
        /// <summary>节点2-2：账号密码出错区域 / 同意登录 二选一。</summary>
        ErrorOrAgree,
        /// <summary>节点2-3：点击进入 —— 已输账密就点进去，但**还不算成功**，转去等主界面。</summary>
        ClickEnter,

        /// <summary>
        /// 节点2-4：点过「点击进入」之后等**主界面**。
        /// ★ 点那个按钮只是开始加载，要等游戏真正进到主界面（左上角派蒙头像出现）
        ///   才算登录成功 —— 在此之前登录流程不结束。
        /// </summary>
        WaitMainMenu,

        /// <summary>节点3-2：是否保留登录记录窗 —— 命中后点「退出登录点击坐标」。</summary>
        KeepRecord,
    }

    /// <summary>
    /// 登录流程的「图像识别 + 按键/点击」循环。
    ///
    /// <para>
    /// 从「确认登录」开始：游戏窗口已被激活（见 <c>Utils.GameWindow.GameWindowActivator</c>），
    /// 然后按「定位检测 / 指定区域检测」两种模式推进（见后面的说明）。整个流程分两大段：
    /// </para>
    /// <para>
    /// <b>第一段 · 退出当前账号</b>（前五个区域）：
    /// </para>
    /// <list type="bullet">
    ///   <item><b>主界面</b>：左上角派蒙头像（<c>主界面区域</c> + 模板 <c>其他模版_主界面</c>）</item>
    ///   <item><b>派蒙菜单</b>：主界面按 ESC 打开的那一页（<c>派蒙菜单区域</c> + 模板 <c>其他模版_ESC菜单</c>）</item>
    ///   <item><b>设置界面</b>：派蒙菜单点左下角「设置」后打开的那一页（<c>设置界面区域</c> + 模板 <c>其他模版_退出游戏_设置</c>）</item>
    ///   <item><b>账户跳转</b>：设置界面里点过「账户」后出现的跳转按钮（<c>账户跳转区域</c> + 模板 <c>其他模版_退出游戏_点击跳转</c>）</item>
    ///   <item><b>用户中心</b>：点过「账户跳转」之后加载出来的页面，看到刷新按钮就说明页面好了
    ///     （<c>用户中心刷新按钮区域</c> + 模板 <c>其他模版_退出游戏_用户中心刷新</c>）</item>
    /// </list>
    /// <para>
    /// <b>第二段 · 登录新账号</b>（后三个区域，退出到登录界面之后才开始）：
    /// </para>
    /// <list type="bullet">
    ///   <item><b>登录其他账号</b>：退出后若停在带登录记录的界面，点它打开账密输入页
    ///     （<c>登录其他账号区域</c> + 模板 <c>其他模版_登录账号_登录其他账号</c>）</item>
    ///   <item><b>进入游戏</b>：账密输入页的登录按钮（<c>进入游戏区域</c> + 模板 <c>其他模版_登录账号_进入游戏</c>）；
    ///     名字叫"进入游戏"，实际是"用这套账密登录"的意思</item>
    ///   <item><b>同意</b>：账密提交后弹出的确认登录窗口里的同意按钮
    ///     （<c>同意登录区域</c> + 模板 <c>其他模版_登录账号_同意登录</c>）</item>
    ///   <item><b>点击进入</b>：登录完成、加载完毕后登录界面上的进入按钮
    ///     （<c>点击进入区域</c> + 模板 <c>其他模版_点击进入</c>）。
    ///     ★ 点它**只是开始加载**，真正登录成功要等随后出现<b>主界面</b>（见节点2 末尾）。</item>
    /// </list>
    /// <para>
    /// ★★ <b>两套检测模式</b>（2026-10-14 重构，取代早期的「每轮全扫 + 图层排序」）：
    /// </para>
    /// <list type="bullet">
    ///   <item><b>定位检测</b>（间隔 <see cref="LocatingIntervalMs"/> = 1000ms）：不知道当前停在哪个画面时，
    ///     每轮把全部区域扫一遍来「定位」；命中哪个位置就执行它的动作，然后<b>退出定位检测</b>，
    ///     转去持续盯住它的下一步。一个都没命中就按一次 ESC —— <b>只有定位检测会按 ESC</b>。</item>
    ///   <item><b>指定区域检测</b>（间隔 <see cref="StepIntervalMs"/> = 100ms）：只盯当前这一步的
    ///     那一个（或那一对）区域，不再全扫；检测到就执行并推进到下一步；
    ///     连续 <see cref="StepTimeoutMs"/>（10 秒）都检测不到 → 退回定位检测重新确认现在在哪。</item>
    /// </list>
    /// <para>
    /// 三条固定的推进链（定位到其中任意一环之后就按链往下走）：
    /// </para>
    /// <list type="bullet">
    ///   <item><b>节点1 · 从游戏内退出到登录大厅</b>：主界面 → ESC菜单 → 设置 → 点击跳转 → 用户中心刷新
    ///     →（登录其他账号 / 进入游戏 二选一，哪个命中执行哪个）→ 接到节点2。</item>
    ///   <item><b>节点2 · 输入界面到登录成功</b>：进入游戏 →（账号密码出错区域 / 同意登录 二选一）
    ///     → 点击进入 → <b>等主界面</b> → 登录成功（点过「点击进入」还不算，要检测到主界面）。</item>
    ///   <item><b>节点3 · 大厅里在线的是别人的账号</b>：点击进入 → 是否保留登录记录 → 回到节点2 的「进入游戏」。</item>
    /// </list>
    /// <para>
    /// 节拍：定位检测 <see cref="LocatingIntervalMs"/> 一轮、指定区域检测 <see cref="StepIntervalMs"/> 一轮；
    /// 命中之后、执行点击之前额外等 <see cref="ClickDelayMs"/> 毫秒，让刚切换出来的画面稳定下来再点。
    /// </para>
    /// </para>
    /// <para>
    /// ★ <b>失焦点暂停</b>：每轮开始前查一次游戏窗口是否仍是焦点窗口
    /// （宿主注入的 <c>GameFocusProvider</c>，底层是 <c>WindowFocusService.Reevaluate</c>）。
    /// 不在焦点就停在那里——识别和按键全部停住，第二行字幕换成「已暂停」，
    /// 每 500ms 查一次，焦点回游戏再接着跑（轮次连续，不从头开始）。
    /// 宿主没注入这个能力时按"仍在焦点"处理，不暂停。
    /// </para>
    /// <para>
    /// 识别能力来自共享库 <c>LvchaxsBS_Resources</c>（本模块只引用它，不引用宿主）。
    /// 字幕走宿主注入的覆盖层（复用钓鱼/烹饪/伐木那一层，本模块不自己建窗口）。
    /// </para>
    /// </summary>
    public static class AccountLoginFlow
    {
        /// <summary>字幕位置 key（定义在共享库 <c>CoordinateFormats</c> 的「退出登录-用户中心退出」段）。</summary>
        public const string SubtitlePositionKey = "登录_字幕显示坐标";

        /// <summary>主界面（左上角派蒙头像存在即为主界面）。</summary>
        public const string MainRegionKey = "主界面区域";
        public const string MainTemplateKey = "其他模版_主界面";

        /// <summary>派蒙菜单（主界面按 ESC 后打开的那一页）。</summary>
        public const string PaimonRegionKey = "派蒙菜单区域";
        public const string PaimonTemplateKey = "其他模版_ESC菜单";

        /// <summary>设置界面（派蒙菜单点左下角「设置」后打开的那一页）。</summary>
        public const string SettingsRegionKey = "设置界面区域";
        public const string SettingsTemplateKey = "其他模版_退出游戏_设置";

        /// <summary>账户跳转按钮（设置界面点过「账户」之后出现的那个按钮）。</summary>
        public const string JumpRegionKey = "账户跳转区域";
        public const string JumpTemplateKey = "其他模版_退出游戏_点击跳转";

        /// <summary>
        /// 用户中心刷新按钮：点过「账户跳转」之后加载出来的用户中心页面里才有，
        /// 看到它就说明页面加载完了，是退出流程的最后一个阶段。
        /// </summary>
        public const string UserCenterRegionKey = "用户中心刷新按钮区域";
        public const string UserCenterTemplateKey = "其他模版_退出游戏_用户中心刷新";

        /// <summary>
        /// 「登录其他账号」按钮：从用户中心退出到登录界面后，如果游戏存有登录记录，
        /// 会停在没有账密输入框的界面，点它才打开账密登录页。
        /// </summary>
        public const string LoginOtherRegionKey = "登录其他账号区域";
        public const string LoginOtherTemplateKey = "其他模版_登录账号_登录其他账号";

        /// <summary>
        /// 「进入游戏」按钮区域：账密输入页的登录按钮。
        /// （名字叫进入游戏，实际是"用这套账密登录"的意思，不是直接进游戏。）
        /// </summary>
        public const string EnterGameRegionKey = "进入游戏区域";
        public const string EnterGameTemplateKey = "其他模版_登录账号_进入游戏";

        /// <summary>
        /// ★★「登录或退出区域」：登录大厅右下角那个按钮（登录大厅两个状态的分水岭）。
        /// <list type="bullet">
        ///   <item>**检测得到** → 当前客户区**没有账号在线**，点它直接进入账密输入界面。</item>
        ///   <item>**检测不到**（但「点击进入」检测得到）→ 当前**已有账号在线**，
        ///        此时它显示的不是这个按钮，模板匹配不上，但位置照旧可点 ——
        ///        点它会弹出退出流程（见 <see cref="LogoutWindowRegionKey"/>）。</item>
        /// </list>
        /// ★ 关键：还没为本账号输入过账密时，绝不能直接点「点击进入」，
        ///   那样进的是当前在线的别人的账号 —— 必须先走这里的退出流程换过来。
        /// </summary>
        public const string LoginLogoutRegionKey = "登录或退出区域";
        public const string LoginLogoutTemplateKey = "其他模版_登录账号_登录账号";

        /// <summary>
        /// 「是否保留登录记录」窗口：点了「登录或退出区域」之后弹出来的退出确认窗。
        /// 用窗口里的复选框图标做标识，检测到就点 <see cref="LogoutClickKey"/>（确认退出登录），之后会落到：
        /// 账密输入界面，或者「登录其他账号」（再点一下也到输入界面）。
        /// </summary>
        public const string LogoutWindowRegionKey = "是否保留登录记录";
        // ★ key 由模板路径现推：`Template/其他模版/退出游戏/是否保留登录记录.png`
        //   → 「其他模版_退出游戏_是否保留登录记录」。文件在「退出游戏」子目录里（属退出流程）。
        // ★ 2026-10-14 复盘：这张图匹配不上**不是区域/余量的问题，是模板图本身失真** ——
        //   重做 23×23 之后立刻正常。所以以后「检测不到」先怀疑模板图，别急着放宽区域。
        public const string LogoutWindowTemplateKey = "其他模版_退出游戏_是否保留登录记录";

        /// <summary>「退出登录」窗口里「确认退出登录」的点击坐标。</summary>
        public const string LogoutClickKey = "退出登录点击坐标";

        /// <summary>「点击进入」：登录完成、加载完毕后登录界面上的进入按钮，点它即全部完成。</summary>
        public const string ClickEnterRegionKey = "点击进入区域";
        public const string ClickEnterTemplateKey = "其他模版_点击进入";

        /// <summary>
        /// 「空月祝福」：点「点击进入」进游戏后可能弹出来的月卡领取界面，
        /// 会**盖住主界面**，所以「等主界面」时必须同时盯它。
        /// <list type="bullet">
        ///   <item>★ 两个模板（<c>空月祝福1</c> / <c>空月祝福2</c>）**任中一个**就算中；</item>
        ///   <item>★ 区域 key = 共享库 <c>CoordinateFormats</c> 里的**字段名**「空月祝福」
        ///         （注意：这个字段名没有"区域"后缀，别顺手拼一个）。</item>
        /// </list>
        /// </summary>
        public const string MoonBlessingRegionKey = "空月祝福";
        private static readonly string[] MoonBlessingTemplateKeys =
        {
            "其他模版_空月祝福1",
            "其他模版_空月祝福2",
        };

        /// <summary>
        /// 「连接已断开」：游戏**进行中**随时可能突然弹出的两种提示（**登录大厅不会弹**，
        /// 所以凡是还站在登录大厅 / 账密输入页的轮次都不用测它，见 <c>IsLoginHallStep</c>）。
        /// <list type="bullet">
        ///   <item><c>连接服务器失败</c> —— 网络出问题了，再试也没用 ⇒ **彻底结束登录**；</item>
        ///   <item><c>账号在另一处重复登录</c> —— 这个号在别的地方又登了一次（被顶了）
        ///         ⇒ 按 ESC 退回登录界面，流程回定位检测重来。</item>
        /// </list>
        /// ★ 区域 key = 共享库 <c>CoordinateFormats</c> 里的字段名「连接已断开」（没有"区域"后缀）。
        /// </summary>
        public const string ConnectionLostRegionKey = "连接已断开";
        public const string ConnectFailedTemplateKey = "其他模版_连接服务器失败";
        public const string ReloginTemplateKey = "其他模版_账号在另一处重复登录";
        private static readonly string[] ConnectionLostTemplateKeys =
        {
            ConnectFailedTemplateKey,
            ReloginTemplateKey,
        };

        /// <summary>
        /// 「同意」按钮：账密按 ENTER 提交后弹出的确认登录窗口里的同意按钮。
        /// ★ 它只有在提交过账密之后才可能出现，所以和「点击进入」同时命中时它优先。
        /// </summary>
        public const string AgreeRegionKey = "同意登录区域";
        public const string AgreeTemplateKey = "其他模版_登录账号_同意登录";

        /// <summary>
        /// 「账号密码出错区域」：同意之后**没输进去 / 输错了**才会出现的一行提示。
        /// 这片区域对应四个模板，检测到**任意一个**就算出错：
        /// <list type="bullet">
        ///   <item><c>请输入密码</c>：密码框是空的</item>
        ///   <item><c>请输入手机号邮箱</c>：账号框是空的</item>
        ///   <item><c>账号格式错误</c></item>
        ///   <item><c>账号或密码错误</c></item>
        /// </list>
        /// </summary>
        public const string ErrorRegionKey = "账号密码出错区域";

        /// <summary>
        /// ★★「验证身份」区域：输入完账密按回车后，服务器若判定「本机设备验证已失效」，
        /// 会弹出「您的账号存在安全风险，为确保是您本人操作，请先验证身份后再登录」。
        /// <para>★ 区域 key 就是字段名「验证身份」（没有"区域"后缀，别顺手拼一个）。</para>
        /// </summary>
        public const string VerifyRegionKey = "验证身份";
        public const string VerifyTemplateKey = "其他模版_登录账号_验证身份";

        /// <summary>
        /// 「取消验证」：那个弹窗上的「取消」按钮坐标。检测到了就点它把弹窗关掉，然后结束登录。
        /// <para>★ 关掉而不是继续验证 —— 验证要收短信/走网页，程序里做不了，交给用户自己处理。</para>
        /// </summary>
        public const string CancelVerifyClickKey = "取消验证";

        /// <summary>
        /// ★★ A 类：本地即时校验（**输入框是空的**）—— 回车后**立刻**弹出，不走服务器。
        /// <list type="bullet">
        ///   <item><c>请输入密码</c>：密码框没粘进去</item>
        ///   <item><c>请输入手机号邮箱</c>：账号框没粘进去</item>
        /// </list>
        /// 这类提示说明**压根没提交过**（前端就拦下了），所以：
        /// <list type="bullet">
        ///   <item>不会走到服务器，也就**不会导致账号被风控冻结** —— 可以放心重试</item>
        ///   <item>提示本身就说明「此刻正在账密输入页」，所以**不必等它消失**，可以立刻重粘</item>
        ///   <item>**不占用** <see cref="MaxCredentialAttempts"/> 的服务器错误配额</item>
        /// </list>
        /// </summary>
        private static readonly string[] MissingInputTemplateKeys =
        {
            "其他模版_登录账号_请输入密码",
            "其他模版_登录账号_请输入手机号邮箱",
        };

        /// <summary>
        /// ★★ B 类：服务器校验失败 —— 账号密码**格式都正确、确实提交上去了**，
        /// 服务器比对后返回「不对」。要等一个网络往返才会显示（比 A 类慢得多）。
        /// <list type="bullet">
        ///   <item><c>账号格式错误</c></item>
        ///   <item><c>账号或密码错误</c></item>
        /// </list>
        /// 这类才是真正危险的：**重试多了会被临时冻结几十分钟**，
        /// 所以严格限制在 <see cref="MaxCredentialAttempts"/> 次以内，且要等画面稳定再动。
        /// </summary>
        private static readonly string[] ServerErrorTemplateKeys =
        {
            "其他模版_登录账号_账号格式错误",
            "其他模版_登录账号_账号或密码错误",
        };

        /// <summary>账号输入框。</summary>
        public const string AccountInputClickKey = "输入账号点击坐标";

        /// <summary>密码输入框。点一下把焦点切过去 —— 比按 TAB 稳（TAB 偶尔会把内容粘错框）。</summary>
        public const string PasswordInputClickKey = "输入密码点击坐标";

        /// <summary>派蒙菜单左下角「设置」按钮。</summary>
        public const string SettingsClickKey = "设置点击坐标";

        /// <summary>设置界面内的「账户」。</summary>
        public const string AccountClickKey = "账户点击坐标";

        /// <summary>「定位检测」的轮间隔：不知道当前在哪个画面，每轮把全部区域扫一遍。</summary>
        private const int LocatingIntervalMs = 1000;

        /// <summary>「指定区域检测」的轮间隔：只盯住当前这一步的那一个（或一对）区域。</summary>
        private const int StepIntervalMs = 100;

        /// <summary>
        /// 「指定区域检测」连续这么久都检测不到 → 退回「定位检测」重新确认当前位置。
        /// </summary>
        private const int StepTimeoutMs = 10000;

        /// <summary>
        /// 等主界面时的轮询间隔：这段时间没有别的事要做，可以比 <see cref="StepIntervalMs"/>
        /// 宽松些（识别本身有开销，没必要 100ms 就扫一遍全屏）。
        /// ★ 本状态**不设超时回落** —— 原因见 <c>case WatchTarget.WaitMainMenu</c> 里的注释。
        /// </summary>
        private const int GameEnterPollMs = 300;

        /// <summary>
        /// 点过「空月祝福」之后，多久还看不到主界面就补一次左键点击。
        /// 领完祝福界面会变成「领取成功」—— 那个界面认不出空月祝福、但也还不是主界面，
        /// 补一下左键它大概就消失了。
        /// </summary>
        private const int MoonBlessingExtraClickMs = 5000;

        /// <summary>
        /// 激活游戏窗口之后、第一轮检测之前的等待：切前台的动画和画面加载都要走一会儿，
        /// 不等的话第一轮大概率全线未命中，白白按一次 ESC。
        /// </summary>
        private const int StartDelayMs = 1000;

        /// <summary>命中之后、执行点击之前的等待：让刚切换出来的画面稳定下来再点。</summary>
        private const int ClickDelayMs = 200;

        /// <summary>
        /// 检测到「用户中心刷新按钮」之后、原地点击之前的等待。
        /// 用户中心页面刚加载完，比普通界面多等一会儿（1000ms）让它彻底稳定。
        /// </summary>
        private const int UserCenterDelayMs = 1000;

        /// <summary>粘贴账号之后、切到密码框之前的等待。</summary>
        private const int AfterAccountPasteMs = 500;

        /// <summary>
        /// 账密最多尝试几次（**含第一次输入**）。第一次输入 + 一次重试 = 共 2 次，
        /// 第 2 次还错就结束并给账号打「账号或密码错误」标记。
        /// </summary>
        private const int MaxCredentialAttempts = 2;

        // ★ A 类（输入框空、没粘进去）**不设次数上限** —— 它会一直重粘到粘进去为止。
        //   理由：这类提示压根没提交到服务器，不会触发风控冻结，没必要掐死；
        //   相反，剪贴板偶尔被占 / 首次点击没落到输入框而直接放弃就太亏了。
        //   它也不会失控：**只要提示还挂着就证明框还是空的**，重粘就是正确的应对；
        //   粘进去之后提示自然不再命中，过程自动收敛。

        /// <summary>第二行字幕：暂停中（游戏窗口不在焦点）。</summary>
        private const string Line2Paused = "已暂停（游戏窗口不在焦点）";

        /// <summary>第二行字幕：正在领取空月祝福。</summary>
        private const string Line2MoonBlessing = "正在领取空月祝福";

        /// <summary>第二行字幕：账号在另一处重复登录（被顶了），正在退回登录界面。</summary>
        private const string Line2ReloginElsewhere = "账号在别处登录，正在退回登录界面";

        /// <summary>第二行字幕：连接服务器失败。</summary>
        private const string Line2ConnectFailed = "连接服务器失败，已停止登录";

        /// <summary>第二行字幕：登录完成（黄色强调）。</summary>
        private const string Line2Succeeded = "登录完成";

        /// <summary>第二行字幕：TAB 隐藏提示（正常白色，操作提示不抢眼）。</summary>
        private const string Line2TabHint = "-TAB隐藏";

        /// <summary>第二行汇总行（账号 + 计时）的刷新间隔。</summary>
        private const int SummaryRefreshMs = 1000;

        /// <summary>暂停期间每隔多久查一次焦点。</summary>
        private const int FocusPollMs = 500;

        /// <summary>模板缺失只警告一次，避免每轮刷屏。</summary>
        private static bool _missingTemplateWarned;

        #region 字幕

        /// <summary>
        /// 第三行是不是本次挂出来的。
        /// <para>只有**分组内的登录**才挂第三行（完整流程 = 登录 + 登录后的附加动作），
        /// 账号列表里的登录是单纯的登录，只有两行 —— 收字幕时要按这个标志决定收不收第三行。</para>
        /// </summary>
        private static bool _thirdLineActive;

        /// <summary>
        /// 挂出登录用的字幕：默认两行（两行都带头像，和自动清药一致）：
        /// <list type="bullet">
        ///   <item><b>第一行</b>：<c>正在登录 {账号} {计时}</c>，账号名用高亮色（黄），每秒刷新计时；</item>
        ///   <item><b>第二行</b>：当前步骤的状态日志（"正在定位当前界面" → "正在输入账号密码" → …）。</item>
        ///   <item><b>第三行</b>：<paramref name="thirdLineText"/> 非空时才挂 —— 写明本次登录后的附加动作，
        ///         之后由 <c>PostLoginActions</c> 接着在这行上刷新进度 / 联机人数。</item>
        /// </list>
        /// <para>★ 本方法<b>只换内容</b>：不隐藏任何一行、也不重开"一次运行"，
        ///   所以自动换号时能就地把内容换成新账号的（字幕从头到尾不消失）。
        ///   「有字幕的功能同时只能跑一个」那一步（停掉其它功能）在调用方做，见 <see cref="RunAsync"/>。</para>
        /// </summary>
        /// <param name="accountName">账号名。</param>
        /// <param name="thirdLineText">
        /// 第三行的初始文案（如「附加动作：打开聊天界面」）。
        /// **空** = 不挂第三行（账号列表里的单纯登录）。
        /// </param>
        /// <param name="elapsedText">
        /// 第一行末尾那个计时。默认 <c>00:00</c>（新一轮登录从零开始）；
        /// ★ **自动换号**时由调用方把**总计时**传进来（见 <c>AccountManagerView.SwitchToNextAccountAsync</c>）
        /// —— 第一行是整轮运行的总时长，换号不许归零。
        /// </param>
        public static void ShowRunningSubtitles(string accountName, string thirdLineText = "", string elapsedText = "00:00")
        {
            // 登录期间按 TAB 不收字幕（和自动清药一个道理：字幕是进度，收起来就不知道走到哪了）
            SubtitlePresenter.LockTabHide();

            // 第一行：正在登录 + 账号名（黄）+ 计时
            SubtitlePresenter.StartRun(SubtitlePositionKey);
            SubtitlePresenter.SetRichText("正在登录 ", SafeName(accountName), " " + elapsedText);

            // 第二行：当前步骤（状态日志）
            SubtitlePresenter.SetAvatarVisible2(true);
            SubtitlePresenter.StartRun2(SubtitlePositionKey);
            SubtitlePresenter.SetText2(DescribeStep(WatchTarget.Locating));

            // 第三行：分组内的完整流程才有（写明这次登录后还要做什么）
            _thirdLineActive = !string.IsNullOrEmpty(thirdLineText);
            if (_thirdLineActive)
            {
                SubtitlePresenter.StartRun3(SubtitlePositionKey);
                SubtitlePresenter.SetAvatarVisible3(true);
                SubtitlePresenter.AssignAvatars();   // 三行头像一次分配好（不能三个都一样）
                SubtitlePresenter.SetText3(thirdLineText);
            }
        }

        /// <summary>
        /// 收起第三行（如果这次挂了的话）。
        /// <para>★ 登录成功**不调**它 —— 那之后第三行要留给附加动作继续用；
        /// 失败 / 取消 / 附加动作自己跑完时才收。</para>
        /// </summary>
        public static void HideThirdLine()
        {
            if (!_thirdLineActive) return;
            _thirdLineActive = false;
            SubtitlePresenter.Hide3();
        }

        /// <summary>
        /// 登录成功：第一行 <c>登录成功 {账号} {总时长}</c>（账号名黄）；
        /// 第二行 <c>登录完成</c>（黄）+ <c>-TAB隐藏</c>（正常白，操作提示不抢眼）。
        /// </summary>
        public static void ShowSucceededSubtitle(string accountName, TimeSpan elapsed)
        {
            // 只有登录成功才允许按 TAB 收字幕
            SubtitlePresenter.UnlockTabHide();

            SubtitlePresenter.SetRichText("登录成功 ", SafeName(accountName), " " + FormatElapsed(elapsed));
            SubtitlePresenter.SetRichText2("", Line2Succeeded, Line2TabHint);
        }

        /// <summary>账号名兜底：空名字时也别让高亮段消失（那一行会看着缺一块）。</summary>
        private static string SafeName(string? accountName)
            => string.IsNullOrEmpty(accountName) ? "未知账号" : accountName;

        /// <summary>计时格式：分:秒（00:00）。★ 对外开放：自动换号要拿它拼「总计时」。</summary>
        public static string FormatElapsed(TimeSpan elapsed)
            => $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

        /// <summary>
        /// 这一步是不是**登录大厅 / 账密输入**那一套（<c>CoordinateFormats</c> 里的「登录账号」区）。
        /// <para>「连接已断开」只在**游戏里**弹，站在这几步上时不可能命中 —— 跳过省一次识别。</para>
        /// </summary>
        private static bool IsLoginHallStep(WatchTarget target) => target
            is WatchTarget.EnterGame
            or WatchTarget.ErrorOrAgree
            or WatchTarget.ClickEnter
            or WatchTarget.LoginOtherOrEnterGame;

        /// <summary>
        /// ★★【连接已断开】每轮都要顺带看一眼 —— 它可能**在任何时刻突然弹出**，
        /// 一旦弹出来就把当前画面挡住，正在盯的那个目标永远是"测不到"，流程会原地空转。
        /// 所以每轮检测的规矩是：**当前目标 + 这张**，两张一起测。
        /// <list type="bullet">
        ///   <item><c>连接服务器失败</c> → 网络故障，再点也没用，返回 <see cref="LoginResult.ConnectionFailed"/>
        ///         让上层彻底收尾；</item>
        ///   <item><c>账号在另一处重复登录</c> → 这个号被别处顶掉了，按 ESC 退回登录界面，
        ///         然后回定位检测重新认画面（该重输账密的自然会走到那一步）。</item>
        /// </list>
        /// </summary>
        /// <returns>null = 这一轮没事、继续按原计划走；否则 = 登录流程到此为止的结果。</returns>
        private static async Task<(LoginResult? End, WatchTarget? Redirect)> CheckConnectionLostAsync(
            WatchTarget target, CancellationToken token)
        {
            if (IsLoginHallStep(target)) return (null, null);

            var lost = DetectConnectionLost();
            if (!lost.detected) return (null, null);

            if (lost.hitKey == ReloginTemplateKey)
            {
                Debug.WriteLine(
                    $"[UID/登录] 检测到「账号在另一处重复登录」{lost.similarity * 100:F1}% " +
                    "→ 按 ESC 退回登录界面，回定位检测重新认画面");
                SubtitlePresenter.SetText2(Line2ReloginElsewhere);
                SimulationService.PressEscape();
                await Task.Delay(LocatingIntervalMs, token);

                // ★★ **必须**回定位检测：这一下 ESC 之后画面已经不知道变成什么样了 ——
                //    继续盯原来那一步（例如「等主界面」）会一直空等到天荒地老。
                return (null, WatchTarget.Locating);
            }

            Debug.WriteLine(
                $"[UID/登录] 检测到「连接服务器失败」{lost.similarity * 100:F1}% → 网络故障，彻底结束登录");
            SubtitlePresenter.SetText2(Line2ConnectFailed);
            return (LoginResult.ConnectionFailed, null);
        }

        /// <summary>把「当前盯着的目标」翻译成第二行字幕的状态日志。</summary>
        private static string DescribeStep(WatchTarget target) => target switch
        {
            WatchTarget.Locating => "正在定位当前界面",
            WatchTarget.EscMenu => "正在打开派蒙菜单",
            WatchTarget.Settings => "正在进入设置界面",
            WatchTarget.Jump => "正在跳转账户页面",
            WatchTarget.UserCenter => "正在退出当前账号",
            WatchTarget.LoginOtherOrEnterGame => "正在切换到登录界面",
            WatchTarget.EnterGame => "正在输入账号密码",
            WatchTarget.ErrorOrAgree => "检测是否错误",
            WatchTarget.ClickEnter => "正在等待进入游戏",
            WatchTarget.WaitMainMenu => "正在进入游戏",
            WatchTarget.KeepRecord => "正在确认退出登录",
            _ => "登录中..."
        };

        /// <summary>
        /// 登录**失败**时收尾用的两行字幕（成功走 <see cref="ShowSucceededSubtitle"/>）：
        /// <list type="bullet">
        ///   <item><b>第一行</b>：<c>登录失败 {账号} {总时长}</c>（账号名黄）；</item>
        ///   <item><b>第二行</b>：<c>{失败原因}</c>（黄）+ <c>-TAB隐藏</c>（正常白）。</item>
        /// </list>
        /// ★ 失败也是「流程已结束」，所以这里要解开 TAB 锁 —— 第二行既然提示可以按 TAB 收，
        ///   就必须真的能收起来。
        /// </summary>
        /// <param name="accountName">账号名。</param>
        /// <param name="elapsed">本次登录总时长。</param>
        /// <param name="reason">失败原因（「账号验证失效」这种短句）。</param>
        public static void ShowFailedSubtitle(string accountName, TimeSpan elapsed, string reason)
        {
            SubtitlePresenter.UnlockTabHide();

            SubtitlePresenter.SetRichText("登录失败 ", SafeName(accountName), " " + FormatElapsed(elapsed));
            SubtitlePresenter.SetRichText2("", reason, Line2TabHint);
        }

        /// <summary>收起字幕（第三行这次挂了的话一并收）。</summary>
        public static void HideSubtitles()
        {
            // 失败 / 取消也一定要解开：否则标志卡死后，用户按 TAB 再也收不起字幕
            SubtitlePresenter.UnlockTabHide();

            SubtitlePresenter.Hide();
            SubtitlePresenter.Hide2();
            HideThirdLine();
        }

        /// <summary>
        /// 解开「按 TAB 隐藏字幕」的锁。供登录流程的兜底路径调用
        /// （正常解锁走 <see cref="ShowSucceededSubtitle"/> / <see cref="HideSubtitles"/>）。
        /// </summary>
        public static void UnlockTabHide() => SubtitlePresenter.UnlockTabHide();

        #region 第一行计时：分组内的完整流程要一路走到附加动作结束

        /// <summary>
        /// 本次计时刷新停止时要不要「什么都不写」。
        /// <para>★ 只有**用户放弃**的那些场景（关窗口 / 又开了新一轮登录）才置 true ——
        /// 那时第一行已经被收掉、或即将由新流程接管，ticker 再补一笔就是诈尸的旧字幕。</para>
        /// </summary>
        private static volatile bool _elapsedSilentExit;

        /// <summary>让本次计时刷新在停止时保持沉默（不写收尾那一笔）。见 <see cref="_elapsedSilentExit"/>。</summary>
        public static void SilenceElapsedTicker() => _elapsedSilentExit = true;

        /// <summary>
        /// ★ 登录成功之后**继续刷这一行**（只有分组内的完整流程需要）。
        /// <para>
        /// 分组内的登录成功**不是整个流程的结束** —— 后面还接着跑「打开派蒙 / 聊天 / 联机」这些附加动作。
        /// 但**文字要立刻变成「登录成功」**（登录这一段确实成功了），只有**计时继续往前走**，
        /// 直到 <paramref name="token"/>（= 整个流程的那个）被取消 —— 这样用户既看到"登录成功"，
        /// 又能从计时上看出后面还在跑。
        /// </para>
        /// <para>
        /// ★ 收尾那一笔特意由 <b>ticker 自己</b>在退出时写（<paramref name="finalize"/>）——
        /// 第一行的写手始终只有一个，就不存在「别处刚写下终稿、这里又刷回计时」的互相覆盖。
        /// </para>
        /// </summary>
        /// <param name="accountName">账号名（第一行里那段高亮的字）。</param>
        /// <param name="startTime">计时起点 —— 登录开始那一刻，**不是**现在。</param>
        /// <param name="token">整个流程（登录 + 附加动作）共用的令牌，取消 = 全部结束。</param>
        /// <param name="finalize">停止时写「定格的一行」（一般是「登录成功 {账号} {总时长}」）。</param>
        public static void ResumeElapsedTicker(
            string accountName, DateTime startTime, CancellationToken token, Action finalize)
        {
            _elapsedSilentExit = false;

            if (token.IsCancellationRequested)
            {
                finalize?.Invoke();
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        // ★ 文案是「登录成功」不是「正在登录」：登录这一段已经成功了，
                        //   只有计时还在走（后面的附加动作还没跑完）。
                        SubtitlePresenter.SetRichText(
                            "登录成功 ", SafeName(accountName), " " + FormatElapsed(DateTime.Now - startTime));

                        await Task.Delay(SummaryRefreshMs, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常退出：整个流程跑完了（或被停止）
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UID/登录] 第一行计时刷新异常：{ex.Message}");
                }
                finally
                {
                    // ★ 静默停止（关窗 / 新一轮登录）时一个字都不写
                    if (!_elapsedSilentExit)
                    {
                        try { finalize?.Invoke(); }
                        catch (Exception ex) { Debug.WriteLine($"[UID/登录] 第一行收尾失败：{ex.Message}"); }
                    }
                }
            }, token);
        }

        #endregion

        #endregion

        #region 主流程

        /// <summary>
        /// 跑登录流程。
        /// ★★ 两套检测模式（2026-10-14 重构，取代早期的「每轮全扫 + 图层排序」）：
        /// <list type="bullet">
        ///   <item><b>定位检测</b>：不知道当前停在哪个画面 → 每轮全区域扫一遍（1000ms）；
        ///     命中哪个位置就执行它的动作，然后<b>退出定位检测</b>，转去持续盯住它的下一步；
        ///     一个都没命中就按一次 ESC（只有定位检测会按 ESC）。</item>
        ///   <item><b>指定区域检测</b>：只盯当前这一步的那一个（或一对）区域（100ms）；
        ///     检测到就执行并推进到下一步；连续 10 秒都检测不到 → 退回定位检测重新确认位置。</item>
        /// </list>
        /// </summary>
        /// <returns>
        /// <see cref="LoginResult.Success"/>：点过「点击进入」之后检测到**主界面**（真正进到游戏里）；
        /// <see cref="LoginResult.CredentialError"/>：账密重试满 <see cref="MaxCredentialAttempts"/> 次仍报错；
        /// <see cref="LoginResult.Failed"/>：其他情况（用户停止、或始终没轮到本账号进游戏）。
        /// </returns>
        /// <param name="accountName">要登录的账号名（已显示在字幕第一行）。</param>
        /// <param name="username">登录用的账号（粘到账号输入框）。</param>
        /// <param name="password">登录用的密码（粘到密码输入框）。</param>
        /// <param name="uid">账号 UID。密码粘完立刻用它顶替剪贴板，别把密码留在剪贴板里。</param>
        /// <param name="thirdLineText">
        /// 第三行字幕的初始文案（分组内的完整流程传「附加动作：…」，之后由 <c>PostLoginActions</c> 接着刷新）；
        /// **空** = 只显示两行（账号列表里的单纯登录）。
        /// </param>
        /// <param name="token">取消令牌（用户点「停止登录」时触发）。</param>
        public static async Task<LoginResult> RunAsync(
            string accountName, string username, string password, string uid,
            string thirdLineText, CancellationToken token)
        {
            // ★ 有字幕的功能同时只能跑一个：新的中断旧的。
            //   登录要接管鼠标键盘，且第一行字幕是共用行 —— 先把还在跑的功能全部停掉。
            //   （2026-10-17：这一步原本写在 ShowRunningSubtitles 里；为了让自动换号能"只换内容"，
            //    把它挪到入口，ShowRunningSubtitles 从此是幂等的纯换内容。）
            SubtitlePresenter.StopAllFunctions();

            ShowRunningSubtitles(accountName, thirdLineText);

            // 游戏窗口刚被激活（切前台的动画、画面加载都要走一会儿），等 1 秒再开始扫。
            Debug.WriteLine("[UID/登录] 游戏窗口已激活，等待 1 秒后开始检测");
            await Task.Delay(StartDelayMs, token);

            // 流程起始时刻：第二行字幕的「已运行时长」从这儿算起
            DateTime runStart = DateTime.Now;
            long lastSummaryTicks = Environment.TickCount64;

            // B 类（服务器校验失败）尝试次数：累计到 MaxCredentialAttempts（含首次输入）就放弃 + 打红标签。
            // ★ 只统计 **B 类** —— A 类（输入框空）压根没提交到服务器，不占这个配额。
            int credentialRetries = 0;

            // A 类（输入框空的本地校验）重粘次数。**仅用于日志**，不设上限、不做放弃判定。
            int missingInputRetries = 0;

            // ★★ 是否已经为**本账号**输入过账密。这是区分「点击进入」两种含义的唯一开关。
            bool hasInputCredentials = false;

            // ★★ 账密输入/验证的三态状态机（防止「提交后画面仍停在输入页」导致重复输入）。
            LoginPhase phase = LoginPhase.PendingInput;

            // 当前状态：Locating = 定位检测（全区域扫描）；其余 = 只盯那一个区域
            WatchTarget target = WatchTarget.Locating;
            long lastHitTicks = Environment.TickCount64;

            // 最近一次点「空月祝福」的时刻；0 = 还没点过（「等主界面」里 5 秒补点用）
            long lastMoonBlessingTicks = 0;

            // 第二行字幕当前显示的文字（相同就不重复发，省得每轮都 Invoke 一次宿主）
            string currentLine2 = "";
            void SetLine2(string text)
            {
                if (text == currentLine2) return;
                currentLine2 = text;
                SubtitlePresenter.SetText2(text);
            }

            // 切到下一个要盯的区域（同时重置「10 秒没检测到」的计时）
            // ★ 顺手把第二行字幕换成新状态 —— 第二行就是「当前登录状态日志」
            async Task MoveTo(WatchTarget next)
            {
                target = next;
                lastHitTicks = Environment.TickCount64;
                SetLine2(DescribeStep(next));
                await Task.Delay(StepIntervalMs, token);
            }

            // 当前盯的区域连续 10 秒没检测到 → 退回定位检测
            bool StepTimedOut() => Environment.TickCount64 - lastHitTicks >= StepTimeoutMs;

            for (int round = 1; ; round++)
            {
                token.ThrowIfCancellationRequested();

                // 游戏不在焦点就暂停：识别和按键全部停住，焦点回来再接着跑（轮次连续）。
                if (!GameFocusProvider.IsGameFocused())
                {
                    Debug.WriteLine("[UID/登录] 游戏窗口不在焦点，暂停（焦点恢复后继续）");
                    SetLine2(Line2Paused);

                    while (!GameFocusProvider.IsGameFocused())
                    {
                        await Task.Delay(FocusPollMs, token);
                    }

                    Debug.WriteLine("[UID/登录] 焦点已恢复，继续登录");
                    SetLine2(DescribeStep(target));
                    // 暂停的时间不算进「10 秒没检测到」
                    lastHitTicks = Environment.TickCount64;
                }

                // 第一行（正在登录 + 账号名 + 计时）每秒刷新一次计时
                if (Environment.TickCount64 - lastSummaryTicks >= SummaryRefreshMs)
                {
                    lastSummaryTicks = Environment.TickCount64;
                    SubtitlePresenter.SetRichText(
                        "正在登录 ", SafeName(accountName), " " + FormatElapsed(DateTime.Now - runStart));
                }

                // ★★【连接已断开】每一轮都先看一眼：它可能在游戏里**任何时刻突然弹出**，
                //    弹出来就把画面挡住，正在盯的目标就永远测不到。
                //    ⇒ 每轮的规矩是「当前目标 + 这张」两张一起测（一轮最少测两个区域）。
                //    站在登录大厅 / 账密输入页时它不可能出现 ⇒ 那几步跳过，不浪费一次识别。
                var chk = await CheckConnectionLostAsync(target, token);
                if (chk.End != null)
                {
                    Debug.WriteLine($"[UID/登录] 第 {round} 轮 连接异常（{target}）→ 结束登录：{chk.End}");
                    return chk.End.Value;
                }
                if (chk.Redirect != null)
                {
                    // 画面已经被上面那一下 ESC 改变了 → 转向新的目标再进本轮的分支
                    await MoveTo(chk.Redirect.Value);
                    target = chk.Redirect.Value;
                    continue;
                }

                // ==========================================================
                // 一、定位检测：不知道当前在哪个画面 → 全区域扫一遍
                // ==========================================================
                if (target == WatchTarget.Locating)
                {
                    var main = Detect(MainRegionKey, MainTemplateKey);
                    var paimon = Detect(PaimonRegionKey, PaimonTemplateKey);
                    var settings = Detect(SettingsRegionKey, SettingsTemplateKey);
                    var jump = Detect(JumpRegionKey, JumpTemplateKey);
                    var userCenter = Detect(UserCenterRegionKey, UserCenterTemplateKey);
                    var loginOther = Detect(LoginOtherRegionKey, LoginOtherTemplateKey);
                    var enterGame = Detect(EnterGameRegionKey, EnterGameTemplateKey);
                    var agree = Detect(AgreeRegionKey, AgreeTemplateKey);
                    var clickEnter = Detect(ClickEnterRegionKey, ClickEnterTemplateKey);
                    var loginLogout = Detect(LoginLogoutRegionKey, LoginLogoutTemplateKey);
                    var logoutWindow = Detect(LogoutWindowRegionKey, LogoutWindowTemplateKey);
                    var missingInput = DetectMissingInput();
                    var serverError = DetectServerError();
                    // ★ 空月祝福（月卡领取界面）：它会**盖住整个画面，而且 ESC 关不掉** ——
                    //   定位检测撞上它时，⑫ 那记 ESC 是白按的，流程会一圈圈空转。
                    //   所以每轮都测它，命中就先领掉（见下面的 ⓪ 分支）。
                    var moon = DetectMoonBlessing();

                    bool atInputPage = enterGame.detected;

                    var hits = new System.Collections.Generic.List<string>();
                    if (main.detected) hits.Add("主界面");
                    if (paimon.detected) hits.Add("派蒙菜单");
                    if (settings.detected) hits.Add("设置界面");
                    if (jump.detected) hits.Add("账户跳转");
                    if (userCenter.detected) hits.Add("用户中心");
                    if (loginOther.detected) hits.Add("登录其他账号");
                    if (enterGame.detected) hits.Add("账密输入页");
                    if (agree.detected) hits.Add("同意窗");
                    if (clickEnter.detected) hits.Add(hasInputCredentials ? "点击进入(已输账密)" : "点击进入(别人在线)");
                    if (loginLogout.detected) hits.Add("登录或退出");
                    if (logoutWindow.detected) hits.Add("保留记录窗");
                    if (missingInput.detected) hits.Add("空输入提示");
                    if (serverError.detected) hits.Add("服务器错提示");
                    if (moon.detected) hits.Add("空月祝福");
                    string hitList = hits.Count > 0 ? string.Join("+", hits) : "无";
                    if (hits.Count > 0) lastHitTicks = Environment.TickCount64;

                    Debug.WriteLine(
                        $"[UID/登录] 第 {round} 轮 [定位检测] " +
                        $"主界面 {main.similarity * 100:F1}%({(main.detected ? "中" : "-")}) " +
                        $"派蒙菜单 {paimon.similarity * 100:F1}%({(paimon.detected ? "中" : "-")}) " +
                        $"设置界面 {settings.similarity * 100:F1}%({(settings.detected ? "中" : "-")}) " +
                        $"账户跳转 {jump.similarity * 100:F1}%({(jump.detected ? "中" : "-")}) " +
                        $"用户中心 {userCenter.similarity * 100:F1}%({(userCenter.detected ? "中" : "-")}) " +
                        $"登录其他账号 {loginOther.similarity * 100:F1}%({(loginOther.detected ? "中" : "-")}) " +
                        $"进入游戏 {enterGame.similarity * 100:F1}%({(enterGame.detected ? "中" : "-")}) " +
                        $"同意登录 {agree.similarity * 100:F1}%({(agree.detected ? "中" : "-")}) " +
                        $"点击进入 {clickEnter.similarity * 100:F1}%({(clickEnter.detected ? "中" : "-")}) " +
                        $"登录或退出 {loginLogout.similarity * 100:F1}%({(loginLogout.detected ? "中" : "-")}) " +
                        $"保留记录 {logoutWindow.similarity * 100:F1}%({(logoutWindow.detected ? "中" : "-")}) " +
                        $"空输入 {missingInput.similarity * 100:F1}%({(missingInput.detected ? ShortKey(missingInput.hitKey) : "-")}) " +
                        $"服务器错 {serverError.similarity * 100:F1}%({(serverError.detected ? ShortKey(serverError.hitKey) : "-")}) " +
                        $"空月祝福 {moon.similarity * 100:F1}%({(moon.detected ? ShortKey(moon.hitKey) : "-")}) " +
                        $"(服务器错{credentialRetries}/{MaxCredentialAttempts} 重粘{missingInputRetries})" +
                        $" | 命中[{hitList}]");

                    // 没在领空月祝福了 → 第二行字幕回到「正在定位当前界面」；
                    // 同时把「补点」计时作废（画面一旦认出来，就再也不该盲目补点了）
                    if (!moon.detected)
                    {
                        SetLine2(DescribeStep(WatchTarget.Locating));
                        if (hits.Count > 0) lastMoonBlessingTicks = 0;
                    }

                    // ⓪【空月祝福（月卡领取界面）】排在所有分支**最前面**：
                    //   它是浮在最上层的弹窗，挡着的时候下面「主界面 / 派蒙菜单 / 大厅按钮」
                    //   的判断都不准；而它 ESC 关不掉，只能走自己的连点序列领掉。
                    if (moon.detected)
                    {
                        Debug.WriteLine(
                            $"[UID/登录] 第 {round} 轮 [定位检测] 检测到「空月祝福」" +
                            $"{moon.similarity * 100:F1}%({ShortKey(moon.hitKey)}) → 连点 5 次领掉 | 命中[{hitList}]");
                        SetLine2(Line2MoonBlessing);
                        await ClickMoonBlessingAsync(token);
                        // 重新计时：下一轮回到定位检测，重新看看现在到底站在哪个画面
                        lastHitTicks = Environment.TickCount64;
                        lastMoonBlessingTicks = Environment.TickCount64;
                        continue;
                    }

                    // ①【登录成功 · 第一步】已经输入过账密之后再看到「点击进入」→ 那个按钮属于本账号。
                    //    ★ 点下去**还不算成功**：从登录大厅进游戏要加载一会儿，只有随后检测到
                    //      「主界面」才算真的进到游戏里 —— 由 WaitMainMenu 状态收尾。
                    if (clickEnter.detected && hasInputCredentials && !atInputPage)
                    {
                        Debug.WriteLine($"[UID/登录] → 已输入账密后检测到「点击进入」→ 点进去，转去等主界面 | 命中[{hitList}]");
                        await Task.Delay(ClickDelayMs, token);
                        ClickRegionCenter(ClickEnterRegionKey);
                        await MoveTo(WatchTarget.WaitMainMenu);
                        continue;
                    }

                    // ②-3「是否保留登录记录」窗（退出确认窗，一弹出来就盖住整个大厅 → 最优先）
                    //    点「退出登录点击坐标」之后同样落到两种画面之一（有历史记录 →「登录其他账号」；
                    //    没有 → 直接是账密输入页），所以下一步盯**两个区域**。
                    if (logoutWindow.detected)
                    {
                        Debug.WriteLine($"[UID/登录] → 检测到「是否保留登录记录」窗，点击确认退出登录 | 命中[{hitList}]");
                        await Task.Delay(ClickDelayMs, token);
                        ClickPoint(LogoutClickKey);
                        await MoveTo(WatchTarget.LoginOtherOrEnterGame);
                        continue;
                    }

                    // ②-A A 类（输入框空）：立即重粘，不等提示消失、不占服务器配额
                    if (missingInput.detected && phase != LoginPhase.PendingInput)
                    {
                        missingInputRetries++;
                        Debug.WriteLine(
                            $"[UID/登录] → A类空输入[{ShortKey(missingInput.hitKey)}]：账号/密码没粘进去，" +
                            $"直接重粘重来（第 {missingInputRetries} 次，不计服务器配额）");
                        await Task.Delay(ClickDelayMs, token);
                        await ReenterCredentials(username, password, uid, token);
                        phase = LoginPhase.AwaitingResult;
                        hasInputCredentials = true;
                        await MoveTo(WatchTarget.ErrorOrAgree);
                        continue;
                    }

                    // ②-B B 类（服务器校验失败）：严格计数，之后等提示消失、画面回到输入页再重试
                    if (serverError.detected)
                    {
                        if (phase != LoginPhase.ErrorShown)
                        {
                            credentialRetries++;
                            Debug.WriteLine(
                                $"[UID/登录] → B类服务器错[{ShortKey(serverError.hitKey)}]：账密不对" +
                                $"（第 {credentialRetries}/{MaxCredentialAttempts} 次）");

                            if (credentialRetries >= MaxCredentialAttempts)
                            {
                                Debug.WriteLine($"[UID/登录] → 账密已错误 {MaxCredentialAttempts} 次（含第一次输入），放弃");
                                return LoginResult.CredentialError;
                            }
                            phase = LoginPhase.ErrorShown;
                        }
                        await MoveTo(WatchTarget.EnterGame);
                        continue;
                    }

                    // ③ 同意登录窗（只在提交账密后才出现）
                    if (agree.detected)
                    {
                        Debug.WriteLine("[UID/登录] → 检测到「同意」，点击同意");
                        await Task.Delay(ClickDelayMs, token);
                        ClickRegionCenter(AgreeRegionKey);
                        phase = LoginPhase.AwaitingResult;
                        await MoveTo(WatchTarget.ClickEnter);
                        continue;
                    }

                    // ④ 账密输入页（进入游戏）—— 排在登录大厅那些按钮之前：
                    //    已经站在输入页了，首要任务就是把账密输进去。
                    //    ★ 命中它之后，下面那些回退型分支根本走不到，所以不用再加 !atInputPage 守卫。
                    //    ★★ **无条件重新输入**（不管 phase 是什么）：能回落到定位检测、并且还看得见输入页，
                    //       就说明上一次输入**没有走到「同意」→「点击进入」这两关** —— 可能被后来弹出的窗
                    //       口盖掉了，也可能压根没粘进输入框。**不能"粘过一次就算登录过"**，
                    //       只有过了「同意」+「点击进入」才算真正登录成功，没成功之前回到输入页就该重来。
                    //       （重粘本身不占 B 类配额、也不会冻结账号；配额只由服务器返回的错误累计。）
                    if (enterGame.detected)
                    {
                        bool isRetry = phase != LoginPhase.PendingInput;
                        Debug.WriteLine(
                            "[UID/登录] → 账密输入页，" +
                            (isRetry
                                ? "上一次输入没走到「同意/点击进入」，重新输入并回车提交"
                                : "输入账号密码并回车提交"));
                        await Task.Delay(ClickDelayMs, token);
                        await ReenterCredentials(username, password, uid, token);
                        phase = LoginPhase.AwaitingResult;
                        hasInputCredentials = true;
                        await MoveTo(WatchTarget.ErrorOrAgree);
                        continue;
                    }

                    // ⑤-2「登录其他账号」—— 图层在「登录或退出」之上，必须排它前面
                    if (loginOther.detected)
                    {
                        Debug.WriteLine("[UID/登录] → 检测到「登录其他账号」，点击打开账密登录页");
                        await Task.Delay(ClickDelayMs, token);
                        ClickRegionCenter(LoginOtherRegionKey);
                        await MoveTo(WatchTarget.EnterGame);
                        continue;
                    }

                    // ⑤-3「登录或退出」（大厅里没有账号在线时显示的是它，而不是「点击进入」）
                    //    → 点它，之后会落到两种画面之一，所以下一步盯**两个区域**：
                    //      ①「登录其他账号」（有历史登录记录时）→ 点开它才是账密输入页；
                    //      ②「进入游戏」= 账密输入页本身（没有历史记录时直接给输入页）。
                    if (loginLogout.detected)
                    {
                        Debug.WriteLine($"[UID/登录] → 登录大厅且无账号在线，点击「登录或退出」 | 命中[{hitList}]");
                        await Task.Delay(ClickDelayMs, token);
                        ClickRegionCenter(LoginLogoutRegionKey);
                        // ★ 不能只盯「进入游戏」——有登录记录时先弹的是「登录其他账号」，那样会卡住不动。
                        await MoveTo(WatchTarget.LoginOtherOrEnterGame);
                        continue;
                    }

                    // ⑤-4「点击进入」的第一种含义：大厅里有**别人**的账号在线。
                    //    ★ 绝不能点「点击进入」，改点「登录或退出区域」走退出流程。
                    if (clickEnter.detected)
                    {
                        Debug.WriteLine(
                            $"[UID/登录] → 大厅已有账号在线且本账号还没输入过账密 → " +
                            $"不点「点击进入」（那是别人的账号），改点「登录或退出」走退出流程 | 命中[{hitList}]");
                        await Task.Delay(ClickDelayMs, token);
                        ClickRegionCenter(LoginLogoutRegionKey);
                        await MoveTo(WatchTarget.KeepRecord);
                        continue;
                    }

                    // ⑦ 用户中心（节点1 第 5 步）：原地点一下 → TAB → ENTER → ENTER 退号
                    if (userCenter.detected)
                    {
                        Debug.WriteLine("[UID/登录] → 用户中心已加载，原地点击 → TAB → ENTER → ENTER，退出当前账号");
                        await Task.Delay(UserCenterDelayMs, token);
                        ClickRegionCenter(JumpRegionKey);
                        await Task.Delay(ClickDelayMs, token);
                        SimulationService.PressTab();
                        await Task.Delay(ClickDelayMs, token);
                        SimulationService.PressEnter();
                        await Task.Delay(ClickDelayMs, token);
                        SimulationService.PressEnter();
                        await MoveTo(WatchTarget.LoginOtherOrEnterGame);
                        continue;
                    }

                    // ⑧ 账户跳转（节点1 第 4 步）
                    if (jump.detected)
                    {
                        Debug.WriteLine("[UID/登录] → 检测到账户跳转，点击跳转");
                        await Task.Delay(ClickDelayMs, token);
                        ClickRegionCenter(JumpRegionKey);
                        await MoveTo(WatchTarget.UserCenter);
                        continue;
                    }

                    // ⑨ 设置界面（节点1 第 3 步）：先点「账户」，跳转按钮才出来
                    if (settings.detected)
                    {
                        Debug.WriteLine("[UID/登录] → 设置界面，先点「账户」");
                        await Task.Delay(ClickDelayMs, token);
                        ClickPoint(AccountClickKey);
                        await MoveTo(WatchTarget.Jump);
                        continue;
                    }

                    // ⑩ 派蒙菜单（节点1 第 2 步）：点左下角「设置」
                    if (paimon.detected)
                    {
                        Debug.WriteLine("[UID/登录] → 派蒙菜单，点击「设置」");
                        await Task.Delay(ClickDelayMs, token);
                        ClickPoint(SettingsClickKey);
                        await MoveTo(WatchTarget.Settings);
                        continue;
                    }

                    // ⑪ 主界面（节点1 第 1 步）：这一下 ESC 正好打开派蒙菜单
                    if (main.detected)
                    {
                        Debug.WriteLine("[UID/登录] → 主界面，按 ESC 打开派蒙菜单");
                        SimulationService.PressEscape();
                        await MoveTo(WatchTarget.EscMenu);
                        continue;
                    }

                    // ⑫-A 刚领过空月祝福、却还是啥画面都认不出来 →
                    //     多半停在「领取成功」界面：它既不认月卡模板，ESC 也关不掉。
                    //     补一次左键点掉它，别让 ⑫ 一圈圈白按 ESC。
                    if (lastMoonBlessingTicks > 0 &&
                        Environment.TickCount64 - lastMoonBlessingTicks >= MoonBlessingExtraClickMs)
                    {
                        Debug.WriteLine(
                            $"[UID/登录] [定位检测] 领取空月祝福后 {MoonBlessingExtraClickMs}ms 仍认不出画面" +
                            " → 补一次左键点掉「领取成功」界面");
                        ClickRegionCenter(MoonBlessingRegionKey);
                        lastMoonBlessingTicks = Environment.TickCount64;
                        await Task.Delay(LocatingIntervalMs, token);
                        continue;
                    }

                    // ⑫ 一个都没检测到 → 按 ESC 把挡在前面的界面关掉，下轮再定位
                    Debug.WriteLine("[UID/登录] [定位检测] 未检测到任何目标，按 ESC 关掉其他界面");
                    SimulationService.PressEscape();
                    await Task.Delay(LocatingIntervalMs, token);
                    continue;
                }

                // ==========================================================
                // 二、指定区域检测：只盯当前这一步，10 秒没反应就回定位检测
                // ==========================================================
                switch (target)
                {
                    case WatchTarget.EscMenu:
                    {
                        var r = Detect(PaimonRegionKey, PaimonTemplateKey);
                        if (r.detected)
                        {
                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 ESC菜单] {r.similarity * 100:F1}% → 点「设置」");
                            await Task.Delay(ClickDelayMs, token);
                            ClickPoint(SettingsClickKey);
                            await MoveTo(WatchTarget.Settings);
                            continue;
                        }
                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 ESC菜单] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    case WatchTarget.Settings:
                    {
                        var r = Detect(SettingsRegionKey, SettingsTemplateKey);
                        if (r.detected)
                        {
                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 设置界面] {r.similarity * 100:F1}% → 点「账户」");
                            await Task.Delay(ClickDelayMs, token);
                            ClickPoint(AccountClickKey);
                            await MoveTo(WatchTarget.Jump);
                            continue;
                        }
                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 设置界面] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    case WatchTarget.Jump:
                    {
                        var r = Detect(JumpRegionKey, JumpTemplateKey);
                        if (r.detected)
                        {
                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 账户跳转] {r.similarity * 100:F1}% → 点跳转");
                            await Task.Delay(ClickDelayMs, token);
                            ClickRegionCenter(JumpRegionKey);
                            await MoveTo(WatchTarget.UserCenter);
                            continue;
                        }
                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 账户跳转] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    case WatchTarget.UserCenter:
                    {
                        var r = Detect(UserCenterRegionKey, UserCenterTemplateKey);
                        if (r.detected)
                        {
                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 用户中心] {r.similarity * 100:F1}% → 原地点击 + TAB + ENTER + ENTER");
                            await Task.Delay(UserCenterDelayMs, token);
                            ClickRegionCenter(JumpRegionKey);
                            await Task.Delay(ClickDelayMs, token);
                            SimulationService.PressTab();
                            await Task.Delay(ClickDelayMs, token);
                            SimulationService.PressEnter();
                            await Task.Delay(ClickDelayMs, token);
                            SimulationService.PressEnter();
                            await MoveTo(WatchTarget.LoginOtherOrEnterGame);
                            continue;
                        }
                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 用户中心] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    // 节点1 第 5 步之后：登录大厅两种归宿，二选一，哪个命中执行哪个
                    case WatchTarget.LoginOtherOrEnterGame:
                    {
                        var lo = Detect(LoginOtherRegionKey, LoginOtherTemplateKey);
                        var eg = Detect(EnterGameRegionKey, EnterGameTemplateKey);

                        if (lo.detected)
                        {
                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 登录其他账号/输入页] 命中「登录其他账号」{lo.similarity * 100:F1}% → 点开输入页");
                            await Task.Delay(ClickDelayMs, token);
                            ClickRegionCenter(LoginOtherRegionKey);
                            await MoveTo(WatchTarget.EnterGame);
                            continue;
                        }
                        if (eg.detected)
                        {
                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 登录其他账号/输入页] 命中「输入页」{eg.similarity * 100:F1}%");
                            if (phase == LoginPhase.PendingInput)
                            {
                                await Task.Delay(ClickDelayMs, token);
                                await ReenterCredentials(username, password, uid, token);
                                phase = LoginPhase.AwaitingResult;
                                hasInputCredentials = true;
                            }
                            await MoveTo(WatchTarget.ErrorOrAgree);
                            continue;
                        }
                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 登录其他账号/输入页] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    case WatchTarget.EnterGame:
                    {
                        var r = Detect(EnterGameRegionKey, EnterGameTemplateKey);
                        if (r.detected)
                        {
                            if (phase == LoginPhase.ErrorShown)
                            {
                                // 服务器错误提示已经消失、画面回到输入页 → 可以重新输入了
                                phase = LoginPhase.PendingInput;
                            }
                            if (phase == LoginPhase.PendingInput)
                            {
                                Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 输入页] {r.similarity * 100:F1}% → 输入账号密码并回车提交");
                                await Task.Delay(ClickDelayMs, token);
                                await ReenterCredentials(username, password, uid, token);
                                phase = LoginPhase.AwaitingResult;
                                hasInputCredentials = true;
                            }
                            else
                            {
                                Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 输入页] {r.similarity * 100:F1}%，已提交过，等服务器结果");
                            }
                            await MoveTo(WatchTarget.ErrorOrAgree);
                            continue;
                        }
                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 输入页] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    // 节点2 第 2 步：**输完账密按下回车**之后只有两种结果 ——
                    //   ①「同意登录」= 粘进去了、格式也没问题，服务器准备校验；
                    //   ②「账号密码出错区域」的 **A 类**模板（请输入密码 / 请输入手机号邮箱）
                    //      = 压根没粘进输入框（本地前端就拦下了）。
                    //   所以这里**每轮同时测这两个区域**。
                    //   ★ 下面那段 B 类（账号格式错误 / 账号或密码错误）检测是**兜底**：正常流程它只在
                    //     点完「同意」之后才出现（那时轮到 ClickEnter 状态管），留在这儿是为了防止
                    //     「同意」窗被跳过/没识别出来时漏掉这唯一一次计数机会 —— `phase != ErrorShown`
                    //     守卫保证同一条错误在两个状态里**只会被计一次**。
                    case WatchTarget.ErrorOrAgree:
                    {
                        var agreeR = Detect(AgreeRegionKey, AgreeTemplateKey);
                        var missR = DetectMissingInput();
                        var errR = DetectServerError();
                        var verifyR = Detect(VerifyRegionKey, VerifyTemplateKey);

                        // ★ 最优先：账号验证失效。「请先验证身份后再登录」这种事程序替不了，
                        //   点「取消验证」把弹窗关掉就收场，别再往下试（试也还是同一个结果）。
                        if (verifyR.detected)
                        {
                            Debug.WriteLine(
                                $"[UID/登录] 第 {round} 轮 [盯 同意/粘贴失败] 命中「验证身份」" +
                                $"{verifyR.similarity * 100:F1}% → 账号验证失效，点「取消验证」并结束登录");
                            await Task.Delay(ClickDelayMs, token);
                            ClickPoint(CancelVerifyClickKey);
                            return LoginResult.VerifyInvalid;
                        }

                        if (agreeR.detected)
                        {
                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 同意/粘贴失败] 命中「同意」{agreeR.similarity * 100:F1}% → 点击同意");
                            await Task.Delay(ClickDelayMs, token);
                            ClickRegionCenter(AgreeRegionKey);
                            phase = LoginPhase.AwaitingResult;
                            await MoveTo(WatchTarget.ClickEnter);
                            continue;
                        }

                        if (missR.detected && phase != LoginPhase.PendingInput)
                        {
                            missingInputRetries++;
                            Debug.WriteLine(
                                $"[UID/登录] 第 {round} 轮 [盯 同意/粘贴失败] A类粘贴失败[{ShortKey(missR.hitKey)}] → " +
                                $"重粘重来（第 {missingInputRetries} 次，不计服务器配额）");
                            await Task.Delay(ClickDelayMs, token);
                            await ReenterCredentials(username, password, uid, token);
                            phase = LoginPhase.AwaitingResult;
                            hasInputCredentials = true;
                            await MoveTo(WatchTarget.ErrorOrAgree);
                            continue;
                        }

                        if (errR.detected)
                        {
                            if (phase != LoginPhase.ErrorShown)
                            {
                                credentialRetries++;
                                Debug.WriteLine(
                                    $"[UID/登录] 第 {round} 轮 [盯 同意/粘贴失败] B类服务器错(兜底)[{ShortKey(errR.hitKey)}]" +
                                    $"（第 {credentialRetries}/{MaxCredentialAttempts} 次）");

                                if (credentialRetries >= MaxCredentialAttempts)
                                {
                                    Debug.WriteLine($"[UID/登录] → 账密已错误 {MaxCredentialAttempts} 次（含第一次输入），放弃");
                                    return LoginResult.CredentialError;
                                }
                                phase = LoginPhase.ErrorShown;
                            }
                            // 错误提示还挂着：继续等着它自己消失（不重复计数、不碰输入框）
                            lastHitTicks = Environment.TickCount64;
                            await Task.Delay(StepIntervalMs, token);
                            continue;
                        }

                        // 走到这里说明本轮没看到服务器错误
                        if (phase == LoginPhase.ErrorShown)
                        {
                            // 错误提示消失了 → 回输入页重新输入
                            phase = LoginPhase.PendingInput;
                            await MoveTo(WatchTarget.EnterGame);
                            continue;
                        }

                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 同意/粘贴失败] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    // 节点2 第 3 步：点了「同意」之后服务器开始校验账密，结果同样有两种 →
                    //   **每轮同时测两个区域**：
                    //     ①「点击进入」—— 校验通过（没出错才会有这个按钮）→ 点它并转去等主界面；
                    //     ②「账号密码出错区域」的 **B 类**模板（账号格式错误 / 账号或密码错误）
                    //        —— 服务器判定账密不对。
                    //   ★★ 这四个模板都**只显示约 3 秒就自己消失**，本状态 100ms 一轮，来得及抓到；
                    //      抓不到的话这次就白等，只能靠超时回落到定位检测重新来。
                    case WatchTarget.ClickEnter:
                    {
                        var ce = Detect(ClickEnterRegionKey, ClickEnterTemplateKey);
                        var errR = DetectServerError();

                        if (ce.detected)
                        {
                            if (hasInputCredentials)
                            {
                                Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 点击进入] {ce.similarity * 100:F1}% → 服务器校验通过，点进去，转去等主界面");
                                await Task.Delay(ClickDelayMs, token);
                                ClickRegionCenter(ClickEnterRegionKey);
                                await MoveTo(WatchTarget.WaitMainMenu);
                                continue;
                            }

                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 点击进入] {ce.similarity * 100:F1}% → 别人的账号在线，改点「登录或退出」");
                            await Task.Delay(ClickDelayMs, token);
                            ClickRegionCenter(LoginLogoutRegionKey);
                            await MoveTo(WatchTarget.KeepRecord);
                            continue;
                        }

                        if (errR.detected)
                        {
                            if (phase != LoginPhase.ErrorShown)
                            {
                                credentialRetries++;
                                Debug.WriteLine(
                                    $"[UID/登录] 第 {round} 轮 [盯 点击进入] B类服务器错[{ShortKey(errR.hitKey)}]" +
                                    $"（第 {credentialRetries}/{MaxCredentialAttempts} 次）");

                                if (credentialRetries >= MaxCredentialAttempts)
                                {
                                    Debug.WriteLine($"[UID/登录] → 账密已错误 {MaxCredentialAttempts} 次（含第一次输入），放弃");
                                    return LoginResult.CredentialError;
                                }
                                phase = LoginPhase.ErrorShown;
                            }
                            // 错误提示还挂着（约 3 秒）：原地等它自己消失，不重复计数、不碰画面
                            lastHitTicks = Environment.TickCount64;
                            await Task.Delay(StepIntervalMs, token);
                            continue;
                        }

                        // 本轮没看到服务器错误
                        if (phase == LoginPhase.ErrorShown)
                        {
                            // 错误提示消失了 → 回输入页重新输入（这次重试不额外占配额，配额只计新的 B 类错误）
                            Debug.WriteLine("[UID/登录] [盯 点击进入] B类错误提示已消失 → 回输入页重新输入");
                            phase = LoginPhase.PendingInput;
                            await MoveTo(WatchTarget.EnterGame);
                            continue;
                        }

                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 点击进入] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    // 节点2 第 4 步：点过「点击进入」之后等主界面 —— **登录成功的最后一道关卡**。
                    //   点「点击进入」只是开始加载，要等游戏真正进到主界面（左上角派蒙头像出现）
                    //   才算登录完成；在此之前流程不结束。
                    //   ★ 这期间可能弹出「空月祝福」（月卡领取界面）**盖住主界面** ——
                    //     所以每轮**两个都检测**：谁先出现就应对谁，但最终仍必须等到主界面。
                    //   ★★ **这里没有"超时回落定位检测"**：一旦回落，定位检测看到主界面会按 ESC
                    //      去退号（那是给"从游戏内退出当前账号"用的分支），会把一次已经成功的登录毁掉。
                    //      所以就在这一个状态里安心等 —— 用户等不及可以点「停止登录」取消。
                    case WatchTarget.WaitMainMenu:
                    {
                        // ① 空月祝福：领掉它（盖着主界面，不点掉永远等不到主界面）
                        var moon = DetectMoonBlessing();
                        if (moon.detected)
                        {
                            Debug.WriteLine(
                                $"[UID/登录] 第 {round} 轮 [等 主界面] 检测到「空月祝福」" +
                                $"{moon.similarity * 100:F1}%({ShortKey(moon.hitKey)}) → 连点 5 次领掉");
                            SetLine2(Line2MoonBlessing);
                            await ClickMoonBlessingAsync(token);
                            // 重新计时：领完会变成「领取成功」界面，5 秒后还看不到主界面就补一下
                            lastMoonBlessingTicks = Environment.TickCount64;
                            continue;
                        }

                        // 没在领空月祝福了 → 第二行字幕回到「正在进入游戏」
                        SetLine2(DescribeStep(WatchTarget.WaitMainMenu));

                        // ② 主界面：出现才算真的进到游戏里
                        var main = Detect(MainRegionKey, MainTemplateKey);
                        if (main.detected)
                        {
                            Debug.WriteLine(
                                $"[UID/登录] 第 {round} 轮 [等 主界面] {main.similarity * 100:F1}% → 已进入游戏，登录成功");
                            return LoginResult.Success;
                        }

                        // ③ 「点击进入」还在（上一下没点上/被别的窗口挡了）→ 再点一次，然后继续等
                        var ce = Detect(ClickEnterRegionKey, ClickEnterTemplateKey);
                        if (ce.detected && hasInputCredentials)
                        {
                            Debug.WriteLine(
                                $"[UID/登录] 第 {round} 轮 [等 主界面] 仍看到「点击进入」{ce.similarity * 100:F1}% → 上一下没点上，再点一次");
                            await Task.Delay(ClickDelayMs, token);
                            ClickRegionCenter(ClickEnterRegionKey);
                            await Task.Delay(GameEnterPollMs, token);
                            continue;
                        }

                        // ④ 点过空月祝福、5 秒还看不到主界面 → 多半停在「领取成功」界面
                        //    （那个界面认不出空月祝福，但也还不是主界面）→ 补一次左键点掉它
                        if (lastMoonBlessingTicks > 0 &&
                            Environment.TickCount64 - lastMoonBlessingTicks >= MoonBlessingExtraClickMs)
                        {
                            Debug.WriteLine(
                                $"[UID/登录] [等 主界面] 领取空月祝福后 {MoonBlessingExtraClickMs}ms 仍看不到主界面" +
                                " → 补一次左键点掉「领取成功」界面");
                            ClickRegionCenter(MoonBlessingRegionKey);
                            lastMoonBlessingTicks = Environment.TickCount64;
                            await Task.Delay(GameEnterPollMs, token);
                            continue;
                        }

                        await Task.Delay(GameEnterPollMs, token);
                        continue;
                    }

                    case WatchTarget.KeepRecord:
                    {
                        var r = Detect(LogoutWindowRegionKey, LogoutWindowTemplateKey);
                        if (r.detected)
                        {
                            Debug.WriteLine($"[UID/登录] 第 {round} 轮 [盯 是否保留登录记录] {r.similarity * 100:F1}% → 点确认退出登录");
                            await Task.Delay(ClickDelayMs, token);
                            ClickPoint(LogoutClickKey);
                            // ★ 退出之后可能是「登录其他账号」，也可能是账密输入页本身 → 盯两个区域
                            await MoveTo(WatchTarget.LoginOtherOrEnterGame);
                            continue;
                        }
                        if (StepTimedOut())
                        {
                            Debug.WriteLine($"[UID/登录] [盯 是否保留登录记录] 连续 {StepTimeoutMs}ms 未命中 → 回定位检测");
                            await MoveTo(WatchTarget.Locating);
                            continue;
                        }
                        await Task.Delay(StepIntervalMs, token);
                        continue;
                    }

                    default:
                        await MoveTo(WatchTarget.Locating);
                        continue;
                }
            }
        }

        /// <summary>
        /// 「空月祝福」领取界面的连点序列：一共点 **5 次**鼠标左键（保险起见全点到）。
        /// <para>
        /// 节奏：先点 1 次 → 等 200ms → **连着再点 4 次**（每次间隔 100ms）。
        /// </para>
        /// <para>★ 每次都点在 <see cref="MoonBlessingRegionKey"/> 区域中心 —— 领完界面会变，
        ///   但坐标不变，继续点在同一位置无害（点掉「领取成功」也是靠它）。</para>
        /// </summary>
        private static async Task ClickMoonBlessingAsync(CancellationToken token)
        {
            // 第 1 次
            ClickRegionCenter(MoonBlessingRegionKey);
            await Task.Delay(200, token);

            // 第 2 次
            ClickRegionCenter(MoonBlessingRegionKey);
            await Task.Delay(100, token);

            // 第 3 次
            ClickRegionCenter(MoonBlessingRegionKey);
            await Task.Delay(100, token);

            // 第 4 次
            ClickRegionCenter(MoonBlessingRegionKey);
            await Task.Delay(100, token);

            // 第 5 次
            ClickRegionCenter(MoonBlessingRegionKey);
        }

        /// <summary>
        /// 往账密页重新输入一次账号密码并回车提交（「进入游戏」阶段与「出错重试」共用的动作）。
        /// 点击输入框后若框里已有内容会自动全选，所以直接粘就覆盖掉旧值。
        /// 密码粘完立刻用 UID 顶掉剪贴板里的密码。
        /// </summary>
        private static async Task ReenterCredentials(string username, string password, string uid, CancellationToken token)
        {
            // ★★ 粘贴之前**先清空输入框**（Ctrl+A → Backspace）。
            //    现在「回到输入页就无条件重输」，重输次数比过去多得多；
            //    不清框的话，上一次若真的粘进去了，再粘一次就首尾相接变成两份
            //    （「账号账号」「密码密码」），提交出去必然是错的。
            // 账号
            ClickPoint(AccountInputClickKey);
            await Task.Delay(ClickDelayMs, token);
            SimulationService.ClearInputField();
            await Task.Delay(ClickDelayMs, token);
            SimulationService.PasteText(username);
            await Task.Delay(AfterAccountPasteMs, token);

            // 密码：点一下密码输入框把焦点切过去（不用 TAB，TAB 偶尔会把密码粘错框）
            ClickPoint(PasswordInputClickKey);
            await Task.Delay(ClickDelayMs, token);
            SimulationService.ClearInputField();
            await Task.Delay(ClickDelayMs, token);
            SimulationService.PasteText(password);

            // 密码已经粘进输入框，剪贴板里那份立刻用 UID 顶掉 ——
            // UID 是公开信息，留在剪贴板里无所谓；密码不行，万一粘到别处就漏了。
            SimulationService.SetClipboardText(uid ?? string.Empty);

            await Task.Delay(ClickDelayMs, token);

            // 不点「进入游戏」，直接回车提交 —— 会弹出带「同意」的确认登录窗口
            SimulationService.PressEnter();
        }

        #endregion

        #region 点击

        /// <summary>
        /// 点击一个 <see cref="CoordinateFormats.Point"/> 坐标（缓存里已经是屏幕坐标，可直接用）。
        /// Point 是值类型，<c>GetCached&lt;T&gt;</c> 要求引用类型，所以照自动伐木的做法走 object 再强转。
        /// </summary>
        internal static void ClickPoint(string pointKey)
        {
            try
            {
                var pointObj = CoordinateFormats.GetCached<object>(pointKey);
                if (pointObj == null)
                {
                    Debug.WriteLine($"[UID/登录] {pointKey} 未缓存，跳过点击");
                    return;
                }

                var point = (CoordinateFormats.Point)pointObj;
                Debug.WriteLine($"[UID/登录] 点击 {pointKey}: ({point.X}, {point.Y})");
                SimulationService.LeftClickAt(point.X, point.Y);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 点击 {pointKey} 出错：{ex.Message}");
            }
        }

        /// <summary>点击一个区域的中心（缓存里已经是屏幕坐标）。</summary>
        internal static void ClickRegionCenter(string regionKey)
        {
            try
            {
                var rect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(regionKey);
                if (rect == null)
                {
                    Debug.WriteLine($"[UID/登录] {regionKey} 未缓存，跳过点击");
                    return;
                }

                int x = (rect.Rect.X1 + rect.Rect.X2) / 2;
                int y = (rect.Rect.Y1 + rect.Rect.Y2) / 2;
                Debug.WriteLine($"[UID/登录] 点击 {regionKey} 中心: ({x}, {y})");
                SimulationService.LeftClickAt(x, y);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 点击 {regionKey} 出错：{ex.Message}");
            }
        }

        #endregion

        #region 识别

        /// <summary>
        /// 在 <paramref name="regionKey"/> 区域里找 <paramref name="templateKey"/> 模板。
        /// 阈值取区域自带的 <c>Threshold</c>（0.9），与主程序里各功能的做法一致。
        /// </summary>
        internal static (bool detected, double similarity) Detect(string regionKey, string templateKey)
        {
            try
            {
                // 坐标缓存没准备好（窗口信息未上报）时识别没有意义
                if (!CoordinateFormats.CacheReady) return (false, 0);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(regionKey);
                if (matchRect == null) return (false, 0);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return (false, 0);

                // ★ 模板是**缓存位图**，绝对不能 using —— 释放掉下次就取不到了。
                //   宿主里所有功能也都是直接取用不释放的。
                var template = TemplateManager.GetCachedTemplate(templateKey);
                if (template == null)
                {
                    WarnMissingTemplateOnce(templateKey);
                    return (false, 0);
                }

                double threshold = matchRect.Threshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);
                if (result == null) return (false, 0);

                return (result.Similarity >= threshold, result.Similarity);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 识别 {regionKey} 出错：{ex.Message}");
                return (false, 0);
            }
        }

        /// <summary>
        /// 在 <paramref name="regionKey"/> 区域里逐个匹配 <paramref name="keys"/>，
        /// 命中**任意一个**就算中。返回命中的 key（日志里能看出到底是哪个）和相似度（没命中时报最高的那个）。
        /// </summary>
        internal static (bool detected, double similarity, string hitKey) DetectAnyOf(string regionKey, string[] keys)
        {
            double best = 0;
            foreach (var key in keys)
            {
                var r = Detect(regionKey, key);
                if (r.detected) return (true, r.similarity, key);
                if (r.similarity > best) best = r.similarity;
            }
            return (false, best, string.Empty);
        }

        /// <summary>
        /// ★ 把 <paramref name="regionKey"/> 区域里**所有命中**都找出来（不只是"有没有"）。
        /// <para>
        /// 每个命中给出**屏幕坐标**（截出来的图是归一到 1080 基准的，要先 <c>/GetCaptureScale()</c>
        /// 还原成实际像素，再加上区域左上角 —— 与 <see cref="Detect"/> 那套换算一致）、
        /// 相似度、以及命中的模板 key。
        /// </para>
        /// <para>★ 用途：把"检测到了什么、检测在哪里"直接标到屏幕上看（<c>DebugMarkerOverlay</c>）。</para>
        /// </summary>
        internal static List<(int X, int Y, double Similarity, string Key)> DetectAll(
            string regionKey, string[] templateKeys)
        {
            var hits = new List<(int X, int Y, double Similarity, string Key)>();

            try
            {
                if (!CoordinateFormats.CacheReady) return hits;

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(regionKey);
                if (matchRect == null) return hits;

                hits = DetectAllInRect(matchRect, templateKeys);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 扫描 {regionKey} 的命中出错：{ex.Message}");
            }

            return hits;
        }

        /// <summary>
        /// ★★ 用**临时矩形**做同一套「截图 + 多模板全命中」的扫描。
        /// <para>
        /// 给"区域不固定"的场景用 —— 例如「从最新那条队友消息的位置往下 200 像素」这种，
        /// 矩形每一轮都要按检测结果现算，没法写进 <see cref="CoordinateFormats"/> 的固定区域表。
        /// </para>
        /// <para>
        /// ★ <paramref name="matchRect"/> 必须是**屏幕坐标**（和 <c>GetCached</c> 返回的那种一样，别再 ToScreen）。
        /// </para>
        /// <para>
        /// ★★★ <paramref name="roughScale"/> &gt; 1 时走<b>粗匹配</b>（<see cref="ImageRecognition.MatchTemplateScaledBest"/>）：
        /// 把图和模板一起缩小这么多倍先粗扫一遍，再回原图局部精匹配。
        /// 区域大（比如整块聊天列表）时快很多，代价是**每张模板只留最像的那一个**候选
        /// —— 这里只需要"命中哪一句"，不需要几百个滑动窗口的命中点。
        /// </para>
        /// </summary>
        internal static List<(int X, int Y, double Similarity, string Key)> DetectAllInRect(
            CoordinateFormats.MatchRect matchRect, string[] templateKeys,
            int roughScale = 1, bool requireWordSize = false, string[]? skipWordSizeKeys = null)
        {
            var hits = new List<(int X, int Y, double Similarity, string Key)>();

            try
            {
                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return hits;

                double scale = ImageRecognition.GetCaptureScale();
                if (scale <= 0) scale = 1.0;

                // ★★★ 需要几何校验时，先做一张"白字掩码"（整张截图只算一次，所有模板共用）。
                bool[]? whiteMask = null;
                int maskWidth = 0, maskHeight = 0;
                if (requireWordSize)
                    whiteMask = BuildWhiteMask(source, out maskWidth, out maskHeight);

                foreach (var key in templateKeys)
                {
                    var template = TemplateManager.GetCachedTemplate(key);
                    if (template == null)
                    {
                        WarnMissingTemplateOnce(key);
                        continue;
                    }

                    // ★ 表情包那几张（不是白字）不该走白字几何校验 —— 由调用方用 skipWordSizeKeys 排除
                    bool needWordSize = whiteMask != null
                        && (skipWordSizeKeys == null || Array.IndexOf(skipWordSizeKeys, key) < 0);

                    if (roughScale > 1)
                    {
                        var best = ImageRecognition.MatchTemplateScaledBest(
                            source, template, matchRect.Threshold, roughScale, key);
                        if (!best.Matched) continue;

                        if (needWordSize &&
                            !PassesWordSize(whiteMask, maskWidth, maskHeight,
                                            best.X, best.Y, template.Width, template.Height))
                            continue;

                        hits.Add((
                            matchRect.Rect.X1 + (int)Math.Round(best.X / scale),
                            matchRect.Rect.Y1 + (int)Math.Round(best.Y / scale),
                            best.Similarity,
                            key));
                        continue;
                    }

                    foreach (var hit in ImageRecognition.MatchTemplateAll(source, template, matchRect.Threshold))
                    {
                        if (needWordSize &&
                            !PassesWordSize(whiteMask, maskWidth, maskHeight,
                                            hit.X, hit.Y, template.Width, template.Height))
                            continue;

                        hits.Add((
                            matchRect.Rect.X1 + (int)Math.Round(hit.X / scale),
                            matchRect.Rect.Y1 + (int)Math.Round(hit.Y / scale),
                            hit.Similarity,
                            key));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 临时矩形扫描出错：{ex.Message}");
            }

            return hits;
        }

        // ==================== 白字几何校验（2026-10-18 用户方案） ====================

        /// <summary>白色阈值：B/G/R 三个分量都不低于它才算"白字"（聊天文字纯白，抗锯齿边偏灰）。</summary>
        private const byte WhitePixelLevel = 200;

        /// <summary>字与字的间隙容忍（缩放后像素）：连续这么多列没有白像素，就算"不是一个词"。</summary>
        private const int WordGapColumns = 10;

        /// <summary>宽度比例上限 —— 超过它说明消息比模板长（例如「你可以发送申请权限」）。</summary>
        private const double MaxWordWidthRatio = 1.35;

        /// <summary>高度比例允许范围（描边 / 抗锯齿会让高度浮动一点）。</summary>
        private const double MinWordHeightRatio = 0.6;

        /// <inheritdoc cref="MinWordHeightRatio"/>
        private const double MaxWordHeightRatio = 1.9;

        /// <summary>最近一次几何校验的说明（给聊天日志看，排查"为什么没认出来"用）。</summary>
        internal static string LastWordSizeNote = "";

        /// <summary>
        /// 把截图里的**白字**标成掩码（1 = 白）。只用来做几何校验，不参与匹配本身。
        /// <para>★ 截图是 GDI+ 的 <c>Bitmap</c>（<c>CaptureRegionScaled</c> 返回的那种），
        /// 所以走 <c>LockBits</c> 直接读像素 —— 逐像素 <c>GetPixel</c> 太慢。</para>
        /// </summary>
        private static bool[] BuildWhiteMask(System.Drawing.Bitmap source, out int width, out int height)
        {
            width = source.Width;
            height = source.Height;
            var mask = new bool[width * height];

            var rect = new System.Drawing.Rectangle(0, 0, width, height);
            var data = source.LockBits(
                rect,
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                int stride = Math.Abs(data.Stride);
                var buffer = new byte[stride * height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    int maskRow = y * width;

                    for (int x = 0; x < width; x++)
                    {
                        int p = row + x * 4;   // BGRA
                        mask[maskRow + x] = buffer[p] >= WhitePixelLevel
                                         && buffer[p + 1] >= WhitePixelLevel
                                         && buffer[p + 2] >= WhitePixelLevel;
                    }
                }
            }
            finally
            {
                source.UnlockBits(data);
            }

            return mask;
        }

        /// <summary>
        /// ★★★ 命中位置的**几何校验**（2026-10-18 用户方案）：
        /// 在命中那一行上，向左向右一直扩到"字的间隙"为止，量出**这个词**有多宽多高；
        /// 只有它和模板差不多才算是模板本体。
        /// <para>
        /// 目的：队友发「你可以发送申请权限」时，「申请权限」模板会匹配到后半段，
        /// 但往左一扩就会发现左边还连着「你可以发送」⇒ 量出来的词明显比模板宽 ⇒ 判为不是。
        /// </para>
        /// </summary>
        private static bool PassesWordSize(
            bool[] mask, int maskWidth, int maskHeight,
            int hitX, int hitY, int templateWidth, int templateHeight)
        {
            if (hitX < 0 || hitY < 0 || hitX >= maskWidth || hitY >= maskHeight) return false;

            int scanTop = Math.Max(0, hitY - 2);
            int scanBottom = Math.Min(maskHeight - 1, hitY + templateHeight + 1);

            // 这一列（在命中行范围内）有没有白像素
            bool ColumnHasWhite(int x)
            {
                if (x < 0 || x >= maskWidth) return false;
                for (int y = scanTop; y <= scanBottom; y++)
                {
                    if (mask[y * maskWidth + x]) return true;
                }
                return false;
            }

            // 向左扩
            int left = hitX;
            int gap = 0;
            for (int x = hitX - 1; x >= 0 && gap <= WordGapColumns; x--)
            {
                if (ColumnHasWhite(x)) { left = x; gap = 0; }
                else gap++;
            }

            // 向右扩
            int right = Math.Min(maskWidth - 1, hitX + templateWidth - 1);
            gap = 0;
            for (int x = hitX + templateWidth; x < maskWidth && gap <= WordGapColumns; x++)
            {
                if (ColumnHasWhite(x)) { right = x; gap = 0; }
                else gap++;
            }

            int wordWidth = right - left + 1;

            // 在"词"的水平范围内量上下边界
            int top = int.MaxValue, bottom = -1;
            for (int y = scanTop; y <= scanBottom; y++)
            {
                int row = y * maskWidth;
                for (int x = left; x <= right; x++)
                {
                    if (!mask[row + x]) continue;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            }

            if (bottom < 0) return false;

            int wordHeight = bottom - top + 1;

            double widthRatio = (double)wordWidth / templateWidth;
            double heightRatio = (double)wordHeight / templateHeight;

            bool pass = widthRatio <= MaxWordWidthRatio
                     && heightRatio >= MinWordHeightRatio
                     && heightRatio <= MaxWordHeightRatio;

            LastWordSizeNote = $"词 {wordWidth}×{wordHeight} / 模板 {templateWidth}×{templateHeight} " +
                               $"（宽比 {widthRatio:F2} 高比 {heightRatio:F2}）{(pass ? "通过" : "拒绝")}";

            return pass;
        }

        /// <summary>
        /// A 类检测：输入框是空的（请输入密码 / 请输入手机号邮箱）。
        /// 本地前端校验，回车后立刻出现，**没提交到服务器**。
        /// </summary>
        private static (bool detected, double similarity, string hitKey) DetectMissingInput()
            => DetectAnyOf(ErrorRegionKey, MissingInputTemplateKeys);

        /// <summary>
        /// B 类检测：服务器校验失败（账号格式错误 / 账号或密码错误）。
        /// 这类才是「账密确实不对」，会触发风控。
        /// </summary>
        private static (bool detected, double similarity, string hitKey) DetectServerError()
            => DetectAnyOf(ErrorRegionKey, ServerErrorTemplateKeys);

        /// <summary>
        /// 「空月祝福」检测：两个模板任中一个就算中（月卡领取界面有两套皮肤/文案）。
        /// </summary>
        private static (bool detected, double similarity, string hitKey) DetectMoonBlessing()
            => DetectAnyOf(MoonBlessingRegionKey, MoonBlessingTemplateKeys);

        /// <summary>
        /// 「连接已断开」检测：网络失败 / 账号被顶。两个模板任中一个就算中。
        /// ★ 用 <see cref="hitKey"/> 区分到底中了哪一个 —— 两者要走的应对完全不同。
        /// </summary>
        public static (bool detected, double similarity, string hitKey) DetectConnectionLost()
            => DetectAnyOf(ConnectionLostRegionKey, ConnectionLostTemplateKeys);

        /// <summary>
        /// 模板 key 的短名：`其他模版_登录账号_请输入密码` → `请输入密码`（日志里打印用）。
        /// </summary>
        private static string ShortKey(string templateKey)
        {
            if (string.IsNullOrEmpty(templateKey)) return string.Empty;
            int idx = templateKey.LastIndexOf('_');
            return idx >= 0 ? templateKey.Substring(idx + 1) : templateKey;
        }

        /// <summary>
        /// 模板不存在时提醒一次（模板要用户自己截图放到资源包里）。
        /// 少了模板的检测会恒为 false，不说一声的话表现为「流程卡住不动」。
        /// </summary>
        private static void WarnMissingTemplateOnce(string templateKey)
        {
            if (_missingTemplateWarned) return;
            _missingTemplateWarned = true;

            Debug.WriteLine(
                $"[UID/登录] ⚠ 模板 {templateKey} 不存在 → 对应区域将永远检测不到。" +
                "请把截图按 其他模版/退出游戏/<名字>.png 放进资源包后重新生成。");
        }

        #endregion
    }
}
