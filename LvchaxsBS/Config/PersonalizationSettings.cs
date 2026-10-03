namespace LvchaxsBS.Config
{
    [ConfigFile("PersonalizationSettings.json")]
    public class PersonalizationSettings
    {
        // 个性化开关
        public bool EnablePersonalization { get; set; } = false;
        // 壁纸路径
        public string WallpaperPath { get; set; } = string.Empty;

        // 卡片2 滑块参数（默认0）
        public double WallpaperOpacity { get; set; } = 10;
        public double WallpaperBlur { get; set; } = 0;
        public double CardOpacity { get; set; } = 50;

        // 卡片3 滑块参数（默认0）
        public double XPosition { get; set; } = 0;
        public double YPosition { get; set; } = 0;
        public double WallpaperScale { get; set; } = 50;
        public double WallpaperRotation { get; set; } = 0;

        // 卡片4 滑块 + 文本
        public double TitleOpacity { get; set; } = 10;       // 窗口圆角（历史命名）
        public double WindowOpacity { get; set; } = 5;     // 窗口透明度
        public double TitleFontSize { get; set; } = 12;     // 签名字体大小（历史命名）
        public string WindowTitle { get; set; } = "才识是年岁的冠冕，正如思念是我们共度的时间。"; // 窗口标题
        public string WindowTitleColor { get; set; } = "#FF4B5563"; // 窗口标题颜色

        // 卡片5 复选框
        public bool EnableMirror { get; set; } = false;      // 启用镜像
        public bool EnableWallpaper { get; set; } = false;   // 启用壁纸
    }
}