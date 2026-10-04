using System;
using System.Runtime.InteropServices;

namespace LvchaxsBS.Helpers
{
    public static class NativeClipboardHelper
    {
        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr lstrcpy(IntPtr dest, string src);

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;

        public static bool SafeSetText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            bool result = false;
            IntPtr hGlobal = IntPtr.Zero;

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    if (OpenClipboard(IntPtr.Zero))
                    {
                        try
                        {
                            EmptyClipboard();
                            uint sizeInBytes = (uint)((text.Length + 1) * 2);
                            hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)sizeInBytes);

                            if (hGlobal == IntPtr.Zero)
                                return false;

                            IntPtr lockedPtr = GlobalLock(hGlobal);
                            if (lockedPtr == IntPtr.Zero)
                                return false;

                            lstrcpy(lockedPtr, text);
                            GlobalUnlock(lockedPtr);

                            if (SetClipboardData(CF_UNICODETEXT, hGlobal) != IntPtr.Zero)
                            {
                                result = true;
                                hGlobal = IntPtr.Zero;
                            }
                        }
                        finally
                        {
                            CloseClipboard();
                        }
                        break;
                    }
                    System.Threading.Thread.Sleep(10);
                }
            }
            catch
            {
                result = false;
            }
            finally
            {
                if (hGlobal != IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                }
            }

            return result;
        }
    }
}