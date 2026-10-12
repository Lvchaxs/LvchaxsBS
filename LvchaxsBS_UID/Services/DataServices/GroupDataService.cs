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

                // 确保每个分组都有必要的数据结构
                foreach (var group in groups)
                {
                    group.AccountIds ??= new List<int>();
                    group.AccountAddTimes ??= new Dictionary<int, DateTime>();
                    group.MarkedTimes ??= new Dictionary<int, DateTime>();

                    // 如果创建时间为默认值，设置为当前时间
                    if (group.CreatedTime == default)
                        group.CreatedTime = DateTime.Now;

                    DebugLog($"分组: {group.Name}, ID: {group.Id}, 账号数: {group.AccountCount}, 标记数: {group.MarkedTimes.Count}");
                }

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
                // 确保新分组有创建时间
                if (group.CreatedTime == default)
                    group.CreatedTime = DateTime.Now;
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
                    group.AccountAddTimes ??= new Dictionary<int, DateTime>();
                    group.MarkedTimes ??= new Dictionary<int, DateTime>();

                    // 确保每个分组都有创建时间
                    if (group.CreatedTime == default)
                        group.CreatedTime = DateTime.Now;
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

        public static bool SaveGroupPinnedState(int groupId, bool isPinned, DateTime? pinnedTime = null)
        {
            var groups = LoadGroups();
            var group = groups.FirstOrDefault(g => g.Id == groupId);

            if (group != null)
            {
                group.IsPinned = isPinned;
                group.PinnedTime = isPinned ? pinnedTime ?? DateTime.Now : null;
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

        // 标记相关方法（现在直接保存在分组对象中）
        public static bool SaveGroupMarks(AccountGroup group)
        {
            // 标记时间现在直接保存在分组对象的MarkedTimes字段中
            // 所以只需保存整个分组即可
            return SaveGroup(group);
        }

        public static List<int> LoadGroupMarks(int groupId)
        {
            var groups = LoadGroups();
            var group = groups.FirstOrDefault(g => g.Id == groupId);

            if (group != null && group.MarkedTimes != null)
            {
                return group.MarkedTimes.Keys.ToList();
            }

            return new List<int>();
        }

        // 辅助方法
        private static void UpdateGroup(AccountGroup target, AccountGroup source)
        {
            target.Name = source.Name;
            target.TagNote = source.TagNote ?? "";
            target.AccountIds = source.AccountIds ?? new List<int>();
            target.AccountAddTimes = source.AccountAddTimes ?? new Dictionary<int, DateTime>();
            target.MarkedTimes = source.MarkedTimes ?? new Dictionary<int, DateTime>();
            target.IsPinned = source.IsPinned;
            target.PinnedTime = source.PinnedTime;
            DebugLog($"更新分组: {source.Name}, 标记数: {source.MarkedTimes?.Count ?? 0}");
        }

        private static void DebugLog(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}