using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Core;
using LvchaxsBS.UI.Controls;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class AutoCookConfig : Page
    {
        private bool _isLoading = true;
        private StatusTagPanel? _match;

        private static readonly string[] MedicineTypes = { "复活药", "体力药" };

        private static readonly (string Type, string Star, string Name)[] MedicineItems =
        {
            ("复活药", "一星料理", "提瓦特煎蛋"),
            ("复活药", "一星料理", "烤肉排"),
            ("复活药", "一星料理", "蒙德烤鱼"),
            ("复活药", "一星料理", "摩拉肉"),
            ("复活药", "一星料理", "爆炒肉片"),
            ("复活药", "一星料理", "鸟蛋烧"),
            ("复活药", "二星料理", "庄园烤松饼"),
            ("复活药", "二星料理", "素鲍鱼"),
            ("复活药", "二星料理", "蟹黄豆腐"),
            ("复活药", "二星料理", "什锦炒面"),
            ("复活药", "二星料理", "乌冬面"),
            ("复活药", "二星料理", "绿汁脆球"),
            ("复活药", "二星料理", "脆饼珐提"),
            ("复活药", "二星料理", "多彩之森"),
            ("复活药", "三星料理", "蟹黄火腿焗时蔬"),
            ("复活药", "三星料理", "绯樱饼"),
            ("复活药", "三星料理", "椰炭饼"),
            ("复活药", "三星料理", "塔塔可"),
            ("复活药", "三星料理", "夏槲蛋糕"),
            ("复活药", "三星料理", "薄荷泡泡糖"),

            ("体力药", "二星料理", "北地烟熏鸡"),
            ("体力药", "二星料理", "山珍热卤面"),
            ("体力药", "二星料理", "阿如拌饭"),
            ("体力药", "二星料理", "桔香鸭胸肉"),
            ("体力药", "二星料理", "清心花饼"),
            ("体力药", "三星料理", "炸鱼薯条"),
            ("体力药", "三星料理", "巧克力"),
            ("体力药", "三星料理", "钱汤馒头"),
            ("体力药", "三星料理", "白灵果派"),
            ("体力药", "三星料理", "肉末酿豆腐"),
        };

        public AutoCookConfig()
        {
            InitializeComponent();
            Loaded += AutoCookConfig_Loaded;
            Unloaded += AutoCookConfig_Unloaded;
        }

        private void AutoCookConfig_Unloaded(object sender, RoutedEventArgs e)
        {
            AutoCookLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
        }

        private void OnDetectionResultUpdated(double matchScore, long elapsedMs, double threshold, string type)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                string? filteredType = (type == "未检测到烹饪") ? null : type;
                _match?.SetResult(matchScore, elapsedMs, threshold, filteredType);
            });
        }

        private void AutoCookConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<AutoCookSettings>();

            AutoCookIntervalCard.Value = s.AutoCookInterval;
            AutoCookIntervalCard.SpinValue = s.DetectThreshold;
            NormalJudgeCard.Value = s.NormalJudgePercent;
            NormalJudgeCard.SpinValue = s.NormalBinarizeTolerance;
            PerfectJudgeCard.Value = s.PerfectJudgePercent;
            PerfectJudgeCard.SpinValue = s.PerfectBinarizeTolerance;

            MedicineTotalLimitSpinBox.Value = s.MedicineTotalLimit;
            MedicineIntervalSpinBox.Value = s.MedicineInterval_1;
            MedicineDetectThresholdSpinBox.Value = s.MedicineDetectThreshold;
            MedicineScaleFactorSpinBox.Value = Math.Clamp(s.MedicineScaleFactor, 1, 5);

            MedicineTypeSelect.ItemsSource = MedicineTypes;

            var parts = (s.AutoClearMedicineSelection ?? "").Split(',');
            string savedType = parts.Length > 0 ? parts[0].Trim() : "复活药";
            string savedStar = parts.Length > 1 ? parts[1].Trim() : "一星料理";
            string savedItem = parts.Length > 2 ? parts[2].Trim() : "未选择";

            int typeIdx = Array.IndexOf(MedicineTypes, savedType);
            if (typeIdx < 0) typeIdx = 0;
            MedicineTypeSelect.SelectedIndex = typeIdx;

            UpdateStarLevels(typeIdx, savedStar);
            UpdateItemList(typeIdx, StarLevelSelect.SelectedIndex, savedItem);

            MedicineTypeSelect.SelectionChanged += MedicineTypeSelect_Changed;
            StarLevelSelect.SelectionChanged += StarLevelSelect_Changed;
            ItemSelect.SelectionChanged += ItemSelect_Changed;

            _isLoading = false;

            _match = AutoCookIntervalCard.GetTitleTag(0);

            // 订阅运行状态（先解绑再绑定，避免重复导航时重复订阅）
            AutoCookLogic.DetectionResultUpdated -= OnDetectionResultUpdated;
            AutoCookLogic.DetectionResultUpdated += OnDetectionResultUpdated;
            _match?.SetEmpty();

            SliderEntryAnimator.PlayAll(this);
        }

        private void UpdateStarLevels(int medicineTypeIdx, string? selectStar = null)
        {
            string type = MedicineTypes[medicineTypeIdx];

            var stars = MedicineItems
                .Where(x => x.Type == type)
                .Select(x => x.Star)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            StarLevelSelect.ItemsSource = stars;

            int idx = 0;
            if (selectStar != null)
            {
                int i = stars.IndexOf(selectStar);
                if (i >= 0) idx = i;
            }
            StarLevelSelect.SelectedIndex = idx;
        }

        private void UpdateItemList(int medicineTypeIdx, int starIdx, string? selectItem = null)
        {
            string type = MedicineTypes[medicineTypeIdx];

            var stars = (StarLevelSelect.ItemsSource as List<string>) ?? new List<string>();
            if (starIdx < 0 || starIdx >= stars.Count)
            {
                ItemSelect.ItemsSource = new List<string> { "未选择" };
                ItemSelect.SelectedIndex = 0;
                return;
            }
            string star = stars[starIdx];

            var items = MedicineItems
                .Where(x => x.Type == type && x.Star == star)
                .Select(x => x.Name)
                .ToList();

            items.Insert(0, "未选择");

            ItemSelect.ItemsSource = items;

            int idx = 0;
            if (selectItem != null)
            {
                int i = items.IndexOf(selectItem);
                if (i >= 0) idx = i;
            }
            ItemSelect.SelectedIndex = idx;
        }

        private void MedicineTypeSelect_Changed(object? sender, int idx)
        {
            if (_isLoading) return;
            UpdateStarLevels(idx);
            UpdateItemList(idx, StarLevelSelect.SelectedIndex);
            SaveSelection();
        }

        private void StarLevelSelect_Changed(object? sender, int idx)
        {
            if (_isLoading) return;
            UpdateItemList(MedicineTypeSelect.SelectedIndex, idx);
            SaveSelection();
        }

        private void ItemSelect_Changed(object? sender, int idx)
        {
            if (_isLoading) return;
            SaveSelection();
        }

        private void SaveSelection()
        {
            string type = MedicineTypes[MedicineTypeSelect.SelectedIndex];
            var stars = (StarLevelSelect.ItemsSource as List<string>) ?? new List<string>();
            string star = (StarLevelSelect.SelectedIndex >= 0 && StarLevelSelect.SelectedIndex < stars.Count)
                ? stars[StarLevelSelect.SelectedIndex] : "";
            var items = (ItemSelect.ItemsSource as List<string>) ?? new List<string>();
            string item = (ItemSelect.SelectedIndex >= 0 && ItemSelect.SelectedIndex < items.Count)
                ? items[ItemSelect.SelectedIndex] : "未选择";

            ConfigSync.Mutate<AutoCookSettings>(s => s.AutoClearMedicineSelection = $"{type},{star},{item}");
        }

        private void AutoCookIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.AutoCookInterval = (int)e.NewValue);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.DetectThreshold = e.NewValue);
        }

        private void NormalJudgePercentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.NormalJudgePercent = (int)e.NewValue);
        }

        private void PerfectJudgePercentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.PerfectJudgePercent = (int)e.NewValue);
        }

        private void NormalBinarizeToleranceSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.NormalBinarizeTolerance = (int)e.NewValue);
        }

        private void PerfectBinarizeToleranceSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.PerfectBinarizeTolerance = (int)e.NewValue);
        }

        private void MedicineTotalLimitSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.MedicineTotalLimit = (int)e.NewValue);
        }

        private void MedicineIntervalSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.MedicineInterval_1 = (int)e.NewValue);
        }

        private void MedicineDetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            ConfigSync.Mutate<AutoCookSettings>(s => s.MedicineDetectThreshold = e.NewValue);
        }

        private void MedicineScaleFactorSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;

            int factor = (int)Math.Round(e.NewValue);
            if (factor < 1) factor = 1;
            else if (factor > 5) factor = 5;

            ConfigSync.Mutate<AutoCookSettings>(s => s.MedicineScaleFactor = factor);
        }
    }
}