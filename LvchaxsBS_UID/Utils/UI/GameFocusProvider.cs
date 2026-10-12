using System;

namespace LvchaxsBS_UID.Utils.UI
{
    /// <summary>
    /// 游戏窗口焦点查询：宿主通过 <c>UidPlugin.SetHostFocusProvider</c> 注入
    /// （宿主侧是 <c>WindowFocusService.Reevaluate</c> —— 同步用真实前台窗口复核一次）。
    ///
    /// <para>
    /// 登录流程靠它实现「游戏不在焦点就暂停」：切到别的程序时识别和按键全部停住，
    /// 焦点回游戏再接着跑，避免把点击 / 按键送到别人的窗口里。
    /// </para>
    /// <para>
    /// 宿主没注入时永远返回 true（= 不暂停）：降级行为和别的注入能力一致，
    /// 宁可按老样子跑，也不因为拿不到焦点信息就卡住。
    /// </para>
    /// </summary>
    public static class GameFocusProvider
    {
        private static Func<bool>? _provider;

        /// <summary>宿主是否注入了焦点查询能力。</summary>
        public static bool IsAvailable => _provider != null;

        /// <summary>
        /// 游戏窗口此刻是否是焦点窗口。
        /// 没注入过返回 true（调用方据此不做暂停处理）。
        /// </summary>
        public static bool IsGameFocused()
        {
            try
            {
                return _provider == null || _provider();
            }
            catch
            {
                // 查询失败时按"仍在焦点"处理：误暂停只是白等一会儿，
                // 误不停则会把按键发给别人 —— 选前者更安全。
                return true;
            }
        }

        /// <summary>注入焦点查询委托（宿主启动探测到插件后调用一次）。</summary>
        public static void SetProvider(Func<bool> provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }
    }
}
