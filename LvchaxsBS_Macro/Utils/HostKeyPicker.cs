using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Controls;

namespace LvchaxsBS_Macro.Utils
{
    /// <summary>
    /// 向宿主借「按键网格页」（宿主 <c>TriggerKeyPage</c>）。
    ///
    /// <para>
    /// ★ 插件是反射加载的独立 dll，**没有宿主的编译期引用**，所以不能直接 new 宿主页面，
    ///   只能反射调 <c>LvchaxsBS.Services.MacroModuleService.CreateKeyPicker(...)</c>。
    /// </para>
    /// <para>
    /// ★ 宿主那边造页面时的配置顺序与「快速切图给地区绑键」完全一致
    ///   （组合键模式 → 禁选 → 组合键黑名单 → 预选当前键 → 占用者小字），
    ///   所以观感和交互跟快速切图一模一样。
    /// </para>
    /// </summary>
    public static class HostKeyPicker
    {
        /// <summary>
        /// 造一个配好的触发键页。
        /// </summary>
        /// <param name="currentKey">当前触发键（"未设置" = 没有）。</param>
        /// <param name="occupied">别的宏已占用的键 → 占用者名字（基础键会被禁选，完整名进组合键黑名单）。</param>
        /// <param name="onSelected">点了新键 = 选好了（调用方负责保存 + 关页面）。</param>
        /// <param name="onAdjusted">只调了组合键（Ctrl/Alt），保存但别关页面。</param>
        /// <param name="onCleared">点了已选中的键 = 取消，清配置但不关页面。</param>
        /// <returns>宿主的按键页；旧宿主 / 调用失败时返回 null。</returns>
        public static Page? Create(
            string currentKey,
            IDictionary<string, string>? occupied,
            Action<string> onSelected,
            Action<string> onAdjusted,
            Action onCleared)
        {
            try
            {
                Type? svc = Type.GetType("LvchaxsBS.Services.MacroModuleService, LvchaxsBS");
                MethodInfo? mi = svc?.GetMethod("CreateKeyPicker",
                    BindingFlags.Public | BindingFlags.Static);
                if (mi == null) return null;

                object? result = mi.Invoke(null, new object?[]
                {
                    currentKey, occupied, onSelected, onAdjusted, onCleared,
                });

                return result as Page;
            }
            catch
            {
                // 旧宿主没有这个方法 / 内部异常 → 调用方按「借不到」处理（自己降级提示）
                return null;
            }
        }
    }
}
