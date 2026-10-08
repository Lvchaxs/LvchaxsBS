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
        public int RightListClickItemDelay_1 { get; set; } = 100;

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
    }
}