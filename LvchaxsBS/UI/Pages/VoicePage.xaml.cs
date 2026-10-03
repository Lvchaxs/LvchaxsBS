using LvchaxsBS.Config;
using LvchaxsBS.UI.Controls;
using LvchaxsBS.UI.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.UI.Pages
{
    public partial class VoicePage : Page
    {
        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "Voice", "voice_config.json");

        private static readonly string PicturesDir =
            Path.Combine(AppContext.BaseDirectory, "Voice", "Pictures");

        private static readonly string VoicesDir =
            Path.Combine(AppContext.BaseDirectory, "Voice", "Voices");

        private static readonly string VoiceDir =
            Path.Combine(AppContext.BaseDirectory, "Voice");

        private const string NoRoleKey = "false";

        private static readonly StringComparer ZhComparer =
            StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true);

        private bool _isLoadingSettings = false;
        private long _totalSizeBytes = 0;

        // ===== 静态缓存（跨页面实例复用，避免每次进页都扫盘/重载头像） =====

        /// <summary>扫描结果缓存：目录没变就不重扫</summary>
        private static (VoiceConfig cfg, long totalBytes, DateTime voiceStamp, DateTime voicesStamp, DateTime picturesStamp)? _scanCache;

        /// <summary>
        /// 当前生效的配置。**必须是静态的**：渲染好的卡片控件会被跨页面实例复用，
        /// 挂在控件上的事件回调不能捕获某个页面实例，否则编辑保存会写到旧实例的数据上。
        /// </summary>
        private static VoiceConfig? _currentConfig;

        /// <summary>头像图片缓存（已 Freeze，可跨线程/跨实例安全复用）</summary>
        private static readonly Dictionary<string, BitmapSource?> _avatarCache = new();

        /// <summary>标签颜色缓存（预生成冻结画刷，避免每个标签都 new Brush）</summary>
        private static readonly Brush[] TagBrushes = CreateTagBrushes();

        /// <summary>搜索防抖定时器</summary>
        private System.Windows.Threading.DispatcherTimer? _searchTimer;

        // ===== 渲染结果缓存：再次进入页面时直接复用，不再扫描 / 写盘 / 重建 =====

        /// <summary>上次渲染好的卡片（DynamicCharacterPanel 的内容）</summary>
        private static List<UIElement>? _cachedCards;

        /// <summary>上次渲染好的 空 / 荧 标签</summary>
        private static List<UIElement>? _cachedKongTags;
        private static List<UIElement>? _cachedYingTags;

        /// <summary>缓存对应的统计信息（再次进入时直接显示，不用重算）</summary>
        private static (int matched, int total, int wavCount, long sizeBytes)? _cachedStats;

        // ===== 渐进渲染：本次运行第一次进页面时逐张显示卡片，能直接看到加载过程 =====

        /// <summary>本次运行是否已经做过首次渐进加载（只有第一次完整加载才逐张显示）</summary>
        private static bool _progressiveShownOnce;

        /// <summary>待渲染的条目队列（顺序 = voice_config.json 里的键顺序）</summary>
        private readonly Queue<(string name, CharacterConfig cc)> _renderQueue = new();

        /// <summary>渲染调度器（首次进入的慢速渐进加载用）</summary>
        private System.Windows.Threading.DispatcherTimer? _renderTimer;

        /// <summary>每批渲染几张（按总数量自适应，保证总时长基本恒定）</summary>
        private int _renderBatchSize = 1;

        /// <summary>true = 由 Timer 按固定节奏驱动（首次进入的渐进加载）；false = 连拍快速渲染</summary>
        private bool _renderUseTimer;

        /// <summary>渲染代次号：取消 / 重开时自增，使在途的续拍回调失效</summary>
        private int _renderToken;

        /// <summary>已渲染 / 待渲染 总数（仅用于进度显示）</summary>
        private int _renderDone;
        private int _renderTotal;

        /// <summary>正在渲染的配置（进度与收尾用）</summary>
        private VoiceConfig? _renderConfig;

        /// <summary>json 键顺序缓存（搜索 / 排序会反复触发渲染，避免每次都读盘解析）</summary>
        private List<string>? _jsonKeyOrder;

        /// <summary>渐进加载的批次间隔（ms）：× 目标批次数 ≈ 总时长，默认约 0.9 秒</summary>
        private const int RenderTickMs = 30;

        /// <summary>目标总批次数（卡片多少，总时长都差不多）</summary>
        private const int RenderTargetBatches = 30;

        /// <summary>快速渲染时每批的卡片数（越大越快，越小越不容易卡）</summary>
        private const int FastRenderBatchSize = 20;

        private static Brush[] CreateTagBrushes()
        {
            string[] hex = { "#60A5FA", "#34D399", "#F87171", "#FB923C", "#A78BFA", "#F472B6", "#2DD4BF", "#818CF8", "#22D3EE" };
            var brushes = new Brush[hex.Length];
            for (int i = 0; i < hex.Length; i++)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex[i]));
                b.Freeze();
                brushes[i] = b;
            }
            return brushes;
        }

        public VoicePage()
        {
            InitializeComponent();
            Loaded += VoicePage_Loaded;
            Unloaded += VoicePage_Unloaded;
        }

        private void VoicePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _searchTimer?.Stop();
            _voicePlayer?.Stop();   // 离开页面时停掉正在试听的音频

            // 渲染中途被打断 → 页面上的内容不完整，缓存作废（下次进入重新完整加载）
            if (CancelRender())
            {
                ClearContentCache();
                return;
            }

            // 搜索状态下显示的是过滤结果：保留上一次的完整缓存即可
            // （下次进入仍是完整列表，和空的搜索框一致）
            if (!string.IsNullOrEmpty(SearchBox.Text)) return;

            SaveContentCache();
        }

        private void VoicePage_Loaded(object sender, RoutedEventArgs e)
        {
            SliderEntryAnimator.PlayAll(this);

            _isLoadingSettings = true;

            var voice = ConfigManager.Get<VoiceSettings>();

            SubtitleOpacitySlider.Value = voice.SubtitleOpacity;

            SortFieldSelect.ItemsSource = new[] { "名称", "字数" };
            SortFieldSelect.SelectedIndex = voice.SortField;

            SortOrderSelect.ItemsSource = new[] { "正序", "倒序" };
            SortOrderSelect.SelectedIndex = voice.SortOrder;

            _isLoadingSettings = false;

            SubtitleOpacitySlider.ValueChanged -= SubtitleOpacitySlider_ValueChanged;
            SortFieldSelect.SelectionChanged -= SortFieldSelect_SelectionChanged;
            SortOrderSelect.SelectionChanged -= SortOrderSelect_SelectionChanged;
            SearchBox.TextChanged -= SearchBox_TextChanged;

            SubtitleOpacitySlider.ValueChanged += SubtitleOpacitySlider_ValueChanged;
            SortFieldSelect.SelectionChanged += SortFieldSelect_SelectionChanged;
            SortOrderSelect.SelectionChanged += SortOrderSelect_SelectionChanged;
            SearchBox.TextChanged += SearchBox_TextChanged;

            // 本次运行已经完整加载过 → 直接复用上次渲染好的控件，
            // 不再扫描、不再写盘、不再重建（秒开）
            if (RestoreContentCache()) return;

            // 只有本次运行第一次进入才走"完整加载"：先让页面出现，再扫描 + 写盘 + 逐张渲染
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded) return;   // 这一帧内页面已被切走，放弃

                // 只有启动后第一次完整加载才逐张显示；其余（缓存失效后的重建）直接快速填充
                bool progressive = !_progressiveShownOnce;
                _progressiveShownOnce = true;

                RefreshFromDisk(progressive);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>完整走一遍：扫描 → 写盘 → 渲染。</summary>
        private void RefreshFromDisk(bool progressive)
        {
            _currentConfig = ScanAndBuildConfig();
            SaveConfig(_currentConfig);
            RenderCharacters(_currentConfig, progressive);
        }

        // ============ 配置读写 ============

        private void SubtitleOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoadingSettings) return;
            ConfigSync.Mutate<VoiceSettings>(v => v.SubtitleOpacity = (int)e.NewValue);
        }

        private void SortFieldSelect_SelectionChanged(object? sender, int idx)
        {
            if (_isLoadingSettings) return;
            ConfigSync.Mutate<VoiceSettings>(v => v.SortField = idx);

            if (_currentConfig != null)
            {
                RenderCharacters(_currentConfig);
                SaveConfig(_currentConfig);   // 界面顺序变了，json 的键顺序跟着更新
            }
        }

        private void SortOrderSelect_SelectionChanged(object? sender, int idx)
        {
            if (_isLoadingSettings) return;
            ConfigSync.Mutate<VoiceSettings>(v => v.SortOrder = idx);

            if (_currentConfig != null)
            {
                RenderCharacters(_currentConfig);
                SaveConfig(_currentConfig);   // 同上
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 搜索防抖：连续输入只在停顿 200ms 后过滤一次
            _searchTimer ??= new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _searchTimer.Tick -= SearchTimer_Tick;
            _searchTimer.Tick += SearchTimer_Tick;

            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer?.Stop();
            if (_currentConfig != null)
                RenderCharacters(_currentConfig);
        }

        /// <summary>
        /// 刷新 = 不重启重新加载：重扫 Pictures\*.png 与 Voices\*.wav，重写 voice_config.json 并刷新界面。
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // 强制重扫：头像与渲染结果缓存一并清空（用户可能刚放进或替换了图片 / 音频）
            _avatarCache.Clear();
            _jsonKeyOrder = null;      // json 顺序可能变了（新增角色），重新读取
            ClearContentCache();

            RefreshFromDisk(progressive: false);

            // 给出反馈，避免"点了没反应"的错觉
            var (matched, total, wavCount) = _currentConfig is { } cfg ? CalcStats(cfg) : (0, 0, 0);

            if (Application.Current?.MainWindow is UI.MainWindow mw)
                mw.ShowToast($"已重新加载：角色 {matched}/{total}，音频 {wavCount}", true);
        }
        // ============ 扫描 + 生成 ============

        private VoiceConfig ScanAndBuildConfig(bool force = false)
        {
            // 目录没变 + 非强制刷新 → 直接复用上次扫描结果
            var voiceStamp = GetDirStamp(VoiceDir);
            var voicesStamp = GetDirStamp(VoicesDir);
            var picturesStamp = GetDirStamp(PicturesDir);

            if (!force && _scanCache is { } cache
                && cache.voiceStamp == voiceStamp
                && cache.voicesStamp == voicesStamp
                && cache.picturesStamp == picturesStamp)
            {
                _totalSizeBytes = cache.totalBytes;
                return cache.cfg;
            }

            _totalSizeBytes = 0;

            // 整个 Voice/ 文件夹（含子文件夹）的大小
            if (Directory.Exists(VoiceDir))
            {
                try
                {
                    foreach (var f in Directory.GetFiles(VoiceDir, "*", SearchOption.AllDirectories))
                    {
                        try { _totalSizeBytes += new FileInfo(f).Length; } catch { }
                    }
                }
                catch { }
            }

            var cfg = new VoiceConfig();

            var avatarSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(PicturesDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(PicturesDir, "*.png"))
                        avatarSet.Add(Path.GetFileNameWithoutExtension(file));
                }
                catch { }
            }

            var roleMap = new Dictionary<string, List<(string fileBase, string dur, string text)>>(StringComparer.OrdinalIgnoreCase);
            var noRoleList = new List<(string fileBase, string dur, string text)>();

            // json 里已有的文本（用户可能编辑过标点）。重扫时优先沿用，否则会把编辑结果覆盖回文件名推导出的文本。
            var existingTexts = LoadExistingVoiceTexts();

            if (Directory.Exists(VoicesDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(VoicesDir, "*.wav"))
                    {
                        string fileBase = Path.GetFileNameWithoutExtension(file);
                        string duration = GetWavDuration(file);

                        existingTexts.TryGetValue(fileBase, out string? savedText);
                        bool hasSaved = !string.IsNullOrEmpty(savedText);

                        int dashIndex = fileBase.IndexOf('-');
                        if (dashIndex < 0) dashIndex = fileBase.IndexOf('－');

                        if (dashIndex <= 0 || dashIndex >= fileBase.Length - 1)
                        {
                            noRoleList.Add((fileBase, duration, hasSaved ? savedText! : fileBase));
                            continue;
                        }

                        string roleName = fileBase.Substring(0, dashIndex).Trim();
                        string voiceText = hasSaved ? savedText! : fileBase.Substring(dashIndex + 1).Trim();

                        if (string.IsNullOrEmpty(roleName) || string.IsNullOrEmpty(voiceText))
                        {
                            noRoleList.Add((fileBase, duration, hasSaved ? savedText! : fileBase));
                            continue;
                        }

                        if (!roleMap.TryGetValue(roleName, out var list))
                        {
                            list = new List<(string, string, string)>();
                            roleMap[roleName] = list;
                        }
                        list.Add((fileBase, duration, voiceText));
                    }
                }
                catch { }
            }

            var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in avatarSet) allNames.Add(k);
            foreach (var k in roleMap.Keys) allNames.Add(k);
            allNames.Remove(NoRoleKey);

            foreach (var name in allNames)
            {
                bool hasAvatar = avatarSet.Contains(name);
                bool hasVoices = roleMap.ContainsKey(name);

                var cc = new CharacterConfig();
                cc.Matched = hasAvatar && hasVoices;

                if (hasVoices)
                {
                    foreach (var v in roleMap[name])
                    {
                        cc.Wavs[v.fileBase] = $"{v.dur}-{v.text}";
                    }
                }

                cfg[name] = cc;
            }

            if (noRoleList.Count > 0)
            {
                var cc = new CharacterConfig { Matched = false };
                foreach (var v in noRoleList)
                {
                    cc.Wavs[v.fileBase] = $"{v.dur}-{v.text}";
                }
                cfg[NoRoleKey] = cc;
            }

            _scanCache = (cfg, _totalSizeBytes, voiceStamp, voicesStamp, picturesStamp);
            return cfg;
        }

        private static DateTime GetDirStamp(string dir)
            => Directory.Exists(dir) ? Directory.GetLastWriteTimeUtc(dir) : DateTime.MinValue;

        private static void SaveConfig(VoiceConfig cfg)
        {
            try
            {
                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions
                {
                    Indented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }))
                {
                    writer.WriteStartObject();

                    // 按界面显示顺序写出（空 → 荧 → 其余按排序规则 → 未分类），方便和界面对照
                    foreach (var key in GetDisplayOrder(cfg))
                    {
                        var cc = cfg[key];

                        writer.WritePropertyName(key);
                        writer.WriteStartObject();

                        foreach (var w in cc.Wavs)
                            writer.WriteString(w.Key, w.Value);

                        writer.WriteBoolean("matched", cc.Matched);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }

                string json = Encoding.UTF8.GetString(ms.ToArray());

                string? dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 内容没变就不写盘
                if (File.Exists(ConfigPath) && File.ReadAllText(ConfigPath) == json)
                    return;

                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        /// <summary>
        /// 读取 voice_config.json 里各音频的文本部分
        /// （配置值形如 "00:00:02-这个世界也在回应我吗。"，取 "-" 之后的内容）。
        /// 重新扫描时优先沿用它，这样用户编辑过的标点不会被打回文件名推导出的文本。
        /// </summary>
        private static Dictionary<string, string> LoadExistingVoiceTexts()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(ConfigPath)) return map;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));

                foreach (var role in doc.RootElement.EnumerateObject())
                {
                    if (role.Value.ValueKind != JsonValueKind.Object) continue;

                    foreach (var wav in role.Value.EnumerateObject())
                    {
                        // 只处理字符串项，自动跳过 matched 之类的非音频字段
                        if (wav.Value.ValueKind != JsonValueKind.String) continue;

                        string value = wav.Value.GetString() ?? "";
                        int dash = value.IndexOf('-');
                        if (dash < 0 || dash >= value.Length - 1) continue;

                        map[wav.Name] = value.Substring(dash + 1);
                    }
                }
            }
            catch
            {
                // json 损坏时忽略，退回按文件名推导
            }

            return map;
        }

        // ============ WAV 时长 ============

        private static string GetWavDuration(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                br.ReadBytes(4);
                br.ReadUInt32();
                br.ReadBytes(4);
                br.ReadBytes(4);
                br.ReadUInt32();
                br.ReadUInt16();
                br.ReadUInt16();
                br.ReadUInt32();
                uint byteRate = br.ReadUInt32();
                br.ReadUInt16();
                br.ReadUInt16();

                long dataSize = 0;
                while (fs.Position < fs.Length - 8)
                {
                    byte[] idBytes = br.ReadBytes(4);
                    if (idBytes.Length < 4) break;
                    string chunkId = Encoding.ASCII.GetString(idBytes);
                    uint chunkSize = br.ReadUInt32();
                    if (chunkId == "data") { dataSize = chunkSize; break; }
                    fs.Seek(chunkSize, SeekOrigin.Current);
                }

                if (byteRate == 0) return "00:00:00";

                double seconds = (double)dataSize / byteRate;
                var ts = TimeSpan.FromSeconds(seconds);
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
            catch
            {
                return "00:00:00";
            }
        }

        // ============ 渲染 ============

        private void RenderCharacters(VoiceConfig cfg, bool progressive = false)
        {
            CancelRender();

            string keyword = SearchBox?.Text?.Trim() ?? "";

            KongCard.Visibility = MatchKeyword("空", keyword) ? Visibility.Visible : Visibility.Collapsed;
            YingCard.Visibility = MatchKeyword("荧", keyword) ? Visibility.Visible : Visibility.Collapsed;

            // 清空旧内容（渐进加载时就是从空白开始逐张出现）
            KongTagPanel.Children.Clear();
            YingTagPanel.Children.Clear();
            KongCountText.Text = "0";
            YingCountText.Text = "0";
            DynamicCharacterPanel.Children.Clear();

            var items = BuildRenderList(cfg, keyword);

            if (items.Count == 0)
            {
                UpdateStats(cfg);
                return;
            }

            foreach (var item in items)
                _renderQueue.Enqueue(item);

            _renderConfig = cfg;
            _renderDone = 0;
            _renderTotal = items.Count;

            int token = ++_renderToken;

            if (progressive)
            {
                // ---------- 首次进入：慢速分批，能直接看到"正在加载"的过程 ----------
                // 卡片越多每批越多，这样无论多少角色总时长都差不多（约 30 批 × 30ms）
                _renderBatchSize = Math.Max(1, (int)Math.Ceiling(items.Count / (double)RenderTargetBatches));
                _renderUseTimer = true;

                _renderTimer ??= CreateRenderTimer();
                UpdateProgressStats(cfg);
                _renderTimer.Start();
            }
            else
            {
                // ---------- 再次进入 / 搜索 / 排序 / 刷新：快速分批 ----------
                // 每批之间回到消息循环让界面重绘，避免上百张卡片一次性创建造成的卡顿，
                // 但整体仍是"立刻出现"的手感。
                _renderBatchSize = FastRenderBatchSize;
                _renderUseTimer = false;
                RenderNextBatch(token);
            }
        }

        private System.Windows.Threading.DispatcherTimer CreateRenderTimer()
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RenderTickMs)
            };

            // 每拍都取当前代次号，避免拿着过期 token 被丢弃
            timer.Tick += (s, e) => RenderNextBatch(_renderToken);
            return timer;
        }

        /// <summary>渲染下一批卡片；队列清空后收尾（停表 + 显示完整统计）。</summary>
        private void RenderNextBatch(int token)
        {
            if (token != _renderToken) return;   // 过期的续拍回调，直接丢弃

            for (int i = 0; i < _renderBatchSize && _renderQueue.Count > 0; i++)
            {
                var (name, cc) = _renderQueue.Dequeue();
                RenderOne(name, cc);
                _renderDone++;
            }

            if (_renderQueue.Count > 0)
            {
                if (_renderUseTimer)
                {
                    if (_renderConfig != null) UpdateProgressStats(_renderConfig);
                    return;   // 下一拍交给 Timer
                }

                // 快速模式：回到消息循环后立刻继续，让界面有机会重绘（不冻结）
                Dispatcher.BeginInvoke(new Action(() => RenderNextBatch(token)),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            _renderTimer?.Stop();
            if (_renderConfig != null) UpdateStats(_renderConfig);
        }

        /// <summary>取消正在进行的渲染调度；返回 true 表示确实有未完成的渲染被打断。</summary>
        private bool CancelRender()
        {
            bool interrupted = _renderQueue.Count > 0;

            _renderToken++;        // 使在途的续拍回调失效
            _renderTimer?.Stop();
            _renderQueue.Clear();

            return interrupted;
        }

        /// <summary>渲染一个条目：空 / 荧 填进固定卡片，其余（含未分类 "?"）加进动态列表。</summary>
        private void RenderOne(string name, CharacterConfig cc)
        {
            if (name == "空") { FillTags(cc, KongTagPanel, KongCountText); return; }
            if (name == "荧") { FillTags(cc, YingTagPanel, YingCountText); return; }

            DynamicCharacterPanel.Children.Add(CreateCard(name, cc));
        }

        /// <summary>
        /// 构造渲染列表，顺序 = voice_config.json 里的键顺序
        /// （空 → 荧 → 其余按排序规则 → 未分类），json 里没有的键补在最后。
        /// 返回的 name 中，"?" 表示未分类卡片。
        /// </summary>
        private List<(string name, CharacterConfig cc)> BuildRenderList(VoiceConfig cfg, string keyword)
        {
            var list = new List<(string, CharacterConfig)>();

            foreach (string key in GetJsonKeyOrder(cfg))
            {
                if (!cfg.TryGetValue(key, out var cc)) continue;

                if (key == NoRoleKey)
                {
                    bool show = string.IsNullOrEmpty(keyword)
                        || cc.Wavs.Keys.Any(k => k.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (show) list.Add(("?", cc));
                    continue;
                }

                if (MatchKeyword(key, keyword))
                    list.Add((key, cc));
            }

            return list;
        }

        /// <summary>
        /// 读 voice_config.json 顶层的键顺序。渲染就按这个顺序显示，和文件里的排列一致
        /// （文件里的顺序正是 <see cref="SaveConfig"/> 按排序规则写出的）。
        /// json 缺失或写盘失败时，退回同一套排序规则，保证顺序仍然正确。
        /// 结果在页面内缓存，避免反复读盘解析。
        /// </summary>
        private List<string> GetJsonKeyOrder(VoiceConfig cfg)
        {
            if (_jsonKeyOrder != null) return _jsonKeyOrder;

            var order = new List<string>();

            try
            {
                if (File.Exists(ConfigPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (cfg.ContainsKey(prop.Name) && !order.Contains(prop.Name))
                                order.Add(prop.Name);
                        }
                    }
                }
            }
            catch { }

            // 保险：json 里没有的键（json 缺失 / 新增角色 / 写盘失败）补在最后。
            // 用界面排序规则补，这样即使拿不到 json，顺序也不会退化成字典的哈希顺序（随机）。
            foreach (var key in GetDisplayOrder(cfg))
            {
                if (!order.Contains(key))
                    order.Add(key);
            }

            _jsonKeyOrder = order;
            return order;
        }

        private void UpdateStats(VoiceConfig cfg)
        {
            var (matched, total, wavCount) = CalcStats(cfg);
            double mb = _totalSizeBytes / 1024.0 / 1024.0;

            StatsText.Text = $"角色数量：{matched}/{total}    音频数量：{wavCount}    大小：{mb:F0} MB";
        }

        /// <summary>统计角色数 / 音频数（界面统计与缓存共用）。</summary>
        private static (int matched, int total, int wavCount) CalcStats(VoiceConfig cfg)
            => (cfg.Values.Count(v => v.Matched), cfg.Count, cfg.Values.Sum(v => v.Wavs.Count));

        /// <summary>渐进加载过程中的进度显示（让"正在加载"一目了然）。</summary>
        private void UpdateProgressStats(VoiceConfig cfg)
        {
            int wavCount = cfg.Values.Sum(v => v.Wavs.Count);
            double mb = _totalSizeBytes / 1024.0 / 1024.0;

            StatsText.Text = $"正在加载 {_renderDone}/{_renderTotal}    音频数量：{wavCount}    大小：{mb:F0} MB";
        }

        // ============ 渲染结果缓存（再次进入页面时直接复用） ============

        /// <summary>
        /// 复用上次渲染好的控件（再次进入页面时调用）：直接塞回容器，
        /// 不扫描、不写盘、不重建。返回 false 表示没有可用缓存，需要走完整加载。
        /// </summary>
        private bool RestoreContentCache()
        {
            if (_cachedCards == null || _currentConfig == null) return false;

            KongCard.Visibility = Visibility.Visible;
            YingCard.Visibility = Visibility.Visible;

            KongTagPanel.Children.Clear();
            YingTagPanel.Children.Clear();
            DynamicCharacterPanel.Children.Clear();

            if (_cachedKongTags != null)
                foreach (var el in _cachedKongTags) KongTagPanel.Children.Add(el);

            if (_cachedYingTags != null)
                foreach (var el in _cachedYingTags) YingTagPanel.Children.Add(el);

            foreach (var el in _cachedCards)
                DynamicCharacterPanel.Children.Add(el);

            // 计数文字是页面上（XAML 里）的元素，不属于缓存控件，必须重新赋一次，
            // 否则会出现"标签都显示了、数字却是 0"。
            KongCountText.Text = GetRoleCountText(_currentConfig, "空");
            YingCountText.Text = GetRoleCountText(_currentConfig, "荧");

            if (_cachedStats is { } st)
            {
                _totalSizeBytes = st.sizeBytes;
                StatsText.Text = $"角色数量：{st.matched}/{st.total}    音频数量：{st.wavCount}    大小：{st.sizeBytes / 1024.0 / 1024.0:F0} MB";
            }

            return true;
        }

        /// <summary>取某个角色的音频数量文本（无该角色时为 "0"）。</summary>
        private static string GetRoleCountText(VoiceConfig cfg, string name)
            => cfg.TryGetValue(name, out var cc) ? cc.Wavs.Count.ToString() : "0";

        /// <summary>
        /// 离开页面时把渲染好的控件取出来缓存。
        /// Clear() 会解除父子关系，旧页面实例因此可以被回收（控件本身仍在缓存里活着）。
        /// </summary>
        private void SaveContentCache()
        {
            var cfg = _currentConfig;
            if (cfg == null) return;

            _cachedKongTags = GrabChildren(KongTagPanel);
            _cachedYingTags = GrabChildren(YingTagPanel);
            _cachedCards = GrabChildren(DynamicCharacterPanel);

            var (matched, total, wavCount) = CalcStats(cfg);
            _cachedStats = (matched, total, wavCount, _totalSizeBytes);
        }

        /// <summary>丢弃渲染结果缓存（下次进入页面走完整加载）。</summary>
        private static void ClearContentCache()
        {
            _cachedCards = null;
            _cachedKongTags = null;
            _cachedYingTags = null;
            _cachedStats = null;
        }

        /// <summary>取出容器里的子元素并清空容器（解除父子关系）。</summary>
        private static List<UIElement> GrabChildren(Panel panel)
        {
            var list = panel.Children.Cast<UIElement>().ToList();
            panel.Children.Clear();
            return list;
        }

        /// <summary>
        /// 按当前"排序字段 + 排序方向"给角色排序（匹配的优先）。
        /// 界面渲染与 voice_config.json 的写入顺序共用这一份规则，保证两者始终一致。
        /// </summary>
        private static List<KeyValuePair<string, CharacterConfig>> SortCharacters(
            IEnumerable<KeyValuePair<string, CharacterConfig>> src)
        {
            var voice = ConfigManager.Get<VoiceSettings>();
            int sortField = voice.SortField;
            int sortOrder = voice.SortOrder;

            // 按名称长度
            if (sortField == 1)
            {
                return sortOrder == 0
                    ? src.OrderByDescending(kv => kv.Value.Matched)
                         .ThenBy(kv => kv.Key.Length)
                         .ThenBy(kv => kv.Key, ZhComparer).ToList()
                    : src.OrderByDescending(kv => kv.Value.Matched)
                         .ThenByDescending(kv => kv.Key.Length)
                         .ThenBy(kv => kv.Key, ZhComparer).ToList();
            }

            // 按名称（倒序）
            if (sortField == 0 && sortOrder != 0)
            {
                return src.OrderByDescending(kv => kv.Value.Matched)
                          .ThenByDescending(kv => kv.Key, ZhComparer).ToList();
            }

            // 按名称（正序）/ 其它情况
            return src.OrderByDescending(kv => kv.Value.Matched)
                      .ThenBy(kv => kv.Key, ZhComparer).ToList();
        }

        /// <summary>
        /// 按界面显示顺序返回角色键：空 → 荧 →（其余按当前排序规则）→ 未分类。
        /// 这样写出的 voice_config.json 顺序和界面上看到的完全对应。
        /// </summary>
        private static List<string> GetDisplayOrder(VoiceConfig cfg)
        {
            var keys = new List<string>();

            // 界面上最前面两张是固定卡片
            if (cfg.ContainsKey("空")) keys.Add("空");
            if (cfg.ContainsKey("荧")) keys.Add("荧");

            var others = cfg.Where(kv => kv.Key != "空" && kv.Key != "荧" && kv.Key != NoRoleKey);
            keys.AddRange(SortCharacters(others).Select(kv => kv.Key));

            // 界面上"未分类"卡片排在最后
            if (cfg.ContainsKey(NoRoleKey)) keys.Add(NoRoleKey);

            // 保险：万一以后有别的来源的键，也别漏写
            foreach (var kv in cfg)
            {
                if (!keys.Contains(kv.Key))
                    keys.Add(kv.Key);
            }

            return keys;
        }

        private static bool MatchKeyword(string name, string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return true;
            return name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>把某个角色的音频标签填进固定卡片（空 / 荧）。</summary>
        private static void FillTags(CharacterConfig cc, WrapPanel tagPanel, TextBlock countText)
        {
            tagPanel.Children.Clear();
            countText.Text = cc.Wavs.Count.ToString();

            // Key = 音频文件名（不含扩展名），点击标签要按它找到 Voices 下的 wav
            foreach (var wav in cc.Wavs)
                tagPanel.Children.Add(CreateVoiceTag(wav.Key, ExtractVoiceText(wav.Value)));
        }

        /// <summary>
        /// 统一的角色卡片创建（原 CreateCharacterCard / CreateNoRoleCard 合并，
        /// 两者只差头像与名字）。名字传 "?" 即为"未分类"卡片。
        /// </summary>
        private static Border CreateCard(string name, CharacterConfig cc)
        {
            var card = new Border();
            card.SetResourceReference(Border.StyleProperty, "ModuleFunctionCardAutoStyle");

            var grid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avatar = LoadAvatar(name);

            var avatarBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 8, 0),
                Child = avatar != null
                    ? new Image
                    {
                        Source = avatar,
                        Width = 48,
                        Height = 48,
                        Stretch = Stretch.UniformToFill
                    }
                    : CreateQuestionMark()
            };
            avatarBorder.SetResourceReference(Border.BackgroundProperty, "Gray100Brush");
            Grid.SetColumn(avatarBorder, 0);
            grid.Children.Add(avatarBorder);

            var nameBlock = new TextBlock
            {
                Text = name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Grid.SetColumn(nameBlock, 1);
            grid.Children.Add(nameBlock);

            var countText = new TextBlock
            {
                Text = cc.Wavs.Count.ToString(),
                FontSize = 9,
                FontWeight = FontWeights.Bold
            };
            countText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var countBorder = new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Child = countText
            };
            countBorder.SetResourceReference(Border.BackgroundProperty, "Gray200Brush");
            Grid.SetColumn(countBorder, 2);
            grid.Children.Add(countBorder);

            var tagPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tagPanel, 3);

            foreach (var wav in cc.Wavs)
                tagPanel.Children.Add(CreateVoiceTag(wav.Key, ExtractVoiceText(wav.Value)));

            grid.Children.Add(tagPanel);
            card.Child = grid;
            return card;
        }

        /// <summary>加载头像（带静态缓存，Freeze 后可安全复用）。无头像返回 null。</summary>
        private static BitmapSource? LoadAvatar(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_avatarCache.TryGetValue(name, out var cached)) return cached;

            BitmapSource? src = null;
            string imgPath = Path.Combine(PicturesDir, name + ".png");
            if (File.Exists(imgPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    // 关键：告诉 WPF 不要用按 Uri 的图片缓存。
                    // 否则"替换同名 png"后即使清了 _avatarCache，刷新仍会显示旧图，
                    // 必须重启才更新。加上这个选项后，刷新即可读到磁盘上的最新图片。
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bmp.UriSource = new Uri(imgPath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    src = bmp;
                }
                catch { src = null; }
            }

            _avatarCache[name] = src;
            return src;
        }

        private static string ExtractVoiceText(string value)
        {
            int dash = value.IndexOf('-');
            if (dash < 0 || dash >= value.Length - 1) return value;
            return value.Substring(dash + 1);
        }

        private static FrameworkElement CreateQuestionMark()
        {
            var tb = new TextBlock
            {
                Text = "?",
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextDisabledBrush");
            return tb;
        }

        /// <summary>
        /// 创建一个音频标签（标签 + 编辑按钮）：
        /// 点击标签播放一次音频；点击编辑按钮改文本，提交后写回 voice_config.json。
        /// </summary>
        /// <remarks>
        /// 必须是 static：这些控件会被缓存并跨页面实例复用，
        /// 事件回调若捕获了某个页面实例，编辑保存就会写到旧实例上。
        /// </remarks>
        /// <param name="fileBase">音频文件名（不含扩展名），用于定位 wav 与配置项</param>
        /// <param name="content">标签显示的文字（配置值 "时长-" 之后的那部分）</param>
        private static VoiceTagItem CreateVoiceTag(string fileBase, string content)
        {
            var item = new VoiceTagItem
            {
                Text = content,
                TagColor = GetTagColor(content)
            };

            item.TagClicked += (s, e) => PlayVoice(fileBase);
            item.TextCommitted += (s, newText) => CommitVoiceText(fileBase, newText);

            return item;
        }

        /// <summary>
        /// 把编辑后的文本写回 voice_config.json。
        /// 配置值格式是 "时长-文本"（例如 "00:00:02-这个世界也在回应我吗。"），
        /// 这里只替换 "-" 之后的部分，时长保持不变。
        /// </summary>
        private static void CommitVoiceText(string fileBase, string newText)
        {
            var cfg = _currentConfig;
            if (cfg == null || string.IsNullOrEmpty(newText)) return;

            var cc = FindOwner(cfg, fileBase);
            if (cc == null || !cc.Wavs.TryGetValue(fileBase, out var oldValue)) return;

            int dash = oldValue.IndexOf('-');
            string newValue = (dash >= 0 ? oldValue.Substring(0, dash + 1) : "") + newText;

            if (string.Equals(newValue, oldValue, StringComparison.Ordinal)) return;

            cc.Wavs[fileBase] = newValue;
            SaveConfig(cfg);   // 内容变了才写盘
        }

        /// <summary>找到包含该音频的角色配置（音频文件名在整份配置里是唯一的）。</summary>
        private static CharacterConfig? FindOwner(VoiceConfig cfg, string fileBase)
        {
            foreach (var kv in cfg)
                if (kv.Value.Wavs.ContainsKey(fileBase))
                    return kv.Value;

            return null;
        }

        private static Brush GetTagColor(string content)
        {
            int hash = content.GetHashCode() & 0x7FFFFFFF;
            return TagBrushes[hash % TagBrushes.Length];
        }

        // ============ 音频播放 ============

        /// <summary>当前播放器。SoundPlayer 是异步播放的，必须保持引用，否则会被 GC 提前回收。</summary>
        private static System.Media.SoundPlayer? _voicePlayer;

        /// <summary>
        /// 播放一次指定音频。再次点击会先停掉上一个再播新的。
        /// </summary>
        private static void PlayVoice(string fileBase)
        {
            string path = Path.Combine(VoicesDir, fileBase + ".wav");
            if (!File.Exists(path)) return;

            try
            {
                _voicePlayer?.Stop();

                var player = new System.Media.SoundPlayer(path);
                player.Load();          // 先载入内存，播放期间不占用 wav 文件
                player.Play();          // 异步播放一次
                _voicePlayer = player;
            }
            catch
            {
                // 文件损坏 / 被占用等情况静默忽略，不影响界面
            }
        }

        // ============ JSON 数据结构 ============

        private class VoiceConfig : Dictionary<string, CharacterConfig> { }

        private class CharacterConfig
        {
            [JsonPropertyName("matched")]
            public bool Matched { get; set; }

            [JsonExtensionData]
            public Dictionary<string, JsonElement> Extras { get; set; } = new();

            [JsonIgnore]
            public Dictionary<string, string> Wavs { get; set; } = new();
        }


        private void OpenVoiceFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string voiceDir = Path.Combine(AppContext.BaseDirectory, "Voice");

                if (!Directory.Exists(voiceDir))
                {
                    Directory.CreateDirectory(voiceDir);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = voiceDir,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}