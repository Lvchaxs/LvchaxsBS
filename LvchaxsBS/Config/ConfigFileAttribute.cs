using System;

namespace LvchaxsBS.Config
{
    /// <summary>
    /// 标注配置类对应的 JSON 文件名（相对 Config 目录）。
    /// ConfigManager 通过反射扫描此特性自动注册配置路径。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ConfigFileAttribute : Attribute
    {
        /// <summary>
        /// 相对 Config 目录的 JSON 文件路径，例如 "AppSettings.json"
        /// 或 "FunctionConfigs/QuickTeleportSettings.json"。
        /// </summary>
        public string FileName { get; }

        public ConfigFileAttribute(string fileName)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }
    }
}