using LvchaxsBS.Config;
using LvchaxsBS.Services.Hooks;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LvchaxsBS.Services
{
    public static partial class ImageRecognition
    {
        #region 截图源选择

        /// <summary>
        /// 根据配置返回截图源 DC 及对应的坐标转换。
        /// CaptureMode=0：窗口 DC，坐标需减窗口左上角；
        /// CaptureMode=1：屏幕 DC，坐标直接用屏幕坐标。
        /// </summary>
        private static (IntPtr hdc, IntPtr hwnd, int offsetX, int offsetY, bool isScreen) GetCaptureSource()
        {
            var settings = ConfigManager.Get<AppSettings>();
            if (settings.CaptureMode == 1)
            {
                return (GetDC(IntPtr.Zero), IntPtr.Zero, 0, 0, true);
            }

            var bounds = WindowFocusService.LastBounds;
            IntPtr hwnd = bounds.Hwnd;
            if (hwnd == IntPtr.Zero)
                return (IntPtr.Zero, IntPtr.Zero, 0, 0, false);

            return (GetDC(hwnd), hwnd, bounds.Left, bounds.Top, false);
        }

        private static void ReleaseCaptureSource(IntPtr hdc, IntPtr hwnd, bool isScreen)
        {
            if (isScreen)
                ReleaseDC(IntPtr.Zero, hdc);
            else if (hwnd != IntPtr.Zero)
                ReleaseDC(hwnd, hdc);
        }

        #endregion

        #region 截图

        public static byte[] CaptureRegionGDI(int screenX, int screenY, int width, int height)
        {
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            var (hdcSource, hwnd, offsetX, offsetY, isScreen) = GetCaptureSource();
            if (hdcSource == IntPtr.Zero)
                return null;

            IntPtr hdcMem = CreateCompatibleDC(hdcSource);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSource, width, height);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            try
            {
                int srcX = isScreen ? screenX : screenX - offsetX;
                int srcY = isScreen ? screenY : screenY - offsetY;

                BitBlt(hdcMem, 0, 0, width, height, hdcSource, srcX, srcY, SRCCOPY);

                BITMAPINFO bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                        biWidth = width,
                        biHeight = -height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0
                    }
                };

                byte[] pixels = new byte[height * width * 4];
                GetDIBits(hdcMem, hBitmap, 0u, (uint)height, pixels, ref bmi, DIB_RGB_COLORS);
                return pixels;
            }
            finally
            {
                SelectObject(hdcMem, hOld);
                DeleteObject(hBitmap);
                DeleteDC(hdcMem);
                ReleaseCaptureSource(hdcSource, hwnd, isScreen);
            }
        }

        public static byte[] CaptureRegionGDI(CoordinateFormats.MatchRect matchRect)
        {
            var rect = matchRect.Rect;
            return CaptureRegionGDI(rect.X1, rect.Y1, rect.Width, rect.Height);
        }

        public static (int width, int height) GetMatchTargetSize()
        {
            int clientH = CoordinateFormats.ClientHeight;
            return clientH >= 1080 ? (1920, 1080) : (1600, 900);
        }

        /// <summary>
        /// 截图并缩放到模板基准尺度。
        /// 只按高度缩放，保持等比：
        /// 客户区高 >= 1080：缩到 1080 高；
        /// 客户区高 <  1080：不缩（scale = 1.0）。
        /// </summary>
        public static Bitmap CaptureRegionScaled(int screenX, int screenY, int width, int height)
        {
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            var pixels = CaptureRegionGDI(screenX, screenY, width, height);
            if (pixels == null) return null;

            using var raw = PixelsToBitmap(pixels, width, height);

            double scale = GetCaptureScale();   // 只按高度

            int newW = (int)(width * scale);
            int newH = (int)(height * scale);

            if (newW < 1) newW = 1;
            if (newH < 1) newH = 1;

            if (raw.Width == newW && raw.Height == newH)
                return new Bitmap(raw);

            return ScaleBitmap(raw, newW, newH);
        }

        public static Bitmap CaptureRegionScaled(CoordinateFormats.MatchRect matchRect)
        {
            var rect = matchRect.Rect;
            return CaptureRegionScaled(rect.X1, rect.Y1, rect.Width, rect.Height);
        }

        /// <summary>
        /// 截图缩放比，与 CaptureRegionScaled 保持一致。
        /// 客户区 >= 1080：返回 1080/实际高；否则返回 1.0。
        /// </summary>
        public static double GetCaptureScale()
        {
            int clientH = CoordinateFormats.ClientHeight;
            if (clientH < 1) return 1.0;
            if (clientH >= 1080) return 1080.0 / clientH;
            return 1.0;
        }

        #endregion
    }
}