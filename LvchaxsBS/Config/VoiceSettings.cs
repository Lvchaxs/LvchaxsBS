namespace LvchaxsBS.Config
{
    [ConfigFile("VoiceSettings.json")]
    public class VoiceSettings
    {
        /// <summary>
        /// 字幕背景透明度（0~100，%）
        /// </summary>
        public int SubtitleOpacity { get; set; } = 80;

        /// <summary>
        /// 列表排序字段：0=名称，1=字数
        /// </summary>
        public int SortField { get; set; } = 0;

        /// <summary>
        /// 列表排序方向：0=正序，1=倒序
        /// </summary>
        public int SortOrder { get; set; } = 0;
    }
}