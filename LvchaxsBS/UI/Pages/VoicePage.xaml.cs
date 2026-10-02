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

        public VoicePage()
        {
            InitializeComponent();
            Loaded += VoicePage_Loaded;
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

            var voice = ConfigManager.Get<VoiceSettings>();
            voice.SubtitleOpacity = (int)e.NewValue;
            ConfigManager.Save(voice);
        }

        private void SortFieldSelect_SelectionChanged(object? sender, int idx)
        {
            if (_isLoadingSettings) return;

            var voice = ConfigManager.Get<VoiceSettings>();
            voice.SortField = idx;
            ConfigManager.Save(voice);

            if (_cachedConfig != null)
                RenderCharacters(_cachedConfig);
        }

        private void SortOrderSelect_SelectionChanged(object? sender, int idx)
        {
            if (_isLoadingSettings) return;

            var voice = ConfigManager.Get<VoiceSettings>();
            voice.SortOrder = idx;
            ConfigManager.Save(voice);

            if (_cachedConfig != null)
                RenderCharacters(_cachedConfig);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_cachedConfig != null)
                RenderCharacters(_cachedConfig);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _cachedConfig = ScanAndBuildConfig();
            SaveConfig(_cachedConfig);
            RenderCharacters(_cachedConfig);
        }
        // ============ 扫描 + 生成 ============

        private VoiceConfig ScanAndBuildConfig()
        {
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

            return cfg;
        }

        private static void SaveConfig(VoiceConfig cfg)
        {
            try
            {
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var stream = File.Create(ConfigPath);
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
                {
                    Indented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

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
            {
                var card = CreateCharacterCard(kv.Key, kv.Value);
                if (card != null)
                    DynamicCharacterPanel.Children.Add(card);
            }

            if (cfg.TryGetValue(NoRoleKey, out var noRoleCc))
            {
                bool showNoRole = string.IsNullOrEmpty(keyword)
                    || noRoleCc.Wavs.Keys.Any(k => k.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

                if (showNoRole)
                {
                    var card = CreateNoRoleCard(noRoleCc);
                    if (card != null)
                        DynamicCharacterPanel.Children.Add(card);
                }
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

        private static bool HasAvatar(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return File.Exists(Path.Combine(PicturesDir, name + ".png"));
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

        private Border? CreateCharacterCard(string name, CharacterConfig cc)
        {
            var card = new Border();
            card.SetResourceReference(Border.StyleProperty, "ModuleFunctionCardAutoStyle");

            var grid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            bool hasAvatar = HasAvatar(name);

            var avatarBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            avatarBorder.SetResourceReference(Border.BackgroundProperty, "Gray100Brush");

            if (hasAvatar)
            {
                string imgPath = Path.Combine(PicturesDir, name + ".png");
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(imgPath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    avatarBorder.Child = new Image
                    {
                        Source = bmp,
                        Width = 48,
                        Height = 48,
                        Stretch = Stretch.UniformToFill
                    };
                }
                catch { avatarBorder.Child = CreateQuestionMark(); }
            }
            else
            {
                avatarBorder.Child = CreateQuestionMark();
            }

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

        private Border? CreateNoRoleCard(CharacterConfig cc)
        {
            var card = new Border();
            card.SetResourceReference(Border.StyleProperty, "ModuleFunctionCardAutoStyle");

            var grid = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avatarBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 8, 0),
                Child = CreateQuestionMark()
            };
            avatarBorder.SetResourceReference(Border.BackgroundProperty, "Gray100Brush");
            Grid.SetColumn(avatarBorder, 0);
            grid.Children.Add(avatarBorder);

            var nameBlock = new TextBlock
            {
                Text = "?",
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
            string[] colors = { "#60A5FA", "#34D399", "#F87171", "#FB923C", "#A78BFA", "#F472B6", "#2DD4BF", "#818CF8", "#22D3EE" };
            int hash = content.GetHashCode() & 0x7FFFFFFF;
            return (Brush)new BrushConverter().ConvertFromString(colors[hash % colors.Length])!;
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