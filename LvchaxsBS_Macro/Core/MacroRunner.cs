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
        /// 启动一条宏。
        /// </summary>
        /// <param name="loop">true = 整段跑完立刻再来一遍（按住/开关型）；false = 只跑一遍（松开即止）。</param>
        public static void Start(MacroItem? macro, bool loop)
        {
            if (macro == null) return;

            var cmds = MacroScript.Parse(macro.Script);
            if (cmds.Count == 0) return;

            Stop();                       // 上一轮还在跑就先让它收尾

            // ★ 等上一轮**真的退出**（它要在 finally 里把按住的键抬起来）再开新一轮 ——
            //   否则连按两次时，旧线程会在新脚本跑到一半时把键抬掉，动作就乱了。
            //   旧线程最长再睡 10ms 就醒（Delay 是按 10ms 一片切的），这里给 60ms 上限兜底。
            _exited?.Wait(60);

            var cts = new CancellationTokenSource();
            _cts = cts;
            CurrentMacroName = macro.Name ?? "";

            var exited = new ManualResetEventSlim(false);
            _exited = exited;

            CancellationToken token = cts.Token;
            Task.Run(() =>
            {
                try { RunLoop(cmds, loop, token); }
                finally { exited.Set(); }
            });
        }

        /// <summary>停止执行（线程会自己抬起按键然后退出，本方法不阻塞）。</summary>
        public static void Stop()
        {
            var cts = _cts;
            if (cts == null) return;

            _cts = null;
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
                    SimulationService.TypeText(c.Text);
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
