@echo off
rem ============================================================
rem  MortarHUD 发布脚本
rem
rem  用法：
rem    publish.cmd            单文件模式（默认，顶层只有 3 项，便于找入口）
rem    publish.cmd folder     文件夹模式（体积小一些，但顶层约 180 个 DLL）
rem
rem  两种都是自包含发布，目标机器不需要装 .NET / Python / Tesseract CLI。
rem
rem  为什么不能「文件夹模式 + DLL 收进子目录」：
rem  .NET 宿主强制要求托管程序集与 exe 同目录。runtimeconfig.json 的
rem  additionalProbingPaths 只认 NuGet 包目录布局（libs/<包名>/<版本>/...），
rem  扁平子目录实测无效——会报 "assembly specified in the dependency
rem  manifest was not found"。所以要么接受扁平目录，要么用单文件。
rem ============================================================
setlocal

set ROOT=%~dp0
set PROJECT=%ROOT%src\MortarHUD.App\MortarHUD.App.csproj
set OUTPUT=%ROOT%dist\MortarHUD
set DOTNET=dotnet

where dotnet >nul 2>nul
if errorlevel 1 (
    if exist "C:\dotnet10\dotnet.exe" (
        set DOTNET="C:\dotnet10\dotnet.exe"
    ) else (
        echo [错误] 找不到 dotnet。请安装 .NET 10 SDK，或把 SDK 路径写进本脚本。
        exit /b 1
    )
)

if /i "%~1"=="folder" goto folder

echo 正在以「单文件」模式发布到 %OUTPUT% ...
%dotnet% publish "%PROJECT%" ^
    -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=false ^
    -o "%OUTPUT%" --nologo -v:q
if errorlevel 1 exit /b 1
goto done

:folder
echo 正在以「文件夹」模式发布到 %OUTPUT% ...
%dotnet% publish "%PROJECT%" ^
    -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=false ^
    -o "%OUTPUT%" --nologo -v:q
if errorlevel 1 exit /b 1

:done
echo.
echo 发布完成：%OUTPUT%
echo.
echo 注意：Models\ 目录（tessdata 语言包 + 字形模板库）随程序一起发布，
echo       删掉它 Tesseract 引擎会不可用，程序会退回模板引擎。
endlocal
