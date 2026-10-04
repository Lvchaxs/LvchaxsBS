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

            // 1. 一次性加载所有配置
            ConfigManager.LoadAll();

            // 2. 根据配置应用主题
            ThemeService.LoadFromConfig();

            // 3. 初始化功能引擎
            InitializeEngine();
        }

        /// <summary>
        /// 初始化功能引擎（从旧项目 App.OnStartup 移植并按顺序整理）。
        /// 每一步都用 try/catch 包裹，避免某个钩子注册失败拖垮整个程序启动。
        /// </summary>
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

            // ===== 坐标 + 模板 =====
            try
            {
                var dummy = CoordinateFormats.HasWindowInfo;
                Debug.WriteLine("坐标加载成功");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"坐标加载失败：{ex.Message}");
            }

            try
            {
                TemplateManager.LoadTemplates();
                var dummy2 = TemplateManager.HasWindowInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"模板加载失败：{ex.Message}");
            }

            // ===== 焦点服务 + 检测循环（启动流程最后一步）=====
            Dispatcher.BeginInvoke(new Action(() =>
            {
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

        private void OnAnyControlGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is Control control)
            {
                control.FocusVisualStyle = null;
            }
        }

        /// <summary>右 Shift 打开截图工具（游戏窗口聚焦时）。</summary>
        private void OnKeyboardEvent(object? sender, KeyboardEventArgs args)
        {
            if (SimulationService.IsSimulating)
                return;

            if (args.EventType != KeyboardEventType.KeyDown)
                return;

            if (args.VirtualKeyCode == 0xA1)
            {
                if (!WindowFocusService.IsTargetFocused)
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
                });
            }
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
                IconService.Shutdown();
                SubtitleOverlayService.Shutdown();
                GlobalMouseHookService.Stop();
                GlobalKeyboardHookService.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"退出清理失败：{ex.Message}");
            }

            // 退出前把防抖延迟中尚未落盘的配置全部写回磁盘
            ConfigManager.FlushAll();

            base.OnExit(e);
        }
    }
}
