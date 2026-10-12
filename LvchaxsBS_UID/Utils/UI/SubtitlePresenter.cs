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
        private const string CmdStart = "subtitle:start";      // 参数：位置坐标 key
        private const string CmdText = "subtitle:text";        // 参数：正文
        private const string CmdSuffix = "subtitle:suffix";    // 参数：后缀
        private const string CmdHide = "subtitle:hide";        // 无参数
        private const string CmdOpacity = "subtitle:opacity";  // 参数：0~100

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

        /// <summary>在正文后追加后缀（末尾已有则不重复追加），用于「3 / 5」这类进度。</summary>
        public static void AppendSuffix(string? suffix) => Send(CmdSuffix, suffix);

        /// <summary>收起字幕（不关窗口，只是淡出）。</summary>
        public static void Hide() => Send(CmdHide);

        /// <summary>设置气泡底色不透明度（0~100）。</summary>
        public static void SetBackgroundOpacity(int percent) => Send(CmdOpacity, percent);
    }
}
