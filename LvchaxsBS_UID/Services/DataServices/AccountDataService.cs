using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LvchaxsBS_UID.Models;
using LvchaxsBS_UID.Services.EncryptionServices;
using LvchaxsBS_UID.Utils.File;

namespace LvchaxsBS_UID.Services.DataServices
{
    public class AccountDataService
    {
        private const string DataFileName = "accounts.enc.json";
        private const string LegacyFileName = "accounts.json";
        private static readonly string DataFilePath = AppPaths.ConfigFile(DataFileName);
        private static readonly string LegacyFilePath = AppPaths.ConfigFile(LegacyFileName);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,  // 改为 Never 或 WhenWritingDefault
            Converters = { new JsonStringEnumConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static List<Account> LoadAccounts()
        {
            try
            {
                if (File.Exists(DataFilePath))
                {
                    string json = File.ReadAllText(DataFilePath);
                    DebugLog($"加载账号文件: {DataFilePath}, 长度: {json.Length}");

                    var accounts = JsonSerializer.Deserialize<List<Account>>(json, JsonOptions);
                    if (accounts != null && accounts.Any())
                    {
                        // 文件里的 uid / nickname 是混淆串，统一还原为明文再交给界面。
                        // 旧版本文件里这两项是明文，Decrypt 对无前缀文本原样返回，因此天然兼容。
                        foreach (var account in accounts)
                        {
                            account.Uid = AccountEncryptionService.Decrypt(account.Uid);
                            account.Nickname = AccountEncryptionService.Decrypt(account.Nickname);
                        }

                        DebugLog($"成功加载账号数量: {accounts.Count}");
                        LogFirstAccountInfo(accounts[0]);
                        return accounts;
                    }
                }

                return LoadLegacyAccounts();
            }
            catch (Exception ex)
            {
                DebugLog($"加载账号数据失败: {ex.Message}");
                return new List<Account>();
            }
        }

        private static List<Account> LoadLegacyAccounts()
        {
            if (!File.Exists(LegacyFilePath))
                return new List<Account>();

            try
            {
                string json = File.ReadAllText(LegacyFilePath);
                var legacyAccounts = JsonSerializer.Deserialize<List<LegacyAccount>>(json, JsonOptions);

                if (legacyAccounts == null || !legacyAccounts.Any())
                    return new List<Account>();

                var accounts = legacyAccounts.Select(legacy => new Account
                {
                    Id = legacy.Id,
                    Uid = legacy.Uid ?? "",
                    Level = legacy.Level,
                    Nickname = legacy.Nickname ?? "",
                    Username = legacy.Username ?? "",
                    Password = legacy.Password ?? "",
                    CreatedTime = DateTime.Now, // 设置创建时间为当前时间
                    PinnedTime = null
                }).ToList();

                SaveAccounts(accounts);
                BackupLegacyFile();

                DebugLog("成功迁移并加密旧格式账号数据");
                return accounts;
            }
            catch (Exception ex)
            {
                DebugLog($"加载旧格式账号数据失败: {ex.Message}");
                return new List<Account>();
            }
        }

        public static bool SaveAccounts(List<Account> accounts)
        {
            try
            {
                // 确保所有账号都有创建时间
                foreach (var account in accounts)
                {
                    if (account.CreatedTime == default)
                        account.CreatedTime = DateTime.Now;
                }

                var dataToSave = accounts.Select(a => new
                {
                    a.Id,
                    // uid / nickname 同样以混淆串落盘，避免直接打开文件就能看到账号信息。
                    // 内存里 a.Uid / a.Nickname 始终是明文，只有写文件这一刻才加密。
                    uid = AccountEncryptionService.Encrypt(a.Uid),
                    a.Level,
                    nickname = AccountEncryptionService.Encrypt(a.Nickname),
                    // 头像：资源包里的角色名（必填，空值在 Account 里已回落到「空」）。
                    // 命名策略是 CamelCase，这里会写成 "avatar"，与 Account 上的
                    // [JsonPropertyName("avatar")] 对应。
                    a.Avatar,
                    a.EncryptedUsername,
                    a.EncryptedPassword,
                    a.IsMarked,
                    a.IsPinned,
                    a.CreatedTime,
                    a.PinnedTime,
                    Metadata = new
                    {
                        Version = "2.0",
                        Encryption = "AES-256-CBC",
                        Saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        accounts.Count
                    }
                }).ToList();

                string json = JsonSerializer.Serialize(dataToSave, JsonOptions);
                File.WriteAllText(DataFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog($"保存账号数据失败: {ex.Message}");
                return false;
            }
        }

        public static bool SaveAccountPinnedState(int accountId, bool isPinned, DateTime? pinnedTime = null)
        {
            var accounts = LoadAccounts();
            var account = accounts.FirstOrDefault(a => a.Id == accountId);

            if (account != null)
            {
                account.IsPinned = isPinned;
                account.PinnedTime = isPinned ? pinnedTime ?? DateTime.Now : null;
                return SaveAccounts(accounts);
            }

            return false;
        }

        public static bool SaveAccount(Account account)
        {
            var accounts = LoadAccounts();
            var existingAccount = accounts.FirstOrDefault(a => a.Id == account.Id);

            if (existingAccount != null)
            {
                UpdateAccount(existingAccount, account);
            }
            else
            {
                account.Id = GetNextId();
                // 确保新账号有创建时间
                if (account.CreatedTime == default)
                    account.CreatedTime = DateTime.Now;
                accounts.Add(account);
            }

            return SaveAccounts(accounts);
        }

        public static bool DeleteAccount(int accountId)
        {
            var accounts = LoadAccounts();
            accounts.RemoveAll(a => a.Id == accountId);
            return SaveAccounts(accounts);
        }

        public static bool DeleteAccounts(List<int> accountIds)
        {
            var accounts = LoadAccounts();
            accounts.RemoveAll(a => accountIds.Contains(a.Id));
            return SaveAccounts(accounts);
        }

        public static int GetNextId()
        {
            var accounts = LoadAccounts();
            return accounts.Any() ? accounts.Max(a => a.Id) + 1 : 1;
        }

        public static int GetAccountCount()
        {
            return LoadAccounts().Count;
        }

        public static List<Account> SearchAccounts(string searchTerm)
        {
            var accounts = LoadAccounts();

            if (string.IsNullOrWhiteSpace(searchTerm))
                return accounts;

            return accounts.Where(a =>
                (a.Uid?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.Nickname?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.Username?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToList();
        }

        // 辅助方法
        private static void UpdateAccount(Account target, Account source)
        {
            target.Uid = source.Uid;
            target.Level = source.Level;
            target.Nickname = source.Nickname;
            target.Username = source.Username;
            target.Password = source.Password;
            // 头像也要一起搬过来，否则「编辑账号」保存后头像会被旧值覆盖回「空」
            target.Avatar = source.Avatar;
            target.IsMarked = source.IsMarked;
            target.IsPinned = source.IsPinned;
            target.PinnedTime = source.PinnedTime;
        }

        private static void BackupLegacyFile()
        {
            try
            {
                string backupName = $"accounts.legacy.{DateTime.Now:yyyyMMddHHmmss}.json";
                string backupPath = AppPaths.ConfigFile(backupName);
                File.Copy(LegacyFilePath, backupPath, true);
                DebugLog($"已备份旧格式数据到: {backupName}");
            }
            catch { }
        }

        private static void DebugLog(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }

        private static void LogFirstAccountInfo(Account account)
        {
            DebugLog($"第一个账号信息 - ID: {account.Id}, UID: {account.Uid}, 创建时间: {account.CreatedTime}");
        }

        private class LegacyAccount
        {
            public int Id { get; set; }
            public string? Uid { get; set; }
            public int Level { get; set; }
            public string? Nickname { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
        }
    }
}