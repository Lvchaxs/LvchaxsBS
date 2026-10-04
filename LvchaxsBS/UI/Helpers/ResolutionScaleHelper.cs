using System;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 按游戏客户区分辨率计算缩放比例（基于 3840×2160 基准）。
    /// </summary>
    public static class ResolutionScaleHelper
    {
        public const double BASE_WIDTH = 3840;
        public const double BASE_HEIGHT = 2160;

        public static double GetScale(int clientWidth, int clientHeight,
            double min = 0.0, double max = double.MaxValue)
        {
            if (clientWidth <= 0 || clientHeight <= 0)
                return 1.0;

            double scaleX = clientWidth / BASE_WIDTH;
            double scaleY = clientHeight / BASE_HEIGHT;
            double scale = Math.Min(scaleX, scaleY);

            if (scale < min) scale = min;
            if (scale > max) scale = max;
            return scale;
        }
    }
}