using System.Collections.Generic;
using LvchaxsBS.Config;

namespace LvchaxsBS.Config
{
    [ConfigFile("HomePageSettings.json")]
    public class HomePageSettings
    {
        // 功能总开关
        public bool MasterSwitch { get; set; } = true;

        // 快速传送
        public bool QuickTeleport { get; set; } = true;
        public string QuickTeleportKey { get; set; } = "左键";
        // 快速拾取
        public bool QuickPickup { get; set; } = true;
        public string QuickPickupKey { get; set; } = "`~";
        // 剧情对话
        public bool StoryDialogue { get; set; } = true;
        public string StoryDialogueKey { get; set; } = "←";
        // 自动烹饪
        public bool AutoCook { get; set; } = true;
        public string AutoCookKey { get; set; } = "↑";
        // 钓鱼辅助
        public bool FishingAssist { get; set; } = true;
        public string FishingAssistKey { get; set; } = "→";
        // 自动伐木
        public bool AutoLumber { get; set; } = true;
        public string AutoLumberKey { get; set; } = "↓";
        // 手柄拾取
        public bool ControllerPickup { get; set; } = false;
        public string ControllerPickupKey { get; set; } = "B";

        /// <summary>
        /// 功能卡片（以及顶部那一排功能图标）的显示顺序，存模块 Id（如 "QuickTeleport"）。
        /// <para>★ 列表里没有的 Id = 后来新增的模块，按 <see cref="ModuleRegistry.All"/> 的默认顺序补在后面；
        /// 多出来的（已删除的模块）忽略。⇒ 新增模块不必改这里。</para>
        /// <para>★ 只有排序、没有置顶/置底（那是 UID 账号列表的逻辑，这里用不上）。</para>
        /// </summary>
        public List<string> ModuleOrder { get; set; } = new();
    }
}