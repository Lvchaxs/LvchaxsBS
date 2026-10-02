using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Config.FunctionConfigs
{
    [ConfigFile("FunctionConfigs/AutoCookSettings.json")]
    public class AutoCookSettings
    {
        /// <summary>
        /// 烹饪检测间隔（毫秒）
        /// </summary>
        public int AutoCookInterval { get; set; } = 100;

        /// <summary>
        /// 烹饪检测匹配度阈值（0-1）
        /// </summary>
        public double DetectThreshold { get; set; } = 0.9;

        /// <summary>
        /// 一般品质判定线位置
        /// </summary>
        public int NormalJudgePercent { get; set; } = 10;

        /// <summary>
        /// 完美品质判定线位置
        /// </summary>
        public int PerfectJudgePercent { get; set; } = 10;

        /// <summary>
        /// 一般品质二值化容差
        /// </summary>
        public int NormalBinarizeTolerance { get; set; } = 10;

        /// <summary>
        /// 完美品质二值化容差
        /// </summary>
        public int PerfectBinarizeTolerance { get; set; } = 0;

        /// <summary>
        /// 自动清药：三个下拉框的选择，格式 "药品类型,星级,具体物品"
        /// 例："复活药,一星料理,提瓦特煎蛋"
        /// </summary>
        public string AutoClearMedicineSelection { get; set; } = "复活药,一星料理,未选择";

        /// <summary>
        /// 自动清药：检测间隔（毫秒）
        /// </summary>
        public int MedicineInterval_1 { get; set; } = 50;

        /// <summary>
        /// 自动清药：检测匹配度阈值（0-1）
        /// </summary>
        public double MedicineDetectThreshold { get; set; } = 0.9;

        /// <summary>
        /// 自动清药：本次运行累计储存上限（达到后结束）
        /// </summary>
        public int MedicineTotalLimit { get; set; } = 2000;

        /// <summary>
        /// 自动清药：是否保存匹配截图日志
        /// </summary>
        public bool MedicineSaveScreenshotLog { get; set; } = false;
    }
}