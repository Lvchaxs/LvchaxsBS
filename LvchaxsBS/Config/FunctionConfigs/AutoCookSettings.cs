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
        /// 自动清药：格子区/拆除区域的"粗匹配"缩放档位（1-5，默认 3）。
        /// 这两处是两段式匹配：先把图缩小到 1/N 做粗匹配找到候选位置，再回原图精匹配。
        /// 1 = 不缩放（等于关闭粗匹配，原图直跑，最准最慢）；
        /// 数值越大越快，但缩放后模板越糊、位置误差越大，太大可能直接匹配不到。
        /// </summary>
        public int MedicineScaleFactor { get; set; } = 3;

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