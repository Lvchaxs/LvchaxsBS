using System;
using System.Collections.Generic;
using LvchaxsBS_UID.Models;

namespace LvchaxsBS_UID.Services.DataServices
{
    /// <summary>
    /// 账号数据的「预热缓存」：把首屏最耗时的那一步（读盘 + 逐条解密）提前做掉。
    ///
    /// <para>
    /// 背景：账号多的时候 <see cref="AccountDataService.LoadAccounts"/> 要好几秒，
    /// 而它原本是在 <c>AccountManagerView.Loaded</c> 里同步跑的 ——
    /// 那几秒里 UI 线程被占满，界面和鼠标一起冻住。
    /// 现在宿主会在打开 UID 界面<b>之前</b>，先在后台线程调 <see cref="Run"/>，
    /// 界面随后创建时直接用这里的结果（<see cref="TryTake"/>），于是那几秒发生在
    /// 加载覆盖层转圈的时候，用户看得到进度、鼠标也不卡。
    /// </para>
    /// <para>
    /// 缓存是<b>一次性</b>的：取走即清空，避免界面拿到过期数据
    /// （之后所有的增删改仍然照旧走 AccountDataService / GroupDataService 读盘）。
    /// 没预热、预热失败、或线程竞争没抢到，都自动退回原来的加载路径。
    /// </para>
    /// </summary>
    public static class DataPreload
    {
        private static readonly object Gate = new object();

        private static List<Account>? _accounts;
        private static List<AccountGroup>? _groups;
        private static bool _ready;

        /// <summary>
        /// 读一遍账号与分组数据放进缓存。给<b>后台线程</b>调用；重复调用只有第一次生效。
        /// 失败一律吞掉 —— 界面会自己再读一次，只是没有预热快。
        /// </summary>
        public static void Run()
        {
            lock (Gate)
            {
                if (_ready) return;
            }

            try
            {
                // 这两步只碰文件与内存，不碰任何 UI 元素，可以安全地在后台线程跑
                var accounts = AccountDataService.LoadAccounts();
                var groups = GroupDataService.LoadGroups();

                lock (Gate)
                {
                    if (_ready) return;
                    _accounts = accounts;
                    _groups = groups;
                    _ready = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UID] 数据预热失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 取走预热好的数据（一次性）。没有预热过时返回 false，调用方照旧自己读。
        /// </summary>
        public static bool TryTake(out List<Account>? accounts, out List<AccountGroup>? groups)
        {
            lock (Gate)
            {
                if (!_ready)
                {
                    accounts = null;
                    groups = null;
                    return false;
                }

                accounts = _accounts;
                groups = _groups;

                // 取走即清空：这份数据只服务首屏，之后一律由界面自己的流程负责
                _accounts = null;
                _groups = null;
                _ready = false;
                return true;
            }
        }
    }
}
