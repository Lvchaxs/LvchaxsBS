using System.Drawing;
using System.Drawing.Imaging;

namespace LvchaxsBS.Services
{
    public static partial class ImageRecognition
    {
        #region 像素数据转Bitmap

        public static Bitmap PixelsToBitmap(byte[] pixels, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                int stride = data.Stride;
                unsafe
                {
                    byte* destPtr = (byte*)data.Scan0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = (y * width + x) * 4;
                            int destIdx = y * stride + x * 4;
                            destPtr[destIdx] = pixels[srcIdx];
                            destPtr[destIdx + 1] = pixels[srcIdx + 1];
                            destPtr[destIdx + 2] = pixels[srcIdx + 2];
                            destPtr[destIdx + 3] = 255;
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        #endregion
    }
}