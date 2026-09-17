# MortarHUD 设置窗口视觉重做（A 方案）

日期：2026年09月17日
状态：待实现

## 背景

设置窗口在 2026年09月17日 由上一轮工作从六页重做成三页（日常使用 / 外观 / 诊断），
结构与信息组织的问题已经解决。用户反馈剩下的问题在视觉层：

> 「GUI 布局太靠边缘，太丑太简陋」

诊断结论（依据当前实际渲染截图与源码）：

1. **留白不统一**。顶部栏 `Padding="24,18"`、底部操作条 `Padding="12"`、
   内容区 `Padding="28,16,28,24"` 三套标准并存。底部「收起」按钮距窗口右边只有 12px，
   比它上面的内容区少 16px，视觉上就贴在边上。
2. **灰阶层次压平**。背景 `#151B21`、卡片 `#202932`、输入框 `#2A3540` 三层几乎同一个明度，
   卡片浮不起来，整页看着是一块平板。
3. **圆角不成体系**。卡片 3px、输入框 2px、导航选中块 2px，像是随手写的。
4. **控件基本是原生外观**。`SettingsTheme.xaml` 虽然已有 11 个控件的 `ControlTemplate`，
   但按钮、复选框、下拉、滑块仍是「原生控件加一层底色」的做工。

用户从三个视觉方向中选定 **A · 精修现有深色**：保留深色底与雷达绿强调色，
把做工做足，不改变产品气质。

## 目标与非目标

**目标**：让设置窗口看起来像一个做完的桌面软件，而不是开发中的半成品。

**非目标**：

- 不改三页结构、导航方式、页面内信息组织
- 不改任何绑定、事件、ViewModel、业务逻辑
- 不改 HUD Overlay 窗口与 Debug 面板的视觉
- 不引入第三方 UI 库或图标字体

## 设计语言

### 颜色：三层表面 + 一层凹陷

现状的问题不是配色难看，而是**层次没拉开**。改成明确的四层：

| 角色 | 值 | 用在哪 |
| --- | --- | --- |
| `SurfaceBrush` | `#141A1F` | 窗口背景、导航栏底 |
| `SurfaceRaisedBrush` | `#181F25` | 顶部栏、底部操作条 |
| `SurfaceCardBrush` | `#1B242B` | 卡片、折叠项头部 |
| `SurfaceSunkenBrush` | `#0F151A` | 输入框、下拉框、滑块轨道（凹陷） |

边框分三级（WPF 用 `#AARRGGBB`）：

| 角色 | 值 | 用在哪 |
| --- | --- | --- |
| `BorderSubtleBrush` | `#0EFFFFFF` | 卡片描边、顶底栏分隔线 |
| `BorderDefaultBrush` | `#17FFFFFF` | 输入框、按钮描边 |
| `BorderStrongBrush` | `#24FFFFFF` | 悬停态描边 |

强调色保留现有绿，文字色在现有基础上微调：

| 角色 | 值 | 用途 |
| --- | --- | --- |
| `AccentBrush` | `#7ECDB8` | 主按钮底、区块标题、导航选中文字、焦点描边 |
| `AccentSoftBrush` | `#227ECDB8` | 导航选中底、复选框选中底 |
| `TextBrush` | `#E6EDF3` | 正文、标题（原 `#E4EAF0`，略提亮） |
| `MutedTextBrush` | `#A4B4C2` | 标签、次要说明（不变） |
| `SubtleTextBrush` | `#6B7C8A` | 提示文字、禁用态（新增） |

**token 迁移方式**（避免实现时产生歧义）：

- `SurfaceBrush`、`BorderBrush`、`AccentBrush`、`TextBrush`、`MutedTextBrush` **保留键名，改值**
- `SurfaceAltBrush`（`#202932`）**废弃**：它现在同时被用在底部栏和卡片上，正是层次压平的来源。
  底部栏改用新增的 `SurfaceRaisedBrush`，卡片改用新增的 `SurfaceCardBrush`
- 新增：`SurfaceRaisedBrush`、`SurfaceCardBrush`、`SurfaceSunkenBrush`、
  `BorderSubtleBrush`、`BorderDefaultBrush`、`BorderStrongBrush`、`AccentSoftBrush`、`SubtleTextBrush`
- 旧颜色键（`SurfaceColor` / `SurfaceAltColor` / `BorderColor` / `AccentColor`）在迁移完成后删除，
  避免同一语义两套定义并存

### 间距：一套阶梯

只用 `4 / 8 / 12 / 16 / 22 / 26 / 32` 这几个值。

| 位置 | 值 |
| --- | --- |
| 顶部栏内边距 | `26,16` |
| 底部操作条内边距 | `26,14` |
| 内容区滚动容器内边距 | `26,22,26,26` |
| 卡片内边距 | `22` |
| 卡片之间 | `20` |
| 控件行之间 | `10` |
| 区块之间（标题到内容） | `24` |

**关键约束**：顶部栏、底部栏、内容区三者左右内边距必须相同（26px），
这样按钮、卡片、标题在竖直方向对齐成一条线。这正是现在最刺眼的地方。

### 圆角

| 角色 | 值 |
| --- | --- |
| 卡片、HUD 预览框 | `12` |
| 输入框、按钮、导航项、折叠项头部 | `8` |
| 复选框、小色块 | `5` |
| 滑块轨道、进度条 | `2` |

### 字号

基准保持窗口的 `FontSize=13`（Microsoft YaHei UI）。

| 角色 | 值 |
| --- | --- |
| 页面标题 `PageTitle` | `22px` SemiBold |
| 卡片标题 `SectionHeader` | `12px` Bold，字间距 `0.7`，强调色，全大写不适用中文故不加 |
| 正文与标签 | `13px` Regular |
| 辅助说明 `Hint` | `12px`，`MutedTextBrush` |
| 键位显示 | `13px`，等宽优先级：`Consolas, Microsoft YaHei UI` |

顶部栏标题从 `20px` 收到 `12px`，副标题 `11px`——它不该和页面标题抢视觉重心。

## 控件规格

以下控件需要重写或调整 `ControlTemplate`。已有的模板重调参数，缺的补上。

### TextBox（凹陷）

背景 `SurfaceSunkenBrush`，1px `BorderDefaultBrush` 描边，圆角 8，内边距 `8,10`。
文字 `TextBrush`。焦点态：描边换 `AccentBrush`；只读的热键框悬停时描边换 `BorderStrongBrush`，
光标保持 `Hand`。禁用态整体降到 40% 不透明度。

### CheckBox

方框 `18×18`，圆角 5，背景 `SurfaceSunkenBrush`，描边 `BorderDefaultBrush`。
选中：填充 `AccentSoftBrush`，描边 `AccentBrush`，勾为 `AccentBrush` 的 `Path`。
悬停：描边 `BorderStrongBrush`。标签与方框间距 `10`，整体点击区含标签。

### ComboBox

外观与 TextBox 一致（凹陷 + 圆角 8），右侧放一个 `AccentBrush` 的细箭头
（现在是默认三角）。下拉弹层背景 `SurfaceRaisedBrush`，圆角 8，
`DropShadowEffect` 柔和投影，列表项高度 32，选中项用 `AccentSoftBrush` 底色。

### Button

普通按钮：背景 `SurfaceRaisedBrush`，1px `BorderDefaultBrush`，圆角 8，内边距 `8,16`，
文字 `MutedTextBrush`。悬停背景提亮到 `#202932`，按下再降一档。
`PrimaryButton`：背景 `AccentBrush`，文字用深色 `#0F1F1B`，SemiBold。

### Slider

轨道高 4，圆角 2，背景 `SurfaceSunkenBrush`；已填充部分 `AccentBrush`。
滑块 `Thumb` 为 16px 圆，填充 `TextBrush`，带 1px 深色描边。悬停时滑块放大到 18px。

### Expander

头部：背景 `SurfaceRaisedBrush`，圆角 8，内边距 `14,12`，整行可点。
左侧一个 6×6 的三角箭头（`AccentBrush`），展开时旋转 90°。
悬停：背景提亮。展开后内容区左内边距 `16`、上内边距 `14`，背景与页面背景一致
（不做一个大色块，避免和卡片打架）。

### TabItem（左侧导航）

项内边距 `10,14`，圆角 8，项之间 `3`。
选中：背景 `AccentSoftBrush`，文字 `AccentBrush` + SemiBold。
悬停：背景 `#0EFFFFFF`，文字提亮。导航容器左右内边距 `14`，上内边距 `18`，宽度约 `168`。

### Card

背景 `SurfaceCardBrush`，1px `BorderSubtleBrush`，圆角 12，内边距 `22`。

## 布局调整

### 顶部栏

压缩到约 `64px` 高（现在约 92px）：图标 22px、标题 12px、副标题 11px。
**删掉右上角的「离线坐标辅助」**——它在设置窗口里没有信息量，且是当前右侧留白失衡的原因之一。

### 底部操作条

内边距统一到 `26,14`，与内容区对齐。「设置在点击应用后生效。」保持左对齐，
按钮组右对齐。按钮之间间距 `8`。

### 内容区

滚动容器内边距 `26,22,26,26`。

**外观页的 HUD 预览卡**：右列预览框改为自适应高度填满右列可用空间，
底部的「示例坐标·保存后应用到游戏」居中显示在框内底部。
当前预览框下方的大片空白是这一页最明显的失衡。

## 实现范围

| 文件 | 改动 |
| --- | --- |
| `src/MortarHUD.App/Themes/SettingsTheme.xaml` | 主要工作：颜色 token、间距、圆角、全部控件模板 |
| `src/MortarHUD.App/Views/SettingsWindow.xaml` | 顶部栏/底部栏/内容区的内边距与结构微调、预览卡高度 |

不改任何 `.cs` 文件。不改任何绑定表达式。

## 风险与约束

**`SettingsTheme.xaml` 是全局资源字典**（由 `App.xaml` 引用），其中的隐式样式
（`<Style TargetType="Button">` 这类没有 `x:Key` 的）会影响所有窗口。

已经确认：`OverlayWindow.xaml` 与 `DebugOverlayWindow.xaml` 都不引用任何具名样式。
Overlay 是纯绘制，不受影响。Debug 面板需要在实现后跑一次 `--screenshot` 或自检确认外观未劣化；
如果受影响，把设置窗口专用样式下沉到 `SettingsWindow.xaml` 的 `Window.Resources`。

**`ControlTemplate` 不能破坏现有交互**：热键输入框依赖 `PreviewKeyDown` / `PreviewMouseDown` /
`GotFocus`，重写 TextBox 模板时必须保留 `PART_ContentHost`；`TabControl` 的 `SelectionChanged`
依赖 `TabItem` 的 `IsSelected` 触发器，重写模板时不能去掉。

**滚轮与键盘**：`ScrollViewer`、`Expander` 的默认行为不能因为模板重写而丢失。

## 验证方式

1. `"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release` —— 0 警告 0 错误
2. `"C:/dotnet10/dotnet.exe" test MortarHUD.sln -c Release` —— 202 项全绿
3. `MortarHUD.exe --screenshot <目录>` —— 生成三页截图，逐页核对留白、层次、对齐
4. `MortarHUD.exe --selftest` —— 退出码 0，确认窗口构造未因模板改动而失败
5. 人工确认 Debug 面板外观未被全局样式劣化（托盘菜单是 WinForms 的，不受影响）
6. 对照本规格的间距表，检查顶部栏、底部栏、内容区左右内边距是否一致

**截图验证的盲区**：`--screenshot` 只渲染初始状态，折叠项是收起的，
所以**展开态、悬停态、焦点态、下拉弹层在这条路径上看不到**。
这几类状态必须在真实窗口里人工过一遍——尤其折叠项展开后的排版、
热键输入框的焦点描边、下拉框弹层有没有被裁掉。

## 未纳入本次范围

- 窗口自定义标题栏与圆角（需要 `WindowChrome`，改动大且影响拖动/最大化行为）
- 窗口最大化时内容最大宽度约束
- 浅色主题 / 跟随系统主题
- Debug 面板与 HUD Overlay 的视觉调整
