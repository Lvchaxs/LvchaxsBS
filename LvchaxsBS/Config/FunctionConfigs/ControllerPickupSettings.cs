using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Config.FunctionConfigs
{
    [ConfigFile("FunctionConfigs/ControllerPickupSettings.json")]
    public class ControllerPickupSettings
    {
        /// <summary>
        /// 触发延迟（毫秒）
        /// </summary>
        public int TriggerDelay { get; set; } = 50;
    }
}