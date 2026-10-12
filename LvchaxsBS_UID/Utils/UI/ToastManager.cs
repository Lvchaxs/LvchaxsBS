using System;
using System.Diagnostics;

namespace LvchaxsBS_UID.Utils.UI
{
    /// <summary>
    /// 提示条（Toast）。
    ///
    /// <para>
    /// 以前是模块自己在内容区右上角画一个弹窗（复制账号时那个）。现在把它删掉，
    /// 统一改用<b>主程序标题栏的提示</b>：宿主通过 <see cref="HostToast"/> 注入入口，
    /// 模块只负责把消息转过去。
    /// </para>
    /// <para>
    /// 保留本类的目的只是让 30 多处调用点不用改；它已经不再有任何自己的渲染逻辑。
    /// </para>
    /// </summary>
    public class ToastManager
    {
        /// <summary>
        /// 宿主注入的提示入口（消息, 是否成功）。宿主没注入时只写调试输出，不弹任何东西。
        /// </summary>
        public static Action<string, bool>? HostToast { get; set; }

        public ToastManager()
        {
        }

        /// <summary>带标题的提示：标题与正文拼成一行交给宿主。</summary>
        public void Show(string title, string message, bool isSuccess = true)
        {
            string text = string.IsNullOrWhiteSpace(title)
                ? message ?? ""
                : $"{title}｜{message}";

            Show(text, isSuccess);
        }

        /// <summary>单行提示。</summary>
        public void Show(string message, bool isSuccess = true)
        {
            if (HostToast != null)
            {
                HostToast(message ?? "", isSuccess);
                return;
            }

            Debug.WriteLine($"[UID Toast] {message}");
        }
    }
}
