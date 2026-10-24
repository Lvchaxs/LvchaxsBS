using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LvchaxsBS.Services;

namespace LvchaxsBS.UI.Controls
{
    public partial class TopCenterBar : UserControl
    {
        public event EventHandler<string>? FunctionClicked;

        /// <summary>相邻两个功能图标的间距（XAML 里 Canvas.Left 的步长）。</summary>
        private const double ButtonPitch = 43;

        // 当前是否右对齐
        private bool _alignRight = false;

        /// <summary>模块 Id → 图标按钮。顺序由 <see cref="ApplyModuleOrder"/> 维护。</summary>
        private readonly Dictionary<string, Button> _functionButtons = new();

        /// <summary>当前的图标顺序（模块 Id）。波浪动画的延迟顺序按它算。</summary>
        private List<string> _orderedIds = new();

        /// <summary>每个模块由「顺序」决定的基准位置（Canvas.Left）。</summary>
        private readonly Dictionary<string, double> _baseLeft = new();

        /// <summary>整体偏移（右对齐时整个一排往右推的量）。</summary>
        private double _offset;

        /// <summary>每个按钮的最新目标位置 —— 旧动画落地前要校验它有没有被更新的动画顶掉。</summary>
        private readonly Dictionary<Button, double> _targetLeft = new();

        public TopCenterBar()
        {
            InitializeComponent();

            _functionButtons["QuickTeleport"] = BtnQuickTeleport;
            _functionButtons["QuickPickup"] = BtnQuickPickup;
            _functionButtons["StoryDialogue"] = BtnStoryDialogue;
            _functionButtons["AutoCook"] = BtnAutoCook;
            _functionButtons["FishingAssist"] = BtnFishingAssist;
            _functionButtons["AutoLumber"] = BtnAutoLumber;
            _functionButtons["ControllerPickup"] = BtnControllerPickup;

            // ★ 自定义宏（2026-10-19 重构）不再进这一排功能图标：
            //   入口改为左侧主导航栏（ButtonMacro / TopCenterBar.xaml），与 UID 同逻辑。
            //   所以它不参与这一排的排序，这里不再注册它的图标。

            _orderedIds = _functionButtons.Keys.ToList();
            for (int i = 0; i < _orderedIds.Count; i++)
                _baseLeft[_orderedIds[i]] = i * ButtonPitch;

            SizeChanged += (s, e) => UpdateOffset(_alignRight, animate: false);
        }

        /// <summary>
        /// 按首页功能卡片的新顺序重排这一排图标。
        /// 顺序来源：<c>HomePageSettings.ModuleOrder</c>。
        /// </summary>
        /// <param name="orderedIds">新的模块 Id 顺序。</param>
        /// <param name="animate">
        /// 是否播放滑动动画。
        /// ★★ 启动时（<c>MainWindow.Loaded</c> 里的首次同步）必须传 <c>false</c>：
        ///    那时 XAML 里按钮还停在写死的默认顺序，直接重排会当场播一次 220ms 波浪动画 ——
        ///    用户看到的就是"每次开程序顶部图标自己动一下"。启动时应该是「一上来就是保存的顺序」。
        ///    只有**排序真正被改动**的那一刻（首页拖动完毕）才需要动画。
        /// </param>
        /// <summary>
        /// 插件 dll 补上后热加载：左侧主导航的 UID / 自定义宏按钮显隐由
        /// <see cref="MainWindow.RefreshOptionalModules"/> 统一处理，这里只负责功能图标这排
        /// （快速传送…手柄拾取）的顺序重排。
        /// </summary>
        public void RefreshOptionalModules()
        {
            try
            {
                _orderedIds = _functionButtons.Keys.ToList();
                _baseLeft.Clear();
                for (int i = 0; i < _orderedIds.Count; i++)
                    _baseLeft[_orderedIds[i]] = i * ButtonPitch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[顶栏] 刷新可选模块失败：{ex.Message}");
            }
        }

        public void ApplyModuleOrder(IEnumerable<string>? orderedIds, bool animate = true)
        {
            var ids = orderedIds?
                .Where(id => _functionButtons.ContainsKey(id))
                .Distinct()
                .ToList() ?? new List<string>();

            // 设置里漏了某个模块（新增模块还没来得及存顺序）→ 补在后面，别让图标凭空消失
            foreach (var id in _functionButtons.Keys)
                if (!ids.Contains(id)) ids.Add(id);

            _orderedIds = ids;
            for (int i = 0; i < ids.Count; i++)
                _baseLeft[ids[i]] = i * ButtonPitch;

            // 顺序变了 → 重算每个图标的最终位置：基准位置 + 整体偏移
            ApplyPositions(animate: animate, waveDelay: animate, durationMs: 220);
        }

        private void FunctionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                FunctionClicked?.Invoke(this, name);
            }
        }

        /// <summary>
        /// 设置对齐（波浪滑动）
        /// </summary>
        public void SetAlignment(bool alignRight, bool animate = true)
        {
            _alignRight = alignRight;
            UpdateOffset(alignRight, animate);
        }

        /// <summary>
        /// 核心：根据容器宽度算出整体偏移，再把「基准位置 + 偏移」推给每个图标。
        /// </summary>
        private void UpdateOffset(bool alignRight, bool animate)
        {
            _alignRight = alignRight;

            var container = FunctionButtonsContainer;
            container.UpdateLayout();

            double containerWidth = container.ActualWidth;
            if (containerWidth <= 0) return;

            double totalButtonsWidth = _orderedIds.Count * ButtonPitch;
            _offset = alignRight ? Math.Max(0, containerWidth - totalButtonsWidth) : 0;

            ApplyPositions(animate, waveDelay: true, durationMs: 150);
        }

        /// <summary>
        /// 把 <see cref="_baseLeft"/> + <see cref="_offset"/> 应用到全部图标。
        /// ★★ 位移**只走 Canvas.Left 一条通道**：
        ///    RenderTransform 是 hover 缩放（`InteractionScale`）的地盘 —— 鼠标一划过它就被换成
        ///    TransformGroup/ScaleTransform，若把位移动画也放在 RenderTransform 上，那条 TranslateTransform
        ///    会取到 null，图标从此再也跟不上右移动画，永远钉在原地（2026-10-15 的 bug）。
        /// </summary>
        private void ApplyPositions(bool animate, bool waveDelay, int durationMs)
        {
            int count = _orderedIds.Count;

            for (int i = 0; i < count; i++)
            {
                var btn = _functionButtons[_orderedIds[i]];
                double to = _baseLeft[_orderedIds[i]] + _offset;
                double from = (double)btn.GetValue(Canvas.LeftProperty);
                _targetLeft[btn] = to;

                if (!animate)
                {
                    btn.BeginAnimation(Canvas.LeftProperty, null);
                    btn.SetValue(Canvas.LeftProperty, to);
                    continue;
                }

                if (Math.Abs(from - to) < 0.5) continue;   // 位置没变就不抖

                int delay = waveDelay
                    ? (_offset > 0 ? (count - 1 - i) : i) * 35
                    : 0;

                var anim = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    BeginTime = TimeSpan.FromMilliseconds(delay)
                };

                // 动画结束后「落地」：先摘动画再写值，否则会被动画的 HoldEnd 顶回去。
                // ★ 幂等校验：期间可能又开了更新的动画（连点 / 顺序刚改完就切页面），
                //   这时候旧动画的 Completed 必须认输，否则会把位置写回到上一个目标。
                anim.Completed += (s, _) =>
                {
                    if (!_targetLeft.TryGetValue(btn, out double latest) || Math.Abs(latest - to) > 0.5)
                        return;

                    btn.BeginAnimation(Canvas.LeftProperty, null);
                    btn.SetValue(Canvas.LeftProperty, to);
                };

                btn.BeginAnimation(Canvas.LeftProperty, anim);
            }
        }
    }
}
