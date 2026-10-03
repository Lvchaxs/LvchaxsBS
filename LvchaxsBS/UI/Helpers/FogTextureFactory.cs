using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 生成迷雾纹理（白色 + 柔和噪声 alpha），只生成一次并缓存。
    /// 用作主题过渡层的 OpacityMask：alpha 高低形成一团团雾。
    /// </summary>
    internal static class FogTextureFactory
    {
        private const int TextureSize = 512;

        private static ImageSource? _cached;

        /// <summary>取迷雾纹理（首次调用时生成，之后复用）。</summary>
        public static ImageSource Get()
        {
            if (_cached != null) return _cached;

            int size = TextureSize;
            var rnd = new Random(20261003);

            // 三个倍频的规则网格，值域 [0,1]，四周环绕保证纹理无缝。
            // 频率决定雾团大小：8 倍频 => 雾团约 125 逻辑像素，太粗会像渐变、太细会像噪点。
            int[][] grids =
            {
                BuildLattice(8, rnd),
                BuildLattice(14, rnd),
                BuildLattice(24, rnd)
            };
            double[] weights = { 0.55, 0.30, 0.15 };

            var pixels = new byte[size * size * 4];

            for (int y = 0; y < size; y++)
            {
                double v = y / (double)size;

                for (int x = 0; x < size; x++)
                {
                    double u = x / (double)size;

                    double n = 0;
                    for (int o = 0; o < grids.Length; o++)
                        n += SampleGrid(grids[o], u, v) * weights[o];

                    // 低对比度重映射：只保留柔和的浓淡变化，最低 0.26 保证雾始终有厚度
                    double a = Math.Clamp(0.5 + (n - 0.5) * 1.7, 0.26, 0.98);
                    byte alpha = (byte)Math.Clamp(a * 255.0, 0, 255);

                    int i = (y * size + x) * 4;
                    // Pbgra32：白色预乘 alpha
                    pixels[i + 0] = alpha; // B
                    pixels[i + 1] = alpha; // G
                    pixels[i + 2] = alpha; // R
                    pixels[i + 3] = alpha; // A
                }
            }

            var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
            bmp.Freeze();
            _cached = bmp;
            return bmp;
        }

        private static int[] BuildLattice(int cells, Random rnd)
        {
            var grid = new int[cells * cells];
            for (int i = 0; i < grid.Length; i++)
                grid[i] = rnd.Next(0, 1024);
            return grid;
        }

        /// <summary>双线性 + smoothstep 插值采样规则网格。</summary>
        private static double SampleGrid(int[] grid, double u, double v)
        {
            int cells = (int)Math.Round(Math.Sqrt(grid.Length));

            double fx = u * cells;
            double fy = v * cells;

            int x0 = (int)Math.Floor(fx);
            int y0 = (int)Math.Floor(fy);

            double tx = fx - x0;
            double ty = fy - y0;

            x0 = ((x0 % cells) + cells) % cells;
            y0 = ((y0 % cells) + cells) % cells;
            int x1 = (x0 + 1) % cells;
            int y1 = (y0 + 1) % cells;

            double sx = tx * tx * (3 - 2 * tx);
            double sy = ty * ty * (3 - 2 * ty);

            double a = grid[y0 * cells + x0] / 1023.0;
            double b = grid[y0 * cells + x1] / 1023.0;
            double c = grid[y1 * cells + x0] / 1023.0;
            double d = grid[y1 * cells + x1] / 1023.0;

            double top = a + (b - a) * sx;
            double bottom = c + (d - c) * sx;
            return top + (bottom - top) * sy;
        }
    }
}
