namespace LvchaxsBS.Config
{
    [ConfigFile("AppSettings.json")]
    public class AppSettings
    {
        // 主题（"Light" / "Dark"）
        public string Theme { get; set; } = "Light";

        // 截图方式：0 = BitBlt(HWND) 窗口，1 = BitBlt(NULL) 屏幕
        public int CaptureMode { get; set; } = 0;

        /// <summary>
        /// 游戏主界面检测间隔（毫秒），默认 50ms
        /// </summary>
        public int MainWindowCheckInterval_1 { get; set; } = 50;

        /// <summary>
        /// 官服路径（YuanShen.exe）
        /// </summary>
        public string OfficialServerPath { get; set; } = string.Empty;

        /// <summary>
        /// 国际服路径（GenshinImpact.exe）
        /// </summary>
        public string InternationalServerPath { get; set; } = string.Empty;

        /// <summary>
        /// DPI 缩放百分比（主程序窗口）
        /// </summary>
        public int DpiScalePercent_1 { get; set; } = 100;

        /// <summary>
        /// 角色语音播报开关
        /// </summary>
        public bool VoiceEnabled { get; set; } = false;

        /// <summary>
        /// 更新完成后是否启动程序（"最新版本"弹窗里的"启动更新"复选框）
        /// </summary>
        public bool StartUpdateAfterDownload { get; set; } = true;
    }
}