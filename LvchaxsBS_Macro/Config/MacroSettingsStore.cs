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
    /// 「自定义宏」配置的读写：<c>&lt;exe 目录&gt;\Config\Macro\macro.json</c>。
    /// <para>
    /// ★ 之所以自己写一套（不用宿主的 ConfigManager）：插件**不引用宿主**，
    ///   而且用户要求这份配置放在**插件专属文件夹**里。
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

        /// <summary>专属配置文件夹：<c>&lt;exe 目录&gt;\Config\Macro</c>。</summary>
        public static string FolderPath { get; } = Path.Combine(AppContext.BaseDirectory, "Config", "Macro");

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

        /// <summary>改一处并立刻落盘。</summary>
        public static void Mutate(Action<MacroSettings> change)
        {
            lock (Gate)
            {
                var settings = _cache ??= LoadCore();
                change(settings);
                SaveCore(settings);
            }
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
        public static MacroItem? Add(string? name, string? description)
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
                    ModeIndex = 0,
                };

                int index = s.Macros.FindIndex(m => !m.IsPinned);   // -1 = 全都置顶了
                if (index < 0) s.Macros.Add(added);
                else s.Macros.Insert(index, added);
            });
            return added;
        }

        /// <summary>改名称 / 描述。</summary>
        public static void Update(string id, string? name, string? description)
            => Mutate(s =>
            {
                var m = s.Macros.FirstOrDefault(x => x.Id == id);
                if (m == null) return;

                string trimmed = (name ?? "").Trim();
                if (trimmed.Length > 0) m.Name = trimmed;
                m.Description = (description ?? "").Trim();
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

        // ==================== 当前宏 / 触发方式（首页卡片那两个下拉） ====================

        /// <summary>
        /// 首页卡片下拉选中的宏（触发键按下就执行它）。
        /// <para>
        /// ★★ 按 <see cref="MacroItem.Id"/> 认人 —— **新建宏 / 拖动排序 / 删除别的宏都不会改掉选择**，
        ///    这正是"我选哪个就用哪个"。名字只是旧配置的兜底。
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
        /// </summary>
        /// <returns>
        /// <c>item</c>=找到的宏（列表空时为 null）；
        /// <c>persistId</c>=需要写回配置的 Id（不是按 Id 命中时给出，null = 不用写）。
        /// </returns>
        private static (MacroItem? item, string? persistId) ResolveActive(MacroSettings settings)
        {
            var list = settings.Macros;
            if (list == null || list.Count == 0) return (null, null);

            // ① 按 Id（★ 唯一权威来源）
            string id = settings.ActiveMacroId ?? "";
            if (id.Length > 0)
            {
                var byId = list.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
                if (byId != null) return (byId, null);
                return (list[0], list[0].Id);     // 那条被删了 → 落到第一条并固化
            }

            // ② 旧配置按名字兜底一次，顺手换成 Id
            string name = settings.ActiveMacroName ?? "";
            if (name.Length > 0)
            {
                var byName = list.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
                if (byName != null) return (byName, byName.Id);
                return (list[0], list[0].Id);
            }

            // ③ 从没选过 → 自动选第一条并固化（触发键不至于按了没反应）
            return (list[0], list[0].Id);
        }

        // ==================== 生效范围（卡片第三个下拉） ====================

        /// <summary>生效范围下标：0=全局生效 / 1=客户区内生效。</summary>
        public static int GetScopeIndex()
        {
            var settings = Current;
            return Math.Clamp(settings.ScopeIndex, 0, MacroScopeNames.All.Length - 1);
        }

        /// <summary>写生效范围（落盘）。</summary>
        public static void SetScopeIndex(int index)
            => Mutate(s => s.ScopeIndex = Math.Clamp(index, 0, MacroScopeNames.All.Length - 1));

        /// <summary>是不是「只在游戏客户区内生效」。</summary>
        public static bool IsClientOnly => Current.Scope == MacroScope.ClientOnly;

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
            MacroSettings settings;

            try
            {
                if (!File.Exists(FilePath))
                {
                    // ★ 第一次就跑起来（首页读开关状态时）就把默认配置落盘，
                    //   用户能直接在 Config\Macro 下看到这个文件，而不是"点了开关才冒出来"。
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
        }

        /// <summary>
        /// 一次性迁移：配置结构变了（比如枚举语义调整）时把老数据翻成新语义。
        /// <para>★ 每加一段迁移就 <see cref="MacroSettings.CurrentSchemaVersion"/> +1。</para>
        /// </summary>
        private static void Migrate(MacroSettings settings)
        {
            // ---- v0 → v1（2026-10-19）：触发方式 0 和 2 语义互换 ----
            //      旧：0=按下触发再次按下结束 / 2=按下生效抬起后结束
            //      新：0=按下仅执行一次     / 2=按下触发循环，再次按下结束
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

                settings.SchemaVersion = MacroSettings.CurrentSchemaVersion;
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
