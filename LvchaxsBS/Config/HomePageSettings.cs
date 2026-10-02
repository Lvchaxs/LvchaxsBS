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
    }
}