using System;
using System.Collections.Generic;

namespace LvchaxsBS_Macro.Core
{
    /// <summary>
    /// 虚拟键码（Win32 VK）→ **宏引擎认的键名**。
    ///
    /// <para>
    /// ★ 为什么不能直接用宿主的 <c>GlobalKeyboardHookService.GetKeyName</c>：
    ///   宿主那套键名是给"触发键"用的（"Backspace"、"左Ctrl"、"_-"…），
    ///   而**宏引擎**的键名表是另一套（见 <c>SimulationService.TryGetKeyCode</c>）：
    ///   "A" / "Space" / "Esc" / "Back" / "LShift" / "Minus" …
    ///   键名对不上 ⇒ 宏执行时那条指令就废了。
    /// </para>
    /// <para>
    /// ★ 键名一律走**英文原码**（和磁盘上存的脚本一致）—— 面板显示什么、
    ///   复制出来是什么，都用这一套名字，不做中英转换。
    /// </para>
    /// </summary>
    public static class MacroKeyNames
    {
        /// <summary>是不是"修饰键"（Shift / Ctrl / Alt / Win）。</summary>
        public static bool IsModifier(int vk)
            => vk is 0x10 or 0xA0 or 0xA1 or 0x11 or 0xA2 or 0xA3
                     or 0x12 or 0xA4 or 0xA5 or 0x5B or 0x5C;

        /// <summary>
        /// 虚拟键码 → 宏引擎键名。返回 null = 引擎不认识这个键（不填，省得插一条执行不了的指令）。
        /// </summary>
        public static string? FromVirtualKey(int vk)
        {
            string? name = Map(vk);
            if (name == null) return null;

            // ★ 用引擎自己的表校验一遍：映射表和引擎键名表必须对得上，
            //   否则拼出来的指令执行时会被引擎判为"不认识的按键"。
            return SimulationService.TryGetKeyCode(name, out _) ? name : null;
        }

        private static string? Map(int vk)
        {
            // ---- 字母 ----
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();

            // ---- 主键盘数字 ----
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();

            // ---- 小键盘数字 ----
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);

            // ---- 功能键 F1-F24 ----
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);

            switch (vk)
            {
                case 0x08: return "Back";          // Backspace
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x2C: return "PrintScreen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x13: return "Pause";
                case 0x14: return "CapsLock";
                case 0x90: return "NumLock";

                // ---- 修饰键（左右分开，宏引擎认 L/R 前缀）----
                case 0x10: return "Shift";
                case 0xA0: return "LShift";
                case 0xA1: return "RShift";
                case 0x11: return "Ctrl";
                case 0xA2: return "LCtrl";
                case 0xA3: return "RCtrl";
                case 0x12: return "Alt";
                case 0xA4: return "LAlt";
                case 0xA5: return "RAlt";
                case 0x5B: return "LWin";
                case 0x5C: return "RWin";

                // ---- 标点（OEM 键位）----
                case 0xBD: return "Minus";         // - _
                case 0xBB: return "Equal";         // = +
                case 0xBC: return "Comma";         // , <
                case 0xBE: return "Period";        // . >
                case 0xBF: return "Slash";         // / ?
                case 0xBA: return "Semicolon";     // ; :
                case 0xDE: return "Quote";         // ' "
                case 0xDB: return "LBracket";      // [ {
                case 0xDD: return "RBracket";      // ] }
                case 0xDC: return "Backslash";     // \ |
                case 0xC0: return "Backtick";      // ` ~

                // ---- 小键盘符号（引擎没有独立名字，归到同名符号键）----
                case 0x6A: return "Plus";          // Num *
                case 0x6B: return "Plus";          // Num +
                case 0x6D: return "Minus";         // Num -
                case 0x6F: return "Slash";         // Num /
                case 0x6E: return "Period";        // Num .
            }

            return null;
        }

        /// <summary>
        /// 宏引擎认的全部键名（供界面提示 / 校验用）。
        /// </summary>
        public static IEnumerable<string> AllNames
        {
            get
            {
                for (char c = 'A'; c <= 'Z'; c++) yield return c.ToString();
                for (int i = 0; i <= 9; i++) yield return i.ToString();
                for (int i = 1; i <= 24; i++) yield return "F" + i;
                yield return "Space";
                yield return "Enter";
                yield return "Esc";
                yield return "Tab";
                yield return "Back";
                yield return "Delete";
                yield return "Insert";
                yield return "Up";
                yield return "Down";
                yield return "Left";
                yield return "Right";
                yield return "Home";
                yield return "End";
                yield return "PageUp";
                yield return "PageDown";
                yield return "CapsLock";
                yield return "NumLock";
                yield return "PrintScreen";
                yield return "Pause";
                yield return "Shift";
                yield return "Ctrl";
                yield return "Alt";
                yield return "Win";
            }
        }
    }
}
