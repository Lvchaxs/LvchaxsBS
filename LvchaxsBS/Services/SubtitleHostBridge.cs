using System;
using System.Diagnostics;
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
    /// 字幕有<b>两行</b>（和自动清药一样）：第一行带头像；第二行默认也带头像，
    /// 但 UID 会用 <see cref="CmdAvatar2"/> 把它关掉（登录字幕只要文字）。
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
        /// <summary>命令：收起第二行。</summary>
        public const string CmdHide2 = "subtitle2:hide";
        /// <summary>命令：第二行是否显示头像。参数 = bool（UID 传 false）。</summary>
        public const string CmdAvatar2 = "subtitle2:avatar";

        /// <summary>
        /// 覆盖层是否可用。它由 <c>App.InitializeEngine()</c> 初始化，而那一步只在
        /// 共享组件存在时才跑（位置坐标来自共享库的 <c>CoordinateFormats</c>），
        /// 所以这里跟 <see cref="SharedComponent.IsAvailable"/> 保持一致。
        /// </summary>
        public static bool IsAvailable => SharedComponent.IsAvailable;

        /// <summary>模块命令入口。未知命令只记日志，不抛。</summary>
        public static void Handle(string command, object? arg)
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

                case CmdHide2:
                    SubtitleOverlayService.HideSubtitle2();
                    break;

                case CmdAvatar2:
                    SubtitleOverlayService.SetAvatarVisible2(arg is bool visible && visible);
                    break;

                default:
                    Debug.WriteLine($"[UID/字幕] 未知命令：{command}");
                    break;
            }
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
                // 清掉上一条的内容与头像路径 → 下次刷新时随机掷一个（空 / 荧）
                SubtitleOverlayService.HideSubtitle();
                SubtitleOverlayService.ResetAvatarForNewRun();
                SubtitleOverlayService.SetPositionKey(positionKey);
                SubtitleOverlayService.ShowSubtitle();
            }
        }
    }
}
