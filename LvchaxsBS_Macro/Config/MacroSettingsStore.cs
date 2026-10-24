using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using LvchaxsBS_Macro.Models;

namespace LvchaxsBS_Macro.Config
{
    /// <summary>
    /// 「自定义宏」配置的读写：<c>&lt;exe 目录&gt;\Data\Macro\macro.json</c>。
    /// <para>
    /// ★ 之所以自己写一套（不用宿主的 ConfigManager）：插件**不引用宿主**，
    ///   而且用户要求这份配置放在**插件专属文件夹**里。
    /// </para>
    /// <para>
    /// ★ 为什么放在 <c>Data</c> 而不是 <c>Config</c>：宏是<b>用户自己写的内容</b>，不是设置，
    ///   而「重置配置」会把整个 <c>Config</c> 删掉 —— 放在那儿会被一并带走。
    /// </para>
    /// <para>
    /// ★ 写入是「先写 .tmp 再替换」的原子做法 —— 中途断电也不会留下半截 JSON。
    /// </para>
    /// </summary>
    internal static class MacroSettingsStore
    {
        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            // 中文别转成 \uXXXX，方便用户直接看/改配置文件
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static MacroSettings? _cache;

        /// <summary>专属配置文件夹：<c>&lt;exe 目录&gt;\Data\Macro</c>。</summary>
        public static string FolderPath { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "Macro");

        /// <summary>配置文件完整路径。</summary>
        public static string FilePath { get; } = Path.Combine(FolderPath, "macro.json");

        /// <summary>当前配置（首次访问时读盘；读不到就用默认值）。</summary>
        public static MacroSettings Current
        {
            get
            {
                lock (Gate) return _cache ??= LoadCore();
            }
        }

        /// <summary>
        /// 配置被改动过（每次 <see cref="Mutate"/> 落盘后触发）。
        /// <para>
        /// ★ 给首页卡片上的「宏文件 N 个」标签用：新建/删除宏、切换文件夹、改名字之后，
        ///   卡片上那点内容要跟着变，但它是宿主创建的一个**长期存在**的控件，
        ///   不会因为列表变化而重建 —— 只能靠这个事件自己刷新。
        /// </para>
        /// <para>★ 可能在**非 UI 线程**触发（比如执行引擎里改配置），订阅方自己切 Dispatcher。</para>
        /// </summary>
        public static event Action? Changed;

        /// <summary>改一处并立刻落盘。</summary>
        public static void Mutate(Action<MacroSettings> change)
        {
            lock (Gate)
            {
                var settings = _cache ??= LoadCore();
                change(settings);
                SaveCore(settings);
            }

            // ★ 在锁**外**通知：处理器里再读配置（很常见）不会卡在别人手上，
            //   也不会因为处理器抛异常把配置改动回滚掉。
            try { Changed?.Invoke(); } catch { /* 通知失败不影响已落盘的配置 */ }
        }

        // ==================== 宏的增删改 / 置顶 / 排序 ====================

        private static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// 名称是否已被**别的**宏占用（编辑时把自己排除掉）。
        /// <para>★ 名称是用户识别宏的唯一线索，重名会让"选哪个宏"没法区分，所以在界面上拦一道。</para>
        /// </summary>
        public static bool NameExists(string? name, string exceptId = "")
        {
            string target = (name ?? "").Trim();
            if (target.Length == 0) return false;

            lock (Gate)
            {
                var settings = _cache ??= LoadCore();
                return settings.Macros.Any(m =>
                    !string.Equals(m.Id, exceptId, StringComparison.Ordinal) &&
                    string.Equals(m.Name, target, StringComparison.Ordinal));
            }
        }

        /// <summary>
        /// 新增一条宏。
        /// <para>
        /// ★ 插到**非置顶区的最前面**（没有置顶项时就是列表第一个）：
        ///   新建的宏一眼就能看到，也不会沉到列表底部、看着像"按创建时间排序"。
        /// </para>
        /// </summary>
        public static MacroItem? Add(string? name, string? description, string folderId = "",
                                     string? avatar = null,
                                     int modeIndex = (int)Models.MacroTriggerMode.Once,
                                     int scopeIndex = (int)Models.MacroScope.ClientOnly,
                                     int conditionIndex = (int)Models.MacroCondition.None)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) return null;

            MacroItem? added = null;
            Mutate(s =>
            {
                added = new MacroItem
                {
                    Id = NewId(),
                    Name = trimmed,
                    Description = (description ?? "").Trim(),
                    Avatar = avatar ?? Utils.AvatarCatalog.DefaultName,
                    ModeIndex = modeIndex,
                    ScopeIndex = scopeIndex,
                    ConditionIndex = conditionIndex,
                    FolderId = folderId ?? "",

                    // ★ 新宏直接给一份「三种触发方式各一个块」的示例 ——
                    //   新语法（代码块）光靠状态条那行提示不好上手，照着改最快。
                    Script = Core.MacroScript.Sample,
                };

                int index = s.Macros.FindIndex(m => !m.IsPinned);   // -1 = 全都置顶了
                if (index < 0) s.Macros.Add(added);
                else s.Macros.Insert(index, added);
            });
            return added;
        }

        /// <summary>
        /// 改名称 / 描述 / 头像 / 触发方式 / 生效范围 / 触发条件。
        /// <para>★ 传 null 表示这一项不动。</para>
        /// </summary>
        public static void Update(string id, string? name, string? description, string? avatar = null,
                                  int? modeIndex = null, int? scopeIndex = null, int? conditionIndex = null)
            => Mutate(s =>
            {
                var m = s.Macros.FirstOrDefault(x => x.Id == id);
                if (m == null) return;

                string trimmed = (name ?? "").Trim();
                if (trimmed.Length > 0) m.Name = trimmed;
                m.Description = (description ?? "").Trim();
                if (avatar != null) m.Avatar = avatar;
                if (modeIndex != null) m.ModeIndex = modeIndex.Value;
                if (scopeIndex != null) m.ScopeIndex = scopeIndex.Value;
                if (conditionIndex != null) m.ConditionIndex = conditionIndex.Value;
            });

        /// <summary>
        /// 删除一条宏。
        /// <para>★ 删的正好是首页选中的那条 ⇒ 清掉记录，让下次自动落到第一条。</para>
        /// </summary>
        public static void Remove(string id)
            => Mutate(s =>
            {
                var victim = s.Macros.FirstOrDefault(x => x.Id == id);
                bool wasActive = victim != null &&
                                 (string.Equals(s.ActiveMacroId, id, StringComparison.Ordinal) ||
                                  (string.IsNullOrEmpty(s.ActiveMacroId) &&
                                   string.Equals(s.ActiveMacroName, victim.Name, StringComparison.Ordinal)));

                s.Macros.RemoveAll(x => x.Id == id);

                if (wasActive)
                {
                    s.ActiveMacroId = "";
                    s.ActiveMacroName = "";
                }
            });

        /// <summary>写回一条宏的指令脚本（指令编辑页的「保存」）。</summary>
        public static void SetScript(string id, string script)
            => Mutate(s =>
            {
                var m = s.Macros.FirstOrDefault(x => x.Id == id);
                if (m != null) m.Script = script ?? "";
            });

        // ==================== 文件夹（分组） ====================

        /// <summary>文件夹名是否已被别的文件夹占用（重命名时把自己排除）。</summary>
        public static bool FolderNameExists(string? name, string exceptId = "")
        {
            string target = (name ?? "").Trim();
            if (target.Length == 0) return false;

            lock (Gate)
            {
                var settings = _cache ??= LoadCore();
                return settings.Folders.Any(f =>
                    !string.Equals(f.Id, exceptId, StringComparison.Ordinal) &&
                    string.Equals(f.Name, target, StringComparison.Ordinal));
            }
        }

        /// <summary>新建一个文件夹。</summary>
        public static MacroFolder? AddFolder(string? name, string? description)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) return null;

            MacroFolder? added = null;
            Mutate(s =>
            {
                added = new MacroFolder
                {
                    Id = NewId(),
                    Name = trimmed,
                    Description = (description ?? "").Trim(),
                };
                s.Folders.Add(added);
            });
            return added;
        }

        /// <summary>改文件夹名 / 备注。</summary>
        public static void UpdateFolder(string id, string? name, string? description)
            => Mutate(s =>
            {
                var f = s.Folders.FirstOrDefault(x => x.Id == id);
                if (f == null) return;

                string trimmed = (name ?? "").Trim();
                if (trimmed.Length > 0) f.Name = trimmed;
                f.Description = (description ?? "").Trim();
            });

        /// <summary>
        /// 删文件夹 —— ★ **不删宏**：里面的宏退回未分组（<c>FolderId = ""</c>），
        /// 否则用户删一个分类会连带丢掉一堆宏，那是灾难。
        /// </summary>
        public static void RemoveFolder(string id)
            => Mutate(s =>
            {
                s.Folders.RemoveAll(f => f.Id == id);

                foreach (MacroItem m in s.Macros)
                    if (string.Equals(m.FolderId, id, StringComparison.Ordinal))
                        m.FolderId = "";

                if (string.Equals(s.ActiveFolderId, id, StringComparison.Ordinal))
                    s.ActiveFolderId = "";
            });

        /// <summary>当前文件夹列表（只读快照，**置顶的在前** —— 稳定排序，各自保持原有相对顺序）。</summary>
        public static List<MacroFolder> GetFolders()
        {
            lock (Gate)
            {
                var settings = _cache ??= LoadCore();
                return settings.Folders.OrderByDescending(f => f.IsPinned).ToList();
            }
        }

        /// <summary>取某个文件夹里的宏（<c>""</c> = 未分组）。顺序同总列表。</summary>
        public static List<MacroItem> GetMacrosIn(string folderId)
        {
            string target = folderId ?? "";

            lock (Gate)
            {
                var settings = _cache ??= LoadCore();
                return settings.Macros
                    .Where(m => string.Equals(m.FolderId ?? "", target, StringComparison.Ordinal))
                    .ToList();
            }
        }

        /// <summary>
        /// 取**全部文件夹**里的所有宏（编辑器标题下拉用 —— 用户要求「展开全部宏」）。
        /// <para>★ 按文件夹分组排序，同一文件夹内保持原有顺序；
        ///   每项的 <see cref="MacroItem.FolderId"/> 保留归属，调用方可自行显示文件夹名。</para>
        /// </summary>
        public static List<MacroItem> GetAllMacros()
        {
            lock (Gate)
            {
                var settings = _cache ??= LoadCore();
                var order = settings.Folders
                    .OrderByDescending(f => f.IsPinned)
                    .Select(f => f.Id ?? "")
                    .ToList();

                return settings.Macros
                    .OrderBy(m =>
                    {
                        int i = order.IndexOf(m.FolderId ?? "");
                        return i < 0 ? int.MaxValue : i;   // 不在列表里的排最后
                    })
                    .ToList();
            }
        }

        /// <summary>把一条宏移进某个文件夹（<c>""</c> = 移出到未分组）。</summary>
        public static void MoveToFolder(string macroId, string folderId)
            => Mutate(s =>
            {
                var m = s.Macros.FirstOrDefault(x => x.Id == macroId);
                if (m != null) m.FolderId = folderId ?? "";
            });

        /// <summary>首页卡片「文件夹」下拉选中的项（<c>""</c> = 全部）。文件夹被删了自动归零。</summary>
        public static string GetActiveFolderId()
        {
            lock (Gate)
            {
                var settings = _cache ??= LoadCore();
                string id = settings.ActiveFolderId ?? "";
                if (id.Length == 0) return "";
                return settings.Folders.Any(f => f.Id == id) ? id : "";
            }
        }

        /// <summary>记住首页卡片选中的文件夹。</summary>
        public static void SetActiveFolderId(string? id)
            => Mutate(s => s.ActiveFolderId = id ?? "");

        // ==================== 当前宏 / 触发方式（首页卡片那两个下拉） ====================

        /// <summary>
        /// 首页卡片下拉选中的宏（触发键按下就执行它；「编辑此宏」打开的也是它）。
        /// <para>
        /// ★★ 按 <see cref="MacroItem.Id"/> 认人 —— **新建宏 / 拖动排序 / 删除别的宏都不会改掉选择**，
        ///    这正是"我选哪个就用哪个"。名字只是旧配置的兜底。
        /// </para>
        /// <para>
        /// ★★ **只在首页卡片当前选中的文件夹范围内认人**（详见 <see cref="ResolveActive"/>）：
        ///    选中的那条不在这个文件夹里时，自动落到本文件夹第一条并固化。
        /// </para>
        /// <para>
        /// 只有「从没选过」或「选中的那条被删了」才自动落到第一条，并且**立刻写盘固化**，
        /// 之后列表再怎么变都不动它。
        /// </para>
        /// </summary>
        public static MacroItem? GetActiveMacro()
        {
            var settings = Current;
            var (item, persistId) = ResolveActive(settings);
            if (persistId != null) Mutate(s => s.ActiveMacroId = persistId);
            return item;
        }

        /// <summary>卡片「宏名称」下拉选了哪一项 → 记的是这条宏的 **Id**（写盘）。</summary>
        public static void SetActiveMacroId(string? id)
            => Mutate(s => s.ActiveMacroId = id ?? "");

        /// <summary>卡片「触发方式」下拉选了哪一项 → 写进当前宏自己的 ModeIndex。</summary>
        public static void SetActiveModeIndex(int modeIndex)
            => Mutate(s =>
            {
                var (item, _) = ResolveActive(s);
                if (item != null)
                    item.ModeIndex = Math.Clamp(modeIndex, 0, MacroTriggerModeNames.All.Length - 1);
            });

        /// <summary>
        /// 在给定配置里找当前选中的宏。
        /// <para>
        /// ★★ **只在当前文件夹的范围内认人**（<see cref="MacroSettings.ActiveFolderId"/>，<c>""</c> = 全部宏）：
        /// 2026-10-20 用户实测的 bug —— 在玛薇卡里选了「单码头」，切到丝柯克文件夹后，
        /// 首页下拉**显示**的是「双丝头」，但触发键执行的和「编辑此宏」开的都还是单码头。
        /// 原因就是这里解析时压根没看文件夹：<c>ActiveMacroId</c> 在全部宏里找到了单码头，
        /// 而 <c>MacroCardView.ReloadMacroList</c> 只是把 <c>SelectedIndex</c> 摆到 0（显示层面），
        /// 并没有真正改选择。
        /// </para>
        /// <para>
        /// 修法：选中那条不在当前文件夹里（被删了 / 是从别的文件夹选完后切过来的）⇒
        /// **自动落到本文件夹第一条并立刻固化**。从此「下拉显示的 = 实际执行的 = 编辑的」永远成立。
        /// </para>
        /// <para>
        /// ★ 当前文件夹一条宏都没有 ⇒ 返回 null（触发键按了没反应、编辑按钮置灰），
        ///   但**不清掉** <c>ActiveMacroId</c> —— 切回原来的文件夹时还记得之前选的那条。
        /// </para>
        /// </summary>
        /// <returns>
        /// <c>item</c>=找到的宏（列表空 / 当前文件夹里没有宏时为 null）；
        /// <c>persistId</c>=需要写回配置的 Id（不是按 Id 命中时给出，null = 不用写）。
        /// </returns>
        private static (MacroItem? item, string? persistId) ResolveActive(MacroSettings settings)
        {
            var list = settings.Macros;
            if (list == null || list.Count == 0) return (null, null);

            // 当前文件夹范围内可见的宏（"" = 全部宏，不筛选）
            string folderId = settings.ActiveFolderId ?? "";
            List<MacroItem> visible = folderId.Length == 0
                ? list
                : list.Where(m => string.Equals(m.FolderId ?? "", folderId, StringComparison.Ordinal)).ToList();
            if (visible.Count == 0) return (null, null);

            // ① 按 Id（★ 唯一权威来源）
            string id = settings.ActiveMacroId ?? "";
            if (id.Length > 0)
            {
                var byId = visible.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
                if (byId != null) return (byId, null);
                return (visible[0], visible[0].Id);   // 不在当前文件夹 / 被删了 → 落到本文件夹第一条并固化
            }

            // ② 旧配置按名字兜底一次（同样限当前文件夹），顺手换成 Id
            string name = settings.ActiveMacroName ?? "";
            if (name.Length > 0)
            {
                var byName = visible.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
                if (byName != null) return (byName, byName.Id);
                return (visible[0], visible[0].Id);
            }

            // ③ 从没选过 → 自动选当前文件夹第一条并固化（触发键不至于按了没反应）
            return (visible[0], visible[0].Id);
        }

        // ==================== 生效范围（每条宏自己的第二个选项） ====================

        /// <summary>
        /// 当前激活宏的生效范围下标（0=全局生效 / 1=客户区内生效）。
        /// <para>★ 读的是**用户自己选的那个值**，没有任何默认覆盖。</para>
        /// </summary>
        public static int GetScopeIndex()
        {
            var macro = GetActiveMacro();
            return macro?.ScopeIndex ?? (int)Models.MacroScope.ClientOnly;
        }

        /// <summary>
        /// 把生效范围写进**当前激活宏**（落盘，下次直接沿用）。
        /// <para>★ 用户选 0 就存 0、选 1 就存 1 —— 不再有"统一客户区内"这回事。</para>
        /// </summary>
        public static void SetScopeIndex(int index)
            => SetActiveScopeIndex(index);

        /// <summary>
        /// 把生效范围写进当前激活宏（<see cref="SetActiveModeIndex"/> 的生效范围版）。
        /// </summary>
        public static void SetActiveScopeIndex(int scopeIndex)
        {
            int clamped = Math.Clamp(scopeIndex, 0, MacroScopeNames.All.Length - 1);

            Mutate(s =>
            {
                var (macro, _) = ResolveActive(s);
                if (macro != null) macro.ScopeIndex = clamped;
            });
        }
        // ============ 每条宏自己的开关 / 触发键（2026-10-21） ============

        /// <summary>
        /// 写某条宏的胶囊开关。
        /// <para>
        /// ★★ 2026-10-22 起：**同一个触发键同时只能有一个宏启用**（用户要求）——
        ///   打开这个 ⇒ 把同键的其他宏全部关掉（那些宏的开关会在界面上自己滑回去）；
        ///   关闭这个不影响别人（关了之后这个键就暂时没有宏会响应）。
        /// </para>
        /// </summary>
        public static void SetMacroEnabled(string id, bool enabled)
            => Mutate(s =>
            {
                var m = s.Macros.FirstOrDefault(x => x.Id == id);
                if (m == null) return;

                m.Enabled = enabled;

                if (enabled && m.HasTriggerKey) DisableOthersSharingKey(s, m);
            });

        /// <summary>
        /// 写某条宏的触发键。
        /// <para>
        /// ★ 多个宏可以设**同一个**触发键（2026-10-22 起允许共用）；
        ///   但改完之后仍要守住「同键同时只有一个启用」这条不变量：
        ///   自己是启用状态且新键上已有别的启用宏 ⇒ 以本次编辑的为准，关掉对方。
        /// </para>
        /// </summary>
        public static void SetMacroTriggerKey(string id, string key)
            => Mutate(s =>
            {
                var m = s.Macros.FirstOrDefault(x => x.Id == id);
                if (m == null) return;

                m.TriggerKey = key;

                if (m.Enabled && m.HasTriggerKey) DisableOthersSharingKey(s, m);
            });

        /// <summary>把配置里除 <paramref name="keeper"/> 外、同触发键且启用着的宏全部关掉。</summary>
        private static void DisableOthersSharingKey(MacroSettings settings, MacroItem keeper)
        {
            if (!keeper.HasTriggerKey) return;

            foreach (var other in settings.Macros)
            {
                if (ReferenceEquals(other, keeper)) continue;
                if (!other.Enabled || !other.HasTriggerKey) continue;

                if (string.Equals(other.TriggerKey, keeper.TriggerKey, StringComparison.OrdinalIgnoreCase))
                    other.Enabled = false;
            }
        }

        /// <summary>
        /// 所有「启用 + 已设触发键」的键 —— 交给宿主的键盘钩子一起监听。
        /// <para>★ 去重：多个宏共用同一个键时只注册一次（按下时由互斥规则保证只有一个会跑）。</para>
        /// </summary>
        public static List<string> GetTriggerKeys()
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var m in Current.Macros)
            {
                if (!m.Enabled) continue;
                string k = m.TriggerKey;
                if (string.IsNullOrWhiteSpace(k) || k == "未设置") continue;
                if (seen.Add(k)) list.Add(k);
            }

            return list;
        }

        /// <summary>
        /// 按触发键找宏（返回**第一个**启用且键匹配的）。
        /// <para>★ 同一个键上同时只可能有一个宏是启用的（开/改键时都会互斥），
        ///   所以"第一个"就是唯一会响应的那个。</para>
        /// </summary>
        public static MacroItem? FindByTriggerKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            return Current.Macros.FirstOrDefault(m =>
                m.Enabled &&
                string.Equals(m.TriggerKey, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 某条宏是不是「只在游戏客户区内生效」—— 读它自己的 <see cref="MacroItem.ScopeIndex"/>。
        /// <para>★ 每条宏各管各的：有的要在任何窗口都能按，有的只想在游戏里生效。</para>
        /// </summary>
        public static bool IsClientOnly(MacroItem? macro)
            => macro != null && macro.ScopeIndex == (int)Models.MacroScope.ClientOnly;

        /// <summary>读指令编辑器缩放百分比（50~200）。</summary>
        public static int GetEditorZoom() => Math.Clamp(Current.EditorZoomPercent, 50, 200);

        /// <summary>写指令编辑器缩放百分比（落盘，下次直接沿用）。</summary>
        public static void SetEditorZoom(int percent)
            => Mutate(s => s.EditorZoomPercent = Math.Clamp(percent, 50, 200));

        /// <summary>
        /// 把一条宏挪到指定下标（拖动排序用）。
        /// <para>★ 松手后统一按「置顶的在前」稳定重排一遍：置顶是长期状态，
        /// 不能被一次拖动弄丢（置顶区内部顺序仍按用户拖的来）。</para>
        /// </summary>
        public static void Move(string id, int toIndex)
            => Mutate(s =>
            {
                var m = s.Macros.FirstOrDefault(x => x.Id == id);
                if (m == null) return;

                int from = s.Macros.IndexOf(m);
                int to = Math.Clamp(toIndex, 0, s.Macros.Count - 1);
                if (from != to)
                {
                    s.Macros.RemoveAt(from);
                    s.Macros.Insert(to, m);
                }

                var ordered = s.Macros.OrderByDescending(x => x.IsPinned).ToList();   // OrderBy 是稳定排序
                s.Macros.Clear();
                s.Macros.AddRange(ordered);
            });

        /// <summary>
        /// 把一个文件夹挪到指定下标（顶层拖动排序用）。
        /// <para>★ 拖完同样要按置顶做一次**稳定重排**（同 <see cref="Move"/>）——
        /// 否则把一个未置顶的文件夹拖到置顶区上方，界面上会"拖进置顶区却没进"。</para>
        /// </summary>
        public static void MoveFolder(string id, int toIndex)
            => Mutate(s =>
            {
                var f = s.Folders.FirstOrDefault(x => x.Id == id);
                if (f == null) return;

                int from = s.Folders.IndexOf(f);
                int to = Math.Clamp(toIndex, 0, s.Folders.Count - 1);
                if (from != to)
                {
                    s.Folders.RemoveAt(from);
                    s.Folders.Insert(to, f);
                }

                var ordered = s.Folders.OrderByDescending(x => x.IsPinned).ToList();   // 稳定排序
                s.Folders.Clear();
                s.Folders.AddRange(ordered);
            });

        /// <summary>
        /// 文件夹置顶 / 取消置顶（点卡片上的文件夹图标）。
        /// <para>★ 语义与 <see cref="TogglePin"/> 完全一致：只翻标志位 + 稳定重排。</para>
        /// </summary>
        public static void TogglePinFolder(string id)
            => Mutate(s =>
            {
                var f = s.Folders.FirstOrDefault(x => x.Id == id);
                if (f == null) return;

                f.IsPinned = !f.IsPinned;

                var ordered = s.Folders.OrderByDescending(x => x.IsPinned).ToList();
                s.Folders.Clear();
                s.Folders.AddRange(ordered);
            });

        /// <summary>
        /// 置顶 / 取消置顶（点卡片图标）。
        /// <para>
        /// ★★ 只翻 <see cref="MacroItem.IsPinned"/> 标志，然后对整表做一次**稳定排序**
        /// （置顶的在前，各自**保持原有相对顺序**）—— 和 UID 分组列表完全一致。
        /// ⛔ 不能写成「Remove + Insert(0)」：那样再次置顶原来在下边的那条时，
        ///   它会插到最前、和上边那条**互换位置**（用户实测过，UID 不会这样）。
        /// </para>
        /// </summary>
        public static void TogglePin(string id)
            => Mutate(s =>
            {
                var m = s.Macros.FirstOrDefault(x => x.Id == id);
                if (m == null) return;

                m.IsPinned = !m.IsPinned;

                // OrderByDescending 是**稳定排序** ⇒ 置顶区、非置顶区内部顺序都不变
                var ordered = s.Macros.OrderByDescending(x => x.IsPinned).ToList();
                s.Macros.Clear();
                s.Macros.AddRange(ordered);
            });

        private static MacroSettings LoadCore()
        {
            // ★ 老位置（Config\Macro）里的文件搬过来：宏是用户自己写的，不能因为重置配置就没了
            MigrateFromLegacyFolder();

            MacroSettings settings;

            try
            {
                if (!File.Exists(FilePath))
                {
                    // ★ 第一次就跑起来（首页读开关状态时）就把默认配置落盘，
                    //   用户能直接在 Data\Macro 下看到这个文件，而不是"点了开关才冒出来"。
                    settings = new MacroSettings();
                    Normalize(settings);
                    SaveCore(settings);
                    return settings;
                }

                string json = File.ReadAllText(FilePath);
                settings = string.IsNullOrWhiteSpace(json)
                    ? new MacroSettings()
                    : JsonSerializer.Deserialize<MacroSettings>(json, JsonOptions) ?? new MacroSettings();
            }
            catch
            {
                // 文件坏了 / 被占用 → 用默认值跑起来，别让宿主因为插件配置读不了而起不来
                settings = new MacroSettings();
            }

            int beforeVersion = settings.SchemaVersion;
            Normalize(settings);

            // 迁移过（版本号变了）就立刻固化，下次不用再迁
            if (settings.SchemaVersion != beforeVersion) SaveCore(settings);

            return settings;
        }

        /// <summary>
        /// 补齐旧配置缺的东西：<see cref="MacroItem.Id"/> 为空就生成一个，重复就换一个。
        /// <para>★ 早期版本的配置里没有 Id 字段，加载后必须补 —— 否则编辑/删除都定位不到行。</para>
        /// </summary>
        private static void Normalize(MacroSettings settings)
        {
            settings.Macros ??= new List<MacroItem>();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in settings.Macros)
            {
                if (string.IsNullOrEmpty(m.Id) || !seen.Add(m.Id))
                {
                    m.Id = NewId();
                    seen.Add(m.Id);
                }
            }

            Migrate(settings);

            // ★ 不变量**每次加载都修一遍**（不是只在迁移时修一次）：
            //   手改配置文件 / 旧版本写出的配置都可能出现「同一个键两个宏都开着」。
            EnforceSingleEnabledPerKey(settings);
        }

        /// <summary>
        /// 守住「同一个触发键同时只有一个宏启用」：每个键只保留**第一个**启用的，其余关掉。
        /// <para>★ 幂等、无副作用：本来就没冲突时什么都不会改。</para>
        /// </summary>
        private static void EnforceSingleEnabledPerKey(MacroSettings settings)
        {
            if (settings.Macros == null || settings.Macros.Count == 0) return;

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in settings.Macros)
            {
                if (!m.Enabled || !m.HasTriggerKey) continue;
                if (!seenKeys.Add(m.TriggerKey)) m.Enabled = false;
            }
        }

        /// <summary>
        /// 一次性迁移：配置结构变了（比如枚举语义调整）时把老数据翻成新语义。
        /// <para>★ 每加一段迁移就 <see cref="MacroSettings.CurrentSchemaVersion"/> +1。</para>
        /// </summary>
        private static void Migrate(MacroSettings settings)
        {
            // ---- v0 → v1（2026-10-19）：触发方式 0 和 2 语义互换 ----
            //      旧：0=按下触发再次按下结束 / 2=按下生效抬起后结束
            //      新：0=按下仅执行一次     / 2=按下时切换开关
            if (settings.SchemaVersion < 1)
            {
                foreach (var m in settings.Macros)
                {
                    m.ModeIndex = m.ModeIndex switch
                    {
                        0 => (int)MacroTriggerMode.Toggle,
                        2 => (int)MacroTriggerMode.Once,
                        _ => m.ModeIndex,
                    };
                }

                settings.SchemaVersion = 1;
            }

            // ---- v1 → v2（2026-10-21）：开关 / 触发键从"全局一份"下沉到**每条宏各一份** ----
            //      旧的全局开关先继承给所有宏；旧的全局触发键归给"原来选中的那条"（没有就第一条）。
            if (settings.SchemaVersion < 2)
            {
                string oldKey = settings.TriggerKey ?? "未设置";
                bool oldEnabled = settings.Enabled;
                string activeId = settings.ActiveMacroId ?? "";

                foreach (var m in settings.Macros)
                {
                    m.Enabled = oldEnabled;
                    m.TriggerKey = "未设置";
                }

                var target = settings.Macros.FirstOrDefault(x => x.Id == activeId)
                             ?? settings.Macros.FirstOrDefault();
                if (target != null && !string.IsNullOrWhiteSpace(oldKey) && oldKey != "未设置")
                    target.TriggerKey = oldKey;

                // ★ 只升到 2 —— 后面还有 v2→v3 要接着跑（直接跳到当前版本会把 v3 的迁移吞掉）
                settings.SchemaVersion = 2;
            }

            // ---- v2 → v3（2026-10-22）：同一个触发键同时只允许一个宏启用 ----
            //      旧规则下同键多宏根本选不上（选键页直接禁选），但手改配置文件可能弄出
            //      「同键两个都开着」⇒ 每个键只保留第一个启用的，其余关掉。
            if (settings.SchemaVersion < 3)
            {
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in settings.Macros)
                {
                    if (!m.Enabled || !m.HasTriggerKey) continue;
                    if (!seenKeys.Add(m.TriggerKey)) m.Enabled = false;
                }

                settings.SchemaVersion = MacroSettings.CurrentSchemaVersion;
            }
        }

        /// <summary>
        /// 把老位置 <c>Config\Macro\macro.json</c> 里的配置搬到 <c>Data\Macro\macro.json</c>。
        /// <para>
        /// ★ 只在目标文件<b>不存在</b>时搬一次：之后两边各自独立，用户在 <c>Data</c> 下的改动
        ///   不会被老文件覆盖回去。
        /// </para>
        /// <para>★ 只<b>复制</b>不删除：老文件留着当备份，出问题还能手动捞回来。</para>
        /// </summary>
        private static void MigrateFromLegacyFolder()
        {
            try
            {
                if (File.Exists(FilePath)) return;

                string legacy = Path.Combine(AppContext.BaseDirectory, "Config", "Macro", "macro.json");
                if (!File.Exists(legacy)) return;

                Directory.CreateDirectory(FolderPath);
                File.Copy(legacy, FilePath, overwrite: false);
            }
            catch
            {
                // 搬不动就用默认配置跑起来，绝不让插件因为这一层而起不来
            }
        }

        private static void SaveCore(MacroSettings settings)
        {
            try
            {
                Directory.CreateDirectory(FolderPath);

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(FilePath))
                {
                    try { File.Replace(tempPath, FilePath, null); }
                    catch
                    {
                        File.Copy(tempPath, FilePath, overwrite: true);
                        try { File.Delete(tempPath); } catch { /* 留着也无害 */ }
                    }
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }
            }
            catch
            {
                // 落盘失败（只读目录 / 被杀毒锁）不能影响界面操作
            }
        }
    }
}
