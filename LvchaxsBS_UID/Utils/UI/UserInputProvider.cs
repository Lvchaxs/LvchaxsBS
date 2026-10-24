using System;

namespace LvchaxsBS_UID.Utils.UI
{
    /// <summary>
    /// 「用户有多久没动过键鼠了」的查询入口：宿主通过 <c>UidPlugin.SetHostUserInputProvider</c> 注入
    /// （宿主侧是 <c>UserInputMonitor.IdleMs</c> —— 它订阅键鼠钩子，**只记用户真实操作**，
    /// 模块自己灌的模拟按键不算）。
    ///
    /// <para>
    /// 用途：登录后的常驻人数监控会按 ESC / Enter 自动回到聊天界面。用户如果正在自己操作
    /// （翻背包、看地图、打字…），这些模拟按键会和他打架 —— 所以监控每轮先问一句
    /// <see cref="IdleMs"/>，用户刚动过就先只检测、不按键，把这段操作空间让给他。
    /// </para>
    /// <para>
    /// 宿主没注入时返回 <see cref="int.MaxValue"/>（= 一直很安静）：降级行为和别的注入能力一致，
    /// 宁可按老样子跑，也不因为拿不到数据就不按键。
    /// </para>
    /// </summary>
    public static class UserInputProvider
    {
        private static Func<int>? _provider;

        /// <summary>宿主是否注入了用户输入空闲时间。</summary>
        public static bool IsAvailable => _provider != null;

        /// <summary>
        /// 距离上一次用户操作过去了多少毫秒；没注入（或从没操作过）时是 <see cref="int.MaxValue"/>。
        /// </summary>
        public static int IdleMs
        {
            get
            {
                try
                {
                    return _provider?.Invoke() ?? int.MaxValue;
                }
                catch
                {
                    // 查询失败按"很安静"处理：宁可按老样子按键，也不因为一次取值失败就停摆
                    return int.MaxValue;
                }
            }
        }

        /// <summary>注入查询委托（宿主启动探测到插件后调用一次）。</summary>
        public static void SetProvider(Func<int> provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }
    }
}
