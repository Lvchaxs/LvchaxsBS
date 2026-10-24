using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Models;
using LvchaxsBS_Macro.Utils;

namespace LvchaxsBS_Macro.Views
{
    /// <summary>
    /// 给**一条宏**绑触发键的页面（承载宿主的键盘网格）。
    ///
    /// <para>
    /// ★★ 交互照「快速切图给地区绑键」：点一下某个键 = 选好、**自动返回**宏列表；
    ///   点 Ctrl / Alt 只是调组合键，保存但不返回（还要接着选基础键）；
    ///   点已选中的键 = 取消（清掉这个宏的键，但页面留着）。
    /// </para>
    /// <para>
    /// ★ 2026-10-22 起宏之间**允许共用同一个触发键**（同时只能有一个启用）：
    ///   别的宏用过的键不再禁选，由宿主页面标成「灰底红字 + 按钮底部占用者小字」，可点。
    ///   小写内容 = 那个键只绑了一个宏时显示宏名，绑了多个时显示「N个宏」。
    /// </para>
    /// </summary>
    public partial class MacroKeyPickerPage : Page
    {
        private readonly string _macroId;
        private MacroItem? _macro;
        private bool _built;

        public MacroKeyPickerPage(string macroId)
        {
            InitializeComponent();
            _macroId = macroId;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // ★ 只建一次：页面被导航回来时（返回栈）不要重建，否则会重复订阅事件
            if (_built) return;
            _built = true;

            Build();
        }

        private void Build()
        {
            _macro = MacroSettingsStore.GetAllMacros().FirstOrDefault(m => m.Id == _macroId);
            if (_macro == null)
            {
                GoBackToList();
                return;
            }

            MacroNameText.Text = _macro.Name;

            Page? picker = HostKeyPicker.Create(
                _macro.TriggerKey,
                BuildOccupied(),
                onSelected: key =>
                {
                    SaveKey(key);
                    GoBackToList();      // ★ 选好就自动返回（和快速切图一致）
                },
                onAdjusted: key =>
                {
                    SaveKey(key);        // 只调整了组合键：存下来，页面留着
                },
                onCleared: () => SaveKey("未设置"));

            if (picker == null)
            {
                // 旧宿主没有借按键页的入口 → 明确告诉用户，而不是留一片空白
                HintText.Text = "当前宿主版本不支持在这个界面设置触发键，请更新主程序后再试。";
                return;
            }

            PickerHost.Navigate(picker);
            UpdateCurrentKeyText();
        }

        /// <summary>
        /// 已经绑到某个键上的宏 → 那个键在按钮底部小字里的显示名。
        /// <para>★ 组合键传**完整键名**：宿主取基础键做标注。</para>
        /// <para>
        /// ★★ 2026-10-22 起宏之间允许共用触发键 ⇒ 显示名按**这条键上一共有几个宏**合成：
        ///   只有**一个**宏在用 ⇒ 显示那个宏的名字；
        ///   **两个及以上** ⇒ 显示「N个宏」（N = 含自己在内的总数）。
        ///   ⛔ 计数不能把自己排除掉：自己和另一条宏共用 X 时，按钮上要写「2个宏」而不是对方的名字。
        /// </para>
        /// </summary>
        private Dictionary<string, string> BuildOccupied()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in MacroSettingsStore.Current.Macros
                         .Where(m => m.HasTriggerKey)
                         .GroupBy(m => m.TriggerKey, StringComparer.OrdinalIgnoreCase))
            {
                var members = group.ToList();
                int count = members.Count;

                // 只有自己一条在用 ⇒ 没什么可提示的（自己的键是可点的选中态），不标注
                if (count <= 1)
                {
                    var only = members[0];
                    if (string.Equals(only.Id, _macroId, StringComparison.Ordinal)) continue;

                    map[group.Key] = string.IsNullOrWhiteSpace(only.Name) ? "其他宏" : only.Name;
                    continue;
                }

                map[group.Key] = $"{count}个宏";
            }

            return map;
        }

        private void SaveKey(string key)
        {
            MacroSettingsStore.SetMacroTriggerKey(_macroId, key);

            if (_macro != null) _macro.TriggerKey = key;
            UpdateCurrentKeyText();
        }

        private void UpdateCurrentKeyText()
        {
            if (_macro == null) return;

            bool has = _macro.HasTriggerKey;
            CurrentKeyText.Text = has ? _macro.TriggerKey : "未设置";
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => GoBackToList();

        public bool TryGoBack()
        {
            GoBackToList();
            return true;
        }

        public string GetBackTip() => "返回 宏列表";

        /// <summary>返回宏列表（复用同一个实例，回到原来的滚动位置）。</summary>
        private void GoBackToList()
        {
            Page target = MacroConfigPage.CurrentInstance ?? new MacroConfigPage();
            NavigationService?.Navigate(target);
        }
    }
}
