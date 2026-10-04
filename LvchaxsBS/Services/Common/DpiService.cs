using System;
using System.Runtime.InteropServices;

namespace LvchaxsBS.Services.Common
{
    /// <summary>
    /// 统一 DPI 服务。
    /// 约定：业务层用物理像素思考，写入 WPF 属性时除以 DPI 转 DIP，
    /// 传给 Win32 API（SetWindowPos 等）时用物理像素。
    /// </summary>
    public static class DpiService
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;

        #endregion

        private static bool _initialized = false;
        private static double _dpiScaleX = 1.0;
        private static double _dpiScaleY = 1.0;

        /// <summary>X 方向缩放：物理像素 / DIP，如 150% DPI 时为 1.5</summary>
        public static double ScaleX => _dpiScaleX;

        /// <summary>Y 方向缩放：物理像素 / DIP，如 150% DPI 时为 1.5</summary>
        public static double ScaleY => _dpiScaleY;

        /// <summary>
        /// 初始化。可在 App 启动时调用一次，也可在各服务 Initialize 里调用（幂等）。
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                IntPtr hdc = GetDC(IntPtr.Zero);
                if (hdc != IntPtr.Zero)
                {
                    int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
                    int dpiY = GetDeviceCaps(hdc, LOGPIXELSY);
                    ReleaseDC(IntPtr.Zero, hdc);

                    _dpiScaleX = dpiX / 96.0;
                    _dpiScaleY = dpiY / 96.0;
                }
            }
            catch
            {
                _dpiScaleX = 1.0;
                _dpiScaleY = 1.0;
            }

            _initialized = true;
        }

        /// <summary>强制重新读取系统 DPI（一般无需调用）</summary>
        public static void Refresh()
        {
            _initialized = false;
            Initialize();
        }

        #region 换算

        /// <summary>物理像素 → DIP（X 方向）</summary>
        public static double PhysicalToDipX(double physical) => physical / _dpiScaleX;

        /// <summary>物理像素 → DIP（Y 方向）</summary>
        public static double PhysicalToDipY(double physical) => physical / _dpiScaleY;

        /// <summary>DIP → 物理像素（X 方向）</summary>
        public static double DipToPhysicalX(double dip) => dip * _dpiScaleX;

        /// <summary>DIP → 物理像素（Y 方向）</summary>
        public static double DipToPhysicalY(double dip) => dip * _dpiScaleY;

        #endregion
    }
}