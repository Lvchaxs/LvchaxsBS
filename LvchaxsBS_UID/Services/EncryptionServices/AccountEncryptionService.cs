using System;
using System.Text;

namespace LvchaxsBS_UID.Services.EncryptionServices
{
    /// <summary>
    /// 轻量级本地数据混淆工具。
    /// 目的很简单：让直接打开本地账号数据文件（accounts.enc.json）时，看不懂里面的账号与密码内容。
    /// 实现方式为「固定密钥异或 + Base64」，秒级完成、无依赖，不追求密码学强度。
    /// </summary>
    public static class AccountEncryptionService
    {
        /// <summary>混淆后的数据都会带上该前缀，便于识别与解码。</summary>
        private const string Prefix = "enc:";

        /// <summary>简单混淆密钥（写死在程序内）。</summary>
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("LvchaxsBS_UID@2026");

        /// <summary>把明文变为「看不懂」的乱码字符串。</summary>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            byte[] data = Encoding.UTF8.GetBytes(plainText);

            for (int i = 0; i < data.Length; i++)
                data[i] ^= Key[i % Key.Length];

            return Prefix + Convert.ToBase64String(data);
        }

        /// <summary>
        /// 判断字符串是否已经是本程序混淆过的数据（带前缀且 Base64 合法）。
        /// 用于"加载旧文件 → 保存"这类可能重复处理同一段文本的场景，避免二次加密。
        /// </summary>
        public static bool IsEncrypted(string? text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Prefix, StringComparison.Ordinal))
                return false;

            try
            {
                Convert.FromBase64String(text.Substring(Prefix.Length));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>把乱码字符串还原为明文。</summary>
        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            // 没有前缀说明不是本程序混淆过的数据（例如手写的明文），原样返回
            if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
                return cipherText;

            try
            {
                byte[] data = Convert.FromBase64String(cipherText.Substring(Prefix.Length));

                for (int i = 0; i < data.Length; i++)
                    data[i] ^= Key[i % Key.Length];

                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return cipherText;
            }
        }

        /// <summary>界面展示用的打码（与存储混淆无关）。</summary>
        public static string Obfuscate(string text, bool isPassword = false)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (isPassword)
                return new string('●', Math.Min(text.Length, 12));

            if (text.Length <= 4)
                return "****";

            if (text.Contains("@"))
            {
                var parts = text.Split('@');
                if (parts.Length == 2 && parts[0].Length > 3)
                    return $"{parts[0].Substring(0, 3)}●●●●@{parts[1]}";
            }

            if (text.Length > 8)
                return $"{text.Substring(0, 4)}●●●●{text.Substring(text.Length - 4)}";

            return new string('●', Math.Min(text.Length, 8));
        }
    }
}
