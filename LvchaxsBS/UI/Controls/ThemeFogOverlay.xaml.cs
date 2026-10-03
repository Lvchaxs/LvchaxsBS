using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Controls
{
    /// <summary>
    /// 主题切换迷雾过渡层。
    ///
    /// 视觉上只有两层：雾体（FogCore，铺满遮住跳变）+ 雾纹（FogNoise，噪声纹理做质感）。
    /// 两层同时起跑、扩散速度一致，只是雾纹早 40ms 到峰值（雾丝先涌进来、雾体随后填满）。
    ///
    /// 时间上分两段（同一时刻起算）：
    ///   ① 0→460ms    灰色雾从点击处滚满整个窗口（460ms 换主题，此刻已 100% 盖住，跳变被吃掉）
    ///   ② 660→1090ms 雾心先散开、雾墙被向外推开并飘出窗外，露出新主题
    ///
    /// 雾色<b>全程保持灰色</b>（不做"由灰白变黑"的渐变），浅→深与深→浅共用同一套，
    /// 所以两个方向的观感一致，都是"灰雾涌来 → 灰雾散开"。
    /// 实现用"铺满窗口的矩形 + 绝对映射的径向渐变"，只靠动画改半径与渐变挡位，
    /// 不依赖任何尺寸/偏移几何，所以一定能盖住整屏（不会只盖住某个角）。
    /// </summary>
    public partial class ThemeFogOverlay : UserControl
    {
        // ===== 时间线（毫秒，全部相对 Play() 同一时刻）=====
        private const double RollInMs = 460;       // 雾滚满全屏（半径到位，整屏被盖住）
        private const double SwapMs = 560;         // 换主题（此时已盖满，前后都留足余量）
        private const double HoldMs = 660;         // 雾心开始散
        private const double SweepMs = 430;        // 雾墙推出窗外
        private const double CoreFadeAtMs = 980;   // 残余雾体开始退场
        private const double CoreFadeMs = 160;
        private const double NoiseFadeMs = 460;
        private const double FinishMs = 1220;

        // ===== 雾的几何 =====
        private const double PlateauEnd = 0.78;    // 不透明平台结束位置（相对半径）
        private const double RampStart = 0.001;    // 雾墙内侧起点（用极小值避免零宽渐变段）
        private const double RadiusStart = 2;      // 起始半径（像素）
        private const double RadiusEndScale = 1.08;// 散雾时再向外膨胀一点
        private const double HoleEnd = 0.86;       // 雾心空洞推到哪（相对半径）
        private const double RampEnd = 0.94;       // 雾墙内侧坡度结束位置

        // ===== 雾纹 =====
        private const double NoisePeak = 0.85;
        private const double NoiseGrow = 1.14;
        private const double NoiseDriftX = -22;
        private const double NoiseDriftY = -12;

        // ===== 雾色（单一灰色雾，全程不变深，两个方向共用）=====
        // 按需求不做"由灰白逐渐变黑"的颜色渐变，整段过渡都是灰色雾。
        // 若要恢复变色，把这里改回 From/To 两色并启用 ColorAnimation（可参考 git 历史）。
        private static readonly Color FogCoreColor = Rgb(0xA9, 0xB2, 0xBF); // 雾体：灰白雾（负责遮住整屏）
        private static readonly Color FogTintColor = Rgb(0x7C, 0x87, 0x98); // 雾纹：略深的冷灰（与雾体有明度差，纹理才可见）

        // ===== 资源（全部代码创建，避免 XAML 声明的 Freezable 被冻结后无法动画）=====
        private readonly RadialGradientBrush _coreBrush;
        private readonly RadialGradientBrush _noiseBrush;
        private readonly ImageBrush _noiseMask;
        private readonly ScaleTransform _texScale;
        private readonly TranslateTransform _texDrift;

        private readonly List<(DependencyObject Target, DependencyProperty Prop)> _animated = new();

        private DispatcherTimer? _swapTimer;
        private DispatcherTimer? _finishTimer;
        private Action? _swapTheme;
        private bool _swapped;

        public ThemeFogOverlay()
        {
            InitializeComponent();

            _coreBrush = BuildBrush();
            _noiseBrush = BuildBrush();

            FogCore.Fill = _coreBrush;
            FogNoise.Fill = _noiseBrush;

            _texScale = new ScaleTransform(1, 1);
            _texDrift = new TranslateTransform(0, 0);

            var group = new TransformGroup();
            group.Children.Add(_texScale);
            group.Children.Add(_texDrift);

            _noiseMask = new ImageBrush(FogTextureFactory.Get())
            {
                Stretch = Stretch.Fill,
                Transform = group
            };
            FogNoise.OpacityMask = _noiseMask;

            RegisterAnimated();
        }

        /// <summary>过渡动画是否正在播放。</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 播放一次主题切换过渡。
        /// </summary>
        /// <param name="origin">迷雾扩散中心（本控件坐标，通常是主题按钮中心）</param>
        /// <param name="swapTheme">雾盖满全屏时执行的主题切换动作</param>
        /// <returns>true = 已接管本次切换；false = 正在播放中，调用方应忽略本次点击</returns>
        public bool Play(Point origin, Action swapTheme)
        {
            if (IsPlaying) return false;

            double w = ActualWidth;
            double h = ActualHeight;

            // 布局还没准备好就不做动画，直接切主题，保证一定能切过去
            if (w < 1 || h < 1)
            {
                swapTheme();
                return true;
            }

            if (double.IsNaN(origin.X) || double.IsNaN(origin.Y))
                origin = new Point(w / 2, h / 2);

            origin = new Point(Math.Clamp(origin.X, 0, w), Math.Clamp(origin.Y, 0, h));

            IsPlaying = true;
            IsHitTestVisible = true;
            _swapTheme = swapTheme;
            _swapped = false;

            try
            {
                // 覆盖半径：让最远角落在平台区（0.78）之内，再留 3% 余量
                double reach = MaxCornerDistance(origin, w, h) / PlateauEnd * 1.03;

                ResetState(origin);
                BuildTimeline(reach);
                _swapTimer = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(SwapMs)
                };
                _swapTimer.Tick += (s, e) =>
                {
                    _swapTimer?.Stop();
                    _swapTimer = null;
                    DoSwap();
                };
                _swapTimer.Start();

                // 收尾
                _finishTimer = new DispatcherTimer(DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(FinishMs)
                };
                _finishTimer.Tick += (s, e) =>
                {
                    _finishTimer?.Stop();
                    _finishTimer = null;
                    Finish();
                };
                _finishTimer.Start();

                return true;
            }
            catch
            {
                Finish();
                return true;
            }
        }

        // ============ 时间线 ============

        /// <summary>
        /// 搭时间线。<b>每条属性只发一条关键帧曲线走完全程</b>——
        /// 同一个属性上二次 BeginAnimation 会把前一条整个替换掉（会导致滚进动画被顶掉）。
        /// </summary>
        private void BuildTimeline(double reach)
        {
            double hold = HoldMs;
            double sweepEnd = HoldMs + SweepMs;
            IEasingFunction spread = EaseInOut();
            IEasingFunction rollIn = EaseOut(2);

            foreach (var brush in new[] { _coreBrush, _noiseBrush })
            {
                // 半径：2px 滚进来 → 保持 → 散雾时再向外膨胀
                foreach (var prop in new[] { RadialGradientBrush.RadiusXProperty, RadialGradientBrush.RadiusYProperty })
                {
                    Keys(brush, prop,
                        (0, RadiusStart, null),
                        (RollInMs, reach, rollIn),
                        (hold, reach, null),
                        (sweepEnd, reach * RadiusEndScale, spread));
                }

                // 挡位：[0] 雾心空洞沿 [1] 雾墙内侧坡度 [2] 平台结束（散雾时一起外推）
                Keys(brush.GradientStops[0], GradientStop.OffsetProperty,
                    (0, 0.0, null), (hold, 0.0, null), (sweepEnd, HoleEnd, spread));
                Keys(brush.GradientStops[1], GradientStop.OffsetProperty,
                    (0, RampStart, null), (hold, RampStart, null), (sweepEnd, RampEnd, spread));
                Keys(brush.GradientStops[2], GradientStop.OffsetProperty,
                    (0, PlateauEnd, null), (hold, PlateauEnd, null), (sweepEnd, 1.0, spread));
            }

            // 雾体浓度：240ms 就变实，保证 460ms 时一定是满覆盖
            Keys(FogCore, UIElement.OpacityProperty,
                (0, 0.0, null),
                (240, 1.0, rollIn),
                (CoreFadeAtMs, 1.0, null),
                (CoreFadeAtMs + CoreFadeMs, 0.0, EaseOut(2)));

            // 雾纹：比雾体早到（雾丝先涌进来），全程缓慢膨胀 + 漂移
            Keys(FogNoise, UIElement.OpacityProperty,
                (0, 0.0, null),
                (200, NoisePeak, rollIn),
                (hold, NoisePeak, null),
                (hold + NoiseFadeMs, 0.0, EaseOut(2)));

            Keys(_texScale, ScaleTransform.ScaleXProperty, (0, 1.0, null), (FinishMs, NoiseGrow, null));
            Keys(_texScale, ScaleTransform.ScaleYProperty, (0, 1.0, null), (FinishMs, NoiseGrow, null));
            Keys(_texDrift, TranslateTransform.XProperty, (0, 0.0, null), (FinishMs, NoiseDriftX, null));
            Keys(_texDrift, TranslateTransform.YProperty, (0, 0.0, null), (FinishMs, NoiseDriftY, null));

            // 雾色：灰白雾 → 浓雾（两个方向共用同一套）
            PaintFog();

            // 换主题的"刻度"：一条不改变任何视觉的动画（Brush.Opacity 恒为 1），
            // 走完 RollInMs 触发换主题。此刻雾已 100% 盖满整窗，且跟着时钟走不会漂。
            var marker = Keys(_coreBrush, Brush.OpacityProperty, (0, 1.0, null), (RollInMs, 1.0, null));
            marker.Completed += (s, e) => DoSwap();
        }

        // ============ 配色 ============

        /// <summary>
        /// 给两层雾上色。当前为<b>单一灰色雾</b>：整段过渡颜色不变，只有滚动/散开两种运动。
        /// <para>
        /// 两个方向共用同一套雾色，所以无论浅→深还是深→浅，观感都是"灰雾涌来 → 灰雾散开"。
        /// </para>
        /// </summary>
        private void PaintFog()
        {
            PaintStops(_coreBrush, FogCoreColor);
            PaintStops(_noiseBrush, FogTintColor);
        }

        private static void PaintStops(RadialGradientBrush brush, Color c)
        {
            var s = brush.GradientStops;

            // 挡位语义：[0] 雾心空洞（alpha 0） [1] 雾墙内侧（alpha 1）
            //           [2] 雾墙外侧平台（alpha 1） [3] 外缘（alpha 0）
            s[0].Color = WithAlpha(c, 0);
            s[1].Color = c;                 // ★ alpha 必须为 1，否则中心区域盖不实
            s[2].Color = c;
            s[3].Color = WithAlpha(c, 0);
        }

        // ============ 状态复位 / 收尾 ============

        private void ResetState(Point origin)
        {
            foreach (var brush in new[] { _coreBrush, _noiseBrush })
            {
                brush.Center = origin;
                brush.GradientOrigin = origin;
                brush.RadiusX = RadiusStart;
                brush.RadiusY = RadiusStart;

                var s = brush.GradientStops;
                s[0].Offset = 0.0;
                s[1].Offset = RampStart;
                s[2].Offset = PlateauEnd;
                s[3].Offset = 1.0;
            }

            FogCore.Opacity = 0;
            FogNoise.Opacity = 0;
            _coreBrush.Opacity = 1;   // 换主题刻度用的标记动画（恒为 1）

            _texScale.ScaleX = 1;
            _texScale.ScaleY = 1;
            _texDrift.X = 0;
            _texDrift.Y = 0;
        }

        private void DoSwap()
        {
            if (_swapped) return;
            _swapped = true;
            _swapTheme?.Invoke();
        }

        private void Finish()
        {
            _swapTimer?.Stop();
            _swapTimer = null;
            _finishTimer?.Stop();
            _finishTimer = null;

            DoSwap();   // 兜底：无论如何主题一定要切过去

            foreach (var (target, prop) in _animated)
                Stop(target, prop);

            FogCore.Opacity = 0;
            FogNoise.Opacity = 0;

            IsHitTestVisible = false;
            IsPlaying = false;
            _swapTheme = null;
        }

        // ============ 动画小工具 ============

        private static DoubleAnimationUsingKeyFrames Keys(DependencyObject target, DependencyProperty prop,
            params (double Ms, double Value, IEasingFunction? Ease)[] frames)
        {
            var anim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };

            foreach (var f in frames)
            {
                var kf = new EasingDoubleKeyFrame(f.Value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(f.Ms)));
                if (f.Ease != null) kf.EasingFunction = f.Ease;
                anim.KeyFrames.Add(kf);
            }

            Begin(target, prop, anim);
            return anim;
        }

        private static void Begin(DependencyObject target, DependencyProperty prop, AnimationTimeline? anim)
        {
            if (target is UIElement element) element.BeginAnimation(prop, anim);
            else if (target is Animatable animatable) animatable.BeginAnimation(prop, anim);
        }

        private static void Stop(DependencyObject target, DependencyProperty prop) => Begin(target, prop, null);

        private static IEasingFunction EaseOut(double power)
            => new PowerEase { Power = power, EasingMode = EasingMode.EaseOut };

        private static IEasingFunction EaseInOut()
            => new SineEase { EasingMode = EasingMode.EaseInOut };

        private static System.Windows.Duration Ms(double ms) => new System.Windows.Duration(TimeSpan.FromMilliseconds(ms));

        private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static double MaxCornerDistance(Point o, double w, double h)
        {
            double d1 = Dist(o, 0, 0);
            double d2 = Dist(o, w, 0);
            double d3 = Dist(o, 0, h);
            double d4 = Dist(o, w, h);
            return Math.Max(Math.Max(d1, d2), Math.Max(d3, d4));
        }

        private static double Dist(Point o, double x, double y)
        {
            double dx = o.X - x;
            double dy = o.Y - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 雾的渐变结构（绝对映射，中心与半径由动画驱动）：
        ///   [0] 雾心空洞外沿（alpha 0）      [1] 雾墙内侧坡度结束（alpha 1）
        ///   [2] 雾墙外侧平台结束（alpha 1）  [3] 外缘（alpha 0，固定 1.0）
        /// 起始时 [0][1] 都在 0，等于整片实雾；散雾时 [0][1][2] 一起外推成一道会走的雾墙。
        /// </summary>
        private static RadialGradientBrush BuildBrush()
        {
            var stops = new GradientStopCollection
            {
                new GradientStop(Colors.Transparent, 0.0),
                new GradientStop(Colors.Transparent, RampStart),
                new GradientStop(Colors.Transparent, PlateauEnd),
                new GradientStop(Colors.Transparent, 1.0)
            };

            return new RadialGradientBrush(stops)
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(0, 0),
                GradientOrigin = new Point(0, 0),
                RadiusX = RadiusStart,
                RadiusY = RadiusStart
            };
        }

        private void RegisterAnimated()
        {
            foreach (var brush in new[] { _coreBrush, _noiseBrush })
            {
                _animated.Add((brush, RadialGradientBrush.RadiusXProperty));
                _animated.Add((brush, RadialGradientBrush.RadiusYProperty));

                var s = brush.GradientStops;
                _animated.Add((s[0], GradientStop.OffsetProperty));
                _animated.Add((s[1], GradientStop.OffsetProperty));
                _animated.Add((s[2], GradientStop.OffsetProperty));
                _animated.Add((s[3], GradientStop.OffsetProperty));
            }

            _animated.Add((FogCore, UIElement.OpacityProperty));
            _animated.Add((FogNoise, UIElement.OpacityProperty));
            _animated.Add((_coreBrush, Brush.OpacityProperty));
            _animated.Add((_texScale, ScaleTransform.ScaleXProperty));
            _animated.Add((_texScale, ScaleTransform.ScaleYProperty));
            _animated.Add((_texDrift, TranslateTransform.XProperty));
            _animated.Add((_texDrift, TranslateTransform.YProperty));
        }
    }
}
