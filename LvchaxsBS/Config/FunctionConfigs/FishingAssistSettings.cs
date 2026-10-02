using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Config.FunctionConfigs
{
    [ConfigFile("FunctionConfigs/FishingAssistSettings.json")]
    public class FishingAssistSettings
    {
        /// <summary>
        /// 鱼竿状态检测间隔（毫秒）
        /// </summary>
        public int FishingAssistInterval { get; set; } = 100;

        /// <summary>
        /// 鱼竿状态检测匹配度阈值（0-1）
        /// </summary>
        public double DetectThreshold { get; set; } = 0.9;

        /// <summary>
        /// 张力区检测间隔（毫秒）
        /// </summary>
        public int TensionInterval { get; set; } = 1;

        /// <summary>
        /// 张力区二值化容差
        /// </summary>
        public int TensionTolerance { get; set; } = 10;

        /// <summary>
        /// 判定线位置（百分比 0-100）
        /// </summary>
        public int JudgmentLinePosition { get; set; } = 40;
    }
}