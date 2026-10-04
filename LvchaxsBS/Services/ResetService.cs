using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 重置服务：生成 reset.bat 并启动，之后主程序自行退出
    /// bat 负责：等主程序退出 → 备份白名单 → robocopy 清空根目录 → 还原白名单 → 启动新 exe → 自删
    /// </summary>
    public static class ResetService
    {
        /// <summary>
        /// 白名单：这些文件不删除（不区分大小写）
        /// </summary>
        private static readonly string[] KeepNames =
        {
            "LvchaxsBS.dll",
            "LvchaxsBS.exe",
            "LvchaxsBS.runtimeconfig.json"
        };

        /// <summary>
        /// 生成 reset.bat 并启动
        /// </summary>
        public static async Task PrepareAndLaunchAsync()
        {
            // 1. 准备临时目录
            string tempDir = Path.Combine(Path.GetTempPath(), "LvchaxsBS_Reset");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            Directory.CreateDirectory(tempDir);

            // 2. 生成 bat
            string batPath = Path.Combine(tempDir, "reset.bat");
            File.WriteAllText(batPath, BuildBatContent(), new UTF8Encoding(false));

            // 3. 启动 bat
            int pid = Process.GetCurrentProcess().Id;
            string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string exeName = Path.GetFileName(
                Process.GetCurrentProcess().MainModule?.FileName ?? "LvchaxsBS.exe");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\" {pid} \"{rootDir}\" {exeName}\"",
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
        /// %2 = 程序根目录
        /// %3 = exe 文件名
        /// </summary>
        private static string BuildBatContent()
        {
            // 生成备份白名单文件的 copy 命令
            var backupLines = new StringBuilder();
            var restoreLines = new StringBuilder();
            foreach (var name in KeepNames)
            {
                backupLines.AppendLine($"copy /y \"%2\\{name}\" \"%KEEP%\\\" >nul 2>&1");
                restoreLines.AppendLine($"copy /y \"%KEEP%\\{name}\" \"%2\\\" >nul 2>&1");
            }

            return string.Join("\r\n", new[]
            {
                "@echo off",
                "chcp 65001 >nul",
                "setlocal",
                "",
                ":: 先等 2 秒，给主程序自己退出的时间",
                "timeout /t 2 /nobreak >nul",
                "",
                ":: 循环等主程序退出，最多等 30 秒",
                "set /a count=0",
                ":wait",
                "tasklist /fi \"PID eq %1\" | find \"%1\" >nul",
                "if not errorlevel 1 (",
                "    timeout /t 1 /nobreak >nul",
                "    set /a count+=1",
                "    if %count% lss 30 goto wait",
                "    taskkill /f /pid %1 >nul 2>&1",
                ")",
                "",
                ":: 准备临时目录",
                "set KEEP=%TEMP%\\LvchaxsBS_Keep",
                "set EMPTY=%TEMP%\\LvchaxsBS_Empty",
                "if exist \"%KEEP%\" rd /s /q \"%KEEP%\" >nul 2>&1",
                "if exist \"%EMPTY%\" rd /s /q \"%EMPTY%\" >nul 2>&1",
                "mkdir \"%KEEP%\"",
                "mkdir \"%EMPTY%\"",
                "",
                ":: 备份白名单文件",
                backupLines.ToString().TrimEnd(),
                "",
                ":: 用 robocopy 镜像空目录清空根目录（递归删除所有文件和子目录）",
                "robocopy \"%EMPTY%\" \"%2\" /MIR /NFL /NDL /NJH /NJS /NP /R:0 /W:0 >nul",
                "",
                ":: 还原白名单文件",
                restoreLines.ToString().TrimEnd(),
                "",
                ":: 清理临时目录",
                "rd /s /q \"%KEEP%\" >nul 2>&1",
                "rd /s /q \"%EMPTY%\" >nul 2>&1",
                "",
                ":: 启动新程序",
                "start \"\" \"%2\\%3\"",
                "",
                ":: 自删",
                "del \"%~f0\""
            });
        }
    }
}