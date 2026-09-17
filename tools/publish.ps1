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
& $sdk publish $project -c Release -r win-x64 --self-contained $selfContained `
    "-p:PublishSingleFile=$single" "-p:EnableCompressionInSingleFile=$compress" `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:OrganizeLegacyLayout=false `
    -p:NuGetAudit=false -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }

$buildDeps = Join-Path $repo 'src\MortarHUD.App\bin\Release\net10.0-windows\win-x64\MortarHUD.deps.json'
if (-not (Test-Path -LiteralPath $buildDeps)) { throw '缺少打包前的依赖清单，无法验证' }
if (Select-String -LiteralPath $buildDeps -Pattern 'ffmpeg') { throw '单文件内嵌依赖清单仍引用 FFmpeg' }

# 依赖清单与实际文件都不得再携带未使用的视频插件。
if (Get-ChildItem -LiteralPath $destination -Recurse -File -Filter '*ffmpeg*') { throw '发布产物仍包含 FFmpeg' }
Get-ChildItem -LiteralPath $destination -Filter '*.deps.json' | ForEach-Object {
    if (Select-String -LiteralPath $_.FullName -Pattern 'ffmpeg') { throw '依赖清单仍引用 FFmpeg' }
}
@'
@echo off
cd /d "%~dp0"
start "" "%~dp0MortarHUD.exe"
'@ | Set-Content -LiteralPath (Join-Path $destination 'run.cmd') -Encoding ascii
$files = Get-ChildItem -LiteralPath $destination -Recurse -File
[pscustomobject]@{
    Mode = $Mode
    Directory = $destination
    Files = $files.Count
    MiB = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
    RequiresDesktopRuntime = ($Mode -eq 'runtime')
}
