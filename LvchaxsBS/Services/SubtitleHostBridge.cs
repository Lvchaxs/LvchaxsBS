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
    /// 命令表与 UID 侧 <c>Utils/UI/SubtitlePresenter.cs</c> 一一对应。
    /// </para>
    /// </summary>
    public static class SubtitleHostBridge
    {
        /// <summary>命令：开始一次显示（重掷头像 + 定位 + 显示）。参数 = 位置坐标 key。</summary>
        public const string CmdStart = "subtitle:start";
        /// <summary>命令：设置正文。参数 = 文本。</summary>
        public const string CmdText = "subtitle:text";
        /// <summary>命令：追加后缀。参数 = 后缀。</summary>
        public const string CmdSuffix = "subtitle:suffix";
        /// <summary>命令：收起字幕。</summary>
        public const string CmdHide = "subtitle:hide";
        /// <summary>命令：设置气泡底色不透明度。参数 = 0~100。</summary>
        public const string CmdOpacity = "subtitle:opacity";

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
                case CmdStart:
                    // 清掉上一条的字幕内容与头像路径 → 下次刷新时随机掷一个（空 / 荧）
                    SubtitleOverlayService.HideSubtitle();
                    SubtitleOverlayService.ResetAvatarForNewRun();
                    SubtitleOverlayService.SetPositionKey(arg as string ?? string.Empty);
                    SubtitleOverlayService.ShowSubtitle();
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

                default:
                    Debug.WriteLine($"[UID/字幕] 未知命令：{command}");
                    break;
            }
        }
    }
}
