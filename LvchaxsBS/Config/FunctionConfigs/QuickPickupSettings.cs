using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Config.FunctionConfigs
{
    [ConfigFile("FunctionConfigs/QuickPickupSettings.json")]
    public class QuickPickupSettings
    {
        /// <summary>
        /// 每次拾取循环的间隔（毫秒）
        /// </summary>
        public int PickupInterval { get; set; } = 30;

        /// <summary>
        /// (F 和 滚动)之间的延迟（毫秒）
        /// </summary>
        public int FAndScrollDelay { get; set; } = 1;

        /// <summary>
        /// 暂停键（支持多个按键，用逗号分隔）
        /// </summary>
        public string PauseKeys { get; set; } = "L,Esc,F1,F2,F3,F4,F5,F6,F7,F8,F9,F10,F11,F12,B,C,G,U,O,Enter";
    }
}