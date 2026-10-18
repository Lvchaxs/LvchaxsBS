using System;
using System.Collections.Generic;
using System.Text;

namespace LvchaxsBS_Macro.Core
{
    /// <summary>一条指令的操作类型。</summary>
    public enum MacroOp
    {
        // 键盘
        KeyDown,
        KeyUp,
        KeyPress,

        // 时间
        Delay,

        // 鼠标
        MouseDown,
        MouseUp,
        MouseClick,
        Move,
        MoveTo,
        Wheel,

        // 输入
        Text,
    }

    /// <summary>
    /// 解析出来的一条指令。
    /// <para>★ 带行号与错误信息 —— 编辑器/执行引擎都能指出"第几行写错了"。</para>
    /// </summary>
    public readonly struct MacroCommand
    {
        public MacroOp Op { get; init; }

        /// <summary>键名（KeyDown/KeyUp/KeyPress 用）。</summary>
        public string KeyName { get; init; }

        /// <summary>第一个数值参数（Delay 毫秒 / Wheel 格数 / Move 的 dx / MoveTo 的 x）。</summary>
        public int Arg1 { get; init; }

        /// <summary>第二个数值参数（Move 的 dy / MoveTo 的 y）。</summary>
        public int Arg2 { get; init; }

        /// <summary>KeyPress 按住的毫秒数。</summary>
        public int HoldMs { get; init; }

        /// <summary>鼠标按键（Mouse* 用）。</summary>
        public SimulationService.MouseButtonKind Button { get; init; }

        /// <summary>要敲进去的文本（Text 用）。</summary>
        public string Text { get; init; }

        /// <summary>行号（从 1 开始，给报错用）。</summary>
        public int LineNo { get; init; }

        /// <summary>这一行语法/参数有没有问题。</summary>
        public bool IsValid { get; init; }

        /// <summary>无效时的原因。</summary>
        public string Error { get; init; }

        public MacroCommand()
        {
            Op = MacroOp.Delay;
            KeyName = "";
            Arg1 = 0;
            Arg2 = 0;
            HoldMs = 0;
            Button = SimulationService.MouseButtonKind.Left;
            Text = "";
            LineNo = 0;
            IsValid = true;
            Error = "";
        }
    }

    /// <summary>
    /// 宏指令脚本的**语法定义与解析器**。
    ///
    /// <para>
    /// 语法（一行一条，指令名不分大小写；<c>#</c> 或 <c>//</c> 是注释）：
    /// <code>
    /// KeyDown  W            按住 W（不抬起）
    /// KeyUp    W            抬起 W
    /// KeyPress F 50         按一下 F（按住 50 毫秒再抬起；不写第二个参数就是按一下）
    /// Delay    100          等 100 毫秒
    /// MouseDown Left        按住鼠标左键
    /// MouseUp   Left        松开鼠标左键
    /// MouseClick Right      点一下右键
    /// Move     10 -20       鼠标相对移动（x 右为正，y 下为正）
    /// MoveTo   800 600      鼠标移到屏幕绝对坐标
    /// Wheel    3            滚轮滚 3 格（负数 = 向下）
    /// Text     hello        敲入文本
    /// </code>
    /// </para>
    ///
    /// <para>
    /// ★ 键名用 <see cref="SimulationService.TryGetKeyCode"/> 校验（A-Z / 0-9 / F1-F24 /
    /// Space / Shift / Ctrl / Alt / Enter / Esc / Tab / 方向键…都认，不分大小写）。
    /// </para>
    /// </summary>
    public static class MacroScript
    {
        /// <summary>
        /// 指令关键字 —— 顺序必须和 <see cref="MacroOp"/> 一致（编辑器着色也读它）。
        /// </summary>
        public static readonly string[] OpNames =
        {
            "KeyDown", "KeyUp", "KeyPress",
            "Delay",
            "MouseDown", "MouseUp", "MouseClick",
            "Move", "MoveTo", "Wheel",
            "Text",
        };

        /// <summary>鼠标按键名（不分大小写）。</summary>
        public static readonly string[] ButtonNames = { "Left", "Middle", "Right" };

        /// <summary>这个单词是不是指令关键字（编辑器着色用，不分大小写）。</summary>
        public static bool IsOpKeyword(string word)
        {
            foreach (string op in OpNames)
                if (string.Equals(op, word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>这个单词是不是鼠标按键名（编辑器着色用）。</summary>
        public static bool IsButtonName(string word)
        {
            foreach (string b in ButtonNames)
                if (string.Equals(b, word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>新建宏时给的默认脚本（顺便当语法示例）。</summary>
        public const string Sample =
            "# 自定义宏指令脚本：一行一条，# 或 // 开头是注释\r\n" +
            "# 触发键在主界面「自定义宏」卡片上设置\r\n" +
            "\r\n" +
            "KeyPress F 30          # 按一下 F（按住 30 毫秒）\r\n" +
            "Delay 100              # 等 100 毫秒\r\n" +
            "MouseClick Left        # 点一下鼠标左键\r\n" +
            "Wheel -1               # 滚轮向下 1 格\r\n";

        /// <summary>编辑器右上角的说明（给用户的备忘）。</summary>
        public const string HelpText =
            "KeyDown 键名 —— 按住不抬起；KeyUp 键名 —— 抬起；KeyPress 键名 [毫秒] —— 按一下\r\n" +
            "Delay 毫秒 —— 等待；MouseDown/MouseUp/MouseClick Left|Middle|Right —— 鼠标键\r\n" +
            "Move dx dy —— 相对移动；MoveTo x y —— 移到屏幕坐标；Wheel 格数 —— 滚轮（负=下）\r\n" +
            "Text 文本 —— 敲入文本；# 或 // 开头是注释";

        /// <summary>
        /// 解析整段脚本 → 指令列表（**只包含能执行的行**；行号保留给报错用）。
        /// </summary>
        public static List<MacroCommand> Parse(string? script)
        {
            var result = new List<MacroCommand>();
            if (string.IsNullOrWhiteSpace(script)) return result;

            string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                MacroCommand? cmd = ParseLine(lines[i], i + 1);
                if (cmd != null) result.Add(cmd.Value);
            }

            return result;
        }

        /// <summary>
        /// 解析结果 + 错误行（编辑器用它标红）。
        /// </summary>
        public static List<MacroCommand> ParseWithErrors(string? script, out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(script)) return new List<MacroCommand>();

            var result = new List<MacroCommand>();
            string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                MacroCommand? cmd = ParseLine(lines[i], i + 1);
                if (cmd == null) continue;

                if (!cmd.Value.IsValid) errors.Add($"第 {i + 1} 行：{cmd.Value.Error}");
                else result.Add(cmd.Value);
            }

            return result;
        }

        /// <summary>
        /// 解析一行。返回 null = 空行/注释（直接忽略）；
        /// 返回 <c>IsValid=false</c> 的项 = 这行写错了（不执行，但能报错）。
        /// </summary>
        private static MacroCommand? ParseLine(string rawLine, int lineNo)
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0) return null;

            // 切出指令名
            int sp = IndexOfWhiteSpace(line);
            string opWord = sp < 0 ? line : line.Substring(0, sp);
            string rest = sp < 0 ? "" : line.Substring(sp).Trim();

            if (!TryMatchOp(opWord, out MacroOp op))
            {
                return Fail(op, lineNo, $"不认识的指令「{opWord}」");
            }

            // 参数：按空白切；Text 直接要剩下的原文
            string[] parts = op == MacroOp.Text
                ? new[] { rest }
                : rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            var cmd = new MacroCommand { Op = op, LineNo = lineNo };

            switch (op)
            {
                case MacroOp.KeyDown:
                case MacroOp.KeyUp:
                case MacroOp.KeyPress:
                {
                    if (parts.Length == 0 || parts[0].Length == 0)
                        return Fail(op, lineNo, $"{OpNames[(int)op]} 后面要写键名，例如「{OpNames[(int)op]} W」");

                    string keyName = parts[0];
                    if (!SimulationService.TryGetKeyCode(keyName, out _))
                        return Fail(op, lineNo, $"不认识的键名「{keyName}」");

                    int holdMs = 0;
                    if (op == MacroOp.KeyPress && parts.Length >= 2 && !TryInt(parts[1], out holdMs))
                        return Fail(op, lineNo, $"按住毫秒数「{parts[1]}」不是整数");

                    return new MacroCommand
                    {
                        Op = op, KeyName = keyName, HoldMs = Math.Max(0, holdMs), LineNo = lineNo, IsValid = true,
                    };
                }

                case MacroOp.Delay:
                case MacroOp.Wheel:
                {
                    if (parts.Length == 0 || !TryInt(parts[0], out int value))
                        return Fail(op, lineNo, $"{OpNames[(int)op]} 后面要写一个整数，例如「{OpNames[(int)op]} {(op == MacroOp.Delay ? "100" : "1")}」");

                    return new MacroCommand { Op = op, Arg1 = value, LineNo = lineNo, IsValid = true };
                }

                case MacroOp.MouseDown:
                case MacroOp.MouseUp:
                case MacroOp.MouseClick:
                {
                    if (parts.Length == 0 || !TryMatchButton(parts[0], out var button))
                        return Fail(op, lineNo, $"{OpNames[(int)op]} 后面要写 Left / Middle / Right");

                    return new MacroCommand { Op = op, Button = button, LineNo = lineNo, IsValid = true };
                }

                case MacroOp.Move:
                case MacroOp.MoveTo:
                {
                    if (parts.Length < 2 || !TryInt(parts[0], out int x) || !TryInt(parts[1], out int y))
                        return Fail(op, lineNo, $"{OpNames[(int)op]} 后面要写两个整数，例如「{OpNames[(int)op]} {(op == MacroOp.Move ? "10 -20" : "800 600")}」");

                    return new MacroCommand { Op = op, Arg1 = x, Arg2 = y, LineNo = lineNo, IsValid = true };
                }

                case MacroOp.Text:
                {
                    if (rest.Length == 0)
                        return Fail(op, lineNo, "Text 后面要写要输入的文本");

                    return new MacroCommand { Op = op, Text = rest, LineNo = lineNo, IsValid = true };
                }

                default:
                    return Fail(op, lineNo, "不支持的指令");
            }
        }

        private static MacroCommand Fail(MacroOp op, int lineNo, string error)
            => new() { Op = op, LineNo = lineNo, IsValid = false, Error = error };

        /// <summary>去掉行尾注释（<c>#</c> 或 <c>//</c>；行首就是注释的整行也会变成空）。</summary>
        private static string StripComment(string line)
        {
            int hash = line.IndexOf('#');
            int slash = line.IndexOf("//", StringComparison.Ordinal);

            int cut = -1;
            if (hash >= 0 && slash >= 0) cut = Math.Min(hash, slash);
            else if (hash >= 0) cut = hash;
            else if (slash >= 0) cut = slash;

            return cut < 0 ? line : line.Substring(0, cut);
        }

        private static int IndexOfWhiteSpace(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] == ' ' || s[i] == '\t') return i;
            return -1;
        }

        private static bool TryMatchOp(string word, out MacroOp op)
        {
            for (int i = 0; i < OpNames.Length; i++)
            {
                if (string.Equals(OpNames[i], word, StringComparison.OrdinalIgnoreCase))
                {
                    op = (MacroOp)i;
                    return true;
                }
            }
            op = MacroOp.Delay;
            return false;
        }

        private static bool TryMatchButton(string word, out SimulationService.MouseButtonKind button)
        {
            if (string.Equals(word, "Left", StringComparison.OrdinalIgnoreCase)) { button = SimulationService.MouseButtonKind.Left; return true; }
            if (string.Equals(word, "Middle", StringComparison.OrdinalIgnoreCase)) { button = SimulationService.MouseButtonKind.Middle; return true; }
            if (string.Equals(word, "Right", StringComparison.OrdinalIgnoreCase)) { button = SimulationService.MouseButtonKind.Right; return true; }

            button = SimulationService.MouseButtonKind.Left;
            return false;
        }

        private static bool TryInt(string? s, out int value)
            => int.TryParse(s, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
