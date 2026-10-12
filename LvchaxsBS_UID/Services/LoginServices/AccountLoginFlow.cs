using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LvchaxsBS.Core;       // SimulationService（按 ESC / 点击）、TemplateManager（取模板）
using LvchaxsBS.Services;   // CoordinateFormats、ImageRecognition
using LvchaxsBS_UID.Utils.UI;

namespace LvchaxsBS_UID.Services.LoginServices
{
    /// <summary>
    /// 登录流程的「图像识别 + 按键/点击」循环。
    ///
    /// <para>
    /// 从「确认登录」开始：游戏窗口已被激活（见 <c>Utils.GameWindow.GameWindowActivator</c>），
    /// 然后每轮同时识别五个区域：
    /// </para>
    /// <list type="bullet">
    ///   <item><b>主界面</b>：左上角派蒙头像（<c>主界面区域</c> + 模板 <c>其他模版_主界面</c>）</item>
    ///   <item><b>派蒙菜单</b>：主界面按 ESC 打开的那一页（<c>派蒙菜单区域</c> + 模板 <c>其他模版_ESC菜单</c>）</item>
    ///   <item><b>设置界面</b>：派蒙菜单点左下角「设置」后打开的那一页（<c>设置界面区域</c> + 模板 <c>其他模版_退出游戏_设置</c>）</item>
    ///   <item><b>账户跳转</b>：设置界面里点过「账户」后出现的跳转按钮（<c>账户跳转区域</c> + 模板 <c>其他模版_退出游戏_点击跳转</c>）</item>
    ///   <item><b>用户中心</b>：点过「账户跳转」之后加载出来的页面，看到刷新按钮就说明页面好了
    ///     （<c>用户中心刷新按钮区域</c> + 模板 <c>其他模版_退出游戏_用户中心刷新</c>）</item>
    /// </list>
    /// <para>
    /// 整个流程就是「退出登录」那条链，按顺序分五步走：
    /// </para>
    /// <list type="number">
    ///   <item><b>主界面</b>：按一次 ESC 打开派蒙菜单；</item>
    ///   <item><b>派蒙菜单</b>：点一次 <c>设置点击坐标</c>（左下角「设置」）打开设置界面；</item>
    ///   <item><b>设置界面</b>：点一次 <c>账户点击坐标</c> 选中「账户」，下一轮跳转按钮就出来了
    ///     （只命中设置界面时；跳转按钮本来就看得见时这步会自动跳过）；</item>
    ///   <item><b>账户跳转</b>：点一次跳转，等用户中心页面加载；</item>
    ///   <item><b>用户中心</b>：看到刷新按钮 → 原地点一下（借 <c>账户跳转区域</c> 的位置）→
    ///     等 <c>ClickDelayMs</c> 按 TAB → 再等 <c>ClickDelayMs</c> 按 ENTER，
    ///     <b>登录到此完成</b>。</item>
    /// </list>
    /// <para>
    /// ★ <b>自适应接续</b>：每轮都重新检测五个区域来判定「当前处于哪个阶段」，只做该阶段的动作，
    /// 不记历史状态。所以从任何一步中断之后都能接着跑 —— 已经在设置界面就直接走第三步，
    /// 不会按一路 ESC 回主界面重来。一轮里同时命中多个区域时（界面切换动画中可能出现），
    /// <b>越靠后的阶段越优先</b>：既然已经能看到用户中心，就没有必要退回菜单再点一次设置。
    /// </para>
    /// <para>
    /// 五个区域都没命中时的处理分两档：
    /// </para>
    /// <list type="bullet">
    ///   <item><b>初始档</b>（还从没命中过）：刚激活窗口，不知道当前停在哪个画面，
    ///     每轮按一次 ESC 把挡在前面的其他界面关掉，再重新找；</item>
    ///   <item><b>已知档</b>（命中过一次）：说明已经在某个阶段里了，给
    ///     <see cref="LostGraceMs"/> 毫秒宽限期，期间只安静等下一轮
    ///     （界面切换动画 / 加载中都表现为全线未命中），超过宽限期仍无命中才恢复每轮按 ESC。</item>
    /// </list>
    /// <para>
    /// 节拍：每轮之内四个区域连续检测、彼此无间隔；一轮结束后等
    /// <see cref="RoundIntervalMs"/> 毫秒进下一轮；命中之后、执行点击之前额外等
    /// <see cref="ClickDelayMs"/> 毫秒，让刚切换出来的画面稳定下来再点。
    /// </para>
    /// <para>
    /// 识别能力来自共享库 <c>LvchaxsBS_Resources</c>（本模块只引用它，不引用宿主）。
    /// 字幕走宿主注入的覆盖层（复用钓鱼/烹饪/伐木那一层，本模块不自己建窗口）。
    /// </para>
    /// </summary>
    public static class AccountLoginFlow
    {
        /// <summary>字幕位置 key（定义在共享库 <c>CoordinateFormats</c> 的「退出登录-用户中心退出」段）。</summary>
        public const string SubtitlePositionKey = "登录_字幕显示坐标";

        /// <summary>主界面（左上角派蒙头像存在即为主界面）。</summary>
        public const string MainRegionKey = "主界面区域";
        public const string MainTemplateKey = "其他模版_主界面";

        /// <summary>派蒙菜单（主界面按 ESC 后打开的那一页）。</summary>
        public const string PaimonRegionKey = "派蒙菜单区域";
        public const string PaimonTemplateKey = "其他模版_ESC菜单";

        /// <summary>设置界面（派蒙菜单点左下角「设置」后打开的那一页）。</summary>
        public const string SettingsRegionKey = "设置界面区域";
        public const string SettingsTemplateKey = "其他模版_退出游戏_设置";

        /// <summary>账户跳转按钮（设置界面点过「账户」之后出现的那个按钮）。</summary>
        public const string JumpRegionKey = "账户跳转区域";
        public const string JumpTemplateKey = "其他模版_退出游戏_点击跳转";

        /// <summary>
        /// 用户中心刷新按钮：点过「账户跳转」之后加载出来的用户中心页面里才有，
        /// 看到它就说明页面加载完了，是整个流程的最后一个阶段。
        /// </summary>
        public const string UserCenterRegionKey = "用户中心刷新按钮区域";
        public const string UserCenterTemplateKey = "其他模版_退出游戏_用户中心刷新";

        /// <summary>派蒙菜单左下角「设置」按钮。</summary>
        public const string SettingsClickKey = "设置点击坐标";

        /// <summary>设置界面内的「账户」。</summary>
        public const string AccountClickKey = "账户点击坐标";

        /// <summary>每轮之间的等待（轮内四个区域连续检测，彼此无间隔）。</summary>
        private const int RoundIntervalMs = 100;

        /// <summary>命中之后、执行点击之前的等待：让刚切换出来的画面稳定下来再点。</summary>
        private const int ClickDelayMs = 200;

        /// <summary>
        /// 曾命中过任意目标之后，允许连续多久一个都检测不到。
        /// 超过这个时长才恢复「每轮按 ESC」——期间只安静地等下一轮
        /// （界面切换动画、加载中都表现为短暂的全线未命中）。
        /// </summary>
        private const int LostGraceMs = 10000;

        /// <summary>第二行字幕：进行中。</summary>
        private const string Line2Running = "登录中...";

        /// <summary>第二行字幕：成功。</summary>
        private const string Line2Succeeded = "登录成功-TAB关闭";

        /// <summary>模板缺失只警告一次，避免每轮刷屏。</summary>
        private static bool _missingTemplateWarned;

        #region 字幕

        /// <summary>
        /// 挂出登录用的两行字幕：
        /// 第一行「正在执行登录账号 xxx」（带头像，每次重掷 → 随机 空 / 荧）；
        /// 第二行「登录中...」（不要头像，显示在第一行下面）。
        /// </summary>
        public static void ShowRunningSubtitles(string accountName)
        {
            // 第一行
            SubtitlePresenter.StartRun(SubtitlePositionKey);
            SubtitlePresenter.SetText(string.IsNullOrEmpty(accountName)
                ? "正在执行登录账号"
                : $"正在执行登录账号 {accountName}");

            // 第二行：只要文字，不要头像
            SubtitlePresenter.SetAvatarVisible2(false);
            SubtitlePresenter.StartRun2(SubtitlePositionKey);
            SubtitlePresenter.SetText2(Line2Running);
        }

        /// <summary>登录成功后把第二行换成「登录成功-TAB关闭」。</summary>
        public static void ShowSucceededSubtitle() => SubtitlePresenter.SetText2(Line2Succeeded);

        /// <summary>收起两行字幕。</summary>
        public static void HideSubtitles()
        {
            SubtitlePresenter.Hide();
            SubtitlePresenter.Hide2();
        }

        #endregion

        #region 主流程

        /// <summary>
        /// 四个区域都没命中时，这一轮要不要按 ESC。
        /// </summary>
        /// <param name="everHitAny">本次流程启动以来是否命中过任意目标。</param>
        /// <param name="lostMs">距离上一次命中任意目标过去了多少毫秒。</param>
        /// <returns>
        /// 初始档（从没命中过）→ 按，先把挡在前面的其他界面关掉；
        /// 已知档 → 宽限期内不按，超过 <see cref="LostGraceMs"/> 才恢复按。
        /// </returns>
        internal static bool ShouldPressEscape(bool everHitAny, long lostMs)
            => !everHitAny || lostMs >= LostGraceMs;

        /// <summary>
        /// 跑登录流程，返回是否达成目标（点击了「账户跳转区域」）。
        /// 被取消时抛 <see cref="OperationCanceledException"/>。
        /// </summary>
        /// <param name="accountName">要登录的账号名（已显示在字幕第一行）。</param>
        /// <param name="token">取消令牌（用户点「停止登录」时触发）。</param>
        public static async Task<bool> RunAsync(string accountName, CancellationToken token)
        {
            ShowRunningSubtitles(accountName);

            // 「四个都没命中时按不按 ESC」分两档：
            //   · 初始档（还从没命中过任何目标）：刚激活窗口，不知道当前停在哪个画面，
            //     每轮检测不到就按一次 ESC，把挡在前面的其他界面关掉再重新找；
            //   · 已知档（命中过一次，说明已经在某个阶段里了）：给 <see cref="LostGraceMs"/>
            //     毫秒宽限期，期间检测不到只是等下一轮（切换动画 / 加载中都表现为全线未命中），
            //     超过宽限期仍无命中，才退回初始档那样每轮按 ESC。
            bool everHitAny = false;
            long lastHitTicks = Environment.TickCount64;

            for (int round = 1; ; round++)
            {
                token.ThrowIfCancellationRequested();

                // 每轮识别一次，五个区域一起（区域之间没有间隔，连着测完再统一决策）
                var main = Detect(MainRegionKey, MainTemplateKey);
                var paimon = Detect(PaimonRegionKey, PaimonTemplateKey);
                var settings = Detect(SettingsRegionKey, SettingsTemplateKey);
                var jump = Detect(JumpRegionKey, JumpTemplateKey);
                var userCenter = Detect(UserCenterRegionKey, UserCenterTemplateKey);

                if (main.detected || paimon.detected || settings.detected || jump.detected || userCenter.detected)
                {
                    everHitAny = true;
                    lastHitTicks = Environment.TickCount64;
                }

                Debug.WriteLine(
                    $"[UID/登录] 第 {round} 轮：" +
                    $"主界面 {main.similarity * 100:F1}%({(main.detected ? "中" : "-")}) " +
                    $"派蒙菜单 {paimon.similarity * 100:F1}%({(paimon.detected ? "中" : "-")}) " +
                    $"设置界面 {settings.similarity * 100:F1}%({(settings.detected ? "中" : "-")}) " +
                    $"账户跳转 {jump.similarity * 100:F1}%({(jump.detected ? "中" : "-")}) " +
                    $"用户中心 {userCenter.similarity * 100:F1}%({(userCenter.detected ? "中" : "-")})" +
                    (everHitAny ? "" : "  [初始档]"));

                // ① 用户中心（第五步，终点）—— 点过跳转后加载出来的页面，
                //    看到刷新按钮就说明页面好了：原地点一下 → TAB → ENTER，登录到此完成
                if (userCenter.detected)
                {
                    Debug.WriteLine("[UID/登录] → 阶段5：用户中心已加载，原地点击 → TAB → ENTER，登录完成");
                    await Task.Delay(ClickDelayMs, token);
                    ClickRegionCenter(JumpRegionKey);   // 这个点击不要精确坐标，借 账户跳转区域 的位置
                    await Task.Delay(ClickDelayMs, token);
                    SimulationService.PressTab();
                    await Task.Delay(ClickDelayMs, token);
                    SimulationService.PressEnter();
                    return true;
                }

                // ② 账户跳转（第四步）—— 点一下进用户中心，然后继续检测（还没完）
                if (jump.detected)
                {
                    Debug.WriteLine("[UID/登录] → 阶段4：检测到账户跳转，点击跳转");
                    await Task.Delay(ClickDelayMs, token);
                    ClickRegionCenter(JumpRegionKey);
                    await Task.Delay(RoundIntervalMs, token);
                    continue;
                }

                // ③ 设置界面（第三步的前半）—— 只看到设置界面：说明还没选中「账户」选项，
                //    跳转按钮在账户选项里所以看不到。点一次「账户」，下一轮就能看到跳转了
                if (settings.detected)
                {
                    Debug.WriteLine("[UID/登录] → 阶段3：设置界面，先点「账户」");
                    await Task.Delay(ClickDelayMs, token);
                    ClickPoint(AccountClickKey);
                    await Task.Delay(RoundIntervalMs, token);
                    continue;
                }

                // ④ 派蒙菜单（第二步）—— 点左下角「设置」，进入设置界面后再继续检测
                if (paimon.detected)
                {
                    Debug.WriteLine("[UID/登录] → 阶段2：派蒙菜单，点击「设置」");
                    await Task.Delay(ClickDelayMs, token);
                    ClickPoint(SettingsClickKey);
                    await Task.Delay(RoundIntervalMs, token);
                    continue;
                }

                // ⑤ 主界面（第一步）—— 这一下 ESC 正好打开派蒙菜单
                if (main.detected)
                {
                    Debug.WriteLine("[UID/登录] → 阶段1：主界面，按 ESC 打开派蒙菜单");
                    SimulationService.PressEscape();
                    await Task.Delay(RoundIntervalMs, token);
                    continue;
                }

                // ⑥ 五个都没检测到
                if (ShouldPressEscape(everHitAny, Environment.TickCount64 - lastHitTicks))
                {
                    Debug.WriteLine(everHitAny
                        ? $"[UID/登录] 已连续 {Environment.TickCount64 - lastHitTicks} ms 未检测到任何目标，" +
                          "恢复每轮按 ESC"
                        : "[UID/登录] 初始档：未检测到目标，按 ESC 关掉其他界面");
                    SimulationService.PressEscape();
                }

                await Task.Delay(RoundIntervalMs, token);
            }
        }

        #endregion

        #region 点击

        /// <summary>
        /// 点击一个 <see cref="CoordinateFormats.Point"/> 坐标（缓存里已经是屏幕坐标，可直接用）。
        /// Point 是值类型，<c>GetCached&lt;T&gt;</c> 要求引用类型，所以照自动伐木的做法走 object 再强转。
        /// </summary>
        private static void ClickPoint(string pointKey)
        {
            try
            {
                var pointObj = CoordinateFormats.GetCached<object>(pointKey);
                if (pointObj == null)
                {
                    Debug.WriteLine($"[UID/登录] {pointKey} 未缓存，跳过点击");
                    return;
                }

                var point = (CoordinateFormats.Point)pointObj;
                Debug.WriteLine($"[UID/登录] 点击 {pointKey}: ({point.X}, {point.Y})");
                SimulationService.LeftClickAt(point.X, point.Y);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 点击 {pointKey} 出错：{ex.Message}");
            }
        }

        /// <summary>点击一个区域的中心（缓存里已经是屏幕坐标）。</summary>
        private static void ClickRegionCenter(string regionKey)
        {
            try
            {
                var rect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(regionKey);
                if (rect == null)
                {
                    Debug.WriteLine($"[UID/登录] {regionKey} 未缓存，跳过点击");
                    return;
                }

                int x = (rect.Rect.X1 + rect.Rect.X2) / 2;
                int y = (rect.Rect.Y1 + rect.Rect.Y2) / 2;
                Debug.WriteLine($"[UID/登录] 点击 {regionKey} 中心: ({x}, {y})");
                SimulationService.LeftClickAt(x, y);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 点击 {regionKey} 出错：{ex.Message}");
            }
        }

        #endregion

        #region 识别

        /// <summary>
        /// 在 <paramref name="regionKey"/> 区域里找 <paramref name="templateKey"/> 模板。
        /// 阈值取区域自带的 <c>Threshold</c>（0.9），与主程序里各功能的做法一致。
        /// </summary>
        private static (bool detected, double similarity) Detect(string regionKey, string templateKey)
        {
            try
            {
                // 坐标缓存没准备好（窗口信息未上报）时识别没有意义
                if (!CoordinateFormats.CacheReady) return (false, 0);

                var matchRect = CoordinateFormats.GetCached<CoordinateFormats.MatchRect>(regionKey);
                if (matchRect == null) return (false, 0);

                using var source = ImageRecognition.CaptureRegionScaled(matchRect);
                if (source == null) return (false, 0);

                // ★ 模板是**缓存位图**，绝对不能 using —— 释放掉下次就取不到了。
                //   宿主里所有功能也都是直接取用不释放的。
                var template = TemplateManager.GetCachedTemplate(templateKey);
                if (template == null)
                {
                    WarnMissingTemplateOnce(templateKey);
                    return (false, 0);
                }

                double threshold = matchRect.Threshold;
                var result = ImageRecognition.MatchTemplate(source, template, threshold);
                if (result == null) return (false, 0);

                return (result.Similarity >= threshold, result.Similarity);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UID/登录] 识别 {regionKey} 出错：{ex.Message}");
                return (false, 0);
            }
        }

        /// <summary>
        /// 模板不存在时提醒一次（模板要用户自己截图放到资源包里）。
        /// 少了模板的检测会恒为 false，不说一声的话表现为「流程卡住不动」。
        /// </summary>
        private static void WarnMissingTemplateOnce(string templateKey)
        {
            if (_missingTemplateWarned) return;
            _missingTemplateWarned = true;

            Debug.WriteLine(
                $"[UID/登录] ⚠ 模板 {templateKey} 不存在 → 对应区域将永远检测不到。" +
                "请把截图按 其他模版/退出游戏/<名字>.png 放进资源包后重新生成。");
        }

        #endregion
    }
}
