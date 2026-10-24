using System;
using System.Windows;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 全局 Toast 通知入口（重构后版本）。
    /// <para>
    /// 旧项目的 ToastService 依赖 MainWindow 里的 ToastContainer/ToastText 两个原生元素 +
    /// Storyboard 资源，与新项目的 TitleBarToast 控件不兼容。
    /// 这里改为把消息转发到新 UI 的 <see cref="LvchaxsBS.UI.MainWindow.ShowToast"/>，
    /// 因此对引擎（IconService / 各功能逻辑 / 钩子回调）而言 API 完全不变，
    /// 但它们可能运行在后台线程，所以内部统一做一次 Dispatcher 编组。
    /// </para>
    /// </summary>
    public static class ToastService
    {
        /// <summary>兼容旧调用签名；新实现无需外部注入宿主，保留空实现。</summary>
        public static void Initialize(object? host = null) { }

        public static void Show(string message, bool isSuccess = true)
            => Dispatch(message, isSuccess, DefaultDurationMs);

        public static void Show(string title, string message, bool isSuccess = true)
            => Dispatch(string.IsNullOrEmpty(title) ? message : $"{title}: {message}",
                        isSuccess, DefaultDurationMs);

        public static void ShowWithTitle(string title, string message, bool isSuccess = true)
            => Show(title, message, isSuccess);

        /// <summary>
        /// 不限时通知：一直挂在标题栏上不淡出，直到下一次 <see cref="Show"/> 把它顶掉。
        /// <para>用于"要等多久不确定"的等待场景（比如下载更新）—— 普通通知固定几秒就没了。</para>
        /// </summary>
        public static void ShowSticky(string message, bool isSuccess = true)
            => Dispatch(message, isSuccess, Sticky);

        public static void ShowSticky(string title, string message, bool isSuccess = true)
            => Dispatch(string.IsNullOrEmpty(title) ? message : $"{title}: {message}",
                        isSuccess, Sticky);

        public static void Cleanup() { }

        /// <summary>普通通知的默认显示时长（毫秒）。</summary>
        private const int DefaultDurationMs = 5000;

        /// <summary>传给 Toast 控件的"不限时"标记：&lt;= 0 时不排淡出动画。</summary>
        private const int Sticky = -1;

        private static void Dispatch(string message, bool isSuccess, int durationMs)
        {
            var app = Application.Current;
            if (app == null) return;

            if (app.Dispatcher.CheckAccess())
                ShowCore(message, isSuccess, durationMs);
            else
                app.Dispatcher.BeginInvoke(new Action(() => ShowCore(message, isSuccess, durationMs)));
        }

        private static void ShowCore(string message, bool isSuccess, int durationMs)
        {
            if (Application.Current?.MainWindow is LvchaxsBS.UI.MainWindow mw)
                mw.ShowToast(message, isSuccess, durationMs);
        }
    }
}
