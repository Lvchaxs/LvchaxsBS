using LvchaxsBS.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.Core
{
    public static class TemplateManager
    {
        private const int BASE_WIDTH = 1920;
        private const int BASE_HEIGHT = 1080;

        private static readonly HashSet<(int width, int height)> _supportedResolutions = new HashSet<(int, int)>
        {
            (5120, 2880),
            (3840, 2160),
            (2560, 1440),
            (1920, 1080),
            (1600, 900),
            (5120, 2160),
            (3440, 1440),
            (2560, 1080)
        };

        private static int _currentWidth = 0;
        private static int _currentHeight = 0;
        private static bool _hasWindowInfo = false;
        private static string _processName = "";

        // 模板字典
        private static readonly Dictionary<string, Bitmap?> _templates = new Dictionary<string, Bitmap?>();
        // 缓存字典
        private static readonly Dictionary<string, Bitmap?> _cache = new Dictionary<string, Bitmap?>();

        private static int _lastWidth = 0;
        private static int _lastHeight = 0;
        private static bool _hasLastInfo = false;

        public static bool HasWindowInfo => _hasWindowInfo;

        public static void UpdateWindowInfo(int width, int height, string processName)
        {
            _currentWidth = width;
            _currentHeight = height;
            _processName = processName;
            _hasWindowInfo = true;
            UpdateCache();
        }

        private static bool IsResolutionSupported(int width, int height)
        {
            return _supportedResolutions.Contains((width, height));
        }

        public static double GetScaleY()
        {
            if (_currentHeight == 0) return 1.0;
            if (_currentHeight >= BASE_HEIGHT) return 1.0;
            return (double)_currentHeight / BASE_HEIGHT;
        }

        private static void UpdateCache()
        {
            if (!_hasWindowInfo)
            {
                return;
            }

            if (!IsResolutionSupported(_currentWidth, _currentHeight))
            {
                return;
            }

            bool resolutionChanged = (_currentWidth != _lastWidth || _currentHeight != _lastHeight);

            if (!_hasLastInfo || resolutionChanged)
            {
                double scale = GetScaleY();

                foreach (var key in _cache.Keys)
                {
                    _cache[key]?.Dispose();
                }
                _cache.Clear();

                int loadedCount = 0;
                foreach (var kvp in _templates)
                {
                    var scaled = ScaleTemplate(kvp.Value, scale, scale);
                    _cache[kvp.Key] = scaled;
                    if (kvp.Value != null) loadedCount++;
                }

                _lastWidth = _currentWidth;
                _lastHeight = _currentHeight;
                _hasLastInfo = true;

                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("========== 模版缓存更新 ==========");
                System.Diagnostics.Debug.WriteLine($"进程: {_processName} 窗口分辨率: {_currentWidth}x{_currentHeight}  缩放比例: {scale:F3}  模版数量: {loadedCount}/{_templates.Count}");

                foreach (var kvp in _templates)
                {
                    LogCacheDetail(kvp.Key, kvp.Value, _cache.TryGetValue(kvp.Key, out var cached) ? cached : null);
                }

                System.Diagnostics.Debug.WriteLine("====================================");
            }
        }

        private static void LogCacheDetail(string name, Bitmap? source, Bitmap? cached)
        {
            if (source == null)
            {
                System.Diagnostics.Debug.WriteLine($"{name}: 基础模板为空");
                return;
            }

            if (cached == null)
            {
                System.Diagnostics.Debug.WriteLine($"{name}: 缓存生成失败  基础尺寸: ({source.Width}, {source.Height})");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"{name}_缓存尺寸: ({cached.Width}, {cached.Height})  基础尺寸: ({source.Width}, {source.Height})");
        }

        private static Bitmap? ScaleTemplate(Bitmap? source, double scaleX, double scaleY)
        {
            if (source == null) return null;

            int newWidth = (int)(source.Width * scaleX);
            int newHeight = (int)(source.Height * scaleY);

            if (newWidth <= 0 || newHeight <= 0) return null;

            var scaled = new Bitmap(newWidth, newHeight);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, newWidth, newHeight);
            }
            return scaled;
        }

        #region 模板加载

        /// <summary>
        /// 从路径添加模板，自动解析名称
        /// 例如: ResourcePaths.Pack("Template/快速传送/掩码1.png")
        /// 解析为: "快速传送_掩码1"
        ///
        /// <para>
        /// <paramref name="uriString"/> 为空（资源包 LvchaxsBS_Resources.dll 缺失时
        /// <see cref="ResourcePaths.Pack"/> 会返回空串）则直接跳过，
        /// 该模板留空 —— 图像识别找不到模板自然匹配不上，但主程序其它功能不受影响。
        /// </para>
        /// </summary>
        private static void AddTemplateFromPath(string uriString)
        {
            if (string.IsNullOrEmpty(uriString))
            {
                return;
            }

            string relativePath = uriString;

            // 优先匹配 /Resources/Template/
            int index = relativePath.IndexOf("/Resources/Template/");
            if (index >= 0)
            {
                relativePath = relativePath.Substring(index + "/Resources/Template/".Length);
            }
            else
            {
                // 兜底：匹配 /Resources/
                int idx2 = relativePath.IndexOf("/Resources/");
                if (idx2 >= 0)
                    relativePath = relativePath.Substring(idx2 + "/Resources/".Length);
            }

            // "快速传送/掩码1.png" -> "快速传送_掩码1"
            string name = relativePath.Replace("/", "_").Replace("\\", "_");
            name = name.EndsWith(".png") ? name.Substring(0, name.Length - 4) : name;

            if (_templates.ContainsKey(name))
            {
                _templates[name]?.Dispose();
                _templates.Remove(name);
            }

            var bitmap = LoadBitmapFromResource(uriString);
            _templates[name] = bitmap;
        }

        public static void LoadTemplates()
        {
            // 资源包是可选的：没有它就没有任何识别模板，图像识别功能整体失效，
            // 但主程序照常启动（不抛异常、不崩）。
            if (!ResourcePaths.IsAvailable)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"【模板】未找到资源包 {ResourcePaths.ResourceAssemblyName}.dll，图像识别模板全部为空");
                return;
            }

            try
            {
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/传送.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/靶场.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/七天神像.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/新月神像.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/传送锚点.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/秘境1.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/秘境2.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/列车.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/宅邸.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/幽境危战.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/青玄乘阳华盖.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/快速传送/口袋锚点.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/剧情对话/隐藏按钮.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/钓鱼辅助/未抛钩.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/钓鱼辅助/已抛钩.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/钓鱼辅助/上钩了.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/自动伐木/王树瑞佑.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动伐木/获得.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/自动烹饪/烹饪界面.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动烹饪/自动烹饪.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动烹饪/手动烹饪.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动烹饪/确认.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/寄物装置.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/打开寄物装置.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/美味确认.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/拆除.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/凝造.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/提瓦特煎蛋.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/烤肉排.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/蒙德烤鱼.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/摩拉肉.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/爆炒肉片.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/鸟蛋烧.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/庄园烤松饼.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/素鲍鱼.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/蟹黄豆腐.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/什锦炒面.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/乌冬面.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/绿汁脆球.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/脆饼珐提.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/多彩之森.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/蟹黄火腿焗时蔬.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/绯樱饼.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/椰炭饼.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/塔塔可.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/夏槲蛋糕.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/薄荷泡泡糖.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/北地烟熏鸡.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/山珍热卤面.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/阿如拌饭.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/桔香鸭胸肉.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/清心花饼.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/炸鱼薯条.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/巧克力.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/钱汤馒头.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/白灵果派.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/自动清药/肉末酿豆腐.png"));

                // 其他模版：多个功能共用
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/主界面.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/点击进入.png"));
                // ★ 点「点击进入」进游戏后可能弹出的月卡领取界面（两个模板任中一个）
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/空月祝福1.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/空月祝福2.png"));
                // ★「连接已断开」：游戏**进行中**随时可能突然弹出的提示（登录大厅不会弹），两个模板任中一个。
                //   ① 连接服务器失败 = 网络故障 → 彻底结束登录；
                //   ② 账号在另一处重复登录 = 被顶号 → 按 ESC 退回登录界面 / 自动换下一个账号。
                //   key 由路径推导：其他模版_连接服务器失败 / 其他模版_账号在另一处重复登录
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/连接服务器失败.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/账号在另一处重复登录.png"));

                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/ESC菜单.png"));

                // 登录账号流程：从用户中心退出到登录界面之后的登录步骤
                // （模板在子目录里，key 会带上子目录名 —— 其他模版_登录账号_xxx）
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/登录其他账号.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/进入游戏.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/同意登录.png"));

                // 「登录或退出区域」：登录大厅右下角那个按钮。检测到 = 当前没账号在线，点它直接到输入界面；
                // 检测不到、但「点击进入」检测得到 = 已有账号在线（此时要点它走退出流程，不能直接点进游戏）。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/登录账号.png"));
                // 「是否保留登录记录区域」：点了「登录或退出区域」之后弹的退出确认窗。
                // ★ 文件在「退出游戏」子目录里，**不在**「登录账号」下 ——
                //   key 由路径推导，所以这里是「其他模版_退出游戏_是否保留登录记录」。
                // ★ 这是窗口里的小复选框图标；2026-10-14 曾因旧图失真匹配不上，重做 23×23 后正常。
                //   （曾用同窗口的横向标题「退出登录」替代过，现已确认此处可用，那套已删除。）
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/退出游戏/是否保留登录记录.png"));

                // 「账号密码出错区域」的四个模板：检测到任意一个 = 账密没输进去 / 输错了
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/请输入密码.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/请输入手机号邮箱.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/账号格式错误.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/账号或密码错误.png"));
                // 「欢迎进入游戏区域」：同意之后没报错才会出现，看到它就是登录成功了
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/欢迎进入游戏.png"));

                // ★★「验证身份」：账密提交后弹出「您的账号存在安全风险…请先验证身份后再登录」= 此设备验证失效。
                //    检测到说明这个账号在这台机器上不能继续登录（要点「取消验证」关掉弹窗结束流程），
                //    并给账号打红标签「账号验证失效」。区域 = "验证身份"，关窗点坐标 = "取消验证"。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/登录账号/验证身份.png"));

                // ---- 登录成功之后的「附加动作」（PostLoginActions）用到的模板 ----

                // 聊天界面：主界面按 Enter 打开聊天，右下角出现「消息发送」按钮即为已打开。
                // 区域 = "消息发送"；key = 其他模版_聊天界面_消息发送
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/消息发送.png"));

                // 联机大厅：主界面按 F2 打开。里面「世界权限」有三种状态，任意一个命中就说明在联机大厅了。
                // 区域 = "世界权限"；key = 其他模版_联机状态_世界权限XXX
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/世界权限不允许加入.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/世界权限直接加入.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/世界权限确认后可加入.png"));

                // 区域 = "联机队友"（主界面右侧那一竖条）；key = 其他模版_联机状态_NP队友
                // ★ 三张图各代表一个队友槽位：命中几张 = 当前有几个队友（人数 = 队友数 + 自己）
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/2P队友.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/3P队友.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/4P队友.png"));

                // 区域 = "自己消息" / "队友消息"（聊天界面里每条消息前面的那个 P 标记）：
                // ★★ 2026-10-18 用户重做了**聊天界面专用**的一套，放在 Template/其他模版/聊天界面/ 下：
                //    自己 = 1P，队友 = 2P / 3P / 4P。
                //    聊天里**不再借用**「联机状态」那几张（1P地主 / 2P队友…）—— 那几张只留给「数人」用。
                // ⚠️ `2p.png` 文件名是**小写 p**，key 就是 `其他模版_聊天界面_2p`；
                //    改文件名要同步 PostLoginActions 里的 OwnMsgTemplateKeys / ChatTeammateTemplateKeys。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/1P.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/2p.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/3P.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/4P.png"));

                // 区域 = 「最新消息内容」（**动态区域**：从最新那条队友消息的位置往下 200 / 1080 基准）：
                // ★ 把队友常说的几句话本身做成模板 —— 认出是哪一句就回对应的话
                //   （PostLoginActions.ReplyTextFor：救命⇒菜就多练！/ 开始挑战吧⇒加油！/ 你好⇒你好NP /
                //    谢谢⇒不用谢！/ 真是精彩⇒真是精彩！）。
                // ⚠️ 文件名里的感叹号**有全角也有半角**（「真是精彩!」是**半角** !），key 是按文件名现推的，
                //    改文件名就要同步改 PostLoginActions 里的 MsgContent*Key。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/救命！.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/开始挑战吧！.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/你好！.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/谢谢！.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/真是精彩!.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/世界权限.png"));

                // 队友**点名要某个权限**的三张（认到就真的去改，不只是回一句话）：
                // 不允许加入 ⇒ 改不允许加入 / 直接加入 ⇒ 改直接加入（已是它只回「当前已经是直接加入」）/
                // 确认后可加入 ⇒ 改确认后可加入。见 PostLoginActions.HandlePermissionRequestAsync。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/不允许加入.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/直接加入.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/确认后可加入.png"));

                // 队友发「换号」⇒ 播报「我去换号了下一个是「名字：UID」」→（必要时）先改权限改成确认后可加入
                // → 交给上层换下一个账号。见 PostLoginActions.HandleSwitchRequestAsync。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/换号.png"));

                // 队友发「下一个」⇒ 只回答下一个号是谁（「下一个「名字：UID」」），**不换号**。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/下一个.png"));

                // 队友发「申请权限」⇒ 没权限的拒；有权限的开 5 秒窗口招人（给 1 个人批权限，UI 勾选同步）。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/申请权限.png"));

                // 队友发表情包「指指点点」⇒ 回「反弹！」。★ 它不是白字，PostLoginActions 里走
                // `EmojiContentKeys` 白名单，跳过白字几何校验。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/聊天界面/队友消息/指指点点.png"));

                // 区域 = "多人模式"（主界面左上角那个联机状态图标）；**只在主界面时检测**。
                // ★ 四张模板表达「当前世界的联机状态」：
                //   · 多人模式 → 在自己的世界里开着多人模式；
                //   · 他人世界2P/3P/4P → 在联机中，但站在**别人**的世界里；
                //   · 四个都检测不到 → 单人世界。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/多人模式.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/他人世界2P.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/他人世界3P.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/他人世界4P.png"));

                // 区域 = "当前队伍"：聊天界面里那个"当前队伍"频道按钮（**只有联机时才有**）。
                // ★ 用来把聊天频道切到多人模式的公共大厅：检测到它说明列表顶上是"当前队伍"频道；
                //   再配合「选择当前队伍」那个点的颜色（(65,72,86) = 该频道被选中的背景色）判断有没有选上。
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/联机状态/当前队伍.png"));

                // 退出游戏流程：模板放在子目录里，key 会带上子目录名 —— 其他模版_退出游戏_xxx
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/退出游戏/退出至登录界面.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/退出游戏/设置.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/退出游戏/点击跳转.png"));
                AddTemplateFromPath(ResourcePaths.Pack("Template/其他模版/退出游戏/用户中心刷新.png"));

                System.Diagnostics.Debug.WriteLine($"模板加载成功: {_templates.Count} 个");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载模板失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取模板（原始尺寸）
        /// </summary>
        public static Bitmap? GetTemplate(string name)
        {
            return _templates.TryGetValue(name, out var bitmap) ? bitmap : null;
        }

        /// <summary>
        /// 获取缓存的模板（缩放后）
        /// </summary>
        public static Bitmap? GetCachedTemplate(string name)
        {
            return _cache.TryGetValue(name, out var cached) ? cached : null;
        }

        /// <summary>
        /// 从嵌入资源取模板位图。
        ///
        /// <para>
        /// 直接 <see cref="Application.GetResourceStream(Uri)"/> 拿到 PNG 字节交给 GDI+ 解一次，
        /// 再统一转成 32bppArgb（下游 ImageRecognition 一律按 32bppArgb 取像素）。
        /// </para>
        ///
        /// <para>
        /// 原来的写法是「WPF <c>BitmapImage</c> 解码 → <c>PngBitmapEncoder</c> 重新编码成 PNG →
        /// GDI+ 再解码」：同一个文件解了两遍、还多编码一遍。实测 63 张模板耗时 62ms、
        /// 过程中额外产生约 7MB 私有内存增量（裸像素只有 0.75MB，其余都是这条来回的临时物）。
        /// 已在探针里对两条路径的输出做了逐像素比对（63/63 完全一致），所以可以安全替换。
        /// </para>
        /// </summary>
        private static Bitmap? LoadBitmapFromResource(string uriString)
        {
            if (string.IsNullOrEmpty(uriString)) return null;

            Uri uri;
            try
            {
                uri = new Uri(uriString, UriKind.RelativeOrAbsolute);
            }
            catch
            {
                // URI 非法（例如资源包缺失时拿到空串）→ 当作没有这张图
                return null;
            }

            // ---- 快路径：PNG 字节直接交给 GDI+ 解一次 ----
            try
            {
                var resource = Application.GetResourceStream(uri);
                if (resource?.Stream != null)
                {
                    using var stream = resource.Stream;
                    using var decoded = new Bitmap(stream);

                    var rect = new Rectangle(0, 0, decoded.Width, decoded.Height);
                    return decoded.Clone(rect, PixelFormat.Format32bppArgb);
                }

                System.Diagnostics.Debug.WriteLine($"【模板】取不到资源流，走兜底解码: {uriString}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"【模板】快路径失败({ex.Message})，走兜底解码: {uriString}");
            }

            // ---- 兜底：原来的 WPF 解码路径（行为与改动前完全一致）----
            try
            {
                var bitmapImage = new BitmapImage(uri);
                bitmapImage.Freeze();

                using var ms = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
                encoder.Save(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        static TemplateManager()
        {
            // 客户区变化 → 刷新模板缩放用的窗口信息（宿主启动时把 WindowFocusService 的变化转发到这里）
            WindowBoundsBridge.Changed += info =>
            {
                if (!info.IsEmpty)
                {
                    UpdateWindowInfo(info.Width, info.Height, info.ProcessName);
                }
            };
        }
    }
}