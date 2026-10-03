using System;

namespace LvchaxsBS.Config
{
    /// <summary>
    /// 配置页处理器助手：一行完成"读缓存 → 改属性 → 保存"。
    /// 保存走 ConfigManager 的防抖落盘，连续触发（如拖滑条）只会写一次磁盘。
    /// </summary>
    public static class ConfigSync
    {
        /// <summary>
        /// 示例：
        ///   ConfigSync.Mutate&lt;AutoCookSettings&gt;(s => s.AutoCookInterval = (int)e.NewValue);
        /// </summary>
        /// <returns>修改后的配置实例（需要继续读取时使用）</returns>
        public static T Mutate<T>(Action<T> mutate) where T : class, new()
        {
            var s = ConfigManager.Get<T>();
            mutate(s);
            ConfigManager.Save(s);
            return s;
        }
    }
}
