using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace LvchaxsBS.Services
{
    public static partial class ImageRecognition
    {
        #region 二值化检测（钓鱼辅助专用）

        public static Bitmap BinarizeImage(Bitmap original, CoordinateFormats.RgbColor targetColor, int tolerance = 8)
        {
            if (original == null) return null;

            int width = original.Width;
            int height = original.Height;
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            int lowerR = Math.Max(0, targetColor.R - tolerance);
            int lowerG = Math.Max(0, targetColor.G - tolerance);
            int lowerB = Math.Max(0, targetColor.B - tolerance);
            int upperR = Math.Min(255, targetColor.R + tolerance);
            int upperG = Math.Min(255, targetColor.G + tolerance);
            int upperB = Math.Min(255, targetColor.B + tolerance);

            var srcData = original.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            var dstData = result.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    byte* srcPtr = (byte*)srcData.Scan0;
                    byte* dstPtr = (byte*)dstData.Scan0;
                    int srcStride = srcData.Stride;
                    int dstStride = dstData.Stride;

                    for (int y = 0; y < height; y++)
                    {
                        byte* srcRow = srcPtr + y * srcStride;
                        byte* dstRow = dstPtr + y * dstStride;

                        for (int x = 0; x < width; x++)
                        {
                            int idx = x * 4;
                            byte b = srcRow[idx];
                            byte g = srcRow[idx + 1];
                            byte r = srcRow[idx + 2];

                            bool inRange = (r >= lowerR && r <= upperR) &&
                                           (g >= lowerG && g <= upperG) &&
                                           (b >= lowerB && b <= upperB);

                            byte val = inRange ? (byte)255 : (byte)0;
                            dstRow[idx] = val;
                            dstRow[idx + 1] = val;
                            dstRow[idx + 2] = val;
                            dstRow[idx + 3] = 255;
                        }
                    }
                }
            }
            finally
            {
                original.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }

            return result;
        }

        public class BinarizeDetectionResult
        {
            public bool Detected { get; set; }
            public int SliderCenterX { get; set; }
            public int TargetLeft { get; set; }
            public int TargetRight { get; set; }
            public int TargetCenter { get; set; }
            public int JudgeLineX { get; set; }
        }

        public static BinarizeDetectionResult DetectSliderAndTarget(Bitmap binarized, int judgeLinePercent = 50)
        {
            var result = new BinarizeDetectionResult { Detected = false };

            if (binarized == null) return result;

            int width = binarized.Width;
            int height = binarized.Height;

            var columnCounts = new int[width];
            for (int x = 0; x < width; x++)
            {
                int count = 0;
                for (int y = 0; y < height; y++)
                {
                    var pixel = binarized.GetPixel(x, y);
                    if (pixel.R > 128)
                        count++;
                }
                columnCounts[x] = count;
            }

            var regions = new List<(int start, int end, int maxCount, int regionWidth, int totalPixels, int maxHeight)>();
            int regionStart = -1;

            for (int x = 0; x < width; x++)
            {
                if (columnCounts[x] > 0 && regionStart == -1)
                {
                    regionStart = x;
                }
                else if (columnCounts[x] == 0 && regionStart != -1)
                {
                    int maxCount = 0;
                    int totalPixels = 0;
                    int maxHeight = 0;
                    for (int i = regionStart; i < x; i++)
                    {
                        totalPixels += columnCounts[i];
                        if (columnCounts[i] > maxCount)
                            maxCount = columnCounts[i];
                        if (columnCounts[i] > maxHeight)
                            maxHeight = columnCounts[i];
                    }
                    int rWidth = x - regionStart;
                    if (maxCount > 2 && rWidth >= 3 && totalPixels > 50)
                    {
                        regions.Add((regionStart, x - 1, maxCount, rWidth, totalPixels, maxHeight));
                    }
                    regionStart = -1;
                }
            }

            if (regionStart != -1)
            {
                int maxCount = 0;
                int totalPixels = 0;
                int maxHeight = 0;
                for (int i = regionStart; i < width; i++)
                {
                    totalPixels += columnCounts[i];
                    if (columnCounts[i] > maxCount)
                        maxCount = columnCounts[i];
                    if (columnCounts[i] > maxHeight)
                        maxHeight = columnCounts[i];
                }
                int rWidth = width - regionStart;
                if (maxCount > 2 && rWidth >= 3 && totalPixels > 50)
                {
                    regions.Add((regionStart, width - 1, maxCount, rWidth, totalPixels, maxHeight));
                }
            }

            if (regions.Count < 2)
                return result;

            var sortedByTotalPixels = regions.OrderByDescending(r => r.totalPixels).ToList();
            int takeCount = Math.Min(3, sortedByTotalPixels.Count);
            var topRegions = sortedByTotalPixels.Take(takeCount).ToList();

            if (topRegions.Count < 2)
                return result;

            var sortedByHeight = topRegions.OrderByDescending(r => r.maxHeight).ToList();
            var sliderRegion = sortedByHeight.First();

            if (sliderRegion.totalPixels < 30)
                return result;

            var targetRegions = topRegions
                .Where(r => r != sliderRegion)
                .ToList();

            if (!targetRegions.Any())
                return result;

            result.SliderCenterX = (sliderRegion.start + sliderRegion.end) / 2;
            result.TargetLeft = targetRegions.Min(r => r.start);
            result.TargetRight = targetRegions.Max(r => r.end);
            result.TargetCenter = (result.TargetLeft + result.TargetRight) / 2;

            int targetWidth = result.TargetRight - result.TargetLeft;
            result.JudgeLineX = result.TargetLeft + (int)(targetWidth * (judgeLinePercent / 100.0));

            result.Detected = true;
            return result;
        }

        #endregion

        #region 二值化检测（自动烹饪专用）

        public class WhiteBlob
        {
            public int MinX { get; set; }
            public int MinY { get; set; }
            public int MaxX { get; set; }
            public int MaxY { get; set; }
            public int PixelCount { get; set; }

            public int Width => MaxX - MinX + 1;
            public int Height => MaxY - MinY + 1;
        }

        public static (bool found, int leftX, int leftY, int rightX, int rightY, int top, int bottom) GetWhiteHorizontalBounds(Bitmap binarized)
        {
            if (binarized == null) return (false, 0, 0, 0, 0, 0, 0);

            int width = binarized.Width;
            int height = binarized.Height;

            int leftX = int.MaxValue, rightX = int.MinValue;
            int leftY = 0, rightY = 0;
            int top = int.MaxValue, bottom = int.MinValue;
            bool leftSet = false, rightSet = false;

            var data = binarized.LockBits(
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
                            if (row[x * 4] > 128)
                            {
                                if (x < leftX) { leftX = x; leftY = y; leftSet = true; }
                                if (x > rightX) { rightX = x; rightY = y; rightSet = true; }
                                if (y < top) top = y;
                                if (y > bottom) bottom = y;
                            }
                        }
                    }
                }
            }
            finally
            {
                binarized.UnlockBits(data);
            }

            if (!leftSet || !rightSet) return (false, 0, 0, 0, 0, 0, 0);
            return (true, leftX, leftY, rightX, rightY, top, bottom);
        }

        public static WhiteBlob? FindWhiteBlobAtPoint(Bitmap binarized, int startX, int startY)
        {
            if (binarized == null) return null;

            int width = binarized.Width;
            int height = binarized.Height;

            if (startX < 0 || startX >= width) return null;
            if (startY < 0 || startY >= height) return null;

            bool[,] white = new bool[height, width];

            var data = binarized.LockBits(
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
                            white[y, x] = row[x * 4] > 128;
                        }
                    }
                }
            }
            finally
            {
                binarized.UnlockBits(data);
            }

            if (!white[startY, startX]) return null;

            bool[,] visited = new bool[height, width];
            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

            var blob = new WhiteBlob
            {
                MinX = startX,
                MaxX = startX,
                MinY = startY,
                MaxY = startY,
                PixelCount = 0
            };

            var queue = new Queue<(int y, int x)>();
            queue.Enqueue((startY, startX));
            visited[startY, startX] = true;

            while (queue.Count > 0)
            {
                var (cy, cx) = queue.Dequeue();
                blob.PixelCount++;
                if (cx < blob.MinX) blob.MinX = cx;
                if (cx > blob.MaxX) blob.MaxX = cx;
                if (cy < blob.MinY) blob.MinY = cy;
                if (cy > blob.MaxY) blob.MaxY = cy;

                for (int k = 0; k < 8; k++)
                {
                    int ny = cy + dy[k];
                    int nx = cx + dx[k];
                    if (ny < 0 || ny >= height || nx < 0 || nx >= width) continue;
                    if (!white[ny, nx] || visited[ny, nx]) continue;
                    visited[ny, nx] = true;
                    queue.Enqueue((ny, nx));
                }
            }

            return blob;
        }

        #endregion
    }
}