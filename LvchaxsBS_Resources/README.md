# LvchaxsBS_Resources

LvchaxsBS 的**共用图片资源包**。把主程序与 UID 模块用到的所有图片集中在这里，
编译成 `LvchaxsBS_Resources.dll` 供两者共用。

> 这是**可选组件**：dll 不在 `LvchaxsBS.exe` 同目录时，主程序与 UID 模块照常启动，
> 只是所有图片取不到（`Image` 显示为空白），**不会抛异常、不会崩溃**。

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
    ├── 通用模版/
    └── 角色头像/150x150/
```

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
`..\LvchaxsBS\LvchaxsBS\bin\<配置>\net8.0-windows\`（与 `LvchaxsBS.exe` 并列）。

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

## 许可

代码与清单同主仓库，GPL-3.0（见 `LICENSE`）。
`Resources/` 下取自游戏画面的素材不属授权范围，仅供本工具本地使用。
