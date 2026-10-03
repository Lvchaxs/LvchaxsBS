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
using LvchaxsBS.Config;
using LvchaxsBS.UI.Helpers;

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

        private VoiceConfig? _cachedConfig;
        private bool _isLoadingSettings = false;
        private long _totalSizeBytes = 0;

        // ===== 静态缓存（跨页面实例复用，避免每次进页都扫盘/重载头像） =====

        /// <summary>扫描结果缓存：目录没变就不重扫</summary>
        private static (VoiceConfig cfg, long totalBytes, DateTime voiceStamp, DateTime voicesStamp, DateTime picturesStamp)? _scanCache;

        /// <summary>头像图片缓存（已 Freeze，可跨线程/跨实例安全复用）</summary>
        private static readonly Dictionary<string, BitmapSource?> _avatarCache = new();

        /// <summary>标签颜色缓存（预生成冻结画刷，避免每个标签都 new Brush）</summary>
        private static readonly Brush[] TagBrushes = CreateTagBrushes();

        /// <summary>搜索防抖定时器</summary>
        private System.Windows.Threading.DispatcherTimer? _searchTimer;

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

            _cachedConfig = ScanAndBuildConfig();
            SaveConfig(_cachedConfig);
            RenderCharacters(_cachedConfig);
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

            if (_cachedConfig != null)
                RenderCharacters(_cachedConfig);
        }

        private void SortOrderSelect_SelectionChanged(object? sender, int idx)
        {
            if (_isLoadingSettings) return;
            ConfigSync.Mutate<VoiceSettings>(v => v.SortOrder = idx);

            if (_cachedConfig != null)
                RenderCharacters(_cachedConfig);
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
            if (_cachedConfig != null)
                RenderCharacters(_cachedConfig);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // 强制重扫：头像缓存一并清空（用户可能刚放进新图片）
            _avatarCache.Clear();
            _cachedConfig = ScanAndBuildConfig(force: true);
            SaveConfig(_cachedConfig);
            RenderCharacters(_cachedConfig);
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

            if (Directory.Exists(VoicesDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(VoicesDir, "*.wav"))
                    {
                        string fileBase = Path.GetFileNameWithoutExtension(file);
                        string duration = GetWavDuration(file);

                        int dashIndex = fileBase.IndexOf('-');
                        if (dashIndex < 0) dashIndex = fileBase.IndexOf('－');

                        if (dashIndex <= 0 || dashIndex >= fileBase.Length - 1)
                        {
                            noRoleList.Add((fileBase, duration, fileBase));
                            continue;
                        }

                        string roleName = fileBase.Substring(0, dashIndex).Trim();
                        string voiceText = fileBase.Substring(dashIndex + 1).Trim();

                        if (string.IsNullOrEmpty(roleName) || string.IsNullOrEmpty(voiceText))
                        {
                            noRoleList.Add((fileBase, duration, fileBase));
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
                    foreach (var kv in cfg)
                    {
                        writer.WritePropertyName(kv.Key);
                        writer.WriteStartObject();

                        foreach (var w in kv.Value.Wavs)
                            writer.WriteString(w.Key, w.Value);

                        writer.WriteBoolean("matched", kv.Value.Matched);
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

        private void RenderCharacters(VoiceConfig cfg)
        {
            string keyword = SearchBox?.Text?.Trim() ?? "";

            KongCard.Visibility = MatchKeyword("空", keyword) ? Visibility.Visible : Visibility.Collapsed;
            YingCard.Visibility = MatchKeyword("荧", keyword) ? Visibility.Visible : Visibility.Collapsed;

            FillDefaultCharacter(cfg, "空", KongTagPanel, KongCountText);
            FillDefaultCharacter(cfg, "荧", YingTagPanel, YingCountText);

            DynamicCharacterPanel.Children.Clear();

            var voice = ConfigManager.Get<VoiceSettings>();
            int sortField = voice.SortField;
            int sortOrder = voice.SortOrder;

            var others = cfg
                .Where(kv => kv.Key != "空" && kv.Key != "荧" && kv.Key != NoRoleKey)
                .ToList();

            if (!string.IsNullOrEmpty(keyword))
            {
                others = others
                    .Where(kv => kv.Key.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            switch (sortField)
            {
                case 0:
                    others = sortOrder == 0
                        ? others.OrderByDescending(kv => kv.Value.Matched)
                                .ThenBy(kv => kv.Key, ZhComparer).ToList()
                        : others.OrderByDescending(kv => kv.Value.Matched)
                                .ThenByDescending(kv => kv.Key, ZhComparer).ToList();
                    break;

                case 1:
                    others = sortOrder == 0
                        ? others.OrderByDescending(kv => kv.Value.Matched)
                                .ThenBy(kv => kv.Key.Length)
                                .ThenBy(kv => kv.Key, ZhComparer).ToList()
                        : others.OrderByDescending(kv => kv.Value.Matched)
                                .ThenByDescending(kv => kv.Key.Length)
                                .ThenBy(kv => kv.Key, ZhComparer).ToList();
                    break;

                default:
                    others = others.OrderByDescending(kv => kv.Value.Matched)
                            .ThenBy(kv => kv.Key, ZhComparer).ToList();
                    break;
            }

            foreach (var kv in others)
                DynamicCharacterPanel.Children.Add(CreateCard(kv.Key, kv.Value));

            if (cfg.TryGetValue(NoRoleKey, out var noRoleCc))
            {
                bool showNoRole = string.IsNullOrEmpty(keyword)
                    || noRoleCc.Wavs.Keys.Any(k => k.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

                if (showNoRole)
                    DynamicCharacterPanel.Children.Add(CreateCard("?", noRoleCc));
            }

            UpdateStats(cfg);
        }

        private void UpdateStats(VoiceConfig cfg)
        {
            int matched = cfg.Values.Count(v => v.Matched);
            int total = cfg.Count;
            int wavCount = cfg.Values.Sum(v => v.Wavs.Count);
            double mb = _totalSizeBytes / 1024.0 / 1024.0;

            StatsText.Text = $"角色数量：{matched}/{total}    音频数量：{wavCount}    大小：{mb:F0} MB";
        }

        private static bool MatchKeyword(string name, string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return true;
            return name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> GetWavValues(CharacterConfig cc)
        {
            return cc.Wavs.Values.ToList();
        }

        private void FillDefaultCharacter(VoiceConfig cfg, string name, WrapPanel tagPanel, TextBlock countText)
        {
            tagPanel.Children.Clear();

            if (!cfg.TryGetValue(name, out var cc))
            {
                countText.Text = "0";
                return;
            }

            var values = GetWavValues(cc);
            countText.Text = values.Count.ToString();

            foreach (var v in values)
            {
                string content = ExtractVoiceText(v);
                tagPanel.Children.Add(CreateVoiceTag(content));
            }
        }

        /// <summary>
        /// 统一的角色卡片创建（原 CreateCharacterCard / CreateNoRoleCard 合并，
        /// 两者只差头像与名字）。名字传 "?" 即为"未分类"卡片。
        /// </summary>
        private Border CreateCard(string name, CharacterConfig cc)
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

            var values = GetWavValues(cc);

            var countText = new TextBlock
            {
                Text = values.Count.ToString(),
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

            foreach (var v in values)
            {
                string content = ExtractVoiceText(v);
                tagPanel.Children.Add(CreateVoiceTag(content));
            }

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

        private FrameworkElement CreateQuestionMark()
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

        private Button CreateVoiceTag(string content)
        {
            var btn = new Button
            {
                Content = content,
                Margin = new Thickness(0, 0, 4, 2)
            };
            btn.SetResourceReference(FrameworkElement.StyleProperty, "VoiceTagButtonStyle");
            btn.Background = GetTagColor(content);
            return btn;
        }

        private Brush GetTagColor(string content)
        {
            int hash = content.GetHashCode() & 0x7FFFFFFF;
            return TagBrushes[hash % TagBrushes.Length];
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