using LvchaxsBS.Config;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 检查更新服务
    /// 从 Gitee 仓库获取 modules.json 中的版本号，与本地 exe 版本号比较
    /// </summary>
    public static class UpdateCheckService
    {
        #region 配置

        /// <summary>
        /// modules.json 的原始文件地址
        /// 注意：请根据你的默认分支修改 master/main
        /// </summary>
        private const string MODULES_JSON_URL =
            "https://gitee.com/accompanying-it/associated-with-lvchaxs/raw/master/modules.json";
        /// <summary>
        /// 请求超时时间（秒）
        /// </summary>
        private const int REQUEST_TIMEOUT_SECONDS = 15;

        #endregion

        #region 状态

        private static readonly HttpClient _httpClient;

        /// <summary>
        /// 是否正在检查更新
        /// </summary>
        public static bool IsChecking { get; private set; } = false;

        /// <summary>
        /// 最近一次检查到的远程版本
        /// </summary>
        public static string? LastRemoteVersion { get; private set; }

        /// <summary>
        /// 最近一次检查到的本地版本
        /// </summary>
        public static string? LastLocalVersion { get; private set; }

        /// <summary>
        /// 最近一次检查时间
        /// </summary>
        public static DateTime? LastCheckTime { get; private set; }

        /// <summary>
        /// 是否有新版本
        /// </summary>
        public static bool HasNewVersion { get; private set; } = false;

        /// <summary>
        /// 本次运行内，已经提示过的新版本号
        /// 用于避免启动时提示一次，进入设置页又提示一次
        /// </summary>
        private static string? _notifiedVersion = null;

        /// <summary>
        /// 检查更新完成事件
        /// 参数：是否有新版本、远程版本、本地版本
        /// </summary>
        public static event Action<bool, string, string>? UpdateChecked;

        #endregion

        #region 构造函数

        static UpdateCheckService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "LvchaxsBS-UpdateChecker/1.0");
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取本地 exe 版本号
        /// </summary>
        public static string GetLocalVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version?.ToString() ?? "0.0.0.0";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取本地版本失败：{ex.Message}");
                return "0.0.0.0";
            }
        }

        /// <summary>
        /// 异步检查更新
        /// </summary>
        /// <returns>是否有新版本</returns>
        public static async Task<bool> CheckUpdateAsync()
        {
            if (IsChecking)
            {
                Debug.WriteLine("检查更新已在进行中，跳过");
                return HasNewVersion;
            }

            IsChecking = true;
            HasNewVersion = false;

            // 每次检查先清空上次结果，避免误判
            LastRemoteVersion = null;

            try
            {
                string localVersion = GetLocalVersion();
                LastLocalVersion = localVersion;

                Debug.WriteLine($"");
                Debug.WriteLine($"========== 检查更新 ==========");
                Debug.WriteLine($"本地版本: {localVersion}");
                Debug.WriteLine($"请求地址: {MODULES_JSON_URL}");

                // 获取远程 modules.json
                string json = await FetchModulesJsonAsync();

                if (string.IsNullOrEmpty(json))
                {
                    Debug.WriteLine("远程 modules.json 为空或获取失败");
                    return false;
                }

                // 解析版本号
                string? remoteVersion = ParseVersionFromJson(json);

                if (string.IsNullOrEmpty(remoteVersion))
                {
                    Debug.WriteLine("无法从 modules.json 中解析版本号");
                    return false;
                }

                LastRemoteVersion = remoteVersion;
                Debug.WriteLine($"远程版本: {remoteVersion}");

                // 比较版本号
                bool isNewer = CompareVersions(remoteVersion, localVersion);

                HasNewVersion = isNewer;
                LastCheckTime = DateTime.Now;

                Debug.WriteLine($"是否有新版本: {(isNewer ? "是" : "否")}");
                Debug.WriteLine($"==============================");

                // 触发事件
                UpdateChecked?.Invoke(isNewer, remoteVersion, localVersion);

                return isNewer;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检查更新失败：{ex.Message}");
                return false;
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>
        /// 检查更新并 Toast 通知 / 自动更新
        /// 规则：
        /// - 有新版本 → 读"新版本自动更新"复选框：
        ///     开：直接执行更新（不弹确认），更新完程序自动重启
        ///     关：Toast 提示用户前往程序设置更新（同一次运行内同一个版本只提示一次）
        /// - 出错 → 提示
        /// - 已最新 → 静默
        /// </summary>
        public static async Task CheckAndNotifyAsync()
        {
            bool hasNew = await CheckUpdateAsync();

            string remoteVer = LastRemoteVersion ?? "";

            if (hasNew && !string.IsNullOrEmpty(remoteVer))
            {
                bool autoUpdate = ConfigManager.Get<AppSettings>().StartUpdateAfterDownload;

                if (autoUpdate)
                {
                    // 勾选了"新版本自动更新" → 直接执行更新，不弹确认
                    Debug.WriteLine($"勾选自动更新，直接执行更新 V{remoteVer}");

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ToastService.Show("自动更新", $"检测到新版本 V{remoteVer}，即将自动更新...");
                    });

                    // 让 Toast 显示一下再执行
                    await Task.Delay(1500);

                    try
                    {
                        await UpdateService.PrepareAndLaunchAsync(UpdateService.RELEASE_BASE_URL);

                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"自动更新失败：{ex}");
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            ToastService.Show("自动更新失败", false);
                        });
                    }
                }
                else
                {
                    // 未勾选 → 只提示
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        // 同一个版本在一次运行内只提示一次
                        if (_notifiedVersion == remoteVer)
                        {
                            Debug.WriteLine($"新版本 V{remoteVer} 已提示过，跳过");
                            return;
                        }

                        _notifiedVersion = remoteVer;
                        ToastService.Show($"发现有新版本 V{remoteVer} 前往程序设置进行更新", false);
                    });
                }
            }
            else if (string.IsNullOrEmpty(remoteVer))
            {
                // 出错
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ToastService.Show("检查更新出错", false);
                });
            }
            // 已最新 → 静默
        }

        /// <summary>
        /// 重置"已提示版本"标记（一般不需要调用）
        /// </summary>
        public static void ResetNotifiedVersion()
        {
            _notifiedVersion = null;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 获取 modules.json 内容
        /// </summary>
        private static async Task<string> FetchModulesJsonAsync()
        {
            try
            {
                // 添加时间戳避免缓存
                string url = $"{MODULES_JSON_URL}?t={DateTime.Now.Ticks}";

                using var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"HTTP 请求失败：{response.StatusCode}");
                    return string.Empty;
                }

                string content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"modules.json 内容长度: {content.Length}");
                return content;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("请求超时");
                return string.Empty;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"网络请求异常：{ex.Message}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取 modules.json 失败：{ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 从 JSON 中解析版本号
        /// 支持格式：
        /// 1. { "version": "1.0.0.1" }
        /// 2. { "Version": "1.0.0.1" }
        /// 3. { "modules": { "version": "1.0.0.1" } }
        /// 4. 纯字符串 "1.0.0.1"
        /// </summary>
        private static string? ParseVersionFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                // 先尝试当作纯字符串处理
                string trimmed = json.Trim().Trim('"');
                if (Version.TryParse(trimmed, out _))
                {
                    return trimmed;
                }

                // 尝试解析为 JSON 对象
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 直接查找 version 字段（不区分大小写）
                if (TryGetVersionProperty(root, out string? version))
                {
                    return version;
                }

                // 查找嵌套的 version 字段
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            if (TryGetVersionProperty(prop.Value, out version))
                            {
                                return version;
                            }
                        }
                    }
                }

                return null;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JSON 解析失败：{ex.Message}");

                // 最后的兜底：用正则提取版本号
                var match = Regex.Match(json, @"\d+\.\d+\.\d+(\.\d+)?");
                if (match.Success)
                {
                    return match.Value;
                }

                return null;
            }
        }

        /// <summary>
        /// 尝试从 JSON 对象中获取 version 属性
        /// </summary>
        private static bool TryGetVersionProperty(JsonElement element, out string? version)
        {
            version = null;

            if (element.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name.Equals("version", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Equals("ver", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        version = prop.Value.GetString();
                        return !string.IsNullOrEmpty(version);
                    }
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        version = prop.Value.GetRawText();
                        return !string.IsNullOrEmpty(version);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 比较版本号
        /// </summary>
        /// <returns>remote 是否比 local 新</returns>
        private static bool CompareVersions(string remote, string local)
        {
            try
            {
                // 标准化版本号（补齐到 4 位）
                string r = NormalizeVersion(remote);
                string l = NormalizeVersion(local);

                if (Version.TryParse(r, out var remoteVer) &&
                    Version.TryParse(l, out var localVer))
                {
                    return remoteVer > localVer;
                }

                // 兜底：字符串比较
                return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"版本比较失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 标准化版本号，补齐到 4 位
        /// 例如 "1.0" -> "1.0.0.0"
        /// </summary>
        private static string NormalizeVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return "0.0.0.0";

            string[] parts = version.Split('.');
            var result = new string[4];

            for (int i = 0; i < 4; i++)
            {
                if (i < parts.Length && int.TryParse(parts[i], out int num))
                {
                    result[i] = num.ToString();
                }
                else
                {
                    result[i] = "0";
                }
            }

            return string.Join(".", result);
        }

        #endregion
    }
}