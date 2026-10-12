using System;
using System.IO;

namespace LvchaxsBS_UID.Utils.File
{
    /// <summary>
    /// UID 模块本地数据文件的统一位置。
    /// 所有账号 / 分组 / 偏好等数据都放在 <c>程序目录\UID\Config</c> 下。
    ///
    /// <para>
    /// 为什么不是直接放 <c>程序目录\Config</c>：本模块现在以 dll 形式挂在宿主
    /// LvchaxsBS.exe 里运行，而宿主的「重置配置」功能会把整个 <c>Config</c> 目录
    /// 递归删除。若与宿主共用该目录，一次重置就会把用户的账号数据一并清空。
    /// 因此把 UID 数据单独收进 <c>UID</c> 子目录，与宿主配置完全隔离。
    /// </para>
    /// </summary>
    public static class AppPaths
    {
        /// <summary>程序目录下 UID 模块的数据根目录名。</summary>
        public const string DataRootFolderName = "UID";

        /// <summary>数据根目录下的配置子目录名。</summary>
        public const string ConfigFolderName = "Config";

        private static readonly string ConfigDir = EnsureConfigDirectory();

        /// <summary>UID 模块数据的根目录（程序目录\UID）。</summary>
        public static string DataRootDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DataRootFolderName);

        /// <summary>Config 文件夹的绝对路径（不存在时自动创建）。</summary>
        public static string ConfigDirectory => ConfigDir;

        /// <summary>取 Config 文件夹下某个数据文件的完整路径。</summary>
        public static string ConfigFile(string fileName) => Path.Combine(ConfigDir, fileName);

        private static string EnsureConfigDirectory()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DataRootFolderName, ConfigFolderName);

            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建 Config 目录失败: {ex.Message}");
            }

            return dir;
        }

        /// <summary>
        /// 一次性迁移：把早期版本散落在「程序根目录」或「程序根目录\Config」里的
        /// 数据文件搬进 UID 专属目录。只有当目标里还没有同名文件时才搬，不会覆盖已有数据。
        /// </summary>
        public static void MigrateLegacyFilesFromRoot()
        {
            string[] dataFiles =
            {
                "accounts.enc.json",
                "accounts.json",
                "groups.json",
                "pinned_orders.json",
                "mark_after_login_preference.json",
                "login_checkbox_settings.json"
            };

            string root = AppDomain.CurrentDomain.BaseDirectory;

            // 旧位置 1：程序根目录（最初版本）
            // 旧位置 2：程序根目录\Config（上一版本，与宿主配置混在一起）
            string[] legacyDirs =
            {
                root,
                Path.Combine(root, ConfigFolderName)
            };

            foreach (string fileName in dataFiles)
            {
                string newPath = Path.Combine(ConfigDir, fileName);

                // 目标已存在就不动，避免覆盖当前数据
                if (System.IO.File.Exists(newPath))
                    continue;

                foreach (string legacyDir in legacyDirs)
                {
                    try
                    {
                        string oldPath = Path.Combine(legacyDir, fileName);
                        if (System.IO.File.Exists(oldPath))
                        {
                            System.IO.File.Move(oldPath, newPath);
                            System.Diagnostics.Debug.WriteLine($"已迁移数据文件到 {ConfigDir}: {fileName}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"迁移数据文件失败 {fileName}: {ex.Message}");
                    }
                }
            }
        }
    }
}
