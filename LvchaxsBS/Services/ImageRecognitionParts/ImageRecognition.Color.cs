using System.Drawing;
using System.Drawing.Imaging;

namespace LvchaxsBS.Services
{
    public static partial class ImageRecognition
    {
        #region 图像处理辅助方法

        public static Bitmap ScaleBitmap(Bitmap source, int newWidth, int newHeight)
        {
            var dest = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(dest);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, newWidth, newHeight);
            return dest;
        }

        public static Bitmap CropBitmap(Bitmap source, int x, int y, int width, int height)
        {
            var dest = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(dest);
            g.DrawImage(source, 0, 0, new Rectangle(x, y, width, height), GraphicsUnit.Pixel);
            return dest;
        }

        #endregion
    }
}