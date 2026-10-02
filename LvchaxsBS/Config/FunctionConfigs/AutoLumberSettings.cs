using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Config.FunctionConfigs
{
    [ConfigFile("FunctionConfigs/AutoLumberSettings.json")]
    public class AutoLumberSettings
    {
        /// <summary>
        /// 伐木检测间隔（毫秒）
        /// </summary>
        public int AutoLumberInterval { get; set; } = 100;

        /// <summary>
        /// 伐木检测匹配度阈值（0-1）
        /// </summary>
        public double DetectThreshold { get; set; } = 0.9;
    }
}