using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.UI.Helpers;

namespace LvchaxsBS.UI.Pages.FunctionConfigs
{
    public partial class AutoCookConfig : Page
    {
        private bool _isLoading = true;

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
        }

        private void AutoCookConfig_Loaded(object sender, RoutedEventArgs e)
        {
            var s = ConfigManager.Get<AutoCookSettings>();

            AutoCookIntervalSlider.Value = s.AutoCookInterval;
            DetectThresholdSpinBox.Value = s.DetectThreshold;
            NormalJudgePercentSlider.Value = s.NormalJudgePercent;
            PerfectJudgePercentSlider.Value = s.PerfectJudgePercent;
            NormalBinarizeToleranceSpinBox.Value = s.NormalBinarizeTolerance;
            PerfectBinarizeToleranceSpinBox.Value = s.PerfectBinarizeTolerance;

            MedicineTotalLimitSpinBox.Value = s.MedicineTotalLimit;
            MedicineIntervalSpinBox.Value = s.MedicineInterval_1;
            MedicineDetectThresholdSpinBox.Value = s.MedicineDetectThreshold;

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

            var s = ConfigManager.Get<AutoCookSettings>();
            s.AutoClearMedicineSelection = $"{type},{star},{item}";
            ConfigManager.Save(s);
        }

        private void AutoCookIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.AutoCookInterval = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void DetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.DetectThreshold = e.NewValue;
            ConfigManager.Save(s);
        }

        private void NormalJudgePercentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.NormalJudgePercent = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void PerfectJudgePercentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.PerfectJudgePercent = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void NormalBinarizeToleranceSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.NormalBinarizeTolerance = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void PerfectBinarizeToleranceSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.PerfectBinarizeTolerance = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void MedicineTotalLimitSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.MedicineTotalLimit = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void MedicineIntervalSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.MedicineInterval_1 = (int)e.NewValue;
            ConfigManager.Save(s);
        }

        private void MedicineDetectThresholdSpinBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = ConfigManager.Get<AutoCookSettings>();
            s.MedicineDetectThreshold = e.NewValue;
            ConfigManager.Save(s);
        }
    }
}