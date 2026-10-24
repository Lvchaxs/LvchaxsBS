using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LvchaxsBS.Config;
using LvchaxsBS.Config.FunctionConfigs;
using LvchaxsBS.Services;
using LvchaxsBS.Services.Hooks;

namespace LvchaxsBS.Core
{
    /// <summary>
    /// 快速切图：**在地图界面**按下某个地区绑定的触发键，直接把地图切到那个地区。
    ///
    /// <para>行为（从旧版「快速切图」搬过来，逻辑一致）：</para>
    /// <list type="number">
    /// <item>必须在地图界面（地图左下角缩放条底色对得上），否则不响应 —— 主界面按地区键不会有副作用。</item>
    /// <item>地图右下角"地区导航条"还在 → 直接点它展开右侧地区列表，等
    /// <see cref="QuickTeleportSettings.QuickMapRegionDelay"/> 再点地区。</item>
    /// <item>导航条已经没了、但左上角地区列表底色在 → 说明列表已经展开，直接点地区
    /// （先等 <see cref="QuickTeleportSettings.QuickMapRegionDelay"/>）。</item>
    /// <item>两个都不在 → 放弃（画面不是我们认识的地图界面）。</item>
    /// <item>点完把鼠标还原到按触发键之前的位置。</item>
    /// </list>
    ///
    /// <para>
    /// ★ 与旧版唯一的差别：**去掉了"按下开图键后 10 秒内才有效"的时间窗**。
    ///   现在只要在地图界面按下地区触发键就执行，不再要求先按 M 再赶时间。
    /// </para>
    ///
    /// <para>
    /// ★★ 「启用切图识别」关掉时（配置页可切，复选框在总开关下面）：上面 ①~④ 的
    ///   RGB 画面识别**全部跳过**，按下地区键直接按「点导航条 → 等延迟 → 点地区」执行 ——
    ///   用户自己负责在地图界面按（按到别的画面会点在地图上）。
    /// </para>
    ///
    /// <para>
    /// ★ 防重复：长按触发键会有系统自动重复的 KeyDown，这里用"按下集合"过滤
    ///   （按下记一次、抬起才清），再叠一层执行中互斥，不会连点。
    /// </para>
    /// </summary>
    public static class QuickMapSwitchLogic
    {
        // ===== 延时（毫秒）=====
        // 默认值与旧版一致；实际取值来自 QuickTeleportSettings，可在配置页的延迟卡片上调。
        private const int DefaultRegionClickDelayMs = 100;
        private const int ClickGapMs = 30;            // 点完地区的收尾等待

        // ===== 颜色容差（旧版默认值 2）=====

        /// <summary>地区表：Id（配置键）/ 显示名 / 点击坐标。</summary>
        private static readonly (string Id, string Name, CoordinateFormats.Point Coord)[] Regions =
        {
            ("Mondstadt",             "蒙德",     CoordinateFormats.快速切图_蒙德),
            ("Liyue",                 "璃月",     CoordinateFormats.快速切图_璃月),
            ("Inazuma",               "稻妻",     CoordinateFormats.快速切图_稻妻),
            ("Sumeru",                "须弥",     CoordinateFormats.快速切图_须弥),
            ("Fontaine",              "枫丹",     CoordinateFormats.快速切图_枫丹),
            ("Natlan",                "纳塔",     CoordinateFormats.快速切图_纳塔),
            ("NodKrai",               "挪德卡莱", CoordinateFormats.快速切图_挪德卡莱),
            ("Snezhnaya",             "至冬",     CoordinateFormats.快速切图_至冬),
            ("Enkanomiya",            "渊下宫",   CoordinateFormats.快速切图_渊下宫),
            ("Chasm",                 "层岩巨渊", CoordinateFormats.快速切图_层岩巨渊),
            ("SeaOfBygoneEras",       "旧日之海", CoordinateFormats.快速切图_旧日之海),
            ("AncientSacredMountain", "远古圣山", CoordinateFormats.快速切图_远古圣山),
            ("TempleOfTheVoid",       "空之神殿", CoordinateFormats.快速切图_空之神殿),
            ("Frostmoon",             "霜月",     CoordinateFormats.快速切图_霜月),
            ("SereniteaPot",          "尘歌壶",   CoordinateFormats.快速切图_尘歌壶),
            ("StellarRealm",          "千星奇域", CoordinateFormats.快速切图_千星奇域),
        };

        /// <summary>
        /// 地区显示名（找不到就原样返回 Id，方便排错）。
        /// <para>★ 故意用字面量 switch 而不是遍历 <see cref="Regions"/>：
        ///   那个数组里带着资源包的坐标类型，缺少 Resources.dll 时连类型初始化都会失败，
        ///   而这个方法在配置页里随时可能被调到。</para>
        /// </summary>
        public static string RegionDisplayName(string regionId) => regionId switch
        {
            "Mondstadt" => "蒙德",
            "Liyue" => "璃月",
            "Inazuma" => "稻妻",
            "Sumeru" => "须弥",
            "Fontaine" => "枫丹",
            "Natlan" => "纳塔",
            "NodKrai" => "挪德卡莱",
            "Snezhnaya" => "至冬",
            "Enkanomiya" => "渊下宫",
            "Chasm" => "层岩巨渊",
            "SeaOfBygoneEras" => "旧日之海",
            "AncientSacredMountain" => "远古圣山",
            "TempleOfTheVoid" => "空之神殿",
            "Frostmoon" => "霜月",
            "SereniteaPot" => "尘歌壶",
            "StellarRealm" => "千星奇域",
            _ => regionId
        };

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private static bool _initialized;

        /// <summary>正在执行的标记（同时只允许一次切换）。</summary>
        private static int _running;

        /// <summary>已按下、还没抬起的键（过滤长按自动重复）。</summary>
        private static readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);

        #region 生命周期

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            GlobalKeyboardHookService.KeyboardEvent += OnKeyboardEvent;
            Debug.WriteLine("【快速切图】已启动（地图界面内按地区触发键直接切地区）");
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            _initialized = false;

            GlobalKeyboardHookService.KeyboardEvent -= OnKeyboardEvent;
            _heldKeys.Clear();
            Interlocked.Exchange(ref _running, 0);
        }

        #endregion

        #region 按键

        private static void OnKeyboardEvent(object? sender, KeyboardEventArgs e)
        {
            if (string.IsNullOrEmpty(e.KeyName)) return;

            if (e.EventType == KeyboardEventType.KeyUp)
            {
                _heldKeys.Remove(e.KeyName);
                return;
            }

            if (e.IsInjected) return;
            if (!_heldKeys.Add(e.KeyName)) return;   // 长按自动重复：只认第一次

            try
            {
                var settings = ConfigManager.Get<QuickTeleportSettings>();
                if (!settings.EnableQuickMapSwitch) return;
                if (!ConfigManager.Get<HomePageSettings>().MasterSwitch) return;

                // 必须先找到这个键绑到哪个地区，别的键直接放过（不做任何耗时操作）
                string? regionId = FindRegionByKey(settings, e.KeyName);
                if (regionId == null) return;

                // 不是游戏窗口在前台 → 不响应（和旧版一致，避免在别的程序里误触）
                if (!WindowFocusService.IsTargetFocused) return;

                // 上一次还没跑完 → 不叠加
                if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;

                var region = FindRegion(regionId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteAsync(region.Id, region.Name, region.Coord);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"【快速切图】执行异常: {ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【快速切图】按键处理异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 找这个键绑到哪个地区。
        /// <para>★ 用 <see cref="IconService.MatchesTriggerKey"/> 而不是直接比较：
        ///   地区键也支持组合键（如 左Ctrl+1），并且普通键在按着 Ctrl/Alt 时不被顺带触发。</para>
        /// </summary>
        private static string? FindRegionByKey(QuickTeleportSettings settings, string keyName)
        {
            var map = settings.QuickMapKeys;
            if (map == null || map.Count == 0) return null;

            foreach (var (id, _, _) in Regions)
            {
                if (map.TryGetValue(id, out var bound)
                    && !string.IsNullOrWhiteSpace(bound)
                    && IconService.MatchesTriggerKey(bound, keyName))
                {
                    return id;
                }
            }

            return null;
        }

        private static (string Id, string Name, CoordinateFormats.Point Coord) FindRegion(string regionId)
        {
            foreach (var r in Regions)
            {
                if (r.Id == regionId) return r;
            }

            return Regions[0];
        }

        #endregion

        #region 执行

        private static async Task ExecuteAsync(string regionId, string regionName, CoordinateFormats.Point coord)
        {
            if (!CoordinateFormats.CacheReady)
            {
                Debug.WriteLine("【快速切图】坐标缓存未就绪，跳过");
                return;
            }

            var settings = ConfigManager.Get<QuickTeleportSettings>();
            bool recognize = settings.EnableCutRecognition;

            // ★ 识别模式：只在地图界面响应。不在图里就静默退出（旧版同样先验地图色）。
            //   地图界面判定直接复用全局 DetectionManager.IsInMap（由 快速传送_地图缩放按钮 每 50ms 维护，
            //   不再单独保留 快速切图_地图探测点 这个重复坐标）。
            //   「启用切图识别」关掉后完全不看画面，按下地区键直接执行（用户要求）。
            if (recognize && !DetectionManager.IsInMap)
            {
                Debug.WriteLine($"【快速切图】{regionName}: 当前不在地图界面，忽略");
                return;
            }

            GetCursorPos(out POINT origin);

            try
            {
                int regionDelay = ClampDelay(settings.QuickMapRegionDelay, DefaultRegionClickDelayMs);

                // 识别关掉 ⇒ 两个探测都不跑，navVisible 恒 false、regionPanelOpen 恒 false，
                // 下面走 else if 的 !recognize 分支（直接按导航条路径执行）
                bool navVisible = recognize && IsNavBarVisible();
                bool regionPanelOpen = !navVisible && recognize && IsRegionPanelOpen();

                if (regionPanelOpen)
                {
                    // 地区列表已经展开 ⇒ 直接点地区
                    await Task.Delay(regionDelay);
                    Debug.WriteLine($"【快速切图】{regionName}: 地区列表已展开，直接点地区");
                }
                else if (navVisible || !recognize)
                {
                    // 导航条还在 ⇒ 点它展开右侧地区列表；不识别模式也走这条（直接执行）
                    var nav = CoordinateFormats.快速切图_导航条区域.ToScreen().Rect;

                    // 导航条直接点，不再等待（用户要求：去掉"点导航条之前的延迟"）
                    await LeftClickAsync((nav.X1 + nav.X2) / 2, (nav.Y1 + nav.Y2) / 2);
                    await Task.Delay(regionDelay);

                    Debug.WriteLine($"【快速切图】{regionName}: 已点导航条，等 {regionDelay}ms 后点地区");
                }
                else
                {
                    Debug.WriteLine($"【快速切图】{regionName}: 既没有导航条也没有地区列表，放弃");
                    return;
                }

                var target = coord.ToScreen();
                await LeftClickAsync(target.X, target.Y);
                await Task.Delay(ClickGapMs);

                ToastService.Show("快速切图", $"已切换到 {regionName}");
                Debug.WriteLine($"【快速切图】{regionName}: 完成（点击 {target.X},{target.Y}）");
            }
            finally
            {
                SetCursorPos(origin.X, origin.Y);
            }
        }

        private static Task LeftClickAsync(int x, int y)
            => Task.Run(() => SimulationService.LeftClickAt(x, y));

        /// <summary>延迟夹到 0~500ms（负数才回落到默认），与配置页滑块的上限一致。</summary>
        private static int ClampDelay(int value, int fallback)
            => value < 0 ? fallback : Math.Min(value, 500);

        #endregion

        #region 画面判定

        /// <summary>右侧地区列表是否已展开（左上角深色底）。</summary>
        private static bool IsRegionPanelOpen()
        {
            try
            {
                return ImageRecognition.CheckPixelColor(CoordinateFormats.快速切图_地区探测点);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【快速切图】地区列表判定异常: {ex.Message}");
                return false;
            }
        }

        private static bool IsNavBarVisible()
        {
            try
            {
                var region = CoordinateFormats.快速切图_导航条区域.ToScreen();
                var screen = region.Rect;

                int width = screen.Width;
                int height = screen.Height;
                if (width <= 0 || height <= 0) return false;

                var targets = region.Colors;
                if (targets == null || targets.Count == 0) return false;
                int tol = region.Tolerance;

                var pixels = ImageRecognition.CaptureRegionGDI(screen.X1, screen.Y1, width, height);
                if (pixels == null) return false;

                // GDI 抓回来的是 BGRA；扫到任意一个像素命中任一目标色，就算导航条还在
                for (int i = 0; i + 3 < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];

                    foreach (var t in targets)
                    {
                        if (Math.Abs(r - t.R) <= tol &&
                            Math.Abs(g - t.G) <= tol &&
                            Math.Abs(b - t.B) <= tol)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"【快速切图】导航条判定异常: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
