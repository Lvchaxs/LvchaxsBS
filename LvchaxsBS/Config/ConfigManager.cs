using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using LvchaxsBS.Config.FunctionConfigs;

namespace LvchaxsBS.Config
{
    public static class ConfigManager
    {
        private static readonly string ConfigFolder;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // ===== 核心：基础数据 + 缓存 =====
        // _configs: 类型 -> 从磁盘加载的配置实例（基础数据）
        // _cache  : 类型 -> 当前生效的配置实例（读取都走这里）
        // _paths  : 类型 -> 磁盘路径
        private static readonly Dictionary<Type, object> _configs = new();
        private static readonly Dictionary<Type, object> _cache = new();
        private static readonly Dictionary<Type, string> _paths = new();

        /// <summary>
        /// 全局缓存是否就绪（所有配置已加载）
        /// </summary>
        public static bool CacheReady { get; private set; } = false;

        static ConfigManager()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            ConfigFolder = Path.Combine(appDirectory, "Config");
        }

        #region 注册与扫描

        /// <summary>
        /// 扫描程序集中所有带 [ConfigFile] 的配置类，注册类型 -> 路径。
        /// </summary>
        private static void EnsureRegistered()
        {
            if (_paths.Count > 0) return;

            Assembly assembly = typeof(ConfigManager).Assembly;

            IEnumerable<Type> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null)!;
            }

            foreach (var type in types)
            {
                if (type == null || !type.IsClass || type.IsAbstract) continue;

                var attr = type.GetCustomAttribute<ConfigFileAttribute>(inherit: false);
                if (attr == null) continue;

                string fullPath = Path.Combine(ConfigFolder, attr.FileName);
                _paths[type] = fullPath;
            }
        }

        #endregion

        #region 一次性加载 / 缓存生成

        /// <summary>
        /// 加载所有配置到 _configs，生成 _cache，并核对清理废弃字段。
        /// 在 App.OnStartup 里调用一次即可。
        /// </summary>
        public static void LoadAll()
        {
            EnsureRegistered();

            _configs.Clear();
            _cache.Clear();

            foreach (var kvp in _paths)
            {
                Type type = kvp.Key;
                string path = kvp.Value;

                object instance = LoadOrCreate(type, path);
                _configs[type] = instance;
                _cache[type] = instance;   // 首次缓存 = 基础数据
            }

            CacheReady = true;

            // 核对清理：把内存实例重新写回磁盘，
            // 自动丢弃"类里没有、JSON 里有"的废弃字段。
            PurgeDeprecatedFields();
        }

        private static object LoadOrCreate(Type type, string path)
        {
            EnsureDirectory(path);

            if (!File.Exists(path))
            {
                object def = Activator.CreateInstance(type)!;
                SaveToFile(type, def, path);
                return def;
            }

            try
            {
                string json = File.ReadAllText(path);
                object? obj = JsonSerializer.Deserialize(json, type);
                return obj ?? Activator.CreateInstance(type)!;
            }
            catch
            {
                object def = Activator.CreateInstance(type)!;
                SaveToFile(type, def, path);
                return def;
            }
        }

        /// <summary>
        /// 核对清理：将所有配置实例重新序列化并写回磁盘。
        /// System.Text.Json 只序列化类中实际存在的属性，
        /// JSON 里多出来的旧字段会在重写时被自动丢弃。
        /// </summary>
        private static void PurgeDeprecatedFields()
        {
            foreach (var kvp in _configs)
            {
                Type type = kvp.Key;
                if (!_paths.TryGetValue(type, out var path)) continue;

                try
                {
                    SaveToFile(type, kvp.Value, path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"配置核对清理失败 [{type.Name}]: {ex.Message}");
                }
            }
        }

        #endregion

        #region 泛型读取 / 保存

        /// <summary>
        /// 从缓存读取配置。缓存未就绪时自动触发 LoadAll。
        /// </summary>
        public static T Get<T>() where T : class, new()
        {
            if (_cache.TryGetValue(typeof(T), out var cached))
                return (T)cached;

            if (!CacheReady)
                LoadAll();

            if (_cache.TryGetValue(typeof(T), out cached))
                return (T)cached;

            // 兜底：类型未注册 [ConfigFile] 或注册失败
            var def = new T();
            _configs[typeof(T)] = def;
            _cache[typeof(T)] = def;
            return def;
        }

        /// <summary>
        /// 保存配置到磁盘，并刷新缓存。
        /// </summary>
        public static void Save<T>(T settings) where T : class, new()
        {
            EnsureRegistered();

            if (!_paths.TryGetValue(typeof(T), out var path))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"类型 {typeof(T).Name} 未标注 [ConfigFile]，仅更新内存。");
                _configs[typeof(T)] = settings;
                _cache[typeof(T)] = settings;
                return;
            }

            _configs[typeof(T)] = settings;
            _cache[typeof(T)] = settings;
            SaveToFile(typeof(T), settings, path);
        }

        /// <summary>
        /// 用当前缓存里的实例直接保存。
        /// </summary>
        public static void Save<T>() where T : class, new()
        {
            Save(Get<T>());
        }

        /// <summary>
        /// 从磁盘强制重新加载指定类型（不走缓存）。
        /// </summary>
        public static void Reload<T>() where T : class, new()
        {
            EnsureRegistered();

            if (!_paths.TryGetValue(typeof(T), out var path))
                return;

            object instance = LoadOrCreate(typeof(T), path);
            _configs[typeof(T)] = instance;
            _cache[typeof(T)] = instance;
        }

        private static void SaveToFile(Type type, object instance, string path)
        {
            EnsureDirectory(path);
            string json = JsonSerializer.Serialize(instance, type, JsonOptions);
            File.WriteAllText(path, json);
        }

        private static void EnsureDirectory(string filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        #endregion
    }
}