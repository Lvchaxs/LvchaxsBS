using System.Windows;
using LvchaxsBS.Config;
using LvchaxsBS.Services;

namespace LvchaxsBS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. 一次性加载所有配置
            ConfigManager.LoadAll();

            // 2. 根据配置应用主题
            ThemeService.LoadFromConfig();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 退出前把防抖延迟中尚未落盘的配置全部写回磁盘
            ConfigManager.FlushAll();

            base.OnExit(e);
        }
    }
}