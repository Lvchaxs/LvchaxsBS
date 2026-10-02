using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Config.FunctionConfigs
{
    [ConfigFile("FunctionConfigs/StoryDialogueSettings.json")]
    public class StoryDialogueSettings
    {
        /// <summary>
        /// 剧情检测间隔（毫秒）
        /// </summary>
        public int StoryDialogueInterval { get; set; } = 500;

        /// <summary>
        /// 剧情检测匹配度阈值（0-1）
        /// </summary>
        public double DetectThreshold { get; set; } = 0.9;

        /// <summary>
        /// 每次按下F键的循环间隔（毫秒）
        /// </summary>
        public int FKeyInterval { get; set; } = 100;
    }
}