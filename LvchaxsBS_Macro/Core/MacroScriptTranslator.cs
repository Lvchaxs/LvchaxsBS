using System;
using System.Collections.Generic;
using System.Text;

namespace LvchaxsBS_Macro.Core
{
    /// <summary>
    /// 宏脚本「英文原码 ↔ 中文别名」的双向翻译。
    /// <para>
    /// ★ 只翻译**行首的指令关键字**，键名 / 参数 / 代码块名 / 花括号一律不动。
    ///   例：<c>KeyDown E</c> ⇄ <c>按键键 E</c>；<c>Delay 20</c> ⇄ <c>延迟 20</c>；
    ///   <c>MouseDown Right</c> ⇄ <c>鼠标按下 Right</c>。
    /// </para>
    /// <para>
    /// ★★ 这**只是编辑器里的显示翻译**：脚本在磁盘上 / 执行引擎里永远是英文原码，
    ///   中文别名只在「中/英」按钮切成中文显示时出现，执行时再翻译回原码跑。
    /// </para>
    /// </summary>
    public static class MacroScriptTranslator
    {
        /// <summary>英文指令名 → 中文别名（显示名）。</summary>
        private static readonly Dictionary<string, string> EnToZh =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["KeyDown"] = "按键键",
                ["KeyUp"] = "抬起键",
                ["KeyPress"] = "按一下",
                ["Delay"] = "延迟",
                ["MouseDown"] = "鼠标按下",
                ["MouseUp"] = "鼠标抬起",
                ["MouseClick"] = "鼠标点击",
                ["Move"] = "移动",
                ["MoveTo"] = "移动到",
                ["Wheel"] = "滚轮",
                ["Text"] = "文本",
            };

        /// <summary>中文别名 → 英文指令名（反向表，由 EnToZh 反转而来）。</summary>
        private static readonly Dictionary<string, string> ZhToEn = BuildReverse();

        private static Dictionary<string, string> BuildReverse()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in EnToZh)
                d[kv.Value] = kv.Key;
            return d;
        }

        /// <summary>所有中文别名（编辑器里给用户看的提示/校验用）。</summary>
        public static IEnumerable<string> ChineseAliases => EnToZh.Values;

        /// <summary>这个单词是不是中文别名（如「延迟」「按键键」）。</summary>
        public static bool IsChineseAlias(string word) => ZhToEn.ContainsKey(word);

        /// <summary>英文指令名 → 中文别名；不是指令就原样返回。</summary>
        public static string ToChinese(string opName)
            => EnToZh.TryGetValue(opName, out var zh) ? zh : opName;

        /// <summary>中文别名 → 英文指令名；不是别名就原样返回。</summary>
        public static string ToEnglish(string word)
        {
            if (ZhToEn.TryGetValue(word, out var en)) return en;

            // ★ 已经是英文指令名时，**统一成标准大小写**（`keydown` → `KeyDown`）。
            //   为什么：英文 → 中文 → 英文 的往返必须幂等，否则
            //   「只切中英」之后内容归一化跟原来不一样 ⇒ 被误判成「有改动」。
            foreach (var kv in EnToZh)
            {
                if (string.Equals(kv.Key, word, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
            return word;
        }

        // ── 鼠标键名（只鼠标指令翻译，键盘键名不翻） ──

        private static readonly Dictionary<string, string> MouseEnToZh =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Left"] = "左键",
                ["Right"] = "右键",
                ["Middle"] = "中键",
            };

        private static readonly Dictionary<string, string> MouseZhToEn = BuildMouseReverse();

        private static Dictionary<string, string> BuildMouseReverse()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in MouseEnToZh) d[kv.Value] = kv.Key;
            return d;
        }

        private static string MouseToChinese(string key)
            => MouseEnToZh.TryGetValue(key, out var zh) ? zh : key;

        private static string MouseToEnglish(string key)
        {
            if (MouseZhToEn.TryGetValue(key, out var en)) return en;

            // ★ 同理：英文鼠标键名统一成标准大小写（`left` → `Left`），保证往返幂等
            foreach (var kv in MouseEnToZh)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
            return key;
        }

        /// <summary>这个词是不是鼠标指令名（英文或中文别名）。</summary>
        private static bool IsMouseOpName(string word)
        {
            switch (word.ToLowerInvariant())
            {
                case "mousedown":
                case "mouseup":
                case "mouseclick":
                case "鼠标按下":
                case "鼠标抬起":
                case "鼠标点击":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 把**一行**脚本翻译掉：行首指令关键字 + 鼠标指令的鼠标键参数。
        /// <para>键盘指令的键名（KeyDown E 的 E）不翻译；鼠标键（MouseDown Right 的 Right → 右键）翻译。</para>
        /// </summary>
        public static string TranslateLine(string line, bool toChinese)
        {
            if (string.IsNullOrEmpty(line)) return line;

            // 行首（可带前导空白）第一个词 = 指令名
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            int opStart = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            if (opStart == i) return line;

            // ★★ **代码块定义行 / 调用行不翻译首词** ——
            //   `单玛头 {`（定义）、`单玛头()`（调用）里的名字是用户自己起的，
            //   万一叫「移动」「延迟」这种跟指令同名的，不能被当成指令翻掉。
            if (IsBlockNameLine(line)) return line;

            string op = line.Substring(opStart, i - opStart);
            string mappedOp = toChinese ? ToChinese(op) : ToEnglish(op);
            bool isMouseOp = IsMouseOpName(op);

            var sb = new StringBuilder(line.Length + 8);
            sb.Append(line, 0, opStart);
            sb.Append(mappedOp);

            if (isMouseOp && i < line.Length)
            {
                // 鼠标指令：翻译第二个词（鼠标键）
                int j = i;
                while (j < line.Length && (line[j] == ' ' || line[j] == '\t')) j++;
                int keyStart = j;
                while (j < line.Length && !char.IsWhiteSpace(line[j])) j++;

                sb.Append(line, i, keyStart - i);   // 指令与键名之间的空白
                if (keyStart < j)
                {
                    string key = line.Substring(keyStart, j - keyStart);
                    sb.Append(toChinese ? MouseToChinese(key) : MouseToEnglish(key));
                }
                sb.Append(line, j, line.Length - j);   // 键名之后（含后续参数）
            }
            else
            {
                sb.Append(line, i, line.Length - i);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 这一行是不是「代码块定义行」或「代码块调用行」。
        /// <para>★ 判据要精确：</para>
        /// <list type="bullet">
        ///   <item>定义：<c>名字 {</c>（去掉尾括号后**只有一个词**、且不含空格）</item>
        ///   <item>调用：<c>名字()</c>（整行就是一个词加一对空括号）</item>
        /// </list>
        /// <para><c>Text 你好)</c> 这种正文带括号的一律**不算**块行，照常翻译。</para>
        /// </summary>
        private static bool IsBlockNameLine(string line)
        {
            string t = line.Trim();

            // 去行尾注释（# 或 //）
            int cut = t.IndexOf('#');
            if (cut >= 0) t = t.Substring(0, cut).Trim();
            else
            {
                cut = t.IndexOf("//", StringComparison.Ordinal);
                if (cut >= 0) t = t.Substring(0, cut).Trim();
            }
            if (t.Length == 0) return false;

            // ---- 定义行：名字 + { ----
            if (t.EndsWith("{") || t.EndsWith("｛"))
            {
                string name = t.Substring(0, t.Length - 1).Trim();
                return name.Length > 0 &&
                       name.IndexOf(' ') < 0 && name.IndexOf('\t') < 0;
            }

            // ---- 调用行：名字() ----
            if (t.EndsWith(")") || t.EndsWith("）"))
            {
                string inner = t.Substring(0, t.Length - 1).TrimEnd();
                if (!inner.EndsWith("(") && !inner.EndsWith("（")) return false;
                string name = inner.Substring(0, inner.Length - 1).Trim();
                return name.Length > 0 &&
                       name.IndexOf(' ') < 0 && name.IndexOf('\t') < 0;
            }

            return false;
        }

        /// <summary>整段脚本翻译（逐行处理，保留换行风格）。</summary>
        public static string Translate(string text, bool toChinese)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string eol = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder(text.Length + 32);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append(eol);
                sb.Append(TranslateLine(lines[i], toChinese));
            }
            return sb.ToString();
        }
    }
}
