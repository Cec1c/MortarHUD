<#
.SYNOPSIS
    把发布目录里的托管程序集收进 libs\，顶层只留运行必需品。

.DESCRIPTION
    .NET 宿主强制要求托管程序集与 exe 同目录，唯一的例外是
    runtimeconfig.json 的 additionalProbingPaths —— 但它只认 NuGet 包目录布局：

        libs\<包名小写>\<版本>\<相对路径>

    扁平目录（所有 dll 直接塞进 libs\）实测无效，宿主会报
    "assembly specified in the application dependencies manifest was not found"。

    好在 MortarHUD.deps.json 里正好记录了每个文件的「包名 / 版本 / 相对路径」，
    照着它重建出那个层级即可。

    只搬 deps.json 中归在 runtime 段下的托管程序集。
    native 段的东西（OpenCvSharpExtern、Tesseract 原生库、.NET 运行时本体）
    必须留在原地——它们要在读到 runtimeconfig.json 之前就被加载。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$EntryAssembly = 'MortarHUD.dll',
    [string]$ProbingPath = 'libs'
)

$ErrorActionPreference = 'Stop'

$PublishDirectory = (Resolve-Path $PublishDirectory).Path
$depsPath = Join-Path $PublishDirectory ([IO.Path]::GetFileNameWithoutExtension($EntryAssembly) + '.deps.json')

if (-not (Test-Path $depsPath)) {
    Write-Host "找不到 $depsPath，跳过整理。"
    exit 0
}

$deps = Get-Content $depsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$libsRoot = Join-Path $PublishDirectory $ProbingPath
$entryName = [IO.Path]::GetFileName($EntryAssembly)
$moved = 0

foreach ($target in $deps.targets.PSObject.Properties) {
    foreach ($library in $target.Value.PSObject.Properties) {

        # 键的形状是 "包名/版本"
        $key = $library.Name
        $slash = $key.LastIndexOf('/')
        if ($slash -le 0) { continue }

        $packageId = $key.Substring(0, $slash).ToLowerInvariant()
        $version = $key.Substring($slash + 1)

        $runtime = $library.Value.runtime
        if ($null -eq $runtime) { continue }

        foreach ($asset in $runtime.PSObject.Properties) {

            $relative = $asset.Name

            # 入口程序集必须留在根目录：宿主按固定位置找它，不走探测路径。
            if ([IO.Path]::GetFileName($relative) -ieq $entryName) { continue }

            # 基础类库同理，而且更严格：它在探测路径生效之前就要被加载。
            if ([IO.Path]::GetFileName($relative) -ieq 'System.Private.CoreLib.dll') { continue }

            $source = Join-Path $PublishDirectory $relative
            if (-not (Test-Path $source)) { continue }

            $destination = Join-Path (Join-Path $libsRoot (Join-Path $packageId $version)) $relative
            $destinationDirectory = Split-Path $destination -Parent

            if (-not (Test-Path $destinationDirectory)) {
                New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
            }

            Move-Item -Force $source $destination
            $moved++
        }
    }
}

if ($moved -eq 0) {
    Write-Host '没有需要整理的托管程序集。'
    exit 0
}

# 注入探测路径
$configPath = Join-Path $PublishDirectory ([IO.Path]::GetFileNameWithoutExtension($EntryAssembly) + '.runtimeconfig.json')

if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw -Encoding UTF8

    if ($config -notmatch 'additionalProbingPaths') {
        $marker = '"runtimeOptions": {'
        $injected = $marker + ' "additionalProbingPaths": [ "' + $ProbingPath + '" ],'
        $config = $config.Replace($marker, $injected)
        Set-Content -Path $configPath -Value $config -Encoding UTF8 -NoNewline
    }
}

Write-Host "已把 $moved 个托管程序集收进 $ProbingPath\（NuGet 包布局 + additionalProbingPaths）"

# ---------------------------------------------------------------------------
# 校验：deps.json 里列过的每个文件都必须能找到。
#
# 宿主会逐个核对依赖清单，少一个就直接拒绝启动，而报错要等到用户双击那一刻才出现
# （"An assembly specified in the application dependencies manifest was not found"）。
# 之前瘦身时误删了 Microsoft.DiaSymReader 和 createdump.exe，就是这么炸的。
# 在这里提前查一遍，把运行时崩溃变成发布时的警告。
# ---------------------------------------------------------------------------

$missing = @()

foreach ($target in $deps.targets.PSObject.Properties) {
    foreach ($library in $target.Value.PSObject.Properties) {

        $key = $library.Name
        $slash = $key.LastIndexOf('/')
        if ($slash -le 0) { continue }

        $packageId = $key.Substring(0, $slash).ToLowerInvariant()
        $version = $key.Substring($slash + 1)

        foreach ($section in @('runtime', 'native')) {

            $assets = $library.Value.$section
            if ($null -eq $assets) { continue }

            foreach ($asset in $assets.PSObject.Properties) {

                $relative = $asset.Name
                $leaf = [IO.Path]::GetFileName($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))

                $atRoot = Join-Path $PublishDirectory $leaf
                $inLibs = Join-Path (Join-Path $libsRoot (Join-Path $packageId $version)) $relative

                if (-not (Test-Path $atRoot) -and -not (Test-Path $inLibs)) {
                    $missing += "$packageId/$version -> $relative"
                }
            }
        }
    }
}

if ($missing.Count -gt 0) {
    Write-Warning "以下依赖在发布目录里找不到，程序启动会失败："
    $missing | Select-Object -Unique | ForEach-Object { Write-Warning "  $_" }
}
else {
    Write-Host '依赖清单校验通过：deps.json 里列出的文件全部就位。'
}
