namespace LvchaxsBS.Config
{
    [ConfigFile("PersonalizationSettings.json")]
    public class PersonalizationSettings
    {
        // 个性化开关
        public bool EnablePersonalization { get; set; } = false;
        // 壁纸路径
        public string WallpaperPath { get; set; } = string.Empty;

        // 卡片1
        public double WallpaperOpacity { get; set; } = 50;
        public double WallpaperBlur { get; set; } = 50;
        public double CardOpacity { get; set; } = 50;
        public bool EnableMirror { get; set; } = false;      // 启用镜像
        public bool EnableWallpaper { get; set; } = false;   // 启用壁纸

        // 卡片2
        public double XPosition { get; set; } = 0;
        public double YPosition { get; set; } = 0;
        public double WallpaperScale { get; set; } = 50;
        public double WallpaperRotation { get; set; } = 0;

        // 卡片3
        public double TitleOpacity { get; set; } = 10;       // 窗口圆角
        public double WindowOpacity { get; set; } = 0;     // 窗口透明度
        public double TitleFontSize { get; set; } = 11;     // 签名字体大小
        public string WindowTitle { get; set; } = "才识是年岁的冠冕，正如思念是我们共度的时间。";
    }
}