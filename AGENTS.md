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

# 测试（当前 190 个，必须全绿）
"C:/dotnet10/dotnet.exe" test MortarHUD.sln -c Release

# 发布到 dist\MortarHUD\
publish.cmd              # 默认：39 项 / 253MB
publish.cmd folder       # 备选：扁平布局 / 226MB
```

### ⚠️ 启动时必须切到程序目录

`dist\MortarHUD\libs\` 是托管程序集，靠 `runtimeconfig.json` 里的
`additionalProbingPaths` 定位。**该相对路径按「当前工作目录」解析，不是 exe 所在目录。**

从别处启动会报 `Could not load file or assembly PresentationFramework`。
双击 exe / 快捷方式没问题（资源管理器会切目录），`run.cmd` 已显式 `cd /d`。

---

## 三、诊断工具（排查问题时先想到它们）

```bash
# 启动自检：走完整启动流程但「不显示窗口、不注册热键、不碰桌面」
# 构造全部三个窗口 + 跑一次真实识别。退出码 0 表示通过。
dist\MortarHUD\MortarHUD.exe --selftest

# 设置窗口离屏渲染成 PNG（窗口不显示、不抢焦点）
dist\MortarHUD\MortarHUD.exe --screenshot <输出目录>

# OCR 基准测试（TDD §17），会生成 docs\ocr-benchmark.md
"C:/dotnet10/dotnet.exe" tools/MortarHUD.Benchmark/bin/Debug/net10.0-windows/MortarHUD.Benchmark.dll

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
│  ├─ Session/                  状态机、HUD 排版、Debug 排版
│  ├─ Configuration/            设置模型 + 持久化 + schema 迁移
│  ├─ Themes/                   主题模型 / 内置主题 / 读写
│  └─ Diagnostics/              文件日志
│
├─ MortarHUD.Capture/           截屏 → 预处理 → OCR
│  ├─ ScreenCapture/            IScreenCaptureProvider + GDI 实现 + ROI 计算
│  ├─ ImageProcessing/          Pipeline A / B / C（TDD §14）
│  └─ Ocr/                      引擎接口、Tesseract、模板匹配、交叉验证编排
│
├─ MortarHUD.Platform.Windows/  所有 Win32 互操作
│  ├─ Hotkeys/                  RegisterHotKey + Raw Input
│  ├─ NativeMethods/            Win32 / Gdi32 / RawInput
│  ├─ WindowStyles/             Overlay 窗口样式、显示器信息
│  ├─ Dpi/                      Per-Monitor V2
│  └─ Startup/                  开机自启
│
└─ MortarHUD.App/               WPF
   ├─ Views/                    Overlay、Debug 面板、设置窗口、HudRenderer、ColorEditor
   ├─ ViewModels/               SettingsViewModel
   ├─ Services/                 HudController
   ├─ Tray/                     系统托盘
   └─ Models/                   随程序发布的 OCR 资源（tessdata + 字形库）
```

**核心原则**：`Capture ≠ OCR ≠ Parser ≠ Calculator ≠ Overlay`，每层可独立替换与测试。

### OCR 是怎么工作的

1. 光标附近切 ROI（默认 `offset(-15,-90)` `150x140`，**由三张实机截图实测反推**，不是拍脑袋）
2. 用**两条独立流水线**（A：对比度拉伸 + Otsu；C：顶帽 + 双门限）分别二值化
3. 各自跑 Tesseract，各自解析
4. **交叉验证**：两条都成功且结果不一致 → 判定失败，不挑一个用
5. 严格解析：必须两位小数、不许歧义、不许猜小数点

**单条流水线只有 2/3 正确率，且错的不是同一例**；交叉验证后 3/3。这是整个 OCR 设计的立足点。

---

## 五、当前未决问题（明天从这里开始）

### 🔴 1. OCR 间歇性失败（最优先）

**现象**：同一个坐标、连续点多次，有概率失败。**用户已确认不是遮挡导致的。**

**当前缓解**：采集失败自动重试 3 次（间隔 120ms）。`App.CaptureAsync` → `CaptureOnceAsync` 循环。

**推测根因**：读数处在二值化临界点上。光标差 1px → 抗锯齿变一点 → Otsu 阈值一切就翻。
属于固定阈值方案的固有抖动，重试只是缓解。

**根治需要数据**，现在还没有：

```
设置 → 调试 → 勾选「启用 Debug 模式」+「保存原始 ROI」+「保存预处理结果」
→ 复现失败
→ 取 %AppData%\MortarHUD\Debug\ 里最新的三个文件：
     *_raw.png        实际截到的画面
     *_processed.png  二值化结果  ← 关键，能直接看出是字被削断还是压根没读出来
     *_result.json    每条流水线的原始文本与耗时
```

看到 `_processed.png` 才能判断是：
- 笔画被阈值削断 → 调 Pipeline A/C 的参数
- 完全没读到字 → ROI 位置问题
- 读到了但格式不符 → 解析器太严

### 🟡 2. 按 M 自动校准炮位（刚改完，待实测）

游戏按 M 打开地图时鼠标会复位到中心 = 自己的位置，所以「按 M」等价于「光标移到炮位上」。

- 走**观察型热键**（Raw Input），只监听不拦截 —— `RegisterHotKey` 会截住 M，游戏就收不到、地图打不开
- 按下后等 `AutoCalibrateDelayMs`（默认 350ms），读一次，失败再补一次（+150ms）
- **任何其它操作立刻取消**（`CancelPendingAutoCalibrate`）——
  窗口拉长是有害的：迟到的重试会在**错误的时刻**读到**别处**的坐标并当成炮位，比读不到更糟

**待验证**：关掉地图再开，是否每次都能正确锁定。

### 🟡 3. 输入层（Raw Input）稳定性（待实测）

热键/鼠标键曾用低级钩子，**两次出现「刚启动好使、用着用着彻底失效」** ——
根因是 Windows 对钩子回调有 300ms 硬超时，超时就静默摘掉且永不恢复。

现已换成 `RegisterRawInputDevices` + `RIDEV_INPUTSINK`（无回调、无超时）。
**待验证**：长时间使用后 M 和中键是否仍然可靠。日志里有 `[输入]` 前缀的记录。

### 🟡 4. 设置页「热键」新 UI 未做视觉验证

新增了「地图键自动校准」三行（开关 / 按键 / 延迟）。用 `--screenshot` 看一眼排版。

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

---

## 六、踩过的坑（别再踩一遍）

| 坑 | 后果 | 正确做法 |
| --- | --- | --- |
| **`Monitor` / `lock` 跨越 `await`** | 续体可能落在别的线程，`Monitor.Exit` 抛 `SynchronizationLockException` —— 每次热键都失败 | 用 `SemaphoreSlim.WaitAsync` |
| **低级钩子做输入监听** | 回调超时 → 被系统静默摘掉，永不恢复 | 用 Raw Input |
| **XAML `IsChecked="True"` + `Checked="..."`** | 事件在 `InitializeComponent()` 期间就触发，字段还全是 null → 构造窗口时崩溃、窗口一次都没显示 | 事件在构造函数尾部用代码挂 |
| **Aero2 默认控件模板** | 只设 `Foreground` 会「浅字压浅底」，字看不见；`SystemColors` 覆盖也无效 | 手写 `ControlTemplate`，补齐悬停/选中/禁用状态 |
| **删 `deps.json` 里列过的文件** | 宿主逐个校验依赖清单，少一个就拒绝启动 | 删之前先搜 deps.json；发布脚本末尾有全量核对 |
| **`additionalProbingPaths` 用扁平目录** | 只认 NuGet 布局 `libs/<包名>/<版本>/...` | 见 `tools/organize-publish.ps1` |
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
