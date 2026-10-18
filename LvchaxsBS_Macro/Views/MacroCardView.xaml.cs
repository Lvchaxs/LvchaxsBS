using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using LvchaxsBS_Macro.Config;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Views
{
    /// <summary>
    /// 首页「自定义宏」卡片的**第二行**（两个下拉：宏名称 / 触发方式）。
    /// <para>
    /// ★ 整张卡片由宿主模板提供，这里只管那一行控件 —— 于是排序、触发键、配置页、胶囊开关
    ///   全部和宿主其它功能一致，插件不用各写一套。
    /// </para>
    /// <para>★ 每次宿主建卡片都会新建一个实例（宿主每次进首页都重建列表），所以不要做成单例。</para>
    /// </summary>
    public partial class MacroCardView : UserControl
    {
        /// <summary>
        /// 程序自己给下拉赋值时置 true —— 否则「赋值」会被当成「用户选择」又写回配置（死循环）。
        /// </summary>
        private bool _loading;

        public MacroCardView()
        {
            InitializeComponent();

            // ⛔ 直接用 SelectedIndex 赋值会触发 SelectionChanged；
            //    加这个标记，回调里就知道不是用户在选。
            _loading = true;
            ReloadFromSettings();
            _loading = false;
        }

        /// <summary>当前选中的宏名（没有宏时为空串）。</summary>
        public string SelectedMacroName => MacroNameSelect.ItemsSource is { Length: > 0 } names
                                           && MacroNameSelect.SelectedIndex >= 0
                                           && MacroNameSelect.SelectedIndex < names.Length
            ? names[MacroNameSelect.SelectedIndex]
            : "";

        /// <summary>当前选中的触发方式文本。</summary>
        public string SelectedTriggerMode => TriggerModeSelect.ItemsSource is { Length: > 0 } modes
                                             && TriggerModeSelect.SelectedIndex >= 0
                                             && TriggerModeSelect.SelectedIndex < modes.Length
            ? modes[TriggerModeSelect.SelectedIndex]
            : "";

        /// <summary>下拉里每一行对应的宏（下标 ↔ <c>MacroNameSelect</c> 的选项一一对应）。</summary>
        private List<MacroItem> _items = new();

        /// <summary>从插件配置里取宏列表填进三个下拉（选中项也从配置里取）。</summary>
        private void ReloadFromSettings()
        {
            var settings = MacroSettingsStore.Current;

            _items = (settings.Macros ?? new List<MacroItem>())
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .ToList();

            MacroNameSelect.ItemsSource = _items.Count > 0
                ? _items.Select(m => m.Name).ToArray()
                : new[] { "（没有宏）" };

            // ★ 选中项按 **Id** 定位（新建宏 / 拖动排序都不影响）
            var active = MacroSettingsStore.GetActiveMacro();
            int activeIndex = active == null ? -1 : _items.FindIndex(m => m.Id == active.Id);
            MacroNameSelect.SelectedIndex = activeIndex < 0 ? 0 : activeIndex;

            // 触发方式 = 当前这条宏自己的配置
            TriggerModeSelect.ItemsSource = MacroTriggerModeNames.All;
            TriggerModeSelect.SelectedIndex = active?.ModeIndex ?? 0;

            // 生效范围 = 模块级（跟开关、触发键同级）
            ScopeSelect.ItemsSource = MacroScopeNames.All;
            ScopeSelect.SelectedIndex = MacroSettingsStore.GetScopeIndex();
        }

        /// <summary>
        /// 换了「宏名称」→ 记进配置（触发键按下时执行的就是它）。
        /// <para>★ 记的是 <c>Id</c>，不是下标也不是名字 ⇒ 列表怎么变都认得人。</para>
        /// </summary>
        private void MacroNameSelect_SelectionChanged(object sender, int index)
        {
            if (_loading) return;
            if (index < 0 || index >= _items.Count) return;

            MacroSettingsStore.SetActiveMacroId(_items[index].Id);

            // 触发方式跟着切到这条宏自己的
            var active = MacroSettingsStore.GetActiveMacro();
            _loading = true;
            TriggerModeSelect.SelectedIndex = active?.ModeIndex ?? 0;
            _loading = false;
        }

        /// <summary>换了「触发方式」→ 写进当前这条宏。</summary>
        private void TriggerModeSelect_SelectionChanged(object sender, int index)
        {
            if (_loading) return;
            if (index < 0 || index >= MacroTriggerModeNames.All.Length) return;

            MacroSettingsStore.SetActiveModeIndex(index);
        }

        /// <summary>
        /// 换了「生效范围」→ 写进模块配置。
        /// <para>★ 切成「客户区内生效」时，如果宏正在跑就先停掉（免得它继续在后台执行）。</para>
        /// </summary>
        private void ScopeSelect_SelectionChanged(object sender, int index)
        {
            if (_loading) return;
            if (index < 0 || index >= MacroScopeNames.All.Length) return;

            MacroSettingsStore.SetScopeIndex(index);

            if (MacroSettingsStore.IsClientOnly && Core.MacroRunner.IsRunning)
                Core.MacroRunner.Stop();
        }
    }
}
