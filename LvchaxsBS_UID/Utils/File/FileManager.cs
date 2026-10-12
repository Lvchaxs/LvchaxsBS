using System;
using System.IO;
using System.Text.Json;

namespace LvchaxsBS_UID.Utils.File
{
    public static class FileManager
    {
        public static bool SaveJson<T>(string fileName, T data)
        {
            try
            {
                string filePath = AppPaths.ConfigFile(fileName);
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                System.IO.File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存文件失败 {fileName}: {ex.Message}");
                return false;
            }
        }

        public static T LoadJson<T>(string fileName, T defaultValue = default)
        {
            try
            {
                string filePath = AppPaths.ConfigFile(fileName);

                if (!System.IO.File.Exists(filePath))
                    return defaultValue;

                string json = System.IO.File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载文件失败 {fileName}: {ex.Message}");
                return defaultValue;
            }
        }

        public static bool FileExists(string fileName)
        {
            string filePath = AppPaths.ConfigFile(fileName);
            return System.IO.File.Exists(filePath);
        }

        public static bool DeleteFile(string fileName)
        {
            try
            {
                string filePath = AppPaths.ConfigFile(fileName);

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"删除文件失败 {fileName}: {ex.Message}");
                return false;
            }
        }

        public static string GetAppDataFolder()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "LvchaxsBS_UID");

            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);

            return appFolder;
        }
    }
}