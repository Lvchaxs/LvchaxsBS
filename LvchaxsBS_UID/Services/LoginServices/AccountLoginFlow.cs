using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LvchaxsBS.Core;       // SimulationService（按 ESC）、TemplateManager（取模板）
using LvchaxsBS.Services;   // CoordinateFormats、ImageRecognition
using LvchaxsBS_UID.Utils.UI;

namespace LvchaxsBS_UID.Services.LoginServices
{
    /// <summary>
    /// 登录流程的「图像识别 + 按 ESC」循环。
    ///
    /// <para>
    /// 从「确认登录」开始：游戏窗口已被激活（见 <c>Utils.GameWindow.GameWindowActivator</c>），
    /// 然后每轮同时识别两个区域：
    /// </para>
    /// <list type="bullet">
    ///   <item><b>主界面</b>：左上角派蒙头像（<c>主界面区域</c> + 模板 <c>其他模版_主界面</c>）</item>
    ///   <item><b>派蒙菜单</b>：按 ESC 打开的那一页（<c>派蒙菜单区域</c> + 模板 <c>其他模版_ESC菜单</c>）</item>
    /// </list>
    /// <para>
    /// 判断顺序：<b>检测到派蒙菜单 → 登录成功，直接结束</b>；
    /// 否则按一次 ESC（主界面时这一下正好打开派蒙菜单；两个都没检测到时则是先退出当前画面再重找），
    /// 等 1 秒再进下一轮。
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

        /// <summary>每轮之间的等待：给游戏时间把上一轮那次 ESC 处理完、画面稳定下来。</summary>
        private const int RoundIntervalMs = 1000;

        /// <summary>第二行字幕：进行中。</summary>
        private const string Line2Running = "登录中...";

        /// <summary>第二行字幕：成功。</summary>
        private const string Line2Succeeded = "登录成功-TAB关闭";

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
        /// 跑登录流程，返回是否成功（检测到派蒙菜单 = 已进入菜单界面）。
        /// 被取消时抛 <see cref="OperationCanceledException"/>。
        /// </summary>
        /// <param name="accountName">要登录的账号名（已显示在字幕第一行）。</param>
        /// <param name="token">取消令牌（用户点「停止登录」时触发）。</param>
        public static async Task<bool> RunAsync(string accountName, CancellationToken token)
        {
            ShowRunningSubtitles(accountName);

            for (int round = 1; ; round++)
            {
                token.ThrowIfCancellationRequested();

                // 每轮识别一次，两个区域一起
                var main = Detect(MainRegionKey, MainTemplateKey);
                var paimon = Detect(PaimonRegionKey, PaimonTemplateKey);

                Debug.WriteLine(
                    $"[UID/登录] 第 {round} 轮：主界面 {main.similarity * 100:F1}%" +
                    $"({(main.detected ? "命中" : "未命中")})  派蒙菜单 {paimon.similarity * 100:F1}%" +
                    $"({(paimon.detected ? "命中" : "未命中")})");

                if (paimon.detected)
                {
                    // 已经在派蒙菜单 → 登录完成
                    return true;
                }

                // 主界面 → 这一下 ESC 正好打开派蒙菜单；
                // 两个都没检测到 → 也按 ESC（先退出当前画面，下一轮重新找）
                SimulationService.PressEscape();

                // 等画面稳定后再进下一轮
                await Task.Delay(RoundIntervalMs, token);
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
                if (template == null) return (false, 0);

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

        #endregion
    }
}
