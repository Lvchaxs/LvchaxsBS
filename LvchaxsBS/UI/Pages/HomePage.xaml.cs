using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LvchaxsBS.Config;
using LvchaxsBS.Core;
using LvchaxsBS.Services;

namespace LvchaxsBS.UI.Pages
{
    public partial class HomePage : Page
    {
        // 防止"加载时"触发 Toggle_Changed
        // （容器生成/绑定初始化会经过布局阶段，故延后到 ContextIdle 再放开）
        private bool _isLoading = false;

        public HomePage()
        {
            InitializeComponent();
            Loaded += HomePage_Loaded;
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;

            var s = ConfigManager.Get<HomePageSettings>();
            ModuleList.ItemsSource = ModuleRegistry.All.Select(m => new ModuleCardItem(m, s)).ToList();

            // 手柄拾取是常驻监听（不走触发键通道），需要在进入主页时同步启停
            ControllerPickupLogic.SyncWithSettings();

            // 等容器生成 + 绑定初始化全部跑完，再应用总开关视觉并放开事件
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyMasterSwitchVisual(s.MasterSwitch);
                _isLoading = false;
            }), DispatcherPriority.ContextIdle);
        }

        // ============ 总开关视觉 ============

        /// <summary>
        /// 总开关关时，所有功能胶囊开关视觉置灰（配置值不变）
        /// </summary>
        private void ApplyMasterSwitchVisual(bool masterOn)
        {
            foreach (var cb in FindVisualChildren<CheckBox>(ModuleList))
                cb.Opacity = masterOn ? 1.0 : 0.4;
        }

        /// <summary>供 MainWindow 在总开关切换后调用，刷新本页开关视觉</summary>
        public void RefreshMasterSwitchVisual()
        {
            var s = ConfigManager.Get<HomePageSettings>();
            ApplyMasterSwitchVisual(s.MasterSwitch);
        }

        // ============ 开关切换 → 保存 ============

        private void Toggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox cb || cb.DataContext is not ModuleCardItem item) return;

            bool isChecked = cb.IsChecked == true;
            var settings = ConfigManager.Get<HomePageSettings>();
            item.Module.SetEnabled(settings, isChecked);
            ConfigManager.Save(settings);

            // 手柄拾取需要跟随开关即时启停监听
            ControllerPickupLogic.SyncWithSettings();

            if (Application.Current.MainWindow is UI.MainWindow mw)
                mw.ShowToast($"{item.Module.Name}: {(isChecked ? "已启用" : "已关闭")}", isChecked);
        }

        // ============ 按钮点击 ============

        private void TriggerKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not ModuleCardItem item) return;
            NavigateToModule(item.Module.Name, showTriggerKey: true);
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not ModuleCardItem item) return;
            NavigateToModule(item.Module.Name, showTriggerKey: false);
        }

        /// <summary>
        /// 导航到指定功能的配置容器页（可直跳触发键页）。
        /// 供各处复用：首页卡片、触发键页的"点击占用键跳转"。
        /// </summary>
        public static void NavigateToModule(string moduleName, bool showTriggerKey)
        {
            var container = CreateContainerFor(moduleName);
            if (container == null) return;

            if (showTriggerKey) container.ShowTriggerKeyPage();
            else container.ShowConfigPage();

            (Application.Current.MainWindow as UI.MainWindow)?.MainFrame.Navigate(container);
        }

        // ============ 创建容器 ============

        public static ConfigContainerPage? CreateContainerFor(string moduleName)
        {
            var module = ModuleRegistry.ByName(moduleName);
            if (module == null) return null;

            var container = new ConfigContainerPage();
            container.SetSourceModule(module.Name,
                module.Description,
                module.IconPath,
                module.CreateTags?.Invoke());
            container.SetTriggerKeyPage(
                module.UseGamepad ? new GamepadTriggerKeyPage() : new TriggerKeyPage(),
                module.KeyTag);
            container.SetConfigPage(module.CreateConfigPage());

            return container;
        }

        // ============ 工具 ============

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var c in FindVisualChildren<T>(child))
                    yield return c;
            }
        }
    }

    /// <summary>
    /// 主页模块卡片的数据项：模块 + 开关状态 + 触发键显示。
    /// 卡片外观由 HomePage.xaml 的 DataTemplate 呈现。
    /// </summary>
    public class ModuleCardItem
    {
        public ModuleCardItem(ModuleInfo module, HomePageSettings s)
        {
            Module = module;
            IsChecked = module.IsEnabled(s);

            string key = module.GetKey(s);
            HasKey = !string.IsNullOrEmpty(key) && key != "未设置";
            TriggerKeyText = HasKey ? key : "触发键";
            TriggerKeyTooltip = $"当前设置：{(HasKey ? key : "未设置")}";

            ShowTags = module.CreateTags != null;
        }

        public ModuleInfo Module { get; }
        public bool IsChecked { get; set; }
        public bool HasKey { get; }
        public string TriggerKeyText { get; }
        public string TriggerKeyTooltip { get; }
        public bool ShowTags { get; }
    }
}
