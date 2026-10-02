using System;
using System.Linq;
using System.Windows;
using LvchaxsBS.Config;

namespace LvchaxsBS.Services
{
    public enum AppTheme { Light, Dark }

    public static class ThemeService
    {
        private static readonly Uri LightUri = new("pack://application:,,,/LvchaxsBS;component/UI/Assets/Themes/Light.xaml");
        private static readonly Uri DarkUri = new("pack://application:,,,/LvchaxsBS;component/UI/Assets/Themes/Dark.xaml");

        public static AppTheme Current { get; private set; } = AppTheme.Light;

        public static event EventHandler<AppTheme>? ThemeChanged;

        public static void Apply(AppTheme theme)
        {
            if (Application.Current == null) return;

            var dicts = Application.Current.Resources.MergedDictionaries;

            // 1. 移除旧主题
            var oldTheme = dicts.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.EndsWith("Light.xaml") ||
                 d.Source.OriginalString.EndsWith("Dark.xaml")));
            if (oldTheme != null) dicts.Remove(oldTheme);

            // 2. 插入新主题
            var newUri = theme == AppTheme.Light ? LightUri : DarkUri;
            dicts.Insert(0, new ResourceDictionary { Source = newUri });

            // 3. 已删除（不再重新加载 CardStyles.xaml）

            Current = theme;
            ThemeChanged?.Invoke(null, theme);
        }

        public static void Toggle()
        {
            var next = Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
            Apply(next);

            var settings = ConfigManager.Get<AppSettings>();
            settings.Theme = next.ToString();
            ConfigManager.Save(settings);
        }

        public static void LoadFromConfig()
        {
            var settings = ConfigManager.Get<AppSettings>();
            var theme = string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase)
                ? AppTheme.Dark
                : AppTheme.Light;
            Apply(theme);
        }
    }
}