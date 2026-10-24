using System;
using System.Collections.Generic;

namespace LvchaxsBS_Macro.Core
{
    /// <summary>
    /// 一对「按下 → 抬起」：<paramref name="DownLine"/> 行按下、<paramref name="UpLine"/> 行抬起，
    /// Depth 是嵌套深度。
    /// <para>★ <paramref name="Dashed"/> = true 表示「**只是概念上的一对**」：
    ///   中间多了一次同键按下（键只能按住一次），所以没法真正对应，
    ///   但仍连起来（用虚线），提醒用户这行的按下和这行的抬起其实是同一个键。</para>
    /// </summary>
    public sealed record MacroPair(int DownLine, int UpLine, int Depth, string Key, bool IsMouse, bool Dashed = false);

    /// <summary>一个「按下」直到结尾都没有对应抬起（画红色虚线，不报错）。</summary>
    public sealed record UnpairedDown(int Line, string Key, bool IsMouse);

    /// <summary>
    /// 一个「抬起」找不到任何能配对的按下（连虚线对都凑不出来）——画红色虚线，不报错。
    /// </summary>
    public sealed record UnpairedUp(int Line, string Key, bool IsMouse);

    /// <summary>
    /// 「按下 → 抬起」配对分析。
    /// <para>
    /// ★ 规则（用户 2026-10-20 定）：
    ///   ① 抬起优先配**最近的**同键按下（画实线灰色括号）；
    ///   ② 同一键又按下了一次 ⇒ 较旧的按下被顶替（物理上不可能按住两次），
    ///      它**不丢**，而是等后面出现的同键抬起 —— 配成**虚线**对（概念上确实是一对，
    ///      只是时机上对不上），**不报错**；
    ///   ③ 按下直到结尾没抬起 → 画红色虚线，不报错；
    ///   ④ 抬起连虚线对都凑不出来，但**这个键在脚本里按下过** ⇒ 画红色虚线，不报错；
    ///      只有「这个键从头到尾就没按下过」才报错。
    /// </para>
    /// </summary>
    public static class MacroPairAnalyzer
    {
        private static bool IsDownKey(string w)
            => w.Equals("KeyDown", StringComparison.OrdinalIgnoreCase) || w == "按键键";

        private static bool IsUpKey(string w)
            => w.Equals("KeyUp", StringComparison.OrdinalIgnoreCase) || w == "抬起键";

        private static bool IsDownMouse(string w)
            => w.Equals("MouseDown", StringComparison.OrdinalIgnoreCase) || w == "鼠标按下";

        private static bool IsUpMouse(string w)
            => w.Equals("MouseUp", StringComparison.OrdinalIgnoreCase) || w == "鼠标抬起";

        /// <summary>扫描中的「按下」。</summary>
        private struct Open
        {
            public string Key;
            public bool IsMouse;
            public int Line;
            public int Depth;
        }

        /// <summary>
        /// 扫描整段脚本做配对。返回 (配对, 未配对按下, 未配对抬起, 错误)。
        /// <para>★ 错误只有一种：这个键**从来没按下过**，却写了抬起。</para>
        /// </summary>
        public static (List<MacroPair> Pairs, List<UnpairedDown> UnpairedDowns,
                        List<UnpairedUp> UnpairedUps, List<string> Errors) Analyze(string? text)
        {
            var pairs = new List<MacroPair>();
            var errors = new List<string>();
            var unpaired = new List<UnpairedDown>();
            var unpairedUps = new List<UnpairedUp>();
            if (string.IsNullOrEmpty(text)) return (pairs, unpaired, unpairedUps, errors);

            var stack = new List<Open>();
            // ★ 被同键新按下顶替掉的按下（行号升序）：等后面的同键抬起配成**虚线**对
            var spare = new List<Open>();
            // ★ 这个键（区分键盘/鼠标）脚本里到底按下过没有
            var downSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string line = lines[i].Trim();

                int hash = line.IndexOf('#');
                int slash = line.IndexOf("//", StringComparison.Ordinal);
                int cmt = hash >= 0 && slash >= 0 ? Math.Min(hash, slash) : (hash >= 0 ? hash : slash);
                if (cmt >= 0) line = line.Substring(0, cmt).Trim();
                if (line.Length == 0) continue;

                int sp = 0;
                while (sp < line.Length && !char.IsWhiteSpace(line[sp])) sp++;
                string op = line.Substring(0, sp);

                bool downKey = IsDownKey(op), upKey = IsUpKey(op);
                bool downMouse = IsDownMouse(op), upMouse = IsUpMouse(op);
                if (!downKey && !upKey && !downMouse && !upMouse) continue;

                string rest = line.Substring(sp).TrimStart();
                int e = 0;
                while (e < rest.Length && !char.IsWhiteSpace(rest[e])) e++;
                string key = rest.Substring(0, e);

                bool isMouse = downMouse || upMouse;
                string token = (isMouse ? "M:" : "K:") + key;

                if (downKey || downMouse)
                {
                    downSeen.Add(token);

                    // ★★ 同一键已经有一个**还没配对**的按下 ⇒ 物理上不可能（键只能按住一次）。
                    //   把较旧的那个「顶替」下来（进 spare），它等后面的同键抬起配成虚线对。
                    for (int s = stack.Count - 1; s >= 0; s--)
                    {
                        if (stack[s].IsMouse == isMouse &&
                            string.Equals(stack[s].Key, key, StringComparison.OrdinalIgnoreCase))
                        {
                            var old = stack[s];
                            stack.RemoveAt(s);
                            old.Depth = 0;
                            spare.Add(old);
                            break;
                        }
                    }

                    // 嵌套层级 = 入栈前栈里的层数（并列的按下不算额外层级，只是同层）
                    stack.Add(new Open { Key = key, IsMouse = isMouse, Line = lineNo, Depth = stack.Count });
                }
                else // 抬起
                {
                    // ① 优先配**最近的**同键按下（实线）
                    int idx = -1;
                    for (int s = stack.Count - 1; s >= 0; s--)
                    {
                        if (stack[s].IsMouse == isMouse &&
                            string.Equals(stack[s].Key, key, StringComparison.OrdinalIgnoreCase))
                        {
                            idx = s;
                            break;
                        }
                    }

                    if (idx >= 0)
                    {
                        var open = stack[idx];
                        pairs.Add(new MacroPair(open.Line, lineNo, open.Depth, open.Key, open.IsMouse));
                        stack.RemoveAt(idx);
                        continue;
                    }

                    // ② 配**被顶替的**同键按下（虚线 —— 概念上是一对，只是时机对不上）
                    int spIdx = -1;
                    for (int s = 0; s < spare.Count; s++)
                    {
                        if (spare[s].IsMouse == isMouse &&
                            string.Equals(spare[s].Key, key, StringComparison.OrdinalIgnoreCase))
                        {
                            spIdx = s;   // 取最早的一个
                            break;
                        }
                    }
                    if (spIdx >= 0)
                    {
                        var old = spare[spIdx];
                        spare.RemoveAt(spIdx);
                        // ★ 虚线对画在**最外层**（Depth = -1），避免和中间的实线对抢位置
                        pairs.Add(new MacroPair(old.Line, lineNo, -1, old.Key, old.IsMouse, Dashed: true));
                        continue;
                    }

                    // ③ 连虚线对都凑不出来
                    if (downSeen.Contains(token))
                    {
                        // ★ 这个键在脚本里按下过 ⇒ 只是多一次抬起（画虚线，不报错）
                        unpairedUps.Add(new UnpairedUp(lineNo, key, isMouse));
                    }
                    else
                    {
                        errors.Add($"第 {lineNo} 行：{(isMouse ? "MouseUp" : "KeyUp")} {key} 没有对应的按下");
                    }
                }
            }

            // 栈里残留的按下 = 缺抬起 → 不报错，画红线
            foreach (var open in stack)
                unpaired.Add(new UnpairedDown(open.Line, open.Key, open.IsMouse));

            // ★ spare 里剩下的（顶替了但始终没等到同键抬起）→ 也是缺抬起，画红线
            foreach (var open in spare)
                unpaired.Add(new UnpairedDown(open.Line, open.Key, open.IsMouse));

            return (pairs, unpaired, unpairedUps, errors);
        }
    }
}
