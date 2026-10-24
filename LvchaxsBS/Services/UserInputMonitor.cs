using System;
using System.Diagnostics;
using System.Threading;
using LvchaxsBS.Core;   // SimulationService（IsSimulating：区分模拟按键与用户真实输入）

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 「用户刚刚动过键鼠吗」的监视器。
    ///
    /// <para>
    /// 数据来源就是已有的那两个全局钩子（<see cref="GlobalKeyboardHookService"/> /
    /// <see cref="GlobalMouseHookService"/>）：它们既能拿到用户真实输入，也能拿到模块自己
    /// 用 <c>keybd_event</c> 灌进去的模拟事件 —— 靠 <see cref="SimulationService.IsSimulating"/>
    /// 把两者分开，<b>只把用户真实操作这才算作"用户活动"</b>。
    /// </para>
    ///
    /// <para>
    /// 用途：给 UID 登录插件当"让路"依据 —— 登录后的常驻监控本来会按 ESC / Enter，
    /// 如果用户正在自己操作（翻背包、看地图…），模拟按键会和他打架。所以监控每轮先问一句
    /// <see cref="IdleMs"/>，用户刚动过就先不按键，把这段操作空间留给他。
    /// </para>
    ///
    /// <para>
    /// ★ 鼠标钩子只上报按键 / 滚轮，<b>不含鼠标移动</b> —— 动一下鼠标不会把倒计时一直顶住。
    /// </para>
    /// </summary>
    public static class UserInputMonitor
    {
        /// <summary>最近一次用户真实操作的时刻（<see cref="Environment.TickCount64"/>）。</summary>
        private static long _lastInputTicks;

        private static bool _subscribed;

        /// <summary>宿主是否已开始监听。</summary>
        public static bool IsRunning => _subscribed;

        /// <summary>
        /// 距离上一次用户操作过去了多少毫秒。
        /// <para>★ 从没记到过用户操作时返回 <see cref="int.MaxValue"/>（= 一直很安静），
        /// 这样调用方不必区分"还没操作过"和"很久没操作"。</para>
        /// </summary>
        public static int IdleMs
        {
            get
            {
                long last = Interlocked.Read(ref _lastInputTicks);
                if (last == 0) return int.MaxValue;

                long idle = Environment.TickCount64 - last;
                return idle < 0 ? 0 : (int)Math.Min(idle, int.MaxValue);
            }
        }

        /// <summary>开始监听用户输入（宿主启动引擎时调一次）。</summary>
        public static void Start()
        {
            if (_subscribed) return;

            GlobalKeyboardHookService.KeyboardEvent += OnKeyboardInput;
            GlobalMouseHookService.MouseEvent += OnMouseInput;
            _subscribed = true;

            Debug.WriteLine("[用户输入] 已开始监听键鼠活动（用于给 UID 监控让路）");
        }

        /// <summary>停止监听（宿主退出时调）。</summary>
        public static void Stop()
        {
            if (!_subscribed) return;

            GlobalKeyboardHookService.KeyboardEvent -= OnKeyboardInput;
            GlobalMouseHookService.MouseEvent -= OnMouseInput;
            _subscribed = false;
        }

        private static void OnKeyboardInput(object? sender, KeyboardEventArgs e) => NoteInput(e.IsInjected);

        private static void OnMouseInput(object? sender, MouseEventArgs e) => NoteInput(e.IsInjected);

        /// <summary>
        /// 记一次"用户操作"。
        /// <para>
        /// ★★ 两道过滤，缺一不可：
        /// <list type="number">
        ///   <item><b><paramref name="isInjected"/></b>（<c>LLKHF_/LLMHF_INJECTED</c>）——
        ///         事件本身带"注入"标志 = 程序自己发的模拟按键/点击，不是用户按的。**这才是主判据。**</item>
        ///   <item><see cref="SimulationService.IsSimulating"/> —— 兜底（模拟还没结束时的事件）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// ⛔ <b>不能只靠第 2 条</b>（2026-10-15 踩过）：低级钩子的回调跑在消息循环里，
        /// 模拟事件常常在 <c>EndSimulation()</c> **之后**才被回调处理 —— 那时标志已复位，
        /// 于是"自己发的模拟键"被当成"用户操作"，用户明明没碰键鼠却每隔一会弹一次
        /// 「发现用户进行了操作，等待 10 秒…」。
        /// </para>
        /// </summary>
        private static void NoteInput(bool isInjected)
        {
            if (isInjected) return;                       // 注入（模拟）事件：不算用户操作
            if (SimulationService.IsSimulating) return;   // 双保险（宿主自己灌的键）

            Interlocked.Exchange(ref _lastInputTicks, Environment.TickCount64);
        }
    }
}
