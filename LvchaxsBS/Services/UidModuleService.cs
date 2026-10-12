using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using LvchaxsBS.Services.Hooks;

namespace LvchaxsBS.Services
{
    /// <summary>
    /// UID 账号管理模块（插件）加载器。
    ///
    /// <para>
    /// 宿主与模块之间<b>没有编译期引用</b>：启动时检查 exe 同目录下是否存在
    /// <c>LvchaxsBS_uid.dll</c>，存在则用 <see cref="Assembly.LoadFrom(string)"/> 载入，
    /// 并按约定类型名 + 约定方法签名反射调用。模块不存在时全部功能静默降级
    /// （顶部按钮隐藏），宿主照常运行。
    /// </para>
    /// <para>
    /// 约定入口：<c>LvchaxsBS_UID.UidPlugin.GetAccountManagerView()</c> 返回一个
    /// <see cref="FrameworkElement"/>（账号管理界面），由宿主放进自己的内容区显示。
    /// </para>
    /// </summary>
    public static class UidModuleService
    {
        /// <summary>约定：与 exe 同目录、固定文件名的模块 dll。</summary>
        private const string ModuleFileName = "LvchaxsBS_uid.dll";

        /// <summary>约定：模块入口类型（LvchaxsBS_UID 项目里的 UidPlugin）。</summary>
        private const string EntryTypeName = "LvchaxsBS_UID.UidPlugin";

        /// <summary>约定入口方法：返回要嵌入宿主内容区的界面。</summary>
        private const string EntryMethodName = "GetAccountManagerView";

        private static MethodInfo? _getView;
        private static MethodInfo? _showHelp;
        private static FrameworkElement? _view;
        private static bool _probed;

        /// <summary>exe 同目录下是否存在可用的 UID 模块。</summary>
        public static bool IsAvailable { get; private set; }

        /// <summary>
        /// 已创建的账号管理界面实例（没进过 UID 页面时为 null）。
        /// 宿主用它判断"当前内容区显示的是不是 UID 页面"。
        /// </summary>
        public static FrameworkElement? CurrentView => _view;

        /// <summary>用户在模块的帮助窗里点了『关闭工具』（宿主要切回自己的页面）。</summary>
        public static event EventHandler? CloseToolRequested;

        /// <summary>
        /// 探测并载入模块。可重复调用，只真正执行一次。
        /// 失败一律静默处理，不影响宿主其它功能。
        /// </summary>
        public static void Probe()
        {
            if (_probed) return;
            _probed = true;

            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModuleFileName);
                if (!File.Exists(path))
                {
                    Debug.WriteLine($"[UID] 未找到模块：{path}");
                    return;
                }

                Assembly asm = Assembly.LoadFrom(path);

                Type? type = asm.GetType(EntryTypeName, throwOnError: false);
                if (type == null)
                {
                    Debug.WriteLine($"[UID] 模块缺少入口类型：{EntryTypeName}");
                    return;
                }

                MethodInfo? getView = type.GetMethod(
                    EntryMethodName, BindingFlags.Public | BindingFlags.Static);

                if (getView == null || !typeof(FrameworkElement).IsAssignableFrom(getView.ReturnType)
                    || getView.GetParameters().Length != 0)
                {
                    Debug.WriteLine($"[UID] 模块版本不匹配：缺少 {EntryMethodName}()，" +
                                    "请使用与本程序配套的 LvchaxsBS_uid.dll");
                    return;
                }

                _getView = getView;
                IsAvailable = true;
                Debug.WriteLine("[UID] 模块加载成功");

                // 「使用说明」入口：模块页内那个「!」按钮已挪到宿主标题栏，
                // 由宿主反射调用这里的 ShowHelp()（模块可选提供，没有就不给标题栏加按钮）。
                _showHelp = type.GetMethod("ShowHelp", BindingFlags.Public | BindingFlags.Static);

                // 把宿主的全局模态遮罩注入给模块：
                // 模块没有宿主的编译期引用，所以用 "命令 + 参数" 的单一委托来驱动，
                // 命令含义见 ModalOverlayService.HandleModuleCommand。
                var setOverlay = type.GetMethod("SetHostOverlay", BindingFlags.Public | BindingFlags.Static);
                if (setOverlay != null && setOverlay.GetParameters().Length == 1)
                {
                    setOverlay.Invoke(null, new object[]
                    {
                        new Action<string, object?>(ModalOverlayService.HandleModuleCommand)
                    });
                    Debug.WriteLine("[UID] 已注入宿主遮罩");
                }
                else
                {
                    Debug.WriteLine("[UID] 模块未提供 SetHostOverlay，遮罩将退回模块自绘");
                }

                // 「窗口缩放」也交给模块：模块弹的独立窗口不会继承主界面那层
                // LayoutTransform，需要自己按这个系数放大，才能和主界面比例一致。
                var setScale = type.GetMethod("SetHostUiScaleProvider", BindingFlags.Public | BindingFlags.Static);
                if (setScale != null && setScale.GetParameters().Length == 1)
                {
                    setScale.Invoke(null, new object[]
                    {
                        new Func<double>(() => LvchaxsBS.UI.Helpers.UiScale.Current)
                    });
                    Debug.WriteLine("[UID] 已注入窗口缩放系数");
                }
                else
                {
                    Debug.WriteLine("[UID] 模块未提供 SetHostUiScaleProvider，其弹窗将保持 100% 大小");
                }

                // 提示条也交给宿主：模块原来的"内容区右上角弹窗"已删除，
                // 统一用主程序标题栏的提示。
                var setToast = type.GetMethod("SetHostToast", BindingFlags.Public | BindingFlags.Static);
                if (setToast != null && setToast.GetParameters().Length == 1)
                {
                    setToast.Invoke(null, new object[]
                    {
                        new Action<string, bool>((msg, ok) =>
                        {
                            if (Application.Current?.MainWindow is LvchaxsBS.UI.MainWindow host)
                                host.ShowToast(msg, ok);
                            else
                                Debug.WriteLine($"[UID Toast] {msg}");
                        })
                    });
                    Debug.WriteLine("[UID] 已注入标题栏提示");
                }

                // 客户区字幕也交给宿主：模块登录时要在游戏画面上显示进度，
                // 复用宿主那一个覆盖层窗口（一个进程只能有一个），风格与钓鱼/烹饪/伐木一致。
                // 覆盖层由 InitializeEngine() 初始化，而那一步只在共享组件存在时才跑，
                // 所以这里也跟着 IsAvailable 走，缺 dll 时不注入（模块侧自动空操作）。
                var setSubtitle = type.GetMethod("SetHostSubtitle", BindingFlags.Public | BindingFlags.Static);
                if (setSubtitle != null && setSubtitle.GetParameters().Length == 1)
                {
                    if (SubtitleHostBridge.IsAvailable)
                    {
                        setSubtitle.Invoke(null, new object[]
                        {
                            new Action<string, object?>(SubtitleHostBridge.Handle)
                        });
                        Debug.WriteLine("[UID] 已注入客户区字幕");
                    }
                    else
                    {
                        Debug.WriteLine("[UID] 共享组件缺失，未注入客户区字幕");
                    }
                }
                else
                {
                    Debug.WriteLine("[UID] 模块未提供 SetHostSubtitle，登录进度不会显示在游戏画面上");
                }

                // 焦点查询也交给模块：登录流程靠它做「游戏不在焦点就暂停」——
                // 用户切到别的程序时识别和按键全停，焦点回游戏再接着跑，
                // 免得把点击 / 按键送到别人的窗口里。
                // Reevaluate 会用真实前台窗口同步复核一次，比读缓存的轮询值更准。
                var setFocus = type.GetMethod("SetHostFocusProvider", BindingFlags.Public | BindingFlags.Static);
                if (setFocus != null && setFocus.GetParameters().Length == 1)
                {
                    setFocus.Invoke(null, new object[]
                    {
                        new Func<bool>(() => WindowFocusService.Reevaluate())
                    });
                    Debug.WriteLine("[UID] 已注入游戏焦点查询");
                }
                else
                {
                    Debug.WriteLine("[UID] 模块未提供 SetHostFocusProvider，登录过程不会因失焦点暂停");
                }
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                Debug.WriteLine($"[UID] 模块加载失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 取账号管理界面。模块未加载 / 版本不匹配时返回 null。
        ///
        /// <para>
        /// 模块内部是<b>单例</b>：多次调用拿到的是同一个实例，
        /// 因此宿主切到别的页面再切回来时，用户上次浏览到的分组等状态会被保留。
        /// 必须在 UI 线程调用。
        /// </para>
        /// </summary>
        public static FrameworkElement? GetAccountManagerView()
        {
            Probe();
            if (!IsAvailable || _getView == null) return null;

            if (_view != null) return _view;

            try
            {
                _view = _getView.Invoke(null, null) as FrameworkElement;

                // 把模块内部的『关闭工具』转发给宿主
                var evt = _view?.GetType().GetEvent("CloseToolRequested");
                evt?.AddEventHandler(_view, new EventHandler((s, e) => CloseToolRequested?.Invoke(null, EventArgs.Empty)));

                return _view;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID] 创建账号管理界面失败：{ex.Message}");
                MessageBox.Show($"打开 UID 账号管理失败：{ex.Message}\n\n" +
                                $"请确认 {ModuleFileName} 与 LvchaxsBS.exe 版本匹配。",
                                "UID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        /// <summary>
        /// 打开账号管理的「使用说明」窗口（宿主标题栏那个「!」按钮调用）。
        ///
        /// <para>
        /// 页面里的「!」按钮已经不再自绘，统一放到主程序标题栏。
        /// 模块未加载、视图还没创建过、或模块未提供该入口时都静默返回。
        /// 必须在 UI 线程调用。
        /// </para>
        /// </summary>
        public static void ShowHelp()
        {
            Probe();
            if (!IsAvailable || _showHelp == null) return;

            try
            {
                _showHelp.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID] 打开使用说明失败：{ex.Message}");
            }
        }
    }
}
