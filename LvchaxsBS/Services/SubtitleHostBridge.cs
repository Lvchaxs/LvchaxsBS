using System;
using System.Diagnostics;
using System.Windows;
using LvchaxsBS.Core;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 把「客户区字幕」这项能力交给 UID 模块的命令处理器。
    ///
    /// <para>
    /// 两者之间没有编译期引用（模块是 <c>Assembly.LoadFrom</c> 载入的），所以沿用
    /// <see cref="ModalOverlayService.HandleModuleCommand"/> 那套：宿主注入一个
    /// <c>Action&lt;string, object?&gt;</c>，模块用「命令 + 参数」驱动这里。
    /// </para>
    /// <para>
    /// 为什么不把显示层搬进共享库：<c>SubtitleOverlayService</c> 依赖
    /// <c>WindowFocusService</c> / <c>AssetLocator</c> / <c>ResourceImageHelper</c>
    /// —— 这三个是<b>故意留在宿主</b>的（缺共享 dll 时 UI 还要能正常渲染）。
    /// 而且一个进程只能有一个覆盖层窗口，模块复用它最自然。
    /// </para>
        /// <para>
        /// 字幕有<b>两行</b>（和自动清药一样）：两行都带头像 —— 第一行是当前步骤，
        /// 第二行是汇总行（登录 = 账号 + 已运行时长；自动清药 = 已储存数量 + 运行时长）。
        /// <see cref="CmdAvatar2"/> 保留着，需要时可以把第二行的头像关掉。
        /// 两行共用同一个位置 key，第二行由覆盖层自己往下偏移。
        /// </para>
    /// <para>
    /// 命令表与 UID 侧 <c>Utils/UI/SubtitlePresenter.cs</c> 一一对应。
    /// </para>
    /// </summary>
    public static class SubtitleHostBridge
    {
        // ---- 第一行 ----
        /// <summary>命令：开始第一行（重掷头像 + 定位 + 显示）。参数 = 位置坐标 key。</summary>
        public const string CmdStart = "subtitle:start";
        /// <summary>命令：设置第一行正文。参数 = 文本。</summary>
        public const string CmdText = "subtitle:text";
        /// <summary>
        /// 命令：第一行设成三段富文本（中间段用高亮色，账号名用）。
        /// 参数 = <c>string[3]</c>：{ 前缀, 高亮段, 后缀 }。
        /// </summary>
        public const string CmdRich = "subtitle:rich";
        /// <summary>命令：追加第一行后缀。参数 = 后缀。</summary>
        public const string CmdSuffix = "subtitle:suffix";
        /// <summary>命令：收起第一行。</summary>
        public const string CmdHide = "subtitle:hide";
        /// <summary>命令：设置气泡底色不透明度。参数 = 0~100。</summary>
        public const string CmdOpacity = "subtitle:opacity";

        // ---- 第二行 ----
        /// <summary>命令：开始第二行（定位 + 显示）。参数 = 位置坐标 key。</summary>
        public const string CmdStart2 = "subtitle2:start";
        /// <summary>命令：设置第二行正文。参数 = 文本。</summary>
        public const string CmdText2 = "subtitle2:text";
        /// <summary>
        /// 命令：第二行设成三段富文本（中间段用高亮色）。
        /// 参数 = <c>string[3]</c>：{ 前缀, 高亮段, 后缀 }。
        /// </summary>
        public const string CmdRich2 = "subtitle2:rich";
        /// <summary>命令：收起第二行。</summary>
        public const string CmdHide2 = "subtitle2:hide";
        /// <summary>命令：第二行是否显示头像。参数 = bool（默认 true，与自动清药一致）。</summary>
        public const string CmdAvatar2 = "subtitle2:avatar";
        // ---- 第三行（联机人数提示，UID 在「登录后打开聊天界面」时常驻显示） ----
        /// <summary>命令：开始第三行（定位 + 显示）。参数 = 位置坐标 key。</summary>
        public const string CmdStart3 = "subtitle3:start";
        /// <summary>命令：设置第三行正文。参数 = 文本。</summary>
        public const string CmdText3 = "subtitle3:text";
        /// <summary>命令：第三行设成三段富文本。参数 = <c>string[3]</c>。</summary>
        public const string CmdRich3 = "subtitle3:rich";
        /// <summary>命令：收起第三行。</summary>
        public const string CmdHide3 = "subtitle3:hide";
        /// <summary>命令：第三行是否显示头像。参数 = bool。</summary>
        public const string CmdAvatar3 = "subtitle3:avatar";
        /// <summary>
        /// 命令：一次把三行的头像重新分配（空 / 荧）。无参数。
        /// 规则：1 行随机、2 行必须不同、3 行允许两个一样但不能三个都一样。
        /// </summary>
        public const string CmdAvatars = "subtitle:avatars";

        // ---- 第四行（UID 的「用户正在操作，暂缓按键」提示） ----
        /// <summary>
        /// 命令：**预热**第四行。无参数。
        /// <para>
        /// 窗口先建好、定位好，但保持纯透明常驻 —— 之后 <see cref="CmdText4"/> 只是把不透明度
        /// 打上去，触发即出现（不重建、不闪）。登录开始（三行挂出）时调一次就够。
        /// </para>
        /// </summary>
        public const string CmdPrewarm4 = "subtitle4:prewarm";
        /// <summary>命令：显示第四行并设置正文。参数 = 文本。</summary>
        public const string CmdText4 = "subtitle4:text";
        /// <summary>命令：收起第四行（**只归零不透明度，不销毁窗口**）。无参数。</summary>
        public const string CmdHide4 = "subtitle4:hide";

        /// <summary>命令：锁住「按 TAB 隐藏字幕」（登录流程运行期间用）。无参数。</summary>
        public const string CmdTabLock = "subtitle:tablock";
        /// <summary>命令：解开「按 TAB 隐藏字幕」。无参数。</summary>
        public const string CmdTabUnlock = "subtitle:tabunlock";

        /// <summary>
        /// 命令：**停掉所有正在运行的功能**（含自动清药）。无参数。
        /// <para>UID 登录流程开始前发一次 —— 有字幕的功能同时只能跑一个，新的中断旧的。</para>
        /// </summary>
        public const string CmdStopAllFuncs = "func:stopall";

        /// <summary>
        /// 「按 TAB 隐藏字幕」是否被锁住。
        /// <para>登录流程运行期间为 true —— 那会儿字幕是**进度**，收起来就不知道走到哪一步了
        /// （和自动清药运行时同理）；只有登录结束（第二行出现「登录完成-TAB隐藏」）后才解锁。</para>
        /// <para>由 UID 模块通过 <see cref="CmdTabLock"/> / <see cref="CmdTabUnlock"/> 驱动。</para>
        /// </summary>
        public static bool IsTabHideLocked { get; private set; }

        /// <summary>
        /// ★ UID 登录流程（含登录成功后的附加动作）是否正在运行。
        /// <para>
        /// 直接复用 <see cref="IsTabHideLocked"/>：它的生命周期就是这条流程 ——
        /// <c>ShowRunningSubtitles</c> 开始时加锁，成功 / 失败 / 取消 / 附加动作结束时解锁
        /// （<c>AccountLoginFlow:442/483/528/538</c>、<c>PostLoginActions:526/933</c>），
        /// **每条结束路径都会解锁**，比另起一个标志可靠（不会漏复位把功能永久锁死）。
        /// </para>
        /// <para>用途：登录期间**拒绝启动别的功能**（两者都要操作鼠标键盘、且共用第一行字幕）。</para>
        /// </summary>
        public static bool IsUidLoginRunning => IsTabHideLocked;

        /// <summary>
        /// 覆盖层是否可用。它由 <c>App.InitializeEngine()</c> 初始化，而那一步只在
        /// 共享组件存在时才跑（位置坐标来自共享库的 <c>CoordinateFormats</c>），
        /// 所以这里跟 <see cref="SharedComponent.IsAvailable"/> 保持一致。
        /// </summary>
        public static bool IsAvailable => SharedComponent.IsAvailable;

        /// <summary>模块命令入口。未知命令只记日志，不抛。</summary>
        public static void Handle(string command, object? arg)
        {
            // ★★ 一律回到 UI 线程再执行。
            // 覆盖层的窗口是 WPF 对象：<c>new Window()</c> 要求 STA，跨线程访问 <c>Window.Show()</c>
            // 也会直接抛。而模块的「登录后附加动作」是 <c>Task.Run</c> 起的（线程池 / MTA），
            // 从那里直接调 <c>subtitle3:start</c> 会在建窗那一步抛异常、被模块的 try/catch 吞掉 ——
            // 表现就是「第三行死活不出来，等下一次焦点变化（UI 线程上）才补显示」。
            // 这里统一做一次线程转换，所有命令就都不用各自操心调用方在哪个线程了。
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                try
                {
                    dispatcher.Invoke(() => HandleCore(command, arg));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UID/字幕] 命令 {command} 跨线程转发失败：{ex.Message}");
                }
                return;
            }

            HandleCore(command, arg);
        }

        /// <summary>命令的真正实现（<see cref="Handle"/> 保证它在 UI 线程上跑）。</summary>
        private static void HandleCore(string command, object? arg)
        {
            switch (command)
            {
                // ---- 第一行 ----
                case CmdStart:
                    StartLine(arg, line2: false);
                    break;

                case CmdText:
                    SubtitleOverlayService.SetSubtitle(arg as string ?? string.Empty);
                    break;

                case CmdRich:
                    if (arg is string[] parts && parts.Length == 3)
                        SubtitleOverlayService.SetSubtitleRich(parts[0], parts[1], parts[2]);
                    break;

                case CmdSuffix:
                    SubtitleOverlayService.AppendSuffix(arg as string ?? string.Empty);
                    break;

                case CmdHide:
                    SubtitleOverlayService.HideSubtitle();
                    break;

                case CmdOpacity:
                    if (arg is int percent)
                        SubtitleOverlayService.SetBackgroundOpacity(percent);
                    break;

                // ---- 第二行 ----
                case CmdStart2:
                    StartLine(arg, line2: true);
                    break;

                case CmdText2:
                    SubtitleOverlayService.SetSubtitle2(arg as string ?? string.Empty);
                    break;

                case CmdRich2:
                    if (arg is string[] parts2 && parts2.Length == 3)
                        SubtitleOverlayService.SetSubtitleRich2(parts2[0], parts2[1], parts2[2]);
                    break;

                case CmdHide2:
                    SubtitleOverlayService.HideSubtitle2();
                    break;

                case CmdAvatar2:
                    SubtitleOverlayService.SetAvatarVisible2(arg is bool visible && visible);
                    break;

                // ---- 第三行 ----
                case CmdStart3:
                    StartLine3(arg);
                    break;

                case CmdText3:
                    SubtitleOverlayService.SetSubtitle3(arg as string ?? string.Empty);
                    break;

                case CmdRich3:
                    if (arg is string[] parts3 && parts3.Length == 3)
                        SubtitleOverlayService.SetSubtitleRich3(parts3[0], parts3[1], parts3[2]);
                    break;

                case CmdHide3:
                    SubtitleOverlayService.HideSubtitle3();
                    break;

                case CmdAvatar3:
                    // ★ 变量名不能叫 visible —— 同一 switch 作用域里 CmdAvatar2 已经用过这个名字
                    SubtitleOverlayService.SetAvatarVisible3(arg is bool avatar3 && avatar3);
                    break;

                case CmdAvatars:
                    SubtitleOverlayService.AssignAvatars();
                    break;

                // ---- 第四行（用户操作提示；常驻透明，触发即显示） ----
                case CmdPrewarm4:
                    SubtitleOverlayService.PrewarmSubtitle4();
                    break;

                case CmdText4:
                    SubtitleOverlayService.SetSubtitle4(arg as string ?? string.Empty);
                    break;

                case CmdHide4:
                    SubtitleOverlayService.HideSubtitle4();
                    break;

                case CmdStopAllFuncs:
                    // UID 登录流程开始前发：把还在跑的功能全停掉（新的中断旧的）
                    Core.IconService.StopAllModules();
                    break;

                case CmdTabLock:
                    IsTabHideLocked = true;
                    Debug.WriteLine("[UID/字幕] 已锁住「TAB 隐藏字幕」（登录进行中）");
                    break;

                case CmdTabUnlock:
                    IsTabHideLocked = false;
                    Debug.WriteLine("[UID/字幕] 已解开「TAB 隐藏字幕」");
                    break;

                default:
                    Debug.WriteLine($"[UID/字幕] 未知命令：{command}");
                    break;
            }
        }

        /// <summary>
        /// 开始第三行：先收起（顺带清掉上次的正文），再定位、显示。三行共用同一个位置 key。
        /// </summary>
        private static void StartLine3(object? arg)
        {
            string positionKey = arg as string ?? string.Empty;

            SubtitleOverlayService.HideSubtitle3();
            SubtitleOverlayService.SetPositionKey(positionKey);
            SubtitleOverlayService.ShowSubtitle3();
        }

        /// <summary>
        /// 开始一行字幕：先收起这一行（顺带清掉上次的正文；第一行还清头像路径），再定位、显示。
        /// 两行共用同一个位置 key。
        /// </summary>
        private static void StartLine(object? arg, bool line2)
        {
            string positionKey = arg as string ?? string.Empty;

            if (line2)
            {
                SubtitleOverlayService.HideSubtitle2();
                SubtitleOverlayService.SetPositionKey(positionKey);
                SubtitleOverlayService.ShowSubtitle2();
            }
            else
            {
                // ★ 挂行的第一步：用**真实前台窗口**复核一次焦点。
                //   原因详见 SubtitleOverlayService.RefreshFocusState —— 缓存里的焦点一旦错位，
                //   这一轮挂出来的三行全都满足不了"显示"条件，画面上啥也看不到。
                SubtitleOverlayService.RefreshFocusState();

                // 清掉上一条的内容与头像路径 → 下次刷新时随机掷一个（空 / 荧）
                SubtitleOverlayService.HideSubtitle();          // 强制：UID 自己重开一行
                SubtitleOverlayService.ResetAvatarForNewRun();
                SubtitleOverlayService.SetPositionKey(positionKey);
                // ★ 认主 = UidLogin：第一行是共用行，钓鱼 / 烹饪 / 伐木的刷新循环在"自己没内容"时
                //   会调 HideSubtitle，不认主的话会把登录这一行整行收掉（2026-10-16 的 bug）。
                SubtitleOverlayService.ShowSubtitle(SubtitleOverlayService.SubtitleOwner.UidLogin);
            }
        }
    }
}
