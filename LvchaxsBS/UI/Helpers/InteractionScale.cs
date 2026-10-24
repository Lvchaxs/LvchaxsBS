using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LvchaxsBS.UI.Helpers
{
    /// <summary>
    /// 全局统一的「悬停放大 / 按下缩小」交互反馈行为（附加属性）。
    ///
    /// <para>
    /// 以前每个 ControlTemplate 都各自抄一份 EventTrigger + BeginStoryboard 来做这件事
    /// （按钮、文件夹图标、复选框方块、胶囊开关、滑块把手…一共 6 处、每处 40~90 行），
    /// 抄出来的副本除了数值不同完全一样，而且写法上都有同一个毛病：
    /// <c>PreviewMouseUp</c> 会把缩放<b>直接写死</b>成"悬停值"。于是"按下 → 把鼠标拖出按钮 →
    /// 松开"这条路径会先被 MouseLeave 收回、再被 MouseUp 放大，最终卡在放大态。
    /// 现在动画只有这一份实现，上面这些问题也一并修掉。
    /// </para>
    ///
    /// <para>
    /// 用法（数值写在 Style 上，或者直接写在模板元素上）：
    /// <code>
    /// &lt;Setter Property="helpers:InteractionScale.HoverScale" Value="1.06"/&gt;
    /// &lt;Setter Property="helpers:InteractionScale.PressScale" Value="0.97"/&gt;
    /// </code>
    /// 只想缩放元素内部的某个子元素（比如按钮里的图标、复选框里的方块）时，
    /// 用 <c>Target</c> 指过去即可，此时事件仍然挂在宿主元素上，所以命中区域不变：
    /// <code>
    /// helpers:InteractionScale.Target="{Binding ElementName=FolderIcon}"
    /// </code>
    /// </para>
    ///
    /// <para>与旧写法相比的稳定性改进：</para>
    /// <list type="bullet">
    /// <item>
    /// <b>单元素单动画</b>：重复触发时走 <see cref="HandoffBehavior.SnapshotAndReplace"/>，
    /// 新动画从「当前实际值」续播。鼠标快速划过时不会出现两条动画互顶、数值来回跳。
    /// </item>
    /// <item>
    /// <b>按下状态可追踪</b>：按下 → 拖出 → 松开，会正确回到常态；按下中移出也不会提前缩回
    /// （遍历到"拖动中的滑块把手"这类场景仍然保持放大）。
    /// </item>
    /// <item>
    /// <b>卸载复位</b>：元素从可视树摘除时停掉动画并落回 1.0，
    /// 避免容器复用/回收后残留一个缩放值。
    /// </item>
    /// <item>
    /// <b>不做无意义的动画</b>：目标值已经等于当前值时直接跳过，不产生多余的动画时钟。
    /// </item>
    /// </list>
    /// </summary>
    public static class InteractionScale
    {
        // ===== 时长（毫秒，与重构前各处 XAML 的取值保持一致）=====

        /// <summary>悬停到位 / 移开复位时长（原 XAML 各处统一是 0.15s）。</summary>
        private const double HoverMs = 150;

        /// <summary>按下缩小时长（原 XAML 是 0.05s）。</summary>
        private const double PressMs = 60;

        /// <summary>松开回弹时长（原 XAML 是 0.1s）。</summary>
        private const double ReleaseMs = 110;

        /// <summary>常态缩放值。</summary>
        private const double Neutral = 1.0;

        /// <summary>判定「鼠标刚才动过」的时间窗（毫秒）。</summary>
        private const double MouseActiveMs = 150;

        /// <summary>
        /// 最近一次鼠标输入（移动 / 按键 / 滚轮）的时间戳。
        /// 用来区分两件事：**鼠标移到元素上** vs **元素自己跑到静止的鼠标下面**。
        /// </summary>
        private static long _lastMouseInputTicks;

        static InteractionScale()
        {
            try
            {
                InputManager.Current.PreProcessInput += (_, e) =>
                {
                    if (e.StagingItem?.Input is MouseEventArgs)
                        _lastMouseInputTicks = Environment.TickCount64;
                };
            }
            catch
            {
                // 拿不到 InputManager（理论上不会）：退化成"总是播动画"，行为和改动前一致
            }
        }

        /// <summary>
        /// 鼠标是否刚刚动过（有真实的鼠标输入）。
        /// false = 是**元素**自己跑到静止的鼠标下面（列表重排 / 容器重建 / 弹窗出现…），
        /// 这种情况不该再播一次悬停动画 —— 否则"点头像置顶"会看到缩放动画被播了两遍。
        /// </summary>
        private static bool MouseMovedRecently()
            => Environment.TickCount64 - _lastMouseInputTicks < MouseActiveMs;

        /// <summary>判定"数值没变"的容差，用来跳过无意义的动画。</summary>
        private const double Epsilon = 0.0005;

        /// <summary>
        /// 所有缩放动画共用的缓动曲线。
        /// <para>
        /// ★ 原来是每次 <see cref="AnimateTo"/> 都 <c>new CubicEase</c>（一次悬停进出 = 4 个动画 = 4 个对象）。
        ///   <see cref="Freezable"/> 冻结后可以安全地被多条动画同时引用，这里做成静态只读，
        ///   行为与曲线完全不变，只是不再往 GC 里丢垃圾。
        /// </para>
        /// </summary>
        private static readonly IEasingFunction ScaleEase = CreateFrozenEase();

        private static IEasingFunction CreateFrozenEase()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ease.Freeze();
            return ease;
        }

        // ============ 附加属性 ============

        /// <summary>悬停时的缩放值。0（默认）= 不做悬停缩放。</summary>
        public static readonly DependencyProperty HoverScaleProperty =
            DependencyProperty.RegisterAttached(
                "HoverScale",
                typeof(double),
                typeof(InteractionScale),
                new PropertyMetadata(0.0, OnConfigChanged));

        /// <summary>按下时的缩放值。0（默认）= 不做按下缩放。</summary>
        public static readonly DependencyProperty PressScaleProperty =
            DependencyProperty.RegisterAttached(
                "PressScale",
                typeof(double),
                typeof(InteractionScale),
                new PropertyMetadata(0.0, OnConfigChanged));

        /// <summary>
        /// 实际被缩放的子元素。默认 null = 缩放宿主元素自身。
        /// 事件（进出/按下/松开）始终挂在宿主元素上，所以换 Target 不会改变命中区域。
        /// </summary>
        public static readonly DependencyProperty TargetProperty =
            DependencyProperty.RegisterAttached(
                "Target",
                typeof(FrameworkElement),
                typeof(InteractionScale),
                new PropertyMetadata(null));

        /// <summary>每个元素的运行时状态（挂在元素自己身上，元素回收时一起回收）。</summary>
        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State",
                typeof(InteractionState),
                typeof(InteractionScale),
                new PropertyMetadata(null));

        public static double GetHoverScale(DependencyObject obj) => (double)obj.GetValue(HoverScaleProperty);
        public static void SetHoverScale(DependencyObject obj, double value) => obj.SetValue(HoverScaleProperty, value);

        public static double GetPressScale(DependencyObject obj) => (double)obj.GetValue(PressScaleProperty);
        public static void SetPressScale(DependencyObject obj, double value) => obj.SetValue(PressScaleProperty, value);

        public static FrameworkElement? GetTarget(DependencyObject obj) => (FrameworkElement?)obj.GetValue(TargetProperty);
        public static void SetTarget(DependencyObject obj, FrameworkElement? value) => obj.SetValue(TargetProperty, value);

        private static InteractionState? GetState(DependencyObject obj) => (InteractionState?)obj.GetValue(StateProperty);
        private static void SetState(DependencyObject obj, InteractionState? value) => obj.SetValue(StateProperty, value);

        // ============ 挂载 / 卸载 ============

        private static void OnConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement owner)
                return;

            bool wanted = IsEnabledOn(owner);
            bool attached = GetState(owner) != null;

            if (wanted && !attached)
                Attach(owner);
            else if (!wanted && attached)
                Detach(owner);
        }

        private static bool IsEnabledOn(FrameworkElement owner)
            => GetHoverScale(owner) > 0 || GetPressScale(owner) > 0;

        private static void Attach(FrameworkElement owner)
        {
            var state = new InteractionState(owner);
            SetState(owner, state);

            owner.MouseEnter += state.OnMouseEnter;
            owner.MouseLeave += state.OnMouseLeave;
            owner.PreviewMouseLeftButtonDown += state.OnMouseDown;
            owner.PreviewMouseLeftButtonUp += state.OnMouseUp;
            owner.LostMouseCapture += state.OnLostMouseCapture;
            owner.Unloaded += state.OnUnloaded;
        }

        private static void Detach(FrameworkElement owner)
        {
            var state = GetState(owner);
            if (state == null)
                return;

            owner.MouseEnter -= state.OnMouseEnter;
            owner.MouseLeave -= state.OnMouseLeave;
            owner.PreviewMouseLeftButtonDown -= state.OnMouseDown;
            owner.PreviewMouseLeftButtonUp -= state.OnMouseUp;
            owner.LostMouseCapture -= state.OnLostMouseCapture;
            owner.Unloaded -= state.OnUnloaded;

            state.Reset();
            SetState(owner, null);
        }

        // ============ 运行时状态 ============

        private sealed class InteractionState
        {
            private readonly FrameworkElement _owner;
            private ScaleTransform? _scale;
            private bool _pressed;

            /// <summary>当前这条缩放动画的目标值，用来判断"要不要真的重开一条动画"。</summary>
            private double _target = Neutral;

            public InteractionState(FrameworkElement owner) => _owner = owner;

            public void OnMouseEnter(object sender, MouseEventArgs e)
            {
                ClearStalePressed();

                if (e.LeftButton == MouseButtonState.Pressed && _pressed)
                    return;   // 按下状态优先，别被进出的悬停动画顶掉

                double hover = GetHoverScale(_owner) > 0 ? GetHoverScale(_owner) : Neutral;

                // ★ 鼠标没动过 → 是元素自己跑到鼠标下面（列表重排后卡片换了位置），
                //   直接落到悬停值、不播动画；不这么做的话，重排瞬间旧卡片 MouseLeave（缩回）
                //   + 新卡片 MouseEnter（放大）会各播一次，看着就是"动画又来了一遍"。
                if (!MouseMovedRecently())
                {
                    ApplyInstantly(hover);
                    return;
                }

                AnimateTo(hover, HoverMs);
            }

            public void OnMouseLeave(object sender, MouseEventArgs e)
            {
                ClearStalePressed();

                if (_pressed)
                    return;   // 按住拖出时保持按下反馈，等松开再回弹

                if (!MouseMovedRecently())
                {
                    ApplyInstantly(Neutral);   // 同 OnMouseEnter：元素挪走了就别再演一遍
                    return;
                }

                AnimateTo(Neutral, HoverMs);
            }

            public void OnMouseDown(object sender, MouseButtonEventArgs e)
            {
                double press = GetPressScale(_owner);
                if (press <= 0)
                    return;

                _pressed = true;
                AnimateTo(press, PressMs);
            }

            public void OnMouseUp(object sender, MouseButtonEventArgs e)
            {
                if (!_pressed)
                    return;

                _pressed = false;

                // 松开后回到"鼠标现在在哪就应该在哪"的形态：
                // 还在元素上 → 悬停值；已经拖出去了 → 常态。
                // 旧写法在这里写死成悬停值，所以拖出去松开就会卡在放大态。
                double target = _owner.IsMouseOver && GetHoverScale(_owner) > 0
                    ? GetHoverScale(_owner)
                    : Neutral;

                AnimateTo(target, ReleaseMs);
            }

            public void OnLostMouseCapture(object sender, MouseEventArgs e)
            {
                // 捕获丢了（弹窗/提示抢走、按钮被换掉…）→ 这一次按下已经结束了，
                // 按"鼠标现在在哪"回弹，免得卡在按下态
                ClearStalePressed();
                if (_pressed) return;

                AnimateTo(_owner.IsMouseOver && GetHoverScale(_owner) > 0
                    ? GetHoverScale(_owner)
                    : Neutral, ReleaseMs);
            }

            /// <summary>
            /// 清掉「已经不成立的按下状态」。
            /// <para>
            /// ★ <see cref="OnMouseUp"/> 不一定收得到（复制后弹提示、按钮被换掉、捕获被抢…），
            ///   漏一次 <c>_pressed</c> 就永远是 true，之后 <see cref="OnMouseLeave"/> 开头那句
            ///   <c>if (_pressed) return;</c> 会把复位全部挡掉 ——
            ///   表现就是「点一下之后图标一直是大号的，鼠标移开也不回去」。
            ///   所以每次进出都以**鼠标按键的真实状态**为准：左键已经松开了，按下态就该结束。
            /// </para>
            /// </summary>
            private void ClearStalePressed()
            {
                if (_pressed && Mouse.LeftButton != MouseButtonState.Pressed)
                    _pressed = false;
            }

            public void OnUnloaded(object sender, RoutedEventArgs e) => Reset();

            /// <summary>停掉动画并落回常态（元素卸载、行为被移除时调用）。</summary>
            public void Reset()
            {
                _pressed = false;
                _target = Neutral;

                var scale = _scale;
                if (scale == null)
                    return;

                try
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = Neutral;
                    scale.ScaleY = Neutral;
                }
                catch (InvalidOperationException)
                {
                    // 变换已被冻结（理论上不会走到，防御性兜底）：保持现状即可
                }
            }

            /// <summary>
            /// 不播动画、直接落到指定缩放值（元素被动挪到 / 挪出鼠标下方时用）。
            /// 停掉在飞的动画再写值，否则动画的 HoldEnd 会把写进去的值顶掉。
            /// </summary>
            private void ApplyInstantly(double value)
            {
                var scale = EnsureScale();

                try
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                }
                catch (InvalidOperationException)
                {
                    // 变换已冻结：写不了就算了，交互本身不受影响
                }

                scale.ScaleX = value;
                scale.ScaleY = value;
                _target = value;
            }

            // ===== 缩放目标 =====

            /// <summary>拿到（必要时创建）真正要缩放的那条 ScaleTransform。</summary>
            private ScaleTransform EnsureScale()
            {
                if (_scale != null)
                    return _scale;

                var host = GetTarget(_owner) ?? _owner;
                bool created = false;

                switch (host.RenderTransform)
                {
                    case ScaleTransform existing:
                        _scale = existing;
                        break;

                    case TransformGroup group:
                        {
                            // 例如胶囊开关的圆钮：TransformGroup 里已有 ThumbScale + ThumbTranslate，
                            // 必须复用里面那条 ScaleTransform，不能整体替换（否则会打断位移动画）。
                            ScaleTransform? found = null;
                            foreach (var child in group.Children)
                            {
                                if (child is ScaleTransform st)
                                {
                                    found = st;
                                    break;
                                }
                            }

                            if (found == null)
                            {
                                found = new ScaleTransform(Neutral, Neutral);
                                group.Children.Insert(0, found);
                                created = true;
                            }

                            _scale = found;
                            break;
                        }

                    case null:
                        _scale = new ScaleTransform(Neutral, Neutral);
                        host.RenderTransform = _scale;
                        created = true;
                        break;

                    default:
                        {
                            // 已有其它类型的变换（旋转/位移…）：包一层 TransformGroup，别动它
                            var group = new TransformGroup();
                            _scale = new ScaleTransform(Neutral, Neutral);
                            group.Children.Add(_scale);
                            group.Children.Add(host.RenderTransform);
                            host.RenderTransform = group;
                            created = true;
                            break;
                        }
                }

                // 只在我们自己新建变换时补默认锚点；元素原本写在 XAML 里的锚点保持不动。
                if (created)
                    host.RenderTransformOrigin = new Point(0.5, 0.5);

                return _scale;
            }

            private void AnimateTo(double to, double durationMs)
            {
                var scale = EnsureScale();

                // 到位了就不要再开新动画（例如滑块把手只有按下缩放、悬停时目标本来就是 1.0，
                // 进进出出不必各起一条动画）。
                // 注意必须同时看"目标值"和"当前值"：只比当前值的话，
                // 上一次动画还在飞行途中（当前值还没到位）时会误判为"已经到位"而把这次请求吞掉——
                // 表现就是快速进出后停在放大态。
                if (Math.Abs(_target - to) < Epsilon
                    && Math.Abs(scale.ScaleX - to) < Epsilon
                    && Math.Abs(scale.ScaleY - to) < Epsilon)
                    return;

                _target = to;

                var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));

                // 不 Stop 旧动画、也不写 From：BeginAnimation 默认走 SnapshotAndReplace，
                // 新动画以"当前实际值"为起点，所以快速打断也是平滑续播而不是从头跳。
                BeginSafely(scale, ScaleTransform.ScaleXProperty, to, duration);
                BeginSafely(scale, ScaleTransform.ScaleYProperty, to, duration);
            }

            private static void BeginSafely(Animatable target, DependencyProperty prop,
                                            double to, Duration duration)
            {
                try
                {
                    target.BeginAnimation(prop, new DoubleAnimation
                    {
                        To = to,
                        Duration = duration,
                        EasingFunction = ScaleEase
                    });
                }
                catch (InvalidOperationException)
                {
                    // 变换已被冻结时动不了：静默跳过，交互本身不受影响
                }
            }
        }
    }
}
