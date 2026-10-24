using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using LvchaxsBS.Config;
using LvchaxsBS.Core;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 重置配置服务：把 <c>Config</c> 目录整个删掉，然后立刻重启程序，
    /// 由 <see cref="ConfigManager"/> 在下次启动时按各配置类的默认值重新生成。
    ///
    /// <para><b>为什么不用 bat 了（上一个版本的坑）</b></para>
    /// 旧实现是"生成 reset.bat → 主程序退出 → bat 等进程消失 → 删目录 → 重启"。
    /// 实测：主程序 <c>Shutdown()</c> 后窗口关了，但进程可能仍滞留在后台
    /// （引擎的全局钩子 / 检测线程 / 常驻浮层窗口），于是 bat 只能干等到 30 轮上限后
    /// 用 <c>taskkill /f</c> 强杀，用户看到的现象就是：
    /// <i>确认之后程序关了，但配置没删、也没有重启</i>（其实是 40 多秒后才悄悄完成）。
    /// 另外 bat 里还有 cmd 引号解析、<c>tasklist</c> PID 复用等隐患。
    ///
    /// <para><b>现在的做法</b></para>
    /// 全部在当前进程内完成，不再依赖任何外部脚本或进程退出时序：
    /// <list type="number">
    /// <item>冻结配置写盘（<see cref="ConfigManager.SuppressPersist"/>），防止内存里的旧值把目录重建回来；</item>
    /// <item>停掉引擎（钩子 / 检测循环 / 浮层窗口）；</item>
    /// <item>删除 <c>Config</c> 目录（只删这一个目录，其余文件一律不动）；</item>
    /// <item>拉起新的程序实例；</item>
    /// <item><see cref="Environment.Exit"/> 立即结束当前进程。</item>
    /// </list>
    /// </summary>
    public static class ResetService
    {
        /// <summary>
        /// 删除 Config 目录并重启程序。调用后当前进程会立即结束，不会返回。
        /// </summary>
        public static void ResetAndRestart()
        {
            string configDir = ConfigManager.ConfigFolder.TrimEnd('\\', '/');

            // 安全兜底：只允许操作"名为 Config 的目录"，路径算错就中止，绝不递归删别的目录
            if (!string.Equals(Path.GetFileName(configDir), "Config", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"配置目录名不是 Config，已中止重置：{configDir}");

            string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath))
                exePath = Path.Combine(rootDir, "LvchaxsBS.exe");

            // 1) 冻结写盘
            ConfigManager.SuppressPersist = true;

            // 2) 停掉引擎，避免退出后钩子/检测线程继续驻留
            StopEngine();

            // 2.5) ★ Config\Macro 里放的是宏相关文件（脚本 / 诊断日志），不是「设置」——
            //      不能跟着重置一起没。删 Config 之前先把它挪到临时目录，删完再原样挪回来。
            string macroDir = Path.Combine(configDir, "Macro");
            string? backupRoot = null;

            if (Directory.Exists(macroDir))
            {
                backupRoot = Path.Combine(Path.GetTempPath(),
                                          "LvchaxsBS_Reset_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(backupRoot);

                try
                {
                    Directory.Move(macroDir, Path.Combine(backupRoot, "Macro"));
                }
                catch (Exception ex)
                {
                    // 备份不成功就别删 —— 宁可重置失败，也不能把用户写的宏弄丢
                    throw new IOException($"备份宏目录失败，已取消重置：{macroDir}\n{ex.Message}");
                }
            }

            // 3) 删除 Config 目录
            bool deleted = TryDeleteDirectory(configDir, out string? deleteError);
            if (!deleted && Directory.Exists(configDir))
            {
                // 没删掉 ⇒ 把宏原样放回去，再报错（否则宏就凭空消失了）
                if (backupRoot != null) RestoreMacro(configDir, backupRoot);
                throw new IOException($"删除配置目录失败：{configDir}\n{deleteError}");
            }

            if (backupRoot != null) RestoreMacro(configDir, backupRoot);

            // 4) 立刻拉起新实例（新实例启动时会重新生成默认配置）
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = rootDir,
                UseShellExecute = true
            });

            // 5) 立即结束当前进程。
            //    不用 Application.Current.Shutdown()：它要等 Dispatcher 收尾，
            //    一旦被后台线程/浮层窗口拖住就会卡在后台（这正是上一版失效的原因）。
            Environment.Exit(0);
        }

        /// <summary>
        /// 把临时目录里的宏目录放回 <c>Config\Macro</c>。
        /// <para>★ 失败只记日志：走到这里时配置已经重置完了，不能因为这一步失败就中断重启。</para>
        /// </summary>
        private static void RestoreMacro(string configDir, string backupRoot)
        {
            string src = Path.Combine(backupRoot, "Macro");
            if (!Directory.Exists(src)) return;

            try
            {
                Directory.CreateDirectory(configDir);

                string dest = Path.Combine(configDir, "Macro");
                if (Directory.Exists(dest)) Directory.Delete(dest, true);

                Directory.Move(src, dest);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResetService] 恢复宏目录失败：{ex.Message}");
            }
            finally
            {
                try { Directory.Delete(backupRoot, true); } catch { /* 临时目录，清不掉也无所谓 */ }
            }
        }

        /// <summary>停止引擎的各路后台活动；任何一步失败都不影响后续步骤。</summary>
        private static void StopEngine()
        {
            Try(IconService.Shutdown);
            Try(SubtitleOverlayService.Shutdown);
            Try(GlobalGamepadHookService.Stop);
            Try(GlobalMouseHookService.Stop);
            Try(GlobalKeyboardHookService.Stop);
        }

        private static void Try(Action action)
        {
            try { action(); }
            catch (Exception ex) { Debug.WriteLine($"[ResetService] 停止服务失败: {ex.Message}"); }
        }

        /// <summary>删除目录；失败时短暂重试（文件句柄通常几十毫秒内释放）。</summary>
        private static bool TryDeleteDirectory(string dir, out string? error)
        {
            error = null;
            if (!Directory.Exists(dir)) return true;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(dir, true);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Thread.Sleep(80);
                }
            }
            return false;
        }
    }
}
