using System;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 外观相关的全局通知服务（窗口标题、圆角、透明度等）。
    /// 目前只提供窗口标题变化通知，后续可扩展。
    /// </summary>
    public static class AppearanceService
    {
        /// <summary>窗口标题变化（参数为新标题）</summary>
        public static event EventHandler<string>? WindowTitleChanged;

        /// <summary>广播窗口标题变化。由个性化页调用。</summary>
        public static void NotifyWindowTitleChanged(string newTitle)
        {
            WindowTitleChanged?.Invoke(null, newTitle ?? "");
        }
    }
}