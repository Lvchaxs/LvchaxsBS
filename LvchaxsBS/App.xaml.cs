using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LvchaxsBS.Config;
using LvchaxsBS.Core;
using LvchaxsBS.Services;
using LvchaxsBS.Services.Hooks;
using LvchaxsBS.Toolbox;
using LvchaxsBS.UI.Dialogs;

namespace LvchaxsBS
{
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        protected override void OnStartup(StartupEventArgs e)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            SetProcessDPIAware();

            base.OnStartup(e);

            // 去掉控件默认焦点虚线框
            EventManager.RegisterClassHandler(typeof(Control),
                FrameworkElement.GotFocusEvent,
                new RoutedEventHandler(OnAnyControlGotFocus));

            // 全局快速剪贴板：所有 TextBox（含插件 dll 里的）的 Ctrl+C / X / V 都改走 Win32，
            // 绕开 WPF/OLE 那个"等所有剪贴板监听者处理完才返回"的卡顿。
            FastClipboard.Install();

            // 0. 启动自检：exe 同目录缺了依赖 dll 就先从正式版渠道下载补齐。
            //    ★ 同步等它下载完 —— 这一步在建窗口（StartupUri）之前，
            //      下面探测共享组件 / 插件时它们才在位。
            try
            {
                UpdateService.EnsureDependenciesAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【启动】依赖自检异常（不影响启动）：{ex.Message}");
            }

            // 1. 一次性加载所有配置
            ConfigManager.LoadAll();

            // 2. 共享组件（LvchaxsBS_Resources.dll）：
            //    它是一个可选组件 —— 不在 exe 同目录时下面这步注入整个跳过，
            //    图片全部留空、图像识别与模拟点击停用，但 UI 与配置照常工作。
            //    注意：Register() 里引用了共享类型，dll 缺失时连 JIT 都会失败，
            //    所以必须先用 IsAvailable（纯文件探测）拦一道再调。
            if (SharedComponent.IsAvailable)
            {
                SharedComponent.Register();
            }
            else
            {
                Debug.WriteLine("【启动】未找到 LvchaxsBS_Resources.dll：图片留空、图像识别与模拟点击停用，" +
                                "UI 交互与配置修改不受影响");
            }

            // 3. 根据配置应用主题
            ThemeService.LoadFromConfig();

            // 4. 初始化功能引擎（识别/模拟输入相关的都在这里，缺共享组件时整块跳过）
            if (SharedComponent.IsAvailable)
            {
                _engineInitialized = true;
                InitializeEngine();
            }

            // 5. 探测可选插件（exe 同目录的 LvchaxsBS_uid.dll = UID 账号管理模块）
            UidModuleService.Probe();

            // 6. 「刚更新完」的这一次启动：自动弹一次当前版本的改动说明
            //    （开关由更新脚本 update.bat 在拉起 exe 之前写成 true，见 UpdateService）
            TryShowPendingChangelog();
        }

        /// <summary>
        /// 检查 <c>AppSettings.ShowChangelogOnStartup</c>；为 true 就弹一次「版本改动」并立刻复位。
        ///
        /// <para>★ 先复位再弹：万一弹窗过程抛异常，也不会让这个窗口从此每次启动都弹。</para>
        ///
        /// <para>★ 延后到 <see cref="DispatcherPriority.ApplicationIdle"/> 再弹：模态遮罩层是
        ///   MainWindow 初始化时注册进来的，太早弹拿不到遮罩。</para>
        /// </summary>
        private void TryShowPendingChangelog()
        {
            try
            {
                if (!ConfigManager.Get<AppSettings>().ShowChangelogOnStartup)
                    return;

                ConfigSync.Mutate<AppSettings>(s => s.ShowChangelogOnStartup = false);
                Debug.WriteLine("【启动】检测到更新标记，本次启动自动显示一次版本改动");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【启动】检查更新标记失败：{ex.Message}");
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var dialog = new ChangelogDialog
                    {
                        Owner = Application.Current.MainWindow
                    };
                    ModalOverlayService.ShowModalDialog(dialog);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"【启动】显示版本改动失败：{ex.Message}");
                }
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// 每一步都用 try/catch 包裹，避免某个钩子注册失败拖垮整个程序启动。
        /// </summary>
        /// <summary>
        /// 引擎（图标浮层 / 字幕浮层 / 键鼠钩子 / 检测循环）是否已经初始化过。
        /// <para>★ 启动时缺 <c>LvchaxsBS_Resources.dll</c> ⇒ 整块跳过；启动自检下载补齐后调
        /// <see cref="EnsureEngineStarted"/> 补上一次（只补一次）。</para>
        /// </summary>
        private bool _engineInitialized;

        /// <summary>
        /// 运行中装上资源包后，把启动时跳过的那次引擎初始化补上。
        /// <para>
        /// ★ 只有「启动时没初始化」才会真正执行，已初始化过直接返回 —— 避免钩子 / 检测循环被重复启动。
        /// ★ 调这个之前必须先 <see cref="Services.SharedComponent.Reload"/> 成功（否则引擎里引用共享类型的
        ///   代码会在 JIT 阶段失败）。
        /// </para>
        /// </summary>
        public static void EnsureEngineStarted()
        {
            try
            {
                if (Application.Current is not App app) return;
                if (app._engineInitialized) return;

                app._engineInitialized = true;
                app.InitializeEngine();
                Debug.WriteLine("【热加载】引擎初始化已补齐（图标浮层 / 键鼠钩子 / 检测循环）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【热加载】引擎初始化失败：{ex.GetBaseException().Message}");
            }
        }

        private void InitializeEngine()
        {
            IconService.Initialize();
            SubtitleOverlayService.Initialize();

            // ===== 键盘钩子 =====
            try
            {
                GlobalKeyboardHookService.Start();
                GlobalKeyboardHookService.KeyboardEvent += OnKeyboardEvent;
                Debug.WriteLine("键盘钩子注册成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"键盘钩子注册失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"键盘钩子注册失败：{ex.Message}");
            }

            // ===== 快速切图（地图界面内按地区触发键直接切地区）=====
            // 复用的是上面那个全局键盘钩子，所以必须在钩子启动之后再订阅
            try
            {
                QuickMapSwitchLogic.Initialize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"快速切图初始化失败：{ex.Message}");
            }

            // ===== 用户输入监视（给 UID 登录监控让路用）=====
            // 复用上面两个钩子的事件，只记「用户真实操作」的时刻（模拟按键不算），
            // UID 那边据此在用户刚动过键鼠时不发模拟按键。
            try
            {
                UserInputMonitor.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"用户输入监视启动失败：{ex.Message}");
            }

            // ===== 鼠标钩子（延后到应用空闲，避免和窗口初始化抢时序）=====
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    GlobalMouseHookService.Start();
                    Debug.WriteLine("鼠标钩子注册成功");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"鼠标钩子注册失败：{ex.Message}");
                }
            }), DispatcherPriority.ApplicationIdle);

            // ===== 手柄钩子 =====
            try
            {
                GlobalGamepadHookService.GamepadEvent += OnGamepadEvent;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var mainWindow = Application.Current.MainWindow;
                        if (mainWindow != null)
                        {
                            GlobalGamepadHookService.Start(mainWindow);
                            Debug.WriteLine("手柄钩子注册成功");
                        }
                        else
                        {
                            Debug.WriteLine("手柄钩子注册失败：主窗口为空");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"手柄钩子注册失败：{ex.Message}");
                    }
                }), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"手柄钩子订阅失败：{ex.Message}");
            }

            // ===== 坐标 + 模板：挪到窗口首帧之后加载 =====
            // 这两步要解码几十张模板 PNG + 反序列化坐标缓存，纯 CPU/IO，和界面显示无关。
            // 留在 OnStartup 里等于让窗口白等它们（它们跑完才开始建窗口），
            // 现在统一放进下面那个 ApplicationIdle 块 —— 顺序仍是"先加载、后启动检测循环"。
            // 见 WarmUpTemplatesAndCoordinates。

            // ===== 坐标 + 模板 + 焦点服务 + 检测循环（启动流程最后一步）=====
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 先把模板 / 坐标读进来，再启动检测循环 —— 顺序不能反，
                // 否则首轮检测会在模板还是空的时候跑一遍（白跑一轮）。
                WarmUpTemplatesAndCoordinates();

                try
                {
                    WindowFocusService.Start();

                    // 焦点服务启动后，如果游戏已聚焦，主动启动检测循环
                    if (WindowFocusService.IsTargetFocused)
                    {
                        DetectionManager.StartLoop();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"窗口焦点服务启动失败：{ex.Message}");
                }
            }), DispatcherPriority.ApplicationIdle);

            // ===== 自动检查更新 =====
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000);
                        await UpdateCheckService.CheckAndNotifyAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"自动检查更新失败：{ex.Message}");
                    }
                });
                Debug.WriteLine("检查更新服务已启动");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检查更新服务启动失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 预热坐标缓存与识别模板。
        /// <para>
        /// ★ 从 <see cref="OnStartup"/> 的同步段挪来的：这两步只影响"检测能不能跑"，
        ///   不影响窗口显示，放在首帧之后的 <see cref="DispatcherPriority.ApplicationIdle"/> 里做，
        ///   窗口就能立刻显示出来，而不用等几十张 PNG 解码完。两步各自 try/catch，
        ///   出错只记日志（和原来的行为一致）。
        /// </para>
        /// </summary>
        private static void WarmUpTemplatesAndCoordinates()
        {
            try
            {
                _ = CoordinateFormats.HasWindowInfo;
                Debug.WriteLine("坐标加载成功");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"坐标加载失败：{ex.Message}");
            }

            try
            {
                TemplateManager.LoadTemplates();
                _ = TemplateManager.HasWindowInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"模板加载失败：{ex.Message}");
            }
        }

        private void OnAnyControlGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is Control control)
            {
                control.FocusVisualStyle = null;
            }
        }

        /// <summary>
        /// 全局键盘事件：
        /// <list type="bullet">
        /// <item>Ctrl+F12 —— 关闭程序（任何情况）。</item>
        /// <item>右 Shift —— 打开截图工具（游戏窗口聚焦时）。</item>
        /// </list>
        /// </summary>
        private void OnKeyboardEvent(object? sender, KeyboardEventArgs args)
        {
            if (SimulationService.IsSimulating)
                return;

            if (args.EventType != KeyboardEventType.KeyDown)
                return;

            // ★ Ctrl+F12：程序关闭快捷键 —— 任何情况都直接关，
            //   不看焦点在哪个窗口、也不看游戏在不在前台。
            if (IsCloseHotkey(args))
            {
                Dispatcher.Invoke(() =>
                {
                    try { Shutdown(); }
                    catch { Environment.Exit(0); }
                });
                return;
            }

            if (args.VirtualKeyCode == 0xA1)
            {
                if (!WindowFocusService.IsTargetFocused)
                    return;

                // ★ 已经开着就别再开：右键 Shift 连按 / 按住不放都会不断补发 KeyDown，
                //   而且窗口里的 SHIFT 是「切换坐标模式」用的，不能每次都新弹一个窗口出来。
                if (!ScreenshotToolWindow.TryEnter())
                    return;

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var captureWindow = new ScreenshotToolWindow();
                        captureWindow.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"打开截图工具失败：{ex.Message}");
                    }
                    finally
                    {
                        ScreenshotToolWindow.Leave();
                    }
                });
            }
        }

        // 虚拟键码（Ctrl / Alt 要判左右，钩子给的 vkCode 不会是通用的 0x11 / 0x12）
        private const int VkF12 = 0x7B;
        private const int VkControl = 0x11;
        private const int VkLControl = 0xA2;
        private const int VkRControl = 0xA3;
        private const int VkMenu = 0x12;
        private const int VkLMenu = 0xA4;
        private const int VkRMenu = 0xA5;

        /// <summary>
        /// 是不是「Ctrl+F12」—— 程序关闭快捷键。
        /// <para>
        /// ★ Alt 同时按着时<b>不算</b>（那是 Ctrl+Alt+F12）：关闭快捷键要求 Alt 没按下，
        ///   触发键页也按同一条规则只在「只激活了 Ctrl」时禁掉 F12，两边保持一致。
        /// </para>
        /// <para>
        /// ★ 修饰键状态取钩子自己维护的物理按键集合（<c>IsPhysicallyDown</c>）——
        ///   钩子回调不在 UI 线程，读不到 WPF 的 <c>Keyboard.Modifiers</c>。
        /// </para>
        /// </summary>
        private static bool IsCloseHotkey(KeyboardEventArgs args)
        {
            if (args.VirtualKeyCode != VkF12) return false;
            if (args.IsRepeat) return false;   // 按住不放只关一次

            if (GlobalKeyboardHookService.IsPhysicallyDown(VkMenu)
                || GlobalKeyboardHookService.IsPhysicallyDown(VkLMenu)
                || GlobalKeyboardHookService.IsPhysicallyDown(VkRMenu))
                return false;

            return GlobalKeyboardHookService.IsPhysicallyDown(VkControl)
                   || GlobalKeyboardHookService.IsPhysicallyDown(VkLControl)
                   || GlobalKeyboardHookService.IsPhysicallyDown(VkRControl);
        }

        private void OnGamepadEvent(object? sender, GamepadEventArgs e)
        {
            string buttonName = GlobalGamepadHookService.GetButtonName(e.Button);
            string state = e.IsPressed ? "按下" : "抬起";
            Debug.WriteLine($"[手柄] {buttonName} {state}");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                GlobalGamepadHookService.Stop();
                UserInputMonitor.Stop();
                IconService.Shutdown();
                SubtitleOverlayService.Shutdown();
                GlobalMouseHookService.Stop();
                GlobalKeyboardHookService.Stop();

                // 快速切图放在最后单独 try：它的静态字段引用了资源包里的坐标类型，
                // 资源包缺失时（这次运行根本没初始化过它）连 JIT 都会失败，
                // 不能让它把上面的清理连带打断。
                try { QuickMapSwitchLogic.Shutdown(); }
                catch (Exception ex) { Debug.WriteLine($"快速切图卸载失败：{ex.Message}"); }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"退出清理失败：{ex.Message}");
            }

            // 退出前把防抖延迟中尚未落盘的配置全部写回磁盘
            ConfigManager.FlushAll();

            base.OnExit(e);

            // 兜底强制退出。
            // 引擎里挂着全局低级钩子、检测循环和常驻浮层窗口，实测出现过
            // "窗口关了、OnExit 也走完了，但进程仍留在后台"的情况（exe 一直不释放，
            // 钩子还在响应按键）。清理已在上方做完，这里直接终止进程，确保干净退出。
            Environment.Exit(e.ApplicationExitCode);
        }
    }
}
