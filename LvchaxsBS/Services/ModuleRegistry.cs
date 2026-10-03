using System;
using System.Linq;
using LvchaxsBS.Config;
using LvchaxsBS.UI.Pages;
using LvchaxsBS.UI.Pages.FunctionConfigs;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 单个功能模块的元数据：名称、描述、图标、标签、
    /// 触发键读写、开关读写、配置页工厂。
    /// 新增功能模块只需要在 ModuleRegistry.All 里加一条记录。
    /// </summary>
    public class ModuleInfo
    {
        /// <summary>模块 Id（HomePage 开关 Tag，如 "QuickTeleport"）</summary>
        public string Id { get; init; } = "";

        /// <summary>显示名（如 "快速传送"）</summary>
        public string Name { get; init; } = "";

        public string Description { get; init; } = "";

        /// <summary>模块图标 pack URI</summary>
        public string IconPath { get; init; } = "";

        /// <summary>触发键配置标签（如 "QuickTeleportKey"）</summary>
        public string KeyTag { get; init; } = "";

        /// <summary>是否使用手柄按键页（GamepadTriggerKeyPage）</summary>
        public bool UseGamepad { get; init; } = false;

        /// <summary>配置页工厂</summary>
        public Func<System.Windows.Controls.Page> CreateConfigPage { get; init; }
            = () => new System.Windows.Controls.Page();

        /// <summary>分辨率标签工厂（null = 不显示标签）</summary>
        public Func<TagInfo[]?>? CreateTags { get; init; }

        /// <summary>读开关状态（HomePageSettings）</summary>
        public Func<HomePageSettings, bool> IsEnabled { get; init; } = _ => false;

        /// <summary>写开关状态（HomePageSettings）</summary>
        public Action<HomePageSettings, bool> SetEnabled { get; init; } = (_, _) => { };

        /// <summary>读触发键（HomePageSettings）</summary>
        public Func<HomePageSettings, string> GetKey { get; init; } = _ => "未设置";

        /// <summary>写触发键（HomePageSettings）</summary>
        public Action<HomePageSettings, string> SetKey { get; init; } = (_, _) => { };
    }

    /// <summary>
    /// 全部功能模块的唯一注册处。
    /// 之前"模块 → 开关 / 触发键 / 配置页"的映射散落在
    /// HomePage、ConfigContainerPage 共 6 个 switch 里，现在统一查表。
    /// </summary>
    public static class ModuleRegistry
    {
        public static readonly ModuleInfo[] All =
        {
            new ModuleInfo
            {
                Id = "QuickTeleport", Name = "快速传送",
                Description = "地图界面中选择锚点后按下触发键识别并自动执行点击F传送操作",
                IconPath = "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/快速传送.png",
                KeyTag = "QuickTeleportKey", CreateTags = CommonTags,
                CreateConfigPage = () => new QuickTeleportConfig(),
                IsEnabled = s => s.QuickTeleport, SetEnabled = (s, v) => s.QuickTeleport = v,
                GetKey = s => s.QuickTeleportKey, SetKey = (s, k) => s.QuickTeleportKey = k,
            },
            new ModuleInfo
            {
                Id = "QuickPickup", Name = "快速拾取",
                Description = "主界面中按下触发键开启/关闭 F滚轮快速循环，非主界面自动暂停",
                IconPath = "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/快速拾取.png",
                KeyTag = "QuickPickupKey", CreateTags = CommonTags,
                CreateConfigPage = () => new QuickPickupConfig(),
                IsEnabled = s => s.QuickPickup, SetEnabled = (s, v) => s.QuickPickup = v,
                GetKey = s => s.QuickPickupKey, SetKey = (s, k) => s.QuickPickupKey = k,
            },
            new ModuleInfo
            {
                Id = "StoryDialogue", Name = "剧情对话",
                Description = "对话界面中按下触发键开启/关闭 识别到左上角对话隐藏图标时自动F对话",
                IconPath = "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/剧情对话.png",
                KeyTag = "StoryDialogueKey", CreateTags = CommonTags,
                CreateConfigPage = () => new StoryDialogueConfig(),
                IsEnabled = s => s.StoryDialogue, SetEnabled = (s, v) => s.StoryDialogue = v,
                GetKey = s => s.StoryDialogueKey, SetKey = (s, k) => s.StoryDialogueKey = k,
            },
            new ModuleInfo
            {
                Id = "AutoCook", Name = "自动烹饪",
                Description = "自动烹饪料理+自动清理完美品质的复活药体力药",
                IconPath = "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/自动烹饪.png",
                KeyTag = "AutoCookKey", CreateTags = CommonTags,
                CreateConfigPage = () => new AutoCookConfig(),
                IsEnabled = s => s.AutoCook, SetEnabled = (s, v) => s.AutoCook = v,
                GetKey = s => s.AutoCookKey, SetKey = (s, k) => s.AutoCookKey = k,
            },
            new ModuleInfo
            {
                Id = "FishingAssist", Name = "钓鱼辅助",
                Description = "钓鱼界面中按下触发键开启/关闭 只需手动抛竿，识别到上钩后自动收杆并控制滑块",
                IconPath = "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/钓鱼辅助.png",
                KeyTag = "FishingAssistKey", CreateTags = CommonTags,
                CreateConfigPage = () => new FishingAssistConfig(),
                IsEnabled = s => s.FishingAssist, SetEnabled = (s, v) => s.FishingAssist = v,
                GetKey = s => s.FishingAssistKey, SetKey = (s, k) => s.FishingAssistKey = k,
            },
            new ModuleInfo
            {
                Id = "AutoLumber", Name = "自动伐木",
                Description = "主界面并装备王树瑞佑后按下触发键开启/关闭 自动识别循环使用小道具",
                IconPath = "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/自动伐木.png",
                KeyTag = "AutoLumberKey", CreateTags = CommonTags,
                CreateConfigPage = () => new AutoLumberConfig(),
                IsEnabled = s => s.AutoLumber, SetEnabled = (s, v) => s.AutoLumber = v,
                GetKey = s => s.AutoLumberKey, SetKey = (s, k) => s.AutoLumberKey = k,
            },
            new ModuleInfo
            {
                Id = "ControllerPickup", Name = "手柄拾取",
                Description = "按下触发键执行一次鼠标左键一键拾取，需要关闭游戏内-主界面控制设备自动切换",
                IconPath = "pack://application:,,,/LvchaxsBS;component/UI/Assets/Images/手柄拾取.png",
                KeyTag = "ControllerPickupKey", UseGamepad = true,
                CreateConfigPage = () => new ControllerPickupConfig(),
                IsEnabled = s => s.ControllerPickup, SetEnabled = (s, v) => s.ControllerPickup = v,
                GetKey = s => s.ControllerPickupKey, SetKey = (s, k) => s.ControllerPickupKey = k,
            },
        };

        /// <summary>按模块 Id 查找（HomePage 开关 Tag）</summary>
        public static ModuleInfo? ById(string id)
            => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>按显示名查找（ConfigContainerPage 的 CurrentModuleName）</summary>
        public static ModuleInfo? ByName(string name)
            => All.FirstOrDefault(m => m.Name == name);

        /// <summary>按触发键配置标签查找（SetTriggerKeyPage 的 keyConfigTag）</summary>
        public static ModuleInfo? ByKeyTag(string keyTag)
            => All.FirstOrDefault(m => m.KeyTag == keyTag);

        /// <summary>通用分辨率标签</summary>
        public static TagInfo[] CommonTags() => new[]
        {
            new TagInfo { Text = "16:9", BgKey = "TagBlueBgBrush", FgKey = "TagBlueFgBrush" },
            new TagInfo { Text = "5120x2160", BgKey = "TagPurpleBgBrush", FgKey = "TagPurpleFgBrush" },
            new TagInfo { Text = "3440x1440", BgKey = "TagGreenBgBrush", FgKey = "TagGreenFgBrush" },
            new TagInfo { Text = "2560x1080", BgKey = "TagRedBgBrush", FgKey = "TagRedFgBrush" },
        };
    }
}
