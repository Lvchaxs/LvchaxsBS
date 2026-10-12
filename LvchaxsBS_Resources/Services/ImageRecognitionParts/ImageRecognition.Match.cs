using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;

namespace LvchaxsBS.Services
{
    public static partial class ImageRecognition
    {
        #region 模板匹配（通用 2D 全图滑动）

        public class MatchResult
        {
            public bool Matched { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public double Similarity { get; set; }
        }

        public static MatchResult MatchTemplate(Bitmap source, Bitmap template, double threshold)
        {
            if (template.Width > source.Width || template.Height > source.Height)
                return new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            int sourceWidth = source.Width;
            int sourceHeight = source.Height;
            int templateWidth = template.Width;
            int templateHeight = template.Height;

            float[,] sourceGray = ToGrayArray(source);
            float[,] templateGray = ToGrayArray(template);

            var (_, templateStd, templateMeanDiff) = ComputeTemplateStats(templateGray);

            if (templateStd < 1.0)
                return new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            BuildIntegral(sourceGray, out var integral, out var integralSq);

            int searchWidth = sourceWidth - templateWidth + 1;
            int searchHeight = sourceHeight - templateHeight + 1;
            float templateArea = templateWidth * templateHeight;

            double bestScore = -1.0;
            int bestX = 0;
            int bestY = 0;

            for (int y = 0; y < searchHeight; y++)
            {
                for (int x = 0; x < searchWidth; x++)
                {
                    double ncc = ComputeNccAt(sourceGray, x, y,
                        integral, integralSq,
                        templateMeanDiff, templateStd, templateArea,
                        templateWidth, templateHeight);

                    if (ncc > bestScore)
                    {
                        bestScore = ncc;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            double finalSimilarity = Math.Max(0, Math.Min(1, bestScore));

            return new MatchResult
            {
                Matched = finalSimilarity >= threshold,
                X = bestX,
                Y = bestY,
                Similarity = finalSimilarity
            };
        }

        public static List<MatchResult> MatchTemplateAll(Bitmap source, Bitmap template, double threshold)
        {
            var results = new List<MatchResult>();

            if (source == null || template == null) return results;
            if (template.Width > source.Width || template.Height > source.Height) return results;

            int sourceWidth = source.Width;
            int sourceHeight = source.Height;
            int templateWidth = template.Width;
            int templateHeight = template.Height;

            float[,] sourceGray = ToGrayArray(source);
            float[,] templateGray = ToGrayArray(template);

            var (_, templateStd, templateMeanDiff) = ComputeTemplateStats(templateGray);

            if (templateStd < 1.0) return results;

            BuildIntegral(sourceGray, out var integral, out var integralSq);

            int searchWidth = sourceWidth - templateWidth + 1;
            int searchHeight = sourceHeight - templateHeight + 1;
            float templateArea = templateWidth * templateHeight;

            for (int y = 0; y < searchHeight; y++)
            {
                for (int x = 0; x < searchWidth; x++)
                {
                    double ncc = ComputeNccAt(sourceGray, x, y,
                        integral, integralSq,
                        templateMeanDiff, templateStd, templateArea,
                        templateWidth, templateHeight);

                    if (ncc >= threshold)
                    {
                        results.Add(new MatchResult
                        {
                            Matched = true,
                            X = x,
                            Y = y,
                            Similarity = Math.Max(0, Math.Min(1, ncc))
                        });
                    }
                }
            }

            return results;
        }

        #endregion

        #region 缩放模板匹配（粗匹配 + 原图局部精匹配）

        public static MatchResult MatchTemplateScaledBest(
            Bitmap source, Bitmap template, double threshold, int scaleFactor, string? tag = null)
        {
            if (source == null || template == null)
                return new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            if (scaleFactor < 1) scaleFactor = 1;

            if (scaleFactor == 1)
                return MatchTemplate(source, template, threshold);

            int sourceWidth = source.Width;
            int sourceHeight = source.Height;
            int templateWidth = template.Width;
            int templateHeight = template.Height;

            if (templateWidth > sourceWidth || templateHeight > sourceHeight)
                return new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            int scaledSourceWidth = Math.Max(1, sourceWidth / scaleFactor);
            int scaledSourceHeight = Math.Max(1, sourceHeight / scaleFactor);
            int scaledTemplateWidth = Math.Max(1, templateWidth / scaleFactor);
            int scaledTemplateHeight = Math.Max(1, templateHeight / scaleFactor);

            if (scaledTemplateWidth > scaledSourceWidth || scaledTemplateHeight > scaledSourceHeight)
                return new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            using var scaledSource = ScaleBitmap(source, scaledSourceWidth, scaledSourceHeight);
            using var scaledTemplate = ScaleBitmap(template, scaledTemplateWidth, scaledTemplateHeight);

            double scaledThreshold = Math.Max(0, threshold - 0.1);

            var roughAll = MatchTemplateAll(scaledSource, scaledTemplate, scaledThreshold);

            if (!string.IsNullOrEmpty(tag))
                Debug.WriteLine($"【{tag}-模板粗匹配】候选数={roughAll.Count}");
            else
                Debug.WriteLine($"【模板粗匹配】候选数={roughAll.Count}");

            if (roughAll.Count == 0)
                return new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            int marginX = Math.Max(templateWidth / 4, 8);
            int marginY = Math.Max(templateHeight / 4, 8);

            MatchResult? best = null;

            foreach (var rough in roughAll)
            {
                int roughX = rough.X * scaleFactor;
                int roughY = rough.Y * scaleFactor;

                int searchX = Math.Max(0, roughX - marginX);
                int searchY = Math.Max(0, roughY - marginY);
                int searchW = Math.Min(sourceWidth - searchX, templateWidth + marginX * 2);
                int searchH = Math.Min(sourceHeight - searchY, templateHeight + marginY * 2);

                if (searchW < templateWidth || searchH < templateHeight)
                    continue;

                using var searchRegion = CropBitmap(source, searchX, searchY, searchW, searchH);

                var precise = MatchTemplate(searchRegion, template, threshold);
                if (!precise.Matched) continue;

                var candidate = new MatchResult
                {
                    Matched = true,
                    X = searchX + precise.X,
                    Y = searchY + precise.Y,
                    Similarity = precise.Similarity
                };

                if (best == null || candidate.Similarity > best.Similarity)
                    best = candidate;
            }

            return best ?? new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };
        }

        #endregion

        #region 右侧列表专用匹配（只沿Y轴滑动，内部原语）

        public static List<MatchResult> MatchTemplateAllForRightListInternal(Bitmap source, Bitmap template, double threshold)
            => MatchTemplateAllForRightListInternal(source, template, threshold, out _);

        /// <summary>
        /// 同上一个重载，额外输出 <paramref name="bestEffortScore"/>：
        /// 滑动过程中出现过的**最高相似度**（可能低于阈值），只用于 UI 展示"差多少"，不参与判定。
        /// </summary>
        public static List<MatchResult> MatchTemplateAllForRightListInternal(
            Bitmap source, Bitmap template, double threshold, out double bestEffortScore)
        {
            bestEffortScore = 0;
            var results = new List<MatchResult>();

            if (template.Width > source.Width || template.Height > source.Height)
                return results;

            int sourceHeight = source.Height;
            int templateWidth = template.Width;
            int templateHeight = template.Height;

            float[,] sourceGray = ToGrayArray(source);
            float[,] templateGray = ToGrayArray(template);

            var (_, templateStd, templateMeanDiff) = ComputeTemplateStats(templateGray);

            if (templateStd < 1.0)
                return results;

            BuildIntegral(sourceGray, out var integral, out var integralSq);

            int searchHeight = sourceHeight - templateHeight + 1;
            float templateArea = templateWidth * templateHeight;

            for (int y = 0; y < searchHeight; y++)
            {
                double ncc = ComputeNccAt(sourceGray, 0, y,
                    integral, integralSq,
                    templateMeanDiff, templateStd, templateArea,
                    templateWidth, templateHeight);

                double finalSimilarity = Math.Max(0, Math.Min(1, ncc));

                // 无论达不达标都记下最高分，供 UI 显示"没中但最高到过多少"
                if (finalSimilarity > bestEffortScore)
                    bestEffortScore = finalSimilarity;

                if (finalSimilarity >= threshold)
                {
                    results.Add(new MatchResult
                    {
                        Matched = true,
                        X = 0,
                        Y = y,
                        Similarity = finalSimilarity
                    });
                }
            }

            return results;
        }

        #endregion

        #region 右下角专用匹配（只沿X轴滑动，原语）

        public static MatchResult MatchTemplateForRightCorner(Bitmap source, Bitmap template, double threshold)
        {
            if (template.Width > source.Width || template.Height > source.Height)
                return new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            int sourceWidth = source.Width;
            int templateWidth = template.Width;
            int templateHeight = template.Height;

            float[,] sourceGray = ToGrayArray(source);
            float[,] templateGray = ToGrayArray(template);

            var (_, templateStd, templateMeanDiff) = ComputeTemplateStats(templateGray);

            if (templateStd < 1.0)
                return new MatchResult { Matched = false, Similarity = 0, X = 0, Y = 0 };

            BuildIntegral(sourceGray, out var integral, out var integralSq);

            int searchWidth = sourceWidth - templateWidth + 1;
            float templateArea = templateWidth * templateHeight;

            double bestScore = -1.0;
            int bestX = 0;

            for (int x = 0; x < searchWidth; x++)
            {
                double ncc = ComputeNccAt(sourceGray, x, 0,
                    integral, integralSq,
                    templateMeanDiff, templateStd, templateArea,
                    templateWidth, templateHeight);

                if (ncc > bestScore)
                {
                    bestScore = ncc;
                    bestX = x;
                }
            }

            double finalSimilarity = Math.Max(0, Math.Min(1, bestScore));

            return new MatchResult
            {
                Matched = finalSimilarity >= threshold,
                X = bestX,
                Y = 0,
                Similarity = finalSimilarity
            };
        }

        #endregion
    }
}