using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 更新服务：生成 update.bat（外加它调用的一个 ps1）并启动，之后主程序自行退出。
    /// bat 负责：等主程序退出 → 下载文件覆盖 → 打开「弹改动窗口」开关 → 启动新 exe → 自删。
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
        /// 主程序配置文件（<c>AppSettings.json</c>）里那个「本次启动弹出版本改动」开关的键名。
        /// 脚本与主程序共用它 —— 改键名时两边一起改。
        /// </summary>
        public const string ChangelogFlagKey = "ShowChangelogOnStartup";

        /// <summary>
        /// 兜底清单：只有**问不到远端目录清单**时才用它（见 <see cref="ListRemoteFolderAsync"/>）。
        /// <para>★ 正常情况下文件名不写死 —— 渠道目录里有什么就下什么，新版本多出来的 dll 也能装上。</para>
        /// </summary>
        private static readonly string[] RequiredUpdateFiles =
        {
            "LvchaxsBS.exe",
            "LvchaxsBS.dll",
            "LvchaxsBS.runtimeconfig.json",
            "LvchaxsBS_Resources.dll",
            "LvchaxsBS_uid.dll",
            "LvchaxsBS_macro.dll",
        };

        /// <summary>
        /// 有就一起更新、没有就算了的文件（远端没有时跳过，不阻断更新）。
        /// <para>宏编辑器依赖 `ICSharpCode.AvalonEdit.dll`：正式版目录里可能没传它。</para>
        /// </summary>
        private static readonly string[] OptionalUpdateFiles =
        {
            "ICSharpCode.AvalonEdit.dll",
        };

        private static readonly HttpClient Http = CreateHttpClient();

        /// <summary>
        /// 启动自检补件专用的 client：超时比更新短得多 —— 它在启动路径上，不能让用户干等。
        /// </summary>
        private static readonly HttpClient StartupHttp = CreateHttpClient(TimeSpan.FromSeconds(20));

        private static HttpClient CreateHttpClient(TimeSpan? timeout = null)
        {
            var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(120) };
            // Gitee 的 raw 地址有时会挡掉没有 UA 的请求
            client.DefaultRequestHeaders.Add("User-Agent", "LvchaxsBS-Updater/1.0");
            return client;
        }

        /// <summary>
        /// 更新流程：
        /// <list type="number">
        /// <item>先把远端文件**全部**下载到临时目录（这一步主程序还在跑）；</item>
        /// <item>缺必需文件 ⇒ 抛异常「远程仓库文件缺失无法安装」，临时目录留着不动、不关程序；</item>
        /// <item>都齐了才生成 bat 并启动：bat 等主程序退出 → 从临时目录复制覆盖 → 启动新 exe。</item>
        /// </list>
        /// </summary>
        /// <param name="baseUrl">文件下载目录前缀，如 https://.../raw/master/release</param>
        public static async Task PrepareAndLaunchAsync(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("下载地址为空", nameof(baseUrl));

            // 1. 准备临时目录（整个重建，避免上一次的残留被当成这次下载好的文件）
            string tempDir = Path.Combine(Path.GetTempPath(), "LvchaxsBS_Update");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }

            string payloadDir = Path.Combine(tempDir, "payload");
            Directory.CreateDirectory(payloadDir);

            // 2. 先把文件全下下来（下不全就直接失败，一个都不装）
            await DownloadAllAsync(baseUrl, payloadDir).ConfigureAwait(false);

            // 3. 生成 ps1（改配置文件开关用的）与 bat
            //    ★ ps1 带 BOM 写：Windows PowerShell 5.1 读无 BOM 的 UTF-8 脚本时会按系统
            //      ANSI（GBK）解码，脚本里的中文注释会变成乱码 —— 加 BOM 它才按 UTF-8 解析。
            File.WriteAllText(Path.Combine(tempDir, FlagScriptName),
                              BuildFlagScriptContent(), new UTF8Encoding(true));

            string batPath = Path.Combine(tempDir, "update.bat");
            File.WriteAllText(batPath, BuildBatContent(), new UTF8Encoding(false));

            // 4. 启动 bat（它自己会等主程序退出）
            int pid = Process.GetCurrentProcess().Id;
            string rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string exeName = Path.GetFileName(
                Process.GetCurrentProcess().MainModule?.FileName ?? "LvchaxsBS.exe");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\" {pid} \"{payloadDir}\" \"{rootDir}\" \"{exeName}\"\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };

            Process.Start(psi);

            // 5. 给 cmd 一点启动时间，然后主程序退出
            await Task.Delay(500);
        }

        /// <summary>
        /// 把一个渠道目录（beta / release）里的文件**全部**下载到临时目录。
        /// <list type="number">
        /// <item>先问 Gitee 这个目录里有哪些文件（不写死文件名 ⇒ 新版本多出来的 dll 也会被下载）；</item>
        /// <item>清单问不到（网络 / 接口限流 / 地址不是 gitee raw）⇒ 退回内置清单；</item>
        /// <item>清单里的文件一个都少不了：少一个就抛「远程仓库文件缺失无法安装」。</item>
        /// </list>
        /// </summary>
        private static async Task DownloadAllAsync(string baseUrl, string payloadDir)
        {
            // ① 定下载清单
            var plan = new List<(string File, string Url, bool Required)>();

            List<string>? remote = await ListRemoteFolderAsync(baseUrl).ConfigureAwait(false);

            if (remote is { Count: > 0 })
            {
                foreach (string file in remote)
                    plan.Add((file, $"{baseUrl}/{file}", true));
            }
            else
            {
                // 拿不到目录清单 ⇒ 用内置清单，先逐个确认存在再下
                Debug.WriteLine("[更新] 取不到远端目录清单，改用内置清单");

                foreach (string file in RequiredUpdateFiles)
                {
                    if (!await RemoteFileExistsAsync($"{baseUrl}/{file}").ConfigureAwait(false))
                        throw new InvalidOperationException($"远程仓库文件缺失无法安装：缺少 {file}");

                    plan.Add((file, $"{baseUrl}/{file}", true));
                }

                foreach (string file in OptionalUpdateFiles)
                {
                    if (!await RemoteFileExistsAsync($"{baseUrl}/{file}").ConfigureAwait(false))
                    {
                        Debug.WriteLine($"[更新] 远端没有 {file}，跳过（可选文件）");
                        continue;
                    }

                    plan.Add((file, $"{baseUrl}/{file}", false));
                }
            }

            // ② 全部下载到临时目录
            foreach ((string file, string url, bool required) in plan)
            {
                string dest = Path.Combine(payloadDir, file);

                try
                {
                    bool ok = await TryDownloadAsync(url, dest).ConfigureAwait(false);
                    if (!ok)
                    {
                        if (required)
                            throw new InvalidOperationException($"远程仓库文件缺失无法安装：缺少 {file}");

                        Debug.WriteLine($"[更新] 可选文件 {file} 没下到，跳过");
                    }
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    // 必需/可选都一样：下到一半断了就别装（宁可不装也不装一半）
                    throw new IOException($"下载失败：{file}（{ex.Message}）", ex);
                }
            }
        }

        /// <summary>
        /// 启动自检要补齐的依赖：除主程序本体（<c>LvchaxsBS.exe</c> / <c>LvchaxsBS.dll</c>）之外那几个。
        /// <para>★ 全是"可有可无"的组件：缺了程序照样能起来，只是对应功能没有
        ///   （没有宏 / 没有宏编辑器 / 没有 UID 账号管理 / 图片留空且识别停用）。</para>
        /// </summary>
        public static readonly string[] RuntimeDependencies =
        {
            "LvchaxsBS_macro.dll",
            "ICSharpCode.AvalonEdit.dll",
            "LvchaxsBS_uid.dll",
            "LvchaxsBS_Resources.dll",
        };

        /// <summary>
        /// 启动自检：exe 同目录缺了哪个依赖 dll，就从**最新版本（release）**渠道下载补齐。
        /// <list type="bullet">
        /// <item>★ 必须在建窗口之前跑完：晚一步到位的话，共享组件会被判定"不可用"，
        ///       这一次的图片与识别就都没了 —— 所以调用处是**同步等**它返回的。</item>
        /// <item>★ 下不到（网络不通 / 仓库里没有）就跳过：这几个 dll 缺了程序照样能启动，
        ///       绝不能因为补件失败而不让程序起来。</item>
        /// </list>
        /// </summary>
        /// <returns>这次真正补齐的文件名（本来就不缺的不算）。</returns>
        public static async Task<List<string>> EnsureDependenciesAsync()
        {
            var repaired = new List<string>();
            string rootDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (string file in RuntimeDependencies)
            {
                string dest = Path.Combine(rootDir, file);

                // 已经有了就不动它（顺带验一下不是 0 字节 / 不是 HTML 之类的坏文件）
                if (File.Exists(dest) && LooksLikeValidFile(dest))
                    continue;

                Debug.WriteLine($"【启动】缺少 {file}，开始下载补齐…");

                try
                {
                    bool ok = await TryDownloadAsync($"{RELEASE_BASE_URL}/{file}", dest, StartupHttp)
                                  .ConfigureAwait(false);

                    if (!ok)
                    {
                        Debug.WriteLine($"【启动】仓库里没有 {file}，跳过（对应功能本次不可用）");
                        continue;
                    }

                    repaired.Add(file);
                    Debug.WriteLine($"【启动】已补齐 {file}");
                }
                catch (Exception ex)
                {
                    // 连不上就是连不上：剩下几个别再各等一个超时，直接放弃
                    Debug.WriteLine($"【启动】补齐 {file} 失败，跳过：{ex.Message}");
                    break;
                }
            }

            return repaired;
        }

        /// <summary>
        /// 问 Gitee 某个渠道目录（<c>beta</c> / <c>release</c>）里有哪些文件，返回文件名列表。
        /// <para>
        /// ★★ 不写死文件名：新版本往目录里多加一个 dll，老版本程序也能把它一起下下来 ——
        ///   否则"本地只认得旧清单"的新文件永远不会被更新到。
        /// </para>
        /// <para>
        /// 拿不到清单（网络异常 / 接口限流 / 地址不是 gitee raw）返回 <c>null</c>，
        /// 由调用方退回内置清单。
        /// </para>
        /// </summary>
        private static async Task<List<string>?> ListRemoteFolderAsync(string baseUrl)
        {
            // baseUrl 形如 https://gitee.com/{owner}/{repo}/raw/{ref}/{folder}
            Match m = Regex.Match(baseUrl,
                @"^https?://gitee\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/raw/(?<ref>[^/]+)/(?<folder>[^/]+)/?$",
                RegexOptions.IgnoreCase);

            if (!m.Success) return null;

            string api = "https://gitee.com/api/v5/repos/" +
                         m.Groups["owner"].Value + "/" + m.Groups["repo"].Value +
                         "/contents/" + m.Groups["folder"].Value +
                         "?ref=" + m.Groups["ref"].Value;

            try
            {
                using HttpResponseMessage resp = await Http.GetAsync(api).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[更新] 列目录失败：HTTP {(int)resp.StatusCode}");
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

                var files = new List<string>();

                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    // 只收文件：子目录不递归（渠道目录是平的）
                    if (item.TryGetProperty("type", out JsonElement type) &&
                        !string.Equals(type.GetString(), "file", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!item.TryGetProperty("name", out JsonElement name)) continue;

                    string? file = name.GetString();
                    if (string.IsNullOrWhiteSpace(file)) continue;

                    // 跳过 .keep 之类的占位 / 隐藏文件（Git 空目录占位用的，0 字节）
                    if (file.StartsWith(".", StringComparison.Ordinal)) continue;

                    files.Add(file);
                }

                Debug.WriteLine($"[更新] 远端目录 {m.Groups["folder"].Value} 共 {files.Count} 个文件");
                return files;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[更新] 列目录异常：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 远端有没有这个文件（只看响应状态码，不下载正文）。
        /// <para>★ 用 GET + <c>ResponseHeadersRead</c>：拿到响应头就返回，不会把 7MB 的资源包真下下来。</para>
        /// </summary>
        private static async Task<bool> RemoteFileExistsAsync(string url)
        {
            try
            {
                using HttpResponseMessage resp =
                    await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[更新] 探测 {url} 失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 下载单个文件。返回 false = **远端没有**（404 / 内容不是有效的 PE）；网络异常直接往外抛。
        /// </summary>
        /// <param name="client">用哪个 client（默认更新那个；启动补件传短超时的那个）。</param>
        private static async Task<bool> TryDownloadAsync(string url, string dest, HttpClient? client = null)
        {
            try
            {
                using HttpResponseMessage resp =
                    await (client ?? Http).GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return false;

                await using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                await using (FileStream dst = new FileStream(dest, FileMode.Create,
                                                             FileAccess.Write, FileShare.None))
                {
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }

                return LooksLikeValidFile(dest);
            }
            catch (HttpRequestException ex)
            {
                throw new IOException($"下载失败：{ex.Message}", ex);
            }
            catch (TaskCanceledException)
            {
                throw new IOException("下载超时");
            }
            finally
            {
                // 半成品不要留着被当成"下载好了"
                if (!LooksLikeValidFile(dest))
                {
                    try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                }
            }
        }

        /// <summary>
        /// 下下来的文件像不像那么回事：dll / exe 必须是 PE（"MZ" 头），其它（json）只要非空。
        /// <para>★ Gitee 有时会对不存在的文件返回一个 HTML 页面（而不是 404），这里兜一层。</para>
        /// </summary>
        private static bool LooksLikeValidFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0) return false;

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".dll" && ext != ".exe") return true;

                if (info.Length < 1024) return false;

                using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
            }
            catch
            {
                return false;
            }
        }

        /// <summary>改配置开关那份 ps1 的文件名（与 bat 同放在临时目录里）。</summary>
        private const string FlagScriptName = "set_changelog_flag.ps1";

        /// <summary>
        /// 改主程序配置文件的 ps1 内容：把 <c>ShowChangelogOnStartup</c> 置为 <c>true</c>。
        ///
        /// <para>为什么单独一份 ps1、而不是在 bat 里拼字符串：</para>
        /// <list type="number">
        /// <item>cmd 里引号嵌套没有可靠写法，直接在 bat 里写 PowerShell 极易写坏；</item>
        /// <item>老配置文件里**根本没有这个键**（本次更新才新增），
        ///       纯字符串替换会静默失效 —— 这里键不存在时会插到 JSON 末尾。</item>
        /// </list>
        ///
        /// <para>全程 try/catch：脚本改不动也只是不弹窗，绝不能挡住启动。</para>
        /// </summary>
        private static string BuildFlagScriptContent()
        {
            // ★ 引号一律用 [char]34 拼，不在 PowerShell 字符串里写裸 " —— 避免转义地狱
            return string.Join("\r\n", new[]
            {
                "param([string]$Path)",
                "",
                "$ErrorActionPreference = 'Stop'",
                "if (-not $Path) { exit 0 }",
                "if (-not (Test-Path -LiteralPath $Path)) { exit 0 }",
                "",
                "try {",
                "    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)",
                "    $q = [char]34",
                "    $key = '" + ChangelogFlagKey + "'",
                "",
                "    if ($text.Contains($key)) {",
                "        # 键已存在：只把它的值换成 true，其它内容一字不动",
                "        $pattern = $q + $key + $q + '\\s*:\\s*(true|false)'",
                "        $replace = $q + $key + $q + ': true'",
                "        $text = [regex]::Replace($text, $pattern, $replace)",
                "    } else {",
                "        # 键不存在（老配置文件）：插到最后一个 } 之前",
                "        $text = $text.TrimEnd()",
                "        $i = $text.LastIndexOf('}')",
                "        if ($i -ge 0) {",
                "            $head = $text.Substring(0, $i).TrimEnd()",
                "            if (-not $head.EndsWith('{')) { $head = $head.TrimEnd(',') + ',' }",
                "            $line = '  ' + $q + $key + $q + ': true'",
                "            $text = $head + \"`r`n\" + $line + \"`r`n}\"",
                "        }",
                "    }",
                "",
                "    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)",
                "    [System.IO.File]::WriteAllText($Path, $text, $utf8NoBom)",
                "} catch {",
                "    exit 0",
                "}"
            });
        }

        /// <summary>
        /// bat 脚本内容
        /// %1 = 主程序 PID
        /// %2 = 已经下载好的文件所在目录（临时目录里的 payload）
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
            var lines = new List<string>
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
                ":: ★ 文件在关程序之前就已经下载到临时目录了，这里只负责复制覆盖",
                "::   （以前是关完程序再 curl 下载，慢且失败也没人知道）",
            };

            // ★ 下什么文件是下载时按**远端目录清单**定的 ⇒ 这里整目录复制，
            //   以后仓库里加减文件都不用再改脚本。
            lines.Add("copy /y \"%~2\\*.*\" \"%~3\"");

            lines.AddRange(new[]
            {
                "",
                ":: ★ 让新程序启动后自动弹一次「版本改动」：打开配置里的开关",
                "::   主程序启动读到 true → 弹一次 → 立刻复位成 false 落盘 ⇒ 之后正常启动不再弹。",
                "::   路径直接由 %~3 拼（已去引号）；没有 PowerShell 就跳过，不影响启动。",
                "powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0" + FlagScriptName +
                    "\" \"%~3\\Config\\AppSettings.json\"",
                "",
                ":: 启动新程序",
                "start \"\" \"%~3\\%~4\"",
                "",
                ":: 清掉临时下载目录（bat 在它的上一级，最后自删）",
                "rd /s /q \"%~2\"",
                "del \"%~f0\""
            });

            return string.Join("\r\n", lines);
        }
    }
}