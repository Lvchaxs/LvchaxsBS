using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace LvchaxsBS.Services
{
    public static partial class ImageRecognition
    {
        #region 模板匹配共享辅助

        private static float[,] ToGrayArray(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            float[,] gray = new float[height, width];

            var data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)data.Scan0;
                    int stride = data.Stride;
                    for (int y = 0; y < height; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = x * 4;
                            gray[y, x] = 0.299f * row[idx + 2] + 0.587f * row[idx + 1] + 0.114f * row[idx];
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return gray;
        }

        private static (float mean, float std, float[,] meanDiff) ComputeTemplateStats(float[,] templateGray)
        {
            int templateHeight = templateGray.GetLength(0);
            int templateWidth = templateGray.GetLength(1);
            float templateArea = templateWidth * templateHeight;

            float mean = 0;
            for (int y = 0; y < templateHeight; y++)
                for (int x = 0; x < templateWidth; x++)
                    mean += templateGray[y, x];
            mean /= templateArea;

            float std = 0;
            for (int y = 0; y < templateHeight; y++)
                for (int x = 0; x < templateWidth; x++)
                {
                    float diff = templateGray[y, x] - mean;
                    std += diff * diff;
                }
            std = (float)Math.Sqrt(std / templateArea);

            float[,] meanDiff = new float[templateHeight, templateWidth];
            for (int y = 0; y < templateHeight; y++)
                for (int x = 0; x < templateWidth; x++)
                    meanDiff[y, x] = templateGray[y, x] - mean;

            return (mean, std, meanDiff);
        }

        /// <summary>构建灰度图的积分图（原值 + 平方值）。</summary>
        private static void BuildIntegral(float[,] gray, out float[,] integral, out float[,] integralSq)
        {
            int h = gray.GetLength(0);
            int w = gray.GetLength(1);
            integral = new float[h + 1, w + 1];
            integralSq = new float[h + 1, w + 1];

            for (int y = 0; y < h; y++)
            {
                float rowSum = 0;
                float rowSumSq = 0;
                for (int x = 0; x < w; x++)
                {
                    float v = gray[y, x];
                    rowSum += v;
                    rowSumSq += v * v;
                    integral[y + 1, x + 1] = integral[y, x + 1] + rowSum;
                    integralSq[y + 1, x + 1] = integralSq[y, x + 1] + rowSumSq;
                }
            }
        }

        /// <summary>从积分图取矩形区域的和。</summary>
        private static float RectSum(float[,] integral, int x, int y, int w, int h)
        {
            return integral[y + h, x + w]
                 - integral[y, x + w]
                 - integral[y + h, x]
                 + integral[y, x];
        }

        /// <summary>
        /// 在 source 的 (x, y) 位置（模板大小）计算 NCC 相似度。
        /// sourceStd &lt; 1 时返回 0。
        /// </summary>
        private static double ComputeNccAt(
            float[,] sourceGray, int x, int y,
            float[,] integral, float[,] integralSq,
            float[,] templateMeanDiff,
            float templateStd, float templateArea,
            int templateWidth, int templateHeight)
        {
            float sum = RectSum(integral, x, y, templateWidth, templateHeight);
            float sumSq = RectSum(integralSq, x, y, templateWidth, templateHeight);

            float mean = sum / templateArea;
            float sourceStd = (float)Math.Sqrt(Math.Max(0, (sumSq / templateArea) - (mean * mean)));

            if (sourceStd < 1.0) return 0;

            float numerator = 0;
            for (int ty = 0; ty < templateHeight; ty++)
                for (int tx = 0; tx < templateWidth; tx++)
                    numerator += (sourceGray[y + ty, x + tx] - mean) * templateMeanDiff[ty, tx];

            double denominator = sourceStd * templateStd * templateArea;
            return (denominator != 0) ? numerator / denominator : 0;
        }

        #endregion
    }
}