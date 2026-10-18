using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Config
{
    /// <summary>
    /// 「自定义宏」模块自己的配置（落在 <c>Config\Macro\macro.json</c>，和宿主的 HomePageSettings 分开）。
    /// <para>★ 用户要求：开关、触发键这些**放在插件专属文件夹**里，不塞进宿主那份配置。</para>
    /// </summary>
    public class MacroSettings
    {
        /// <summary>首页卡片上的胶囊开关。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>触发键（"未设置" = 还没设置过）。</summary>
        public string TriggerKey { get; set; } = "未设置";

        /// <summary>
        /// 宏列表 —— **默认是空的**，只有用户点「添加宏」才会往里加。
        /// <para>⛔ 别再放占位宏（早期放过「宏 1/2/3」，用户看到会以为是内置的固定内容）。</para>
        /// <para>顺序 = 用户拖出来的顺序（置顶的固定在前，其余自由排序）。</para>
        /// </summary>
        public List<MacroItem> Macros { get; set; } = new();

        /// <summary>
        /// 首页卡片「宏名称」下拉选中的那条 —— **触发键按下时执行的就是它**。
        /// <para>
        /// ★ 卡片每次进首页都重建，所以选中项必须落盘，不能只留在控件里。
        /// ★★ 只看 <see cref="ActiveMacroId"/>：按 <c>Id</c> 认人，
        ///    **新建宏 / 拖动排序 / 删除别的宏都不会动它**（名字只作旧配置兜底）。
        /// </para>
        /// </summary>
        public string ActiveMacroId { get; set; } = "";

        /// <summary>
        /// 【旧字段，只用于兼容】早期版本按**名字**记录选中项。
        /// <para>★ 新代码一律读写 <see cref="ActiveMacroId"/>；只有 Id 找不到时才拿它兜底一次。</para>
        /// </summary>
        public string ActiveMacroName { get; set; } = "";

        /// <summary>
        /// 生效范围下标（对应 <see cref="MacroScope"/> 的枚举值）：0=全局生效 / 1=客户区内生效。
        /// <para>★ 客户区内生效时，只有游戏窗口在前台才响应触发键；运行中失去焦点会自动停止。</para>
        /// </summary>
        public int ScopeIndex { get; set; } = (int)MacroScope.ClientOnly;

        /// <summary>生效范围（枚举形式）。★ 只读，不参与序列化。</summary>
        [JsonIgnore]
        public MacroScope Scope => (MacroScope)Math.Clamp(ScopeIndex, 0, MacroScopeNames.All.Length - 1);

        /// <summary>
        /// 配置文件结构版本 —— 用来做**一次性迁移**，默认 0 表示"旧文件（还没写过版本号）"。
        /// <para>★ 新配置创建时会被置为 <see cref="CurrentSchemaVersion"/>。</para>
        /// </summary>
        public int SchemaVersion { get; set; }

        /// <summary>当前结构版本：改了枚举语义/字段含义就要 +1 并写迁移代码。</summary>
        public const int CurrentSchemaVersion = 1;
    }
}
