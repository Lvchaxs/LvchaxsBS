using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Loader;
using LvchaxsBS.Config;
using LvchaxsBS.Core;
using LvchaxsBS.Services.Hooks;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// 共享组件 <c>LvchaxsBS_Resources.dll</c> 的可用性探测与依赖注入。
    ///
    /// <para>
    /// 这个 dll 里除了图片资源，还装着坐标管理 / 模板加载 / 截图 / 匹配 / 模拟输入。
    /// <b>它不在 exe 同目录时主程序照样启动</b>：所有图片留空、图像识别与模拟点击整体停用，
    /// 但 <b>UI 交互与配置读写完全正常</b>。
    /// </para>
    ///
    /// <para>
    /// 之所以能做到这一点：图片访问走宿主自己的 <c>AssetLocator</c>（只拼字符串 URI，
    /// 取不到就是 null/空白，从不碰共享类型），而所有共享类型的引用都只出现在
    /// 「引擎初始化」这条链上（<c>App.InitializeEngine</c>），dll 缺失时整条链直接跳过。
    /// </para>
    ///
    /// <para>
    /// ★ <see cref="Register"/> 的方法体里引用了共享类型，<b>必须在
    /// <see cref="IsAvailable"/> 为 true 时才能调用</b> —— dll 缺失时连 JIT 这一步都会失败。
    /// <see cref="IsAvailable"/> 本身只做文件探测，不碰共享类型，所以它永远安全。
    /// </para>
    /// </summary>
    internal static class SharedComponent
    {
        private const string AssemblyFileName = "LvchaxsBS_Resources.dll";

        /// <summary>
        /// 兜住「dll 是**启动过程中才补进** exe 目录的」这种情况。
        /// <para>
        /// ★★ 默认加载上下文只在 TPA 列表里找程序集，而 TPA 是**进程启动时扫一遍目录**定下来的 ——
        ///   启动自检（缺件自动下载）后补进来的 dll 不在列表里，于是 JIT 解析共享类型时
        ///   抛 <c>FileNotFoundException</c>：文件明明躺在 exe 旁边，运行时却说"找不到"。
        ///   这里注册 Resolving：默认解析失败时，只要 exe 目录里有同名 dll 就按路径加载。
        /// </para>
        /// <para>反射加载的那几个插件（uid / macro）不走默认上下文，本来就不受这个影响。</para>
        /// </summary>
        static SharedComponent()
        {
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                if (string.IsNullOrEmpty(name.Name)) return null;

                string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
                return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
            };
        }

        /// <summary>
        /// 共享组件是否可用（纯文件探测，不触碰它的任何类型）。
        /// <para>★ dll 补上之后会调 <see cref="Reload"/> 重新探测 + 重新注入，不用重启。</para>
        /// </summary>
        public static bool IsAvailable => _isAvailable;

        private static bool _isAvailable = Probe();

        /// <summary><see cref="Register"/> 是否已经跑过（避免重复订阅 <c>WindowFocusService.BoundsChanged</c>）。</summary>
        private static bool _registered;

        private static bool Probe()
        {
            try
            {
                return File.Exists(Path.Combine(AppContext.BaseDirectory, AssemblyFileName));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 运行中装上资源包后热加载：重新探测 → 重新注入。
        /// <para>
        /// ★ 走的是「启动时缺文件 ⇒ 整条注入链没跑过」这条路，所以这里调用等于**第一次**初始化，
        ///   不会重复订阅事件。
        /// </para>
        /// </summary>
        /// <returns>热加载后是否可用。</returns>
        public static bool Reload()
        {
            _isAvailable = AssetLocator.Reload();   // 真正把程序集加载进来 + 算好 pack 前缀
            if (!_isAvailable) return false;

            try
            {
                Register();
                Debug.WriteLine("【共享组件】热加载成功（图片 / 识别 / 模拟输入已可用）");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【共享组件】热加载注入失败：{ex.GetBaseException().Message}");
                return false;
            }
        }

        /// <summary>
        /// 把宿主侧的信息注入共享库：截图模式 / 资源包是否可用 / 窗口客户区变化。
        /// 只能在 <see cref="IsAvailable"/> 为 true 时调用。
        /// </summary>
        public static void Register()
        {
            if (_registered) return;      // 幂等：热加载会再调一次
            _registered = true;

            // 截图模式：注册成 lambda，设置页改完立刻生效，不会读到旧值
            CaptureSettings.CaptureModeProvider = () => ConfigManager.Get<AppSettings>().CaptureMode;

            // 资源包：Pack 规则两边本来就一致，这里只把"资源包在不在"同步过去
            ResourcePaths.IsAvailable = AssetLocator.IsAvailable;
            ResourcePaths.PackProvider = AssetLocator.Pack;
            ResourcePaths.LoadProvider = AssetLocator.Load;

            // 窗口客户区：跟踪游戏窗口是宿主 WindowFocusService 的职责（缺 dll 时它照样工作），
            // 变化时转发给共享库 —— 坐标缓存与截图都从那里取
            WindowFocusService.BoundsChanged += OnHostBoundsChanged;
            if (!WindowFocusService.LastBounds.IsEmpty)
            {
                OnHostBoundsChanged(null, WindowFocusService.LastBounds);
            }

            Debug.WriteLine($"【共享组件】已注入（资源包可用={AssetLocator.IsAvailable}）");
        }

        private static void OnHostBoundsChanged(object? sender, WindowFocusService.WindowBounds bounds)
        {
            WindowBoundsBridge.Report(new WindowBoundsInfo(
                bounds.Hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                bounds.Title, bounds.ProcessName));
        }
    }
}
