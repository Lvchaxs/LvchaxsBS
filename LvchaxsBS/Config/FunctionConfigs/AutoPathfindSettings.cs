using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Config.FunctionConfigs
{
    [ConfigFile("FunctionConfigs/AutoPathfindSettings.json")]
    public class AutoPathfindSettings
    {
        /// <summary>
        /// 自动寻路检测间隔（毫秒）
        /// </summary>
        public int PollIntervalMs { get; set; } = 20;
    }
}