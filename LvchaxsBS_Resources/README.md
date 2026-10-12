# LvchaxsBS_Resources

LvchaxsBS 的**共享基础库**。既集中了主程序与 UID 模块共用的一切图片，
也承载**图像识别能力代码**（坐标管理 / 模板加载 / 截图 / 匹配 / 模拟输入），
编译成 `LvchaxsBS_Resources.dll` 供两者共用。

> **它仍然是可以缺失的**：dll 不在 `LvchaxsBS.exe` 同目录时，主程序照常启动 ——
> 图片全部留空、图像识别与模拟点击整体停用，但 **UI 交互与配置读写完全正常**，
> 不会抛异常、不会崩溃。做法见下面「可选性是怎么保证的」一节。

## 目录约定

```
Resources/
├── Images/图标/            顶部功能栏 / 模块图标（钓鱼1~3.png、锚点1~3.png …）
├── Images/元素/            七元素筛选图标（火水风雷草冰岩.png）
├── Images/Pictures/
│   ├── 角色头像/           角色头像（122 张，文件名 = 角色名，供语音页 + UID 账号头像用）
│   └── 其他头像/           NPC / 任务类头像（30 张，与角色无关）
├── Data/CharacterElements.json  角色编号 / 元素对照表
└── Template/               图像识别模板
    ├── 剧情对话/
    ├── 快速传送/
    ├── 自动伐木/
    ├── 自动清药/
    ├── 自动烹饪/
    ├── 钓鱼辅助/
    ├── 其他模版/             各功能共用（主界面 / 点击进入 / ESC菜单）
    │   └── 退出游戏/           退出流程（退出至登录界面 / 设置 / 切换账号）
    └── 角色头像/150x150/

Core/                           ← 代码（目录与命名空间一一对应）
├── TemplateManager.cs           模板加载（按路径注册，key = 路径各段用 _ 连起来）
└── SimulationService.cs         模拟输入（按键 / 点击）

Services/
├── CoordinateFormats.cs         坐标管理（基准 3840×2160 + 各分辨率补偿、缓存）
├── ResourcePaths.cs             资源路径注入点
├── WindowBoundsBridge.cs        窗口客户区注入点
├── CaptureSettings.cs           截图模式注入点
└── ImageRecognitionParts/       图像识别（8 个 partial 分片：截图 / 匹配 / 二值化 …）
```

> `AssetLocator` / `ResourceImageHelper` / `WindowFocusService` 仍留在宿主，
> 原因见下面「可选性是怎么保证的」。
>
> ⚠️ **模板 key 由路径现推**：`其他模版/退出游戏/设置.png` → key `其他模版_退出游戏_设置`
> （斜杠换下划线、去掉 `.png`）。所以**改文件名、改所在文件夹都会改 key** ——
> 必须同步 `TemplateManager.LoadTemplates` 的注册路径 + 各功能里的 `GetCachedTemplate("…")`，
> 否则 `GetCachedTemplate` 返回 null，检测会**静默失效**（不报错、只是认不出来）。

新增图片**直接放进对应目录即可**——`.csproj` 用的是通配符
（`Resources\**\*.png|ico|jpg|jpeg|bmp|gif`），不需要改工程文件。

> **加完图建议 Rebuild 一次**：增量构建是按「输入文件时间 vs 上次输出时间」判断的，
> 如果你加进来的是修改时间较早的老文件（例如从别处剪切/复制来的），
> 构建会认为没变化而跳过打包，运行起来就会「图没生效」。
>
> ```bash
> dotnet build LvchaxsBS_Resources.csproj -t:Rebuild
> ```

## 编译

```bash
dotnet build LvchaxsBS_Resources.csproj
```

编译后会自动把 `LvchaxsBS_Resources.dll` 复制到
`..\LvchaxsBS\bin\<配置>\net8.0-windows\`（与 `LvchaxsBS.exe` 并列）。

工程也已加入 `LvchaxsBS.sln`，在 Visual Studio 里随解决方案一起生成。

## 在代码里取用

资源路径统一以 `Resources/` 为根，**不要**自己拼 `pack://` 前缀：

```csharp
// 主程序（LvchaxsBS）
using LvchaxsBS.Core;

string uri = AssetLocator.Pack("Images/图标/钓鱼1.png");        // → pack://…/LvchaxsBS_Resources;component/Resources/Images/图标/钓鱼1.png
BitmapImage? img = AssetLocator.Load("Images/图标/钓鱼1.png");   // 资源包缺失时返回 null
```

```csharp
// UID 模块（LvchaxsBS_UID）
using LvchaxsBS_UID.Utils.UI;

image.Source = AssetLocator.Load("Template/角色头像/150x150/荧.png");
```

两个项目的 `AssetLocator` 各自持有一份实现（模块与宿主之间没有编译期引用），
但行为一致：**资源包缺失时 `Pack()` 返回空串、`Load()` 返回 `null`**。

### 按资源包内容生成清单

有些地方需要"以资源包里的图片为准"而不是扫磁盘目录（例如语音页的角色数量）：

```csharp
IReadOnlyList<string> names = AssetLocator.ListFileNames("Images/Pictures/角色头像");
// → ["空", "荧", "迪希雅", ...]  只取本层、不含扩展名、已做 URL 解码
```

资源包缺失时返回空列表。语音页的角色清单就用它——
**角色数量 = 资源包图片数量（编译期固定），不存在"没有头像的角色"**。
要增删角色，往 `Resources/Images/Pictures/角色头像/` 放图 / 删图后重新编译资源包即可。

> 注意：`ListFileNames` 取的是 `g.resources` 的键，键对非 ASCII 做了百分号转义、
> 且英文部分被转成小写，所以它**只适合中文名的角色图**；英文名会变成小写。

## 代码部分：注入点

本工程是共享库，**不能反向引用宿主**（宿主与 UID 都引用它）。所以有三处信息由宿主在启动时
"喂"进来 — 都是静态属性，注册一次即可，代码都在 `LvchaxsBS/Services/SharedComponent.cs`：

| 注入点 | 作用 | 宿主侧来源 |
|---|---|---|
| `CaptureSettings.CaptureModeProvider` | 截图用窗口 DC 还是屏幕 DC | `AppSettings.CaptureMode`（注册成 lambda，设置页改完立刻生效） |
| `ResourcePaths.IsAvailable` / `PackProvider` / `LoadProvider` | 资源包在不在、怎么拼 pack URI | 宿主的 `AssetLocator`（规则本来就一致，这里只同步"在不在"） |
| `WindowBoundsBridge.Report(...)` | 游戏窗口客户区（坐标缓存与截图都取它） | 宿主 `WindowFocusService.BoundsChanged` 转发过来 |

宿主没注入时：截图模式按 0、资源包视为不可用、客户区为空 —— 识别功能整体停用，但不会抛异常。

## 可选性是怎么保证的

`LvchaxsBS_Resources.dll` **可以被删掉而不影响主程序启动**，靠三条约定：

1. **图片访问留在宿主**：`AssetLocator` / `ResourceImageHelper` 只拼字符串 URI，取不到就是
   `null` / 空白，从不触碰本工程的类型 —— 所以 UI 在没有 dll 时照常渲染（只是图空）。
2. **本工程类型的引用只出现在「引擎初始化」那条链上**（`App.InitializeEngine` → `IconService` /
   `DetectionManager` / `SubtitleOverlayService` / `Core/Function/*`）。
   UI、配置、主题、窗口焦点跟踪全都在链外，因此不需要那个 dll。
3. **入口先探测再调用**：`SharedComponent.IsAvailable` 只做 `File.Exists`（不碰任何类型），
   为 false 时跳过注入与引擎初始化 —— 连 JIT 都不会去解析本程序集，自然不抛异常。

> 验证方式（不需要启动 UI）：把宿主 bin 复制一份但**不含本 dll**，用 `RuntimeHelpers.PrepareMethod`
> 逐个 JIT `App.OnStartup` / `MainWindow..ctor` / `VoicePage..ctor` 等入口 —— 应全部通过；
> 而 `DetectionManager.DetectMainWindow`（用到 `CoordinateFormats`）应抛 `FileNotFoundException`，
> 后者用来证明这个检查足够灵敏。

## 许可

代码与清单同主仓库，GPL-3.0（见 `LICENSE`）。
`Resources/` 下取自游戏画面的素材不属授权范围，仅供本工具本地使用。
