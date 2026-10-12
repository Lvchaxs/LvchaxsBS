using System;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 截图相关设置的注入点。
    ///
    /// <para>
    /// 本工程是共享库，不能反向引用宿主的 <c>ConfigManager</c> / <c>AppSettings</c>，
    /// 所以由宿主在启动时把"怎么读截图模式"注册进来。
    /// </para>
    ///
    /// <para>
    /// 注册的是一个 lambda 而不是值：宿主设置页改了截图模式之后**立刻生效**，
    /// 不需要重新注册，也不会出现"改了设置但识别还在用旧模式"的错位。
    /// 宿主没注册时按 0（窗口 DC）处理。
    /// </para>
    /// </summary>
    public static class CaptureSettings
    {
        /// <summary>
        /// 截图模式读取器：返回 0 = 窗口 DC（坐标需减客户区左上角），1 = 屏幕 DC（屏幕绝对坐标）。
        /// </summary>
        public static Func<int>? CaptureModeProvider { get; set; }

        /// <summary>
        /// 当前截图模式。宿主未注入时为 0。
        /// </summary>
        public static int CaptureMode => CaptureModeProvider?.Invoke() ?? 0;
    }
}
