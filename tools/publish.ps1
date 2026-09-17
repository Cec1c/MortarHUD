[CmdletBinding()]
param(
    [ValidateSet('portable', 'folder', 'runtime')][string]$Mode = 'portable',
    [string]$OutputDirectory,
    [switch]$NoCompression
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sdk = 'C:\dotnet10\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { throw "缺少 .NET 10 SDK：$sdk" }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "dist\MortarHUD-next-$Mode" }
$destination = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if ((Test-Path -LiteralPath $destination) -and (Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)) {
    throw "发布目录非空，为避免混入旧 DLL，请指定新的空目录：$destination"
}

$single = if ($Mode -eq 'folder') { 'false' } else { 'true' }
$selfContained = if ($Mode -eq 'runtime') { 'false' } else { 'true' }
$compress = if ($NoCompression -or $Mode -eq 'folder') { 'false' } else { 'true' }
$project = Join-Path $repo 'src\MortarHUD.App\MortarHUD.App.csproj'
# IncludeAllContentForSelfExtract 不是可选项，是必需的：
# Tesseract 的 .NET 包装按 x64/tesseract50.dll 这类相对路径找原生库，标准单文件模式
# 解压后没有这个目录结构，加载会失败——而失败是**静默**的，程序会退回模板匹配引擎
# （缺数字 2、3，在复杂地图背景上还常切不出字形，表现为「怎么点都识别不出来」）。
# 打开这个开关会把全部文件解压到 %TEMP%\.net\ 并把 AppContext.BaseDirectory 指过去，
# 代价是首次启动多一次解压、磁盘多占一份解压缓存。
& $sdk publish $project -c Release -r win-x64 --self-contained $selfContained `
    "-p:PublishSingleFile=$single" "-p:EnableCompressionInSingleFile=$compress" `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true `
    -p:OrganizeLegacyLayout=false -p:NuGetAudit=false -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }

$buildDeps = Join-Path $repo 'src\MortarHUD.App\bin\Release\net10.0-windows\win-x64\MortarHUD.deps.json'
if (-not (Test-Path -LiteralPath $buildDeps)) { throw '缺少打包前的依赖清单，无法验证' }
if (Select-String -LiteralPath $buildDeps -Pattern 'ffmpeg') { throw '单文件内嵌依赖清单仍引用 FFmpeg' }

# 依赖清单与实际文件都不得再携带未使用的视频插件。
if (Get-ChildItem -LiteralPath $destination -Recurse -File -Filter '*ffmpeg*') { throw '发布产物仍包含 FFmpeg' }
Get-ChildItem -LiteralPath $destination -Filter '*.deps.json' | ForEach-Object {
    if (Select-String -LiteralPath $_.FullName -Pattern 'ffmpeg') { throw '依赖清单仍引用 FFmpeg' }
}
# 产物里不再放 run.cmd。它当初是为了让用户从任意工作目录启动时能找到同目录的
# Models\ 与原生库；改成扁平布局、且 IncludeAllContentForSelfExtract 会把内容
# 解压到 %TEMP%\.net\ 之后，工作目录就不再影响加载了，双击 exe 即可。
$files = Get-ChildItem -LiteralPath $destination -Recurse -File
[pscustomobject]@{
    Mode = $Mode
    Directory = $destination
    Files = $files.Count
    MiB = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
    RequiresDesktopRuntime = ($Mode -eq 'runtime')
}
