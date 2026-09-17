@echo off
rem ============================================================
rem  MortarHUD 启动脚本
rem
rem  请用这个脚本启动，不要直接去 bin\ 下面找 exe。
rem
rem  原因：dotnet build 产出的是「框架依赖」版本，它要求系统里
rem  注册过 .NET 10 桌面运行时。本机 .NET 10 装在 C:\dotnet10 且
rem  没有并入系统 dotnet（系统里只有 .NET 6），所以直接双击
rem  bin\Release\...\MortarHUD.exe 会弹出
rem  "You must install or update .NET to run this application."。
rem
rem  dist\ 下面那份是「自包含」的，运行时打在里面，谁都不用装东西。
rem ============================================================
setlocal

set ROOT=%~dp0
set APP=%ROOT%dist\MortarHUD\MortarHUD.exe

if not exist "%APP%" (
    echo 还没发布过，正在构建自包含版本（首次约 1-2 分钟）...
    echo.

    where dotnet >nul 2>nul
    if errorlevel 1 (
        if exist "C:\dotnet10\dotnet.exe" (
            set DOTNET="C:\dotnet10\dotnet.exe"
        ) else (
            echo [错误] 找不到 dotnet。请安装 .NET 10 SDK，或把 SDK 路径写进本脚本。
            exit /b 1
        )
    ) else (
        set DOTNET=dotnet
    )

    call %DOTNET% publish "%ROOT%src\MortarHUD.App\MortarHUD.App.csproj" ^
        -c Release -r win-x64 --self-contained true ^
        -p:PublishSingleFile=false -o "%ROOT%dist\MortarHUD" --nologo -v:q

    if errorlevel 1 (
        echo [错误] 发布失败。
        exit /b 1
    )
)

echo 启动 MortarHUD...
echo   设置窗口打开后可以关掉，程序会留在系统托盘。
echo   默认热键：F6 记录炮位 / 鼠标中键 记录目标 / F8 显示隐藏 HUD / F9 打开设置
echo.

rem 必须切到程序目录再启动：
rem runtimeconfig.json 里的 additionalProbingPaths 是相对路径，而 .NET 按
rem **当前工作目录**（不是 exe 所在目录）解析它。从别处启动会找不到框架程序集，
rem 报 "Could not load file or assembly PresentationFramework"。
rem 双击 exe 不会有这个问题（资源管理器会把工作目录设成 exe 目录），
rem 但脚本启动会有，所以这里显式切过去。
cd /d "%ROOT%dist\MortarHUD"
start "" "%APP%"
endlocal
