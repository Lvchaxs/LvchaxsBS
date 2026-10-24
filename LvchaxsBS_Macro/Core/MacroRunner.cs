using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Core
{
    /// <summary>
    /// 宏脚本执行引擎。
    ///
    /// <para>
    /// ★ 在**后台线程**跑（前台线程会被 Delay 卡死 UI）：
    /// <c>Start</c> 丢一个 <see cref="Task"/>，<c>Stop</c> 只置取消标志，不做 <c>Wait</c>
    /// —— 低级键盘钩子的回调必须尽快返回，堵 200ms 会让系统键盘整体发黏。
    /// </para>
    ///
    /// <para>
    /// ★★ 停止时一定把**按下去还没抬起来的键/鼠标键全部抬起**（<c>finally</c> 里做）：
    /// 否则在长按循环中途松手，游戏里那个键会一直卡着。
    /// </para>
    ///
    /// <para>
    /// ★ 每个执行线程持有**自己**的"已按住"集合（不是全局单例）——
    /// 否则上一轮收尾时会把新一轮刚按下的键一起抬掉。
    /// </para>
    /// </summary>
    public static class MacroRunner
    {
        private static CancellationTokenSource? _cts;

        /// <summary>上一轮执行线程"已经收尾（键都抬起来了）"的信号。</summary>
        private static ManualResetEventSlim? _exited;

        /// <summary>脚本正在跑（<c>_cts</c> 非空 = 还没收尾）。</summary>
        public static bool IsRunning => _cts != null;

        /// <summary>现在正在执行的宏名（调试/诊断用）。</summary>
        public static string CurrentMacroName { get; private set; } = "";

        /// <summary>
        /// 现在正在执行的那条宏（失焦时靠它读**这条宏自己**的生效范围）。
        /// <para>★ 跑完/被停掉时置 null。</para>
        /// </summary>
        public static MacroItem? CurrentMacro { get; private set; }

        /// <summary>
        /// 「按下触发、再次按下结束」：跑着就停，没跑就启动（循环执行）。
        /// </summary>
        public static void Toggle(MacroItem? macro)
        {
            if (_cts != null)
            {
                Stop();
                return;
            }
            Start(macro, loop: true);
        }

        /// <summary>
        /// 排队中等着执行的请求（★ 只有「按下执行一次」会用到）。
        /// <para>
        /// 连点两次时，第二次按下如果第一轮还没跑完，就先记在这里，
        /// 等上一轮**真正收尾（键都抬起来了）**再自动接上 —— 既不打断也不并发。
        /// </para>
        /// <para>
        /// ★ 只留最后一次：连点十下也只再补一遍，不会排成一长串雪崩。
        /// </para>
        /// </summary>
        private static MacroItem? _pending;

        private static readonly object _gate = new object();

        /// <summary>
        /// 启动一条宏。
        /// </summary>
        /// <param name="loop">true = 整段跑完立刻再来一遍（按住/开关型）；false = 只跑一遍（松开即止）。</param>
        public static void Start(MacroItem? macro, bool loop)
        {
            if (macro == null) return;

            // ★★「按下执行一次」连点：上一轮没跑完就**排队**，绝不打断它
            //    （另外两种触发方式不走这里：长按循环是"按住才算"，停下的语义是抬手；
            //     开关型第二次按下是"停止"，更不能排队。）
            if (!loop)
            {
                lock (_gate)
                {
                    if (_cts != null)      // 正在跑 ⇒ 入队，等它自己结束
                    {
                        _pending = macro;
                        return;
                    }
                }
            }

            StartCore(macro, loop);
        }

        /// <summary>
        /// 真正启动一条宏（不判排队 —— 排队判断在 <see cref="Start"/>）。
        /// </summary>
        private static void StartCore(MacroItem? macro, bool loop)
        {
            if (macro == null) return;

            // ★ 脚本分块时只取「这条宏当前触发方式」对应的那个块；
            //   整篇没有块就按老规矩执行全篇（旧脚本兼容）。
            // ★★ 解析前**先翻回英文原码** —— 磁盘上的脚本理论上永远是英文，
            //   但老版本可能存过中文（那时保存的是编辑器显示文本），翻一下才能正常执行。
            var cmds = MacroScript.ParseFor(
                MacroScriptTranslator.Translate(macro.Script ?? "", toChinese: false),
                macro.ModeIndex);
            if (cmds.Count == 0) return;

            Stop();                       // 上一轮还在跑就先让它收尾

            // ★ 等上一轮**真的退出**（它要在 finally 里把按住的键抬起来）再开新一轮 ——
            //   否则连按两次时，旧线程会在新脚本跑到一半时把键抬掉，动作就乱了。
            //   旧线程最长再睡 10ms 就醒（Delay 是按 10ms 一片切的），这里给 60ms 上限兜底。
            _exited?.Wait(60);

            var cts = new CancellationTokenSource();
            _cts = cts;
            CurrentMacroName = macro.Name ?? "";
            CurrentMacro = macro;

            var exited = new ManualResetEventSlim(false);
            _exited = exited;

            CancellationToken token = cts.Token;

            // ★ 告诉宿主「宏开始跑了」⇒ 游戏内那排图标切成 自定义宏2.png
            Utils.HostBridge.ReportMacroRunning(true);

            Task.Run(() =>
            {
                try { RunLoop(cmds, loop, token); }
                finally
                {
                    // ★ 自己是"跑完了一遍"自然退出的（不是被 Stop 掉的）⇒ 也要把 _cts 复位。
                    //   不清的话：① IsRunning 永远为 true；② 后续按下会被误判成"正在跑"而一直入队，
                    //   队列反而永远没人来取。
                    lock (_gate)
                    {
                        if (ReferenceEquals(_cts, cts))
                        {
                            _cts = null;
                            CurrentMacro = null;
                        }
                    }

                    exited.Set();

                    // 收尾完成（按住的键都抬起来了）⇒ 把排队的那一次接上
                    DrainPending();

                    // ★ 忙完了才报"停了"⇒ 图标换回 自定义宏1.png。
                    //   若刚接上排队的那一轮（_cts 又非空），就保持"运行中"，别让图标闪一下。
                    if (_cts == null) Utils.HostBridge.ReportMacroRunning(false);
                }
            });
        }

        /// <summary>
        /// 上一轮**真正收尾后**调用：如果期间有人排队，就把它跑起来。
        /// <para>★ 排队只发生在「按下执行一次」；而且收尾在前、启动在后 ⇒ 两段动作绝不会重叠。</para>
        /// </summary>
        private static void DrainPending()
        {
            MacroItem? next;
            lock (_gate)
            {
                if (_cts != null) return;   // 已经有新一轮在跑了（Stop+Start 场景），别抢
                next = _pending;
                _pending = null;
            }

            if (next != null) StartCore(next, loop: false);
        }

        /// <summary>停止执行（线程会自己抬起按键然后退出，本方法不阻塞）。</summary>
        public static void Stop()
        {
            // ★ 停止就**丢掉排队的那次**：失焦 / 关开关 / 换宏之后都不该继续往下跑
            lock (_gate) { _pending = null; }

            var cts = _cts;
            if (cts == null) return;

            _cts = null;
            CurrentMacro = null;
            cts.Cancel();
        }

        private static void RunLoop(List<MacroCommand> cmds, bool loop, CancellationToken token)
        {
            var heldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var heldButtons = new HashSet<SimulationService.MouseButtonKind>();

            try
            {
                do
                {
                    foreach (MacroCommand c in cmds)
                    {
                        if (token.IsCancellationRequested) return;
                        Apply(c, token, heldKeys, heldButtons);
                    }
                }
                while (loop && !token.IsCancellationRequested);
            }
            finally
            {
                // ★ 无论如何都要松手 —— 不然游戏里那个键会卡住
                ReleaseAll(heldKeys, heldButtons);
                CurrentMacroName = "";
            }
        }

        private static void Apply(MacroCommand c,
                                  CancellationToken token,
                                  HashSet<string> heldKeys,
                                  HashSet<SimulationService.MouseButtonKind> heldButtons)
        {
            switch (c.Op)
            {
                case MacroOp.KeyDown:
                    if (SimulationService.TryGetKeyCode(c.KeyName, out int downVk))
                    {
                        SimulationService.KeyDown(downVk);
                        heldKeys.Add(c.KeyName);
                    }
                    break;

                case MacroOp.KeyUp:
                    if (SimulationService.TryGetKeyCode(c.KeyName, out int upVk))
                    {
                        SimulationService.KeyUp(upVk);
                        heldKeys.Remove(c.KeyName);
                    }
                    break;

                case MacroOp.KeyPress:
                    if (SimulationService.TryGetKeyCode(c.KeyName, out int pressVk))
                        SimulationService.KeyPress(pressVk, c.HoldMs);
                    break;

                case MacroOp.Delay:
                    Sleep(c.Arg1, token);
                    break;

                case MacroOp.MouseDown:
                    SimulationService.MouseDown(c.Button);
                    heldButtons.Add(c.Button);
                    break;

                case MacroOp.MouseUp:
                    SimulationService.MouseUp(c.Button);
                    heldButtons.Remove(c.Button);
                    break;

                case MacroOp.MouseClick:
                    SimulationService.MouseClick(c.Button);
                    break;

                case MacroOp.Move:
                    SimulationService.MoveRelative(c.Arg1, c.Arg2);
                    break;

                case MacroOp.MoveTo:
                    SimulationService.MoveTo(c.Arg1, c.Arg2);
                    break;

                case MacroOp.Wheel:
                    SimulationService.MouseScroll(c.Arg1);
                    break;

                case MacroOp.Text:
                    // ★★★ 走**剪贴板粘贴**，不再逐键模拟打字（用户要求：「不要敲，直接粘贴」）。
                    //   原因：`TypeText` 用 `VkKeyScan` 翻键码，**中文敲不出来**（返回 -1 直接跳过），
                    //   而且在中文输入法下敲英文会**走候选词** —— 都是按键模拟的固有毛病。
                    //   `PasteText` 写剪贴板再发 Ctrl+V，任何字符（含中文）都能正确输入。
                    SimulationService.PasteText(c.Text);
                    break;
            }
        }

        /// <summary>
        /// 等待 <paramref name="ms"/> 毫秒（**可被取消**：取消/停止时立刻返回）。
        /// <para>
        /// ⛔⛔ 千万别再切成 10ms 小片循环 <c>Thread.Sleep(10)</c> ——
        /// Windows 默认时钟分辨率是 <b>15.625ms</b>，每次 Sleep 都会被向上取整到 15.6ms，
        /// 实测写 <c>Delay 300</c> 会真的等 ~468ms（慢 56%），
        /// 于是「同样的脚本在别的宏软件里正常、在我们这儿节奏全错」。
        /// </para>
        /// <para>
        /// ★ 用 <c>token.WaitHandle.WaitOne(ms)</c> 一次等到底：
        ///   高精度等待不累积误差，而且取消时会被立刻唤醒（不用等到超时）。
        /// </para>
        /// </summary>
        private static void Sleep(int ms, CancellationToken token)
        {
            if (ms <= 0) return;
            if (token.IsCancellationRequested) return;

            token.WaitHandle.WaitOne(ms);
        }

        private static void ReleaseAll(HashSet<string> heldKeys,
                                       HashSet<SimulationService.MouseButtonKind> heldButtons)
        {
            foreach (string key in heldKeys)
            {
                if (SimulationService.TryGetKeyCode(key, out int vk))
                    SimulationService.KeyUp(vk);
            }
            heldKeys.Clear();

            foreach (var button in heldButtons)
                SimulationService.MouseUp(button);
            heldButtons.Clear();
        }
    }
}
