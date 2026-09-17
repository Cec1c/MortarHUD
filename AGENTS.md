# AGENTS.md —— MortarHUD 项目速读

给接手这个项目的 AI / 开发者看的。**先读完这份再动手**，能省掉大量重复踩坑。

---

## 一、这是什么

《Wardogs》的迫击炮坐标解算外置 HUD。

玩家打开地图、鼠标指向某处时，游戏会在光标旁画出该点的绝对坐标：

```text
y109.78
x98.09
```

程序读出这两行，算出炮位到目标的方位角与距离，用透明置顶、鼠标穿透的 HUD 显示：

```text
AZ  079.0°
RNG 152m
```

**硬约束（改代码时不能破）**：不注入进程、不读写游戏内存、**不模拟键鼠输入**、不联网。
只做两件事：注册全局热键、在按键那一刻截取屏幕上一小块。
完整需求见 `MortarHUD_TDD_v0.2.md`。

---

## 二、环境（最容易卡住的地方）

| 事项 | 说明 |
| --- | --- |
| **.NET 10 SDK** | 装在 `C:\dotnet10`，**不在 PATH 里**。裸 `dotnet` 会解析到 .NET 6（系统 PATH 优先于用户 PATH，且 `C:\Program Files\dotnet` 只有 .NET 6、无写权限）。<br>**一律用 `"C:/dotnet10/dotnet.exe"`。** |
| **PowerShell** | 用 `pwsh`（7.6.6），**不要用 `powershell`**（5.1）。5.1 读无 BOM 的 UTF-8 脚本会按 GBK 解码，中文注释会把语法搞崩。 |
| **Python** | 3.10.11，带 `cv2` / `PIL` / `numpy`，用来分析截图很方便。 |
| **代理** | `127.0.0.1:7890`（已在环境变量里）。GitHub / NuGet 慢时走它。 |

```bash
# 构建
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release

# 测试（当前 202 个，必须全绿）
"C:/dotnet10/dotnet.exe" test MortarHUD.sln -c Release

# 发布（输出到 dist\MortarHUD-next-<模式>\，目录非空会拒绝发布）
publish.cmd              # portable（默认）：单文件压缩 / 约 97MB
publish.cmd folder       # 备选：文件夹布局（287 个文件），启动更快 / 约 227MB
publish.cmd runtime      # 框架依赖：目标机需装 .NET 10 桌面运行时
```

`publish.cmd` 只是 `tools\publish.ps1` 的壳。脚本固定用 `C:\dotnet10\dotnet.exe`，
不再 `where dotnet`（那会解析到 .NET 6）。发布完会校验产物里没有 FFmpeg 残留。

### 启动目录

现在的发布产物是标准扁平布局，**从任意目录启动都可以**——便携版与文件夹版都实测过。
早先的产物用 `libs\` + `additionalProbingPaths` 组织，那种才要求工作目录等于程序目录；
`OrganizeLegacyLayout=false` 之后不再生成这种布局。

---

## 三、诊断工具（排查问题时先想到它们）

```bash
# 启动自检：走完整启动流程，但「不显示窗口、不注册热键、不截取屏幕」
# 构造全部三个窗口，再用随程序发布的实机样本 Models\selftest\roi.png 跑一次端到端识别。
# 识别结果不对会非 0 退出——不会出现「识别挂了但自检仍然绿」。
dist\MortarHUD\MortarHUD.exe --selftest

# 设置窗口离屏渲染成 PNG（窗口不显示、不抢焦点）
dist\MortarHUD\MortarHUD.exe --screenshot <输出目录>

# 追加 --expanded：把所有折叠项展开后再渲染。
# 这条很重要 —— 折叠项展开后的排版本来只能靠人工看，有了它就能自动核对了。
dist\MortarHUD\MortarHUD.exe --screenshot <输出目录> --expanded

# 追加 --compact：用 920x680 渲染（默认 1040x760）
dist\MortarHUD\MortarHUD.exe --screenshot <输出目录> --compact

# OCR 基准测试（TDD §17），会生成 docs\ocr-benchmark.md
"C:/dotnet10/dotnet.exe" tools/MortarHUD.Benchmark/bin/Release/net10.0-windows/MortarHUD.Benchmark.dll

# 把每条流水线的二值化结果 dump 成 PNG —— 调 OCR 时唯一靠谱的手段
... MortarHUD.Benchmark.dll --dump <输出目录>

# 从实机截图重新学习字形模板库
... MortarHUD.Benchmark.dll --gen-templates
```

日志：`%AppData%\MortarHUD\Logs\yyyy-MM-dd.log`（输入层、热键、采集、OCR 明细都在里面）
Debug 转储：`%AppData%\MortarHUD\Debug\`（需在设置里开启）

---

## 四、架构

```
src/
├─ MortarHUD.Core/              纯计算，无 Windows / UI 依赖
│  ├─ Models/                   MapCoordinate、MortarSolution、CoordinateOcrResult
│  ├─ Ballistics/               距离 + 方位角（atan2(东, 北)，顺序别写反）
│  ├─ Parsing/                  x/y 解析 + OCR 字符修正
│  ├─ Validation/               范围 / 置信度校验
│  ├─ Session/                  状态机、HUD 排版、Debug 排版、操作调度（LatestOperationRunner）
│  ├─ Configuration/            设置模型 + 持久化 + schema 迁移
│  ├─ Themes/                   主题模型 / 内置主题 / 读写
│  └─ Diagnostics/              文件日志
│
├─ MortarHUD.Capture/           截屏 → 预处理 → OCR
│  ├─ ScreenCapture/            IScreenCaptureProvider + GDI 实现 + ROI 计算
│  ├─ ImageProcessing/          Pipeline A / B / C（TDD §14）
│  ├─ Diagnostics/              Debug 转储（原始 ROI / 预处理图 / 结果 JSON）
│  └─ Ocr/                      引擎接口、Tesseract、模板匹配、交叉验证编排
│
├─ MortarHUD.Platform.Windows/  所有 Win32 互操作
│  ├─ Hotkeys/                  RegisterHotKey + Raw Input（含按下/松开去重）
│  ├─ Mouse/                    光标位置、前台窗口与归位判断（CaptureContext）
│  ├─ NativeMethods/            Win32 / Gdi32 / RawInput
│  ├─ WindowStyles/             Overlay 窗口样式、显示器信息
│  ├─ Dpi/                      Per-Monitor V2
│  └─ Startup/                  开机自启
│
└─ MortarHUD.App/               WPF
   ├─ Views/                    Overlay、Debug 面板、设置窗口（三页）、HudRenderer、ColorEditor
   ├─ ViewModels/               SettingsViewModel
   ├─ Services/                 HudController
   ├─ Tray/                     系统托盘
   ├─ Assets/                   应用图标（EXE / 窗口 / 托盘共用同一份 .ico）
   └─ Models/                   随程序发布的 OCR 资源（tessdata + 字形库 + selftest 样本）
```

**核心原则**：`Capture ≠ OCR ≠ Parser ≠ Calculator ≠ Overlay`，每层可独立替换与测试。

### OCR 是怎么工作的

1. 光标附近切 ROI（默认 `offset(-15,-90)` `150x140`，**由三张实机截图实测反推**，不是拍脑袋）
2. 用**两条独立流水线**（A：对比度拉伸 + Otsu；C：顶帽 + 双门限）分别二值化
3. 各自跑 Tesseract，各自解析
4. **交叉验证**：有**任何**分歧就整体判失败（`PIPELINE_DISAGREEMENT`）——
   不投票取多数，也不挑一个用。多加一条相似流水线不能把一条不同的读数「投」下去
5. 严格解析：必须两位小数、不许歧义、不许猜小数点
6. **最后**才用综合置信度（`0.55 + 0.25×一致比例 + 0.20×引擎自报`）跟 `MinimumConfidence` 比。
   单条流水线的原始置信度**不**参与门槛判断——实测 Tesseract 对读对了的坐标也会给 0.00

**单条流水线只有 2/3 正确率，且错的不是同一例**；交叉验证后 3/3。这是整个 OCR 设计的立足点。

---

## 五、当前未决问题（明天从这里开始）

### 🔴 1. OCR 间歇性失败（最优先）

**现象**：同一个坐标、连续点多次，有概率失败。**用户已确认不是遮挡导致的。**

**当前缓解**：采集失败自动重试 3 次（间隔 120ms），
并且整轮采集已经串行化 + 可取消——迟到的识别结果不会再覆盖新状态。

**推测根因**：读数处在二值化临界点上。光标差 1px → 抗锯齿变一点 → Otsu 阈值一切就翻。
属于固定阈值方案的固有抖动，重试只是缓解。

**根治需要数据**，现在还没有。采集入口已经做好了：

```
设置 → 诊断 → 「收集接下来 10 次采集」
→ 进游戏复现失败
→ 取 %AppData%\MortarHUD\Debug\ 里最新的那批文件：
     *_raw.png        实际截到的画面
     *_processed.png  二值化结果  ← 关键，能直接看出是字被削断还是压根没读出来
     *_result.json    每条流水线的原始文本与耗时
```

这个按钮只收集指定次数就自动停，不用一直开着全局 Debug 开关。

看到 `_processed.png` 才能判断是：
- 笔画被阈值削断 → 调 Pipeline A/C 的参数
- 完全没读到字 → ROI 位置问题
- 读到了但格式不符 → 解析器太严

### 🟡 2. 实机验证：M 键自动校准与输入层

游戏按 M 打开地图时鼠标会复位到中心 = 自己的位置，所以「按 M」等价于「光标移到炮位上」。

设计要点（改过两轮，别再退回去）：

- 走**观察型热键**（Raw Input），只监听不拦截 —— `RegisterHotKey` 会截住 M，游戏就收不到、地图打不开
- 观察型只能绑**单个裸键**，带修饰键的绑定会被 `ApplyObserved` 直接拒绝
- **按下与松开分开处理**（`ObservedKeyState`）：Raw Input 会把按下、长按重复、松开都报上来，
  只把新的按下当成一次操作。在此之前，松开 M 也会触发一次校准，还会取消掉刚发起的那次
- 按下后等 `AutoCalibrateDelayMs`（默认 350ms），最多补一次（+150ms）
- **只有光标确实回到前台窗口中心才采用结果**（`CaptureContext.IsClientCenter`），
  没归位就跳过并记日志——这条挡住了「关图后鼠标没归位，却把别处的坐标当成炮位」
- **任何其它操作立刻取消**（`UserActivity` → `LatestOperationRunner.CancelOnActivity`）——
  窗口拉长是有害的：迟到的重试会在**错误的时刻**读到**别处**的坐标

输入层整体：热键/鼠标曾用低级钩子，**两次出现「刚启动好使、用着用着彻底失效」** ——
根因是 Windows 对钩子回调有 300ms 硬超时，超时就静默摘掉且永不恢复。
现已换成 `RegisterRawInputDevices` + `RIDEV_INPUTSINK`（无回调、无超时）。

**待验证**（只能实机做）：

- 关图再开，是否每次都能正确锁定炮位
- 长时间使用后 M 和中键是否仍然可靠（日志里有 `[输入]` 前缀的记录）
- 光标没归位时是否正确跳过，而不是读到别处的坐标

### 🟡 3. 设置界面：初态与展开态已核对，交互态待人工

视觉重做（配色、间距、八类控件模板、卡片、滚动条、ToolTip）已完成。三页（日常使用 / 外观 / 诊断）
的**初始态**与**展开态**（`--screenshot <目录> --expanded`）都已逐页核对：
栅格、圆角、表面层次、滑块轨道都符合规格。

`--screenshot` 这条路径看不到的只剩真正的交互态：悬停、键盘焦点、下拉弹层、高 DPI，
以及设置应用、保存主题、删除主题这些实际操作。

**已知的两处未收口**（都不影响使用，记录备查）：

- 「颜色编辑器」（外观页折叠项里）还是旧视觉语言：`Views/ColorEditor.xaml` 自己写了
  `#3A3D42` / `#26282C` 和圆角 3，没有跟着这次重做走。它只在展开折叠项后才可见。
- 卡片内按钮的悬停底色用的是新增的 `SurfaceHoverBrush`；`SurfaceAltBrush` 已废弃，
  不要再把它拿回来当通用底色用——那正是这次「层次压平」的病根。

### ⚪ 5. 从未验证过的 TDD 验收项

这些都必须在真实桌面会话里手工验证，自动化不了：

- Overlay 是否真置顶 / 真鼠标穿透 / 不抢焦点 / Alt+Tab 行为
- 100% / 125% / 150% / 200% DPI 下的位置与 ROI
- 多显示器
- 窗口化 / 无边框窗口 / 独占全屏三种模式
- 热键与游戏本身是否冲突

### ⚪ 6. 基准测试集只有 3 个 fixture

TDD §17 要求至少 30 个，覆盖不同地图区域 / 明暗背景 / 缩放等级。
补图方式：扔进 `tests/Fixtures/screenshots/`，往 `fixtures.json` 加条目。
`labelBounds` 字段是手工实测的文字区域，只有模板生成器用。

### ⚪ 7. 两处已知的小问题

- `HotkeySettings.AutoCalibrateAttempts`（默认 4）**从来没被读过**——
  `App.RunCaptureAsync` 里是硬编码的 `automatic ? 2 : 3`。要么接上它，要么删掉。
- `publish.cmd` 的默认输出目录叫 `dist\MortarHUD-next-portable`，
  `next` 是开发期遗留，正式分发前该定名。

---

## 六、踩过的坑（别再踩一遍）

| 坑 | 后果 | 正确做法 |
| --- | --- | --- |
| **`Monitor` / `lock` 跨越 `await`** | 续体可能落在别的线程，`Monitor.Exit` 抛 `SynchronizationLockException` —— 每次热键都失败 | 用 `SemaphoreSlim.WaitAsync` |
| **低级钩子做输入监听** | 回调超时 → 被系统静默摘掉，永不恢复 | 用 Raw Input |
| **XAML `IsChecked="True"` + `Checked="..."`** | 事件在 `InitializeComponent()` 期间就触发，字段还全是 null → 构造窗口时崩溃、窗口一次都没显示 | 事件在构造函数尾部用代码挂 |
| **Aero2 默认控件模板** | 只设 `Foreground` 会「浅字压浅底」，字看不见；`SystemColors` 覆盖也无效 | 手写 `ControlTemplate`，补齐悬停/选中/禁用状态 |
| **删 `deps.json` 里列过的文件** | 宿主逐个校验依赖清单，少一个就拒绝启动 | 删之前先搜 deps.json；发布脚本末尾有全量核对 |
| **发布后删本机原生库** | 单文件模式下 EXE 早就打包完了，事后删目录里的文件没有任何作用 | 在 `ComputeFilesToPublish` 之后、`_ComputeFilesToBundle` 之前用 MSBuild 过滤（见 `MortarHUD.App.csproj` 的 `FilterUnusedPublishAssets`）；或者源头就用 `ExcludeAssets="all"` + 显式 `Content` 只带要的那一个 |
| **Raw Input 把按下、长按重复、松开都报上来** | 松开 M 也触发一次校准，还会取消掉刚发起的那次 | `ObservedKeyState` 按 (设备, 键) 记状态，只认新的按下；松开只清理 |
| **`WM_INPUT` 处理完不调 `DefWindowProc`** | 系统的输入缓冲不会被清理 | 返回 `DefWindowProc(hWnd, msg, wParam, lParam)` |
| **`additionalProbingPaths` 指向扁平目录** | 只认 NuGet 布局 `libs/<包名>/<版本>/...`，扁平目录会报 "assembly specified in the dependency manifest was not found" | 现在默认不再组织成 libs 布局（`OrganizeLegacyLayout=false`，产物是标准扁平布局）。只有要恢复旧布局才需要 `tools/organize-publish.ps1` |
| **Python 用 `utf-8` 读带 BOM 的文件** | 不会剥掉 BOM，再写一次就变成双 BOM | 用 `utf-8-sig` 或 `lstrip('\ufeff')` |
| **`.cmd` 文件存成 UTF-8** | cmd.exe 按 GBK 读，中文注释被解成命令 | 存成 GBK |
| **`UseWPF` + `UseWindowsForms` 同时开** | `Application`/`Window`/`Brush`/`Size` 全部二义 | 用 `<Using Remove="..."/>` 去掉 WinForms 的隐式 using |
| **`RunOnMessageThread` 超时后静默返回** | 热键静默失效，日志无痕 | 超时必须抛异常 |
| **重建 `_session` 后忘了 `HudController`** | HUD 一直显示旧 session，表现为「程序还在但什么都不更新」 | 见 `ApplySettingsFromUi` |

---

## 七、代码约定

- **注释写「为什么」，不写「是什么」**。每个非常规决定都注明了原因和当时的实测数据。
- 全中文注释与界面文案。技术术语保留英文。
- **测试即规格**。改行为前先看对应测试；改完必须 `dotnet test` 全绿。
- 关键语义（有测试钉住，别破坏）：
  - 采集失败**不得**改动已保存的炮位/目标
  - 没有炮位时按记录目标 → 拒绝且不计算
  - 方位角恒在 `[0, 360)`
  - OCR 失败时 HUD 必须显示「目标未改变」

---

## 八、给新会话的开场建议

```
先读 AGENTS.md。当前要处理的是「未决问题」第 1 条（OCR 间歇性失败）。
我已经准备好失败时的 ROI 转储，在 <路径>。
```

不要重复本文档已经记录的环境探索 —— 直接开始干活。
