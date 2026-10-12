using System.Configuration;
using System.Data;
using System.Windows;

namespace LvchaxsBS_UID
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 启动前把散落在程序根目录的旧数据文件搬进 Config 文件夹（一次性）
            Utils.File.AppPaths.MigrateLegacyFilesFromRoot();
        }
    }

}
