namespace LvchaxsBS.Core
{
    public static class SubtitleTexts
    {
        public static class Fishing
        {
            public const string ReadyToCast = "钓鱼已就位，就差你这一甩了！";
            public const string WaitingBite = "鱼儿鱼儿快上钩，我已经等不及啦！";
            public const string Hooked = "来啦来啦！这条大鱼就交给我吧！";
            public const string PullRight = "它想往右边溜，快收线把它拉过来！";
            public const string PullLeft = "它想往左边溜，快松线泄掉冲击力！";
        }

        public static class AutoCook
        {
            public const string QualityStrange = "奇怪";
            public const string QualityNormal = "一般";
            public const string QualityPerfect = "完美";
            public const string QualityAutoX99 = "自动x99";
            public const string QualityUnknown = "未知";

            public const string SelectOptionsWithAuto = "F1/奇怪 F2/一般 F3/完美 F4/自动x99";
            public const string SelectOptionsManual = "F1/奇怪 F2/一般 F3/完美";
            public const string SelectCountdownSuffix = "秒内选择";

            public const string RunStatusFormat = "正在执行 {0} 成功: {1}次 超时:{2}/{3}";

            public const string Preparing = "烹饪已就绪，正在获取选择类型。";

            public const string ClearMedicineStorageFormat = "确认是完美品质：已储存{0}/{1}";
            public const string ClearMedicineClosingFormat = "确认是完美品质：已储存{0}/{1}-确认储存并关闭寄物装置...";
            public const string ClearMedicineWaitingMain = "等待回到主界面...";
            public const string ClearMedicineDestroyStorage = "主界面中-开始摧毁寄物装置...";
            public const string ClearMedicineFuelInsufficient = "主界面中-克什尼克燃料\"不足\"...";
            public const string ClearMedicineRecreateStorage = "主界面中-重新创造寄物装置并打开...";
            public const string ClearMedicineOpeningStorage = "正在打开寄物装置...";
            public const string ClearMedicineClearingCapacity = "正在清空寄物装置容量...";
        }

        public static class AutoLumber
        {
            public const string RunStatusFormat = "自动伐木 成功:{0} 超时:{1}/{2} (3/3自动结束 超时+1 成功-1)";
        }
    }
}