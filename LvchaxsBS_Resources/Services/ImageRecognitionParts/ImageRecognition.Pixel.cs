using System;
using System.Collections.Generic;

namespace LvchaxsBS.Services
{
    public static partial class ImageRecognition
    {
        #region 单坐标像素RGB检测

        public static CoordinateFormats.RgbColor GetPixelColor(int screenX, int screenY)
        {
            var (hdcSource, hwnd, offsetX, offsetY, isScreen) = GetCaptureSource();
            if (hdcSource == IntPtr.Zero)
            {
                return new CoordinateFormats.RgbColor(0, 0, 0);
            }

            IntPtr hdcMem = CreateCompatibleDC(hdcSource);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSource, 1, 1);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            try
            {
                int srcX = isScreen ? screenX : screenX - offsetX;
                int srcY = isScreen ? screenY : screenY - offsetY;

                bool bltOk = BitBlt(hdcMem, 0, 0, 1, 1, hdcSource, srcX, srcY, SRCCOPY);

                BITMAPINFO bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                        biWidth = 1,
                        biHeight = -1,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0
                    }
                };

                byte[] pixel = new byte[4];
                int dibOk = GetDIBits(hdcMem, hBitmap, 0u, 1u, pixel, ref bmi, DIB_RGB_COLORS);

                if (!bltOk || dibOk == 0)
                {
                    return new CoordinateFormats.RgbColor(0, 0, 0);
                }

                byte b = pixel[0];
                byte g = pixel[1];
                byte r = pixel[2];
                return new CoordinateFormats.RgbColor(r, g, b);
            }
            finally
            {
                SelectObject(hdcMem, hOld);
                DeleteObject(hBitmap);
                DeleteDC(hdcMem);
                ReleaseCaptureSource(hdcSource, hwnd, isScreen);
            }
        }

        public static CoordinateFormats.RgbColor GetPixelColor(CoordinateFormats.Point point)
        {
            var screen = point.ToScreen();
            return GetPixelColor(screen.X, screen.Y);
        }

        public static bool CheckPixelColor(int x, int y, CoordinateFormats.RgbColor targetColor, int tolerance = 5)
        {
            var actual = GetPixelColor(x, y);
            return Math.Abs(actual.R - targetColor.R) <= tolerance &&
                   Math.Abs(actual.G - targetColor.G) <= tolerance &&
                   Math.Abs(actual.B - targetColor.B) <= tolerance;
        }

        public static bool CheckPixelColor(int x, int y, List<CoordinateFormats.RgbColor> targetColors, int tolerance = 5)
        {
            var actual = GetPixelColor(x, y);
            foreach (var target in targetColors)
            {
                if (Math.Abs(actual.R - target.R) <= tolerance &&
                    Math.Abs(actual.G - target.G) <= tolerance &&
                    Math.Abs(actual.B - target.B) <= tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool CheckPixelColor(CoordinateFormats.DetectPoint detectPoint)
        {
            var screen = detectPoint.ToScreen();
            return CheckPixelColor(screen.X, screen.Y, detectPoint.Colors, detectPoint.Tolerance);
        }

        #endregion
    }
}