using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LvchaxsBS.Config;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 重置配置服务：删除 <c>Config</c> 目录并重启程序，
    /// 由 <see cref="ConfigManager"/> 在下次启动时按各配置类的默认值重新生成。
    /// <para>
    /// <b>重构说明（重要）</b>：旧实现用 <c>robocopy 空目录 → 程序目录 /MIR</c> 把
    /// **整个程序目录递归删除**，只保留 dll/exe/runtimeconfig 三个白名单文件，
    /// 会把用户放在同目录下的 Voice（头像 png / 语音 wav）、Wallpapers、截图日志等
    /// 一起永久删除（不进回收站）。现改为**只删 Config 目录**，其余文件一律不动，
    /// 并在 C# 与 bat 两侧都加目录名校验兜底。
    /// </para>
    /// </summary>
    public static class ResetService
    {
        /// <summary>
        /// 生成 reset.bat 并启动：等主程序退出 → 删除 Config 目录 → 重启程序 → bat 自删。
        /// 调用方应在返回后尽快关闭程序。
        /// </summary>
        public static async Task PrepareAndLaunchAsync()
        {
            string configDir = ConfigManager.ConfigFolder.TrimEnd('\\', '/');

            // 安全兜底①：只允许操作“名为 Config 的目录”，其余一律中止
            if (!string.Equals(Path.GetFileName(configDir), "Config", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"配置目录名不是 Config，已中止重置：{configDir}");

            string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                             ?? Path.Combine(rootDir, "LvchaxsBS.exe");

            // 1. 准备临时目录
            string tempDir = Path.Combine(Path.GetTempPath(), "LvchaxsBS_Reset");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) { Debug.WriteLine($"[ResetService] 清理临时目录失败: {ex.Message}"); }
            }
            Directory.CreateDirectory(tempDir);

            // 2. 生成 bat
            string batPath = Path.Combine(tempDir, "reset.bat");
            File.WriteAllText(batPath, BuildBatContent(), new UTF8Encoding(false));

            // 3. 启动 bat
            int pid = Process.GetCurrentProcess().Id;
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\" {pid} \"{configDir}\" \"{exePath}\"\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            Process.Start(psi);

            // 4. 给 cmd 一点启动时间，然后主程序自行退出
            await Task.Delay(500);
        }

        /// <summary>
        /// bat 脚本内容（逻辑全用 ASCII，避免批处理在中文代码页下解析出问题）。
        /// %1 = 主程序 PID，%2 = Config 目录完整路径，%3 = 程序 exe 完整路径
        /// </summary>
        private static string BuildBatContent()
        {
            return string.Join("\r\n", new[]
            {
                "@echo off",
                "chcp 65001 >nul",
                "setlocal enabledelayedexpansion",
                "",
                ":: wait a moment for the app to exit by itself",
                "timeout /t 2 /nobreak >nul",
                "",
                ":: wait for the main process to exit, max 30s, then force kill",
                "set /a count=0",
                ":wait",
                "tasklist /fi \"PID eq %1\" | find \"%1\" >nul",
                "if not errorlevel 1 (",
                "    timeout /t 1 /nobreak >nul",
                "    set /a count+=1",
                "    if !count! lss 30 goto wait",
                "    taskkill /f /pid %1 >nul 2>&1",
                ")",
                "",
                ":: safety guard: only operate when the target folder is named exactly \"Config\"",
                "for %%I in (\"%2\") do set CFGNAME=%%~nxI",
                "if /i not \"%CFGNAME%\"==\"Config\" (",
                "    echo [ResetService] target is not a Config folder, aborted: %2",
                "    goto restart",
                ")",
                "",
                ":: delete ONLY the config folder; Voice / Wallpapers / screenshots are left untouched",
                "if exist \"%2\" rd /s /q \"%2\" >nul 2>&1",
                "echo [ResetService] config folder reset: %2",
                "",
                ":restart",
                ":: restart the app (it will regenerate default configs)",
                "start \"\" \"%3\"",
                "",
                ":: self delete",
                "del \"%~f0\""
            });
        }
    }
}
