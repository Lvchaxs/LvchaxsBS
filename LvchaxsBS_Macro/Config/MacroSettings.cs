using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Config
{
    /// <summary>
    /// 「自定义宏」模块自己的配置（落在 <c>Data\Macro\macro.json</c>，和宿主的 HomePageSettings 分开）。
    /// <para>★ 用户要求：开关、触发键这些**放在插件专属文件夹**里，不塞进宿主那份配置。</para>
    /// </summary>
    public class MacroSettings
    {
        /// <summary>
        /// 【v2 起废弃】旧版全局胶囊开关 —— 只在迁移时用来给每个宏赋初值，
        /// 之后一律读 <see cref="MacroItem.Enabled"/>。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 【v2 起废弃】旧版全局触发键 —— 只在迁移时把它赋给原来选中的那条宏，
        /// 之后一律读 <see cref="MacroItem.TriggerKey"/>。
        /// </summary>
        public string TriggerKey { get; set; } = "未设置";

        /// <summary>
        /// 宏列表 —— **默认是空的**，只有用户点「添加宏」才会往里加。
        /// <para>⛔ 别再放占位宏（早期放过「宏 1/2/3」，用户看到会以为是内置的固定内容）。</para>
        /// <para>顺序 = 用户拖出来的顺序（置顶的固定在前，其余自由排序）。</para>
        /// </summary>
        public List<MacroItem> Macros { get; set; } = new();

        /// <summary>
        /// 【v2 起下线】文件夹（分组）列表 —— 用户要求去掉文件夹功能，配置页直接是宏列表。
        /// <para>★ 字段保留只为**不删用户数据**（旧配置里的文件夹名不会凭空消失）；代码不再读写它。</para>
        /// </summary>
        public List<MacroFolder> Folders { get; set; } = new();

        /// <summary>【v2 起废弃】旧版首页卡片的文件夹下拉。保留字段，不再读写。</summary>
        public string ActiveFolderId { get; set; } = "";

        /// <summary>
        /// 【v2 起废弃】旧版「首页卡片选中的宏」—— 现在每条宏都有自己的触发键，不再有"当前宏"。
        /// <para>★ 保留字段仅用于迁移（把旧全局触发键赋给它）。</para>
        ///
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

        /// <summary>指令编辑器缩放百分比（默认 100）。范围 50~200。</summary>
        public int EditorZoomPercent { get; set; } = 100;

        /// <summary>
        /// 配置文件结构版本 —— 用来做**一次性迁移**，默认 0 表示"旧文件（还没写过版本号）"。
        /// <para>★ 新配置创建时会被置为 <see cref="CurrentSchemaVersion"/>。</para>
        /// </summary>
        public int SchemaVersion { get; set; }

        /// <summary>
        /// 当前结构版本：改了枚举语义/字段含义就要 +1 并写迁移代码。
        /// <para>★ v2（2026-10-21）：开关与触发键从「全局一份」改成**每个宏各一份**
        ///   （<see cref="MacroItem.Enabled"/> / <see cref="MacroItem.TriggerKey"/>），
        ///   文件夹功能下线（<see cref="Folders"/> 只保留旧数据、不再读写）。</para>
        /// <para>★ v3（2026-10-22）：宏之间允许**共用同一个触发键**，但同一键同时只能有一个启用
        ///   （加载时按此规则归一化一遍）。</para>
        /// </summary>
        public const int CurrentSchemaVersion = 3;
    }
}
