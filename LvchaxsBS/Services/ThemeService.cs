using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LvchaxsBS.Config;

namespace LvchaxsBS.Services
{
    public enum AppTheme { Light, Dark }

    public static class ThemeService
    {
        private static readonly Uri LightUri = new("pack://application:,,,/LvchaxsBS;component/UI/Assets/Themes/Light.xaml");
        private static readonly Uri DarkUri = new("pack://application:,,,/LvchaxsBS;component/UI/Assets/Themes/Dark.xaml");

        /// <summary>主题色渐变的时长（毫秒）。</summary>
        private const double FadeMs = 300;

        /// <summary>备用方式（逐帧换实例）的每帧间隔（毫秒）。</summary>
        private const double StepMs = 16;

        public static AppTheme Current { get; private set; } = AppTheme.Light;

        public static event EventHandler<AppTheme>? ThemeChanged;

        // ===== 一次渐变过程中的临时状态（动画方式）=====
        private static readonly List<string> _fadingKeys = new();
        private static readonly List<SolidColorBrush> _fadingBrushes = new();
        private static DispatcherTimer? _fadeTimer;
        private static bool _hasApplied;

        // ===== 一次渐变过程中的临时状态（逐帧换实例方式）=====
        private static readonly Dictionary<string, (Color From, Color To)> _stepPairs = new(StringComparer.Ordinal);
        private static Application? _stepApp;
        private static DateTime _stepStart;
        private static DispatcherTimer? _stepTimer;

        /// <summary>
        /// 应用主题：换掉主题字典，并让<b>每个主题色自己从旧色渐变到新色</b>
        /// （背景、卡片、文字、边框、标签…一起过渡，不是整窗硬切）。
        /// </summary>
        /// <param name="theme">目标主题。</param>
        /// <param name="animate">false = 不做渐变（启动时用：首帧还没渲染出来，渐变没有意义）。</param>
        public static void Apply(AppTheme theme, bool animate = true)
        {
            var app = Application.Current;
            if (app == null) return;

            // 上一次渐变还没落定（连点主题按钮）→ 先落定，再从当前颜色接着渐变
            FinishFade();

            var newDict = new ResourceDictionary { Source = theme == AppTheme.Light ? LightUri : DarkUri };
            var keys = newDict.Keys.OfType<object>()
                .Select(k => k?.ToString())
                .Where(k => !string.IsNullOrEmpty(k))
                .Select(k => k!)
                .ToList();

            // ① 渐变起点：换字典之前，每个键现在生效的颜色
            var from = Snapshot(app, keys);

            // ② 换上新主题 —— 此刻资源已经是终值，但这一帧还没渲染出去，看不到跳变
            var dicts = app.Resources.MergedDictionaries;
            ReplaceThemeDict(dicts, FindThemeDictionary(dicts), newDict);

            Current = theme;

            // 主题色变了，按新主题重新计算 控件/窗口 透明度覆盖色
            AppearanceService.ReapplyOpacities();

            bool first = !_hasApplied;
            _hasApplied = true;

            // ③ 覆盖成起点色、挂上动画（每帧只动 43 个画刷的颜色）
            //    ★ 必须在广播 ThemeChanged **之前**：导航栏边框这类「由代码赋色」的控件
            //      是在 ThemeChanged 里取画刷的，先挂动画它们取到的就是这只正在渐变的画刷，
            //      于是跟着一起过渡；反过来就会瞬间跳到新色。
            if (animate && !first)
            {
                try
                {
                    StartFade(app, from, Snapshot(app, keys));
                }
                catch
                {
                    // 渐变只是观感：出问题就退回"直接切"，主题本身在 ② 已经换好了
                    FinishFade();
                }
            }

            ThemeChanged?.Invoke(null, theme);
        }

        /// <summary>
        /// 取某个主题色的「终值」—— 绕过渐变过程中临时写在 <c>Application.Resources</c> 上的画刷。
        /// <para>
        /// ★ 给「把画刷取走自己长期留着」的调用方用（典型：插件里重建 Style 时取常态背景色）。
        ///   渐变途中直接 <c>TryFindResource</c> 拿到的是那只<b>正在从旧色变到新色</b>的临时画刷，
        ///   把它塞进 Style 后颜色会被固定成某一帧的中间值 ⇒ 表现为「切换主题后按钮背景是另一个主题的」。
        /// </para>
        /// </summary>
        /// <param name="key">主题色键（如 <c>AppBackgroundBrush</c>）。</param>
        /// <returns>一个**新的、未冻结**的画刷（调用方可以随便塞进 Style）；键不存在或取不到返回 null。</returns>
        public static Brush? GetThemeBrush(string key)
        {
            try
            {
                var app = Application.Current;
                if (app == null || string.IsNullOrEmpty(key)) return null;

                var dict = FindThemeDictionary(app.Resources.MergedDictionaries);
                if (dict == null || !dict.Contains(key)) return null;

                // ★ 返回副本：主题字典里那只会被样式 seal 冻结，交给外部保存/修改不安全
                return dict[key] is SolidColorBrush solid
                    ? new SolidColorBrush(solid.Color)
                    : dict[key] as Brush;
            }
            catch
            {
                return null;
            }
        }

        public static void Toggle()
        {
            var next = Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
            Apply(next);

            var settings = ConfigManager.Get<AppSettings>();
            settings.Theme = next.ToString();
            ConfigManager.Save(settings);
        }

        public static void LoadFromConfig()
        {
            var settings = ConfigManager.Get<AppSettings>();
            var theme = string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark
                : AppTheme.Light;
            Apply(theme, animate: false);
        }

        // ============ 颜色渐变 ============

        /// <summary>读出这批键「现在生效的颜色」（含透明度覆盖写进 <c>Application.Resources</c> 的那些）。</summary>
        private static Dictionary<string, Color> Snapshot(Application app, List<string> keys)
        {
            var map = new Dictionary<string, Color>(StringComparer.Ordinal);

            foreach (string key in keys)
            {
                if (app.TryFindResource(key) is SolidColorBrush brush)
                    map[key] = brush.Color;
            }

            return map;
        }

        /// <summary>
        /// 把每个要变的键临时覆盖成一个「起点色」的画刷，再动画到终点色。
        /// <para>
        /// ★ 写在 <c>Application.Resources</c> 自身条目里（优先级高于合并进来的主题字典），
        ///   所以连"被透明度覆盖顶掉的背景色"也一起渐变；渐变结束后 <see cref="FinishFade"/> 撤掉这些临时条目。
        /// </para>
        /// <para>
        /// ★★ <b>必须先挂动画、再放进资源</b>：画刷一旦进资源字典，样式/模板 seal 的时候
        ///   会被 WPF 自动 <c>Freeze()</c>，冻住的对象再 <c>BeginAnimation</c> 会直接抛
        ///   <c>InvalidOperationException</c>（"该对象已密封或已冻结"）。反过来先挂上动画，
        ///   <c>CanFreeze</c> 就变成 false，seal 时冻不住它，动画照常跑。
        /// </para>
        /// </summary>
        private static void StartFade(Application app, Dictionary<string, Color> from, Dictionary<string, Color> to)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(FadeMs));
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            int pending = 0;   // 真正需要渐变的键数量（两主题同色的键不算）

            foreach (string key in from.Keys)
            {
                if (!to.TryGetValue(key, out Color target)) continue;

                Color start = from[key];
                if (start == target) continue;   // 两主题同色的键（如 PinBarBrush）不用动

                pending++;

                var brush = new SolidColorBrush(start);

                try
                {
                    brush.BeginAnimation(SolidColorBrush.ColorProperty,
                        new ColorAnimation(start, target, duration) { EasingFunction = ease });

                    app.Resources[key] = brush;

                    // 万一路子上还是把它冻住了 → 撤掉这个键的临时覆盖（交由终值，等于这一个键硬切），
                    // 绝不能让它停在起点色上
                    if (brush.IsFrozen)
                    {
                        app.Resources.Remove(key);
                        continue;
                    }

                    _fadingKeys.Add(key);
                    _fadingBrushes.Add(brush);
                }
                catch
                {
                    // 渐变只是锦上添花：这一个键动不了就动不了，主题本身已经切好了
                    try { app.Resources.Remove(key); } catch { }
                }
            }

            if (_fadingKeys.Count == 0)
            {
                // 一个键都没挂上动画（画刷全被冻住）→ 换"逐帧换实例"的方式，保证渐变一定看得见
                if (pending > 0) StartStepFade(app, from, to);
                return;
            }

            _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FadeMs + 80) };
            _fadeTimer.Tick += (_, _) => FinishFade();
            _fadeTimer.Start();
        }

        // ============ 渐变：逐帧换实例（动画那条路走不通时的备用方式）============

        /// <summary>
        /// 每帧把每个键换成一个<b>新的</b>插值色画刷。
        /// <para>
        /// 和动画方式的差别：不指望画刷活到下一帧 —— 每帧都是新实例，
        /// 所以"被样式 seal 冻结"这件事根本不影响它。
        /// </para>
        /// </summary>
        private static void StartStepFade(Application app, Dictionary<string, Color> from, Dictionary<string, Color> to)
        {
            _stepPairs.Clear();

            foreach (string key in from.Keys)
            {
                if (!to.TryGetValue(key, out Color target)) continue;

                Color start = from[key];
                if (start != target) _stepPairs[key] = (start, target);
            }

            if (_stepPairs.Count == 0) return;

            _stepApp = app;
            _stepStart = DateTime.Now;

            _stepTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StepMs) };
            _stepTimer.Tick += OnStepTick;
            _stepTimer.Start();
        }

        private static void OnStepTick(object? sender, EventArgs e)
        {
            var app = _stepApp;
            if (app == null) { StopStepFade(); return; }

            double t = (DateTime.Now - _stepStart).TotalMilliseconds / FadeMs;
            bool done = t >= 1.0;
            if (done) t = 1.0;

            double k = SineEaseInOut(t);

            foreach (var pair in _stepPairs)
            {
                Color c = Lerp(pair.Value.From, pair.Value.To, k);
                try { app.Resources[pair.Key] = new SolidColorBrush(c); } catch { }
            }

            if (done) StopStepFade();
        }

        /// <summary>逐帧渐变收尾：撤掉临时条目 + 重建透明度覆盖。</summary>
        private static void StopStepFade()
        {
            _stepTimer?.Stop();
            _stepTimer = null;
            _stepApp = null;

            if (_stepPairs.Count == 0) return;

            var app = Application.Current;
            if (app != null)
            {
                foreach (string key in _stepPairs.Keys)
                {
                    try { app.Resources.Remove(key); } catch { }
                }

                AppearanceService.ReapplyOpacities();
            }

            _stepPairs.Clear();
        }

        private static double SineEaseInOut(double t)
            => 0.5 - Math.Cos(Math.PI * Math.Clamp(t, 0, 1)) / 2.0;

        private static Color Lerp(Color a, Color b, double k)
            => Color.FromArgb(
                (byte)Math.Round(a.A + (b.A - a.A) * k),
                (byte)Math.Round(a.R + (b.R - a.R) * k),
                (byte)Math.Round(a.G + (b.G - a.G) * k),
                (byte)Math.Round(a.B + (b.B - a.B) * k));

        /// <summary>
        /// 结束渐变：摘掉动画、删掉临时条目，再把透明度覆盖重建回来
        /// （临时条目顶掉过它，不重建的话背景会退回不带 alpha 的原色）。
        /// </summary>
        private static void FinishFade()
        {
            _fadeTimer?.Stop();
            _fadeTimer = null;

            StopStepFade();   // 上一次若是逐帧方式在跑，同样先落定

            if (_fadingKeys.Count == 0) return;

            var app = Application.Current;
            if (app != null)
            {
                // 先撤临时条目（画刷随后就没人引用了），再摘动画，最后重建透明度覆盖
                foreach (string key in _fadingKeys)
                {
                    try { app.Resources.Remove(key); } catch { }
                }

                // ★ 摘动画前先把终值写进本地值：摘掉动画后画刷会退回到本地值（= 起点色），
                //   而有些控件是在渐变途中把这只画刷取走自己留着的（导航栏边框、插件里重建的样式），
                //   不固化的话它们会停在旧主题色上 —— 那就是"背景颜色异常"。
                foreach (var brush in _fadingBrushes)
                {
                    try
                    {
                        Color settled = brush.Color;
                        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                        brush.Color = settled;
                    }
                    catch { }
                }

                AppearanceService.ReapplyOpacities();
            }

            _fadingKeys.Clear();
            _fadingBrushes.Clear();
        }

        // ============ 主题字典 ============

        private static ResourceDictionary? FindThemeDictionary(Collection<ResourceDictionary> dicts)
            => dicts.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.EndsWith("Light.xaml") ||
                 d.Source.OriginalString.EndsWith("Dark.xaml")));

        private static void ReplaceThemeDict(Collection<ResourceDictionary> dicts,
            ResourceDictionary? oldDict, ResourceDictionary newDict)
        {
            if (oldDict == null)
            {
                dicts.Insert(0, newDict);
                return;
            }

            int index = dicts.IndexOf(oldDict);
            dicts.Remove(oldDict);
            dicts.Insert(index < 0 ? 0 : Math.Min(index, dicts.Count), newDict);
        }

        /// <summary>
        /// 主题切换的统一入口（插件里的主题按钮通过反射调它）。
        /// <para>
        /// 过渡方式是「每个主题色自己渐变到新色」，整窗背景、卡片、文字、边框一起过渡，
        /// 所以不需要额外的遮挡层；插件页面（在 MainFrame 里）用的是同一批主题资源，观感一致。
        /// </para>
        /// </summary>
        /// <param name="originSource">保留参数：渐变不区分起点，传什么都不影响。</param>
        /// <returns>恒为 true：渐变随时可以重新开始（连点会从当前中间色接着过渡）。</returns>
        public static bool ToggleWithTransition(Visual? originSource = null)
        {
            Toggle();
            return true;
        }
    }
}