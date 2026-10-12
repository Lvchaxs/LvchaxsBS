using System;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 窗口客户区信息。共享库自己的一份（字段与宿主 <c>WindowFocusService.WindowBounds</c> 对应），
    /// 这样坐标与截图逻辑不用反向引用宿主。
    /// </summary>
    public readonly struct WindowBoundsInfo
    {
        public IntPtr Hwnd { get; }
        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public string Title { get; }
        public string ProcessName { get; }

        public WindowBoundsInfo(IntPtr hwnd, int left, int top, int width, int height,
                                string? title, string? processName)
        {
            Hwnd = hwnd;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            Title = title ?? string.Empty;
            ProcessName = processName ?? string.Empty;
        }

        public bool IsEmpty => Hwnd == IntPtr.Zero || Width <= 0 || Height <= 0;
    }

    /// <summary>
    /// 窗口客户区的注入点（坐标缓存与截图都从这里取）。
    ///
    /// <para>
    /// 跟踪游戏窗口是宿主 <c>WindowFocusService</c> 的职责（它留在宿主里，因为它还要管焦点、
    /// 窗口存在性这些纯宿主概念）。宿主在启动时把 <c>BoundsChanged</c> 转发到
    /// <see cref="Report"/>，共享库就能拿到客户区，而不需要引用宿主。
    /// </para>
    ///
    /// <para>
    /// 宿主未注入 / 资源 dll 缺失时 <see cref="LastBounds"/> 恒为空，
    /// <c>CoordinateFormats.CacheReady</c> 保持 false —— 识别功能整体停用，但不会抛异常。
    /// </para>
    /// </summary>
    public static class WindowBoundsBridge
    {
        /// <summary>最近一次的客户区信息（未被注入时为空）。</summary>
        public static WindowBoundsInfo LastBounds { get; private set; }

        /// <summary>客户区发生变化时触发（宿主转发进来的）。</summary>
        public static event Action<WindowBoundsInfo>? Changed;

        /// <summary>宿主推送一次客户区信息。</summary>
        public static void Report(WindowBoundsInfo info)
        {
            LastBounds = info;
            Changed?.Invoke(info);
        }
    }
}
