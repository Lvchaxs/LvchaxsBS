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

        /// <summary>
        /// 本次启动是否自动弹出「当前版本改动」窗口。
        ///
        /// <para>用途：区分「用户自己启动程序」和「更新脚本更新完拉起程序」。</para>
        /// <list type="number">
        /// <item>更新脚本（update.bat）下载替换完所有文件之后、启动 exe <b>之前</b>，
        ///       把这里写成 <c>true</c>（键不存在时会直接插进去，兼容老配置文件）；</item>
        /// <item>主程序启动时读到 <c>true</c> → 显示一次改动内容 → <b>立刻复位成 false 并落盘</b>；</item>
        /// <item>之后正常启动读到的都是 <c>false</c>，不再弹。</item>
        /// </list>
        ///
        /// ⇒ 效果：只有「刚更新完、由脚本拉起来」这一次会看到改动说明。
        /// </summary>
        public bool ShowChangelogOnStartup { get; set; } = false;
    }
}