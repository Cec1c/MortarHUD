# MortarHUD 设置窗口视觉重做 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把设置窗口从「开发中的半成品」改造成做完的桌面软件外观，保留全部结构与功能。

**Architecture:** 改动集中在两个 XAML 文件。`SettingsTheme.xaml` 是全局资源字典（由 `App.xaml` 引用），承载颜色 token 与控件模板；`SettingsWindow.xaml` 承载布局与间距。不改任何 `.cs`、任何绑定、任何页面结构。

**Tech Stack:** WPF / XAML、.NET 10、Microsoft YaHei UI。无新增依赖、无第三方 UI 库。

**规格来源：** `docs/superpowers/specs/2026-09-17-settings-gui-design.md`

## Global Constraints

- 构建：`"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release` —— 裸 `dotnet` 会解析到 .NET 6
- 测试：`"C:/dotnet10/dotnet.exe" test MortarHUD.sln -c Release` —— 必须 202 项全绿
- 自检：在程序目录下运行 `MortarHUD.exe --selftest`，退出码必须为 `0`
- 截图：在程序目录下运行 `MortarHUD.exe --screenshot <目录>`
- 间距只用 `4 / 8 / 12 / 16 / 22 / 26 / 32`
- 圆角：卡片 `12`、控件 `8`、小件 `5`、轨道 `2`
- 顶部栏、底部栏、内容区左右内边距必须都是 `26`
- 不改任何 `.cs` 文件、任何绑定表达式、任何页面结构、任何界面文案（规格中点名要删的「离线坐标辅助」除外）
- 注释写「为什么」，不写「是什么」；全中文

## 关于验证方式

XAML 没有单元测试，本计划不采用「先写失败的测试」的 TDD 循环。替代的验证阶梯：

1. **构建** 捕获 XAML 语法错误
2. **`--selftest`** 构造全部三个窗口 + 跑一次端到端识别。**引用不存在的资源键、模板写坏、触发器语法错都会在这一步抛异常**，这是本计划的主力回归防线
3. **`--screenshot`** 生成三页 PNG，人工核对留白、层次、对齐
4. **`dotnet test`** 确认没有碰到业务代码

每个任务都必须跑完 1、2 才能提交；3 用于视觉确认。

**验证用开发构建，不要每次重新发布。** `dotnet build` 的产物是框架依赖版本，
本机的 .NET 10 装在 `C:\dotnet10`（非标准位置），需要把 `DOTNET_ROOT` 指过去才能启动：

```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest)
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --screenshot "../../../../../_analysis/ui-<任务名>")
```

`--screenshot` 的输出路径按**当前工作目录**解析，所以要先 `cd` 到程序目录，
再用五层 `../` 回到仓库根。这条路径的渲染结果与发布产物一致——已比对过，
三张 PNG 的字节数完全相同（70297 / 51648 / 46305）。

**只有 Task 6 需要真正发布一次**，因为那一步要验证的是发布产物本身。

## 已知的验证盲区

`--screenshot` 只渲染初始状态，折叠项是收起的。**展开态、悬停态、焦点态、下拉弹层在这条路径上看不到**，必须在真实窗口里人工过一遍（计划最后一个任务负责这件事）。

---

### Task 1: 颜色 token 体系

这是所有后续任务的基础。完成后界面会立刻显出层次，但间距和控件形状还没变——这是预期的中间状态。

**Files:**
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:9-18`
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:106,120,136,169,248,262,272,299`
- Modify: `src/MortarHUD.App/Views/SettingsWindow.xaml:22`

**Interfaces:**
- Produces: 后续任务使用的全部颜色资源键 ——
  `SurfaceBrush` `SurfaceRaisedBrush` `SurfaceCardBrush` `SurfaceSunkenBrush`
  `BorderSubtleBrush` `BorderBrush` `BorderStrongBrush`
  `AccentBrush` `AccentSoftBrush`
  `TextBrush` `MutedTextBrush` `SubtleTextBrush`
- Consumes: 无

**与规格的一处偏差（有意）**：规格表格里列了 `BorderDefaultBrush`，但同一节又要求「`BorderBrush` 保留键名，改值」。
两者是同一个语义。为避免同义键并存，**保留 `BorderBrush` 作为默认边框，不新增 `BorderDefaultBrush`**。
因此新增键是 7 个而不是 8 个。

- [ ] **Step 1: 替换颜色定义块**

把 `SettingsTheme.xaml` 第 9–18 行整块替换为：

```xml
  <!--
        表面分四层：窗口底 < 顶底栏 < 卡片，输入控件反过来做成凹陷。
        改动前这几层几乎同一个明度（#151B21 / #202932 / #2A3540），卡片浮不起来，
        整页看着是一块平板 —— 这是「简陋」最主要的来源。

        边框改用半透明白而不是实色（原来是 #34434F）：实色边框在深色底上会
        形成一圈硬边，半透明则随底色自然衰减，层次更干净。
    -->
  <SolidColorBrush x:Key="SurfaceBrush" Color="#141A1F"/>
  <SolidColorBrush x:Key="SurfaceRaisedBrush" Color="#181F25"/>
  <SolidColorBrush x:Key="SurfaceCardBrush" Color="#1B242B"/>
  <SolidColorBrush x:Key="SurfaceSunkenBrush" Color="#0F151A"/>

  <SolidColorBrush x:Key="BorderSubtleBrush" Color="#0EFFFFFF"/>
  <SolidColorBrush x:Key="BorderBrush" Color="#17FFFFFF"/>
  <SolidColorBrush x:Key="BorderStrongBrush" Color="#24FFFFFF"/>

  <SolidColorBrush x:Key="AccentBrush" Color="#7ECDB8"/>
  <SolidColorBrush x:Key="AccentSoftBrush" Color="#227ECDB8"/>

  <SolidColorBrush x:Key="TextBrush" Color="#E6EDF3"/>
  <SolidColorBrush x:Key="MutedTextBrush" Color="#A4B4C2"/>
  <SolidColorBrush x:Key="SubtleTextBrush" Color="#6B7C8A"/>
```

说明：原来的 `SurfaceColor` / `SurfaceAltColor` / `BorderColor` / `AccentColor` 四个 `<Color>` 键只在
本文件内部被引用（已全仓确认），直接删除，改为在 Brush 上写死值——少一层间接引用。

- [ ] **Step 2: 替换 8 处 `SurfaceAltBrush` 引用**

`SurfaceAltBrush` 同时被用在底部栏和卡片上，正是层次压平的来源，按用途拆开。
**这个键在本步骤后不再存在**，漏改任何一处都会在自检时报资源找不到。

| 行 | 位置 | 改为 |
| --- | --- | --- |
| 106 | ComboBox 的 ToggleButton 底 | `{StaticResource SurfaceSunkenBrush}` |
| 120 | ComboBox 的 Popup 弹层底 | `{StaticResource SurfaceRaisedBrush}` |
| 136 | Button 的 `Background` Setter | `{StaticResource SurfaceRaisedBrush}` |
| 169 | ToggleButton 的 Chrome 底 | `{StaticResource SurfaceRaisedBrush}` |
| 248 | CheckBox 悬停时 Box 底 | `{StaticResource SurfaceRaisedBrush}` |
| 262 | TextBox 的 `Background` Setter | `{StaticResource SurfaceSunkenBrush}` |
| 272 | ListBox 的 `Background` Setter | `{StaticResource SurfaceSunkenBrush}` |
| 299 | Card 的 `Background` Setter | `{StaticResource SurfaceCardBrush}` |

- [ ] **Step 3: 替换模板里的硬编码颜色**

| 行 | 原值 | 改为 |
| --- | --- | --- |
| 40 | TabItem 选中底 `#293C3A` | `{StaticResource AccentSoftBrush}` |
| 59 | TabControl 导航栏底 `#11171C` | `{StaticResource SurfaceBrush}` |
| 80 | ComboBoxItem 高亮底 `#3A4A38` | `{StaticResource AccentSoftBrush}` |
| 197 | Slider Thumb 描边 `#14161A` | `{StaticResource SurfaceSunkenBrush}` |
| 305 | PrimaryButton 前景 `#102922` | `#0F1F1B` |

- [ ] **Step 4: 替换 SettingsWindow.xaml 的底部栏背景**

第 22 行，`Background="{StaticResource SurfaceAltBrush}"` 改为 `Background="{StaticResource SurfaceRaisedBrush}"`。

- [ ] **Step 5: 确认没有残留引用**

Run:
```bash
grep -rn "SurfaceAltBrush\|SurfaceAltColor\|SurfaceColor\|BorderColor\|AccentColor" src/ --include="*.xaml" | grep -v "/obj/"
```
Expected: 无输出。`SurfaceColor`、`BorderColor`、`AccentColor` 各自是独立字符串，
不会与保留下来的 `SurfaceBrush`、`BorderBrush`、`AccentBrush` 互相匹配。

- [ ] **Step 6: 构建并自检**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest; echo "退出码: $?")
```
Expected: `0 个警告 0 个错误`；全部 `[ OK ]` 后输出 `自检通过。`，退出码 `0`

**资源键缺失会在「构造设置窗口」这一步抛异常并显示 `[FAIL]`** —— 这是本任务的主要风险点，
因为 `SurfaceAltBrush` 在这一步之后就不存在了，漏改任何一处引用都会在这里暴露。

- [ ] **Step 7: 截图确认层次**

Run:
```bash
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --screenshot "../../../../../_analysis/ui-task1")
```

Expected: 卡片比窗口背景亮一档、输入框比卡片暗一档，三层肉眼可辨

- [ ] **Step 8: 提交**

```bash
git add src/MortarHUD.App/Themes/SettingsTheme.xaml src/MortarHUD.App/Views/SettingsWindow.xaml
git commit -m "style: 设置窗口改为四层表面 + 三级半透明边框的配色体系"
```

---

### Task 2: 布局与间距

**Files:**
- Modify: `src/MortarHUD.App/Views/SettingsWindow.xaml:9-20`（顶部栏）
- Modify: `src/MortarHUD.App/Views/SettingsWindow.xaml:22`（底部栏）
- Modify: `src/MortarHUD.App/Views/SettingsWindow.xaml:37`（内容区）
- Modify: `src/MortarHUD.App/Views/SettingsWindow.xaml:313-323`（外观页预览卡）

**Interfaces:**
- Consumes: Task 1 的颜色键
- Produces: 无（叶子任务）

- [ ] **Step 1: 顶部栏压缩并对齐**

把第 9–20 行整块替换为：

```xml
    <Border DockPanel.Dock="Top" Background="{StaticResource SurfaceRaisedBrush}" Padding="26,16" BorderBrush="{StaticResource BorderSubtleBrush}" BorderThickness="0,0,0,1">
      <StackPanel Orientation="Horizontal">
        <Image Source="/Assets/MortarHUD.png" Width="22" Height="22" Margin="0,0,10,0"/>
        <StackPanel VerticalAlignment="Center">
          <TextBlock Text="MortarHUD" FontSize="12" FontWeight="SemiBold"/>
          <TextBlock Text="迫击炮坐标解算" Foreground="{StaticResource SubtleTextBrush}" FontSize="11"/>
        </StackPanel>
      </StackPanel>
    </Border>
```

删掉的是右上角的「离线坐标辅助」：它在设置窗口里没有信息量，而且是用 `HorizontalAlignment` 顶到最右边，
是右侧留白失衡的来源之一。同时标题从 20px 收到 12px、图标 32→22px，整条栏从约 92px 降到约 64px。

补上 `Background="{StaticResource SurfaceRaisedBrush}"` 是必要的：顶栏原本没有背景，
而 Task 1 已经给底栏设了 `SurfaceRaisedBrush`，不补的话两条栏不对称，
`SettingsTheme.xaml` 里注释承诺的「顶底栏同层」也就落空了。

- [ ] **Step 2: 底部栏对齐到同一栅格**

第 22 行改为：

```xml
    <Border DockPanel.Dock="Bottom" Background="{StaticResource SurfaceRaisedBrush}" BorderBrush="{StaticResource BorderSubtleBrush}" BorderThickness="0,1,0,0" Padding="26,14">
```

原来 `Padding="12"`，比内容区少 16px，导致「收起」按钮比它上面的内容更贴右边——
这是用户说的「太靠边缘」最直接的来源。

- [ ] **Step 3: 内容区对齐到同一栅格**

第 37 行改为：

```xml
    <TabControl x:Name="Tabs" Background="{StaticResource SurfaceBrush}" BorderThickness="0" Padding="26,22,26,26" TabStripPlacement="Left">
```

- [ ] **Step 4: 外观页预览卡**

把第 313–323 行整块替换为：

```xml
          <DockPanel Grid.Column="2">
            <TextBlock DockPanel.Dock="Top" Text="HUD 预览" Style="{StaticResource SectionHeader}"/>
            <Border Background="#0F151A" BorderBrush="{StaticResource BorderSubtleBrush}" BorderThickness="1" CornerRadius="12">
              <Grid>
                <TextBlock Text="示例坐标 · 保存后应用到游戏" Foreground="{StaticResource SubtleTextBrush}" HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="16" FontSize="11"/>
                <views:HudRenderer x:Name="PreviewRenderer" Margin="10" VerticalAlignment="Center" HorizontalAlignment="Left"/>
              </Grid>
            </Border>
          </DockPanel>
```

三处改动：删掉第 315–316 行那个**空的占位 `Border`**（它不承载任何内容，还带一个 `Padding="8"`）；
预览底从纯灰 `#141414` 换成与输入框同一层的凹陷色，圆角统一到 12；
`HudRenderer` 的对齐从 `Top` 改为 `Center`——预览框是撑满右列的，HUD 内容贴在顶部会让下方留出大片空洞。

- [ ] **Step 5: 构建并自检**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest; echo "退出码: $?")
```
Expected: 构建 `0 个警告 0 个错误`；自检 `自检通过。`；退出码 `0`

- [ ] **Step 6: 截图核对对齐**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --screenshot "../../../../../_analysis/ui-task2")
```

核对三点：顶部栏左侧的图标、内容区左侧的卡片、底部栏左侧的说明文字**左边缘在同一条竖直线上**；
右侧的按钮组与卡片右边缘同样对齐。

- [ ] **Step 7: 提交**

```bash
git add src/MortarHUD.App/Views/SettingsWindow.xaml
git commit -m "style: 顶栏/底栏/内容区统一到 26px 栅格，压缩顶栏并去掉无用占位"
```

---

### Task 3: 输入类控件模板

**Files:**
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:261-270`（TextBox）
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:224-257`（CheckBox）
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:91-130`（ComboBox）

**Interfaces:**
- Consumes: Task 1 的颜色键
- Produces: 无（叶子任务）

- [ ] **Step 1: 重写 TextBox 模板**

TextBox 现在没有 `ControlTemplate`，靠默认模板 + 设置 `Background`/`BorderBrush`。
默认模板**不支持 `CornerRadius`**，所以圆角必须自己写模板才能拿到。

把第 261–270 行整块替换为：

```xml
  <Style TargetType="TextBox">
    <Setter Property="Background" Value="{StaticResource SurfaceSunkenBrush}"/>
    <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
    <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}"/>
    <Setter Property="Padding" Value="10,8"/>
    <Setter Property="MinHeight" Value="34"/>
    <Setter Property="Margin" Value="0,3"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="CaretBrush" Value="{StaticResource AccentBrush}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Border x:Name="Chrome"
                  Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1"
                  CornerRadius="8">
            <!-- PART_ContentHost 是 TextBox 按名字查找的宿主，不能改名也不能换类型。 -->
            <ScrollViewer x:Name="PART_ContentHost"
                          Margin="{TemplateBinding Padding}"
                          VerticalAlignment="Center"
                          Focusable="False"
                          HorizontalScrollBarVisibility="Hidden"
                          VerticalScrollBarVisibility="Hidden"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Chrome" Property="BorderBrush" Value="{StaticResource BorderStrongBrush}"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Chrome" Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="Chrome" Property="Opacity" Value="0.45"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

热键输入框依赖 `PreviewKeyDown` / `PreviewMouseDown` / `GotFocus`，这些是控件级事件，不受模板影响；
但 `Cursor="Hand"` 是控件属性，模板里的 `Border` 不会自动继承，所以**不要**在模板里写死 Cursor。

- [ ] **Step 2: 构建并自检**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest; echo "退出码: $?")
```
Expected: `0 个警告 0 个错误`；`自检通过。`；退出码 `0`

- [ ] **Step 3: 重写 CheckBox 模板**

把第 230–254 行（`<ControlTemplate TargetType="CheckBox">` 整块）替换为：

```xml
        <ControlTemplate TargetType="CheckBox">
          <Border x:Name="Focus" BorderThickness="1" BorderBrush="Transparent" CornerRadius="6" Padding="2">
            <DockPanel Background="Transparent">
              <Border x:Name="Box" Width="18" Height="18" CornerRadius="5"
                      Background="{StaticResource SurfaceSunkenBrush}"
                      BorderThickness="1" BorderBrush="{StaticResource BorderBrush}"
                      Margin="0,0,10,0" VerticalAlignment="Center">
                <Path x:Name="Check" Visibility="Collapsed" Data="M 4,9 L 7,12 L 13,5"
                      Stroke="{StaticResource AccentBrush}" StrokeThickness="2"
                      StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
              </Border>
              <ContentPresenter VerticalAlignment="Center" RecognizesAccessKey="True" TextElement.Foreground="{TemplateBinding Foreground}"/>
            </DockPanel>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Check" Property="Visibility" Value="Visible"/>
              <Setter TargetName="Box" Property="Background" Value="{StaticResource AccentSoftBrush}"/>
              <Setter TargetName="Box" Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Box" Property="BorderBrush" Value="{StaticResource BorderStrongBrush}"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Focus" Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="Focus" Property="Opacity" Value="0.45"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
```

变化：方框 17→18px、圆角 3→5、未选态有凹陷底色（原来是透明）、选中态加 `AccentSoftBrush` 填充、
勾的路径加圆角端点、标签间距 9→10。

- [ ] **Step 4: 构建并自检**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest; echo "退出码: $?")
```
Expected: `0 个警告 0 个错误`；`自检通过。`；退出码 `0`

- [ ] **Step 5: 重写 ComboBox 模板**

第 106 行的 ToggleButton 模板与第 120 行的 Popup 边框改成：圆角 3→8，箭头从实心三角换成细描边箭头，
弹层加柔和投影。

把第 104–115 行（`<ToggleButton.Template>` 整块）替换为：

```xml
              <ToggleButton.Template>
                <ControlTemplate TargetType="ToggleButton">
                  <Border x:Name="Chrome" Background="{StaticResource SurfaceSunkenBrush}" BorderBrush="{StaticResource BorderBrush}" BorderThickness="1" CornerRadius="8">
                    <Path HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0"
                          Data="M 0,0 L 5,5 L 10,0" Stroke="{StaticResource MutedTextBrush}" StrokeThickness="1.6"
                          StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Chrome" Property="BorderBrush" Value="{StaticResource BorderStrongBrush}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
```

第 117 行的 `ContentPresenter` `Margin` 从 `9,5,26,5` 改为 `12,9,32,9`（适配更大的箭头与内边距）。

第 120 行的 Popup 边框替换为：

```xml
              <Border MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}" Background="{StaticResource SurfaceRaisedBrush}" BorderBrush="{StaticResource BorderSubtleBrush}" BorderThickness="1" CornerRadius="8" Margin="0,4,0,0">
                <Border.Effect>
                  <DropShadowEffect BlurRadius="16" ShadowDepth="4" Direction="270" Opacity="0.5" Color="#000000"/>
                </Border.Effect>
```

- [ ] **Step 6: 构建、自检、截图**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest; echo "退出码: $?")
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --screenshot "../../../../../_analysis/ui-task3")
```
Expected: 构建无警告无错误；自检通过退出码 0；截图里输入框是凹陷圆角、复选框是圆角方框

- [ ] **Step 7: 提交**

```bash
git add src/MortarHUD.App/Themes/SettingsTheme.xaml
git commit -m "style: 重写输入框/复选框/下拉框模板，输入类控件改为凹陷圆角"
```

---

### Task 4: 操作类控件模板

**Files:**
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:132-163`（Button）
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:164-183`（ToggleButton）
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:185-207`（Slider）
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:308-333`（Expander）

**Interfaces:**
- Consumes: Task 1 的颜色键
- Produces: 无（叶子任务）

- [ ] **Step 1: 重写 Button 模板**

把第 132–163 行整块替换为：

```xml
  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource MutedTextBrush}"/>
    <Setter Property="Padding" Value="14,8"/>
    <Setter Property="MinHeight" Value="36"/>
    <Setter Property="Background" Value="{StaticResource SurfaceRaisedBrush}"/>
    <Setter Property="Margin" Value="0,0,8,0"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="Chrome" Background="{TemplateBinding Background}" BorderBrush="{StaticResource BorderBrush}" BorderThickness="1" CornerRadius="8" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Chrome" Property="Background" Value="{StaticResource SurfaceCardBrush}"/>
              <Setter TargetName="Chrome" Property="BorderBrush" Value="{StaticResource BorderStrongBrush}"/>
              <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Chrome" Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Chrome" Property="Opacity" Value="0.7"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="Chrome" Property="Opacity" Value="0.45"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

WPF 的 `Padding` 顺序是「左,上,右,下」，所以 `14,8` 是左右 14、上下 8 的宽扁按钮。
保持这个比例不变，只把 `MinHeight` 从 34 提到 36，让按钮在底栏里更稳。

悬停态从「边框变强调色 + 文字变强调色」改为「背景提亮一档 + 文字提亮」：
强调色留给主按钮，普通按钮悬停时不该抢视觉。原来那版会让底栏三个按钮同时亮成绿色。

- [ ] **Step 2: 构建并自检**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest; echo "退出码: $?")
```
Expected: `0 个警告 0 个错误`；`自检通过。`；退出码 `0`

- [ ] **Step 3: 重写 ToggleButton 模板**

把第 166–182 行（`<ControlTemplate TargetType="ToggleButton">` 整块）替换为：

```xml
        <ControlTemplate TargetType="ToggleButton">
          <Border x:Name="Chrome" Background="{StaticResource SurfaceRaisedBrush}" BorderBrush="{StaticResource BorderBrush}" BorderThickness="1" CornerRadius="8" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" TextElement.Foreground="{StaticResource TextBrush}"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Chrome" Property="Background" Value="{StaticResource SurfaceCardBrush}"/>
              <Setter TargetName="Chrome" Property="BorderBrush" Value="{StaticResource BorderStrongBrush}"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Chrome" Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Chrome" Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="Chrome" Property="Opacity" Value="0.45"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
```

（原来的第 176 行把三个触发器挤在一行里，这里拆开以便阅读；行为不变。）

- [ ] **Step 4: 重写 Slider 模板**

`Track` 的 `DecreaseRepeatButton` / `IncreaseRepeatButton` 是 WPF 用来画「已填充 / 未填充」两段的
标准机制，用它就不需要额外的宽度转换器。整个替换第 185–207 行：

```xml
  <Style TargetType="Slider">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Slider">
          <Grid VerticalAlignment="Center" MinHeight="22">
            <!-- PART_Track 是 Slider 内部按名字查找的，不能改名。 -->
            <Track x:Name="PART_Track">
              <Track.DecreaseRepeatButton>
                <RepeatButton Command="Slider.DecreaseLarge" Focusable="False">
                  <RepeatButton.Template>
                    <ControlTemplate TargetType="RepeatButton">
                      <Border Height="4" CornerRadius="2" Background="{StaticResource AccentBrush}" VerticalAlignment="Center"/>
                    </ControlTemplate>
                  </RepeatButton.Template>
                </RepeatButton>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command="Slider.IncreaseLarge" Focusable="False">
                  <RepeatButton.Template>
                    <ControlTemplate TargetType="RepeatButton">
                      <Border Height="4" CornerRadius="2" Background="{StaticResource SurfaceSunkenBrush}" VerticalAlignment="Center"/>
                    </ControlTemplate>
                  </RepeatButton.Template>
                </RepeatButton>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb Width="16" Height="16" Cursor="Hand">
                  <Thumb.Template>
                    <ControlTemplate TargetType="Thumb">
                      <Ellipse Fill="{StaticResource TextBrush}" Stroke="{StaticResource SurfaceSunkenBrush}" StrokeThickness="2"/>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

变化：轨道从单一灰底改为「已填充强调色 + 未填充凹陷色」，滑块从 14px 绿色圆改为 16px 浅色圆
（绿色留给填充段，滑块用浅色才不会和填充段糊在一起）。

- [ ] **Step 5: 重写 Expander 模板**

把第 313–330 行（`<ControlTemplate TargetType="Expander">` 整块）替换为：

```xml
        <ControlTemplate TargetType="Expander">
          <StackPanel>
            <ToggleButton x:Name="Header" IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}}" HorizontalContentAlignment="Stretch" Padding="14,12">
              <DockPanel>
                <Path x:Name="Disclosure" DockPanel.Dock="Left" Data="M 0,0 L 4,4 L 0,8" Stroke="{StaticResource AccentBrush}" StrokeThickness="1.6" Width="8" Height="8" RenderTransformOrigin="0.5,0.5" VerticalAlignment="Center" Margin="0,0,10,0"/>
                <ContentPresenter Content="{TemplateBinding Header}" VerticalAlignment="Center"/>
              </DockPanel>
            </ToggleButton>
            <Border x:Name="Body" Visibility="Collapsed" Padding="16,14,8,12">
              <ContentPresenter/>
            </Border>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsExpanded" Value="True">
              <Setter TargetName="Body" Property="Visibility" Value="Visible"/>
              <Setter TargetName="Disclosure" Property="RenderTransform">
                <Setter.Value><RotateTransform Angle="90"/></Setter.Value>
              </Setter>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
```

变化：箭头从右侧移到左侧（折叠列表的习惯位置）、颜色从灰改为强调色、头部内边距 12,10 → 14,12、
内容区左内边距 4 → 16（和头部文字对齐）。

- [ ] **Step 6: 构建、自检、截图**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest; echo "退出码: $?")
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --screenshot "../../../../../_analysis/ui-task4")
```
Expected: 构建无警告无错误；自检通过退出码 0；截图里折叠项头部是圆角块、滑块有强调色填充段

- [ ] **Step 7: 提交**

```bash
git add src/MortarHUD.App/Themes/SettingsTheme.xaml
git commit -m "style: 重写按钮/开关/滑块/折叠项模板"
```

---

### Task 5: 导航、卡片与文本样式

**Files:**
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:30-53`（TabItem）
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:54-69`（TabControl）
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:212-223`（SectionHeader / Hint）
- Modify: `src/MortarHUD.App/Themes/SettingsTheme.xaml:290-302`（PageTitle / Card）

**Interfaces:**
- Consumes: Task 1 的颜色键
- Produces: 无（叶子任务）

- [ ] **Step 1: 调整 TabItem 导航项**

把第 35 行的 `Chrome` 替换为：

```xml
          <Border x:Name="Chrome" Padding="10,14" Margin="0,0,0,3" CornerRadius="8" Background="Transparent" BorderThickness="0" BorderBrush="Transparent">
```

把第 43–48 行的两个触发器（`IsMouseOver` 与 `IsKeyboardFocused`）一起替换为：

```xml
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Chrome" Property="Background" Value="#0EFFFFFF"/>
              <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
            </Trigger>
```

原来的 `IsKeyboardFocused` 会画一圈强调色边框，和选中态的圆角块打架。
改成只提亮文字——**键盘焦点的可见指示必须保留**，键盘用户要靠它知道光标在哪一项上。

第 40 行的选中底色已经是 `AccentSoftBrush`（Task 1 改过），无需再动。

- [ ] **Step 2: 调整 TabControl 导航容器**

把第 59 行的导航栏 `Border` 替换为：

```xml
            <Border DockPanel.Dock="Left" Width="168" Background="{StaticResource SurfaceBrush}" Padding="14,18,14,0">
```

去掉了原来的 `BorderThickness="0,0,1,0"`：导航栏与内容区之间已经有背景色差异和 26px 的
内容区左内边距，再画一条竖分隔线是多余的。

- [ ] **Step 3: 调整文本样式**

把第 212–223 行（`SectionHeader` 与 `Hint` 两个样式）替换为：

```xml
  <Style x:Key="SectionHeader" TargetType="TextBlock">
    <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="Margin" Value="0,16,0,10"/>
  </Style>
  <Style x:Key="Hint" TargetType="TextBlock">
    <Setter Property="Foreground" Value="{StaticResource SubtleTextBrush}"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="TextWrapping" Value="Wrap"/>
    <Setter Property="Margin" Value="0,2,0,0"/>
  </Style>
```

`SectionHeader` 只改字号（14→12）与字重（SemiBold→Bold），**`Margin` 保持 `0,16,0,10` 不动**。
这个样式同时用在卡片内（如「操作按键」）和页面级区块（如「主题预设」）：
卡片内靠它撑出与卡片顶边的距离，页面级靠它分隔区块。把上边距归零会让页面级区块标题
紧贴上一行说明文字，很不舒服——不要动它。

- [ ] **Step 4: 调整页面标题与卡片**

把第 290–302 行（`PageTitle` 与 `Card`）替换为：

```xml
  <Style x:Key="PageTitle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="22"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
    <Setter Property="Margin" Value="0,0,0,6"/>
  </Style>
  <Style x:Key="Card" TargetType="Border">
    <Setter Property="Padding" Value="22"/>
    <Setter Property="CornerRadius" Value="12"/>
    <Setter Property="Background" Value="{StaticResource SurfaceCardBrush}"/>
    <Setter Property="BorderBrush" Value="{StaticResource BorderSubtleBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
  </Style>
```

变化：`PageTitle` 24→22px、下边距 10→6；`Card` 内边距 18→22、圆角 8→12、改用卡片层底色与最淡的边框。

- [ ] **Step 5: 构建、自检、截图**

Run:
```bash
DEV="src/MortarHUD.App/bin/Release/net10.0-windows"
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --selftest; echo "退出码: $?")
(cd "$DEV" && DOTNET_ROOT="C:/dotnet10" ./MortarHUD.exe --screenshot "../../../../../_analysis/ui-task5")
```
Expected: 构建无警告无错误；自检通过退出码 0；截图里导航选中项是圆角强调色块、卡片圆角 12

- [ ] **Step 6: 提交**

```bash
git add src/MortarHUD.App/Themes/SettingsTheme.xaml
git commit -m "style: 导航项/卡片/文本样式统一到新设计语言"
```

---

### Task 6: 全量验证与收尾

**Files:**
- Modify: `docs/ocr-benchmark.md`（如果基准测试重新生成过）
- 无源码改动

**Interfaces:**
- Consumes: Task 1–5 的全部改动
- Produces: 可发布的最终产物

- [ ] **Step 1: 全量构建与测试**

Run:
```bash
"C:/dotnet10/dotnet.exe" build MortarHUD.sln -c Release
"C:/dotnet10/dotnet.exe" test MortarHUD.sln -c Release
```
Expected: 构建 `0 个警告 0 个错误`；测试 `失败: 0，通过: 202`

- [ ] **Step 2: 重新发布并自检**

Run:
```bash
rm -rf dist/verify-portable
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/publish.ps1 -Mode portable -OutputDirectory "dist/verify-portable"
cd dist/verify-portable && ./MortarHUD.exe --selftest; echo "退出码: $?"
```
Expected: `自检通过。`，退出码 `0`

- [ ] **Step 3: 生成三页截图并逐页核对**

Run: `cd dist/verify-portable && ./MortarHUD.exe --screenshot "../../_analysis/ui-final"`

逐页核对下列清单，任何一条不满足就回到对应任务修：

- 顶部栏左侧图标、内容区卡片、底部栏说明文字**左边缘在同一条竖直线上**（约 26px）
- 底部按钮组右边缘与卡片右边缘对齐
- 卡片比窗口背景亮、输入框比卡片暗，三层肉眼可辨
- 所有圆角符合规格：卡片 12、控件 8、复选框 5
- 「离线坐标辅助」已经消失
- 外观页预览卡下方不再有大片空洞（HUD 内容垂直居中）

- [ ] **Step 4: 确认 Debug 面板未被全局样式劣化**

`SettingsTheme.xaml` 是全局字典，其中的隐式样式会影响 `DebugOverlayWindow`。
该窗口不引用任何具名样式，但它里面的 `Button`/`CheckBox` 等控件会套用本次改的模板。

Run: 在真实桌面会话里启动 `dist/verify-portable/MortarHUD.exe`，按 `F8` 之外的调试热键打开 Debug 面板
（或在设置里打开「持续调试」后回到游戏按一次采集热键），确认面板里的控件外观正常、没有文字看不见或错位。

**如果 Debug 面板被劣化**：把设置窗口专用的样式下沉到 `SettingsWindow.xaml` 的
`<Window.Resources>`，全局字典只保留颜色。这属于本次范围的收尾，不要留到以后。

- [ ] **Step 5: 人工验证截图看不到的交互态**

`--screenshot` 只渲染初始状态。在真实窗口里逐项确认：

- 折叠项（外观页的「精确位置」「字体、颜色与特效」等）展开后排版正常、内容不与头部重叠
- 热键输入框获得焦点时是强调色描边，且仍能正常捕获按键（按一次 F6 看是否写入）
- 下拉框弹层完整显示、没有被窗口边缘裁掉、投影正常
- 复选框悬停与选中态的视觉正确
- 滑块拖动正常，已填充段随值变化
- 页面滚动到底部时底部栏不遮挡内容

- [ ] **Step 6: 把验证结果写进审计文档**

如果 Step 4、Step 5 发现问题并修复了，在 `AGENTS.md` 的「当前未决问题」里更新
（设置界面那条从「只做过离屏渲染验证」改为实际状态）。

- [ ] **Step 7: 最终提交**

```bash
git add -A src/ docs/ AGENTS.md
git commit -m "style: 设置窗口视觉重做完成，附全量验证"
```

---

## 自查记录

**规格覆盖检查**：规格的「颜色」→ Task 1；「间距」「圆角」「字号」→ Task 2 与 Task 5；
「控件规格」八个控件 → Task 3（TextBox/CheckBox/ComboBox）+ Task 4（Button/Slider/Expander）+ Task 5（TabItem/Card）；
「布局调整」三处 → Task 2；「风险与约束」的全局字典问题 → Task 6 Step 4；
「验证方式」六条 → Task 6 Step 1–5。无遗漏。

**与规格的两处偏差**（均已在对应任务里注明理由）：不新增 `BorderDefaultBrush`（与保留的 `BorderBrush` 同义）；
`PageTitle` 定为 22px 而规格表格写的 22px 一致，但 `SectionHeader` 的 Margin 调整为 `0,0,0,12`（规格未定此项）。
