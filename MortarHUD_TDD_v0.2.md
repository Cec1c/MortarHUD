# MortarHUD 技术设计文档（TDD）v0.2

## 0. 文档定位

本项目是一个 Windows 外置 HUD 工具，目标体验接近 Crosshair X：

- 不注入游戏进程；
- 不读取游戏内存；
- 不向游戏发送键鼠输入；
- 只读取屏幕像素；
- 通过全局快捷键触发坐标采集；
- OCR 识别游戏地图光标附近显示的 `x/y` 坐标；
- 计算炮位 A 到目标 B 的方位角（AZ）与距离（RNG）；
- 使用透明、置顶、鼠标穿透的 HUD 显示结果；
- HUD 的位置、颜色、字体、样式、主题均可配置。

项目名暂定：**MortarHUD**

---

# 1. 用户流程

## 1.1 记录炮位

1. 玩家打开地图。
2. 将鼠标指向炮位/自身位置。
3. 游戏在鼠标附近显示绝对坐标，例如：

```text
y109.78
x98.09
```

4. 用户按下“记录炮位”快捷键。
5. 程序：
   - 读取当前鼠标屏幕坐标；
   - 以鼠标为锚点截取一块小 ROI；
   - 本地 OCR 识别 `x/y`；
   - 解析并校验；
   - 保存为炮位 A；
   - HUD 短暂显示 `GUN LOCKED`。

炮位保存后，即使进入迫击炮后自己的地图箭头消失，也不影响后续使用。

## 1.2 记录目标

1. 玩家在地图上将鼠标移动到目标位置。
2. 游戏显示目标点绝对坐标。
3. 用户按下“记录目标”快捷键。
4. 程序识别坐标并保存为目标 B。
5. 立即计算：
   - ΔX
   - ΔY
   - RNG
   - AZ
6. HUD 更新显示。

地图可以：

- 缩放；
- 缩小；
- 平移；
- 重新居中；

因为程序读取的是游戏自身显示的**绝对坐标**，所以不依赖地图像素比例。

---

# 2. 已知坐标表现

现有截图中观察到：

```text
x98.09
y109.78
```

```text
x99.58
y110.07
```

```text
x107.66
y114.54
```

已知默认坐标系：

```text
+X = East
+Y = North
```

方位角定义：

```text
North = 0°
East  = 90°
South = 180°
West  = 270°
```

地图：

```text
1 coordinate unit ≈ 100 m
```

这些参数必须配置化，不能硬编码在核心算法里。

---

# 3. MVP 范围

v0.1 / v0.2 必须完成：

1. WPF 设置 GUI。
2. 系统托盘常驻。
3. 全局快捷键。
4. 获取鼠标位置。
5. 鼠标相对 ROI 截图。
6. OpenCV 图像预处理。
7. 本地 OCR。
8. `x/y` Parser。
9. OCR 结果校验。
10. 炮位 A 缓存。
11. 目标 B 缓存。
12. RNG / AZ 计算。
13. Crosshair X 风格透明 HUD。
14. HUD 位置调整。
15. HUD 颜色调整。
16. HUD 字体与字号调整。
17. HUD 样式/主题系统。
18. Debug 模式。
19. OCR 截图与预处理结果预览。
20. 设置持久化。

---

# 4. 非目标

当前版本不实现：

- 游戏进程注入；
- DLL 注入；
- 内存读取；
- 自动瞄准；
- 自动输入迫击炮参数；
- 自动控制键盘或鼠标；
- 自动开火；
- DeepSeek / GPT / 云端 OCR；
- 多人网络同步；
- 弹道模拟；
- 风速修正；
- 武器数据库；
- 自动识别敌人；
- 视频分析。

---

# 5. 推荐开发环境

## 5.1 基础环境

推荐：

```text
Windows 11 x64
Visual Studio 2026
.NET 10 SDK
C#
WPF
Git
```

Visual Studio 安装时选择：

```text
.NET desktop development
.NET 10
```

项目模板：

```text
WPF Application
```

不要使用：

```text
WPF Application (.NET Framework)
```

目标框架：

```xml
<TargetFramework>net10.0-windows</TargetFramework>
```

建议架构：

```text
x64
```

## 5.2 可选 IDE

也可以使用：

- JetBrains Rider；
- VS Code + C# Dev Kit；

但本项目涉及：

- WPF；
- XAML；
- Win32；
- 调试透明窗口；

所以优先推荐 Visual Studio。

---

# 6. NuGet / 技术依赖

建议基础依赖：

```text
OpenCvSharp4
OpenCvSharp4.runtime.win
Microsoft.Extensions.Logging
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Configuration
System.Text.Json
```

OCR 实现必须放在接口后方。

候选：

```text
Tesseract 5
ONNX Runtime OCR
PaddleOCR-compatible ONNX
自定义数字模板识别
```

推荐先建立 OCR Benchmark，再决定默认 OCR Engine。

不要让 UI 或核心业务直接依赖某个 OCR SDK。

---

# 7. Solution 结构

```text
MortarHUD.sln

src/
├─ MortarHUD.App/
│  ├─ App.xaml
│  ├─ MainWindow.xaml
│  ├─ OverlayWindow.xaml
│  ├─ Tray/
│  ├─ Views/
│  ├─ ViewModels/
│  └─ Themes/
│
├─ MortarHUD.Core/
│  ├─ Models/
│  ├─ Ballistics/
│  ├─ Configuration/
│  ├─ Validation/
│  └─ Themes/
│
├─ MortarHUD.Capture/
│  ├─ ScreenCapture/
│  ├─ ImageProcessing/
│  └─ Ocr/
│
└─ MortarHUD.Platform.Windows/
   ├─ Hotkeys/
   ├─ Mouse/
   ├─ WindowStyles/
   ├─ Dpi/
   └─ NativeMethods/

tests/
├─ MortarHUD.Core.Tests/
├─ MortarHUD.Ocr.Tests/
└─ Fixtures/
```

核心原则：

```text
Capture != OCR != Parser != Calculator != Overlay
```

各模块必须可以独立测试和替换。

---

# 8. 核心数据模型

## 8.1 MapCoordinate

```csharp
public readonly record struct MapCoordinate(
    double X,
    double Y
);
```

## 8.2 MortarSolution

```csharp
public readonly record struct MortarSolution(
    double DeltaX,
    double DeltaY,
    double RangeMeters,
    double BearingDegrees
);
```

## 8.3 CoordinateOcrResult

```csharp
public sealed record CoordinateOcrResult
{
    public bool Success { get; init; }

    public double? X { get; init; }
    public double? Y { get; init; }

    public string RawText { get; init; } = "";
    public double Confidence { get; init; }

    public string? Error { get; init; }

    public TimeSpan CaptureTime { get; init; }
    public TimeSpan PreprocessTime { get; init; }
    public TimeSpan OcrTime { get; init; }
}
```

---

# 9. 弹道参数计算

炮位：

```text
A = (Xa, Ya)
```

目标：

```text
B = (Xb, Yb)
```

计算：

```text
dx = Xb - Xa
dy = Yb - Ya
```

距离：

```text
coordinateDistance = sqrt(dx² + dy²)

RNG = coordinateDistance × metersPerCoordinateUnit
```

默认：

```text
metersPerCoordinateUnit = 100
```

方位角：

```csharp
double bearing =
    Math.Atan2(dx, dy) * 180.0 / Math.PI;

if (bearing < 0)
    bearing += 360.0;
```

输出：

```text
0 <= Bearing < 360
```

示例：

```text
A = 98.09, 109.78
B = 99.58, 110.07

RNG ≈ 151.8 m
AZ  ≈ 79.0°
```

---

# 10. 鼠标与 ROI 截图

## 10.1 鼠标的职责

鼠标**不是测量坐标的工具**。

它只是告诉程序：

> 游戏当前在哪里绘制了 x/y 坐标。

流程：

```text
Hotkey
  ↓
GetCursorPos()
  ↓
Mouse-relative ROI
  ↓
Screenshot
  ↓
OCR
```

因此：

```text
地图缩放不影响
地图平移不影响
重新居中不影响
```

## 10.2 ROI 配置

默认暂定：

```text
Width   = 240 px
Height  = 140 px
OffsetX = +10 px
OffsetY = -80 px
```

实际默认值必须根据截图 Benchmark 调整。

GUI 中必须支持：

```text
Width
Height
Offset X
Offset Y
```

并提供实时预览。

---

# 11. 屏幕捕获

定义：

```csharp
public interface IScreenCaptureProvider
{
    Bitmap Capture(Rectangle physicalPixelRect);
}
```

v0.1 可以先实现简单 provider：

```text
GDI / Graphics.CopyFromScreen
```

因为：

- 只在快捷键触发时截图；
- ROI 很小；
- 不需要 60 FPS 连续捕获。

后续如果部分游戏模式无法捕获，可以增加：

```text
DXGI Desktop Duplication
```

作为第二 provider。

Capture 模块不得写死某一种技术。

---

# 12. DPI

必须启用：

```text
Per-Monitor V2 DPI Awareness
```

必须正确处理：

```text
100%
125%
150%
175%
200%
```

注意：

```text
WPF DIP != Physical Pixel
```

`GetCursorPos()`、截图 ROI、Overlay 定位需要明确在哪个坐标空间工作。

建议底层统一使用：

```text
Physical Pixels
```

WPF 展示层再做转换。

---

# 13. OCR 架构

接口：

```csharp
public interface ICoordinateOcrEngine
{
    Task<CoordinateOcrResult> RecognizeAsync(
        Mat input,
        CancellationToken cancellationToken);
}
```

要求：

- 完全本地；
- 不要求网络；
- 不要求 Python runtime；
- OCR engine 可以替换；
- 支持 Benchmark。

---

# 14. OCR 预处理

OCR 输入不是整屏，而是约 200 × 100 的 ROI。

建议至少实现三套 pipeline。

## Pipeline A

```text
Crop
→ Resize 3x
→ Grayscale
→ Contrast Enhance
→ Binary Threshold
→ OCR
```

## Pipeline B

```text
Crop
→ Resize 4x
→ Grayscale
→ Adaptive Threshold
→ Morphology Close
→ OCR
```

## Pipeline C

```text
Crop
→ Brightness/Color Mask
→ 提取亮色 UI 文本
→ Resize
→ Threshold
→ OCR
```

设置中：

```text
Preprocessor:
Auto
A
B
C
```

Debug 模式允许直接比较不同 Pipeline。

---

# 15. OCR Parser

OCR 不负责最终可信判断。

识别结果必须进入 Parser。

目标格式：

```text
x107.66
y114.54
```

x/y 顺序不可写死。

匹配：

```regex
[xX]\s*[:：]?\s*(-?\d{1,3}(?:\.\d{1,2})?)
```

```regex
[yY]\s*[:：]?\s*(-?\d{1,3}(?:\.\d{1,2})?)
```

允许有限 OCR 修正：

```text
O / o → 0
I / l → 1
```

但只能在数字上下文中修正。

禁止类似：

```text
10766 → 自动猜成 107.66
```

如果小数点无法确认，应判定失败。

---

# 16. OCR Validation

结果至少满足：

```text
X found
Y found
X range valid
Y range valid
Format valid
Confidence valid
```

配置：

```text
CoordinateMin
CoordinateMax
MinimumConfidence
```

OCR 失败：

```text
OCR FAILED
```

必须保持原目标不变，并明确显示：

```text
Target unchanged
```

不能让用户误以为新目标已经锁定。

---

# 17. OCR Benchmark

正式选择 OCR 引擎前，必须建立真实截图测试集。

当前初始 Fixture：

```text
Fixture 001
Expected X = 98.09
Expected Y = 109.78
```

```text
Fixture 002
Expected X = 99.58
Expected Y = 110.07
```

```text
Fixture 003
Expected X = 107.66
Expected Y = 114.54
```

后续要求至少收集：

```text
30 个 ROI
```

覆盖：

- 不同地图区域；
- 黑色背景；
- 灰色背景；
- 复杂地形；
- 目标图标附近；
- UI 弹窗附近；
- 不同缩放等级。

Benchmark 输出：

```text
Fixture
OCR Engine
Pipeline
Raw Text
Parsed X
Parsed Y
Expected X
Expected Y
Correct
Preprocess ms
OCR ms
Total ms
```

目标：

```text
Coordinate correctness >= 99%
```

如果测试集不足，不得宣称 OCR 已稳定。

---

# 18. 全局快捷键

使用：

```text
RegisterHotKey
UnregisterHotKey
```

程序只监听输入，不模拟输入。

默认：

```text
F6 = Capture Gun
F7 = Capture Target
F8 = Toggle HUD
F9 = Open Settings
```

支持：

```text
单键
Ctrl + Key
Alt + Key
Shift + Key
组合键
```

要求：

- 热键冲突检查；
- RegisterHotKey 失败时明确提示；
- 两个功能不能绑定相同热键。

---

# 19. Overlay HUD

## 19.1 行为

Overlay 目标体验接近 Crosshair X。

要求：

```text
Transparent
Always On Top
Borderless
No Taskbar
Mouse Click-through
No Activate
No Focus Steal
```

Win32 可使用：

```text
WS_EX_LAYERED
WS_EX_TRANSPARENT
WS_EX_TOOLWINDOW
WS_EX_NOACTIVATE
```

正常运行时：

```text
游戏接受鼠标输入
HUD 不响应鼠标
```

进入“编辑 HUD”模式后：

```text
临时关闭 Click-through
允许拖动
允许调整位置
```

保存后重新进入穿透状态。

---

# 20. HUD 默认视觉

默认主题应尽量像 Crosshair 工具，而不是普通桌面窗口。

默认采用：

```text
绿色高亮字体
透明背景
无窗口边框
轻微文字描边/阴影
紧凑布局
```

默认 HUD：

```text
AZ  079.0°
RNG 152m
```

可选详细模式：

```text
GUN    98.09 / 109.78
TGT    99.58 / 110.07

AZ     079.0°
RNG    152m
```

状态信息短暂显示：

```text
GUN LOCKED
TARGET LOCKED
OCR FAILED
```

状态信息自动淡出，但核心 AZ/RNG 持续显示。

---

# 21. HUD 自定义系统

HUD 必须支持用户修改：

```text
位置
颜色
字体
字号
字体粗细
透明度
文字描边
阴影
行距
字间距
对齐
显示字段
数值精度
布局
```

## 21.1 Position

支持两种方式：

### Drag

设置界面：

```text
[Unlock HUD]
```

用户直接拖动 Overlay。

### Numeric

```text
Anchor:
Top Left
Top Center
Top Right
Center Left
Center
Center Right
Bottom Left
Bottom Center
Bottom Right

Offset X
Offset Y
```

这样不同分辨率下位置更稳定。

---

# 22. HUD 颜色

支持：

```text
文字颜色
强调色
状态成功色
警告色
失败色
描边颜色
阴影颜色
```

默认：

```text
Primary = Bright Green
```

GUI 使用颜色选择器。

允许：

```text
HEX
RGB
ARGB
```

示例：

```text
#7CFF6B
```

用户可以完全改成：

```text
White
Cyan
Orange
Red
Purple
```

---

# 23. HUD 字体

支持：

```text
Font Family
Font Size
Font Weight
Italic
Letter Spacing
Line Height
```

默认优先使用清晰的 UI / Mono 字体。

例如：

```text
Segoe UI
Segoe UI Variable
Cascadia Mono
Consolas
```

不应依赖程序附带第三方字体文件。

---

# 24. HUD 特效

允许配置：

```text
Text Outline:
Off / Thin / Medium / Thick

Shadow:
Off / Soft / Hard

Background:
None
Transparent Panel
Solid Panel

Background Opacity
Corner Radius
Padding
```

默认主题：

```text
Background = None
Text Outline = Thin
Shadow = Soft
```

这样在亮/暗地图上都尽量可见。

---

# 25. HUD 布局

预设：

## Minimal

```text
079.0°
152m
```

## Compact

```text
AZ 079.0°
RNG 152m
```

## Detailed

```text
GUN 98.09 109.78
TGT 99.58 110.07
AZ  079.0°
RNG 152m
```

## Horizontal

```text
AZ 079.0°   RNG 152m
```

后续可以继续增加布局模板。

---

# 26. Theme 系统

主题不应该写死在 XAML。

建议：

```text
%AppData%/
└─ MortarHUD/
   ├─ settings.json
   └─ Themes/
      ├─ DefaultGreen.json
      ├─ TacticalWhite.json
      ├─ Amber.json
      └─ Custom.json
```

主题示例：

```json
{
  "name": "Default Green",
  "fontFamily": "Cascadia Mono",
  "fontSize": 22,
  "fontWeight": "SemiBold",

  "primaryColor": "#7CFF6B",
  "secondaryColor": "#B8FFAF",

  "successColor": "#7CFF6B",
  "warningColor": "#FFD866",
  "errorColor": "#FF6464",

  "outlineEnabled": true,
  "outlineColor": "#80000000",
  "outlineThickness": 1.0,

  "shadowEnabled": true,

  "backgroundEnabled": false,
  "backgroundColor": "#80000000",

  "opacity": 1.0,

  "layout": "Compact"
}
```

用户修改主题时：

```text
Preview
Apply
Save As
Reset
```

支持创建自定义主题。

---

# 27. Settings GUI

GUI 目标：

```text
简单
清楚
不干扰游戏
```

建议 Tab：

```text
General
Hotkeys
HUD
Themes
OCR
Debug
```

---

# 28. General

内容：

```text
[ ] Start minimized
[ ] Start with Windows
[ ] Minimize to tray

Coordinate scale:
[100] meters / unit

X positive:
[East]

Y positive:
[North]

Language:
[简体中文]
```

---

# 29. Hotkeys

```text
Capture Gun       [ F6 ]
Capture Target    [ F7 ]
Toggle HUD        [ F8 ]
Open Settings     [ F9 ]

[Restore Defaults]
```

按键输入框：

1. 点击；
2. 显示 `Press a key...`；
3. 用户按组合键；
4. 自动记录。

---

# 30. HUD 设置页

包含实时 Preview。

设置：

```text
Layout
Font
Font Size
Font Weight

Primary Color
Secondary Color

Opacity
Outline
Shadow

Anchor
Offset X
Offset Y

Bearing Decimals
Range Decimals

[x] Show AZ
[x] Show RNG
[ ] Show Gun
[ ] Show Target

[Unlock HUD Position]
[Reset Position]
```

修改设置时 HUD 应实时刷新。

---

# 31. Themes 页

显示主题卡片：

```text
Default Green
Tactical White
Amber
High Contrast
Custom
```

功能：

```text
Apply
Duplicate
Rename
Delete
Reset
Import
Export
```

内置主题不能删除。

自定义主题存 JSON。

---

# 32. OCR 设置页

内容：

```text
OCR Engine:
[Auto]

Preprocessing:
[Auto]

ROI:
Width
Height
Offset X
Offset Y

Coordinate Min
Coordinate Max

Minimum Confidence

[Test OCR]
```

Test OCR 后显示：

```text
Raw ROI
Processed ROI

Raw OCR:
y114.54 x107.66

Parsed:
X 107.66
Y 114.54

PASS

Capture:    1.8 ms
Preprocess: 0.9 ms
OCR:       14.5 ms
Total:     17.2 ms
```

---

# 33. Debug 模式

必须提供 Debug Toggle。

Debug Mode 开启后允许：

```text
[ ] Show ROI rectangle
[ ] Show cursor anchor
[ ] Show raw OCR text
[ ] Show parsed coordinates
[ ] Show confidence
[ ] Show timing
[ ] Save raw ROI
[ ] Save processed ROI
```

保存目录：

```text
%AppData%/MortarHUD/Debug/
```

命名：

```text
2026-09-16_201500_raw.png
2026-09-16_201500_processed.png
2026-09-16_201500_result.json
```

Debug 默认关闭。

---

# 34. Debug Overlay

开启 Debug HUD 时可以显示：

```text
CURSOR 1534,682

ROI
X=1544
Y=602
W=240
H=140

OCR 18.4ms
CONF 0.94

RAW:
y114.54 x107.66

X 107.66
Y 114.54
```

Debug Overlay 与正常 HUD 分离。

---

# 35. 设置持久化

目录：

```text
%AppData%/MortarHUD/
```

主要文件：

```text
settings.json
Themes/
Logs/
Debug/
```

设置模型建议版本化：

```json
{
  "schemaVersion": 1
}
```

以后升级时支持 migration。

---

# 36. System Tray

关闭主设置窗口时：

```text
Minimize to tray
```

Tray Menu：

```text
Show Settings
Toggle HUD
Capture Gun
Capture Target
Debug Mode
Exit
```

程序退出时：

```text
UnregisterHotKey
Dispose Overlay
Dispose OCR
Flush logs
```

---

# 37. 状态机

建议内部维护：

```text
NoGun
GunLocked
TargetLocked
OcrError
```

典型流程：

```text
NoGun
  ↓ F6 success
GunLocked
  ↓ F7 success
TargetLocked
  ↓ F7 success
TargetLocked (updated)
```

F7 在没有炮位时：

```text
NO GUN POSITION
```

不得计算。

---

# 38. 错误处理

## OCR Failure

```text
OCR FAILED
Target unchanged
```

## Invalid Coordinate

```text
INVALID COORDINATE
```

## Hotkey Conflict

```text
Hotkey registration failed.
The key may already be used by another application.
```

## Capture Failure

```text
CAPTURE FAILED
```

所有错误写入 log。

---

# 39. Logging

使用：

```text
Microsoft.Extensions.Logging
```

日志目录：

```text
%AppData%/MortarHUD/Logs/
```

记录：

```text
startup
shutdown
hotkey
capture
OCR raw output
parsed coordinates
validation failure
calculation
settings changes
exceptions
```

默认不要保存整屏截图。

---

# 40. 性能目标

一次目标采集：

```text
Hotkey
→ Capture
→ Preprocess
→ OCR
→ Parse
→ Calculate
→ HUD Update
```

目标：

```text
< 100 ms
```

理想：

```text
20 ~ 50 ms
```

因为这是按键触发，不要求连续 60 FPS OCR。

Overlay 本身应保持轻量。

---

# 41. 测试

## 41.1 Calculator Tests

必须覆盖：

```text
North
East
South
West
NE
SE
SW
NW
Same Position
```

例如：

```text
Gun    = (0, 0)
Target = (0, 1)

AZ = 0°
```

```text
Target = (1, 0)

AZ = 90°
```

```text
Target = (0, -1)

AZ = 180°
```

```text
Target = (-1, 0)

AZ = 270°
```

---

# 42. Parser Tests

覆盖：

```text
x107.66 y114.54
y114.54 x107.66
X107.66 Y114.54
x:107.66
y:114.54
```

OCR 轻微错误：

```text
xI07.66
yI14.54
```

异常：

```text
x10766
y11454
```

必须拒绝不可安全恢复的结果。

---

# 43. Overlay 测试

检查：

- HUD 是否置顶；
- 游戏是否仍收到鼠标；
- HUD 是否抢焦点；
- Alt+Tab 行为；
- 设置窗口是否可以正常获得焦点；
- HUD Unlock 后是否可以拖动；
- Lock 后是否恢复穿透；
- 100/125/150/200% DPI；
- 多显示器。

---

# 44. 游戏显示模式

至少测试：

```text
Windowed
Borderless Windowed
Fullscreen
```

如果某种 Exclusive Fullscreen 下：

- HUD 不可见；
- 或屏幕捕获失败；

应明确记录，不通过“注入”方式绕过。

MVP 可以优先支持：

```text
Borderless Windowed
```

---

# 45. 发布

建议：

```text
win-x64
Self-contained
Single-file 可选
```

Release 目录尽量做到：

```text
MortarHUD.exe
```

或少量必要文件。

不要求用户安装 Python、Tesseract CLI 或额外运行环境。

OCR 模型如果需要：

```text
Models/
```

随程序发布。

---

# 46. Agent 实现顺序

## Phase 1 — Skeleton

完成：

```text
Solution
WPF Settings
Tray
Config
Logging
```

## Phase 2 — Overlay

完成：

```text
透明窗口
Always-on-top
Mouse click-through
位置调整
绿色默认 HUD
主题预览
```

## Phase 3 — Core

完成：

```text
Coordinate model
RNG
Bearing
Unit tests
```

## Phase 4 — Capture

完成：

```text
Global hotkeys
GetCursorPos
ROI capture
Debug ROI
```

## Phase 5 — OCR Benchmark

完成：

```text
Fixtures
Preprocessors
OCR engines
Parser
Validation
Benchmark report
```

先 Benchmark，不要一开始锁死 OCR 引擎。

## Phase 6 — Integration

连接：

```text
F6
→ Gun

F7
→ Target
→ Calculator
→ HUD
```

## Phase 7 — Polish

完成：

```text
HUD Themes
Custom colors
Fonts
Layouts
Position editor
Settings persistence
OCR Debug UI
```

---

# 47. MVP 验收条件

以下条件全部满足才能认为 v0.1 可用：

- [ ] F6 可以记录炮位；
- [ ] F7 可以记录目标；
- [ ] 地图缩放不影响坐标读取；
- [ ] 地图平移不影响坐标读取；
- [ ] 炮位箭头消失后仍可继续使用；
- [ ] RNG 计算正确；
- [ ] AZ 计算正确；
- [ ] OCR 错误不会静默使用错误数据；
- [ ] HUD 不抢游戏焦点；
- [ ] HUD 鼠标穿透；
- [ ] HUD 默认绿色文本清晰可见；
- [ ] HUD 可以拖动调整位置；
- [ ] HUD 颜色可以修改；
- [ ] HUD 字体/字号可以修改；
- [ ] HUD 支持至少 3 个内置主题；
- [ ] 用户可以保存自定义主题；
- [ ] 热键可修改；
- [ ] Debug 模式可以显示 ROI；
- [ ] Debug 模式可以查看 OCR 原图和预处理图；
- [ ] 设置重启后保持；
- [ ] Borderless Windowed 模式下正常工作。

---

# 48. 推荐默认主题

## Default Green

```text
Layout: Compact
Font: Cascadia Mono SemiBold
Font Size: 22
Primary: #7CFF6B
Background: None
Outline: Thin
Shadow: Soft
Opacity: 100%
```

显示：

```text
AZ  079.0°
RNG 152m
```

这应该成为 MortarHUD 的默认视觉语言。

---

# 49. 后续版本候选

后续可以考虑：

```text
多个游戏 Profile
不同地图比例
不同坐标系
HUD Theme Marketplace / Import
OCR 模型切换
快捷键 Profile
多目标历史
锁定多个 Target
弹道表
MIL 输出
距离修正
声音提示
自动检测游戏启动/退出
```

这些不进入当前 MVP。
