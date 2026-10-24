using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Config.FunctionConfigs
{
    [ConfigFile("FunctionConfigs/QuickTeleportSettings.json")]
    public class QuickTeleportSettings
    {
        /// <summary>
        /// 右下角检测延迟（毫秒）
        /// </summary>
        public int RightCornerDetectDelay_1 { get; set; } = 15;

        /// <summary>
        /// 右下角匹配阈值（0-1）
        /// </summary>
        public double DetectThreshold { get; set; } = 0.9;

        /// <summary>
        /// 右列表检测延迟（毫秒）
        /// </summary>
        public int RightListDetectDelay_1 { get; set; } = 300;

        /// <summary>
        /// 右列表匹配阈值（0-1）
        /// </summary>
        public double RightListThreshold { get; set; } = 0.9;

        /// <summary>
        /// 右列表"粗匹配"缩放档位（1-5，默认 3）。
        /// 右列表是两段式匹配：先把图缩小到 1/N 做粗匹配找到候选位置，再回原图精匹配。
        /// 1 = 不缩放（等于关闭粗匹配，原图直跑，最准最慢）；
        /// 数值越大越快，但缩放后模板越糊、位置误差越大，太大可能直接匹配不到。
        /// </summary>
        public int RightListScaleFactor { get; set; } = 3;

        /// <summary>
        /// 右列表点击项延迟（毫秒）
        /// </summary>
        public int RightListClickItemDelay_2 { get; set; } = 200;

        /// <summary>
        /// 右列表秘境F键延迟（毫秒）
        /// </summary>
        public int RightListAbyssFKeyDelay { get; set; } = 150;

        /// <summary>
        /// 右列表F键延迟（毫秒）
        /// </summary>
        public int RightListFKeyDelay { get; set; } = 50;

        /// <summary>
        /// 右侧列表识别：勾选后执行传送时也对右侧列表进行识别
        /// （包含 神像 / 秘境 / 宅邸 / 列车 / 临时锚点 等）。
        /// 取消勾选则只循环检测右下角，不跑右侧列表。
        /// </summary>
        public bool EnableListRecognition { get; set; } = true;

        /// <summary>
        /// 禁用快速切图
        /// </summary>
        public bool DisableQuickScreenshot { get; set; } = false;

        /// <summary>
        /// 深渊剧诗过滤
        /// </summary>
        public bool DisableAbyssFilter { get; set; } = true;

        /// <summary>
        /// 右键取消传送：勾选后，处于地图界面且触发键为左键时，
        /// 按下鼠标右键可临时禁用本次传送（图标 锚点2 → 锚点3），
        /// 直到检测到主界面自动清除，或再次按下右键解除。
        /// </summary>
        public bool EnableRightClickCancel { get; set; } = true;

        /// <summary>
        /// 开图键：快速传送模块专用的地图打开按键（多选，逗号分隔）
        /// </summary>
        public string OpenMapKey_1 { get; set; } = "M";

        // ============ 快速切图（地图界面内按地区触发键直接切地区）============

        /// <summary>
        /// 快速切图总开关。关掉后地区触发键完全不响应（触发键配置保留）。
        /// </summary>
        public bool EnableQuickMapSwitch { get; set; } = true;

        /// <summary>
        /// 切图识别开关（配置页「启用切图识别」复选框，放在总开关下面）：
        /// <para>勾选（默认）= 按地区触发键后先做 RGB 画面识别（地图探测点 / 导航条 / 地区列表），
        ///   确认在地图界面才执行，不在图里或画面不认识就静默放弃。</para>
        /// <para>不勾选 = **完全不识别画面**，按下地区键直接按「点导航条 → 等延迟 → 点地区」执行
        ///   （用户自己负责在地图界面按，按错了会点到地图上）。</para>
        /// </summary>
        public bool EnableCutRecognition { get; set; } = true;

        /// <summary>
        /// 各地区的触发键：地区 ID（Mondstadt / Liyue …）→ 键名（"F1" / "M" …）。
        /// 没有这一项 = 未设置，该地区不响应。
        /// <para>
        /// ★ 默认值 = 用户实机调好的一整套（1/2/3/4 + QWEASDZXCRFV），
        ///   新装 / 重置配置后直接就能用，不用再一个个绑。
        /// </para>
        /// </summary>
        public Dictionary<string, string> QuickMapKeys { get; set; } = new()
        {
            // 提瓦特
            ["Mondstadt"] = "1",
            ["Liyue"] = "2",
            ["Inazuma"] = "Q",
            ["Sumeru"] = "W",
            ["Fontaine"] = "A",
            ["Natlan"] = "S",
            ["NodKrai"] = "Z",
            ["Snezhnaya"] = "X",
            // 地下 / 其他
            ["Enkanomiya"] = "3",
            ["Chasm"] = "4",
            ["SeaOfBygoneEras"] = "E",
            ["AncientSacredMountain"] = "R",
            ["TempleOfTheVoid"] = "D",
            ["Frostmoon"] = "F",
            ["SereniteaPot"] = "C",
            ["StellarRealm"] = "V",
        };

        /// <summary>
        /// 快速切图：点地区之前的等待（毫秒）。
        /// 地区列表是展开动画，太短会点在没展开完的列表上。
        /// <para>导航条本身是直接点的，没有"点导航条之前的延迟"。</para>
        /// </summary>
        public int QuickMapRegionDelay { get; set; } = 100;
    }
}