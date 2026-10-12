using System;
using System.Windows;

namespace LvchaxsBS_UID
{
    /// <summary>
    /// UID 账号管理模块的插件入口。
    ///
    /// <para>
    /// 宿主 LvchaxsBS.exe 与本草程序集之间<b>没有任何编译期引用</b>：
    /// 宿主在启动时检查 exe 同目录下是否存在 <c>LvchaxsBS_uid.dll</c>，
    /// 存在就用 <see cref="System.Reflection.Assembly.LoadFrom(string)"/> 载入，
    /// 再按约定名字反射调用这里的静态方法。因此本模块可以随时删掉，
    /// 宿主不会因此编译失败或运行异常。
    /// </para>
    /// </summary>
    public static class UidPlugin
    {
        /// <summary>模块显示名（宿主顶部按钮用）。</summary>
        public static string ModuleName => "UID";

        private static bool _initialized;
        private static Views.AccountManagerView? _view;

        /// <summary>
        /// 一次性初始化：迁移旧版本遗留在程序根目录的数据文件。
        /// 重复调用安全。
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Utils.File.AppPaths.MigrateLegacyFilesFromRoot();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UID] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 取（首次调用时创建）账号管理界面。
        ///
        /// <para>
        /// <b>刻意做成单例</b>：宿主把它放进自己的内容区显示，
        /// 用户切到别的页面再切回来时复用同一个实例，
        /// 这样上次浏览到的分组、页面位置等状态都会被保留。
        /// 每次 new 一个新实例就会丢掉这些状态。
        /// </para>
        /// <para>必须在 UI 线程调用。</para>
        /// </summary>
        public static FrameworkElement GetAccountManagerView()
        {
            Initialize();

            if (_view == null)
            {
                _view = new Views.AccountManagerView();
                // 帮助窗里的『关闭工具』：视图自己没窗口可关，转发给宿主处理
                _view.CloseToolRequested += (s, e) => CloseToolRequested?.Invoke(null, EventArgs.Empty);
            }

            return _view;
        }

        /// <summary>
        /// 宿主注入「全局模态遮罩」的入口（命令, 参数）。
        ///
        /// <para>
        /// 宿主在加载模块时会调用本方法，把它的遮罩控制器交过来。之后模块的页内模态框
        /// 会改用宿主那层铺在<b>整个窗口</b>上的遮罩（含顶部栏），
        /// 而不是只压暗内容区；模态框本身也会被搬进宿主的模态层，浮在遮罩之上。
        /// </para>
        /// <para>
        /// 命令：<c>mask:show</c> / <c>mask:hide</c> / <c>modal:attach</c> / <c>modal:detach</c>。
        /// 宿主没注入时，模块自动退回自带的、只盖内容区的遮罩。
        /// </para>
        /// </summary>
        public static void SetHostOverlay(Action<string, object?> handler)
        {
            Utils.UI.ModalManager.HostOverlay = handler;
        }

        /// <summary>
        /// 宿主注入「提示条」入口（消息, 是否成功）。
        ///
        /// <para>
        /// 模块原来自己在内容区右上角画提示弹窗（复制账号时那个），现在已删除，
        /// 统一改用<b>主程序标题栏的提示</b>。宿主没注入时只写调试输出。
        /// </para>
        /// </summary>
        public static void SetHostToast(Action<string, bool> handler)
        {
            Utils.UI.ToastManager.HostToast = handler;
        }

        /// <summary>
        /// 宿主注入「客户区字幕」入口（命令, 参数）。
        ///
        /// <para>
        /// 模块要在游戏画面上显示登录进度，但显示层（铺在客户区上的分层窗口 + 随机头像）
        /// 在主程序里 —— 一个进程只能有一个，所以复用宿主那一个，
        /// 风格与钓鱼 / 自动烹饪 / 自动伐木完全一致。
        /// </para>
        /// <para>
        /// 命令：<c>subtitle:start</c>（参数=位置坐标 key）/ <c>subtitle:text</c> /
        /// <c>subtitle:suffix</c> / <c>subtitle:hide</c> / <c>subtitle:opacity</c>。
        /// 宿主没注入时（例如共享组件缺失）模块这边全部走空操作。
        /// </para>
        /// </summary>
        public static void SetHostSubtitle(Action<string, object?> handler)
        {
            Utils.UI.SubtitlePresenter.Host = handler;
        }

        /// <summary>
        /// 宿主注入「窗口缩放」系数来源。
        ///
        /// <para>
        /// 模块弹出的独立窗口不会继承主界面那层 LayoutTransform，需要自己按这个系数放大，
        /// 才能和主界面比例一致。宿主没注入时按 1.0 处理。
        /// </para>
        /// </summary>
        public static void SetHostUiScaleProvider(Func<double> provider)
        {
            Utils.UI.UiScale.SetProvider(provider);
        }

        /// <summary>
        /// 宿主注入「游戏窗口是否在焦点」的查询。
        ///
        /// <para>
        /// 登录流程靠它做「不在焦点就暂停」：用户切到别的程序时，识别和按键全部停住，
        /// 焦点回游戏再接着跑。宿主没注入时按"仍在焦点"处理（不暂停）。
        /// </para>
        /// </summary>
        public static void SetHostFocusProvider(Func<bool> provider)
        {
            Utils.UI.GameFocusProvider.SetProvider(provider);
        }

        /// <summary>
        /// 打开账号管理的「使用说明」窗口。
        ///
        /// <para>
        /// 页内那个「!」按钮已经挪到<b>主程序标题栏</b>（标题右边、只在 UID 页面显示），
        /// 由宿主反射调用这里，再转给当前视图实例。
        /// 视图还没创建过（用户没进过 UID 页面）时什么都不做。
        /// </para>
        /// </summary>
        public static void ShowHelp()
        {
            try
            {
                _view?.ShowHelpDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UID] 打开使用说明失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 用户在帮助窗口里点了『关闭工具』。
        /// 宿主订阅它来切回自己的页面（首页）。
        /// </summary>
        public static event EventHandler? CloseToolRequested;
    }
}
