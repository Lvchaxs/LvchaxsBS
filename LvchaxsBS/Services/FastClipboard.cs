using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 全局「快速剪贴板」：把 TextBox 的复制 / 剪切 / 粘贴换成 Win32 直读直写。
    ///
    /// <para>
    /// ★ 为什么要换：WPF 的 <c>TextBox.Copy()</c> 走 <c>System.Windows.Clipboard</c> →
    /// OLE <c>SetDataObject(data, copy: true)</c>，**要等所有剪贴板监听者处理完才返回**。
    /// 机器上只要有程序占着剪贴板（输入法、剪贴板历史、录屏、办公软件插件…），
    /// Ctrl+C / Ctrl+X 就会顿一下 —— 文本越长越明显。剪切还额外触发一次 TextChanged（重新着色），
    /// 两个卡点叠在一起就是"卡顿"。
    /// </para>
    ///
    /// <para>
    /// ★ 做法：在 <see cref="TextBoxBase"/> 的**隧道阶段**（<c>PreviewKeyDown</c>）拦下
    /// Ctrl+C / Ctrl+X / Ctrl+V，自己用 <c>OpenClipboard / SetClipboardData</c> 读写
    /// <c>CF_UNICODETEXT</c>，再 <c>e.Handled = true</c> 挡掉 WPF 那一套。
    /// </para>
    ///
    /// <para>
    /// ★★ 用**类处理器**注册 ⇒ **全进程所有 TextBox 一次覆盖**：
    /// 宿主自己的页面、插件 dll（如「自定义宏」的指令编辑器）里 new 出来的 TextBox 全都在内，
    /// 插件一行都不用改。
    /// <c>PasswordBox</c> 不是 <see cref="TextBoxBase"/>，不受影响（密码框的复制另有规矩）。
    /// </para>
    ///
    /// <para>
    /// ★ 只处理"剪贴板里是纯文本"的情况：读不到文本就**不接管**，让 WPF 按自己的路子试 ——
    /// 宁可慢一点，也不能把"从 Excel 粘贴表格"这类场景弄坏。
    /// </para>
    /// </summary>
    public static class FastClipboard
    {
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;

        /// <summary>剪贴板被别的程序占着时重试几次（每次 5ms，最多 25ms）。</summary>
        private const int MaxAttempts = 5;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private static bool _installed;

        /// <summary>
        /// 装到进程里（App 启动时调一次即可，重复调用无副作用）。
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            // ★ 类处理器：对**所有** TextBoxBase 生效，且先于控件自己的键盘处理执行
            EventManager.RegisterClassHandler(typeof(TextBoxBase),
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(OnPreviewKeyDown));
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ★ SelectionLength / SelectedText 是 TextBox、RichTextBox 各自定义的
            //   （TextBoxBase 上并没有），所以这里必须收窄到 TextBox。
            //   本项目只有 TextBox；将来用 RichTextBox 的话它会退回 WPF 原路径，不影响功能。
            if (sender is not TextBox box) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

            // 输入法处理中时 e.Key 报 ImeProcessed，真正的键在 ImeProcessedKey 里
            Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

            switch (key)
            {
                case Key.C:
                    if (box.SelectionLength == 0) return;
                    if (WriteText(box.SelectedText)) e.Handled = true;
                    break;

                case Key.X:
                    if (box.SelectionLength == 0 || box.IsReadOnly) return;
                    if (WriteText(box.SelectedText))
                    {
                        box.SelectedText = "";
                        e.Handled = true;
                    }
                    break;

                case Key.V:
                    if (box.IsReadOnly) return;
                    string? text = ReadText();
                    if (text == null) return;          // 剪贴板里没有纯文本 ⇒ 交回 WPF
                    box.SelectedText = NormalizeNewlines(text);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>把文本写进剪贴板（CF_UNICODETEXT）。成功返回 true。</summary>
        public static bool WriteText(string text)
        {
            if (text == null) return false;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    Thread.Sleep(5);               // 被别的程序占着，让一下再试
                    continue;
                }

                try
                {
                    EmptyClipboard();

                    int bytes = (text.Length + 1) * 2;   // UTF-16 + 结尾 \0
                    IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)bytes);
                    if (hMem == IntPtr.Zero) return false;

                    IntPtr target = GlobalLock(hMem);
                    if (target == IntPtr.Zero) { GlobalFree(hMem); return false; }

                    try
                    {
                        Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    }
                    finally
                    {
                        GlobalUnlock(hMem);
                    }

                    if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
                    {
                        GlobalFree(hMem);
                        return false;
                    }

                    // ★★ SetClipboardData 成功后这块内存的所有权归系统，**绝对不能再 GlobalFree**
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            return false;
        }

        /// <summary>读剪贴板里的纯文本；没有文本就返回 null。</summary>
        public static string? ReadText()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    Thread.Sleep(5);
                    continue;
                }

                try
                {
                    if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;

                    IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                    if (handle == IntPtr.Zero) return null;

                    IntPtr p = GlobalLock(handle);
                    if (p == IntPtr.Zero) return null;

                    try
                    {
                        return Marshal.PtrToStringUni(p);
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }

            return null;
        }

        /// <summary>剪贴板里的换行五花八门（\n / \r / \r\n），TextBox 里统一用 \r\n。</summary>
        private static string NormalizeNewlines(string s)
        {
            if (s.IndexOf('\r') < 0 && s.IndexOf('\n') < 0) return s;

            return s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        }
    }
}
