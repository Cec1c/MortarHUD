<div align="center">

# MortarHUD

[![运行时：.NET 10](https://img.shields.io/static/v1?label=%E8%BF%90%E8%A1%8C%E6%97%B6&message=.NET%2010&color=512BD4&style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![界面：WPF](https://img.shields.io/static/v1?label=%E7%95%8C%E9%9D%A2&message=WPF&color=512BD4&style=flat-square)](#项目结构)
[![图像：OpenCvSharp4](https://img.shields.io/static/v1?label=%E5%9B%BE%E5%83%8F&message=OpenCvSharp4&color=5C3EE8&style=flat-square&logo=opencv&logoColor=white)](https://github.com/shimat/opencvsharp)
[![OCR：Tesseract 5](https://img.shields.io/static/v1?label=OCR&message=Tesseract%205&color=2A6EBB&style=flat-square)](https://github.com/tesseract-ocr/tesseract)
[![测试：xUnit](https://img.shields.io/static/v1?label=%E6%B5%8B%E8%AF%95&message=xUnit&color=25A162&style=flat-square)](#测试)
[![平台：Windows x64](https://img.shields.io/static/v1?label=%E5%B9%B3%E5%8F%B0&message=Windows%20x64&color=0078D6&style=flat-square&logo=windows&logoColor=white)](#环境要求)

读屏识别游戏地图上的坐标读数，实时解算迫击炮的方位角与距离，并以透明置顶 HUD 显示。

[快速开始](#快速开始) ｜ [OCR 基准测试](#ocr-基准测试) ｜ [项目结构](#项目结构) ｜ [已知边界](#已知边界)

> 要接手这个项目继续开发？先读 **[AGENTS.md](AGENTS.md)** —— 环境坑、架构、未决问题、踩过的雷都在里面。

</div>

## 它做什么

在《Wardogs》这类战术射击游戏里，玩家打开地图并把鼠标移到某处时，游戏会在光标旁显示该点的绝对坐标：

```text
y109.78
x98.09
```

MortarHUD 做的事情就是把这个读数读出来，替你算完迫击炮需要的两个数：

```text
AZ  079.0°
RNG 152m
```

完整流程是：**按下热键 → 读光标位置 → 截取光标附近一小块屏幕 → 图像预处理 → 本地 OCR → 解析校验 → 解算 → 更新 HUD**。

因为读的是游戏自己显示的**绝对坐标**，地图缩放、平移、重新居中都不影响结果。炮位锁定后即使游戏里自己的地图箭头消失（进入迫击炮后常见），已保存的炮位依然有效。

### 安全边界

这不是一个作弊工具，设计上刻意守着几条线：

- **不注入游戏进程**，不使用 DLL 注入；
- **不读写游戏内存**；
- **不模拟任何键鼠输入**，不自动开火、不自动填参数；
- **不联网**，OCR 完全在本地跑，不调用任何云端服务；
- 只做两件事：注册全局热键，以及在你按键的那一刻截取屏幕上一小块区域。

程序以普通用户权限运行，不需要管理员。

> [!NOTE]
> 本项目不实现自动瞄准、弹道模拟、风偏修正、武器数据库或敌人识别。它的输出只有方位角和距离两个数，怎么用由你决定。

## 快速开始

### 环境要求

需要 **.NET 10 SDK**（`net10.0-windows`）。仅支持 **Windows x64**。

> [!IMPORTANT]
> 本项目的开发机上 .NET 10 SDK 安装在 `C:\dotnet10`，没有并入系统 `dotnet`（`C:\Program Files\dotnet` 里只有 .NET 6，且无管理员权限写入）。
> 如果你的机器上 `dotnet --version` 显示的低于 10，请用完整路径调用，或把 `C:\dotnet10` 放到 PATH 中 `C:\Program Files\dotnet` **之前**。

```bash
# 克隆或解压后，在仓库根目录
dotnet build MortarHUD.sln -c Release

# 运行测试
dotnet test MortarHUD.sln

# 发布（自包含，用户端不需要装 .NET）
publish.cmd
```

### 怎么启动

> [!IMPORTANT]
> 如果你的 `dist\MortarHUD\` 里有一个 `libs\` 子目录，**启动时的工作目录必须是程序所在目录**。
> .NET 按「当前工作目录」（而不是 exe 所在目录）解析 `runtimeconfig.json` 里的相对探测路径，
> 从别处启动会报 `Could not load file or assembly PresentationFramework`。
> 双击 exe 或从快捷方式启动都没问题（资源管理器会把工作目录设成 exe 目录），
> `run.cmd` 也已经显式切目录。要彻底避开这个约束就用 `publish.cmd folder`（扁平布局）。


**双击仓库根目录的 `run.cmd`**，或者直接运行 `dist\MortarHUD\MortarHUD.exe`。没有发布过的话 `run.cmd` 会先自动构建。

> [!WARNING]
> **不要**直接去 `src\MortarHUD.Appin\...` 下面启动 `MortarHUD.exe`。
> `dotnet build` 产出的是**框架依赖**版本，它要求系统里注册过 .NET 10 桌面运行时。
> 如果机器上只有别的版本（或者像本项目的开发机那样，.NET 10 装在 `C:\dotnet10` 这种非标准位置），
> 双击它会弹出 `You must install or update .NET to run this application.`。
> 那份产物是给开发和调试用的，不是给用户跑的。

`dist\MortarHUD\` 里是**自包含**发布：整个 .NET 10 运行时和原生库都打在里面，
目标机器不需要安装 .NET、Python 或 Tesseract CLI，拷过去就能跑。
代价是体积（约 273 MB），想换小体积见下面的「分发选项」。

### 首次使用

1. 启动 `MortarHUD.exe`。默认会打开设置窗口，关掉后程序留在系统托盘。
2. 打开游戏地图，把鼠标移到你的炮位，按 **F6**。HUD 显示 `GUN LOCKED`。
3. 把鼠标移到目标位置，按 **F7**。HUD 显示 `TARGET LOCKED`，并给出 AZ 与 RNG。
4. 移动鼠标到新目标再按 **F7** 即可持续更新。

默认热键：`F6` 记录炮位、`F7` 记录目标、`F8` 显示/隐藏 HUD、`F9` 打开设置。全部可在设置里改。

如果识别失败，HUD 会显示 `OCR FAILED` 与 `Target unchanged`，并**保留上一个目标**——不会拿一个可能是错的坐标继续用。

> [!TIP]
> 第一次用建议先打开设置里的 **OCR → Test OCR**，按一次「对当前光标位置测试一次」。它会显示实际截到的图、预处理结果和每条流水线的原始识别文本，能立刻看出 ROI 有没有框住坐标文字。

## 坐标系与解算

默认约定（全部可配置）：

```text
+X = 东（East）
+Y = 北（North）
1 坐标单位 ≈ 100 米
方位角：北 = 0°，东 = 90°，南 = 180°，西 = 270°
```

解算公式：

```text
dx = X目标 − X炮位
dy = Y目标 − Y炮位

RNG = √(dx² + dy²) × 每单位米数
AZ  = atan2(dx, dy) 转角度，归一到 [0, 360)
```

`atan2` 的参数顺序是 **(东分量, 北分量)**，和数学上常见的 `(y, x)` 相反。这一条在 `MortarCalculator` 里有注释说明，并有覆盖八个方向的单元测试钉住。

## OCR 基准测试

TDD 要求「先 Benchmark，再决定默认 OCR 引擎」，而不是凭感觉挑一个。仓库里有三张实机截图作为基准测试集，工具是 `tools/MortarHUD.Benchmark`：

```bash
dotnet run --project tools/MortarHUD.Benchmark
```

它会打印完整矩阵，并生成 [`docs/ocr-benchmark.md`](docs/ocr-benchmark.md)。当前结果（3 个 fixture）：

| 配置 | 正确率 | 平均耗时 | 平均置信度 |
| --- | --- | --- | --- |
| **Auto（生产默认）** | **3/3** | ~128 ms | 0.82 |
| Tesseract + 全部流水线 | 3/3 | ~134 ms | 0.82 |
| 任意单条流水线（A / B / C） | 2/3 | 40–70 ms | 0.53–0.64 |
| Template（模板匹配引擎） | 2/3 | 40–70 ms | 0.63 |

关键发现是：**每条流水线单独用都会错一例，而且错的不是同一例**。所以生产配置（`Auto`）会用不同算法把同一块 ROI 二值化两遍、各自独立识别，只有当结果互相矛盾时才判定失败。

```text
Fixture 002：流水线 A 读失败，流水线 C 读对
Fixture 003：流水线 C 读失败，流水线 A 读对
→ 交叉验证后 3/3
```

这条逻辑有两层保护作用：既把正确率从 67% 提到 100%，又保证了「两条独立算法给出不同答案时宁可报错，也不挑一个用下去」。

### ROI 默认值是怎么来的

TDD 初稿给的默认 ROI 是 `offset(+10, −80)`、`240×140`，并注明「实际默认值必须根据截图 Benchmark 调整」。仓库里的三张 1920×1080 实机截图交叉验证后测得的实际几何是：

```text
y 行左上角 ≈ 光标 + (+21, −57)
x 行左上角 ≈ y 行 + (+25, +55)      # 两行左对齐、第二行缩进
整块读数约 70 × 67 px，字符高度约 11 px
```

因此实际默认值是 `offset(−15, −90)`、`150×140`——把整块读数包住并留出约 ±30px 容错。相关推导写在 `RoiSettings` 与 `RoiCalculatorTests` 里，测试会验证默认 ROI 确实盖得住实测文字块。

不同分辨率下会用 `AutoScale` 按屏幕高度相对 1080 自动缩放。

> [!WARNING]
> 当前基准测试集只有 **3 个 ROI**，而 TDD §17 要求至少 **30 个**，覆盖不同地图区域、明暗背景、复杂地形与不同缩放等级。
> 在这之前不应认为 OCR 已经稳定（目标正确率 ≥ 99%）。如果你能提供更多截图，`tests/Fixtures/` 的补充方式见 `tests/Fixtures/fixtures.json` 的结构。

## 配置

设置分六页：**General / Hotkeys / HUD / Themes / OCR / Debug**。

所有配置存在 `%AppData%\MortarHUD\`：

```text
%AppData%\MortarHUD\
├─ settings.json          # 主设置（带 schemaVersion，便于以后迁移）
├─ Themes\                # 自定义 HUD 主题（每个主题一个 JSON）
├─ Logs\                  # 按天分文件的日志，自动清理 14 天前的
└─ Debug\                 # Debug 模式下转储的 ROI 与识别结果（默认关闭）
```

### HUD 自定义

HUD 的一切都可以改：布局（Minimal / Compact / Detailed / Horizontal）、字体、字号、字重、字间距、行距、对齐、七个颜色项、描边粗细、阴影、背景板与圆角、整体不透明度、锚点与偏移、显示哪些字段、方位角与距离的小数位。

位置支持两种调整方式：在设置里填锚点 + 偏移（换分辨率更稳定），或勾选「解锁 HUD 位置」后直接把屏幕上的 HUD 拖到位。

内置四套主题：**Default Green**（默认视觉语言）、**Tactical White**、**Amber**、**High Contrast**。内置主题不能删除或改名，但可以「另存为」出自定义主题再改，支持导入导出。

### OCR 设置

| 设置项 | 默认值 | 说明 |
| --- | --- | --- |
| OCR 引擎 | Auto | 优先 Tesseract；语言包缺失时退回模板引擎 |
| 预处理 | Auto | 依次跑流水线 A 与 C 并交叉验证 |
| ROI 宽/高/偏移 X/偏移 Y | 150 / 140 / −15 / −90 | 鼠标相对，实测得出 |
| 按分辨率自动缩放 | 开 | 以 1080 为基准 |
| 坐标最小值 / 最大值 | 0 / 200 | 超出范围判定失败 |
| 最低置信度 | 0.60 | 见下方说明 |
| 必须识别出小数点 | 开 | 强烈建议保持开启 |

> [!IMPORTANT]
> 置信度不是 OCR 引擎自己给的值。实测 Tesseract 对**读对了**的坐标也会给出 0.00，直接拿它当门槛会把正确结果拦掉。因此程序用的是综合置信度：格式解析通过给基线 0.55，多条流水线一致再加权，OCR 自身置信度只占 0.20。

「必须识别出小数点」关闭后，OCR 把 `98.09` 读成 `98` 也会被当成合法坐标——那是 81 米的误差。除非你的地图确实使用整数坐标，否则不要关。

## 项目结构

```text
MortarHUD.sln

src/
├─ MortarHUD.Core/               # 纯计算，无 Windows / UI 依赖
│  ├─ Models/                    # MapCoordinate、MortarSolution、CoordinateOcrResult
│  ├─ Ballistics/                # 距离与方位角解算
│  ├─ Parsing/                   # x/y 文本解析与 OCR 字符修正
│  ├─ Validation/                # 范围、格式、置信度校验
│  ├─ Session/                   # 状态机、HUD 排版、Debug 排版
│  ├─ Configuration/             # 设置模型与持久化
│  ├─ Themes/                    # 主题模型、内置主题、主题读写
│  └─ Diagnostics/               # 文件日志、Debug 转储
│
├─ MortarHUD.Capture/            # 截屏 → 预处理 → OCR
│  ├─ ScreenCapture/             # IScreenCaptureProvider + GDI 实现 + ROI 计算
│  ├─ ImageProcessing/           # Pipeline A / B / C
│  └─ Ocr/                       # 引擎接口、Tesseract、模板匹配、交叉验证编排
│
├─ MortarHUD.Platform.Windows/   # 所有 Win32 互操作
│  ├─ Hotkeys/                   # RegisterHotKey 封装
│  ├─ Mouse/                     # GetCursorPos
│  ├─ WindowStyles/              # Overlay 窗口样式
│  ├─ Dpi/                       # Per-Monitor V2
│  └─ Startup/                   # 开机自启
│
└─ MortarHUD.App/                # WPF 界面
   ├─ Views/                     # Overlay、设置窗口、Debug 面板、HUD 渲染器
   ├─ ViewModels/                # 设置页视图模型
   ├─ Services/                  # HUD 控制器
   ├─ Tray/                      # 系统托盘
   └─ Models/                    # 随程序发布的 OCR 资源

tests/
├─ MortarHUD.Core.Tests/         # 解算、解析、校验、状态机、设置、主题、热键
├─ MortarHUD.Ocr.Tests/          # 真实截图端到端 OCR + 预处理
└─ Fixtures/                     # 3 张实机截图 + 标注 + 实测文字区域

tools/MortarHUD.Benchmark/       # OCR 基准测试 + 字形模板生成
docs/ocr-benchmark.md            # 自动生成的基准报告
```

核心原则是各层解耦：`Capture ≠ OCR ≠ Parser ≠ Calculator ≠ Overlay`，每一层都能单独替换和单独测试。OCR 引擎藏在 `ICoordinateOcrEngine` 后面，预处理藏在 `IImagePreprocessor` 后面，换实现不影响上层。

## 测试

```bash
dotnet test MortarHUD.sln
```

当前 **176 个测试全部通过**（162 个 Core + 14 个 OCR）。

覆盖面包括：

- **解算**：八方向 + 同点 + 平移不变性 + `[0, 360)` 边界 + 2000 组随机的方位角范围检查；
- **解析**：TDD 列出的全部合法格式、可修正的 OCR 错认（`xI07.66`），以及必须拒绝的情况（`x10766`、小数位丢失、同轴歧义、轴字母出现在单词里）；
- **校验**：缺轴、越界、非有限值、低置信度、自定义范围；
- **状态机**：没有炮位时拒绝计算、OCR 失败时目标不被污染、换炮位后重算；
- **ROI**：默认值确实覆盖实测文字块、分辨率缩放、屏幕边缘裁剪、多显示器负原点；
- **OCR 端到端**：三张真实截图全部读对，且交叉验证的正确率不低于任何单条流水线；
- **预处理**：输出极性必须是黑字白底（极性反了 Tesseract 会静默降质）、三条流水线结果必须不同、不同 ROI 尺寸都能处理；
- **设置与主题**：读写往返、损坏文件降级、原子写入、内置主题不可删除、文件名净化。

### 生成字形模板库

模板匹配引擎的字形库是从实机截图里自动学出来的：

```bash
dotnet run --project tools/MortarHUD.Benchmark -- --gen-templates
```

它按「某一行切出的字形个数正好等于期望字符串长度」自动对齐标注，逐字符取平均，并在生成后做一致性检查剔除配错的样本。输出 `contact-sheet.png` 可以肉眼核对字形。

> [!NOTE]
> 当前模板库覆盖 `x y . 0 1 4 5 6 7 8 9`，**缺数字 2 和 3**——它们没有出现在这三张截图里。遇到不认识的字形时引擎会输出 `?` 而不是猜一个值，解析器随即判定失败。Tesseract 覆盖全部十个数字，所以生产默认走 Tesseract；模板引擎是语言包缺失时的兜底，也是基准测试的对照项。

## 分发选项

| 方式 | 命令 | 体积 | 目标机器需要什么 |
| --- | --- | --- | --- |
| **自包含文件夹**（默认） | `publish.cmd` | ~273 MB | 什么都不用装 |
| 自包含单文件 | `publish.cmd single` | 略小（压缩过） | 什么都不用装 |
| 框架依赖 | `dotnet publish -c Release -r win-x64 --self-contained false` | ~60 MB | .NET 10 桌面运行时 |

体积的大头是 .NET 运行时（70 MB）、`OpenCvSharpExtern.dll`（59 MB）和 WPF 框架程序集；
项目自身编译出来的三个 DLL 加起来只有 0.25 MB。

日常自用推荐默认的自包含文件夹：省心，且不受目标机器 dotnet 环境影响。
单文件模式会把原生库解压到临时目录再加载，Tesseract 与 OpenCvSharp 都带原生 DLL，
个别环境下可能解压失败，所以它是可选路径。

## 已知边界

**已经自动验证的**：上面列出的 176 个测试、启动自检（`--selftest`），加上在 3 张实机截图上的端到端 OCR 正确率。

**需要人工验证的**（这些本质上需要在真实桌面会话里操作，无法自动化）：

- Overlay 是否真的置顶、是否真的鼠标穿透、是否抢焦点、Alt+Tab 行为；
- 100% / 125% / 150% / 200% DPI 下的位置与 ROI 是否准确；
- 多显示器下的行为；
- 窗口化 / 无边框窗口 / 独占全屏三种模式下的可见性与截图能力；
- 在真实游戏环境里热键是否与游戏或其他软件冲突。

> [!IMPORTANT]
> 独占全屏（Exclusive Fullscreen）下 HUD 可能不可见、或截图失败。这是外置工具的固有限制，本项目不会通过「注入进程」的方式绕过。**推荐使用无边框窗口模式**，这也是 MVP 优先支持的模式。

**其它限制**：

- 基准测试集只有 3 个 ROI，正确率的统计意义有限；
- 模板引擎缺数字 2、3；
- 没有安装包，发布产物是一个自包含文件夹；
- 首次运行时 ROI 用的是按 1080p 实测得出的默认值。如果你的游戏 UI 缩放与默认差得较多，需要用 Test OCR 手动调一次。

## 开发说明

### 加一个新的预处理流水线

实现 `IImagePreprocessor`，返回黑字白底的单通道 `Mat`，然后在 `PreprocessorFactory.All` 里注册。如果它在基准测试里表现足够好，再考虑加进 `AutoCandidates`——注意 `Auto` 每多一条流水线就多约 40ms 的耗时，而 TDD 要求的端到端目标是 100ms 以内。

### 加一个新的 OCR 引擎

实现 `ICoordinateOcrEngine`。接口约定：引擎只负责把图上的字读出来，填 `RawText` 与 `Confidence`，`X`/`Y` 一律留空——数值解析由 `CoordinateTextParser` 负责，这是 TDD §15「OCR 不参与最终可信判断」的落地方式。

### 调试 OCR

预处理结果肉眼可见，这是排查 OCR 问题唯一靠谱的手段：

```bash
dotnet run --project tools/MortarHUD.Benchmark -- --dump ./_analysis/processed
```

它会把每条流水线在每张 fixture 上的二值化结果写成 PNG。调参时先看图，再看识别结果，不要反过来。

程序内的对应功能是设置里的 **OCR → Test OCR**（显示原图、预处理图、每条流水线的原始文本与耗时）和 **Debug** 页（把 ROI 与识别结果转储到 `%AppData%\MortarHUD\Debug\`）。

### 启动自检

改完启动路径相关的代码后，先跑自检再启动 GUI：

```bash
dist\MortarHUD\MortarHUD.exe --selftest
```

它会把启动流程完整走一遍——加载设置、构建 OCR 链路、构造全部三个窗口与视图模型、
跑一次真实采集——但**不显示窗口、不注册全局热键**，所以可以在游戏运行时安全执行。
退出码 0 表示通过。

存在的理由很实际：GUI 的启动路径（XAML 初始化顺序、事件触发时机、窗口构造）
是最容易出错又最不容易被测到的一段。这个自检第一次运行就抓出了两个真实缺陷——
一个让程序在没显示任何窗口的情况下静默退出，另一个让每个热键按下时都抛异常。

### 日志

`%AppData%\MortarHUD\Logs\yyyy-MM-dd.log`，按天分文件，自动清理 14 天前的。记录启动、退出、热键触发、采集、OCR 原始输出、解析结果、校验失败、解算结果与异常。默认不保存整屏截图。

---

本文档描述当前仓库源码。项目尚未建立版本发布流程。
