using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LvchaxsBS_UID.Models;
using LvchaxsBS_UID.Utils.File;

namespace LvchaxsBS_UID.Services.DataServices
{
    public class GroupDataService
    {
        private const string DataFileName = "groups.json";
        private static readonly string DataFilePath = AppPaths.ConfigFile(DataFileName);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,  // 确保null值也序列化
            Converters = { new JsonStringEnumConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public static List<AccountGroup> LoadGroups()
        {
            try
            {
                if (!File.Exists(DataFilePath))
                {
                    DebugLog($"分组文件不存在: {DataFilePath}");
                    return new List<AccountGroup>();
                }

                string json = File.ReadAllText(DataFilePath);
                DebugLog($"加载分组文件: {DataFilePath}");

                var groups = JsonSerializer.Deserialize<List<AccountGroup>>(json, JsonOptions) ?? new List<AccountGroup>();

                foreach (var group in groups)
                    group.AccountIds ??= new List<int>();

                // 老数据迁移：markedTimes（账号 → 置底时间）→ markedAccountIds（只要编号）
                bool migrated = MigrateMarkedTimes(groups, json);

                // 老数据（还没有 order 字段）按旧规则补一遍排序号，保证升级后顺序不变
                SortingService.EnsureGroupOrders(groups);

                foreach (var group in groups)
                    DebugLog($"分组: {group.Name}, ID: {group.Id}, 账号数: {group.AccountCount}, 标记数: {group.MarkedAccountIds?.Count ?? 0}");

                // ★ 迁移过的文件立刻回写一次：这样用户打开 groups.json 看到的就是干净的新格式，
                //   不必等下一次"编辑分组 / 移入账号"之类的操作才顺带刷新。
                if (migrated)
                    SaveGroups(groups);

                DebugLog($"成功加载分组数量: {groups.Count}");
                return groups;
            }
            catch (Exception ex)
            {
                DebugLog($"加载分组数据失败: {ex.Message}");
                return new List<AccountGroup>();
            }
        }

        public static bool SaveGroup(AccountGroup group)
        {
            var groups = LoadGroups();
            var existingGroup = groups.FirstOrDefault(g => g.Id == group.Id);

            if (existingGroup != null)
            {
                UpdateGroup(existingGroup, group);
            }
            else
            {
                groups.Add(group);
            }

            return SaveGroups(groups);
        }

        public static bool SaveGroups(List<AccountGroup> groups)
        {
            try
            {
                foreach (var group in groups)
                {
                    group.AccountIds ??= new List<int>();
                    group.MarkedAccountIds ??= new List<int>();
                }

                string json = JsonSerializer.Serialize(groups, JsonOptions);
                DebugLog($"保存分组数据到: {DataFilePath}");

                File.WriteAllText(DataFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"保存分组数据失败: {ex.Message}");
                return false;
            }
        }

        public static bool SaveGroupPinnedState(int groupId, bool isPinned)
        {
            var groups = LoadGroups();
            var group = groups.FirstOrDefault(g => g.Id == groupId);

            if (group != null)
            {
                group.IsPinned = isPinned;
                return SaveGroups(groups);
            }

            return false;
        }

        public static bool DeleteGroup(int groupId)
        {
            var groups = LoadGroups();
            groups.RemoveAll(g => g.Id == groupId);
            return SaveGroups(groups);
        }

        public static int GetNextId()
        {
            var groups = LoadGroups();
            return groups.Any() ? groups.Max(g => g.Id) + 1 : 1;
        }

        // 置底相关方法（现在直接保存在分组对象中）
        public static bool SaveGroupMarks(AccountGroup group)
        {
            // 已置底的账号编号直接保存在分组对象的 MarkedAccountIds 字段中
            // 所以只需保存整个分组即可
            return SaveGroup(group);
        }

        public static List<int> LoadGroupMarks(int groupId)
        {
            var groups = LoadGroups();
            var group = groups.FirstOrDefault(g => g.Id == groupId);

            if (group != null && group.MarkedAccountIds != null)
            {
                return group.MarkedAccountIds.ToList();
            }

            return new List<int>();
        }

        /// <summary>
        /// 一次性迁移：老文件里的 <c>markedTimes</c> 是「账号 → 置底时间」的字典，
        /// 那个时间从来没被读过（界面只关心在不在集合里），所以这里只把**键**（账号编号）
        /// 搬进 <see cref="AccountGroup.MarkedAccountIds"/>。
        /// ★ 幂等：只在「新字段为空 且 老字段存在」时改；迁移完回写后老字段就没了，下次不再触发。
        /// ★ 反序列化时老字段没有对应属性，会被自动忽略 —— 所以必须直接从原始 json 里取。
        /// </summary>
        private static bool MigrateMarkedTimes(List<AccountGroup> groups, string rawJson)
        {
            if (groups == null || groups.Count == 0 || string.IsNullOrWhiteSpace(rawJson))
                return false;

            bool changed = false;

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;

                int i = 0;
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var group = i < groups.Count ? groups[i] : null;
                    i++;
                    if (group == null) break;

                    // 新格式已经有了就跳过（老数据里 markedAccountIds 不存在 → 反序列化后是空表）
                    if (group.MarkedAccountIds != null && group.MarkedAccountIds.Count > 0) continue;

                    if (element.ValueKind != JsonValueKind.Object) continue;
                    if (!element.TryGetProperty("markedTimes", out var markedTimes)) continue;
                    if (markedTimes.ValueKind != JsonValueKind.Object) continue;

                    var ids = new List<int>();
                    foreach (var prop in markedTimes.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out int id))
                            ids.Add(id);
                    }

                    if (ids.Count > 0)
                    {
                        group.MarkedAccountIds = ids;
                        changed = true;
                        DebugLog($"迁移分组「{group.Name}」的置底记录：{ids.Count} 条（已去掉时间值）");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"迁移 markedTimes 失败: {ex.Message}");
                return false;
            }

            return changed;
        }

        // 辅助方法
        private static void UpdateGroup(AccountGroup target, AccountGroup source)
        {
            target.Name = source.Name;
            target.TagNote = source.TagNote ?? "";
            target.AccountIds = source.AccountIds ?? new List<int>();
            target.MarkedAccountIds = source.MarkedAccountIds ?? new List<int>();
            target.IsPinned = source.IsPinned;
            DebugLog($"更新分组: {source.Name}, 标记数: {source.MarkedAccountIds?.Count ?? 0}");
        }

        private static void DebugLog(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}