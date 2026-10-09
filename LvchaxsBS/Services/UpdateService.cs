using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 更新服务：生成 update.bat 并启动，之后主程序自行退出
    /// bat 负责：等主程序退出 → 下载文件覆盖 → 启动新 exe → 自删
    /// </summary>
    public static class UpdateService
    {
        /// <summary>正式版本下载目录前缀</summary>
        public const string RELEASE_BASE_URL =
            "https://gitee.com/accompanying-it/associated-with-lvchaxs/raw/master/release";

        /// <summary>测试版本下载目录前缀</summary>
        public const string BETA_BASE_URL =
            "https://gitee.com/accompanying-it/associated-with-lvchaxs/raw/master/beta";

        /// <summary>
        /// 生成 bat 并启动
        /// </summary>
        /// <param name="baseUrl">文件下载目录前缀，如 https://.../raw/master/release</param>
        public static async Task PrepareAndLaunchAsync(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("下载地址为空", nameof(baseUrl));

            // 1. 准备临时目录
            string tempDir = Path.Combine(Path.GetTempPath(), "LvchaxsBS_Update");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            Directory.CreateDirectory(tempDir);

            // 2. 生成 bat
            string batPath = Path.Combine(tempDir, "update.bat");
            File.WriteAllText(batPath, BuildBatContent(), new UTF8Encoding(false));

            // 3. 启动 bat
            int pid = Process.GetCurrentProcess().Id;
            string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string exeName = Path.GetFileName(
                Process.GetCurrentProcess().MainModule?.FileName ?? "LvchaxsBS.exe");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\" {pid} \"{baseUrl}\" \"{rootDir}\" \"{exeName}\"\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };

            Process.Start(psi);

            // 4. 给 cmd 一点启动时间，然后主程序退出
            await Task.Delay(500);
        }

        /// <summary>
        /// bat 脚本内容
        /// %1 = 主程序 PID
        /// %2 = 下载目录前缀 URL
        /// %3 = 程序根目录
        /// %4 = exe 文件名
        ///
        /// ⚠ 必须用 <c>%~1</c>~<c>%~4</c>，不能写 <c>%1</c>~<c>%4</c>：
        /// cmd 传给批处理的 <c>%N</c> 会**把参数自带的引号一起保留**，只有 <c>%~N</c> 才会去掉。
        /// 命令行里路径是带引号传的，所以写 <c>%3</c> 会拼出
        /// <c>""D:\某个目录"\LvchaxsBS.exe"</c> 这种坏路径 —— 下载写不进去，
        /// 最后的 start 也打不开程序，Windows 会干脆用资源管理器把那个路径当文件夹打开
        /// （表现就是"更新完弹出一个空白文件夹"）。
        /// </summary>
        private static string BuildBatContent()
        {
            return string.Join("\r\n", new[]
            {
                "@echo off",
                "chcp 65001 >nul",
                "",
                ":: 先等 2 秒，给主程序自己退出的时间",
                ":: 这里用 ping 而不是 timeout —— timeout 需要控制台输入句柄，",
                ":: 本进程没有控制台时会直接报错退出，等于没等。",
                "ping -n 3 127.0.0.1 >nul",
                "",
                ":: 循环等主程序退出，最多等 30 秒",
                "set /a count=0",
                ":wait",
                "tasklist /fi \"PID eq %~1\" | find \"%~1\" >nul",
                "if not errorlevel 1 (",
                "    ping -n 2 127.0.0.1 >nul",
                "    set /a count+=1",
                "    if %count% lss 30 goto wait",
                "    taskkill /f /pid %~1 >nul 2>&1",
                ")",
                "",
                ":: 下载 3 个文件，直接覆盖根目录",
                "curl -s -L -f -o \"%~3\\LvchaxsBS.exe\"                  \"%~2/LvchaxsBS.exe\"",
                "curl -s -L -f -o \"%~3\\LvchaxsBS.dll\"                  \"%~2/LvchaxsBS.dll\"",
                "curl -s -L -f -o \"%~3\\LvchaxsBS.runtimeconfig.json\"   \"%~2/LvchaxsBS.runtimeconfig.json\"",
                "",
                ":: 启动新程序",
                "start \"\" \"%~3\\%~4\"",
                "",
                ":: 自删",
                "del \"%~f0\""
            });
        }
    }
}