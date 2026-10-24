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

        /// <summary>
        /// 调用另一个代码块（**内部标记**，只在展开前存在于块里，展开后不会出现在执行序列里）。
        /// </summary>
        Call,
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
    /// Text     你好          粘贴一段文本（走剪贴板，中文没问题）
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
            "__Call",       // 内部标记（用户写不出来的名字，避免和指令冲突）
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

        // ============ 代码块：自定义名字 + 调用 ============

        /// <summary>
        /// 入口块的名字（**两个都认**，用户爱用哪个用哪个）：
        /// <list type="bullet">
        ///   <item><c>main</c> —— 短、不会和用户自己起的块名撞车</item>
        ///   <item><c>入口</c> —— 想让脚本全中文时用这个</item>
        /// </list>
        /// <para>不分大小写。有入口块时**只执行入口里的内容**；没有就退回"执行第一个块"，老脚本照样能跑。</para>
        /// </summary>
        public static readonly string[] EntryBlockNames = { "main", "入口" };

        /// <summary>
        /// 一个代码块（像函数）：自定义名字 + <c>{ }</c> 包住的指令。
        /// <para>★ 名字和触发方式**没有任何关系** —— 触发方式只决定"跑一遍还是循环"。</para>
        /// </summary>
        public sealed class MacroBlock
        {
            /// <summary>块名（用户自己起的，如「单玛头」）。</summary>
            public string Name { get; init; } = "";

            /// <summary>块头所在行（1 基，报错用）。</summary>
            public int HeaderLine { get; init; }

            /// <summary>块内能执行的指令（语法错的行不进来）。</summary>
            public List<MacroCommand> Commands { get; } = new();
        }

        /// <summary>一次调用：块外单独写一行块名。</summary>
        public sealed class MacroCall
        {
            /// <summary>被调用的块名。</summary>
            public string Name { get; init; } = "";

            /// <summary>这一行在第几行（1 基）。</summary>
            public int LineNo { get; init; }
        }

        /// <summary>整篇脚本的结构化解析结果。</summary>
        public sealed class MacroScriptDoc
        {
            /// <summary>所有代码块定义（按出现顺序）。</summary>
            public List<MacroBlock> Blocks { get; } = new();

            /// <summary>块外的调用（按出现顺序 —— 这就是执行顺序）。</summary>
            public List<MacroCall> Calls { get; } = new();

            /// <summary>
            /// 块外写的东西，**按出现顺序**（普通指令 + <see cref="MacroOp.Call"/> 调用混在一起）。
            /// <para>
            /// ★ 没有 main/入口 时，这里就是"顶层流程"，会原样按顺序执行；
            /// 有入口时它不参与执行（<see cref="Errors"/> 里会提示）。
            /// </para>
            /// </summary>
            public List<MacroCommand> Loose { get; } = new();

            /// <summary>语法/结构错误（"第 N 行：原因"）。</summary>
            public List<string> Errors { get; } = new();

            /// <summary>
            /// 脚本里**出现过块头**（哪怕块写坏了、被作废了）。
            /// <para>
            /// ★ 关键：判断"要不要退回旧的全篇执行"必须看它 —— 只看 <see cref="HasBlocks"/> 的话，
            /// 块写坏（被 Remove）后会变回"没有块"，于是整篇又按旧脚本跑一遍，
            /// 把真正的结构错误盖成"不认识的指令"，用户根本看不懂。
            /// </para>
            /// </summary>
            public bool SawBlockHeader { get; set; }

            /// <summary>这篇脚本**按块来解释**（有有效块，或者写了块头但写坏了）。</summary>
            public bool UsesBlocks => Blocks.Count > 0 || SawBlockHeader;

            /// <summary>定义了块没有。</summary>
            public bool HasBlocks => Blocks.Count > 0;

            /// <summary>按名字找块（不分大小写）。</summary>
            public MacroBlock? FindBlock(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;

                string t = name.Trim();
                foreach (MacroBlock b in Blocks)
                    if (string.Equals(b.Name, t, StringComparison.OrdinalIgnoreCase)) return b;
                return null;
            }

            /// <summary>取入口块（<c>main</c> 或 <c>入口</c>，先到先得）。</summary>
            public MacroBlock? FindEntryBlock()
            {
                foreach (string name in EntryBlockNames)
                {
                    MacroBlock? block = FindBlock(name);
                    if (block != null) return block;
                }
                return null;
            }
        }

        /// <summary>
        /// 这一行像不像「块名」或「调用」—— 编辑器着色用。
        /// <para>
        /// 判定：去掉行尾 <c>{</c> 之后只剩**一个词**，且不是指令关键字。
        /// 于是 <c>MouseDown Left</c>（两个词）不算、<c>Delay</c>（关键字）不算、
        /// <c>单玛头</c> / <c>单玛头 {</c> 算。
        /// </para>
        /// </summary>
        public static bool IsBlockHeaderLine(string? rawLine)
        {
            string t = StripComment(rawLine ?? "").Trim();
            if (t.Length == 0) return false;

            // ⚠️ 单独一个 { 或 } 是花括号行，不是块名（否则 } 会被当成块名染成主色）
            if (IsOnlyOpenBrace(t) || IsOnlyCloseBrace(t)) return false;

            if (t.EndsWith("{") || t.EndsWith("｛")) t = t.Substring(0, t.Length - 1).Trim();
            if (t.Length == 0) return false;

            if (t.IndexOf(' ') >= 0 || t.IndexOf('\t') >= 0) return false;
            return !IsOpKeyword(t);
        }

        /// <summary>行尾是 <c>{</c>（半角或全角）。</summary>
        private static bool EndsWithOpenBrace(string s)
            => s.EndsWith("{") || s.EndsWith("｛");

        /// <summary>整行就是一个 <c>{</c>。</summary>
        private static bool IsOnlyOpenBrace(string s) => s == "{" || s == "｛";

        /// <summary>整行就是一个 <c>}</c>。</summary>
        private static bool IsOnlyCloseBrace(string s) => s == "}" || s == "｝";

        /// <summary>
        /// 把整篇脚本解析成「代码块定义 + 调用 + 块外指令 + 错误」。
        /// <para>语法（名字随便起，和触发方式无关）：</para>
        /// <code>
        /// 单玛头                  ← 块外单独写一行名字 = 调用它
        /// 单玛头                  ← 定义：名字 + { 内容 }
        /// {
        ///     MouseDown Left
        ///     Delay 180
        /// }
        /// </code>
        /// <para>
        /// ★ 定义和调用**先后顺序无所谓**（先解析出所有定义，再按调用顺序展开）。
        /// ★ 只定义了块、一行调用都没写 ⇒ 触发时执行**第一个块**。
        /// ★ 全篇没有任何块结构 ⇒ 按老规矩把全篇当一段执行（旧脚本不受影响）。
        /// </para>
        /// </summary>
        public static MacroScriptDoc ParseDoc(string? script)
        {
            var doc = new MacroScriptDoc();
            if (string.IsNullOrWhiteSpace(script)) return doc;

            string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            MacroBlock? current = null;                     // 正在往里放指令的块
            (string name, int lineNo)? pendingName = null;  // 刚看到一行裸名字，下一行才知道是"定义"还是"调用"

            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string t = StripComment(lines[i]).Trim();
                if (t.Length == 0) continue;      // 空行 / 纯注释

                // ---------- 单独一个 { ----------
                if (IsOnlyOpenBrace(t))
                {
                    // 出现过花括号结构就算"块脚本" —— 哪怕结构写坏了也**绝不能退回全篇执行**
                    // （否则 { } 会被当成指令一样发出去，乱按键比什么都不做危险得多）
                    doc.SawBlockHeader = true;

                    if (pendingName != null)
                    {
                        current = StartBlock(doc, pendingName.Value.name, pendingName.Value.lineNo);
                        pendingName = null;
                    }
                    else if (current == null)
                    {
                        doc.Errors.Add($"第 {lineNo} 行：多余的「{{」（前面没有代码块名字）");
                    }
                    // 块里再出现 { ⇒ 暂不支持嵌套，忽略这一行
                    continue;
                }

                // ---------- 单独一个 } ----------
                if (IsOnlyCloseBrace(t))
                {
                    doc.SawBlockHeader = true;

                    if (current != null) current = null;
                    else doc.Errors.Add($"第 {lineNo} 行：多余的「}}」（没有已打开的代码块）");
                    continue;
                }

                // ---------- 上一行的裸名字，这一行不是 { ⇒ 它是一次调用 ----------
                if (pendingName != null)
                {
                    AddTopLevelCall(doc, pendingName.Value.name, pendingName.Value.lineNo);
                    pendingName = null;
                }

                // ---------- 块内：调用 or 指令（一行可以写多条，用分号分隔）----------
                if (current != null)
                {
                    foreach (string stmt in SplitStatements(lines[i]))
                    {
                        string st = stmt.Trim();
                        if (st.Length == 0) continue;

                        // 「名字()」= 调用另一个块，占位记下来，等展开时**就地代入**它的指令
                        if (TryParseCall(st, out string callee))
                        {
                            current.Commands.Add(new MacroCommand
                            {
                                Op = MacroOp.Call, KeyName = callee, LineNo = lineNo, IsValid = true,
                            });
                            continue;
                        }

                        MacroCommand? inner = ParseLine(st, lineNo);
                        if (inner == null) continue;
                        if (!inner.Value.IsValid) { doc.Errors.Add($"第 {lineNo} 行：{inner.Value.Error}"); continue; }
                        current.Commands.Add(inner.Value);
                    }
                    continue;
                }

                // ---------- 块外 ----------
                // 一行多条（分号分隔）⇒ 逐段当顶层内容处理
                List<string> statements = SplitStatements(lines[i]);
                if (statements.Count > 1)
                {
                    foreach (string stmt in statements)
                    {
                        string st = stmt.Trim();
                        if (st.Length == 0) continue;

                        if (TryParseCall(st, out string multiCallee))
                        {
                            AddTopLevelCall(doc, multiCallee, lineNo);
                            continue;
                        }

                        MacroCommand? multi = ParseLine(st, lineNo);
                        if (multi == null) continue;
                        if (!multi.Value.IsValid) { doc.Errors.Add($"第 {lineNo} 行：{multi.Value.Error}"); continue; }
                        doc.Loose.Add(multi.Value);
                    }
                    continue;
                }

                // 「名字()」= 顶层调用（按出现顺序参与顶层流程）
                if (TryParseCall(t, out string topCallee))
                {
                    doc.Calls.Add(new MacroCall { Name = topCallee, LineNo = lineNo });
                    doc.Loose.Add(new MacroCommand
                    {
                        Op = MacroOp.Call, KeyName = topCallee, LineNo = lineNo, IsValid = true,
                    });
                    continue;
                }

                // 以指令关键字开头 ⇒ 普通指令（旧脚本，以及块外零散写的指令）
                string firstWord = FirstWord(t);
                if (IsOpKeyword(firstWord))
                {
                    MacroCommand? cmd = ParseLine(lines[i], lineNo);
                    if (cmd == null) continue;
                    if (!cmd.Value.IsValid) { doc.Errors.Add($"第 {lineNo} 行：{cmd.Value.Error}"); continue; }
                    doc.Loose.Add(cmd.Value);
                    continue;
                }

                // 同行花括号的定义：`单玛头 {`
                if (EndsWithOpenBrace(t))
                {
                    string name = t.Substring(0, t.Length - 1).Trim();
                    current = StartBlock(doc, name, lineNo);
                    continue;
                }

                // 裸名字：先记着 —— 下一行是 { 就是定义头，否则它就是一次调用
                pendingName = (t, lineNo);
            }

            // ---------- 收尾 ----------
            if (pendingName != null)
                AddTopLevelCall(doc, pendingName.Value.name, pendingName.Value.lineNo);

            if (current != null)
                doc.Errors.Add($"第 {current.HeaderLine} 行：「{current.Name}」这个块没有用「}}」结束");

            // 调用必须能找到对应定义
            foreach (MacroCall call in doc.Calls)
            {
                if (doc.FindBlock(call.Name) == null)
                    doc.Errors.Add($"第 {call.LineNo} 行：没有名为「{call.Name}」的代码块（名字写错了？）");
            }

            return doc;
        }

        /// <summary>
        /// 记一次「块外调用」：<see cref="MacroScriptDoc.Calls"/> 用来统计和校验块名，
        /// <see cref="MacroScriptDoc.Loose"/> 用来保住它和周围指令的**先后顺序**（顶层流程）。
        /// </summary>
        private static void AddTopLevelCall(MacroScriptDoc doc, string name, int lineNo)
        {
            doc.Calls.Add(new MacroCall { Name = name, LineNo = lineNo });
            doc.Loose.Add(new MacroCommand
            {
                Op = MacroOp.Call, KeyName = name, LineNo = lineNo, IsValid = true,
            });
        }

        /// <summary>
        /// 开一个新块（顺手校验名字）：空 / 是指令关键字 / 含空格 / 重名 都会报错并返回 null。
        /// </summary>
        private static MacroBlock? StartBlock(MacroScriptDoc doc, string rawName, int lineNo)
        {
            string name = (rawName ?? "").Trim();
            doc.SawBlockHeader = true;

            if (name.Length == 0)
            {
                doc.Errors.Add($"第 {lineNo} 行：代码块名字不能为空");
                return null;
            }

            if (IsOpKeyword(name))
            {
                doc.Errors.Add($"第 {lineNo} 行：「{name}」是指令名，不能当代码块名字");
                return null;
            }

            if (name.IndexOf(' ') >= 0 || name.IndexOf('\t') >= 0)
            {
                doc.Errors.Add($"第 {lineNo} 行：代码块名字「{name}」不能含空格");
                return null;
            }

            MacroBlock? duplicate = doc.FindBlock(name);
            if (duplicate != null)
                doc.Errors.Add($"第 {lineNo} 行：代码块「{name}」重复定义了（第 {duplicate.HeaderLine} 行已经有一个）");

            var block = new MacroBlock { Name = name, HeaderLine = lineNo };
            doc.Blocks.Add(block);
            return block;
        }

        /// <summary>取行首第一个词（不看参数）。</summary>
        private static string FirstWord(string s)
        {
            int i = 0;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            return s.Substring(0, i);
        }

        /// <summary>
        /// <summary>
        /// 2026-10-20 起：一行**只能一个指令**，不再按分号拆多条。
        /// <para>整行原样返回（单元素）；分号多指令会在 <see cref="ParseLine"/> 里被识别并报错。</para>
        /// </summary>
        private static List<string> SplitStatements(string rawLine)
            => new List<string>(1) { rawLine };

        /// <summary>
        /// 认「名字()」这种调用写法（半角/全角括号都行，名字不分大小写）。
        /// <para>★ 名字不能是指令关键字 —— 否则 <c>Delay(5)</c> 这种会被误当成调用。</para>
        /// </summary>
        private static bool TryParseCall(string t, out string name)
        {
            name = "";

            if (t.Length < 3) return false;
            if (!t.EndsWith(")") && !t.EndsWith("）")) return false;

            string inner = t.Substring(0, t.Length - 1).TrimEnd();
            if (!inner.EndsWith("(") && !inner.EndsWith("（")) return false;

            string candidate = inner.Substring(0, inner.Length - 1).Trim();
            if (candidate.Length == 0) return false;
            if (candidate.IndexOf(' ') >= 0 || candidate.IndexOf('\t') >= 0) return false;
            if (IsOpKeyword(candidate)) return false;

            name = candidate;
            return true;
        }

        /// <summary>
        /// 要执行的指令序列 = **按块外调用的顺序**，把各块的指令拼起来。
        /// <para>
        /// ★ 一行调用都没写 ⇒ 执行**第一个块**（"只定义不调用"的兜底，也保证单个块的老写法照常能跑）。
        /// ★ 全篇没有块结构 ⇒ 整篇当一段执行（真正的旧脚本兼容）。
        /// </para>
        /// <para>★ <paramref name="modeIndex"/> 已经**不影响**取哪段脚本 —— 触发方式只管"跑一遍 / 循环"。</para>
        /// </summary>
        public static List<MacroCommand> ParseFor(string? script, int modeIndex)
        {
            MacroScriptDoc doc = ParseDoc(script);

            // ★ 用 UsesBlocks 而不是 HasBlocks：写了块结构但写坏时，宁可什么都不执行，
            //   也不能退回"全篇当旧脚本跑"（那样用户以为的块边界全失效了）。
            if (!doc.UsesBlocks) return Parse(script);

            var result = new List<MacroCommand>();
            BuildRunList(doc, result);
            return result;
        }

        /// <summary>解析 + 错误列表（编辑器状态条用）。</summary>
        public static List<MacroCommand> ParseForWithErrors(string? script, int modeIndex, out List<string> errors)
        {
            MacroScriptDoc doc = ParseDoc(script);

            // 完全没有块结构：沿用原来的整篇解析（错误信息格式与以前完全一致）
            if (!doc.UsesBlocks) return ParseWithErrors(script, out errors);

            errors = doc.Errors;

            var cmds = new List<MacroCommand>();
            BuildRunList(doc, cmds);
            return cmds;
        }

        /// <summary>
        /// 整理出真正要执行的指令序列。
        /// <para>
        /// ① 有 <c>main</c> 块 ⇒ **只跑 main**：把它里面的 <c>名字()</c> 就地展开，
        ///    展开完继续往下走（所以 main 里可以"先调用、再写指令"混着来）。
        /// </para>
        /// <para>
        /// ② 没有 main ⇒ 兼容老写法：块外的调用按出现顺序依次执行；一个调用都没有就取第一个块。
        /// </para>
        /// </summary>
        private static void BuildRunList(MacroScriptDoc doc, List<MacroCommand> into)
        {
            MacroBlock? entry = doc.FindEntryBlock();

            // ① 有入口块 ⇒ 只跑入口，块外的东西不执行（明说一句，免得用户以为写了就会跑）
            if (entry != null)
            {
                if (doc.Loose.Count > 0)
                    doc.Errors.Add($"第 {doc.Loose[0].LineNo} 行：有「{entry.Name}」入口时，块外写的指令 / 调用不会执行");

                ExpandBlock(doc, entry, into, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                return;
            }

            // ② 没有入口 ⇒ **块外内容按出现顺序执行**：调用就地展开，散指令照常执行
            if (doc.Loose.Count > 0)
            {
                ExpandCommands(doc, doc.Loose, into, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                return;
            }

            // ③ 块外什么都没写 ⇒ **什么都不执行**
            //    ★ 代码块只是定义，必须被调用才会跑（用户明确要这个语义）——
            //      所以这里**不能**拿"第一个块"来兜底，否则改完没调用会莫名其妙跑起来。
            if (doc.Blocks.Count > 0)
            {
                doc.Errors.Add($"脚本里定义了 {doc.Blocks.Count} 个代码块，但一行调用都没有 —— " +
                               "触发键不会执行任何指令（在块外写一行「块名()」来调用）");
            }
        }

        /// <summary>
        /// 把一个块**就地展开**成指令：遇到 <see cref="MacroOp.Call"/> 就把被调块的指令原样代入。
        /// <para>★ 用 <paramref name="visiting"/> 记当前展开路径上的块名 —— 循环调用会无限展开，
        /// 一旦重复立刻停手并报错，而不是让程序跑爆。</para>
        /// </summary>
        private static void ExpandBlock(MacroScriptDoc doc, MacroBlock block,
                                        List<MacroCommand> into, HashSet<string> visiting)
        {
            if (!visiting.Add(block.Name))
            {
                doc.Errors.Add($"第 {block.HeaderLine} 行：代码块「{block.Name}」循环调用（自己调到自己），这段不再展开");
                return;
            }

            ExpandCommands(doc, block.Commands, into, visiting);

            visiting.Remove(block.Name);
        }

        /// <summary>把一串指令里的 <see cref="MacroOp.Call"/> 就地展开成被调块的指令（其余原样保留）。</summary>
        private static void ExpandCommands(MacroScriptDoc doc, List<MacroCommand> source,
                                          List<MacroCommand> into, HashSet<string> visiting)
        {
            foreach (MacroCommand c in source)
            {
                if (c.Op != MacroOp.Call)
                {
                    into.Add(c);
                    continue;
                }

                MacroBlock? target = doc.FindBlock(c.KeyName);
                if (target == null)
                {
                    doc.Errors.Add($"第 {c.LineNo} 行：没有名为「{c.KeyName}」的代码块（名字写错了？）");
                    continue;
                }

                ExpandBlock(doc, target, into, visiting);
            }
        }

        /// <summary>新建宏时给的默认脚本（顺便当语法示例）。</summary>
        public const string Sample =
            "# 自定义宏指令脚本\r\n" +
            "# 有main时触发键只执行 main { } 里的内容\r\n" +
            "# main 里可以直接写指令，也可以写「名字()」调用别的代码块\r\n" +
            "#不使用main时可以直接这样写\r\n" +
            "#MouseDown Left\r\n" +
            "#Delay 100\r\n" +
            "#MouseUp Left\r\n" +
            "main\r\n" +
            "{\r\n" +
            "    左键连点()\r\n" +
            "}\r\n" +
            "\r\n" +
            "左键连点\r\n" +
            "{\r\n" +
            "    MouseDown Left\r\n" +
            "    Delay 100\r\n" +
            "    MouseUp Left\r\n" +
            "}\r\n";

        /// <summary>编辑器右上角的说明（给用户的备忘）。</summary>
        public const string HelpText =
            "入口 main { }：只执行 main 里的内容；里面可写指令，也可写「名字()」调用别的块\r\n" +
            "KeyDown 键名 —— 按住不抬起；KeyUp 键名 —— 抬起；KeyPress 键名 [毫秒] —— 按一下\r\n" +
            "Delay 毫秒 —— 等待；MouseDown/MouseUp/MouseClick Left|Middle|Right —— 鼠标键\r\n" +
            "Move dx dy —— 相对移动；MoveTo x y —— 移到屏幕坐标；Wheel 格数 —— 滚轮（负=下）\r\n" +
            "Text 文本 —— 粘贴一段文本（中文可直接写）；# 或 // 开头是注释";

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
                // ★ 一行可以写多条（分号分隔）—— 旧脚本路径也得认，否则块外/无块的分号写法会被吞
                foreach (string stmt in SplitStatements(lines[i]))
                {
                    MacroCommand? cmd = ParseLine(stmt, i + 1);
                    if (cmd != null) result.Add(cmd.Value);
                }
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
                // ★ 同上：分号分隔的一行多条，逐条解析、逐条报错
                foreach (string stmt in SplitStatements(lines[i]))
                {
                    MacroCommand? cmd = ParseLine(stmt, i + 1);
                    if (cmd == null) continue;

                    if (!cmd.Value.IsValid) errors.Add($"第 {i + 1} 行：{cmd.Value.Error}");
                    else result.Add(cmd.Value);
                }
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

            // ★ 一行只能一个指令：参数里出现分号 = 用户在一行写了多条，报错（Text 指令除外，分号是文本）
            if (op != MacroOp.Text && (rest.IndexOf(';') >= 0 || rest.IndexOf('；') >= 0))
                return Fail(op, lineNo, "一行只能写一个指令（不要用分号「;」在一行里写多条）");

            // ★ 一行只能一个指令：参数里出现另一个指令关键字（如「KeyDown r MouseDown Left」）也报错
            if (op != MacroOp.Text)
            {
                foreach (string w in rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (IsOpKeyword(w))
                        return Fail(op, lineNo, $"一行只能写一个指令（「{w}」是另一个指令，请换行写）");
                }
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
                    // ★ 逗号也当分隔符：「800, 600」「800，600」「800,600」都认
                    //   （面板里的坐标就写成带逗号的样子，手抄/粘贴进来也不能报错）
                    string[] nums = string.Join(' ', parts)
                                          .Replace('，', ' ')
                                          .Replace(',', ' ')
                                          .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                    if (nums.Length < 2 || !TryInt(nums[0], out int x) || !TryInt(nums[1], out int y))
                        return Fail(op, lineNo, $"{OpNames[(int)op]} 后面要写两个整数，例如「{OpNames[(int)op]} {(op == MacroOp.Move ? "10, -20" : "800, 600")}」");

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
