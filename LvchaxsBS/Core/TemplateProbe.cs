using System;
using System.Diagnostics;
using LvchaxsBS.Services;   // CoordinateFormats / ImageRecognition

namespace LvchaxsBS.Core
{
    /// <summary>
    /// 「取区域缓存 → 区域截图 → 取模板 → 模板匹配」这条探测流水线的唯一实现。
    ///
    /// <para>
    /// 自动伐木 / 自动烹饪 / 剧情对话 里每个检测点原来都各抄了一份完全一样的 35~45 行：
    /// 判 <c>CacheReady</c>、<c>GetCached&lt;MatchRect&gt;</c>、<c>CaptureRegionScaled</c>、
    /// <c>GetCachedTemplate</c>、<c>MatchTemplate</c>，再按阈值判命中 —— 只差区域键 / 模板键 / 日志文案。
    /// 抄出来的副本连"失败时返回什么"都必须保持一致，改一处就得记得改全部，非常容易漏。
    /// 现在统一走这里，各功能模块只留一层薄包装（保持原有的返回值形状，调用方不用动）。
    /// </para>
    ///
    /// <para>
    /// ★ 语义与原实现逐项对齐，不要"顺手优化"：
    /// <list type="bullet">
    /// <item>未命中但匹配到东西时，<paramref name="similarity"/> 仍然返回**真实匹配度**（界面上要显示分数），
    /// 不是 0；只有在流程中途失败（缓存没就绪 / 截图为空 / 模板缺失）时才是 0。</item>
    /// <item><paramref name="elapsedMs"/> 在原实现的每条返回路径上都是 Stopwatch 的当前读数，
    /// 早退路径也一样（不返回 0）。</item>
    /// <item>异常只写 Debug 日志、返回"未命中"，绝不向上抛 —— 检测循环靠这个活着。</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class TemplateProbe
    {
        /// <summary>
        /// 跑一次探测。
        /// </summary>
        /// <param name="rectKey">坐标缓存里的区域键，例如 <c>自动伐木_王树瑞佑区域</c>。</param>
        /// <param name="templateKey">识别模板键，例如 <c>自动伐木_王树瑞佑</c>。</param>
        /// <param name="threshold">该功能自己的阈值（各功能配置项不同，由调用方传入）。</param>
        /// <param name="logTag">日志前缀，例如 <c>自动伐木</c>。</param>
        /// <param name="label">日志里"检测到某某"的名字。</param>
        /// <param name="similarity">匹配度（未命中但匹配到了也返回真实值）。</param>
        /// <param name="elapsedMs">本次探测耗时。</param>
        /// <param name="clickX">命中点的屏幕 X（仅 <paramref name="computeClickPoint"/> 为 true 时有意义）。</param>
        /// <param name="clickY">命中点的屏幕 Y（同上）。</param>
        /// <param name="logHit">命中时是否写 Debug 日志（高频轮询的入口可以关掉）。</param>
        /// <param name="computeClickPoint">是否顺带算出"模板中心在屏幕上的坐标"（点击类检测用）。</param>
        /// <param name="requireNonEmptyRect">区域宽/高必须为正（自动清药系列的原有前置校验）。</param>
        /// <param name="templateMissingLog">模板缺失时额外写一条日志（传 null = 不写）。</param>
        /// <returns>是否命中（匹配度达到阈值）。</returns>
        public static bool TryRun(
            string rectKey,
            string templateKey,
            double threshold,
            string logTag,
            string label,
            out double similarity,
            out long elapsedMs,
            out int clickX,
            out int clickY,
            bool logHit = true,
            bool computeClickPoint = false,
            bool requireNonEmptyRect = false,
            string? templateMissingLog = null)
        {
            var sw = Stopwatch.StartNew();
            similarity = 0;
            elapsedMs = 0;
            clickX = 0;
            clickY = 0;

            try
            {
                if (!CoordinateFormats.CacheReady)
                {
                    elapsedMs = sw.ElapsedMilliseconds;
                    return false;
                }

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(rectKey);
                if (matchRect == null)
                {
                    elapsedMs = sw.ElapsedMilliseconds;
                    return false;
                }

                if (requireNonEmptyRect)
                {
                    var rect = matchRect.Rect;
                    if (rect.Width <= 0 || rect.Height <= 0)
                    {
                        elapsedMs = sw.ElapsedMilliseconds;
                        return false;
                    }
                }

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null)
                {
                    elapsedMs = sw.ElapsedMilliseconds;
                    return false;
                }

                var template = TemplateManager.GetCachedTemplate(templateKey);
                if (template == null)
                {
                    if (templateMissingLog != null)
                        Debug.WriteLine($"【{logTag}】{templateMissingLog}");
                    elapsedMs = sw.ElapsedMilliseconds;
                    return false;
                }

                var result = ImageRecognition.MatchTemplate(source, template, threshold);
                elapsedMs = sw.ElapsedMilliseconds;

                similarity = result?.Similarity ?? 0;

                bool detected = result != null && result.Similarity >= threshold;
                if (!detected) return false;

                if (logHit)
                {
                    Debug.WriteLine($"【{logTag}】检测到{label} 匹配度: {result.Similarity * 100:F2}%");
                }

                if (computeClickPoint)
                {
                    // 模板中心换算回屏幕坐标：截图可能被缩放，除以采集缩放系数即可。
                    double capScale = ImageRecognition.GetCaptureScale();
                    clickX = matchRect.Rect.X1 + (int)((result.X + template.Width / 2.0) / capScale);
                    clickY = matchRect.Rect.Y1 + (int)((result.Y + template.Height / 2.0) / capScale);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【{logTag}】{label}检测异常: {ex.Message}");
                elapsedMs = sw.ElapsedMilliseconds;
                return false;
            }
        }
    }
}
